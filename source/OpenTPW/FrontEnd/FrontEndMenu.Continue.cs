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
		if ( saved != null ) entries.Add( (() => strings.Extra( OpenTpwText.ContinueSavedPark ) + ": " + (saved.Park.DisplayName ?? SelectedName) + " (" + Mode( saved.Mode ) + ")", () => Activate( () => { PlayEnterPark( saved.Mode == ParkGameMode.InstantAction ); actions.ContinuePark!( saved ); } )) );
		if ( automatic != null ) entries.Add( (() => strings.Extra( OpenTpwText.ContinueAutomaticPark ) + ": " + automatic.DisplayName + " (" + Mode( automatic.Mode ) + ")", () => Activate( () => { PlayEnterPark( automatic.Mode == ParkGameMode.InstantAction ); actions.ContinueAutomatic!( automatic ); } )) );
		entries.Add( (() => strings.Extra( OpenTpwText.NewPark ), () => Activate( () => { Stack.Pop(); ShowGameMode(); } )) );
		entries.Add( (() => strings.Extra( OpenTpwText.Back ), () => { if ( Current() ) Stack.Pop(); }) );
		// [BIN:TPW-EXE:0x753f50 name dialog] the theme name is the caption in the table's title box (655,285)-(1315,365); the choices stack as full-width text buttons on the rows of the table's two toggles (764,405)-(1206,476) and (764,498)-(1206,569), 93 units apart, widened to the window since this dialog has no tick or X
		// [EXT:theme-continue] the parks' own names are part of the buttons' labels
		var summary = SelectedName + "\n";
		choice = ProgressChoiceScreen.Message( "themeStart", () => summary, entries.ToArray() );
		var rows = OriginalLayoutTables.NameDialog.Model( "b_ltoggle" ).Rect;
		var pitch = OriginalLayoutTables.NameDialog.All.Where( node => node.Model == "b_ltoggle" ).Select( node => node.Rect.Y ).OrderBy( y => y ).Take( 2 ).ToArray();
		var step = pitch.Length == 2 ? pitch[1] - pitch[0] : 93f;
		choice.Find( "caption" )!.Bounds = OriginalLayoutTables.NameDialog.Control( 0x709 ).Rect;
		for ( var index = 0; index < entries.Count; index++ ) choice.Find( $"choice{index}" )!.Bounds = new UiRect( 560, rows.Y + step * index, 896, rows.Height );
		Stack.Push( choice );
	}
}
