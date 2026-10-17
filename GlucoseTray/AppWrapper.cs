namespace GlucoseTray;

public class AppWrapper : ApplicationContext
{
    public AppWrapper(AppRunner app, LifecycleDiagnostics? lifecycle = null)
    {
        _ = RunAsync(app, lifecycle);
    }

    private static async Task RunAsync(AppRunner app, LifecycleDiagnostics? lifecycle)
    {
        try
        {
            await app.Start();
        }
        catch
        {
            lifecycle?.Failure(LifecycleEvent.RefreshLoopFailed);
            // AppRunner already logs the fatal error and disposes the tray;
            // ensure the WinForms message loop shuts down cleanly.
            Application.Exit();
        }
    }
}
