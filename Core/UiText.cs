using System.Text.Json;

namespace OpenSteamToolGUI.Core;

public static class UiText
{
    public static string Language { get; set; } = "en";
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = BuildCatalogs();
    private static Dictionary<string, Dictionary<string, string>> BuildCatalogs()
    {
        var catalogs = LanguageService.Languages.Where(language => language.Code != "en")
            .ToDictionary(language => language.Code, language => Load(language.Code));
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("OpenSteamToolGUI.Core.Errors.tsv")
            ?? throw new InvalidOperationException("Missing error translations");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;
            var cells = line.Split('\t');
            if (cells.Length != LanguageService.Languages.Length)
                throw new InvalidDataException($"Invalid error translation row: {cells[0]}");
            for (int index = 1; index < cells.Length; index++)
                catalogs[LanguageService.Languages[index].Code].Add(cells[0], cells[index]);
        }
        return catalogs;
    }
    private static Dictionary<string, string> Load(string language)
    {
        string resource = language == "tr" ? "Turkish" : language;
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream($"OpenSteamToolGUI.Core.{resource}.json")
            ?? throw new InvalidOperationException($"Missing language catalog: {resource}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    public static bool Contains(string text) => Catalogs["tr"].ContainsKey(text);
    public static string Translate(string language, string text)
    {
        if (!Catalogs.TryGetValue(language, out var catalog)) return text;
        if (catalog.TryGetValue(text, out var value)) return value;
        var prefix = catalog.Keys.Where(key => key.EndsWith(' ') && text.StartsWith(key, StringComparison.Ordinal))
            .OrderByDescending(key => key.Length).FirstOrDefault();
        if (prefix is null) return text;
        var translatedPrefix = catalog[prefix];
        if (!char.IsWhiteSpace(translatedPrefix[^1]) && translatedPrefix[^1] != '：') translatedPrefix += " ";
        return translatedPrefix + text[prefix.Length..];
    }
    public static IReadOnlyCollection<string> MissingKeys(string language) => Catalogs["tr"].Keys
        .Where(key => !Catalogs.TryGetValue(language, out var catalog) || !catalog.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        .ToArray();
    public static string T(string text) => LanguageService.T(Language, text);
}
