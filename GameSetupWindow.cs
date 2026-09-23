using OpenSteamToolGUI.Core;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace OpenSteamToolGUI;

public sealed class GameSetupWindow : Window
{
    private readonly TextBox _app = new();
    private readonly TextBox _depots = new();
    private readonly TextBox _token = new();
    private readonly TextBox _manifests = new();
    private readonly TextBox _stat = new();
    private readonly TextBox _appTicket = new();
    private readonly TextBox _eTicket = new();
    public uint AppId { get; private set; }
    public string Script { get; private set; } = "";
    public GameSetupWindow()
    {
        Title = UiText.T("Add Game"); Width = 620; Height = 650; MinWidth = 520; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner; ThemeService.StyleWindow(this);
        var root = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = UiText.T("Create Lua"), IsDefault = true };
        var cancel = new Button { Content = UiText.T("Cancel"), IsCancel = true };
        save.Click += (_, _) => Save(); cancel.Click += (_, _) => Close(); buttons.Children.Add(save); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var form = new StackPanel { Margin = new Thickness(16, 12, 16, 12) }; scroll.Content = form; root.Children.Add(scroll); Content = root;
        Add(form, UiText.T("Game AppID"), _app, UiText.T("Example: 1361510"));
        Add(form, UiText.T("Depot IDs and optional 64-character hex keys"), _depots, UiText.T("One per line: depotId,key (key optional)"), 90);
        Add(form, UiText.T("Access token (optional uint64)"), _token, UiText.T("Decimal digits"));
        Add(form, UiText.T("Manifest overrides"), _manifests, UiText.T("One per line: depotId,manifestGID"), 90);
        Add(form, UiText.T("Stats SteamID (optional uint64)"), _stat, UiText.T("Decimal digits"));
        Add(form, UiText.T("AppTicket (optional hex)"), _appTicket, UiText.T("Ticket from an account that owns the game"));
        Add(form, UiText.T("ETicket (optional hex)"), _eTicket, UiText.T("Ticket from an account that owns the game"));
        form.Children.Add(new TextBlock { Text = UiText.T("SteamStub may use a local ticket automatically. Denuvo games need valid explicit ticket data. This form creates a new Lua file; edit advanced Lua callbacks in the raw editor."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
    }
    private static void Add(Panel panel, string label, TextBox box, string hint, int height = 0)
    {
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 7, 0, 1) });
        panel.Children.Add(new TextBlock { Text = hint, Style = (Style)Application.Current.FindResource("MutedText") });
        if (height > 0) { box.Height = height; box.AcceptsReturn = true; box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
        panel.Children.Add(box);
    }
    private void Save()
    {
        try
        {
            if (!uint.TryParse(_app.Text.Trim(), out var app) || app == 0) throw new InvalidDataException(UiText.T("Enter a valid positive AppID."));
            AppId = app;
            var lines = new List<string> { "-- Created by OpenSteamTool GUI", $"addappid({app})" };
            foreach (var raw in _depots.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = raw.TrimEnd('\r').Split(',', 2, StringSplitOptions.TrimEntries);
                if (!uint.TryParse(parts[0], out var depot) || depot == 0) throw new InvalidDataException(UiText.T("Invalid depot ID: ") + parts[0]);
                if (parts.Length == 1 || parts[1].Length == 0) lines.Add($"addappid({depot})");
                else { ValidateHex(parts[1], 64, UiText.T("Depot key")); lines.Add($"addappid({depot}, 0, \"{parts[1]}\")"); }
            }
            if (_token.Text.Trim().Length > 0) { if (!ulong.TryParse(_token.Text.Trim(), out _)) throw new InvalidDataException(UiText.T("Invalid access token.")); lines.Add($"addtoken({app}, \"{_token.Text.Trim()}\")"); }
            foreach (var raw in _manifests.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = raw.TrimEnd('\r').Split(',', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !uint.TryParse(parts[0], out var depot) || !ulong.TryParse(parts[1], out _)) throw new InvalidDataException(UiText.T("Manifest lines must be depotId,manifestGID."));
                lines.Add($"setManifestid({depot}, \"{parts[1]}\")");
            }
            if (_stat.Text.Trim().Length > 0) { if (!ulong.TryParse(_stat.Text.Trim(), out _)) throw new InvalidDataException(UiText.T("Invalid SteamID.")); lines.Add($"setStat({app}, \"{_stat.Text.Trim()}\")"); }
            if (_appTicket.Text.Trim().Length > 0) { ValidateHex(_appTicket.Text.Trim(), null, "AppTicket"); lines.Add($"setAppTicket({app}, \"{_appTicket.Text.Trim()}\")"); }
            if (_eTicket.Text.Trim().Length > 0) { ValidateHex(_eTicket.Text.Trim(), null, "ETicket"); lines.Add($"setETicket({app}, \"{_eTicket.Text.Trim()}\")"); }
            Script = string.Join("\n", lines) + "\n";
            DialogResult = true; Close();
        }
        catch (Exception ex) { MessageDialog.Show(this, ex.Message, UiText.T("Invalid game settings"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private static void ValidateHex(string value, int? exact, string label)
    {
        if (value.Length == 0 || value.Length % 2 != 0 || exact is not null && value.Length != exact || value.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException(string.Format(UiText.T("{0} must contain {1} hexadecimal characters."), label, exact?.ToString() ?? UiText.T("an even number of")));
    }
}
