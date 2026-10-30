using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Rhino;
using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace SuperHelper;

internal sealed class MacCanvasWheelBinding : IDisposable
{
    private readonly GH_Canvas _canvas;
    private readonly MouseEventHandler _nativeWheel;
    private readonly object _application;
    private readonly PropertyInfo _currentEvent;
    private readonly PropertyInfo _trackpadGestures;
    private bool _reversed;
    private bool _disposed;

    private MacCanvasWheelBinding(GH_Canvas canvas, MethodInfo nativeWheel, Type applicationType)
    {
        _canvas = canvas;
        _nativeWheel = (MouseEventHandler)nativeWheel.CreateDelegate(typeof(MouseEventHandler), canvas);
        _application = applicationType.GetProperty("SharedApplication").GetValue(null);
        _currentEvent = applicationType.GetProperty("CurrentEvent");
        _trackpadGestures = typeof(CentralSettings).GetProperty("CanvasTrackpadGestures");
        SuperHelperPreferences.ReverseCanvasWheelZoomChanged += Apply;
        _canvas.Disposed += OnCanvasDisposed;
        Apply(SuperHelperPreferences.ReverseCanvasWheelZoom);
    }

    public static IDisposable TryCreate(GH_Canvas canvas)
    {
        if (canvas == null || canvas.IsDisposed)
        {
            return null;
        }
        var nativeWheel = typeof(GH_Canvas).GetMethod("Canvas_MouseWheel", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(object), typeof(MouseEventArgs) }, null);
        var applicationType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("AppKit.NSApplication", false))
            .FirstOrDefault(type => type != null);
        if (nativeWheel == null || applicationType == null)
        {
            RhinoApp.WriteLine("SuperHelper: this Rhino build does not support independent GH wheel zoom.");
            return null;
        }
        try
        {
            return new MacCanvasWheelBinding(canvas, nativeWheel, applicationType);
        }
        catch (Exception exception)
        {
            RhinoApp.WriteLine("SuperHelper canvas wheel binding: " + exception.GetBaseException().Message);
            return null;
        }
    }

    private void Apply(bool reverse)
    {
        if (_disposed || _canvas.IsDisposed || _reversed == reverse)
        {
            return;
        }
        if (reverse)
        {
            _canvas.MouseWheel -= _nativeWheel;
            _canvas.MouseWheel += OnMouseWheel;
        }
        else
        {
            _canvas.MouseWheel -= OnMouseWheel;
            _canvas.MouseWheel += _nativeWheel;
        }
        _reversed = reverse;
    }

    private void OnMouseWheel(object sender, MouseEventArgs args)
    {
        // Preserve GH's native trackpad panning, momentum and Control+scroll.
        // Only a wheel zoom receives the opposite delta; GH still performs
        // its own cursor anchoring, zoom limits and viewport notifications.
        var pan = false;
        try
        {
            var gesturesPan = _trackpadGestures?.GetValue(null) is not false && Control.ModifierKeys != Keys.Control;
            if (gesturesPan)
            {
                var nativeEvent = _currentEvent.GetValue(_application);
                var eventType = nativeEvent?.GetType();
                // AppKit aborts the process if Phase is read from a non-scroll
                // event, even inside a managed try/catch (e.g. scripted input).
                if (eventType?.GetProperty("Type")?.GetValue(nativeEvent)?.ToString() == "ScrollWheel")
                {
                    pan = Convert.ToInt64(eventType.GetProperty("Phase").GetValue(nativeEvent)) != 0 ||
                          Convert.ToInt64(eventType.GetProperty("MomentumPhase").GetValue(nativeEvent)) != 0;
                }
            }
        }
        catch (Exception exception)
        {
            RhinoApp.WriteLine("SuperHelper canvas wheel: " + exception.GetBaseException().Message);
        }
        if (pan)
        {
            _nativeWheel(sender, args);
            return;
        }

        // Use GH's public zoom operation directly. Its private Mac wheel
        // handler reads CurrentEvent again and is unsafe outside scrollWheel:.
        var reversed = new MouseEventArgs(args.Button, args.Clicks, args.X, args.Y, -args.Delta);
        _canvas.Viewport.DollyZoom(new GH_CanvasMouseEvent(_canvas.Viewport, reversed));
        _canvas.OnViewportChanged();
        _canvas.Refresh();
    }

    private void OnCanvasDisposed(object sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        Apply(false);
        _disposed = true;
        SuperHelperPreferences.ReverseCanvasWheelZoomChanged -= Apply;
        _canvas.Disposed -= OnCanvasDisposed;
    }
}
