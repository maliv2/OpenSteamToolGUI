namespace OpenSteamToolGUI.Core;

public static class ImportTracking
{
    public static bool IsImportRecord(BackupRecord record) =>
        record.ImportSourceName is not null || record.Description.StartsWith("Import ", StringComparison.Ordinal)
            && new[] { ".zip", ".lua", ".manifest" }.Contains(Path.GetExtension(record.Description[7..]), StringComparer.OrdinalIgnoreCase);

    public static string SourceName(BackupRecord record) => record.ImportSourceName ?? record.Description[7..];

    public static IReadOnlyList<BackupRecord> ActiveGroups(Storage storage, SteamInstallation steam) => storage.ListBackups()
        .Where(x => x.Completed && !x.Restored && !x.ImportRemoved && IsImportRecord(x) && x.Operations.Count > 0
            && x.Operations.All(op => IsManagedTarget(steam, op.TargetPath)))
        .ToList();

    public static BackupRecord? ForPackage(Storage storage, SteamInstallation steam, GamePackage package) =>
        MatchPackage(ActiveGroups(storage, steam), package);

    public static BackupRecord? MatchPackage(IEnumerable<BackupRecord> groups, GamePackage package)
    {
        if (!File.Exists(package.SourcePath)) return null;
        string hash = FileTools.HashFile(package.SourcePath);
        return groups.FirstOrDefault(record => record.Operations.Any(op =>
            op.TargetPath.Equals(package.OriginalPath, StringComparison.OrdinalIgnoreCase) && (op.ManagedHash ?? op.NewHash) == hash));
    }

    public static void UpdateManagedHash(Storage storage, SteamInstallation steam, string activePath, string previousHash, string newHash)
    {
        var record = ActiveGroups(storage, steam).FirstOrDefault(x => x.Operations.Any(op =>
            op.TargetPath.Equals(activePath, StringComparison.OrdinalIgnoreCase) && (op.ManagedHash ?? op.NewHash) == previousHash));
        if (record is null) return;
        record.Operations.First(op => op.TargetPath.Equals(activePath, StringComparison.OrdinalIgnoreCase)).ManagedHash = newHash;
        storage.SaveRecord(record);
    }

    public static BackupRecord RemoveGroup(Storage storage, SteamInstallation steam, BackupRecord record)
    {
        if (!ActiveGroups(storage, steam).Any(x => x.Id == record.Id)) throw new IOException("Import record is no longer available.");
        var steamChanges = new List<(string Target, byte[]? Content)>();
        var disabledChanges = new List<(string Target, byte[]? Content)>();
        foreach (var op in record.Operations)
        {
            if (!IsManagedTarget(steam, op.TargetPath)) throw new IOException("Import contains a file outside managed folders.");
            bool lua = FileTools.Inside(steam.LuaDirectory, op.TargetPath);
            var disabledCandidates = lua && Directory.Exists(storage.DisabledDirectory)
                ? Directory.EnumerateFiles(storage.DisabledDirectory, "*.lua", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
                    .Where(path => Path.GetFileName(path).Equals(Path.GetFileName(op.TargetPath), StringComparison.OrdinalIgnoreCase)).ToList()
                : [];
            FileTools.RejectReparsePath(lua ? steam.LuaDirectory : steam.DepotCache, op.TargetPath);
            foreach (var candidate in disabledCandidates) FileTools.RejectReparsePath(storage.DisabledDirectory, candidate);
            bool activeExists = File.Exists(op.TargetPath);
            string expectedHash = op.ManagedHash ?? op.NewHash ?? "";
            bool activeMatches = activeExists && FileTools.HashFile(op.TargetPath) == expectedHash;
            var matchingDisabled = disabledCandidates.Where(path => FileTools.HashFile(path) == expectedHash).ToList();
            if (matchingDisabled.Count > 1 || activeMatches && matchingDisabled.Count > 0)
                throw new IOException("Multiple copies match the imported Lua file: " + op.TargetPath);
            if (activeExists && !activeMatches && (matchingDisabled.Count == 0 || op.BackupPath is not null))
                throw new IOException("Imported file changed since import: " + op.TargetPath);
            if (!activeExists && disabledCandidates.Count > 0 && matchingDisabled.Count == 0)
                throw new IOException("Disabled Lua file changed since import: " + op.TargetPath);
            string? disabled = activeMatches ? null : matchingDisabled.SingleOrDefault();
            bool disabledExists = disabled is not null;
            if (op.BackupPath is not null && !File.Exists(op.BackupPath))
                throw new IOException("Original file backup is missing: " + op.TargetPath);
            if (disabledExists) disabledChanges.Add((disabled!, null));
            if (activeMatches || op.BackupPath is not null && !activeExists)
                steamChanges.Add((op.TargetPath, op.BackupPath is null ? null : File.ReadAllBytes(op.BackupPath)));
        }
        BackupRecord? disabledRemoval = null;
        if (disabledChanges.Count > 0)
            disabledRemoval = new FileTransaction(storage).Apply("Remove disabled Lua from " + SourceName(record), disabledChanges);
        BackupRecord removal;
        try
        {
            removal = steamChanges.Count > 0
                ? new FileTransaction(storage).Apply("Remove import " + SourceName(record), steamChanges, steamRoot: steam.Root)
                : disabledRemoval ?? new FileTransaction(storage).Apply("Remove import " + SourceName(record), []);
        }
        catch
        {
            if (disabledRemoval is not null) new FileTransaction(storage).Restore(disabledRemoval);
            throw;
        }
        record.ImportRemoved = true;
        storage.SaveRecord(record);
        removal.RemovedImportId = record.Id;
        if (disabledRemoval is not null && disabledRemoval.Id != removal.Id) removal.CompanionRemovalId = disabledRemoval.Id;
        storage.SaveRecord(removal);
        return removal;
    }

    public static BackupRecord RemoveLua(Storage storage, SteamInstallation steam, GamePackage package, string expectedHash)
    {
        string path = Path.GetFullPath(package.SourcePath);
        string root = package.Enabled ? steam.LuaDirectory : storage.DisabledDirectory;
        if (!FileTools.Inside(root, path) || !Path.GetExtension(path).Equals(".lua", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Lua file is outside its managed folder.");
        FileTools.RejectReparsePath(root, path);
        if (!File.Exists(path) || FileTools.HashFile(path) != expectedHash)
            throw new IOException("Lua file changed since selection. Refresh the library.");
        return new FileTransaction(storage).Apply("Remove Lua " + Path.GetFileName(path), [(path, (byte[]?)null)], steamRoot: steam.Root);
    }

    private static bool IsManagedTarget(SteamInstallation steam, string path) =>
        FileTools.Inside(steam.LuaDirectory, path) && Path.GetExtension(path).Equals(".lua", StringComparison.OrdinalIgnoreCase)
        || FileTools.Inside(steam.DepotCache, path) && Path.GetExtension(path).Equals(".manifest", StringComparison.OrdinalIgnoreCase);
}
