using Eto.Drawing;
using Eto.Forms;
using Grasshopper;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SuperHelper;

internal sealed class DevelopmentPanel : Panel
{
    private readonly Label _typeInformation = new()
    {
        TextAlignment = TextAlignment.Center,
        Wrap = WrapMode.Word,
    };
    private readonly Button _assemblyLocation = new()
    {
        MinimumSize = new Size(50, 24),
        ToolTip = "Open the assembly folder.",
    };

    public DevelopmentPanel()
    {
        _assemblyLocation.Click += (_, _) => OpenAssemblyFolder();
        Content = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = true,
            ExpandContentHeight = false,
            Content = new TableLayout
            {
                Padding = new Padding(8, 6),
                Spacing = new Size(4, 6),
                Rows =
                {
                    new TableRow(new TableCell(_typeInformation, true)),
                    new TableRow(new TableCell(new TableLayout
                    {
                        Rows =
                        {
                            new TableRow(
                                new TableCell(null, true),
                                _assemblyLocation,
                                new TableCell(null, true)),
                        },
                    }, true)),
                    new TableRow(new TableCell(null, true)) { ScaleHeight = true },
                },
            },
        };
    }

    public void SetObject(GH_DocumentObject obj)
    {
        _typeInformation.Text = obj == null ? string.Empty : BuildTypeInformation(obj);
        _assemblyLocation.Text = obj == null ? string.Empty : FindAssemblyLocation(obj);
    }

    private static string BuildTypeInformation(GH_DocumentObject obj)
    {
        var type = obj.GetType();
        var bases = new List<string>();
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            bases.Add(current.FullName ?? current.Name);
        }
        bases.Reverse();

        return $"{type.FullName}{Environment.NewLine}{Environment.NewLine}" +
               $"Guid: {obj.ComponentGuid}{Environment.NewLine}{Environment.NewLine}" +
               $"Interfaces:{Environment.NewLine}{string.Join(Environment.NewLine, type.GetInterfaces().Select(item => item.FullName))}" +
               $"{Environment.NewLine}{Environment.NewLine}Inheritance:{Environment.NewLine}{string.Join(Environment.NewLine, bases)}";
    }

    private static string FindAssemblyLocation(GH_DocumentObject obj)
    {
        var assembly = obj.GetType().Assembly;
        var library = Instances.ComponentServer.Libraries.FirstOrDefault(item => item.Assembly == assembly);
        return library?.Location ?? assembly.Location ?? string.Empty;
    }

    private void OpenAssemblyFolder()
    {
        var path = _assemblyLocation.Text;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "SuperHelper", MessageBoxType.Error);
        }
    }
}
