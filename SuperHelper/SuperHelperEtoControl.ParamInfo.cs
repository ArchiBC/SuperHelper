using Eto.Drawing;
using Eto.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace SuperHelper;

public sealed partial class SuperHelperControl
{
    private Control BuildParameterInfo(IGH_Param parameter)
    {
        var detail = new Panel
        {
            Content = new Label
            {
                Text = parameter.InstanceDescription ?? string.Empty,
                Wrap = WrapMode.Word,
            },
        };
        var toggle = new Button { Text = "Show Hierarchy", MinimumSize = new Size(90, 24) };
        var expanded = false;
        toggle.Click += (_, _) =>
        {
            expanded = !expanded;
            toggle.Text = expanded ? "Hide Hierarchy" : "Show Hierarchy";
            detail.Content = expanded
                ? BuildParameterHierarchy(parameter)
                : new Label { Text = parameter.InstanceDescription ?? string.Empty, Wrap = WrapMode.Word };
        };

        var toggleBar = new TableLayout
        {
            Rows =
            {
                new TableRow(new TableCell(null, true), toggle, new TableCell(null, true)),
            },
        };
        return new GroupBox
        {
            Padding = new Padding(4, 3),
            Content = new TableLayout
            {
                Spacing = new Size(3, 3),
                Rows =
                {
                    new TableRow(new TableCell(toggleBar, true)),
                    new TableRow(new TableCell(detail, true)),
                    new TableRow(new TableCell(null, true)) { ScaleHeight = true },
                },
            },
        };
    }

    private Control BuildParameterHierarchy(IGH_Param parameter)
    {
        var structure = parameter.VolatileData;
        var tree = new TreeGridView
        {
            Border = BorderType.None,
            ShowHeader = false,
            RowHeight = 24,
        };
        tree.Columns.Add(new GridColumn { DataCell = new TextBoxCell(0), Width = 105 });
        tree.Columns.Add(new GridColumn { DataCell = new TextBoxCell(1), AutoSize = true });
        var store = new TreeGridItemCollection();
        if (structure != null)
        {
            var expand = structure.PathCount < 15 && structure.DataCount < 100;
            for (var pathIndex = 0; pathIndex < structure.PathCount; pathIndex++)
            {
                var path = structure.get_Path(pathIndex);
                var branch = structure.get_Branch(pathIndex);
                var branchItem = new TreeGridItem(new object[] { path.ToString(), $"N = {branch?.Count ?? 0}" })
                {
                    Expanded = expand,
                };
                if (branch != null)
                {
                    for (var itemIndex = 0; itemIndex < branch.Count; itemIndex++)
                    {
                        var goo = branch[itemIndex] as IGH_Goo;
                        branchItem.Children.Add(new TreeGridItem(new object[]
                        {
                            itemIndex.ToString(),
                            goo?.ToString() ?? branch[itemIndex]?.ToString() ?? "null",
                        }) { Tag = goo });
                    }
                }
                store.Add(branchItem);
            }
        }
        tree.DataStore = store;
        tree.SelectedItemChanged += (_, _) => HighlightTreeItem(tree.SelectedItem as TreeGridItem);

        return new TableLayout
        {
            Rows =
            {
                new TableRow(new Label
                {
                    Text = $"Sources: {parameter.Sources?.Count ?? 0}",
                    TextAlignment = TextAlignment.Center,
                    TextColor = Colors.Gray,
                }),
                new TableRow(new Label
                {
                    Text = BuildStructureBrief(structure),
                    Font = SystemFonts.Bold(),
                    TextAlignment = TextAlignment.Center,
                }),
                new TableRow(new TableCell(tree, true)),
            },
        };
    }

    private static string BuildStructureBrief(IGH_Structure structure)
    {
        if (structure == null || structure.DataCount == 0)
        {
            return "Empty parameter";
        }
        if (structure.PathCount == 1)
        {
            var count = structure.get_Branch(0)?.Count ?? 0;
            return count == 1 ? "Branch(List) with 1 item" : $"Branch(List) with {count} items";
        }
        return $"Tree with {structure.PathCount} branches and {structure.DataCount} items";
    }

    private static void HighlightTreeItem(TreeGridItem item)
    {
        HighLightConduit.HighLightObject = item?.Tag is IGH_PreviewData preview
            ? new[] { preview }
            : null;
        RedrawRhinoViews();
    }
}
