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

        var columns = new (string, Func<object, string?>)[]
        {
            ("ID",   r => ((dynamic)r).Id.ToString()),
            ("Name", r => ((dynamic)r).Name),
            ("Note", r => ((dynamic)r).Note),
        };

        // Use typed overload with anonymous record
        var typedColumns = new (string, Func<dynamic, string?>)[]
        {
            ("ID",   r => ((int)r.Id).ToString()),
            ("Name", r => (string?)r.Name),
            ("Note", r => (string?)r.Note),
        };

        CsvExportService.ExportToCsv<dynamic>(path, rows, typedColumns);

        var lines = File.ReadAllLines(path);
        Assert.Equal(4, lines.Length); // header + 3 rows
        Assert.Equal("ID;Name;Note", lines[0]);
        Assert.Equal("1;Alpha;OK", lines[1]);
        Assert.Contains("with;semicolon", lines[2]); // semicolon must be quoted
        Assert.True(lines[2].Contains('"'), "Field with semicolon should be quoted");
        Assert.Contains("with\"\"quote", lines[3]); // double-quote should be escaped
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
