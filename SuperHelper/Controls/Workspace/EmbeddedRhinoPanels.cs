using Eto.Drawing;
using Eto.Forms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SuperHelper;

internal static class EmbeddedRhinoPanelIds
{
    public static readonly Guid Command = typeof(SuperHelperCommandPanel).GUID;
    public static readonly Guid Viewport = typeof(SuperHelperViewportPanel).GUID;
    public static readonly Guid OriginHelp = typeof(SuperHelperOriginHelpPanel).GUID;
    public static readonly Guid WebHelp = typeof(SuperHelperWebHelpPanel).GUID;
    public static readonly Guid Example = typeof(SuperHelperExamplePanel).GUID;
    public static readonly Guid ParamsHelp = typeof(SuperHelperParamsHelpPanel).GUID;
    public static readonly Guid DevelopmentHelp = typeof(SuperHelperDevelopmentHelpPanel).GUID;
    public static readonly Guid FileManager = typeof(SuperHelperFileManagerPanel).GUID;
    public static readonly Guid Settings = typeof(SuperHelperSettingsPanel).GUID;

    public static IReadOnlyList<Guid> HelpPages { get; } =
    [
        OriginHelp,
        WebHelp,
        Example,
        ParamsHelp,
        DevelopmentHelp,
        FileManager,
        Settings,
    ];

    public static IReadOnlyList<Guid> All { get; } =
    [
        Command,
        Viewport,
        .. HelpPages,
    ];

    // Preserve layouts saved before GH2 started sharing GH1's panel identities.
    public static Guid Normalize(Guid id) => id.ToString("N") switch
    {
        "183c0c9c016d58848c8222f5ecb176e6" => Command,
        "54f3a81bfe33520bb476c4dafebd3452" => Viewport,
        "15e9113582e35c278997fc619db74a47" => OriginHelp,
        "5cbb10429563571ebdc1f9165a6d08b6" => WebHelp,
        "55abe78ca08151ef8653c5868cdf5c69" => Example,
        "4e7645497a7254c3b7af260de03db1d7" => ParamsHelp,
        "4efe29a5406b5797b9e22f8e83479b33" => DevelopmentHelp,
        "58cc29e4ec7e590e854ab488ed3e99b2" => FileManager,
        "7669ab5c3d3257719646b32218c0ddd4" => Settings,
        _ => id,
    };
}

internal static class EmbeddedRhinoPanelContent
{
    private const string RegistryKey = "SuperHelper.SharedPanelContentResolvers.v1";

    // Both extensions compile these panels, and either can register first.
    // Keep only BCL/Eto types in the process registry so the registered panel
    // type can resolve the other extension's workspace without loading GH1.
    private static Dictionary<Guid, Func<Guid, Control>> Resolvers
    {
        get
        {
            lock (AppDomain.CurrentDomain)
            {
                if (AppDomain.CurrentDomain.GetData(RegistryKey) is not Dictionary<Guid, Func<Guid, Control>> registry)
                {
                    registry = new();
                    AppDomain.CurrentDomain.SetData(RegistryKey, registry);
                }
                return registry;
            }
        }
    }

    // Registration, lookup and removal run on Rhino's UI thread.
    public static void Register(Guid owner, Func<Guid, Control> resolve) => Resolvers.Add(owner, resolve);
    public static void Unregister(Guid owner) => Resolvers.Remove(owner);

    internal static Control Resolve(Guid instanceId)
    {
        foreach (var resolve in Resolvers.Values.ToArray())
        {
            var content = resolve(instanceId);
            if (content != null && !content.IsDisposed) return content;
        }
        return null;
    }

    public static void Attach(Panel panel, Guid instanceId)
    {
        var content = Resolve(instanceId);
        if (content != null)
        {
            if (!ReferenceEquals(panel.Content, content)) panel.Content = content;
        }
        else if (panel.Content == null)
        {
            panel.Content = new Label { Text = "Open this panel in a SuperHelper workspace.", Wrap = WrapMode.Word };
        }
    }
}

public abstract class SuperHelperRhinoPanel : Panel
{
    private const BindingFlags ReflectionFlags =
        BindingFlags.Instance |
        BindingFlags.Public |
        BindingFlags.NonPublic;

    private Panel _sizeParent;
    private readonly Guid _instanceId;

    protected SuperHelperRhinoPanel(Guid instanceId, Rhino.RhinoDoc _)
    {
        _instanceId = instanceId;
        EmbeddedRhinoPanelContent.Attach(this, instanceId);
        SizeChanged += (_, _) => FitContent();
        Load += OnPanelLoaded;
        UnLoad += OnPanelUnloaded;
    }

    private void FitContent()
    {
        if (_sizeParent != null)
        {
            FitToContainer(this, _sizeParent.ClientSize);
        }
    }

    internal static void FitToContainer(Panel panel, Size size)
    {
        if (size.Width < 1 || size.Height < 1)
        {
            return;
        }

        if (panel.Size != size)
        {
            panel.Size = size;
        }
        FitNestedPanel(panel);
    }

    private static void FitNestedPanel(Panel panel)
    {
        // Size the scroll viewport, but let its document keep its natural
        // height. Clamping it to ClientSize removes the scrollable overflow.
        if (panel is Scrollable)
        {
            return;
        }
        var content = panel.Content;
        if (content == null || panel.ClientSize.Width < 1 || panel.ClientSize.Height < 1)
        {
            return;
        }

        if (content.Size != panel.ClientSize)
        {
            content.Size = panel.ClientSize;
        }
        if (content is Panel childPanel)
        {
            FitNestedPanel(childPanel);
        }
    }

    private void OnPanelLoaded(object sender, EventArgs e)
    {
        // A collection can create its panel before construction has returned
        // and before its workspace has recorded ownership of the collection.
        EmbeddedRhinoPanelContent.Attach(this, _instanceId);
        var sizeParent = FindRhinoContentPanel() ?? Parent as Panel;
        if (sizeParent != null && !ReferenceEquals(sizeParent, _sizeParent))
        {
            DetachSizeParent();
            _sizeParent = sizeParent;
            _sizeParent.SizeChanged += OnSizeParentChanged;
        }
        FitContent();
    }

    private void OnPanelUnloaded(object sender, EventArgs e) => DetachSizeParent();

    private void OnSizeParentChanged(object sender, EventArgs e) => FitContent();

    private void DetachSizeParent()
    {
        if (_sizeParent != null)
        {
            _sizeParent.SizeChanged -= OnSizeParentChanged;
            _sizeParent = null;
        }
    }

    private Panel FindRhinoContentPanel()
    {
        for (Control current = Parent; current != null; current = current.Parent)
        {
            if (!string.Equals(current.GetType().Name, "TabPanelCollectionControl", StringComparison.Ordinal))
            {
                continue;
            }

            return current.GetType().GetProperty("ContentPanel", ReflectionFlags)?.GetValue(current) as Panel;
        }
        return null;
    }

}

[Guid("5C8F5192-7A83-4866-A603-6E5B230E9F01")]
public sealed class SuperHelperCommandPanel(Guid instanceId, Rhino.RhinoDoc document) : SuperHelperRhinoPanel(instanceId, document);

[Guid("451EB1BD-790D-4B72-9186-A38B47C2FBD5")]
public sealed class SuperHelperViewportPanel(Guid instanceId, Rhino.RhinoDoc document) : SuperHelperRhinoPanel(instanceId, document);

[Guid("A9F1BC68-2869-4BB6-8A65-FA1AF8AD23D6")]
public sealed class SuperHelperOriginHelpPanel(Guid instanceId, Rhino.RhinoDoc document) : SuperHelperRhinoPanel(instanceId, document);

[Guid("7A2DBA7C-7B28-4B4D-A239-7E5422AB4A13")]
public sealed class SuperHelperWebHelpPanel(Guid instanceId, Rhino.RhinoDoc document) : SuperHelperRhinoPanel(instanceId, document);

[Guid("8DB7B7D4-29C3-4B5E-BAD4-8FCEDE0919B1")]
public sealed class SuperHelperExamplePanel(Guid instanceId, Rhino.RhinoDoc document) : SuperHelperRhinoPanel(instanceId, document);

[Guid("142DD13C-B97E-4580-BA34-4BCECE987DB5")]
public sealed class SuperHelperParamsHelpPanel(Guid instanceId, Rhino.RhinoDoc document) : SuperHelperRhinoPanel(instanceId, document);

[Guid("4666D86A-83FA-4BAA-A027-B9683EB8CE05")]
public sealed class SuperHelperDevelopmentHelpPanel(Guid instanceId, Rhino.RhinoDoc document) : SuperHelperRhinoPanel(instanceId, document);

[Guid("215021E3-AC6B-4E48-80C8-F660D9761568")]
public sealed class SuperHelperFileManagerPanel(Guid instanceId, Rhino.RhinoDoc document) : SuperHelperRhinoPanel(instanceId, document);

[Guid("60B8F205-EFA7-4E83-A96F-87CBA6636086")]
public sealed class SuperHelperSettingsPanel(Guid instanceId, Rhino.RhinoDoc document) : SuperHelperRhinoPanel(instanceId, document);
