using SimpleGrasshopper.Attributes;
using System;

namespace SuperHelper;

public static partial class SuperSetting
{
    [Config("Open"), ToolButton("C:\\Users\\dell\\OneDrive\\vsProject\\SuperHelper\\SuperHelper\\Resources\\SuperHelperIcon_24.png")]
    [Setting]
    private static readonly bool open = true;

    [Config("CapsuleHighlight")]
    public static bool CapsuleHighlight
    {         
        get => Grasshopper.CentralSettings.CapsuleHighlight;
        set 
        {
            Grasshopper.CentralSettings.CapsuleHighlight = value;
            OnCapsuleHighlightChanged?.Invoke(value);
        }
    }

    public static event Action<bool> OnCapsuleHighlightChanged;

    [Config("CapsuleShine")]
    public static bool CapsuleShine
    {
        get => Grasshopper.CentralSettings.CapsuleShine;
        set
        {
            Grasshopper.CentralSettings.CapsuleShine = value;
            OnCapsuleShineChanged?.Invoke(value);
        }
    }

    public static event Action<bool> OnCapsuleShineChanged;


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
