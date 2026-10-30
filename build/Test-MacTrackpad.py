# Run once to record real trackpad input, then again to validate and restore.
# Default host is GH2. Set scriptcontext.sticky['SuperHelper.TrackpadHost']='GH1'
# before running to test GH1. No synthetic input is counted as a hardware test.
import clr
import hashlib
import json
import os
import traceback
import System
import Rhino
import scriptcontext as sc
clr.AddReference('Microsoft.macOS')
clr.AddReference('Grasshopper')
clr.AddReference('Grasshopper2')
import Grasshopper
import Grasshopper2
from AppKit import NSEvent, NSEventMask, LocalEventHandler
from Eto.Forms import Application, Control
from Eto.Drawing import PointF
from System.Reflection import BindingFlags

flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
host_name = sc.sticky.get('SuperHelper.TrackpadHost', 'GH2')
key = 'SuperHelper.Trackpad.' + host_name
path = os.path.join(root, 'artifacts', host_name.lower() + '-native-trackpad.json')

def projection(canvas):
    if host_name == 'GH2':
        p = canvas.Projection
        return [float(p.Zoom), float(p.Origin.X), float(p.Origin.Y)]
    return [float(canvas.Viewport.Zoom), float(canvas.Viewport.Target.X), float(canvas.Viewport.Target.Y)]

def save(state):
    with open(path, 'w') as stream: json.dump(state['report'], stream, indent=2)

def camera(state):
    workspace = state['assembly'].GetType('SuperHelper.SuperHelperGh2Plugin').GetField('_workspace', flags).GetValue(None) if host_name == 'GH2' else None
    control = workspace.GetType().GetProperty('Control', flags).GetValue(workspace, None) if workspace else state['assembly'].GetType('SuperHelper.MenuReplacer').GetProperty('Control', flags).GetValue(None, None)
    panel = control.GetType().GetField('_viewportPanel', flags).GetValue(control)
    native = panel.GetType().GetField('_viewport', flags).GetValue(panel)
    viewport = native.GetType().GetProperty('Viewport', flags).GetValue(native, None)
    return [float(v) for p in [viewport.CameraLocation, viewport.CameraTarget, viewport.CameraDirection] for v in [p.X, p.Y, p.Z]]

def restore(state):
    if 'monitor' in state:
        monitor = state.pop('monitor')
        NSEvent.RemoveMonitor(monitor)
        monitor.Dispose()
    if 'original' in state:
        if host_name == 'GH2': state['canvas'].Projection = state['original']
        else:
            state['canvas'].Viewport.Zoom = state['original'][0]
            state['canvas'].Viewport.Target = state['original'][1]
    if 'gh1_visible' in state and Grasshopper.Instances.DocumentEditor is not None:
        Grasshopper.Instances.DocumentEditor.Visible = state['gh1_visible']
    if 'gh2_visible' in state: Grasshopper2.UI.Editor.Instance.Visible = state['gh2_visible']
    if key in sc.sticky: del sc.sticky[key]
    if 'SuperHelper.TrackpadHost' in sc.sticky: del sc.sticky['SuperHelper.TrackpadHost']

state = sc.sticky.get(key, {'report': {'host': host_name, 'pending': True, 'passed': False, 'events': [], 'checks': [], 'rhino': str(Rhino.RhinoApp.Version)}})
try:
    if key not in sc.sticky:
        if host_name == 'GH1' and Grasshopper.Instances.ActiveCanvas is None:
            Rhino.RhinoApp.RunScript('_Grasshopper', False)
        state['assembly'] = next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == ('SuperHelper.GH2' if host_name == 'GH2' else 'SuperHelper'))
        state['canvas'] = Grasshopper2.UI.Editor.Instance.Canvas if host_name == 'GH2' else Grasshopper.Instances.ActiveCanvas
        state['original'] = state['canvas'].Projection if host_name == 'GH2' else (state['canvas'].Viewport.Zoom, state['canvas'].Viewport.Target)
        state['camera'] = camera(state)
        state['zoom_scale'] = float(Rhino.ApplicationSettings.ViewSettings.ZoomScale)
        state['gh1_visible'] = Grasshopper.Instances.DocumentEditor is not None and Grasshopper.Instances.DocumentEditor.Visible
        state['gh2_visible'] = Grasshopper2.UI.Editor.Instance.Visible
        state['report']['tested_at'] = str(System.DateTime.Now.ToString('o'))
        with open(state['assembly'].Location, 'rb') as stream: state['report']['pluginSha256'] = hashlib.sha256(stream.read()).hexdigest()
        if host_name == 'GH2':
            handler = clr.GetClrType(Control).GetProperty('Handler', flags | BindingFlags.DeclaredOnly).GetValue(state['canvas'], None)
            conversions = next(a.GetType('Eto.Mac.MacConversions', False) for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetType('Eto.Mac.MacConversions', False))
            convert = conversions.GetMethod('GetMouseEvent', flags)
            view = handler.GetType().GetProperty('ContainerControl', flags).GetValue(handler, None)
            if Grasshopper.Instances.DocumentEditor is not None: Grasshopper.Instances.DocumentEditor.Visible = False
            Grasshopper2.UI.Editor.Instance.Visible = True
            Grasshopper2.UI.Editor.Instance.BringToFront()
            from Grasshopper2.UI.Flex import Projection
            state['canvas'].Projection = Projection(PointF(50, 60), System.Single(2))
        else:
            import System.Windows.Forms
            view = clr.GetClrType(System.Windows.Forms.Control).GetProperty('NativeView', flags).GetValue(state['canvas'], None)
            Grasshopper2.UI.Editor.Instance.Visible = False
            Grasshopper.Instances.DocumentEditor.Show()
            Grasshopper.Instances.DocumentEditor.Activate()
            state['canvas'].Viewport.Zoom = 2
        state['window_number'] = str(view.Window.WindowNumber)
        state['report']['canvas_size'] = str(state['canvas'].Size)
        sc.sticky[key] = state

        def observe(event):
            try:
                kind = str(event.Type)
                if kind not in ['ScrollWheel', 'Magnify'] or str(event.WindowNumber) != state['window_number']: return event
                if host_name == 'GH2':
                    mouse = convert.Invoke(None, System.Array[System.Object]([handler, event, False]))
                    x, y = float(mouse.Location.X), float(mouse.Location.Y)
                else:
                    point = view.ConvertPointFromView(event.LocationInWindow, None)
                    x, y = float(point.X), float(point.Y)
                    if not view.IsFlipped: y = float(view.Frame.Height) - y
                if not (0 <= x <= state['canvas'].Width and 0 <= y <= state['canvas'].Height): return event
                if len(state['report']['events']) >= 1000: return event
                sample = {'type': kind, 'before': projection(state['canvas'])}
                if kind == 'ScrollWheel':
                    sample.update({'phase': int(event.Phase), 'momentum': int(event.MomentumPhase),
                        'dx': float(event.ScrollingDeltaX), 'dy': float(event.ScrollingDeltaY), 'inverted': bool(event.IsDirectionInvertedFromDevice)})
                else: sample['magnification'] = float(event.Magnification)
                def after():
                    try:
                        sample['after'] = projection(state['canvas'])
                        state['report']['events'].append(sample)
                        save(state)
                    except: pass
                Application.Instance.AsyncInvoke(System.Action(after))
            except:
                state['report']['observer_error'] = traceback.format_exc()
            return event

        state['callback'] = LocalEventHandler(observe)
        state['monitor'] = NSEvent.AddLocalMonitorForEventsMatchingMask(NSEventMask.ScrollWheel | NSEventMask.EventMagnify, state['callback'])
        state['report']['phase'] = 'Record real horizontal/vertical two-finger pan, momentum, pinch out and pinch in, then run again.'
    else:
        events = state['report']['events']
        pans = [e for e in events if e['type'] == 'ScrollWheel' and (e['phase'] or e['momentum']) and (abs(e['dx']) + abs(e['dy']) > 0.001)]
        momentum = [e for e in pans if e['momentum']]
        pinch = [e for e in events if e['type'] == 'Magnify' and abs(e['magnification']) > 0.001]
        def check(name, passed): state['report']['checks'].append({'name': name, 'passed': bool(passed)})
        check('Real horizontal trackpad pan is observed', any(abs(e['dx']) > 0.01 and e['before'][1:] != e['after'][1:] for e in pans))
        check('Real vertical trackpad pan is observed', any(abs(e['dy']) > 0.01 and e['before'][1:] != e['after'][1:] for e in pans))
        check('Trackpad pan retains canvas zoom', bool(pans) and all(abs(e['before'][0] - e['after'][0]) < 1e-6 for e in pans))
        check('Real trackpad momentum is preserved', any(e['before'][1:] != e['after'][1:] for e in momentum))
        check('Real pinch out zooms in', any(e['magnification'] > 0 and e['after'][0] > e['before'][0] for e in pinch))
        check('Real pinch in zooms out', any(e['magnification'] < 0 and e['after'][0] < e['before'][0] for e in pinch))
        check('Embedded Rhino camera is unchanged', all(abs(x-y) < 1e-6 for x, y in zip(state['camera'], camera(state))))
        check('Rhino zoom setting is unchanged', state['zoom_scale'] == float(Rhino.ApplicationSettings.ViewSettings.ZoomScale))
        restore(state)
        state['report']['pending'] = False
        state['report']['passed'] = all(c['passed'] for c in state['report']['checks']) and 'observer_error' not in state['report']
        state['report']['phase'] = 'Finished; original projection restored.'
except:
    state['report']['error'] = traceback.format_exc()
    state['report']['passed'] = False
    state['report']['pending'] = False
    restore(state)
finally:
    save(state)
    Rhino.RhinoApp.WriteLine('SuperHelper {0} trackpad: {1}; {2}'.format(host_name, state['report'].get('phase', 'setup failed'), path))
