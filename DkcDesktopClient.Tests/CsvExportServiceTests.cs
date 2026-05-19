using DkcDesktopClient.App.Services;

namespace DkcDesktopClient.Tests;

public class CsvExportServiceTests
{
    private static readonly string TmpDir =
        Path.Combine(Path.GetTempPath(), "DkcDesktopClient.Tests.CsvExport");

    public CsvExportServiceTests()
    {
        Directory.CreateDirectory(TmpDir);
    }

    [Fact]
    public void ExportToCsv_WritesHeaderAndRows()
    {
        var path = Path.Combine(TmpDir, $"test_{Guid.NewGuid():N}.csv");

        var rows = new[]
        {
            new { Id = 1, Name = "Alpha",  Note = "OK" },
            new { Id = 2, Name = "Beta",   Note = "with;semicolon" },
            new { Id = 3, Name = "Gamma",  Note = "with\"quote" },
        };

        var typedColumns = new (string, Func<dynamic, string?>)[]
        {
            ("ID",   r => ((int)r.Id).ToString()),
            ("Name", r => (string?)r.Name),
            ("Note", r => (string?)r.Note),
        };

        CsvExportService.ExportToCsv<dynamic>(path, rows, typedColumns);

        // Verify UTF-8 BOM (EF BB BF)
        var rawBytes = File.ReadAllBytes(path);
        Assert.True(rawBytes.Length >= 3 &&
                    rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF,
                    "File should start with UTF-8 BOM for Excel compatibility");

        var lines = File.ReadAllLines(path);
        Assert.Equal(4, lines.Length); // header + 3 rows
        Assert.Equal("ID;Name;Note", lines[0]);
        Assert.Equal("1;Alpha;OK", lines[1]);
        Assert.Contains("with;semicolon", lines[2]); // semicolon must be quoted
        Assert.True(lines[2].Contains('"'), "Field with semicolon should be quoted");
        Assert.Contains("with\"\"quote", lines[3]); // double-quote should be escaped
    }

    [Fact]
    public void ExportToCsv_UsesCrlfLineEndings()
    {
        var path = Path.Combine(TmpDir, $"test_{Guid.NewGuid():N}.csv");

        var rows = new[] { "row1", "row2" };
        var columns = new (string, Func<string, string?>)[]
        {
            ("Text", r => r),
        };

        CsvExportService.ExportToCsv(path, rows, columns);

        // Read raw text and verify CRLF (RFC-4180 §2)
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        Assert.Contains("\r\n", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""), StringComparison.Ordinal);
    }

    [Fact]
    public void ExportToCsv_EmptyRows_WritesOnlyHeader()
    {
        var path = Path.Combine(TmpDir, $"test_{Guid.NewGuid():N}.csv");

        var columns = new (string, Func<string, string?>)[]
        {
            ("Col1", _ => ""),
        };

        CsvExportService.ExportToCsv(path, Enumerable.Empty<string>(), columns);

        var text = File.ReadAllText(path);
        Assert.Contains("Col1", text);
        // Should only have the header line + line ending
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
    }

    [Fact]
    public void ExportToCsv_FieldWithNewline_IsQuoted()
    {
        var path = Path.Combine(TmpDir, $"test_{Guid.NewGuid():N}.csv");

        var rows = new[] { "line1\nline2" };
        var columns = new (string, Func<string, string?>)[]
        {
            ("Text", r => r),
        };

        CsvExportService.ExportToCsv(path, rows, columns);

        var text = File.ReadAllText(path);
        // The field must be wrapped in quotes
        Assert.Contains("\"line1\nline2\"", text);
    }
}
