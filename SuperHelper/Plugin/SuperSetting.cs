using System;

namespace SuperHelper;

public static class SuperSetting
{
    private const string OpenSettingKey = "SuperHelper.SuperSetting.Open";

    internal static event Action<bool> OpenChanged;

    public static bool Open
    {
        get => HelperHost.Current.Settings.GetValue(OpenSettingKey, true);
        set
        {
            if (Open == value)
            {
                return;
            }

            HelperHost.Current.Settings.SetValue(OpenSettingKey, value);
            OpenChanged?.Invoke(value);
        }
    }
}
