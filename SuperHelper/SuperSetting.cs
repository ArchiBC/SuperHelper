using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SimpleGrasshopper.Attributes;

namespace SuperHelper;

public static partial class SuperSetting
{
    [Config("SuperHelper","SuperHelper"), ToolButton("https://github.com/ArchiDog1998/SimpleGrasshopper/raw/main/assets/image-20231123225140458.png")]
    [Setting]
    private static readonly bool open = true;

    static SuperSetting()
    {
        OnOpenChanged += (value) =>
        {
            if (value) 
                SimpleAssemblyPriority.Show(); 
        };
    }
}
