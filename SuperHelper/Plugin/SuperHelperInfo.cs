using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace SuperHelper;

public sealed class SuperHelperInfo : GH_AssemblyInfo
{
    public override string Name => "SuperHelper R9";
    public override Bitmap Icon => Properties.Resources.SuperHelperIcon_24;
    public override string Description => "Much better helper control for grasshopper! The original author is 秋水";
    public override Guid Id => new("A71D5B0A-9D5B-4E27-8933-BB83CB68066D");
    public override string AuthorName => "ArchiBC";
    public override string AuthorContact => "Archi-bc@outlook.com";
    public override string Version => "2.1.2.0";
}
