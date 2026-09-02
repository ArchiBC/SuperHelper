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
    private GH_DocumentEditor _editor;
    private System.Windows.Forms.Control _target;
    private System.Windows.Forms.Control _nativeHost;
    private System.Windows.Forms.Panel _hostShell;
    private GH_Splitter _splitter;

    public GrasshopperPanelHost(Eto.Forms.Control etoControl)
    {
        _etoControl = etoControl ?? throw new ArgumentNullException(nameof(etoControl));
    }

    public bool IsAttached => _hostShell != null && !_hostShell.IsDisposed;

    public bool Visible => IsAttached && _hostShell.Visible;

    public int Width
    {
        get => IsAttached ? _hostShell.Width : 0;
        set
        {
            if (IsAttached)
            {
                _hostShell.Width = Math.Max(50, value);
            }
        }
    }

    public bool DockOnRight
    {
        get => !IsAttached || _hostShell.Dock == DockStyle.Right;
        set
        {
            if (!IsAttached)
            {
                return;
            }

            var dock = value ? DockStyle.Right : DockStyle.Left;
            _hostShell.Dock = dock;
            _splitter.Dock = dock;
            _hostShell.BringToFront();
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
        _editor = editor;
        _target = target;

        _nativeHost = Grasshopper.EtoExtensions.ToSwf(_etoControl, attach: true);
        _nativeHost.Name = "SuperHelper Eto Host";
        _nativeHost.Dock = DockStyle.Fill;

        _hostShell = new System.Windows.Forms.Panel
        {
            Margin = Padding.Empty,
            Name = "SuperHelper Host Shell",
            Padding = Padding.Empty,
            Width = UsableInitialWidth(width, target.ClientSize.Width),
        };
        _hostShell.Controls.Add(_nativeHost);

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
        target.Controls.Add(_hostShell);
        DockOnRight = dockOnRight;

        void refreshBottomInset(object sender, EventArgs args)
        {
            EnsureHostFitsTarget(target);
            UpdateBottomInset(editor, target);
        }
        target.SizeChanged += refreshBottomInset;
        editor.SizeChanged += refreshBottomInset;
        editor.Layout += refreshBottomInset;
        if (editor.IsHandleCreated)
        {
            editor.BeginInvoke((Action)(() => UpdateBottomInset(editor, target)));
        }
        else
        {
            void initializeBottomInset(object sender, EventArgs args)
            {
                editor.HandleCreated -= initializeBottomInset;
                editor.BeginInvoke((Action)(() => UpdateBottomInset(editor, target)));
            }
            editor.HandleCreated += initializeBottomInset;
        }
    }

    public void Show()
    {
        if (!IsAttached)
        {
            return;
        }

        _hostShell.Show();
        _splitter.Show();
        if (_target != null)
        {
            EnsureHostFitsTarget(_target);
        }
        _hostShell.BringToFront();
        _splitter.BringToFront();
        if (!_hostShell.IsDisposed && _editor != null && _target != null)
        {
            UpdateBottomInset(_editor, _target);
            if (_editor.IsHandleCreated)
            {
                _editor.BeginInvoke((Action)(() => UpdateBottomInset(_editor, _target)));
            }
        }
    }

    public void Hide()
    {
        if (!IsAttached)
        {
            return;
        }

        _hostShell.Hide();
        _splitter.Hide();
    }

    private void UpdateBottomInset(GH_DocumentEditor editor, System.Windows.Forms.Control target)
    {
        if (!IsAttached || !target.IsHandleCreated || target.ClientSize.Height < 1)
        {
            return;
        }

        var targetBounds = target.RectangleToScreen(target.ClientRectangle);
        var bottomInset = 0;
        foreach (var candidate in Descendants(editor))
        {
            if (!candidate.Visible
                || candidate.IsDisposed
                || ReferenceEquals(candidate, _hostShell)
                || IsDescendantOf(candidate, _hostShell))
            {
                continue;
            }

            var typeName = candidate.GetType().Name;
            var statusLike = candidate is StatusStrip
                || typeName.Contains("Status", StringComparison.OrdinalIgnoreCase)
                || candidate.Name.Contains("Status", StringComparison.OrdinalIgnoreCase);
            var bottomBarLike = candidate.Dock == DockStyle.Bottom
                && candidate.Height <= Math.Max(96, editor.ClientSize.Height / 8);
            if (!statusLike && !bottomBarLike)
            {
                continue;
            }

            var bounds = candidate.RectangleToScreen(candidate.ClientRectangle);
            var horizontalOverlap = Math.Min(targetBounds.Right, bounds.Right)
                - Math.Max(targetBounds.Left, bounds.Left);
            if (horizontalOverlap < Math.Min(targetBounds.Width, bounds.Width) / 2)
            {
                continue;
            }

            var overlap = Math.Min(targetBounds.Bottom, bounds.Bottom)
                - Math.Max(targetBounds.Top, bounds.Top);
            if (overlap > 0 && bounds.Top >= targetBounds.Top + targetBounds.Height / 2)
            {
                bottomInset = Math.Max(bottomInset, overlap);
            }
        }

        var padding = _hostShell.Padding;
        if (padding.Bottom != bottomInset)
        {
            _hostShell.Padding = new Padding(padding.Left, padding.Top, padding.Right, bottomInset);
        }
    }

    private void EnsureHostFitsTarget(System.Windows.Forms.Control target)
    {
        if (!IsAttached || target.ClientSize.Width < 100)
        {
            return;
        }

        var maximumUsableWidth = Math.Max(50, target.ClientSize.Width - 80);
        if (_hostShell.Width > maximumUsableWidth)
        {
            _hostShell.Width = Math.Max(50, (int)Math.Round(target.ClientSize.Width * 0.6));
            target.PerformLayout();
        }
    }

    private static int UsableInitialWidth(int requestedWidth, int targetWidth)
    {
        var width = Math.Max(50, requestedWidth);
        if (targetWidth >= 100 && width > targetWidth - 80)
        {
            return Math.Max(50, (int)Math.Round(targetWidth * 0.6));
        }

        return width;
    }

    private static bool IsDescendantOf(
        System.Windows.Forms.Control candidate,
        System.Windows.Forms.Control ancestor)
    {
        for (var parent = candidate.Parent; parent != null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static System.Collections.Generic.IEnumerable<System.Windows.Forms.Control> Descendants(
        System.Windows.Forms.Control root)
    {
        foreach (System.Windows.Forms.Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

}
