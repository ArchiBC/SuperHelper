# Run inside Rhino after opening GH1 and GH2 with SuperHelper enabled.
import clr
import json
import os
import traceback
import System
import Rhino
from System.Reflection import BindingFlags

flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'artifacts', 'panel-registration.json')
report = {'passed': False, 'checks': [], 'panels': [], 'rhino': str(Rhino.RhinoApp.Version)}
try:
    system = System.Type.GetType('Rhino.UI.PanelSystem, RhinoCommon')
    definitions = system.GetMethod('GetDefinitionList', flags)
    assemblies = [a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name in ['SuperHelper', 'SuperHelper.GH2']]
    ids = {}
    for assembly in assemblies:
        host = str(assembly.GetName().Name)
        for t in assembly.GetExportedTypes():
            if t.Name.startswith('SuperHelper') and t.Name.endswith('Panel') and not t.IsAbstract:
                ids[str(t.GUID)] = host
        if host == 'SuperHelper.GH2':
            workspace = assembly.GetType('SuperHelper.SuperHelperGh2Plugin').GetField('_workspace', flags).GetValue(None)
            control = workspace.GetType().GetProperty('Control', flags).GetValue(workspace, None)
        else: control = assembly.GetType('SuperHelper.MenuReplacer').GetProperty('Control', flags).GetValue(None, None)
        layout = control.GetType().GetField('_workspaceLayout', flags).GetValue(control)
        workspace = layout.GetType().GetField('_workspace', flags).GetValue(layout)
        before = definitions.Invoke(None, System.Array[System.Object]([False])).Count
        for unused in range(5): workspace.GetType().GetMethod('RegisterSuperHelperPanels', flags).Invoke(workspace, None)
        after = definitions.Invoke(None, System.Array[System.Object]([False])).Count
        report['checks'].append({'name': host + ' repeated registration is idempotent', 'passed': before == after})
    for item in definitions.Invoke(None, System.Array[System.Object]([False])):
        if str(item.Item2) in ids:
            report['panels'].append({'host': ids[str(item.Item2)], 'id': str(item.Item2), 'caption': str(item.Item1)})
    report['checks'].append({'name': 'Each host registers exactly nine panels', 'passed': all(sum(p['host'] == h for p in report['panels']) == 9 for h in ['SuperHelper', 'SuperHelper.GH2'])})
    captions = [p['caption'] for p in report['panels']]
    report['checks'].append({'name': 'Rhino panel captions are distinct across hosts', 'passed': len(captions) == len(set(captions)) == 18})
    report['checks'].append({'name': 'GH2 panels identify their host', 'passed': all(p['caption'].endswith(' (GH2)') for p in report['panels'] if p['host'] == 'SuperHelper.GH2')})
except:
    report['error'] = traceback.format_exc()
finally:
    report['passed'] = bool(report['checks']) and all(c['passed'] for c in report['checks']) and 'error' not in report
    with open(path, 'w') as stream: json.dump(report, stream, indent=2)
    Rhino.RhinoApp.WriteLine('SuperHelper panel registration: {0}; {1}'.format('PASS' if report['passed'] else 'FAIL', path))
