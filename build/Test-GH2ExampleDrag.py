# Run three times inside Rhino. First: drag the example card in the temporary
# window onto the GH2 canvas. Second: drag it outside the canvas to cancel.
# Third: validate cancellation and restore the original document and settings.
# Set sticky['SuperHelper.GH2.ExampleDragMode']='cancel' for a separate two-run
# cancellation fixture. Escape must end the native drag before validation.
import clr
import hashlib
import json
import os
import traceback
import System
import Rhino
import scriptcontext as sc
clr.AddReference('Grasshopper2')
import Grasshopper2
clr.AddReference('Grasshopper')
import Grasshopper
from Eto.Forms import Form, Mouse
from Eto.Drawing import Point, Size
from System.Reflection import BindingFlags

flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
mode = sc.sticky.get('SuperHelper.GH2.ExampleDragMode', 'full')
path = os.path.join(root, 'artifacts', 'gh2-example-drag' + ('-cancel' if mode == 'cancel' else '') + '.json')
key = 'SuperHelper.GH2.ExampleDrag' + ('.Cancel' if mode == 'cancel' else '')

def field(obj, name): return obj.GetType().GetField(name, flags).GetValue(obj)
def prop(obj, name): return obj.GetType().GetProperty(name, flags).GetValue(obj, None)
def save(report):
    with open(path, 'w') as stream: json.dump(report, stream, indent=2)
def check(state, name, passed, detail=''):
    state['report']['checks'].append({'name': name, 'passed': bool(passed), 'detail': str(detail)})

def cleanup(state):
    if 'record_drop' in state:
        state['editor'].Canvas.DragDrop -= state['record_drop']
    if 'record_end' in state:
        state['form'].Content.DragEnd -= state['record_end']
    if 'form' in state:
        state['form'].Close()
        state['form'].Dispose()
    if 'example' in state: state['example'].Dispose()
    if 'subscriptions' in state:
        callbacks = state['subscriptions']
        while callbacks.Count > state['subscription_count']:
            callbacks[callbacks.Count - 1].Invoke()
            callbacks.RemoveAt(callbacks.Count - 1)
    if 'document' in state:
        state['editor'].Canvas.Document = state['previous_document']
        state['document'].Solution.Stop()
        state['document'].Close()
    if 'auto_target' in state: state['preference'].SetValue(None, state['auto_target'], None)
    if state.get('gh1_editor') is not None: state['gh1_editor'].Visible = state['gh1_visible']
    state['report']['pending'] = False
    state['report']['passed'] = bool(state['report']['checks']) and all(c['passed'] for c in state['report']['checks']) and 'error' not in state['report']
    if key in sc.sticky: del sc.sticky[key]
    if 'SuperHelper.GH2.ExampleDragMode' in sc.sticky: del sc.sticky['SuperHelper.GH2.ExampleDragMode']

state = sc.sticky.get(key, {'report': {'pending': True, 'passed': False, 'checks': [], 'rhino': str(Rhino.RhinoApp.Version)}})
try:
    if key not in sc.sticky:
        sc.sticky[key] = state
        assembly = next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == 'SuperHelper.GH2')
        state['report']['tested_at'] = str(System.DateTime.Now.ToString('o'))
        with open(assembly.Location, 'rb') as stream: state['report']['pluginSha256'] = hashlib.sha256(stream.read()).hexdigest()
        workspace = assembly.GetType('SuperHelper.SuperHelperGh2Plugin').GetField('_workspace', flags).GetValue(None)
        control = prop(workspace, 'Control')
        state['preference'] = assembly.GetType('SuperHelper.SuperHelperPreferences').GetProperty('AutoTarget', flags)
        state['auto_target'] = state['preference'].GetValue(None, None)
        state['preference'].SetValue(None, False, None)
        state['editor'] = Grasshopper2.UI.Editor.Instance
        state['previous_document'] = state['editor'].Canvas.Document
        state['document'] = Grasshopper2.Doc.Document.NewActiveDocument()
        state['editor'].Canvas.Document = state['document']
        state['gh1_editor'] = Grasshopper.Instances.DocumentEditor
        if state['gh1_editor'] is not None:
            state['gh1_visible'] = state['gh1_editor'].Visible
            state['gh1_editor'].Visible = False
        state['editor'].Visible = True
        state['editor'].BringToFront()
        state['report']['drop_events'] = []
        state['report']['drag_end_events'] = []
        def record_drop(sender, args):
            state['report']['drop_events'].append({'location': str(args.Location), 'mouse': str(Mouse.Position),
                'canvas_size': str(sender.Size), 'objects_after_drop': state['document'].Objects.Count})
            save(state['report'])
        state['record_drop'] = record_drop
        state['editor'].Canvas.DragDrop += record_drop
        state['example_type'] = assembly.GetType('SuperHelper.Gh2Example')
        source = os.path.join(root, 'Examples', 'Sets', 'Sequence', 'Series', 'Series.gh')
        state['example'] = System.Activator.CreateInstance(state['example_type'], System.Array[System.Object]([source]))
        example_document = field(state['example'], '_document')
        if example_document is None: raise Exception('Wait for the local example to load before dragging.')
        example_document.Methods.DeselectAll()
        state['source_count'] = example_document.Objects.Count
        state['dragged'] = state['example_type'].GetField('DraggedDocuments', flags).GetValue(None)
        panel = field(control, '_examplesPanel')
        state['subscriptions'] = field(panel, '_cardSubscriptions')
        state['subscription_count'] = state['subscriptions'].Count
        card = panel.GetType().GetMethod('BuildCard', flags).Invoke(panel, System.Array[System.Object]([state['example']]))
        def record_end(sender, args):
            state['report']['drag_end_events'].append(str(args.Effects))
            save(state['report'])
        state['record_end'] = record_end
        card.DragEnd += record_end
        window = Form()
        window.Title = 'SuperHelper example drag test'
        window.ClientSize = Size(340, 120)
        window.Location = Point(600, 520)
        window.Owner = state['editor']
        window.Content = card
        state['form'] = window
        window.Show()
        window.Focus()
        state['phase'] = 1 if mode == 'cancel' else 0
        state['report']['phase'] = 'Drag the card outside the canvas or press Escape to cancel, then run again.' if mode == 'cancel' else 'Drag the Series.gh card from the test window onto the GH2 canvas, then run again.'
    elif state['phase'] == 0:
        count = state['document'].Objects.Count
        check(state, 'Native example drag inserts the whole document once', count == state['source_count'], count)
        check(state, 'Insertion used a native canvas drop event', len(state['report']['drop_events']) == 1)
        check(state, 'Native example drag retains wires', len(list(state['document'].Objects.AllWires)) > 0)
        check(state, 'Drag token is released after drop', prop(state['dragged'], 'Count') == 0)
        source = field(state['example'], '_document')
        check(state, 'Drag preserves unselected source objects', source.Objects.SelectedCount == 0)
        state['document'].Undo.Undo()
        check(state, 'Native drag insertion is undoable', state['document'].Objects.Count == 0)
        state['document'].Undo.Redo()
        check(state, 'Native drag insertion is redoable', state['document'].Objects.Count == count)
        state['document'].Undo.Undo()
        state['phase'] = 1
        state['cancel_start'] = len(state['report']['drag_end_events'])
        state['report']['phase'] = 'Drag the same card outside the canvas to cancel, then run again.'
    elif 'None' not in state['report']['drag_end_events'][state.get('cancel_start', 0):]:
        state['report']['phase'] = 'Waiting for native DragEnd (None). Press Escape to cancel the drag, then run again.'
    else:
        check(state, 'Native cancellation dispatches DragEnd (None)', True)
        check(state, 'Cancelled drag leaves the canvas empty', state['document'].Objects.Count == 0, state['document'].Objects.Count)
        check(state, 'Cancelled drag does not dispatch another canvas drop', len(state['report']['drop_events']) == (0 if mode == 'cancel' else 1))
        check(state, 'Cancelled drag releases its token', prop(state['dragged'], 'Count') == 0)
        check(state, 'Cancelled drag keeps its source alive', field(state['example'], '_document').Objects.Count == state['source_count'])
        cleanup(state)
        state['report']['phase'] = 'Finished; original document and settings restored.'
except:
    state['report']['error'] = traceback.format_exc()
    cleanup(state)
finally:
    save(state['report'])
    Rhino.RhinoApp.WriteLine('SuperHelper GH2 example drag: ' + state['report'].get('phase', state['report'].get('error', '')))
