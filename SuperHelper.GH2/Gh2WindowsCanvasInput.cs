using Eto.Drawing;
using Eto.Forms;
using Grasshopper2.UI;
using Grasshopper2.UI.Canvas;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SuperHelper;

/// <summary>Reverses Windows wheel messages only over this GH2 canvas.</summary>
internal sealed class Gh2WindowsCanvasInput : IDisposable
{
    private readonly Canvas _canvas;
    private readonly Editor _editor;
    private readonly HookProc _callback;
    private IntPtr _hook;
    public Gh2WindowsCanvasInput(Editor editor, Canvas canvas)
    {
        _editor = editor;
        _canvas = canvas;
        _callback = Filter;
        // WH_GETMESSAGE is confined to Rhino's current UI thread, with no global hook.
        _hook = SetWindowsHookEx(3, _callback, IntPtr.Zero, GetCurrentThreadId());
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private IntPtr Filter(int code, IntPtr removed, IntPtr pointer)
    {
        if (code >= 0 && removed != IntPtr.Zero && SuperHelperPreferences.ReverseCanvasWheelZoom
            && _canvas.Loaded && _canvas.Visible && !_editor.IsDisposed && _editor.Visible && _canvas.AllowZoom)
        {
            var message = Marshal.PtrToStructure<NativeMessage>(pointer);
            if (message.Message == 0x020A)
            {
                var screen = new PointF((short)(message.LParam.ToInt64() & 0xffff), (short)((message.LParam.ToInt64() >> 16) & 0xffff));
                var root = GetAncestor(_editor.NativeHandle, 2);
                if (root != IntPtr.Zero && GetAncestor(message.Window, 2) == root && GetAncestor(WindowFromPoint(new NativePoint((int)screen.X, (int)screen.Y)), 2) == root)
                {
                    var local = _canvas.PointFromScreen(screen);
                    if (new RectangleF(0, 0, _canvas.Width, _canvas.Height).Contains(local))
                        Marshal.WriteIntPtr(pointer, Marshal.OffsetOf<NativeMessage>(nameof(NativeMessage.WParam)).ToInt32(), ReverseDelta(message.WParam));
                }
            }
        }
        return CallNextHookEx(_hook, code, removed, pointer);
    }
    internal static IntPtr ReverseDelta(IntPtr value)
    {
        var raw = value.ToInt64();
        var delta = (short)((raw >> 16) & 0xffff);
        var reversed = (ushort)(short)Math.Clamp(-delta, short.MinValue, short.MaxValue);
        return new IntPtr((raw & ~0xffff0000L) | ((long)reversed << 16));
    }
    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        GC.KeepAlive(_callback);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
