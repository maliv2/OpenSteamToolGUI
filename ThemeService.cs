using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace OpenSteamToolGUI;

public static class ThemeService
{
    public static void Apply(string appearance)
    {
        bool systemLight = true;
        try { systemLight = Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1)) != 0; } catch { }
        bool light = appearance is "Light" or "Fluent" || appearance == "System" && systemLight;
        string[] keys = ["Canvas", "Sidebar", "Surface", "Input", "Text", "Muted", "Border", "Hover", "Selected", "Accent", "AccentText", "Warning", "StatusOnline", "StatusOffline", "StatusChecking"];
        string[] colors = light
            ? ["#F3F5F9", "#E9EEF5", "#FFFFFF", "#F8FAFD", "#18253B", "#53647C", "#CDD6E3", "#E8EEF7", "#DCE9FF", "#245AC6", "#FFFFFF", "#98501D", "#167044", "#B42318", "#946200"]
            : ["#10151F", "#141C29", "#1B2535", "#141D2C", "#EDF2FA", "#A7B6CD", "#36465E", "#283950", "#263F65", "#97BCFF", "#102039", "#F4BD85", "#4ADE80", "#FF6B6B", "#FACC15"];
        for (int i = 0; i < keys.Length; i++)
            Application.Current.Resources["Ui" + keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
    }

    public static void StyleWindow(Window window)
    {
        window.SetResourceReference(Window.BackgroundProperty, "UiCanvas");
        window.SetResourceReference(Window.ForegroundProperty, "UiText");
        window.FontFamily = new FontFamily("Segoe UI");
        window.FontSize = 14;
    }
}
