using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenSteamToolGUI.Core;

public static class FileTools
{
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static string HashFile(string path) => Hash(File.ReadAllBytes(path));
    public static bool Inside(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
    public static void RejectReparsePath(string root, string target)
    {
        if (!Inside(root, target)) throw new InvalidDataException("Target is outside its destination directory.");
        string current = Path.GetDirectoryName(Path.GetFullPath(target))!;
        while (true)
        {
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Destination contains a symbolic link or junction: " + current);
            string parent = Path.GetDirectoryName(current)!;
            if (parent == current || string.IsNullOrEmpty(parent)) break;
            current = parent;
        }
        if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Target is a symbolic link: " + target);
    }
}

public static class SteamLocator
{
    public static SteamInstallation? Detect(string preferred = "")
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(preferred)) paths.Add(preferred);
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view).OpenSubKey(@"Software\Valve\Steam");
                foreach (var name in new[] { "SteamPath", "SteamExe" })
                {
                    var value = key?.GetValue(name) as string;
                    if (!string.IsNullOrWhiteSpace(value)) paths.Add(name == "SteamExe" ? Path.GetDirectoryName(value)! : value);
                }
            }
            catch { }
        }
        foreach (var process in Process.GetProcessesByName("steam"))
        {
            try { if (process.MainModule?.FileName is { } exe) paths.Add(Path.GetDirectoryName(exe)!); }
            catch { }
            finally { process.Dispose(); }
        }
        paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).Select(x => new SteamInstallation(x)).FirstOrDefault(x => x.IsValid);
    }
    public static bool IsRunning() => Process.GetProcessesByName("steam").Any();
}

public static class LuaAnalyzer
{
    private static readonly Regex AddApp = new(@"^[ ]*addappid[ ]*[(][ ]*([0-9]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex ManifestDepot = new(@"^[ ]*setmanifestid[ ]*[(][ ]*([0-9]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex OtherCall = new(@"^[ ]*(?:addtoken|setmanifestid|setstat|setappticket|seteticket)[ ]*[(][ ]*([0-9]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
    public static List<uint> AppIds(string text)
    {
        var ids = new HashSet<uint>();
        foreach (Match m in AddApp.Matches(text)) if (uint.TryParse(m.Groups[1].Value, out var id)) ids.Add(id);
        foreach (Match m in ManifestDepot.Matches(text)) if (uint.TryParse(m.Groups[1].Value, out var depot)) ids.Remove(depot);
        return ids.Order().ToList();
    }
    public static List<uint> RelatedIds(string text)
    {
        var ids = new HashSet<uint>(AppIds(text));
        foreach (Match m in OtherCall.Matches(text)) if (uint.TryParse(m.Groups[1].Value, out var id)) ids.Add(id);
        return ids.Order().ToList();
    }
    public static bool IsComplex(string text)
    {
        var known = new Regex(@"(?im)^\s*(?:--.*|(?:addappid|addtoken|setmanifestid|setstat|setappticket|seteticket)\s*\(.*\)\s*(?:--.*)?|\s*)$");
        return text.Replace("\r", "").Split('\n').Any(line => !known.IsMatch(line));
    }
    public static List<GamePackage> Scan(SteamInstallation steam, Storage storage)
    {
        var result = new List<GamePackage>();
        foreach (var dir in new[] { steam.LuaDirectory, storage.DisabledDirectory })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.EnumerateFiles(dir, "*.lua", new EnumerationOptions { RecurseSubdirectories = dir == storage.DisabledDirectory, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                string text; try { text = File.ReadAllText(path); } catch { continue; }
                var ids = AppIds(text);
                string name = ids.Count == 1 ? ids[0].ToString() : ids.Count > 1 ? "Shared package" : "Unidentified package";
                result.Add(new GamePackage { Name = name, SourcePath = path, OriginalPath = Path.Combine(steam.LuaDirectory, Path.GetFileName(path)), Enabled = dir == steam.LuaDirectory, AppIds = ids, ComplexScript = IsComplex(text) });
            }
        }
        return result.OrderBy(x => x.Name).ToList();
    }
    public static void Toggle(GamePackage package, SteamInstallation steam, Storage storage)
    {
        string target = package.Enabled ? Path.Combine(storage.DisabledDirectory, Path.GetFileName(package.SourcePath)) : package.OriginalPath;
        if (package.Enabled && File.Exists(target))
            target = Path.Combine(storage.DisabledDirectory, Guid.NewGuid().ToString("N"), Path.GetFileName(package.SourcePath));
        if (File.Exists(target)) throw new IOException(UiText.T("Another Lua with this name is active. Disable or remove it before enabling this copy."));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (package.Enabled)
        {
            File.Copy(package.SourcePath, target);
            try
            {
                var record = new FileTransaction(storage).Apply("Disable Lua " + Path.GetFileName(package.SourcePath), [(package.SourcePath, (byte[]?)null)], steamRoot: steam.Root);
                record.AuxiliaryPath = target; record.AuxiliaryHash = FileTools.HashFile(target); storage.SaveRecord(record);
            }
            catch { File.Delete(target); throw; }
        }
        else
        {
            var record = new FileTransaction(storage).Apply("Enable Lua " + Path.GetFileName(target), [(target, (byte[]?)File.ReadAllBytes(package.SourcePath))], steamRoot: steam.Root);
            string auxBackup = Path.Combine(storage.BackupDirectory, record.Id, "disabled.bak");
            try
            {
                File.Copy(package.SourcePath, auxBackup);
                File.Delete(package.SourcePath);
                record.AuxiliaryPath = package.SourcePath; record.AuxiliaryBackupPath = auxBackup; storage.SaveRecord(record);
            }
            catch { new FileTransaction(storage).Restore(record, steamRoot: steam.Root); throw; }
        }
    }
}

public sealed class FileTransaction(Storage storage)
{
    public BackupRecord Apply(string description, IEnumerable<(string Target, byte[]? Content)> changes, CancellationToken cancel = default, string? steamRoot = null)
    {
        var changeList = changes.ToList();
        if (steamRoot is not null && ElevatedFileTransaction.NeedsElevation(changeList.Select(x => x.Target)))
            return ElevatedFileTransaction.Apply(storage, steamRoot, description, changeList);
        var record = new BackupRecord { Description = description };
        string backupDir = Path.Combine(storage.BackupDirectory, record.Id);
        Directory.CreateDirectory(backupDir);
        try
        {
            foreach (var (target, content) in changeList)
            {
                cancel.ThrowIfCancellationRequested();
                string? backup = null;
                if (File.Exists(target))
                {
                    backup = Path.Combine(backupDir, record.Operations.Count.ToString("D4") + ".bak");
                    File.Copy(target, backup);
                }
                record.Operations.Add(new FileOperation { TargetPath = target, BackupPath = backup, NewHash = content is null ? null : FileTools.Hash(content) });
                storage.SaveRecord(record);
                if (content is null) File.Delete(target); else Storage.AtomicWrite(target, content);
            }
            record.Completed = true;
            storage.SaveRecord(record);
            return record;
        }
        catch
        {
            try { Restore(record, false); } catch { }
            throw;
        }
    }
    public void Restore(BackupRecord record, bool verifyCurrent = true, string? steamRoot = null)
    {
        if (record.AuxiliaryPath is not null)
        {
            if (!FileTools.Inside(storage.DisabledDirectory, record.AuxiliaryPath)) throw new IOException("Invalid auxiliary backup path.");
            if (verifyCurrent && record.AuxiliaryHash is not null && (!File.Exists(record.AuxiliaryPath) || FileTools.HashFile(record.AuxiliaryPath) != record.AuxiliaryHash)) throw new IOException("Disabled Lua file changed since backup.");
            if (verifyCurrent && record.AuxiliaryHash is null && File.Exists(record.AuxiliaryPath)) throw new IOException("Disabled Lua path was reused since backup.");
        }
        if (steamRoot is not null && ElevatedFileTransaction.NeedsElevation(record.Operations.Select(x => x.TargetPath)))
        {
            foreach (var op in record.Operations)
            {
                if (verifyCurrent && op.NewHash is not null && (!File.Exists(op.TargetPath) || FileTools.HashFile(op.TargetPath) != op.NewHash)) throw new IOException($"Changed since backup: {op.TargetPath}");
                if (verifyCurrent && op.NewHash is null && File.Exists(op.TargetPath)) throw new IOException($"Changed since backup: {op.TargetPath}");
            }
            var changes = record.Operations.AsEnumerable().Reverse().Select(x => (x.TargetPath, x.BackupPath is null ? (byte[]?)null : File.ReadAllBytes(x.BackupPath))).ToList();
            ElevatedFileTransaction.Apply(storage, steamRoot, "Restore " + record.Description, changes);
        }
        else foreach (var op in record.Operations.AsEnumerable().Reverse())
        {
            if (verifyCurrent && op.NewHash is not null && File.Exists(op.TargetPath) && FileTools.HashFile(op.TargetPath) != op.NewHash)
                throw new IOException($"Changed since backup: {op.TargetPath}");
            if (verifyCurrent && op.NewHash is null && File.Exists(op.TargetPath))
                throw new IOException($"Changed since backup: {op.TargetPath}");
            if (op.BackupPath is not null) Storage.AtomicWrite(op.TargetPath, File.ReadAllBytes(op.BackupPath));
            else if (File.Exists(op.TargetPath)) File.Delete(op.TargetPath);
        }
        if (record.AuxiliaryPath is not null)
        {
            if (record.AuxiliaryBackupPath is null) File.Delete(record.AuxiliaryPath);
            else Storage.AtomicWrite(record.AuxiliaryPath, File.ReadAllBytes(record.AuxiliaryBackupPath));
        }
        record.Restored = true;
        storage.SaveRecord(record);
    }
}

public sealed class ImportService(Storage storage)
{
    private const long MaxEntry = 128L * 1024 * 1024;
    private const long MaxTotal = 512L * 1024 * 1024;
    public ImportPlan AnalyzePath(string source, SteamInstallation steam)
    {
        if (Path.GetExtension(source).Equals(".zip", StringComparison.OrdinalIgnoreCase)) return Analyze(source, steam);
        string extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".lua" or ".manifest")) throw new InvalidDataException("Choose a ZIP, Lua or manifest file.");
        var info = new FileInfo(source);
        if (!info.Exists || info.Length > MaxEntry) throw new InvalidDataException("File is missing or exceeds the size limit.");
        var bytes = File.ReadAllBytes(source);
        string root = extension == ".lua" ? steam.LuaDirectory : steam.DepotCache;
        string target = Path.Combine(root, info.Name);
        FileTools.RejectReparsePath(root, target);
        uint? appId = null; string confidence = "Unknown";
        if (extension == ".lua")
        {
            var ids = LuaAnalyzer.AppIds(Encoding.UTF8.GetString(bytes));
            if (ids.Count == 1) { appId = ids[0]; confidence = "Lua addappid"; }
            else if (uint.TryParse(Path.GetFileNameWithoutExtension(info.Name), out var id)) { appId = id; confidence = "Filename only"; }
        }
        string oldHash = File.Exists(target) ? FileTools.HashFile(target) : "";
        return new ImportPlan { Source = source, Files = [new ImportFile { ArchivePath = info.Name, TargetPath = target, Kind = extension == ".lua" ? "Lua" : "Manifest", AppId = appId, Game = appId?.ToString() ?? "Unidentified package", Confidence = confidence, Action = oldHash.Length == 0 ? ImportAction.Add : oldHash == FileTools.Hash(bytes) ? ImportAction.SkipIdentical : ImportAction.KeepExisting, ExistingHash = oldHash, Content = bytes, UncompressedSize = bytes.Length }] };
    }
    public ImportPlan Analyze(string source, SteamInstallation steam)
    {
        var plan = new ImportPlan { Source = source };
        using var archive = ZipFile.OpenRead(source);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (Path.IsPathRooted(name) || name.Split('/').Any(x => x is "." or "..") || name.Contains(':')) throw new InvalidDataException("Unsafe ZIP entry path.");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("ZIP symbolic links are unsupported.");
            if (entry.Length > MaxEntry || (total += entry.Length) > MaxTotal) throw new InvalidDataException("ZIP exceeds import size limits.");
            string extension = Path.GetExtension(entry.Name).ToLowerInvariant();
            if (extension is not (".lua" or ".manifest")) continue;
            if (plan.Files.Count >= 1000) throw new InvalidDataException("Too many importable files.");
            string root = extension == ".lua" ? steam.LuaDirectory : steam.DepotCache;
            string target = Path.Combine(root, entry.Name);
            if (!FileTools.Inside(root, target)) throw new InvalidDataException("Unsafe target path.");
            using var input = entry.Open(); using var memory = new MemoryStream();
            var buffer = new byte[81920]; int read;
            while ((read = input.Read(buffer)) > 0)
            {
                if (memory.Length + read > MaxEntry || total - entry.Length + memory.Length + read > MaxTotal) throw new InvalidDataException("ZIP exceeds import size limits.");
                memory.Write(buffer, 0, read);
            }
            byte[] bytes = memory.ToArray();
            if (bytes.LongLength != entry.Length) throw new InvalidDataException("ZIP size mismatch.");
            uint? appId = null;
            string confidence = "Unknown";
            if (extension == ".lua")
            {
                var ids = LuaAnalyzer.AppIds(Encoding.UTF8.GetString(bytes));
                if (ids.Count == 1) { appId = ids[0]; confidence = "Lua addappid"; }
                else if (ids.Count > 1) confidence = "Multiple games";
                else if (uint.TryParse(Path.GetFileNameWithoutExtension(entry.Name), out var parsed)) { appId = parsed; confidence = "Filename only"; }
            }
            else confidence = "Depot manifest; game unknown";
            string oldHash = File.Exists(target) ? FileTools.HashFile(target) : "";
            var action = oldHash.Length == 0 ? ImportAction.Add : oldHash == FileTools.Hash(bytes) ? ImportAction.SkipIdentical : ImportAction.KeepExisting;
            plan.Files.Add(new ImportFile { ArchivePath = entry.FullName, TargetPath = target, Kind = extension == ".lua" ? "Lua" : "Manifest", Game = appId?.ToString() ?? "Unidentified package", AppId = appId, Confidence = confidence, Action = action, ExistingHash = oldHash, Content = bytes, UncompressedSize = bytes.Length });
        }
        var duplicate = plan.Files.GroupBy(x => x.TargetPath, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException("ZIP contains duplicate target: " + duplicate.Key);
        foreach (var file in plan.Files.Where(x => x.Kind == "Manifest"))
        {
            string folder = Path.GetDirectoryName(file.ArchivePath.Replace('/', Path.DirectorySeparatorChar)) ?? "";
            var matches = plan.Files.Where(x => x.Kind == "Lua" && x.AppId is not null && (Path.GetDirectoryName(x.ArchivePath.Replace('/', Path.DirectorySeparatorChar)) ?? "") == folder).Select(x => x.AppId!.Value).Distinct().ToList();
            if (matches.Count == 1) { file.AppId = matches[0]; file.Game = matches[0].ToString(); file.Confidence = "Same ZIP folder as Lua"; }
        }
        return plan;
    }
    public BackupRecord Apply(ImportPlan plan, SteamInstallation steam, CancellationToken cancel = default)
    {
        foreach (var file in plan.Files.Where(x => x.Include && x.Action is ImportAction.Add or ImportAction.Replace))
        {
            string root = file.Kind == "Lua" ? steam.LuaDirectory : steam.DepotCache;
            FileTools.RejectReparsePath(root, file.TargetPath);
            string currentHash = File.Exists(file.TargetPath) ? FileTools.HashFile(file.TargetPath) : "";
            if (currentHash != file.ExistingHash) throw new IOException("Target changed after preview: " + file.TargetPath);
        }
        var record = new FileTransaction(storage).Apply("Import " + Path.GetFileName(plan.Source), plan.Files
            .Where(x => x.Include && x.Action is ImportAction.Add or ImportAction.Replace).Select(x => (x.TargetPath, (byte[]?)x.Content)), cancel, steam.Root);
        record.ImportSourceName = Path.GetFileName(plan.Source);
        foreach (var operation in record.Operations) operation.ManagedHash = operation.NewHash;
        storage.SaveRecord(record);
        return record;
    }
}

public sealed class ReleaseService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    public ReleaseService() { _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OpenSteamToolGUI", typeof(ReleaseService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0")); }
    public async Task<ReleaseInfo> LatestAsync(string channel, CancellationToken cancel = default)
    {
        using var stream = await _http.GetStreamAsync("https://api.github.com/repos/OpenSteam001/OpenSteamTool/releases/latest", cancel);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancel);
        var root = json.RootElement;
        var assets = root.GetProperty("assets").EnumerateArray();
        var asset = assets.FirstOrDefault(x => x.GetProperty("name").GetString()?.EndsWith("-" + channel + ".zip", StringComparison.OrdinalIgnoreCase) == true);
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("Release ZIP asset not found.");
        string digest = asset.TryGetProperty("digest", out var field) ? field.GetString() ?? "" : "";
        return new ReleaseInfo { Version = root.GetProperty("tag_name").GetString() ?? "", ReleaseUrl = root.GetProperty("html_url").GetString() ?? "", AssetName = asset.GetProperty("name").GetString() ?? "", AssetUrl = asset.GetProperty("browser_download_url").GetString() ?? "", Channel = channel, Sha256 = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : "" };
    }
    public async Task<string> DownloadAsync(ReleaseInfo release, IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        string path = Path.Combine(Path.GetTempPath(), "ostgui-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using var response = await _http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancel);
            await using var output = File.Create(path);
            var buffer = new byte[81920]; long count = 0; int read;
            while ((read = await source.ReadAsync(buffer, cancel)) != 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancel); count += read;
                if (response.Content.Headers.ContentLength is long length && length > 0) progress?.Report((int)(count * 100 / length));
            }
            await output.FlushAsync(cancel);
            await output.DisposeAsync();
            if (release.Sha256.Length == 64 && !FileTools.HashFile(path).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Downloaded release checksum does not match GitHub metadata.");
            return path;
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
}

public sealed class Installer(Storage storage, AppPreferences preferences, Func<bool>? steamRunning = null)
{
    private static readonly string[] Required = ["dwmapi.dll", "xinput1_4.dll", "OpenSteamTool.dll"];
    private readonly Func<bool> _steamRunning = steamRunning ?? SteamLocator.IsRunning;
    public bool IsDisabled => preferences.OwnedFiles.Count == Required.Length && preferences.OwnedFiles.All(x => x.DisabledBackup is not null);
    private static OwnedFile CopyOwned(OwnedFile file) => new() { RelativePath = file.RelativePath, InstalledHash = file.InstalledHash, OriginalBackup = file.OriginalBackup, DisabledBackup = file.DisabledBackup };
    private void SavePreviousState(BackupRecord record)
    {
        record.PreviousVersion = preferences.InstalledVersion;
        record.PreviousChannel = preferences.InstalledChannel;
        record.PreviousOwnedFiles = preferences.OwnedFiles.Select(CopyOwned).ToList();
        storage.SaveRecord(record);
    }
    private List<OwnedFile> ManagedFiles(SteamInstallation steam)
    {
        if (!steam.IsValid || preferences.OwnedFiles.Count != Required.Length ||
            Required.Any(name => preferences.OwnedFiles.Count(x => x.RelativePath.Equals(name, StringComparison.OrdinalIgnoreCase)) != 1))
            throw new IOException("No managed OpenSteamTool installation was found.");
        foreach (var name in Required) FileTools.RejectReparsePath(steam.Root, Path.Combine(steam.Root, name));
        return Required.Select(name => preferences.OwnedFiles.Single(x => x.RelativePath.Equals(name, StringComparison.OrdinalIgnoreCase))).ToList();
    }
    public void SetEnabled(SteamInstallation steam, bool enabled)
    {
        if (_steamRunning()) throw new IOException("Close Steam before changing OpenSteamTool state.");
        var files = ManagedFiles(steam);
        if (files.Any(x => x.DisabledBackup is not null) != files.All(x => x.DisabledBackup is not null))
            throw new IOException("OpenSteamTool managed files have an inconsistent state.");
        if (enabled == !IsDisabled) return;
        var changes = new List<(string Target, byte[]? Content)>();
        foreach (var owned in files)
        {
            string target = Path.Combine(steam.Root, owned.RelativePath);
            if (enabled)
            {
                byte[]? original = owned.OriginalBackup is null ? null : File.ReadAllBytes(owned.OriginalBackup);
                if (File.Exists(target) != (original is not null) || original is not null && FileTools.HashFile(target) != FileTools.Hash(original))
                    throw new IOException("File changed since installation: " + target);
                if (owned.DisabledBackup is null || !File.Exists(owned.DisabledBackup)) throw new IOException("Managed DLL backup is missing.");
                byte[] installed = File.ReadAllBytes(owned.DisabledBackup);
                if (FileTools.Hash(installed) != owned.InstalledHash) throw new IOException("Managed DLL backup changed.");
                changes.Add((target, installed));
            }
            else
            {
                if (!File.Exists(target) || FileTools.HashFile(target) != owned.InstalledHash)
                    throw new IOException("File changed since installation: " + target);
                changes.Add((target, owned.OriginalBackup is null ? null : File.ReadAllBytes(owned.OriginalBackup)));
            }
        }
        var record = new FileTransaction(storage).Apply(enabled ? "Enable OpenSteamTool" : "Disable OpenSteamTool", changes, steamRoot: steam.Root);
        SavePreviousState(record);
        for (int i = 0; i < files.Count; i++) files[i].DisabledBackup = enabled ? null : record.Operations[i].BackupPath;
        storage.SavePreferences(preferences);
    }
    public BackupRecord Install(string zipPath, ReleaseInfo release, SteamInstallation steam, bool replaceUntracked = false)
    {
        if (_steamRunning()) throw new IOException("Close Steam before installing OpenSteamTool.");
        if (preferences.OwnedFiles.Any(x => x.DisabledBackup is not null)) throw new IOException("Enable OpenSteamTool before updating it.");
        var changes = new List<(string Target, byte[]? Content)>();
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (string name in Required)
        {
            var matching = zip.Entries.Where(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matching.Count != 1) throw new InvalidDataException("Release asset is missing or duplicates " + name);
            using var s = matching[0].Open(); using var m = new MemoryStream(); s.CopyTo(m);
            if (m.Length is < 100 or > 128_000_000) throw new InvalidDataException("Invalid DLL size: " + name);
            string target = Path.Combine(steam.Root, name);
            var owned = preferences.OwnedFiles.FirstOrDefault(x => x.RelativePath.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (File.Exists(target) && owned is null && !replaceUntracked) throw new IOException("Existing untracked DLL requires manual review: " + target);
            if (File.Exists(target) && owned is not null && FileTools.HashFile(target) != owned.InstalledHash) throw new IOException("Installed DLL changed externally: " + target);
            changes.Add((target, m.ToArray()));
        }
        var record = new FileTransaction(storage).Apply("Install OpenSteamTool " + release.Version, changes, steamRoot: steam.Root);
        SavePreviousState(record);
        foreach (var op in record.Operations)
        {
            string name = Path.GetFileName(op.TargetPath);
            var owned = preferences.OwnedFiles.FirstOrDefault(x => x.RelativePath.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (owned is null) preferences.OwnedFiles.Add(new OwnedFile { RelativePath = name, InstalledHash = op.NewHash!, OriginalBackup = op.BackupPath });
            else owned.InstalledHash = op.NewHash!;
        }
        Directory.CreateDirectory(steam.LuaDirectory);
        preferences.InstalledVersion = release.Version; preferences.InstalledChannel = release.Channel;
        storage.SavePreferences(preferences);
        return record;
    }
    public void Uninstall(SteamInstallation steam)
    {
        if (_steamRunning()) throw new IOException("Close Steam before uninstalling OpenSteamTool.");
        if (preferences.OwnedFiles.Any(x => x.DisabledBackup is not null)) throw new IOException("Enable OpenSteamTool before uninstalling it.");
        var changes = new List<(string Target, byte[]? Content)>();
        foreach (var owned in preferences.OwnedFiles)
        {
            string path = Path.Combine(steam.Root, owned.RelativePath);
            if (!FileTools.Inside(steam.Root, path)) throw new IOException("Invalid tracked file.");
            if (File.Exists(path) && FileTools.HashFile(path) != owned.InstalledHash) throw new IOException("File changed since installation: " + path);
            changes.Add((path, owned.OriginalBackup is not null ? File.ReadAllBytes(owned.OriginalBackup) : null));
        }
        var record = new FileTransaction(storage).Apply("Uninstall OpenSteamTool and restore originals", changes, steamRoot: steam.Root);
        SavePreviousState(record);
        preferences.OwnedFiles.Clear(); preferences.InstalledVersion = ""; preferences.InstalledChannel = "";
        storage.SavePreferences(preferences);
    }
}
