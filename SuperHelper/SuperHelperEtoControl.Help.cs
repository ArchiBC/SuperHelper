using Eto.Drawing;
using Eto.Forms;
using System;

namespace SuperHelper;

public sealed partial class SuperHelperControl
{
    private Control BuildWebHelpPage()
    {
        var back = new Button { Text = "←", Width = 50 };
        var forward = new Button { Text = "→", Width = 50 };
        var save = new Button { Text = "Save", Width = 50 };
        var go = new Button { Text = "Go", Width = 50 };

        foreach (var label in WebHelpMappings.SourceLabels)
        {
            _webHelpSource.Items.Add(label);
        }
        _webHelpSource.SelectedIndex = (int)WebHelpMappings.CurrentSource;
        _webHelpSource.Width = 145;
        _webHelpSource.SelectedIndexChanged += (_, _) =>
        {
            if (_webHelpSource.SelectedIndex < 0)
            {
                return;
            }
            WebHelpMappings.CurrentSource = (WebHelpSource)_webHelpSource.SelectedIndex;
            MenuReplacer.RefreshCurrentObject();
        };

        back.Click += (_, _) =>
        {
            if (_webHelp.CanGoBack)
            {
                _webHelp.GoBack();
            }
        };
        forward.Click += (_, _) =>
        {
            if (_webHelp.CanGoForward)
            {
                _webHelp.GoForward();
            }
        };
        save.Click += (_, _) => SaveWebUrl();
        go.Click += (_, _) => NavigateFromAddressBar();
        _urlTextBox.KeyDown += (_, e) =>
        {
            if (e.Key == Keys.Enter)
            {
                NavigateFromAddressBar();
                e.Handled = true;
            }
        };

        var toolbar = new TableLayout
        {
            Padding = new Padding(3, 1),
            Spacing = new Size(4, 0),
            Rows =
            {
                new TableRow(back, forward, _webHelpSource,
                    new Label { Text = "Url:", VerticalAlignment = VerticalAlignment.Center },
                    new TableCell(_urlTextBox, true), save, go),
            },
        };

        return new TableLayout
        {
            Rows =
            {
                new TableRow(toolbar),
                new TableRow(new TableCell(_webHelp, true)) { ScaleHeight = true },
            },
        };
    }

    private static void ConfigureWebView(WebView webView)
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

    private void UpdateOriginHelp(string html)
    {
        html = string.IsNullOrWhiteSpace(html)
            ? "We're sorry. Help is not yet available for this object"
            : html;

        if (html.StartsWith("GOTO:", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(html[5..], UriKind.Absolute, out var url))
        {
            _originHelp.Url = url;
            return;
        }

        // WebView2/WebKit may coalesce consecutive LoadHtml calls that use
        // the same about:blank base URL. A unique, inert base URI makes every
        // GH selection a distinct navigation while preserving relative links.
        var revision = ++_originHelpRevision;
        _originHelp.LoadHtml(html, new Uri($"https://superhelper.invalid/origin/{revision}/"));
    }

    private void UpdateWebHelp(string webUrl, bool navigate)
    {
        _urlTextBox.Text = webUrl ?? string.Empty;
        if (navigate && Uri.TryCreate(webUrl, UriKind.Absolute, out var url))
        {
            _webHelp.Url = url;
        }
    }

    private void NavigateFromAddressBar()
    {
        if (Uri.TryCreate(_urlTextBox.Text, UriKind.Absolute, out var url))
        {
            _webHelp.Url = url;
            return;
        }

        MessageBox.Show(this, "输入的 URL 无效。", "SuperHelper", MessageBoxType.Error);
    }

    private void SaveWebUrl()
    {
        if (_currentObject == null)
        {
            return;
        }

        WebHelpMappings.SaveCurrent(_currentObject, _urlTextBox.Text);
    }
}
