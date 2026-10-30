# Run inside Rhino after opening Grasshopper and SuperHelper.
# This checks wheel arithmetic and binding lifetime. Real trackpad panning,
# momentum and pinch gestures still require native input verification.
import clr
import hashlib
import json
import os
import traceback
import System
import Rhino
clr.AddReference('Grasshopper')
clr.AddReference('System.Windows.Forms')
import Grasshopper
from System.Drawing import Point
from System.Reflection import BindingFlags
from System.Windows.Forms import MouseEventArgs, MouseButtons

flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
path = os.path.join(root, 'artifacts', 'canvas-wheel-zoom.json')
report = {
    'rhino': str(Rhino.RhinoApp.Version),
    'tested_at': str(System.DateTime.Now.ToString('o')),
    'input': 'scripted MouseWheel; native trackpad gestures tested separately',
    'checks': [],
    'passed': False,
}
state = {}

def check(name, passed, detail=''):
    report['checks'].append({'name': name, 'passed': bool(passed), 'detail': str(detail)})

def wheel(reverse, delta=120, zoom=2.0):
    state['preference'].SetValue(None, reverse, None)
    canvas = state['canvas']
    canvas.Viewport.Zoom = zoom
    canvas.Viewport.Target = Point(50, 60)
    mouse = MouseEventArgs(MouseButtons.None, 0, 300, 200, delta)
    state['on_wheel'].Invoke(canvas, System.Array[System.Object]([mouse]))
    result_zoom = float(canvas.Viewport.Zoom)
    target = canvas.Viewport.Target
    # Rounding the integral target may move the cursor anchor by <= 0.5 px.
    anchor_error = max(abs(300 + (50 - 300) * result_zoom / zoom - target.X),
                       abs(200 + (60 - 200) * result_zoom / zoom - target.Y))
    return {'zoom': result_zoom, 'target': [int(target.X), int(target.Y)], 'anchor_error': anchor_error}

try:
    assembly = next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == 'SuperHelper')
    report['assembly'] = str(assembly.Location)
    with open(str(assembly.Location), 'rb') as stream:
        report['assembly_sha256'] = hashlib.sha256(stream.read()).hexdigest()
    state['preference'] = assembly.GetType('SuperHelper.SuperHelperPreferences').GetProperty('ReverseCanvasWheelZoom', flags)
    state['gestures'] = clr.GetClrType(Grasshopper.CentralSettings).GetProperty('CanvasTrackpadGestures', flags)
    state['canvas'] = Grasshopper.Instances.ActiveCanvas
    state['on_wheel'] = state['canvas'].GetType().GetMethod('OnMouseWheel', flags)
    state['zoom'] = state['canvas'].Viewport.Zoom
    state['target'] = state['canvas'].Viewport.Target
    state['original_preference'] = state['preference'].GetValue(None, None)
    state['original_gestures'] = state['gestures'].GetValue(None, None)
    state['rhino_scale'] = Rhino.ApplicationSettings.ViewSettings.ZoomScale

    # GH's private Mac handler reads NSEvent.Phase when gestures are enabled.
    # A script has a KitDefined/key event, not a scroll event; that access
    # aborts Rhino in native code and cannot be caught by Python or .NET.
    state['gestures'].SetValue(None, False, None)
    normal = wheel(False)
    reversed_zoom = wheel(True)
    restored = wheel(False)
    report['zooms'] = [normal['zoom'], reversed_zoom['zoom'], restored['zoom']]
    check('Independent setting reverses GH canvas wheel zoom',
          (normal['zoom'] - 2.0) * (reversed_zoom['zoom'] - 2.0) < 0)
    check('Disabling setting restores native canvas behavior', normal == restored)

    for delta in [-240, -120, -24, 0, 24, 120, 240]:
        expected = wheel(False, -delta)
        actual = wheel(True, delta)
        check('Reverse delta {0} matches native opposite wheel'.format(delta),
              abs(actual['zoom'] - expected['zoom']) < 0.000001 and actual['target'] == expected['target'],
              json.dumps({'expected': expected, 'actual': actual}))
        check('Cursor anchor stays fixed for delta ' + str(delta), actual['anchor_error'] <= 0.51,
              actual['anchor_error'])

    for zoom in [0.05, 50.0]:
        for delta in [-120, 120]:
            value = wheel(True, delta, zoom)['zoom']
            check('Native zoom limits at {0}, delta {1}'.format(zoom, delta), 0.05 - 1e-6 <= value <= 50.0)

    samples = []
    for unused in range(10):
        samples.append(wheel(True)['zoom'])
        samples.append(wheel(False)['zoom'])
    check('Repeated toggles do not duplicate wheel handlers',
          all(abs(value - (reversed_zoom['zoom'] if i % 2 == 0 else normal['zoom'])) < 1e-6
              for i, value in enumerate(samples)), samples)

    # Never call the native/default handler with synthetic input while gestures
    # are enabled. The reversed binding must handle this case safely itself.
    state['gestures'].SetValue(None, True, None)
    synthetic = wheel(True)
    check('Reverse zoom safely handles a non-scroll CurrentEvent',
          abs(synthetic['zoom'] - reversed_zoom['zoom']) < 1e-6)
    check('Rhino zoom setting is unchanged',
          state['rhino_scale'] == Rhino.ApplicationSettings.ViewSettings.ZoomScale)
except:
    check('Canvas wheel regression', False, traceback.format_exc())
finally:
    try:
        if 'original_gestures' in state:
            state['gestures'].SetValue(None, state['original_gestures'], None)
            state['preference'].SetValue(None, state['original_preference'], None)
            state['canvas'].Viewport.Zoom = state['zoom']
            state['canvas'].Viewport.Target = state['target']
            state['canvas'].OnViewportChanged()
            state['canvas'].Refresh()
            check('Trackpad and reverse preferences are restored',
                  state['gestures'].GetValue(None, None) == state['original_gestures']
                  and state['preference'].GetValue(None, None) == state['original_preference'])
    except:
        check('Restore original canvas state', False, traceback.format_exc())
    report['passed'] = bool(report['checks']) and all(item['passed'] for item in report['checks'])
    if not os.path.isdir(os.path.dirname(path)):
        os.makedirs(os.path.dirname(path))
    with open(path, 'w') as stream:
        json.dump(report, stream, indent=2)
    Rhino.RhinoApp.WriteLine('SuperHelper canvas wheel: {0}; {1}'.format(report['passed'], path))
