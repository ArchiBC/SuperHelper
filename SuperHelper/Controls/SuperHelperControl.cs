using Eto.Forms;
using Eto.Serialization.Xaml;
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
    private HelpObject _currentObject;
    private bool _workspaceShutdown;

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

    public HelpObject CurrentObject => _currentObject;

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
        HelpObject obj,
        string html,
        string webUrl,
        IEnumerable<string> examplePaths)
    {
        if (obj == null)
        {
            return;
        }

        _examplesPanel.Save();
        if (_currentObject?.ComponentGuid == obj.ComponentGuid && HelperHost.Current.Examples.Items.TryGetValue(obj.ComponentGuid.ToString(), out var savedExamples))
            examplePaths = savedExamples;
        var componentChanged = _currentObject?.ComponentGuid != obj.ComponentGuid;
        var previous = _currentObject;
        _currentObject = obj;

        RefreshObjectData();
        UpdatePageSafely(() => _originHelpPanel.SetHtml(html), "Origin Help");
        UpdatePageSafely(() => _webHelpPanel.SetObject(obj, webUrl, componentChanged), "Web Help");
        UpdatePageSafely(() => _examplesPanel.SetExamples(obj, examplePaths), "Examples");
        if (!ReferenceEquals(previous, obj)) previous?.Dispose();
    }

    public void RefreshObjectData()
    {
        HelperHost.Current.ClearHighlight();
        _objectInfoPanel.SetObject(_currentObject);
        _developmentPanel.SetObject(_currentObject);
        _examplesPanel.SetCurrentObject(_currentObject);
    }

    public void SaveExampleValue() => _examplesPanel.Save();

    public void ClearObject()
    {
        _examplesPanel.Save();
        _currentObject?.Dispose();
        _currentObject = null;
        HelperHost.Current.ClearHighlight();
        RefreshObjectData();
        _examplesPanel.SetExamples(null, Array.Empty<string>());
        _originHelpPanel.SetHtml(string.Empty);
        _webHelpPanel.SetObject(null, string.Empty, navigate: false);
    }

    public void ResetViewport()
    {
        if (_workspaceShutdown)
        {
            return;
        }
        _viewportPanel.ResetViewport();
        _commandPanel.ResetDocument();
    }

    public void OnClosed()
    {
        HelperHost.Current.ClearHighlight();
        RhinoDoc.ActiveDoc?.Views.Redraw();
    }

    internal void OnWorkspaceShown()
    {
        if (_workspaceShutdown)
        {
            return;
        }
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
        if (_workspaceShutdown)
        {
            return;
        }
        _workspaceShutdown = true;
        _examplesPanel.Save();
        StopFeatureControls();
        _commandPanel.Shutdown();
        _viewportPanel.Shutdown();
        _examplesPanel.Shutdown();
        _workspaceLayout.Shutdown();
        _currentObject?.Dispose();
        _currentObject = null;
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
        if (_workspaceShutdown)
        {
            return;
        }
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
