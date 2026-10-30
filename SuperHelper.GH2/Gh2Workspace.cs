using Eto.Drawing;
using Eto.Forms;
using Grasshopper2.Doc;
using Grasshopper2.Documentation;
using Grasshopper2.UI;
using Grasshopper2.UI.Canvas;
using Grasshopper2.UI.Flex;
using System;
using System.Linq;
using Rhino;
using Canvas = Grasshopper2.UI.Canvas.Canvas;

namespace SuperHelper;

/// <summary>Owns only the Eto editor attachment and GH2 event subscriptions.</summary>
internal sealed class Gh2Workspace : IDisposable
{
    private Editor _editor;
    private Canvas _canvas;
    private Document _document;
    private Control _originalContent;
    private Splitter _splitter;
    private SuperHelperControl _control;
    private ButtonMenuItem _menu;
    private CheckMenuItem _toggle;
    private ButtonToolItem _button;
    private bool _disposed;
    private IDisposable _canvasInput;
    private bool _helpPending;
    private bool _refreshPending;
    private int _generation;
    private bool? _appliedOpen;
    private bool _appliedRight;
    private bool _applyingLayout;
    private int _panelWidth;
    private Editor _failedEditor;
    public SuperHelperControl Control => _control;
    public bool DockOnRight => HelperHost.Current.Settings.GetValue("IsSuperHelperOnRight", true);

    public void Start()
    {
        SuperSetting.OpenChanged += ApplyOpenState;
        RhinoApp.Idle += OnIdle;
        RhinoApp.Closing += OnRhinoClosing;
        OnIdle(null, EventArgs.Empty);
    }

    private void OnIdle(object sender, EventArgs e)
    {
        var editor = Editor.Instance;
        if (_disposed || editor == null || editor.IsDisposed || ReferenceEquals(editor, _editor) || ReferenceEquals(editor, _failedEditor)) return;
        Detach();
        try { Attach(editor); }
        catch (Exception exception)
        {
            RhinoApp.WriteLine("SuperHelper GH2 workspace could not be attached: " + exception);
            Detach();
            _failedEditor = editor;
        }
    }

    private void Attach(Editor editor)
    {
        _editor = editor;
        _canvas = editor.Canvas;
        _originalContent = editor.Content;
        _control = new SuperHelperControl();
        editor.Content = null;
        _splitter = new Splitter
        {
            Orientation = Orientation.Horizontal,
            FixedPanel = DockOnRight ? SplitterFixedPanel.Panel2 : SplitterFixedPanel.Panel1,
        };
        editor.Content = _splitter;
        _splitter.PositionChanged += OnSplitterMoved;
        editor.Closed += OnEditorClosed;
        _canvas.DocumentChanged += OnDocumentChanged;
        _canvas.MouseDown += OnCanvasMouseDown;
        try
        {
            _canvasInput = Rhino.Runtime.HostUtils.RunningOnOSX
                ? new Gh2MacCanvasInput(editor, _canvas)
                : new Gh2WindowsCanvasInput(editor, _canvas);
        }
        catch (Exception exception)
        {
            RhinoApp.WriteLine("SuperHelper GH2 canvas wheel binding failed: " + exception.GetBaseException().Message);
        }
        _canvas.KeyDown += OnCanvasKeyDown;
        _canvas.PopulateContextMenu += OnPopulateContextMenu;
        _canvas.DragEnter += OnExampleDrag;
        _canvas.DragOver += OnExampleDrag;
        _canvas.DragDrop += OnExampleDrop;
        AddCommands(editor);
        OnDocumentChanged(null, EventArgs.Empty);
        ApplyOpenState(SuperSetting.Open);
    }

    private void AddCommands(Editor editor)
    {
        _menu = new ButtonMenuItem { Text = "SuperHelper" };
        _toggle = new CheckMenuItem { Text = "Show Workspace" };
        _toggle.Click += (_, _) => SuperSetting.Open = !SuperSetting.Open;
        _menu.Items.Add(_toggle);
        AddMenu("Help for Selected Component", ShowSelectedHelp);
        AddMenu("Open Settings", () => Run(control => control.OpenWorkspaceSettings()));
        _menu.Items.Add(new SeparatorMenuItem());
        foreach (var section in Enum.GetValues<EmbeddedWorkspaceSection>())
        {
            var target = section;
            AddMenu("Toggle " + section, () => Run(control => control.SetWorkspaceSectionVisible(target, !control.IsWorkspaceSectionVisible(target))));
        }
        _menu.Items.Add(new SeparatorMenuItem());
        AddMenu("Switch Side", SwitchSide);
        AddMenu("Restore Default Workspace", () => Run(control => control.RestoreDefaultWorkspace()));
        AddMenu("Use Current as Default", () => Run(control => control.UseCurrentWorkspaceAsDefault()));
        AddMenu("Restore Built-in Workspace", () => Run(control => control.RestoreBuiltInWorkspace()));
        editor.Menu ??= new MenuBar();
        editor.Menu.Items.Add(_menu);
        _button = new ButtonToolItem
        {
            Text = "SuperHelper", ToolTip = "Show or hide the SuperHelper GH2 workspace",
            Image = EtoImageUtilities.FromBitmap(Properties.Resources.SuperHelperIcon_24),
        };
        _button.Click += (_, _) => SuperSetting.Open = !SuperSetting.Open;
        editor.ToolBar ??= new ToolBar();
        editor.ToolBar.Items.Add(_button);
    }

    private void AddMenu(string text, Action action)
    {
        var item = new ButtonMenuItem { Text = text };
        item.Click += (_, _) => action();
        _menu.Items.Add(item);
    }
    private void Run(Action<SuperHelperControl> action)
    {
        Show();
        if (_control != null) action(_control);
    }
    private void Show()
    {
        if (!SuperSetting.Open) SuperSetting.Open = true;
        else ApplyOpenState(true);
    }

    private void ApplyOpenState(bool open)
    {
        if (_splitter == null || _editor?.IsDisposed != false) return;
        _toggle.Checked = open;
        if (_appliedOpen == open && (!open || _appliedRight == DockOnRight)) return;
        SaveWidth();
        _applyingLayout = true;
        try
        {
            _control.OnWorkspaceHidden();
            _splitter.Panel1 = null;
            _splitter.Panel2 = null;
            if (!open)
            {
                _splitter.Panel1 = _originalContent;
                _control.OnClosed();
                return;
            }
            var width = Math.Clamp(HelperHost.Current.Settings.GetValue("SuperHelperPanelWidth", 450), 280, Math.Max(280, _editor.ClientSize.Width - 100));
            _splitter.FixedPanel = DockOnRight ? SplitterFixedPanel.Panel2 : SplitterFixedPanel.Panel1;
            _splitter.Panel1 = DockOnRight ? _originalContent : _control;
            _splitter.Panel2 = DockOnRight ? _control : _originalContent;
            _splitter.Position = DockOnRight ? Math.Max(100, _editor.ClientSize.Width - width) : width;
            _control.OnWorkspaceShown();
        }
        finally
        {
            _appliedOpen = open;
            _appliedRight = DockOnRight;
            _applyingLayout = false;
        }
    }

    private void OnSplitterMoved(object sender, EventArgs e)
    {
        if (!_applyingLayout && SuperSetting.Open && _control?.Width >= 280) _panelWidth = _control.Width;
    }
    private void SaveWidth()
    {
        if (_panelWidth >= 280) HelperHost.Current.Settings.SetValue("SuperHelperPanelWidth", _panelWidth);
    }
    public void SwitchSide()
    {
        HelperHost.Current.Settings.SetValue("IsSuperHelperOnRight", !DockOnRight);
        ApplyOpenState(SuperSetting.Open);
    }

    private void OnDocumentChanged(object sender, EventArgs e)
    {
        UnsubscribeDocument();
        ++_generation;
        _control?.ClearObject();
        _document = _canvas?.Document;
        if (_document == null) return;
        _document.Objects.ObjectSelectionChanged += OnSelectionChanged;
        _document.Objects.ObjectRemoved += OnObjectRemoved;
        _document.Solution.SolutionCompleted += OnSolutionCompleted;
        FollowSelection();
    }
    private void OnSelectionChanged(object sender, ObjectEventArgs e) => QueueFollowSelection();
    private void OnObjectRemoved(object sender, AfterRemoveObjectEventArgs e)
    {
        if (_control?.CurrentObject is { } current && _document?.Objects.Find(current.InstanceGuid) == null)
            _control.ClearObject();
    }
    private void QueueFollowSelection()
    {
        if (_helpPending) return;
        _helpPending = true;
        Application.Instance.AsyncInvoke(() => { _helpPending = false; FollowSelection(); });
    }
    private void FollowSelection()
    {
        if (!SuperSetting.Open || !SuperHelperPreferences.AutoTarget) return;
        var target = _document?.Objects.SelectedObjects.LastOrDefault();
        if (target != null && !ReferenceEquals(_control?.CurrentObject?.NativeObject, target)) SetObject(target);
    }
    private IDocumentObject Pick(PointF location)
    {
        if (_canvas?.Document == null) return null;
        var point = _canvas.Map(location, CoordinateSystem.Control, CoordinateSystem.Content);
        var pick = _canvas.ResolvePick(point, true, true, false, false, true);
        var obj = _document.Objects.Find(pick.ObjectUnderPick)
            ?? _document.Objects.FindParameter(pick.InletUnderPick)
            ?? _document.Objects.FindParameter(pick.OutletUnderPick);
        return obj?.FoundingObject ?? obj;
    }
    private void OnCanvasMouseDown(object sender, MouseEventArgs e)
    {
        if (!SuperSetting.Open || !SuperHelperPreferences.AutoTarget) return;
        var target = Pick(e.Location);
        if (target != null) Application.Instance.AsyncInvoke(() => { if (target.Document == _document) SetObject(target); });
    }
    private void OnCanvasKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Keys.F1) return;
        ShowSelectedHelp();
        e.Handled = true;
    }
    private void OnPopulateContextMenu(object sender, PopulateContextMenuEventArgs e)
    {
        if (e.Menu == null) return;
        var target = Pick(e.MouseEvent.Location) ?? _document?.Objects.SelectedObjects.LastOrDefault();
        if (target == null) return;
        var help = new ButtonMenuItem { Text = "SuperHelper Help" };
        help.Click += (_, _) => { Show(); SetObject(target); };
        e.Menu.Items.Add(new SeparatorMenuItem());
        e.Menu.Items.Add(help);
    }
    private void OnExampleDrag(object sender, DragEventArgs e)
    {
        if (Gh2Example.TryGetDragged(e.Data, out _) && e.AllowedEffects.HasFlag(DragEffects.Copy)) e.Effects = DragEffects.Copy;
    }
    private void OnExampleDrop(object sender, DragEventArgs e)
    {
        if (!Gh2Example.TryGetDragged(e.Data, out var source)) return;
        var point = _canvas.Map(e.Location, CoordinateSystem.Control, CoordinateSystem.Content);
        e.Effects = Gh2Example.Insert(source, _canvas, point) ? DragEffects.Copy : DragEffects.None;
    }
    public void ShowSelectedHelp()
    {
        var target = _document?.Objects.SelectedObjects.LastOrDefault() ?? _control?.CurrentObject?.NativeObject as IDocumentObject;
        if (target == null) return;
        Show();
        SetObject(target);
    }
    public void RefreshHelp()
    {
        if (_control?.CurrentObject?.NativeObject is IDocumentObject target) SetObject(target);
    }
    private void SetObject(IDocumentObject target)
    {
        if (_control == null || target.Document != _document) return;
        try
        {
            var model = Gh2ObjectInfo.Describe(target);
            var page = Gh2Documentation.Find(model.ComponentGuid);
            var html = page.Html ?? model.OriginHtml ?? Gh2ObjectInfo.FallbackHtml(target);
            _control.SaveExampleValue();
            var examples = HelperHost.Current.Examples.Items.TryGetValue(model.ComponentGuid.ToString(), out var customExamples)
                ? customExamples : page.Examples;
            _control.SetObject(model, html, WebHelpMappings.Resolve(model, html), examples);
        }
        catch (Exception exception) { RhinoApp.WriteLine("SuperHelper GH2 help failed: " + exception); }
    }
    private static bool FileExists(string path) => !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path);
    private void OnSolutionCompleted(object sender, SolutionEventArgs e)
    {
        if (_refreshPending) return;
        _refreshPending = true;
        var generation = _generation;
        Application.Instance.AsyncInvoke(() =>
        {
            _refreshPending = false;
            if (!_disposed && generation == _generation && SuperSetting.Open) _control?.RefreshObjectData();
        });
    }

    private void UnsubscribeDocument()
    {
        if (_document == null) return;
        _document.Objects.ObjectSelectionChanged -= OnSelectionChanged;
        _document.Objects.ObjectRemoved -= OnObjectRemoved;
        _document.Solution.SolutionCompleted -= OnSolutionCompleted;
        _document = null;
    }
    private void OnEditorClosed(object sender, EventArgs e) => Detach();
    private void Detach()
    {
        SaveWidth();
        UnsubscribeDocument();
        ++_generation;
        _canvasInput?.Dispose();
        _canvasInput = null;
        if (_canvas != null)
        {
            _canvas.DocumentChanged -= OnDocumentChanged;
            _canvas.MouseDown -= OnCanvasMouseDown;

            _canvas.KeyDown -= OnCanvasKeyDown;
            _canvas.PopulateContextMenu -= OnPopulateContextMenu;
            _canvas.DragEnter -= OnExampleDrag;
            _canvas.DragOver -= OnExampleDrag;
            _canvas.DragDrop -= OnExampleDrop;
        }
        _control?.OnWorkspaceHidden();
        _control?.SaveExampleValue();
        _control?.ShutdownWorkspace();
        if (_splitter?.IsDisposed == false) { _splitter.Panel1 = null; _splitter.Panel2 = null; }
        if (_editor != null)
        {
            _editor.Closed -= OnEditorClosed;
            if (!_editor.IsDisposed)
            {
                _editor.Content = _originalContent;
                if (_menu != null) _editor.Menu?.Items.Remove(_menu);
                if (_button != null) _editor.ToolBar?.Items.Remove(_button);
            }
        }
        _control?.Dispose();
        _splitter?.Dispose();
        _menu?.Dispose();
        _button?.Image?.Dispose();
        _button?.Dispose();
        _control = null; _splitter = null; _originalContent = null; _editor = null; _canvas = null;
        _appliedOpen = null;
        _panelWidth = 0;
    }
    private void OnRhinoClosing(object sender, EventArgs e) => Dispose();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SuperSetting.OpenChanged -= ApplyOpenState;
        RhinoApp.Idle -= OnIdle;
        RhinoApp.Closing -= OnRhinoClosing;
        Detach();
        Gh2HighlightConduit.Set(null);
    }
}
