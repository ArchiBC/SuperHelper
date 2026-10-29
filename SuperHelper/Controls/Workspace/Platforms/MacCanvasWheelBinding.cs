using Grasshopper;
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
        return new MacCanvasWheelBinding(canvas, nativeWheel, applicationType);
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
        MouseEventArgs forwarded = args;
        try
        {
            var nativeEvent = _currentEvent.GetValue(_application);
            var gesturesPan = _trackpadGestures?.GetValue(null) is not false && Control.ModifierKeys != Keys.Control;
            var hasGesturePhase = nativeEvent != null &&
                (Convert.ToInt64(nativeEvent.GetType().GetProperty("Phase").GetValue(nativeEvent)) != 0 ||
                 Convert.ToInt64(nativeEvent.GetType().GetProperty("MomentumPhase").GetValue(nativeEvent)) != 0);
            if (!gesturesPan || !hasGesturePhase)
            {
                forwarded = new MouseEventArgs(args.Button, args.Clicks, args.X, args.Y, -args.Delta);
            }
        }
        catch (Exception exception)
        {
            RhinoApp.WriteLine("SuperHelper canvas wheel: " + exception.GetBaseException().Message);
        }
        _nativeWheel(sender, forwarded);
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
