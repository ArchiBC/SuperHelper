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
    private static readonly Lazy<GrasshopperPanelHost> PanelHostFactory =
        new(() => new GrasshopperPanelHost(MenuReplacer.Control));
    private readonly HashSet<ToolStripButton> _workspaceMenuButtons = [];

    private static GrasshopperPanelHost PanelHost => PanelHostFactory.Value;

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
        MenuReplacer.Control.OnWorkspaceHidden();
        PanelHost.Hide();
        MenuReplacer.Control.OnClosed();
        UseSuperHelperPanel = false;
    }

    public static void Show()
    {
        PanelHost.Show();
        UseSuperHelperPanel = true;
        MenuReplacer.Control.OnWorkspaceShown();
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
        editor.Shown += Editor_Shown;
        if (editor.IsHandleCreated)
        {
            editor.BeginInvoke((Action)(() => AttachWorkspaceContextMenu(editor)));
        }

        MenuReplacer.Init();
        PanelHost.Attach(editor, SuperHelperPanelWidth, IsSuperHelperOnRight);
        if (UseSuperHelperPanel)
        {
            PanelHost.Show();
            MenuReplacer.Control.OnWorkspaceShown();
        }
        else
        {
            MenuReplacer.Control.OnWorkspaceHidden();
            PanelHost.Hide();
        }

        editor.FormClosing += (_, _) =>
        {
            SuperHelperPanelWidth = PanelHost.Width;
            MenuReplacer.Control.OnWorkspaceHidden();
            MenuReplacer.Control.ShutdownWorkspace();
            editor.Shown -= Editor_Shown;
            DetachWorkspaceContextMenus();
        };
        Rhino.RhinoDoc.EndOpenDocument += RhinoDoc_EndOpenDocument;
        Rhino.RhinoDoc.EndSaveDocument += RhinoDoc_EndSaveDocument;
    }

    private void RhinoDoc_EndSaveDocument(object sender, Rhino.DocumentSaveEventArgs e) => RefreshViewport();
    private void RhinoDoc_EndOpenDocument(object sender, Rhino.DocumentOpenEventArgs e) => RefreshViewport();

    private void Editor_Shown(object sender, EventArgs e)
    {
        if (sender is GH_DocumentEditor editor)
        {
            AttachWorkspaceContextMenu(editor);
        }
    }

    private void AttachWorkspaceContextMenu(GH_DocumentEditor editor)
    {
        var buttons = Descendants(editor)
            .OfType<ToolStrip>()
            .SelectMany(strip => strip.Items.OfType<ToolStripButton>())
            .Where(item => string.Equals(
                item.ToolTipText,
                ToolbarButtonDescription,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var button in buttons)
        {
            if (_workspaceMenuButtons.Add(button))
            {
                button.MouseDown += ToolbarButton_MouseDown;
            }
        }
    }

    private void DetachWorkspaceContextMenus()
    {
        foreach (var button in _workspaceMenuButtons)
        {
            button.MouseDown -= ToolbarButton_MouseDown;
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

    private static void ToolbarButton_MouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || sender is not ToolStripButton button)
        {
            return;
        }

        var menu = BuildWorkspaceContextMenu();
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(Cursor.Position);
    }

    private static ContextMenuStrip BuildWorkspaceContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(CreateMenuItem("Open Settings", control => control.OpenWorkspaceSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateSectionMenuItem("Command", EmbeddedWorkspaceSection.Command));
        menu.Items.Add(CreateSectionMenuItem("Viewport", EmbeddedWorkspaceSection.Viewport));
        menu.Items.Add(CreateSectionMenuItem("Help", EmbeddedWorkspaceSection.Help));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateMenuItem("Restore Default Workspace", control => control.RestoreDefaultWorkspace()));
        menu.Items.Add(CreateMenuItem("Use Current as Default", control => control.UseCurrentWorkspaceAsDefault()));
        menu.Items.Add(CreateMenuItem("Restore Built-in Workspace", control => control.RestoreBuiltInWorkspace()));
        return menu;
    }

    private static ToolStripMenuItem CreateSectionMenuItem(string text, EmbeddedWorkspaceSection section)
    {
        var control = MenuReplacer.Control;
        var item = new ToolStripMenuItem(text)
        {
            Checked = control.IsWorkspaceSectionVisible(section),
            CheckOnClick = false,
        };
        item.Click += (_, _) =>
        {
            EnsureWorkspaceVisible();
            control = MenuReplacer.Control;
            var targetVisible = !control.IsWorkspaceSectionVisible(section);
            control.SetWorkspaceSectionVisible(section, targetVisible);
            item.Checked = control.IsWorkspaceSectionVisible(section);
        };
        return item;
    }

    private static ToolStripMenuItem CreateMenuItem(string text, Action<SuperHelperControl> action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) =>
        {
            EnsureWorkspaceVisible();
            action(MenuReplacer.Control);
        };
        return item;
    }

    private static void EnsureWorkspaceVisible()
    {
        if (!SuperSetting.Open)
        {
            SuperSetting.Open = true;
        }
        else
        {
            Show();
        }
    }
}
