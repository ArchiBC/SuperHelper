using Eto.Drawing;
using Eto.Forms;
using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using System;
using WinForms = System.Windows.Forms;

namespace SuperHelper;

internal sealed class SettingsPanel : Panel
{
    private readonly CheckBox _autoTarget = new();
    private readonly CheckBox _commandVisible = new() { Text = "Command" };
    private readonly CheckBox _viewportVisible = new() { Text = "Viewport" };
    private readonly CheckBox _helpVisible = new() { Text = "Help" };
    private readonly CheckBox _reverseCanvasWheel = new()
    {
        Text = "Reverse wheel direction",
        ToolTip = "Reverse Grasshopper canvas zoom without changing Rhino viewport zoom or help scrolling.",
    };
    private readonly Slider _wireWidth = new() { MinValue = 1, MaxValue = 15 };
    private Scrollable _scroll;
    private bool _syncingVisibility;

    public SettingsPanel()
    {
        Content = BuildContent();
    }

    public event Action<EmbeddedWorkspaceSection, bool> SectionVisibilityChanged;

    public void SetSectionVisibility(bool command, bool viewport, bool help)
    {
        _syncingVisibility = true;
        try
        {
            _commandVisible.Checked = command;
            _viewportVisible.Checked = viewport;
            _helpVisible.Checked = help;
        }
        finally
        {
            _syncingVisibility = false;
        }
    }

    public void ResetScrollPosition()
    {
        if (_scroll != null && !_scroll.IsDisposed)
        {
            _scroll.ScrollPosition = Point.Empty;
        }
        Application.Instance?.AsyncInvoke(() =>
        {
            if (_scroll != null && !_scroll.IsDisposed)
            {
                _scroll.ScrollPosition = Point.Empty;
            }
        });
    }

    private Control BuildContent()
    {
        _autoTarget.Checked = SuperHelperPreferences.AutoTarget;
        _autoTarget.CheckedChanged += (_, _) =>
            SuperHelperPreferences.AutoTarget = _autoTarget.Checked == true;

        _reverseCanvasWheel.Checked = SuperHelperPreferences.ReverseCanvasWheelZoom;
        _reverseCanvasWheel.CheckedChanged += (_, _) =>
            SuperHelperPreferences.ReverseCanvasWheelZoom = _reverseCanvasWheel.Checked == true;

        _wireWidth.Value = SuperHelperPreferences.DisplayWireWidth;
        _wireWidth.ValueChanged += (_, _) =>
            SuperHelperPreferences.DisplayWireWidth = _wireWidth.Value;

        _commandVisible.CheckedChanged += (_, _) => RaiseSectionVisibility(
            EmbeddedWorkspaceSection.Command,
            _commandVisible);
        _viewportVisible.CheckedChanged += (_, _) => RaiseSectionVisibility(
            EmbeddedWorkspaceSection.Viewport,
            _viewportVisible);
        _helpVisible.CheckedChanged += (_, _) => RaiseSectionVisibility(
            EmbeddedWorkspaceSection.Help,
            _helpVisible);

        var switchSide = new Button { Height = 28 };
        void RefreshSwitchSideText() => switchSide.Text = SuperHelperAssemblyPriority.IsSuperHelperOnRight
            ? "Move to Left"
            : "Move to Right";
        RefreshSwitchSideText();
        switchSide.Click += (_, _) =>
        {
            SuperHelperAssemblyPriority.SwitchSide();
            RefreshSwitchSideText();
        };

        var widthValue = new Label
        {
            Text = SuperHelperPreferences.DisplayWireWidth.ToString(),
            Width = 34,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _wireWidth.ValueChanged += (_, _) => widthValue.Text = _wireWidth.Value.ToString();

        var settingsContent = new TableLayout
        {
            Padding = new Padding(5, 2, 5, 5),
            Spacing = new Size(2, 3),
            Rows =
            {
                new TableRow(new TableCell(BuildVisibleSections(), true)),
                new TableRow(new TableCell(BuildGeneralSettings(switchSide), true)),
                new TableRow(new TableCell(BuildHighlightSettings(widthValue), true)),
            },
        };
        _scroll = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = true,
            ExpandContentHeight = false,
            Content = settingsContent,
        };
        _scroll.Load += (_, _) => ResetScrollPosition();
        return _scroll;
    }

    private Control BuildVisibleSections() => new GroupBox
    {
        Text = "Visible Sections",
        Padding = new Padding(7, 5),
        Content = new TableLayout
        {
            Spacing = new Size(18, 0),
            Rows =
            {
                new TableRow(
                    _commandVisible,
                    _viewportVisible,
                    _helpVisible,
                    new TableCell(null, true)),
            },
        },
    };

    private Control BuildGeneralSettings(Button switchSide)
    {
        var layout = new TableLayout
        {
            Spacing = new Size(6, 6),
            Rows =
            {
                new TableRow(
                    new Label { Text = "Auto Target:", VerticalAlignment = VerticalAlignment.Center },
                    new TableCell(new TableLayout
                    {
                        Spacing = new Size(6, 0),
                        Rows =
                        {
                            new TableRow(_autoTarget, new TableCell(null, true), switchSide),
                        },
                    }, true)),
            },
        };
        if (Rhino.Runtime.HostUtils.RunningOnOSX)
        {
            layout.Rows.Add(new TableRow(
                new Label { Text = "GH Canvas Zoom:", VerticalAlignment = VerticalAlignment.Center },
                new TableCell(_reverseCanvasWheel, true)));
        }
        return new GroupBox { Text = "General", Padding = new Padding(7, 5), Content = layout };
    }

    private Control BuildHighlightSettings(Label widthValue) => new Expander
    {
        Header = "Highlight Appearance",
        Expanded = false,
        Content = new TableLayout
        {
            Padding = new Padding(7, 5),
            Spacing = new Size(5, 6),
            Rows =
            {
                new TableRow(
                    new Label { Text = "Wire Width:", VerticalAlignment = VerticalAlignment.Center },
                    new TableCell(new TableLayout
                    {
                        Rows =
                        {
                            new TableRow(new TableCell(_wireWidth, true), widthValue),
                        },
                    }, true)),
                new TableRow(new TableCell(BuildColorSettings(), true)),
            },
        },
    };

    private static Control BuildColorSettings() => new GroupBox
    {
        Text = "Colors",
        Padding = new Padding(7, 5),
        Content = new TableLayout
        {
            Spacing = new Size(7, 5),
            Rows =
            {
                BuildColorSettingRow(
                    "Wire Color",
                    () => SuperHelperPreferences.WireColor,
                    color => SuperHelperPreferences.WireColor = color,
                    SuperHelperPreferences.DefaultWireColor),
                BuildColorSettingRow(
                    "Material Color",
                    () => SuperHelperPreferences.MaterialColor,
                    color => SuperHelperPreferences.MaterialColor = color,
                    SuperHelperPreferences.DefaultMaterialColor),
            },
        },
    };

    private static TableRow BuildColorSettingRow(
        string title,
        Func<System.Drawing.Color> getColor,
        Action<System.Drawing.Color> setColor,
        System.Drawing.Color defaultColor)
    {
        var pickerButton = new Button
        {
            Width = 58,
            Height = 26,
            ToolTip = $"Choose {title.ToLowerInvariant()}",
        };
        var value = new Label
        {
            Width = 142,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var reset = new Button { Text = "Reset", Width = 58, Height = 26 };

        void Refresh(System.Drawing.Color color)
        {
            pickerButton.BackgroundColor = Color.FromArgb(color.R, color.G, color.B, color.A);
            value.Text = $"R {color.R}  G {color.G}  B {color.B}  A {color.A}";
        }

        pickerButton.Click += (_, _) => ShowGrasshopperColorPicker(getColor(), color =>
        {
            setColor(color);
            Refresh(color);
        });
        reset.Click += (_, _) =>
        {
            setColor(defaultColor);
            Refresh(defaultColor);
        };
        Refresh(getColor());

        return new TableRow(
            new Label { Text = title, VerticalAlignment = VerticalAlignment.Center },
            new TableCell(pickerButton),
            new TableCell(value),
            new TableCell(null, true),
            reset);
    }

    private static void ShowGrasshopperColorPicker(
        System.Drawing.Color initialColor,
        Action<System.Drawing.Color> colorChanged)
    {
        var popup = new WinForms.ToolStripDropDown
        {
            AutoClose = true,
            Padding = WinForms.Padding.Empty,
        };
        GH_DocumentObject.ColourEventHandler handler = (_, args) =>
        {
            colorChanged(args.Colour);
            if (!args.Intermediate)
            {
                popup.Close();
            }
        };
        var picker = GH_DocumentObject.Menu_AppendColourPicker(popup, initialColor, handler);
        picker.AllowNumericInput = true;
        picker.ShowChannelSliders = true;
        picker.ShowAlphaSlider = true;
        WinFormsDropDownLifetime.DisposeOnIdleAfterClosed(popup);
        popup.Show(WinForms.Cursor.Position);
    }

    private void RaiseSectionVisibility(EmbeddedWorkspaceSection section, CheckBox checkBox)
    {
        if (!_syncingVisibility)
        {
            SectionVisibilityChanged?.Invoke(section, checkBox.Checked == true);
        }
    }
}
