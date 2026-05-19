using DkcDesktopClient.App.Services;

namespace DkcDesktopClient.Tests;

public class HtmlHelperTests
{
    // ── Null / empty ──────────────────────────────────────────────────────────

    [Fact]
    public void StripTags_Null_ReturnsEmpty()
        => Assert.Equal(string.Empty, HtmlHelper.StripTags(null));

    [Fact]
    public void StripTags_Empty_ReturnsEmpty()
        => Assert.Equal(string.Empty, HtmlHelper.StripTags(string.Empty));

    [Fact]
    public void StripTags_Whitespace_ReturnsEmpty()
        => Assert.Equal(string.Empty, HtmlHelper.StripTags("   "));

    // ── Plain text pass-through ───────────────────────────────────────────────

    [Fact]
    public void StripTags_PlainText_Unchanged()
        => Assert.Equal("Hello world", HtmlHelper.StripTags("Hello world"));

    // ── Block-tag → newline conversion ────────────────────────────────────────

    [Theory]
    [InlineData("<br>")]
    [InlineData("<br/>")]
    [InlineData("<br />")]
    public void StripTags_BreakTags_BecomeNewline(string br)
    {
        var result = HtmlHelper.StripTags($"line1{br}line2");
        Assert.Contains('\n', result);
        Assert.Contains("line1", result);
        Assert.Contains("line2", result);
    }

    [Fact]
    public void StripTags_ClosingParagraph_BecomeNewline()
    {
        var result = HtmlHelper.StripTags("<p>first</p><p>second</p>");
        Assert.Contains("first", result);
        Assert.Contains("second", result);
        Assert.Contains('\n', result);
    }

    [Fact]
    public void StripTags_ClosingListItem_BecomeNewline()
    {
        var result = HtmlHelper.StripTags("<ul><li>one</li><li>two</li></ul>");
        Assert.Contains("one", result);
        Assert.Contains("two", result);
        Assert.Contains('\n', result);
    }

    [Fact]
    public void StripTags_ClosingDiv_BecomeNewline()
    {
        var result = HtmlHelper.StripTags("<div>foo</div><div>bar</div>");
        Assert.Contains("foo", result);
        Assert.Contains("bar", result);
        Assert.Contains('\n', result);
    }

    // ── All inline tags are stripped ──────────────────────────────────────────

    [Fact]
    public void StripTags_InlineTags_Removed()
    {
        var result = HtmlHelper.StripTags("<strong>bold</strong> and <em>italic</em>");
        Assert.Equal("bold and italic", result);
    }

    [Fact]
    public void StripTags_Anchor_TextPreserved()
    {
        var result = HtmlHelper.StripTags("<a href=\"https://example.com\">link text</a>");
        Assert.Equal("link text", result);
    }

    // ── Nested / malformed tags ───────────────────────────────────────────────

    [Fact]
    public void StripTags_NestedTags_ContentPreserved()
    {
        var result = HtmlHelper.StripTags("<p><strong><em>nested</em></strong></p>");
        Assert.Contains("nested", result);
    }

    [Fact]
    public void StripTags_MalformedTag_DoesNotThrow()
    {
        var result = HtmlHelper.StripTags("<unclosed <p>text</p>");
        Assert.Contains("text", result);
    }

    [Fact]
    public void StripTags_UnclosedTag_DoesNotThrow()
    {
        var result = HtmlHelper.StripTags("<p>text without closing");
        Assert.Contains("text without closing", result);
    }

    // ── HTML entity decoding ──────────────────────────────────────────────────

    [Fact]
    public void StripTags_AmpEntity_Decoded()
        => Assert.Equal("a & b", HtmlHelper.StripTags("a &amp; b"));

    [Fact]
    public void StripTags_LtGtEntities_Decoded()
    {
        // &lt; and &gt; decoded from already-stripped text
        var result = HtmlHelper.StripTags("<p>&lt;code&gt;</p>");
        Assert.Contains("<code>", result);
    }

    [Fact]
    public void StripTags_NbspEntity_DecodedToSpace()
    {
        var result = HtmlHelper.StripTags("a&nbsp;b");
        Assert.Equal("a b", result.Trim());
    }

    [Fact]
    public void StripTags_QuotEntity_Decoded()
        => Assert.Equal("say \"hi\"", HtmlHelper.StripTags("say &quot;hi&quot;"));

    [Fact]
    public void StripTags_Apos_Decoded()
        => Assert.Equal("it's", HtmlHelper.StripTags("it&#39;s"));

    // ── Double-encoding safety: &amp;lt; should not become < ─────────────────

    [Fact]
    public void StripTags_DoubleEncoded_DecodesSequentially()
    {
        // "&amp;lt;" decodes in two passes: &amp; → & producing "&lt;", then &lt; → "<".
        // This documents the expected sequential-decoding behavior.
        var result = HtmlHelper.StripTags("&amp;lt;");
        Assert.Equal("<", result);
    }

    // ── Whitespace collapsing ─────────────────────────────────────────────────

    [Fact]
    public void StripTags_MultipleSpaces_Collapsed()
    {
        var result = HtmlHelper.StripTags("a   b    c");
        Assert.Equal("a b c", result);
    }

    [Fact]
    public void StripTags_LeadingTrailingWhitespace_Trimmed()
    {
        var result = HtmlHelper.StripTags("  hello  ");
        Assert.Equal("hello", result);
    }

    // ── CKEditor-typical output ───────────────────────────────────────────────

    [Fact]
    public void StripTags_CkEditorOutput_TextExtractedWithSeparators()
    {
        const string ck = """
            <p>First paragraph.</p>
            <ul>
              <li>Item one</li>
              <li>Item two</li>
            </ul>
            <blockquote><p>A quote.</p></blockquote>
            """;

        var result = HtmlHelper.StripTags(ck);
        Assert.Contains("First paragraph.", result);
        Assert.Contains("Item one", result);
        Assert.Contains("Item two", result);
        Assert.Contains("A quote.", result);

        // Block-level tags must have been converted to separators so content segments
        // are not run together; adjacent whitespace (including multiple newlines) is
        // collapsed by the whitespace-normaliser to a single space or newline.
        Assert.DoesNotContain("First paragraph.Item one", result);
        Assert.DoesNotContain("Item oneItem two", result);
    }
}
