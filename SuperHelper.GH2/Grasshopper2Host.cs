using Eto.Drawing;
using Eto.Forms;
using Grasshopper2;
using Grasshopper2.UI;
using Grasshopper2.UI.Canvas;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SuperHelper;

internal sealed class Grasshopper2Host : IHelperHost
{
    private readonly Gh2Workspace _workspace;
    public Grasshopper2Host(Gh2Workspace workspace)
    {
        _workspace = workspace;
        Settings = new Gh2Settings();
        Examples = new ExampleCatalog(Path.Combine(SettingsDirectory, "urlex.json"));
    }
    public IHelperSettings Settings { get; }
    public IWorkspacePlatform Platform { get; } = Rhino.Runtime.HostUtils.RunningOnOSX
        ? new MacWorkspacePlatform()
        : new WindowsWorkspacePlatform();
    public string SettingsDirectory => Path.Combine(Folders.SettingsFolder, "SuperHelper");
    public string OfficialHelpRoot => "https://www.rhino3d.com/docs/grasshopper2/";
    public IReadOnlyCollection<string> DocumentExtensions { get; } = new[] { ".ghz", ".gh", ".ghx" };
    public ExampleCatalog Examples { get; }
    public bool DockOnRight => _workspace.DockOnRight;
    public void SwitchSide() => _workspace.SwitchSide();
    public void RefreshHelp() => _workspace.RefreshHelp();
    public void OpenDocument(string path)
    {
        if (Editor.Instance?.Documents.TryOpenDocument(path, OpenDocumentOptions.Default) != true)
            throw new IOException("Grasshopper 2 could not open " + path);
    }
    public IHelpExample CreateExample(string path = null) => new Gh2Example(path);
    public System.Threading.Tasks.Task<bool> DragExample(Control owner, IHelpExample example) => ((Gh2Example)example).DragFrom(owner);
    public bool TryOpenHelpLink(Uri uri)
    {
        switch (uri?.Scheme.ToLowerInvariant())
        {
            case "term": Grasshopper2.Documentation.Docs.ShowTermInNewWindow(Gh2Documentation.Unescape(uri.Host)); return true;
            case "topic": Grasshopper2.Documentation.Docs.ShowTopicInNewWindow(Gh2Documentation.Unescape(uri.Host)); return true;
            case "spec":
                if (Guid.TryParse(uri.Host, out var id)) Grasshopper2.Documentation.Docs.ShowSpecInNewWindow(id);
                return true;
            case "search": return true;
            default: return false;
        }
    }
    public bool CanUseHops(string path) => Path.GetExtension(path).Equals(".gh", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".ghx", StringComparison.OrdinalIgnoreCase);
    public void ClearHighlight() => Gh2HighlightConduit.Set(null);
    // GH1 and Rhino can start getters while GH2 is hidden. Only GH2's own
    // scheduled getter may restore a hidden editor; otherwise it steals focus.
    public bool IsPromptActive => Gh2PromptVisibilityBinding.GetterScheduled
        || IsEditorVisible && Rhino.Input.RhinoGet.InGet(Rhino.RhinoDoc.ActiveDoc);
    public bool IsEditorVisible => Editor.Instance is { Visible: true, Collapsed: false, WindowState: not WindowState.Minimized };
    public void RestoreEditor()
    {
        if (Editor.Instance is not { IsDisposed: false } editor) return;
        editor.Collapsed = false;
        if (editor.WindowState == WindowState.Minimized) editor.WindowState = WindowState.Normal;
        if (!editor.Visible) editor.Show();
        editor.BringToFront();
    }
    public IDisposable WatchEditorVisibility(EventHandler handler) => new Gh2PromptVisibilityBinding(Editor.Instance, handler);
    public void ShowColorPicker(Control owner, System.Drawing.Color color, Action<System.Drawing.Color> changed)
    {
        using var dialog = new ColorDialog { Color = Color.FromArgb(color.R, color.G, color.B, color.A), AllowAlpha = true };
        if (dialog.ShowDialog(owner) == DialogResult.Ok)
        {
            var selected = dialog.Color;
            changed(System.Drawing.Color.FromArgb(selected.Ab, selected.Rb, selected.Gb, selected.Bb));
        }
    }

    private sealed class Gh2Settings : IHelperSettings
    {
        private readonly SettingsFile _file = SettingsFile.InDefaultFolder("SuperHelper");
        public Gh2Settings() => _file.TryLoadSettingsFromFile();
        public T GetValue<T>(string key, T fallback) => (T)(fallback switch
        {
            bool value => (object)_file.Get(key, value),
            int value => _file.Get(key, value),
            string value => _file.Get(key, value),
            DateTime value => _file.Get(key, value),
            System.Drawing.Color value => ReadColor(key, value),
            _ => throw new NotSupportedException(typeof(T).FullName),
        });
        private System.Drawing.Color ReadColor(string key, System.Drawing.Color fallback)
        {
            var c = _file.Get(key, Color.FromArgb(fallback.R, fallback.G, fallback.B, fallback.A));
            return System.Drawing.Color.FromArgb(c.Ab, c.Rb, c.Gb, c.Bb);
        }
        public void SetValue<T>(string key, T value)
        {
            switch (value)
            {
                case bool item: _file.Set(key, item); break;
                case int item: _file.Set(key, item); break;
                case string item: _file.Set(key, item); break;
                case DateTime item: _file.Set(key, item); break;
                case System.Drawing.Color item: _file.Set(key, Color.FromArgb(item.R, item.G, item.B, item.A)); break;
                default: throw new NotSupportedException(typeof(T).FullName);
            }
            _file.TrySaveSettingsToFile();
        }
    }
}
