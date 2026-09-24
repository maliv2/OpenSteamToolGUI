namespace OpenSteamToolGUI.Core;

/// <summary>Runs a managed file operation while Steam is closed, restoring its initial running state afterward.</summary>
public sealed class SteamLifecycle(Func<bool> isRunning, Func<Task> stopAsync, Action start)
{
    public async Task RunAsync(bool wasRunning, Func<Task> operation)
    {
        if (!wasRunning && isRunning()) throw new InvalidOperationException("Steam started after confirmation. Try again.");
        if (wasRunning && isRunning()) await stopAsync();
        if (isRunning()) throw new TimeoutException("Steam did not close. OpenSteamTool files were not changed.");

        try { await operation(); }
        finally { if (wasRunning) start(); }
    }
}
