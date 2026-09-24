using System.Diagnostics;

namespace OpenSteamToolGUI.Core;

public static class SteamProcessManager
{
    public static async Task StopAsync(SteamInstallation steam)
    {
        if (!steam.IsValid) throw new IOException("Invalid Steam installation.");
        string exe = Path.GetFullPath(Path.Combine(steam.Root, "steam.exe"));
        using var shutdown = Process.Start(new ProcessStartInfo(exe, "-shutdown")
        {
            WorkingDirectory = steam.Root,
            UseShellExecute = true
        });
        if (shutdown is null) throw new InvalidOperationException("Could not ask Steam to close.");

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (SteamLocator.IsRunning() && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        if (!SteamLocator.IsRunning()) return;

        foreach (var process in Process.GetProcessesByName("steam"))
        {
            using (process)
            {
                try
                {
                    if (!Path.GetFullPath(process.MainModule?.FileName ?? "").Equals(exe, StringComparison.OrdinalIgnoreCase)) continue;
                    process.Kill();
                }
                catch (InvalidOperationException) { /* Process exited before it could be stopped. */ }
                catch (System.ComponentModel.Win32Exception) { /* Leave an inaccessible process untouched. */ }
            }
        }
        var forcedDeadline = DateTime.UtcNow.AddSeconds(5);
        while (SteamLocator.IsRunning() && DateTime.UtcNow < forcedDeadline)
            await Task.Delay(100);
        if (SteamLocator.IsRunning()) throw new TimeoutException("Steam did not close. OpenSteamTool files were not changed.");
    }

    public static void Start(SteamInstallation steam)
    {
        if (!steam.IsValid) throw new IOException("Invalid Steam installation.");
        if (SteamLocator.IsRunning()) return;
        string exe = Path.GetFullPath(Path.Combine(steam.Root, "steam.exe"));
        using var started = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = steam.Root, UseShellExecute = true });
        if (started is null) throw new InvalidOperationException("Steam could not be started.");
    }
}
