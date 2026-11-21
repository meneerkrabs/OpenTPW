using System.Text;
using Veldrid;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>What the mission window reads of the park and the world beside the career and the mission controller.</summary>
internal interface IThemeParkIncMissionWindowWorld
{
	/// <summary>The mission controller (park thing class 21); its career is the window's.</summary>
	ThemeParkIncMissionController Missions { get; }
	/// <summary>The park's turn, the clock the completion tick starts statistics with.</summary>
	long Turn { get; }
	/// <summary>The current obstacle thing (<c>park+0x1EAB6E</c>), the thing message 0x24 carries.</summary>
	ushort CurrentObstacle { get; }
	/// <summary>The research manager's obstacle project's progress: <c>0x61E5E0(0x61EB30())</c>, 0 when there is no such project.</summary>
	int ObstacleResearchPercent { get; }
	/// <summary>The researcher list head (<c>park+0x1EAB84</c>) is set: a scientist is hired.</summary>
	bool ResearchersHired { get; }
	/// <summary>Whether every slot of the staff HQ's obstacle table is empty (kind 7) or holds a staff thing; null where the slots are not modelled.</summary>
	bool? ObstacleStaffSlotsFilled { get; }
	/// <summary>The addressee: the player's name (<c>0x777870</c>, the user record's <c>+8</c>).</summary>
	string PlayerName { get; }
	/// <summary>Proceed's action (<see cref="ParkEconomy.PressThemeParkIncMissionComplete"/>): message 0x24 to the staff HQ and the controller, or OnMissionComplete; false when the requirements are not met.</summary>
	bool PressComplete();
}

/// <summary>What survives the mission window object between openings (the globals at <c>0xA61814</c> and the list pointer <c>0xA61818</c>).</summary>
internal sealed class ThemeParkIncMissionWindowSession
{
	/// <summary><c>0xA61814</c>: set by the window's first four-second timer; from then on every tick refreshes. Only the close and cancel buttons clear it.</summary>
	public bool RefreshArmed { get; set; }
	/// <summary><c>0xA61818</c> (window <c>+8</c>): the staff list was set up (<c>0x55D6D0</c>), which the builder does only in obstacle state 2.</summary>
	public bool StaffListBuilt { get; set; }
}

/// <summary>
/// Theme Park Inc's mission window, "Current Objective" (docs/reverse/TPI-MISSIONS.md §7): a letter-like window over the layout table <c>0x9BE808</c> with the mission's title,
/// picture and sender, a briefing page and a details page that lists the objective's requirements and status, the Objectives and Challenges tabs and the Proceed button, enabled when
/// the mission controller's requirements are met.
/// </summary>
// [BIN:TPI-EXE:0x55A7F0 mission window] the builder: career +0x27 = 0, the window's +0x1C (scroll) and +0x24 (page) = 0, CreateWindow 0x84DEA0(0x9BE808, 0x55BA70)
internal sealed class ThemeParkIncMissionWindow
{
	/// <summary>The screen's name on the HUD stack.</summary>
	public const string ScreenName = "tpiMission";

	// [BIN:TPI-EXE:0x55AFA6 mission window texts] UITEXT 0x1CD (title), 0x1CF/0x1D0 (the tabs), 0x52 (Proceed), 0x3DC/0x3DD (the page toggle), 0x357/0x358/0x359 (To:, From:, Subject:)
	public const int TitleText = 461, ObjectivesTab = 463, ChallengesTab = 464, ProceedText = 82, ToDetails = 988, ToBriefing = 989, ToLabel = 855, FromLabel = 856, SubjectLabel = 857;
	// UITEXT ids of the details page
	public const int WrongZone = 75, Requirements = 83, TrainedStaffRequired = 84, TicketsNeeded = 85, ObjectiveStatus = 86, StaffTypeHeader = 87, AbilityHeader = 88,
		EmployeeHeader = 89, ResearchProgress = 91, StaffMissing = 92, StaffSufficient = 93, CurrentPosition = 98, PromotedWellDone = 99, ResearchNeeded = 103, LabNeeded = 104,
		ScientistsNeeded = 105, ProjectNeeded = 106;

	/// <summary>The sender: Mr Maybury, row key 1 of the sender table <c>0x9CD6D8</c> (name entry 173, title 174, mugshot 1).</summary>
	// [BIN:TPI-EXE:0x55B3AA mission sender] 0x5CD630(1) = 0x9CD6D8 + 16 × 1: {key 1, name 173, title 174, mugshot 1}
	public const int SenderName = 173, SenderTitle = 174, SenderMugshot = 1;

	/// <summary>The mission picture's frame plus one, by mission counter (<c>0x9BECCC</c>, 18 words).</summary>
	// [BIN:TPI-EXE:0x9BECCC mission pictures] read at 0x55B57E as 0x9BECCC[counter] - 1
	public static readonly IReadOnlyList<int> Pictures = new[] { 10, 12, 4, 15, 4, 8, 6, 4, 11, 13, 4, 17, 16, 14, 4, 7, 9, 4 };

	/// <summary>The requirement and status lines of <c>STAT_TITLES.str</c> by exit-statistic type (<c>0x9BED18</c>, eight bytes per type).</summary>
	// [BIN:TPI-EXE:0x9BED18 statistic lines] types 0..8: (0,0) (0,0) (7,8) (1,2) (0,0) (0,0) (5,6) (9,10) (11,12); a type above 8 reads past the table, which no shipped mission has
	public static readonly IReadOnlyList<(int Requirement, int Status)> StatisticLines = new[] { (0, 0), (0, 0), (7, 8), (1, 2), (0, 0), (0, 0), (5, 6), (9, 10), (11, 12) };

	// STAT_TITLES entries the window names itself
	public const int WinPolarChallenges = 3, PolarChallengesWon = 4, OneGoldenTicket = 13, GoldenTicketsCount = 14, GoldenTicketsAvailable = 15;

	private readonly IThemeParkIncTexts texts;
	private readonly UiStringTable strings;
	private readonly IThemeParkIncMissionWindowWorld world;
	private readonly ThemeParkIncMissionWindowSession session;
	private readonly Action close;
	private readonly Action openChallenges;
	private readonly StringBuilder details = new();
	private string lastFormatted = "";
	private float timer;
	private UiScreen? screen;
	private UiButton? proceedButton;
	/// <summary>The labels of the obstacle group 0xCEC63 (shown with it).</summary>
	private readonly List<UiLabel> shownWithGroup = new();

	public ThemeParkIncMissionWindow( IThemeParkIncTexts texts, UiStringTable strings, IThemeParkIncMissionWindowWorld world, ThemeParkIncMissionWindowSession session, Action close,
		Action openChallenges )
	{
		this.texts = texts;
		this.strings = strings;
		this.world = world;
		this.session = session;
		this.close = close;
		this.openChallenges = openChallenges;
	}

	private ThemeParkIncCareer Career => world.Missions.Career;

	/// <summary><c>+0x24</c> (<c>0xA61834</c>): 0 the briefing, 1 the details.</summary>
	public int Page { get; private set; }
	/// <summary><c>+0x28</c>: the briefing page's text.</summary>
	public string Briefing { get; private set; } = "";
	/// <summary><c>+0x13B2</c>: the details page's text (kept between refreshes: a refresh that writes nothing leaves the last text and adds a line feed).</summary>
	public string Details => details.ToString();
	/// <summary>The body the letter frame shows: the page's text, or nothing on the obstacle page.</summary>
	public string Body { get; private set; } = "";
	/// <summary>The obstacle group <c>0xCEC63</c> is shown (the details page in obstacle state 2).</summary>
	public bool ObstacleGroupShown { get; private set; }
	/// <summary>The obstacle group's lines: <c>0xCEC65</c>, <c>0xCEC66</c> and <c>0xCEC67</c>.</summary>
	public (string Heading, string Tickets, string Status) ObstacleLines { get; private set; } = ("", "", "");
	/// <summary>Proceed (<c>0xCEC68</c>) is enabled: the requirements were met at the last refresh.</summary>
	public bool ProceedEnabled { get; private set; }

	/// <summary>Builds the window: its controls and their texts, then the first refresh.</summary>
	public UiScreen Build()
	{
		var table = ThemeParkIncLayoutTables.MissionWindow;
		var career = Career;
		// [BIN:TPI-EXE:0x55A860 career flag] the builder clears the career's byte +0x27
		career.ClearMissionWindowFlag();
		Page = 0;
		screen = new UiScreen( ScreenName ) { Back = Close };
		var counter = career.MissionCounter;
		var operation = texts.Format( ThemeParkIncTable.Operation, counter );
		UiModelImage Image( string id, uint control, Func<int>? frame = null ) => screen.Add( new UiModelImage
		{
			Id = id,
			Model = table.Control( control ).Model!,
			Frame = frame ?? (() => 0),
			Bounds = table.Control( control ).Rect,
			Anchor = UiAnchor.Center
		} );
		UiLabel Label( string id, UiRect bounds, Func<string> text, Func<UiFonts, FontAtlas> font, RgbaByte colour, UiAlign align = UiAlign.Left, bool wrap = false, Func<bool>? visible = null )
		{
			var label = screen.Add( new UiLabel { Id = id, Text = text, Font = font, Color = colour, Align = align, Wrap = wrap, Bounds = bounds, Anchor = UiAnchor.Center } );
			if ( visible != null )
				shownWithGroup.Add( label );
			return label;
		}
		// [EXT:ui-font-tier-by-scale] which shipped font serves font slots 5, 6 and 7 (0x500DE0) is OpenTPW's choice: the label role for 5 and 6, the small role for 7, as the letter window
		Func<UiFonts, FontAtlas> slot5 = fonts => fonts.Label, slot6 = fonts => fonts.Label, slot7 = fonts => fonts.Small;
		// [BIN:TPI-EXE:0x5123A3 text default colour] a text control without a colour call keeps the text class's white
		var white = new RgbaByte( 0xFF, 0xFF, 0xFF, 0xFF );

		// [BIN:TPI-EXE:0x9BE808 mission table] the !window 0xCEC52 (196,38)-(2028,1318) carries every control; the !f_plain page frame 0xCEC53
		Image( "window", 0xCEC52 );
		Image( "pageFrame", 0xCEC53 );
		// [BIN:TPI-EXE:0x55AFCE mission title] the title control 0xCEC6A (0x501D50, as the letter's) with UITEXT 461
		screen.Add( new UiLabel
		{
			Id = "title",
			Text = () => strings.Entry( TitleText ),
			Font = fonts => fonts.Title,
			Shadow = true,
			ShadowAlpha = UiBatch.TpiTitleCopyAlpha,
			Color = UiColors.Title,
			Fit = true,
			Bounds = table.Control( 0xCEC6A ).Rect,
			Anchor = UiAnchor.Center
		} );
		// [BIN:TPI-EXE:0x55AFD8 mission tabs] the Objectives tab 0xCEC59 is disabled and set (vtable +0x14(0), 0x85B402(1, 1)); both tabs centre their UITEXT in font slot 7 (0x84A4D2(1, 1, 3))
		var objectives = Button( "objectivesTab", table.Control( 0xCEC59 ), null, enabled: false );
		objectives.Selected = () => true;
		TextOn( "objectivesTabText", table.Control( 0xCEC59 ), () => strings.Entry( ObjectivesTab ), white );
		// [BIN:TPI-EXE:0x55BCC1 challenges tab] the Challenges tab closes the window and opens the challenges window (0xA59F28 -> 0x529960(-1))
		// the challenges window (table 0x9B54CC) is not built: the text HUD's challenge panel ([EXT:tpi-text-hud]) stands in for it
		Button( "challengesTab", table.Control( 0xCEC58 ), () => { Close(); openChallenges(); }, enabled: true );
		TextOn( "challengesTabText", table.Control( 0xCEC58 ), () => strings.Entry( ChallengesTab ), white );
		// [BIN:TPI-EXE:0x55A932 mission name] 0xCEC56 in font slot 5, centred (0x84A4D2(1, 1, 3)), white (0x84A6BB(0xFF, 0xFF, 0xFF, 0xFF)): OPERATION[counter]
		Label( "operation", table.Control( 0xCEC56 ).Rect, () => operation, slot5, white, UiAlign.Center, wrap: true );
		// [BIN:TPI-EXE:0x55B4CB mission picture] the objectim 0xCEC55 in the !frame 0xCEC54 shows frame 0x9BECCC[counter] - 1
		Image( "pictureFrame", 0xCEC54 );
		var picture = counter >= 0 && counter < Pictures.Count ? Pictures[counter] - 1 : -1;
		Image( "picture", 0xCEC55, () => picture );
		// [BIN:TPI-EXE:0x55A9C9 mission letter frame] the !frame 0xCEC5A holds the From/To/Subject pairs: each label in font slot 6 with the colour (0xEA, 0xEF, 0x66, 0xFF), each value in font slot 6 with no colour call
		Image( "letterFrame", 0xCEC5A );
		var labelColour = new RgbaByte( 0xEA, 0xEF, 0x66, 0xFF );
		// [EXT:no-player-profiles] OpenTPW has no player profiles: the addressee is the world's PlayerName, the Personnel File's one player
		void Field( string id, uint labelControl, int label, uint valueControl, Func<string> value )
		{
			Label( id + "Label", table.Control( labelControl ).Rect, () => strings.Entry( label ), slot6, labelColour );
			screen.Add( new UiLabel { Id = id, Text = value, Font = slot6, Color = white, Fit = true, Bounds = table.Control( valueControl ).Rect, Anchor = UiAnchor.Center } );
		}
		// [BIN:TPI-EXE:0x55B2EF mission fields] Subject: OPERATION[counter]; From: TAG_SYSTEM[sender +4] (173); To: the player's name
		Field( "subject", 0xCEC5E, SubjectLabel, 0xCEC5F, () => operation );
		Field( "from", 0xCEC5B, FromLabel, 0xCEC5C, () => texts.Format( ThemeParkIncTable.TagSystem, SenderName ) );
		Field( "to", 0xCEC60, ToLabel, 0xCEC61, () => world.PlayerName );
		// [BIN:TPI-EXE:0x55B40F mission mugshot] the mugshot 0xCEC5D shows the sender's word +0xC less one
		Image( "mugshot", 0xCEC5D, () => SenderMugshot - 1 );
		// [BIN:TPI-EXE:0x55AD32 mission body] the frame's text in font slot 7, left and top (0x84A4D2(0, 0, 3)), wrapped (0x84A577(1, 0, 3)), no colour call; the body goes to the scroll text over the slider 0xCEC62 (0x52C4C0)
		var frame = table.Control( 0xCEC5A );
		Label( "body", frame.Inner ?? frame.Rect, () => Body, slot7, white, wrap: true );
		// the scroll text control (0x52C4C0, 0x55B9A0: a pixel offset the slider sets) is not ported: the body shows from its top, as at every opening (+0x1C = 0), and the slider is drawn without moving it
		foreach ( var part in table.Control( 0xCEC62 ).Children )
			screen.Add( new UiModelImage { Id = "scroll" + part.Model, Model = part.Model!, Bounds = part.Rect, Anchor = UiAnchor.Center } );
		// [BIN:TPI-EXE:0x55ADB2 obstacle group] the group 0xCEC63's three lines in font slot 7, left and top, wrapped
		Label( "obstacleHeading", table.Control( 0xCEC65 ).Rect, () => ObstacleLines.Heading, slot7, white, wrap: true, visible: () => ObstacleGroupShown );
		Label( "obstacleTickets", table.Control( 0xCEC66 ).Rect, () => ObstacleLines.Tickets, slot7, white, wrap: true, visible: () => ObstacleGroupShown );
		Label( "obstacleStatus", table.Control( 0xCEC67 ).Rect, () => ObstacleLines.Status, slot7, white, wrap: true, visible: () => ObstacleGroupShown );
		// [BIN:TPI-EXE:0x55D6D0 staff list] the list 0xCEC64's column headers 0x10, 0x11 and 0x12: UITEXT 87, 88 and 89
		// [EXT:tpi-capability-gates] the rows (0x55E020) are the staff HQ's obstacle slots, which belong to the staff roles OpenTPW does not port: the list has no rows
		var list = table.Control( 0xCEC64 );
		foreach ( var (column, text) in new[] { (0x10u, StaffTypeHeader), (0x11u, AbilityHeader), (0x12u, EmployeeHeader) } )
		{
			var header = list.Children.First( child => child.Id == column );
			Label( $"staffHeader{column:X}", header.Rect, () => strings.Entry( text ), slot7, white, visible: () => ObstacleGroupShown );
		}
		if ( world.Missions.ObstacleState() == 2 )
			session.StaffListBuilt = true;
		// [BIN:TPI-EXE:0x55B16E page toggle] the b_updown 0xCEC69 says UITEXT 988 on the briefing and 989 on the details, centred in font slot 7; its toggle state is the details page
		// (0x85B402(page == 1, 1)); the colour call (0xFA, 0xFA, 0xFA) at 0x55AD21 styles the first text object, which 0x55B1D6 replaces (0x84B2DE) with no colour call: the class default, white
		var toggle = Button( "page", table.Control( 0xCEC69 ), TogglePage, enabled: true );
		toggle.Selected = () => Page == 1;
		TextOn( "pageText", table.Control( 0xCEC69 ), () => strings.Entry( Page == 0 ? ToDetails : ToBriefing ), white );
		// [BIN:TPI-EXE:0x55B236 proceed] the b_ltoggle10 0xCEC68 says UITEXT 82, centred in font slot 7 with the colour (0xC8, 0xC8, 0xDC, 0xFF)
		// [BIN:TPI-EXE:0x55B5BA proceed sparkle] when the requirements are met the builder starts a screen particle burst at the button (0x500B70 -> 0x7BACC0), which OpenTPW's interface does not draw
		var proceed = proceedButton = Button( "proceed", table.Control( 0xCEC68 ), Proceed, enabled: false );
		TextOn( "proceedText", table.Control( 0xCEC68 ), () => strings.Entry( ProceedText ), new RgbaByte( 0xC8, 0xC8, 0xDC, 0xFF ) );
		// [BIN:TPI-EXE:0x55BEAD help] the help button only plays sound 0x1B6 (0x503A40); Theme Park Inc's interface sounds are not ported
		Button( "help", table.Control( 0xCEC6B ), null, enabled: true );
		// [BIN:TPI-EXE:0x55BD35 close] the close and the cancel button share the id -2: both close the window and clear 0xA61814
		Button( "close", table.Model( "b_close" ), Cancel, enabled: true );
		Button( "cancel", table.Model( "b_cancel" ), Cancel, enabled: true );
		Refresh();
		screen.Updating += context => Tick( context.Delta );
		screen.Focus( proceed.Enabled ? proceed : toggle );
		return screen;

		UiButton Button( string id, LayoutNode node, Action? clicked, bool enabled ) => screen!.Add( new UiButton
		{
			Id = id,
			Model = node.Model,
			Help = node.Help >= 0 ? strings.Help( node.Help ) : null,
			Clicked = clicked,
			Enabled = enabled,
			Bounds = node.Rect,
			Anchor = UiAnchor.Center
		} );

		void TextOn( string id, LayoutNode node, Func<string> text, RgbaByte colour ) =>
			screen!.Add( new UiLabel { Id = id, Text = text, Font = slot7, Color = colour, Align = UiAlign.Center, Fit = true, Bounds = node.Inner ?? node.Rect, Anchor = UiAnchor.Center } );
	}

	/// <summary>
	/// The window's timer and tick (<c>0x55BA70</c> messages 0x10, 0x15 and 0x1E): four seconds after the window opens the session's refresh flag is set, and from then on every tick
	/// refreshes the window; when the obstacle reaches state 2 and the staff list was never set up, the window is built again instead, on its briefing page.
	/// </summary>
	// [BIN:TPI-EXE:0x55BAB1 mission timer] message 0x15 (table 0x55BF24/0x55BF08) arms timer 0x80080 for 0xFA0 ms (0x84D0A9); its expiry, message 0x10 with id 0x80080 (0x55BAF6), sets 0xA61814 = 1
	// [BIN:TPI-EXE:0x55BBD0 mission tick] message 0x1E with 0xA61814 set: obstacle state 2 and 0xA61818 == 0 closes the window and builds it again (0x55A7F0); else the refresh 0x55C190
	internal void Tick( float delta )
	{
		if ( !session.RefreshArmed )
		{
			timer += delta;
			if ( timer >= 4f )
				session.RefreshArmed = true;
			return;
		}
		// a refused rebuild (a lost park opens no window) keeps this window refreshing rather than frozen
		if ( !session.StaffListBuilt && world.Missions.ObstacleState() == 2 && Rebuilt?.Invoke() == true )
			return;
		Refresh();
	}

	/// <summary>Raised when the tick builds the window again (the HUD replaces the screen); false when the HUD refused to build it.</summary>
	public Func<bool>? Rebuilt { get; set; }

	/// <summary>The refresh <c>0x55C190</c>: the page's texts, the obstacle group and Proceed's state.</summary>
	// [BIN:TPI-EXE:0x55C190 mission refresh] page 0: hide 0xCEC63, the briefing 0x55D520; page 1 in obstacle state 2: show 0xCEC63, the body text "" and the obstacle lines 0x55D1B0; page 1 otherwise:
	// hide 0xCEC63 and the details 0x55C350; then 0xCEC68's vtable +0x14 (enable) with MetRequirements 0x55A620
	public void Refresh()
	{
		if ( Page == 0 )
		{
			ObstacleGroupShown = false;
			Briefing = ComposeBriefing();
			Body = Briefing;
		}
		else if ( world.Missions.ObstacleState() == 2 )
		{
			ObstacleGroupShown = true;
			Body = "";
			ObstacleLines = ComposeObstacleLines();
		}
		else
		{
			ObstacleGroupShown = false;
			ComposeDetails();
			Body = Details;
		}
		ProceedEnabled = world.Missions.MetRequirements();
		if ( proceedButton != null )
			proceedButton.Enabled = ProceedEnabled;
		foreach ( var label in shownWithGroup )
			label.Visible = ObstacleGroupShown;
	}

	private string Format( ThemeParkIncTable table, int entry, IReadOnlyDictionary<int, string>? values = null ) => lastFormatted = texts.Format( table, entry, values );

	private static Dictionary<int, string> Argument( int parameter, string value ) => new() { [parameter] = value };

	/// <summary>
	/// The briefing (<c>0x55D520</c>): mission 0's <c>MISSION_DESC</c> with its first exit statistic's target; later missions <c>MISSION_DESC_GOTPROMO</c> when the required rank is already held
	/// (except mission 2), else <c>MISSION_DESC</c>; then a line feed and the sender's signature.
	/// </summary>
	// [BIN:TPI-EXE:0x55D520 briefing] counter 0: MISSION_DESC[0] with the integer argument 0 = status block 0's +8 (0x74C580(0), the target value); else 0x5F0150 (Promotion.Number +0x13D4,
	// rank +0x1060): Number != 0, rank >= Number (jl skips) and counter != 2: MISSION_DESC_GOTPROMO[counter]; else MISSION_DESC[counter]; then "\n" (0x9BEF94) and 0x5CD290(buffer, 1)
	public string ComposeBriefing()
	{
		var career = Career;
		var counter = career.MissionCounter;
		string text;
		if ( counter == 0 )
			text = Format( ThemeParkIncTable.MissionDesc, 0, Argument( 0, ThemeParkIncTexts.Int( career.StatusWord( 0, 2 ) ) ) );
		else
		{
			var required = career.Mission.PromotionNumber;
			text = required != 0 && career.Rank >= required && counter != 2
				? Format( ThemeParkIncTable.MissionDescGotPromo, counter )
				: Format( ThemeParkIncTable.MissionDesc, counter );
		}
		return text + "\n" + Signature();
	}

	/// <summary>The letter signature <c>0x5CD290(buffer, 1)</c>: a line feed, the sender's name, a line feed and the sender's title.</summary>
	// [BIN:TPI-EXE:0x5CD290 signature] the sender row 0x5CD630(1): "\n", TAG_SYSTEM[+4] (173), "\n", TAG_SYSTEM[+8] (174), as the letter's 0x5CD1C0
	private string Signature() => "\n" + texts.Format( ThemeParkIncTable.TagSystem, SenderName ) + "\n" + texts.Format( ThemeParkIncTable.TagSystem, SenderTitle );

	/// <summary>The details page outside obstacle state 2 (<c>0x55C350</c>).</summary>
	// [BIN:TPI-EXE:0x55C350 details] the current zone record's +0x64 == EndingTheme (+0x10E0): an obstacle mission (0x5F0190) in obstacle state 1 gives the research lines (0x55C800 when the project's
	// progress 0x61E5E0(0x61EB30()) is 0, else UITEXT 86, "\n" and 91 with the integer argument 11); obstacle states 3 and 4 write nothing; a mission without an obstacle the objectives 0x55CA60. In
	// another zone: rank >= Promotion.Number (jl skips; also a Number of 0) the zone hint 0x55C600, else the objectives. Then "\n" (0x9BEF94) is appended whatever was written
	public void ComposeDetails()
	{
		var career = Career;
		var missions = world.Missions;
		if ( CurrentZoneNumber() == career.Mission.EndingTheme )
		{
			if ( career.HasObstacle )
			{
				if ( missions.ObstacleState() == 1 )
				{
					var progress = world.ObstacleResearchPercent;
					if ( progress == 0 )
						ComposeResearchHints();
					else
					{
						details.Clear();
						details.Append( Format( ThemeParkIncTable.UiText, ObjectiveStatus ) ).Append( '\n' );
						details.Append( Format( ThemeParkIncTable.UiText, ResearchProgress, Argument( 11, ThemeParkIncTexts.Int( progress ) ) ) );
					}
				}
			}
			else
				ComposeObjectives();
		}
		else if ( career.Rank >= career.Mission.PromotionNumber )
			ComposeZoneHint();
		else
			ComposeObjectives();
		details.Append( '\n' );
		// [BIN:TPI-EXE:0x55C3A2 details buffer] the details are written into the window's buffer at +0x13B2, whose size the callers pass as 0x9C4 wide characters (0x55C3A2, 0x55C440,
		// 0x55C50A); the appends themselves (0x871475, wcscat) take no size, so a text that keeps growing (states 3 and 4 add a line feed on every tick) would run past the buffer into
		// the window object's following fields. The text stops at the buffer's end instead: at most 0x9C3 characters and the terminator
		if ( details.Length > DetailsCapacity - 1 )
			details.Length = DetailsCapacity - 1;
	}

	/// <summary>The details buffer's size in wide characters (<c>+0x13B2</c>, 0x9C4), its terminator included.</summary>
	public const int DetailsCapacity = 0x9C4;

	/// <summary>The research lines (<c>0x55C800</c>).</summary>
	// [BIN:TPI-EXE:0x55C800 research lines] UITEXT 86, "\n", 103, "\n"; 104 and "\n" when the research manager thing (0x64EB20, park+0x1EAB56) is none; 105 and "\n" when the researcher list head
	// (park+0x1EAB84) is 0; 106
	// [BIN:TPI-EXE:0x648423 research manager] every new world builds the research manager (0x61BB30, 0x2508 bytes) and keeps its id at park+0x1EAB56, so line 104 never shows
	private void ComposeResearchHints()
	{
		details.Clear();
		details.Append( Format( ThemeParkIncTable.UiText, ObjectiveStatus ) ).Append( '\n' );
		details.Append( Format( ThemeParkIncTable.UiText, ResearchNeeded ) ).Append( '\n' );
		if ( !world.ResearchersHired )
			details.Append( Format( ThemeParkIncTable.UiText, ScientistsNeeded ) ).Append( '\n' );
		details.Append( Format( ThemeParkIncTable.UiText, ProjectNeeded ) );
	}

	/// <summary>The other-zone hint (<c>0x55C600</c>): UITEXT 86, a line feed and 75 with the ending zone's name.</summary>
	// [BIN:TPI-EXE:0x55C600 zone hint] UITEXT 86, "\n", then 75 with the string argument 5 = 0x55C770(EndingTheme): 0 -> THEMENAMES[3], 1 -> [0], 2 -> [1], any other the empty string
	private void ComposeZoneHint()
	{
		details.Clear();
		details.Append( Format( ThemeParkIncTable.UiText, ObjectiveStatus ) ).Append( '\n' );
		var zone = Career.Mission.EndingTheme switch
		{
			0 => texts.Format( ThemeParkIncTable.ThemeNames, 3 ),
			1 => texts.Format( ThemeParkIncTable.ThemeNames, 0 ),
			2 => texts.Format( ThemeParkIncTable.ThemeNames, 1 ),
			_ => ""
		};
		details.Append( Format( ThemeParkIncTable.UiText, WrongZone, Argument( 5, zone ) ) );
	}

	/// <summary>The objectives (<c>0x55CA60</c>): the requirements, then the status.</summary>
	// [BIN:TPI-EXE:0x55CA60 objectives] z = the current zone record's +0x64; UITEXT 83 and "\n"; with Promotion.Number (0x5F0120): rank < Number gives the hint by counter (2: 94; 4: z == 1 ? 95 : 100;
	// 10: z == 0 ? 96 : 101; 14: z == 1 ? 97 : 102; any other counter formats nothing and appends the last formatted text, 0xA4E56C), else 99 with the string argument 12 = PROMOTIONLEVELS[Number],
	// then "\n"; for i below the count of the mission's exit statistics with a type (0x5F02F0): STAT_TITLES[0x9BED18[status i's type].requirement] with the integer argument 0 = status i's +8, "\n";
	// RequiredChallenges.Number > 0 (0x5F00B0): STAT_TITLES 3 with it, "\n"; "\n"; Obstacle.GoldenTickets n != 0: UITEXT 85, "\n", STAT_TITLES 13 (n == 1) or 14 with n, "\n\n" (0x9B19C4);
	// UITEXT 86, "\n"; with Promotion.Number: 98 with PROMOTIONLEVELS[rank], "\n"; for each statistic STAT_TITLES[status line] with status i's +0x18, "\n"; RequiredChallenges.Number > 0: STAT_TITLES 4
	// with the required zone record's +0xF0, "\n"; n != 0: STAT_TITLES 15 with the career's tickets (0x5F05E0, +0x1074)
	private void ComposeObjectives()
	{
		var career = Career;
		var mission = career.Mission;
		var zone = CurrentZoneNumber();
		var counter = career.MissionCounter;
		details.Clear();
		details.Append( Format( ThemeParkIncTable.UiText, Requirements ) ).Append( '\n' );
		var required = mission.PromotionNumber;
		if ( required != 0 )
		{
			if ( career.Rank < required )
			{
				int? hint = counter switch
				{
					2 => 94,
					4 => zone == 1 ? 95 : 100,
					10 => zone == 0 ? 96 : 101,
					14 => zone == 1 ? 97 : 102,
					_ => null
				};
				details.Append( hint is { } id ? Format( ThemeParkIncTable.UiText, id ) : lastFormatted );
			}
			else
				details.Append( Format( ThemeParkIncTable.UiText, PromotedWellDone, Argument( 12, texts.Format( ThemeParkIncTable.PromotionLevels, required ) ) ) );
			details.Append( '\n' );
		}
		var count = Enumerable.Range( 0, ThemeParkIncMission.ExitStatisticCapacity ).Count( index => mission.ExitStatistic( index ).Type != 0 );
		for ( var index = 0; index < count; index++ )
		{
			details.Append( Format( ThemeParkIncTable.StatTitles, Line( career.StatusWord( index, 1 ) ).Requirement, Argument( 0, ThemeParkIncTexts.Int( career.StatusWord( index, 2 ) ) ) ) );
			details.Append( '\n' );
		}
		var (challenges, won) = RequiredChallenges();
		if ( challenges > 0 )
			details.Append( Format( ThemeParkIncTable.StatTitles, WinPolarChallenges, Argument( 0, ThemeParkIncTexts.Int( challenges ) ) ) ).Append( '\n' );
		details.Append( '\n' );
		var tickets = mission.ObstacleGoldenTickets;
		if ( tickets != 0 )
		{
			details.Append( Format( ThemeParkIncTable.UiText, TicketsNeeded ) ).Append( '\n' );
			details.Append( Format( ThemeParkIncTable.StatTitles, tickets == 1 ? OneGoldenTicket : GoldenTicketsCount, Argument( 0, ThemeParkIncTexts.Int( tickets ) ) ) );
			details.Append( "\n\n" );
		}
		details.Append( Format( ThemeParkIncTable.UiText, ObjectiveStatus ) ).Append( '\n' );
		if ( required != 0 )
			details.Append( Format( ThemeParkIncTable.UiText, CurrentPosition, Argument( 12, texts.Format( ThemeParkIncTable.PromotionLevels, career.Rank ) ) ) ).Append( '\n' );
		for ( var index = 0; index < count; index++ )
		{
			details.Append( Format( ThemeParkIncTable.StatTitles, Line( career.StatusWord( index, 1 ) ).Status, Argument( 0, ThemeParkIncTexts.Int( career.StatusWord( index, 6 ) ) ) ) );
			details.Append( '\n' );
		}
		if ( challenges > 0 )
			details.Append( Format( ThemeParkIncTable.StatTitles, PolarChallengesWon, Argument( 0, ThemeParkIncTexts.Int( won ) ) ) ).Append( '\n' );
		if ( tickets != 0 )
			details.Append( Format( ThemeParkIncTable.StatTitles, GoldenTicketsAvailable, Argument( 0, ThemeParkIncTexts.Int( career.GoldenTickets ) ) ) );
	}

	/// <summary>The obstacle group's lines in obstacle state 2 (<c>0x55D1B0</c>).</summary>
	// [BIN:TPI-EXE:0x55D1B0 obstacle lines] 0xCEC65 = UITEXT 84; 0xCEC66 = with Obstacle.GoldenTickets n != 0: UITEXT 85, " " (0x9BBC54), STAT_TITLES 13 (n == 1) or 14 with n, else ""; 0xCEC67 = UITEXT 86,
	// "\n", with n != 0 STAT_TITLES 15 with the career's tickets and "\n", then 93 when every one of the 20 slots (0x64E940 +0x14, stride 12) has +0 == 7 or word +8 != 0, else 92
	// [EXT:tpi-capability-gates] the slots belong to the staff roles OpenTPW does not port: without them no slot is reported filled and the status says 92
	public (string Heading, string Tickets, string Status) ComposeObstacleLines()
	{
		var career = Career;
		var tickets = career.Mission.ObstacleGoldenTickets;
		var heading = Format( ThemeParkIncTable.UiText, TrainedStaffRequired );
		var ticketLine = tickets != 0
			? Format( ThemeParkIncTable.UiText, TicketsNeeded ) + " " + Format( ThemeParkIncTable.StatTitles, tickets == 1 ? OneGoldenTicket : GoldenTicketsCount, Argument( 0, ThemeParkIncTexts.Int( tickets ) ) )
			: "";
		var status = Format( ThemeParkIncTable.UiText, ObjectiveStatus ) + "\n";
		if ( tickets != 0 )
			status += Format( ThemeParkIncTable.StatTitles, GoldenTicketsAvailable, Argument( 0, ThemeParkIncTexts.Int( career.GoldenTickets ) ) ) + "\n";
		status += Format( ThemeParkIncTable.UiText, world.ObstacleStaffSlotsFilled == true ? StaffSufficient : StaffMissing );
		return (heading, ticketLine, status);
	}

	private static (int Requirement, int Status) Line( int type ) => type >= 0 && type < StatisticLines.Count
		? StatisticLines[type]
		: throw new InvalidOperationException( $"Exit statistic type {type} has no line in the table 0x9BED18 (the original would read past it)." );

	// [BIN:TPI-EXE:0x5F00B0 required challenges] *a1 = RequiredChallenges.Number (+0x13E0), *a3 = RequiredChallenges.Theme (+0x13DC), *a2 = 0x74F4B0(Theme) +0xF0
	private (int Number, int Won) RequiredChallenges()
	{
		var mission = Career.Mission;
		if ( Career.ZoneRecord( mission.RequiredChallengesTheme ) is not { } record )
			throw new InvalidOperationException( $"The required challenges' zone {mission.RequiredChallengesTheme} has no career record (the original's 0x74F4B0 would return none and fault)." );
		return (mission.RequiredChallengesNumber, record.ChallengesDone);
	}

	// [BIN:TPI-EXE:0x55C362 current zone] the career's current record (+0x4C) +0x64 through 0x74F4B0, that record's +0x64
	private int CurrentZoneNumber()
	{
		var career = Career;
		if ( career.CurrentZone is not { } current || career.ZoneRecord( current.ParkNumber ) is not { } record )
			throw new InvalidOperationException( "The mission window needs the career's current zone record (the original's 0x74F4B0 would return none and fault)." );
		return record.ParkNumber;
	}

	/// <summary>The page toggle (<c>0x55BED1</c>): the other page, a refresh, the button's text, and the scroll state cleared.</summary>
	// [BIN:TPI-EXE:0x55BED1 page toggle] page ^= 1, 0x55C190, 0x55A510 (UITEXT 988 or 989), 0xA61830 = 0 and 0xA6182C = 0
	private void TogglePage()
	{
		Page = Page == 0 ? 1 : 0;
		Refresh();
	}

	/// <summary>
	/// Proceed (<c>0x55BD79</c>): nothing unless the requirements are met; an obstacle mission posts message 0x24 with the current obstacle, any other mission runs OnMissionComplete; then the
	/// window closes.
	/// </summary>
	// [BIN:TPI-EXE:0x55BD79 proceed] 0x55A620 false: nothing; 0x5F0190 (HasObstacle): message 0x24 with the thing park+0x1EAB6E (0x436140, vtable 0x93A3D0) through the dispatcher 0xA09728
	// (0x436DD0 calls its listeners at once in ascending thing handle order: the staff HQ's rebuild 0x634D20, then the controller's handler 0x5F0F96; ParkEconomy.PressThemeParkIncMissionComplete); else
	// 0x64ED00 -> 0x5F0770; then 0x85049F(window, 4), 0x501CD0(0) and the advisor panel 0x1FA (presentation)
	internal void Proceed()
	{
		if ( !world.PressComplete() )
			return;
		Close();
	}

	private void Cancel()
	{
		session.RefreshArmed = false;
		Close();
	}

	// [BIN:TPI-EXE:0x55BB0A close] message 5 and every button that closes: 0x85049F(window, 4), 0x501CD0(0) and the advisor panel 0x1FA (presentation)
	private void Close() => close();
}
