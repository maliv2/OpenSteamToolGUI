using OpenSteamToolGUI.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenSteamToolGUI;

public sealed class TextEditorWindow : Window
{
    private readonly TextBox _editor;
    public string EditedText => _editor.Text;
    public TextEditorWindow(string title, string content)
    {
        Title = title; Width = 800; Height = 560; MinWidth = 560; MinHeight = 380; WindowStartupLocation = WindowStartupLocation.CenterOwner; ThemeService.StyleWindow(this);
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _editor = new TextBox { Text = content, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FontSize = 13, Margin = new Thickness(8) };
        grid.Children.Add(_editor);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = UiText.T("Save"), MinWidth = 80, IsDefault = true };
        save.Click += (_, _) => { DialogResult = true; Close(); };
        var cancel = new Button { Content = UiText.T("Cancel"), MinWidth = 80, IsCancel = true };
        cancel.Click += (_, _) => Close(); panel.Children.Add(save); panel.Children.Add(cancel);
        Grid.SetRow(panel, 1); grid.Children.Add(panel); Content = grid;
    }
}
