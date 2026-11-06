using System.Text.RegularExpressions;

namespace Salochi.Launcher.Core.Game;

/// <summary>
/// BepInEx loads through Unity Doorstop (winhttp.dll next to the game exe), which reads doorstop_config.ini. Setting
/// <c>enabled = false</c> in its [General] section makes the game start vanilla: nothing is deleted, and turning it back
/// on brings the mods back exactly as they were. Only that one line changes; the rest of the file is kept as it is.
/// </summary>
public static partial class DoorstopConfig
{
    public const string FileName = "doorstop_config.ini";

    [GeneratedRegex(@"^(\s*enabled\s*=\s*)(\S*)(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex EnabledLine();

    /// <summary>The [General] section's enabled value; null when the key is missing.</summary>
    public static bool? ReadEnabled(string iniText)
    {
        string section = "";
        foreach (var line in SplitLines(iniText))
        {
            if (IsHeader(line, out var name)) { section = name; continue; }
            if (!IsGeneral(section)) continue;
            var m = EnabledLine().Match(line);
            if (m.Success) return m.Groups[2].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        return null;
    }

    /// <summary>The text with [General] enabled set to <paramref name="on"/>; the key (and section) are added when missing.</summary>
    public static string WriteEnabled(string iniText, bool on)
    {
        string value = on ? "true" : "false";
        string nl = iniText.Contains("\r\n") ? "\r\n" : "\n";
        var lines = SplitLines(iniText);
        string section = "";
        int generalHeader = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (IsHeader(lines[i], out var name))
            {
                section = name;
                if (IsGeneral(section) && generalHeader < 0) generalHeader = i;
                continue;
            }
            if (!IsGeneral(section)) continue;
            var m = EnabledLine().Match(lines[i]);
            if (!m.Success) continue;
            lines[i] = m.Groups[1].Value + value + m.Groups[3].Value;
            return string.Join(nl, lines);
        }
        if (generalHeader >= 0) lines.Insert(generalHeader + 1, "enabled = " + value);
        else lines.InsertRange(0, ["[General]", "enabled = " + value, ""]);
        return string.Join(nl, lines);
    }

    private static List<string> SplitLines(string text) => text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

    private static bool IsGeneral(string section) => section.Equals("General", StringComparison.OrdinalIgnoreCase);

    private static bool IsHeader(string line, out string name)
    {
        string t = line.Trim();
        if (t.Length >= 2 && t[0] == '[' && t[^1] == ']') { name = t[1..^1].Trim(); return true; }
        name = "";
        return false;
    }

    /// <summary>The game's doorstop setting: true (mods load), false (vanilla), null (no doorstop: BepInEx isn't installed).</summary>
    public static bool? Read(string gameRoot)
    {
        string path = Path.Combine(gameRoot, FileName);
        return File.Exists(path) ? ReadEnabled(File.ReadAllText(path)) : null;
    }

    public static void Write(string gameRoot, bool on)
    {
        string path = Path.Combine(gameRoot, FileName);
        string text = File.Exists(path) ? File.ReadAllText(path) : "";
        string updated = WriteEnabled(text, on);
        if (updated != text) FileUtil.WriteAtomic(path, updated);
    }
}
