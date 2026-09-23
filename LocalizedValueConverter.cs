using System.Globalization;
using System.Windows.Data;
using OpenSteamToolGUI.Core;

namespace OpenSteamToolGUI;

public sealed class LocalizedValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? "";
        foreach (var prefix in new[] { "Install OpenSteamTool ", "Disable Lua ", "Enable Lua ", "Edit Lua ", "Create game ", "Import ", "Restore " })
            if (text.StartsWith(prefix, StringComparison.Ordinal)) return UiText.T(prefix) + text[prefix.Length..];
        return UiText.T(text);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
