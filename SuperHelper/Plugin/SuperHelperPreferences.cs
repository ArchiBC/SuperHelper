using Grasshopper;
using Rhino;

namespace SuperHelper;

internal static class SuperHelperPreferences
{
    internal static event System.Action<bool> ReverseCanvasWheelZoomChanged;
    private static readonly System.Drawing.Color WireColorDefault = System.Drawing.Color.DarkBlue;
    private static readonly System.Drawing.Color MaterialColorDefault = System.Drawing.Color.DarkBlue;

    public static bool AutoTarget
    {
        get => Instances.Settings.GetValue(nameof(AutoTarget), true);
        set => Instances.Settings.SetValue(nameof(AutoTarget), value);
    }

    public static bool ReverseCanvasWheelZoom
    {
        get => Instances.Settings.GetValue("SuperHelper.ReverseCanvasWheelZoom", false);
        set
        {
            if (ReverseCanvasWheelZoom == value)
            {
                return;
            }
            Instances.Settings.SetValue("SuperHelper.ReverseCanvasWheelZoom", value);
            ReverseCanvasWheelZoomChanged?.Invoke(value);
        }
    }

    public static string FileManagerDirectory
    {
        get => Instances.Settings.GetValue("FileManageWorkDirectorey", "Please enter folder.");
        set => Instances.Settings.SetValue("FileManageWorkDirectorey", value ?? string.Empty);
    }

    public static bool OpenDocumentWhenRightClick
    {
        get => Instances.Settings.GetValue(nameof(OpenDocumentWhenRightClick), false);
        set => Instances.Settings.SetValue(nameof(OpenDocumentWhenRightClick), value);
    }

    public static int DisplayWireWidth
    {
        get => Instances.Settings.GetValue(nameof(DisplayWireWidth), 1);
        set
        {
            Instances.Settings.SetValue(nameof(DisplayWireWidth), value);
            RedrawRhinoViews();
        }
    }

    public static System.Drawing.Color WireColor
    {
        get => Instances.Settings.GetValue(nameof(WireColor), WireColorDefault);
        set
        {
            Instances.Settings.SetValue(nameof(WireColor), value);
            RedrawRhinoViews();
        }
    }

    public static System.Drawing.Color MaterialColor
    {
        get => Instances.Settings.GetValue(nameof(MaterialColor), MaterialColorDefault);
        set
        {
            Instances.Settings.SetValue(nameof(MaterialColor), value);
            RedrawRhinoViews();
        }
    }

    public static System.Drawing.Color DefaultWireColor => WireColorDefault;
    public static System.Drawing.Color DefaultMaterialColor => MaterialColorDefault;

    private static void RedrawRhinoViews() => RhinoDoc.ActiveDoc?.Views.Redraw();
}
