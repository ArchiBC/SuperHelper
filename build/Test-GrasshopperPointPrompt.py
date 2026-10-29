# Run after Test-RhinoCompatibility.py, then click in the embedded viewport.
# A temporary GH point parameter exercises GH's own persistent-data getter.
import clr
import json
import os
import traceback
import System
import Rhino
clr.AddReference('Grasshopper')
clr.AddReference('System.Windows.Forms')
import Grasshopper
from Grasshopper.Kernel import GH_Document
from Grasshopper.Kernel.Parameters import Param_Point
from System.Drawing import PointF
from System.Reflection import BindingFlags
from System.Windows.Forms import ContextMenuStrip

flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
path = os.path.join(root, 'artifacts', 'grasshopper-point-prompt.json')
report = {'pending': True, 'passed': False, 'rhino': str(Rhino.RhinoApp.Version)}
assembly = next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == 'SuperHelper')
control = assembly.GetType('SuperHelper.MenuReplacer').GetProperty('Control', flags).GetValue(None, None)
viewport_panel = control.GetType().GetField('_viewportPanel', flags).GetValue(control)

class ClickRecorder(Rhino.UI.MouseCallback):
    def OnMouseDown(self, args):
        viewport = viewport_panel.GetType().GetField('_viewport', flags).GetValue(viewport_panel)
        view = viewport.GetType().GetProperty('View').GetValue(viewport, None)
        if args.View.RuntimeSerialNumber == view.RuntimeSerialNumber:
            report['embedded_view_clicked'] = True
            report['grasshopper_visible_at_click'] = Grasshopper.Instances.DocumentEditor.Visible

recorder = ClickRecorder()
previous_document = Grasshopper.Instances.ActiveCanvas.Document
document = GH_Document()
point = Param_Point()
point.CreateAttributes()
point.Attributes.Pivot = PointF(400, 250)
document.AddObject(point, False)
Grasshopper.Instances.ActiveCanvas.Document = document
Grasshopper.Instances.ActiveCanvas.Refresh()
menu = ContextMenuStrip()
point.AppendMenuItems(menu)
prompt = next(item for item in menu.Items if str(item.Text).lower().startswith('set one'))

def save():
    with open(path, 'w') as stream:
        json.dump(report, stream, indent=2)

def run(sender, args):
    if Rhino.Commands.Command.InCommand():
        return
    Rhino.RhinoApp.Idle -= run
    try:
        recorder.Enabled = True
        prompt.PerformClick()
        report['point_count'] = point.PersistentDataCount
        report['points'] = [str(value.Value) for value in point.PersistentData.AllData(True)]
        report['grasshopper_visible'] = Grasshopper.Instances.DocumentEditor.Visible
        report['passed'] = point.PersistentDataCount == 1 and report['grasshopper_visible'] \
            and report.get('embedded_view_clicked', False) and report.get('grasshopper_visible_at_click', False)
    except:
        report['error'] = traceback.format_exc()
    finally:
        recorder.Enabled = False
        Grasshopper.Instances.ActiveCanvas.Document = previous_document
        document.Dispose()
        menu.Dispose()
        report['pending'] = False
        save()
        Rhino.RhinoApp.WriteLine('SuperHelper GH point prompt: {0}; {1}'.format('PASS' if report['passed'] else 'FAIL', path))

Rhino.RhinoApp.Idle += run
save()
