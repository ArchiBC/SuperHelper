# Run inside Rhino after opening Grasshopper and SuperHelper.
# Exercise the canvas event, including toggling back to native behavior.
import clr
import hashlib
import json
import os
import System
import Rhino
clr.AddReference('Grasshopper')
clr.AddReference('System.Windows.Forms')
import Grasshopper
from System.Reflection import BindingFlags
from System.Windows.Forms import MouseEventArgs, MouseButtons

flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
assembly = next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == 'SuperHelper')
preference = assembly.GetType('SuperHelper.SuperHelperPreferences').GetProperty('ReverseCanvasWheelZoom', flags)
canvas = Grasshopper.Instances.ActiveCanvas
original_zoom = canvas.Viewport.Zoom
original_target = canvas.Viewport.Target
original_preference = preference.GetValue(None, None)
rhino_scale = Rhino.ApplicationSettings.ViewSettings.ZoomScale
report = {'rhino': str(Rhino.RhinoApp.Version), 'tested_at': str(System.DateTime.Now.ToString('o')), 'checks': []}
with open(str(assembly.Location), 'rb') as stream:
    report['assembly_sha256'] = hashlib.sha256(stream.read()).hexdigest()

def check(name, passed):
    report['checks'].append({'name': name, 'passed': bool(passed)})

def wheel(reverse):
    preference.SetValue(None, reverse, None)
    canvas.Viewport.Zoom = 1.0
    mouse = MouseEventArgs(MouseButtons.None, 0, 300, 200, 120)
    canvas.GetType().GetMethod('OnMouseWheel', flags).Invoke(canvas, System.Array[System.Object]([mouse]))
    return canvas.Viewport.Zoom

try:
    normal = wheel(False)
    reversed_zoom = wheel(True)
    restored = wheel(False)
    report['zooms'] = [normal, reversed_zoom, restored]
    check('Independent setting reverses GH canvas wheel zoom', (normal - 1.0) * (reversed_zoom - 1.0) < 0)
    check('Disabling setting restores native canvas behavior', abs(normal - restored) < 0.000001)
    check('Rhino zoom setting is unchanged', rhino_scale == Rhino.ApplicationSettings.ViewSettings.ZoomScale)
finally:
    preference.SetValue(None, original_preference, None)
    canvas.Viewport.Zoom = original_zoom
    canvas.Viewport.Target = original_target
    canvas.Refresh()
    report['passed'] = len(report['checks']) == 3 and all(item['passed'] for item in report['checks'])
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    with open(os.path.join(root, 'artifacts', 'canvas-wheel-zoom.json'), 'w') as stream:
        json.dump(report, stream, indent=2)
    Rhino.RhinoApp.WriteLine('SuperHelper canvas wheel: ' + str(report['passed']))
