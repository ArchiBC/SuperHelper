using Eto.Forms;
using Rhino;
using System;
using System.Reflection;

namespace SuperHelper;

internal interface IWorkspacePlatform
{
    object DockSitesOwner(Assembly rhinoUiAssembly, RhinoDoc document);
    uint WindowSerialNumber(Assembly rhinoUiAssembly, RhinoDoc document);
    Control ViewportEventSource(Control wrapper, Control nativeControl);
    void FocusViewport(Control nativeControl);
    IDisposable BindCommandInput(TextBox input);
}

internal static class WorkspacePlatform
{
    public static IWorkspacePlatform Current { get; } = Rhino.Runtime.HostUtils.RunningOnOSX
        ? new MacWorkspacePlatform()
        : new WindowsWorkspacePlatform();
}
