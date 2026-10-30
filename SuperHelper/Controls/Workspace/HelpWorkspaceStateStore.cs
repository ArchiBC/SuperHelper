using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SuperHelper;

internal sealed class HelpWorkspaceState
{
	public List<Guid> PanelIds { get; set; } = new();

	public Guid SelectedPanelId { get; set; }
}

internal static class HelpWorkspaceStateStore
{
	private const string SettingKey = "SuperHelper.HelpWorkspaceState";

	public static HelpWorkspaceState Load()
	{
		try
		{
			string json = HelperHost.Current.Settings.GetValue(SettingKey, string.Empty);
			if (string.IsNullOrWhiteSpace(json))
			{
				return new HelpWorkspaceState();
			}

			var state = JsonSerializer.Deserialize<HelpWorkspaceState>(json) ?? new HelpWorkspaceState();
			state.PanelIds = (state.PanelIds ?? new()).Select(EmbeddedRhinoPanelIds.Normalize).Distinct().ToList();
			state.SelectedPanelId = EmbeddedRhinoPanelIds.Normalize(state.SelectedPanelId);
			return state;
		}
		catch (Exception)
		{
			return new HelpWorkspaceState();
		}
	}

	public static bool TrySave(HelpWorkspaceState state)
	{
		try
		{
			HelperHost.Current.Settings.SetValue(SettingKey, JsonSerializer.Serialize(state));
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}
}
