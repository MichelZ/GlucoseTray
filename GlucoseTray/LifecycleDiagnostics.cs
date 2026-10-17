using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;

namespace GlucoseTray;

public enum LifecycleEvent { Started, PreviousRunUnclean, ExitMenuRequested, WindowsLogoff, WindowsShutdown, Suspend, Resume, PowerStatusChanged, MessageLoopExited, Completed, ProcessExiting, MainFailed, RefreshLoopFailed, UnhandledException }

/// <summary>Independent, bounded diagnostics. No settings, exception text, or arbitrary strings are accepted.</summary>
public sealed class LifecycleDiagnostics : IDisposable
{
    public const string LogFileName = "lifecycle.log";
    private readonly string _log;
    private readonly string _marker;
    private readonly object _gate = new();
    private volatile bool _failed;
    private bool _disposed;

    public LifecycleDiagnostics(string folder)
    {
        _log = Path.Combine(folder, LogFileName);
        using var process = Process.GetCurrentProcess();
        _marker = Path.Combine(folder, $"lifecycle-{Environment.ProcessId}-{process.StartTime.ToUniversalTime().Ticks}.pending");
        TryWrite(() =>
        {
            foreach (var marker in Directory.EnumerateFiles(folder, "lifecycle-*.pending"))
            {
                var parts = Path.GetFileNameWithoutExtension(marker).Split('-');
                if (parts.Length != 3 || !int.TryParse(parts[1], out var pid) || !long.TryParse(parts[2], out var ticks)) continue;
                try
                {
                    using var previous = Process.GetProcessById(pid);
                    if (previous.StartTime.ToUniversalTime().Ticks == ticks) continue;
                }
                catch (ArgumentException) { }
                Record(LifecycleEvent.PreviousRunUnclean, pid);
                File.Delete(marker);
            }
            File.WriteAllText(_marker, string.Empty);
        });
        Record(LifecycleEvent.Started);
    }

    public void Attach()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Application.ApplicationExit += OnApplicationExit;
        SystemEvents.SessionEnding += OnSessionEnding;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Record(LifecycleEvent kind, int? processId = null)
    {
        if (!Enum.IsDefined(kind)) return;
        lock (_gate)
        {
            TryWrite(() =>
            {
                if (File.Exists(_log) && new FileInfo(_log).Length >= 262144)
                    File.Move(_log, _log + ".1", true);
                File.AppendAllText(_log, string.Create(CultureInfo.InvariantCulture,
                    $"{DateTime.UtcNow:O} PID={Environment.ProcessId} Event={kind} RelatedPID={processId ?? Environment.ProcessId}\n"));
            });
        }
    }

    public void Failure(LifecycleEvent kind)
    {
        if (kind is not (LifecycleEvent.MainFailed or LifecycleEvent.RefreshLoopFailed or LifecycleEvent.UnhandledException)) return;
        _failed = true;
        Record(kind);
    }

    public void Complete()
    {
        if (_failed) return;
        Record(LifecycleEvent.Completed);
        TryWrite(() => File.Delete(_marker));
    }

    private void OnUnhandled(object sender, UnhandledExceptionEventArgs e) => Failure(LifecycleEvent.UnhandledException);
    private void OnProcessExit(object? sender, EventArgs e) => Record(LifecycleEvent.ProcessExiting);
    private void OnApplicationExit(object? sender, EventArgs e) => Record(LifecycleEvent.MessageLoopExited);
    private void OnSessionEnding(object sender, SessionEndingEventArgs e) => Record(e.Reason == SessionEndReasons.Logoff ? LifecycleEvent.WindowsLogoff : LifecycleEvent.WindowsShutdown);
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) => Record(e.Mode switch
    {
        PowerModes.Suspend => LifecycleEvent.Suspend,
        PowerModes.Resume => LifecycleEvent.Resume,
        _ => LifecycleEvent.PowerStatusChanged,
    });
    private static void TryWrite(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException) { }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        Application.ApplicationExit -= OnApplicationExit;
        SystemEvents.SessionEnding -= OnSessionEnding;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
