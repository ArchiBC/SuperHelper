using Eto.Drawing;
using Eto.Forms;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;

namespace SuperHelper;

public sealed partial class SuperHelperControl
{
    public void RefreshObjectData()
    {
        if (_currentObject == null)
        {
            _parameterContent.Content = null;
            _developmentText.Text = string.Empty;
            _assemblyLocationButton.Text = string.Empty;
            return;
        }

        _parameterContent.Content = BuildDocumentObjectInfo(_currentObject);
        FitPanelContent(_parameterContent);
        _developmentText.Text = BuildTypeInformation(_currentObject);
        _assemblyLocationButton.Text = FindAssemblyLocation(_currentObject);
        UpdateObjectSummary();
    }

    private Control BuildParameterPage()
    {
        _parameterContent.SizeChanged += (_, _) => FitPanelContent(_parameterContent);
        _parameterContent.Load += (_, _) => FitPanelContent(_parameterContent);
        return new TableLayout
        {
            Padding = new Padding(3, 2),
            Rows =
            {
                new TableRow(new TableCell(_parameterContent, true)) { ScaleHeight = true },
            },
        };
    }

    private static void FitPanelContent(Panel panel)
    {
        if (panel.Content != null && panel.ClientSize.Width > 0 && panel.ClientSize.Height > 0 && panel.Content.Size != panel.ClientSize)
        {
            panel.Content.Size = panel.ClientSize;
        }
    }

    private Control BuildCurrentObjectSummary()
    {
        var header = new TableLayout
        {
            Padding = new Padding(4, 2),
            Rows =
            {
                new TableRow(new TableCell(null, true), _objectIcon, _objectTitle, new TableCell(null, true)),
            },
        };
        var description = new GroupBox
        {
            Padding = new Padding(4, 2),
            Content = _objectDescription,
        };
        return new TableLayout
        {
            Spacing = new Size(2, 2),
            Rows =
            {
                new TableRow(header),
                new TableRow(_objectInstanceDescription),
                new TableRow(description),
            },
        };
    }

    private static Control BuildObjectSummary(IGH_DocumentObject obj)
    {
        var icon = new ImageView
        {
            Image = ToEtoImage(obj.Icon_24x24),
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

    private Control BuildDocumentObjectInfo(IGH_DocumentObject obj)
    {
        Control details = obj switch
        {
            IGH_Component component => BuildComponentInfo(component),
            IGH_Param parameter => BuildParameterInfo(parameter),
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

    private void UpdateObjectSummary()
    {
        if (_currentObject == null)
        {
            _objectIcon.Image = null;
            _objectTitle.Text = string.Empty;
            _objectInstanceDescription.Text = string.Empty;
            _objectDescription.Text = string.Empty;
            _runtimeMessages.Text = string.Empty;
            return;
        }

        _objectIcon.Image = ToEtoImage(_currentObject.Icon_24x24);
        _objectTitle.Text = $"{_currentObject.Name} ({_currentObject.NickName})";
        _objectInstanceDescription.Text = _currentObject is IGH_Param
            ? string.Empty
            : _currentObject.InstanceDescription ?? string.Empty;
        _objectDescription.Text = _currentObject.Description ?? string.Empty;
        _runtimeMessages.Text = BuildRuntimeMessages(_currentObject);
    }

    private static string BuildRuntimeMessages(GH_DocumentObject obj)
    {
        if (obj is not GH_ActiveObject activeObject)
        {
            return string.Empty;
        }

        var messages = new List<string>();
        foreach (var level in new[]
        {
            GH_RuntimeMessageLevel.Error,
            GH_RuntimeMessageLevel.Warning,
            GH_RuntimeMessageLevel.Remark,
        })
        {
            var values = activeObject.RuntimeMessages(level);
            if (values?.Count > 0)
            {
                messages.Add($"{level}: {string.Join(" | ", values)}");
            }
        }
        messages.Add($"Time: {activeObject.ProcessorTime}");
        return string.Join(Environment.NewLine, messages);
    }
}
