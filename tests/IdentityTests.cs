using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Text;
using CrystalDragon;

class IdentityTests {
    static readonly Type Identity = typeof(Entry).Assembly.GetType("CrystalDragon.OfficialIdentity");
    static readonly BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    static void Assert(bool condition, string label) { if (!condition) throw new Exception(label); }
    static void Refuses(string method, params object[] args) {
        bool refused = false;
        try { Identity.GetMethod(method, PrivateStatic).Invoke(null, args); }
        catch (TargetInvocationException error) { refused = error.InnerException is InvalidDataException || error.InnerException is Win32Exception || error.InnerException is XmlException; }
        Assert(refused, method + " must reject invalid input");
    }
    static XmlReader Manifest(string xml) { return XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }); }
    static string Xml(string path) { return "<Package><Applications><Application Id='App' Executable='" + path + "'/></Applications></Package>"; }
    static byte[] AliasRecord(string family, string app, string executable) {
        byte[] fields = Encoding.Unicode.GetBytes(family + "\0" + app + "\0" + executable + "\0");
        byte[] record = new byte[12 + fields.Length];
        Array.Copy(BitConverter.GetBytes(0x8000001bu), 0, record, 0, 4);
        Array.Copy(BitConverter.GetBytes((ushort)(fields.Length + 4)), 0, record, 4, 2);
        Array.Copy(BitConverter.GetBytes(3u), 0, record, 8, 4); Array.Copy(fields, 0, record, 12, fields.Length); return record;
    }
    static int Main() {
        string fixture = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "identity-fixture");
        Directory.CreateDirectory(Path.Combine(fixture, "app"));
        string file = Path.Combine(fixture, "app", "client.exe"); File.WriteAllText(file, "unsigned offline fixture");
        using (var reader = Manifest(Xml("app/client.exe"))) Assert(Equals(Identity.GetMethod("ResolveExecutable", PrivateStatic).Invoke(null, new object[] { fixture, reader }), file), "valid registered relative path");
        foreach (string invalid in new[] { "../outside.exe", "C:/outside.exe", "app/missing.exe" }) using (var reader = Manifest(Xml(invalid))) Refuses("ResolveExecutable", fixture, reader);
        using (var reader = Manifest("<Package><Applications/></Package>")) Refuses("ResolveExecutable", fixture, reader);
        using (var reader = Manifest("<Package><Applications><Application Id='App' Executable='app/client.exe'/><Application Id='App' Executable='app/client.exe'/></Applications></Package>")) Refuses("ResolveExecutable", fixture, reader);
        using (var reader = Manifest("<!DOCTYPE Package [<!ENTITY test 'app/client.exe'>]><Package><Applications><Application Id='App' Executable='&test;'/></Applications></Package>")) Refuses("ResolveExecutable", fixture, reader);
        Refuses("VerifySignature", file);
        Refuses("VerifySignature", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");
        string family = "OpenAI.Codex_2p2nqsd0c76g0", package = "OpenAI.Codex_1.2.3.4_x64__2p2nqsd0c76g0", app = family + "!App";
        byte[] alias = AliasRecord(family, app, file);
        Identity.GetMethod("ValidateAliasRecord", PrivateStatic).Invoke(null, new object[] { alias, alias.Length, package, file });
        foreach (byte[] invalid in new[] { AliasRecord("Other.Package", app, file), AliasRecord(family, family + "!OtherApp", file), AliasRecord(family, app, @"C:\wrong.exe"), new byte[14] }) Refuses("ValidateAliasRecord", invalid, invalid.Length, package, file);
        Refuses("ValidateAliasRecord", alias, 12, package, file);
        Console.WriteLine("Official identity: traversal/rooted/missing/duplicate entries and DTD refused; unsigned and non-OpenAI executables refused.");
        Console.WriteLine("Execution alias: package/application/image binding, tag and truncated record checks passed.");
        return 0;
    }
}
