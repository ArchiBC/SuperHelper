# Run inside Rhino 9 with GH1, GH2, SuperHelper and Hops installed.
# Start a real ghhops_server endpoint at http://127.0.0.1:18731/superhelper-test
# with numeric inputs A=6, B=7 and result A*B+1. Only scratch documents are used.
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
from Grasshopper2.Components.Standard import GH1InteropComponent

flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
path = os.path.join(root, 'artifacts', 'hops-compatibility.json')
endpoint = 'http://127.0.0.1:18731/superhelper-test'
report = {'passed': False, 'checks': [], 'rhino': str(Rhino.RhinoApp.Version),
          'tested_at': str(System.DateTime.Now.ToString('o')), 'endpoint': endpoint}
state = {}

def check(name, passed, detail=''):
    report['checks'].append({'name': name, 'passed': bool(passed), 'detail': str(detail)})

def invoke(obj, name, *args):
    return obj.GetType().GetMethod(name, flags).Invoke(obj, System.Array[System.Object](args))

try:
    assembly = next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == 'SuperHelper.GH2')
    with open(assembly.Location, 'rb') as stream: report['pluginSha256'] = hashlib.sha256(stream.read()).hexdigest()
    editor = Grasshopper2.UI.Editor.Instance
    state['canvas'] = editor.Canvas
    state['previous_document'] = editor.Canvas.Document
    state['preference'] = assembly.GetType('SuperHelper.SuperHelperPreferences').GetProperty('AutoTarget', flags)
    state['auto_target'] = state['preference'].GetValue(None, None)
    state['preference'].SetValue(None, False, None)
    state['target'] = Grasshopper2.Doc.Document.NewActiveDocument()
    editor.Canvas.Document = state['target']

    native = Grasshopper.Instances.ComponentServer.EmitObject(System.Guid('C69BB52C-88BA-4640-B69F-188D111029E8'))
    check('GH1 locates the installed native Hops component', native is not None)
    report['hops_assembly'] = str(native.GetType().Assembly.Location)
    report['hops_version'] = str(native.GetType().Assembly.GetName().Version)
    native.GetType().GetField('synchronous', flags).SetValue(native, True)
    native.GetType().GetField('cacheResultsInMemory', flags).SetValue(native, False)
    state['native_document'] = Grasshopper.Kernel.GH_Document()
    state['enable_solutions'] = Grasshopper.Kernel.GH_Document.EnableSolutions
    Grasshopper.Kernel.GH_Document.EnableSolutions = True
    native.CreateAttributes()
    state['native_document'].AddObject(native, False)
    state['native_document'].Enabled = True
    native.Locked = False
    check('Native Hops belongs to the scratch GH1 document', state['native_document'].ObjectCount == 1)
    native.RemoteDefinitionLocation = endpoint
    check('Hops fetches the live Python schema', native.Params.Input.Count == 2 and native.Params.Output.Count == 1)
    state['native_document'].NewSolution(False)
    report['native_messages'] = [str(m) for level in [Grasshopper.Kernel.GH_RuntimeMessageLevel.Error, Grasshopper.Kernel.GH_RuntimeMessageLevel.Warning]
                                  for m in native.RuntimeMessages(level)]
    report['native_phase'] = str(native.Phase)
    report['native_message'] = str(native.Message)
    report['native_response'] = str(native.HttpRecord.SolveResponse)
    numbers = [float(v.Value) for v in native.Params.Output[0].VolatileData.AllData(True)]
    check('Native Hops solves against the live Python server', numbers == [43.0], numbers)
    Grasshopper.Kernel.GH_Document.EnableSolutions = state['enable_solutions']

    proxy = Grasshopper2.Interop.Grasshopper1.Instance.CreateObject(System.Guid('C69BB52C-88BA-4640-B69F-188D111029E8'))
    check('GH2 locates the installed native Hops component', proxy is not None)
    interop_native = proxy.UnderlyingObject
    interop_native.GetType().GetField('synchronous', flags).SetValue(interop_native, True)
    interop_native.GetType().GetField('cacheResultsInMemory', flags).SetValue(interop_native, False)
    interop_native.GetType().GetProperty('RemoteDefinitionLocation', flags).SetValue(interop_native, endpoint, None)
    wrapped = GH1InteropComponent(proxy)
    check('GH2 wrapper preserves Hops identity', wrapped.Grasshopper1Id == System.Guid('C69BB52C-88BA-4640-B69F-188D111029E8'))
    check('GH2 wrapper preserves the live endpoint', endpoint in wrapped.Grasshopper1Xml)
    state['source'] = Grasshopper2.Doc.Document.NewInertDocument()
    state['source'].Objects.Add(wrapped)
    helper = assembly.GetType('SuperHelper.Gh2Example')
    inserted = helper.GetMethod('Insert', flags).Invoke(None, System.Array[System.Object]([state['source'], editor.Canvas, None]))
    check('SuperHelper inserts the Hops wrapper once', bool(inserted) and state['target'].Objects.Count == 1)
    state['target'].Solution.StartWait()
    target_object = next(iter(state['target'].Objects.Forwards))
    inputs = list(target_object.Parameters.Inputs)
    outputs = list(target_object.Parameters.Outputs)
    check('Inserted Hops exposes its inputs and output in GH2', len(inputs) == 2 and len(outputs) == 1)
    info = assembly.GetType('SuperHelper.Gh2ObjectInfo')
    arguments = System.Array[System.Object]([outputs[0], None])
    tree = info.GetMethod('ParameterTree', flags).Invoke(None, arguments)
    values = [float(p.Item) for p in tree.AllPears] if tree is not None else []
    check('GH2 Hops returns the live server result', values == [43.0], values)
    state['target'].Undo.Undo()
    check('Hops insertion is undoable', state['target'].Objects.Count == 0)
    state['target'].Undo.Redo()
    check('Hops insertion is redoable', state['target'].Objects.Count == 1)
except:
    report['error'] = traceback.format_exc()
finally:
    if 'target' in state:
        state['target'].Solution.Stop()
        state['canvas'].Document = state['previous_document']
        state['target'].Close()
    if 'source' in state: state['source'].Close()
    if 'native_document' in state: state['native_document'].Dispose()
    if 'enable_solutions' in state: Grasshopper.Kernel.GH_Document.EnableSolutions = state['enable_solutions']
    if 'auto_target' in state: state['preference'].SetValue(None, state['auto_target'], None)
    report['passed'] = bool(report['checks']) and all(c['passed'] for c in report['checks']) and 'error' not in report
    with open(path, 'w') as stream: json.dump(report, stream, indent=2)
    Rhino.RhinoApp.WriteLine('SuperHelper Hops compatibility: {0}; {1}'.format('PASS' if report['passed'] else 'FAIL', path))
