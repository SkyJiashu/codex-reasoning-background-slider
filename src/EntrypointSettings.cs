using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;

namespace CrystalDragon {
    internal static class EntrypointSettings {
        static string StateDirectory {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexReasoningBackgroundSlider","entrypoints");}}
        static string ChoicePath {get{return Path.Combine(StateDirectory,"choice.txt");}}
        const string Title="Codex 推理背景滑块 · 启动入口 / Launch shortcuts";
        static Mutex SettingsGate(){return new Mutex(false,"Local\\CodexRbsEntrypoints-"+WindowsIdentity.GetCurrent().User.Value);}
        static bool Enter(Mutex gate){try{return gate.WaitOne(0);}catch(AbandonedMutexException){return true;}}
        static string Quote(string value){return "\""+value.Replace("\"","\\\"")+"\"";}
        internal static void OfferOnce(string root) {
            if(File.Exists(ChoicePath)) {
                if(File.ReadAllText(ChoicePath)=="1")Run(root,"Bind",false);
                return;
            }
            var answer=MessageBox.Show(
                "是否让当前用户的 Codex 桌面和开始菜单快捷方式，通过本启动器打开？\n\n只处理可确认的普通快捷方式，先备份、可恢复。不会接管系统协议、任务栏固定项或官方程序。\n\nRoute existing Codex desktop/Start Menu shortcuts through this launcher? Backups are kept. Protocol handlers and taskbar pins are unchanged.\n\n是 / Yes：绑定快捷方式\n否 / No：继续使用独立入口\n取消 / Cancel：下次再询问",
                Title,MessageBoxButton.YesNoCancel,MessageBoxImage.Question,MessageBoxResult.No);
            if(answer==MessageBoxResult.Cancel)return;
            if(answer==MessageBoxResult.Yes)Run(root,"Bind");
            RememberChoice(answer==MessageBoxResult.Yes);
        }
        internal static int Manage(string root) {
            using(var gate=SettingsGate()) {
                if(!Enter(gate)){MessageBox.Show("已有启动入口设置正在处理，请完成后再试。\nShortcut setup is already open.",Title);return 0;}
                try {
                    var answer=MessageBox.Show(
                        "是 / Yes：备份并绑定当前用户的普通 Codex 快捷方式\n\n否 / No：恢复本项目备份的快捷方式\n\n取消 / Cancel：关闭\n\n仅处理桌面和开始菜单普通 .lnk；系统协议、任务栏及虚拟应用入口保持原样。\nOnly ordinary desktop/Start Menu shortcuts are supported.",
                        Title,MessageBoxButton.YesNoCancel,MessageBoxImage.Question,MessageBoxResult.Cancel);
                    if(answer!=MessageBoxResult.Cancel) {
                        Run(root,answer==MessageBoxResult.Yes?"Bind":"Restore");
                        RememberChoice(answer==MessageBoxResult.Yes);
                    }
                    return 0;
                } finally {gate.ReleaseMutex();}
            }
        }
        static void RememberChoice(bool bind) {
            Directory.CreateDirectory(StateDirectory);
            File.WriteAllText(ChoicePath,bind?"1":"0",Encoding.ASCII);
        }
        internal static void OfferSafely(string root) {
            try {
                using(var gate=SettingsGate()) {
                    if(!Enter(gate))return;
                    try {OfferOnce(root);}finally{gate.ReleaseMutex();}
                }
            }
            catch(Exception error){MessageBox.Show("启动入口设置未完成。请使用独立入口；可稍后运行 Manage-Entrypoints.cmd 重试或恢复。\nEntry point setup did not finish. Error: "+error.GetType().Name,Title);}
        }
        static void Run(string root,string action,bool showResult=true) {
            var values=new List<string>{"-NoProfile","-ExecutionPolicy","Bypass","-File",Path.Combine(root,"scripts","manage-entrypoints.ps1"),"-Action",action,"-LauncherPath",Path.Combine(root,"CrystalDragon.exe"),"-StateDirectory",StateDirectory};
            if(action=="Bind") {
                var identity=OfficialIdentity.Discover();
                values.AddRange(new[]{"-OfficialExecutable",(string)identity["path"],"-OfficialAlias",(string)identity["launchPath"]});
            }
            string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
            var start=new ProcessStartInfo(powershell,String.Join(" ",values.Select(Quote))) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
            Dictionary<string,object> result;
            using(var child=Process.Start(start)) {
                Task errors=Task.Run(delegate {var buffer=new char[4096];while(child.StandardError.Read(buffer,0,buffer.Length)>0){}});
                string output=child.StandardOutput.ReadToEnd();child.WaitForExit();errors.GetAwaiter().GetResult();
                if(child.ExitCode!=0)throw new InvalidOperationException("shortcut-operation-failed");
                result=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(output);
            }
            int changed=Convert.ToInt32(result["changed"]),skipped=Convert.ToInt32(result["skipped"]),failed=Convert.ToInt32(result["failed"]);
            string text=(action=="Bind"?"绑定 / Bound":"恢复 / Restored")+": "+changed+"\n跳过 / Skipped: "+skipped+"\n未完成 / Failed: "+failed;
            if(changed==0&&action=="Bind")text+="\n\n未找到可安全绑定的普通快捷方式时，请使用 Codex Reasoning Slider 独立入口并自行固定到任务栏。\nVirtual app entries and taskbar pins are not rewritten.";
            if(showResult||failed>0)MessageBox.Show(text,Title,MessageBoxButton.OK,failed==0?MessageBoxImage.Information:MessageBoxImage.Warning);
        }
    }
}
