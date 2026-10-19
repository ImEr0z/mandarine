using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Mandarine.App.Services;

/// <summary>Notification area icon and its menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _pauseItem;

    public TrayIcon(bool paused)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir la zone de dépôt", null, (_, _) => DropZoneRequested?.Invoke());
        menu.Items.Add("Paramètres…", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        _pauseItem = new Forms.ToolStripMenuItem("Suspendre le geste") { CheckOnClick = true, Checked = paused };
        _pauseItem.CheckedChanged += (_, _) => PauseChanged?.Invoke(_pauseItem.Checked);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quitter Mandarine", null, (_, _) => ExitRequested?.Invoke());

        using var stream = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Mandarine;component/Assets/mandarine.ico")).Stream;
        _icon = new Forms.NotifyIcon
        {
            Icon = new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            Text = "Mandarine",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                DropZoneRequested?.Invoke();
            }
        };
    }

    public event Action? DropZoneRequested;

    public event Action? SettingsRequested;

    public event Action<bool>? PauseChanged;

    public event Action? ExitRequested;

    public void SetPaused(bool paused) => _pauseItem.Checked = paused;

    public void Notify(string title, string text) => _icon.ShowBalloonTip(6000, title, text, Forms.ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
