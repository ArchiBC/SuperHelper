# Run inside Rhino after opening GH1 and GH2 with SuperHelper enabled.
import clr
import hashlib
import json
import os
import traceback
import System
import Rhino
from System.Reflection import BindingFlags

flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'artifacts', 'panel-registration.json')
report = {'passed': False, 'checks': [], 'panels': [], 'assemblies': [], 'rhino': str(Rhino.RhinoApp.Version), 'tested_at': str(System.DateTime.Now.ToString('o'))}
try:
    system = System.Type.GetType('Rhino.UI.PanelSystem, RhinoCommon')
    definitions = system.GetMethod('GetDefinitionList', flags)
    assemblies = [a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name in ['SuperHelper', 'SuperHelper.GH2']]
    ids = set()
    host_ids = []
    for assembly in assemblies:
        host = str(assembly.GetName().Name)
        with open(assembly.Location, 'rb') as stream: digest = hashlib.sha256(stream.read()).hexdigest()
        report['assemblies'].append({'path': assembly.Location, 'sha256': digest})
        registered = set()
        for t in assembly.GetExportedTypes():
            if t.Name.startswith('SuperHelper') and t.Name.endswith('Panel') and not t.IsAbstract:
                registered.add(str(t.GUID))
        ids.update(registered)
        host_ids.append(registered)
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
        contents = workspace.GetType().GetField('_contents', flags).GetValue(workspace)
        collections = workspace.GetType().GetField('_ownedCollections', flags).GetValue(workspace)
        checked = 0
        correct = True
        for collection in collections:
            items = collection.GetType().GetProperty('Items', flags).GetValue(collection, None)
            for item in items:
                panel_id = item.GetType().GetProperty('Id', flags).GetValue(item, None)
                if str(panel_id) not in registered: continue
                panel = item.GetType().GetMethod('Content', flags).Invoke(item, System.Array[System.Object]([
                    System.UInt32(Rhino.RhinoDoc.ActiveDoc.RuntimeSerialNumber), False]))
                checked += 1
                correct = correct and panel is not None and System.Object.ReferenceEquals(panel.Content, contents[panel_id])
        report['checks'].append({'name': host + ' shared panel instances resolve only their own workspace content',
                                 'passed': checked > 0 and correct, 'detail': str(checked) + ' panel instances'})
    for item in definitions.Invoke(None, System.Array[System.Object]([False])):
        if str(item.Item2) in ids:
            definition = system.GetMethod('Definition', flags, None, System.Array[System.Type]([clr.GetClrType(System.Guid)]), None).Invoke(None, System.Array[System.Object]([item.Item2]))
            panel_type = definition.GetType().GetProperty('Type', flags).GetValue(definition, None)
            owner_id = definition.GetType().GetProperty('PlugInId', flags).GetValue(definition, None)
            owner = Rhino.PlugIns.PlugIn.GetPlugInInfo(owner_id)
            report['panels'].append({'id': str(item.Item2), 'caption': str(item.Item1), 'registrationAssembly': str(panel_type.Assembly.GetName().Name),
                                     'owner': str(owner_id), 'nativeOwnerLoaded': owner is not None and owner.IsLoaded})
    report['checks'].append({'name': 'GH1 and GH2 use the same nine definitions',
                             'passed': len(host_ids) == 2 and len(host_ids[0]) == 9 and host_ids[0] == host_ids[1]})
    captions = [p['caption'] for p in report['panels']]
    report['checks'].append({'name': 'Rhino registers only one set of nine panels', 'passed': len(captions) == len(set(captions)) == 9})
    report['checks'].append({'name': 'Shared captions have no GH2 suffix', 'passed': all(not p['caption'].endswith(' (GH2)') for p in report['panels'])})
    report['checks'].append({'name': 'All panels belong to the loaded native Rhino plugin, not a GH2 extension GUID',
                             'passed': len(report['panels']) == 9 and all(p['nativeOwnerLoaded'] and p['owner'] == '89447b4b-3888-4b3f-9c83-4f3a5416cb25' for p in report['panels'])})
except:
    report['error'] = traceback.format_exc()
finally:
    report['passed'] = bool(report['checks']) and all(c['passed'] for c in report['checks']) and 'error' not in report
    with open(path, 'w') as stream: json.dump(report, stream, indent=2)
    Rhino.RhinoApp.WriteLine('SuperHelper panel registration: {0}; {1}'.format('PASS' if report['passed'] else 'FAIL', path))
