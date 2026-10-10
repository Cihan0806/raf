using System.Windows;
using WinForms = System.Windows.Forms;

namespace Raf.App;

/// <summary>Saatin yanındaki simge: pencere kapalıyken Raf buradan açılır ve bildirim gösterir.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;

    public TrayIcon(MainWindow main, Action quit)
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/raf.ico"))!.Stream;

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Raf'ı aç", null, (_, _) => main.ShowLauncher());
        menu.Items.Add("Gelen klasörünü aç", null, (_, _) => Shell.OpenFolder(App.S.Inbox));
        menu.Items.Add("Raf klasörünü aç", null, (_, _) => Shell.OpenFolder(App.S.Config.Root));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => quit());

        _icon = new WinForms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize),
            Text = "Raf",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) main.ShowLauncher();
        };
        _icon.BalloonTipClicked += (_, _) => main.ShowLauncher();
    }

    public void Notify(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, WinForms.ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
