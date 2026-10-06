using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace OpenSteamToolGUI.Core;

public sealed record FoundGame(uint AppId, string Name);
public sealed record FinderServer(string Name, bool Online);
public sealed record FoundFile(string Source, string FileName, byte[] Content);

public sealed class GameFinderService : IDisposable
{
    private const int MaxJsonBytes = 2 * 1024 * 1024;
    private const int MaxLuaBytes = 16 * 1024 * 1024;
    private const int MaxZipBytes = 64 * 1024 * 1024;
    private const string ManifestBase = "https://steammanifest.com";
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private string? _csrf;

    public GameFinderService(HttpClient? http = null)
    {
        _ownsClient = http is null;
        _http = http ?? new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false });
        _http.Timeout = TimeSpan.FromSeconds(65);
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenSteamToolGUI/" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"));
    }

    public async Task<IReadOnlyList<FinderServer>> CheckServersAsync(CancellationToken cancel = default, Action<FinderServer>? onResult = null)
    {
        async Task<FinderServer> Check(string name, Func<Task> probe)
        {
            FinderServer result;
            try { await probe(); result = new(name, true); }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch { result = new(name, false); }
            onResult?.Invoke(result);
            return result;
        }
        var store = Check("Steam Store", async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://store.steampowered.com/api/storesearch/?term=Counter-Strike&l=english&cc=US");
            using var json = await ReadJsonAsync(request, cancel);
            if (!json.RootElement.TryGetProperty("items", out _)) throw new InvalidDataException("Invalid Steam Store response.");
        });
        var manifest = Check("SteamManifest.com", async () => { _csrf = null; await EnsureSessionAsync(cancel); });
        var remlua = Check("Remlua", async () =>
        {
            using var request = RemluaRequest(HttpMethod.Head, 730);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
        });
        return await Task.WhenAll(store, manifest, remlua);
    }

    public async Task<IReadOnlyList<FoundGame>> SearchAsync(string query, CancellationToken cancel = default)
    {
        query = query.Trim();
        if (query.Length is < 1 or > 100) throw new ArgumentException("Enter a game name or AppID.");
        if (uint.TryParse(query, out var appId) && appId != 0)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://store.steampowered.com/api/appdetails?appids={appId}&l=english");
                using var json = await ReadJsonAsync(request, cancel);
                var app = json.RootElement.GetProperty(appId.ToString());
                if (app.GetProperty("success").GetBoolean())
                    return [new(appId, app.GetProperty("data").GetProperty("name").GetString() ?? $"AppID {appId}")];
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch { }
            return [new(appId, $"AppID {appId}")];
        }
        var store = TrySearchAsync(() => SearchStoreAsync(query, cancel), cancel);
        var manifest = TrySearchAsync(() => SearchManifestAsync(query, cancel), cancel);
        await Task.WhenAll(store, manifest);
        if (store.Result is null && manifest.Result is null) throw new HttpRequestException("Search servers are unavailable.");
        return (store.Result ?? []).Concat(manifest.Result ?? [])
            .Where(game => game.AppId != 0 && !string.IsNullOrWhiteSpace(game.Name))
            .GroupBy(game => game.AppId).Select(group => group.First()).Take(30).ToArray();
    }

    public async Task<IReadOnlyList<uint>> GetDlcIdsAsync(uint appId, CancellationToken cancel = default)
    {
        if (appId == 0) throw new ArgumentOutOfRangeException(nameof(appId));
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://store.steampowered.com/api/appdetails?appids={appId}&l=english");
        using var json = await ReadJsonAsync(request, cancel);
        return ParseDlcIds(json.RootElement, appId);
    }

    public static IReadOnlyList<uint> ParseDlcIds(JsonElement root, uint appId)
    {
        JsonElement app;
        if (!root.TryGetProperty(appId.ToString(), out app))
        {
            app = root.EnumerateObject().Select(property => property.Value).FirstOrDefault(value =>
                value.ValueKind == JsonValueKind.Object && value.TryGetProperty("data", out var candidate) &&
                candidate.ValueKind == JsonValueKind.Object &&
                candidate.TryGetProperty("steam_appid", out var id) &&
                id.ValueKind == JsonValueKind.Number && id.TryGetUInt32(out uint parsed) && parsed == appId);
        }
        if (app.ValueKind != JsonValueKind.Object ||
            !app.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
            !app.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            (data.TryGetProperty("steam_appid", out var actualId) &&
                (actualId.ValueKind != JsonValueKind.Number || !actualId.TryGetUInt32(out uint parsedId) || parsedId != appId)))
            throw new InvalidDataException("Steam did not provide DLC information for this game.");
        if (!data.TryGetProperty("dlc", out var dlc)) return [];
        if (dlc.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Steam returned an invalid DLC list.");
        var ids = new List<uint>();
        foreach (var item in dlc.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetUInt32(out uint id) || id == 0)
                throw new InvalidDataException("Steam returned an invalid DLC list.");
            if (id != appId && !ids.Contains(id)) ids.Add(id);
        }
        return ids;
    }

    private static async Task<IReadOnlyList<FoundGame>?> TrySearchAsync(Func<Task<IReadOnlyList<FoundGame>>> search, CancellationToken cancel)
    {
        try { return await search(); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private async Task<IReadOnlyList<FoundGame>> SearchStoreAsync(string query, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(query) + "&l=english&cc=US");
        using var json = await ReadJsonAsync(request, cancel);
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Where(item => item.TryGetProperty("id", out var id) && id.TryGetUInt32(out _) && item.TryGetProperty("name", out _))
            .Select(item => new FoundGame(item.GetProperty("id").GetUInt32(), item.GetProperty("name").GetString() ?? ""))
            .ToArray();
    }

    private async Task<IReadOnlyList<FoundGame>> SearchManifestAsync(string query, CancellationToken cancel)
    {
        using var request = await ManifestRequestAsync(new { action = "search", q = query }, cancel);
        using var json = await ReadJsonAsync(request, cancel);
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Where(item => item.TryGetProperty("appid", out var id) && id.TryGetUInt32(out _) && item.TryGetProperty("name", out _))
            .Select(item => new FoundGame(item.GetProperty("appid").GetUInt32(), item.GetProperty("name").GetString() ?? ""))
            .ToArray();
    }

    public async Task<FoundFile> DownloadRemluaAsync(uint appId, CancellationToken cancel = default)
    {
        if (appId == 0) throw new ArgumentOutOfRangeException(nameof(appId));
        using (var head = RemluaRequest(HttpMethod.Head, appId))
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var availability = await _http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            availability.EnsureSuccessStatusCode();
        }
        using var request = RemluaRequest(HttpMethod.Get, appId);
        byte[] bytes = await ReadBytesAsync(request, MaxZipBytes, cancel);
        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (!zip.Entries.Any(entry => entry.Name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The source ZIP contains no Lua file.");
        return new("Remlua", $"{appId}.zip", bytes);
    }

    public async Task<FoundFile> DownloadSteamManifestAsync(uint appId, CancellationToken cancel = default)
    {
        if (appId == 0) throw new ArgumentOutOfRangeException(nameof(appId));
        using var request = await ManifestRequestAsync(new { action = "lua", appid = appId.ToString(), format = "lua", turnstile = "" }, cancel);
        byte[] body = await ReadBytesAsync(request, MaxLuaBytes, cancel);
        string text = Encoding.UTF8.GetString(body);
        try
        {
            using var json = JsonDocument.Parse(text);
            if (!json.RootElement.TryGetProperty("file", out var file) || file.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("SteamManifest did not provide a Lua file.");
            text = file.GetString() ?? "";
        }
        catch (JsonException) { }
        if (!LuaAnalyzer.AppIds(text).Contains(appId)) throw new InvalidDataException("The source Lua does not match the selected AppID.");
        return new("SteamManifest.com", $"{appId}.lua", Encoding.UTF8.GetBytes(text));
    }

    public static void MatchDownloadedGame(ImportPlan plan, uint appId)
    {
        var matching = plan.Files.Where(file => file.Kind == "Lua" &&
            (file.AppId == appId || Path.GetFileNameWithoutExtension(file.ArchivePath).Equals(appId.ToString(), StringComparison.OrdinalIgnoreCase)) &&
            LuaAnalyzer.AppIds(Encoding.UTF8.GetString(file.Content)).Contains(appId)).ToList();
        if (matching.Count == 0) throw new InvalidDataException("The source has no Lua file for the selected AppID.");
        foreach (var file in matching.Where(file => file.AppId is null))
        {
            file.AppId = appId;
            file.Game = appId.ToString();
            file.Confidence = "Filename only";
        }
    }

    private static HttpRequestMessage RemluaRequest(HttpMethod method, uint appId)
    {
        string s3 = $"https://steamgames554.s3.us-east-1.amazonaws.com/{appId}.zip";
        var request = new HttpRequestMessage(method, "https://remlua.com/proxy.php?url=" + Uri.EscapeDataString(s3));
        request.Headers.Referrer = new Uri("https://remlua.com/");
        request.Headers.TryAddWithoutValidation("Origin", "https://remlua.com");
        return request;
    }

    private async Task EnsureSessionAsync(CancellationToken cancel)
    {
        if (!string.IsNullOrEmpty(_csrf)) return;
        await _sessionLock.WaitAsync(cancel);
        try
        {
            if (!string.IsNullOrEmpty(_csrf)) return;
            using var request = new HttpRequestMessage(HttpMethod.Get, ManifestBase + "/api.php?action=session");
            AddManifestHeaders(request);
            using var json = await ReadJsonAsync(request, cancel);
            _csrf = json.RootElement.GetProperty("csrf").GetString();
            if (string.IsNullOrWhiteSpace(_csrf)) throw new InvalidDataException("SteamManifest session is unavailable.");
        }
        finally { _sessionLock.Release(); }
    }

    private async Task<HttpRequestMessage> ManifestRequestAsync(object payload, CancellationToken cancel)
    {
        _csrf = null;
        await EnsureSessionAsync(cancel);
        var request = new HttpRequestMessage(HttpMethod.Post, ManifestBase + "/api.php") { Content = JsonContent.Create(payload) };
        AddManifestHeaders(request);
        request.Headers.TryAddWithoutValidation("X-SteamManifest-CSRF", _csrf);
        return request;
    }

    private static void AddManifestHeaders(HttpRequestMessage request)
    {
        request.Headers.Referrer = new Uri(ManifestBase + "/");
        request.Headers.TryAddWithoutValidation("Origin", ManifestBase);
    }

    private async Task<JsonDocument> ReadJsonAsync(HttpRequestMessage request, CancellationToken cancel)
    {
        byte[] bytes = await ReadBytesAsync(request, MaxJsonBytes, cancel);
        return JsonDocument.Parse(bytes);
    }

    private async Task<byte[]> ReadBytesAsync(HttpRequestMessage request, int maximum, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(maximum > MaxJsonBytes ? 60 : 20));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Downloaded file exceeds the size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, timeout.Token)) != 0)
        {
            if (output.Length + read > maximum) throw new InvalidDataException("Downloaded file exceeds the size limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
        _sessionLock.Dispose();
    }
}
