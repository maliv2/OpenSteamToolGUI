using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenSteamToolGUI.Core;

public sealed class GameNames(Storage storage)
{
    private readonly Dictionary<uint, string> _cache = LoadCache(storage.Root);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static Dictionary<uint, string> LoadCache(string root)
    {
        try { return JsonSerializer.Deserialize<Dictionary<uint, string>>(File.ReadAllText(Path.Combine(root, "game-names.json"))) ?? []; }
        catch { return []; }
    }
    public void ReadLocal(SteamInstallation steam)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(steam.Root, "steamapps") };
        string libraryFile = Path.Combine(steam.Root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFile))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(libraryFile), "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                folders.Add(Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "steamapps"));
        }
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var path in Directory.EnumerateFiles(folder, "appmanifest_*.acf"))
            {
                if (!uint.TryParse(Path.GetFileNameWithoutExtension(path)["appmanifest_".Length..], out var id)) continue;
                try
                {
                    var m = Regex.Match(File.ReadAllText(path), "\"name\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (m.Success) _cache[id] = m.Groups[1].Value;
                }
                catch { }
            }
        }
        Save();
    }
    public string Get(uint id) => _cache.TryGetValue(id, out var value) ? value : id.ToString();
    public bool HasName(uint id) => _cache.ContainsKey(id);
    public async Task<string> ResolveAsync(uint id, CancellationToken cancel = default)
    {
        if (_cache.TryGetValue(id, out var name)) return name;
        try
        {
            using var stream = await _http.GetStreamAsync($"https://store.steampowered.com/api/appdetails?appids={id}&filters=basic", cancel);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancel);
            var game = json.RootElement.GetProperty(id.ToString());
            if (game.GetProperty("success").GetBoolean())
            {
                name = game.GetProperty("data").GetProperty("name").GetString();
                if (!string.IsNullOrWhiteSpace(name)) { _cache[id] = name; Save(); return name; }
            }
        }
        catch { }
        return id.ToString();
    }
    private void Save() => Storage.AtomicWrite(Path.Combine(storage.Root, "game-names.json"), JsonSerializer.Serialize(_cache));
}
