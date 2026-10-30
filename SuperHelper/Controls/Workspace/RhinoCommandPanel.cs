using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.ApplicationSettings;
using Rhino.Input.Custom;
using Rhino.UI.Controls;
using Rhino.UI.DialogPanels;
using System;
using System.Collections.Generic;
using System.Linq;
using RhinoCommand = Rhino.Commands.Command;
using RhinoCommandEventArgs = Rhino.Commands.CommandEventArgs;

namespace SuperHelper;

internal sealed class RhinoCommandPanel : Panel
{
    private readonly RhinoViewportPanel _viewport;
    private readonly Panel _commandRegionHost = new();
    private readonly Panel _commandHistoryHost = new();
    private readonly Panel _commandOptionsHost = new() { Visible = false };
    private readonly Button _historyToggleButton = new() { Text = "History", Width = 58, Height = 24 };
    private readonly Label _commandPromptLabel = new() { Text = "Command:", VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _commandTextBox = new() { AutoComplete = true, ReadOnly = false };
    private readonly Button _runCommandButton = new() { Text = "Run", Width = 42, Height = 24 };
    private readonly List<string> _commandEntries = new();
    private Control _commandFallbackContent;
    private RhinoCommandMirrorHost _commandMirror;
    private uint _commandMirrorDocumentSerial;
    private CommandHistoryPanel _commandHistoryPanel;
    private uint _commandHistoryDocumentSerial;
    private int _commandEntryIndex;
    private bool _eventsSubscribed;

    public RhinoCommandPanel(RhinoViewportPanel viewport)
    {
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        Content = _commandRegionHost;
    }

    public void Start()
    {
        SubscribeEvents();
        ResetDocument();
    }

    public void Stop()
    {
        UnsubscribeEvents();
    }

    public void Shutdown()
    {
        Stop();
        DisposeCommandMirror();
        _commandHistoryHost.Content = null;
        _commandHistoryPanel?.Dispose();
        _commandHistoryPanel = null;
        _commandHistoryDocumentSerial = 0;
    }

    public void ResetDocument()
    {
        EnsureCommandHistory();
        UpdateCommandPrompt();
    }

    private Control BuildFallbackContent()
    {
        var commandBar = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Padding = new Padding(4, 1),
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                _historyToggleButton,
                _commandPromptLabel,
                new StackLayoutItem(_commandTextBox, true),
                _runCommandButton,
            },
        };
        return new GroupBox
        {
            Text = "Command History",
            Padding = Padding.Empty,
            Content = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 1,
                Items =
                {
                    new StackLayoutItem(_commandHistoryHost, true),
                    commandBar,
                    _commandOptionsHost,
                },
            },
        };
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
                case Keys.Up when string.IsNullOrWhiteSpace(_commandTextBox.Text):
                    RecallCommand(-1);
                    e.Handled = true;
                    break;
                case Keys.Down when string.IsNullOrWhiteSpace(_commandTextBox.Text):
                    RecallCommand(1);
                    e.Handled = true;
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

        if (_commandMirror != null && _commandMirrorDocumentSerial == document.RuntimeSerialNumber)
        {
            _commandMirror.SetDocument(document);
            return;
        }
        DisposeCommandMirror();

        if (RhinoCommandMirrorHost.TryCreate(document, out var mirror))
        {
            _commandMirror = mirror;
            _commandMirrorDocumentSerial = document.RuntimeSerialNumber;
            _commandRegionHost.Content = null;
            _commandRegionHost.Content = mirror;
            _commandHistoryHost.Content = null;
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

        if (_commandFallbackContent == null)
        {
            _commandFallbackContent = BuildFallbackContent();
            WireCommandBar();
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
        _commandMirrorDocumentSerial = 0;
    }

    private void RunCommandFromBar()
    {
        var command = _commandTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        if (_commandEntries.Count == 0 ||
            !string.Equals(_commandEntries[^1], command, StringComparison.Ordinal))
        {
            _commandEntries.Add(command);
        }
        _commandEntryIndex = _commandEntries.Count;
        _commandTextBox.Text = string.Empty;
        _viewport.Activate();

        if (RhinoCommand.InCommand())
        {
            RhinoApp.SendKeystrokes(command, appendReturn: true);
        }
        else if (RhinoDoc.ActiveDoc is { } document)
        {
            _viewport.PrepareForCommand();
            if (!RhinoApp.RunScript(document.RuntimeSerialNumber, command, echo: true))
            {
                _viewport.CancelPreparedCommand();
            }
        }

        _viewport.Activate();
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
        if (_commandMirror != null)
        {
            return;
        }
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
            Padding = new Padding(4, 1),
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
            _viewport.Activate();
        }
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
        if (_commandMirror != null)
        {
            return;
        }
        var prompt = string.IsNullOrWhiteSpace(e.PromptDefault)
            ? e.Prompt
            : $"{e.Prompt} <{e.PromptDefault}>";
        var options = e.Options?
            .Where(option => option != null && option.OptionType != CommandLineOptionType.Hidden)
            .Select(SnapshotCommandOption)
            .ToArray() ?? Array.Empty<CommandOptionSnapshot>();
        InvokeOnEtoUi(() =>
        {
            _commandPromptLabel.Text = string.IsNullOrWhiteSpace(prompt) ? "Command:" : prompt;
            UpdateCommandOptions(options);
        });
    }

    private void OnCommandEnded(object sender, RhinoCommandEventArgs e) => InvokeOnEtoUi(() =>
    {
        if (_commandMirror != null)
        {
            return;
        }
        RefreshCommandSuggestions();
        UpdateCommandPrompt();
        UpdateCommandOptions(Array.Empty<CommandOptionSnapshot>());
    });

    private static void InvokeOnEtoUi(Action action)
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

    private readonly record struct CommandOptionSnapshot(
        string LocalName,
        string EnglishName,
        string DisplayText);
}
