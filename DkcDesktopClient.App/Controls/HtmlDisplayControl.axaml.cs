using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DkcDesktopClient.App.Services;

namespace DkcDesktopClient.App.Controls;

/// <summary>
/// A read-only HTML renderer backed by <c>NativeWebView</c>.
/// Use this to display CKEditor-generated HTML content without allowing edits.
/// Falls back to a plain <see cref="TextBlock"/> when the native WebView adapter
/// is unavailable.
/// </summary>
public partial class HtmlDisplayControl : UserControl
{
    /// <summary>
    /// Bindable HTML content property (OneWay by default).
    /// The provided HTML is rendered inside a minimal page template.
    /// </summary>
    public static readonly StyledProperty<string?> HtmlContentProperty =
        AvaloniaProperty.Register<HtmlDisplayControl, string?>(nameof(HtmlContent));

    private NativeWebView? _webView;
    private TextBlock? _fallbackBlock;
    private bool _navigated;

    public string? HtmlContent
    {
        get => GetValue(HtmlContentProperty);
        set => SetValue(HtmlContentProperty, value);
    }

    public HtmlDisplayControl()
    {
        AvaloniaXamlLoader.Load(this);
        InitializeWebView();
    }

    private void InitializeWebView()
    {
        try
        {
            _webView = new NativeWebView();
            _webView.NavigationCompleted += OnNavigationCompleted;
            Content = _webView;
            // Load the base display page; content is injected once navigation completes.
            _webView.NavigateToString(DisplayHtml, new Uri("https://localhost/display.html"));
        }
        catch
        {
            // WebView backend not available — fall back to a plain TextBlock.
            _webView = null;
            ShowFallback();
        }
    }

    private void ShowFallback()
    {
        _fallbackBlock = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = HtmlHelper.StripTags(HtmlContent),
        };
        Content = _fallbackBlock;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != HtmlContentProperty) return;

        var newHtml = (string?)(change.NewValue);

        if (_fallbackBlock != null)
        {
            _fallbackBlock.Text = HtmlHelper.StripTags(newHtml);
            return;
        }

        if (_navigated && _webView != null)
            _ = InjectContentAsync(newHtml);
    }

    private async void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _navigated = true;
        await InjectContentAsync(HtmlContent);
    }

    private async Task InjectContentAsync(string? html)
    {
        if (_webView == null) return;
        var encoded = JsonSerializer.Serialize(html ?? string.Empty);
        try { await _webView.InvokeScript($"setContent({encoded})"); }
        catch { /* Ignore JS errors during content injection. */ }
    }

    // ── Embedded display HTML ─────────────────────────────────────────────────
    private const string DisplayHtml = """
        <!DOCTYPE html>
        <html>
        <head>
          <meta charset="UTF-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <style>
            * { box-sizing: border-box; }
            body {
              font-family: system-ui, sans-serif;
              font-size: 13px;
              margin: 6px;
              color: #2d3748;
              overflow-x: hidden;
            }
            p { margin: 0 0 4px; }
            ul, ol { padding-left: 20px; margin: 0 0 4px; }
            blockquote {
              border-left: 3px solid #cbd5e0;
              padding-left: 8px;
              color: #718096;
              margin: 4px 0;
            }
            a { color: #2b6cb0; }
          </style>
        </head>
        <body>
          <div id="content"></div>
          <script>
            function setContent(html) {
              document.getElementById('content').innerHTML = html;
            }
          </script>
        </body>
        </html>
        """;
}
