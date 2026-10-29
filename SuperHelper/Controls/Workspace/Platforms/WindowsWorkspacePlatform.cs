using Eto.Forms;
using Rhino;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace SuperHelper;

internal sealed class WindowsWorkspacePlatform : IWorkspacePlatform
{
    public object DockSitesOwner(Assembly rhinoUiAssembly, RhinoDoc document) => document;
    public uint WindowSerialNumber(Assembly rhinoUiAssembly, RhinoDoc document) => 0;
    public Control ViewportEventSource(Control wrapper, Control nativeControl) => nativeControl;
    public void ConfigureViewport(Control nativeControl) { }
    public void FocusViewport(Control nativeControl) => nativeControl.Focus();
    public void OpenFileLocation(string path) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = Directory.Exists(path) ? path : Path.GetDirectoryName(path),
            UseShellExecute = true,
        })?.Dispose();
    public IDisposable BindCommandInput(TextBox input) => null;
    public IDisposable BindCanvasInput(Grasshopper.GUI.Canvas.GH_Canvas canvas) => null;
}
