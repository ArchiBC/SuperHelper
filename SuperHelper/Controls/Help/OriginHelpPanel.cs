using Eto.Forms;
using System;

namespace SuperHelper;

internal sealed class OriginHelpPanel : Panel
{
    private readonly WebView _webView = new();

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

        // Eto's WebView2 handler maps every non-null base URI to the same
        // local server URL, so later assignments do not trigger navigation.
        // Loading HTML directly refreshes the document for each GH object.
        _webView.LoadHtml(html);
    }
}
