using Microsoft.Win32;
using OpenSteamToolGUI.Core;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenSteamToolGUI;

public partial class MainWindow : Window
{
    private readonly Storage _storage;
    private readonly AppPreferences _preferences;
    private SteamInstallation? _steam;
    private ImportPlan? _import;
    private ReleaseInfo? _latest;
    private string? _releaseError;
    private readonly ConfigService _configService = new();
    private readonly ReleaseService _releases = new();
    private readonly AppUpdateService _appUpdates = new();
    private readonly GameFinderService _gameFinder = new();
    private AppUpdate? _availableAppUpdate;
    private bool _appUpdateBusy;
    private string _appUpdateStatusKey = "App version: ";
    private string _appUpdateStatusDetail = "";
    private readonly GameNames _gameNames;
    private readonly HashSet<uint> _pendingGameNames = [];
    private readonly HashSet<uint> _failedGameNames = [];
    private readonly SemaphoreSlim _gameNameRequests = new(4);
    private bool _initializing = true;
    private bool _steamActionInProgress;
    private bool _managedActionInProgress;
    private bool _onlineGameBusy;
    private readonly DispatcherTimer _steamStateTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Dictionary<DataGridColumn, string> _originalHeaders = [];
    private readonly string[] _pages = ["Dashboard", "Library", "Game Search", "Import", "OpenSteamTool Settings", "Backups", "Diagnostics", "App Settings"];

    public MainWindow(Storage? storage = null, bool offline = false)
    {
        _storage = storage ?? new Storage();
        InitializeComponent();
        GameSearchPage.Initialize(_gameFinder);
        GameSearchPage.GameChosen += game => _ = StageOnlineGameAsync(game.AppId, game.Name);
        var cellTextStyle = new Style(typeof(TextBlock));
        cellTextStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap));
        cellTextStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        cellTextStyle.Setters.Add(new Setter(TextBlock.ToolTipProperty, new System.Windows.Data.Binding("Text") { RelativeSource = System.Windows.Data.RelativeSource.Self }));
        foreach (var column in new[] { LibraryGrid, ImportGrid, BackupsGrid }.SelectMany(grid => grid.Columns).OfType<DataGridTextColumn>())
            column.ElementStyle = cellTextStyle;
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        _steamStateTimer.Tick += (_, _) => { if (DashboardPage.Visibility == Visibility.Visible && !_steamActionInProgress) RefreshSteamState(); };
        Loaded += (_, _) => { _steamStateTimer.Start(); if (!offline) GameSearchPage.StartServerMonitoring(); };
        Closed += (_, _) => { _steamStateTimer.Stop(); GameSearchPage.StopServerCheck(); SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged; _gameFinder.Dispose(); };
        _preferences = _storage.LoadPreferences();
        _gameNames = new GameNames(_storage);
        _steam = offline ? null : SteamLocator.Detect(_preferences.SteamPath);
        if (_steam is not null) _gameNames.ReadLocal(_steam);
        LogLevel.SelectedValuePath = "Key"; LogLevel.DisplayMemberPath = "Value";
        ManifestProvider.ItemsSource = new[] { "opensteamtool", "steamrun", "wudrm" };
        LibraryFilter.ItemsSource = new[] { "All", "Active", "Inactive" }; LibraryFilter.SelectedIndex = 0;
        AppearanceBox.SelectedValuePath = "Key"; AppearanceBox.DisplayMemberPath = "Value";
        if (_preferences.Appearance == "Fluent") _preferences.Appearance = "Light";
        LanguageBox.ItemsSource = LanguageService.Languages.Select(x => x.Name).ToArray();
        LanguageBox.SelectedIndex = Array.FindIndex(LanguageService.Languages, x => x.Code == _preferences.Language);
        if (LanguageBox.SelectedIndex < 0) LanguageBox.SelectedIndex = 0;
        AutoUpdateCheck.IsChecked = _preferences.CheckUpdates;
        RetentionBox.Text = _preferences.BackupRetention.ToString();
        _initializing = false;
        ApplyLanguage(); ApplyAppearance();
        Nav.SelectedIndex = 0;
        RefreshAll();
        ShowAppUpdateStatus("App version: ", typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
        if (_preferences.CheckUpdates && !offline) { _ = CheckLatestAsync(); _ = CheckAppUpdateAsync(true); }
    }

    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_preferences.Appearance == "System") Dispatcher.Invoke(ApplyAppearance);
    }

    private void RefreshAll()
    {
        RefreshSummary();
        RefreshLibrary(); RefreshBackups(); LoadConfig();
    }
    private void RefreshSummary()
    {
        SteamPathDisplay.Text = _steam is null ? UiText.T("Steam folder: not found") : UiText.T("Steam folder: ") + _steam.Root;
        RefreshSteamState();
        ToolVersionDisplay.Text = "OpenSteamTool: " + (string.IsNullOrWhiteSpace(_preferences.InstalledVersion) ? UiText.T("not managed") : _preferences.InstalledVersion + " (" + UiText.T(_preferences.InstalledChannel) + ")" + (new Installer(_storage, _preferences).IsDisabled ? " — " + UiText.T("disabled") : ""));
        ReleaseDisplay.Text = _releaseError is not null ? UiText.T("Release check failed: ") + UiText.T(_releaseError)
            : _latest is null ? UiText.T("Latest release: checking…") : UiText.T("Latest release: ") + _latest.Version + " (" + UiText.T(_latest.Channel) + ")";
        DashboardHint.Text = UiText.T("Lua: <Steam>/config/lua  •  Manifests: <Steam>/depotcache. Changes to Lua/config are watched by OpenSteamTool; an applied status cannot be confirmed by this app.");
        UninstallHint.Text = UiText.T("Uninstall restores the original backed-up DLLs and leaves your games, Lua files, manifests and settings intact.");
    }
    private void RefreshSteamState()
    {
        bool running = SteamLocator.IsRunning();
        SteamStateDisplay.Text = UiText.T("Steam: ") + UiText.T(running ? "running" : "closed");
        SteamActionButton.Content = UiText.T(running ? "Restart Steam" : "Start Steam");
        SteamActionButton.IsEnabled = _steam is { IsValid: true } && !_steamActionInProgress && !_managedActionInProgress;
        bool disabled = new Installer(_storage, _preferences).IsDisabled;
        ToolActionButton.Content = UiText.T(disabled ? "Enable OpenSteamTool" : "Disable OpenSteamTool");
        ToolActionButton.IsEnabled = _steam is { IsValid: true } && _preferences.OwnedFiles.Count == 3 && !_managedActionInProgress;
        ToolActionButton.ToolTip = null;
        InstallButton.IsEnabled = RepairButton.IsEnabled = DebugInstallButton.IsEnabled = UninstallButton.IsEnabled = RestoreBackupButton.IsEnabled = !_managedActionInProgress;
    }
    private async void ToolAction_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedSteam() || _managedActionInProgress) return;
        bool enable = new Installer(_storage, _preferences).IsDisabled;
        bool wasRunning = SteamLocator.IsRunning();
        if (wasRunning && MessageDialog.Show(this, UiText.T(enable
                ? "Close Steam, enable OpenSteamTool, then restart Steam?"
                : "Close Steam, disable OpenSteamTool, then restart Steam?") + SteamClosureNotice(),
                UiText.T(enable ? "Enable OpenSteamTool" : "Disable OpenSteamTool"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SetManagedBusy(true);
        try
        {
            await ManagedSteamLifecycle().RunAsync(wasRunning, () => Task.Run(() => new Installer(_storage, _preferences).SetEnabled(_steam!, enable)));
            SetStatus(UiText.T(enable
                ? wasRunning ? "OpenSteamTool enabled. Steam restart requested." : "OpenSteamTool enabled. Start Steam to load the DLLs."
                : wasRunning ? "OpenSteamTool disabled. Steam restart requested." : "OpenSteamTool disabled. Start Steam to apply the change."));
            RefreshAll();
        }
        catch (Exception ex) { Error(ex); }
        finally { SetManagedBusy(false); }
    }
    private void SetManagedBusy(bool busy) { _managedActionInProgress = busy; RefreshSteamState(); }
    private static string SteamClosureNotice() => "\n\n" + UiText.T("If Steam remains open after 3 seconds, it will be force closed.");
    private SteamLifecycle ManagedSteamLifecycle() => new(SteamLocator.IsRunning,
        async () => { SetStatus(UiText.T("Waiting for Steam to close…")); await SteamProcessManager.StopAsync(_steam!); },
        () => SteamProcessManager.Start(_steam!));
    private void ShowAppUpdateStatus(string key, string detail = "")
    {
        _appUpdateStatusKey = key;
        _appUpdateStatusDetail = detail;
        AppUpdateStatus.Text = UiText.T(key) + UiText.T(detail);
    }
    private async void SteamAction_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedSteam() || _steamActionInProgress || _managedActionInProgress) return;
        bool running = SteamLocator.IsRunning();
        if (running && MessageDialog.Show(this, UiText.T("Close Steam and start it again? Running games may be interrupted.") + SteamClosureNotice(), UiText.T("Restart Steam"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _steamActionInProgress = true;
        SteamActionButton.IsEnabled = false;
        try
        {
            if (running)
            {
                SetStatus(UiText.T("Waiting for Steam to close…"));
                await SteamProcessManager.StopAsync(_steam!);
            }
            SteamProcessManager.Start(_steam!);
            SetStatus(UiText.T(running ? "Steam restart requested." : "Steam start requested."));
        }
        catch (Exception ex) { Error(ex); }
        finally { _steamActionInProgress = false; RefreshSteamState(); }
    }
    private void SetStatus(string status) => StatusText.Text = status;
    private void Error(Exception ex) { var message = UiText.T(ex.Message); SetStatus(message); MessageDialog.Show(this, message, "OpenSteamTool GUI", MessageBoxButton.OK, MessageBoxImage.Error); }
    private bool NeedSteam()
    {
        if (_steam is { IsValid: true }) return true;
        MessageDialog.Show(this, UiText.T("Choose a Steam folder containing steam.exe first."), "OpenSteamTool GUI");
        return false;
    }
    private void ChooseSteam_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = UiText.T("Choose Steam installation folder") };
        if (dialog.ShowDialog(this) != true) return;
        var selected = new SteamInstallation(dialog.FolderName);
        if (!selected.IsValid) { MessageDialog.Show(this, UiText.T("The selected folder does not contain steam.exe.")); return; }
        _steam = selected; _preferences.SteamPath = selected.Root; _storage.SavePreferences(_preferences);
        _gameNames.ReadLocal(selected); RefreshAll();
    }
    private async Task CheckLatestAsync(string channel = "Release")
    {
        try { _latest = await _releases.LatestAsync(channel); _releaseError = null; ReleaseDisplay.Text = UiText.T("Latest release: ") + _latest.Version + " (" + UiText.T(channel) + ")"; SetStatus(UiText.T("Latest release found.")); }
        catch (Exception ex) { _releaseError = ex.Message; ReleaseDisplay.Text = UiText.T("Release check failed: ") + UiText.T(ex.Message); SetStatus(UiText.T(ex.Message)); }
    }
    private async void CheckRelease_Click(object sender, RoutedEventArgs e) => await CheckLatestAsync();
    private async void CheckAppUpdate_Click(object sender, RoutedEventArgs e) => await CheckAppUpdateAsync(false);
    private async Task CheckAppUpdateAsync(bool onLaunch)
    {
        if (_appUpdateBusy) return;
        _appUpdateBusy = true;
        CheckAppUpdateButton.IsEnabled = false;
        try
        {
            ShowAppUpdateStatus("Checking app updates…");
            _availableAppUpdate = await _appUpdates.CheckAsync();
            InstallAppUpdateButton.IsEnabled = _availableAppUpdate is not null;
            if (_availableAppUpdate is null) ShowAppUpdateStatus("The app is up to date.");
            else ShowAppUpdateStatus("App update available: ", _availableAppUpdate.Tag);
            if (onLaunch && _availableAppUpdate is not null &&
                MessageDialog.Show(this, UiText.T("App update available: ") + _availableAppUpdate.Tag + "\n" + UiText.T("Install and restart now?"),
                    UiText.T("App update"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                await InstallAppUpdateAsync();
        }
        catch (Exception ex) { ShowAppUpdateStatus("App update check failed: ", ex.Message); }
        finally { _appUpdateBusy = false; CheckAppUpdateButton.IsEnabled = true; }
    }
    private async void InstallAppUpdate_Click(object sender, RoutedEventArgs e) => await InstallAppUpdateAsync();
    private async Task InstallAppUpdateAsync()
    {
        if (_availableAppUpdate is null) return;
        InstallAppUpdateButton.IsEnabled = false;
        string? stage = null;
        try
        {
            ShowAppUpdateStatus("Downloading app update…");
            stage = await _appUpdates.StageAsync(_availableAppUpdate);
            AppUpdateService.LaunchInstaller(stage);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            if (stage is not null && Directory.Exists(stage)) Directory.Delete(stage, true);
            ShowAppUpdateStatus("App update failed: ", ex.Message);
            InstallAppUpdateButton.IsEnabled = true;
            Error(ex);
        }
    }
    private async void Install_Click(object sender, RoutedEventArgs e) => await InstallAsync("Release");
    private async void InstallDebug_Click(object sender, RoutedEventArgs e) => await InstallAsync("Debug");
    private async Task InstallAsync(string channel)
    {
        if (!NeedSteam() || _managedActionInProgress) return;
        if (new Installer(_storage, _preferences).IsDisabled) { MessageDialog.Show(this, UiText.T("Enable OpenSteamTool before updating it.")); return; }
        string? temp = null;
        SetManagedBusy(true);
        try
        {
            SetStatus(UiText.T("Checking latest release…"));
            var release = await _releases.LatestAsync(channel);
            bool replaceUntracked = new[] { "dwmapi.dll", "xinput1_4.dll", "OpenSteamTool.dll" }
                .Any(x => File.Exists(Path.Combine(_steam!.Root, x)) && !_preferences.OwnedFiles.Any(y => y.RelativePath.Equals(x, StringComparison.OrdinalIgnoreCase)));
            var progress = new Progress<int>(v => DownloadProgress.Value = v);
            SetStatus(UiText.T("Downloading ") + release.AssetName + "…");
            temp = await _releases.DownloadAsync(release, progress);
            bool wasRunning = SteamLocator.IsRunning();
            if (wasRunning || replaceUntracked)
            {
                string prompt = wasRunning
                    ? replaceUntracked
                        ? "Close Steam, back up and replace the existing untracked DLLs, install OpenSteamTool, then restart Steam? Uninstall will restore the originals."
                        : "Close Steam, install or update OpenSteamTool, then restart Steam?"
                    : "One or more target DLLs already exist and are not managed by this app. Back up and replace them? Uninstall will restore these originals.";
                if (MessageDialog.Show(this, UiText.T(prompt) + (wasRunning ? SteamClosureNotice() : ""), UiText.T(replaceUntracked ? "Review existing DLLs" : "Install / Update"),
                    MessageBoxButton.YesNo, replaceUntracked ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            }
            await ManagedSteamLifecycle().RunAsync(wasRunning,
                () => Task.Run(() => new Installer(_storage, _preferences).Install(temp, release, _steam!, replaceUntracked)));
            SetStatus(UiText.T("Installed ") + release.Version + UiText.T(wasRunning ? ". Steam restart requested." : ". Restart Steam to load the DLLs."));
            RefreshAll();
        }
        catch (Exception ex) { Error(ex); }
        finally { if (temp is not null && File.Exists(temp)) File.Delete(temp); DownloadProgress.Value = 0; SetManagedBusy(false); }
    }
    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedSteam() || _managedActionInProgress) return;
        if (_preferences.OwnedFiles.Count == 0) { MessageDialog.Show(this, UiText.T("No managed OpenSteamTool installation was found.")); return; }
        if (new Installer(_storage, _preferences).IsDisabled) { MessageDialog.Show(this, UiText.T("Enable OpenSteamTool before uninstalling it.")); return; }
        bool wasRunning = SteamLocator.IsRunning();
        string prompt = wasRunning
            ? "Close Steam, uninstall OpenSteamTool, restore the original DLLs, then restart Steam? Lua and manifest files will remain."
            : "Remove the managed OpenSteamTool DLLs and restore the originals from backup? Lua and manifest files will remain.";
        if (MessageDialog.Show(this, UiText.T(prompt) + (wasRunning ? SteamClosureNotice() : ""), "Uninstall OpenSteamTool", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SetManagedBusy(true);
        try
        {
            SetStatus(UiText.T("Restoring original files…"));
            await ManagedSteamLifecycle().RunAsync(wasRunning, () => Task.Run(() => new Installer(_storage, _preferences).Uninstall(_steam!)));
            SetStatus(UiText.T("OpenSteamTool uninstalled; original DLLs restored.")); RefreshAll();
        }
        catch (Exception ex) { Error(ex); }
        finally { SetManagedBusy(false); }
    }
    private void RefreshLibrary()
    {
        if (_steam is null) { LibraryGrid.ItemsSource = null; LibraryInfo.Text = UiText.T("Choose a Steam folder containing steam.exe first."); return; }
        var packages = LuaAnalyzer.Scan(_steam, _storage);
        var importGroups = ImportTracking.ActiveGroups(_storage, _steam);
        var bundleIds = importGroups.Where(group => group.BundleAppId is not null).Select(group => group.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var package in packages)
        {
            try { package.ImportRecordId = ImportTracking.MatchPackage(importGroups, package)?.Id; }
            catch (IOException) { package.ImportRecordId = null; }
            catch (UnauthorizedAccessException) { package.ImportRecordId = null; }
            if (package.ImportRecordId is not null && bundleIds.Contains(package.ImportRecordId)) continue;
            if (package.AppIds.Count == 1)
            {
                var id = package.AppIds[0];
                package.Name = _gameNames.Get(id);
                ResolveLibraryName(id);
            }
            else if (uint.TryParse(Path.GetFileNameWithoutExtension(package.SourcePath), out var fileId))
            {
                package.Name = UiText.T("Package ") + _gameNames.Get(fileId);
                ResolveLibraryName(fileId);
            }
        }
        foreach (var group in importGroups.Where(group => group.BundleAppId is not null))
        {
            var members = packages.Where(package => package.ImportRecordId == group.Id).ToList();
            packages.RemoveAll(package => package.ImportRecordId == group.Id);
            bool anyActive = members.Any(member => member.Enabled);
            bool anyInactive = members.Any(member => !member.Enabled);
            packages.Add(new GamePackage
            {
                Name = string.Format(UiText.T("Package: {0}"), group.BundleName ?? ImportTracking.SourceName(group)),
                SourcePath = members.FirstOrDefault()?.SourcePath ?? group.Operations[0].TargetPath,
                OriginalPath = group.Operations[0].TargetPath,
                AppIds = group.BundleAppIds.Count > 0 ? group.BundleAppIds : members.SelectMany(member => member.AppIds).Distinct().ToList(),
                Enabled = anyActive,
                ImportOnly = members.Count == 0,
                IsBundle = true,
                ImportRecordId = group.Id,
                StatusOverride = anyActive && anyInactive ? "Mixed" : null,
                PathOverride = string.Format(UiText.T("{0} files in package"), group.Operations.Count)
            });
        }
        foreach (var group in importGroups.Where(group => !packages.Any(package => package.ImportRecordId == group.Id)))
            packages.Add(new GamePackage { Name = UiText.T("Import package: ") + ImportTracking.SourceName(group), SourcePath = group.Operations[0].TargetPath, OriginalPath = group.Operations[0].TargetPath, ImportOnly = true, ImportRecordId = group.Id });
        var items = packages.AsEnumerable();
        string search = LibrarySearch.Text.Trim();
        if (search.Length > 0) items = items.Where(x => x.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || x.Summary.Contains(search, StringComparison.OrdinalIgnoreCase) || x.SourcePath.Contains(search, StringComparison.OrdinalIgnoreCase));
        if (LibraryFilter.SelectedIndex == 1) items = items.Where(x => x.Enabled);
        if (LibraryFilter.SelectedIndex == 2) items = items.Where(x => !x.Enabled && !x.ImportOnly);
        var list = items.ToList(); LibraryGrid.ItemsSource = list;
        LibraryInfo.Text = list.Count == 0
            ? packages.Count == 0 ? UiText.T("No Lua packages were found in this Steam folder.") : UiText.T("No packages match the current search or filter. Press Refresh to show all packages.")
            : string.Format(UiText.T("{0} packages • {1} active. Shared Lua files affect every listed ID."), list.Count, list.Count(x => x.Enabled));
    }
    private void ResolveLibraryName(uint id)
    {
        if (_gameNames.HasName(id) || _failedGameNames.Contains(id) || !_pendingGameNames.Add(id)) return;
        _ = ResolveLibraryNameAsync(id);
    }
    private async Task ResolveLibraryNameAsync(uint id)
    {
        try
        {
            await _gameNameRequests.WaitAsync();
            try { await _gameNames.ResolveAsync(id); }
            finally { _gameNameRequests.Release(); }
            if (_gameNames.HasName(id)) RefreshLibrary();
            else _failedGameNames.Add(id);
        }
        catch { _failedGameNames.Add(id); }
        finally { _pendingGameNames.Remove(id); }
    }
    private void RefreshLibrary_Click(object sender, RoutedEventArgs e)
    {
        _failedGameNames.Clear();
        _initializing = true;
        try { LibrarySearch.Clear(); LibraryFilter.SelectedIndex = 0; }
        finally { _initializing = false; }
        RefreshLibrary();
    }
    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedSteam()) return;
        var dialog = new GameSetupWindow { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            string path = Path.Combine(_steam!.LuaDirectory, dialog.AppId + ".lua");
            if (File.Exists(path)) throw new IOException(UiText.T("A Lua file for this AppID already exists. Open it from the library instead."));
            new FileTransaction(_storage).Apply("Create game " + dialog.AppId, [(path, (byte[]?)Encoding.UTF8.GetBytes(dialog.Script))], steamRoot: _steam.Root);
            RefreshLibrary(); SetStatus(UiText.T("Game Lua created. OpenSteamTool will load it from the watched folder."));
        }
        catch (Exception ex) { Error(ex); }
    }
    private void LibrarySearch_TextChanged(object sender, RoutedEventArgs e) { if (!_initializing) RefreshLibrary(); }
    private void ToggleGame_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedSteam()) return;
        var selected = LibraryGrid.SelectedItems.OfType<GamePackage>().ToList(); if (selected.Count == 0) return;
        if (selected.Any(x => !x.IsBundle && x.AppIds.Count > 1) && MessageDialog.Show(this, UiText.T("Some scripts contain multiple App IDs. Toggle the whole scripts?"), UiText.T("Shared packages"), MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (selected.Any(x => x.ImportOnly)) { MessageDialog.Show(this, UiText.T("Select a Lua file to change its state.")); return; }
        try
        {
            foreach (var item in selected)
            {
                if (item.IsBundle)
                {
                    var group = ImportTracking.ActiveGroups(_storage, _steam!).Single(record => record.Id == item.ImportRecordId);
                    ImportTracking.ToggleBundle(_storage, _steam!, group);
                }
                else LuaAnalyzer.Toggle(item, _steam!, _storage);
            }
            RefreshLibrary(); SetStatus(UiText.T("Lua package state changed. Steam may need time to reload."));
        }
        catch (Exception ex) { Error(ex); }
    }
    private void EditGame_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryGrid.SelectedItem is not GamePackage package) return;
        if (package.ImportOnly) { MessageDialog.Show(this, UiText.T("Select a Lua file to edit.")); return; }
        try
        {
            string original = File.ReadAllText(package.SourcePath); string hash = FileTools.HashFile(package.SourcePath);
            var editor = new TextEditorWindow(UiText.T("Edit Lua — ") + Path.GetFileName(package.SourcePath), original) { Owner = this };
            if (editor.ShowDialog() != true) return;
            if (FileTools.HashFile(package.SourcePath) != hash) throw new IOException(UiText.T("Lua file changed outside the app. Reopen it before saving."));
            new FileTransaction(_storage).Apply("Edit Lua " + Path.GetFileName(package.SourcePath), [(package.SourcePath, (byte[]?)Encoding.UTF8.GetBytes(editor.EditedText))], steamRoot: _steam?.Root);
            if (_steam is not null) ImportTracking.UpdateManagedHash(_storage, _steam, package.OriginalPath, hash, FileTools.HashFile(package.SourcePath));
            RefreshLibrary(); SetStatus(UiText.T("Lua saved. OpenSteamTool will reload watched files."));
        }
        catch (Exception ex) { Error(ex); }
    }
    private void FindOnline_Click(object sender, RoutedEventArgs e) => Nav.SelectedIndex = 2;

    private async Task StageOnlineGameAsync(uint appId, string gameName)
    {
        if (_onlineGameBusy || !NeedSteam()) return;
        _onlineGameBusy = true;
        FindOnlineButton.IsEnabled = false;
        try
        {
            SetStatus(UiText.T("Downloading game files…"));
            var dlcIds = await _gameFinder.GetDlcIdsAsync(appId);
            var plan = await DownloadGamePlanAsync(appId);
            plan.Source = gameName + " (" + appId + ")";
            plan.BundleAppId = appId;
            plan.BundleName = gameName;
            plan.BundleAppIds.Add(appId);
            foreach (var file in plan.Files.Where(file => file.AppId is not null))
                file.Game = file.AppId == appId ? gameName : _gameNames.Get(file.AppId!.Value);
            var unavailable = new List<uint>();
            for (int index = 0; index < dlcIds.Count; index++)
            {
                uint dlcId = dlcIds[index];
                SetStatus(string.Format(UiText.T("Downloading DLC {0} of {1}…"),
                    index + 1, dlcIds.Count));
                ImportPlan dlcPlan;
                try
                {
                    dlcPlan = await DownloadGamePlanAsync(dlcId);
                }
                catch (InvalidDataException) { unavailable.Add(dlcId); continue; }
                foreach (var file in dlcPlan.Files.Where(file => file.AppId is not null))
                    file.Game = file.AppId == dlcId ? _gameNames.Get(dlcId) : _gameNames.Get(file.AppId!.Value);
                ImportService.AddToPlan(plan, dlcPlan);
                plan.BundleAppIds.Add(dlcId);
            }
            _import = plan;
            ImportGrid.ItemsSource = null; ImportGrid.ItemsSource = plan.Files;
            string summary = string.Format(UiText.T("DLC found: {0}; prepared: {1}; unavailable: {2}."),
                dlcIds.Count, dlcIds.Count - unavailable.Count, unavailable.Count);
            ImportSource.Text = plan.Source + " — " + plan.Files.Count + UiText.T(" importable files") + " — " + summary;
            ImportSource.ToolTip = unavailable.Count == 0 ? null : string.Format(UiText.T("Unavailable DLC AppIDs: {0}"), string.Join(", ", unavailable));
            Nav.SelectedIndex = 3;
            SetStatus(summary + " " + UiText.T("Review games, target paths and conflicts before importing."));
        }
        catch (Exception ex) { Error(ex); }
        finally { FindOnlineButton.IsEnabled = true; _onlineGameBusy = false; }
    }

    private async Task<ImportPlan> DownloadGamePlanAsync(uint appId)
    {
        foreach (var source in new Func<uint, Task<FoundFile>>[]
        {
            id => _gameFinder.DownloadRemluaAsync(id),
            id => _gameFinder.DownloadSteamManifestAsync(id)
        })
        {
            string? temporaryDirectory = null;
            try
            {
                FoundFile found = await source(appId);
                if (Path.GetFileName(found.FileName) != found.FileName) throw new InvalidDataException("Unsafe downloaded filename.");
                temporaryDirectory = Path.Combine(Path.GetTempPath(), "ostgui-finder-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryDirectory);
                string temporary = Path.Combine(temporaryDirectory, found.FileName);
                ImportPlan plan = await Task.Run(() =>
                {
                    File.WriteAllBytes(temporary, found.Content);
                    return new ImportService(_storage).AnalyzePath(temporary, _steam!);
                });
                GameFinderService.MatchDownloadedGame(plan, appId);
                return plan;
            }
            catch (Exception) { }
            finally { if (temporaryDirectory is not null && Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true); }
        }
        throw new InvalidDataException("No verified Lua or manifest files were found for this game.");
    }
    private async void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedSteam()) return;
        if (LibraryGrid.SelectedItems.Count != 1 || LibraryGrid.SelectedItem is not GamePackage package)
        {
            MessageDialog.Show(this, UiText.T("Select one package to remove.")); return;
        }
        try
        {
            var group = package.ImportRecordId is null ? null : ImportTracking.ActiveGroups(_storage, _steam!).FirstOrDefault(x => x.Id == package.ImportRecordId);
            string? expectedHash = group is null ? FileTools.HashFile(package.SourcePath) : null;
            string message = group is null
                ? string.Format(UiText.T("Remove the entire Lua file {0}? A backup will be created."), Path.GetFileName(package.SourcePath))
                : string.Format(UiText.T("Remove all {0} files imported from {1}? Replaced originals will be restored. A backup will be created."), group.Operations.Count, ImportTracking.SourceName(group));
            if (MessageDialog.Show(this, message, UiText.T("Remove package"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetStatus(UiText.T("Removing selected package…"));
            await Task.Run(() =>
            {
                if (group is null) ImportTracking.RemoveLua(_storage, _steam!, package, expectedHash!);
                else ImportTracking.RemoveGroup(_storage, _steam!, group);
            });
            RefreshLibrary(); RefreshBackups(); SetStatus(UiText.T("Package removed. A backup is available."));
        }
        catch (Exception ex) { Error(ex); }
    }
    private void ChooseZip_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = UiText.T("Importable files (*.zip;*.lua;*.manifest)") + "|*.zip;*.lua;*.manifest" };
        if (dialog.ShowDialog(this) == true) AnalyzeZip(dialog.FileName);
    }
    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        if (new[] { ".zip", ".lua", ".manifest" }.Contains(Path.GetExtension(files[0]).ToLowerInvariant())) AnalyzeZip(files[0]);
    }
    private async void AnalyzeZip(string path)
    {
        if (!NeedSteam()) return;
        try
        {
            SetStatus(UiText.T("Analyzing ZIP…"));
            _import = await Task.Run(() => new ImportService(_storage).AnalyzePath(path, _steam!));
            foreach (var file in _import.Files.Where(x => x.AppId is not null)) file.Game = _gameNames.Get(file.AppId!.Value);
            ImportGrid.ItemsSource = null; ImportGrid.ItemsSource = _import.Files; ImportSource.Text = path + " — " + _import.Files.Count + UiText.T(" importable files"); Nav.SelectedIndex = 3; SetStatus(UiText.T("Review games, target paths and conflicts before importing."));
            foreach (var id in _import.Files.Where(x => x.AppId is not null).Select(x => x.AppId!.Value).Distinct())
            {
                string name = await _gameNames.ResolveAsync(id);
                if (_import is null) break;
                foreach (var file in _import.Files.Where(x => x.AppId == id)) file.Game = name;
                ImportGrid.Items.Refresh();
            }
        }
        catch (Exception ex) { Error(ex); }
    }
    private void ReplaceSelected_Click(object sender, RoutedEventArgs e) => ChangeImportAction(ImportAction.Replace);
    private void KeepSelected_Click(object sender, RoutedEventArgs e) => ChangeImportAction(ImportAction.KeepExisting);
    private void SelectAllImport_Click(object sender, RoutedEventArgs e) { if (_import is null) return; foreach (var file in _import.Files) file.Include = true; ImportGrid.Items.Refresh(); }
    private void SelectNoneImport_Click(object sender, RoutedEventArgs e) { if (_import is null) return; foreach (var file in _import.Files) file.Include = false; ImportGrid.Items.Refresh(); }
    private async void AssignGame_Click(object sender, RoutedEventArgs e)
    {
        var selected = ImportGrid.SelectedItems.OfType<ImportFile>().ToList(); if (selected.Count == 0) return;
        var dialog = new AppIdWindow { Owner = this };
        if (dialog.ShowDialog() != true) return;
        string name = await _gameNames.ResolveAsync(dialog.AppId);
        foreach (var file in selected) { file.AppId = dialog.AppId; file.Game = name; file.Confidence = "Assigned by user"; }
        ImportGrid.Items.Refresh();
    }
    private void ChangeImportAction(ImportAction action)
    {
        foreach (var file in ImportGrid.SelectedItems.OfType<ImportFile>()) if (file.ExistingHash.Length > 0 && file.Action != ImportAction.SkipIdentical) file.Action = action;
        ImportGrid.Items.Refresh();
    }
    private async void ApplyImport_Click(object sender, RoutedEventArgs e)
    {
        if (_import is null || !NeedSteam()) return;
        ImportGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (_import.BundleAppId is not null && _import.Files.Any(file => !file.Include || file.Action == ImportAction.KeepExisting))
        {
            MessageDialog.Show(this, UiText.T("Resolve every package conflict before importing. Select conflicting files and choose Replace Selected."));
            return;
        }
        int count = _import.Files.Count(file => file.Include &&
            (file.Action is ImportAction.Add or ImportAction.Replace || _import.BundleAppId is not null && file.Action == ImportAction.SkipIdentical));
        if (MessageDialog.Show(this, string.Format(UiText.T("Import {0} selected files?"), count), "Confirm import", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try { SetStatus(UiText.T("Importing selected files…")); await Task.Run(() => new ImportService(_storage).Apply(_import, _steam!)); SetStatus(UiText.T("Import complete. Files were saved with a rollback record.")); _import = null; ImportGrid.ItemsSource = null; RefreshAll(); }
        catch (Exception ex) { Error(ex); }
    }
    private void LoadConfig()
    {
        if (_steam is null) return;
        try
        {
            var c = _configService.Load(_steam.ConfigFile);
            LogLevel.SelectedValue = c.LogLevel; ManifestProvider.SelectedItem = c.ManifestProvider;
            ResolveTimeout.Text = c.ResolveTimeout.ToString(); ConnectTimeout.Text = c.ConnectTimeout.ToString(); SendTimeout.Text = c.SendTimeout.ToString(); ReceiveTimeout.Text = c.ReceiveTimeout.ToString();
            LuaPaths.Text = c.LuaPaths; InjectEnabled.IsChecked = c.InjectEnabled; LibraryX64.Text = c.LibraryX64; LibraryX86.Text = c.LibraryX86; RemoteTemplate.Text = c.RemoteTemplate;
            StatsEnabled.IsChecked = c.StatsApi; CloudEnabled.IsChecked = c.CloudEnabled; CloudLibrary.Text = c.CloudLibrary;
            var caps = ToolCapabilities.ForVersion(_preferences.InstalledVersion);
            StatsEnabled.IsEnabled = caps.StatsApi; CloudEnabled.IsEnabled = caps.CloudRedirect; CloudLibrary.IsEnabled = caps.CloudRedirect;
            NewerFeaturesInfo.Text = caps.StatsApi ? "" : UiText.T("Stats API and CloudRedirect are not supported by release 1.4.8. Available in newer upstream source builds.");
        }
        catch (Exception ex) { SetStatus(UiText.T("Config read failed: ") + UiText.T(ex.Message)); }
    }
    private void ReloadConfig_Click(object sender, RoutedEventArgs e) => LoadConfig();
    private void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedSteam()) return;
        try
        {
            var c = new ToolConfig { LogLevel = LogLevel.SelectedValue?.ToString() ?? "debug", ManifestProvider = ManifestProvider.SelectedItem?.ToString() ?? "wudrm", ResolveTimeout = int.Parse(ResolveTimeout.Text), ConnectTimeout = int.Parse(ConnectTimeout.Text), SendTimeout = int.Parse(SendTimeout.Text), ReceiveTimeout = int.Parse(ReceiveTimeout.Text), LuaPaths = LuaPaths.Text, InjectEnabled = InjectEnabled.IsChecked == true, LibraryX64 = LibraryX64.Text, LibraryX86 = LibraryX86.Text, RemoteTemplate = RemoteTemplate.Text, StatsApi = StatsEnabled.IsChecked == true, CloudEnabled = CloudEnabled.IsChecked == true, CloudLibrary = CloudLibrary.Text };
            _configService.Save(c, ToolCapabilities.ForVersion(_preferences.InstalledVersion), new FileTransaction(_storage));
            SetStatus(UiText.T("Configuration saved. OpenSteamTool will reload valid changes."));
        }
        catch (Exception ex) { Error(ex); }
    }
    private void RawConfig_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedSteam()) return;
        var editor = new TextEditorWindow(UiText.T("Edit opensteamtool.toml"), _configService.Raw) { Owner = this };
        if (editor.ShowDialog() != true) return;
        try { _configService.SaveRaw(editor.EditedText, new FileTransaction(_storage)); LoadConfig(); SetStatus(UiText.T("Configuration saved.")); }
        catch (Exception ex) { Error(ex); }
    }
    private void RefreshBackups() { _storage.PruneBackups(_preferences.BackupRetention, _preferences); BackupsGrid.ItemsSource = _storage.ListBackups(); }
    private void RefreshBackups_Click(object sender, RoutedEventArgs e) => RefreshBackups();
    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_managedActionInProgress || BackupsGrid.SelectedItem is not BackupRecord record) return;
        if (record.Restored) { MessageDialog.Show(this, UiText.T("This operation was already restored.")); return; }
        bool managesDlls = record.PreviousOwnedFiles is not null;
        bool wasRunning = managesDlls && SteamLocator.IsRunning();
        string prompt = wasRunning
            ? "Close Steam, restore this backup, then restart Steam? Files changed since the backup will block restoration."
            : "Restore this operation? Files changed since the backup will block restoration.";
        if (MessageDialog.Show(this, UiText.T(prompt) + (wasRunning ? SteamClosureNotice() : ""), UiText.T("Restore backup"), MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        SetManagedBusy(true);
        try
        {
            if (managesDlls)
                await ManagedSteamLifecycle().RunAsync(wasRunning, () => Task.Run(() => RestoreRecord(record)));
            else await Task.Run(() => RestoreRecord(record));
            SetStatus(UiText.T("Backup restored.")); RefreshAll();
        }
        catch (Exception ex) { Error(ex); }
        finally { SetManagedBusy(false); }
    }
    private void RestoreRecord(BackupRecord record)
    {
        if (record.PreviousOwnedFiles is not null && SteamLocator.IsRunning()) throw new IOException("Close Steam before restoring OpenSteamTool DLLs.");
        new FileTransaction(_storage).Restore(record, steamRoot: _steam?.Root);
        if (record.CompanionRemovalId is not null)
        {
            var companion = _storage.ListBackups().FirstOrDefault(x => x.Id == record.CompanionRemovalId) ?? throw new IOException("Companion backup is missing.");
            new FileTransaction(_storage).Restore(companion);
        }
        if (record.RemovedImportId is not null)
        {
            var import = _storage.ListBackups().FirstOrDefault(x => x.Id == record.RemovedImportId);
            if (import is not null) { import.ImportRemoved = false; _storage.SaveRecord(import); }
        }
        if (record.PreviousOwnedFiles is not null)
        {
            _preferences.OwnedFiles = record.PreviousOwnedFiles;
            _preferences.InstalledVersion = record.PreviousVersion ?? "";
            _preferences.InstalledChannel = record.PreviousChannel ?? "";
            _storage.SavePreferences(_preferences);
        }
    }
    private void RunDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var lines = new List<string>();
        if (_steam is null) lines.Add(UiText.T("Steam folder not found."));
        else
        {
            lines.Add(UiText.T("Steam: ") + _steam.Root); lines.Add(UiText.T("Process: ") + (SteamLocator.IsRunning() ? UiText.T("running") : UiText.T("closed")));
            foreach (var dll in new[] { "dwmapi.dll", "xinput1_4.dll", "OpenSteamTool.dll" }) lines.Add(dll + ": " + (File.Exists(Path.Combine(_steam.Root, dll)) ? UiText.T("present") : UiText.T("missing")));
            lines.Add(UiText.T("Lua folder: ") + (Directory.Exists(_steam.LuaDirectory) ? UiText.T("present") : UiText.T("missing")));
            lines.Add(UiText.T("Steam depotcache: ") + (Directory.Exists(_steam.DepotCache) ? UiText.T("present") : UiText.T("missing")));
            lines.Add(UiText.T("Legacy config/depotcache: ") + (Directory.Exists(Path.Combine(_steam.Root, "config", "depotcache")) ? UiText.T("present; review existing manifests") : UiText.T("absent")));
            try { ConfigService.ValidateSyntax(File.Exists(_steam.ConfigFile) ? File.ReadAllText(_steam.ConfigFile) : ""); lines.Add(UiText.T("Config: basic syntax valid")); } catch (Exception ex) { lines.Add(UiText.T("Config: ") + UiText.T(ex.Message)); }
            try { string temp = Path.Combine(_steam.Root, ".ostgui-write-test-" + Guid.NewGuid().ToString("N")); File.WriteAllText(temp, ""); File.Delete(temp); lines.Add(UiText.T("Write permission: available")); } catch { lines.Add(UiText.T("Write permission: elevation may be required")); }
            var folder = Path.Combine(_steam.Root, "opensteamtool"); lines.Add(UiText.T("Logs: ") + (Directory.Exists(folder) ? folder : UiText.T("not found (Debug release only)")));
        }
        DiagnosticsText.Text = string.Join(Environment.NewLine, lines); SetStatus(UiText.T("Diagnostics complete."));
    }
    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_steam is null) return; string dir = Path.Combine(_steam.Root, "opensteamtool");
        if (Directory.Exists(dir)) Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }
    private void PreferenceChanged(object sender, RoutedEventArgs e) { if (_initializing) return; _preferences.CheckUpdates = AutoUpdateCheck.IsChecked == true; _storage.SavePreferences(_preferences); }
    private void RetentionBox_LostFocus(object sender, RoutedEventArgs e) { if (int.TryParse(RetentionBox.Text, out var count) && count is >= 1 and <= 1000) { _preferences.BackupRetention = count; _storage.SavePreferences(_preferences); } else RetentionBox.Text = _preferences.BackupRetention.ToString(); }
    private void AppearanceBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_initializing) return; _preferences.Appearance = AppearanceBox.SelectedValue?.ToString() ?? "Dark"; _storage.SavePreferences(_preferences); ApplyAppearance(); }
    private void ApplyAppearance()
    {
        ThemeService.Apply(_preferences.Appearance);
        ThemeService.StyleWindow(this);
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || LanguageBox.SelectedIndex < 0) return;
        _preferences.Language = LanguageService.Languages[LanguageBox.SelectedIndex].Code; _storage.SavePreferences(_preferences); ApplyLanguage(); RefreshSummary(); RefreshLibrary(); ImportGrid.Items.Refresh(); SetStatus(UiText.T("Ready"));
        ShowAppUpdateStatus(_appUpdateStatusKey, _appUpdateStatusDetail);
        NewerFeaturesInfo.Text = ToolCapabilities.ForVersion(_preferences.InstalledVersion).StatsApi ? "" : UiText.T("Stats API and CloudRedirect are not supported by release 1.4.8. Available in newer upstream source builds.");
    }
    private void ApplyLanguage()
    {
        UiText.Language = _preferences.Language;
        GameSearchPage.RefreshLanguage();
        string T(string value) => LanguageService.T(_preferences.Language, value);
        _initializing = true;
        var level = LogLevel.SelectedValue?.ToString() ?? "debug";
        LogLevel.ItemsSource = new[] { "trace", "debug", "info", "warn", "error" }.Select(x => new KeyValuePair<string, string>(x, T(x))).ToArray();
        LogLevel.SelectedValue = level;
        var filter = Math.Max(0, LibraryFilter.SelectedIndex);
        LibraryFilter.ItemsSource = new[] { T("All"), T("Active"), T("Inactive") }; LibraryFilter.SelectedIndex = filter;
        AppearanceBox.ItemsSource = new[] { "Dark", "Light", "System" }.Select(x => new KeyValuePair<string, string>(x, T(x))).ToArray();
        AppearanceBox.SelectedValue = _preferences.Appearance;
        _initializing = false;
        int current = Nav.SelectedIndex;
        Nav.ItemsSource = _pages.Select(T).ToArray(); Nav.SelectedIndex = current < 0 ? 0 : current;
        (Button Control, string Text)[] buttons = [(ChooseSteamButton, "Choose Steam Folder"), (CheckReleaseButton, "Check Latest Release"), (InstallButton, "Install / Update"), (RepairButton, "Repair"), (DebugInstallButton, "Install Debug"), (RefreshLibraryButton, "Refresh"), (FindOnlineButton, "Find Games Online"), (AddGameButton, "Add Game"), (ToggleGameButton, "Enable / Disable"), (EditGameButton, "Edit Lua"), (RemoveGameButton, "Remove Package"), (ChooseZipButton, "Choose ZIP"), (SelectAllImportButton, "Select All"), (SelectNoneImportButton, "Select None"), (AssignGameButton, "Assign AppID"), (ApplyImportButton, "Import Selected"), (ReplaceSelectedButton, "Replace Selected"), (KeepSelectedButton, "Keep Existing"), (SaveConfigButton, "Save Settings"), (RawConfigButton, "Edit Raw TOML"), (ReloadConfigButton, "Reload"), (RefreshBackupsButton, "Refresh"), (RestoreBackupButton, "Restore Selected"), (RunDiagnosticsButton, "Run Diagnostics"), (OpenLogsButton, "Open Logs"), (SettingsSteamButton, "Choose Steam Folder"), (UninstallButton, "Uninstall OpenSteamTool"), (CheckAppUpdateButton, "Check App Update"), (InstallAppUpdateButton, "Install App Update")];
        foreach (var (control, value) in buttons) control.Content = T(value);
        TranslateStatic(this);
        PageTitle.Text = Nav.SelectedItem?.ToString() ?? "";
        foreach (var column in new[] { LibraryGrid, ImportGrid, BackupsGrid }.SelectMany(x => x.Columns))
        {
            if (!_originalHeaders.TryGetValue(column, out var original)) _originalHeaders[column] = original = column.Header?.ToString() ?? "";
            column.Header = T(original);
        }
        ImportHint.Text = UiText.T("Drop a ZIP anywhere in the window, review the games and target files, then import. Conflicts keep existing files by default.");
    }
    private void TranslateStatic(DependencyObject root)
    {
        foreach (var item in LogicalTreeHelper.GetChildren(root))
        {
            if (item is not DependencyObject child) continue;
            if (child is TextBlock block && block != PageTitle)
            {
                if (block.Tag is not string && LanguageService.IsTranslatable(block.Text)) block.Tag = block.Text;
                if (block.Tag is string original) block.Text = LanguageService.T(_preferences.Language, original);
            }
            if (child is CheckBox check)
            {
                if (check.Tag is not string && check.Content is string caption && LanguageService.IsTranslatable(caption)) check.Tag = caption;
                if (check.Tag is string original) check.Content = LanguageService.T(_preferences.Language, original);
            }
            TranslateStatic(child);
        }
    }
    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedIndex < 0 || _initializing) return;
        PageTitle.Text = Nav.SelectedItem?.ToString() ?? "";
        FrameworkElement[] views = [DashboardPage, LibraryPage, GameSearchPage, ImportPage, ConfigPage, BackupsPage, DiagnosticsPage, AppSettingsPage];
        for (int i = 0; i < views.Length; i++) views[i].Visibility = i == Nav.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
    }
}
