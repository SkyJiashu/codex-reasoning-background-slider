// Adapted from Dragon Codex Boot (MIT); see licenses/dragon-codex-boot-MIT.txt.
using System;
using System.Runtime.InteropServices;

namespace CrystalDragon {
    internal static class NativeWindow {
        internal delegate bool EnumCallback(IntPtr window, IntPtr parameter);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct WindowPosition { public IntPtr Window, After; public int X, Y, Width, Height; public uint Flags; }
        static void ConstrainPosition(IntPtr position, Rect frame) {
            if (position == IntPtr.Zero) return;
            var value = (WindowPosition)Marshal.PtrToStructure(position, typeof(WindowPosition));
            value.X = frame.Left; value.Y = frame.Top; value.Width = frame.Right - frame.Left; value.Height = frame.Bottom - frame.Top;
            value.Flags &= ~3u;
            Marshal.StructureToPtr(value, position, false);
        }
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int mode);
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr window, int mode);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] static extern int GetFrameBounds(IntPtr window, int attribute, out Rect rect, int size);
        [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);
        [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
        [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref ThumbnailProperties properties);
        [DllImport("dwmapi.dll")] static extern int DwmFlush();
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr destination, IntPtr first, IntPtr second, int mode);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] static extern int GetRgnBox(IntPtr region, out Rect bounds);
        [DllImport("gdi32.dll")] static extern bool PtInRegion(IntPtr region, int x, int y);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
        [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr window, IntPtr region);
        // Independently implements the own-cover anti-occlusion approach described in
        // codex-startup-animation PR #1: a one-physical-pixel corner cutout makes the
        // otherwise opaque cover nonrectangular. Call only on initial size/DPI changes.
        internal static bool EnableBackgroundPreparation(System.Windows.Window cover) {
            if (cover == null || !cover.CheckAccess() || cover.AllowsTransparency) return false;
            IntPtr window = new System.Windows.Interop.WindowInteropHelper(cover).Handle;
            Rect bounds;
            if (!IsOwned(window,System.Diagnostics.Process.GetCurrentProcess().Id) || !GetWindowRect(window,out bounds)) return false;
            int width = bounds.Right-bounds.Left, height = bounds.Bottom-bounds.Top;
            if (width<=128 || height<=128) return false;
            IntPtr shape=IntPtr.Zero, corner=IntPtr.Zero, observed=IntPtr.Zero;
            try {
                shape=CreateRectRgn(0,0,width,height); corner=CreateRectRgn(0,0,1,1); observed=CreateRectRgn(0,0,0,0);
                if (shape==IntPtr.Zero || corner==IntPtr.Zero || observed==IntPtr.Zero) return false;
                if (CombineRgn(shape,shape,corner,4)!=3 || SetWindowRgn(window,shape,true)==0) return false;
                shape=IntPtr.Zero; // USER32 now owns this HRGN, including on query failure.
                Rect actual;
                return GetWindowRgn(window,observed)==3 && GetRgnBox(observed,out actual)==3
                    && actual.Left==0 && actual.Top==0 && actual.Right==width && actual.Bottom==height
                    && !PtInRegion(observed,0,0) && PtInRegion(observed,1,0) && PtInRegion(observed,0,1)
                    && PtInRegion(observed,width/2,height/2) && PtInRegion(observed,width-1,height-1);
            } finally {
                if (shape!=IntPtr.Zero) DeleteObject(shape);
                if (corner!=IntPtr.Zero) DeleteObject(corner);
                if (observed!=IntPtr.Zero) DeleteObject(observed);
            }
        }
        internal static void DisableOwnTransitions(IntPtr window) {
            if (!IsOwned(window, System.Diagnostics.Process.GetCurrentProcess().Id)) return;
            int disabled = 1; DwmSetWindowAttribute(window, 3, ref disabled, 4);
        }
        internal static Rect InitialFrame() {
            var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
            return new Rect { Left=area.Left, Top=area.Top, Right=area.Right, Bottom=area.Bottom };
        }
        internal static bool PresentableFrame(IntPtr window, int pid, out Rect frame) {
            frame = new Rect();
            int cloaked;
            return IsOwned(window,pid) && IsWindowVisible(window) && !IsIconic(window)
                && !(DwmGetWindowAttribute(window,14,out cloaked,4)==0 && cloaked!=0)
                && Frame(window,out frame) && frame.Right-frame.Left>=450 && frame.Bottom-frame.Top>=280;
        }
        internal static bool SameFrame(Rect a, Rect b) { return a.Left==b.Left && a.Top==b.Top && a.Right==b.Right && a.Bottom==b.Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct ThumbnailProperties {
            public uint Flags; public Rect Destination, Source; public byte Opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool Visible;
            [MarshalAs(UnmanagedType.Bool)] public bool ClientOnly;
        }
        internal static bool IsOwned(IntPtr window, int processId) { uint id; GetWindowThreadProcessId(window, out id); return IsWindow(window) && id == processId; }
        static bool Frame(IntPtr window, out Rect rect) {
            Rect outer; if (!GetWindowRect(window, out outer)) { rect = new Rect(); return false; }
            // DWM can report a stale/empty visible frame before the first composition.
            if (GetFrameBounds(window, 9, out rect, Marshal.SizeOf(typeof(Rect))) != 0 || rect.Right <= rect.Left || rect.Bottom <= rect.Top
                || rect.Right <= outer.Left || rect.Left >= outer.Right || rect.Bottom <= outer.Top || rect.Top >= outer.Bottom) rect = outer;
            return rect.Right > rect.Left && rect.Bottom > rect.Top;
        }
        internal static bool MatchFrame(IntPtr overlay, IntPtr target, int processId) {
            Rect frame, current;
            if (!IsOwned(target, processId) || !Frame(target, out frame) || frame.Right - frame.Left < 450 || frame.Bottom - frame.Top < 280) return false;
            if (GetWindowRect(overlay, out current) && current.Left == frame.Left && current.Top == frame.Top && current.Right == frame.Right && current.Bottom == frame.Bottom) return true;
            return SetWindowPos(overlay, new IntPtr(-1), frame.Left, frame.Top, frame.Right - frame.Left, frame.Bottom - frame.Top, 0x10)
                && GetWindowRect(overlay, out current) && current.Left == frame.Left && current.Top == frame.Top && current.Right == frame.Right && current.Bottom == frame.Bottom;
        }
        internal static bool MatchLayout(System.Windows.Window overlay, IntPtr target, int processId) {
            Rect frame;
            if (!PresentableFrame(target,processId,out frame)) return false;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(overlay).Handle;
            var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
            if (source == null || source.CompositionTarget == null) return false;
            var rendered = source.CompositionTarget.TransformToDevice.Transform(new System.Windows.Vector(overlay.ActualWidth,overlay.ActualHeight));
            if (FramesEqual(hwnd,target) && Math.Abs(rendered.X-(frame.Right-frame.Left))<1 && Math.Abs(rendered.Y-(frame.Bottom-frame.Top))<1) return true;
            PlaceLayout(overlay,frame);
            return FramesEqual(hwnd,target);
        }
        internal static void PlaceLayout(System.Windows.Window overlay, Rect frame) {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(overlay).Handle;
            var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
            if (source == null || source.CompositionTarget == null || !IsOwned(hwnd,System.Diagnostics.Process.GetCurrentProcess().Id) || frame.Right<=frame.Left || frame.Bottom<=frame.Top) return;
            var transform = source.CompositionTarget.TransformFromDevice;
            var origin = transform.Transform(new System.Windows.Point(frame.Left, frame.Top));
            var size = transform.Transform(new System.Windows.Vector(frame.Right-frame.Left, frame.Bottom-frame.Top));
            // WPF writes each geometry property separately. Keep intermediate native requests
            // at the final rectangle so an exposed corner cannot flash between those writes.
            System.Windows.Interop.HwndSourceHook constraint = delegate(IntPtr w,int message,IntPtr a,IntPtr l,ref bool handled) {
                if(message==0x0046)ConstrainPosition(l,frame);
                return IntPtr.Zero;
            };
            source.AddHook(constraint);
            try {
                overlay.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
                overlay.Left = origin.X; overlay.Top = origin.Y; overlay.Width = size.X; overlay.Height = size.Y;
                overlay.UpdateLayout();
                SetWindowPos(hwnd,new IntPtr(-1),frame.Left,frame.Top,frame.Right-frame.Left,frame.Bottom-frame.Top,0x10);
            } finally { source.RemoveHook(constraint); }
        }
        internal static bool FramesEqual(IntPtr overlay, IntPtr target) {
            Rect a, b;
            return GetWindowRect(overlay, out a) && Frame(target, out b) && a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
        }
        internal static Rect Crop(Rect outer, Rect visible) {
            int width = outer.Right - outer.Left, height = outer.Bottom - outer.Top;
            return new Rect { Left = Math.Max(0, visible.Left - outer.Left), Top = Math.Max(0, visible.Top - outer.Top), Right = Math.Min(width, visible.Right - outer.Left), Bottom = Math.Min(height, visible.Bottom - outer.Top) };
        }
        internal static IntPtr AttachThumbnail(IntPtr overlay, IntPtr target) {
            IntPtr thumbnail; return DwmRegisterThumbnail(overlay, target, out thumbnail) == 0 ? thumbnail : IntPtr.Zero;
        }
        internal static string BlendFailure;
        internal static bool BlendThumbnail(IntPtr thumbnail, IntPtr overlay, IntPtr target, byte opacity) {
            BlendFailure = null;
            Rect outer, visible, destination;
            if (!GetWindowRect(target, out outer) || !Frame(target, out visible) || !GetClientRect(overlay, out destination)) { BlendFailure = "bounds-query"; return false; }
            Rect source = Crop(outer, visible);
            if (source.Right <= source.Left || source.Bottom <= source.Top || destination.Right <= 0 || destination.Bottom <= 0) { BlendFailure = "invalid-rectangles"; return false; }
            var properties = new ThumbnailProperties { Flags = 31, Source = source, Destination = destination, Opacity = opacity, Visible = true, ClientOnly = false };
            int result = DwmUpdateThumbnailProperties(thumbnail, ref properties);
            if (result != 0) BlendFailure = "dwm-" + result;
            return result == 0;
        }
        internal static void ReleaseThumbnail(IntPtr thumbnail) { if (thumbnail != IntPtr.Zero) DwmUnregisterThumbnail(thumbnail); }
        internal static void Flush() { DwmFlush(); }
        internal static bool RestoreOwned(IntPtr target, int processId) {
            if (!IsOwned(target, processId)) return false;
            if (IsIconic(target) || !IsWindowVisible(target)) {
                uint owner; uint thread=GetWindowThreadProcessId(target,out owner);
                // Never wait for a foreign UI thread from the splash dispatcher. True may
                // mean a queued restore; fade ticks and post-Close focus recheck its state.
                if (thread!=GetCurrentThreadId()) return ShowWindowAsync(target,4);
                ShowWindow(target, 4);
            }
            return IsWindowVisible(target) && !IsIconic(target);
        }
        internal static bool RevealOwned(IntPtr target, int processId) {
            if (!RestoreOwned(target, processId)) return false;
            SetForegroundWindow(target);
            return GetForegroundWindow()==target && IsWindowVisible(target) && !IsIconic(target);
        }
        internal static IntPtr Find(int processId, bool includeMinimized = false) {
            IntPtr result = IntPtr.Zero;
            EnumWindows(delegate(IntPtr w, IntPtr unused) {
                uint id; GetWindowThreadProcessId(w, out id);
                if (id != processId || !IsWindowVisible(w) || GetWindow(w, 4) != IntPtr.Zero || (!includeMinimized && IsIconic(w))) return true;
                int cloaked; if (DwmGetWindowAttribute(w, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                Rect r; if (!(includeMinimized && IsIconic(w)) && (!GetClientRect(w, out r) || r.Right < 450 || r.Bottom < 280)) return true;
                IntPtr response;
                if (SendMessageTimeout(w, 0, IntPtr.Zero, IntPtr.Zero, 2, 80, out response) == IntPtr.Zero) return true;
                result = w; return false;
            }, IntPtr.Zero);
            return result;
        }
        internal static void Reveal(int processId) {
            IntPtr w = Find(processId); if (w == IntPtr.Zero) return;
            if (IsIconic(w)) ShowWindow(w, 9);
            SetForegroundWindow(w);
        }
    }
}
