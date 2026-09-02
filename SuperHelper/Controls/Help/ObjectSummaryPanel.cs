using Eto.Drawing;
using Eto.Forms;
using Grasshopper.Kernel;

namespace SuperHelper;

internal sealed class ObjectSummaryPanel : Panel
{
    private readonly ImageView _icon = new() { Width = 24, Height = 24 };
    private readonly Label _title = new()
    {
        Font = SystemFonts.Bold(16f),
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Label _instanceDescription = new()
    {
        TextAlignment = TextAlignment.Center,
        Wrap = WrapMode.Word,
    };
    private readonly Label _description = new() { Wrap = WrapMode.Word };

    public ObjectSummaryPanel()
    {
        Content = new TableLayout
        {
            Spacing = new Size(2, 2),
            Rows =
            {
                new TableRow(new TableLayout
                {
                    Padding = new Padding(4, 2),
                    Rows =
                    {
                        new TableRow(new TableCell(null, true), _icon, _title, new TableCell(null, true)),
                    },
                }),
                new TableRow(_instanceDescription),
                new TableRow(new GroupBox
                {
                    Padding = new Padding(4, 2),
                    Content = _description,
                }),
            },
        };
    }

    public void SetObject(GH_DocumentObject obj)
    {
        if (obj == null)
        {
            _icon.Image = null;
            _title.Text = string.Empty;
            _instanceDescription.Text = string.Empty;
            _description.Text = string.Empty;
            return;
        }

        _icon.Image = EtoImageUtilities.FromBitmap(obj.Icon_24x24);
        _title.Text = $"{obj.Name} ({obj.NickName})";
        _instanceDescription.Text = obj is IGH_Param
            ? string.Empty
            : obj.InstanceDescription ?? string.Empty;
        _description.Text = obj.Description ?? string.Empty;
    }
}
