using Eto.Forms;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.UI.Controls;
using System;
using System.Reflection;
using RhinoCommand = Rhino.Commands.Command;

namespace SuperHelper;

/// <summary>
/// Hosts Rhino's real Eto-backed document view. Unlike ViewportControl this
/// creates a registered RhinoView, so GetPoint/GetObject and view interaction
/// are handled by Rhino itself. The public display-only control is retained as
/// a compatibility fallback for Rhino builds without the internal view host.
/// </summary>
internal sealed class RhinoWorkViewHost : Panel
{
    private const string ViewControlTypeName = "Rhino.UI.Controls.ViewControl";

    private readonly RhinoDoc _document;
    private readonly Control _control;
    private readonly MethodInfo _refreshMethod;
    private bool _resizePending;
    private ViewportInfo _viewProjection;
    private bool _restoreProjectionPending;
    private bool _initialLoadComplete;

    public RhinoView View { get; }

    public RhinoViewport Viewport { get; }

    public bool IsInteractiveRhinoView => View != null;

    public bool NeedsRecreation => View != null
        && View.Document?.RuntimeSerialNumber != _document.RuntimeSerialNumber;

    public RhinoWorkViewHost(RhinoDoc document, ViewportInfo viewProjection = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;

        if (TryCreateRhinoView(document, out var control, out var view, out var viewport, out var refreshMethod))
        {
            _control = control;
            _refreshMethod = refreshMethod;
            View = view;
            Viewport = viewport;
        }
        else
        {
            var fallback = new ViewportControl("SuperHelper");
            _control = fallback;
            Viewport = fallback.Viewport;
            _refreshMethod = fallback.GetType().GetMethod(nameof(Refresh), Type.EmptyTypes);
        }

        Content = _control;
        SetDefaultPerspectiveProjection();
        if (viewProjection != null)
        {
            _viewProjection = new ViewportInfo(viewProjection);
            RestoreViewProjection();
        }
        // Rhino's ViewControl fits all geometry in OnLoadComplete, including
        // when GH shows it again after a getter. Keep the latest camera, rather
        // than the camera from before the prompt, across that native reload.
        _control.Load += (_, _) => _restoreProjectionPending = _viewProjection != null;
        _control.LoadComplete += (_, _) => QueueProjectionRestore();
        _control.UnLoad += (_, _) => RememberViewProjection();
        RhinoView.Modified += OnViewModified;
        // Rhino's native NSView does not implement Eto's IMacControl event
        // bridge. Attach Eto focus/mouse events to our wrapper on macOS.
        Control eventSource = WorkspacePlatform.Current.ViewportEventSource(this, _control);
        eventSource.GotFocus += (_, _) => Activate(focusControl: false);
        eventSource.MouseDown += (_, _) => Activate();
        eventSource.MouseEnter += (_, _) =>
        {
            if (RhinoCommand.InCommand())
            {
                Activate(focusControl: false);
            }
        };
        SizeChanged += (_, _) => QueueViewportResize();
        Load += (_, _) => QueueViewportResize();
    }

    public ViewportInfo CaptureViewProjection()
    {
        RememberViewProjection();
        return _viewProjection == null ? null : new ViewportInfo(_viewProjection);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RhinoView.Modified -= OnViewModified;
            _viewProjection?.Dispose();
            _viewProjection = null;
        }
        base.Dispose(disposing);
    }

    private void OnViewModified(object sender, ViewEventArgs e)
    {
        if (View != null && e.View?.RuntimeSerialNumber == View.RuntimeSerialNumber)
        {
            RememberViewProjection();
        }
    }

    private void RememberViewProjection()
    {
        if (IsDisposed || NeedsRecreation || Viewport == null
            || !_initialLoadComplete || _restoreProjectionPending)
        {
            return;
        }
        var projection = new ViewportInfo(Viewport);
        if (!projection.IsValidCamera || !projection.IsValidFrustum)
        {
            projection.Dispose();
            return;
        }
        _viewProjection?.Dispose();
        _viewProjection = projection;
    }

    private void QueueProjectionRestore()
    {
        // LoadComplete is raised by the base class before Rhino's own fit.
        // Restore on the next UI turn, once that fit has finished.
        Application.Instance?.AsyncInvoke((Action)(() =>
        {
            if (IsDisposed || NeedsRecreation)
            {
                return;
            }
            RestoreViewProjection();
            _restoreProjectionPending = false;
            _initialLoadComplete = true;
            RememberViewProjection();
            Refresh();
        }));
    }

    private void RestoreViewProjection()
    {
        if (_viewProjection != null && Viewport != null)
        {
            Viewport.SetViewProjection(_viewProjection, updateTargetLocation: false);
            Viewport.SetCameraTarget(_viewProjection.TargetPoint, updateCameraLocation: false);
        }
    }

    public void Activate(bool focusControl = true)
    {
        if (NeedsRecreation)
        {
            return;
        }
        if (View != null && View.RuntimeSerialNumber != 0
            && _document.Views.ActiveView?.RuntimeSerialNumber != View.RuntimeSerialNumber)
        {
            _document.Views.ActiveView = View;
        }
        if (focusControl)
        {
            WorkspacePlatform.Current.FocusViewport(_control);
        }
    }

    public void Refresh()
    {
        try
        {
            _refreshMethod?.Invoke(_control, null);
        }
        catch
        {
            RhinoDoc.ActiveDoc?.Views.Redraw();
        }
    }

    private void QueueViewportResize()
    {
        if (IsDisposed || _control.IsDisposed || _resizePending)
        {
            return;
        }

        var application = Application.Instance;
        if (application == null)
        {
            ResizeViewport();
            return;
        }

        _resizePending = true;
        application.AsyncInvoke((Action)(() =>
        {
            _resizePending = false;
            ResizeViewport();
        }));
    }

    private void ResizeViewport()
    {
        if (IsDisposed || _control.IsDisposed || ClientSize.Width < 1 || ClientSize.Height < 1)
        {
            return;
        }

        if (_control.Size != ClientSize)
        {
            _control.Size = ClientSize;
        }
        Refresh();
    }

    private void SetDefaultPerspectiveProjection()
    {
        if (Viewport == null || Viewport.IsPerspectiveProjection)
        {
            return;
        }

        Viewport.SetProjection(
            DefinedViewportProjection.Perspective,
            "Perspective",
            updateConstructionPlane: true);
    }

    private static bool TryCreateRhinoView(
        RhinoDoc document,
        out Control control,
        out RhinoView view,
        out RhinoViewport viewport,
        out MethodInfo refreshMethod)
    {
        control = null;
        view = null;
        viewport = null;
        refreshMethod = null;

        try
        {
            var assembly = typeof(ViewportControl).Assembly;
            var type = assembly.GetType(ViewControlTypeName, throwOnError: true);
            var instance = Activator.CreateInstance(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object[] { document },
                culture: null);
            control = instance as Control;
            view = type.GetProperty("View", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(instance) as RhinoView;
            viewport = type.GetProperty("Viewport", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(instance) as RhinoViewport;
            refreshMethod = type.GetMethod(
                "Refresh",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);
            if (control != null && view != null && viewport != null && refreshMethod != null)
            {
                return true;
            }

            control?.Dispose();
            control = null;
            view = null;
            viewport = null;
            refreshMethod = null;
            return false;
        }
        catch
        {
            control?.Dispose();
            control = null;
            view = null;
            viewport = null;
            refreshMethod = null;
            return false;
        }
    }
}
