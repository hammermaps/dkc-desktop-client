using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace DkcDesktopClient.App.Controls;

/// <summary>
/// A rich-text editor control backed by <c>NativeWebView</c> with CKEditor 5.
/// Falls back to a multi-line <see cref="TextBox"/> when the native WebView adapter
/// is unavailable (e.g. no WebView2 installed on Windows).
/// </summary>
public partial class HtmlEditorControl : UserControl
{
    /// <summary>
    /// Bindable HTML content property (TwoWay by default).
    /// Reads the current editor content and pushes it back when the user types.
    /// </summary>
    public static readonly StyledProperty<string> HtmlContentProperty =
        AvaloniaProperty.Register<HtmlEditorControl, string>(
            nameof(HtmlContent),
            string.Empty,
            defaultBindingMode: BindingMode.TwoWay);

    private NativeWebView? _webView;
    private TextBox? _fallbackBox;

    // Prevent re-entrant property updates when C# injects content into the editor.
    private bool _suppressUpdate;
    // Track whether the initial navigation has completed so content can be injected.
    private bool _navigated;

    public string HtmlContent
    {
        get => GetValue(HtmlContentProperty);
        set => SetValue(HtmlContentProperty, value);
    }

    public HtmlEditorControl()
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
            _webView.WebMessageReceived += OnWebMessageReceived;
            Content = _webView;
            _webView.NavigateToString(EditorHtml, new Uri("https://localhost/editor.html"));
        }
        catch
        {
            // WebView backend not available — fall back to a plain multi-line TextBox.
            _webView = null;
            ShowFallback();
        }
    }

    private void ShowFallback()
    {
        _fallbackBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinHeight = 80,
            Text = HtmlContent,
        };
        _fallbackBox.TextChanged += (_, _) =>
        {
            if (!_suppressUpdate)
            {
                _suppressUpdate = true;
                HtmlContent = _fallbackBox.Text ?? string.Empty;
                _suppressUpdate = false;
            }
        };
        Content = _fallbackBox;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != HtmlContentProperty || _suppressUpdate) return;

        var newHtml = (string)(change.NewValue ?? string.Empty);

        if (_fallbackBox != null)
        {
            _suppressUpdate = true;
            _fallbackBox.Text = newHtml;
            _suppressUpdate = false;
            return;
        }

        if (_navigated && _webView != null)
            _ = InjectContentAsync(newHtml);
    }

    private async void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _navigated = true;
        var html = HtmlContent;
        if (!string.IsNullOrEmpty(html))
            await InjectContentAsync(html);
    }

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        // Messages arrive on the UI thread from NativeWebView.
        Dispatcher.UIThread.Post(() =>
        {
            _suppressUpdate = true;
            try { HtmlContent = e.Body ?? string.Empty; }
            finally { _suppressUpdate = false; }
        });
    }

    private async Task InjectContentAsync(string html)
    {
        if (_webView == null) return;
        // JSON-encode to safely embed arbitrary HTML into a JS string argument.
        var encoded = JsonSerializer.Serialize(html);
        try { await _webView.InvokeScript($"setContent({encoded})"); }
        catch { /* Ignore JS errors during content injection. */ }
    }

    // ── Embedded editor HTML ──────────────────────────────────────────────────
    // CKEditor 5 Classic loaded from CDN with a <textarea> fallback when the
    // CDN is unreachable (offline usage).
    private const string EditorHtml = """
        <!DOCTYPE html>
        <html>
        <head>
          <meta charset="UTF-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <style>
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body { font-family: system-ui, sans-serif; overflow: hidden; }
            .ck-editor__editable { min-height: 80px !important; }
            #fallback {
              display: none; width: 100%; height: 100%; min-height: 80px;
              padding: 8px; border: 1px solid #ccc; resize: vertical;
              font-family: system-ui; font-size: 14px;
            }
          </style>
        </head>
        <body>
          <div id="editor"></div>
          <textarea id="fallback" oninput="sendToHost(this.value)"></textarea>
          <script>
            var editor = null;
            var useFallback = false;

            function initFallback() {
              useFallback = true;
              document.getElementById('fallback').style.display = 'block';
              document.getElementById('editor').style.display = 'none';
            }

            function setContent(html) {
              if (useFallback) {
                document.getElementById('fallback').value = html;
              } else if (editor) {
                editor.setData(html);
              } else {
                window.__pendingContent = html;
              }
            }

            function getContent() {
              if (useFallback) return document.getElementById('fallback').value;
              return editor ? editor.getData() : '';
            }

            function sendToHost(message) {
              try {
                if (window.chrome && window.chrome.webview) {
                  window.chrome.webview.postMessage(message);
                  return;
                }
              } catch (e) {}
              try { window.parent.postMessage(message, '*'); } catch (e) {}
            }

            var script = document.createElement('script');
            script.src = 'https://cdn.ckeditor.com/ckeditor5/41.3.1/classic/ckeditor.js';
            script.onload = function () {
              ClassicEditor.create(document.querySelector('#editor'), {
                toolbar: [
                  'bold', 'italic', '|',
                  'bulletedList', 'numberedList', '|',
                  'blockQuote', 'link', '|',
                  'undo', 'redo'
                ]
              }).then(function (ed) {
                editor = ed;
                editor.model.document.on('change:data', function () {
                  sendToHost(editor.getData());
                });
                if (window.__pendingContent !== undefined) {
                  editor.setData(window.__pendingContent);
                  delete window.__pendingContent;
                }
              }).catch(function (err) {
                console.error('CKEditor init error:', err);
                initFallback();
              });
            };
            script.onerror = function () { initFallback(); };
            document.head.appendChild(script);
          </script>
        </body>
        </html>
        """;
}
