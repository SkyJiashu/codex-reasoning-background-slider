[CmdletBinding()]
param(
    [string]$DestinationDirectory=[Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$executable=Join-Path $projectRoot 'CrystalDragon.exe'
if(!(Test-Path -LiteralPath $executable -PathType Leaf)) {
    $executable=Join-Path $projectRoot 'build/CrystalDragon/CrystalDragon.exe'
}
if(!(Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'CrystalDragon.exe was not found. Extract the complete release package, or build the source project first.'
}
if([string]::IsNullOrWhiteSpace($DestinationDirectory)) {throw 'A shortcut destination is required.'}
$destination=[IO.Path]::GetFullPath($DestinationDirectory)
$shortcutPath=Join-Path $destination 'Codex Reasoning Slider.lnk'
$marker='Codex Crystal Dragon launcher (codex-skin-control)'
$shell=New-Object -ComObject WScript.Shell
if(Test-Path -LiteralPath $shortcutPath) {
    if(!(Test-Path -LiteralPath $shortcutPath -PathType Leaf)) {throw 'The shortcut name is already occupied. No shortcut was changed.'}
    $existing=$shell.CreateShortcut($shortcutPath)
    if($existing.Description -ne $marker) {
        throw 'A different shortcut already uses Codex Reasoning Slider. Rename it before installing this independent launcher. Nothing was overwritten.'
    }
}
[IO.Directory]::CreateDirectory($destination) | Out-Null
$shortcut=$shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath=$executable
$shortcut.Arguments='--cold-test'
$shortcut.WorkingDirectory=Split-Path -Parent $executable
$shortcut.IconLocation=$executable+',0'
$shortcut.Description=$marker
$shortcut.Save()
# Carrying the client's identity makes Windows group its window under this shortcut once pinned.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class LinkIdentity {
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    interface IPropertyStore {
        void GetCount(out uint count); void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, [Out] PropVariant value);
        void SetValue(ref PropertyKey key, [In] PropVariant value); void Commit();
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)] struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit)] sealed class PropVariant {
        [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Text;
    }
    [DllImport("ole32.dll")] static extern int PropVariantClear([In, Out] PropVariant value);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr context, int flags, ref Guid iid, out IPropertyStore store);
    static PropertyKey Key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 }; // PKEY_AppUserModel_ID
    static IPropertyStore Open(string path, int flags) {
        Guid iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"); IPropertyStore store;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, flags, ref iid, out store));
        return store;
    }
    public static string Get(string path) {
        IPropertyStore store = Open(path, 0); var value = new PropVariant();
        try { store.GetValue(ref Key, value); return value.Type == 31 ? Marshal.PtrToStringUni(value.Text) : null; }
        finally { PropVariantClear(value); Marshal.ReleaseComObject(store); }
    }
    public static void Set(string path, string id) {
        IPropertyStore store = Open(path, 2); var value = new PropVariant { Type = 31, Text = Marshal.StringToCoTaskMemUni(id) }; // GPS_READWRITE, VT_LPWSTR
        try { store.SetValue(ref Key, value); store.Commit(); }
        finally { PropVariantClear(value); Marshal.ReleaseComObject(store); }
    }
}
'@
try {[LinkIdentity]::Set($shortcutPath,'OpenAI.Codex_2p2nqsd0c76g0!App')}
catch {Write-Warning 'The shortcut works, but once pinned its Codex window may appear under a separate taskbar icon.'}
Write-Output ('Independent launcher shortcut saved: '+$shortcutPath)
