using Eto.Drawing;
using Eto.Forms;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace SuperHelper;

/// <summary>The application UI depends on this boundary, never on a GH document or editor.</summary>
internal interface IHelperHost
{
    IHelperSettings Settings { get; }
    IWorkspacePlatform Platform { get; }
    string SettingsDirectory { get; }
    string OfficialHelpRoot { get; }
    IReadOnlyCollection<string> DocumentExtensions { get; }
    ExampleCatalog Examples { get; }
    bool DockOnRight { get; }
    void SwitchSide();
    void RefreshHelp();
    void OpenDocument(string path);
    bool CanUseHops(string path);
    IHelpExample CreateExample(string path = null);
    Task<bool> DragExample(Control owner, IHelpExample example);
    bool TryOpenHelpLink(Uri uri);
    void ClearHighlight();
    void ShowColorPicker(Control owner, System.Drawing.Color color, Action<System.Drawing.Color> changed);
    bool IsPromptActive { get; }
    bool IsEditorVisible { get; }
    void RestoreEditor();
    IDisposable WatchEditorVisibility(EventHandler handler);
}

internal interface IHelperSettings
{
    T GetValue<T>(string key, T fallback);
    void SetValue<T>(string key, T value);
}

internal static class HelperHost
{
    public static IHelperHost Current { get; set; }
}

/// <summary>Metadata and native operations for one selected object.</summary>
public sealed class HelpObject : IDisposable
{
    public object NativeObject { get; init; }
    public Type RuntimeType { get; init; }
    public string OriginHtml { get; init; }
    public Guid ComponentGuid { get; init; }
    public Guid InstanceGuid { get; init; }
    public string Name { get; init; }
    public string NickName { get; init; }
    public string Description { get; init; }
    public string InstanceDescription { get; init; }
    public string Category { get; init; }
    public string SubCategory { get; init; }
    public string AssemblyLocation { get; init; }
    public string OfficialUrl { get; init; }
    public Image Icon { get; init; }
    public Func<Control> BuildParameterInfo { get; init; }
    public void Dispose() => Icon?.Dispose();
}

internal interface IHelpExample : INotifyPropertyChanged, IDisposable
{
    string Path { get; set; }
    string FileName { get; }
    string Author { get; }
    string Description { get; }
    Image ThumbnailImage { get; }
    Image IconImage { get; }
    Task PasteFromArchive(bool asHops);
    Task CopyFromArchive();
}

internal sealed class ExampleCatalog
{
    private readonly string _path;
    public Dictionary<string, string[]> Items { get; set; } = new();

    public ExampleCatalog(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path))
                Items = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception exception)
        {
            Rhino.RhinoApp.WriteLine($"SuperHelper examples load failed: {exception.Message}");
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path));
        File.WriteAllText(_path, JsonSerializer.Serialize(Items));
    }
}
