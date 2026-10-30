using Eto.Drawing;
using Eto.Forms;
using Grasshopper2.Components;
using Grasshopper2.Components.Standard;
using Grasshopper2.Data;
using Grasshopper2.Doc;
using Grasshopper2.Framework;
using Grasshopper2.Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SuperHelper;

internal static class Gh2ObjectInfo
{
    public static HelpObject Describe(IDocumentObject obj)
    {
        var id = ComponentId(obj);
        object original = null;
        string originHtml = null;
        if (obj is GH1InteropComponent || obj is Grasshopper2.Parameters.Standard.GH1InteropParameter)
        {
            var wrapper = obj is GH1InteropComponent component
                ? Grasshopper2.Interop.Grasshopper1.Instance.CreateObject(component.Grasshopper1Xml)
                : Grasshopper2.Interop.Grasshopper1.Instance.CreateObject(id);
            original = wrapper?.UnderlyingObject;
            originHtml = original?.GetType().GetMethod("HtmlHelp_Source", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(original, Array.Empty<object>()) as string;
        }
        var runtimeType = original?.GetType() ?? obj.GetType();
        var category = original?.GetType().GetProperty("Category")?.GetValue(original) as string ?? obj.Nomen.Chapter;
        var slug = Uri.EscapeDataString(Regex.Replace(obj.Nomen.Name, @"[^\p{L}\p{Nd}]+", "-").Trim('-'));
        return new HelpObject
        {
            NativeObject = obj, RuntimeType = runtimeType, OriginHtml = originHtml, ComponentGuid = id, InstanceGuid = obj.InstanceId,
            Name = obj.Nomen.Name, NickName = obj.UserName, Description = obj.Nomen.Info,
            InstanceDescription = $"{obj.Phase} · {obj.State.Data?.Duration.TotalMilliseconds ?? 0:0.###} ms",
            Category = obj.Nomen.Chapter, SubCategory = obj.Nomen.Section,
            AssemblyLocation = original != null ? runtimeType.Assembly.Location : PluginServer.FindPluginForObject(obj)?.Location ?? runtimeType.Assembly.Location,
            OfficialUrl = original != null ? $"https://www.rhino3d.com/docs/grasshopper/{Uri.EscapeDataString(category)}/{slug}/" : $"https://www.rhino3d.com/docs/grasshopper2/specs/{id:N}.html",
            Icon = obj.Icon?.DrawToBitmap(new Size(24, 24), 1, Colors.Transparent),
            BuildParameterInfo = () => Build(obj),
        };
    }
    internal static Guid ComponentId(IDocumentObject obj) => obj switch
    {
        GH1InteropComponent component => component.Grasshopper1Id,
        Grasshopper2.Parameters.Standard.GH1InteropParameter parameter => parameter.Grasshopper1Id,
        _ => NativeComponentId(obj.GetType()),
    };
    internal static Guid NativeComponentId(Type type) => GrasshopperIO.IO.GetIoId(type, inherit: false);

    public static string FallbackHtml(IDocumentObject obj) =>
        $"<!doctype html><html><body><h1>{WebUtility.HtmlEncode(obj.Nomen.Name)}</h1><p>{WebUtility.HtmlEncode(obj.Nomen.Info)}</p>" +
        $"<p>{WebUtility.HtmlEncode(obj.Nomen.Chapter)} / {WebUtility.HtmlEncode(obj.Nomen.Section)}</p></body></html>";

    public static Control Build(IDocumentObject obj)
    {
        if (obj is IParameter parameter) return Parameter(parameter);
        if (obj is not Component component) return new Label { Text = obj.Nomen.Info, Wrap = WrapMode.Word };
        var inputs = Column(component.Parameters.Inputs);
        var outputs = Column(component.Parameters.Outputs);
        return new TableLayout
        {
            Spacing = new Size(5, 3),
            Rows =
            {
                new TableRow(new Label { Text = obj.DisplayName, Font = SystemFonts.Bold(16) }),
                new TableRow(new Label { Text = "Inputs", Font = SystemFonts.Bold() }, new Label { Text = "Outputs", Font = SystemFonts.Bold() }),
                new TableRow(new TableCell(Scroll(inputs), true), new TableCell(Scroll(outputs), true)) { ScaleHeight = true },
            },
        };
    }
    private static StackLayout Column(IEnumerable<IParameter> parameters)
    {
        var column = new StackLayout { Spacing = 5, Padding = 4, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        foreach (var parameter in parameters) column.Items.Add(Parameter(parameter));
        return column;
    }
    private static Scrollable Scroll(Control content) => new()
    {
        Border = BorderType.None, ExpandContentWidth = true, ExpandContentHeight = false, Content = content,
    };
    internal static ITree ParameterTree(IParameter parameter, out string source)
    {
        var solved = parameter.State.Data?.Tree() ?? parameter.ParentObject?.State.Data?.Tree(parameter.InstanceId);
        source = solved != null ? "Solution data" : "Persistent data";
        return solved ?? parameter.PersistentDataWeak;
    }
    private static Control Parameter(IParameter parameter)
    {
        var tree = ParameterTree(parameter, out var source);
        var description = $"{parameter.Nomen.Info}\n{parameter.Access} · {parameter.Requirement} · {parameter.Phase}\n" +
            $"Sources: {parameter.Inputs.Count} · {source}: {tree?.PathCount ?? 0} branches, {tree?.LeafCount ?? 0} leaves ({tree?.ItemCount ?? 0} values, {tree?.NullCount ?? 0} nulls)";
        var details = new Panel { Content = new Label { Text = description, Wrap = WrapMode.Word } };
        var toggle = new Button { Text = "Show Hierarchy" };
        var expanded = false;
        toggle.Click += (_, _) =>
        {
            expanded = !expanded;
            toggle.Text = expanded ? "Hide Hierarchy" : "Show Hierarchy";
            details.Content = expanded ? Hierarchy(parameter, tree) : new Label { Text = description, Wrap = WrapMode.Word };
        };
        return new GroupBox
        {
            Text = parameter.DisplayName, Padding = 5,
            Content = new TableLayout { Spacing = new Size(3, 4), Rows = { new TableRow(toggle), new TableRow(details) } },
        };
    }
    private static Control Hierarchy(IParameter parameter, ITree data)
    {
        var tree = new TreeGridView { ShowHeader = false, Border = BorderType.None, RowHeight = 24 };
        tree.Columns.Add(new GridColumn { DataCell = new TextBoxCell(0), Width = 110 });
        tree.Columns.Add(new GridColumn { DataCell = new TextBoxCell(1), AutoSize = true });
        var roots = new TreeGridItemCollection();
        if (data != null)
        {
            for (var pathIndex = 0; pathIndex < data.PathCount; pathIndex++)
            {
                var twig = data.Twigs[pathIndex];
                var root = new TreeGridItem(data.Paths[pathIndex].ToString(), $"N = {twig.LeafCount}")
                { Expanded = data.PathCount < 15 && data.LeafCount < 100 };
                foreach (var leaf in DataLeaves(data, pathIndex))
                {
                    var text = leaf.IsNull || leaf.Pear?.Item == null ? "null" : data.TypeAssistant.Format(leaf.Pear.Item);
                    root.Children.Add(new TreeGridItem(leaf.Index.ToString(), text) { Tag = leaf.IsNull ? null : new Gh2HighlightTarget(parameter, leaf.Pear, data.TypeAssistant) });
                }
                roots.Add(root);
            }
        }
        tree.DataStore = roots;
        tree.SelectedItemChanged += (_, _) => Gh2HighlightConduit.Set((tree.SelectedItem as TreeGridItem)?.Tag as Gh2HighlightTarget);
        return tree;
    }
    internal static IEnumerable<(int Index, bool IsNull, IPear Pear)> DataLeaves(ITree data, int pathIndex)
    {
        for (var index = 0; index < data.Twigs[pathIndex].LeafCount; index++)
            yield return (index, data.NullAt(pathIndex, index), data.Pears[pathIndex, index]);
    }
}
