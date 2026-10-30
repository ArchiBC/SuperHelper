# Run with Rhino's _-RunPythonScript after loading SuperHelper in Grasshopper.
# Uses the host's IronPython runtime so no external Python packages are needed.
import json
import hashlib
import os
import traceback
import clr
import System
import Rhino

clr.AddReference('Grasshopper')
clr.AddReference('System.Windows.Forms')
import Grasshopper
from Grasshopper.Kernel import GH_Document
from Grasshopper.Kernel.Parameters import Param_Point
from System.Reflection import BindingFlags
from System.Runtime.InteropServices import RuntimeInformation
from System.Windows.Forms import ContextMenuStrip

flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
report_path = os.path.join(root, 'artifacts', 'rhino-compatibility.json')
report = {
    'rhino': str(Rhino.RhinoApp.Version),
    'os': str(RuntimeInformation.OSDescription),
    'architecture': str(RuntimeInformation.ProcessArchitecture),
    'runtime': str(RuntimeInformation.FrameworkDescription),
    'checks': [],
}

def field(obj, name):
    return obj.GetType().GetField(name, flags).GetValue(obj)

def prop(obj, name):
    return obj.GetType().GetProperty(name, flags).GetValue(obj, None)

def call(obj, name, *args):
    return obj.GetType().GetMethod(name, flags).Invoke(obj, System.Array[System.Object](args))

def check(name, condition, detail=''):
    report['checks'].append({'name': name, 'passed': bool(condition), 'detail': str(detail)})

def save():
    report['passed'] = not report.get('pending', False) and len(report['checks']) > 0 and all(c['passed'] for c in report['checks'])
    if not os.path.isdir(os.path.dirname(report_path)):
        os.makedirs(os.path.dirname(report_path))
    with open(report_path, 'w') as stream:
        json.dump(report, stream, indent=2)

def start_stability_check(workspace, viewport_panel):
    # Allow command/layout transitions to settle, then verify the same native
    # view survives repeated unchanged layouts. A view that is recreated every
    # idle event can pass a one-time validity check while visibly flickering.
    started = System.DateTime.UtcNow
    samples = []

    def sample(sender, args):
        elapsed = (System.DateTime.UtcNow - started).TotalSeconds
        try:
            if elapsed < 2:
                return
            viewport = field(viewport_panel, '_viewport')
            view = prop(viewport, 'View')
            samples.append({
                'seconds': elapsed,
                'serial': int(view.RuntimeSerialNumber),
                'valid': not prop(viewport, 'NeedsRecreation'),
                'width': viewport.Width,
                'height': viewport.Height,
            })
            call(workspace, 'Relayout')
            if elapsed < 17:
                return
            serials = set(s['serial'] for s in samples)
            check('Native viewport stays stable for 15 seconds', len(samples) >= 40
                  and len(serials) == 1 and all(s['valid'] for s in samples),
                  '{0} samples, {1} native views'.format(len(samples), len(serials)))
            check('Stable viewport has usable native size', all(s['width'] > 50 and s['height'] > 50 for s in samples))
            report['viewport_stability'] = samples
        except:
            check('Viewport stability sampling', False, traceback.format_exc())
        Rhino.RhinoApp.Idle -= sample
        report['pending'] = False
        save()
        Rhino.RhinoApp.WriteLine('SuperHelper compatibility: {0}; {1}'.format('PASS' if report['passed'] else 'FAIL', report_path))

    Rhino.RhinoApp.Idle += sample

previous_document = Grasshopper.Instances.ActiveCanvas.Document
test_document = None
rhino_document = Rhino.RhinoDoc.ActiveDoc
was_modified = rhino_document.Modified
command_check_pending = False
hidden_gh2_editor = None
try:
    assembly = next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == 'SuperHelper')
    report['assembly'] = str(assembly.Location)
    with open(str(assembly.Location), 'rb') as stream:
        report['assembly_sha256'] = hashlib.sha256(stream.read()).hexdigest()
    report['tested_at'] = str(System.DateTime.Now.ToString('o'))
    report['version'] = str(assembly.GetName().Version)
    gh2 = next((a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == 'Grasshopper2'), None)
    if gh2 is not None:
        other_editor = gh2.GetType('Grasshopper2.UI.Editor').GetProperty('Instance', flags).GetValue(None, None)
        if other_editor is not None and not other_editor.Visible:
            hidden_gh2_editor = other_editor
    priority_type = assembly.GetType('SuperHelper.SuperHelperAssemblyPriority')
    menu_type = assembly.GetType('SuperHelper.MenuReplacer')
    control = menu_type.GetProperty('Control', flags).GetValue(None, None)
    host = priority_type.GetProperty('PanelHost', flags).GetValue(None, None)
    shell = field(host, '_hostShell')
    native = field(host, '_nativeHost')
    check('Sidebar has usable native size', shell.Visible and shell.Width >= 50 and shell.Height > 50
          and native.Width > 0 and native.Height > 0, str(shell.Bounds))

    workspace = field(field(control, '_workspaceLayout'), '_workspace')
    viewport_panel = field(control, '_viewportPanel')
    viewport = field(viewport_panel, '_viewport')
    view = prop(viewport, 'View')
    check('Interactive RhinoView', prop(viewport, 'IsInteractiveRhinoView') and view.RuntimeSerialNumber != 0)
    call(viewport_panel, 'Activate', False)
    viewport = field(viewport_panel, '_viewport')
    view = prop(viewport, 'View')
    check('Embedded view becomes active', Rhino.RhinoDoc.ActiveDoc.Views.ActiveView.RuntimeSerialNumber == view.RuntimeSerialNumber)
    command = field(control, '_commandPanel')
    check('Native command mirror', field(command, '_commandMirror') is not None)
    if Rhino.Runtime.HostUtils.RunningOnOSX:
        instance = priority_type.GetField('_instance', flags).GetValue(None)
        bindings = field(instance, '_workspaceContextBindings')
        check('Mac toolbar owns a native context menu binding', bindings.Count > 0)
        check('Mac integrated command focus binding', field(field(command, '_commandMirror'), '_commandInputBinding') is not None)
        call(viewport_panel, 'Activate', True)
        child = field(field(viewport_panel, '_viewport'), '_control')
        handler = next(p for p in child.GetType().GetProperties(flags)
                       if p.Name == 'Handler' and p.DeclaringType == child.GetType()).GetValue(child, None)
        container = prop(handler, 'ContainerControl')
        native_view = field(container, '_nsView')
        check('Mac viewport focuses the native drawing view', native_view.Superview is not None
              and System.Object.ReferenceEquals(native_view.Window.FirstResponder, native_view))

    for index in range(7):
        call(workspace, 'SelectHelpPage', System.Int32(index))
        check('Help page ' + str(index), prop(workspace, 'SelectedHelpIndex') == index)

    section_type = assembly.GetType('SuperHelper.EmbeddedWorkspaceSection')
    for section_name in ['Command', 'Viewport', 'Help']:
        section = System.Enum.Parse(section_type, section_name)
        for visible in [False, True]:
            args = System.Array[System.Object]([section, visible, ''])
            result = workspace.GetType().GetMethod('TrySetSectionVisible', flags).Invoke(workspace, args)
            check(section_name + ' visibility=' + str(visible), result and call(workspace, 'IsSectionVisible', section) == visible, args[2])

    original_side = priority_type.GetProperty('IsSuperHelperOnRight').GetValue(None, None)
    priority_type.GetMethod('SwitchSide').Invoke(None, None)
    check('Switch sidebar side', prop(host, 'DockOnRight') != original_side)
    priority_type.GetMethod('SwitchSide').Invoke(None, None)
    priority_type.GetMethod('Hide').Invoke(None, None)
    check('Hide sidebar', not prop(host, 'Visible'))
    priority_type.GetMethod('Show').Invoke(None, None)
    check('Reopen sidebar', prop(host, 'Visible') and not control.IsDisposed)
    viewport = field(viewport_panel, '_viewport')
    call(viewport_panel, 'Activate', False)
    check('Reopened view remains active', Rhino.RhinoDoc.ActiveDoc.Views.ActiveView.RuntimeSerialNumber == prop(viewport, 'View').RuntimeSerialNumber)

    test_document = GH_Document()
    Grasshopper.Instances.ActiveCanvas.Document = test_document
    obj = Param_Point()
    obj.CreateAttributes()
    test_document.AddObject(obj, False)
    obj.Attributes.Selected = True
    using_menu = ContextMenuStrip()
    obj.AppendMenuItems(using_menu)
    help_items = [item for item in using_menu.Items if 'Help' in item.Text]
    check('Component Help menu exists', len(help_items) == 1)
    if help_items and not Rhino.Runtime.HostUtils.RunningOnOSX:
        help_items[0].PerformClick()
        check('Component Help routes to SuperHelper', System.Object.ReferenceEquals(prop(control, 'CurrentObject').NativeObject, obj))
    using_menu.Dispose()
    view_menu = Grasshopper.Instances.DocumentEditor.MainMenuStrip.Items[2]
    help_command = next(item for item in view_menu.DropDownItems if item.Name == 'SuperHelperObjectHelpCommand')
    help_command.PerformClick()
    check('SuperHelper help command and F1', System.Object.ReferenceEquals(prop(control, 'CurrentObject').NativeObject, obj)
          and str(help_command.ShortcutKeys) == 'F1')
    call(control, 'RefreshObjectData')
    check('Parameter information', field(control, '_objectInfoPanel').GetType().GetProperty('Content').GetValue(field(control, '_objectInfoPanel'), None) is not None)
    check('Development information', 'Param_Point' in field(field(control, '_developmentPanel'), '_typeInformation').Text)

    file_panel = field(control, '_fileManagerPanel')
    field(file_panel, '_directory').Text = os.path.join(root, 'Examples')
    call(file_panel, 'RefreshTree')
    check('File manager lists example folders', len(list(field(file_panel, '_tree').DataStore)) > 0)
    call(workspace, 'SelectHelpPage', System.Int32(6))
    check('Settings opens', prop(workspace, 'SelectedHelpIndex') == 6)
    example_path = os.path.join(root, 'Examples', 'Sets', 'Sequence', 'Range', 'Range.gh')
    example = System.Activator.CreateInstance(assembly.GetType('SuperHelper.HelpExample'), System.Array[System.Object]([example_path]))
    example_task = field(example, '_documentTask')
    command_test_started = System.DateTime.UtcNow

    # A running Python command is itself "InCommand". Wait for idle so the
    # command bar exercises its normal RunScript path and results are ready.
    def test_command_on_idle(sender, args):
        if Rhino.Commands.Command.InCommand():
            return
        if not example_task.IsCompleted and (System.DateTime.UtcNow - command_test_started).TotalSeconds < 30:
            return
        Rhino.RhinoApp.Idle -= test_command_on_idle
        created_points = []
        try:
            # Native command transitions can retire the Mac view. The product
            # idle handler runs before this check and must restore it.
            check('Native viewport recovers on idle', not prop(field(viewport_panel, '_viewport'), 'NeedsRecreation'))
            check('Local example archive loads', example_task.IsCompleted and not example_task.IsFaulted
                  and example_task.Result is not None and example_task.Result.ObjectCount > 0)
            points_before = set(p.Id for p in rhino_document.Objects.GetObjectList(Rhino.DocObjects.ObjectType.Point))
            field(command, '_commandTextBox').Text = '_Point 12,34,56'
            call(command, 'RunCommandFromBar')
            created_points = [p.Id for p in rhino_document.Objects.GetObjectList(Rhino.DocObjects.ObjectType.Point) if p.Id not in points_before]
            check('Command creates the expected geometry', len(created_points) == 1 and
                  rhino_document.Objects.FindId(created_points[0]).Geometry.Location.DistanceTo(Rhino.Geometry.Point3d(12,34,56)) < 1e-6)
            if hidden_gh2_editor is not None:
                check('Hidden GH2 stays hidden during a GH1 command', not hidden_gh2_editor.Visible)
        except System.Exception as error:
            check('Command submission', False, error.ToString())
        except:
            check('Command submission', False, traceback.format_exc())
        finally:
            example.Dispose()
            for point_id in created_points:
                rhino_document.Objects.Delete(point_id, True)
            rhino_document.Modified = was_modified
            save()
            start_stability_check(workspace, viewport_panel)
    command_check_pending = True
    report['pending'] = True
    Rhino.RhinoApp.Idle += test_command_on_idle
except System.Exception as error:
    check('Unhandled host exception', False, error.ToString())
except:
    check('Unhandled script exception', False, traceback.format_exc())
finally:
    rhino_document.Modified = was_modified
    if test_document is not None:
        Grasshopper.Instances.ActiveCanvas.Document = previous_document
        test_document.Dispose()
    save()
    if command_check_pending:
        report['passed'] = False
        with open(report_path, 'w') as stream:
            json.dump(report, stream, indent=2)
        Rhino.RhinoApp.WriteLine('SuperHelper compatibility: waiting for command test on idle.')
    else:
        Rhino.RhinoApp.WriteLine('SuperHelper compatibility: FAIL; ' + report_path)
