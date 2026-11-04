using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace CrystalDragon {
    // Gives the launched client's windows their own taskbar identity. With the package identity they
    // group under the official pin and show its logo, and that pin cannot be rewritten from here.
    // Lasts for the window's life and changes no package file or setting.
    internal sealed class TaskbarIdentity {
        internal const string AppId = "SkyJiashu.CodexReasoningSlider"; // Keep in sync with scripts/install-shortcut.ps1.
        const uint RelaunchCommand = 2, RelaunchIcon = 3, RelaunchName = 4, ModelId = 5;
        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        interface IPropertyStore {
            void GetCount(out uint count); void GetAt(uint index, out PropertyKey key);
            void GetValue(ref PropertyKey key, [Out] PropVariant value);
            void SetValue(ref PropertyKey key, [In] PropVariant value); void Commit();
        }
        [StructLayout(LayoutKind.Sequential, Pack = 4)] struct PropertyKey { public Guid Format; public uint Id; }
        [StructLayout(LayoutKind.Explicit)] sealed class PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Text; }
        delegate bool EnumCallback(IntPtr window, IntPtr parameter);
        [DllImport("ole32.dll")] static extern int PropVariantClear([In, Out] PropVariant value);
        [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid iid, out IPropertyStore store);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int size);
        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        readonly HashSet<IntPtr> applied = new HashSet<IntPtr>();
        readonly string launcher, windowClass;
        // USER icons die with their creating process; this helper outlives the client it serves.
        readonly IntPtr largeIcon, smallIcon;
        // Only the client's UI windows need an identity; helper windows such as its tray host are left alone.
        internal TaskbarIdentity(string launcherPath, string uiWindowClass = "Chrome_WidgetWin_1") {
            launcher = launcherPath; windowClass = uiWindowClass;
            ExtractIconEx(launcher, 0, out largeIcon, out smallIcon, 1);
        }
        internal static string ClassOf(IntPtr window) { var name = new StringBuilder(256); GetClassName(window, name, 256); return name.ToString(); }
        static PropertyKey Key(uint id) { return new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = id }; }
        static IPropertyStore Open(IntPtr window) {
            Guid iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"); IPropertyStore store;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window, ref iid, out store));
            return store;
        }
        static void Write(IPropertyStore store, uint id, string text) {
            PropertyKey key = Key(id); var value = new PropVariant { Type = 31, Text = Marshal.StringToCoTaskMemUni(text) };
            try { store.SetValue(ref key, value); } finally { PropVariantClear(value); }
        }
        internal static string Read(IntPtr window) {
            IPropertyStore store = Open(window); PropertyKey key = Key(ModelId); var value = new PropVariant();
            try { store.GetValue(ref key, value); return value.Type == 31 ? Marshal.PtrToStringUni(value.Text) : null; }
            finally { PropVariantClear(value); Marshal.ReleaseComObject(store); }
        }
        // Hidden windows are included so the identity is in place before the first show.
        internal void Apply(int processId) {
            EnumWindows(delegate(IntPtr window, IntPtr unused) {
                uint owner; GetWindowThreadProcessId(window, out owner);
                if (owner != processId || GetWindow(window, 4) != IntPtr.Zero || ClassOf(window) != windowClass || !applied.Add(window)) return true;
                try {
                    IPropertyStore store = Open(window);
                    try {
                        Write(store, RelaunchCommand, "\"" + launcher + "\" --cold-test");
                        Write(store, RelaunchIcon, launcher + ",0");
                        Write(store, RelaunchName, "Codex");
                        Write(store, ModelId, AppId);
                        store.Commit();
                    } finally { Marshal.ReleaseComObject(store); }
                    IntPtr response;
                    if (largeIcon != IntPtr.Zero) SendMessageTimeout(window, 0x80, new IntPtr(1), largeIcon, 2, 200, out response); // WM_SETICON, SMTO_ABORTIFHUNG
                    if (smallIcon != IntPtr.Zero) SendMessageTimeout(window, 0x80, IntPtr.Zero, smallIcon, 2, 200, out response);
                } catch (COMException) { } // Cosmetic only: the client keeps its own taskbar identity.
                return true;
            }, IntPtr.Zero);
        }
    }
}
