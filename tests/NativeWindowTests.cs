using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CrystalDragon;

class NativeWindowTests {
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out NativeWindow.Rect rect);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr window,IntPtr region);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] static extern int GetRgnBox(IntPtr region,out NativeWindow.Rect rect);
    [DllImport("gdi32.dll")] static extern bool PtInRegion(IntPtr region,int x,int y);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
    static string phase = "geometry";
    static void Assert(bool value, string label) { if (!value) throw new Exception(label); }
    static NativeWindow.Rect Bounds(IntPtr window) { NativeWindow.Rect rect; Assert(GetWindowRect(window,out rect),"fixture rectangle query"); return rect; }
    static void Drain(Window window) { window.UpdateLayout(); window.Dispatcher.Invoke(new Action(delegate {}), System.Windows.Threading.DispatcherPriority.ApplicationIdle); }
    static void CheckPreparationRegion(Window cover) {
        IntPtr window=new WindowInteropHelper(cover).Handle;
        var before=Bounds(window); double opacity=cover.Opacity;
        Assert(NativeWindow.EnableBackgroundPreparation(cover),"own cover background-preparation region accepted");
        Assert(NativeWindow.SameFrame(before,Bounds(window)),"preparation region cannot move or resize the cover");
        Assert(cover.Opacity==opacity,"preparation region must not probe opacity");
        IntPtr observed=CreateRectRgn(0,0,0,0);
        try {
            Assert(observed!=IntPtr.Zero,"region query handle available");
            NativeWindow.Rect box=new NativeWindow.Rect();
            Assert(GetWindowRgn(window,observed)==3&&GetRgnBox(observed,out box)==3,"cover is a complex native region");
            int width=before.Right-before.Left,height=before.Bottom-before.Top;
            Assert(box.Left==0&&box.Top==0&&box.Right==width&&box.Bottom==height,"region retains full physical cover bounds");
            Assert(!PtInRegion(observed,0,0)&&PtInRegion(observed,1,0)&&PtInRegion(observed,0,1),"only the top-left corner pixel is removed");
            Assert(PtInRegion(observed,width/2,height/2)&&PtInRegion(observed,width-1,height-1),"center and opposite corner remain opaque");
        } finally { if(observed!=IntPtr.Zero)DeleteObject(observed); }
    }
    [STAThread] static int Main() {
        try { return Check(); }
        catch (Exception error) { Console.WriteLine("Handoff check failed at " + phase + ": " + error.GetType().Name + " / " + error.Message); return 1; }
    }
    static int Check() {
        foreach (int scale in new[] { 1, 2, 3 }) {
            var outer = new NativeWindow.Rect { Left = -1920, Top = -8, Right = 8, Bottom = 1088 };
            var visible = new NativeWindow.Rect { Left = -1912, Top = 0, Right = 0, Bottom = 1080 };
            outer.Left *= scale; outer.Top *= scale; outer.Right *= scale; outer.Bottom *= scale;
            visible.Left *= scale; visible.Top *= scale; visible.Right *= scale; visible.Bottom *= scale;
            var crop = NativeWindow.Crop(outer, visible);
            Assert(crop.Left == 8 * scale && crop.Top == 8 * scale && crop.Right == 1920 * scale && crop.Bottom == 1088 * scale, "negative-monitor frame crop");
        }
        var source = new Window { Width = 720, Height = 480, Left = -22000, Top = -22000, WindowStartupLocation = WindowStartupLocation.Manual, WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false, Background = Brushes.DimGray };
        var overlay = new Window { Width = 600, Height = 400, Left = -23000, Top = -23000, WindowStartupLocation = WindowStartupLocation.Manual, WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false, Background = Brushes.Black };
        IntPtr thumbnail = IntPtr.Zero;
        source.ResizeMode = ResizeMode.NoResize; overlay.ResizeMode = ResizeMode.NoResize;
        try {
            phase = "show-own-windows";
            source.Show(); overlay.Show();
            IntPtr target = new WindowInteropHelper(source).Handle, host = new WindowInteropHelper(overlay).Handle;
            int pid = Process.GetCurrentProcess().Id;
            var content = new System.Windows.Controls.Border { Background = Brushes.DarkViolet };
            overlay.Content = content;
            phase = "match-frame";
            Assert(!NativeWindow.MatchFrame(host, target, -1), "other process ownership rejected");
            var unchanged = Bounds(host);
            Assert(!NativeWindow.MatchLayout(overlay,target,-1),"layout rejects non-owned target");
            Assert(NativeWindow.SameFrame(unchanged,Bounds(host)),"non-owned target cannot move cover");
            Assert(NativeWindow.MatchLayout(overlay, target, pid), "managed layout matches actual frame");
            Drain(overlay);
            phase="background-preparation-region";
            CheckPreparationRegion(overlay);
            phase = "unchanged-frame-has-no-native-placement";
            var placements = new List<NativeWindow.Rect>();
            bool observePlacements = false;
            HwndSourceHook placementObserver = delegate(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled) {
                if(observePlacements && message==0x0047)placements.Add(Bounds(host));
                return IntPtr.Zero;
            };
            HwndSource.FromHwnd(host).AddHook(placementObserver);
            observePlacements=true;
            for(int i=0;i<20;i++)Assert(NativeWindow.MatchLayout(overlay,target,pid),"unchanged frame remains matched");
            Drain(overlay); observePlacements=false;
            Assert(placements.Count==0,"unchanged frame must not reissue native position/size messages");
            phase = "WPF-layout-and-opacity";
            foreach (double opacity in new[] { 1.0, .75, .5, .25, 0.0, 1.0 }) {
                overlay.Left = -21000; overlay.Top = -21000; overlay.Width = 550; overlay.Height = 320;
                overlay.Opacity = opacity; overlay.UpdateLayout();
                Assert(NativeWindow.MatchLayout(overlay, target, pid), "synchronize managed and physical bounds");
                overlay.Dispatcher.Invoke(new Action(delegate {}), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert(NativeWindow.FramesEqual(host, target), "layout/opacity must not undo frame lock");
                Assert(Math.Abs(content.ActualWidth-source.ActualWidth)<2 && Math.Abs(content.ActualHeight-source.ActualHeight)<2, "content="+content.ActualWidth+"x"+content.ActualHeight+" target="+source.ActualWidth+"x"+source.ActualHeight+" overlay="+overlay.ActualWidth+"x"+overlay.ActualHeight);
            }
            source.Width = 1280; source.Height = 720; source.UpdateLayout();
            source.Dispatcher.Invoke(new Action(delegate {}), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            NativeWindow.Flush();
            NativeWindow.Rect finalFrame;
            Assert(NativeWindow.PresentableFrame(target,pid,out finalFrame),"resized target has valid final frame");
            placements.Clear();observePlacements=true;
            Assert(NativeWindow.MatchLayout(overlay, target, pid), "follow target resize");
            Drain(overlay);observePlacements=false;
            Assert(placements.Count>0,"resize exercises actual native placement");
            foreach(var rect in placements)Assert(NativeWindow.SameFrame(rect,finalFrame),"cover must not expose intermediate resize rectangles");
            phase="preparation-region-after-resize";
            CheckPreparationRegion(overlay);
            overlay.Opacity = .5;
            overlay.Dispatcher.Invoke(new Action(delegate {}), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert(NativeWindow.FramesEqual(host, target), "resized fade remains aligned");
            phase = "invalid-frame-does-not-move-cover";
            unchanged=Bounds(host);
            source.Width=300;source.Height=200;Drain(source);
            Assert(!NativeWindow.MatchLayout(overlay,target,pid),"too-small target refused before placement");
            Assert(NativeWindow.SameFrame(unchanged,Bounds(host)),"small target leaves cover in place");
            source.Width=1280;source.Height=720;Drain(source);
            source.WindowState=WindowState.Minimized;Drain(source);
            Assert(IsIconic(target),"fixture was minimized");
            Assert(!NativeWindow.MatchLayout(overlay,target,pid),"minimized target refused before placement");
            Assert(NativeWindow.SameFrame(unchanged,Bounds(host)),"minimized target leaves cover in place");
            phase = "restore-owned-without-focus";
            var foreground=GetForegroundWindow();
            Assert(!NativeWindow.RestoreOwned(target,-1),"restore rejects wrong ownership");
            Assert(IsIconic(target),"wrong-owner restore did not affect fixture");
            Assert(NativeWindow.RestoreOwned(target,pid),"owned minimized fixture restored without foreground request");
            Drain(source);
            Assert(!IsIconic(target),"owned fixture is no longer minimized");
            Assert(GetForegroundWindow()==foreground,"owned restore must not take foreground focus");
            Assert(NativeWindow.MatchLayout(overlay,target,pid),"restored target may be aligned");
            HwndSource.FromHwnd(host).RemoveHook(placementObserver);
            phase = "DWM-register";
            thumbnail = NativeWindow.AttachThumbnail(host, target); Assert(thumbnail != IntPtr.Zero, "DWM live-window registration");
            foreach (byte opacity in new byte[] { 0, 64, 128, 192, 255 }) Assert(NativeWindow.BlendThumbnail(thumbnail, host, target, opacity), "DWM opacity progression: " + NativeWindow.BlendFailure);
            NativeWindow.Flush();
        } finally { NativeWindow.ReleaseThumbnail(thumbnail); overlay.Close(); source.Close(); }
        var hidden=new Window(); IntPtr hiddenHandle=new WindowInteropHelper(hidden).EnsureHandle(); // Never shown; ShowInTaskbar=false would add a hidden owner.
        string launcherImage=Process.GetCurrentProcess().MainModule.FileName;
        var taskbar=new TaskbarIdentity(launcherImage,TaskbarIdentity.ClassOf(hiddenHandle));
        taskbar.Apply(0);
        Assert(TaskbarIdentity.Read(hiddenHandle)==null,"another process's filter leaves own windows untouched");
        new TaskbarIdentity(launcherImage,"OtherWindowClass").Apply(Process.GetCurrentProcess().Id);
        Assert(TaskbarIdentity.Read(hiddenHandle)==null,"windows of another class, such as the tray host, stay untouched");
        taskbar.Apply(Process.GetCurrentProcess().Id);
        Assert(TaskbarIdentity.Read(hiddenHandle)==TaskbarIdentity.AppId,"hidden window receives the taskbar identity before its first show");
        hidden.Close();
        Console.WriteLine("Handoff: own off-screen windows matched; complex preparation regions and resize reapplication, unchanged placement, atomic resize rectangles, invalid/minimized target rejection, non-activating owned restore, negative-frame cropping and DWM blend endpoints passed. Real Codex not controlled.");
        return 0;
    }
}
