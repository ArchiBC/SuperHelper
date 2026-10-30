using Eto.Drawing;
using Grasshopper2.Data;
using Grasshopper2.Display;
using Grasshopper2.Parameters;
using Grasshopper2.Types.Assistant;
using Rhino;
using Rhino.Display;

namespace SuperHelper;

internal sealed record Gh2HighlightTarget(IParameter Parameter, IPear Pear, ITypeAssistant Assistant);

internal sealed class Gh2HighlightConduit : DisplayConduit
{
    private static readonly Gh2HighlightConduit Instance = new();
    private DisplayCache _cache;
    private Guises _guises;
    public static void Set(Gh2HighlightTarget target)
    {
        Instance.Enabled = false;
        Instance._cache?.Dispose();
        Instance._cache = null;
        if (target?.Pear?.Item != null && target.Assistant.ZupportsDraw)
        {
            var bag = new DisplayBag(target.Parameter, target.Parameter.State.CurrentId, DisplayRules.Empty);
            target.Assistant.Draw(target.Pear, bag);
            Instance._cache = bag.ToCache();
            Instance._guises = target.Parameter.Document?.Display.Guises;
            Instance.Enabled = Instance._cache != null && Instance._guises != null;
        }
        RhinoDoc.ActiveDoc?.Views.Redraw();
    }
    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
        if (_cache != null) e.IncludeBoundingBox(_cache.Clipping);
    }
    protected override void PostDrawObjects(DrawEventArgs e)
    {
        if (_cache == null || _guises == null) return;
        var wire = SuperHelperPreferences.WireColor;
        var material = SuperHelperPreferences.MaterialColor;
        var standard = _guises.Standard.WithColour(Color.FromArgb(wire.R, wire.G, wire.B, wire.A));
        var curve = standard.Curves.WithStroke(SuperHelperPreferences.DisplayWireWidth);
        standard = standard.WithCurves(curve).WithIsocurves(curve).WithNakedEdges(curve).WithInnerEdges(curve)
            .WithNakedTrims(curve).WithInnerTrims(curve).WithNonManifold(curve);
        var faces = _guises.Standard.WithColour(Color.FromArgb(material.R, material.G, material.B, material.A));
        _cache.DrawFaces(e.Display, new Guises(faces, faces));
        _cache.DrawWires(e.Display, new Guises(standard, standard));
    }
}
