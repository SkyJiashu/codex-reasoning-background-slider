using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CrystalDragon;

class MovieCacheTests {
    static void Assert(bool value,string message) {if(!value)throw new Exception(message);}
    static int MockRenderer(string[] args) {
        string root=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string countPath=Path.Combine(root,"render-count.txt");
        int count=File.Exists(countPath)?int.Parse(File.ReadAllText(countPath)):0;
        File.WriteAllText(countPath,(count+1).ToString());
        Console.WriteLine("ignored arbitrary output");Console.Error.WriteLine("ignored encoder diagnostics");
        Console.WriteLine("FRAME 0/324");Console.WriteLine("FRAME 162/324");Console.WriteLine("FRAME 9999/324");
        Console.Out.Flush();
        if(File.Exists(Path.Combine(root,"mock-block")))Thread.Sleep(10000);
        File.WriteAllText(args[2],args[5]+"x"+args[6]);
        if(File.Exists(Path.Combine(root,"mock-fail")))return 1;
        File.WriteAllBytes(args[3],new byte[]{1});File.WriteAllBytes(args[4],new byte[]{2});
        Console.WriteLine("FRAME 324/324");return 0;
    }
    static NativeWindow.Rect Frame(int width,int height) {return new NativeWindow.Rect{Left=-100,Top=40,Right=width-100,Bottom=height+40};}
    static int Count(string root) {string path=Path.Combine(root,"render-count.txt");return File.Exists(path)?int.Parse(File.ReadAllText(path)):0;}
    static int Main(string[] args) {
        if(args.Length==7)return MockRenderer(args);
        try {
            string testBase=Path.GetFullPath(Path.Combine(args[0],"movie-cache-"+Guid.NewGuid().ToString("N")));
            string root=Path.Combine(testBase,"source"),cache=Path.Combine(testBase,"cache");
            Directory.CreateDirectory(Path.Combine(root,"assets"));
            File.Copy(Assembly.GetExecutingAssembly().Location,Path.Combine(root,"RenderFilm.exe"));
            File.WriteAllBytes(Path.Combine(root,"ffmpeg.exe"),new byte[]{0}); // Only passed to our mock; never executed.
            File.WriteAllText(Path.Combine(root,"animation-source.txt"),new string('a',64));
            string hero=Path.Combine(root,"assets","ultra.png");File.WriteAllBytes(hero,new byte[]{1,2,3});
            var progress=new List<int>();
            string first=MovieCache.Ensure(root,Frame(321,181),96,progress.Add,cache);
            Assert(Count(root)==1&&File.Exists(Path.Combine(first,"complete")),"first request builds and commits once");
            Assert(File.ReadAllText(Path.Combine(first,"startup.mp4"))=="320x180","physical dimensions are rounded to encoder-safe even dimensions");
            Assert(progress.Contains(50)&&progress[progress.Count-1]==100&&progress.TrueForAll(n=>n>=0&&n<=100),"only bounded recognized progress reaches caller");
            string again=MovieCache.Ensure(root,Frame(320,180),96,null,cache);
            Assert(again==first&&Count(root)==1,"same normalized display uses cached movie");
            Assert(MovieCache.Ensure(root,Frame(640,360),96,null,cache)!=first&&Count(root)==2,"resolution change rebuilds");
            Assert(MovieCache.Ensure(root,Frame(320,180),144,null,cache)!=first&&Count(root)==3,"DPI change rebuilds");
            File.WriteAllText(Path.Combine(root,"animation-source.txt"),new string('b',64));
            string changedSource=MovieCache.Ensure(root,Frame(320,180),96,null,cache);
            Assert(changedSource!=first&&Count(root)==4,"renderer fingerprint change rebuilds");
            File.WriteAllBytes(hero,new byte[]{4,5,6});
            string changedArt=MovieCache.Ensure(root,Frame(320,180),96,null,cache);
            Assert(changedArt!=changedSource&&Count(root)==5,"character source change rebuilds");
            File.Delete(Path.Combine(changedArt,"startup-end.png"));
            Assert(MovieCache.Ensure(root,Frame(320,180),96,null,cache)==changedArt&&Count(root)==6,"incomplete artifact set is regenerated despite old marker");

            // A failed render must not become a cache hit; the following attempt repairs it.
            File.WriteAllText(Path.Combine(root,"mock-fail"),"1");
            bool failed=false;
            try {MovieCache.Ensure(root,Frame(800,600),96,null,cache);}catch(InvalidOperationException e){failed=e.Message=="film-render-failed";}
            Assert(failed&&Count(root)==7,"failed renderer reported through a fixed error");
            foreach(string directory in Directory.GetDirectories(cache,"800x600-*"))Assert(!File.Exists(Path.Combine(directory,"complete")),"failed output lacks completion marker");
            File.Delete(Path.Combine(root,"mock-fail"));
            MovieCache.Ensure(root,Frame(800,600),96,null,cache);Assert(Count(root)==8,"failed output does not prevent later generation");

            // Two preview/start callers must not concurrently overwrite the same outputs.
            var one=Task.Run(()=>MovieCache.Ensure(root,Frame(1024,768),96,null,cache));
            var two=Task.Run(()=>MovieCache.Ensure(root,Frame(1024,768),96,null,cache));
            Task.WaitAll(one,two);Assert(one.Result==two.Result&&Count(root)==9,"concurrent callers share one generation");

            File.WriteAllText(Path.Combine(root,"mock-block"),"1");
            var timer=Stopwatch.StartNew();
            using(var cancel=new CancellationTokenSource()) {
                failed=false;
                try {MovieCache.Ensure(root,Frame(1280,720),96,n=>{if(n==50)cancel.Cancel();},cache,cancel.Token);}
                catch(OperationCanceledException){failed=true;}
                Assert(failed&&timer.Elapsed.TotalSeconds<3,"cancellation stops only the owned blocked renderer promptly");
            }
            foreach(string directory in Directory.GetDirectories(cache,"1280x720-*"))Assert(!File.Exists(Path.Combine(directory,"complete")),"cancelled output lacks completion marker");
            File.Delete(Path.Combine(root,"mock-block"));
            Assert(Count(root)==10,"cancelled run was the newly owned renderer");

            // Playback reuse has no encoding-tool prerequisite.
            File.Move(Path.Combine(root,"RenderFilm.exe"),Path.Combine(root,"renderer-disabled"));
            File.Delete(Path.Combine(root,"ffmpeg.exe"));
            Assert(MovieCache.Ensure(root,Frame(320,180),96,null,cache)==changedArt&&Count(root)==10,"cache hit works without renderer or encoder lookup");
            File.WriteAllText(Path.Combine(root,"animation-source.txt"),"not-a-fingerprint");
            failed=false;try{MovieCache.Ensure(root,Frame(320,180),96,null,cache);}catch(InvalidDataException e){failed=e.Message=="animation-source-invalid";}
            Assert(failed,"invalid source fingerprint cannot select an unrelated cache");
            Console.WriteLine("Movie cache: first generation, reuse, resolution/DPI/source/art invalidation, incomplete/failed output, concurrent reuse, cancellation and bounded progress passed. Own mock renderer only.");
            return 0;
        } catch(Exception e) {Console.WriteLine("Movie cache test failed: "+e.GetType().Name+": "+e.Message);return 1;}
    }
}
