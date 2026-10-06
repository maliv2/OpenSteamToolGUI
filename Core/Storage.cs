using System.Text.Json;

namespace OpenSteamToolGUI.Core;

public sealed class Storage
{
    public string Root { get; }
    public string DisabledDirectory => Path.Combine(Root, "disabled");
    public string BackupDirectory => Path.Combine(Root, "backups");
    public string PreferencesFile => Path.Combine(Root, "preferences.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSteamToolGUI");
    public Storage(string? root = null, bool recoverIncomplete = true)
    {
        Root = root ?? DefaultRoot;
        Directory.CreateDirectory(Root); Directory.CreateDirectory(DisabledDirectory); Directory.CreateDirectory(BackupDirectory);
        foreach (var record in recoverIncomplete ? ListBackups().Where(x => !x.Completed && !x.Restored) : [])
        {
            try { new FileTransaction(this).Restore(record, false); } catch { /* Diagnostics can expose inaccessible recovery files. */ }
        }
    }
    public AppPreferences LoadPreferences()
    {
        try { return File.Exists(PreferencesFile) ? JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(PreferencesFile), JsonOptions) ?? new() : new(); }
        catch { return new(); }
    }
    public void SavePreferences(AppPreferences prefs) => AtomicWrite(PreferencesFile, JsonSerializer.Serialize(prefs, JsonOptions));
    public IReadOnlyList<BackupRecord> ListBackups() => Directory.GetDirectories(BackupDirectory).Select(p => Path.Combine(p, "record.json"))
        .Where(File.Exists).Select(p => { try { return JsonSerializer.Deserialize<BackupRecord>(File.ReadAllText(p), JsonOptions); } catch { return null; } })
        .OfType<BackupRecord>().OrderByDescending(x => x.CreatedUtc).ToList();
    public void SaveRecord(BackupRecord record)
    {
        string dir = Path.Combine(BackupDirectory, record.Id); Directory.CreateDirectory(dir);
        AtomicWrite(Path.Combine(dir, "record.json"), JsonSerializer.Serialize(record, JsonOptions));
    }
    public void PruneBackups(int retention, AppPreferences preferences)
    {
        var records = ListBackups();
        var protectedPaths = preferences.OwnedFiles.Concat(records.Where(x => !x.Restored).SelectMany(x => x.PreviousOwnedFiles ?? []))
            .SelectMany(x => new[] { x.OriginalBackup, x.DisabledBackup }).OfType<string>().Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referencedIds = records.Where(x => !x.Restored).SelectMany(x => new[] { x.RemovedImportId, x.CompanionRemovalId }).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records.Skip(Math.Max(1, retention)))
        {
            string folder = Path.Combine(BackupDirectory, record.Id);
            if (protectedPaths.Any(x => FileTools.Inside(folder, x))) continue;
            if (ImportTracking.IsImportRecord(record) && !record.Restored && !record.ImportRemoved) continue;
            if (referencedIds.Contains(record.Id)) continue;
            try { Directory.Delete(folder, true); } catch { }
        }
    }
    public static void AtomicWrite(string path, string content) => AtomicWrite(path, System.Text.Encoding.UTF8.GetBytes(content));
    public static void AtomicWrite(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".ostgui-" + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, content); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
