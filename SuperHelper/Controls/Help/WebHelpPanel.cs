using Eto.Drawing;
using Eto.Forms;
using System;

namespace SuperHelper;

internal sealed class WebHelpPanel : Panel
{
    private readonly WebView _webView = new();
    private readonly TextBox _address = new();
    private readonly DropDown _source = new();
    private HelpObject _currentObject;

    public WebHelpPanel()
    {
        WebViewUtilities.Configure(_webView);
        Content = BuildContent();
        _address.Text = HelperHost.Current.OfficialHelpRoot;
        _webView.Url = new Uri(_address.Text);
    }

    public void SetObject(HelpObject obj, string webUrl, bool navigate)
    {
        _currentObject = obj;
        _address.Text = webUrl ?? string.Empty;
        if (navigate && Uri.TryCreate(webUrl, UriKind.Absolute, out var url))
        {
            _webView.Url = url;
        }
    }

    private Control BuildContent()
    {
        var back = new Button { Text = "←", Width = 50 };
        var forward = new Button { Text = "→", Width = 50 };
        var save = new Button { Text = "Save", Width = 50 };
        var go = new Button { Text = "Go", Width = 50 };

        foreach (var label in WebHelpMappings.SourceLabels)
        {
            _source.Items.Add(label);
        }
        _source.SelectedIndex = (int)WebHelpMappings.CurrentSource;
        _source.Width = 145;
        _source.SelectedIndexChanged += (_, _) =>
        {
            if (_source.SelectedIndex < 0)
            {
                return;
            }

            WebHelpMappings.CurrentSource = (WebHelpSource)_source.SelectedIndex;
            HelperHost.Current.RefreshHelp();
        };

        back.Click += (_, _) =>
        {
            if (_webView.CanGoBack)
            {
                _webView.GoBack();
            }
        };
        forward.Click += (_, _) =>
        {
            if (_webView.CanGoForward)
            {
                _webView.GoForward();
            }
        };
        save.Click += (_, _) => SaveCurrentUrl();
        go.Click += (_, _) => NavigateFromAddressBar();
        _address.KeyDown += (_, e) =>
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
                new TableRow(
                    back,
                    forward,
                    _source,
                    new Label { Text = "Url:", VerticalAlignment = VerticalAlignment.Center },
                    new TableCell(_address, true),
                    save,
                    go),
            },
        };
        return new TableLayout
        {
            Rows =
            {
                new TableRow(toolbar),
                new TableRow(new TableCell(_webView, true)) { ScaleHeight = true },
            },
        };
    }

    private void NavigateFromAddressBar()
    {
        if (Uri.TryCreate(_address.Text, UriKind.Absolute, out var url))
        {
            _webView.Url = url;
            return;
        }

        MessageBox.Show(this, "输入的 URL 无效。", "SuperHelper", MessageBoxType.Error);
    }

    private void SaveCurrentUrl()
    {
        if (_currentObject != null)
        {
            WebHelpMappings.SaveCurrent(_currentObject, _address.Text);
        }
    }
}
