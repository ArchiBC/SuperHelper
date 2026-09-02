using Eto.Forms;
using Rhino;
using Rhino.Display;
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

    public RhinoView View { get; }

    public RhinoViewport Viewport { get; }

    public bool IsInteractiveRhinoView => View != null;

    public RhinoWorkViewHost(RhinoDoc document)
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
        _control.GotFocus += (_, _) => Activate(focusControl: false);
        _control.MouseDown += (_, _) => Activate();
        _control.MouseEnter += (_, _) =>
        {
            if (RhinoCommand.InCommand())
            {
                Activate(focusControl: false);
            }
        };
        SizeChanged += (_, _) => QueueViewportResize();
        Load += (_, _) => QueueViewportResize();
    }

    public void Activate(bool focusControl = true)
    {
        if (View != null && View.RuntimeSerialNumber != 0)
        {
            _document.Views.ActiveView = View;
        }
        if (focusControl)
        {
            _control.Focus();
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
        if (_resizePending)
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
        if (ClientSize.Width < 1 || ClientSize.Height < 1)
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
