using Eto.Forms;
using Rhino;
using Rhino.DocObjects;
using System;
using RhinoCommand = Rhino.Commands.Command;
using RhinoCommandEventArgs = Rhino.Commands.CommandEventArgs;

namespace SuperHelper;

internal sealed class RhinoViewportPanel : Panel
{
    private RhinoWorkViewHost _viewport;
    private uint _viewportDocumentSerial;
    private bool _eventsSubscribed;
    private bool _keepGrasshopperVisibleForCommand;
    private bool _keepGrasshopperVisibleWhileWorkspaceOpen;
    private IDisposable _promptEditorBinding;
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
            // A retired native view still owns our last camera snapshot. Keep
            // it only for the same document; a newly opened model starts fresh.
            using var projection = _viewportDocumentSerial == activeDocument.RuntimeSerialNumber
                ? _viewport?.CaptureViewProjection()
                : null;
            Content = null;
            _viewport?.Dispose();
            _viewport = new RhinoWorkViewHost(activeDocument, projection) { Size = ClientSize };
            _viewportDocumentSerial = activeDocument.RuntimeSerialNumber;
            Content = _viewport;
        }

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
        _promptEditorBinding ??= HelperHost.Current.WatchEditorVisibility(OnPromptEditorVisibilityChanged);
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
        _promptEditorBinding?.Dispose();
        _promptEditorBinding = null;
    }

    private void OnPromptEditorVisibilityChanged(object sender, EventArgs e)
    {
        if (!_keepGrasshopperVisibleWhileWorkspaceOpen
            || !HelperHost.Current.IsPromptActive
            || HelperHost.Current.IsEditorVisible
            || _restorePromptEditorPending)
        {
            return;
        }

        _restorePromptEditorPending = true;
        void RestoreEditorAndViewport()
        {
            if (!_keepGrasshopperVisibleWhileWorkspaceOpen
                || !HelperHost.Current.IsPromptActive)
            {
                _restorePromptEditorPending = false;
                return;
            }

            try
            {
                HelperHost.Current.RestoreEditor();
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
        if (!_keepGrasshopperVisibleWhileWorkspaceOpen || !HelperHost.Current.IsPromptActive)
        {
            return;
        }

        InvokeOnEtoUi(() =>
        {
            // GH hides its editor before entering the native getter. Restore
            // it here as queued visibility work can wait until the getter ends.
            HelperHost.Current.RestoreEditor();
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
            HelperHost.Current.RestoreEditor();
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
