using System;
using System.Drawing;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace SasamaProxy.Core
{
    /// <summary>
    /// WPF has no built-in tray icon, so - like essentially every WPF app
    /// with a tray icon - we pull in System.Windows.Forms.NotifyIcon just
    /// for this one piece.
    /// </summary>
    public class TrayIconManager : IDisposable
    {
        private NotifyIcon _icon;
        public event Action RestoreRequested;
        public event Action ExitRequested;

        public void Initialize()
        {
            var menu = new ContextMenuStrip();
            var restoreItem = new ToolStripMenuItem("Open SASAMA Proxy");
            restoreItem.Click += (s, e) => RestoreRequested?.Invoke();
            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => ExitRequested?.Invoke();
            menu.Items.Add(restoreItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            _icon = new NotifyIcon
            {
                Icon = SystemIcons.Shield, // placeholder - swap for a real .ico when you restyle
                Visible = false,
                Text = "SASAMA Proxy",
                ContextMenuStrip = menu
            };
            _icon.DoubleClick += (s, e) => RestoreRequested?.Invoke();
        }

        public void Show() { if (_icon != null) _icon.Visible = true; }
        public void Hide() { if (_icon != null) _icon.Visible = false; }

        public void ShowBalloon(string title, string text)
        {
            if (_icon == null) return;
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = text;
            _icon.ShowBalloonTip(3000);
        }

        public void Dispose()
        {
            if (_icon != null)
            {
                _icon.Visible = false;
                _icon.Dispose();
                _icon = null;
            }
        }
    }
}
