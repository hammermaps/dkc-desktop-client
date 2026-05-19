using System.Text;

namespace DkcDesktopClient.App.Services;

/// <summary>
/// Simple CSV export helper.  Converts a sequence of row objects to CSV text
/// using the supplied column definitions and writes the result to a file.
/// </summary>
public static class CsvExportService
{
    /// <summary>
    /// Builds a CSV string from <paramref name="rows"/> using the provided column
    /// definitions and saves it to <paramref name="filePath"/>.
    /// </summary>
    /// <typeparam name="T">Type of the data rows.</typeparam>
    /// <param name="filePath">Target file path.</param>
    /// <param name="rows">The data rows to export.</param>
    /// <param name="columns">Column definitions: (header, value-selector).</param>
    public static void ExportToCsv<T>(
        string filePath,
        IEnumerable<T> rows,
        IReadOnlyList<(string Header, Func<T, string?> Selector)> columns)
    {
        var sb = new StringBuilder();

        // Header row (RFC-4180 requires CRLF line endings)
        sb.Append(string.Join(";", columns.Select(c => EscapeField(c.Header))));
        sb.Append("\r\n");

        // Data rows
        foreach (var row in rows)
        {
            var fields = columns.Select(c => EscapeField(c.Selector(row)));
            sb.Append(string.Join(";", fields));
            sb.Append("\r\n");
        }

        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    // ── Private ───────────────────────────────────────────────────────────────

    /// <summary>Escapes a single CSV field by quoting it when necessary.</summary>
    private static string EscapeField(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        // Quote fields that contain the separator, double-quote, or newlines.
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }
}
