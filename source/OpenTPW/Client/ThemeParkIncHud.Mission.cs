using OpenTPW.UI.Original;

namespace OpenTPW;

public sealed partial class ThemeParkIncHud
{
	/// <summary>The mission window's globals that outlive one opening.</summary>
	private readonly ThemeParkIncMissionWindowSession missionSession = new();

	/// <summary>The open mission window (null when it is closed).</summary>
	internal ThemeParkIncMissionWindow? MissionWindow { get; private set; }

	/// <summary>The mission window is open.</summary>
	public bool MissionWindowOpen => Stack.Screens.Any( screen => screen.Name == ThemeParkIncMissionWindow.ScreenName );

	/// <summary>The string tables the mission window and the objective letter read (null: the ones of the selected language).</summary>
	internal IThemeParkIncTexts? Texts { get; set; }

	private IThemeParkIncTexts TextsOrLanguage => Texts ??= ThemeParkIncTexts.For( strings, GameLanguage.Current );

	/// <summary>
	/// Opens the mission window ("Current Objective") over the HUD, as the Objectives key, the HUD and the end of a zone-arrival camera flight do; an open one is closed and built again.
	/// False when the park has no mission controller or is lost.
	/// </summary>
	// [BIN:TPI-EXE:0x42F020 objectives command] the key table's Objectives command builds the window unless the game mode 0xC65B80 is 3; the builder first closes the window the HUD has open
	// (0x5013C0 sends message 5 to 0xA568BC), then registers itself there (0x501CD0)
	// [BIN:TPI-EXE:0x50AFC0 loss window] a lost park's key tables are disabled (0x431230), so the key opens nothing then
	public bool OpenMissionWindow()
	{
		if ( Lost || level.Park?.Economy.TpiMissionWindowWorld( strings.Entry( ThemeParkIncText.NewPlayer ) ) is not { } world )
			return false;
		CloseMissionWindow();
		ThemeParkIncMissionWindow? window = null;
		window = new ThemeParkIncMissionWindow( TextsOrLanguage, strings, world, missionSession, () => CloseMissionWindow( window ), OpenChallengePanel );
		var screen = window.Build();
		window.Rebuilt = () => OpenMissionWindow();
		MissionWindow = window;
		Stack.Push( screen );
		return true;
	}

	private void CloseMissionWindow( ThemeParkIncMissionWindow? only = null )
	{
		if ( only != null && !ReferenceEquals( MissionWindow, only ) )
			return;
		Stack.RemoveWhere( screen => screen.Name == ThemeParkIncMissionWindow.ScreenName );
		MissionWindow = null;
	}

	private void OpenChallengePanel()
	{
		if ( !challengesOpen )
			ToggleChallengePanel();
	}

	/// <summary>The Objectives key (<see cref="UiKeys.Objectives"/>) with no other window open; a text field keeps the key.</summary>
	private void UpdateMissionKey( UiInput input )
	{
		if ( !MenuOpen && !Input.TextEntryActive && input.Has( UiKeys.Objectives ) )
			OpenMissionWindow();
	}

	/// <summary>The text HUD's button that opens the mission window, below the challenges button.</summary>
	// [EXT:tpi-text-hud] the native HUD's buttons that open the window (0x54AC0F, 0x54F7C1) belong to Theme Park Inc's in-game interface, which the slice replaces with the text HUD; this text
	// button stands in for them and says the window's own title (UITEXT 461)
	private void BuildMissionButton()
	{
		var button = ToolButton( "objective", () => strings.Entry( ThemeParkIncMissionWindow.TitleText ), "", () => OpenMissionWindow(), 5 );
		button.Help = null;
		button.Bounds = new UiRect( 2024 - 306, 256, 286, 110 );
		button.Anchor = UiAnchor.TopRight;
	}

	/// <summary>Opens the "Objective Complete!" letter of completed mission <paramref name="completed"/>, dated <paramref name="date"/>.</summary>
	internal void ShowObjectiveLetter( int completed, string date ) =>
		ShowLetter( ThemeParkIncObjectiveLetter.Compose( completed, TextsOrLanguage, strings.Entry( ThemeParkIncText.NewPlayer ), date ), () => { } );

	/// <summary>The letter's go-to (<c>0x5511B0</c>) for the kinds the objective letter uses.</summary>
	// [BIN:TPI-EXE:0x5511B0 letter go-to] kind - 1 through the jump table 0x55155C: kind 2 finds the first attraction of type +0xE18 (0x647850) and, found, moves the camera to it (0x5033C0 with a
	// per-type pair: 0x6A7 (0xFF, 0x32), 0x6A6 (0x3C, 0xF), 0xA8E (0x1E, 0), 0xA8F (0x37, 0x19), 0xE75 (0x32, 0xA), 0xE77 (0x14, 0x23), else (0, 0)); 0xB the challenges window
	// (0x529960(+0xE18)); 0xC the mission window (0x55A7F0); 0x12 0xA59CC0 -> 0x521C90(+0xE18, 0)
	internal void RunLetterGoto( int kind, int argument )
	{
		switch ( kind )
		{
			case ThemeParkIncObjectiveLetter.GotoMissionWindow:
				OpenMissionWindow();
				break;
			case ThemeParkIncObjectiveLetter.GotoChallenges:
				// the challenges window (table 0x9B54CC) is not built: the text HUD's challenge panel ([EXT:tpi-text-hud]) stands in for it, as for the mission window's tab
				OpenChallengePanel();
				break;
			case ThemeParkIncObjectiveLetter.GotoThingOfType:
				// the obstacles are not things of OpenTPW's world yet (TPI-MISSIONS §6), so 0x647850 finds none and the go-to does nothing, as the original's does for a type it does not find;
				// the camera move 0x5033C0 is not ported
				if ( level.Park?.Economy.TpiMissionWorld?.FindAttraction( argument ) is { } thing and not 0 )
					Log.Trace( $"Theme Park Inc letter go-to: thing {thing} of type {argument}; the camera move 0x5033C0 is not ported." );
				break;
			default:
				// kind 0x12's object 0xA59CC0 and its 0x521C90 are not read
				Log.Trace( $"Theme Park Inc letter go-to kind {kind} ({argument}) is not ported." );
				break;
		}
	}
}
