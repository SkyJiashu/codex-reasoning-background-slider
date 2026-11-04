using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CrystalDragon {
    // The launcher's own tray entry: it lives exactly as long as the client this launcher started,
    // and works whether or not the client manages to show its own tray icon.
    internal sealed class ClientTray : IDisposable {
        readonly NotifyIcon icon;
        internal ClientTray(string launcher, Action open, Action quit) {
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开 Codex", null, delegate { Task.Run(open); });
            menu.Items.Add("彻底退出 Codex", null, delegate { Task.Run(quit); });
            icon = new NotifyIcon { Icon = Icon.ExtractAssociatedIcon(launcher), Text = "Codex 龙娘", ContextMenuStrip = menu, Visible = true };
            icon.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Task.Run(open); };
        }
        public void Dispose() { icon.Visible = false; icon.Dispose(); }
    }
}
