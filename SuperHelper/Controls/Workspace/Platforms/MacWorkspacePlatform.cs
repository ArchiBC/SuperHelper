using Eto.Forms;
using Grasshopper;
using Grasshopper.GUI;
using Rhino;
using System;
using System.Linq;
using System.Reflection;

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

    public void FocusViewport(Control nativeControl)
    {
        if (nativeControl.GetType().FullName != "Rhino.UI.Controls.ViewControl")
        {
            nativeControl.Focus();
            return;
        }
        // RhinoMac's ViewControl is a wrapper NSView. Eto.Focus targets that
        // wrapper, while Rhino's mouse/getter input belongs to its MRMetalView.
        // Keep the AppKit bridge here so Windows never loads Mac dependencies.
        var handler = nativeControl.GetType().GetProperty("Handler", InstanceFlags | BindingFlags.DeclaredOnly)
            ?.GetValue(nativeControl);
        var container = handler?.GetType().GetProperty("ContainerControl", InstanceFlags)?.GetValue(handler);
        var view = container?.GetType().GetField("_nsView", InstanceFlags)?.GetValue(container);
        if (view == null)
        {
            throw new InvalidOperationException("Rhino's Mac native viewport is unavailable.");
        }

        var viewType = view.GetType();
        if (viewType.GetProperty("Superview")?.GetValue(view) == null)
        {
            var addSubview = container.GetType().GetMethods().Single(method =>
                method.Name == "AddSubview" && method.GetParameters().Length == 1);
            addSubview.Invoke(container, new[] { view });
            viewType.GetProperty("Frame")?.SetValue(view, container.GetType().GetProperty("Bounds")?.GetValue(container));
        }

        var window = viewType.GetProperty("Window")?.GetValue(view);
        if (window != null)
        {
            var firstResponder = window.GetType().GetMethods().Single(method =>
                method.Name == "MakeFirstResponder" && method.GetParameters().Length == 1);
            firstResponder.Invoke(window, new[] { view });
        }
    }

    public IDisposable BindCommandInput(TextBox input) => new CommandInputFocus(input);

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
