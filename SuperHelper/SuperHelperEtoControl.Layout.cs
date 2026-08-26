using Eto.Forms;
using Rhino;
using System;
using System.Collections.Generic;

namespace SuperHelper;

public sealed partial class SuperHelperControl
{
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
