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

public sealed partial class SuperHelperControl
{
    private Control BuildDevelopmentPage()
    {
        _assemblyLocationButton.Click += (_, _) => OpenAssemblyFolder();
        _assemblyLocationButton.MinimumSize = new Size(50, 24);
        _assemblyLocationButton.ToolTip = "Open the assembly folder.";
        var footer = new TableLayout
        {
            Rows =
            {
                new TableRow(new TableCell(null, true), _assemblyLocationButton, new TableCell(null, true)),
            },
        };
        var content = new TableLayout
        {
            Padding = new Padding(8, 6),
            Spacing = new Size(4, 6),
            Rows =
            {
                new TableRow(new TableCell(_developmentText, true)),
                new TableRow(new TableCell(footer, true)),
                new TableRow(new TableCell(null, true)) { ScaleHeight = true },
            },
        };
        return new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = true,
            ExpandContentHeight = false,
            Content = content,
        };
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
        var path = _assemblyLocationButton.Text;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "SuperHelper", MessageBoxType.Error);
        }
    }
}
