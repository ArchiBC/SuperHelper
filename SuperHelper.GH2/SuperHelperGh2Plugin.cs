using Grasshopper2.Framework;
using System.Runtime.InteropServices;

[assembly: Guid("01A71230-29B2-4B47-A8F0-B141A36A7F2A")]

namespace SuperHelper;

public sealed class SuperHelperGh2Plugin : Plugin
{
    private static Gh2Workspace _workspace;
    public override string Name => "SuperHelperGH2";
    public override string Author => "ArchiBC";
    public override string Contact => "Archi-bc@outlook.com";
    public override void OnLoaded()
    {
        base.OnLoaded();
        if (_workspace != null) return;
        _workspace = new Gh2Workspace();
        HelperHost.Current = new Grasshopper2Host(_workspace);
        _workspace.Start();
    }
}
