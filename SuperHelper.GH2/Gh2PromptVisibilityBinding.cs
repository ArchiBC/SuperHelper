using Eto.Forms;
using Grasshopper2.UI;
using Rhino;
using System;
using System.Reflection;

namespace SuperHelper;

internal sealed class Gh2PromptVisibilityBinding : IDisposable
{
    private static readonly FieldInfo GetterTimer = typeof(Editor).GetField("_getterTimer", BindingFlags.Static | BindingFlags.NonPublic);
    private readonly Editor _editor;
    private readonly EventHandler _changed;
    private readonly UITimer _watch = new() { Interval = 0.05 };
    private UITimer _suppressed;
    public static bool GetterScheduled => GetterTimer?.GetValue(null) != null;
    public Gh2PromptVisibilityBinding(Editor editor, EventHandler changed)
    {
        _editor = editor;
        _changed = changed;
        _watch.Elapsed += Tick;
        _watch.Start();
    }
    private void Tick(object sender, EventArgs args)
    {
        if (_editor == null || _editor.IsDisposed) return;
        if (Rhino.Input.RhinoGet.InGet(RhinoDoc.ActiveDoc))
        {
            if (GetterTimer?.GetValue(null) is UITimer native)
            {
                // GH2's getter timer otherwise hides the editor every 100 ms. Pause
                // this one timer while its getter uses our embedded viewport.
                native.Stop();
                _suppressed = native;
            }
            _changed(_editor, args);
        }
        else if (_suppressed != null)
        {
            if (ReferenceEquals(GetterTimer?.GetValue(null), _suppressed))
            {
                GetterTimer.SetValue(null, null);
                _suppressed.Dispose();
            }
            _suppressed = null;
            HelperHost.Current.RestoreEditor();
        }
    }
    public void Dispose()
    {
        _watch.Stop();
        _watch.Elapsed -= Tick;
        _watch.Dispose();
        // Hiding the workspace during a prompt returns control to GH2's own timer.
        if (_suppressed != null && ReferenceEquals(GetterTimer?.GetValue(null), _suppressed)) _suppressed.Start();
        _suppressed = null;
    }
}
