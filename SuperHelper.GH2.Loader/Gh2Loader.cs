using Rhino;
using Rhino.PlugIns;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: Guid("89447B4B-3888-4B3F-9C83-4F3A5416CB25")]
[assembly: PlugInDescription(DescriptionType.Email, "Archi-bc@outlook.com")]
[assembly: PlugInDescription(DescriptionType.WebSite, "https://github.com/ArchiBC/SuperHelper")]

namespace SuperHelper.GH2.Loader;

/// <summary>
/// Rhino scans package RHPs before GH2's assembly resolver is available.
/// Keep this entry point independent of GH2 and load the nested extension after
/// the user opens GH2. This entry point is shared by Windows and macOS.
/// </summary>
public sealed class Gh2Loader : PlugIn
{
    private Assembly _grasshopper;

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        _grasshopper = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(IsGrasshopper);
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
        RhinoApp.Idle += OnIdle;
        return LoadReturnCode.Success;
    }

    private static bool IsGrasshopper(Assembly assembly) => assembly.GetName().Name == "Grasshopper2";

    private void OnAssemblyLoaded(object sender, AssemblyLoadEventArgs args)
    {
        if (IsGrasshopper(args.LoadedAssembly)) _grasshopper = args.LoadedAssembly;
    }

    private void OnIdle(object sender, EventArgs args)
    {
        if (_grasshopper == null) return;
        var editor = _grasshopper.GetType("Grasshopper2.UI.Editor");
        if (editor?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) == null) return;
        StopWatching();
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location), "gh2", "SuperHelper.GH2.rhp");
            var server = _grasshopper.GetType("Grasshopper2.Framework.PluginServer", true);
            server.GetMethod("ScopePlugin", new[] { typeof(string) }).Invoke(null, new object[] { path });
            var load = server.GetMethods().Single(method => method.Name == "LoadPlugin" && method.GetParameters().Length == 2);
            var arguments = new object[] { path, null };
            if (!(bool)load.Invoke(null, arguments))
                RhinoApp.WriteLine("SuperHelper GH2 could not be loaded: " + arguments[1]);
        }
        catch (Exception exception)
        {
            RhinoApp.WriteLine("SuperHelper GH2 could not be loaded: " + exception.GetBaseException().Message);
        }
    }

    private void StopWatching()
    {
        RhinoApp.Idle -= OnIdle;
        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoaded;
    }

    protected override void OnShutdown()
    {
        StopWatching();
        // Retire both workspaces before Rhino releases this native panel owner.
        foreach (var name in new[] { "SuperHelper", "SuperHelper.GH2" })
        {
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(item => item.GetName().Name == name);
                var type = assembly?.GetType(name == "SuperHelper" ? "SuperHelper.SuperHelperAssemblyPriority" : "SuperHelper.SuperHelperGh2Plugin");
                type?.GetMethod("ShutdownWorkspace", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            }
            catch (Exception exception)
            {
                RhinoApp.WriteLine("SuperHelper shutdown: " + exception.GetBaseException().Message);
            }
        }
    }
}
