using GlucoseTray.Enums;
using System.ComponentModel;
using System.Diagnostics;

namespace GlucoseTray.Display;

public interface ITrayIcon : IDisposable
{
    void ClearMenu();
    void AddAutoRunMenu(bool isAlreadyOn, EventHandler toggleCallback);
    void AddSettingsMenu();
    void AddLibreLinkUpMenu() { }
    void AddExitMenu();
    void RefreshIcon(GlucoseDisplay display);
    void ShowNotification(string alertText);
}

public class NotificationIcon : ITrayIcon
{
    private readonly NotifyIcon _trayIcon;
    private Icon? _currentIcon;
    private GlucoseDisplay? _latestGlucose;

    private readonly ILibreLinkUpSignIn? _signIn;

    public NotificationIcon(ILibreLinkUpSignIn? signIn = null)
    {
        _signIn = signIn;
        _trayIcon = new NotifyIcon
        {
            ContextMenuStrip = new ContextMenuStrip(new Container()),
            Visible = true,
        };
        _trayIcon.DoubleClick += ShowBalloon;
    }

    public void ShowNotification(string alertText) => _trayIcon.ShowBalloonTip(2000, "Glucose Alert", alertText, ToolTipIcon.Warning);
    private void ShowBalloon(object? sender, EventArgs e) => _trayIcon?.ShowBalloonTip(2000, "Glucose", _latestGlucose?.GetDisplayMessage(DateTime.UtcNow) ?? "error", ToolTipIcon.Info);

    public void ClearMenu() => _trayIcon?.ContextMenuStrip?.Items.Clear();
    public void AddAutoRunMenu(bool isAlreadyOn, EventHandler toggleCallback) => _trayIcon?.ContextMenuStrip?.Items.Add(new ToolStripMenuItem(isAlreadyOn ? "Disable auto-start" : "Run on startup", null, toggleCallback));
    public void AddSettingsMenu() => _trayIcon?.ContextMenuStrip?.Items.Add(new ToolStripMenuItem("Settings", null, Settings));
    public void AddExitMenu() => _trayIcon?.ContextMenuStrip?.Items.Add(new ToolStripMenuItem("Exit", null, Exit));

    public void AddLibreLinkUpMenu()
    {
        _trayIcon.ContextMenuStrip?.Items.Add(new ToolStripMenuItem("LibreLinkUp sign in...", null, (_, _) => _signIn?.ShowDialog()));
        _trayIcon.ContextMenuStrip?.Items.Add(new ToolStripMenuItem("LibreLinkUp sign out", null, (_, _) => _signIn?.SignOut()));
    }

    private void Settings(object? sender, EventArgs e)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = Path.GetDirectoryName(AppContext.BaseDirectory) + "\\appsettings.json",
            UseShellExecute = true,
        };
        Process.Start(startInfo);
    }

    public void Dispose()
    {
        _trayIcon.Dispose();
        _currentIcon?.Dispose();
        _currentIcon = null;
    }

    private void Exit(object? sender, EventArgs e)
    {
        Dispose();
        Application.ExitThread();
        Application.Exit();
    }

    public void RefreshIcon(GlucoseDisplay display)
    {
        _latestGlucose = display;
        var status = display.GetDisplayMessage(DateTime.UtcNow);
        _trayIcon.Text = status.Length <= 63 ? status : status[..60] + "...";
        CreateTextIcon(display);
    }

    private void CreateTextIcon(GlucoseDisplay display)
    {
        var icon = TrayIconRenderer.CreateIcon(display, SystemInformation.SmallIconSize.Width);
        var previous = _currentIcon;
        _trayIcon.Icon = icon;
        _currentIcon = icon;
        previous?.Dispose();
    }
}
