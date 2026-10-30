using Rhino;

namespace SuperHelper;

internal static class SuperHelperPreferences
{
    internal static event System.Action<bool> ReverseCanvasWheelZoomChanged;
    private static readonly System.Drawing.Color WireColorDefault = System.Drawing.Color.DarkBlue;
    private static readonly System.Drawing.Color MaterialColorDefault = System.Drawing.Color.DarkBlue;

    public static bool AutoTarget
    {
        get => HelperHost.Current.Settings.GetValue(nameof(AutoTarget), true);
        set => HelperHost.Current.Settings.SetValue(nameof(AutoTarget), value);
    }

    public static bool ReverseCanvasWheelZoom
    {
        get => HelperHost.Current.Settings.GetValue("SuperHelper.ReverseCanvasWheelZoom", false);
        set
        {
            if (ReverseCanvasWheelZoom == value)
            {
                return;
            }
            HelperHost.Current.Settings.SetValue("SuperHelper.ReverseCanvasWheelZoom", value);
            ReverseCanvasWheelZoomChanged?.Invoke(value);
        }
    }

    public static string FileManagerDirectory
    {
        get => HelperHost.Current.Settings.GetValue("FileManageWorkDirectorey", "Please enter folder.");
        set => HelperHost.Current.Settings.SetValue("FileManageWorkDirectorey", value ?? string.Empty);
    }

    public static bool OpenDocumentWhenRightClick
    {
        get => HelperHost.Current.Settings.GetValue(nameof(OpenDocumentWhenRightClick), false);
        set => HelperHost.Current.Settings.SetValue(nameof(OpenDocumentWhenRightClick), value);
    }

    public static int DisplayWireWidth
    {
        get => HelperHost.Current.Settings.GetValue(nameof(DisplayWireWidth), 1);
        set
        {
            HelperHost.Current.Settings.SetValue(nameof(DisplayWireWidth), value);
            RedrawRhinoViews();
        }
    }

    public static System.Drawing.Color WireColor
    {
        get => HelperHost.Current.Settings.GetValue(nameof(WireColor), WireColorDefault);
        set
        {
            HelperHost.Current.Settings.SetValue(nameof(WireColor), value);
            RedrawRhinoViews();
        }
    }

    public static System.Drawing.Color MaterialColor
    {
        get => HelperHost.Current.Settings.GetValue(nameof(MaterialColor), MaterialColorDefault);
        set
        {
            HelperHost.Current.Settings.SetValue(nameof(MaterialColor), value);
            RedrawRhinoViews();
        }
    }

    public static System.Drawing.Color DefaultWireColor => WireColorDefault;
    public static System.Drawing.Color DefaultMaterialColor => MaterialColorDefault;

    private static void RedrawRhinoViews() => RhinoDoc.ActiveDoc?.Views.Redraw();
}
