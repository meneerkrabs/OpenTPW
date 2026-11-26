using OpenTPW.UI.Original;

namespace OpenTPW;

public sealed partial class ThemeParkIncHud
{
	/// <summary>The mission window's globals that outlive one opening.</summary>
	private readonly ThemeParkIncMissionWindowSession missionSession = new();

	/// <summary>The open mission window (null when it is closed).</summary>
	internal ThemeParkIncMissionWindow? MissionWindow { get; private set; }

	/// <summary>The camera flight's mission-window requests this HUD has answered (<see cref="ThemeParkIncCutscene.MissionWindowRequests"/>).</summary>
	private int answeredMissionWindowRequests = -1;

	/// <summary>
	/// Opens the mission window when the camera flight's countdown asked for it since the last update (one update after a zone-arrival flight ends): the countdown's end calls the
	/// builder directly, so it opens over whatever is open, closing a letter as every opening does.
	/// </summary>
	// [BIN:TPI-EXE:0x5D0369 flight window countdown] 0xA9D258 counts 1, 2, 3 on the flight updates after a mode-2 end, then 0xA61810 is built (0x55A7F0)
	private void UpdateMissionWindowRequests()
	{
		var requests = level.Park?.Economy?.TpiCutscene?.MissionWindowRequests ?? 0;
		if ( answeredMissionWindowRequests < 0 )
			answeredMissionWindowRequests = requests;
		if ( requests == answeredMissionWindowRequests )
			return;
		answeredMissionWindowRequests = requests;
		OpenMissionWindow();
	}

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
	// [BIN:TPI-EXE:0x5013C0 close the HUD's window] message 5 to the window registered at 0xA568BC, which is also the letter window's (0x526D27): the letter's procedure takes it as its close
	// (0x52698F: destroyed, and categories 0x5D to 0x5F run their action, 0x649E60 for the bank's and the firing letter); the window is then built whatever that did
	// [BIN:TPI-EXE:0x5013C0 close the HUD's window, second branch] when [0xA5E890] holds a window (the one the loss window closes; not identified further) 0x5013C0 closes that window instead (message 4, 0x5013CC..0x5013EC)
	// and sends no message 5, so no letter or mission window closes then; OpenTPW builds no window for that slot, so the call always takes the message 5 branch here
	// [BIN:TPI-EXE:0x42F04B objectives key game mode] the Objectives row refuses in game mode 3 ([0xC65B80] == 3, the coaster-edit mode); OpenTPW's Theme Park Inc slice runs the career only (mode 0, single player;
	// ParkGameMode has no other value, ThemeParkIncChallenges notes the same), never mode 3, so the key has no refusal to port
	public bool OpenMissionWindow()
	{
		if ( Lost || level.Park?.Economy is not { TpiCareer.CurrentZone: not null } economy || economy.TpiMissionWindowWorld( strings.Entry( ThemeParkIncText.NewPlayer ) ) is not { } world )
			return false;
		CloseHudWindow();
		ThemeParkIncMissionWindow? window = null;
		window = new ThemeParkIncMissionWindow( TextsOrLanguage, strings, world, missionSession, () => CloseMissionWindow( window ), OpenChallengePanel ) { Advice = economy.TpiWindowAdvice };
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

	/// <summary>F1 without a modifier: the advisor says its stored tutorial advice again (<see cref="ParkEconomy.RepeatThemeParkIncTutorial"/>); a text field keeps the key.</summary>
	// [BIN:TPI-EXE:0x9A866C help key] the key table's F1 row (key 0x70, its handler word 0x4305E0 at 0x9A866C) and the default window procedure's F1 (0x507FE8, key 0x70 without a modifier) both call 0x718AB0 on the
	// advisor thing; a lost park's key tables are disabled (0x431230) and the loss window's procedure (0x50ACE0) does not pass keys to the default one
	private void UpdateHelpKey()
	{
		if ( !Lost && !MenuOpen && !Input.TextEntryActive && Input.Pressed( InputButton.Help ) && Input.ModifierMaskDown == 0 )
			level.Park?.Economy.RepeatThemeParkIncTutorial();
	}

	/// <summary>The text HUD's button that opens the mission window, below the challenges button.</summary>
	// [EXT:tpi-text-hud] the native HUD's buttons that open the window (0x54AC0F, 0x54F7C1) belong to Theme Park Inc's in-game interface, which the slice replaces with the text HUD; this text
	// button stands in for them and says the window's own title (UITEXT 461)
	private void BuildMissionButton()
	{
		var button = ToolButton( "objective", () => strings.Entry( ThemeParkIncMissionWindow.TitleText ), "", () => OpenMissionWindow(), 5 );
		button.Help = null;
		// the right edge below the challenges button, narrower than it so that the build, staff and challenge panels' widest row (to x 1824 at 4:3) stays clear
		button.Bounds = new UiRect( 2024 - 190, 256, 190, 110 );
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
				// the obstacles are things of OpenTPW's world (TPI-MISSIONS §6): 0x647850 finds the thing of the type, and the go-to only logs it, as the camera move 0x5033C0 is not ported
				if ( level.Park?.Economy.TpiMissionWorld?.FindAttraction( argument ) is { } thing and not 0 )
					Log.Trace( $"Theme Park Inc letter go-to: thing {thing} of type {argument}; the camera move 0x5033C0 is not ported." );
				break;
			case ThemeParkIncTagLetter.TrainingGoto:
				// [BIN:TPI-EXE:0x55137B training go-to] kind 5: the staff window 0x58C6C0 (0xA6BF40) on the thing of +0xE14; [EXT:tpi-text-hud] the text HUD's staff panel stands in for that window, which
				// is not built, so the go-to opens the panel
				if ( !staffOpen )
					ToggleStaffPanel();
				break;
			default:
				// kind 0x12's object 0xA59CC0 and its 0x521C90 are not read
				Log.Trace( $"Theme Park Inc letter go-to kind {kind} ({argument}) is not ported." );
				break;
		}
	}
}
