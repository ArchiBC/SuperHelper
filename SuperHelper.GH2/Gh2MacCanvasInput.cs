using Eto.Drawing;
using Eto.Forms;
using Grasshopper2.UI;
using Grasshopper2.UI.Flex;
using Rhino;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Canvas = Grasshopper2.UI.Canvas.Canvas;

namespace SuperHelper;

/// <summary>Routes discrete Mac wheel input to GH2's native zoom handler.</summary>
internal sealed class Gh2MacCanvasInput : IDisposable
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Editor _editor;
    private readonly Canvas _canvas;
    private readonly object _canvasHandler;
    private readonly object _window;
    private readonly Type _eventType;
    private readonly MethodInfo _getMouseEvent;
    private readonly MethodInfo _removeMonitor;
    private readonly Action<MouseEventArgs> _nativeWheel;
    private readonly Delegate _callback;
    private object _monitor;

    public Gh2MacCanvasInput(Editor editor, Canvas canvas)
    {
        _editor = editor;
        _canvas = canvas;
        _eventType = FindType("AppKit.NSEvent");
        var conversions = FindType("Eto.Mac.MacConversions");
        _getMouseEvent = conversions.GetMethod("GetMouseEvent", BindingFlags.Public | BindingFlags.Static)
            ?? throw new NotSupportedException("Eto does not expose its native Mac mouse event conversion.");
        _canvasHandler = typeof(Control).GetProperty("Handler", InstanceFlags | BindingFlags.DeclaredOnly).GetValue(canvas);
        var editorHandler = typeof(Control).GetProperty("Handler", InstanceFlags | BindingFlags.DeclaredOnly).GetValue(editor);
        _window = editorHandler.GetType().GetProperties(InstanceFlags)
            .First(property => property.Name == "Control" && property.PropertyType.FullName == "AppKit.NSWindow")
            .GetValue(editorHandler);
        _nativeWheel = (Action<MouseEventArgs>)typeof(FlexControl)
            .GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(MouseEventArgs) }, null)
            .CreateDelegate(typeof(Action<MouseEventArgs>), canvas);

        var addMonitor = _eventType.GetMethod("AddLocalMonitorForEventsMatchingMask", BindingFlags.Public | BindingFlags.Static);
        _removeMonitor = _eventType.GetMethod("RemoveMonitor", BindingFlags.Public | BindingFlags.Static);
        var signature = addMonitor.GetParameters();
        var eventParameter = Expression.Parameter(_eventType, "nativeEvent");
        var filter = GetType().GetMethod(nameof(Filter), BindingFlags.Instance | BindingFlags.NonPublic);
        _callback = Expression.Lambda(signature[1].ParameterType,
            Expression.Convert(Expression.Call(Expression.Constant(this), filter,
                Expression.Convert(eventParameter, typeof(object))), _eventType), eventParameter).Compile();
        _monitor = addMonitor.Invoke(null, new[] { Enum.Parse(signature[0].ParameterType, "ScrollWheel"), _callback });
    }

    private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(assembly => assembly.GetType(name, false)).FirstOrDefault(type => type != null)
        ?? throw new NotSupportedException(name + " is unavailable in this Rhino build.");

    private object Filter(object nativeEvent)
    {
        try
        {
            if (_monitor == null || _editor.IsDisposed || !_editor.Visible || !_canvas.Loaded || !_canvas.Visible
                || !_canvas.Enabled || !_canvas.AllowZoom
                || !Equals(_eventType.GetProperty("WindowNumber").GetValue(nativeEvent),
                    _window.GetType().GetProperty("WindowNumber").GetValue(_window))) return nativeEvent;

            // Phase is only valid for scroll events. High-resolution mice also
            // report precise deltas, so precision alone cannot identify a gesture.
            // Phased scrolling, momentum and pinch stay on GH2's native path.
            if (_eventType.GetProperty("Type").GetValue(nativeEvent)?.ToString() != "ScrollWheel"
                || !IsWheelZoom(Convert.ToInt64(_eventType.GetProperty("Phase").GetValue(nativeEvent)),
                    Convert.ToInt64(_eventType.GetProperty("MomentumPhase").GetValue(nativeEvent)))) return nativeEvent;

            var mouse = (MouseEventArgs)_getMouseEvent.Invoke(null, new[] { _canvasHandler, nativeEvent, true });
            if (mouse.Delta.Height == 0 || !new RectangleF(0, 0, _canvas.Width, _canvas.Height).Contains(mouse.Location)) return nativeEvent;

            // GH2's Mac ScrollGesture consumes mouse wheels as pan input before
            // Eto raises MouseWheel. Use GH2's existing wheel handler for discrete
            // wheels, preserving its natural-scroll correction, Rhino preference,
            // cursor anchor, zoom limits and responsive canvas objects.
            _nativeWheel(SuperHelperPreferences.ReverseCanvasWheelZoom ? ReverseWheel(mouse) : mouse);
            return null;
        }
        catch (Exception exception)
        {
            RhinoApp.WriteLine("SuperHelper GH2 Mac wheel: " + exception.GetBaseException().Message);
            return nativeEvent;
        }
    }

    internal static bool IsWheelZoom(long phase, long momentum) => phase == 0 && momentum == 0;

    internal static MouseEventArgs ReverseWheel(MouseEventArgs mouse) => new(mouse.Buttons, mouse.Modifiers,
        mouse.Location, new SizeF(mouse.Delta.Width, -mouse.Delta.Height), mouse.Pressure, mouse.IsDirectionInverted);

    public void Dispose()
    {
        if (_monitor == null) return;
        _removeMonitor.Invoke(null, new[] { _monitor });
        (_monitor as IDisposable)?.Dispose();
        _monitor = null;
        GC.KeepAlive(_callback);
    }
}
