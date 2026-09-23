using System.Text.Json;

namespace OpenSteamToolGUI.Core;

public static class UiText
{
    public static string Language { get; set; } = "tr";
    private static readonly Dictionary<string, string> Turkish = Load();
    private static Dictionary<string, string> Load()
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("OpenSteamToolGUI.Core.Turkish.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    public static bool Contains(string text) => Turkish.ContainsKey(text);
    public static string Translate(string language, string text) => language == "tr" && Turkish.TryGetValue(text, out var value) ? value : text;
    public static string T(string text) => LanguageService.T(Language, text);
}
