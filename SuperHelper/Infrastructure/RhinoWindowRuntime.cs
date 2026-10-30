using Rhino;
using System.Reflection;

namespace SuperHelper;

internal static class RhinoWindowRuntime
{
    public static uint SerialNumber(Assembly rhinoUiAssembly, RhinoDoc document)
    {
        return WorkspacePlatform.Current.WindowSerialNumber(rhinoUiAssembly, document);
    }
}
