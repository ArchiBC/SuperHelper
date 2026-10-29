# Run with Rhino's _-RunPythonScript in the SuperHelper test workspace.
# Exercises camera preservation through refresh, native view replacement and
# GH's real point getter. Coordinates are submitted through the native command
# editor; this does not replace the separate manual embedded-mouse check.
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
from Eto.Forms import Application
from Grasshopper.Kernel import GH_Document
from Grasshopper.Kernel.Parameters import Param_Point
from System.Reflection import BindingFlags
from System.Windows.Forms import ContextMenuStrip

flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
path = os.path.join(root, 'artifacts', 'viewport-camera.json')
report = {'pending': True, 'passed': False, 'checks': [], 'rhino': str(Rhino.RhinoApp.Version)}
phase = {'stage': 0, 'due': System.DateTime.UtcNow, 'moved': False}
state = {'original': None, 'previous_document': None, 'test_document': None, 'menu': None}

def field(obj, name):
    return obj.GetType().GetField(name, flags).GetValue(obj)

def prop(obj, name):
    return obj.GetType().GetProperty(name, flags).GetValue(obj, None)

def call(obj, name, *args):
    return obj.GetType().GetMethod(name, flags).Invoke(obj, System.Array[System.Object](args))

def save():
    with open(path, 'w') as stream:
        json.dump(report, stream, indent=2)

def check(name, passed, detail=''):
    report['checks'].append({'name': name, 'passed': bool(passed), 'detail': str(detail)})
    save()

def host():
    return field(state['panel'], '_viewport')

def camera():
    viewport = prop(host(), 'Viewport')
    def xyz(value):
        return [value.X, value.Y, value.Z]
    return {
        'location': xyz(viewport.CameraLocation),
        'target': xyz(viewport.CameraTarget),
        'direction': xyz(viewport.CameraDirection),
        'up': xyz(viewport.CameraUp),
        'lens': viewport.Camera35mmLensLength,
        'perspective': viewport.IsPerspectiveProjection,
    }

def same_camera(expected, actual):
    for name in ['location', 'target', 'direction', 'up']:
        if any(abs(a - b) > 1e-6 for a, b in zip(expected[name], actual[name])):
            return False
    return abs(expected['lens'] - actual['lens']) < 1e-6 \
        and expected['perspective'] == actual['perspective']

def move_camera(offset):
    viewport = prop(host(), 'Viewport')
    viewport.SetCameraLocations(Rhino.Geometry.Point3d(120 + offset, 80, 0),
                               Rhino.Geometry.Point3d(240 + offset, 180, 160))
    viewport.Camera35mmLensLength = 42 + offset / 10.0
    prop(host(), 'View').Redraw()
    return camera()

def finish():
    Rhino.RhinoApp.Idle -= run
    Rhino.RhinoApp.CommandPromptChanged -= on_prompt
    try:
        if state['original'] is not None:
            viewport = prop(host(), 'Viewport')
            viewport.SetViewProjection(state['original'], False)
            viewport.SetCameraTarget(state['original'].TargetPoint, False)
            prop(host(), 'View').Redraw()
    finally:
        if state['test_document'] is not None:
            Grasshopper.Instances.ActiveCanvas.Document = state['previous_document']
            state['test_document'].Dispose()
        if state['menu'] is not None:
            state['menu'].Dispose()
        if state['original'] is not None:
            state['original'].Dispose()
        report['pending'] = False
        report['passed'] = bool(report['checks']) and all(c['passed'] for c in report['checks'])
        save()
        Rhino.RhinoApp.WriteLine('SuperHelper viewport camera: {0}; {1}'.format(
            'PASS' if report['passed'] else 'FAIL', path))

def on_prompt(sender, args):
    if phase['moved']:
        return
    def move_and_finish():
        if phase['moved'] or not Rhino.Input.RhinoGet.InGetPoint(Rhino.RhinoDoc.ActiveDoc):
            return
        try:
            phase['moved'] = True
            report['camera_during_getter'] = move_camera(70)
            save()
            prop(state['native_command'], 'CommandLineInput').Text = '12,34,56'
            call(state['native_command'], 'DoExecute')
        except:
            report['submission_error'] = traceback.format_exc()
            save()
            call(state['native_command'], 'DoCancel')
    Application.Instance.AsyncInvoke(System.Action(move_and_finish))

def run(sender, args):
    if Rhino.Commands.Command.InCommand() or System.DateTime.UtcNow < phase['due']:
        return
    try:
        stage = phase['stage']
        if stage == 0:
            report['camera_before_refresh'] = move_camera(0)
            for unused in range(5):
                call(state['panel'], 'ResetViewport')
            check('Repeated refresh keeps camera', same_camera(report['camera_before_refresh'], camera()))
            state['priority'].GetMethod('Hide').Invoke(None, None)
            state['priority'].GetMethod('Show').Invoke(None, None)
        elif stage == 1:
            report['camera_after_reopen'] = camera()
            check('Hide and reopen keep camera', same_camera(report['camera_before_refresh'], camera()))
            report['camera_before_replacement'] = move_camera(30)
            # Use the workspace's normal reparenting path. RhinoView.Close is
            # not a safe way to retire this internal, UI-owned document view.
            report['view_before_relayout'] = int(prop(host(), 'View').RuntimeSerialNumber)
            state['priority'].GetMethod('SwitchSide').Invoke(None, None)
            state['priority'].GetMethod('SwitchSide').Invoke(None, None)
        elif stage == 2:
            report['camera_after_replacement'] = camera()
            report['view_after_relayout'] = int(prop(host(), 'View').RuntimeSerialNumber)
            check('Relayout leaves a valid native view', not prop(host(), 'NeedsRecreation'))
            check('Relayout keeps latest camera', same_camera(report['camera_before_replacement'], camera()))
            state['previous_document'] = Grasshopper.Instances.ActiveCanvas.Document
            state['test_document'] = GH_Document()
            point = Param_Point()
            point.CreateAttributes()
            state['test_document'].AddObject(point, False)
            Grasshopper.Instances.ActiveCanvas.Document = state['test_document']
            state['menu'] = ContextMenuStrip()
            point.AppendMenuItems(state['menu'])
            prompt = next(i for i in state['menu'].Items if str(i.Text).lower().startswith('set one'))
            Rhino.RhinoApp.CommandPromptChanged += on_prompt
            prompt.PerformClick()
            check('Native command submits GH point', point.PersistentDataCount == 1
                  and all(v.Value.DistanceTo(Rhino.Geometry.Point3d(12,34,56)) < 1e-6
                          for v in point.PersistentData.AllData(True)))
            report['camera_after_getter'] = camera()
        else:
            report['camera_after_idle'] = camera()
            check('Camera changed during getter', phase['moved'])
            check('Getter completion keeps latest camera', phase['moved']
                  and same_camera(report['camera_during_getter'], report['camera_after_getter']))
            check('Camera remains after idle', phase['moved']
                  and same_camera(report['camera_during_getter'], report['camera_after_idle']))
            check('Grasshopper stays visible', Grasshopper.Instances.DocumentEditor.Visible)
            finish()
            return
        phase['stage'] += 1
        phase['due'] = System.DateTime.UtcNow.AddSeconds(2)
    except:
        check('Camera regression script', False, traceback.format_exc())
        finish()

try:
    assembly = next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == 'SuperHelper')
    with open(str(assembly.Location), 'rb') as stream:
        report['assembly_sha256'] = hashlib.sha256(stream.read()).hexdigest()
    control = assembly.GetType('SuperHelper.MenuReplacer').GetProperty('Control', flags).GetValue(None, None)
    state['priority'] = assembly.GetType('SuperHelper.SuperHelperAssemblyPriority')
    state['panel'] = field(control, '_viewportPanel')
    state['native_command'] = field(field(field(control, '_commandPanel'), '_commandMirror'), '_commandDockBar')
    state['original'] = Rhino.DocObjects.ViewportInfo(prop(host(), 'Viewport'))
    Rhino.RhinoApp.Idle += run
    save()
except:
    check('Camera regression setup', False, traceback.format_exc())
    finish()
