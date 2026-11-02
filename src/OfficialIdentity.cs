using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Win32.SafeHandles;

namespace CrystalDragon {
    internal static class OfficialIdentity {
        internal const string Family = "OpenAI.Codex_2p2nqsd0c76g0";
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr names, ref uint characters, IntPtr buffer);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetPackagePathByFullName(string fullName, ref uint length, StringBuilder path);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder image, ref uint characters);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetPackageFamilyName(IntPtr process, ref uint characters, StringBuilder family);
        [DllImport("wintrust.dll", ExactSpelling = true)] static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool DeviceIoControl(SafeFileHandle file, uint code, IntPtr input, uint inputSize, [Out] byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
        [StructLayout(LayoutKind.Sequential)] struct TrustFile { public uint Size; public IntPtr Path, File, Subject; }
        [StructLayout(LayoutKind.Sequential)] struct TrustData {
            public uint Size; public IntPtr Policy, Sip; public uint Ui, Revocation, Union; public IntPtr File;
            public uint StateAction; public IntPtr State, Url; public uint Flags, Context;
        }
        internal static bool MatchesRunningClient(string executable, string runningImage, string runningFamily) {
            return String.Equals(runningFamily,Family,StringComparison.OrdinalIgnoreCase) ||
                String.Equals(System.IO.Path.GetDirectoryName(runningImage),System.IO.Path.GetDirectoryName(executable),StringComparison.OrdinalIgnoreCase);
        }
        static string ProcessFamily(IntPtr process) {
            uint characters=0;
            int result=GetPackageFamilyName(process,ref characters,null);
            if(result==15700)return null; // APPMODEL_ERROR_NO_PACKAGE: another unpackaged app may share the executable name.
            if(result!=122 || characters==0 || characters>32768)throw new Win32Exception(result,"running-package-query");
            var family=new StringBuilder((int)characters);
            result=GetPackageFamilyName(process,ref characters,family);
            if(result!=0)throw new Win32Exception(result,"running-package-query");
            return family.ToString();
        }
        internal static bool IsRunningClient(Process process, string executable) {
            // Package family survives a Store update while the previous version's
            // directory does not. Query only identity/path, never process memory.
            IntPtr handle=OpenProcess(0x1000,false,process.Id); // PROCESS_QUERY_LIMITED_INFORMATION
            if(handle==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"running-process-query");
            try {
                uint characters=32768;var image=new StringBuilder((int)characters);
                if(!QueryFullProcessImageName(handle,0,image,ref characters))throw new Win32Exception(Marshal.GetLastWin32Error(),"running-process-path");
                return MatchesRunningClient(executable,image.ToString(),ProcessFamily(handle));
            } finally {CloseHandle(handle);}
        }
        static void VerifySignature(string path) {
            IntPtr text = Marshal.StringToCoTaskMemUni(path), fileMemory = IntPtr.Zero;
            TrustData data = new TrustData();
            Guid action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
            try {
                var file = new TrustFile { Size = (uint)Marshal.SizeOf(typeof(TrustFile)), Path = text };
                fileMemory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustFile))); Marshal.StructureToPtr(file, fileMemory, false);
                data.Size = (uint)Marshal.SizeOf(typeof(TrustData)); data.Ui = 2; data.Union = 1;
                data.File = fileMemory; data.StateAction = 1; data.Flags = 0x1000; // Use local trust cache; no network lookup.
                int result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                if (result != 0) throw new Win32Exception(result, "official-signature-untrusted");
                using (var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path))) {
                    string publisher = certificate.GetNameInfo(X509NameType.SimpleName, false);
                    if (publisher != "OpenAI OpCo, LLC" && publisher != "OpenAI, LLC" && publisher != "OpenAI" && publisher != "OpenAI L.L.C.") throw new InvalidDataException("official-publisher-mismatch");
                }
            } finally {
                if (data.State != IntPtr.Zero) { data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
                if (fileMemory != IntPtr.Zero) Marshal.FreeHGlobal(fileMemory);
                Marshal.FreeCoTaskMem(text);
            }
        }
        internal static string ResolveExecutable(string packageRoot, XmlReader reader) {
            packageRoot = System.IO.Path.GetFullPath(packageRoot).TrimEnd('\\');
            var document = new XmlDocument { XmlResolver = null }; document.Load(reader);
            XmlNodeList entries = document.SelectNodes("/*[local-name()='Package']/*[local-name()='Applications']/*[local-name()='Application'][@Id='App']");
            if (entries.Count != 1 || entries[0].Attributes["Executable"] == null) throw new InvalidDataException("registered-entry-count");
            string relative = entries[0].Attributes["Executable"].Value;
            if (System.IO.Path.IsPathRooted(relative)) throw new InvalidDataException("registered-entry-path");
            string executable = System.IO.Path.GetFullPath(System.IO.Path.Combine(packageRoot, relative));
            if (!executable.StartsWith(packageRoot + "\\", StringComparison.OrdinalIgnoreCase) || !File.Exists(executable)) throw new InvalidDataException("registered-entry-path");
            return executable;
        }
        internal static void ValidateAliasRecord(byte[] record, int length, string packageName, string executable) {
            if (length < 14 || length > record.Length || BitConverter.ToUInt32(record, 0) != 0x8000001b) throw new InvalidDataException("alias-tag");
            int size = BitConverter.ToUInt16(record, 4), count = (int)BitConverter.ToUInt32(record, 8);
            if (size < 6 || size + 8 > length || (size - 4) % 2 != 0 || count < 3 || count > 16) throw new InvalidDataException("alias-record");
            string[] fields = Encoding.Unicode.GetString(record, 12, size - 4).Split('\0');
            if (fields.Length < count || fields[fields.Length - 1] != "") throw new InvalidDataException("alias-record");
            if (!String.Equals(fields[0], Family, StringComparison.OrdinalIgnoreCase) && !String.Equals(fields[0], packageName, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("alias-package-mismatch");
            if (!String.Equals(fields[1], Family + "!App", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("alias-application-mismatch");
            if (!String.Equals(System.IO.Path.GetFullPath(fields[2]), executable, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("alias-executable-mismatch");
        }
        static string ResolveAlias(string packageRoot, string packageName, string executable) {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1000000 };
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(System.IO.Path.Combine(packageRoot, "AppxManifest.xml"), settings)) document.Load(reader);
            XmlNodeList aliases = document.SelectNodes("/*[local-name()='Package']/*[local-name()='Applications']/*[local-name()='Application'][@Id='App']/*[local-name()='Extensions']/*[local-name()='Extension'][@Category='windows.appExecutionAlias']//*[local-name()='ExecutionAlias']");
            if (aliases.Count != 1 || aliases[0].Attributes["Alias"] == null) throw new InvalidDataException("registered-alias-count");
            string name = aliases[0].Attributes["Alias"].Value;
            if (!Regex.IsMatch(name, @"^[A-Za-z0-9][A-Za-z0-9._-]*\.exe$")) throw new InvalidDataException("registered-alias-name");
            string alias = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", Family, name);
            using (SafeFileHandle file = CreateFileW(alias, 0, 7, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero)) {
                if (file.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "registered-alias-open");
                byte[] record = new byte[16384]; uint returned;
                if (!DeviceIoControl(file, 0x000900a8, IntPtr.Zero, 0, record, (uint)record.Length, out returned, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "registered-alias-read");
                ValidateAliasRecord(record, (int)returned, packageName, executable);
            }
            return alias;
        }
        internal static Dictionary<string, object> Discover() {
            uint count = 0, characters = 0;
            int result = GetPackagesByPackageFamily(Family, ref count, IntPtr.Zero, ref characters, IntPtr.Zero);
            if (result != 122 && result != 0) throw new Win32Exception(result, "registered-package-query");
            if (count != 1 || characters == 0 || characters > 32768) throw new InvalidDataException("registered-package-count");
            IntPtr names = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size)), buffer = Marshal.AllocHGlobal(checked((int)characters * 2));
            string fullName;
            try {
                result = GetPackagesByPackageFamily(Family, ref count, names, ref characters, buffer);
                if (result != 0) throw new Win32Exception(result, "registered-package-query");
                if (count != 1) throw new InvalidDataException("registered-package-count");
                fullName = Marshal.PtrToStringUni(Marshal.ReadIntPtr(names));
            } finally { Marshal.FreeHGlobal(names); Marshal.FreeHGlobal(buffer); }
            Match package = Regex.Match(fullName ?? "", @"^OpenAI\.Codex_(\d+\.\d+\.\d+\.\d+)_x64__2p2nqsd0c76g0$");
            if (!package.Success) throw new InvalidDataException("registered-package-identity");
            uint length = 0;
            result = GetPackagePathByFullName(fullName, ref length, null);
            if (result != 122 || length == 0 || length > 32768) throw new Win32Exception(result, "registered-package-path");
            var path = new StringBuilder((int)length);
            result = GetPackagePathByFullName(fullName, ref length, path);
            if (result != 0) throw new Win32Exception(result, "registered-package-path");
            string packageRoot = System.IO.Path.GetFullPath(path.ToString()).TrimEnd('\\');
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1000000 };
            string executable;
            using (var reader = XmlReader.Create(System.IO.Path.Combine(packageRoot, "AppxManifest.xml"), settings)) executable = ResolveExecutable(packageRoot, reader);
            VerifySignature(executable);
            string alias = ResolveAlias(packageRoot, fullName, executable);
            return new Dictionary<string, object> { { "ready", true }, { "path", executable }, { "launchPath", alias }, { "version", package.Groups[1].Value } };
        }
    }
}
