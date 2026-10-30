# Run inside Rhino with GH2 and SuperHelper loaded. Scroll over the embedded
# viewport while the point prompt is active, then click a point in that viewport.
# The native GH2 Point parameter getter is used; no Rhino objects are created.
import clr
import hashlib
import json
import os
import traceback
import System
import Rhino
clr.AddReference('Grasshopper2')
import Grasshopper2
from Eto.Forms import UITimer
from System.Reflection import BindingFlags

flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'artifacts', 'gh2-point-prompt.json')
report = {'pending': True, 'passed': False, 'checks': [], 'rhino': str(Rhino.RhinoApp.Version)}
state = {'original': None, 'document': None, 'recorder': None, 'finished': False, 'due': None}
watch = UITimer()
watch.Interval = 0.1

def field(obj, name):
    return obj.GetType().GetField(name, flags).GetValue(obj)

def prop(obj, name):
    return obj.GetType().GetProperty(name, flags).GetValue(obj, None)

def call(obj, name, *args):
    return obj.GetType().GetMethod(name, flags).Invoke(obj, System.Array[System.Object](args))

def save():
    with open(path, 'w') as stream: json.dump(report, stream, indent=2)

def check(name, passed, detail=''):
    report['checks'].append({'name': name, 'passed': bool(passed), 'detail': str(detail)})

def host():
    return field(state['panel'], '_viewport')

def camera():
    viewport = prop(host(), 'Viewport')
    return [float(v) for p in [viewport.CameraLocation, viewport.CameraTarget, viewport.CameraDirection, viewport.CameraUp]
            for v in [p.X, p.Y, p.Z]] + [float(viewport.Camera35mmLensLength)]

def same_camera(a, b):
    return all(abs(x-y) < 1e-6 for x, y in zip(a, b))

def finish():
    watch.Stop()
    watch.Elapsed -= tick
    watch.Dispose()
    if state['recorder'] is not None: state['recorder'].Enabled = False
    try:
        if state['original'] is not None:
            viewport = prop(host(), 'Viewport')
            viewport.SetViewProjection(state['original'], False)
            viewport.SetCameraTarget(state['original'].TargetPoint, False)
            viewport.SetConstructionPlane(state['construction_plane'])
            prop(host(), 'View').Redraw()
            state['original'].Dispose()
        if state['document'] is not None:
            state['editor'].Canvas.Document = state['previous_document']
            state['document'].Close()
    finally:
        report['pending'] = False
        report['passed'] = bool(report['checks']) and all(c['passed'] for c in report['checks']) and 'error' not in report
        save()
        Rhino.RhinoApp.WriteLine('SuperHelper GH2 point prompt: {0}; {1}'.format('PASS' if report['passed'] else 'FAIL', path))

def tick(sender, args):
    try:
        if not state['finished']:
            if Rhino.Input.RhinoGet.InGetPoint(Rhino.RhinoDoc.ActiveDoc):
                first = not report.get('getter_seen', False)
                report['getter_seen'] = True
                report['editor_visible_during_getter'] = report.get('editor_visible_during_getter', True) and state['editor'].Visible
                if first: save()
                if (System.DateTime.UtcNow - state['started']).TotalSeconds > 60:
                    report['cancelled_after_timeout'] = True
                    call(state['native_command'], 'DoCancel')
            return
        if System.DateTime.UtcNow < state['due']: return
        report['camera_after_idle'] = camera()
        check('Native GH2 point getter ran', report.get('getter_seen', False))
        check('GH2 stays visible during embedded point picking', report.get('editor_visible_during_getter', False)
              and report.get('visible_at_click', False) and state['editor'].Visible)
        check('Mouse picked in the embedded Rhino viewport', report.get('embedded_view_clicked', False))
        check('Embedded wheel changes camera during the getter', not same_camera(report['camera_before'], report['camera_after_getter']))
        check('Getter completion retains the camera after idle', same_camera(report['camera_after_getter'], report['camera_after_idle']))
        check('GH2 getter timer is released', clr.GetClrType(Grasshopper2.UI.Editor).GetField('_getterTimer', flags).GetValue(None) is None)
        finish()
    except:
        report['error'] = traceback.format_exc()
        finish()

class ClickRecorder(Rhino.UI.MouseCallback):
    def OnMouseDown(self, args):
        if args.View.RuntimeSerialNumber == prop(host(), 'View').RuntimeSerialNumber:
            report['embedded_view_clicked'] = True
            report['visible_at_click'] = state['editor'].Visible

def run(sender, args):
    if Rhino.Commands.Command.InCommand(): return
    Rhino.RhinoApp.Idle -= run
    try:
        call(state['panel'], 'Activate', False)
        viewport = prop(host(), 'Viewport')
        state['original'] = Rhino.DocObjects.ViewportInfo(viewport)
        state['construction_plane'] = viewport.ConstructionPlane()
        # Use a visible construction plane even if the user's current camera
        # points away from it. Both camera and construction plane are restored.
        viewport.SetConstructionPlane(Rhino.Geometry.Plane.WorldXY)
        viewport.SetCameraLocations(Rhino.Geometry.Point3d.Origin, Rhino.Geometry.Point3d(60, -80, 70))
        prop(host(), 'View').Redraw()
        report['camera_before'] = camera()
        state['recorder'].Enabled = True
        state['started'] = System.DateTime.UtcNow
        watch.Start()
        save()
        point = Grasshopper2.Parameters.Standard.Point3Parameter()
        state['document'].Objects.Add(point)
        # This is the same native picker used by Point's Set One command.
        result = call(point, 'PickOneItem')
        check('GH2 point picker returns a valid point', result is not None and result.Item.IsValid)
        if result is not None:
            report['point'] = str(result.Item)
            load = point.GetType().GetMethod('LoadNewPersistentData', flags, None,
                System.Array[System.Type]([clr.GetClrType(Rhino.Geometry.Point3d).MakeArrayType()]), None)
            load.Invoke(point, System.Array[System.Object]([System.Array[Rhino.Geometry.Point3d]([result.Item])]))
            check('Picked point reaches persistent data', point.PersistentData.ItemCount == 1)
        report['camera_after_getter'] = camera()
        state['finished'] = True
        state['due'] = System.DateTime.UtcNow.AddSeconds(2)
        save()
    except:
        report['error'] = traceback.format_exc()
        finish()

try:
    assembly = next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == 'SuperHelper.GH2')
    report['tested_at'] = str(System.DateTime.Now.ToString('o'))
    with open(assembly.Location, 'rb') as stream: report['pluginSha256'] = hashlib.sha256(stream.read()).hexdigest()
    workspace = assembly.GetType('SuperHelper.SuperHelperGh2Plugin').GetField('_workspace', flags).GetValue(None)
    control = prop(workspace, 'Control')
    state['panel'] = field(control, '_viewportPanel')
    state['native_command'] = field(field(field(control, '_commandPanel'), '_commandMirror'), '_commandDockBar')
    state['editor'] = Grasshopper2.UI.Editor.Instance
    state['previous_document'] = state['editor'].Canvas.Document
    state['document'] = Grasshopper2.Doc.Document.NewActiveDocument()
    state['editor'].Canvas.Document = state['document']
    state['recorder'] = ClickRecorder()
    watch.Elapsed += tick
    Rhino.RhinoApp.Idle += run
    save()
except:
    report['error'] = traceback.format_exc()
    finish()
