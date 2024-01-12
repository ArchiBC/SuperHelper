using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

namespace SuperHelper
{
    public class HighLightConduit : DisplayConduit
    {
        public static IGH_PreviewData[] HighLightObject { get; set; }

        protected override void DrawOverlay(DrawEventArgs e)
        {
            if(HighLightObject != null && HighLightObject.Length > 0)
            {
                int thickness = MenuReplacer._control.DisplayWireWidth;

                foreach (var item in HighLightObject)
                {
                    item.DrawViewportWires(new GH_PreviewWireArgs(e.Viewport, e.Display, MenuReplacer._control.WireColor, thickness));
                    item.DrawViewportMeshes(new GH_PreviewMeshArgs(e.Viewport, e.Display,
                        new DisplayMaterial(MenuReplacer._control.MaterialColor), MeshingParameters.Default));
                }

            }
            base.DrawOverlay(e);
        }
    }
}
