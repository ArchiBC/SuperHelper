using SimpleGrasshopper.Attributes;
using System;

namespace SuperHelper;

public static partial class SuperSetting
{
    [Setting,Config("SuperHelper R8"), ToolButton("SuperHelperIcon_24.png")]
    private static readonly bool open = true;


    //删除和CapsuleRenderer插件的重复功能
    //[Config("CapsuleHighlight")]
    //public static bool CapsuleHighlight
    //{         
    //    get => Grasshopper.CentralSettings.CapsuleHighlight;
    //    set 
    //    {
    //        Grasshopper.CentralSettings.CapsuleHighlight = value;
    //        OnCapsuleHighlightChanged?.Invoke(value);
    //    }
    //}

    //public static event Action<bool> OnCapsuleHighlightChanged;

    //[Config("CapsuleShine")]
    //public static bool CapsuleShine
    //{
    //    get => Grasshopper.CentralSettings.CapsuleShine;
    //    set
    //    {
    //        Grasshopper.CentralSettings.CapsuleShine = value;
    //        OnCapsuleShineChanged?.Invoke(value);
    //    }
    //}

    //public static event Action<bool> OnCapsuleShineChanged;


    static SuperSetting()
    {
        OnOpenChanged += (value) =>
        {
            if (value) 
                SimpleAssemblyPriority.Show();
            else
                SimpleAssemblyPriority.Hide();
        };
    }
}
