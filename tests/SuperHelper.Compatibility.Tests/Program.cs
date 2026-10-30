using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;

var repository = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var rhino = args.Length > 1 ? args[1] : OperatingSystem.IsMacOS() ? "/Applications/Rhino 9.app" : @"C:\Program Files\Rhino 9";
var resources = Path.Combine(rhino, "Contents/Frameworks/RhCore.framework/Resources");
var gh2Folder = OperatingSystem.IsMacOS() ? Path.Combine(resources, "ManagedPlugIns/Grasshopper2Plugin.rhp") : Path.Combine(rhino, "Plug-ins/Grasshopper2/net8.0");
var probes = new[] { AppContext.BaseDirectory, gh2Folder, Path.Combine(resources, "ManagedPlugIns/GrasshopperPlugin.rhp"), resources, Path.Combine(rhino, "Plug-ins/Grasshopper"), Path.Combine(rhino, "System/netcore"), Path.Combine(rhino, "System") };
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (var folder in probes)
        foreach (var extension in new[] { ".dll", ".rhp", ".gha" })
        {
            var path = Path.Combine(folder, name.Name + extension);
            if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        }
    return null;
};
return Run(repository, gh2Folder);

[MethodImpl(MethodImplOptions.NoInlining)]
static int Run(string repository, string gh2Folder) => CompatibilityChecks.Run(repository, gh2Folder);

internal static class CompatibilityChecks
{
    private static readonly List<object> Checks = new();
    private static int _failed;
    private static void Check(string name, Func<bool> test)
    {
        try
        {
            var passed = test();
            Checks.Add(new { name, passed });
            if (!passed) _failed++;
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
        }
        catch (Exception exception)
        {
            _failed++;
            Checks.Add(new { name, passed = false, error = exception.GetBaseException().Message });
            Console.WriteLine($"FAIL {name}: {exception.GetBaseException().Message}");
        }
    }
    public static int Run(string repository, string gh2Folder)
    {
        var assembly = typeof(SuperHelper.SuperHelperGh2Plugin).Assembly;
        Check("GH2 has no compile-time GH1 dependency", () => !assembly.GetReferencedAssemblies().Any(name => name.Name is "Grasshopper" or "GH_IO" or "SuperHelper"));
        Check("GH2 native plugin identity", () => assembly.GetCustomAttribute<System.Runtime.InteropServices.GuidAttribute>()?.Value == "01A71230-29B2-4B47-A8F0-B141A36A7F2A");
        Check("GH2 native load entry point", () => typeof(SuperHelper.SuperHelperGh2Plugin).GetMethod("OnLoaded").GetBaseDefinition().DeclaringType == typeof(Grasshopper2.Framework.Plugin));
        var resources = assembly.GetManifestResourceNames();
        Check("Shared XAML is embedded", () => resources.Contains("SuperHelper.Controls.SuperHelperControl.xeto"));
        Check("Shared icons are embedded", () => resources.Count(name => name.StartsWith("SuperHelper.Resources.Panels.") && name.EndsWith(".svg")) == 9);
        Check("Web mappings are embedded", () => resources.Contains("SuperHelper.Resources.urls.rhino-cn.json") && resources.Contains("SuperHelper.Resources.urls.100-gh.json"));

        var gh1Path = Path.Combine(repository, "SuperHelper/bin/Release/net10.0/SuperHelper.gha");
        var gh1 = AssemblyLoadContext.Default.LoadFromAssemblyPath(gh1Path);
        var names = new[] { "Command", "Viewport", "OriginHelp", "WebHelp", "Example", "ParamsHelp", "DevelopmentHelp", "FileManager", "Settings" };
        var gh2Ids = names.Select(name => assembly.GetType("SuperHelper.SuperHelper" + name + "Panel", true).GUID).ToArray();
        var gh1Ids = names.Select(name => gh1.GetType("SuperHelper.SuperHelper" + name + "Panel", true).GUID).ToArray();
        Check("GH1 and GH2 panel GUIDs are disjoint", () => gh2Ids.Distinct().Count() == 9 && !gh2Ids.Intersect(gh1Ids).Any());
        var requiredVersion = assembly.GetReferencedAssemblies().Single(name => name.Name == "Grasshopper2").Version;
        var hostVersion = AssemblyName.GetAssemblyName(Path.Combine(gh2Folder, "Grasshopper2.dll")).Version;
        Check($"Host SDK {hostVersion} supports extension SDK {requiredVersion}",
            () => hostVersion.Major == requiredVersion.Major && hostVersion >= requiredVersion);
        Check("Native GH1 migration signature exists", () => typeof(Grasshopper2.Doc.DocumentIO).GetMethod("Convert1", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(Grasshopper2.Interop.IGH_Document), typeof(Grasshopper2.Doc.Migration.MigrationLevel) }, null)?.ReturnType == typeof(Grasshopper2.Doc.Document));
        Check("Native getter timer signature exists", () => typeof(Grasshopper2.UI.Editor).GetField("_getterTimer", BindingFlags.Static | BindingFlags.NonPublic)?.FieldType == typeof(Eto.Forms.UITimer));
        var documentation = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gh2Folder, "DocumentationPlatform.dll"));
        Check("Native documentation signatures exist", () => documentation.GetType("DocumentationPlatform.Database").GetMethod("FindSpec") != null && documentation.GetType("DocumentationPlatform.Content").GetMethod("CreateFullHtml", BindingFlags.Public | BindingFlags.Static) != null);
        Check("Native component identity uses the GH2 serialization identifier", () => SuperHelper.Gh2ObjectInfo.NativeComponentId(typeof(Grasshopper2.Parameters.Special.NumberSliderObject)) == new Guid("00000000-984c-4141-ab95-3491434e7569"));

        Check("Parameter inspection retains mixed null leaves and original indices", () =>
        {
            var tree = Grasshopper2.Data.Garden.TreeFromList(new[] { 1, 0, 3 }, null, new[] { false, true, false });
            var leaves = SuperHelper.Gh2ObjectInfo.DataLeaves(tree, 0).ToArray();
            return leaves.Length == 3 && leaves[1].IsNull && leaves[2].Index == 2 && (int)leaves[2].Pear.Item == 3;
        });
        Check("Parameter inspection retains all-null value-type branches", () =>
        {
            var tree = Grasshopper2.Data.Garden.TreeFromList(new[] { 0, 0 }, null, new[] { true, true });
            return tree.ItemCount == 0 && SuperHelper.Gh2ObjectInfo.DataLeaves(tree, 0).Count(leaf => leaf.IsNull) == 2;
        });

        foreach (var delta in new[] { -120, -1, 0, 1, 120, short.MaxValue })
        {
            var input = new IntPtr(((long)(ushort)(short)delta << 16) | 0x000c);
            var output = SuperHelper.Gh2WindowsCanvasInput.ReverseDelta(input);
            Check($"Wheel delta {delta}: direction and modifier preservation", () => (short)((output.ToInt64() >> 16) & 0xffff) == -delta && (output.ToInt64() & 0xffff) == 0x000c);
            Check($"Wheel delta {delta}: reversing twice restores the message", () => SuperHelper.Gh2WindowsCanvasInput.ReverseDelta(output) == input);
        }
        Check("Wheel minimum delta saturates safely", () => (short)((SuperHelper.Gh2WindowsCanvasInput.ReverseDelta(new IntPtr(0x80000000L)).ToInt64() >> 16) & 0xffff) == short.MaxValue);

        Check("Mac unphased wheel is routed to native GH2 zoom", () => SuperHelper.Gh2MacCanvasInput.IsWheelZoom(0, 0));
        var nativeZoomStep = typeof(Grasshopper2.UI.Flex.FlexControl).Assembly.GetType("Grasshopper2.UI.Flex.MouseWheelZoom")
            .GetMethod("ZoomStep", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(float), typeof(bool), typeof(bool) }, null);
        foreach (var naturalScroll in new[] { false, true })
        foreach (var reverse in new[] { false, true })
        foreach (var rhinoReverse in new[] { false, true })
        foreach (var delta in new[] { -2f, -0.25f, 0.25f, 2f })
        {
            var input = new Eto.Forms.MouseEventArgs(Eto.Forms.MouseButtons.None, Eto.Forms.Keys.None,
                new Eto.Drawing.PointF(100, 100), new Eto.Drawing.SizeF(0, delta), 0, naturalScroll);
            var normalized = SuperHelper.Gh2MacCanvasInput.NormalizeWheel(input, reverse);
            var nativeDelta = normalized.IsDirectionInverted ? normalized.Delta.Height : -normalized.Delta.Height;
            var step = (float)nativeZoomStep.Invoke(null, new object[] { nativeDelta, false, rhinoReverse });
            // GH1 DollyZoom: zoom / (0.85 ^ (delta/2)), reciprocated by Rhino's
            // setting, with the independent preference reversing delta first.
            var expected = Math.Sign(delta) * (reverse ? -1 : 1) * (rhinoReverse ? -1 : 1);
            Check($"Mac GH2 matches GH1: natural={naturalScroll}, reverse={reverse}, Rhino={rhinoReverse}, delta={delta}",
                () => Math.Sign(step) == expected && normalized.IsDirectionInverted == input.IsDirectionInverted);
        }
        foreach (var phase in new long[] { 1, 2, 4, 8, 16, 32, 128 })
        {
            Check($"Mac gesture phase {phase} is preserved", () => !SuperHelper.Gh2MacCanvasInput.IsWheelZoom(phase, 0));
            Check($"Mac momentum phase {phase} is preserved", () => !SuperHelper.Gh2MacCanvasInput.IsWheelZoom(0, phase));
        }
        foreach (var delta in new[] { -3f, -0.25f, 0f, 0.25f, 3f })
        {
            var mouse = new Eto.Forms.MouseEventArgs(Eto.Forms.MouseButtons.Alternate, Eto.Forms.Keys.Control | Eto.Forms.Keys.Shift,
                new Eto.Drawing.PointF(45, 67), new Eto.Drawing.SizeF(2, delta), 0.75f, true);
            var reversed = SuperHelper.Gh2MacCanvasInput.ReverseWheel(mouse);
            Check($"Mac wheel delta {delta} reverses only vertical zoom", () => reversed.Delta.Height == -delta && reversed.Delta.Width == 2);
            Check($"Mac wheel delta {delta} retains native input metadata", () => reversed.Buttons == mouse.Buttons
                && reversed.Modifiers == mouse.Modifiers && reversed.Location == mouse.Location
                && reversed.Pressure == mouse.Pressure && reversed.IsDirectionInverted == mouse.IsDirectionInverted);
            Check($"Mac wheel delta {delta} reverses twice", () => SuperHelper.Gh2MacCanvasInput.ReverseWheel(reversed).Delta == mouse.Delta);
        }

        var scratch = Path.Combine(repository, "artifacts/compatibility-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var host = new TestHost(scratch);
        SuperHelper.HelperHost.Current = host;
        var id = Guid.NewGuid();
        host.Examples.Items[id.ToString()] = new[] { @"C:\案例\点.ghz", "https://example.com/Series.gh" };
        host.Examples.Save();
        Check("Example paths round-trip including Unicode and URLs", () => new SuperHelper.ExampleCatalog(Path.Combine(scratch, "urlex.json")).Items[id.ToString()].SequenceEqual(host.Examples.Items[id.ToString()]));
        var obj = new SuperHelper.HelpObject { ComponentGuid = id, Name = "Curve + Point", Category = "Curve", OfficialUrl = $"https://www.rhino3d.com/docs/grasshopper2/specs/{id:N}.html" };
        SuperHelper.WebHelpMappings.CurrentSource = SuperHelper.WebHelpSource.RhinoOfficial;
        Check("GH2 official help uses component identity", () => SuperHelper.WebHelpMappings.Resolve(obj, null) == obj.OfficialUrl);
        SuperHelper.WebHelpMappings.SaveCurrent(obj, "https://example.com/custom");
        Check("Saved component URL takes precedence", () => SuperHelper.WebHelpMappings.Resolve(obj, null) == "https://example.com/custom");
        SuperHelper.WebHelpMappings.SaveCurrent(obj, "file:///C:/invalid");
        Check("Invalid web URLs cannot overwrite saved mappings", () => SuperHelper.WebHelpMappings.Resolve(obj, null) == "https://example.com/custom");
        SuperHelper.WebHelpMappings.CurrentSource = SuperHelper.WebHelpSource.Gh100;
        Check("Help source maps remain independent", () => SuperHelper.WebHelpMappings.Resolve(obj, null).StartsWith("https://100-gh.com/?s="));
        Check("Saved mappings are persisted in the host directory", () => File.Exists(Path.Combine(scratch, "SuperHelper/urls.official.json")));
        var state = new SuperHelper.HelpWorkspaceState { PanelIds = gh2Ids.ToList(), SelectedPanelId = gh2Ids[4] };
        Check("Workspace layout saves and restores order and selection", () => SuperHelper.HelpWorkspaceStateStore.TrySave(state) && SuperHelper.HelpWorkspaceStateStore.Load().PanelIds.SequenceEqual(gh2Ids) && SuperHelper.HelpWorkspaceStateStore.Load().SelectedPanelId == gh2Ids[4]);
        SuperHelper.SuperSetting.Open = false;
        var count = 0;
        SuperHelper.SuperSetting.OpenChanged += _ => count++;
        SuperHelper.SuperSetting.Open = true;
        SuperHelper.SuperSetting.Open = true;
        SuperHelper.SuperSetting.Open = false;
        Check("Workspace visibility emits only actual state changes", () => count == 2 && !SuperHelper.SuperSetting.Open);
        var report = new { scope = "Managed contracts and installed SDK signatures; no Rhino GUI execution", passed = _failed == 0, checks = Checks, pending = new[] { "GH2 editor and viewport rendering", "Native document migration, solution, drag/drop and undo", "Hops integration", "GH1 and GH2 coexistence in Rhino" } };
        File.WriteAllText(Path.Combine(repository, "artifacts/gh2-contract-checks.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{Checks.Count - _failed}/{Checks.Count} managed checks passed; Rhino GUI validation remains pending.");
        return _failed == 0 ? 0 : 1;
    }

    private sealed class Settings : SuperHelper.IHelperSettings
    {
        private readonly Dictionary<string, object> _values = new();
        public T GetValue<T>(string key, T fallback) => _values.TryGetValue(key, out var value) ? (T)value : fallback;
        public void SetValue<T>(string key, T value) => _values[key] = value;
    }
    private sealed class TestHost : SuperHelper.IHelperHost
    {
        public TestHost(string directory) { SettingsDirectory = directory; Examples = new(Path.Combine(directory, "urlex.json")); }
        public SuperHelper.IHelperSettings Settings { get; } = new Settings();
        public SuperHelper.IWorkspacePlatform Platform => throw new NotSupportedException();
        public string SettingsDirectory { get; }
        public string OfficialHelpRoot => "https://www.rhino3d.com/docs/grasshopper2/";
        public IReadOnlyCollection<string> DocumentExtensions => new[] { ".ghz", ".gh", ".ghx" };
        public SuperHelper.ExampleCatalog Examples { get; }
        public bool DockOnRight => true;
        public bool IsPromptActive => false;
        public bool IsEditorVisible => false;
        public void SwitchSide() => throw new NotSupportedException();
        public void RefreshHelp() => throw new NotSupportedException();
        public void OpenDocument(string path) => throw new NotSupportedException();
        public bool CanUseHops(string path) => false;
        public SuperHelper.IHelpExample CreateExample(string path = null) => throw new NotSupportedException();
        public Task<bool> DragExample(Eto.Forms.Control owner, SuperHelper.IHelpExample example) => Task.FromResult(false);
        public bool TryOpenHelpLink(Uri uri) => false;
        public void ClearHighlight() { }
        public void ShowColorPicker(Eto.Forms.Control owner, System.Drawing.Color color, Action<System.Drawing.Color> changed) => throw new NotSupportedException();
        public void RestoreEditor() => throw new NotSupportedException();
        public IDisposable WatchEditorVisibility(EventHandler handler) => throw new NotSupportedException();
    }
}
