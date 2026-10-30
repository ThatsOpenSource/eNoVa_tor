using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Lragrz.Enova.Client.Services;

/// <summary>
/// Escapes untrusted text before insertion into HTML or UI bindings.
/// </summary>
public static class HtmlSanitizer
{
    private static readonly Regex ControlChars = new(@"[\x00-\x08\x0B\x0C\x0E-\x1F]", RegexOptions.Compiled);

    public static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var cleaned = ControlChars.Replace(value, string.Empty);
        return WebUtility.HtmlEncode(cleaned);
    }

    public static string Plain(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return ControlChars.Replace(value, string.Empty).Trim();
    }
}

