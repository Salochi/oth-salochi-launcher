using System.Text;

namespace Salochi.Launcher.Core.Game;

/// <summary>
/// A minimal reader for Valve's text KeyValues files (libraryfolders.vdf, appmanifest_*.acf): quoted keys, quoted
/// values or nested <c>{ }</c> blocks, <c>//</c> comments. Keys are case-insensitive, as Steam treats them.
/// </summary>
public sealed class Vdf
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Vdf> Blocks { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Value(string key) => Values.TryGetValue(key, out var v) ? v : null;
    public Vdf? Block(string key) => Blocks.TryGetValue(key, out var b) ? b : null;

    public static Vdf Parse(string text)
    {
        int i = 0;
        var root = new Vdf();
        ReadBody(text, ref i, root, topLevel: true);
        return root;
    }

    private static void ReadBody(string s, ref int i, Vdf into, bool topLevel)
    {
        while (true)
        {
            SkipSpace(s, ref i);
            if (i >= s.Length)
            {
                if (topLevel) return;
                throw new FormatException("unexpected end of file inside a block");
            }
            if (s[i] == '}')
            {
                if (topLevel) throw new FormatException("unmatched '}' at " + i);
                i++;
                return;
            }
            string key = ReadString(s, ref i);
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == '{')
            {
                i++;
                var child = new Vdf();
                ReadBody(s, ref i, child, topLevel: false);
                into.Blocks[key] = child;
            }
            else
            {
                into.Values[key] = ReadString(s, ref i);
            }
        }
    }

    private static void SkipSpace(string s, ref int i)
    {
        while (i < s.Length)
        {
            if (char.IsWhiteSpace(s[i])) { i++; continue; }
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }
            break;
        }
    }

    private static string ReadString(string s, ref int i)
    {
        if (i >= s.Length) throw new FormatException("expected a string at end of file");
        var sb = new StringBuilder();
        if (s[i] != '"')
        {
            // an unquoted token: rare in Steam's files, but legal
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '{' && s[i] != '}' && s[i] != '"') sb.Append(s[i++]);
            if (sb.Length == 0) throw new FormatException("expected a string at " + i);
            return sb.ToString();
        }
        i++;
        while (i < s.Length && s[i] != '"')
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char n = s[i + 1];
                sb.Append(n switch { 'n' => '\n', 't' => '\t', _ => n });
                i += 2;
                continue;
            }
            sb.Append(s[i++]);
        }
        if (i >= s.Length) throw new FormatException("unterminated string");
        i++;
        return sb.ToString();
    }
}
