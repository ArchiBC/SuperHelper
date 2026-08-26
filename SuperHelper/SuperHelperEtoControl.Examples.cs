using Eto.Drawing;
using Eto.Forms;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SuperHelper;

public sealed partial class SuperHelperControl
{
    public void SaveExampleValue()
    {
        if (_currentObject == null)
        {
            return;
        }

        MenuReplacer.UrlExDict[_currentObject.ComponentGuid.ToString()] = _examples
            .Select(example => example.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
        MenuReplacer.SaveUrlExToJson();
    }

    private Control BuildExamplePage()
    {
        _exampleGrid.DataStore = _examples;
        _exampleGrid.Border = BorderType.None;
        _exampleGrid.GridLines = GridLines.None;
        _exampleGrid.Columns.Add(new GridColumn
        {
            HeaderText = "Path",
            DataCell = new TextBoxCell(nameof(HelpExample.Path)),
            Editable = true,
            AutoSize = true,
        });
        _exampleGrid.Columns.Add(new GridColumn
        {
            HeaderText = "Name",
            DataCell = new TextBoxCell(nameof(HelpExample.FileName)),
            AutoSize = true,
        });
        _exampleGrid.CellDoubleClick += async (_, e) =>
        {
            if (e.Item is HelpExample example)
            {
                await example.PasteFromArchive(false);
            }
        };

        var edit = new Button { Text = "Edit", MinimumSize = new Size(50, 24) };
        edit.Click += (_, _) =>
        {
            _exampleEditMode = !_exampleEditMode;
            edit.Text = _exampleEditMode ? "Done" : "Edit";
            RefreshExampleContent();
        };

        _exampleHeightSlider.ValueChanged += (_, _) => RefreshExampleContent();
        RefreshExampleContent();
        var footer = new TableLayout
        {
            Spacing = new Size(5, 0),
            Rows = { new TableRow(edit, new TableCell(_exampleHeightSlider, true)) },
        };

        return new TableLayout
        {
            Padding = new Padding(3, 2),
            Spacing = new Size(3, 3),
            Rows =
            {
                new TableRow(BuildCurrentObjectSummary()),
                new TableRow(new TableCell(new GroupBox { Padding = 2, Content = _exampleContent }, true)) { ScaleHeight = true },
                new TableRow(new TableCell(footer, true)),
            },
        };
    }

    private void RefreshExampleContent()
    {
        if (_exampleEditMode)
        {
            var add = new Button { Text = "Add" };
            var remove = new Button { Text = "Remove" };
            var save = new Button { Text = "Save" };
            add.Click += (_, _) => _examples.Add(new HelpExample());
            remove.Click += (_, _) =>
            {
                if (_exampleGrid.SelectedItem is not HelpExample example)
                {
                    return;
                }
                _examples.Remove(example);
                example.Dispose();
            };
            save.Click += (_, _) => SaveExampleValue();
            _exampleContent.Content = new TableLayout
            {
                Rows =
                {
                    new TableRow(new TableCell(_exampleGrid, true)) { ScaleHeight = true },
                    new TableRow(add, remove, save, new TableCell(null, true)),
                },
            };
            return;
        }

        _exampleCards.Items.Clear();
        foreach (var example in _examples)
        {
            _exampleCards.Items.Add(BuildExampleCard(example));
        }
        _exampleContent.Content = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = true,
            ExpandContentHeight = false,
            Content = _exampleCards,
        };
    }

    private Control BuildExampleCard(HelpExample example)
    {
        var height = _exampleHeightSlider.Value;
        var thumbnail = new ImageView { Height = height, Width = Math.Max(70, height * 2) };
        var icon = new ImageView { Height = 20, Width = 20 };
        var name = new Label { Font = SystemFonts.Bold(16f), TextAlignment = TextAlignment.Center };
        var author = new Label { Font = SystemFonts.Bold(12f), VerticalAlignment = VerticalAlignment.Bottom };
        var description = new Label { Wrap = WrapMode.Word, TextAlignment = TextAlignment.Center };
        void Update()
        {
            thumbnail.Image = ToEtoImage(example.Thumbnail);
            icon.Image = ToEtoImage(example.Icon);
            name.Text = example.FileName ?? string.Empty;
            author.Text = example.Author ?? string.Empty;
            description.Text = example.Description ?? string.Empty;
        }
        Update();
        example.PropertyChanged += (_, _) => Application.Instance?.AsyncInvoke(Update);

        var cardHeader = new TableLayout
        {
            Rows =
            {
                new TableRow(
                    new TableCell(null, true),
                    icon,
                    name,
                    author,
                    new TableCell(null, true)),
            },
        };
        var card = new GroupBox
        {
            Padding = new Padding(4, 3),
            ToolTip = $"{example.FileName}{Environment.NewLine}{example.Description}",
            Content = new TableLayout
            {
                Spacing = new Size(6, 2),
                Rows =
                {
                    new TableRow(
                        thumbnail,
                        new TableLayout
                        {
                            Rows =
                            {
                                new TableRow(new TableCell(cardHeader, true)),
                                new TableRow(description) { ScaleHeight = true },
                            },
                        }) { ScaleHeight = true },
                },
            },
        };
        card.MouseDown += async (_, e) =>
        {
            if (e.Buttons.HasFlag(MouseButtons.Alternate))
            {
                await example.CopyFromArchive();
            }
            else if (e.Buttons.HasFlag(MouseButtons.Primary))
            {
                await example.PasteFromArchive(false);
            }
        };
        return card;
    }

    private void UpdateExamples(IEnumerable<string> paths)
    {
        foreach (var example in _examples)
        {
            example.Dispose();
        }

        _examples.Clear();
        foreach (var path in paths ?? Enumerable.Empty<string>())
        {
            _examples.Add(new HelpExample(path));
        }
        RefreshExampleContent();
    }
}
