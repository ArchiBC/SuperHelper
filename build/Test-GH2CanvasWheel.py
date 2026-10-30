# Native pointer test, run inside Rhino with GH2 and SuperHelper loaded.
# Run once to arm. Scroll UP over the empty canvas, then run again.
# Scroll UP once more with the same wheel amount, then run a third time.
# The third run restores the original projection and preference.
import clr
import json
import hashlib
import os
import System
import Rhino
import scriptcontext as sc
clr.AddReference('Grasshopper2')
import Grasshopper2
from Eto.Drawing import PointF
from Grasshopper2.UI.Flex import Projection
from System.Reflection import BindingFlags

flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
key = 'SuperHelper.GH2.NativeWheel'
path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'artifacts', 'gh2-native-wheel.json')
assembly = next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == 'SuperHelper.GH2')
preference = assembly.GetType('SuperHelper.SuperHelperPreferences').GetProperty('ReverseCanvasWheelZoom', flags)
canvas = Grasshopper2.UI.Editor.Instance.Canvas

def gh1_preference():
    gh1 = next((a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == 'SuperHelper'), None)
    if gh1 is None: return None
    return bool(gh1.GetType('SuperHelper.SuperHelperPreferences').GetProperty('ReverseCanvasWheelZoom',flags).GetValue(None,None))

def save(report):
    with open(path,'w') as stream: json.dump(report,stream,indent=2)

def camera():
    workspace = assembly.GetType('SuperHelper.SuperHelperGh2Plugin').GetField('_workspace',flags).GetValue(None)
    control = workspace.GetType().GetProperty('Control',flags).GetValue(workspace,None)
    panel = control.GetType().GetField('_viewportPanel',flags).GetValue(control)
    host = panel.GetType().GetField('_viewport',flags).GetValue(panel)
    vp = host.GetType().GetProperty('Viewport',flags).GetValue(host,None)
    return [float(v) for p in [vp.CameraLocation,vp.CameraTarget,vp.CameraDirection] for v in [p.X,p.Y,p.Z]]

if key not in sc.sticky:
    state = {'projection':canvas.Projection,'preference':preference.GetValue(None,None),
             'camera':camera(),'scale':float(Rhino.ApplicationSettings.ViewSettings.ZoomScale),'gh1_preference':gh1_preference()}
    with open(assembly.Location, 'rb') as stream: plugin_hash = hashlib.sha256(stream.read()).hexdigest()
    state['report']={'pending':True,'passed':False,'rhino':str(Rhino.RhinoApp.Version),'samples':[],
                     'tested_at': str(System.DateTime.Now.ToString('o')), 'pluginSha256': plugin_hash}
    sc.sticky[key]=state
    gh1 = next((a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == 'Grasshopper'), None)
    state['gh1_editor'] = gh1.GetType('Grasshopper.Instances').GetProperty('DocumentEditor').GetValue(None,None) if gh1 else None
    if state['gh1_editor'] is not None:
        state['gh1_visible'] = state['gh1_editor'].Visible
        state['gh1_editor'].Visible = False
    Grasshopper2.UI.Editor.Instance.Visible = True
    Grasshopper2.UI.Editor.Instance.BringToFront()
    Grasshopper2.UI.Editor.Instance.Focus()
    preference.SetValue(None,False,None)
    canvas.Projection=Projection(PointF(50,60),System.Single(2.0))
    state['report']['phase']='native direction: scroll UP over the canvas, then run again'
else:
    state=sc.sticky[key]
    state['report']['samples'].append({'zoom':float(canvas.Projection.Zoom),
        'reverse':bool(preference.GetValue(None,None)),
        'origin':[float(canvas.Projection.Origin.X),float(canvas.Projection.Origin.Y)],
        'rhino_scale':float(Rhino.ApplicationSettings.ViewSettings.ZoomScale),'camera':camera(),'gh1_preference':gh1_preference()})
    if len(state['report']['samples'])==1:
        preference.SetValue(None,True,None)
        canvas.Projection=Projection(PointF(50,60),System.Single(2.0))
        state['report']['phase']='reverse direction: scroll UP by the same amount, then run again'
    else:
        samples=state['report']['samples']
        state['report']['checks']=[
            {'name':'Same native wheel gesture reverses GH2 zoom','passed':(samples[0]['zoom']-2.0)*(samples[1]['zoom']-2.0)<0},
            {'name':'Rhino zoom setting is unchanged','passed':all(s['rhino_scale']==state['scale'] for s in samples)},
            {'name':'Embedded Rhino camera is unchanged','passed':all(all(abs(x-y)<1e-6 for x,y in zip(s['camera'],state['camera'])) for s in samples)},
            {'name':'GH1 reverse preference is isolated','passed':all(s['gh1_preference']==state['gh1_preference'] for s in samples)},
        ]
        preference.SetValue(None,state['preference'],None)
        canvas.Projection=state['projection']
        if state['gh1_editor'] is not None:
            state['gh1_editor'].Visible = state['gh1_visible']
        state['report']['pending']=False
        state['report']['passed']=all(c['passed'] for c in state['report']['checks'])
        state['report']['phase']='finished; original setting and projection restored'
        del sc.sticky[key]
save(state['report'])
Rhino.RhinoApp.WriteLine('SuperHelper GH2 wheel: '+state['report']['phase'])
