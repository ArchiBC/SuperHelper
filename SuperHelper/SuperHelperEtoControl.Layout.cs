using Eto.Forms;
using Grasshopper;
using Rhino;
using System;
using System.Collections.Generic;

namespace SuperHelper;

public sealed partial class SuperHelperControl
{
    private const string DefaultCommandVisibleKey = "SuperHelper.Default.CommandVisible";
    private const string DefaultViewportVisibleKey = "SuperHelper.Default.ViewportVisible";
    private const string DefaultHelpVisibleKey = "SuperHelper.Default.HelpVisible";
    private EmbeddedRhinoWorkspace _embeddedWorkspace;

    private void InitializeWorkspaceLayout()
    {
        var contents = DetachPanelContents();

        _sectionStaging.Items.Clear();
        _workspaceContentHost.Content = null;
        _sectionStaging.Dispose();

        _embeddedWorkspace = new EmbeddedRhinoWorkspace(
            RhinoDoc.ActiveDoc,
            contents,
            OnEmbeddedPanelActivated);
        _workspaceContentHost.Content = _embeddedWorkspace.Control;
        SyncSectionVisibilityControls();
    }

    private Dictionary<Guid, Control> DetachPanelContents()
    {
        var contents = new Dictionary<Guid, Control>
        {
            [EmbeddedRhinoPanelIds.Command] = DetachContent(_commandSection),
            [EmbeddedRhinoPanelIds.Viewport] = DetachContent(_viewportSection),
        };

        var helpIds = EmbeddedRhinoPanelIds.HelpPages;
        for (var index = 0; index < helpIds.Count && index < _tabs.Pages.Count; index++)
        {
            var page = _tabs.Pages[index];
            contents[helpIds[index]] = page.Content;
            page.Content = null;
        }

        _tabs.Pages.Clear();
        _helpSection.Content = null;
        return contents;
    }

    private static Control DetachContent(Panel panel)
    {
        var content = panel.Content;
        panel.Content = null;
        return content;
    }

    private void OnEmbeddedPanelActivated(Guid panelId)
    {
        if (panelId == EmbeddedRhinoPanelIds.Settings)
        {
            SyncSectionVisibilityControls();
            ResetSettingsScrollPosition();
        }
    }

    private void SetSectionVisible(EmbeddedWorkspaceSection section, CheckBox checkBox)
    {
        if (_sectionVisibilityChanging || _embeddedWorkspace == null)
        {
            return;
        }

        var visible = checkBox.Checked == true;
        if (!_embeddedWorkspace.TrySetSectionVisible(section, visible, out var error))
        {
            SyncSectionVisibilityControls();
            ShowWorkspaceError(error);
        }
    }

    private void SyncSectionVisibilityControls()
    {
        if (_embeddedWorkspace == null)
        {
            return;
        }

        _sectionVisibilityChanging = true;
        try
        {
            _commandVisibleCheckBox.Checked = _embeddedWorkspace.IsSectionVisible(EmbeddedWorkspaceSection.Command);
            _viewportVisibleCheckBox.Checked = _embeddedWorkspace.IsSectionVisible(EmbeddedWorkspaceSection.Viewport);
            _helpVisibleCheckBox.Checked = _embeddedWorkspace.IsSectionVisible(EmbeddedWorkspaceSection.Help);
        }
        finally
        {
            _sectionVisibilityChanging = false;
        }
    }

    private int GetSelectedHelpPageIndex() => _embeddedWorkspace?.SelectedHelpIndex ?? _tabs.SelectedIndex;

    private void SelectHelpPage(int index) => _embeddedWorkspace?.SelectHelpPage(index);

    internal void OpenWorkspaceSettings()
    {
        var error = "Rhino workspace is not ready.";
        if (_embeddedWorkspace?.TryOpenSettings(out error) != true)
        {
            ShowWorkspaceError(error);
        }
    }

    internal bool IsWorkspaceSectionVisible(EmbeddedWorkspaceSection section) =>
        _embeddedWorkspace?.IsSectionVisible(section) == true;

    internal void SetWorkspaceSectionVisible(EmbeddedWorkspaceSection section, bool visible)
    {
        var error = "Rhino workspace is not ready.";
        if (_embeddedWorkspace?.TrySetSectionVisible(section, visible, out error) != true)
        {
            ShowWorkspaceError(error);
            return;
        }
        SyncSectionVisibilityControls();
    }

    internal void RestoreBuiltInWorkspace()
    {
        var error = "Rhino workspace is not ready.";
        if (_embeddedWorkspace?.TryRestoreBuiltIn(out error) != true)
        {
            ShowWorkspaceError(error);
            return;
        }
        SyncSectionVisibilityControls();
        ResetSettingsScrollPosition();
    }

    internal void UseCurrentWorkspaceAsDefault()
    {
        Instances.Settings.SetValue(DefaultCommandVisibleKey, IsWorkspaceSectionVisible(EmbeddedWorkspaceSection.Command));
        Instances.Settings.SetValue(DefaultViewportVisibleKey, IsWorkspaceSectionVisible(EmbeddedWorkspaceSection.Viewport));
        Instances.Settings.SetValue(DefaultHelpVisibleKey, IsWorkspaceSectionVisible(EmbeddedWorkspaceSection.Help));
    }

    internal void RestoreDefaultWorkspace()
    {
        SetWorkspaceSectionVisible(
            EmbeddedWorkspaceSection.Command,
            Instances.Settings.GetValue(DefaultCommandVisibleKey, true));
        SetWorkspaceSectionVisible(
            EmbeddedWorkspaceSection.Viewport,
            Instances.Settings.GetValue(DefaultViewportVisibleKey, true));
        SetWorkspaceSectionVisible(
            EmbeddedWorkspaceSection.Help,
            Instances.Settings.GetValue(DefaultHelpVisibleKey, true));
    }

    private void ShowWorkspaceError(string error) => MessageBox.Show(
        this,
        error,
        "SuperHelper Workspace",
        MessageBoxType.Error);

    public void ShutdownWorkspace()
    {
        _embeddedWorkspace?.Dispose();
        _embeddedWorkspace = null;
    }
}
