using System.Text.RegularExpressions;

namespace OpenSteamToolGUI.Core;

public sealed class ToolConfig
{
    public string LogLevel { get; set; } = "debug";
    public string ManifestProvider { get; set; } = "opensteamtool";
    public int ResolveTimeout { get; set; } = 5000;
    public int ConnectTimeout { get; set; } = 5000;
    public int SendTimeout { get; set; } = 10000;
    public int ReceiveTimeout { get; set; } = 10000;
    public string LuaPaths { get; set; } = "";
    public bool InjectEnabled { get; set; }
    public string LibraryX64 { get; set; } = "";
    public string LibraryX86 { get; set; } = "";
    public string RemoteTemplate { get; set; } = "";
    public bool StatsApi { get; set; } = true;
    public bool CloudEnabled { get; set; }
    public string CloudLibrary { get; set; } = "";
}

public sealed class ConfigService
{
    private string _original = "";
    private string _originalHash = "";
    private string _path = "";
    public ToolConfig Load(string path)
    {
        _path = path;
        _original = File.Exists(path) ? File.ReadAllText(path) : "";
        _originalHash = File.Exists(path) ? FileTools.HashFile(path) : "";
        var model = new ToolConfig(); string section = "";
        foreach (var line in _original.Split('\n'))
        {
            var heading = Regex.Match(line, @"^\s*\[([^]]+)\]");
            if (heading.Success) { section = heading.Groups[1].Value.Trim().ToLowerInvariant(); continue; }
            var kv = Regex.Match(line, @"^\s*([\w_]+)\s*=\s*(.*)$"); if (!kv.Success) continue;
            string key = kv.Groups[1].Value, raw = kv.Groups[2].Value.Trim();
            string value = Regex.Replace(raw, @"\s+#.*$", "").Trim().Trim('"');
            bool flag = value.Equals("true", StringComparison.OrdinalIgnoreCase);
            int number = int.TryParse(value, out var n) ? n : 0;
            switch (section + "." + key)
            {
                case "log.level": model.LogLevel = value; break;
                case "manifest.url": model.ManifestProvider = value; break;
                case "manifest.timeout_resolve_ms": model.ResolveTimeout = number; break;
                case "manifest.timeout_connect_ms": model.ConnectTimeout = number; break;
                case "manifest.timeout_send_ms": model.SendTimeout = number; break;
                case "manifest.timeout_recv_ms": model.ReceiveTimeout = number; break;
                case "lua.paths": model.LuaPaths = raw.Trim(); break;
                case "inject.enabled": model.InjectEnabled = flag; break;
                case "inject.library_x64": model.LibraryX64 = value; break;
                case "inject.library_x86": model.LibraryX86 = value; break;
                case "remote.url_template": model.RemoteTemplate = value; break;
                case "stats.enable_api": model.StatsApi = flag; break;
                case "cloud.enabled": model.CloudEnabled = flag; break;
                case "cloud.library": model.CloudLibrary = value; break;
            }
        }
        return model;
    }
    public string Raw => _original;
    public void SaveRaw(string raw, FileTransaction transaction)
    {
        CheckUnchanged(); ValidateSyntax(raw);
        transaction.Apply("Edit OpenSteamTool config", [(_path, (byte[]?)System.Text.Encoding.UTF8.GetBytes(raw))], steamRoot: Path.GetDirectoryName(_path));
        Load(_path);
    }
    public void Save(ToolConfig config, ToolCapabilities caps, FileTransaction transaction)
    {
        CheckUnchanged(); Validate(config, caps);
        var values = new List<(string Section, string Key, string Value)>
        {
            ("log", "level", Quote(config.LogLevel)),
            ("manifest", "url", Quote(config.ManifestProvider)),
            ("manifest", "timeout_resolve_ms", config.ResolveTimeout.ToString()),
            ("manifest", "timeout_connect_ms", config.ConnectTimeout.ToString()),
            ("manifest", "timeout_send_ms", config.SendTimeout.ToString()),
            ("manifest", "timeout_recv_ms", config.ReceiveTimeout.ToString()),
            ("lua", "paths", string.IsNullOrWhiteSpace(config.LuaPaths) ? "[]" : config.LuaPaths),
            ("inject", "enabled", config.InjectEnabled ? "true" : "false"),
            ("inject", "library_x64", Quote(config.LibraryX64)),
            ("inject", "library_x86", Quote(config.LibraryX86)),
            ("remote", "url_template", Quote(config.RemoteTemplate))
        };
        if (caps.StatsApi) values.Add(("stats", "enable_api", config.StatsApi ? "true" : "false"));
        if (caps.CloudRedirect) { values.Add(("cloud", "enabled", config.CloudEnabled ? "true" : "false")); values.Add(("cloud", "library", Quote(config.CloudLibrary))); }
        string result = _original;
        foreach (var (section, key, value) in values) result = Upsert(result, section, key, value);
        SaveRaw(result, transaction);
    }
    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    private void CheckUnchanged()
    {
        string current = File.Exists(_path) ? FileTools.HashFile(_path) : "";
        if (current != _originalHash) throw new IOException("Config changed outside the app. Reload it before saving.");
    }
    private static void Validate(ToolConfig c, ToolCapabilities caps)
    {
        if (!new[] { "trace", "debug", "info", "warn", "error" }.Contains(c.LogLevel)) throw new InvalidDataException("Invalid log level.");
        if (!new[] { "opensteamtool", "steamrun", "wudrm" }.Contains(c.ManifestProvider)) throw new InvalidDataException("Invalid manifest provider.");
        if (new[] { c.ResolveTimeout, c.ConnectTimeout, c.SendTimeout, c.ReceiveTimeout }.Any(x => x is < 1 or > 120000)) throw new InvalidDataException("Timeouts must be 1–120000 ms.");
        if (!string.IsNullOrWhiteSpace(c.RemoteTemplate) && new[] { "{channel}", "{component}", "{sha256}" }.Any(x => !c.RemoteTemplate.Contains(x))) throw new InvalidDataException("Remote template needs all three placeholders.");
        if (c.InjectEnabled && string.IsNullOrWhiteSpace(c.LibraryX64) && string.IsNullOrWhiteSpace(c.LibraryX86)) throw new InvalidDataException("Injection requires a library path.");
        if (caps.CloudRedirect && c.CloudEnabled && string.IsNullOrWhiteSpace(c.CloudLibrary)) throw new InvalidDataException("CloudRedirect requires a DLL path.");
        if (!string.IsNullOrWhiteSpace(c.LuaPaths) && (!c.LuaPaths.StartsWith('[') || !c.LuaPaths.EndsWith(']'))) throw new InvalidDataException("Lua paths must be a TOML array.");
    }
    public static void ValidateSyntax(string text)
    {
        string section = "";
        foreach (var line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            if (trimmed.StartsWith('[')) { if (!Regex.IsMatch(trimmed, @"^\[[\w.-]+\](?:\s*#.*)?$")) throw new InvalidDataException("Invalid TOML section."); section = trimmed; }
            else if (!trimmed.Contains('=') || section.Length == 0) throw new InvalidDataException("Invalid TOML key/value line.");
        }
    }
    private static string Upsert(string text, string section, string key, string value)
    {
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        int start = Array.FindIndex(lines, x => Regex.IsMatch(x, @"^\s*\[" + Regex.Escape(section) + @"\]\s*(?:#.*)?$"));
        if (start < 0) return text.TrimEnd() + "\n\n[" + section + "]\n" + key + " = " + value + "\n";
        int end = start + 1; while (end < lines.Length && !Regex.IsMatch(lines[end], @"^\s*\[")) end++;
        for (int i = start + 1; i < end; i++)
        {
            var match = Regex.Match(lines[i], @"^(\s*" + Regex.Escape(key) + @"\s*=\s*)(.*?)(\s+#.*)?$");
            if (match.Success) { lines[i] = match.Groups[1].Value + value + match.Groups[3].Value; return string.Join("\n", lines); }
        }
        var list = lines.ToList(); list.Insert(end, key + " = " + value); return string.Join("\n", list);
    }
}
