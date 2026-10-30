# Run inside Rhino 9 with _-RunPythonScript after starting GH2.
# Scratch documents are detached from the canvas and closed in finally.
import clr
import os
import json
import hashlib
import traceback
import System
import Rhino
import scriptcontext as sc

clr.AddReference('Grasshopper2')
clr.AddReference('Eto')
import Grasshopper2
import Eto
from System.Reflection import BindingFlags

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
report_path = os.path.join(root, 'artifacts', 'gh2-rhino-compatibility.json')
report = {'scope': 'Rhino 9 GH2 integration', 'rhino': str(Rhino.RhinoApp.Version), 'checks': [], 'skipped': []}

def check(name, condition, detail=''):
    report['checks'].append({'name': name, 'passed': bool(condition), 'detail': str(detail)})

def field(obj, name):
    return obj.GetType().GetField(name, flags).GetValue(obj)

def invoke(obj, name, *args):
    return obj.GetType().GetMethod(name, flags).Invoke(obj, System.Array[System.Object](args))

def save():
    report['passed'] = bool(report['checks']) and all(item['passed'] for item in report['checks']) and 'error' not in report
    report['complete'] = report['passed'] and len(report['skipped']) == 0
    with open(report_path, 'w') as stream:
        json.dump(report, stream, indent=2, ensure_ascii=True)
    Rhino.RhinoApp.WriteLine('SuperHelper GH2 report: ' + report_path)

editor = None
previous = None
scratch = None
source = None
loaded = None
open_property = None
previous_open = None
try:
    Rhino.RhinoApp.RunScript('_GH2', False)
    editor = Grasshopper2.UI.Editor.Instance
    if editor is None:
        raise Exception('Start GH2 before running this script.')
    plugin_path = sc.sticky.get('SuperHelper.GH2.TestAssembly', os.path.join(root, 'SuperHelper.GH2', 'bin', 'Release', 'net10.0', 'SuperHelper.GH2.rhp'))
    assemblies = [item for item in System.AppDomain.CurrentDomain.GetAssemblies() if item.GetName().Name == 'SuperHelper.GH2']
    report['auto_loaded_before_test'] = bool(assemblies)
    failure = None
    if not assemblies:
        Grasshopper2.Framework.PluginServer.ScopePlugin(plugin_path)
        result, failure = Grasshopper2.Framework.PluginServer.LoadPlugin(plugin_path)
        report['plugin_load_result'] = str(result)
        if failure is not None:
            report['plugin_load_failure'] = dict((prop.Name, str(prop.GetValue(failure, None)))
                                                 for prop in failure.GetType().GetProperties()
                                                 if prop.GetIndexParameters().Length == 0 and not prop.GetGetMethod().IsStatic)
        assemblies = [item for item in System.AppDomain.CurrentDomain.GetAssemblies() if item.GetName().Name == 'SuperHelper.GH2']
    else:
        report['plugin_load_result'] = 'Already loaded'
    if not assemblies:
        raise Exception('GH2 extension failed to load: ' + str(failure))
    assembly = assemblies[0]
    report['plugin'] = assembly.Location
    with open(assembly.Location, 'rb') as stream:
        report['pluginSha256'] = hashlib.sha256(stream.read()).hexdigest()
    with open(plugin_path, 'rb') as stream:
        expected_hash = hashlib.sha256(stream.read()).hexdigest()
    check('Loaded this build', report['pluginSha256'] == expected_hash, assembly.Location)
    plugin_type = assembly.GetType('SuperHelper.SuperHelperGh2Plugin')
    workspace = plugin_type.GetField('_workspace', flags).GetValue(None)
    invoke(workspace, 'OnIdle', None, System.EventArgs.Empty)
    control = workspace.GetType().GetProperty('Control', flags).GetValue(workspace, None)
    check('Workspace is attached', control is not None)
    if control is None:
        raise Exception('See the Rhino command log for the workspace attachment failure.')
    host = assembly.GetType('SuperHelper.HelperHost').GetProperty('Current', flags).GetValue(None, None)
    platform = host.GetType().GetProperty('Platform', flags).GetValue(host, None)
    expected_platform = 'MacWorkspacePlatform' if Rhino.Runtime.HostUtils.RunningOnOSX else 'WindowsWorkspacePlatform'
    check('Native workspace platform matches the OS', platform.GetType().Name == expected_platform)
    canvas_input = field(workspace, '_canvasInput')
    expected_input = 'Gh2MacCanvasInput' if Rhino.Runtime.HostUtils.RunningOnOSX else 'Gh2WindowsCanvasInput'
    check('Native canvas input binding is installed', canvas_input is not None and canvas_input.GetType().Name == expected_input)
    open_property = assembly.GetType('SuperHelper.SuperSetting').GetProperty('Open', flags)
    previous_open = open_property.GetValue(None, None)
    open_property.SetValue(None, True, None)
    check('GH2 menu entry', any(item.Text == 'SuperHelper' for item in editor.Menu.Items))
    check('GH2 keeps the settings entry in its menu', any(item.Text == 'Open Settings'
          for menu in editor.Menu.Items if menu.Text == 'SuperHelper' for item in menu.Items))
    check('GH2 offers a switch to Grasshopper 1', any(item.Text == 'Switch to Grasshopper 1'
          for menu in editor.Menu.Items if menu.Text == 'SuperHelper' for item in menu.Items))
    check('GH2 does not add a SuperHelper toolbar button', editor.ToolBar is None
          or not any(item.Text == 'SuperHelper' for item in editor.ToolBar.Items))
    check('Command panel exists', field(control, '_commandPanel') is not None)
    viewport = field(field(control, '_viewportPanel'), '_viewport')
    check('Embedded Rhino viewport was created', viewport is not None)
    if viewport is not None:
        check('Embedded viewport is current', not viewport.GetType().GetProperty('NeedsRecreation', flags).GetValue(viewport, None))
    for name in ['_originHelpPanel', '_webHelpPanel', '_examplesPanel', '_objectInfoPanel', '_developmentPanel', '_fileManagerPanel', '_settingsPanel']:
        panel = field(control, name)
        check(name + ' exists', panel is not None and not panel.IsDisposed)

    previous = editor.Canvas.Document
    scratch = Grasshopper2.Doc.Document.NewActiveDocument()
    editor.Canvas.Document = scratch
    slider = Grasshopper2.Parameters.Special.NumberSliderObject()
    slider.Attributes.Pivot = Eto.Drawing.PointF(0, 0)
    scratch.Objects.Add(slider)
    scratch.Methods.SelectAll()
    scratch.Solution.StartWait()
    invoke(workspace, 'ShowSelectedHelp')
    current = control.GetType().GetProperty('CurrentObject').GetValue(control, None)
    check('Selected GH2 object reaches help', current is not None and current.NativeObject == slider)
    info_type = assembly.GetType('SuperHelper.Gh2ObjectInfo')
    arguments = System.Array[System.Object]([slider, None])
    tree = info_type.GetMethod('ParameterTree', flags).Invoke(None, arguments)
    check('Solved parameter tree is available', tree is not None and tree.ItemCount > 0, arguments[1])
    example_type = assembly.GetType('SuperHelper.Gh2Example')
    sample = os.path.join(root, 'Examples', 'Sets', 'Sequence', 'Series', 'Series.gh')
    loaded = example_type.GetMethod('ReadDocument', flags).Invoke(None, System.Array[System.Object]([sample]))
    check('GH1 example migrates through GH2', loaded is not None and loaded.Objects.Count > 0)
    if loaded is not None:
        check('Migrated example retains wires', len(list(loaded.Objects.AllWires)) > 0)
        loaded.Methods.DeselectAll()
        before = scratch.Objects.Count
        inserted = example_type.GetMethod('Insert', flags).Invoke(None, System.Array[System.Object]([loaded, editor.Canvas, None]))
        scratch.Solution.Stop()
        after = scratch.Objects.Count
        check('Example inserts into the GH2 canvas', inserted and after > before,
              'inserted={0}; before={1}; after={2}'.format(inserted, before, after))
        check('Example copy restores the source selection', loaded.Objects.SelectedCount == 0)
        scratch.Undo.Undo()
        check('Example insertion is undoable', scratch.Objects.Count == before)
        scratch.Undo.Redo()
        check('Example insertion is redoable', scratch.Objects.Count == after)
    open_property.SetValue(None, False, None)
    open_property.SetValue(None, True, None)
    check('Workspace reopens using the same control', workspace.GetType().GetProperty('Control', flags).GetValue(workspace, None) == control)
    report['skipped'].extend([
        'Interactive drag/drop placement and cancellation require a pointer gesture.',
        'Native point picking and camera continuity are tested separately by Test-GH2PointPrompt.py.',
        'Hops requires its optional GH1 plugin and a running compute service.'
    ])
except Exception:
    report['error'] = traceback.format_exc()
    Rhino.RhinoApp.WriteLine(report['error'])
finally:
    if editor is not None and previous is not None:
        editor.Canvas.Document = previous
    elif editor is not None and scratch is not None:
        editor.Canvas.Document = None
    if scratch is not None:
        scratch.Solution.Stop()
        scratch.Close()
    if loaded is not None:
        loaded.Close()
    if open_property is not None and previous_open is not None:
        open_property.SetValue(None, previous_open, None)
    save()
