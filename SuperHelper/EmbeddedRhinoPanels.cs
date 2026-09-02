using Eto.Drawing;
using Eto.Forms;
using System;
using System.Collections.Generic;
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
}

internal static class EmbeddedRhinoPanelContent
{
    private static readonly Dictionary<Guid, Control> Controls = new();

    public static void Clear() => Controls.Clear();

    public static void Set(IReadOnlyDictionary<Guid, Control> controls)
    {
        Controls.Clear();
        foreach (var pair in controls)
        {
            Controls[pair.Key] = pair.Value;
        }
    }

    public static void Attach(Panel panel)
    {
        var id = panel.GetType().GUID;
        if (Controls.TryGetValue(id, out var content) && content != null)
        {
            panel.Content = content;
            return;
        }

        panel.Content = new Label
        {
            Text = $"Panel content is unavailable.\n{id}",
            Wrap = WrapMode.Word,
        };
    }
}

public abstract class SuperHelperRhinoPanel : Panel
{
    private const BindingFlags ReflectionFlags =
        BindingFlags.Instance |
        BindingFlags.Public |
        BindingFlags.NonPublic;

    private Panel _sizeParent;

    protected SuperHelperRhinoPanel()
    {
        EmbeddedRhinoPanelContent.Attach(this);
        SizeChanged += (_, _) => FitContent();
        Load += OnPanelLoaded;
        UnLoad += OnPanelUnloaded;
    }

    private void FitContent()
    {
        if (_sizeParent != null)
        {
            FitToContainer(_sizeParent.ClientSize);
        }
    }

    internal void FitToContainer(Size size)
    {
        if (size.Width < 1 || size.Height < 1)
        {
            return;
        }

        if (Size != size)
        {
            Size = size;
        }
        FitNestedPanel(this);
    }

    private static void FitNestedPanel(Panel panel)
    {
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
public sealed class SuperHelperCommandPanel : SuperHelperRhinoPanel;

[Guid("451EB1BD-790D-4B72-9186-A38B47C2FBD5")]
public sealed class SuperHelperViewportPanel : SuperHelperRhinoPanel;

[Guid("A9F1BC68-2869-4BB6-8A65-FA1AF8AD23D6")]
public sealed class SuperHelperOriginHelpPanel : SuperHelperRhinoPanel;

[Guid("7A2DBA7C-7B28-4B4D-A239-7E5422AB4A13")]
public sealed class SuperHelperWebHelpPanel : SuperHelperRhinoPanel;

[Guid("8DB7B7D4-29C3-4B5E-BAD4-8FCEDE0919B1")]
public sealed class SuperHelperExamplePanel : SuperHelperRhinoPanel;

[Guid("142DD13C-B97E-4580-BA34-4BCECE987DB5")]
public sealed class SuperHelperParamsHelpPanel : SuperHelperRhinoPanel;

[Guid("4666D86A-83FA-4BAA-A027-B9683EB8CE05")]
public sealed class SuperHelperDevelopmentHelpPanel : SuperHelperRhinoPanel;

[Guid("215021E3-AC6B-4E48-80C8-F660D9761568")]
public sealed class SuperHelperFileManagerPanel : SuperHelperRhinoPanel;

[Guid("60B8F205-EFA7-4E83-A96F-87CBA6636086")]
public sealed class SuperHelperSettingsPanel : SuperHelperRhinoPanel;
