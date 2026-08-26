using Eto.Drawing;
using Eto.Forms;
using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using System;
using WinForms = System.Windows.Forms;

namespace SuperHelper;

public sealed partial class SuperHelperControl
{
    private Control BuildSettingsPage()
    {
        _autoTargetCheckBox.Checked = AutoTarget;
        _autoTargetCheckBox.CheckedChanged += (_, _) => AutoTarget = _autoTargetCheckBox.Checked == true;

        _highlightWidthSlider.Value = DisplayWireWidth;
        _highlightWidthSlider.ValueChanged += (_, _) => DisplayWireWidth = _highlightWidthSlider.Value;

        _commandVisibleCheckBox.Checked = true;
        _viewportVisibleCheckBox.Checked = true;
        _helpVisibleCheckBox.Checked = true;
        _commandVisibleCheckBox.CheckedChanged += (_, _) =>
            SetSectionVisible(EmbeddedWorkspaceSection.Command, _commandVisibleCheckBox);
        _viewportVisibleCheckBox.CheckedChanged += (_, _) =>
            SetSectionVisible(EmbeddedWorkspaceSection.Viewport, _viewportVisibleCheckBox);
        _helpVisibleCheckBox.CheckedChanged += (_, _) =>
            SetSectionVisible(EmbeddedWorkspaceSection.Help, _helpVisibleCheckBox);

        var switchSide = new Button { Height = 28 };
        var widthValue = new Label
        {
            Text = DisplayWireWidth.ToString(),
            Width = 34,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _highlightWidthSlider.ValueChanged += (_, _) => widthValue.Text = _highlightWidthSlider.Value.ToString();
        void RefreshSwitchSideText() => switchSide.Text = SimpleAssemblyPriority.IsSuperHelperOnRight
            ? "Move to Left"
            : "Move to Right";

        RefreshSwitchSideText();
        switchSide.Click += (_, _) =>
        {
            SimpleAssemblyPriority.SwitchSide();
            RefreshSwitchSideText();
        };
        var autoTargetRow = new TableLayout
        {
            Spacing = new Size(6, 0),
            Rows =
            {
                new TableRow(
                    _autoTargetCheckBox,
                    new TableCell(null, true),
                    switchSide),
            },
        };

        var visibleSections = new GroupBox
        {
            Text = "Visible Sections",
            Padding = new Padding(7, 5),
            Content = new TableLayout
            {
                Spacing = new Size(18, 0),
                Rows =
                {
                    new TableRow(
                        _commandVisibleCheckBox,
                        _viewportVisibleCheckBox,
                        _helpVisibleCheckBox,
                        new TableCell(null, true)),
                },
            },
        };
        var highlightRow = new TableLayout
        {
            Rows =
            {
                new TableRow(new TableCell(_highlightWidthSlider, true), widthValue),
            },
        };
        var settingsHeader = new GroupBox
        {
            Text = "General",
            Padding = new Padding(7, 5),
            Content = new TableLayout
            {
                Spacing = new Size(6, 6),
                Rows =
                {
                    new TableRow(
                        new Label { Text = "Auto Target:", VerticalAlignment = VerticalAlignment.Center },
                        new TableCell(autoTargetRow, true)),
                },
            },
        };

        var colorSettings = BuildColorSettingsPanel();
        var highlightSettings = new Expander
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
                        new TableCell(highlightRow, true)),
                    new TableRow(new TableCell(colorSettings, true)),
                },
            },
        };
        var settingsContent = new TableLayout
        {
            Padding = new Padding(5, 2, 5, 5),
            Spacing = new Size(2, 3),
            Rows =
            {
                new TableRow(new TableCell(visibleSections, true)),
                new TableRow(new TableCell(settingsHeader, true)),
                new TableRow(new TableCell(highlightSettings, true)),
            },
        };
        _settingsScroll = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = true,
            ExpandContentHeight = false,
            Content = settingsContent,
        };
        _settingsScroll.Load += (_, _) => ResetSettingsScrollPosition();
        return _settingsScroll;
    }

    private Control BuildColorSettingsPanel()
    {
        return new GroupBox
        {
            Text = "Colors",
            Padding = new Padding(7, 5),
            Content = new TableLayout
            {
                Spacing = new Size(7, 5),
                Rows =
                {
                    BuildColorSettingRow("Wire Color", () => WireColor, color => WireColor = color, _wireColorDefault),
                    BuildColorSettingRow("Material Color", () => MaterialColor, color => MaterialColor = color, _materialColorDefault),
                },
            },
        };
    }

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
            new Label
            {
                Text = title,
                VerticalAlignment = VerticalAlignment.Center,
            },
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
        popup.Closed += (_, _) => popup.Dispose();
        popup.Show(WinForms.Cursor.Position);
    }

    private void ResetSettingsScrollPosition()
    {
        if (_settingsScroll != null && !_settingsScroll.IsDisposed)
        {
            _settingsScroll.ScrollPosition = Point.Empty;
        }
        Application.Instance?.AsyncInvoke(() =>
        {
            if (_settingsScroll != null && !_settingsScroll.IsDisposed)
            {
                _settingsScroll.ScrollPosition = Point.Empty;
            }
        });
    }

}
