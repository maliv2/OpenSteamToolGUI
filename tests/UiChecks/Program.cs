using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenSteamToolGUI;
using OpenSteamToolGUI.Core;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/OpenSteamToolGUI;component/Theme.xaml", UriKind.Relative) });
        if (args.Contains("--diagnose-library"))
        {
            var diagnosticRoot = Path.Combine(Path.GetTempPath(), "ost-library-diagnostic-" + Guid.NewGuid().ToString("N"));
            try
            {
                var sourceBackups = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSteamToolGUI", "backups");
                var destinationBackups = Path.Combine(diagnosticRoot, "backups");
                Directory.CreateDirectory(destinationBackups);
                if (Directory.Exists(sourceBackups))
                    foreach (var recordFile in Directory.EnumerateFiles(sourceBackups, "record.json", SearchOption.AllDirectories))
                    {
                        var record = System.Text.Json.JsonSerializer.Deserialize<BackupRecord>(File.ReadAllText(recordFile));
                        if (record?.Completed != true) continue;
                        var destination = Path.Combine(destinationBackups, record.Id);
                        Directory.CreateDirectory(destination);
                        File.Copy(recordFile, Path.Combine(destination, "record.json"));
                    }
                var diagnosticStorage = new Storage(diagnosticRoot);
                diagnosticStorage.SavePreferences(new AppPreferences { Language = "tr", CheckUpdates = false });
                var detected = SteamLocator.Detect();
                Console.WriteLine("Detected Steam: " + (detected?.Root ?? "none"));
                Console.WriteLine("Scanned Lua: " + (detected is null ? 0 : LuaAnalyzer.Scan(detected, diagnosticStorage).Count));
                var diagnosticWindow = new MainWindow(diagnosticStorage) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
                diagnosticWindow.Show();
                ((ListBox)diagnosticWindow.FindName("Nav")).SelectedIndex = 1;
                diagnosticWindow.UpdateLayout();
                app.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                diagnosticWindow.UpdateLayout();
                var grid = (DataGrid)diagnosticWindow.FindName("LibraryGrid");
                Console.WriteLine("Displayed library rows: " + grid.Items.Count);
                Console.WriteLine("Library message: " + ((TextBlock)diagnosticWindow.FindName("LibraryInfo")).Text);
                var search = (TextBox)diagnosticWindow.FindName("LibrarySearch");
                int originalCount = grid.Items.Count;
                search.Text = "__no_matching_game__";
                if (grid.Items.Count != 0) throw new Exception("Library search did not filter rows");
                ((Button)diagnosticWindow.FindName("RefreshLibraryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (grid.Items.Count != originalCount) throw new Exception("Refresh did not restore the full library");
                var diagnosticImage = new RenderTargetBitmap((int)diagnosticWindow.ActualWidth, (int)diagnosticWindow.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                diagnosticImage.Render(diagnosticWindow);
                var diagnosticEncoder = new PngBitmapEncoder(); diagnosticEncoder.Frames.Add(BitmapFrame.Create(diagnosticImage));
                var diagnosticPath = Path.GetFullPath("tests/UiChecks/renders/live-library-readonly.png");
                Directory.CreateDirectory(Path.GetDirectoryName(diagnosticPath)!);
                using (var diagnosticFile = File.Create(diagnosticPath)) diagnosticEncoder.Save(diagnosticFile);
                Console.WriteLine("Render: " + diagnosticPath);
                diagnosticWindow.Close(); app.Shutdown();
            }
            finally { if (Directory.Exists(diagnosticRoot)) Directory.Delete(diagnosticRoot, true); }
            return;
        }
        var root = Path.Combine(Path.GetTempPath(), "ost-ui-" + Guid.NewGuid().ToString("N"));
        var storage = new Storage(root); storage.SavePreferences(new AppPreferences { Language = "tr", CheckUpdates = false });
        var window = new MainWindow(storage, offline: true) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        window.Show();
        window.UpdateLayout();
        var firstDashboardButton = (Button)window.FindName("ChooseSteamButton");
        var lastDashboardButton = (Button)window.FindName("DebugInstallButton");
        var steamActionButton = (Button)window.FindName("SteamActionButton");
        if (firstDashboardButton.ActualHeight > 42 || Math.Abs(firstDashboardButton.TranslatePoint(new Point(), window).Y - lastDashboardButton.TranslatePoint(new Point(), window).Y) > 1)
            throw new Exception("Dashboard actions are oversized or wrap onto another line");
        if (steamActionButton.ActualWidth > 220 || steamActionButton.ActualHeight > 42 ||
            steamActionButton.TranslatePoint(new Point(), window).X <= ((TextBlock)window.FindName("SteamStateDisplay")).TranslatePoint(new Point(), window).X)
            throw new Exception("Steam action is not compact and aligned to the installation card's right side");
        var nav = (ListBox)window.FindName("Nav");
        var appearance = (ComboBox)window.FindName("AppearanceBox");
        var language = (ComboBox)window.FindName("LanguageBox");
        ((DataGrid)window.FindName("ImportGrid")).ItemsSource = new[] {
            new ImportFile { Game = "Örnek oyun", Confidence = "Assigned by user", Kind = "Lua", ArchivePath = "oyun/123.lua", TargetPath = "Steam/config/lua/123.lua", Action = ImportAction.Add },
            new ImportFile { Game = "Unidentified package", Confidence = "Unknown", Include = false, Kind = "Manifest", Action = ImportAction.KeepExisting }
        };
        ((DataGrid)window.FindName("ImportGrid")).SelectedIndex = 0;
        var output = Path.GetFullPath("tests/UiChecks/renders"); Directory.CreateDirectory(output);
        void Render(Window target, string name)
        {
            target.UpdateLayout(); app.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var bitmap = new RenderTargetBitmap((int)target.ActualWidth, (int)target.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(target); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        }
        foreach (var theme in new[] { "Dark", "Light" })
        {
            appearance.SelectedValue = theme;
            if (storage.LoadPreferences().Appearance != theme) throw new Exception("Theme persistence failed");
            for (int i = 0; i < 7; i++) { nav.SelectedIndex = i; Render(window, theme + "-" + i); }
            appearance.IsDropDownOpen = true;
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            if (!appearance.IsDropDownOpen) throw new Exception("Theme popup failed");
            appearance.IsDropDownOpen = false;
            app.Dispatcher.BeginInvoke(() => {
                var dialog = app.Windows.OfType<Window>().First(x => x != window);
                Render(dialog, theme + "-confirmation"); dialog.Close();
            }, DispatcherPriority.ContextIdle);
            if (MessageDialog.Show(window, "Restore this operation? Files changed since the backup will block restoration.", "Restore backup", MessageBoxButton.YesNo) != MessageBoxResult.No)
                throw new Exception("Closing confirmation must decline");
            foreach (var dialog in new Window[] { new GameSetupWindow(), new AppIdWindow(), new TextEditorWindow("Lua", "-- Deneme\naddappid(123)") })
            {
                dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -10000; dialog.Top = -10000; dialog.ShowActivated = false;
                dialog.Show(); Render(dialog, theme + "-" + dialog.GetType().Name);
                if (dialog is AppIdWindow && dialog.Content is StackPanel appIdContent)
                {
                    var action = appIdContent.Children.OfType<Button>().Single();
                    if (action.TranslatePoint(new Point(0, action.ActualHeight), dialog).Y > dialog.ActualHeight - 8)
                        throw new Exception("AppID dialog action is clipped");
                }
                dialog.Close();
            }
        }
        ((TextBox)window.FindName("LuaPaths")).Text = "UNSAVED";
        language.SelectedIndex = 0;
        if (((Button)window.FindName("InstallButton")).Content?.ToString() != "Install / Update") throw new Exception("English switch failed");
        if (steamActionButton.Content?.ToString() is not ("Start Steam" or "Restart Steam")) throw new Exception("English Steam action translation failed");
        language.SelectedIndex = 1;
        if (((Button)window.FindName("InstallButton")).Content?.ToString() != "Kur / Güncelle") throw new Exception("Turkish switch failed");
        if (steamActionButton.Content?.ToString() is not ("Steam'i Başlat" or "Steam'i Yeniden Başlat")) throw new Exception("Turkish Steam action translation failed");
        if (((Button)window.FindName("RemoveGameButton")).Content?.ToString() != "Paketi Sil") throw new Exception("Remove action translation failed");
        if (((TextBox)window.FindName("LuaPaths")).Text != "UNSAVED") throw new Exception("Language switching lost edits");
        if (UiText.T("SkipIdentical") != "Aynı dosyayı atla") throw new Exception("Import translation failed");
        window.Width = 1000; window.Height = 680; nav.SelectedIndex = 3; Render(window, "compact-config");
        window.Close();
        var fakeSteam = Path.Combine(root, "fake-steam");
        var luaFolder = Path.Combine(fakeSteam, "config", "lua");
        var appsFolder = Path.Combine(fakeSteam, "steamapps");
        Directory.CreateDirectory(luaFolder); Directory.CreateDirectory(appsFolder);
        File.WriteAllText(Path.Combine(fakeSteam, "steam.exe"), "fake");
        foreach (var id in new[] { 101, 202 })
        {
            File.WriteAllText(Path.Combine(luaFolder, id + ".lua"), $"addappid({id})");
            File.WriteAllText(Path.Combine(appsFolder, $"appmanifest_{id}.acf"), $"\"name\" \"Test game {id}\"");
        }
        var populatedPreferences = storage.LoadPreferences(); populatedPreferences.SteamPath = fakeSteam;
        storage.SavePreferences(populatedPreferences);
        var populated = new MainWindow(storage) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        populated.Show();
        ((ListBox)populated.FindName("Nav")).SelectedIndex = 1;
        populated.UpdateLayout(); app.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var populatedGrid = (DataGrid)populated.FindName("LibraryGrid");
        if (populatedGrid.Items.Count != 2 || populatedGrid.Columns[0].ActualWidth < 100) throw new Exception("Populated library did not render");
        ((TextBox)populated.FindName("LibrarySearch")).Text = "no match";
        if (populatedGrid.Items.Count != 0) throw new Exception("Library search did not filter");
        ((Button)populated.FindName("RefreshLibraryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (populatedGrid.Items.Count != 2) throw new Exception("Library Refresh did not show all packages");
        Render(populated, "populated-library");
        populated.Close(); app.Shutdown(); Directory.Delete(root, true);
        Console.WriteLine("PASS: 7 pages in both themes, auxiliary windows, dropdown, preference persistence, language round trip, unsaved edits and compact layout. Renders: " + output);
    }
}
