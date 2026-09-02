using Eto.Drawing;
using Eto.Forms;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SuperHelper;

internal sealed class ExamplesPanel : Panel
{
    private readonly ObjectSummaryPanel _objectSummary = new();
    private readonly ObservableCollection<HelpExample> _examples = new();
    private readonly GridView _grid = new();
    private readonly Panel _exampleContent = new();
    private readonly StackLayout _cards = new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 3,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };
    private readonly Slider _heightSlider = new() { MinValue = 20, MaxValue = 150, Value = 55 };
    private GH_DocumentObject _currentObject;
    private bool _editMode;

    public ExamplesPanel()
    {
        Content = BuildContent();
    }

    public void SetCurrentObject(GH_DocumentObject obj)
    {
        _currentObject = obj;
        _objectSummary.SetObject(obj);
    }

    public void SetExamples(GH_DocumentObject obj, IEnumerable<string> paths)
    {
        SetCurrentObject(obj);
        DisposeExamples();
        _examples.Clear();
        foreach (var path in paths ?? Enumerable.Empty<string>())
        {
            _examples.Add(new HelpExample(path));
        }
        RefreshContent();
    }

    public void Save()
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

    public void Shutdown()
    {
        DisposeExamples();
        _examples.Clear();
    }

    private Control BuildContent()
    {
        _grid.DataStore = _examples;
        _grid.Border = BorderType.None;
        _grid.GridLines = GridLines.None;
        _grid.Columns.Add(new GridColumn
        {
            HeaderText = "Path",
            DataCell = new TextBoxCell(nameof(HelpExample.Path)),
            Editable = true,
            AutoSize = true,
        });
        _grid.Columns.Add(new GridColumn
        {
            HeaderText = "Name",
            DataCell = new TextBoxCell(nameof(HelpExample.FileName)),
            AutoSize = true,
        });
        _grid.CellDoubleClick += async (_, e) =>
        {
            if (e.Item is HelpExample example)
            {
                await example.PasteFromArchive(false);
            }
        };

        var edit = new Button { Text = "Edit", MinimumSize = new Size(50, 24) };
        edit.Click += (_, _) =>
        {
            _editMode = !_editMode;
            edit.Text = _editMode ? "Done" : "Edit";
            RefreshContent();
        };
        _heightSlider.ValueChanged += (_, _) => RefreshContent();
        RefreshContent();

        return new TableLayout
        {
            Padding = new Padding(3, 2),
            Spacing = new Size(3, 3),
            Rows =
            {
                new TableRow(_objectSummary),
                new TableRow(new TableCell(new GroupBox { Padding = 2, Content = _exampleContent }, true))
                {
                    ScaleHeight = true,
                },
                new TableRow(new TableCell(new TableLayout
                {
                    Spacing = new Size(5, 0),
                    Rows = { new TableRow(edit, new TableCell(_heightSlider, true)) },
                }, true)),
            },
        };
    }

    private void RefreshContent()
    {
        if (_editMode)
        {
            var add = new Button { Text = "Add" };
            var remove = new Button { Text = "Remove" };
            var save = new Button { Text = "Save" };
            add.Click += (_, _) => _examples.Add(new HelpExample());
            remove.Click += (_, _) =>
            {
                if (_grid.SelectedItem is not HelpExample example)
                {
                    return;
                }
                _examples.Remove(example);
                example.Dispose();
            };
            save.Click += (_, _) => Save();
            _exampleContent.Content = new TableLayout
            {
                Rows =
                {
                    new TableRow(new TableCell(_grid, true)) { ScaleHeight = true },
                    new TableRow(add, remove, save, new TableCell(null, true)),
                },
            };
            return;
        }

        _cards.Items.Clear();
        foreach (var example in _examples)
        {
            _cards.Items.Add(BuildCard(example));
        }
        _exampleContent.Content = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = true,
            ExpandContentHeight = false,
            Content = _cards,
        };
    }

    private Control BuildCard(HelpExample example)
    {
        var height = _heightSlider.Value;
        var thumbnail = new ImageView { Height = height, Width = Math.Max(70, height * 2) };
        var icon = new ImageView { Height = 20, Width = 20 };
        var name = new Label { Font = SystemFonts.Bold(16f), TextAlignment = TextAlignment.Center };
        var author = new Label { Font = SystemFonts.Bold(12f), VerticalAlignment = VerticalAlignment.Bottom };
        var description = new Label { Wrap = WrapMode.Word, TextAlignment = TextAlignment.Center };
        void Update()
        {
            thumbnail.Image = EtoImageUtilities.FromBitmap(example.Thumbnail);
            icon.Image = EtoImageUtilities.FromBitmap(example.Icon);
            name.Text = example.FileName ?? string.Empty;
            author.Text = example.Author ?? string.Empty;
            description.Text = example.Description ?? string.Empty;
        }
        Update();
        example.PropertyChanged += (_, _) => Application.Instance?.AsyncInvoke(Update);

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
                                new TableRow(new TableCell(new TableLayout
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
                                }, true)),
                                new TableRow(description) { ScaleHeight = true },
                            },
                        })
                    {
                        ScaleHeight = true,
                    },
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

    private void DisposeExamples()
    {
        foreach (var example in _examples)
        {
            example.Dispose();
        }
    }
}
