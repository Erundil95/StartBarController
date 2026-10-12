namespace StartBarController.Core;

/// <summary>
/// Windows path helpers that behave the same on every OS, so the core logic (and its
/// tests on Linux/macOS) treat "C:\..." paths exactly as Windows would.
/// </summary>
public static class WinPath
{
    private static readonly char[] Separators = { '\\', '/' };

    public static string Combine(string folder, params string[] parts)
    {
        string result = folder.TrimEnd(Separators);
        foreach (string part in parts)
        {
            result += '\\' + part.Trim(Separators);
        }

        return result;
    }

    public static string? GetDirectory(string path)
    {
        int index = path.TrimEnd(Separators).LastIndexOfAny(Separators);
        return index > 0 ? path[..index] : null;
    }

    public static bool IsAbsolute(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) ||
        (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && Separators.Contains(path[2]));
}
