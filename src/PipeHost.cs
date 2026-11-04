using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CrystalDragon {
    // Private inherited handles; no TCP listener and no user-profile/config access.
    internal sealed class PipeHost {
        // Fixed, process-scoped switches: let the covered client prepare at normal
        // priority/cadence. No settings, package files or system policy are changed.
        internal const string PreparationSwitches = " --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --disable-background-timer-throttling";
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct StartupInfo {
            public int Size; public string Reserved, Desktop, Title;
            public int X, Y, Width, Height, XCount, YCount, Fill, Flags;
            public short Show, ReservedBytes; public IntPtr ReservedData, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)] struct StartupInfoEx { public StartupInfo Info; public IntPtr Attributes; }
        [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process, Thread; public int Id, ThreadId; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr bytes);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
        [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateProcessW(string app, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string cwd, ref StartupInfoEx startup, out ProcessInfo result);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(IntPtr process, uint exitCode);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);
        readonly AnonymousPipeServerStream input = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        readonly AnonymousPipeServerStream output = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        readonly object sendGate = new object(), pendingGate = new object();
        readonly Dictionary<int, TaskCompletionSource<Dictionary<string, object>>> pending = new Dictionary<int, TaskCompletionSource<Dictionary<string, object>>>();
        readonly JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 16000000, RecursionLimit = 80 };
        int nextId; volatile bool faulted;
        IntPtr processHandle;
        internal int ProcessId { get; private set; }
        internal bool Faulted { get { return faulted; } }
        internal bool HasExited { get { return WaitForExit(0); } }
        internal bool WaitForExit(int milliseconds) {
            uint status = WaitForSingleObject(processHandle, milliseconds < 0 ? 0xffffffff : (uint)milliseconds);
            if (status == 0) return true;
            if (status == 258) return false;
            throw new InvalidOperationException("process-wait-failed");
        }

        internal void Start(string executable, string workingDirectory = null) {
            Create(executable, workingDirectory, false);
        }
        // Diagnostic only: the primary thread is never resumed; no application code runs.
        internal void ProbeCreation(string launchPath, string expectedExecutable) {
            try {
                Create(launchPath, Path.GetDirectoryName(expectedExecutable), true);
                ValidateExecutable(expectedExecutable);
            } finally {
                if (processHandle != IntPtr.Zero) {
                    if (!TerminateProcess(processHandle, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "suspended-probe-cleanup");
                    if (!WaitForExit(3000)) throw new TimeoutException("suspended-probe-exit");
                    CloseAfterExit();
                }
            }
        }
        void Create(string executable, string workingDirectory, bool suspended) {
            IntPtr bytes = IntPtr.Zero, list = IntPtr.Zero, handles = IntPtr.Zero;
            bool initialized = false;
            try {
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref bytes);
                list = Marshal.AllocHGlobal(bytes);
                if (!InitializeProcThreadAttributeList(list, 1, 0, ref bytes)) throw new Win32Exception(Marshal.GetLastWin32Error(), "attribute-list");
                initialized = true;
                handles = Marshal.AllocHGlobal(IntPtr.Size * 2);
                IntPtr childRead = new IntPtr(Int64.Parse(output.GetClientHandleAsString()));
                IntPtr childWrite = new IntPtr(Int64.Parse(input.GetClientHandleAsString()));
                Marshal.WriteIntPtr(handles, childRead); Marshal.WriteIntPtr(handles, IntPtr.Size, childWrite);
                if (!UpdateProcThreadAttribute(list, 0, new IntPtr(0x20002), handles, new IntPtr(IntPtr.Size * 2), IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "handle-list");
                StartupInfoEx info = new StartupInfoEx(); info.Info.Size = Marshal.SizeOf(typeof(StartupInfoEx)); info.Attributes = list;
                string command = "\"" + executable + "\"" + PreparationSwitches + " --remote-debugging-pipe --remote-debugging-io-pipes=" + childRead.ToInt64() + "," + childWrite.ToInt64();
                ProcessInfo process;
                // Match a normal registered-package launch. Inherited development
                // overrides can give this client a different userData/singleton
                // namespace from Windows notification activation. Do not inspect
                // or copy their values; change only this helper's environment.
                Environment.SetEnvironmentVariable("CODEX_ELECTRON_USER_DATA_PATH", null, EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("BUILD_FLAVOR", null, EnvironmentVariableTarget.Process);
                if (!CreateProcessW(executable, new StringBuilder(command), IntPtr.Zero, IntPtr.Zero, true, 0x00080000u | (suspended ? 4u : 0u), IntPtr.Zero, workingDirectory ?? Path.GetDirectoryName(executable), ref info, out process)) throw new Win32Exception(Marshal.GetLastWin32Error(), "create-process");
                ProcessId = process.Id; processHandle = process.Process; CloseHandle(process.Thread);
                output.DisposeLocalCopyOfClientHandle(); input.DisposeLocalCopyOfClientHandle();
                if (!suspended) Task.Run((Action)ReadLoop);
            } catch {
                if (ProcessId == 0) { input.Dispose(); output.Dispose(); }
                throw;
            } finally {
                if (initialized) DeleteProcThreadAttributeList(list);
                if (list != IntPtr.Zero) Marshal.FreeHGlobal(list);
                if (handles != IntPtr.Zero) Marshal.FreeHGlobal(handles);
            }
        }
        internal void ValidateExecutable(string expected) {
            int size = 32768; var path = new StringBuilder(size);
            if (!QueryFullProcessImageName(processHandle, 0, path, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error(), "created-process-path");
            if (!String.Equals(Path.GetFullPath(path.ToString()), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("created-process-identity-mismatch");
        }
        void ReadLoop() {
            try {
                byte[] buffer = new byte[65536];
                using (MemoryStream frame = new MemoryStream()) {
                    for (;;) {
                        int n = input.Read(buffer, 0, buffer.Length); if (n == 0) break;
                        int start = 0;
                        for (int i = 0; i < n; i++) if (buffer[i] == 0) {
                            frame.Write(buffer, start, i - start);
                            if (frame.Length > 16000000) throw new InvalidDataException("frame-size");
                            Dictionary<string, object> message = new JavaScriptSerializer { MaxJsonLength = 16000000, RecursionLimit = 80 }.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(frame.ToArray()));
                            frame.SetLength(0); start = i + 1;
                            object id;
                            if (message.TryGetValue("id", out id)) {
                                TaskCompletionSource<Dictionary<string, object>> completion = null;
                                lock (pendingGate) { pending.TryGetValue(Convert.ToInt32(id), out completion); }
                                if (completion != null) completion.TrySetResult(message);
                            }
                        }
                        frame.Write(buffer, start, n - start);
                        if (frame.Length > 16000000) throw new InvalidDataException("frame-size");
                    }
                }
            } catch { } finally {
                faulted = true;
                lock (pendingGate) foreach (var completion in pending.Values) completion.TrySetException(new IOException("transport-ended"));
            }
        }
        internal Dictionary<string, object> Send(string method, object args, string session, int milliseconds) {
            if (method != "Target.getTargets" && method != "Target.attachToTarget" && method != "Target.detachFromTarget" && method != "Runtime.evaluate") throw new ArgumentException("method-not-allowed");
            return Dispatch(method, args, session, milliseconds);
        }
        // Only for the user's explicit "quit" request, kept out of the general allowlist above.
        internal void RequestBrowserClose(int milliseconds) { Dispatch("Browser.close", new { }, null, milliseconds); }
        Dictionary<string, object> Dispatch(string method, object args, string session, int milliseconds) {
            int id = Interlocked.Increment(ref nextId);
            var completion = new TaskCompletionSource<Dictionary<string, object>>();
            lock (pendingGate) pending.Add(id, completion);
            try {
                lock (sendGate) {
                    if (faulted) throw new IOException("transport-unavailable");
                    var message = new Dictionary<string, object> { { "id", id }, { "method", method }, { "params", args } };
                    if (session != null) message["sessionId"] = session;
                    byte[] data = Encoding.UTF8.GetBytes(serializer.Serialize(message) + "\0");
                    output.Write(data, 0, data.Length); output.Flush();
                }
                if (!completion.Task.Wait(milliseconds)) throw new TimeoutException("cdp-timeout");
                var reply = completion.Task.Result;
                if (reply.ContainsKey("error")) throw new InvalidOperationException("cdp-rejected");
                return (Dictionary<string, object>)reply["result"];
            } finally { lock (pendingGate) pending.Remove(id); }
        }
        internal object Evaluate(string session, string expression) {
            var result = Send("Runtime.evaluate", new { expression = expression, returnByValue = true, awaitPromise = false }, session, 3000);
            if (result.ContainsKey("exceptionDetails")) throw new InvalidOperationException("renderer-rejected");
            var remote = (Dictionary<string, object>)result["result"];
            object value; return remote.TryGetValue("value", out value) ? value : null;
        }
        // Closing a live Chromium debugging pipe can exit the client. Keep this host
        // alive until the owned client exits, even after visual restoration.
        internal void CloseAfterExit() {
            if (processHandle != IntPtr.Zero && !HasExited) throw new InvalidOperationException("client-still-running");
            input.Dispose(); output.Dispose();
            if (processHandle != IntPtr.Zero) { CloseHandle(processHandle); processHandle = IntPtr.Zero; }
        }
    }
}
