namespace Lragrz.Enova.Client;

/// <summary>
/// Single source for brand strings. Change here only.
/// </summary>
public static class Brand
{
    public const string Title = "eNoVa-Tor";
    public const string Internal = "eNoVa_tor";
    public const string Version = "0.1";
    public const string Year = "2026";
    public const string VersionSuffix = "[v0.1 // 2026]";
    public const string FullTitle = "eNoVa-Tor [v0.1 // 2026]";
    public const string OutputFolder = "eNoVa_tor";

    public static string Window(string specific) => $"{Title} – {specific}";
}
