using System;
using System.IO;
using System.Text.Json;

namespace Lragrz.Enova.Client.Services;

/// <summary>
/// Lightweight local preferences (last XML folder, optional home directory).
/// </summary>
public sealed class UserPreferences
{
    private static readonly string PathFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "eNoVa_tor",
        "preferences.json");

    public string? LastOpenDirectory { get; set; }
    public string? HomeDirectory { get; set; }

    public static UserPreferences Load()
    {
        try
        {
            if (File.Exists(PathFile))
            {
                var json = File.ReadAllText(PathFile);
                return JsonSerializer.Deserialize<UserPreferences>(json) ?? new UserPreferences();
            }
        }
        catch { /* use defaults */ }
        return new UserPreferences();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathFile)!);
        File.WriteAllText(PathFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Returns a valid initial directory for the open-file dialog.
    /// Priority: HomeDirectory (if still valid) → LastOpenDirectory (if valid) → app base directory.
    /// </summary>
    public string ResolveOpenStartDirectory()
    {
        if (IsValidDirectory(HomeDirectory))
            return HomeDirectory!;
        if (IsValidDirectory(LastOpenDirectory))
            return LastOpenDirectory!;
        return AppContext.BaseDirectory;
    }

    public void RememberDirectoryFromFile(string filePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (IsValidDirectory(dir))
            {
                LastOpenDirectory = dir;
                Save();
            }
        }
        catch { /* ignore */ }
    }

    private static bool IsValidDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        try
        {
            var full = Path.GetFullPath(path);
            return Directory.Exists(full);
        }
        catch
        {
            return false;
        }
    }
}

