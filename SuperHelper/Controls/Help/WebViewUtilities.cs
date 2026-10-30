using Eto.Forms;

namespace SuperHelper;

internal static class WebViewUtilities
{
    public static void Configure(WebView webView)
    {
        webView.DocumentLoading += (_, e) =>
        {
            if (HelperHost.Current.TryOpenHelpLink(e.Uri)) e.Cancel = true;
        };
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
