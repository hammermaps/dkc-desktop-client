using System.Text.RegularExpressions;

namespace DkcDesktopClient.App.Services;

/// <summary>Utility helpers for handling HTML strings from CKEditor fields.</summary>
public static class HtmlHelper
{
    private static readonly Regex TagPattern =
        new(@"<[^>]*>", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex MultipleSpaces =
        new(@"\s{2,}", RegexOptions.Compiled);

    /// <summary>
    /// Strips all HTML tags from <paramref name="html"/> and returns readable plain text.
    /// Returns an empty string when <paramref name="html"/> is <c>null</c>.
    /// </summary>
    public static string StripTags(string? html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;

        // Replace common block-level tags with newlines before stripping.
        var result = html
            .Replace("</p>",    "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>",    "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>",   "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />",  "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</li>",   "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</div>",  "\n", StringComparison.OrdinalIgnoreCase);

        result = TagPattern.Replace(result, string.Empty);

        // Decode common HTML entities.
        result = result
            .Replace("&amp;",  "&",  StringComparison.Ordinal)
            .Replace("&lt;",   "<",  StringComparison.Ordinal)
            .Replace("&gt;",   ">",  StringComparison.Ordinal)
            .Replace("&nbsp;", " ",  StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.Ordinal)
            .Replace("&#39;",  "'",  StringComparison.Ordinal);

        return result.Trim();
    }
}
