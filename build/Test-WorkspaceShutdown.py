# Run only in a dedicated test Rhino: retires both SuperHelper workspaces.
# Does not close or modify GH documents, Rhino documents, or other panels.
import clr
import json
import os
import traceback
import System
import Rhino
from System.Reflection import BindingFlags

flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'artifacts', 'workspace-shutdown.json')
report = {'passed': False, 'checks': [], 'rhino': str(Rhino.RhinoApp.Version)}

def field(obj, name): return obj.GetType().GetField(name, flags).GetValue(obj)
def prop(obj, name): return obj.GetType().GetProperty(name, flags).GetValue(obj, None)
def check(name, passed): report['checks'].append({'name': name, 'passed': bool(passed)})

try:
    assemblies = dict((str(a.GetName().Name), a) for a in System.AppDomain.CurrentDomain.GetAssemblies()
                      if a.GetName().Name in ['SuperHelper', 'SuperHelper.GH2'])
    workspaces = []
    for name, assembly in assemblies.items():
        if name == 'SuperHelper':
            control = assembly.GetType('SuperHelper.MenuReplacer').GetProperty('Control', flags).GetValue(None, None)
        else:
            shell = assembly.GetType('SuperHelper.SuperHelperGh2Plugin').GetField('_workspace', flags).GetValue(None)
            control = prop(shell, 'Control')
        workspaces.append((name, field(field(control, '_workspaceLayout'), '_workspace')))
    collections_type = System.Type.GetType('Rhino.UI.Internal.TabPanels.TabPanelCollections, Rhino.UI', True)
    from_id = collections_type.GetMethod('FromDockBarId', flags, None, System.Array[System.Type]([clr.GetClrType(System.Guid)]), None)
    ids = []
    for name, workspace in workspaces:
        for key in ['_commandDockBarId', '_viewportDockBarId', '_helpDockBarId']:
            ids.append(field(workspace, key))
    check('Both hosts own three private native containers before shutdown', len(ids) == 6
          and all(from_id.Invoke(None, System.Array[System.Object]([id])) is not None for id in ids))
    registry = System.AppDomain.CurrentDomain.GetData('SuperHelper.SharedPanelContentResolvers.v1')
    before = registry.Count
    for name in ['SuperHelper.GH2', 'SuperHelper']:
        type_name = 'SuperHelper.SuperHelperGh2Plugin' if name.endswith('.GH2') else 'SuperHelper.SuperHelperAssemblyPriority'
        method = assemblies[name].GetType(type_name).GetMethod('ShutdownWorkspace', flags)
        method.Invoke(None, None)
        method.Invoke(None, None)
        check(name + ' shutdown is idempotent', True)
    check('Every private native container was removed', all(from_id.Invoke(None, System.Array[System.Object]([id])) is None for id in ids))
    check('Both workspace content registrations were released', before == 2 and registry.Count == 0)
    check('Native panel owner remains loaded until Rhino unloads it', Rhino.PlugIns.PlugIn.GetPlugInInfo(
        System.Guid('89447B4B-3888-4B3F-9C83-4F3A5416CB25')).IsLoaded)
    report['passed'] = all(item['passed'] for item in report['checks'])
except:
    report['error'] = traceback.format_exc()
finally:
    with open(path, 'w') as stream: json.dump(report, stream, indent=2)
    Rhino.RhinoApp.WriteLine('SuperHelper workspace shutdown: {0}; {1}'.format('PASS' if report['passed'] else 'FAIL', path))
