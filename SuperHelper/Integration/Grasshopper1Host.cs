using Eto.Forms;
using Grasshopper;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using WinForms = System.Windows.Forms;

namespace SuperHelper;

internal sealed class Grasshopper1Host : IHelperHost
{
    private static readonly FieldInfo InGet = typeof(GH_Document).GetField("InGet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo FadeOut = typeof(GH_Document).GetField("FadeOut", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    public IHelperSettings Settings { get; } = new Grasshopper1Settings();
    public IWorkspacePlatform Platform { get; } = Rhino.Runtime.HostUtils.RunningOnOSX ? new MacWorkspacePlatform() : new WindowsWorkspacePlatform();
    public string SettingsDirectory => Folders.SettingsFolder;
    public string OfficialHelpRoot => "https://www.rhino3d.com/docs/grasshopper/";
    public IReadOnlyCollection<string> DocumentExtensions { get; } = new[] { ".gh", ".ghx" };
    public ExampleCatalog Examples { get; } = new(Path.Combine(Folders.SettingsFolder, "urlex.json"));
    public bool DockOnRight => SuperHelperAssemblyPriority.IsSuperHelperOnRight;
    public void SwitchSide() => SuperHelperAssemblyPriority.SwitchSide();
    public void RefreshHelp() => MenuReplacer.RefreshCurrentObject();
    public void OpenDocument(string path) => Instances.DocumentEditor?.ScriptAccess_OpenDocument(path);
    public bool CanUseHops(string path) => Path.GetExtension(path).Equals(".gh", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".ghx", StringComparison.OrdinalIgnoreCase);
    public IHelpExample CreateExample(string path = null) => path == null ? new HelpExample() : new HelpExample(path);
    public System.Threading.Tasks.Task<bool> DragExample(Control owner, IHelpExample example) => System.Threading.Tasks.Task.FromResult(false);
    public bool TryOpenHelpLink(Uri uri) => false;
    public void ClearHighlight() => HighLightConduit.HighLightObject = null;
    public bool IsPromptActive => Instances.ActiveCanvas?.Document is { } doc && (InGet?.GetValue(doc) is true || FadeOut?.GetValue(doc) is true);
    public bool IsEditorVisible => Instances.DocumentEditor is { IsDisposed: false, Visible: true, WindowState: not WinForms.FormWindowState.Minimized };

    public void RestoreEditor()
    {
        if (Instances.DocumentEditor is not { IsDisposed: false } editor) return;
        if (editor.WindowState == WinForms.FormWindowState.Minimized) editor.WindowState = WinForms.FormWindowState.Normal;
        if (!editor.Visible) editor.Show();
        editor.Activate();
    }

    public IDisposable WatchEditorVisibility(EventHandler handler) => new VisibilityBinding(handler);

    public void ShowColorPicker(Control owner, System.Drawing.Color color, Action<System.Drawing.Color> changed)
    {
        var popup = new WinForms.ToolStripDropDown { AutoClose = true, Padding = WinForms.Padding.Empty };
        var picker = GH_DocumentObject.Menu_AppendColourPicker(popup, color, (_, args) =>
        {
            changed(args.Colour);
            if (!args.Intermediate) popup.Close();
        });
        picker.AllowNumericInput = true;
        picker.ShowChannelSliders = true;
        picker.ShowAlphaSlider = true;
        WinFormsDropDownLifetime.DisposeOnIdleAfterClosed(popup);
        popup.Show(WinForms.Cursor.Position);
    }

    public static HelpObject Describe(GH_DocumentObject obj)
    {
        var assembly = obj.GetType().Assembly;
        var library = Instances.ComponentServer.Libraries.FirstOrDefault(item => item.Assembly == assembly);
        return new HelpObject
        {
            NativeObject = obj, ComponentGuid = obj.ComponentGuid, InstanceGuid = obj.InstanceGuid,
            Name = obj.Name, NickName = obj.NickName, Description = obj.Description,
            InstanceDescription = obj is IGH_Param ? string.Empty : obj.InstanceDescription,
            Category = obj.Category, SubCategory = obj.SubCategory,
            AssemblyLocation = !string.IsNullOrWhiteSpace(library?.Location) && File.Exists(library.Location) ? library.Location : assembly.Location,
            Icon = EtoImageUtilities.FromBitmap(obj.Icon_24x24),
            BuildParameterInfo = () => DocumentObjectInfoBuilder.Build(obj),
        };
    }

    private sealed class VisibilityBinding : IDisposable
    {
        private readonly Grasshopper.GUI.GH_DocumentEditor _editor = Instances.DocumentEditor;
        private readonly EventHandler _handler;
        public VisibilityBinding(EventHandler handler)
        {
            _handler = handler;
            if (_editor == null) return;
            _editor.VisibleChanged += handler;
            _editor.Resize += handler;
        }
        public void Dispose()
        {
            if (_editor == null) return;
            _editor.VisibleChanged -= _handler;
            _editor.Resize -= _handler;
        }
    }

    private sealed class Grasshopper1Settings : IHelperSettings
    {
        public T GetValue<T>(string key, T fallback) => (T)(fallback switch
        {
            bool value => (object)Instances.Settings.GetValue(key, value),
            int value => Instances.Settings.GetValue(key, value),
            string value => Instances.Settings.GetValue(key, value),
            DateTime value => Instances.Settings.GetValue(key, value),
            System.Drawing.Color value => Instances.Settings.GetValue(key, value),
            _ => throw new NotSupportedException(typeof(T).FullName),
        });
        public void SetValue<T>(string key, T value)
        {
            switch (value)
            {
                case bool item: Instances.Settings.SetValue(key, item); break;
                case int item: Instances.Settings.SetValue(key, item); break;
                case string item: Instances.Settings.SetValue(key, item); break;
                case DateTime item: Instances.Settings.SetValue(key, item); break;
                case System.Drawing.Color item: Instances.Settings.SetValue(key, item); break;
                default: throw new NotSupportedException(typeof(T).FullName);
            }
        }
    }
}
