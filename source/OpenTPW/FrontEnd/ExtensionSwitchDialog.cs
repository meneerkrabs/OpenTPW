using OpenTPW.Mods;
using OpenTPW.UI.Original;

namespace OpenTPW.FrontEnd;

/// <summary>
/// The question a park load asks when the save's official extensions differ from the active ones in a way a player may switch (<see cref="ParkExtensionSwitch"/>): the original confirm dialog with the
/// extensions caption, one line per kind of switch ("This park was played without Rain mood. Continue with Rain mood?", "This park uses Pools, which is off. Turn Pools on and load the park?") and a closing
/// line that the park continues with the extensions that are on now. The tick loads with the switch (its tooltip is "Continue" or "Turn Pools on and load"), the X goes back to the screen the load came from.
/// </summary>
// [EXT:save-extension-switch] user-approved 2026-10-19 ("Ook ontbrekende extensie aanzetten"); the original game has no extensions
internal static class ExtensionSwitchDialog
{
	/// <summary>The extensions' names in the interface language, joined by commas.</summary>
	private static string Names( UiStringTable strings, IEnumerable<IOpenTpwExtension> extensions ) =>
		string.Join( ", ", extensions.Select( extension => ModsScreen.ExtensionName( strings, extension ) ) );

	/// <summary>The dialog's text: the caption line, then the switches and the closing line.</summary>
	internal static string Text( UiStringTable strings, ParkExtensionSwitch plan )
	{
		var lines = new List<string>();
		if ( plan.ContinueWith.Count > 0 )
			lines.Add( string.Format( strings.Extra( OpenTpwText.ExtensionSwitchPlayedWithout ), Names( strings, plan.ContinueWith ) ) );
		if ( plan.ContinueWithout.Count > 0 )
			lines.Add( string.Format( strings.Extra( OpenTpwText.ExtensionSwitchPlayedWith ), Names( strings, plan.ContinueWithout ) ) );
		if ( plan.ContinueNewVersion.Count > 0 )
			lines.Add( string.Format( strings.Extra( OpenTpwText.ExtensionSwitchOtherVersion ), Names( strings, plan.ContinueNewVersion.Select( item => item.Extension ) ) ) );
		if ( plan.TurnOn.Count > 0 )
		{
			var names = Names( strings, plan.TurnOn );
			lines.Add( string.Format( strings.Extra( OpenTpwText.ExtensionSwitchNeeds ), names ) + " " + string.Format( strings.Extra( OpenTpwText.ExtensionSwitchTurnOnQuestion ), names ) );
		}
		lines.Add( strings.Extra( OpenTpwText.ExtensionSwitchNewRun ) );
		return strings.Extra( OpenTpwText.ModsExtensions ) + "\n" + string.Join( "\n", lines );
	}

	/// <summary>The tick's label: "Turn X on and load" when content extensions are switched on, else "Continue".</summary>
	internal static string ConfirmLabel( UiStringTable strings, ParkExtensionSwitch plan ) => plan.TurnOn.Count > 0
		? string.Format( strings.Extra( OpenTpwText.ExtensionSwitchTurnOn ), Names( strings, plan.TurnOn ) )
		: strings.Extra( OpenTpwText.ExtensionSwitchContinue );

	/// <summary>
	/// The dialog for <paramref name="plan"/> on <paramref name="stack"/>: the tick pops it and runs <paramref name="confirmed"/> (once), the X, Escape and the right click pop it and run
	/// <paramref name="cancelled"/>.
	/// </summary>
	internal static UiScreen Create( UiScreenStack stack, UiStringTable strings, ParkExtensionSwitch plan, Action confirmed, Action? cancelled = null )
	{
		ArgumentNullException.ThrowIfNull( plan );
		ProgressChoiceScreen? screen = null;
		bool Current() => !screen!.IsRemoved && ReferenceEquals( stack.Top, screen );
		screen = ProgressChoiceScreen.Message( "extensionSwitch", () => Text( strings, plan ),
			(() => ConfirmLabel( strings, plan ), () =>
			{
				if ( !Current() || !screen!.TryActivate( stack ) )
					return;
				stack.Pop();
				confirmed();
			}),
			(() => strings.Extra( OpenTpwText.Back ), () =>
			{
				if ( !Current() )
					return;
				stack.Pop();
				cancelled?.Invoke();
			}) );
		return screen;
	}
}
