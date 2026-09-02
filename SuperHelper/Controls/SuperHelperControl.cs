using Eto.Forms;
using Eto.Serialization.Xaml;
using Grasshopper.Kernel;
using Rhino;
using System;
using System.Collections.Generic;

namespace SuperHelper;

/// <summary>
/// Coordinates independent feature controls and exposes the small API used by
/// the Grasshopper integration layer.
/// </summary>
public sealed class SuperHelperControl : Panel
{
    // XAML staging hosts. Feature controls own all of their own UI and state.
    private TabControl _tabs = null!;
    private Panel _workspaceContentHost = null!;
    private StackLayout _sectionStaging = null!;
    private Panel _commandSection = null!;
    private Panel _viewportSection = null!;
    private Panel _helpSection = null!;

    private readonly RhinoViewportPanel _viewportPanel;
    private readonly RhinoCommandPanel _commandPanel;
    private readonly OriginHelpPanel _originHelpPanel;
    private readonly WebHelpPanel _webHelpPanel;
    private readonly ExamplesPanel _examplesPanel;
    private readonly ObjectInfoPanel _objectInfoPanel;
    private readonly DevelopmentPanel _developmentPanel;
    private readonly FileManagerPanel _fileManagerPanel;
    private readonly SettingsPanel _settingsPanel;
    private readonly WorkspaceLayoutController _workspaceLayout;
    private GH_DocumentObject _currentObject;

    public SuperHelperControl()
    {
        XamlReader.Load(this, "SuperHelper.Controls.SuperHelperControl.xeto");

        _viewportPanel = new RhinoViewportPanel();
        _commandPanel = new RhinoCommandPanel(_viewportPanel);
        _originHelpPanel = new OriginHelpPanel();
        _webHelpPanel = new WebHelpPanel();
        _examplesPanel = new ExamplesPanel();
        _objectInfoPanel = new ObjectInfoPanel();
        _developmentPanel = new DevelopmentPanel();
        _fileManagerPanel = new FileManagerPanel();
        _settingsPanel = new SettingsPanel();

        _commandSection.Content = _commandPanel;
        _viewportSection.Content = _viewportPanel;
        BuildHelpPages();

        _workspaceLayout = new WorkspaceLayoutController(
            this,
            _workspaceContentHost,
            _sectionStaging,
            _commandSection,
            _viewportSection,
            _helpSection,
            _tabs,
            _settingsPanel);

        Load += (_, _) => StartFeatureControls();
        UnLoad += (_, _) => StopFeatureControls();
    }

    public int SelectedTabIndex => _workspaceLayout.SelectedHelpIndex;

    public GH_DocumentObject CurrentObject => _currentObject;

    public bool AutoTarget
    {
        get => SuperHelperPreferences.AutoTarget;
        set => SuperHelperPreferences.AutoTarget = value;
    }

    public string FileManageWorkDirectorey
    {
        get => SuperHelperPreferences.FileManagerDirectory;
        set => SuperHelperPreferences.FileManagerDirectory = value;
    }

    public bool OpenDocumentWhenRightClick
    {
        get => SuperHelperPreferences.OpenDocumentWhenRightClick;
        set => SuperHelperPreferences.OpenDocumentWhenRightClick = value;
    }

    public int DisplayWireWidth
    {
        get => SuperHelperPreferences.DisplayWireWidth;
        set => SuperHelperPreferences.DisplayWireWidth = value;
    }

    public System.Drawing.Color WireColor
    {
        get => SuperHelperPreferences.WireColor;
        set => SuperHelperPreferences.WireColor = value;
    }

    public System.Drawing.Color MaterialColor
    {
        get => SuperHelperPreferences.MaterialColor;
        set => SuperHelperPreferences.MaterialColor = value;
    }

    public void SetObject(
        GH_DocumentObject obj,
        string html,
        string webUrl,
        IEnumerable<string> examplePaths)
    {
        if (obj == null)
        {
            return;
        }

        _examplesPanel.Save();
        var componentChanged = _currentObject?.ComponentGuid != obj.ComponentGuid;
        _currentObject = obj;

        RefreshObjectData();
        UpdatePageSafely(() => _originHelpPanel.SetHtml(html), "Origin Help");
        UpdatePageSafely(() => _webHelpPanel.SetObject(obj, webUrl, componentChanged), "Web Help");
        UpdatePageSafely(() => _examplesPanel.SetExamples(obj, examplePaths), "Examples");
    }

    public void RefreshObjectData()
    {
        _objectInfoPanel.SetObject(_currentObject);
        _developmentPanel.SetObject(_currentObject);
        _examplesPanel.SetCurrentObject(_currentObject);
    }

    public void SaveExampleValue() => _examplesPanel.Save();

    public void ResetViewport()
    {
        _viewportPanel.ResetViewport();
        _commandPanel.ResetDocument();
    }

    public void OnClosed()
    {
        HighLightConduit.HighLightObject = null;
        RhinoDoc.ActiveDoc?.Views.Redraw();
    }

    internal void OnWorkspaceShown()
    {
        StartFeatureControls();
        _viewportPanel.OnWorkspaceShown();
    }

    internal void OnWorkspaceHidden() => _viewportPanel.OnWorkspaceHidden();

    internal void OpenWorkspaceSettings() => _workspaceLayout.OpenSettings();

    internal bool IsWorkspaceSectionVisible(EmbeddedWorkspaceSection section) =>
        _workspaceLayout.IsSectionVisible(section);

    internal void SetWorkspaceSectionVisible(EmbeddedWorkspaceSection section, bool visible) =>
        _workspaceLayout.SetSectionVisible(section, visible);

    internal void RestoreBuiltInWorkspace() => _workspaceLayout.RestoreBuiltIn();

    internal void UseCurrentWorkspaceAsDefault() => _workspaceLayout.UseCurrentAsDefault();

    internal void RestoreDefaultWorkspace() => _workspaceLayout.RestoreDefault();

    public void ShutdownWorkspace()
    {
        StopFeatureControls();
        _commandPanel.Shutdown();
        _viewportPanel.Shutdown();
        _examplesPanel.Shutdown();
        _workspaceLayout.Shutdown();
    }

    private void BuildHelpPages()
    {
        _tabs.Pages.Add(new TabPage { Text = "Origin Help", Content = _originHelpPanel });
        _tabs.Pages.Add(new TabPage { Text = "Web Help", Content = _webHelpPanel });
        _tabs.Pages.Add(new TabPage { Text = "Example", Content = _examplesPanel });
        _tabs.Pages.Add(new TabPage { Text = "Params Help", Content = _objectInfoPanel });
        _tabs.Pages.Add(new TabPage { Text = "Development Help", Content = _developmentPanel });
        _tabs.Pages.Add(new TabPage { Text = "File Manager", Content = _fileManagerPanel });
        _tabs.Pages.Add(new TabPage { Text = "Settings", Content = _settingsPanel });
        _tabs.SelectedIndex = 5;
    }

    private void StartFeatureControls()
    {
        _viewportPanel.Start();
        _commandPanel.Start();
        ResetViewport();
    }

    private void StopFeatureControls()
    {
        _commandPanel.Stop();
        _viewportPanel.Stop();
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
}
