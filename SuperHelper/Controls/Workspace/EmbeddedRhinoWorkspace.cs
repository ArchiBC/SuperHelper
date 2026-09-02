using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.UI;

namespace SuperHelper;

internal sealed class EmbeddedRhinoWorkspace : IDisposable
{
	private enum DockLocation
	{
		Top,
		Left,
		Right,
		Bottom,
		Floating
	}

	private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

	private static readonly Guid PlugInId = new Guid("B45A29B1-4343-4035-989E-044E8580D9CF");

	private static readonly Guid[] LegacyDockBarIds =
	{
		new("37A151C8-9700-446E-B1A8-82B6BA3CEB0A"),
		new("F7AAB540-D083-4082-8509-52F74054C8B1"),
		new("C30FC4B2-9235-42BD-9966-F3DDD020225C"),
	};

	// The embedded GH workspace is transient. Reusing stable dock-bar ids makes
	// Rhino restore them into its global layout and then rejects the next private
	// workspace with "The dock bar Id is currently in use".
	private readonly Guid _commandDockBarId = Guid.NewGuid();

	private readonly Guid _viewportDockBarId = Guid.NewGuid();

	private readonly Guid _helpDockBarId = Guid.NewGuid();

	private readonly RhinoDoc _document;

	private readonly Action<Guid> _panelActivated;

	private readonly Assembly _rhinoUiAssembly;

	private readonly Type _dockSitesType;

	private readonly Type _dockSiteType;

	private readonly Type _dockLocationType;

	private readonly Type _collectionType;

	private readonly Type _collectionsType;

	private readonly Type _dockSitePlacementType;

	private readonly object _dockSites;

	private readonly Dictionary<DockLocation, object> _sites;

	private readonly Dictionary<DockLocation, Control> _siteControls;

	private readonly List<object> _ownedCollections;

	private readonly PixelLayout _layout;

	private readonly List<(object Source, EventInfo Event, Delegate Handler)> _helpStateSubscriptions = new();

	private object _commandCollection;

	private object _viewportCollection;

	private object _helpCollection;

	private HelpWorkspaceState _loadedHelpWorkspaceState;

	private int _committedLeftExtent;

	private int _committedTopExtent;

	private int _committedRightExtent;

	private int _committedBottomExtent;

	private bool _disposed;

	private bool _initialBandsFitted;

	private bool _relayoutPending;

	private bool _contentFitPending;

	private bool _helpStateSavePending;

	public Control Control => (Control)(object)_layout;

	public int SelectedHelpIndex
	{
		get
		{
			for (int i = 0; i < EmbeddedRhinoPanelIds.HelpPages.Count; i++)
			{
				if (IsPanelSelected(EmbeddedRhinoPanelIds.HelpPages[i]))
				{
					return i;
				}
			}
			return -1;
		}
	}

	public EmbeddedRhinoWorkspace(RhinoDoc document, IReadOnlyDictionary<Guid, Control> contents, Action<Guid> panelActivated)
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Expected O, but got Unknown
		_sites = new Dictionary<DockLocation, object>();
		_siteControls = new Dictionary<DockLocation, Control>();
		_ownedCollections = new List<object>();
		_layout = new PixelLayout();
		_committedLeftExtent = 4;
		_committedTopExtent = 4;
		_committedRightExtent = 4;
		_committedBottomExtent = 4;
		_document = document ?? RhinoDoc.ActiveDoc ?? throw new InvalidOperationException("Rhino has no active document.");
		_panelActivated = panelActivated;
		_rhinoUiAssembly = Assembly.Load("Rhino.UI");
		_dockSitesType = RequireType("Rhino.UI.Internal.TabPanels.TabPanelDockSites");
		_dockSiteType = RequireType("Rhino.UI.Internal.TabPanels.TabPanelDockSite");
		_dockLocationType = RequireType("Rhino.UI.Internal.TabPanels.ControlBarDockLocation");
		_collectionType = RequireType("Rhino.UI.Internal.TabPanels.TabPanelCollection");
		_collectionsType = RequireType("Rhino.UI.Internal.TabPanels.TabPanelCollections");
		_dockSitePlacementType = RequireType("Rhino.UI.Internal.TabPanels.DockSitePlacement");
		RemoveLegacyEmbeddedCollections();
		EmbeddedRhinoPanelContent.Clear();
		RegisterSuperHelperPanels();
		EmbeddedRhinoPanelContent.Set(contents);
		_dockSites = Activator.CreateInstance(_dockSitesType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[1] { _document }, null) ?? throw new InvalidOperationException("Unable to create Rhino dock sites.");
		CreateSiteControls();
		CreateInitialCollections();
		AttachHelpStatePersistence();
		BuildHost();
	}

	private void RemoveLegacyEmbeddedCollections()
	{
		MethodInfo fromDockBarId = _collectionsType.GetMethod(
			"FromDockBarId",
			StaticFlags,
			null,
			new[] { typeof(Guid) },
			null);
		MethodInfo remove = _collectionsType.GetMethod(
			"Remove",
			StaticFlags,
			null,
			new[] { _collectionType },
			null);
		foreach (Guid dockBarId in LegacyDockBarIds)
		{
			object collection = fromDockBarId?.Invoke(null, new object[] { dockBarId });
			if (collection != null)
			{
				remove?.Invoke(null, new[] { collection });
			}
		}

		MethodInfo resetLastCollection = _collectionType.GetMethod(
			"SetLastCollectionPanelWasIn",
			StaticFlags,
			null,
			new[] { typeof(Guid), typeof(Guid) },
			null);
		foreach (Guid panelId in EmbeddedRhinoPanelIds.All)
		{
			resetLastCollection?.Invoke(null, new object[] { panelId, Guid.Empty });
		}
	}

	public void SelectHelpPage(int index)
	{
		if (index >= 0 && index < EmbeddedRhinoPanelIds.HelpPages.Count)
		{
			Guid panelId = EmbeddedRhinoPanelIds.HelpPages[index];
			object obj = ContainsPanel(_helpCollection, panelId) ? _helpCollection : null;
			if (obj != null)
			{
				object obj2 = CollectionState(obj);
				obj2?.GetType().GetProperty("SelectedItemId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(obj2, panelId);
				_panelActivated?.Invoke(panelId);
			}
		}
	}

	public bool TryAddPanel(Guid panelId, out string error)
	{
		error = string.Empty;
		try
		{
			if (PanelDefinition(panelId) == null)
			{
				error = $"Rhino panel is not registered: {panelId}";
				return false;
			}
			EmbeddedWorkspaceSection section = panelId == EmbeddedRhinoPanelIds.Command
				? EmbeddedWorkspaceSection.Command
				: panelId == EmbeddedRhinoPanelIds.Viewport
					? EmbeddedWorkspaceSection.Viewport
					: EmbeddedWorkspaceSection.Help;
			object obj = SectionCollection(section);
			if (obj == null)
			{
				error = "No target Rhino container is available.";
				return false;
			}
			if (!ContainsPanel(obj, panelId) && (!(_collectionType.GetMethod("AddTabPanel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[3]
			{
				typeof(Guid),
				typeof(int),
				typeof(bool)
			}, null)?.Invoke(obj, new object[3] { panelId, -1, true }) is int num) || num == 0))
			{
				error = "Rhino rejected the panel for this container.";
				return false;
			}
			EnsureCollectionVisible(obj, 280);
			SelectPanel(obj, panelId);
			QueueRelayoutAndFitBands();
			return true;
		}
		catch (Exception exception)
		{
			error = Unwrap(exception).Message;
			return false;
		}
	}

	public bool IsSectionVisible(EmbeddedWorkspaceSection section)
	{
		object obj = SectionCollection(section);
		if (obj == null)
		{
			return false;
		}
		object obj2 = _collectionType.GetProperty("DockBar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj);
		object placement = obj2?.GetType().GetMethod(
			"Placement",
			InstanceFlags,
			null,
			new[] { typeof(uint) },
			null)?.Invoke(obj2, new object[] { _document.RuntimeSerialNumber });
		if (placement?.GetType().GetProperty("Visible", InstanceFlags)?.GetValue(placement) is bool visible
			&& !visible)
		{
			return false;
		}
		int result;
		if (obj2 != null)
		{
			if (!WorkspaceContainsDockBar(obj2))
			{
				object obj3 = FloatingForm(obj2);
				Form val = (Form)((obj3 is Form) ? obj3 : null);
				result = ((val != null && ((Control)val).Visible) ? 1 : 0);
			}
			else
			{
				result = 1;
			}
		}
		else
		{
			result = 0;
		}
		return (byte)result != 0;
	}

	public bool TrySetSectionVisible(EmbeddedWorkspaceSection section, bool visible, out string error)
	{
		error = string.Empty;
		try
		{
			object obj = SectionCollection(section);
			if (obj == null)
			{
				error = $"The {section} container is unavailable.";
				return false;
			}
			if (visible)
			{
				EnsureCollectionVisible(obj, SectionBandSize(section));
			}
			else
			{
				HideCollection(obj);
			}
			QueueRelayoutAndFitBands();
			return true;
		}
		catch (Exception exception)
		{
			error = Unwrap(exception).Message;
			return false;
		}
	}

	public bool TryOpenSettings(out string error)
	{
		return TryAddPanel(EmbeddedRhinoPanelIds.Settings, out error);
	}

	public bool TryRestoreBuiltIn(out string error)
	{
		error = string.Empty;
		try
		{
			HideCollection(_commandCollection);
			HideCollection(_viewportCollection);
			HideCollection(_helpCollection);
			EnsureCollectionVisible(_commandCollection, SectionBandSize(EmbeddedWorkspaceSection.Command));
			EnsureCollectionVisible(_viewportCollection, SectionBandSize(EmbeddedWorkspaceSection.Viewport));
			EnsureCollectionVisible(_helpCollection, SectionBandSize(EmbeddedWorkspaceSection.Help));
			_initialBandsFitted = false;
			SelectPanel(_helpCollection, EmbeddedRhinoPanelIds.Settings);
			QueueRelayoutAndFitBands();
			return true;
		}
		catch (Exception exception)
		{
			error = Unwrap(exception).Message;
			return false;
		}
	}

	private void RegisterSuperHelperPanels()
	{
		Dictionary<Type, (string, string)> dictionary = new Dictionary<Type, (string, string)>();
		Type typeFromHandle = typeof(SuperHelperCommandPanel);
		dictionary[typeFromHandle] = ("Command", "SuperHelper.Resources.Panels.Command.svg");
		Type typeFromHandle2 = typeof(SuperHelperViewportPanel);
		dictionary[typeFromHandle2] = ("Viewport", "SuperHelper.Resources.Panels.Viewport.svg");
		Type typeFromHandle3 = typeof(SuperHelperOriginHelpPanel);
		dictionary[typeFromHandle3] = ("Origin Help", "SuperHelper.Resources.Panels.OriginHelp.svg");
		Type typeFromHandle4 = typeof(SuperHelperWebHelpPanel);
		dictionary[typeFromHandle4] = ("Web Help", "SuperHelper.Resources.Panels.WebHelp.svg");
		Type typeFromHandle5 = typeof(SuperHelperExamplePanel);
		dictionary[typeFromHandle5] = ("Example", "SuperHelper.Resources.Panels.Example.svg");
		Type typeFromHandle6 = typeof(SuperHelperParamsHelpPanel);
		dictionary[typeFromHandle6] = ("Params Help", "SuperHelper.Resources.Panels.ParamsHelp.svg");
		Type typeFromHandle7 = typeof(SuperHelperDevelopmentHelpPanel);
		dictionary[typeFromHandle7] = ("Development Help", "SuperHelper.Resources.Panels.DevelopmentHelp.svg");
		Type typeFromHandle8 = typeof(SuperHelperFileManagerPanel);
		dictionary[typeFromHandle8] = ("File Manager", "SuperHelper.Resources.Panels.FileManager.svg");
		Type typeFromHandle9 = typeof(SuperHelperSettingsPanel);
		dictionary[typeFromHandle9] = ("Settings", "SuperHelper.Resources.Panels.Settings.svg");
		Dictionary<Type, (string, string)> dictionary2 = dictionary;
		Assembly assembly = typeof(Panels).Assembly;
		Type type = assembly.GetType("Rhino.UI.PanelSystem", throwOnError: true);
		Type type2 = assembly.GetType("Rhino.UI.PanelType", throwOnError: true);
		MethodInfo methodInfo = type.GetMethod("Register", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[6]
		{
			typeof(Guid),
			typeof(Type),
			typeof(string),
			typeof(Assembly),
			typeof(string),
			type2
		}, null) ?? throw new MissingMethodException(type.FullName, "Register");
		MethodInfo method = type.GetMethod("Definition", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(Guid) }, null);
		object obj = Enum.Parse(type2, "System");
		foreach (KeyValuePair<Type, (string, string)> item in dictionary2)
		{
			if (method?.Invoke(null, new object[1] { item.Key.GUID }) == null)
			{
				methodInfo.Invoke(null, new object[6]
				{
					PlugInId,
					item.Key,
					item.Value.Item1,
					typeof(EmbeddedRhinoWorkspace).Assembly,
					item.Value.Item2,
					obj
				});
			}
		}
	}

	private void CreateSiteControls()
	{
		DockLocation[] array = new DockLocation[4]
		{
			DockLocation.Left,
			DockLocation.Top,
			DockLocation.Right,
			DockLocation.Bottom
		};
		for (int i = 0; i < array.Length; i++)
		{
			DockLocation dockLocation = array[i];
			object obj = _dockSitesType.GetProperty(dockLocation.ToString(), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(_dockSites) ?? throw new InvalidOperationException($"Rhino did not create the {dockLocation} dock site.");
			object obj2 = _dockSiteType.GetProperty("Control", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj);
			Control value = (Control)(((obj2 is Control) ? obj2 : null) ?? throw new InvalidOperationException($"Rhino did not create the {dockLocation} dock control."));
			_sites[dockLocation] = obj;
			_siteControls[dockLocation] = value;
		}
	}

	private void CreateInitialCollections()
	{
		_commandCollection = AddInitialCollection("Command", _commandDockBarId, EmbeddedRhinoPanelIds.Command, new[] { EmbeddedRhinoPanelIds.Command }, 82);
		_viewportCollection = AddInitialCollection("Viewport", _viewportDockBarId, EmbeddedRhinoPanelIds.Viewport, new[] { EmbeddedRhinoPanelIds.Viewport }, 285);

		_loadedHelpWorkspaceState = HelpWorkspaceStateStore.Load();
		Guid[] restoredPanelIds = _loadedHelpWorkspaceState.PanelIds
			.Where(IsRestorableHelpPanel)
			.Distinct()
			.ToArray();
		if (restoredPanelIds.Length == 0)
		{
			restoredPanelIds = EmbeddedRhinoPanelIds.HelpPages.ToArray();
		}

		Guid selectedPanelId = restoredPanelIds.Contains(_loadedHelpWorkspaceState.SelectedPanelId)
			? _loadedHelpWorkspaceState.SelectedPanelId
			: restoredPanelIds.Contains(EmbeddedRhinoPanelIds.FileManager)
				? EmbeddedRhinoPanelIds.FileManager
				: restoredPanelIds[0];
		_helpCollection = AddInitialCollection("Help", _helpDockBarId, selectedPanelId, restoredPanelIds, 300);
	}

	private bool IsRestorableHelpPanel(Guid panelId)
	{
		return panelId != Guid.Empty
			&& panelId != EmbeddedRhinoPanelIds.Command
			&& panelId != EmbeddedRhinoPanelIds.Viewport
			&& PanelDefinition(panelId) != null;
	}

	private void AttachHelpStatePersistence()
	{
		SubscribeToHelpStateEvent(_helpCollection, "ItemAdded");
		SubscribeToHelpStateEvent(_helpCollection, "ItemRemoved");
		SubscribeToHelpStateEvent(_helpCollection, "ItemMoved");

		object state = CollectionState(_helpCollection);
		if (state != null)
		{
			SubscribeToHelpStateEvent(state, "SelectionChanged");
		}
	}

	private void SubscribeToHelpStateEvent(object source, string eventName)
	{
		EventInfo eventInfo = source.GetType().GetEvent(eventName, InstanceFlags);
		if (eventInfo?.EventHandlerType == null)
		{
			return;
		}

		Delegate handler = CreateCallbackDelegate(eventInfo.EventHandlerType, QueueSaveHelpWorkspaceState);
		eventInfo.AddEventHandler(source, handler);
		_helpStateSubscriptions.Add((source, eventInfo, handler));
	}

	private static Delegate CreateCallbackDelegate(Type delegateType, Action callback)
	{
		MethodInfo invoke = delegateType.GetMethod("Invoke")
			?? throw new InvalidOperationException($"{delegateType.FullName} is not a delegate type.");
		ParameterExpression[] parameters = invoke.GetParameters()
			.Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
			.ToArray();
		MethodInfo callbackInvoke = typeof(Action).GetMethod(nameof(Action.Invoke))
			?? throw new MissingMethodException(typeof(Action).FullName, nameof(Action.Invoke));
		MethodCallExpression body = Expression.Call(Expression.Constant(callback), callbackInvoke);
		return Expression.Lambda(delegateType, body, parameters).Compile();
	}

	private void QueueSaveHelpWorkspaceState()
	{
		if (_disposed || _helpStateSavePending)
		{
			return;
		}

		Application application = Application.Instance;
		if (application == null)
		{
			SaveHelpWorkspaceState();
			return;
		}

		_helpStateSavePending = true;
		application.AsyncInvoke((Action)delegate
		{
			_helpStateSavePending = false;
			if (!_disposed)
			{
				SaveHelpWorkspaceState();
			}
		});
	}

	private void SaveHelpWorkspaceState()
	{
		if (_helpCollection == null)
		{
			return;
		}

		List<Guid> visiblePanelIds = CollectionPanelIds(_helpCollection).ToList();
		List<Guid> persistedPanelIds = MergeUnavailablePanelIds(visiblePanelIds);
		object state = CollectionState(_helpCollection);
		Guid selectedPanelId = state?.GetType()
			.GetProperty("SelectedItemId", InstanceFlags)?.GetValue(state) is Guid selected
			? selected
			: Guid.Empty;

		_loadedHelpWorkspaceState = new HelpWorkspaceState
		{
			PanelIds = persistedPanelIds,
			SelectedPanelId = selectedPanelId,
		};
		HelpWorkspaceStateStore.TrySave(_loadedHelpWorkspaceState);
	}

	private IEnumerable<Guid> CollectionPanelIds(object collection)
	{
		if (!(_collectionType.GetProperty("Items", InstanceFlags)?.GetValue(collection) is IEnumerable items))
		{
			yield break;
		}

		foreach (object item in items)
		{
			if (item?.GetType().GetProperty("Id", InstanceFlags)?.GetValue(item) is Guid panelId
				&& panelId != Guid.Empty)
			{
				yield return panelId;
			}
		}
	}

	private List<Guid> MergeUnavailablePanelIds(IReadOnlyCollection<Guid> visiblePanelIds)
	{
		List<Guid> result = visiblePanelIds.Distinct().ToList();
		if (_loadedHelpWorkspaceState == null)
		{
			return result;
		}

		for (int index = 0; index < _loadedHelpWorkspaceState.PanelIds.Count; index++)
		{
			Guid panelId = _loadedHelpWorkspaceState.PanelIds[index];
			if (panelId == Guid.Empty || result.Contains(panelId) || PanelDefinition(panelId) != null)
			{
				continue;
			}

			result.Insert(Math.Min(index, result.Count), panelId);
		}
		return result;
	}

	private void DetachHelpStatePersistence()
	{
		foreach ((object source, EventInfo eventInfo, Delegate handler) in _helpStateSubscriptions)
		{
			eventInfo.RemoveEventHandler(source, handler);
		}
		_helpStateSubscriptions.Clear();
	}

	private object SectionCollection(EmbeddedWorkspaceSection section)
	{
		if (1 == 0)
		{
		}
		object result = section switch
		{
			EmbeddedWorkspaceSection.Command => _commandCollection,
			EmbeddedWorkspaceSection.Viewport => _viewportCollection,
			EmbeddedWorkspaceSection.Help => _helpCollection,
			_ => null,
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static int SectionBandSize(EmbeddedWorkspaceSection section)
	{
		if (1 == 0)
		{
		}
		int result = section switch
		{
			EmbeddedWorkspaceSection.Command => 82,
			EmbeddedWorkspaceSection.Viewport => 285,
			EmbeddedWorkspaceSection.Help => 300,
			_ => 280,
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private object AddInitialCollection(string name, Guid dockBarId, Guid selectedPanel, IEnumerable<Guid> panelIds, int bandSize)
	{
		MethodInfo methodInfo = _collectionType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[7]
		{
			typeof(RhinoDoc),
			typeof(string),
			typeof(Guid),
			_dockLocationType,
			typeof(bool),
			typeof(Guid),
			typeof(IEnumerable<Guid>)
		}, null) ?? throw new MissingMethodException(_collectionType.FullName, "Create");
		object obj = methodInfo.Invoke(null, new object[7]
		{
			_document,
			name,
			dockBarId,
			RhinoDockLocation(DockLocation.Top),
			false,
			selectedPanel,
			panelIds
		}) ?? throw new InvalidOperationException("Rhino could not create the " + name + " container.");
		_ownedCollections.Add(obj);
		object obj2 = _collectionType.GetProperty("DockBar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj) ?? throw new InvalidOperationException("Rhino did not create the " + name + " dock bar.");
		object obj3 = obj2.GetType().GetMethod("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(uint) }, null)?.Invoke(obj2, new object[1] { _document.RuntimeSerialNumber }) ?? throw new InvalidOperationException("Rhino did not create the " + name + " dock bar state.");
		object obj4 = obj2.GetType().GetMethod("Placement", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(uint) }, null)?.Invoke(obj2, new object[1] { _document.RuntimeSerialNumber });
		obj4?.GetType().GetProperty("Visible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(obj4, true);
		object obj5 = Activator.CreateInstance(_dockSitePlacementType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[2] { -1, -1 }, null);
		object obj6 = _sites[DockLocation.Top];
		_dockSiteType.GetMethod("AddDockBar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(obj6, new object[4] { obj2, obj3, obj5, bandSize });
		return obj;
	}

	private void BuildHost()
	{
		foreach (Control value in _siteControls.Values)
		{
			_layout.Add(value, 0, 0);
		}
		((Control)_layout).SizeChanged += delegate
		{
			Relayout();
		};
		((Control)_layout).Load += delegate
		{
			FitInitialTopBands();
			Relayout();
		};
	}

	private void FitInitialTopBands()
	{
		if (_initialBandsFitted)
		{
			return;
		}

		var clientSize = ((Container)_layout).ClientSize;
		if (clientSize.Height < 180)
		{
			return;
		}

		var bands = SiteBands(_sites[DockLocation.Top]).ToArray();
		if (bands.Length == 0)
		{
			return;
		}

		_initialBandsFitted = true;
		var availableHeight = Math.Max(180, clientSize.Height - 16);
		if (bands.Length == 1)
		{
			SetProperty(bands[0], "Size", (float)availableHeight);
			RecalculateSite(DockLocation.Top);
			return;
		}

		if (bands.Length == 2)
		{
			SetProperty(bands[0], "Size", Math.Max(82f, availableHeight * 0.34f));
			RecalculateSite(DockLocation.Top);
			SetProperty(
				bands[1],
				"Size",
				Math.Max(40f, availableHeight - GetFloat(bands[0], "Size")));
			RecalculateSite(DockLocation.Top);
			return;
		}

		// With three visible sections Rhino may normalize the first two band
		// sizes during RecalcLayout. Calculate the third band afterwards from
		// those committed values so it always fills the actual remaining height.
		SetProperty(bands[0], "Size", Math.Min(88f, availableHeight * 0.18f));
		SetProperty(bands[1], "Size", Math.Max(100f, availableHeight * 0.43f));
		RecalculateSite(DockLocation.Top);

		var fixedHeight = bands.Take(bands.Length - 1)
			.Sum(band => Math.Max(0f, GetFloat(band, "Size")));
		SetProperty(bands[^1], "Size", Math.Max(40f, availableHeight - fixedHeight));
		RecalculateSite(DockLocation.Top);
	}

	private void Relayout()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		Size clientSize = ((Container)_layout).ClientSize;
		if (clientSize.Width < 1)
		{
			return;
		}
		clientSize = ((Container)_layout).ClientSize;
		if (clientSize.Height < 1)
		{
			return;
		}
		clientSize = ((Container)_layout).ClientSize;
		int width = clientSize.Width;
		clientSize = ((Container)_layout).ClientSize;
		int height = clientSize.Height;
		FillWorkspaceCenter(width, height);
		_committedLeftExtent = SiteExtent(DockLocation.Left, width, 0);
		_committedRightExtent = SiteExtent(DockLocation.Right, width, 0);
		_committedTopExtent = SiteExtent(DockLocation.Top, height, 0);
		_committedBottomExtent = SiteExtent(DockLocation.Bottom, height, 0);
		int num = _committedLeftExtent;
		int num2 = _committedRightExtent;
		int num3 = _committedTopExtent;
		int num4 = _committedBottomExtent;
		if (num3 + num4 > height)
		{
			float num5 = (float)height / Math.Max(1f, num3 + num4);
			num3 = Math.Max(0, (int)((float)num3 * num5));
			num4 = Math.Max(0, height - num3);
		}
		if (num + num2 > width)
		{
			float num6 = (float)width / Math.Max(1f, num + num2);
			num = Math.Max(0, (int)((float)num * num6));
			num2 = Math.Max(0, width - num);
		}
		Place(DockLocation.Top, num, 0, width - num - num2, num3);
		Place(DockLocation.Bottom, num, height - num4, width - num - num2, num4);
		Place(DockLocation.Left, 0, num3, num, height - num3 - num4);
		Place(DockLocation.Right, width - num2, num3, num2, height - num3 - num4);
		foreach (DockLocation item in _sites.Keys.Where((DockLocation value) => GetInt(_sites[value], "BandCount") > 0))
		{
			RecalculateSite(item);
		}
		DisableEmbeddedDragHandles();
		QueueVisiblePanelContentFit();
	}

	private void QueueVisiblePanelContentFit()
	{
		if (_contentFitPending)
		{
			return;
		}

		Application application = Application.Instance;
		if (application == null)
		{
			FitVisiblePanelContents();
			return;
		}

		_contentFitPending = true;
		application.AsyncInvoke((Action)delegate
		{
			_contentFitPending = false;
			FitVisiblePanelContents();
		});
	}

	private void FitVisiblePanelContents()
	{
		foreach (EmbeddedWorkspaceSection section in new[]
		{
			EmbeddedWorkspaceSection.Command,
			EmbeddedWorkspaceSection.Viewport,
			EmbeddedWorkspaceSection.Help,
		}.Where(IsSectionVisible))
		{
			object state = CollectionState(SectionCollection(section));
			object collectionControl = state?.GetType().GetMethod(
				"GetCollectionControl",
				InstanceFlags,
				null,
				Type.EmptyTypes,
				null)?.Invoke(state, null);
			Panel contentPanel = collectionControl?.GetType().GetProperty(
				"ContentPanel",
				InstanceFlags)?.GetValue(collectionControl) as Panel;
			if (contentPanel?.Content is SuperHelperRhinoPanel panel)
			{
				panel.FitToContainer(contentPanel.ClientSize);
			}
		}
	}

	private void DisableEmbeddedDragHandles()
	{
		foreach (object collection in _ownedCollections)
		{
			object state = CollectionState(collection);
			object collectionControl = state?.GetType().GetMethod(
				"GetCollectionControl",
				InstanceFlags,
				null,
				Type.EmptyTypes,
				null)?.Invoke(state, null);
			if (collectionControl == null)
			{
				continue;
			}

			collectionControl.GetType().GetMethod(
				"SetGripperText",
				InstanceFlags,
				null,
				new[] { typeof(string) },
				null)?.Invoke(collectionControl, new object[] { string.Empty });

			foreach (string name in new[] { "VGripper", "HGripper" })
			{
				object gripper = collectionControl.GetType().GetProperty(
					name,
					InstanceFlags)?.GetValue(collectionControl);
				if (gripper is Control control)
				{
					control.Visible = false;
					control.Size = Size.Empty;
				}
			}
		}
	}

	private void FillWorkspaceCenter(int width, int height)
	{
		int num = SiteExtent(DockLocation.Left, width, 0);
		int num2 = SiteExtent(DockLocation.Right, width, 0);
		int num3 = SiteExtent(DockLocation.Bottom, height, 0);
		if (GetInt(_sites[DockLocation.Top], "BandCount") > 0)
		{
			ExpandLastBandToExtent(DockLocation.Top, Math.Max(0, height - num3));
		}
		else if (GetInt(_sites[DockLocation.Bottom], "BandCount") > 0)
		{
			ExpandLastBandToExtent(DockLocation.Bottom, height);
		}
		else if (GetInt(_sites[DockLocation.Left], "BandCount") > 0)
		{
			ExpandLastBandToExtent(DockLocation.Left, Math.Max(0, width - num2));
		}
		else if (GetInt(_sites[DockLocation.Right], "BandCount") > 0)
		{
			ExpandLastBandToExtent(DockLocation.Right, width);
		}
	}

	private void ExpandLastBandToExtent(DockLocation location, int targetExtent)
	{
		object site = _sites[location];
		object[] array = SiteBands(site).ToArray();
		if (array.Length >= 1 && targetExtent >= 1)
		{
			object target = array[^1];
			float num = GetFloat(target, "Size");
			float num2 = array.Take(array.Length - 1).Sum((object band) => GetFloat(band, "Size"));
			float num3 = Math.Max(40f, (float)targetExtent - num2);
			if (!(Math.Abs(num3 - num) < 0.5f))
			{
				SetProperty(target, "Size", num3);
				RecalculateSite(location);
			}
		}
	}

	private int SiteExtent(DockLocation location, int available, int emptySize)
	{
		object target = _sites[location];
		if (GetInt(target, "BandCount") < 1)
		{
			return emptySize;
		}
		Control val = _siteControls[location];
		float num = Convert.ToSingle(((object)val).GetType().GetProperty("DockSiteSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(val) ?? ((object)0f));
		if (num < 1f)
		{
			num = SiteBands(target).Sum(band => Math.Max(0f, GetFloat(band, "Size")));
		}
		return Math.Clamp((int)Math.Ceiling(num), emptySize, Math.Max(emptySize, available));
	}

	private void Place(DockLocation location, int x, int y, int width, int height)
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		Control val = _siteControls[location];
		val.Size = new Size(Math.Max(0, width), Math.Max(0, height));
		_layout.Move(val, x, y);
	}

	private void QueueRelayout()
	{
		if (_relayoutPending)
		{
			return;
		}
		_relayoutPending = true;
		Application instance = Application.Instance;
		if (instance != null)
		{
			instance.AsyncInvoke((Action)delegate
			{
				_relayoutPending = false;
				Relayout();
			});
		}
	}

	private void QueueRelayoutAndFitBands()
	{
		_initialBandsFitted = false;
		Application instance = Application.Instance;
		if (instance == null)
		{
			NormalizeTopBandOrder();
			FitInitialTopBands();
			Relayout();
			return;
		}
		instance.AsyncInvoke((Action)delegate
		{
			NormalizeTopBandOrder();
			FitInitialTopBands();
			Relayout();
		});
	}

	private void NormalizeTopBandOrder()
	{
		EmbeddedWorkspaceSection[] order =
		{
			EmbeddedWorkspaceSection.Command,
			EmbeddedWorkspaceSection.Viewport,
			EmbeddedWorkspaceSection.Help,
		};
		var visible = order.Where(IsSectionVisible).ToArray();
		foreach (EmbeddedWorkspaceSection section in visible)
		{
			object collection = SectionCollection(section);
			object dockBar = _collectionType.GetProperty("DockBar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(collection);
			if (dockBar == null)
			{
				continue;
			}
			foreach (object site in _sites.Values.Where(value => SiteContainsDockBar(value, dockBar)))
			{
				_dockSiteType.GetMethod("RemoveDockBar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { dockBar.GetType() }, null)?.Invoke(site, new object[1] { dockBar });
			}
		}

		foreach (EmbeddedWorkspaceSection section in visible)
		{
			object collection = SectionCollection(section);
			object dockBar = _collectionType.GetProperty("DockBar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(collection);
			object state = dockBar?.GetType().GetMethod("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(uint) }, null)?.Invoke(dockBar, new object[1] { _document.RuntimeSerialNumber });
			if (dockBar == null || state == null)
			{
				continue;
			}
			object placement = dockBar.GetType().GetMethod("Placement", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(uint) }, null)?.Invoke(dockBar, new object[1] { _document.RuntimeSerialNumber });
			placement?.GetType().GetProperty("Visible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(placement, true);
			placement?.GetType().GetProperty("DockLocation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(placement, RhinoDockLocation(DockLocation.Top));
			object sitePlacement = Activator.CreateInstance(_dockSitePlacementType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[2] { -1, -1 }, null);
			_dockSiteType.GetMethod("AddDockBar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(
				_sites[DockLocation.Top],
				new object[4] { dockBar, state, sitePlacement, SectionBandSize(section) });
		}
		RecalculateSite(DockLocation.Top);
	}

	private bool SiteContainsDockBar(object site, object dockBar)
	{
		object result = _dockSiteType.GetMethod("ContainsDockBar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { dockBar.GetType() }, null)?.Invoke(site, new object[1] { dockBar });
		return result is bool contains && contains;
	}

	private void EnsureCollectionVisible(object collection, int bandSize)
	{
		object obj = _collectionType.GetProperty("DockBar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(collection);
		if (obj == null)
		{
			return;
		}
		object obj2 = obj.GetType().GetMethod("Placement", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(uint) }, null)?.Invoke(obj, new object[1] { _document.RuntimeSerialNumber });
		obj2?.GetType().GetProperty("Visible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(obj2, true);
		if (WorkspaceContainsDockBar(obj))
		{
			return;
		}
		object obj3 = FloatingForm(obj);
		Form val = (Form)((obj3 is Form) ? obj3 : null);
		if (val == null || !((Control)val).Visible)
		{
			object obj4 = obj.GetType().GetMethod("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(uint) }, null)?.Invoke(obj, new object[1] { _document.RuntimeSerialNumber });
			if (obj4 != null)
			{
				obj2?.GetType().GetProperty("DockLocation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(obj2, RhinoDockLocation(DockLocation.Top));
				object obj5 = Activator.CreateInstance(_dockSitePlacementType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[2] { -1, -1 }, null);
				_dockSiteType.GetMethod("AddDockBar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(_sites[DockLocation.Top], new object[4] { obj, obj4, obj5, bandSize });
				RecalculateSite(DockLocation.Top);
			}
		}
	}

	private void HideCollection(object collection)
	{
		object dockBar = _collectionType.GetProperty("DockBar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(collection);
		if (dockBar == null)
		{
			return;
		}
		foreach (object item in _sites.Values.Where(delegate(object value)
		{
			object obj2 = _dockSiteType.GetMethod("ContainsDockBar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { dockBar.GetType() }, null)?.Invoke(value, new object[1] { dockBar });
			return obj2 is bool && (bool)obj2;
		}))
		{
			_dockSiteType.GetMethod("RemoveDockBar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { dockBar.GetType() }, null)?.Invoke(item, new object[1] { dockBar });
		}
		object obj = dockBar.GetType().GetMethod("Placement", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(uint) }, null)?.Invoke(dockBar, new object[1] { _document.RuntimeSerialNumber });
		obj?.GetType().GetProperty("Visible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(obj, false);
	}

	private bool WorkspaceContainsDockBar(object dockBar)
	{
		return _sites.Values.Any(site => SiteContainsDockBar(site, dockBar));
	}

	private object FloatingForm(object dockBar)
	{
		return dockBar.GetType().GetMethod("HasFloatingForm", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(uint) }, null)?.Invoke(dockBar, new object[1] { _document.RuntimeSerialNumber });
	}

	private bool IsPanelSelected(Guid panelId)
	{
		foreach (object item in _ownedCollections.Where(value => ContainsPanel(value, panelId)))
		{
			object obj = CollectionState(item);
			if (obj?.GetType().GetProperty("SelectedItemId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj) is Guid guid && guid == panelId)
			{
				return true;
			}
		}
		return false;
	}

	private bool ContainsPanel(object collection, Guid panelId)
	{
		object obj = _collectionType.GetMethod("ContainsItemId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(Guid) }, null)?.Invoke(collection, new object[1] { panelId });
		return obj is bool && (bool)obj;
	}

	private object CollectionState(object collection)
	{
		return _collectionType.GetMethod("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(uint) }, null)?.Invoke(collection, new object[1] { _document.RuntimeSerialNumber });
	}

	private void SelectPanel(object collection, Guid panelId)
	{
		object obj = CollectionState(collection);
		obj?.GetType().GetProperty("SelectedItemId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(obj, panelId);
		_panelActivated?.Invoke(panelId);
	}

	private object PanelDefinition(Guid panelId)
	{
		Type type = typeof(Panels).Assembly.GetType("Rhino.UI.PanelSystem", throwOnError: true);
		return type.GetMethod("Definition", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(Guid) }, null)?.Invoke(null, new object[1] { panelId });
	}

	private IEnumerable<object> SiteBands(object site)
	{
		if (!(_dockSiteType.GetProperty("Bands", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(site) is IEnumerable bands))
		{
			yield break;
		}
		foreach (object band in bands)
		{
			if (band != null)
			{
				yield return band;
			}
		}
	}

	private void RecalculateSite(DockLocation location)
	{
		((object)_siteControls[location]).GetType().GetMethod("RecalcLayout", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(_siteControls[location], null);
	}

	private object RhinoDockLocation(DockLocation location)
	{
		return Enum.Parse(_dockLocationType, location.ToString());
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			SaveHelpWorkspaceState();
			DetachHelpStatePersistence();
			_disposed = true;
			MethodInfo method = _collectionsType.GetMethod("Remove", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { _collectionType }, null);
			object[] array = _ownedCollections.ToArray();
			foreach (object obj in array)
			{
				method?.Invoke(null, new object[1] { obj });
			}
			_ownedCollections.Clear();
			_dockSitesType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(_dockSites, null);
		}
	}

	private Type RequireType(string fullName)
	{
		return _rhinoUiAssembly.GetType(fullName, throwOnError: true) ?? throw new TypeLoadException(fullName);
	}

	private static int GetInt(object target, string property)
	{
		return Convert.ToInt32(target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target) ?? ((object)0));
	}

	private static float GetFloat(object target, string property)
	{
		return Convert.ToSingle(target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target) ?? ((object)0f));
	}

	private static void SetProperty(object target, string property, object value)
	{
		target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(target, value);
	}

	private static Exception Unwrap(Exception exception)
	{
		return (exception is TargetInvocationException ex && exception.InnerException != null) ? Unwrap(ex.InnerException) : exception;
	}
}
