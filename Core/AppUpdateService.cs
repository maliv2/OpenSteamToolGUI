using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenSteamToolGUI.Core;

public sealed record AppUpdate(string Tag, string AssetUrl, string Sha256);

public sealed class AppUpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/muhammetaliaydin/OpenSteamToolGUI/releases/latest";
    private const string AssetName = "OpenSteamToolGUI-portable-win-x64.zip";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(45) };

    public AppUpdateService() => _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OpenSteamToolGUI", "1.0"));

    public static bool IsNewer(string tag, Version current) =>
        Version.TryParse(tag.TrimStart('v', 'V'), out var available) && available > current;

    public async Task<AppUpdate?> CheckAsync(CancellationToken cancel = default)
    {
        using var response = await _http.GetAsync(ApiUrl, cancel);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancel));
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0);
        if (!IsNewer(tag, current)) return null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != AssetName) continue;
            var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
            if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || digest.Length != 71)
                throw new InvalidDataException("The release asset has no SHA-256 digest.");
            var hash = digest[7..];
            if (!hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid release digest.");
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com")
                throw new InvalidDataException("Unexpected release download URL.");
            return new AppUpdate(tag, url, hash);
        }
        throw new InvalidDataException("The portable Windows release asset is missing.");
    }

    public async Task<string> StageAsync(AppUpdate update, CancellationToken cancel = default)
    {
        var stage = Path.Combine(Path.GetTempPath(), "OpenSteamToolGUI-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var archivePath = Path.Combine(stage, "release.zip");
            using (var response = await _http.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > 250_000_000) throw new InvalidDataException("Update archive is too large.");
                await using var input = await response.Content.ReadAsStreamAsync(cancel);
                await using var output = File.Create(archivePath);
                var buffer = new byte[81920];
                long size = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancel)) != 0)
                {
                    size += read;
                    if (size > 250_000_000) throw new InvalidDataException("Update archive is too large.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancel);
                }
            }
            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archivePath)));
            if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update checksum does not match GitHub.");
            var files = Path.Combine(stage, "files");
            Directory.CreateDirectory(files);
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                if (archive.Entries.Count is < 1 or > 20 || archive.Entries.Sum(x => x.Length) > 400_000_000)
                    throw new InvalidDataException("Unexpected update archive contents.");
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name != entry.FullName || entry.Name is "." or ".." || entry.Name.Contains(':') ||
                        entry.Name.Contains('\\') || entry.Length > 250_000_000 ||
                        (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                        throw new InvalidDataException("Unsafe update archive entry.");
                    entry.ExtractToFile(Path.Combine(files, entry.Name));
                }
            }
            if (!File.Exists(Path.Combine(files, "OpenSteamToolGUI.exe"))) throw new InvalidDataException("Update executable is missing.");
            File.Delete(archivePath);
            return stage;
        }
        catch
        {
            Directory.Delete(stage, true);
            throw;
        }
    }

    public static void LaunchInstaller(string stage)
    {
        var installDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var executable = Path.Combine(installDirectory, "OpenSteamToolGUI.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("The installed application executable was not found.", executable);
        var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("OpenSteamToolGUI.UpdateHelper.ps1")
            ?? throw new InvalidOperationException("Update helper is missing.");
        var script = Path.Combine(stage, "UpdateHelper.ps1");
        using (resource)
        using (var output = File.Create(script)) resource.CopyTo(output);
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                     "-Stage", stage, "-InstallDirectory", installDirectory, "-ProcessId", Environment.ProcessId.ToString() })
            start.ArgumentList.Add(argument);
        Process.Start(start)?.Dispose();
    }
}
