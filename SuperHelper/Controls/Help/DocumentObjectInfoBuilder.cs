using Eto.Drawing;
using Eto.Forms;
using Grasshopper.Kernel;
using System.Collections.Generic;

namespace SuperHelper;

internal static class DocumentObjectInfoBuilder
{
    public static Control Build(IGH_DocumentObject obj)
    {
        Control details = obj switch
        {
            IGH_Component component => BuildComponentInfo(component),
            IGH_Param parameter => ParameterInfoBuilder.Build(parameter),
            _ => null,
        };
        return new TableLayout
        {
            Spacing = new Size(3, 3),
            Rows =
            {
                new TableRow(BuildObjectSummary(obj)),
                new TableRow(new TableCell(details, true)) { ScaleHeight = true },
            },
        };
    }

    private static Control BuildComponentInfo(IGH_Component component)
    {
        var inputs = CreateParameterColumn();
        var outputs = CreateParameterColumn();
        foreach (var parameter in component.Params.Input)
        {
            inputs.Items.Add(Build(parameter));
        }
        foreach (var parameter in component.Params.Output)
        {
            outputs.Items.Add(Build(parameter));
        }

        return new TableLayout
        {
            Spacing = new Size(6, 3),
            Rows =
            {
                new TableRow(
                    new Label { Text = "Inputs", Font = SystemFonts.Bold(16f), TextAlignment = TextAlignment.Center },
                    new Label { Text = "Outputs", Font = SystemFonts.Bold(16f), TextAlignment = TextAlignment.Center }),
                new TableRow(
                    new TableCell(new GroupBox { Padding = 2, Content = CreateScroll(inputs) }, true),
                    new TableCell(new GroupBox { Padding = 2, Content = CreateScroll(outputs) }, true))
                {
                    ScaleHeight = true,
                },
            },
        };
    }

    private static StackLayout CreateParameterColumn() => new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 6,
        Padding = 6,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };

    private static Scrollable CreateScroll(Control content) => new()
    {
        Border = BorderType.None,
        ExpandContentWidth = true,
        ExpandContentHeight = false,
        Content = content,
    };

    private static Control BuildObjectSummary(IGH_DocumentObject obj)
    {
        var icon = new ImageView
        {
            Image = EtoImageUtilities.FromBitmap(obj.Icon_24x24),
            Width = 24,
            Height = 24,
        };
        var title = new Label
        {
            Text = $"{obj.Name} ({obj.NickName})",
            Font = SystemFonts.Bold(14f),
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var header = new TableLayout
        {
            Padding = new Padding(4, 2),
            Rows =
            {
                new TableRow(new TableCell(null, true), icon, title, new TableCell(null, true)),
            },
        };
        var rows = new List<TableRow> { new(header) };
        if (obj is not IGH_Param && !string.IsNullOrWhiteSpace(obj.InstanceDescription))
        {
            rows.Add(new TableRow(new Label
            {
                Text = obj.InstanceDescription,
                TextAlignment = TextAlignment.Center,
                Wrap = WrapMode.Word,
            }));
        }
        rows.Add(new TableRow(new GroupBox
        {
            Padding = new Padding(4, 2),
            Content = new Label { Text = obj.Description ?? string.Empty, Wrap = WrapMode.Word },
        }));

        var layout = new TableLayout { Spacing = new Size(2, 2) };
        foreach (var row in rows)
        {
            layout.Rows.Add(row);
        }
        return layout;
    }
}
