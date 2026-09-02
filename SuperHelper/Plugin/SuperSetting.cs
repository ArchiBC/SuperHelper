using Grasshopper;
using System;

namespace SuperHelper;

public static class SuperSetting
{
    private const string OpenSettingKey = "SuperHelper.SuperSetting.Open";

    internal static event Action<bool> OpenChanged;

    public static bool Open
    {
        get => Instances.Settings.GetValue(OpenSettingKey, true);
        set
        {
            if (Open == value)
            {
                return;
            }

            Instances.Settings.SetValue(OpenSettingKey, value);
            OpenChanged?.Invoke(value);
        }
    }
}
