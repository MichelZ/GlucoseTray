using GlucoseTray.Enums;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

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
    private readonly Control _dispatcher = new();
    private GlucoseWidget? _widget;
    private bool _widgetEnabled;
    private ToolStripMenuItem? _widgetMenu;
    private readonly IWidgetPositionStore _widgetPositions;

    private readonly ILibreLinkUpSignIn? _signIn;
    private readonly LifecycleDiagnostics? _lifecycle;

    public NotificationIcon(ILibreLinkUpSignIn? signIn = null, IOptionsMonitor<AppSettings>? options = null,
        IWidgetPositionStore? widgetPositions = null, LifecycleDiagnostics? lifecycle = null)
    {
        _lifecycle = lifecycle;
        _widgetPositions = widgetPositions ?? new WidgetPositionStore(Path.Combine(AppContext.BaseDirectory, WidgetPositionStore.FileName));
        _ = _dispatcher.Handle;
        _widgetEnabled = options?.CurrentValue.ShowGlucoseWidget ?? false;
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
    public void AddSettingsMenu()
    {
        _trayIcon.ContextMenuStrip?.Items.Add(new ToolStripMenuItem("Settings", null, Settings));
        _widgetMenu = new ToolStripMenuItem("Show glucose widget") { Checked = _widgetEnabled };
        _widgetMenu.Click += (_, _) => SetWidgetVisible(!_widgetEnabled);
        _trayIcon.ContextMenuStrip?.Items.Add(_widgetMenu);
    }

    private void SetWidgetVisible(bool visible)
    {
        _widgetEnabled = visible;
        if (_widgetMenu is not null) _widgetMenu.Checked = visible;
        UpdateWidget();
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, AppSettings.FileName);
            var settings = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            settings[nameof(AppSettings.ShowGlucoseWidget)] = visible;
            File.WriteAllText(path, settings.ToJsonString(new() { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ShowNotification("The widget visibility could not be saved; it applies for this session only.");
        }
    }

    private void UpdateWidget()
    {
        if (!_widgetEnabled) { _widget?.Hide(); return; }
        if (_widget is null)
        {
            _widget = new GlucoseWidget(_widgetPositions);
            _widget.HideRequested += (_, _) => SetWidgetVisible(false);
            _widget.PositionSaveFailed += (_, _) => ShowNotification("The widget position could not be saved. Check write access to the app folder.");
        }
        if (_latestGlucose is not null) _widget.UpdateReading(_latestGlucose);
        if (!_widget.Visible) _widget.Show();
    }
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
        _widget?.Dispose();
        _trayIcon.Dispose();
        _dispatcher.Dispose();
        _currentIcon?.Dispose();
        _currentIcon = null;
    }

    private void Exit(object? sender, EventArgs e)
    {
        _lifecycle?.Record(LifecycleEvent.ExitMenuRequested);
        Dispose();
        Application.ExitThread();
        Application.Exit();
    }

    public void RefreshIcon(GlucoseDisplay display)
    {
        if (_dispatcher.IsDisposed) return;
        if (_dispatcher.InvokeRequired)
        {
            _dispatcher.BeginInvoke(() => RefreshIcon(display));
            return;
        }
        _latestGlucose = display;
        var status = display.GetDisplayMessage(DateTime.UtcNow);
        _trayIcon.Text = status.Length <= 63 ? status : status[..60] + "...";
        CreateTextIcon(display);
        UpdateWidget();
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
