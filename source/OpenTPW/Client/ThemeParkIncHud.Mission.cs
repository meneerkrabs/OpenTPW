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
}
