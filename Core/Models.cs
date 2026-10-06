using System.Text.Json.Serialization;

namespace OpenSteamToolGUI.Core;

public sealed class AppPreferences
{
    public string SteamPath { get; set; } = "";
    public string Language { get; set; } = "en";
    public string Appearance { get; set; } = "Dark";
    public bool CheckUpdates { get; set; } = true;
    public int BackupRetention { get; set; } = 20;
    public string InstalledVersion { get; set; } = "";
    public string InstalledChannel { get; set; } = "";
    public List<OwnedFile> OwnedFiles { get; set; } = [];
}

public sealed class OwnedFile
{
    public string RelativePath { get; set; } = "";
    public string InstalledHash { get; set; } = "";
    public string? OriginalBackup { get; set; }
    public string? DisabledBackup { get; set; }
}

public sealed class SteamInstallation(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string LuaDirectory => Path.Combine(Root, "config", "lua");
    public string DepotCache => Path.Combine(Root, "depotcache");
    public string ConfigFile => Path.Combine(Root, "opensteamtool.toml");
    public bool IsValid => File.Exists(Path.Combine(Root, "steam.exe"));
}

public sealed class ToolCapabilities
{
    public bool StatsApi { get; set; }
    public bool CloudRedirect { get; set; }
    public bool ManifestSize { get; set; }
    public static ToolCapabilities ForVersion(string version)
    {
        // The currently published 1.4.8 release lacks the newer main-branch options.
        return new() { StatsApi = false, CloudRedirect = false, ManifestSize = false };
    }
}

public sealed class GamePackage
{
    public string Name { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    public bool Enabled { get; set; }
    public List<uint> AppIds { get; set; } = [];
    public bool ComplexScript { get; set; }
    public string? ImportRecordId { get; set; }
    public bool ImportOnly { get; set; }
    public bool IsBundle { get; set; }
    public string? StatusOverride { get; set; }
    public string? PathOverride { get; set; }
    public string DisplayPath => PathOverride ?? SourcePath;
    public string Summary => AppIds.Count == 0 ? "Unidentified package" : AppIds.Count <= 5 ? string.Join(", ", AppIds) : string.Join(", ", AppIds.Take(4)) + $" +{AppIds.Count - 4} more";
    public string Status => StatusOverride ?? (ImportOnly ? "Imported files" : Enabled ? "Active" : "Inactive");
    public string DisplaySummary => ImportOnly ? "" : AppIds.Count == 0 ? UiText.T("Unidentified package") : AppIds.Count <= 5 ? string.Join(", ", AppIds) : string.Join(", ", AppIds.Take(4)) + $" +{AppIds.Count - 4}";
}

public enum ImportAction { Add, SkipIdentical, KeepExisting, Replace }
public sealed class ImportFile
{
    public bool Include { get; set; } = true;
    public string ArchivePath { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Game { get; set; } = "Unidentified package";
    public uint? AppId { get; set; }
    public string Confidence { get; set; } = "Unknown";
    public ImportAction Action { get; set; }
    public string ExistingHash { get; set; } = "";
    public long UncompressedSize { get; set; }
    [JsonIgnore] public byte[] Content { get; set; } = [];
}
public sealed class ImportPlan
{
    public string Source { get; set; } = "";
    public List<ImportFile> Files { get; set; } = [];
    public uint? BundleAppId { get; set; }
    public string? BundleName { get; set; }
    public List<uint> BundleAppIds { get; set; } = [];
}

public sealed class FileOperation
{
    public string TargetPath { get; set; } = "";
    public string? BackupPath { get; set; }
    public string? NewHash { get; set; }
    public string? ManagedHash { get; set; }
}
public sealed class BackupRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = "";
    public string? ImportSourceName { get; set; }
    public uint? BundleAppId { get; set; }
    public string? BundleName { get; set; }
    public List<uint> BundleAppIds { get; set; } = [];
    public bool ImportRemoved { get; set; }
    public string? RemovedImportId { get; set; }
    public string? CompanionRemovalId { get; set; }
    public bool Completed { get; set; }
    public bool Restored { get; set; }
    public string? PreviousVersion { get; set; }
    public string? PreviousChannel { get; set; }
    public List<OwnedFile>? PreviousOwnedFiles { get; set; }
    public string? AuxiliaryPath { get; set; }
    public string? AuxiliaryBackupPath { get; set; }
    public string? AuxiliaryHash { get; set; }
    public List<FileOperation> Operations { get; set; } = [];
}

public sealed class ReleaseInfo
{
    public string Version { get; set; } = "";
    public string ReleaseUrl { get; set; } = "";
    public string AssetUrl { get; set; } = "";
    public string AssetName { get; set; } = "";
    public string Channel { get; set; } = "Release";
    public string Sha256 { get; set; } = "";
}
