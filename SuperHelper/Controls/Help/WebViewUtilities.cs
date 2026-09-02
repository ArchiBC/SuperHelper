using Eto.Forms;

namespace SuperHelper;

internal static class WebViewUtilities
{
    public static void Configure(WebView webView)
    {
        webView.OpenNewWindow += (_, e) =>
        {
            e.Cancel = true;
            if (e.Uri != null)
            {
                webView.Url = e.Uri;
            }
        };
    }
}
