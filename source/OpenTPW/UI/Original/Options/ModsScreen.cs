using OpenTPW.Mods;

namespace OpenTPW.UI.Original;

/// <summary>[EXT:extensions-apply-at-load] When extension choices made on the Extensions and mods screen take effect.</summary>
public enum ExtensionChoiceTiming
{
	/// <summary>No park is running: the choices are active now and the next park started or loaded uses them.</summary>
	NextPark,
	/// <summary>A park is running: the choices take effect when the player leaves it or loads another park.</summary>
	AfterThisPark,
	/// <summary>The choices need a new start (or a page reload in the browser).</summary>
	NextStart,
}

/// <summary>What the Extensions and mods screen reads and does outside itself; every member has the game's real default.</summary>
public sealed class ModsServices
{
	/// <summary>The mod folders found at startup.</summary>
	public Func<IReadOnlyList<DiscoveredMod>> Discovered { get; init; } = () => ModStartup.Discovered;
	/// <summary>The <c>mods.json</c> the choices are read from and saved to.</summary>
	public string SettingsPath { get; init; } = ModSettings.GetDefaultPath();
	/// <summary>The extensions this build has.</summary>
	public IReadOnlyList<IOpenTpwExtension> Extensions { get; init; } = OfficialExtensions.All;
	/// <summary>The extensions and mods of this run, for the first display before the choices change.</summary>
	public Func<ModSet> Active { get; init; } = () => OpenTpwExtensions.Active;
	public ModVersion? OpenTpwVersion { get; init; } = ModStartup.OpenTpwVersion();
	public Func<GameEdition> Edition { get; init; } = () => GameEditions.Current;
	/// <summary>Whether script mods are off in this run (safe mode, docs/SCRIPT-MODS.md).</summary>
	public Func<bool> ScriptModsOff { get; init; } = () => ScriptSafeMode.Active;
	/// <summary>
	/// The browser build: no folder dialog of the game, no helper process and no thread pool. A mod input is the copy of the player's own game
	/// files that the page keeps in memory (<see cref="BrowserModInputs"/>), chosen with the page's folder dialog.
	/// </summary>
	public bool Browser { get; init; } = OperatingSystem.IsBrowser();
	/// <summary>
	/// [EXT:extensions-apply-at-load] Applies the official extensions the player switched (id to on) without a restart where it can and says when they take effect (the game's flow,
	/// <c>GameFlow.ApplyExtensionChoices</c>). Null (the default, and while a community mod or an input changed too) leaves every change to the next start.
	/// </summary>
	public Func<IReadOnlyDictionary<string, bool>, ExtensionChoiceTiming>? ApplyExtensions { get; init; }
	/// <summary>
	/// Inspects a folder away from the game's threads (a helper process by default, like the Game files screen). The browser inspects its copy
	/// in this process, at once.
	/// </summary>
	internal Func<string, CancellationToken, Task<InstallationDiscoveryResult>> Inspect { get; init; } = OperatingSystem.IsBrowser()
		? ( path, _ ) => Task.FromResult( new InstallationDiscoveryResult( new[] { GameInstallation.Inspect( path ) }, false, null ) )
		: ( path, cancellation ) => InstallationDiscovery.InspectAsync( path, cancellation: cancellation );
}

/// <summary>
/// The Extensions and mods screen (docs/MODS.md), reached from the OpenTPW options page beside Game files: two columns,
/// the official extensions and the community mods, each a list with its own original <c>b_on</c> switch for the selected row,
/// a version and status line per row (on, off, incompatible, missing input folder, conflict, needs another mod), a details
/// pane (author, content digest, needs, folder, why a mod is refused) and, for a mod that needs an original game folder,
/// the folder chooser of the Game files screen with the check of that folder (<see cref="ModInputCheck"/>, which uses
/// <see cref="GameEditions.Detect"/> and <see cref="GameEditions.HasKnownStandardSam"/>). The statuses come from resolving the real
/// <see cref="ModSet"/> with the pending choices. OK writes <c>mods.json</c>; [EXT:extensions-apply-at-load] official extensions switched on or off apply at the next park start or load without
/// a restart (<see cref="ModsServices.ApplyExtensions"/>), and the screen says so; a change to a community mod or its input, and the cases the flow cannot switch live, apply after a restart, and the screen says that. Cancel
/// and Escape discard them. In the browser build the community list holds the mods shipped with the page, and a mod input is
/// the page's copy of the player's own game files (<see cref="BrowserModInputs"/>), chosen with the page's folder dialog.
/// </summary>
// [EXT:mods-ui] OpenTPW's own mods screen; the original game had no mods
public static class ModsScreen
{
	/// <summary>The extensions whose name and description the screen shows in the interface language.</summary>
	private static readonly IReadOnlyDictionary<string, (OpenTpwText Name, OpenTpwText Description)> ExtensionTexts = new Dictionary<string, (OpenTpwText, OpenTpwText)>
	{
		[OfficialExtensions.SmarterGuests.Id] = (OpenTpwText.ExtensionSmarterGuestsName, OpenTpwText.ExtensionSmarterGuestsDescription),
		[OfficialExtensions.GuestInsights.Id] = (OpenTpwText.ExtensionGuestInsightsName, OpenTpwText.ExtensionGuestInsightsDescription),
		[OfficialExtensions.PoolGuestArt.Id] = (OpenTpwText.ExtensionPoolGuestArtName, OpenTpwText.ExtensionPoolGuestArtDescription),
		[OfficialExtensions.RainMood.Id] = (OpenTpwText.ExtensionRainMoodName, OpenTpwText.ExtensionRainMoodDescription),
		[OfficialExtensions.Umbrellas.Id] = (OpenTpwText.ExtensionUmbrellasName, OpenTpwText.ExtensionUmbrellasDescription),
		[OfficialExtensions.Winter.Id] = (OpenTpwText.ExtensionWinterName, OpenTpwText.ExtensionWinterDescription),
		[OfficialExtensions.WinterWeather.Id] = (OpenTpwText.ExtensionWinterWeatherName, OpenTpwText.ExtensionWinterWeatherDescription),
		[OfficialExtensions.WinterCold.Id] = (OpenTpwText.ExtensionWinterColdName, OpenTpwText.ExtensionWinterColdDescription),
		[OfficialExtensions.WinterAlways.Id] = (OpenTpwText.ExtensionWinterAlwaysName, OpenTpwText.ExtensionWinterAlwaysDescription),
		[OfficialExtensions.RestoredAdvice.Id] = (OpenTpwText.ExtensionRestoredAdviceName, OpenTpwText.ExtensionRestoredAdviceDescription),
		[OfficialExtensions.Evening.Id] = (OpenTpwText.ExtensionEveningName, OpenTpwText.ExtensionEveningDescription),
		[OfficialExtensions.WinterSnow.Id] = (OpenTpwText.ExtensionWinterSnowName, OpenTpwText.ExtensionWinterSnowDescription),
		[OfficialExtensions.Cheats.Id] = (OpenTpwText.ExtensionCheatsName, OpenTpwText.ExtensionCheatsDescription),
	};

	/// <summary>
	/// An extension's name in the interface language where the screen has one ([EXT:save-extension-switch] and the pools extension's, for the load dialog), else the name the registry gives.
	/// </summary>
	internal static string ExtensionName( UiStringTable strings, IOpenTpwExtension extension ) =>
		ExtensionTexts.TryGetValue( extension.Id, out var texts ) ? strings.Extra( texts.Name )
		: extension.Id == OfficialExtensions.Pools.Id ? strings.Extra( OpenTpwText.ExtensionPoolsName ) : extension.Name;

	public static UiScreen Create( UiScreenStack stack, UiStringTable strings, ModsServices? services = null )
	{
		services ??= new ModsServices();
		var discovered = services.Discovered();
		var diagnostics = new List<string>();
		var saved = ModSettings.Load( services.SettingsPath, diagnostics );
		// the browser's inputs are the page's copies: an input without a saved folder uses its edition's copy when there is one
		if ( services.Browser )
			saved = BrowserModInputs.WithDefaults( saved, discovered );
		var pending = saved;
		var version = 0;
		// The rows of the pending choices: the startup's resolution first, then the resolution of each change.
		// Resolved again at once and away from the game's thread (hashing a large mod takes a moment); until then enabled mods read "Checking...".
		var resolved = services.Active();
		var resolvedVersion = -1;
		Task<(ModSet? Set, string? Error, int Version)>? resolution = null;
		IReadOnlyList<ModRow> rows = ModRows.Build( discovered, pending, resolved, services.Extensions, current: false );
		void Rebuild() => rows = ModRows.Build( discovered, pending, resolved, services.Extensions, resolvedVersion == version );
		void Changed()
		{
			version++;
			Rebuild();
		}

		var status = diagnostics.Count > 0 ? diagnostics[0] : services.ScriptModsOff() ? strings.Extra( OpenTpwText.ModSafeMode ) : "";
		var inputIndex = 0;
		var note = "";
		var checks = new Dictionary<string, ModInputCheck>( StringComparer.Ordinal );
		Task<InstallationDiscoveryResult>? picker = null;
		Task<InstallationDiscoveryResult>? inspection = null;
		Action<InstallationReport?>? inspected = null;
		Action<string>? pickerTarget = null;
		var lifetime = new CancellationTokenSource();

		var screen = new UiScreen( "mods" );
		screen.Removed = () => { lifetime.Cancel(); lifetime.Dispose(); };
		var page = new OptionsScreen.PageBuilder( screen );
		page.Frame( () => strings.Extra( OpenTpwText.ModsPage ) );
		static UiRect Rect( float left, float top, float right, float bottom ) => OptionsScreen.PageBuilder.Rect( left, top, right, bottom );

		IReadOnlyList<ModRow> Extensions() => rows.Where( row => row.IsExtension ).ToList();
		IReadOnlyList<ModRow> Community() => rows.Where( row => !row.IsExtension ).ToList();
		string StatusText( ModRowStatus rowStatus ) => strings.Extra( rowStatus switch
		{
			ModRowStatus.On => OpenTpwText.ModOn,
			ModRowStatus.Off => OpenTpwText.ModOff,
			ModRowStatus.Incompatible => OpenTpwText.ModIncompatible,
			ModRowStatus.MissingInput => OpenTpwText.ModMissingInput,
			ModRowStatus.Conflict => OpenTpwText.ModConflict,
			ModRowStatus.NeedsOther => OpenTpwText.ModNeedsOther,
			ModRowStatus.Pending => OpenTpwText.ModChecking,
			ModRowStatus.SafeMode => OpenTpwText.ModOffSafeMode,
			_ => OpenTpwText.ModUnusable,
		} );
		string VersionText( ModRow row ) => row.Version == null ? "" : "  " + row.Version;
		// [EXT:smarter-guests] the guest extensions carry their names and descriptions in the interface languages; the others show the name the registry gives
		string RowName( ModRow row ) => ExtensionTexts.TryGetValue( row.Id, out var texts ) && row.Extension != null ? strings.Extra( texts.Name ) : row.Name;

		UiScrollList AddColumn( string id, float left, OpenTpwText title, Func<IReadOnlyList<ModRow>> source, Func<string> empty )
		{
			var right = left + 943;
			page.Panel( id + "Header", "f_optpanel", Rect( left, 167, right, 316 ) );
			page.Label( id + "Header", Rect( left + 70, 218, left + 700, 263 ), () => strings.Extra( title ) );
			var list = screen.Add( new UiScrollList
			{
				Id = id,
				Bounds = Rect( left, 330, right, 770 ),
				Anchor = UiAnchor.Center,
				RowHeight = 64,
				Rows = () => source().Count == 0 ? new[] { empty() } : source().Select( row => RowName( row ) + VersionText( row ) ).ToArray(),
				Trailing = index => index < source().Count ? StatusText( source()[index].Status ) : "",
			} );
			return list;
		}
		var extensionList = AddColumn( "extensions", 57, OpenTpwText.ModsExtensions, Extensions, () => "" );
		var modList = AddColumn( "mods", 1048, OpenTpwText.ModsCommunity, Community, () => strings.Extra( OpenTpwText.ModNoneInstalled ) );

		ModRow? Selected( UiScrollList list, IReadOnlyList<ModRow> source ) => list.Selected >= 0 && list.Selected < source.Count ? source[list.Selected] : null;
		ModRow? ExtensionRow() => Selected( extensionList, Extensions() );
		ModRow? ModRowSelected() => Selected( modList, Community() );
		// The row the details and the input chooser show: the list that was used last.
		var detailsOnMods = false;
		ModRow? Shown() => detailsOnMods ? ModRowSelected() : ExtensionRow();
		extensionList.SelectionChanged = _ => { detailsOnMods = false; inputIndex = 0; note = ""; };
		modList.SelectionChanged = _ => { detailsOnMods = true; inputIndex = 0; note = ""; };

		void Toggle( ModRow? row )
		{
			if ( row is not { CanToggle: true } )
				return;
			pending = pending.WithEnabled( row.Id, !row.Enabled );
			Changed();
		}
		page.Toggle( "extensionsOn", "b_on", Rect( 57 + 790, 190, 57 + 893, 292 ), () => ExtensionRow()?.Enabled == true, () => Toggle( ExtensionRow() ) );
		page.Toggle( "modsOn", "b_on", Rect( 1048 + 790, 190, 1048 + 893, 292 ), () => ModRowSelected()?.Enabled == true, () => Toggle( ModRowSelected() ) );
		extensionList.RowActivated = _ => Toggle( ExtensionRow() );
		modList.RowActivated = _ => Toggle( ModRowSelected() );
		// [EXT:mods] activating a row toggles it like the On/Off button beside the list, with the button's click; the original has no mods screen (its list rows are silent, UiScrollList.Activate)
		extensionList.ClicksOnActivate = modList.ClicksOnActivate = true;

		string Details()
		{
			if ( Shown() is not { } row )
				return strings.Extra( OpenTpwText.ModSelectHint );
			var lines = new List<string> { $"{RowName( row )} ({row.Id})" };
			if ( row.Extension != null )
			{
				if ( ExtensionTexts.TryGetValue( row.Id, out var texts ) )
					lines.Add( strings.Extra( texts.Description ) );
				lines.Add( $"{strings.Extra( OpenTpwText.ModVersionLabel )}: {row.Version}" );
				lines.Add( strings.Extra( OpenTpwText.ModBuiltIn ) );
				lines.Add( strings.Extra( row.Extension.AffectsSimulation ? OpenTpwText.ModAffectsSimulation : OpenTpwText.ModLeavesSimulation ) );
				return string.Join( "\n", lines );
			}
			var version = row.Version == null ? "" : $"{strings.Extra( OpenTpwText.ModVersionLabel )}: {row.Version}";
			if ( row.Manifest?.Author is { Length: > 0 } author )
				version += $"   {strings.Extra( OpenTpwText.ModAuthorLabel )}: {author}";
			if ( version.Length > 0 )
				lines.Add( version );
			if ( row.Manifest?.Description is { Length: > 0 } description )
				lines.Add( description.Length > 160 ? description[..160] + "..." : description );
			if ( row.Manifest != null )
				lines.Add( $"{strings.Extra( OpenTpwText.ModDigestLabel )}: {row.Digest ?? strings.Extra( OpenTpwText.ModDigestPending )}" );
			if ( row.Manifest?.Content.Scripts != null )
				lines.Add( strings.Extra( OpenTpwText.ModContainsScripts ) );
			if ( row.Manifest?.Dependencies is { Count: > 0 } dependencies )
				lines.Add( $"{strings.Extra( OpenTpwText.ModNeedsLabel )}: {string.Join( ", ", dependencies.Select( dependency => $"{dependency.Id} {dependency.Version}" ) )}" );
			if ( row.Directory != null )
				lines.Add( $"{strings.Extra( OpenTpwText.ModFolderLabel )}: {row.Directory}" );
			if ( row.Reason != null )
				lines.Add( $"{strings.Extra( OpenTpwText.ModReasonLabel )}: {row.Reason}" );
			return string.Join( "\n", lines );
		}
		screen.Add( new UiLabel
		{
			Id = "details",
			Text = Details,
			Font = fonts => fonts.Small,
			Color = UiColors.OptionText,
			Shadow = false,
			Wrap = true,
			Bounds = Rect( 57, 785, 1991, 1065 ),
			Anchor = UiAnchor.Center
		} );

		// The input chooser of the selected community mod (an original game folder the mod reads).
		ModInput? CurrentInput()
		{
			var inputs = detailsOnMods ? ModRowSelected()?.Manifest?.Inputs : null;
			return inputs is { Count: > 0 } ? inputs[Math.Clamp( inputIndex, 0, inputs.Count - 1 )] : null;
		}
		string InputName( ModInput input )
		{
			var name = strings.Extra( input.Edition switch
			{
				"theme-park-inc" => OpenTpwText.ModInputTpi,
				"theme-park-world" => OpenTpwText.ModInputTpw,
				_ => OpenTpwText.ModInputOriginal,
			} );
			return ModRowSelected()?.Manifest?.Inputs.Count > 1 ? $"{name} ({input.Id})" : name;
		}
		string? InputPath( ModInput input ) => ModRowSelected() is { } row ? pending.InputPath( row.Id, input.Id ) : null;
		static string CheckKey( string path, ModInput input ) => path + "|" + input.Edition;
		string EditionName( GameEdition edition ) => strings.Extra( edition == GameEdition.ThemeParkInc ? OpenTpwText.EditionThemeParkInc : OpenTpwText.EditionThemeParkWorld );
		string CheckText( ModInput input, ModInputCheck check ) => check.State switch
		{
			ModInputState.Valid => input.Edition switch
			{
				"theme-park-inc" => strings.Extra( check.Evidence == GameEditionEvidence.StandardSamHash ? OpenTpwText.ModInputFoundTpiKnown : OpenTpwText.ModInputFoundTpiStructure ),
				"theme-park-world" => strings.Extra( OpenTpwText.ModInputFoundTpw ),
				_ => strings.Extra( OpenTpwText.ModInputFound ),
			},
			ModInputState.WrongEdition => string.Format( strings.Extra( OpenTpwText.ModInputWrongEdition ), EditionName( check.Detected ?? GameEdition.ThemeParkWorld ),
				EditionName( ModInputCheck.EditionOf( input ) ?? GameEdition.ThemeParkWorld ) ),
			ModInputState.NotFound => strings.Extra( OpenTpwText.ModInputNotFound ),
			_ => strings.Extra( OpenTpwText.ModInputNotChosen ),
		};
		string InputStatus()
		{
			if ( CurrentInput() is not { } input )
				return "";
			if ( note.Length > 0 )
				return note;
			if ( InputPath( input ) is not { } path )
				return strings.Extra( OpenTpwText.ModInputNotChosen );
			return checks.TryGetValue( CheckKey( path, input ), out var check ) ? CheckText( input, check ) : strings.Extra( OpenTpwText.ModInputChecking );
		}
		screen.Add( new UiLabel
		{
			Id = "inputPath",
			Text = () => CurrentInput() is { } input ? $"{InputName( input )}: {InputPath( input ) ?? strings.Extra( OpenTpwText.NoFolder )}" : "",
			Font = fonts => fonts.Small,
			Color = UiColors.Value,
			Shadow = false,
			Wrap = true,
			Bounds = Rect( 57, 1075, 1991, 1170 ),
			Anchor = UiAnchor.Center
		} );
		screen.Add( new UiLabel { Id = "inputStatus", Text = InputStatus, Font = fonts => fonts.Small, Color = UiColors.OptionText, Shadow = false, Wrap = true,
			Bounds = Rect( 57, 1175, 1991, 1235 ), Anchor = UiAnchor.Center } );

		void Inspect( string path, Action<InstallationReport?> apply )
		{
			if ( inspection != null )
				return;
			inspected = apply;
			inspection = services.Inspect( path, lifetime.Token );
		}
		void SetInput( string chosen )
		{
			if ( ModRowSelected() is not { } row || CurrentInput() is not { } input )
				return;
			Inspect( chosen, report =>
			{
				var check = report == null ? new ModInputCheck( ModInputState.NotFound, null, GameEditionEvidence.None ) : ModInputCheck.Evaluate( report, input );
				if ( report == null || check.State != ModInputState.Valid )
				{
					// Not saved: the folder is not what the mod asks for.
					note = CheckText( input, check );
					return;
				}
				note = "";
				checks[CheckKey( report.Path, input )] = check;
				pending = pending.WithInput( row.Id, input.Id, report.Path );
				Changed();
			} );
		}
		var seenRevision = BrowserModInputs.Revision;
		var choosing = false;
		void ChooseInBrowser( ModInput input )
		{
			if ( BrowserModInputs.FolderFor( input ) is not { } folder || BrowserModInputs.Choose == null )
			{
				note = strings.Extra( OpenTpwText.ModInputBrowserNone );
				return;
			}
			if ( BrowserModInputs.IsLocked )
			{
				note = strings.Extra( OpenTpwText.ModInputBrowserLocked );
				return;
			}
			choosing = true;
			note = strings.Extra( OpenTpwText.ModInputBrowserChoosing );
			BrowserModInputs.Choose( input.Edition! );
		}
		void Browse( Action<string> target, string? initial )
		{
			if ( picker != null )
				return;
			// Without a platform dialog (Linux without zenity or kdialog) the path is typed instead.
			if ( !FolderPicker.IsAvailable )
			{
				stack.Push( GameFilesScreen.PathEntry( stack, strings, initial, target ) );
				return;
			}
			pickerTarget = target;
			picker = InstallationDiscovery.PickAsync( CurrentInput() is { } input ? InputName( input ) : strings.Extra( OpenTpwText.ModsPage ), initial, lifetime.Token );
		}
		var inputChange = screen.Add( new UiButton { Id = "inputChange", Text = () => strings.Extra( OpenTpwText.ChangeFolder ),
			Clicked = () =>
			{
				if ( CurrentInput() is not { } input )
					return;
				if ( services.Browser )
					ChooseInBrowser( input );
				else
					Browse( SetInput, InputPath( input ) );
			},
			Bounds = Rect( 57, 1245, 457, 1320 ), Anchor = UiAnchor.Center } );
		var inputType = screen.Add( new UiButton { Id = "inputType", Text = () => strings.Extra( OpenTpwText.EnterFolderPath ),
			Clicked = () => { if ( CurrentInput() is { } input ) stack.Push( GameFilesScreen.PathEntry( stack, strings, InputPath( input ), SetInput ) ); },
			Bounds = Rect( 477, 1245, 877, 1320 ), Anchor = UiAnchor.Center } );
		var inputRemove = screen.Add( new UiButton { Id = "inputRemove", Text = () => strings.Extra( OpenTpwText.RemoveFolder ),
			Clicked = () =>
			{
				if ( ModRowSelected() is { } row && CurrentInput() is { } input )
				{
					note = "";
					pending = pending.WithInput( row.Id, input.Id, null );
					Changed();
				}
			},
			Bounds = Rect( 897, 1245, 1197, 1320 ), Anchor = UiAnchor.Center } );
		var inputNext = screen.Add( new UiButton { Id = "inputNext", Text = () => strings.Extra( OpenTpwText.ModInputNext ),
			Clicked = () => { inputIndex = (inputIndex + 1) % (ModRowSelected()?.Manifest?.Inputs.Count ?? 1); note = ""; },
			Bounds = Rect( 1217, 1245, 1617, 1320 ), Anchor = UiAnchor.Center } );

		screen.Add( new UiLabel { Id = "status", Text = () => status, Font = fonts => fonts.Small, Color = UiColors.OptionText, Shadow = false, Wrap = true,
			Bounds = Rect( 57, 1335, 1700, 1440 ), Anchor = UiAnchor.Center } );

		// The same effective choices (an extension without an entry has its default) need no write and no restart.
		bool Differs() => ExtensionChanges().Count > 0 || CommunityDiffers();
		// [EXT:extensions-apply-at-load] the official extensions this screen switched (against mods.json as it was), which may apply without a restart
		Dictionary<string, bool> ExtensionChanges() => services.Extensions.Where( extension => saved.IsEnabled( extension.Id, extension.DefaultEnabled ) != pending.IsEnabled( extension.Id, extension.DefaultEnabled ) )
			.ToDictionary( extension => extension.Id, extension => pending.IsEnabled( extension.Id, extension.DefaultEnabled ), StringComparer.Ordinal );
		bool CommunityDiffers()
		{
			foreach ( var mod in discovered.Where( mod => mod.IsValid ) )
			{
				if ( saved.IsEnabled( mod.Id, false ) != pending.IsEnabled( mod.Id, false ) )
					return true;
				foreach ( var input in mod.Manifest!.Inputs )
					if ( saved.InputPath( mod.Id, input.Id ) != pending.InputPath( mod.Id, input.Id ) )
						return true;
			}
			return false;
		}
		void Accept()
		{
			if ( Differs() )
			{
				try
				{
					pending.Save( services.SettingsPath );
				}
				catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
				{
					status = exception.Message;
					return;
				}
				// [EXT:extensions-apply-at-load] extensions alone apply without a restart where the flow can switch them; a community mod or an input needs the next start for everything
				var changes = ExtensionChanges();
				var timing = services.ApplyExtensions != null && changes.Count > 0 && !CommunityDiffers() ? services.ApplyExtensions( changes ) : ExtensionChoiceTiming.NextStart;
				var notice = timing switch
				{
					ExtensionChoiceTiming.NextPark => OpenTpwText.ModsSavedNextPark,
					ExtensionChoiceTiming.AfterThisPark => OpenTpwText.ModsSavedAfterThisPark,
					_ => OpenTpwText.ModsSaved,
				};
				stack.Pop();
				stack.Push( UiDialogs.Message( "modsSaved", () => strings.Extra( notice ), (() => strings.Extra( OpenTpwText.Back ), stack.Pop) ) );
				return;
			}
			stack.Pop();
		}
		void Cancel() => stack.Pop();
		// [DATA:options table 0x4b0dc:120031,OK,Cancel] bottom-right panel with the OK and Cancel buttons, as on the options pages
		screen.Add( new UiModelImage { Id = "buttonsPanel", Model = "!f_plain", Bounds = Rect( 1742, 1321, 1947, 1443 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "ok", Model = "b_okay", Help = strings.Help( 400 ), Clicked = Accept, Bounds = Rect( 1763, 1340, 1846, 1424 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "cancel", Model = "b_exit", Help = strings.Help( 2 ), Clicked = Cancel, Bounds = Rect( 1853, 1340, 1936, 1424 ), Anchor = UiAnchor.Center } );
		screen.Back = Cancel;

		screen.Updating += _ =>
		{
			if ( picker is { IsCompleted: true } )
			{
				var chosen = picker.Status == TaskStatus.RanToCompletion ? picker.Result.Reports.FirstOrDefault()?.Path : null;
				if ( chosen != null )
					pickerTarget?.Invoke( chosen );
				picker = null;
				pickerTarget = null;
			}
			if ( inspection is { IsCompleted: true } )
			{
				inspected?.Invoke( inspection.IsCompletedSuccessfully ? inspection.Result.Reports.FirstOrDefault() : null );
				inspection = null;
				inspected = null;
			}
			if ( resolution is { IsCompleted: true } )
			{
				// Only the latest choice's result counts: an older one (the player changed a choice meanwhile) is dropped
				// and its rows stay "Checking..." until the newer resolution lands.
				var (set, error, forVersion) = resolution.Result;
				resolution = null;
				if ( forVersion == version )
				{
					if ( set != null )
						resolved = set;
					else
						status = error ?? "";
					resolvedVersion = forVersion;
					Rebuild();
				}
			}
			if ( resolution == null && resolvedVersion != version )
			{
				var settings = pending;
				var target = version;
				var found = discovered;
				var edition = services.Edition();
				var scriptsOff = services.ScriptModsOff();
				(ModSet? Set, string? Error, int Version) Resolve()
				{
					try
					{
						return (ModSet.Resolve( found, settings, services.OpenTpwVersion, edition, services.Extensions, scriptModsOff: scriptsOff ), null, target);
					}
					catch ( Exception exception )
					{
						// any fault while resolving (a script compiler bug included) is shown as a message, never thrown out of the background task
						return (null, exception is IOException or UnauthorizedAccessException or InvalidOperationException ? exception.Message : $"Resolving the mods failed ({exception.GetType().Name})", target);
					}
				}
				// Without mod folders, and in the browser (which cannot rely on a thread pool; its shipped mods are two small files), there is little to read or hash: resolve at once.
				resolution = found.Count == 0 || services.Browser ? Task.FromResult( Resolve() ) : Task.Run( Resolve );
			}
			// Check a folder chosen earlier once (in the helper process) when its mod is shown.
			if ( inspection == null && CurrentInput() is { } input && InputPath( input ) is { } path && !checks.ContainsKey( CheckKey( path, input ) ) )
			{
				var key = CheckKey( path, input );
				Inspect( path, report => checks[key] = report == null ? new ModInputCheck( ModInputState.NotFound, null, GameEditionEvidence.None ) : ModInputCheck.Evaluate( report, input ) );
			}
			// The page finished copying (or refused) the player's files: look at the folder again and take it as the input.
			if ( services.Browser && BrowserModInputs.Revision != seenRevision )
			{
				seenRevision = BrowserModInputs.Revision;
				choosing = false;
				checks.Clear();
				note = BrowserModInputs.Message ?? "";
				pending = BrowserModInputs.WithDefaults( pending, discovered );
				Changed();
			}
			var busy = picker != null || inspection != null || choosing;
			var hasInput = CurrentInput() != null;
			foreach ( var element in new UiElement[] { inputChange, inputType, inputRemove } )
				element.Visible = hasInput;
			// The browser has no typed paths and its copy is replaced, not removed, here (the page's setup screen removes the kept files).
			if ( services.Browser )
				inputType.Visible = inputRemove.Visible = false;
			inputNext.Visible = hasInput && (ModRowSelected()?.Manifest?.Inputs.Count ?? 0) > 1;
			inputChange.Enabled = !busy;
			inputType.Enabled = inspection == null;
			inputRemove.Enabled = CurrentInput() is { } current && InputPath( current ) != null && !busy;
			((UiButton)screen.Find( "extensionsOn" )!).Enabled = ExtensionRow() is { CanToggle: true };
			((UiButton)screen.Find( "modsOn" )!).Enabled = ModRowSelected() is { CanToggle: true };
		};
		screen.Focus( extensionList );
		return screen;
	}
}
