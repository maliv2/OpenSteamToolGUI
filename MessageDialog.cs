using System.Windows;
using System.Windows.Controls;
using OpenSteamToolGUI.Core;

namespace OpenSteamToolGUI;

// Keep confirmations within the application's theme, including their buttons.
public static class MessageDialog
{
    public static MessageBoxResult Show(Window owner, string message, string title = "OpenSteamTool", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        var window = new Window { Owner = owner, Title = UiText.T(title), Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ThemeService.StyleWindow(window);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = UiText.T(title), FontSize = 18, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = UiText.T(message), Margin = new Thickness(4, 10, 4, 16), TextWrapping = TextWrapping.Wrap, MaxHeight = 400 });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var result = buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.OK;
        void Add(string label, MessageBoxResult value, bool primary)
        {
            var button = new Button { Content = UiText.T(label), MinWidth = 80, IsDefault = buttons == MessageBoxButton.OK, IsCancel = value == MessageBoxResult.No };
            if (primary) button.Style = (Style)window.FindResource("PrimaryButton");
            button.Click += (_, _) => { result = value; window.Close(); };
            actions.Children.Add(button);
        }
        if (buttons == MessageBoxButton.YesNo) { Add("Yes", MessageBoxResult.Yes, true); Add("No", MessageBoxResult.No, false); }
        else Add("OK", MessageBoxResult.OK, true);
        panel.Children.Add(actions); window.Content = panel; window.ShowDialog(); return result;
    }
}
