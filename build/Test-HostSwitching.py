# Run inside Rhino after loading both SuperHelper extensions. Uses the same
# handlers as the menus; keeps each host's existing document and open state.
import clr
import hashlib
import json
import os
import traceback
import System
import Rhino
clr.AddReference('Grasshopper')
clr.AddReference('Grasshopper2')
import Grasshopper
import Grasshopper2
from System.Reflection import BindingFlags

flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'artifacts', 'host-switching.json')
report = {'passed': False, 'checks': [], 'rhino': str(Rhino.RhinoApp.Version), 'assemblies': []}

def check(name, passed):
    report['checks'].append({'name': name, 'passed': bool(passed)})

def prop(obj, name):
    return obj.GetType().GetProperty(name, flags).GetValue(obj, None)

def call(obj, name):
    return obj.GetType().GetMethod(name, flags).Invoke(obj, None)

gh1 = None
gh2 = None
try:
    assemblies = dict((str(a.GetName().Name), a) for a in System.AppDomain.CurrentDomain.GetAssemblies()
                      if a.GetName().Name in ['SuperHelper', 'SuperHelper.GH2'])
    for a in assemblies.values():
        with open(a.Location, 'rb') as stream: digest = hashlib.sha256(stream.read()).hexdigest()
        report['assemblies'].append({'path': a.Location, 'sha256': digest})
    priority = assemblies['SuperHelper'].GetType('SuperHelper.SuperHelperAssemblyPriority').GetField('_instance', flags).GetValue(None)
    workspace = assemblies['SuperHelper.GH2'].GetType('SuperHelper.SuperHelperGh2Plugin').GetField('_workspace', flags).GetValue(None)
    gh1 = Grasshopper.Instances.DocumentEditor
    gh2 = Grasshopper2.UI.Editor.Instance
    previous_visibility = (gh1.Visible, gh2.Visible)
    documents = (Grasshopper.Instances.ActiveCanvas.Document, gh2.Canvas.Document)
    open_properties = [a.GetType('SuperHelper.SuperSetting').GetProperty('Open', flags) for a in assemblies.values()]
    original_open = [p.GetValue(None, None) for p in open_properties]
    view_menu = gh1.MainMenuStrip.Items[2]
    check('GH1 has exactly one menu option to switch to GH2',
          sum(item.Name == 'SuperHelperSwitchToGH2' for item in view_menu.DropDownItems) == 1)
    menu = workspace.GetType().GetField('_menu', flags).GetValue(workspace)
    check('GH2 has exactly one menu option to switch to GH1', sum(item.Text == 'Switch to Grasshopper 1' for item in menu.Items) == 1)
    for turn in range(3):
        call(priority, 'SwitchToGrasshopper2')
        Rhino.RhinoApp.Wait()
        check('Switch {0}: only GH2 is visible'.format(turn + 1), gh2.Visible and not gh1.Visible)
        call(workspace, 'SwitchToGrasshopper1')
        Rhino.RhinoApp.Wait()
        check('Switch {0}: only GH1 is visible'.format(turn + 1), gh1.Visible and not gh2.Visible)
    check('Switching preserves the GH1 document', System.Object.ReferenceEquals(documents[0], Grasshopper.Instances.ActiveCanvas.Document))
    check('Switching preserves the GH2 document', System.Object.ReferenceEquals(documents[1], gh2.Canvas.Document))
    check('Switching preserves both workspace open settings', original_open == [p.GetValue(None, None) for p in open_properties])
    report['passed'] = all(item['passed'] for item in report['checks'])
except:
    report['error'] = traceback.format_exc()
finally:
    if gh1 is not None and gh2 is not None and 'previous_visibility' in locals():
        gh1.Visible, gh2.Visible = previous_visibility
    with open(path, 'w') as stream: json.dump(report, stream, indent=2)
    Rhino.RhinoApp.WriteLine('SuperHelper host switching: {0}; {1}'.format('PASS' if report['passed'] else 'FAIL', path))
