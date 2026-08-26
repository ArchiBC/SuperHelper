using Eto.Drawing;
using Eto.Forms;
using Grasshopper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SuperHelper;

public sealed partial class SuperHelperControl
{
    private Control BuildFileManagerPage()
    {
        _directoryTextBox.Text = FileManageWorkDirectorey;
        _openDocumentCheckBox.Checked = OpenDocumentWhenRightClick;
        _openDocumentCheckBox.CheckedChanged += (_, _) =>
            OpenDocumentWhenRightClick = _openDocumentCheckBox.Checked == true;

        var update = new Button { Text = "Update" };
        update.Click += (_, _) =>
        {
            FileManageWorkDirectorey = _directoryTextBox.Text;
            RefreshDirectoryTree();
        };

        _directoryTree.Border = BorderType.None;
        _directoryTree.ShowHeader = false;
        _directoryTree.RowHeight = _fileHeightSlider.Value;
        _directoryTree.Columns.Add(new GridColumn
        {
            HeaderText = string.Empty,
            DataCell = new ImageTextCell(0, 1),
            AutoSize = true,
        });
        _directoryTree.Expanding += (_, e) => PopulateDirectoryItem(e.Item as TreeGridItem);
        _directoryTree.CellDoubleClick += async (_, e) => await ActivateFileItem(e.Item, asHops: true);
        _directoryTree.CellClick += async (_, e) =>
        {
            if (e.Buttons.HasFlag(MouseButtons.Alternate))
            {
                await ActivateFileItem(e.Item, asHops: false);
                e.Handled = true;
            }
        };

        RefreshDirectoryTree();

        _fileHeightSlider.ValueChanged += (_, _) =>
            _directoryTree.RowHeight = _fileHeightSlider.Value;
        var header = new TableLayout
        {
            Spacing = new Size(3, 0),
            Rows =
            {
                new TableRow(
                    new Label { Text = "Directory:", Font = SystemFonts.Bold(), VerticalAlignment = VerticalAlignment.Center },
                    new TableCell(_directoryTextBox, true),
                    update),
            },
        };
        var footer = new TableLayout
        {
            Spacing = new Size(5, 0),
            Rows = { new TableRow(_openDocumentCheckBox, new TableCell(_fileHeightSlider, true)) },
        };

        return new TableLayout
        {
            Padding = new Padding(8, 1, 8, 2),
            Spacing = new Size(3, 3),
            Rows =
            {
                new TableRow(new TableCell(header, true)),
                new TableRow(new TableCell(new GroupBox { Padding = 2, Content = _directoryTree }, true)) { ScaleHeight = true },
                new TableRow(new TableCell(footer, true)),
            },
        };
    }

    private void RefreshDirectoryTree()
    {
        var store = new TreeGridItemCollection();
        var directory = _directoryTextBox.Text;
        if (Directory.Exists(directory))
        {
            foreach (var path in EnumerateDirectory(directory))
            {
                store.Add(CreateFileItem(path));
            }
        }
        _directoryTree.DataStore = store;
    }

    private static TreeGridItem CreateFileItem(string path)
    {
        var image = File.Exists(path) ? ToEtoImage(global::SuperHelper.Properties.Resources.SuperHelperIcon_24) : null;
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
            return Directory.GetDirectories(directory).Concat(Directory.GetFiles(directory, "*.gh")).ToArray();
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

        if (item.Children.Count != 1 || item.Children[0] is not TreeGridItem placeholder || placeholder.Tag != null)
        {
            return;
        }

        item.Children.Clear();
        foreach (var childPath in EnumerateDirectory(path))
        {
            item.Children.Add(CreateFileItem(childPath));
        }
    }

    private async Task ActivateFileItem(object itemValue, bool asHops)
    {
        if (itemValue is not TreeGridItem item || item.Tag is not string path || !File.Exists(path))
        {
            return;
        }

        if (!asHops && OpenDocumentWhenRightClick)
        {
            Instances.DocumentEditor.ScriptAccess_OpenDocument(path);
            return;
        }

        using var example = new HelpExample(path);
        await example.PasteFromArchive(asHops);
    }
}
