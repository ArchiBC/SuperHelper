using Eto.Forms;
using Grasshopper;
using Grasshopper.GUI;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace SuperHelper;

/// <summary>
/// The only platform-hosting boundary in SuperHelper. The application UI is
/// pure Eto; Grasshopper owns the conversion into its editor control tree.
/// </summary>
internal sealed class GrasshopperPanelHost
{
    private readonly Eto.Forms.Control _etoControl;
    private System.Windows.Forms.Control _nativeHost;
    private GH_Splitter _splitter;

    public GrasshopperPanelHost(Eto.Forms.Control etoControl)
    {
        _etoControl = etoControl ?? throw new ArgumentNullException(nameof(etoControl));
    }

    public bool IsAttached => _nativeHost != null && !_nativeHost.IsDisposed;

    public bool Visible => IsAttached && _nativeHost.Visible;

    public int Width
    {
        get => IsAttached ? _nativeHost.Width : 0;
        set
        {
            if (IsAttached)
            {
                _nativeHost.Width = Math.Max(50, value);
            }
        }
    }

    public bool DockOnRight
    {
        get => !IsAttached || _nativeHost.Dock == DockStyle.Right;
        set
        {
            if (!IsAttached)
            {
                return;
            }

            var dock = value ? DockStyle.Right : DockStyle.Left;
            _nativeHost.Dock = dock;
            _splitter.Dock = dock;
            _nativeHost.BringToFront();
            _splitter.BringToFront();
        }
    }

    public void Attach(GH_DocumentEditor editor, int width, bool dockOnRight)
    {
        if (IsAttached)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(editor);
        var target = Instances.ActiveCanvas?.Parent ?? (editor.Controls.Count > 0 ? editor.Controls[0] : editor);

        _nativeHost = Grasshopper.EtoExtensions.ToSwf(_etoControl, attach: true);
        _nativeHost.Name = "SuperHelper Eto Host";
        _nativeHost.Width = Math.Max(50, width);

        _splitter = new GH_Splitter
        {
            Cursor = System.Windows.Forms.Cursors.VSplit,
            Margin = new Padding(0),
            MaxSize = 8000,
            MinSize = 50,
            Name = "SuperHelper Splitter",
            Size = new Size(8, target.Height),
        };
        _splitter.MouseDown += (_, _) => _splitter.MaxSize = Math.Max(50, target.Width - 80);

        target.Controls.Add(_splitter);
        target.Controls.Add(_nativeHost);
        DockOnRight = dockOnRight;
    }

    public void Show()
    {
        if (!IsAttached)
        {
            return;
        }

        _nativeHost.Show();
        _splitter.Show();
        _nativeHost.BringToFront();
        _splitter.BringToFront();
    }

    public void Hide()
    {
        if (!IsAttached)
        {
            return;
        }

        _nativeHost.Hide();
        _splitter.Hide();
    }

}
