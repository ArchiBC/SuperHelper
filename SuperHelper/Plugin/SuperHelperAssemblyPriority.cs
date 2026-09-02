using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace SuperHelper;

public sealed class SuperHelperAssemblyPriority : GH_AssemblyPriority
{
    private const string ToolbarButtonName = "SuperHelperWorkspaceButton";
    private const string ToolbarButtonDescription = "SuperHelper workspace";
    private const string ViewMenuCommandName = "SuperHelperWorkspaceCommand";
    private static readonly Lazy<GrasshopperPanelHost> PanelHostFactory =
        new(() => new GrasshopperPanelHost(MenuReplacer.Control));
    private static SuperHelperAssemblyPriority _instance;

    private readonly HashSet<ToolStripButton> _workspaceButtons = [];
    private readonly HashSet<ToolStripMenuItem> _workspaceCommands = [];
    private GH_DocumentEditor _editor;

    private static GrasshopperPanelHost PanelHost => PanelHostFactory.Value;

    public override GH_LoadingInstruction PriorityLoad()
    {
        _instance = this;
        SuperSetting.OpenChanged += OnOpenChanged;
        Instances.CanvasCreated += OnCanvasCreated;
        return GH_LoadingInstruction.Proceed;
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
        if (SuperSetting.Open)
        {
            SuperSetting.Open = false;
            return;
        }

        _instance?.ApplyOpenState(false);
    }

    public static void Show()
    {
        if (!SuperSetting.Open)
        {
            SuperSetting.Open = true;
            return;
        }

        _instance?.ApplyOpenState(true);
    }

    private void OnCanvasCreated(GH_Canvas canvas)
    {
        Instances.CanvasCreated -= OnCanvasCreated;
        if (Instances.DocumentEditor is { } editor)
        {
            InitializeEditor(editor);
            return;
        }

        canvas.DocumentChanged += OnFirstDocumentChanged;
    }

    private void OnFirstDocumentChanged(GH_Canvas sender, GH_CanvasDocumentChangedEventArgs e)
    {
        sender.DocumentChanged -= OnFirstDocumentChanged;
        if (Instances.DocumentEditor is { } editor)
        {
            InitializeEditor(editor);
        }
    }

    private void InitializeEditor(GH_DocumentEditor editor)
    {
        if (ReferenceEquals(_editor, editor))
        {
            return;
        }

        _editor = editor;
        EnsureNativeCommands(editor);
        editor.Shown += EditorShown;
        if (editor.IsHandleCreated)
        {
            editor.BeginInvoke((Action)(() => EnsureNativeCommands(editor)));
        }

        MenuReplacer.Init();
        PanelHost.Attach(editor, SuperHelperPanelWidth, IsSuperHelperOnRight);
        ApplyOpenState(SuperSetting.Open);

        editor.FormClosing += (_, _) =>
        {
            SuperHelperPanelWidth = PanelHost.Width;
            MenuReplacer.Control.OnWorkspaceHidden();
            MenuReplacer.Control.ShutdownWorkspace();
        };
        Rhino.RhinoDoc.EndOpenDocument += (_, _) => RefreshViewport();
        Rhino.RhinoDoc.EndSaveDocument += (_, _) => RefreshViewport();
    }

    private void EditorShown(object sender, EventArgs e)
    {
        if (sender is GH_DocumentEditor editor)
        {
            EnsureNativeCommands(editor);
        }
    }

    private void EnsureNativeCommands(GH_DocumentEditor editor)
    {
        EnsureToolbarButton(editor);
        EnsureViewMenuCommand(editor);
        UpdateCommandState(SuperSetting.Open);
    }

    private void EnsureToolbarButton(GH_DocumentEditor editor)
    {
        var toolbar = Descendants(editor)
            .OfType<ToolStrip>()
            .FirstOrDefault(strip => string.Equals(strip.Name, "CanvasToolbar", StringComparison.Ordinal));
        if (toolbar == null)
        {
            return;
        }

        var button = toolbar.Items
            .OfType<ToolStripButton>()
            .FirstOrDefault(item => string.Equals(item.Name, ToolbarButtonName, StringComparison.Ordinal));
        if (button == null)
        {
            toolbar.Items.Add(new ToolStripSeparator
            {
                Margin = new Padding(2, 0, 2, 0),
                Size = new System.Drawing.Size(6, 40),
            });
            button = new ToolStripButton(Properties.Resources.SuperHelperIcon_24)
            {
                Name = ToolbarButtonName,
                ToolTipText = ToolbarButtonDescription,
                CheckOnClick = false,
                DisplayStyle = ToolStripItemDisplayStyle.Image,
            };
            toolbar.Items.Add(button);
        }

        button.Click -= WorkspaceCommandClick;
        button.Click += WorkspaceCommandClick;
        button.MouseDown -= ToolbarButtonMouseDown;
        button.MouseDown += ToolbarButtonMouseDown;
        _workspaceButtons.Add(button);
    }

    private void EnsureViewMenuCommand(GH_DocumentEditor editor)
    {
        if (editor.MainMenuStrip?.Items.Count <= 2 ||
            editor.MainMenuStrip.Items[2] is not ToolStripMenuItem viewMenu)
        {
            return;
        }

        var command = viewMenu.DropDownItems
            .OfType<ToolStripMenuItem>()
            .FirstOrDefault(item => string.Equals(item.Name, ViewMenuCommandName, StringComparison.Ordinal));
        if (command == null)
        {
            command = new ToolStripMenuItem("SuperHelper R9", Properties.Resources.SuperHelperIcon_24)
            {
                Name = ViewMenuCommandName,
                ToolTipText = "Show or hide the SuperHelper workspace",
                CheckOnClick = false,
            };
            viewMenu.DropDownItems.Insert(Math.Min(3, viewMenu.DropDownItems.Count), command);
        }

        command.Click -= WorkspaceCommandClick;
        command.Click += WorkspaceCommandClick;
        _workspaceCommands.Add(command);
    }

    private static void WorkspaceCommandClick(object sender, EventArgs e) =>
        SuperSetting.Open = !SuperSetting.Open;

    private static void ToolbarButtonMouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || sender is not ToolStripButton)
        {
            return;
        }

        var menu = BuildWorkspaceContextMenu();
        WinFormsDropDownLifetime.DisposeOnIdleAfterClosed(menu);
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

    private static void EnsureWorkspaceVisible() => Show();

    private void OnOpenChanged(bool open) => ApplyOpenState(open);

    private void ApplyOpenState(bool open)
    {
        UpdateCommandState(open);
        if (open)
        {
            PanelHost.Show();
            MenuReplacer.Control.OnWorkspaceShown();
            RefreshViewport();
            return;
        }

        MenuReplacer.Control.OnWorkspaceHidden();
        PanelHost.Hide();
        MenuReplacer.Control.OnClosed();
    }

    private void UpdateCommandState(bool open)
    {
        foreach (var button in _workspaceButtons.Where(button => !button.IsDisposed))
        {
            button.Checked = open;
        }
        foreach (var command in _workspaceCommands.Where(command => !command.IsDisposed))
        {
            command.Checked = open;
        }
    }

    private static void RefreshViewport() =>
        Rhino.RhinoApp.InvokeOnUiThread((Action)(() => MenuReplacer.Control.ResetViewport()));

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
}
