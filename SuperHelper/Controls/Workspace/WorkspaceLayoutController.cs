using Eto.Forms;
using Grasshopper;
using Rhino;
using System;
using System.Collections.Generic;

namespace SuperHelper;

internal sealed class WorkspaceLayoutController
{
    private const string DefaultCommandVisibleKey = "SuperHelper.Default.CommandVisible";
    private const string DefaultViewportVisibleKey = "SuperHelper.Default.ViewportVisible";
    private const string DefaultHelpVisibleKey = "SuperHelper.Default.HelpVisible";

    private readonly Control _owner;
    private readonly SettingsPanel _settingsPanel;
    private EmbeddedRhinoWorkspace _workspace;

    public WorkspaceLayoutController(
        Control owner,
        Panel workspaceContentHost,
        StackLayout sectionStaging,
        Panel commandSection,
        Panel viewportSection,
        Panel helpSection,
        TabControl tabs,
        SettingsPanel settingsPanel)
    {
        _owner = owner;
        _settingsPanel = settingsPanel;
        _settingsPanel.SectionVisibilityChanged += SetSectionVisible;

        var contents = DetachPanelContents(commandSection, viewportSection, helpSection, tabs);
        sectionStaging.Items.Clear();
        workspaceContentHost.Content = null;
        sectionStaging.Dispose();

        _workspace = new EmbeddedRhinoWorkspace(RhinoDoc.ActiveDoc, contents, OnPanelActivated);
        workspaceContentHost.Content = _workspace.Control;
        SyncSectionVisibilityControls();
    }

    public int SelectedHelpIndex => _workspace?.SelectedHelpIndex ?? 0;

    public bool IsSectionVisible(EmbeddedWorkspaceSection section) =>
        _workspace?.IsSectionVisible(section) == true;

    public void SetSectionVisible(EmbeddedWorkspaceSection section, bool visible)
    {
        var error = "Rhino workspace is not ready.";
        if (_workspace?.TrySetSectionVisible(section, visible, out error) != true)
        {
            SyncSectionVisibilityControls();
            ShowWorkspaceError(error);
            return;
        }

        SyncSectionVisibilityControls();
    }

    public void OpenSettings()
    {
        var error = "Rhino workspace is not ready.";
        if (_workspace?.TryOpenSettings(out error) != true)
        {
            ShowWorkspaceError(error);
        }
    }

    public void RestoreBuiltIn()
    {
        var error = "Rhino workspace is not ready.";
        if (_workspace?.TryRestoreBuiltIn(out error) != true)
        {
            ShowWorkspaceError(error);
            return;
        }

        SyncSectionVisibilityControls();
        _settingsPanel.ResetScrollPosition();
    }

    public void UseCurrentAsDefault()
    {
        Instances.Settings.SetValue(DefaultCommandVisibleKey, IsSectionVisible(EmbeddedWorkspaceSection.Command));
        Instances.Settings.SetValue(DefaultViewportVisibleKey, IsSectionVisible(EmbeddedWorkspaceSection.Viewport));
        Instances.Settings.SetValue(DefaultHelpVisibleKey, IsSectionVisible(EmbeddedWorkspaceSection.Help));
    }

    public void RestoreDefault()
    {
        SetSectionVisible(
            EmbeddedWorkspaceSection.Command,
            Instances.Settings.GetValue(DefaultCommandVisibleKey, true));
        SetSectionVisible(
            EmbeddedWorkspaceSection.Viewport,
            Instances.Settings.GetValue(DefaultViewportVisibleKey, true));
        SetSectionVisible(
            EmbeddedWorkspaceSection.Help,
            Instances.Settings.GetValue(DefaultHelpVisibleKey, true));
    }

    public void Shutdown()
    {
        _workspace?.Dispose();
        _workspace = null;
    }

    private static Dictionary<Guid, Control> DetachPanelContents(
        Panel commandSection,
        Panel viewportSection,
        Panel helpSection,
        TabControl tabs)
    {
        var contents = new Dictionary<Guid, Control>
        {
            [EmbeddedRhinoPanelIds.Command] = DetachContent(commandSection),
            [EmbeddedRhinoPanelIds.Viewport] = DetachContent(viewportSection),
        };

        var helpIds = EmbeddedRhinoPanelIds.HelpPages;
        for (var index = 0; index < helpIds.Count && index < tabs.Pages.Count; index++)
        {
            var page = tabs.Pages[index];
            contents[helpIds[index]] = page.Content;
            page.Content = null;
        }

        tabs.Pages.Clear();
        helpSection.Content = null;
        return contents;
    }

    private static Control DetachContent(Panel panel)
    {
        var content = panel.Content;
        panel.Content = null;
        return content;
    }

    private void OnPanelActivated(Guid panelId)
    {
        if (panelId != EmbeddedRhinoPanelIds.Settings)
        {
            return;
        }

        SyncSectionVisibilityControls();
        _settingsPanel.ResetScrollPosition();
    }

    private void SyncSectionVisibilityControls()
    {
        if (_workspace == null)
        {
            return;
        }

        _settingsPanel.SetSectionVisibility(
            _workspace.IsSectionVisible(EmbeddedWorkspaceSection.Command),
            _workspace.IsSectionVisible(EmbeddedWorkspaceSection.Viewport),
            _workspace.IsSectionVisible(EmbeddedWorkspaceSection.Help));
    }

    private void ShowWorkspaceError(string error) => MessageBox.Show(
        _owner,
        error,
        "SuperHelper Workspace",
        MessageBoxType.Error);
}
