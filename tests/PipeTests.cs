using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CrystalDragon;

class PipeTests {
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static void Assert(bool valid, string label) { if (!valid) throw new Exception(label); }
    static int Child(string handles) {
        string[] parts = handles.Split(',');
        Task.Run(delegate { Thread.Sleep(12000); Environment.Exit(3); });
        using (var read = new AnonymousPipeClientStream(PipeDirection.In, parts[0]))
        using (var write = new AnonymousPipeClientStream(PipeDirection.Out, parts[1]))
        using (var frame = new MemoryStream()) {
            for (;;) {
                int b = read.ReadByte(); if (b < 0) return 0;
                if (b != 0) { frame.WriteByte((byte)b); continue; }
                var request = Json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(frame.ToArray())); frame.SetLength(0);
                string method = (string)request["method"];
                var args = (Dictionary<string, object>)request["params"];
                object expression; args.TryGetValue("expression", out expression);
                if (Equals(expression, "stall")) continue;
                var reply = new Dictionary<string, object> { { "id", request["id"] } };
                if (Equals(expression, "reject")) reply["error"] = new { code = -32000 };
                else reply["result"] = method == "Target.getTargets" ? (object)new { targetInfos = new object[0] } : new { result = new { type = "string", value = "紫晶龙娘" } };
                byte[] data = Encoding.UTF8.GetBytes("{\"method\":\"test-event\"}\0" + Json.Serialize(reply) + "\0");
                for (int i = 0; i < data.Length; i += 7) { write.Write(data, i, Math.Min(7, data.Length - i)); write.Flush(); }
                if (Equals(expression, "exit")) return 0;
            }
        }
    }
    static int Main(string[] args) {
        foreach (string arg in args) if (arg.StartsWith("--remote-debugging-io-pipes=")) {
            foreach(string flag in PipeHost.PreparationSwitches.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries))
                if(Array.IndexOf(args,flag)<0)return 4;
            return Child(arg.Substring(arg.IndexOf('=') + 1));
        }
        PipeHost host = new PipeHost();
        try {
            host.Start(Assembly.GetExecutingAssembly().Location);
            host.ValidateExecutable(Assembly.GetExecutingAssembly().Location);
            bool wrongImageRefused = false; try { host.ValidateExecutable(@"C:\not-the-owned-child.exe"); } catch (InvalidDataException) { wrongImageRefused = true; }
            Assert(wrongImageRefused, "created process image is bound to its original handle");
            Assert(!host.HasExited, "owned mock child should be alive");
            var reply = host.Send("Target.getTargets", new { }, null, 2000);
            Assert(reply.ContainsKey("targetInfos"), "fragmented target reply");
            Assert(Equals(host.Evaluate(null, "ok"), "紫晶龙娘"), "UTF8 survives fragmented frames");
            bool rejected = false; try { host.Send("Browser.close", new { }, null, 100); } catch (ArgumentException) { rejected = true; }
            Assert(rejected, "method allowlist");
            bool timedOut = false; try { host.Send("Runtime.evaluate", new { expression = "stall" }, null, 100); } catch (TimeoutException) { timedOut = true; }
            Assert(timedOut, "response deadline");
            Assert(Equals(host.Evaluate(null, "ok"), "紫晶龙娘"), "pipe remains usable after timeout");
            rejected = false; try { host.Evaluate(null, "reject"); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "protocol rejection");
            rejected = false; try { host.CloseAfterExit(); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected && !host.HasExited, "live child pipe cannot be closed");
            host.Evaluate(null, "exit"); Assert(host.WaitForExit(3000), "owned child exits"); host.CloseAfterExit();
            Console.WriteLine("Private pipe: inherited handles, UTF8/fragmentation, event filtering, allowlist, timeout recovery, rejection and exit guard passed.");
            return 0;
        } catch (Exception e) { Console.WriteLine("Private pipe test failed: " + e.GetType().Name); return 1; }
    }
}
