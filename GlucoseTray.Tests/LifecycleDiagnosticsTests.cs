using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GlucoseTray.Tests;

public class LifecycleDiagnosticsTests
{
    private string _folder = null!;
    private string Log => File.ReadAllText(Path.Combine(_folder, LifecycleDiagnostics.LogFileName));
    [SetUp]
    public void SetUp() => Directory.CreateDirectory(_folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
    [TearDown]
    public void TearDown() => Directory.Delete(_folder, true);

    [Test]
    public void CleanCompletionRemovesMarkerAndDoesNotReportAnUncleanRun()
    {
        using (var run = new LifecycleDiagnostics(_folder))
        {
            Assert.That(Directory.GetFiles(_folder, "*.pending"), Has.Length.EqualTo(1));
            run.Record(LifecycleEvent.ExitMenuRequested);
            run.Complete();
        }
        using var next = new LifecycleDiagnostics(_folder);
        Assert.That(Log, Does.Contain("Event=Completed").And.Contain("Event=ExitMenuRequested").And.Not.Contain("PreviousRunUnclean"));
        next.Complete();
        Assert.That(Directory.GetFiles(_folder, "*.pending"), Is.Empty);
    }

    [Test]
    public void AbandonedMarkerForADeadProcessIsReportedAndRemoved()
    {
        // Windows cannot allocate this PID. A mismatched start time also protects against PID reuse.
        var marker = Path.Combine(_folder, "lifecycle-2147483647-1.pending");
        File.WriteAllText(marker, "SYNTHETIC-password-token-email-and-health-data");
        using var run = new LifecycleDiagnostics(_folder);
        Assert.That(Log, Does.Contain("Event=PreviousRunUnclean RelatedPID=2147483647").And.Not.Contain("SYNTHETIC"));
        Assert.That(File.Exists(marker), Is.False);
        run.Complete();
    }

    [Test]
    public void StillRunningProcessIsNotReportedAsUnclean()
    {
        using var process = Process.GetCurrentProcess();
        var marker = Path.Combine(_folder, $"lifecycle-{process.Id}-{process.StartTime.ToUniversalTime().Ticks}.pending");
        File.WriteAllText(marker, string.Empty);
        using var run = new LifecycleDiagnostics(_folder);
        Assert.That(Log, Does.Not.Contain("PreviousRunUnclean"));
        run.Complete();
    }

    [TestCase(LifecycleEvent.MainFailed)]
    [TestCase(LifecycleEvent.RefreshLoopFailed)]
    [TestCase(LifecycleEvent.UnhandledException)]
    public void FatalFailureCannotBeMistakenForCleanCompletion(LifecycleEvent failure)
    {
        using var run = new LifecycleDiagnostics(_folder);
        run.Failure(failure);
        run.Complete();
        Assert.That(Log, Does.Contain("Event=" + failure).And.Not.Contain("Event=Completed"));
        Assert.That(Directory.GetFiles(_folder, "*.pending"), Has.Length.EqualTo(1));
    }

    [Test]
    public void LifecycleOutputContainsOnlyTheFixedSchemaAndRejectsUnknownEvents()
    {
        using var run = new LifecycleDiagnostics(_folder);
        foreach (var kind in Enum.GetValues<LifecycleEvent>()) run.Record(kind);
        run.Record((LifecycleEvent)int.MaxValue);
        run.Complete();
        var names = string.Join("|", Enum.GetNames<LifecycleEvent>());
        foreach (var line in Log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Assert.That(Regex.IsMatch(line, $@"^\d{{4}}-\d{{2}}-\d{{2}}T[\d:.]+Z PID=\d+ Event=({names}) RelatedPID=\d+$"), Is.True, line);
    }

    [Test]
    public void UnhandledExceptionPayloadNeverReachesTheLifecycleLog()
    {
        using var run = new LifecycleDiagnostics(_folder);
        var secret = "SYNTHETIC-password-token-email-private-url";
        var error = new Exception(secret, new Exception(secret));
        error.Data[secret] = secret;
        typeof(LifecycleDiagnostics).GetMethod("OnUnhandled", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(run, [this, new UnhandledExceptionEventArgs(error, true)]);
        Assert.That(Log, Does.Contain("Event=UnhandledException").And.Not.Contain("SYNTHETIC").And.Not.Contain("Exception:"));
        Assert.That(Directory.GetFiles(_folder, "*.pending"), Has.Length.EqualTo(1));
    }

    [Test]
    public void LogRotationKeepsOneBoundedBackup()
    {
        var log = Path.Combine(_folder, LifecycleDiagnostics.LogFileName);
        File.WriteAllText(log, new string('x', 262144));
        using var run = new LifecycleDiagnostics(_folder);
        Assert.That(new FileInfo(log).Length, Is.LessThan(1024));
        Assert.That(new FileInfo(log + ".1").Length, Is.EqualTo(262144));
        run.Complete();
    }

    [Test]
    public void UnwritableDiagnosticLocationDoesNotStopTheApp()
    {
        var file = Path.Combine(_folder, "file-not-folder");
        File.WriteAllText(file, string.Empty);
        Assert.DoesNotThrow(() => { using var run = new LifecycleDiagnostics(file); run.Record(LifecycleEvent.Resume); run.Complete(); });
    }
}
