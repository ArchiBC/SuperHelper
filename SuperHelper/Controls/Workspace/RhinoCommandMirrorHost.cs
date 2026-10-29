using Eto.Forms;
using Rhino;
using Rhino.Input.Custom;
using Rhino.UI.DialogPanels;
using System;
using System.Linq;
using System.Reflection;
using RhinoCommand = Rhino.Commands.Command;
using RhinoCommandEventArgs = Rhino.Commands.CommandEventArgs;

namespace SuperHelper;

/// <summary>
/// Hosts a second instance of Rhino's Eto command-history dock-bar content.
/// Rhino only updates its registered command bar, so prompt updates are mirrored
/// explicitly while the original Rhino command bar remains in place.
/// </summary>
internal sealed class RhinoCommandMirrorHost : Panel
{
    private const string CommandHistoryDockBarTypeName =
        "Rhino.UI.Internal.DockBars.CommandBars.CommandHistoryDockBar";
    private const string CommandInfoTypeName =
        "Rhino.UI.Internal.DockBars.CommandBars.CommandInfo";

    private readonly object _dockBar;
    private readonly Type _commandInfoType;
    private readonly ConstructorInfo _commandInfoConstructor;
    private readonly MethodInfo _buildUiMethod;
    private readonly MethodInfo _endCommandMethod;
    private readonly object _commandDockBar;
    private readonly IDisposable _commandInputBinding;
    private bool _eventsSubscribed;

    private RhinoCommandMirrorHost(
        Control dockBarControl,
        object dockBar,
        Type commandInfoType,
        ConstructorInfo commandInfoConstructor,
        object commandDockBar,
        MethodInfo buildUiMethod,
        MethodInfo endCommandMethod)
    {
        _dockBar = dockBar;
        _commandInfoType = commandInfoType;
        _commandInfoConstructor = commandInfoConstructor;
        _commandDockBar = commandDockBar;
        _buildUiMethod = buildUiMethod;
        _endCommandMethod = endCommandMethod;
        Content = dockBarControl;
        if (commandDockBar.GetType().GetProperty("CommandLineInput",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(commandDockBar) is TextBox input)
        {
            _commandInputBinding = WorkspacePlatform.Current.BindCommandInput(input);
        }
        SubscribeEvents();
    }

    public static bool TryCreate(RhinoDoc document, out RhinoCommandMirrorHost host)
    {
        host = null;
        if (document == null)
        {
            return false;
        }

        try
        {
            var assembly = typeof(CommandHistoryPanel).Assembly;
            var dockBarType = assembly.GetType(CommandHistoryDockBarTypeName, throwOnError: true);
            var commandInfoType = assembly.GetType(CommandInfoTypeName, throwOnError: true);
            var dockBar = Activator.CreateInstance(
                dockBarType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object[] { RhinoWindowRuntime.SerialNumber(assembly, document) },
                culture: null);
            if (dockBar is not Control dockBarControl)
            {
                return false;
            }

            SetHistoryDocument(dockBarType, dockBar, document.RuntimeSerialNumber);
            var implementation = dockBarType
                .GetField("_impl", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(dockBar);
            var commandDockBar = implementation?
                .GetType()
                .GetProperty("CommandDockBar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(implementation);
            if (commandDockBar == null)
            {
                dockBarControl.Dispose();
                return false;
            }

            var commandInfoConstructor = commandInfoType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(string), typeof(string), typeof(CommandLineOption[]) },
                modifiers: null);
            var buildUiMethod = commandDockBar.GetType().GetMethod(
                "BuildUI",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                new[] { commandInfoType },
                modifiers: null);
            var endCommandMethod = commandDockBar.GetType().GetMethod(
                "EndCommand",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);
            if (commandInfoConstructor == null || buildUiMethod == null || endCommandMethod == null)
            {
                dockBarControl.Dispose();
                return false;
            }

            host = new RhinoCommandMirrorHost(
                dockBarControl,
                dockBar,
                commandInfoType,
                commandInfoConstructor,
                commandDockBar,
                buildUiMethod,
                endCommandMethod);
            return true;
        }
        catch
        {
            host?.Dispose();
            host = null;
            return false;
        }
    }

    public void SetDocument(RhinoDoc document)
    {
        if (document == null)
        {
            return;
        }

        SetHistoryDocument(_dockBar.GetType(), _dockBar, document.RuntimeSerialNumber);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _commandInputBinding?.Dispose();
            UnsubscribeEvents();
        }
        base.Dispose(disposing);
    }

    private static void SetHistoryDocument(Type dockBarType, object dockBar, uint documentSerialNumber)
    {
        var history = dockBarType
            .GetField("_commandHistoryControl", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(dockBar);
        history?.GetType()
            .GetMethod("SetDocument", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.Invoke(history, new object[] { documentSerialNumber });
    }

    private void SubscribeEvents()
    {
        if (_eventsSubscribed)
        {
            return;
        }

        RhinoApp.CommandPromptChanged += OnCommandPromptChanged;
        RhinoCommand.EndCommand += OnCommandEnded;
        _eventsSubscribed = true;
    }

    private void UnsubscribeEvents()
    {
        if (!_eventsSubscribed)
        {
            return;
        }

        RhinoApp.CommandPromptChanged -= OnCommandPromptChanged;
        RhinoCommand.EndCommand -= OnCommandEnded;
        _eventsSubscribed = false;
    }

    private void OnCommandPromptChanged(object sender, Rhino.UI.CommandPromptChangedEventArgs e)
    {
        object commandInfo;
        try
        {
            commandInfo = _commandInfoConstructor.Invoke(new object[]
            {
                e.Prompt,
                e.PromptDefault,
                e.Options?.Where(option => option != null).ToArray() ?? Array.Empty<CommandLineOption>(),
            });
        }
        catch
        {
            return;
        }

        InvokeOnUiThread(() =>
        {
            if (!IsDisposed && _commandInfoType.IsInstanceOfType(commandInfo))
            {
                _buildUiMethod.Invoke(_commandDockBar, new[] { commandInfo });
            }
        });
    }

    private void OnCommandEnded(object sender, RhinoCommandEventArgs e)
    {
        InvokeOnUiThread(() =>
        {
            if (!IsDisposed)
            {
                _endCommandMethod.Invoke(_commandDockBar, null);
            }
        });
    }

    private static void InvokeOnUiThread(Action action)
    {
        if (Application.Instance?.IsUIThread == true)
        {
            action();
        }
        else
        {
            Application.Instance?.AsyncInvoke(action);
        }
    }
}
