using SimpleGrasshopper.Attributes;

namespace SuperHelper;

public static partial class SuperSetting
{
    [Config("SuperHelper","SuperHelper"), ToolButton("C:\\Users\\dell\\OneDrive\\vsProject\\SuperHelper\\SuperHelper\\Resources\\SuperHelperIcon_24.png")]
    [Setting]
    private static readonly bool open = true;

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
