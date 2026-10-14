using OpenTPW.UI.Original;

namespace OpenTPW.FrontEnd;

public sealed partial class FrontEndMenu
{
	/// <summary>Whether the enter-park sound (cat_globallobbysfx 4) plays for the game mode that follows: not in Instant Action (game type 2).</summary>
	// [BIN:STP-PPC:0x10183CC0 enter park] the theme islands' enter action plays cat_globallobbysfx 4 unless the game type that follows is 2 (Instant Action); Mac-traced, the PC enter action was not checked
	internal static bool EnterParkSoundPlays( bool instantAction ) => !instantAction;

	private static void PlayEnterPark( bool instantAction )
	{
		if ( EnterParkSoundPlays( instantAction ) )
			GameAudio.PlayEnterPark();
	}

	/// <summary>Distinct own manual/automatic continuation, or the existing new-park mode choice.</summary>
	// [EXT:theme-continue] the theme entry offers distinct own-save actions (Continue saved, Continue automatic, New): host UI with no original rule to match; no native profile or autosave flow is reproduced
	private void ShowThemeStart()
	{
		var saved = actions.ContinuePark == null ? null : actions.ContinueEntry( Selected.Level );
		var automatic = actions.ContinueAutomatic == null ? null : actions.AutomaticEntry( Selected.Level );
		if ( saved == null && automatic == null ) { ShowGameMode(); return; }
		var theme = Selected.Level;
		ProgressChoiceScreen? choice = null;
		bool Current() => !choice!.IsRemoved && ReferenceEquals( Stack.Top, choice ) && Selected.Level == theme;
		void Activate( Action action ) { if ( Current() && choice!.TryActivate( Stack ) ) action(); }
		string Mode( ParkGameMode mode ) => strings[mode == ParkGameMode.InstantAction ? UIStrings.InstantAction : UIStrings.FullSimulation];
		var entries = new List<(Func<string>, Action)>();
		if ( saved != null ) entries.Add( (() => strings.Extra( OpenTpwText.ContinueSavedPark ) + " (" + Mode( saved.Mode ) + ")", () => Activate( () => { PlayEnterPark( saved.Mode == ParkGameMode.InstantAction ); actions.ContinuePark!( saved ); } )) );
		if ( automatic != null ) entries.Add( (() => strings.Extra( OpenTpwText.ContinueAutomaticPark ) + " (" + Mode( automatic.Mode ) + ")", () => Activate( () => { PlayEnterPark( automatic.Mode == ParkGameMode.InstantAction ); actions.ContinueAutomatic!( automatic ); } )) );
		entries.Add( (() => strings.Extra( OpenTpwText.NewPark ), () => Activate( () => { Stack.Pop(); ShowGameMode(); } )) );
		entries.Add( (() => strings.Extra( OpenTpwText.Back ), () => { if ( Current() ) Stack.Pop(); }) );
		var summary = SelectedName;
		if ( saved != null ) summary += "\n" + strings.Extra( OpenTpwText.ContinueSavedPark ) + ": " + (saved.Park.DisplayName ?? SelectedName) + " (" + Mode( saved.Mode ) + ")";
		if ( automatic != null ) summary += "\n" + strings.Extra( OpenTpwText.ContinueAutomaticPark ) + ": " + automatic.DisplayName + " (" + Mode( automatic.Mode ) + ")";
		choice = ProgressChoiceScreen.Message( "themeStart", () => summary, entries.ToArray() );
		var window = choice.Find( "window" )!.Bounds;
		var message = choice.Find( "message" )!; message.Bounds = new UiRect( window.X + 90, window.Y + 60, window.Width - 180, 200 );
		for ( var index = 0; index < entries.Count; index++ ) choice.Find( $"choice{index}" )!.Bounds = new UiRect( window.X + 70 + (index % 2) * 480, window.Y + 280 + (index / 2) * 150, 460, 110 );
		Stack.Push( choice );
	}
}
