using Eto.Forms;
using Rhino;
using System;
using System.Reflection;

namespace SuperHelper;

internal sealed class WindowsWorkspacePlatform : IWorkspacePlatform
{
    public object DockSitesOwner(Assembly rhinoUiAssembly, RhinoDoc document) => document;
    public uint WindowSerialNumber(Assembly rhinoUiAssembly, RhinoDoc document) => 0;
    public Control ViewportEventSource(Control wrapper, Control nativeControl) => nativeControl;
    public void FocusViewport(Control nativeControl) => nativeControl.Focus();
    public IDisposable BindCommandInput(TextBox input) => null;
}
