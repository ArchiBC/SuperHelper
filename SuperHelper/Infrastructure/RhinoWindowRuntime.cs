using Rhino;
using System;
using System.Reflection;

namespace SuperHelper;

internal static class RhinoWindowRuntime
{
    public static uint SerialNumber(Assembly rhinoUiAssembly, RhinoDoc document)
    {
        if (!Rhino.Runtime.HostUtils.RunningOnOSX)
        {
            return 0;
        }

        var mapType = rhinoUiAssembly.GetType(
            "Rhino.UI.Internal.TabPanels.MRDocumentModelWindowControllerMap",
            throwOnError: false);
        var method = mapType?.GetMethod(
            "TryGetRuntimeSerialNumber",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(uint), typeof(uint).MakeByRefType() },
            modifiers: null);
        var arguments = new object[] { document.RuntimeSerialNumber, 0u };
        if (method?.Invoke(null, arguments) is true)
        {
            return (uint)arguments[1];
        }

        throw new InvalidOperationException("Rhino has no window for the active document.");
    }
}
