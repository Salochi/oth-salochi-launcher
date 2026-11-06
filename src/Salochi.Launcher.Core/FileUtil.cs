using System.Security.Cryptography;

namespace Salochi.Launcher.Core;

internal static class FileUtil
{
    /// <summary>Writes through a temp file and a rename, so a crash never leaves half a file.</summary>
    public static void WriteAtomic(string path, string text)
    {
        string tmp = path + ".salochi-tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, overwrite: true);
    }

    public static string Sha256(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(s));
    }
}
