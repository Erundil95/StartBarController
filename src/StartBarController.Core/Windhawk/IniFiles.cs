namespace StartBarController.Core.Windhawk;

/// <summary>
/// INI file access. The Windows app implements this with Get/WritePrivateProfileString,
/// the same API Windhawk uses, so encodings and formatting stay compatible.
/// </summary>
public interface IIniFiles
{
    string? Read(string path, string section, string key);

    /// <summary>Writes a value; a null value deletes the key.</summary>
    void Write(string path, string section, string key, string? value);

    IReadOnlyList<string> Keys(string path, string section);
}

/// <summary>File system queries used to locate Windhawk.</summary>
public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    IReadOnlyList<string> GetDirectories(string path);

    string ExpandEnvironmentVariables(string text);
}
