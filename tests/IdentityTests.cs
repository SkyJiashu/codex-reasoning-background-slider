using System;
using System.ComponentModel;
using System.Diagnostics;
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
    static bool RunningMatch(string expected, string image, string family) {
        return (bool)Identity.GetMethod("MatchesRunningClient",PrivateStatic).Invoke(null,new object[]{expected,image,family});
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
        string current=@"C:\Packages\OpenAI.Codex_2.0.0.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe";
        string previous=@"C:\Packages\OpenAI.Codex_1.0.0.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe";
        string other=@"C:\Packages\Other.ChatGPT_2.0.0.0_x64__other\app\ChatGPT.exe";
        Assert(RunningMatch(current,current,null),"current executable directory remains protected without a package identity");
        Assert(RunningMatch(current,previous,family),"previous package version blocks a second current-version launch");
        Assert(!RunningMatch(current,other,"Other.ChatGPT_other"),"another packaged ChatGPT executable is not Codex");
        Assert(!RunningMatch(current,other,null),"another unpackaged ChatGPT executable is not Codex");
        Assert(!RunningMatch(current,previous,family+".extra"),"package-family prefix lookalike is not accepted");
        using(var own=Process.GetCurrentProcess()) {
            string ownImage=Assembly.GetExecutingAssembly().Location;
            bool matched=(bool)Identity.GetMethod("IsRunningClient",PrivateStatic).Invoke(null,new object[]{own,ownImage});
            Assert(matched,"limited-query handle can inspect the owned offline test process");
            string ownFamily=Identity.GetMethod("ProcessFamily",PrivateStatic).Invoke(null,new object[]{own.Handle}) as string;
            string unrelatedExpected=Path.Combine(fixture,"unrelated","ChatGPT.exe");
            bool unrelated=(bool)Identity.GetMethod("IsRunningClient",PrivateStatic).Invoke(null,new object[]{own,unrelatedExpected});
            Assert(unrelated==RunningMatch(unrelatedExpected,ownImage,ownFamily),"owned native query agrees with exact family/current-directory matching");
            if(ownFamily==null)Assert(!unrelated,"owned unpackaged process does not block an unrelated application");
        }
        Console.WriteLine("Official identity: traversal/rooted/missing/duplicate entries and DTD refused; unsigned and non-OpenAI executables refused.");
        Console.WriteLine("Execution alias: package/application/image binding, tag and truncated record checks passed.");
        Console.WriteLine("Running-client guard: current directory, cross-version package family, unrelated same-name apps and owned-process limited query passed.");
        return 0;
    }
}
