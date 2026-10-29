using Eto.Forms;
using Grasshopper;
using Grasshopper.GUI;
using Rhino;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SuperHelper;

internal sealed class MacWorkspacePlatform : IWorkspacePlatform
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public object DockSitesOwner(Assembly rhinoUiAssembly, RhinoDoc document) =>
        WindowSerialNumber(rhinoUiAssembly, document);

    public uint WindowSerialNumber(Assembly rhinoUiAssembly, RhinoDoc document)
    {
        var mapType = rhinoUiAssembly.GetType(
            "Rhino.UI.Internal.TabPanels.MRDocumentModelWindowControllerMap", throwOnError: false);
        var method = mapType?.GetMethod("TryGetRuntimeSerialNumber",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
            new[] { typeof(uint), typeof(uint).MakeByRefType() }, null);
        var arguments = new object[] { document.RuntimeSerialNumber, 0u };
        if (method?.Invoke(null, arguments) is true)
        {
            return (uint)arguments[1];
        }
        throw new InvalidOperationException("Rhino has no window for the active document.");
    }

    public Control ViewportEventSource(Control wrapper, Control nativeControl) => wrapper;

    public void ConfigureViewport(Control nativeControl)
    {
        if (nativeControl.GetType().FullName != "Rhino.UI.Controls.ViewControl")
        {
            return;
        }
        var view = NativeViewport(nativeControl);
        if (view == null)
        {
            return;
        }

        // RhinoCommon's TitleVisible flag is already true for this view. The
        // Mac host separately hides its own MRViewportLabel. Unhide that native
        // object so Rhino supplies the full title menu and all its commands.
        var stringType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("Foundation.NSString", false))
            .First(type => type != null);
        var macAssembly = stringType.Assembly;
        var objectType = macAssembly.GetType("Foundation.NSObject", true);
        var numberType = macAssembly.GetType("Foundation.NSNumber", true);
        var selectorType = macAssembly.GetType("ObjCRuntime.Selector", true);
        var selector = Activator.CreateInstance(selectorType, new object[] { "setViewLabelHidden:" });
        object key = null;
        object value = null;
        try
        {
            if (view.GetType().GetMethod("RespondsToSelector", new[] { selectorType })
                ?.Invoke(view, new[] { selector }) is not true)
            {
                return;
            }
            key = Activator.CreateInstance(stringType, new object[] { "viewLabelHidden" });
            value = numberType.GetMethod("FromBoolean", new[] { typeof(bool) })
                .Invoke(null, new object[] { false });
            view.GetType().GetMethod("SetValueForKey", new[] { objectType, stringType })
                .Invoke(view, new[] { value, key });
        }
        finally
        {
            (value as IDisposable)?.Dispose();
            (key as IDisposable)?.Dispose();
            (selector as IDisposable)?.Dispose();
        }
    }

    public void FocusViewport(Control nativeControl)
    {
        try
        {
            FocusNativeViewport(nativeControl);
        }
        catch (Exception exception)
        {
            // An AppKit mouse callback must not let a managed bridge failure
            // escape into Cocoa, which aborts the entire Rhino process.
            RhinoApp.WriteLine("SuperHelper viewport focus: " + exception.GetBaseException().Message);
        }
    }

    private static void FocusNativeViewport(Control nativeControl)
    {
        if (nativeControl.GetType().FullName != "Rhino.UI.Controls.ViewControl")
        {
            nativeControl.Focus();
            return;
        }
        // RhinoMac's ViewControl is a wrapper NSView. Eto.Focus targets that
        // wrapper, while Rhino's mouse/getter input belongs to its MRMetalView.
        // Keep the AppKit bridge here so Windows never loads Mac dependencies.
        var view = NativeViewport(nativeControl, out var container);
        if (view == null)
        {
            throw new InvalidOperationException("Rhino's Mac native viewport is unavailable.");
        }

        var viewType = view.GetType();
        if (viewType.GetProperty("Superview")?.GetValue(view) == null)
        {
            // GH temporarily unloads this host while starting a getter. Rhino's
            // NSView can survive that transition detached from its Eto wrapper.
            // Reattach it before assigning first responder, using a usable size
            // before AddSubview triggers a Metal redraw.
            var bounds = container.GetType().GetProperty("Bounds")?.GetValue(container);
            if (bounds == null
                || NativeDouble(bounds.GetType().GetProperty("Width")?.GetValue(bounds)) <= 0
                || NativeDouble(bounds.GetType().GetProperty("Height")?.GetValue(bounds)) <= 0)
            {
                return;
            }
            viewType.GetProperty("Frame")?.SetValue(view, bounds);
            var addSubview = container.GetType().GetMethods().Single(method =>
                method.Name == "AddSubview" && method.GetParameters().Length == 1);
            addSubview.Invoke(container, new[] { view });
        }
        var window = viewType.GetProperty("Window")?.GetValue(view);
        if (window != null)
        {
            var firstResponder = window.GetType().GetMethods().Single(method =>
                method.Name == "MakeFirstResponder" && method.GetParameters().Length == 1);
            firstResponder.Invoke(window, new[] { view });
        }
    }

    private static object NativeViewport(Control nativeControl) => NativeViewport(nativeControl, out _);

    private static double NativeDouble(object value) => value is NFloat number ? (double)number : Convert.ToDouble(value);

    private static object NativeViewport(Control nativeControl, out object container)
    {
        var handler = nativeControl.GetType().GetProperty("Handler", InstanceFlags | BindingFlags.DeclaredOnly)
            ?.GetValue(nativeControl);
        container = handler?.GetType().GetProperty("ContainerControl", InstanceFlags)?.GetValue(handler);
        return container?.GetType().GetField("_nsView", InstanceFlags)?.GetValue(container);
    }

    public void OpenFileLocation(string path)
    {
        var start = new ProcessStartInfo { FileName = "/usr/bin/open", UseShellExecute = false };
        if (File.Exists(path))
        {
            start.ArgumentList.Add("-R");
        }
        start.ArgumentList.Add(path);
        Process.Start(start)?.Dispose();
    }

    public IDisposable BindCommandInput(TextBox input) => new CommandInputFocus(input);
    public IDisposable BindCanvasInput(Grasshopper.GUI.Canvas.GH_Canvas canvas) => MacCanvasWheelBinding.TryCreate(canvas);

    private sealed class CommandInputFocus : IDisposable
    {
        private readonly TextBox _input;
        private GH_DocumentEditor _editor;
        private bool _previousKeyPreview;

        public CommandInputFocus(TextBox input)
        {
            _input = input;
            _input.GotFocus += OnGotFocus;
            _input.LostFocus += OnLostFocus;
        }

        private void OnGotFocus(object sender, EventArgs args)
        {
            if (_editor != null || Instances.DocumentEditor is not { } editor)
            {
                return;
            }
            _editor = editor;
            _previousKeyPreview = editor.KeyPreview;
            // GH only excludes WinForms TextBox/ComboBox from its key preview.
            // The integrated Rhino command editor is an Eto TextBox instead.
            editor.KeyPreview = false;
        }

        private void OnLostFocus(object sender, EventArgs args)
        {
            if (_editor != null && !_editor.IsDisposed)
            {
                _editor.KeyPreview = _previousKeyPreview;
            }
            _editor = null;
        }

        public void Dispose()
        {
            _input.GotFocus -= OnGotFocus;
            _input.LostFocus -= OnLostFocus;
            OnLostFocus(null, EventArgs.Empty);
        }
    }
}
