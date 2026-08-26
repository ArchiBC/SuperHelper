using Eto.Drawing;
using Eto.Forms;
using Eto.Serialization.Xaml;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino;
using Rhino.UI.Controls;
using Rhino.UI.DialogPanels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using EtoSlider = Eto.Forms.Slider;

namespace SuperHelper;

/// <summary>
/// Cross-platform SuperHelper shell. Static layout lives in SuperHelperControl.xeto;
/// feature behavior is grouped in the adjacent partial-class files.
/// </summary>
public sealed partial class SuperHelperControl : Panel
{
    private readonly System.Drawing.Color _wireColorDefault = System.Drawing.Color.DarkBlue;
    private readonly System.Drawing.Color _materialColorDefault = System.Drawing.Color.DarkBlue;

    // XAML name-scope fields.
    private TabControl _tabs = null!;
    private Panel _workspaceContentHost = null!;
    private StackLayout _sectionStaging = null!;
    private Panel _commandSection = null!;
    private Panel _viewportSection = null!;
    private Panel _helpSection = null!;
    private Panel _viewportPane = null!;
    private Panel _commandRegionHost = null!;
    private Panel _commandHistoryHost = null!;
    private Panel _commandOptionsHost = null!;
    private Button _historyToggleButton = null!;
    private Label _commandPromptLabel = null!;
    private ComboBox _commandTextBox = null!;
    private Button _runCommandButton = null!;

    // Command and viewport state.
    private readonly List<string> _commandEntries = new();
    private Control _commandFallbackContent;
    private RhinoCommandMirrorHost _commandMirror;
    private CommandHistoryPanel _commandHistoryPanel;
    private uint _commandHistoryDocumentSerial;
    private int _commandEntryIndex;
    private RhinoWorkViewHost _viewport;
    private uint _viewportDocumentSerial;
    private bool _documentEventsSubscribed;

    // Help pages.
    private readonly WebView _originHelp = new();
    private readonly WebView _webHelp = new();
    private readonly TextBox _urlTextBox = new();
    private readonly DropDown _webHelpSource = new();
    private long _originHelpRevision;

    // Object, parameter, and development pages.
    private readonly ImageView _objectIcon = new() { Width = 24, Height = 24 };
    private readonly Label _objectTitle = new()
    {
        Font = SystemFonts.Bold(16f),
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Label _objectInstanceDescription = new()
    {
        TextAlignment = TextAlignment.Center,
        Wrap = WrapMode.Word,
    };
    private readonly Label _objectDescription = new() { Wrap = WrapMode.Word };
    private readonly Label _runtimeMessages = new();
    private readonly Panel _parameterContent = new();
    private readonly Label _developmentText = new()
    {
        TextAlignment = TextAlignment.Center,
        Wrap = WrapMode.Word,
    };
    private readonly Button _assemblyLocationButton = new();

    // Examples page.
    private readonly ObservableCollection<HelpExample> _examples = new();
    private readonly GridView _exampleGrid = new();
    private readonly Panel _exampleContent = new();
    private readonly StackLayout _exampleCards = new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 3,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };
    private bool _exampleEditMode;

    // File manager page.
    private readonly TextBox _directoryTextBox = new();
    private readonly CheckBox _openDocumentCheckBox = new()
    {
        Text = "Open Doc",
        ToolTip = "Open document when right clicking a file.",
    };
    private readonly TreeGridView _directoryTree = new();

    // Settings page.
    private Scrollable _settingsScroll;
    private readonly CheckBox _autoTargetCheckBox = new();
    private readonly CheckBox _commandVisibleCheckBox = new() { Text = "Command" };
    private readonly CheckBox _viewportVisibleCheckBox = new() { Text = "Viewport" };
    private readonly CheckBox _helpVisibleCheckBox = new() { Text = "Help" };
    private bool _sectionVisibilityChanging;
    private readonly EtoSlider _highlightWidthSlider = new() { MinValue = 1, MaxValue = 15 };
    private readonly EtoSlider _exampleHeightSlider = CreateSlider(20, 150, 55);
    private readonly EtoSlider _fileHeightSlider = CreateSlider(20, 150, 55);
    private GH_DocumentObject _currentObject;

    public SuperHelperControl()
    {
        XamlReader.Load(this);
        _commandFallbackContent = _commandRegionHost.Content;
        WireCommandBar();
        BuildViewport();
        BuildTabs();
        InitializeWorkspaceLayout();

        Load += (_, _) =>
        {
            SubscribeDocumentEvents();
            EnsureCommandHistory();
            ResetViewport();
        };
        UnLoad += (_, _) =>
        {
            UnsubscribeDocumentEvents();
            DisposeCommandMirror();
        };
    }

    public int SelectedTabIndex => GetSelectedHelpPageIndex();

    public GH_DocumentObject CurrentObject => _currentObject;

    public bool AutoTarget
    {
        get => Instances.Settings.GetValue(nameof(AutoTarget), true);
        set => Instances.Settings.SetValue(nameof(AutoTarget), value);
    }

    public string FileManageWorkDirectorey
    {
        get => Instances.Settings.GetValue(nameof(FileManageWorkDirectorey), "Please enter folder.");
        set => Instances.Settings.SetValue(nameof(FileManageWorkDirectorey), value ?? string.Empty);
    }

    public bool OpenDocumentWhenRightClick
    {
        get => Instances.Settings.GetValue(nameof(OpenDocumentWhenRightClick), false);
        set => Instances.Settings.SetValue(nameof(OpenDocumentWhenRightClick), value);
    }

    public int DisplayWireWidth
    {
        get => Instances.Settings.GetValue(nameof(DisplayWireWidth), 1);
        set
        {
            Instances.Settings.SetValue(nameof(DisplayWireWidth), value);
            RedrawRhinoViews();
        }
    }

    public System.Drawing.Color WireColor
    {
        get => Instances.Settings.GetValue(nameof(WireColor), _wireColorDefault);
        set
        {
            Instances.Settings.SetValue(nameof(WireColor), value);
            RedrawRhinoViews();
        }
    }

    public System.Drawing.Color MaterialColor
    {
        get => Instances.Settings.GetValue(nameof(MaterialColor), _materialColorDefault);
        set
        {
            Instances.Settings.SetValue(nameof(MaterialColor), value);
            RedrawRhinoViews();
        }
    }

    public void SetObject(GH_DocumentObject obj, string html, string webUrl, IEnumerable<string> examplePaths)
    {
        if (obj == null)
        {
            return;
        }

        SaveExampleValue();
        var componentChanged = _currentObject?.ComponentGuid != obj.ComponentGuid;
        _currentObject = obj;

        // Object/parameter information is the primary auto-target response.
        // Update it before any WebView, example, or reflected panel operation
        // so an unrelated page failure cannot leave stale component details.
        RefreshObjectData();
        UpdatePageSafely(() => UpdateOriginHelp(html), "Origin Help");
        UpdatePageSafely(() => UpdateWebHelp(webUrl, componentChanged), "Web Help");
        UpdatePageSafely(() => UpdateExamples(examplePaths), "Examples");
    }

    private static void UpdatePageSafely(Action update, string pageName)
    {
        try
        {
            update();
        }
        catch (Exception exception)
        {
            RhinoApp.WriteLine($"SuperHelper {pageName} update failed: {exception.Message}");
        }
    }

    public void OnClosed()
    {
        HighLightConduit.HighLightObject = null;
        RedrawRhinoViews();
    }

    private void BuildTabs()
    {
        _tabs.Pages.Add(new TabPage { Text = "Origin Help", Content = _originHelp });
        _tabs.Pages.Add(new TabPage { Text = "Web Help", Content = BuildWebHelpPage() });
        _tabs.Pages.Add(new TabPage { Text = "Example", Content = BuildExamplePage() });
        _tabs.Pages.Add(new TabPage { Text = "Params Help", Content = BuildParameterPage() });
        _tabs.Pages.Add(new TabPage { Text = "Development Help", Content = BuildDevelopmentPage() });
        _tabs.Pages.Add(new TabPage { Text = "File Manager", Content = BuildFileManagerPage() });
        _tabs.Pages.Add(new TabPage { Text = "Settings", Content = BuildSettingsPage() });
        _tabs.SelectedIndex = 5;

        ConfigureWebView(_originHelp);
        ConfigureWebView(_webHelp);
        _urlTextBox.Text = "http://100-gh.com/";
        _webHelp.Url = new Uri(_urlTextBox.Text);
    }

    private static Eto.Drawing.Image ToEtoImage(System.Drawing.Bitmap bitmap) =>
        bitmap == null ? null : Rhino.UI.EtoExtensions.ToEto(bitmap);

    private static EtoSlider CreateSlider(int minimum, int maximum, int value) => new()
    {
        MinValue = minimum,
        MaxValue = maximum,
        Value = value,
    };

    private static void RedrawRhinoViews()
    {
        RhinoDoc.ActiveDoc?.Views.Redraw();
    }
}
