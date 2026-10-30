using System;
using System.Collections.Generic;
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

			return JsonSerializer.Deserialize<HelpWorkspaceState>(json) ?? new HelpWorkspaceState();
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
