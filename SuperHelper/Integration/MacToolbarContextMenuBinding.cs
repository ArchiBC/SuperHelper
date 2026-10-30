using Rhino;
using System;
using System.Reflection;
using System.Windows.Forms;

namespace SuperHelper;

/// <summary>Attaches the shared WinForms menu to RhinoMac's native toolbar button.</summary>
internal sealed class MacToolbarContextMenuBinding : IDisposable
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly ToolStripButton _button;
    private readonly ContextMenuStrip _menu;
    private readonly object _view;
    private readonly PropertyInfo _menuProperty;
    private readonly object _previousMenu;
    private bool _disposed;

    private MacToolbarContextMenuBinding(ToolStripButton button, ContextMenuStrip menu)
    {
        _button = button;
        _menu = menu;
        _view = typeof(ToolStripItem).GetMethod("GetToolView", InstanceFlags).Invoke(button, null);
        _menuProperty = _view.GetType().GetProperty("Menu", InstanceFlags);
        _previousMenu = _menuProperty.GetValue(_view);
        var nativeMenu = typeof(ToolStripDropDown).GetProperty("NativeMenu", InstanceFlags).GetValue(menu);
        _menuProperty.SetValue(_view, nativeMenu);
        button.Disposed += OnButtonDisposed;
    }

    public static IDisposable TryCreate(ToolStripButton button, Func<ContextMenuStrip> createMenu)
    {
        var menu = createMenu();
        try { return new MacToolbarContextMenuBinding(button, menu); }
        catch (Exception exception)
        {
            menu.Dispose();
            RhinoApp.WriteLine("SuperHelper Mac toolbar menu: " + exception.GetBaseException().Message);
            return null;
        }
    }

    private void OnButtonDisposed(object sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _button.Disposed -= OnButtonDisposed;
        // ToolStripItem already retires its NSView when it raises Disposed.
        if (!_button.IsDisposed) _menuProperty.SetValue(_view, _previousMenu);
        _menu.Dispose();
    }
}
