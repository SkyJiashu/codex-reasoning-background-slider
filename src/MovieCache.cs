using System;
using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CrystalDragon {
    // Only public renderer/art inputs and this application's generated files are read.
    internal static class MovieCache {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern uint SearchPath(string searchPath,string fileName,string extension,int size,StringBuilder result,IntPtr filePart);
        static readonly string[] Artifacts={"startup.mp4","startup-poster.png","startup-end.png"};
        static readonly Regex ProgressLine=new Regex(@"^FRAME ([0-9]{1,4})/([0-9]{1,4})$",RegexOptions.CultureInvariant);

        static string Hash(byte[] bytes) {
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
        }
        static string HeroHash(string path) {
            using(var stream=File.OpenRead(path))
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
        static bool Complete(string folder) {
            if(!File.Exists(Path.Combine(folder,"complete")))return false;
            foreach(string file in Artifacts) {
                var info=new FileInfo(Path.Combine(folder,file));
                if(!info.Exists||info.Length==0)return false;
            }
            return true;
        }
        static string Encoder(string root) {
            string local=Path.Combine(root,"ffmpeg.exe");
            if(File.Exists(local))return local;
            var path=new StringBuilder(32768);
            uint length=SearchPath(null,"ffmpeg.exe",null,path.Capacity,path,IntPtr.Zero);
            if(length>0&&length<path.Capacity&&File.Exists(path.ToString()))return path.ToString();
            throw new FileNotFoundException("ffmpeg-required");
        }
        static string Quote(string value) {return "\""+value.Replace("\"","\\\"")+"\"";}

        internal static string Ensure(string root,NativeWindow.Rect viewport,int dpi,Action<int> progress,string cacheBase=null,CancellationToken cancellation=default(CancellationToken)) {
            cancellation.ThrowIfCancellationRequested();
            root=Path.GetFullPath(root);
            long rawWidth=(long)viewport.Right-viewport.Left,rawHeight=(long)viewport.Bottom-viewport.Top;
            if(rawWidth<2||rawHeight<2||rawWidth>Int32.MaxValue||rawHeight>Int32.MaxValue||dpi<=0)
                throw new ArgumentOutOfRangeException("viewport");
            int width=(int)rawWidth,height=(int)rawHeight;
            width-=width%2;height-=height%2; // YUV420 dimensions must be even.
            string source=File.ReadAllText(Path.Combine(root,"animation-source.txt")).Trim();
            if(!Regex.IsMatch(source,@"\A[0-9a-fA-F]{64}\z",RegexOptions.CultureInvariant))
                throw new InvalidDataException("animation-source-invalid");
            string hero=Path.Combine(root,"assets","ultra.png");
            string key=Hash(Encoding.ASCII.GetBytes(width+"x"+height+"\n"+dpi+"\n"+source.ToLowerInvariant()+"\n"+HeroHash(hero)));
            string basePath=Path.GetFullPath(cacheBase??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CrystalDragon","animation-cache"));
            string folder=Path.GetFullPath(Path.Combine(basePath,width+"x"+height+"-"+dpi+"-"+key));
            if(!folder.StartsWith(basePath.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("cache-path-invalid");
            if(Complete(folder)) {if(progress!=null)progress(100);return folder;}

            // Multiple first-launch/preview callers share one generation for this key.
            using(var gate=new Mutex(false,"Local\\CrystalDragonMovie-"+key)) {
                try {
                    if(WaitHandle.WaitAny(new WaitHandle[]{gate,cancellation.WaitHandle})==1)cancellation.ThrowIfCancellationRequested();
                }catch(AbandonedMutexException) { }
                try {
                    cancellation.ThrowIfCancellationRequested();
                    if(Complete(folder)) {if(progress!=null)progress(100);return folder;}
                    string encoder=Encoder(root),renderer=Path.Combine(root,"RenderFilm.exe");
                    if(!File.Exists(renderer))throw new FileNotFoundException("film-renderer-required");
                    Directory.CreateDirectory(folder);
                    File.Delete(Path.Combine(folder,"complete"));
                    var values=new[]{encoder,hero,Path.Combine(folder,Artifacts[0]),Path.Combine(folder,Artifacts[1]),Path.Combine(folder,Artifacts[2]),width.ToString(CultureInfo.InvariantCulture),height.ToString(CultureInfo.InvariantCulture)};
                    for(int i=0;i<values.Length;i++)values[i]=Quote(values[i]);
                    var start=new ProcessStartInfo(renderer,String.Join(" ",values)) {
                        UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
                        RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root
                    };
                    if(progress!=null)progress(0);
                    cancellation.ThrowIfCancellationRequested();
                    using(var child=Process.Start(start)) {
                      using(cancellation.Register(delegate {
                          try {if(!child.HasExited)child.Kill();}
                          catch(InvalidOperationException) { }
                          catch(Win32Exception) { }
                      })) {
                        // Drain errors without forwarding or retaining arbitrary tool output.
                        Task errors=Task.Run(delegate {var buffer=new char[4096];while(child.StandardError.Read(buffer,0,buffer.Length)>0) { }});
                        string line;
                        while((line=child.StandardOutput.ReadLine())!=null) {
                            if(line.Length>32)continue;
                            var match=ProgressLine.Match(line);
                            if(!match.Success)continue;
                            int frame=int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture),total=int.Parse(match.Groups[2].Value,CultureInfo.InvariantCulture);
                            if(total==324&&frame>=0&&frame<=total&&progress!=null)progress(Math.Min(99,frame*100/total));
                        }
                        child.WaitForExit();errors.GetAwaiter().GetResult();
                        cancellation.ThrowIfCancellationRequested();
                        if(child.ExitCode!=0)throw new InvalidOperationException("film-render-failed");
                      }
                    }
                    cancellation.ThrowIfCancellationRequested();
                    foreach(string file in Artifacts) {
                        var info=new FileInfo(Path.Combine(folder,file));
                        if(!info.Exists||info.Length==0)throw new InvalidDataException("film-output-incomplete");
                    }
                    cancellation.ThrowIfCancellationRequested();
                    File.WriteAllText(Path.Combine(folder,"complete"),"1",Encoding.ASCII);
                    if(progress!=null)progress(100);
                    return folder;
                } finally {gate.ReleaseMutex();}
            }
        }
    }
}
