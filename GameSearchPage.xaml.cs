using OpenSteamToolGUI.Core;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace OpenSteamToolGUI;

public partial class GameSearchPage : UserControl
{
    private static readonly HttpClient ArtworkHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
    private readonly SemaphoreSlim _artworkRequests = new(4);
    private GameFinderService? _finder;
    private CancellationTokenSource? _artworkCancel;
    private readonly Dictionary<string, bool?> _serverStates = new()
    {
        ["Steam Store"] = null,
        ["SteamManifest.com"] = null,
        ["Remlua"] = null
    };
    private readonly CancellationTokenSource _serverCheckCancel = new();
    private bool _serverCheckStarted;
    private bool _busy;

    public event Action<FoundGame>? GameChosen;

    public GameSearchPage()
    {
        InitializeComponent();
        RefreshLanguage();
    }

    public void Initialize(GameFinderService finder) => _finder = finder;

    public void RefreshLanguage()
    {
        Introduction.Text = UiText.T("Search Steam games by name or AppID.");
        QueryBox.ToolTip = UiText.T("Game name or AppID");
        SearchButton.Content = UiText.T("Search Online");
        AddButton.Content = UiText.T("Add to Library");
        ArtworkColumn.Header = UiText.T("Cover");
        AppIdColumn.Header = "AppID";
        GameColumn.Header = UiText.T("Game");
        SetServerLine(SteamStoreStatus, "Steam Store", _serverStates["Steam Store"]);
        SetServerLine(SteamManifestStatus, "SteamManifest.com", _serverStates["SteamManifest.com"]);
        SetServerLine(RemluaStatus, "Remlua", _serverStates["Remlua"]);
        if (!_busy && ResultsGrid.ItemsSource is null)
            ResultsMessage.Text = UiText.T("Search Steam games by name or AppID.");
        else if (!_busy && ResultsGrid.ItemsSource is GameSearchResult[] rows)
            ResultsMessage.Text = rows.Length == 0 ? UiText.T("No games found.") : string.Format(UiText.T("{0} games found."), rows.Length);
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await SearchAsync();
    private async void QueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await SearchAsync(); }
    }

    private async Task SearchAsync()
    {
        if (_busy || _finder is null) return;
        _artworkCancel?.Cancel();
        _artworkCancel?.Dispose();
        _artworkCancel = new CancellationTokenSource();
        var cancel = _artworkCancel.Token;
        SetBusy(true);
        ResultsMessage.Text = UiText.T("Searching…");
        ResultsGrid.ItemsSource = null;
        try
        {
            var games = await _finder.SearchAsync(QueryBox.Text);
            var rows = games.Select(game => new GameSearchResult(game)).ToArray();
            ResultsGrid.ItemsSource = rows;
            ResultsMessage.Text = rows.Length == 0 ? UiText.T("No games found.") : string.Format(UiText.T("{0} games found."), rows.Length);
            _ = LoadArtworkAsync(rows, cancel);
        }
        catch (Exception ex) { ResultsMessage.Text = UiText.T(ex.Message); }
        finally { SetBusy(false); }
    }

    private async Task LoadArtworkAsync(GameSearchResult[] rows, CancellationToken cancel)
    {
        await Task.WhenAll(rows.Select(async row =>
        {
            try
            {
                await _artworkRequests.WaitAsync(cancel);
                try
                {
                    var artwork = await GetArtworkAsync(row.AppId, cancel);
                    if (!cancel.IsCancellationRequested) row.Artwork = artwork;
                }
                finally { _artworkRequests.Release(); }
            }
            catch (OperationCanceledException) { }
            catch { /* A missing cover leaves the neutral placeholder visible. */ }
        }));
    }

    private static async Task<BitmapImage?> GetArtworkAsync(uint appId, CancellationToken cancel)
    {
        string[] urls =
        [
            $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg",
            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg",
            $"https://steamcdn-a.akamaihd.net/steam/apps/{appId}/header.jpg"
        ];
        foreach (string url in urls)
        {
            var bitmap = await TryLoadArtworkAsync(new Uri(url), cancel);
            if (bitmap is not null) return bitmap;
        }
        try
        {
            using var response = await ArtworkHttp.GetAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic", HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 256 * 1024 ||
                response.Content.Headers.ContentType?.MediaType != "application/json") return null;
            await using var input = await response.Content.ReadAsStreamAsync(cancel);
            using var bytes = new MemoryStream();
            var buffer = new byte[16384];
            int read;
            while ((read = await input.ReadAsync(buffer, cancel)) != 0)
            {
                if (bytes.Length + read > 256 * 1024) return null;
                bytes.Write(buffer, 0, read);
            }
            bytes.Position = 0;
            using var json = JsonDocument.Parse(bytes);
            foreach (var url in SteamArtwork.StoreAssetUrls(appId, json.RootElement))
            {
                var bitmap = await TryLoadArtworkAsync(url, cancel);
                if (bitmap is not null) return bitmap;
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
        catch { /* Steam has no usable artwork for this AppID. */ }
        return null;
    }

    private static async Task<BitmapImage?> TryLoadArtworkAsync(Uri url, CancellationToken cancel)
    {
        try
        {
            using var response = await ArtworkHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1024 * 1024 ||
                response.Content.Headers.ContentType?.MediaType is not ("image/jpeg" or "image/png" or "image/webp")) return null;
            await using var input = await response.Content.ReadAsStreamAsync(cancel);
            using var bytes = new MemoryStream();
            var buffer = new byte[16384];
            int read;
            while ((read = await input.ReadAsync(buffer, cancel)) != 0)
            {
                if (bytes.Length + read > 1024 * 1024) throw new InvalidDataException();
                bytes.Write(buffer, 0, read);
            }
            bytes.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 224;
            bitmap.StreamSource = bytes;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
        catch { /* Try the next Steam image. */ }
        return null;
    }

    private static void SetServerLine(TextBlock line, string name, bool? online)
    {
        line.Inlines.Clear();
        line.Inlines.Add(new Run(name + ": "));
        var status = new Run(UiText.T(online is null ? "Checking…" : online.Value ? "Online" : "Offline"));
        status.SetResourceReference(TextElement.ForegroundProperty,
            online is null ? "UiStatusChecking" : online.Value ? "UiStatusOnline" : "UiStatusOffline");
        line.Inlines.Add(status);
    }

    public void UpdateServerStatuses(IReadOnlyList<FinderServer> servers)
    {
        foreach (var server in servers)
            if (_serverStates.ContainsKey(server.Name)) _serverStates[server.Name] = server.Online;
        RefreshLanguage();
    }

    public async Task CheckServersOnStartupAsync()
    {
        if (_serverCheckStarted) return;
        _serverCheckStarted = true;
        try
        {
            using var probe = new GameFinderService();
            UpdateServerStatuses(await probe.CheckServersAsync(_serverCheckCancel.Token, server =>
            {
                if (!_serverCheckCancel.IsCancellationRequested)
                    Dispatcher.Invoke(() => UpdateServerStatuses([server]));
            }));
        }
        catch (OperationCanceledException) when (_serverCheckCancel.IsCancellationRequested) { }
        catch { UpdateServerStatuses(_serverStates.Keys.Select(name => new FinderServer(name, false)).ToArray()); }
    }

    public void StopServerCheck() => _serverCheckCancel.Cancel();

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SearchButton.IsEnabled = !busy;
        AddButton.IsEnabled = !busy && ResultsGrid.SelectedItem is GameSearchResult;
    }

    private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => AddButton.IsEnabled = !_busy && ResultsGrid.SelectedItem is GameSearchResult;
    private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ChooseGame();
    private void AddButton_Click(object sender, RoutedEventArgs e) => ChooseGame();
    private void ChooseGame()
    {
        if (!_busy && ResultsGrid.SelectedItem is GameSearchResult row) GameChosen?.Invoke(row.Game);
    }
}

public sealed class GameSearchResult(FoundGame game) : INotifyPropertyChanged
{
    private BitmapImage? _artwork;
    public FoundGame Game { get; } = game;
    public uint AppId => Game.AppId;
    public string Name => Game.Name;
    public BitmapImage? Artwork
    {
        get => _artwork;
        set { _artwork = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Artwork))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
