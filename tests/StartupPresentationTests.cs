using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CrystalDragon;

class StartupPresentationTests {
    sealed class Client : IStartupClient {
        public IntPtr Handle;
        public bool Started;
        public bool Usable = false, Decoded = false;
        public bool Painted = false;
        public int FindCalls;
        public int ProcessId { get { return Process.GetCurrentProcess().Id; } }
        public bool Ready { get { return Decoded; } }
        public bool ShellReady { get { return Usable; } }
        public bool PaintReady { get { return Painted; } }
        public object PreparationTimings { get { return null; } }
        public IntPtr FindWindow() { FindCalls++; return Started ? Handle : IntPtr.Zero; }
    }
    static void Assert(bool value,string label) { if(!value)throw new Exception(label); }
    static void CheckSurfaceSize(Splash cover,FilmSurface film) {
        Assert(film.ActualWidth>0&&film.ActualHeight>0,"film surface has a laid out client area");
        Assert(Math.Abs(film.ActualWidth-cover.ActualWidth)<1&&Math.Abs(film.ActualHeight-cover.ActualHeight)<1,
            "film surface fills the window client area instead of retaining cached video pixel dimensions");
    }
    static void Capture(Splash cover,string path,bool exactPoster=false) {
        cover.UpdateLayout();
        int width=(int)Math.Ceiling(cover.ActualWidth),height=(int)Math.Ceiling(cover.ActualHeight);
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(cover);
        var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);
        int colored=0;
        for(int i=0;i<pixels.Length;i+=4)if(pixels[i]>80||pixels[i+1]>80||pixels[i+2]>80)colored++;
        Assert(colored>1000,"cover has authored text/content, not an empty black surface");
        Assert(pixels[3]==255&&pixels[pixels.Length-1]==255,"cover corners are opaque");
        int tolerance=exactPoster?0:3;
        Assert(Math.Abs(pixels[0]-25)<=tolerance&&Math.Abs(pixels[1]-14)<=tolerance&&Math.Abs(pixels[2]-16)<=tolerance,"cover keeps authored purple, allowing video compression rounding; observed RGB="+pixels[2]+","+pixels[1]+","+pixels[0]);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var file=File.Create(path))encoder.Save(file);
    }
    [STAThread] static int Main(string[] args) {
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        bool escapeMinimized=args.Length>1&&args[1]=="--escape-minimized";
        bool earlyReady=args.Length>1&&args[1]=="--early-ready";
        bool shellOnly=args.Length>1&&args[1]=="--shell-only";
        bool lateSidebar=args.Length>1&&args[1]=="--late-sidebar";
        bool lateWindow=args.Length>1&&args[1]=="--late-window";
        string root=Path.GetFullPath(args[0]),qa=Path.Combine(root,"qa");Directory.CreateDirectory(qa);
        var frame=new NativeWindow.Rect {Left=-24000,Top=-24000,Right=-23100,Bottom=-23400};
        // Generate once for this fixture's viewport/DPI, then reuse across modes.
        // Cache construction occurs before the playback and handoff stopwatch.
        string filmDirectory=MovieCache.Ensure(root,frame,96,null,Path.Combine(qa,"film-test-cache"));
        var client=new Client();
        var source=new Window {WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Width=720,Height=480,
            Left=-24000,Top=-24000,ShowActivated=false,ShowInTaskbar=false,Background=Brushes.White};
        var cover=new Splash(root,client,filmDirectory,frame) {ShowActivated=false,Topmost=false};
        var film=(FilmSurface)cover.Content;
        var elapsed=new Stopwatch();var watch=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(40)};
        bool checkedHold=false,checkedResize=false,changedSize=false,checkedWaiting=false,exposedLateWindow=false;
        bool mediaOpened=false,mediaCompleted=false;
        double completedAt=-1,fadeAt=-1,resizeAt=-1,windowExposedAt=-1,maxPosition=0;
        int callsAtWindowExposure=0;Exception failure=null;
        var phases=new HashSet<int>();
        app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e) {
            failure=e.Exception;e.Handled=true;watch.Stop();cover.Close();source.Close();app.Shutdown(1);
        };
        cover.FirstFramePresented+=delegate {
            Assert(!client.Started,"client was not started before first presentation");
            Assert(cover.IsVisible&&cover.Opacity==1,"opaque cover is visible before client launch");
            Capture(cover,Path.Combine(qa,"cover-first-frame.png"),true);
            CheckSurfaceSize(cover,film);
            phases.Add(film.Phase);
            client.Started=true;
            source.SourceInitialized+=delegate {NativeWindow.PlaceLayout(source,frame);};
            if(!lateWindow) { source.Show();client.Handle=new WindowInteropHelper(source).Handle; }
            elapsed.Start();watch.Start();
        };
        watch.Tick+=delegate {
            double now=elapsed.Elapsed.TotalSeconds;
            phases.Add(film.Phase);
            mediaOpened=mediaOpened||film.Opened;
            if(!film.Completed)maxPosition=Math.Max(maxPosition,film.PositionSeconds);
            Assert(!film.Failed,"real video must decode successfully; a poster fallback does not pass the film test");
            if(film.Completed&&!mediaCompleted) {mediaCompleted=true;completedAt=now;}
            if(escapeMinimized) {
                if(elapsed.Elapsed.TotalSeconds>.8&&!changedSize) {
                    source.WindowState=WindowState.Minimized;changedSize=true;
                    cover.Skip();
                }
                if(elapsed.Elapsed.TotalSeconds>5)throw new Exception("Escape failed to release the cover");
                return;
            }
            if(!lateWindow&&film.PositionSeconds>3.6&&!changedSize) {
                source.Width+=180;source.Height+=120;changedSize=true;resizeAt=now;
            }
            if(film.Phase==4&&!checkedHold) {
                Assert(cover.IsVisible&&cover.Opacity==1,"purple animation plays continuously before its end");
                Assert(mediaOpened,"fourth phase belongs to an opened video");
                Assert(earlyReady||(!client.Usable&&!client.Decoded&&!client.Painted),"late readiness flags remain false during phase 4");
                Capture(cover,Path.Combine(qa,"purple-before-fade.png"));
                checkedHold=true;
            }
            if(!lateWindow&&changedSize&&now-resizeAt>.5&&!checkedResize) {
                Assert(NativeWindow.FramesEqual(new WindowInteropHelper(cover).Handle,client.Handle),"cover follows resized target");
                CheckSurfaceSize(cover,film);
                checkedResize=true;
            }
            if(mediaCompleted&&now-completedAt<.25)
                Assert(cover.Opacity==1,"video's final frame stays opaque for the requested .3s dwell");
            if(lateWindow&&mediaCompleted&&now-completedAt>.6&&!checkedWaiting) {
                Assert(client.Handle==IntPtr.Zero&&cover.IsVisible&&cover.Opacity==1,"cover cannot reveal desktop while no native window exists");
                Capture(cover,Path.Combine(qa,"preparing-after.png"));
                checkedWaiting=true;
            }
            if(lateWindow&&mediaCompleted&&now-completedAt>1.0&&!exposedLateWindow) {
                callsAtWindowExposure=client.FindCalls;
                source.Show();client.Handle=new WindowInteropHelper(source).Handle;exposedLateWindow=true;windowExposedAt=now;
            }
            if(cover.Opacity<1&&fadeAt<0) {
                Assert(mediaCompleted,"fade cannot begin before the real MediaEnded event");
                fadeAt=now;
                Assert(fadeAt-completedAt>=.27,"fade starts after the requested .3s post-film dwell");
                if(lateWindow)Assert(exposedLateWindow,"late native window must exist before fading");
                else Assert(fadeAt-completedAt<.65,"editor/artwork checks cannot extend the post-film dwell");
            }
            if(earlyReady&&elapsed.Elapsed.TotalSeconds>.8) {client.Usable=true;client.Decoded=true;}
            // Film completion plus .3s is independent of editor/sidebar/artwork/rAF.
            if(!mediaCompleted)Assert(cover.Opacity==1,"no alpha probing or palette swap during video playback");
            if(now>15)throw new Exception("bounded real-video presentation test timeout");
        };
        cover.Closed+=delegate {
            watch.Stop();
            try {
                if(escapeMinimized) {
                    NativeWindow.Rect restored;
                    Assert(changedSize&&NativeWindow.PresentableFrame(client.Handle,client.ProcessId,out restored),"Escape restores owned minimized target independently of alignment");
                } else {
                    Assert(checkedHold&&!client.Painted,"authored purple animation ends without a browser paint gate");
                    Assert(mediaOpened&&mediaCompleted&&!film.Failed&&maxPosition>4.8,"real video opened, advanced and ended without fallback");
                    Assert(completedAt>=0&&fadeAt>=0,"observed actual movie end and transition");
                    Assert(phases.SetEquals(new[] {1,2,3,4}),"all four authored phases play before transition");
                    Assert(earlyReady||(!client.Usable&&!client.Decoded),"late fixture readiness flags never became true");
                    if(lateWindow) {
                        Assert(checkedWaiting&&exposedLateWindow,"missing native window kept desktop covered until it appeared");
                        Assert(elapsed.Elapsed.TotalSeconds-windowExposedAt>.45&&elapsed.Elapsed.TotalSeconds-windowExposedAt<1.1,"late native window hands off after bounded discovery and .55s fade");
                        Assert(client.FindCalls<=Math.Ceiling(windowExposedAt/.25)+3&&client.FindCalls-callsAtWindowExposure<=3,"discovery polling is bounded and stops once the native target is cached");
                    } else {
                        Assert(checkedResize&&changedSize,"fixed-ending fixture exercised target resize");
                        Assert(elapsed.Elapsed.TotalSeconds>5.9&&elapsed.Elapsed.TotalSeconds<8.5,"cached film, decode startup, .3s dwell and fade finish within the playback budget");
                        Assert(elapsed.Elapsed.TotalSeconds-completedAt>.75&&elapsed.Elapsed.TotalSeconds-completedAt<1.15,"post-film handoff is .3s dwell plus .55s fade");
                        Assert(client.FindCalls<=4,"cached native target is reused instead of rediscovered every animation tick");
                    }
                }
            } catch(Exception error){if(failure==null)failure=error;}
            source.Close();app.Shutdown(failure==null?0:1);
        };
        app.Run(cover);
        if(failure!=null){Console.WriteLine("Presentation failed: "+failure.Message);return 1;}
        Console.WriteLine(escapeMinimized ? "Escape: owned minimized target restored and cover released; no real client or foreground request." : "Video presentation ("+(earlyReady?"early-ready":shellOnly?"late-artwork":lateSidebar?"late-sidebar":lateWindow?"late-window":"all-readiness-false")+"): real MediaOpened/MediaEnded; four phases; .3s dwell; no editor/artwork gate; resize and cached native target passed ("+elapsed.Elapsed.TotalSeconds.ToString("F2")+"s, dwell="+(fadeAt-completedAt).ToString("F2")+"s, FindWindow="+client.FindCalls+"). Own off-screen fixtures only.");
        return 0;
    }
}
