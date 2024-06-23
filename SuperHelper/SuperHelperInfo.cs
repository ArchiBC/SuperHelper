using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.Kernel;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Cursors = System.Windows.Forms.Cursors;

namespace SuperHelper;

public class SuperHelperInfo : GH_AssemblyInfo
{
    public override string Name => "SuperHelper R8";

    //Return a 24x24 pixel bitmap to represent this GHA library.
    public override Bitmap Icon => Properties.Resources.SuperHelperIcon_24;

    //Return a short string describing the purpose of this GHA library.
    public override string Description => "Much better helper control for grasshopper! The original author is 秋水";

    public override Guid Id => new Guid("A71D5B0A-9D5B-4E27-8933-BB83CB68066D");

    //Return a string identifying you or your company.
    public override string AuthorName => "ArchiBC";

    //Return a string representing your preferred contact details.
    public override string AuthorContact => "Archi-bc@outlook.com";

    public override string Version => "2.0.2";
}


partial class SimpleAssemblyPriority
{
    protected override int? MenuIndex => 2;

    public static bool UseSuperHelperPanel
    {
        get => Instances.Settings.GetValue(nameof(UseSuperHelperPanel), true);
        set => Instances.Settings.SetValue(nameof(UseSuperHelperPanel), value);
    }

    public static int SuperHelperPanelWidth
    {
        get
        {
            var width = Instances.Settings.GetValue(nameof(SuperHelperPanelWidth), 400);
            if (width < 0)
            {
                width = 400;
                SuperHelperPanelWidth = width;
            }
            return width;
        }
        set => Instances.Settings.SetValue(nameof(SuperHelperPanelWidth), value);
    }

    public static bool IsSuperHelperOnRight
    {
        get => Instances.Settings.GetValue(nameof(SuperHelperPanelWidth), true);
        set
        {
            Instances.Settings.SetValue(nameof(SuperHelperPanelWidth), value);
            if (value)
            {
                _ctrlHost.Dock = DockStyle.Right;
                _splitter.Dock = DockStyle.Right;
            }
            else
            {
                _ctrlHost.Dock = DockStyle.Left;
                _splitter.Dock = DockStyle.Left;
            }

        }
    }

    private static ElementHost _ctrlHost = new ElementHost()
    {
        Dock = IsSuperHelperOnRight ? DockStyle.Right : DockStyle.Left,
        Child = MenuReplacer._control,
        Width = SuperHelperPanelWidth,
    };
    private static GH_Splitter _splitter = new GH_Splitter()
    {
        Cursor = Cursors.VSplit,
        Dock = IsSuperHelperOnRight ? DockStyle.Right : DockStyle.Left,
        Location = new Point(0, 439),
        Margin = new Padding(24),
        MaxSize = 8000,
        MinSize = 50,
        Name = "Helper Splitter",
        Size = new Size(10, 2744),
    };
    public static void SwitchSide()
    {
        if (_ctrlHost == null || _splitter == null) return;

        IsSuperHelperOnRight = !IsSuperHelperOnRight;
    }

    public static void Hide()
    {
        _ctrlHost.Hide();
        _splitter.Hide();
        UseSuperHelperPanel = false;
    }

    public static void Show()
    {
        _ctrlHost.Show();
        _splitter.Show();
        UseSuperHelperPanel = true;
    }
    protected override void DoWithEditor(GH_DocumentEditor editor)
    {
        base.DoWithEditor(editor);

        MenuReplacer.Init();

        editor.Controls[0].Controls.Add(_splitter);
        editor.Controls[0].Controls.Add(_ctrlHost);

        Instances.DocumentEditor.FormClosing += (sender, e) =>
        {
            SuperHelperPanelWidth = _ctrlHost.Width;
        };

        Rhino.RhinoDoc.EndOpenDocument += RhinoDoc_EndOpenDocument;
        Rhino.RhinoDoc.BeginSaveDocument += RhinoDoc_BeginSaveDocument;
        Rhino.RhinoDoc.EndSaveDocument += RhinoDoc_EndSaveDocument;

        _splitter.MouseDown += (sender, e) =>
        {
            _splitter.MaxSize = editor.Controls[0].Width - 80;
        };
    }
    private void RhinoDoc_EndSaveDocument(object sender, Rhino.DocumentSaveEventArgs e)
    {
        MenuReplacer._control.UpdateViewPortHost();
    }

    private void RhinoDoc_BeginSaveDocument(object sender, Rhino.DocumentSaveEventArgs e)
    {
        RhinoViewportHost.RemoveNameView();
    }

    private void RhinoDoc_EndOpenDocument(object sender, Rhino.DocumentOpenEventArgs e)
    {
        Task.Run(() =>
        {
            Task.Delay(500);

            MenuReplacer._control.Dispatcher.Invoke(() =>
            {
                MenuReplacer._control.UpdateViewPortHost();
            });

        });
    }
}
