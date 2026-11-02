using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CrystalDragon;

// Offline asset producer only. It never starts or attaches to the Codex client.
class RenderFilm {
    const int FrameRate=60;
    static string Quote(string path) { return "\""+Path.GetFullPath(path)+"\""; }
    static void SavePng(BitmapSource bitmap,string path) {
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var file=File.Create(path))encoder.Save(file);
    }
    [STAThread] static int Main(string[] args) {
        if(args.Length!=5&&args.Length!=7) {Console.Error.WriteLine("Expected: ffmpeg hero film poster end-frame [width height]");return 2;}
        try {
            int width,height;
            if(args.Length==7) {
                width=int.Parse(args[5],System.Globalization.CultureInfo.InvariantCulture);
                height=int.Parse(args[6],System.Globalization.CultureInfo.InvariantCulture);
            } else {
                // Default developer CLI mode; production supplies explicit physical dimensions.
                var area=System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
                width=area.Width;height=area.Height;
                Console.WriteLine("physical-workarea="+width+"x"+height);
            }
            if(width<2||height<2)throw new ArgumentException("Film dimensions must both be positive and at least two pixels.");
            width-=width%2;height-=height%2;
            Console.WriteLine("encoded-canvas="+width+"x"+height);
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            var scene=new DataScene(Path.GetFullPath(args[1]));
            var background=new SolidColorBrush(Color.FromRgb(16,14,25));background.Freeze();
            var canvas=new Grid {Background=background};
            canvas.Children.Add(new Viewbox {Child=scene,Stretch=Stretch.Uniform});
            canvas.Measure(new Size(width,height));canvas.Arrange(new Rect(0,0,width,height));canvas.UpdateLayout();
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
            // One reusable physical-resolution BGRA frame; ffmpeg stdin applies backpressure.
            byte[] pixels=new byte[checked(width*height*4)];
            int frameCount=(int)Math.Ceiling(DataScene.Duration/DataScene.PlaybackRate*FrameRate);
            Console.WriteLine("FRAME 0/"+frameCount);
            var start=new ProcessStartInfo(Path.GetFullPath(args[0])) {
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true,
                Arguments="-hide_banner -loglevel error -y -f rawvideo -pixel_format bgra -video_size "+width+"x"+height+" -framerate 60 -i pipe:0 -an -c:v libx264 -preset medium -crf 17 -threads 4 -profile:v high -g 60 -pix_fmt yuv420p -vf scale=out_color_matrix=bt709:out_range=pc -color_range pc -colorspace bt709 -color_trc bt709 -color_primaries bt709 -movflags +faststart "+Quote(args[2])
            };
            using(var encoder=Process.Start(start)) {
                var errors=encoder.StandardError.ReadToEndAsync();
                try {
                    using(var input=encoder.StandardInput.BaseStream) {
                        for(int frame=0;frame<frameCount;frame++) {
                            double time=frame==frameCount-1 ? DataScene.Duration : frame*DataScene.PlaybackRate/FrameRate;
                            scene.SetTime(time);canvas.UpdateLayout();bitmap.Render(canvas);
                            bitmap.CopyPixels(pixels,width*4,0);input.Write(pixels,0,pixels.Length);
                            if(frame==0)SavePng(bitmap,Path.GetFullPath(args[3]));
                            if(frame==frameCount-1)SavePng(bitmap,Path.GetFullPath(args[4]));
                            if((frame+1)%12==0||frame==frameCount-1)Console.WriteLine("FRAME "+(frame+1)+"/"+frameCount);
                        }
                    }
                    encoder.WaitForExit();
                    if(encoder.ExitCode!=0)throw new InvalidOperationException("ffmpeg failed: "+errors.Result);
                } catch {
                    if(!encoder.HasExited)encoder.Kill();
                    throw;
                }
            }
            app.Shutdown();
            Console.WriteLine("Rendered "+frameCount+" frames at "+FrameRate+"fps; duration="+(frameCount/(double)FrameRate).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"s; source rate="+DataScene.PlaybackRate);
            return 0;
        } catch(Exception error) {Console.Error.WriteLine(error.Message);return 1;}
    }
}
