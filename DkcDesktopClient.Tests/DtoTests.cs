using DkcDesktopClient.Core.Api;

namespace DkcDesktopClient.Tests;

/// <summary>Tests for computed properties on DTO record types.</summary>
public class DtoTests
{
    // ── MmStatusHelper ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "Offen")]
    [InlineData(1, "In Bearbeitung")]
    [InlineData(2, "Geschlossen")]
    [InlineData(3, "Abgebrochen")]
    public void MmStatusHelper_StatusLabel_KnownStatuses(int status, string expected)
    {
        Assert.Equal(expected, MmStatusHelper.StatusLabel(status));
    }

    [Fact]
    public void MmStatusHelper_StatusLabel_UnknownStatus_ReturnsString()
    {
        Assert.Equal("99", MmStatusHelper.StatusLabel(99));
    }

    // ── MmMessage – StatusText ─────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "Offen")]
    [InlineData(1, "In Bearbeitung")]
    [InlineData(2, "Geschlossen")]
    [InlineData(3, "Abgebrochen")]
    public void MmMessage_StatusText_MatchesHelper(int status, string expected)
    {
        var msg = MakeMessage(status: status);
        Assert.Equal(expected, msg.StatusText);
    }

    // ── MmMessage – DringlichkeitText ──────────────────────────────────────────

    [Theory]
    [InlineData(null,      "Normal")]
    [InlineData("normal",  "Normal")]
    [InlineData("dringend","⚠ Dringend")]
    [InlineData("notfall", "🔴 Notfall")]
    public void MmMessage_DringlichkeitText(string? dringlichkeit, string expected)
    {
        var msg = MakeMessage(dringlichkeit: dringlichkeit);
        Assert.Equal(expected, msg.DringlichkeitText);
    }

    // ── MmMessage – DringlichkeitColorHex ──────────────────────────────────────

    [Theory]
    [InlineData(null,      "#718096")]
    [InlineData("normal",  "#718096")]
    [InlineData("dringend","#D97706")]
    [InlineData("notfall", "#DC2626")]
    public void MmMessage_DringlichkeitColorHex(string? dringlichkeit, string expected)
    {
        var msg = MakeMessage(dringlichkeit: dringlichkeit);
        Assert.Equal(expected, msg.DringlichkeitColorHex);
    }

    // ── MmMessage – StatusColorHex ──────────────────────────────────────────────

    [Theory]
    [InlineData(0, "#3B82F6")]
    [InlineData(1, "#D97706")]
    [InlineData(2, "#10B981")]
    [InlineData(3, "#6B7280")]
    [InlineData(9, "#6B7280")]
    public void MmMessage_StatusColorHex(int status, string expected)
    {
        var msg = MakeMessage(status: status);
        Assert.Equal(expected, msg.StatusColorHex);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static MmMessage MakeMessage(int status = 0, string? dringlichkeit = null) =>
        new(Uid: "test-uid", Status: status, Betreff: null, Street: null,
            Whg: null, Melder: null, Datetime: null,
            Dringlichkeit: dringlichkeit, Nachunternehmer: null,
            Scanned: false, Zugeh: null);
}
