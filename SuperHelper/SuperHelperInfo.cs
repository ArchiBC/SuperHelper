using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SuperHelper;

public class SuperHelperInfo : GH_AssemblyInfo
{
    public override string Name => "SuperHelper R9";
    public override Bitmap Icon => Properties.Resources.SuperHelperIcon_24;
    public override string Description => "Much better helper control for grasshopper! The original author is 秋水";
    public override Guid Id => new("A71D5B0A-9D5B-4E27-8933-BB83CB68066D");
    public override string AuthorName => "ArchiBC";
    public override string AuthorContact => "Archi-bc@outlook.com";
    public override string Version => "2.0.3.1";
}

partial class SimpleAssemblyPriority
{
    private const string ToolbarButtonDescription = "SuperHelper workspace";
    private static readonly GrasshopperPanelHost PanelHost = new(MenuReplacer.Control);
    private readonly HashSet<ToolStripButton> _workspaceMenuButtons = [];

    protected override int? MenuIndex => 2;

    public static bool UseSuperHelperPanel
    {
        get => Instances.Settings.GetValue(nameof(UseSuperHelperPanel), true);
        set => Instances.Settings.SetValue(nameof(UseSuperHelperPanel), value);
    }

    public static int SuperHelperPanelWidth
    {
        get
        {
            var width = Instances.Settings.GetValue(nameof(SuperHelperPanelWidth), 400);
            return width < 50 ? 400 : width;
        }
        set => Instances.Settings.SetValue(nameof(SuperHelperPanelWidth), Math.Max(50, value));
    }

    public static bool IsSuperHelperOnRight
    {
        get => Instances.Settings.GetValue(nameof(IsSuperHelperOnRight), true);
        set => Instances.Settings.SetValue(nameof(IsSuperHelperOnRight), value);
    }

    public static void SwitchSide()
    {
        IsSuperHelperOnRight = !IsSuperHelperOnRight;
        PanelHost.DockOnRight = IsSuperHelperOnRight;
    }

    public static void Hide()
    {
        PanelHost.Hide();
        MenuReplacer.Control.OnClosed();
        UseSuperHelperPanel = false;
    }

    public static void Show()
    {
        PanelHost.Show();
        UseSuperHelperPanel = true;
        RefreshViewport();
    }

    private static void RefreshViewport()
    {
        Rhino.RhinoApp.InvokeOnUiThread((Action)(() => MenuReplacer.Control.ResetViewport()));
    }

    protected override void DoWithEditor(GH_DocumentEditor editor)
    {
        base.DoWithEditor(editor);
        AttachWorkspaceContextMenu(editor);

        MenuReplacer.Init();
        PanelHost.Attach(editor, SuperHelperPanelWidth, IsSuperHelperOnRight);
        if (UseSuperHelperPanel)
        {
            PanelHost.Show();
        }
        else
        {
            PanelHost.Hide();
        }

        editor.FormClosing += (_, _) =>
        {
            SuperHelperPanelWidth = PanelHost.Width;
            MenuReplacer.Control.ShutdownWorkspace();
            DetachWorkspaceContextMenus();
        };
        Rhino.RhinoDoc.EndOpenDocument += RhinoDoc_EndOpenDocument;
        Rhino.RhinoDoc.EndSaveDocument += RhinoDoc_EndSaveDocument;
    }

    private void RhinoDoc_EndSaveDocument(object sender, Rhino.DocumentSaveEventArgs e) => RefreshViewport();
    private void RhinoDoc_EndOpenDocument(object sender, Rhino.DocumentOpenEventArgs e) => RefreshViewport();

    private void AttachWorkspaceContextMenu(GH_DocumentEditor editor)
    {
        var button = Descendants(editor)
            .OfType<ToolStrip>()
            .SelectMany(strip => strip.Items.OfType<ToolStripButton>())
            .FirstOrDefault(item => string.Equals(
                item.ToolTipText,
                ToolbarButtonDescription,
                StringComparison.Ordinal));
        if (button == null || !_workspaceMenuButtons.Add(button))
        {
            return;
        }

        button.MouseUp += ToolbarButton_MouseUp;
    }

    private void DetachWorkspaceContextMenus()
    {
        foreach (var button in _workspaceMenuButtons)
        {
            button.MouseUp -= ToolbarButton_MouseUp;
        }
        _workspaceMenuButtons.Clear();
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static void ToolbarButton_MouseUp(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || sender is not ToolStripButton button)
        {
            return;
        }

        var menu = BuildWorkspaceContextMenu();
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(button.Owner, button.Owner.PointToClient(Cursor.Position));
    }

    private static ContextMenuStrip BuildWorkspaceContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(CreateMenuItem("Open Settings", control => control.OpenWorkspaceSettings()));
        return menu;
    }

    private static ToolStripMenuItem CreateMenuItem(string text, Action<SuperHelperControl> action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) =>
        {
            if (!SuperSetting.Open)
            {
                SuperSetting.Open = true;
            }
            else
            {
                Show();
            }
            action(MenuReplacer.Control);
        };
        return item;
    }
}
