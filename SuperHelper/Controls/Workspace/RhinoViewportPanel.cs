using Eto.Forms;
using Grasshopper;
using Rhino;
using Rhino.DocObjects;
using System;
using System.Reflection;
using RhinoCommand = Rhino.Commands.Command;
using RhinoCommandEventArgs = Rhino.Commands.CommandEventArgs;
using WinForms = System.Windows.Forms;

namespace SuperHelper;

internal sealed class RhinoViewportPanel : Panel
{
    private static readonly FieldInfo GrasshopperDocumentInGetField =
        typeof(Grasshopper.Kernel.GH_Document).GetField(
            "InGet",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly FieldInfo GrasshopperDocumentFadeOutField =
        typeof(Grasshopper.Kernel.GH_Document).GetField(
            "FadeOut",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private RhinoWorkViewHost _viewport;
    private uint _viewportDocumentSerial;
    private bool _eventsSubscribed;
    private bool _keepGrasshopperVisibleForCommand;
    private bool _keepGrasshopperVisibleWhileWorkspaceOpen;
    private Grasshopper.GUI.GH_DocumentEditor _promptDocumentEditor;
    private bool _restorePromptEditorPending;

    public void Start()
    {
        SubscribeEvents();
        ResetViewport();
    }

    public void Stop()
    {
        UnsubscribeEvents();
        OnWorkspaceHidden();
    }

    public void Shutdown()
    {
        Stop();
        Content = null;
        _viewport?.Dispose();
        _viewport = null;
        _viewportDocumentSerial = 0;
    }

    public void ResetViewport()
    {
        var activeDocument = RhinoDoc.ActiveDoc;
        if (activeDocument == null)
        {
            return;
        }

        if (_viewport == null || _viewportDocumentSerial != activeDocument.RuntimeSerialNumber
            || _viewport.NeedsRecreation)
        {
            Content = null;
            _viewport?.Dispose();
            _viewport = new RhinoWorkViewHost(activeDocument);
            _viewportDocumentSerial = activeDocument.RuntimeSerialNumber;
            Content = _viewport;
        }

        _viewport.Viewport?.ZoomExtents();
        _viewport.Refresh();
        if (_keepGrasshopperVisibleWhileWorkspaceOpen)
        {
            // Prime the registered embedded view so the first GH persistent-data
            // prompt can use it without activating a main Rhino viewport first.
            _viewport.Activate(focusControl: false);
        }
    }

    public void Activate(bool focusControl = true)
    {
        if (_viewport?.NeedsRecreation == true)
        {
            ResetViewport();
        }
        _viewport?.Activate(focusControl);
    }

    public void PrepareForCommand() => _keepGrasshopperVisibleForCommand = true;

    public void CancelPreparedCommand() => _keepGrasshopperVisibleForCommand = false;

    public void OnWorkspaceShown()
    {
        Start();
        _keepGrasshopperVisibleWhileWorkspaceOpen = true;
        Activate(focusControl: false);
        var editor = Instances.DocumentEditor;
        if (ReferenceEquals(_promptDocumentEditor, editor))
        {
            return;
        }

        DetachPromptEditor();
        _promptDocumentEditor = editor;
        if (_promptDocumentEditor != null)
        {
            _promptDocumentEditor.VisibleChanged += OnPromptEditorVisibilityChanged;
            _promptDocumentEditor.Resize += OnPromptEditorVisibilityChanged;
        }
    }

    public void OnWorkspaceHidden()
    {
        _keepGrasshopperVisibleWhileWorkspaceOpen = false;
        DetachPromptEditor();
        _restorePromptEditorPending = false;
    }

    private void SubscribeEvents()
    {
        if (_eventsSubscribed)
        {
            return;
        }

        RhinoDoc.AddRhinoObject += OnRhinoObjectChanged;
        RhinoDoc.DeleteRhinoObject += OnRhinoObjectChanged;
        RhinoDoc.UndeleteRhinoObject += OnRhinoObjectChanged;
        RhinoDoc.ReplaceRhinoObject += OnRhinoObjectReplaced;
        RhinoDoc.ModifyObjectAttributes += OnRhinoObjectAttributesChanged;
        RhinoDoc.SelectObjects += OnRhinoObjectSelectionChanged;
        RhinoDoc.DeselectObjects += OnRhinoObjectSelectionChanged;
        RhinoDoc.DeselectAllObjects += OnRhinoObjectsDeselected;
        RhinoApp.CommandPromptChanged += OnCommandPromptChanged;
        RhinoCommand.BeginCommand += OnCommandBegan;
        RhinoCommand.EndCommand += OnCommandEnded;
        if (Rhino.Runtime.HostUtils.RunningOnOSX)
        {
            RhinoApp.Idle += OnRhinoIdle;
        }
        _eventsSubscribed = true;
    }

    private void UnsubscribeEvents()
    {
        if (!_eventsSubscribed)
        {
            return;
        }

        RhinoDoc.AddRhinoObject -= OnRhinoObjectChanged;
        RhinoDoc.DeleteRhinoObject -= OnRhinoObjectChanged;
        RhinoDoc.UndeleteRhinoObject -= OnRhinoObjectChanged;
        RhinoDoc.ReplaceRhinoObject -= OnRhinoObjectReplaced;
        RhinoDoc.ModifyObjectAttributes -= OnRhinoObjectAttributesChanged;
        RhinoDoc.SelectObjects -= OnRhinoObjectSelectionChanged;
        RhinoDoc.DeselectObjects -= OnRhinoObjectSelectionChanged;
        RhinoDoc.DeselectAllObjects -= OnRhinoObjectsDeselected;
        RhinoApp.CommandPromptChanged -= OnCommandPromptChanged;
        RhinoCommand.BeginCommand -= OnCommandBegan;
        RhinoCommand.EndCommand -= OnCommandEnded;
        RhinoApp.Idle -= OnRhinoIdle;
        _eventsSubscribed = false;
    }

    private void DetachPromptEditor()
    {
        if (_promptDocumentEditor == null)
        {
            return;
        }

        _promptDocumentEditor.VisibleChanged -= OnPromptEditorVisibilityChanged;
        _promptDocumentEditor.Resize -= OnPromptEditorVisibilityChanged;
        _promptDocumentEditor = null;
    }

    private void OnPromptEditorVisibilityChanged(object sender, EventArgs e)
    {
        if (sender is not Grasshopper.GUI.GH_DocumentEditor editor
            || !_keepGrasshopperVisibleWhileWorkspaceOpen
            || !IsGrasshopperPromptStartingOrActive()
            || (editor.Visible && editor.WindowState != WinForms.FormWindowState.Minimized)
            || _restorePromptEditorPending)
        {
            return;
        }

        _restorePromptEditorPending = true;
        void RestoreEditorAndViewport()
        {
            if (editor.IsDisposed
                || !_keepGrasshopperVisibleWhileWorkspaceOpen
                || !IsGrasshopperPromptStartingOrActive())
            {
                _restorePromptEditorPending = false;
                return;
            }

            try
            {
                RestoreEditor(editor);
                Activate();
            }
            finally
            {
                _restorePromptEditorPending = false;
            }
        }

        if (Application.Instance != null)
        {
            Application.Instance.AsyncInvoke(RestoreEditorAndViewport);
        }
        else
        {
            RhinoApp.InvokeOnUiThread((Action)RestoreEditorAndViewport);
        }
    }

    private void OnCommandPromptChanged(object sender, Rhino.UI.CommandPromptChangedEventArgs e)
    {
        if (!_keepGrasshopperVisibleWhileWorkspaceOpen || !IsGrasshopperPromptStartingOrActive())
        {
            return;
        }

        InvokeOnEtoUi(() =>
        {
            // GH hides its editor before entering the native getter. Restore
            // it here as queued visibility work can wait until the getter ends.
            RestoreEditor(Instances.DocumentEditor);
            Activate();
        });
    }

    private void OnCommandBegan(object sender, RhinoCommandEventArgs e)
    {
        if (!_keepGrasshopperVisibleForCommand)
        {
            return;
        }

        InvokeOnEtoUi(() =>
        {
            RestoreEditor(Instances.DocumentEditor);
            Activate();
        });
    }

    private void OnCommandEnded(object sender, RhinoCommandEventArgs e) =>
        _keepGrasshopperVisibleForCommand = false;

    private void OnRhinoIdle(object sender, EventArgs e)
    {
        // Reparenting the NSView while GH is opening can retire its original
        // RhinoView. Recreate only after the native window has settled.
        if (Loaded && Visible && !RhinoCommand.InCommand() && _viewport?.NeedsRecreation == true)
        {
            ResetViewport();
        }
    }

    private void OnRhinoObjectChanged(object sender, RhinoObjectEventArgs e) => _viewport?.Refresh();
    private void OnRhinoObjectReplaced(object sender, RhinoReplaceObjectEventArgs e) => _viewport?.Refresh();
    private void OnRhinoObjectAttributesChanged(object sender, RhinoModifyObjectAttributesEventArgs e) => _viewport?.Refresh();
    private void OnRhinoObjectSelectionChanged(object sender, RhinoObjectSelectionEventArgs e) => _viewport?.Refresh();
    private void OnRhinoObjectsDeselected(object sender, RhinoDeselectAllObjectsEventArgs e) => _viewport?.Refresh();

    private static bool IsGrasshopperPromptStartingOrActive()
    {
        var document = Instances.ActiveCanvas?.Document;
        return document != null
            && (GrasshopperDocumentInGetField?.GetValue(document) is true
                || GrasshopperDocumentFadeOutField?.GetValue(document) is true);
    }

    private static void RestoreEditor(Grasshopper.GUI.GH_DocumentEditor editor)
    {
        if (editor == null || editor.IsDisposed)
        {
            return;
        }
        if (editor.WindowState == WinForms.FormWindowState.Minimized)
        {
            editor.WindowState = WinForms.FormWindowState.Normal;
        }
        if (!editor.Visible)
        {
            editor.Show();
        }
        editor.Activate();
    }

    private static void InvokeOnEtoUi(Action action)
    {
        if (Application.Instance?.IsUIThread == true)
        {
            action();
        }
        else
        {
            Application.Instance?.AsyncInvoke(action);
        }
    }
}
