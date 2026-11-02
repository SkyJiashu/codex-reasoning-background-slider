using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CrystalDragon {
    internal static class CachePreparation {
        [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref NativeWindow.Rect rect,uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor,int type,out uint x,out uint y);
        internal static string Prepare(string root,NativeWindow.Rect frame) {
            uint x,y;
            int dpi=GetDpiForMonitor(MonitorFromRect(ref frame,2),0,out x,out y)==0?(int)x:96;
            var bar=new ProgressBar {Minimum=0,Maximum=100,Height=8,Margin=new Thickness(0,20,0,0)};
            var panel=new StackPanel {Margin=new Thickness(24)};
            panel.Children.Add(new TextBlock {Text="首次准备动画缓存",FontSize=20});
            panel.Children.Add(new TextBlock {Text="根据当前显示器在本地生成。完成后，后续启动直接播放缓存。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)});
            panel.Children.Add(bar);
            var window=new Window {Title="Codex · 准备动画",Width=480,Height=190,ResizeMode=ResizeMode.NoResize,
                WindowStartupLocation=WindowStartupLocation.CenterScreen,Content=panel};
            using(var cancel=new CancellationTokenSource()) {
                bool finished=false;
                var generating=new TaskCompletionSource<bool>();
                window.Closing+=delegate {if(!finished)cancel.Cancel();};
                var task=Task.Run(()=>MovieCache.Ensure(root,frame,dpi,percent=>{
                    if(percent<100)generating.TrySetResult(true);
                    window.Dispatcher.BeginInvoke(new Action(()=>bar.Value=percent));
                },null,cancel.Token));
                // A cache hit never shows a preparation window or introduces a flash.
                Task.WhenAny(task,generating.Task).GetAwaiter().GetResult();
                if(!task.IsCompleted) {
                    task.ContinueWith(t=>{
                        var observed=t.Exception;
                        if(!window.Dispatcher.HasShutdownStarted)
                            window.Dispatcher.BeginInvoke(new Action(()=>{finished=true;window.Close();}));
                    });
                    window.ShowDialog();
                }
                // Closing the preparation dialog cancels our renderer, but an encoder
                // may take longer to drain its inherited streams. Never join it on UI.
                cancel.Token.ThrowIfCancellationRequested();
                finished=true;
                return task.GetAwaiter().GetResult();
            }
        }
    }
}
