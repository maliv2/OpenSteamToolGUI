using System.Diagnostics;
using System.Text.Json;

namespace OpenSteamToolGUI.Core;

public sealed class ElevatedRequest
{
    public string SteamRoot { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ElevatedChange> Changes { get; set; } = [];
}
public sealed class ElevatedChange
{
    public string Target { get; set; } = "";
    public string? ContentFile { get; set; }
    public string? ContentHash { get; set; }
}
public sealed class ElevatedResponse
{
    public BackupRecord? Record { get; set; }
    public string? Error { get; set; }
}

public static class ElevatedFileTransaction
{
    public static bool NeedsElevation(IEnumerable<string> targets)
    {
        foreach (string target in targets)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(target));
            while (dir is not null && !Directory.Exists(dir)) dir = Path.GetDirectoryName(dir);
            if (dir is null) return true;
            string probe = Path.Combine(dir, ".ostgui-probe-" + Guid.NewGuid().ToString("N"));
            try { using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { } }
            catch (UnauthorizedAccessException) { return true; }
            catch (IOException) { return true; }
            finally { try { if (File.Exists(probe)) File.Delete(probe); } catch { } }
        }
        return false;
    }
    public static BackupRecord Apply(Storage storage, string steamRoot, string description, IReadOnlyList<(string Target, byte[]? Content)> changes)
    {
        var steam = new SteamInstallation(steamRoot);
        if (!steam.IsValid) throw new IOException("Steam folder must contain steam.exe.");
        foreach (var change in changes) ValidateTarget(steam, change.Target);
        string executable = Environment.ProcessPath ?? "";
        if (!Path.GetFileName(executable).Equals("OpenSteamToolGUI.exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Elevation requires the published OpenSteamToolGUI.exe. Run the packaged application.");
        string stageRoot = Path.Combine(storage.Root, "elevation"); Directory.CreateDirectory(stageRoot);
        string stage = Path.Combine(stageRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            var request = new ElevatedRequest { SteamRoot = steam.Root, Description = description };
            for (int i = 0; i < changes.Count; i++)
            {
                string? contentPath = null;
                if (changes[i].Content is not null)
                {
                    contentPath = Path.Combine(stage, i.ToString("D4") + ".bin");
                    File.WriteAllBytes(contentPath, changes[i].Content!);
                }
                request.Changes.Add(new ElevatedChange { Target = changes[i].Target, ContentFile = contentPath,
                    ContentHash = changes[i].Content is null ? null : FileTools.Hash(changes[i].Content!) });
            }
            byte[] requestBytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));
            Storage.AtomicWrite(Path.Combine(stage, "request.json"), requestBytes);
            using var process = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas",
                Arguments = "--elevated-apply \"" + stage + "\" " + FileTools.Hash(requestBytes), WindowStyle = ProcessWindowStyle.Hidden });
            if (process is null) throw new IOException("Could not start elevated file helper.");
            if (!process.WaitForExit(300000)) { try { process.Kill(); } catch { } throw new TimeoutException("Elevated operation timed out."); }
            string responsePath = Path.Combine(stage, "response.json");
            if (!File.Exists(responsePath)) throw new IOException("Elevated operation did not return a result.");
            var response = JsonSerializer.Deserialize<ElevatedResponse>(File.ReadAllText(responsePath)) ?? throw new InvalidDataException("Invalid helper result.");
            if (response.Error is not null) throw new IOException(response.Error);
            return response.Record ?? throw new InvalidDataException("Missing helper transaction record.");
        }
        finally { try { Directory.Delete(stage, true); } catch { } }
    }
    public static void Execute(string stage, Storage? storageOverride = null, string? expectedRequestHash = null)
    {
        using var scope = new ElevationFileScope();
        bool stageSecured = false;
        try
        {
            string storageRoot = storageOverride?.Root ?? Storage.DefaultRoot;
            string root = Path.Combine(storageRoot, "elevation");
            if (!FileTools.Inside(root, stage)) throw new InvalidDataException("Invalid elevation request location.");
            scope.PinDirectory(stage);
            stageSecured = true;
            scope.PinDirectory(Path.Combine(storageRoot, "disabled"), create: true);
            scope.PinDirectory(Path.Combine(storageRoot, "backups"), create: true);
            // Never replay user-editable recovery journals with the elevated token.
            var storage = new Storage(storageRoot, recoverIncomplete: false);
            byte[] requestBytes = scope.ReadFile(stage, Path.Combine(stage, "request.json"), 2 * 1024 * 1024);
            if (expectedRequestHash is not null && FileTools.Hash(requestBytes) != expectedRequestHash)
                throw new InvalidDataException("Invalid request.");
            var request = JsonSerializer.Deserialize<ElevatedRequest>(requestBytes) ?? throw new InvalidDataException("Invalid request.");
            var steam = new SteamInstallation(request.SteamRoot);
            if (!steam.IsValid) throw new InvalidDataException("Invalid Steam installation.");
            if (request.Changes.Count is < 1 or > 1000) throw new InvalidDataException("Invalid operation count.");
            var changes = new List<(string Target, byte[]? Content)>();
            foreach (var item in request.Changes)
            {
                ValidateTarget(steam, item.Target);
                scope.PinDirectory(Path.GetDirectoryName(Path.GetFullPath(item.Target))!, create: true);
                ValidateTarget(steam, item.Target);
                byte[]? content = null;
                if (item.ContentFile is not null)
                {
                    if (!FileTools.Inside(stage, item.ContentFile)) throw new InvalidDataException("Content outside request directory.");
                    content = scope.ReadFile(stage, item.ContentFile, 128_000_000);
                    if (expectedRequestHash is not null && FileTools.Hash(content) != item.ContentHash)
                        throw new InvalidDataException("Invalid request.");
                }
                changes.Add((item.Target, content));
            }
            var record = new FileTransaction(storage, scope, steam.Root).Apply(request.Description, changes);
            Storage.AtomicWrite(Path.Combine(stage, "response.json"), JsonSerializer.Serialize(new ElevatedResponse { Record = record }));
        }
        catch (Exception ex)
        {
            if (stageSecured)
                try { Storage.AtomicWrite(Path.Combine(stage, "response.json"), JsonSerializer.Serialize(new ElevatedResponse { Error = ex.Message })); } catch { }
        }
    }
    private static void ValidateTarget(SteamInstallation steam, string target)
    {
        string full = Path.GetFullPath(target);
        bool dll = new[] { "dwmapi.dll", "xinput1_4.dll", "OpenSteamTool.dll" }.Any(x => full.Equals(Path.Combine(steam.Root, x), StringComparison.OrdinalIgnoreCase));
        bool config = full.Equals(steam.ConfigFile, StringComparison.OrdinalIgnoreCase);
        bool lua = FileTools.Inside(steam.LuaDirectory, full) && Path.GetExtension(full).Equals(".lua", StringComparison.OrdinalIgnoreCase);
        bool manifest = FileTools.Inside(steam.DepotCache, full) && Path.GetExtension(full).Equals(".manifest", StringComparison.OrdinalIgnoreCase);
        if (!dll && !config && !lua && !manifest) throw new InvalidDataException("Elevated write target is not an OpenSteamTool location.");
        FileTools.RejectReparsePath(steam.Root, full);
    }
}
