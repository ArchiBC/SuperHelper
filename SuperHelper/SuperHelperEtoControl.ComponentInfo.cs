using Eto.Drawing;
using Eto.Forms;
using Grasshopper.Kernel;

namespace SuperHelper;

public sealed partial class SuperHelperControl
{
    private Control BuildComponentInfo(IGH_Component component)
    {
        var inputs = new StackLayout
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            Padding = 6,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        var outputs = new StackLayout
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            Padding = 6,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var parameter in component.Params.Input)
        {
            inputs.Items.Add(BuildDocumentObjectInfo(parameter));
        }
        foreach (var parameter in component.Params.Output)
        {
            outputs.Items.Add(BuildDocumentObjectInfo(parameter));
        }

        var inputScroll = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = true,
            ExpandContentHeight = false,
            Content = inputs,
        };
        var outputScroll = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = true,
            ExpandContentHeight = false,
            Content = outputs,
        };
        return new TableLayout
        {
            Spacing = new Size(6, 3),
            Rows =
            {
                new TableRow(
                    new Label { Text = "Inputs", Font = SystemFonts.Bold(16f), TextAlignment = TextAlignment.Center },
                    new Label { Text = "Outputs", Font = SystemFonts.Bold(16f), TextAlignment = TextAlignment.Center }),
                new TableRow(
                    new TableCell(new GroupBox { Padding = 2, Content = inputScroll }, true),
                    new TableCell(new GroupBox { Padding = 2, Content = outputScroll }, true)) { ScaleHeight = true },
            },
        };
    }
}
