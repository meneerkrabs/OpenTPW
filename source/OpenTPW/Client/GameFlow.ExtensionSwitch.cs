using OpenTPW.FrontEnd;
using OpenTPW.Mods;

namespace OpenTPW;

// [EXT:save-extension-switch] user-approved 2026-10-19 ("Ook ontbrekende extensie aanzetten"): loading an own save whose official extensions differ asks to continue with the active rules, or to switch on the content
// extensions the park uses, instead of refusing (docs/MODS.md, "Loading a park with other extensions"); [EXT:extensions-apply-at-load] a content extension switched on here applies at once, without a restart
internal sealed partial class GameFlow
{
	/// <summary>The <c>mods.json</c> a content extension switched on at a load is written to (tests point it elsewhere).</summary>
	internal string ModSettingsPath { get; set; } = ModSettings.GetDefaultPath();

	/// <summary>
	/// Before the own save at <paramref name="savePath"/> loads: when its extensions differ in a way the player may switch (<see cref="ParkExtensionSwitch.CanSwitch"/>), the switch dialog asks, and
	/// <paramref name="proceed"/> runs with the confirmed switch; otherwise it runs at once with null and the load is strict (it agrees, or refuses with its reasons). A save that cannot be read here is left to the
	/// load, which says why.
	/// </summary>
	internal void ConfirmExtensions( string? savePath, Action<ParkExtensionSwitch?> proceed ) => ConfirmExtensions( savePath, proceed, Hud?.Stack ?? Menu?.Stack );

	/// <summary><see cref="ConfirmExtensions(string?, Action{ParkExtensionSwitch?})"/> on an explicit screen stack (the native park-resume smoke's switch variant, which has no menu).</summary>
	internal void ConfirmExtensions( string? savePath, Action<ParkExtensionSwitch?> proceed, OpenTPW.UI.Original.UiScreenStack? stack )
	{
		ArgumentNullException.ThrowIfNull( proceed );
		ParkExtensionSwitch? plan = null;
		if ( savePath != null && !GameEditions.IsThemeParkInc )
		{
			try { plan = ParkExtensionSwitch.Plan( ParkResumeSaveFile.Load( savePath ).Mods, OpenTpwExtensions.Active ); }
			catch ( Exception error ) when ( error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException ) { plan = null; }
		}
		if ( plan is not { CanSwitch: true } || stack == null )
		{
			proceed( null );
			return;
		}
		Log.Trace( $"Park save {Path.GetFileName( savePath )} differs in its extensions; asking to switch: {Describe( plan )}." );
		stack.Push( ExtensionSwitchDialog.Create( stack, Strings, plan, () => proceed( plan ) ) );
	}

	/// <summary>Makes <paramref name="before"/> active again after a failed load; a failure of that itself is logged and never hides the load's own error.</summary>
	private static void SwitchBack( ModSet before, Exception loadError )
	{
		try { ModStartup.ApplyExtensions( before, Log.Trace ); }
		catch ( Exception error )
		{
			Log.Warning( $"The extensions could not be switched back after a failed load ({loadError.Message}): {error.Message}" );
		}
	}

	private static string Describe( ParkExtensionSwitch plan )
	{
		static string Ids( IEnumerable<IOpenTpwExtension> extensions ) => string.Join( ", ", extensions.Select( extension => extension.Id ) );
		var parts = new List<string>();
		if ( plan.ContinueWith.Count > 0 ) parts.Add( "continue with " + Ids( plan.ContinueWith ) );
		if ( plan.ContinueWithout.Count > 0 ) parts.Add( "continue without " + Ids( plan.ContinueWithout ) );
		if ( plan.ContinueNewVersion.Count > 0 ) parts.Add( "continue with this version of " + Ids( plan.ContinueNewVersion.Select( item => item.Extension ) ) );
		if ( plan.TurnOn.Count > 0 ) parts.Add( "turn on " + Ids( plan.TurnOn ) );
		return string.Join( "; ", parts );
	}

	/// <summary>
	/// The switched load: the plan is made again for the save as it is read now and must be the one the player confirmed (otherwise the strict load runs and says what is unmet); the content extensions are switched
	/// on for this run (<see cref="ModStartup.ApplyExtensions"/>), the save is rewritten for the switched set (<see cref="ParkExtensionSwitch.Apply"/>) and resumed. A failed load switches the extensions back; a
	/// successful one writes the extensions it switched on to <c>mods.json</c>, as the Extensions and mods screen would.
	/// </summary>
	private Level ResumeSwitched( ParkResumeSnapshot snapshot, bool restart, ParkExtensionSwitch confirmed )
	{
		var before = OpenTpwExtensions.Active;
		var plan = ParkExtensionSwitch.Plan( snapshot.Mods, before );
		if ( plan == null || !plan.CanSwitch || !plan.SameAs( confirmed ) )
			return ResumeStrict( snapshot, restart );
		var after = plan.ActiveAfter( before );
		var switched = !ReferenceEquals( after, before );
		if ( switched )
			ModStartup.ApplyExtensions( after, Log.Trace );
		var live = Level;
		Level level;
		try
		{
			level = ResumeStrict( plan.Apply( snapshot, after ), restart );
		}
		catch ( Exception error )
		{
			// switched back only while the park that was running is still the live one: once the switched park has replaced it (a failure after the swap), it runs with the extensions it was built with
			if ( switched && ReferenceEquals( Level, live ) )
				SwitchBack( before, error );
			throw;
		}
		Log.Trace( $"Resumed {snapshot.Theme} park with switched extensions ({Describe( plan )}); it continues as a new run from here." );
		if ( plan.TurnOn.Count > 0 && PersistSettings )
		{
			try { ModStartup.SaveExtensionChoices( ModSettingsPath, plan.TurnOn.ToDictionary( extension => extension.Id, _ => true ) ); }
			catch ( Exception error ) when ( error is IOException or UnauthorizedAccessException )
			{
				// the park runs with the extension; only the choice for the next start could not be kept
				Log.Warning( $"The extensions switched on for this park could not be saved in {ModSettings.FileName}: {error.Message}" );
			}
		}
		return level;
	}
}
