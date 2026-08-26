using Eto.Forms;
using Grasshopper;
using Rhino;
using Rhino.ApplicationSettings;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Input.Custom;
using Rhino.UI.Controls;
using Rhino.UI.DialogPanels;
using System;
using System.Collections.Generic;
using System.Linq;
using RhinoCommand = Rhino.Commands.Command;
using RhinoCommandEventArgs = Rhino.Commands.CommandEventArgs;
using WinForms = System.Windows.Forms;

namespace SuperHelper;

public sealed partial class SuperHelperControl
{
    private bool _keepGrasshopperVisibleForCommand;

    public void ResetViewport()
    {
        var activeDocument = RhinoDoc.ActiveDoc;
        if (activeDocument == null)
        {
            return;
        }

        if (_viewport == null || _viewportDocumentSerial != activeDocument.RuntimeSerialNumber)
        {
            _viewportPane.Content = null;
            _viewport?.Dispose();
            BuildViewport();
        }

        EnsureCommandHistory();
        _viewport?.Viewport.ZoomExtents();
        _viewport?.Refresh();
    }

    private void WireCommandBar()
    {
        RefreshCommandSuggestions();
        _historyToggleButton.Click += (_, _) =>
        {
            _commandHistoryHost.Visible = !_commandHistoryHost.Visible;
            _historyToggleButton.Text = _commandHistoryHost.Visible ? "History" : "Show History";
        };
        _runCommandButton.Click += (_, _) => RunCommandFromBar();
        _commandTextBox.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Keys.Enter:
                    RunCommandFromBar();
                    e.Handled = true;
                    break;
                case Keys.Up:
                    if (string.IsNullOrWhiteSpace(_commandTextBox.Text))
                    {
                        RecallCommand(-1);
                        e.Handled = true;
                    }
                    break;
                case Keys.Down:
                    if (string.IsNullOrWhiteSpace(_commandTextBox.Text))
                    {
                        RecallCommand(1);
                        e.Handled = true;
                    }
                    break;
            }
        };
    }

    private void EnsureCommandHistory()
    {
        var document = RhinoDoc.ActiveDoc;
        if (document == null)
        {
            return;
        }

        if (_commandMirror != null)
        {
            _commandMirror.SetDocument(document);
            return;
        }

        if (RhinoCommandMirrorHost.TryCreate(document, out var mirror))
        {
            _commandMirror = mirror;
            _commandRegionHost.Content = null;
            _commandRegionHost.Content = mirror;
            _commandHistoryPanel?.Dispose();
            _commandHistoryPanel = null;
            _commandHistoryDocumentSerial = 0;
            return;
        }

        if (_commandHistoryPanel != null &&
            _commandHistoryDocumentSerial == document.RuntimeSerialNumber)
        {
            return;
        }

        if (_commandRegionHost.Content != _commandFallbackContent)
        {
            _commandRegionHost.Content = null;
            _commandRegionHost.Content = _commandFallbackContent;
        }

        _commandHistoryHost.Content = null;
        _commandHistoryPanel?.Dispose();
        _commandHistoryPanel = new CommandHistoryPanel(document);
        _commandHistoryDocumentSerial = document.RuntimeSerialNumber;
        _commandHistoryHost.Content = _commandHistoryPanel;
        UpdateCommandPrompt();
    }

    private void DisposeCommandMirror()
    {
        if (_commandMirror == null)
        {
            return;
        }

        _commandRegionHost.Content = null;
        _commandMirror.Dispose();
        _commandMirror = null;
        if (!_commandRegionHost.IsDisposed && _commandFallbackContent != null)
        {
            _commandRegionHost.Content = _commandFallbackContent;
        }
    }

    private void RunCommandFromBar()
    {
        var command = _commandTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        if (_commandEntries.Count == 0 || !string.Equals(_commandEntries[^1], command, StringComparison.Ordinal))
        {
            _commandEntries.Add(command);
        }
        _commandEntryIndex = _commandEntries.Count;
        _commandTextBox.Text = string.Empty;
        _viewport?.Activate();
        if (RhinoCommand.InCommand())
        {
            RhinoApp.SendKeystrokes(command, appendReturn: true);
        }
        else
        {
            var document = RhinoDoc.ActiveDoc;
            if (document != null)
            {
                _keepGrasshopperVisibleForCommand = true;
                if (!RhinoApp.RunScript(document.RuntimeSerialNumber, command, echo: true))
                {
                    _keepGrasshopperVisibleForCommand = false;
                }
            }
        }
        _viewport?.Activate();
    }

    private void RecallCommand(int offset)
    {
        if (_commandEntries.Count == 0)
        {
            return;
        }

        _commandEntryIndex = Math.Clamp(_commandEntryIndex + offset, 0, _commandEntries.Count);
        _commandTextBox.Text = _commandEntryIndex == _commandEntries.Count
            ? string.Empty
            : _commandEntries[_commandEntryIndex];
    }

    private void UpdateCommandPrompt()
    {
        var prompt = RhinoApp.CommandPrompt;
        _commandPromptLabel.Text = string.IsNullOrWhiteSpace(prompt) ? "Command:" : prompt;
    }

    private void RefreshCommandSuggestions()
    {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
            {
                values.Add(value);
            }
        }

        foreach (var recent in RhinoCommand.GetMostRecentCommands())
        {
            Add(recent.DisplayString);
        }
        foreach (var alias in CommandAliasList.GetNames())
        {
            Add(alias);
        }
        foreach (var name in RhinoCommand.GetCommandNames(english: false, loaded: false).OrderBy(name => name))
        {
            Add(name);
        }
        foreach (var name in RhinoCommand.GetCommandNames(english: true, loaded: false).OrderBy(name => name))
        {
            Add(name);
            Add($"_{name}");
        }

        _commandTextBox.DataStore = values;
    }

    private void UpdateCommandOptions(IReadOnlyList<CommandOptionSnapshot> options)
    {
        if (options == null || options.Count == 0)
        {
            _commandOptionsHost.Content = null;
            _commandOptionsHost.Visible = false;
            return;
        }

        var layout = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            Padding = new Eto.Drawing.Padding(4, 1),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (var option in options)
        {
            var snapshot = option;
            var button = new Button
            {
                Text = snapshot.DisplayText,
                Height = 24,
                ToolTip = string.IsNullOrWhiteSpace(snapshot.EnglishName)
                    ? snapshot.LocalName
                    : snapshot.EnglishName,
            };
            button.Click += (_, _) => SendCommandOption(snapshot);
            layout.Items.Add(button);
        }
        _commandOptionsHost.Content = layout;
        _commandOptionsHost.Visible = true;
    }

    private static CommandOptionSnapshot SnapshotCommandOption(CommandLineOption option)
    {
        var localName = option.LocalName;
        var englishName = option.EnglishName;
        var displayName = string.IsNullOrWhiteSpace(localName) ? englishName : localName;
        string value = null;
        switch (option.OptionType)
        {
            case CommandLineOptionType.Toggle:
                option.ToggleValues(english: false, out var offValue, out var onValue);
                value = option.CurrentToggleValue == true ? onValue : offValue;
                break;
            case CommandLineOptionType.List:
                var choices = option.ListOptions(english: false);
                if (option.CurrentListOptionIndex >= 0 && option.CurrentListOptionIndex < choices.Length)
                {
                    value = choices[option.CurrentListOptionIndex];
                }
                break;
            case CommandLineOptionType.Number:
                value = option.CurrentNumericValue.ToString("G");
                break;
        }
        return new CommandOptionSnapshot(
            localName,
            englishName,
            string.IsNullOrWhiteSpace(value) ? displayName : $"{displayName}={value}");
    }

    private void SendCommandOption(CommandOptionSnapshot option)
    {
        var optionName = string.IsNullOrWhiteSpace(option.EnglishName)
            ? option.LocalName
            : $"_{option.EnglishName}";
        if (!string.IsNullOrWhiteSpace(optionName))
        {
            RhinoApp.SendKeystrokes(optionName, appendReturn: true);
            _viewport?.Activate();
        }
    }

    private void BuildViewport()
    {
        var document = RhinoDoc.ActiveDoc;
        if (document == null)
        {
            return;
        }

        _viewport = new RhinoWorkViewHost(document);
        _viewportDocumentSerial = document.RuntimeSerialNumber;
        _viewportPane.Content = _viewport;
    }

    private void SubscribeDocumentEvents()
    {
        if (_documentEventsSubscribed)
        {
            return;
        }

        RhinoDoc.AddRhinoObject += OnRhinoObjectChanged;
        RhinoDoc.DeleteRhinoObject += OnRhinoObjectChanged;
        RhinoDoc.UndeleteRhinoObject += OnRhinoObjectChanged;
        RhinoDoc.ReplaceRhinoObject += OnRhinoObjectReplaced;
        RhinoDoc.ModifyObjectAttributes += OnRhinoObjectAttributesChanged;
        RhinoDoc.SelectObjects += OnRhinoObjectSelectionChanged;
        RhinoDoc.DeselectObjects += OnRhinoObjectSelectionChanged;
        RhinoDoc.DeselectAllObjects += OnRhinoObjectsDeselected;
        RhinoApp.CommandPromptChanged += OnCommandPromptChanged;
        RhinoCommand.BeginCommand += OnCommandBegan;
        RhinoCommand.EndCommand += OnCommandEnded;
        _documentEventsSubscribed = true;
    }

    private void UnsubscribeDocumentEvents()
    {
        if (!_documentEventsSubscribed)
        {
            return;
        }

        RhinoDoc.AddRhinoObject -= OnRhinoObjectChanged;
        RhinoDoc.DeleteRhinoObject -= OnRhinoObjectChanged;
        RhinoDoc.UndeleteRhinoObject -= OnRhinoObjectChanged;
        RhinoDoc.ReplaceRhinoObject -= OnRhinoObjectReplaced;
        RhinoDoc.ModifyObjectAttributes -= OnRhinoObjectAttributesChanged;
        RhinoDoc.SelectObjects -= OnRhinoObjectSelectionChanged;
        RhinoDoc.DeselectObjects -= OnRhinoObjectSelectionChanged;
        RhinoDoc.DeselectAllObjects -= OnRhinoObjectsDeselected;
        RhinoApp.CommandPromptChanged -= OnCommandPromptChanged;
        RhinoCommand.BeginCommand -= OnCommandBegan;
        RhinoCommand.EndCommand -= OnCommandEnded;
        _documentEventsSubscribed = false;
    }

    private void OnRhinoObjectChanged(object sender, RhinoObjectEventArgs e) => _viewport?.Refresh();
    private void OnRhinoObjectReplaced(object sender, RhinoReplaceObjectEventArgs e) => _viewport?.Refresh();
    private void OnRhinoObjectAttributesChanged(object sender, RhinoModifyObjectAttributesEventArgs e) => _viewport?.Refresh();
    private void OnRhinoObjectSelectionChanged(object sender, RhinoObjectSelectionEventArgs e) => _viewport?.Refresh();
    private void OnRhinoObjectsDeselected(object sender, RhinoDeselectAllObjectsEventArgs e) => _viewport?.Refresh();

    private void OnCommandPromptChanged(object sender, Rhino.UI.CommandPromptChangedEventArgs e)
    {
        var prompt = string.IsNullOrWhiteSpace(e.PromptDefault)
            ? e.Prompt
            : $"{e.Prompt} <{e.PromptDefault}>";
        var options = e.Options?
            .Where(option => option != null && option.OptionType != CommandLineOptionType.Hidden)
            .Select(SnapshotCommandOption)
            .ToArray() ?? Array.Empty<CommandOptionSnapshot>();
        void Apply()
        {
            _commandPromptLabel.Text = string.IsNullOrWhiteSpace(prompt) ? "Command:" : prompt;
            UpdateCommandOptions(options);
        }

        if (Application.Instance?.IsUIThread == true)
        {
            Apply();
        }
        else
        {
            Application.Instance?.AsyncInvoke(Apply);
        }
    }

    private void OnCommandBegan(object sender, RhinoCommandEventArgs e)
    {
        if (!_keepGrasshopperVisibleForCommand)
        {
            return;
        }

        void ActivateEmbeddedViewport()
        {
            var editor = Instances.DocumentEditor;
            if (editor != null)
            {
                if (editor.WindowState == WinForms.FormWindowState.Minimized)
                {
                    editor.WindowState = WinForms.FormWindowState.Normal;
                }
                if (!editor.Visible)
                {
                    editor.Show();
                }
                editor.Activate();
            }
            _viewport?.Activate();
        }

        if (Application.Instance?.IsUIThread == true)
        {
            ActivateEmbeddedViewport();
        }
        else
        {
            Application.Instance?.AsyncInvoke(ActivateEmbeddedViewport);
        }
    }

    private void OnCommandEnded(object sender, RhinoCommandEventArgs e)
    {
        _keepGrasshopperVisibleForCommand = false;
        void Refresh()
        {
            RefreshCommandSuggestions();
            UpdateCommandPrompt();
            UpdateCommandOptions(Array.Empty<CommandOptionSnapshot>());
        }

        if (Application.Instance?.IsUIThread == true)
        {
            Refresh();
        }
        else
        {
            Application.Instance?.AsyncInvoke(Refresh);
        }
    }

    private readonly record struct CommandOptionSnapshot(
        string LocalName,
        string EnglishName,
        string DisplayText);
}
