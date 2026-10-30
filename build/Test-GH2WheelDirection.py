# Run inside Rhino. Compares actual GH1 DollyZoom with GH2's native wheel
# handler for both natural-scrolling flags and independent reverse settings.
import clr
import hashlib
import json
import os
import traceback
import System
import Rhino
clr.AddReference('Grasshopper')
clr.AddReference('Grasshopper2')
clr.AddReference('System.Windows.Forms')
import Grasshopper2
from Grasshopper.GUI.Canvas import GH_Viewport
from Grasshopper.GUI import GH_CanvasMouseEvent
from System.Drawing import Point
from System.Windows.Forms import MouseEventArgs as WinMouse, MouseButtons as WinButtons
from Eto.Forms import MouseEventArgs, MouseButtons, Keys
from Eto.Drawing import PointF, SizeF
from Grasshopper2.UI.Flex import Projection, FlexControl
from System.Reflection import BindingFlags

flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
path = os.path.join(root, 'artifacts', 'gh2-wheel-direction.json')
report = {'passed': False, 'checks': [], 'rhino': str(Rhino.RhinoApp.Version), 'tested_at': str(System.DateTime.Now.ToString('o'))}
canvas = Grasshopper2.UI.Editor.Instance.Canvas
original = canvas.Projection
scale = float(Rhino.ApplicationSettings.ViewSettings.ZoomScale)
try:
    assembly = next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == 'SuperHelper.GH2')
    with open(assembly.Location, 'rb') as stream: report['pluginSha256'] = hashlib.sha256(stream.read()).hexdigest()
    normalize = assembly.GetType('SuperHelper.Gh2MacCanvasInput').GetMethod('NormalizeWheel', flags)
    native = clr.GetClrType(FlexControl).GetMethod('OnMouseWheel', flags)
    for natural in [False, True]:
        for reverse in [False, True]:
            for delta in [-2.0, -0.25, 0.25, 2.0]:
                viewport = GH_Viewport(Point(50, 60), System.Single(2.0))
                win_delta = int(delta * 60) * (-1 if reverse else 1)
                viewport.DollyZoom(GH_CanvasMouseEvent(viewport, WinMouse(WinButtons.None, 0, 300, 200, win_delta)))
                expected = float(viewport.Zoom) - 2.0
                canvas.Projection = Projection(PointF(50, 60), System.Single(2.0))
                mouse = MouseEventArgs(MouseButtons.None, Keys.None, PointF(300, 200), SizeF(0, delta), System.Single(0), natural)
                normalized = normalize.Invoke(None, System.Array[System.Object]([mouse, reverse]))
                native.Invoke(canvas, System.Array[System.Object]([normalized]))
                actual = float(canvas.Projection.Zoom) - 2.0
                report['checks'].append({'name': 'GH1/GH2 direction: natural={0}, reverse={1}, delta={2}'.format(natural, reverse, delta),
                    'passed': expected * actual > 0, 'gh1_zoom': float(viewport.Zoom), 'gh2_zoom': float(canvas.Projection.Zoom)})
    report['checks'].append({'name': 'Rhino zoom setting is unchanged', 'passed': float(Rhino.ApplicationSettings.ViewSettings.ZoomScale) == scale})
except:
    report['error'] = traceback.format_exc()
finally:
    canvas.Projection = original
    report['passed'] = bool(report['checks']) and all(c['passed'] for c in report['checks']) and 'error' not in report
    with open(path, 'w') as stream: json.dump(report, stream, indent=2)
    Rhino.RhinoApp.WriteLine('SuperHelper GH2 wheel direction: {0}; {1}'.format('PASS' if report['passed'] else 'FAIL', path))
