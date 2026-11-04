using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CrystalDragon {
    internal interface IStartupClient {
        int ProcessId { get; }
        bool Ready { get; }
        bool ShellReady { get; }
        bool PaintReady { get; }
        object PreparationTimings { get; }
        IntPtr FindWindow();
    }
    internal sealed class SkinController : IStartupClient {
        readonly string root;
        int lastSavedStage=-1;
        readonly Dictionary<string, string> sessions = new Dictionary<string, string>();
        PipeHost host;
        volatile bool createdIdentityVerified;
        volatile bool restored;
        internal volatile bool Ready;
        internal volatile bool ShellReady;
        internal volatile bool PaintReady;
        internal volatile bool Finished;
        internal string Status = "preparing";
        internal string FailureStage, ErrorClass;
        readonly Stopwatch preparationClock = Stopwatch.StartNew();
        Task<Dictionary<string, object>> identityTask;
        Task<string> payloadTask;
        long preflightStartedMs = -1, identityReadyMs = -1, payloadPreparedMs = -1, processCreatedMs = -1, pageSeenMs = -1, assetsQueueStartedMs = -1, assetsQueuedMs = -1, earlyDecodedMs = -1, skinInstalledMs = -1, uiPreparedMs = -1;
        long nativeShellMs=-1, composerMs=-1, sidebarMs=-1, fontsMs=-1, editorPresentMs=-1, documentFocusedMs=-1, documentVisibleMs=-1, protocolPolls, protocolTimeouts, maxProbeMs, maxSampleMs, settlingResets;
        // Each poll samples readiness and installs/reads our skin in the SAME document
        // and turn, instead of three serial round trips through a busy renderer.
        internal const string PollExpression = "(() => {const renderer=window.codexWindowType==='electron';if(!renderer)return {renderer:false};const shell=!!document.querySelector('#root')&&!!document.querySelector('[data-app-shell-frame], [data-selected-reasoning-effort]');const preparation=window.__dragonPreloadV1?.status();const state=window.__dragonSkinV1?window.__dragonSkinV1.status():document.head&&document.body&&typeof window.__dragonInstallPreparedV1==='function'?window.__dragonInstallPreparedV1():null;return {renderer,shell,preparation,state,warming:typeof window.__dragonInstallPreparedV1==='function',predecoded:!!window.__dragonSkinWarmV1?.decoded};})()";
        // Closing the client during a post-update data migration leaves its lease held, and later launches then fail.
        // Only the bare bootstrap page qualifies: any control means login, an error page or the shell is showing.
        internal const string SlowStartHint = "(() => {const id='dragon-slow-start';const bare=!document.querySelector('[data-app-shell-frame], [data-selected-reasoning-effort], button, input, textarea, a');let hint=document.getElementById(id);if(!bare){hint?.remove();return false;}if(!hint&&document.body){hint=document.createElement('div');hint.id=id;hint.setAttribute('role','status');hint.textContent='Codex 正在准备本地数据，更新后首次启动可能需要几分钟，请不要关闭窗口';hint.style.cssText='position:fixed;left:0;right:0;bottom:12%;text-align:center;font:14px system-ui,sans-serif;color:#fff;opacity:.8;pointer-events:none;z-index:2147483647';document.body.append(hint);}return true;})()";
        const long SlowStartHintDelayMs = 20000;
        internal int? NativeErrorCode;
        internal int ProcessId { get { return host == null ? 0 : host.ProcessId; } }
        int IStartupClient.ProcessId { get { return createdIdentityVerified ? ProcessId : 0; } }
        bool IStartupClient.Ready { get { return Ready; } }
        bool IStartupClient.ShellReady { get { return ShellReady; } }
        bool IStartupClient.PaintReady { get { return PaintReady; } }
        object IStartupClient.PreparationTimings { get { return TimingSnapshot; } }
        internal object TimingSnapshot { get { return new { origin="controller-construction", preflightStartedMs=Interlocked.Read(ref preflightStartedMs), identityReadyMs=Interlocked.Read(ref identityReadyMs), payloadPreparedMs=Interlocked.Read(ref payloadPreparedMs), processCreatedMs=Interlocked.Read(ref processCreatedMs), pageSeenMs=Interlocked.Read(ref pageSeenMs), assetsQueueStartedMs=Interlocked.Read(ref assetsQueueStartedMs), assetsQueuedMs=Interlocked.Read(ref assetsQueuedMs), earlyDecodedMs=Interlocked.Read(ref earlyDecodedMs), skinInstalledMs=Interlocked.Read(ref skinInstalledMs), nativeShellMs=Interlocked.Read(ref nativeShellMs), composerMs=Interlocked.Read(ref composerMs), sidebarMs=Interlocked.Read(ref sidebarMs), fontsMs=Interlocked.Read(ref fontsMs), editorPresentMs=Interlocked.Read(ref editorPresentMs), documentFocusedMs=Interlocked.Read(ref documentFocusedMs), documentVisibleMs=Interlocked.Read(ref documentVisibleMs), uiPreparedMs=Interlocked.Read(ref uiPreparedMs), protocolPolls=Interlocked.Read(ref protocolPolls), protocolTimeouts=Interlocked.Read(ref protocolTimeouts), maxProbeMs=Interlocked.Read(ref maxProbeMs), maxSampleMs=Interlocked.Read(ref maxSampleMs), settlingResets=Interlocked.Read(ref settlingResets) }; } }
        void Mark(ref long field) { Interlocked.CompareExchange(ref field,preparationClock.ElapsedMilliseconds,-1); }
        internal void BeginPreparation() {
            if (identityTask!=null) return;
            Mark(ref preflightStartedMs);
            identityTask=Task.Run(delegate { var identity=Discover(root); Mark(ref identityReadyMs); return identity; });
            payloadTask=Task.Run(delegate { string payload=Payload(); Mark(ref payloadPreparedMs); return payload; });
        }
        internal Task Preparation { get { return identityTask==null ? null : Task.WhenAll(identityTask,payloadTask); } }
        public IntPtr FindWindow() { return !createdIdentityVerified||ProcessId==0 ? IntPtr.Zero : NativeWindow.Find(ProcessId); }
        internal SkinController(string directory) { root = directory; }
        internal static Dictionary<string, object> Discover(string directory) {
            return OfficialIdentity.Discover();
        }
        internal static bool IsAlreadyRunning(string executable) {
            foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable))) {
                using (process) {
                    // A failed start can leave a threadless object behind until a kernel reference drops; it cannot run anything.
                    try { if (process.Threads.Count>0 && OfficialIdentity.IsRunningClient(process,executable)) return true; }
                    catch { return true; } // Cannot prove an existing client absent: do not attempt a cold launch.
                }
            }
            return false;
        }
        // A second official launch wakes a client hidden to the tray through its own single-instance
        // handling, then exits. Returns false when the caller should go on to a normal launch.
        internal static bool ActivateRunning(string directory, bool offerRecovery) {
            try {
                var identity = Discover(directory);
                string executable = Path.GetFullPath((string)identity["path"]);
                if (!IsAlreadyRunning(executable)) return false;
                NativeWindow.AllowForeground();
                Process.Start(new ProcessStartInfo((string)identity["launchPath"]) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) }).Dispose();
                var clock = Stopwatch.StartNew();
                bool shown = false;
                while (clock.ElapsedMilliseconds < 6000) {
                    foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable))) {
                        using (process) {
                            IntPtr window = NativeWindow.Find(process.Id, true);
                            if (window == IntPtr.Zero) continue;
                            shown = true;
                            if (NativeWindow.RevealOwned(window, process.Id)) return true;
                        }
                    }
                    Thread.Sleep(100);
                }
                if (shown || !offerRecovery) return true; // A window exists; only the focus request did not land.
                // Windows close but the processes stay when the launcher's debugging pipe is lost; nothing can wake that client.
                if (MessageBox.Show("Codex 还在后台运行，但已经打不开窗口。\n\n要结束它并重新启动吗？未保存的内容可能丢失。", "Codex 推理背景滑块", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return true;
                KillClient(executable);
                return false;
            } catch { return false; } // The normal launch path then reports the problem.
        }
        static void KillClient(string executable) {
            foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable))) {
                using (process) { if (process.Threads.Count > 0 && OfficialIdentity.IsRunningClient(process, executable)) { process.Kill(); process.WaitForExit(5000); } }
            }
        }
        // The client's window close button only hides it, so this is what really ends it.
        internal void RequestQuit() {
            var client = host; string executable = clientExecutable;
            if (client == null || executable == null) return;
            // The client's own quit path: it saves state and asks before interrupting active chats,
            // so answering "cancel" must leave it running. Never force this one.
            string session = quitSession;
            try { if (session != null && Equals(client.Evaluate(session, QuitExpression), true)) return; } catch { }
            // Older or changed clients only. Browser.close closes the windows without quitting the app.
            try { client.RequestBrowserClose(3000); } catch { } // The pipe closing with the client also lands here.
            if (!client.WaitForExit(8000)) KillClient(executable);
        }
        const string QuitExpression = "(()=>{const bridge=window.electronBridge;if(typeof bridge?.sendMessageFromView!=='function')return false;bridge.sendMessageFromView({type:'quit-app'});return true;})()";
        volatile string quitSession;
        string clientExecutable;
        internal void RequestRestore() { restored = true; }
        int ReadStartupStage() {
            // Our own one-byte visual cache, never native app/account configuration.
            string path=Path.Combine(root,"appearance-stage.txt");
            try {
                if(File.Exists(path)&&new FileInfo(path).Length==1) {
                    byte[] saved=File.ReadAllBytes(path);
                    if(saved.Length==1&&saved[0]>='0'&&saved[0]<='4')return saved[0]-'0';
                }
            } catch(IOException) { }
            return 4; // First launch continues the film's mascot, with a neutral theme.
        }
        void RememberStage(Dictionary<string,object> state) {
            if(!Equals(state["status"],"confirmed")||!state.ContainsKey("stage")||!(state["stage"] is int))return;
            int value=(int)state["stage"];if(value<0||value>4||value==lastSavedStage)return;
            try {
                string path=Path.Combine(root,"appearance-stage.txt"),temp=path+".next";
                File.WriteAllBytes(temp,new[]{(byte)('0'+value)});
                if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
                lastSavedStage=value;
            } catch(IOException) { }
        }
        string Payload() {
            string[] names = { "01_Light.png", "medium.png", "high.png", "extra-high.png", "ultra.png" };
            string[] assets = names.Select(name => "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "assets", name)))).ToArray();
            var json = new JavaScriptSerializer { MaxJsonLength = 16000000 };
            return "(() => {const module={exports:{}};" + File.ReadAllText(Path.Combine(root, "skin.js")) + ";const assets=" + json.Serialize(assets) + ",css=" + json.Serialize(File.ReadAllText(Path.Combine(root, "skin.css"))) + ";const state=module.exports.prewarm(window,assets);window.__dragonInstallPreparedV1=()=>{const status=module.exports.install(window,assets,css,"+ReadStartupStage()+");if(status.installed)delete window.__dragonInstallPreparedV1;return status;};(()=>{const module={exports:{}};" + File.ReadAllText(Path.Combine(root,"preload.js")) + ";module.exports.install(window);})();return state;})()";
        }
        internal void Run() {
            try {
                Status = "official-identity-check";
                BeginPreparation();
                var identity = identityTask.GetAwaiter().GetResult();
                string executable = Path.GetFullPath((string)identity["path"]);
                clientExecutable = executable;
                Status = "existing-client-check";
                if (IsAlreadyRunning(executable)) { Status = "cold-start-required"; return; }
                Status = "background-payload-prepare";
                string payload = null;
                Status = "private-pipe-create"; host = new PipeHost();
                Status = "official-client-create"; host.Start((string)identity["launchPath"], Path.GetDirectoryName(executable));
                Mark(ref processCreatedMs);
                Status = "created-client-identity-check"; host.ValidateExecutable(executable); createdIdentityVerified=true; Status = "client-started";
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                    bool cleaned = false, preparedOnce = false, hintSettled = false;
                    var taskbar = new TaskbarIdentity(Path.Combine(root, "CrystalDragon.exe"));
                    while (!host.HasExited) {
                        taskbar.Apply(host.ProcessId);
                        if (restored) {
                            if (!cleaned) {
                                cleaned = true;
                                foreach (string session in sessions.Values) try { host.Evaluate(session, "(() => {document.getElementById('dragon-slow-start')?.remove();window.__dragonPreloadV1?.dispose();window.__dragonSkinWarmV1?.dispose();window.__dragonSkinV1?.dispose();delete window.__dragonInstallPreparedV1;return true;})()"); } catch { Status = "restore-unconfirmed"; }
                                if (Status != "restore-unconfirmed") Status = "restored";
                                Entry.WriteStatus(root, Status, Ready, true, FailureStage, ErrorClass, NativeErrorCode);
                            }
                        } else if (!host.Faulted) {
                            try {
                                var targets = host.Send("Target.getTargets", new { }, null, 3000);
                                bool currentPrepared = false, currentDecoded = false, currentPaint = false;
                                string passStatus = "native-ui-awaiting-shell", preparedStatus = null;
                                foreach (object raw in (IEnumerable)targets["targetInfos"]) {
                                    var target = raw as Dictionary<string, object>;
                                    if (target == null || !Equals(target["type"], "page")) continue;
                                    string url = target["url"] as string, id = target["targetId"] as string;
                                    if (url == null || !url.StartsWith("app://", StringComparison.Ordinal) || id == null || id.Length > 128) continue;
                                    string session;
                                    if (!sessions.TryGetValue(id, out session)) {
                                        var attached = host.Send("Target.attachToTarget", new { targetId = id, flatten = true }, null, 3000);
                                        session = (string)attached["sessionId"]; sessions.Add(id, session);
                                    }
                                    long probeStarted=preparationClock.ElapsedMilliseconds;
                                    Interlocked.Increment(ref protocolPolls);
                                    var probe = host.Evaluate(session, PollExpression) as Dictionary<string, object>;
                                    Interlocked.Exchange(ref maxProbeMs,Math.Max(Interlocked.Read(ref maxProbeMs),preparationClock.ElapsedMilliseconds-probeStarted));
                                    if (probe == null || !Equals(probe["renderer"], true)) continue;
                                    quitSession = session;
                                    Mark(ref pageSeenMs);
                                    if(!hintSettled && preparationClock.ElapsedMilliseconds>=SlowStartHintDelayMs) hintSettled=!Equals(host.Evaluate(session,SlowStartHint),true);
                                    var state=probe["state"] as Dictionary<string,object>;
                                    if (state==null && !Equals(probe["warming"],true)) {
                                        if (payload==null)payload=payloadTask.GetAwaiter().GetResult();
                                        Mark(ref assetsQueueStartedMs);
                                        host.Evaluate(session,payload);
                                        Mark(ref assetsQueuedMs);
                                        probe=host.Evaluate(session,PollExpression) as Dictionary<string,object>;
                                        if(probe==null||!Equals(probe["renderer"],true))continue;
                                        state=probe["state"] as Dictionary<string,object>;
                                    }
                                    bool predecoded=Equals(probe["predecoded"],true);
                                    if(predecoded) Mark(ref earlyDecodedMs);
                                    var preparation = probe["preparation"] as Dictionary<string, object>;
                                    if(!preparedOnce && preparation!=null) {
                                        if(Equals(preparation["editorPresent"],true))Mark(ref editorPresentMs);
                                        if(Equals(preparation["documentFocused"],true))Mark(ref documentFocusedMs);
                                        if(Equals(preparation["documentVisible"],true))Mark(ref documentVisibleMs);
                                        Interlocked.Exchange(ref maxSampleMs,Math.Max(Interlocked.Read(ref maxSampleMs),Convert.ToInt64(preparation["sampleCostMs"])));
                                        if(Equals(preparation["shellReady"],true)) {
                                            Mark(ref nativeShellMs);
                                            if(Equals(preparation["composerReady"],true))Mark(ref composerMs);
                                            if(Equals(preparation["sidebarReady"],true))Mark(ref sidebarMs);
                                            if(Equals(preparation["fontsReady"],true))Mark(ref fontsMs);
                                        }
                                        Interlocked.Exchange(ref settlingResets,Convert.ToInt64(preparation["settlingResets"]));
                                    }
                                    bool uiReady = preparation!=null && preparation.ContainsKey("ready") && Equals(preparation["ready"],true);
                                    if (state != null && state.ContainsKey("installed") && Equals(state["installed"], true)) {
                                        RememberStage(state);
                                        Mark(ref skinInstalledMs);
                                        bool decoded = state.ContainsKey("assetsDecoded") && Equals(state["assetsDecoded"],true);
                                        if (decoded) Mark(ref earlyDecodedMs);
                                        bool painted = state.ContainsKey("paintReady") && Equals(state["paintReady"],true);
                                        currentDecoded = currentDecoded || decoded;
                                        passStatus = !uiReady ? "native-ui-preparing-"+(preparation==null ? "unknown" : preparation["reason"] as string) : !decoded ? "background-decoding" : !painted ? "prepared-behind-cover" : Equals(state["status"], "confirmed") ? "skin-following-native" : Equals(state["status"], "preview") ? "skin-previewing-native" : "skin-awaiting-native-control";
                                        if (uiReady && decoded) { Mark(ref uiPreparedMs); currentPrepared=true; currentPaint=currentPaint||painted; preparedStatus=passStatus; }
                                    }
                                }
                                // UI and art must be prepared in the same verified document.
                                ShellReady=currentPrepared; Ready=currentDecoded; PaintReady=currentPaint;
                                // A working decoded skin must not be removed merely because
                                // a native editor/sidebar diagnostic uses an unfamiliar layout.
                                preparedOnce=preparedOnce||currentDecoded; Status=preparedStatus??passStatus;
                            } catch (TimeoutException) { Interlocked.Increment(ref protocolTimeouts); ShellReady=false; Ready=false; PaintReady=false; Status="connection-not-confirmed"; }
                            catch { ShellReady=false; Ready=false; PaintReady=false; Status="connection-not-confirmed"; }
                        }
                        if (!preparedOnce && DateTime.UtcNow > deadline && !restored) { restored = true; Status = "skin-unavailable-native-client-preserved"; }
                        Entry.WriteStatus(root, Status, Ready, restored, FailureStage, ErrorClass, NativeErrorCode);
                        host.WaitForExit(PaintReady && ShellReady && Ready ? 1500 : 100);
                    }
                }
                Status = "client-exited";
            } catch (Exception error) {
                FailureStage = Status; ErrorClass = error.GetType().Name;
                var native = error as Win32Exception; NativeErrorCode = native == null ? (int?)null : native.NativeErrorCode;
                Status = error is TimeoutException ? "identity-timeout" : "startup-not-confirmed";
            } finally {
                // Never close the debugging transport while its client is still running.
                if (host != null && host.ProcessId != 0) {
                    host.WaitForExit(-1);
                    host.CloseAfterExit();
                }
                Finished = true; Entry.WriteStatus(root, Status, Ready, restored, FailureStage, ErrorClass, NativeErrorCode);
            }
        }
    }

    internal sealed class Splash : Window {
        readonly DispatcherTimer timer;
        readonly Stopwatch wall = Stopwatch.StartNew(), fadeClock = new Stopwatch();
        readonly IStartupClient controller;
        readonly bool preview;
        readonly FilmSurface film;
        readonly string directory;
        bool leaving;
        IntPtr target;
        bool matchedFrame, transitionComplete, skipped;
        bool firstFrameQueued, firstFramePresented, timedOut, stableSample;
        bool focusTransferred;
        bool backgroundPreparationEnabled, coverUpdateQueued;
        long stableSince, nextWindowSearchMs, firstFrameMs = -1, fadeStartedMs = -1;
        long animationEndedMs = -1, shellReadyMs = -1, assetsReadyMs = -1;
        NativeWindow.Rect previousFrame;
        internal event Action FirstFramePresented;
        internal bool CompletedNormally {get{return transitionComplete&&!skipped&&!timedOut;}}
        internal Splash(string directory, IStartupClient live, string filmDirectory, NativeWindow.Rect? initialFrame = null) {
            this.directory=directory;
            controller = live; preview = live == null;
            Topmost = !preview;
            // Keep the native surface invisible until WPF has authored its first frame.
            // The client is not launched during this interval, so no unready app can flash through.
            Opacity = preview ? 1 : 0; ShowInTaskbar = false;
            Title = preview ? "Codex 推理背景滑块 · 动画预览" : "Codex 推理背景滑块";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16,14,25));
            Width = Math.Min(1120, SystemParameters.WorkArea.Width * .88); Height = Width * 9 / 16;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SourceInitialized += delegate {
                var handle = new WindowInteropHelper(this).Handle;
                NativeWindow.DisableOwnTransitions(handle);
                if (!preview) NativeWindow.PlaceLayout(this, initialFrame ?? NativeWindow.InitialFrame());
            };
            SizeChanged += delegate { QueueBackgroundPreparation(); };
            DpiChanged += delegate { QueueBackgroundPreparation(); };
            film = new FilmSurface(filmDirectory);
            Content = film;
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Skip(); };
            MouseLeftButtonDown += delegate { if (preview && !leaving) DragMove(); };
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += delegate {
                if (leaving) {
                    double progress = DataScene.Ease(fadeClock.Elapsed.TotalSeconds / .55);
                    if (!preview && target != IntPtr.Zero) NativeWindow.MatchLayout(this, target, controller.ProcessId);
                    Opacity = 1-progress;
                    if (progress >= 1) {
                        transitionComplete = true; NativeWindow.Flush(); Close();
                        if (!preview && ShowActivated) focusTransferred=NativeWindow.RevealOwned(target,controller.ProcessId);
                        WriteHandoffReport();
                    }
                    return;
                }
                bool nativeWindowAvailable = preview;
                film.Tick();
                if (film.Completed && animationEndedMs<0) animationEndedMs=wall.ElapsedMilliseconds;
                if (!preview) {
                    if (controller.ShellReady && shellReadyMs<0) shellReadyMs=wall.ElapsedMilliseconds;
                    if (controller.Ready && assetsReadyMs<0) assetsReadyMs=wall.ElapsedMilliseconds;
                    NativeWindow.Rect frame = new NativeWindow.Rect();
                    // Finding a window includes a cross-process responsiveness query.
                    // Reuse the owned HWND so that query cannot stall every animation frame.
                    nativeWindowAvailable=target!=IntPtr.Zero && NativeWindow.PresentableFrame(target,controller.ProcessId,out frame);
                    if(!nativeWindowAvailable && wall.ElapsedMilliseconds>=nextWindowSearchMs) {
                        nextWindowSearchMs=wall.ElapsedMilliseconds+250;
                        var candidate=controller.FindWindow();
                        if(candidate!=IntPtr.Zero) {
                            if(candidate!=target)stableSample=false;
                            target=candidate;
                        }
                        nativeWindowAvailable=target!=IntPtr.Zero && NativeWindow.PresentableFrame(target,controller.ProcessId,out frame);
                    }
                    if (nativeWindowAvailable) {
                        if (!stableSample || !NativeWindow.SameFrame(frame,previousFrame)) { stableSince=wall.ElapsedMilliseconds; matchedFrame=false; }
                        stableSample=true; previousFrame=frame;
                        if (wall.ElapsedMilliseconds-stableSince>=400) matchedFrame=NativeWindow.MatchLayout(this,target,controller.ProcessId);
                    } else { stableSample=false; matchedFrame=false; }
                }
                // The MP4's actual end event starts the requested 300ms hold.
                // Neither codec opening time nor native editor readiness truncates it.
                if (animationEndedMs>=0 && wall.ElapsedMilliseconds-animationEndedMs>=300 && nativeWindowAvailable) Leave();
                else if (wall.Elapsed.TotalSeconds>35) { timedOut=true; Leave(); }
            };
            ContentRendered += delegate {
                if (firstFrameQueued) return;
                firstFrameQueued=true; Opacity=1;
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(delegate {
                    if (!IsVisible) return;
                    if (!preview) backgroundPreparationEnabled=NativeWindow.EnableBackgroundPreparation(this);
                    NativeWindow.Flush(); firstFramePresented=true; firstFrameMs=wall.ElapsedMilliseconds;
                    timer.Start();
                    if (FirstFramePresented!=null) FirstFramePresented();
                    film.Start();
                }));
            };
            Closed += delegate {
                timer.Stop();
                film.Close();
                WriteHandoffReport();
            };
        }
        void QueueBackgroundPreparation() {
            if (preview || !firstFramePresented || leaving || coverUpdateQueued) return;
            coverUpdateQueued=true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(delegate {
                coverUpdateQueued=false;
                if (IsVisible && !leaving) backgroundPreparationEnabled=NativeWindow.EnableBackgroundPreparation(this);
            }));
        }
        void WriteHandoffReport() {
            try {
                string qa = Path.Combine(directory, "qa"); Directory.CreateDirectory(qa);
                var report = new {
                    buildVersion="1.0.3.0", previewOnly=preview, firstFramePresented, firstFrameMs,
                    frameMatched=matchedFrame, backgroundPreparationEnabled,
                    shellReady=!preview&&controller.ShellReady, assetsDecoded=!preview&&controller.Ready,
                    rendererPaintReady=!preview&&controller.PaintReady,
                    transitionMode="cached-film-300ms-crossfade", playbackRate=DataScene.PlaybackRate,
                    filmOpened=film.Opened, filmCompleted=film.Completed, filmFailed=film.Failed, filmFailureClass=film.FailureClass,
                    startupTiming=preview?null:controller.PreparationTimings,
                    animationEndedMs, shellReadyMs, assetsReadyMs, fadeStartedMs,
                    endWaitMs=animationEndedMs<0||fadeStartedMs<0?0:Math.Max(0,fadeStartedMs-animationEndedMs),
                    transitionComplete, timedOut, skipped, focusTransferred, utc=DateTime.UtcNow.ToString("o")
                };
                File.WriteAllText(Path.Combine(qa,"handoff.json"),new JavaScriptSerializer().Serialize(report));
            } catch { }
        }
        internal void StopAnimation() { timer.Stop(); film.Pause(); Opacity = 1; }
        internal void Skip() { Leave(true); }
        void Leave(bool skip = false) {
            if (leaving) return; leaving = true; skipped = skip;
            if (!preview && controller.ProcessId != 0) {
                if (target == IntPtr.Zero) target = controller.FindWindow();
                if (!NativeWindow.IsOwned(target,controller.ProcessId)) target=NativeWindow.Find(controller.ProcessId,true);
                if (NativeWindow.RestoreOwned(target,controller.ProcessId)) {
                    matchedFrame=NativeWindow.MatchLayout(this,target,controller.ProcessId);
                    // Focusing a valid owned client must not depend on successful geometry alignment.
                    if (ShowActivated) focusTransferred=NativeWindow.RevealOwned(target,controller.ProcessId);
                    NativeWindow.Flush();
                }
            }
            if (Opacity == 0) { Close(); return; }
            film.Pause();timer.Interval=TimeSpan.FromMilliseconds(16);
            fadeStartedMs=wall.ElapsedMilliseconds; fadeClock.Start();
        }
    }

    public static class Entry {
        static string ControlName { get { return "CrystalDragonSkin-" + WindowsIdentity.GetCurrent().User.Value; } }
        static void Control(SkinController controller) {
            Task.Run(delegate {
                while (!controller.Finished) try {
                    var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false);
                    security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (var server = new NamedPipeServerStream(ControlName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None, 1024, 1024, security)) {
                        server.WaitForConnection();
                        // Fixed byte protocol; no variable-length request or client data is accepted.
                        int action = server.ReadByte();
                        if (action == 1) controller.RequestRestore();
                        if (action == 2) Task.Run((Action)controller.RequestQuit);
                        server.WriteByte(action == 1 || action == 2 ? (byte)1 : (byte)0); server.Flush();
                    }
                } catch { Thread.Sleep(500); }
            });
        }
        static int Quit() {
            try {
                using (var client = new NamedPipeClientStream(".", ControlName, PipeDirection.InOut)) {
                    client.Connect(1200); client.WriteByte(2); client.Flush();
                    return client.ReadByte() == 1 ? 0 : 1;
                }
            } catch { return 1; }
        }
        static int Restore() {
            try {
                using (var client = new NamedPipeClientStream(".", ControlName, PipeDirection.InOut)) {
                    client.Connect(1200); client.WriteByte(1); client.Flush();
                    MessageBox.Show(client.ReadByte() == 1 ? "已请求恢复原生外观。请等待数秒；恢复结果记录在测试包 qa/status.json。辅助进程会等 Codex 正常退出后结束，不会关闭 Codex。" : "恢复请求未确认。", "Codex 推理背景滑块");
                }
                return 0;
            } catch { MessageBox.Show("没有找到本启动器的活动连接。无需修改官方客户端。", "Codex 推理背景滑块"); return 1; }
        }
        internal static void WriteStatus(string directory, string status, bool ready, bool restored, string failureStage = null, string errorClass = null, int? nativeErrorCode = null) {
            try {
                string qa = Path.Combine(directory, "qa"); Directory.CreateDirectory(qa);
                string path = Path.Combine(qa, "status.json"), temp = path + ".next";
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(new { buildVersion = "1.0.3.0", status = status, rendererInstalled = ready, restoreRequested = restored, failureStage = failureStage, error_class = errorClass, nativeErrorCode = nativeErrorCode, utc = DateTime.UtcNow.ToString("o"), liveCompatibilityVerified = false }), Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            } catch { }
        }
        [STAThread] public static int Main(string[] args) {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            try {
                if (args.Length > 1 || (args.Length == 1 && args[0] != "--preview" && args[0] != "--cold-test" && args[0] != "--restore" && args[0] != "--quit" && args[0] != "--render-preview" && args[0] != "--probe" && args[0] != "--probe-create" && args[0] != "--entrypoints")) return 2;
                if (args.Contains("--restore")) return Restore();
                if (args.Contains("--quit")) return Quit();
                if (args.Contains("--entrypoints"))return EntrypointSettings.Manage(root);
                if (args.Contains("--probe")) {
                    var identity = SkinController.Discover(root); Directory.CreateDirectory(Path.Combine(root, "qa"));
                    File.WriteAllText(Path.Combine(root, "qa", "identity.json"), new JavaScriptSerializer().Serialize(new { ready = true, version = identity["version"], launchMethod = "verified-package-alias", coldStartAllowed = !SkinController.IsAlreadyRunning((string)identity["path"]), error_class = (string)null })); return 0;
                }
                if (args.Contains("--probe-create")) {
                    var identity = SkinController.Discover(root);
                    new PipeHost().ProbeCreation((string)identity["launchPath"], (string)identity["path"]);
                    Directory.CreateDirectory(Path.Combine(root, "qa"));
                    File.WriteAllText(Path.Combine(root, "qa", "creation-probe.json"), new JavaScriptSerializer().Serialize(new { ready = true, launchMethod = "verified-package-alias", suspendedCreationVerified = true, applicationCodeExecuted = false, cleanupConfirmed = true, version = identity["version"] }));
                    return 0;
                }
                Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                if (args.Contains("--render-preview")) {
                    string qa = Path.Combine(root, "qa"); Directory.CreateDirectory(qa);
                    var scene = new DataScene(Path.Combine(root, "assets", "ultra.png"));
                    double[] frames = { 1.0, 2.3, 3.2, 5.2 };
                    for (int i = 0; i < frames.Length; i++) scene.RenderPng(frames[i], Path.Combine(qa, "startup-" + (i + 1) + ".png"));
                    return 0;
                }
                bool live = !args.Contains("--preview");
                if (!live) {
                    var frame=NativeWindow.InitialFrame();
                    string cached=CachePreparation.Prepare(root,frame);
                    var preview = new Splash(root,null,cached,frame); preview.Closed += delegate { app.Shutdown(); }; return app.Run(preview);
                }
                bool owns;
                using (Mutex mutex = new Mutex(true, "Local\\" + ControlName, out owns)) {
                    // Without a client yet, the other launcher is still starting it: launching here would race it.
                    if (!owns) { SkinController.ActivateRunning(root, false); return 0; }
                    if (SkinController.ActivateRunning(root, true)) return 0;
                    var frame=NativeWindow.InitialFrame();
                    string cached=CachePreparation.Prepare(root,frame);
                    var controller = new SkinController(root); controller.BeginPreparation();
                    Control(controller);
                    var splash = new Splash(root,controller,cached,frame);
                    splash.Closed+=delegate {
                        if(splash.CompletedNormally)
                            app.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>EntrypointSettings.OfferSafely(root)));
                    };
                    ClientTray tray = null;
                    splash.FirstFramePresented += delegate {
                        try { tray = new ClientTray(Process.GetCurrentProcess().MainModule.FileName, () => SkinController.ActivateRunning(root, false), controller.RequestQuit); } catch { } // Cosmetic: the client runs without it.
                        Task.Run(delegate {
                        controller.Run();
                        app.Dispatcher.BeginInvoke(new Action(delegate {
                            if (controller.ProcessId == 0) {
                                splash.StopAnimation();
                                string message = controller.Status == "cold-start-required"
                                    ? "Codex 仍在运行，包括可能留在后台的窗口。\n\n请先保存工作并手动完整退出 Codex，再双击 Start-Cold-Test.cmd。\n\n本次没有启动新客户端，也没有注入或修改当前页面。"
                                    : "启动条件未通过检查，Codex 没有被启动或修改。\n\n检查步骤：" + controller.FailureStage + "\n错误类型：" + controller.ErrorClass + (controller.NativeErrorCode.HasValue ? "\n系统错误码：" + controller.NativeErrorCode : "") + "\n\n请查看本目录 qa/status.json 后回到聊天继续排查。";
                                // Show the outcome before shutdown, even if the user already skipped the splash.
                                if (splash.IsVisible) MessageBox.Show(splash, message, "Codex 推理背景滑块 · 启动未执行", MessageBoxButton.OK, MessageBoxImage.Information);
                                else MessageBox.Show(message, "Codex 推理背景滑块 · 启动未执行", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            if (tray != null) tray.Dispose();
                            app.Shutdown();
                        }));
                    }); };
                    return app.Run(splash);
                }
            } catch (OperationCanceledException) {return 0;}
            catch (Exception error) {
                var native = error as Win32Exception;
                WriteStatus(root, "launcher-error-" + error.GetType().Name, false, false, args.Contains("--probe-create") ? "suspended-creation-probe" : "launcher", error.GetType().Name, native == null ? (int?)null : native.NativeErrorCode);
                if (args.Contains("--probe") || args.Contains("--render-preview") || args.Contains("--probe-create")) return 1;
                string explanation=error is FileNotFoundException&&error.Message=="ffmpeg-required"
                    ? "首次生成动画缓存需要 FFmpeg。\n\n请安装 FFmpeg 并加入 PATH，或将 ffmpeg.exe 放在启动器目录，再重新启动。生成完成后将直接使用本地缓存。"
                    : "启动器未完成操作。请查看 qa/status.json。不会自动关闭或重启 Codex。";
                MessageBox.Show(explanation,"Codex 推理背景滑块"); return 1;
            }
        }
    }
}
