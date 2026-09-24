using OpenSteamToolGUI.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenSteamToolGUI;

public sealed class GameFinderWindow : Window
{
    private readonly GameFinderService _finder;
    private readonly TextBox _query = new() { MinWidth = 300 };
    private readonly DataGrid _results = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single };
    private readonly TextBlock _message = new() { Margin = new Thickness(4, 8, 4, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Button _search = new();
    private readonly Button _refresh = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _add = new() { IsEnabled = false };
    private readonly Dictionary<string, TextBlock> _serverStates = [];
    private bool _busy;
    public uint SelectedAppId { get; private set; }
    public string SelectedName { get; private set; } = "";

    public GameFinderWindow(GameFinderService finder, bool offline = false)
    {
        _finder = finder;
        Title = UiText.T("Find Games Online");
        Width = 760; Height = 560; MinWidth = 620; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ThemeService.StyleWindow(this);

        var root = new DockPanel { Margin = new Thickness(16) };
        Content = root;
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = UiText.T("Search Steam games by name or AppID."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });

        var servers = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var serverLines = new StackPanel();
        _refresh.Content = UiText.T("Check Servers");
        _refresh.Click += async (_, _) => await RefreshServersAsync();
        DockPanel.SetDock(_refresh, Dock.Right);
        servers.Children.Add(_refresh);
        servers.Children.Add(serverLines);
        foreach (string name in new[] { "Steam Store", "SteamManifest.com", "Remlua" })
        {
            var state = new TextBlock { Text = name + ": " + UiText.T("Checking…") };
            state.ToolTip = UiText.T("Server status shows connectivity; availability varies by game.");
            _serverStates[name] = state; serverLines.Children.Add(state);
        }
        header.Children.Add(servers);

        var searchRow = new StackPanel { Orientation = Orientation.Horizontal };
        _query.ToolTip = UiText.T("Game name or AppID");
        _query.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await SearchAsync(); };
        searchRow.Children.Add(_query);
        _search.Content = UiText.T("Search Online");
        _search.Click += async (_, _) => await SearchAsync();
        searchRow.Children.Add(_search);
        header.Children.Add(searchRow);
        header.Children.Add(_message);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        _add.Content = UiText.T("Add to Library");
        _add.Style = (Style)FindResource("PrimaryButton");
        _add.Click += (_, _) => SelectGame();
        footer.Children.Add(_add);
        var cancel = new Button { Content = UiText.T("Cancel"), IsCancel = true };
        cancel.Click += (_, _) => Close(); footer.Children.Add(cancel);

        _results.Columns.Add(new DataGridTextColumn { Header = "AppID", Binding = new System.Windows.Data.Binding(nameof(FoundGame.AppId)), Width = new DataGridLength(120) });
        _results.Columns.Add(new DataGridTextColumn { Header = UiText.T("Game"), Binding = new System.Windows.Data.Binding(nameof(FoundGame.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _results.SelectionChanged += (_, _) => _add.IsEnabled = !_busy && _results.SelectedItem is FoundGame;
        root.Children.Add(_results);
        if (!offline) Loaded += async (_, _) => await RefreshServersAsync();
    }

    private async Task RefreshServersAsync()
    {
        if (_busy) return;
        _busy = true; _refresh.IsEnabled = false; _search.IsEnabled = false; _add.IsEnabled = false;
        foreach (var (name, state) in _serverStates) state.Text = name + ": " + UiText.T("Checking…");
        try
        {
            foreach (var server in await _finder.CheckServersAsync())
                _serverStates[server.Name].Text = server.Name + ": " + UiText.T(server.Online ? "Online" : "Offline");
        }
        catch (Exception ex) { _message.Text = UiText.T(ex.Message); }
        finally { _busy = false; _refresh.IsEnabled = true; _search.IsEnabled = true; _add.IsEnabled = _results.SelectedItem is FoundGame; }
    }

    private async Task SearchAsync()
    {
        if (_busy) return;
        _busy = true; _refresh.IsEnabled = false; _search.IsEnabled = false; _add.IsEnabled = false;
        _message.Text = UiText.T("Searching…");
        try
        {
            var games = await _finder.SearchAsync(_query.Text);
            _results.ItemsSource = games;
            _message.Text = games.Count == 0 ? UiText.T("No games found.") : string.Format(UiText.T("{0} games found."), games.Count);
        }
        catch (Exception ex) { _results.ItemsSource = null; _message.Text = UiText.T(ex.Message); }
        finally { _busy = false; _refresh.IsEnabled = true; _search.IsEnabled = true; _add.IsEnabled = _results.SelectedItem is FoundGame; }
    }

    private void SelectGame()
    {
        if (_busy || _results.SelectedItem is not FoundGame game) return;
        SelectedAppId = game.AppId;
        SelectedName = game.Name;
        DialogResult = true;
    }
}
