using OpenSteamToolGUI.Core;
using System.Windows;
using System.Windows.Controls;

namespace OpenSteamToolGUI;

public sealed class AppIdWindow : Window
{
    private readonly TextBox _value = new() { MinWidth = 240 };
    public uint AppId { get; private set; }
    public AppIdWindow()
    {
        Title = UiText.T("Assign AppID"); Width = 360; Height = 200; WindowStartupLocation = WindowStartupLocation.CenterOwner; ThemeService.StyleWindow(this);
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = UiText.T("AppID for selected files:") }); stack.Children.Add(_value);
        var button = new Button { Content = UiText.T("Assign"), HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        button.Click += (_, _) => { if (!uint.TryParse(_value.Text.Trim(), out var id) || id == 0) { MessageDialog.Show(this, UiText.T("Enter a positive AppID.")); return; } AppId = id; DialogResult = true; Close(); };
        stack.Children.Add(button); Content = stack;
    }
}
