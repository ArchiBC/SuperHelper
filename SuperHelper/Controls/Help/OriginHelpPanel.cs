using Eto.Forms;
using System;

namespace SuperHelper;

internal sealed class OriginHelpPanel : Panel
{
    private readonly WebView _webView = new();
    private long _revision;

    public OriginHelpPanel()
    {
        WebViewUtilities.Configure(_webView);
        Content = _webView;
    }

    public void SetHtml(string html)
    {
        html = string.IsNullOrWhiteSpace(html)
            ? "We're sorry. Help is not yet available for this object"
            : html;

        if (html.StartsWith("GOTO:", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(html[5..], UriKind.Absolute, out var url))
        {
            _webView.Url = url;
            return;
        }

        // A unique inert base URI prevents WebView from coalescing consecutive
        // LoadHtml calls for different Grasshopper objects.
        _webView.LoadHtml(html, new Uri($"https://superhelper.invalid/origin/{++_revision}/"));
    }
}
