using Eto.Drawing;
using Eto.Forms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SuperHelper;

internal sealed class FileManagerPanel : Panel
{
    private readonly TextBox _directory = new();
    private readonly CheckBox _openDocument = new()
    {
        Text = "Open Doc",
        ToolTip = "Open document when right clicking a file.",
    };
    private readonly TreeGridView _tree = new();
    private readonly Slider _rowHeight = new() { MinValue = 20, MaxValue = 150, Value = 55 };

    public FileManagerPanel()
    {
        Content = BuildContent();
    }

    private Control BuildContent()
    {
        _directory.Text = SuperHelperPreferences.FileManagerDirectory;
        _openDocument.Checked = SuperHelperPreferences.OpenDocumentWhenRightClick;
        _openDocument.CheckedChanged += (_, _) =>
            SuperHelperPreferences.OpenDocumentWhenRightClick = _openDocument.Checked == true;

        var update = new Button { Text = "Update" };
        update.Click += (_, _) =>
        {
            SuperHelperPreferences.FileManagerDirectory = _directory.Text;
            RefreshTree();
        };

        _tree.Border = BorderType.None;
        _tree.ShowHeader = false;
        _tree.RowHeight = _rowHeight.Value;
        _tree.Columns.Add(new GridColumn
        {
            HeaderText = string.Empty,
            DataCell = new ImageTextCell(0, 1),
            AutoSize = true,
        });
        _tree.Expanding += (_, e) => PopulateDirectoryItem(e.Item as TreeGridItem);
        _tree.CellDoubleClick += async (_, e) => await ActivateFileItem(e.Item, asHops: true);
        _tree.CellClick += async (_, e) =>
        {
            if (e.Buttons.HasFlag(MouseButtons.Alternate))
            {
                await ActivateFileItem(e.Item, asHops: false);
                e.Handled = true;
            }
        };
        _rowHeight.ValueChanged += (_, _) => _tree.RowHeight = _rowHeight.Value;
        RefreshTree();

        var header = new TableLayout
        {
            Spacing = new Size(3, 0),
            Rows =
            {
                new TableRow(
                    new Label
                    {
                        Text = "Directory:",
                        Font = SystemFonts.Bold(),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TableCell(_directory, true),
                    update),
            },
        };
        var footer = new TableLayout
        {
            Spacing = new Size(5, 0),
            Rows = { new TableRow(_openDocument, new TableCell(_rowHeight, true)) },
        };
        return new TableLayout
        {
            Padding = new Padding(8, 1, 8, 2),
            Spacing = new Size(3, 3),
            Rows =
            {
                new TableRow(new TableCell(header, true)),
                new TableRow(new TableCell(new GroupBox { Padding = 2, Content = _tree }, true))
                {
                    ScaleHeight = true,
                },
                new TableRow(new TableCell(footer, true)),
            },
        };
    }

    private void RefreshTree()
    {
        var store = new TreeGridItemCollection();
        var directory = _directory.Text;
        if (Directory.Exists(directory))
        {
            foreach (var path in EnumerateDirectory(directory))
            {
                store.Add(CreateFileItem(path));
            }
        }
        _tree.DataStore = store;
    }

    private static TreeGridItem CreateFileItem(string path)
    {
        var image = File.Exists(path)
            ? EtoImageUtilities.FromBitmap(global::SuperHelper.Properties.Resources.SuperHelperIcon_24)
            : null;
        var item = new TreeGridItem(new object[] { image, Path.GetFileName(path) }) { Tag = path };
        if (Directory.Exists(path))
        {
            item.Children.Add(new TreeGridItem(new object[] { null, string.Empty }));
        }
        return item;
    }

    private static IEnumerable<string> EnumerateDirectory(string directory)
    {
        try
        {
            return Directory.GetDirectories(directory)
                .Concat(Directory.GetFiles(directory).Where(path => HelperHost.Current.DocumentExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void PopulateDirectoryItem(TreeGridItem item)
    {
        if (item?.Tag is not string path || !Directory.Exists(path))
        {
            return;
        }
        if (item.Children.Count != 1 ||
            item.Children[0] is not TreeGridItem placeholder ||
            placeholder.Tag != null)
        {
            return;
        }

        item.Children.Clear();
        foreach (var childPath in EnumerateDirectory(path))
        {
            item.Children.Add(CreateFileItem(childPath));
        }
    }

    private static async Task ActivateFileItem(object itemValue, bool asHops)
    {
        if (itemValue is not TreeGridItem item || item.Tag is not string path || !File.Exists(path))
        {
            return;
        }

        if (!asHops && SuperHelperPreferences.OpenDocumentWhenRightClick)
        {
            HelperHost.Current.OpenDocument(path);
            return;
        }

        using var example = HelperHost.Current.CreateExample(path);
        await example.PasteFromArchive(asHops && HelperHost.Current.CanUseHops(path));
    }
}
