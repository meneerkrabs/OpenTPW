using OpenTPW.FrontEnd;
using OpenTPW.Online;
using OpenTPW.Online.Api;
using OpenTPW.Online.Client;
using OpenTPW.Online.Coop;
using OpenTPW.Online.Packages;
using OpenTPW.UI.Original;

namespace OpenTPW.UI;

/// <summary>What the online screens need from the game around them.</summary>
public sealed class OnlineHost
{
	public required OnlineSession Session { get; init; }
	/// <summary>The park being played, for publishing and exporting; null in the lobby.</summary>
	public Func<Level?> Level { get; init; } = () => null;
	/// <summary>Starts a read-only visit of a downloaded or imported park.</summary>
	public Action<ParkVisitInfo>? Visit { get; init; }
	/// <summary>
	/// Starts a read-only visit of an original level's starting park (a chat park's, <see cref="ParkSharing.PrepareLevelVisit"/>): the factory builds the visit
	/// once the level's files are there, as the browser copies a level's files only when it is first played.
	/// </summary>
	public Action<string, Func<ParkVisitInfo>>? VisitLevel { get; init; }
	/// <summary>The online globe behind the Online World screen; null where it is not shown (in a park).</summary>
	public Func<GlobeScene?> Globe { get; init; } = () => null;
	/// <summary>[EXT:restored-advice-online] Tells the advisor that a screen opened (the message of <see cref="RestoredAdvice"/>: 328 publish, 329 park details, 331 other parks, 332 find parks, 333 outbox, 334 world); null where there is no advisor.</summary>
	public Action<int>? Advise { get; init; }
	/// <summary>[EXT:online-coop-session] Shares the open park with other players (<see cref="OnlineScreens.ShowBuildTogether"/>); null where the game does not.</summary>
	internal ICoopHosting? Hosting { get; init; }
	/// <summary>[EXT:online-coop-session] Joins a shared park with a code (<see cref="OnlineScreens.ShowJoinSharedPark"/>); null where the game does not.</summary>
	internal ICoopJoining? Joining { get; init; }
}

/// <summary>
/// The OpenTPW online extension (docs/ONLINE.md) in the original UI style. The screens follow the
/// original online screens described in docs/reverse/UI-MAP.md (login, Find Parks, park details,
/// Publish Park, Send Postcard, Unsent Postcards, chat) and draw the original ui.wad models; models
/// that the original files place on screen (f_profile, b_login, list_findprks, list_outbox,
/// list_msgs, b_vote, i_mail) keep their authored places. Text comes from the original UITEXT where
/// it exists and from <see cref="OnlineStrings"/> otherwise.
/// </summary>
// [EXT:ONLINE-UI] native online screens; composition of the original screens and the place of code-positioned controls are OpenTPW's
public sealed class OnlineScreens
{
	private readonly UiScreenStack stack;
	private readonly UiStringTable strings;
	private readonly UiModels models;
	private readonly OnlineHost host;
	private string localStatus = "";
	private ParkVisitInfo? inspected;

	public OnlineScreens( UiScreenStack stack, UiStringTable strings, UiModels models, OnlineHost host )
	{
		this.stack = stack;
		this.strings = strings;
		this.models = models;
		this.host = host;
	}

	private OnlineSession Session => host.Session;

	/// <summary>Authored place of a model the original files position, or <paramref name="fallback"/>.</summary>
	private UiRect Authored( string model, UiRect fallback )
	{
		var frame = models.Get( model )?.GetFrame( 0 );
		return frame == null || frame.Width <= 0 ? fallback : new UiRect( frame.MinX, frame.MinY, frame.Width, frame.Height );
	}

	// ---- Online world (hub) ------------------------------------------------------------------

	/// <summary>
	/// The Online World as the original lays it out (Mac UI table data:0x523f0): the globe fills the screen, the
	/// worldlob panel at the bottom left holds Find Parks, back to your own lobby, zoom and the four rotate arrows, and
	/// five chat satellites sit around the edges, each showing the number of players in its chat park. OpenTPW adds
	/// the login and its own online menu at the top right.
	/// </summary>
	// [DATA:Mac UI table data:0x523f0] root 90111 worldlob (9,620,510,1531); buttons b_findprk 90115 (49,731,202,885) help 435, b_bck2lob 90112 (49,898,202,1051) help 431, b_zoom 90118 (41,1070,143,1173) help 428/429, b_lobup 90117 (182,1077,336,1231) help 426, b_lobleft 90114 (61,1201,214,1355) help 424, b_lobright 90116 (307,1201,460,1355) help 425, b_lobdown 90113 (184,1327,337,1481) help 427; i_sat 90120–90124 help 423
	public void ShowWorld()
	{
		host.Advise?.Invoke( RestoredAdvice.WorldHelp );
		var screen = new OnlineGlobeScreen( host.Globe );
		screen.CityClicked = ShowCityLobby;
		var (find, zoom) = AddGlobePanel( screen, stack.Pop, ShowFindParks );
		// The chat satellites (data:0x523f0, controls 90120–90124): each is a chat park, shows the players in its
		// room and enters it on a click (the original's "chatellite" connector, 0x1b8808; manual: "The number
		// displayed next to each satellite is the number of people currently in this chat park").
		// [BIN:TPW-EXE:0x7596B0 worldlob table] satellites 90120-90124 with their command-3 text regions; the builder 0x4C4FB0 gives
		// each a text object (0x48FDE0, default white) in font slot 6, centred both ways (0x65C428(1, 1)), into which the connector
		// writes the room's "%d" player count
		var satellites = new (float X0, float Y0, float X1, float Y1, UiRect Count, UiAnchor Anchor)[]
		{
			(131, 139, 438, 446, Region( 312, 373, 401, 418 ), UiAnchor.TopLeft), (484, -51, 791, 256, Region( 664, 183, 754, 228 ), UiAnchor.TopLeft),
			(1690, 344, 1997, 651, Region( 1870, 578, 1960, 623 ), UiAnchor.TopRight), (1743, 620, 2050, 927, Region( 1924, 854, 2013, 899 ), UiAnchor.Right),
			(1335, 1199, 1643, 1506, Region( 1516, 1433, 1605, 1478 ), UiAnchor.BottomRight),
		};
		for ( var index = 0; index < satellites.Length && index < Online.Chat.ChatProtocol.SatelliteRoomCount; index++ )
		{
			var (x0, y0, x1, y1, countRegion, anchor) = satellites[index];
			var number = index + 1;
			var satellite = screen.Add( new UiButton { Id = $"satellite{number}", Model = "i_sat", Help = strings.Help( 423 ), Bounds = new UiRect( x0, y0, x1 - x0, y1 - y0 ), Anchor = anchor,
				Clicked = () => Guard( () => ShowChatPark( number, screen ) ) } );
			var count = screen.Add( new UiLabel { Id = $"satellite{number}Count", Font = fonts => fonts.Menu, Color = UiColors.Text, Align = UiAlign.Center,
				Bounds = countRegion, Anchor = anchor,
				Text = () => Session.ChatParks.FirstOrDefault( park => park.Id == number ) is { } park ? park.Players.ToString( System.Globalization.CultureInfo.InvariantCulture ) : "" } );
			screen.Updating += _ => satellite.Enabled = Session.IsLoggedIn && Session.Busy == 0;
		}
		AddCityLabels( screen, null );
		// [EXT:ONLINE-UI] the login and OpenTPW's own online menu (news, postcards, chat, import, account), at the top centre: the
		// original's city labels take the top right
		var login = screen.Add( new UiButton { Id = "login", Text = () => Session.IsLoggedIn ? OnlineStrings.Get( OnlineLabel.LogOut ) : OnlineStrings.Ui( OnlineStrings.OnlineLogin, "Online login" ),
			Clicked = () => { if ( Session.IsLoggedIn ) Guard( () => { Session.Disconnect(); localStatus = ""; } ); else ShowLogin(); },
			Bounds = new UiRect( 804, 40, 440, 100 ), Anchor = UiAnchor.Top } );
		var menu = screen.Add( new UiButton { Id = "menu", Text = () => OnlineStrings.Get( OnlineLabel.OnlineMenu ), Clicked = ShowMenu,
			Bounds = new UiRect( 804, 150, 440, 100 ), Anchor = UiAnchor.Top } );
		// The online status box (Mac UI table data:0x5011c): the original's progress message at the bottom right
		// while an online request runs, here showing the screen's own status line.
		// [EXT:ONLINE-UI] a status is shown once, on the screen on top, and only what came after the screen was opened or came back to the top (StatusText)
		var worldStatus = StatusText( screen, busy: false );
		screen.Add( new UiLabel { Id = "status", Wrap = true, Font = fonts => fonts.Small, Color = UiColors.Value, Align = UiAlign.Left,
			Bounds = new UiRect( 1349, 1418, 630, 60 ), Anchor = UiAnchor.BottomRight, Text = worldStatus } );
		OnlineClient? worldListed = null;
		var chatParkTimer = 0f;
		screen.Updating += context =>
		{
			find!.Enabled = Session.IsLoggedIn && Session.Busy == 0;
			login.Enabled = menu.Enabled = Session.Busy == 0;
			zoom.Help = strings.Help( GlobeCameraMode.ZoomedIn ? 429 : 428 );
			// Each server's cities and chat parks once, as soon as one is chosen (no login needed).
			if ( Session.Client is { } client && client != worldListed && Session.Busy == 0 )
			{
				worldListed = client;
				Guard( Session.FetchCities );
				Guard( Session.FetchChatParks );
			}
			// The satellites' player counts come from the server; refresh them now and then.
			chatParkTimer += context.Delta;
			if ( chatParkTimer > 15 && Session.Busy == 0 && Session.Client != null )
			{
				chatParkTimer = 0;
				Guard( Session.FetchChatParks );
			}
		};
		screen.Back = stack.Pop;
		Push( screen );
	}

	/// <summary>A table rectangle given by its corners.</summary>
	private static UiRect Region( float x0, float y0, float x1, float y1 ) => new( x0, y0, x1 - x0, y1 - y0 );

	/// <summary>
	/// The hovered city's name and park count at the top right of a globe screen (tp.exe table 0x7581A0), empty while no city is
	/// hovered; <paramref name="idle"/> is the name label's text then (null: none).
	/// </summary>
	// [BIN:TPW-EXE:0x4B9A70 city labels] window 93200 (1477,90)-(2048,511) holds the name label 93201 (1477,90)-(2048,240), font slot 5,
	// and the parks label 93202 (1627,275)-(2048,395), font slot 8, both white and centred both ways; 0x4B9980 sets them from the
	// hovered city (its name, and UITEXT 0x1CE with its park count) and clears both when none is hovered
	// [EXT:online-globe-city-labels] the labels are drawn as plain text, without the hilight model their table entries bind with
	// command 1; whether the original drew that model (highlight.MD2) behind a label's text is not known and not reproduced
	private void AddCityLabels( OnlineGlobeScreen screen, Func<string>? idle )
	{
		screen.Add( new UiLabel { Id = "city", Font = fonts => fonts.Title, Color = UiColors.Text, Align = UiAlign.Center, Bounds = Region( 1477, 90, 2048, 240 ), Anchor = UiAnchor.TopRight,
			Text = () => screen.Hovered is { } city ? city.Name : idle?.Invoke() ?? "" } );
		screen.Add( new UiLabel { Id = "cityParks", Font = fonts => fonts.Small, Color = UiColors.Text, Align = UiAlign.Center, Bounds = Region( 1627, 275, 2048, 395 ), Anchor = UiAnchor.TopRight,
			Text = () => screen.Hovered is { } city ? OnlineStrings.Ui( OnlineStrings.CityParks, "Parks: " ) + city.Parks : "" } );
	}

	/// <summary>
	/// The worldlob panel of the globe screens: Find Parks (when <paramref name="find"/> is given), back (when
	/// <paramref name="back"/> is given), zoom and the four rotate arrows (Mac table data:0x523f0). Returns Find Parks (null
	/// without it) and zoom.
	/// </summary>
	private (UiButton? Find, UiButton Zoom) AddGlobePanel( UiScreen screen, Action? back, Action? find )
	{
		screen.Add( new UiModelImage { Id = "worldPanel", Model = "worldlob", Bounds = new UiRect( 9, 620, 501, 911 ), Anchor = UiAnchor.BottomLeft } );
		// [BIN:TPW-EXE:0x7596B0 worldlob table] root 90111 (9,620,510,1531): command 1 binds root worldlob, command 2 the root nunupan of f_wlobbg.MD2 and command 3 its region (33,1057,480,1505); the table is built by 0x4C4FB0
		screen.Add( new UiModelImage { Id = "worldButtons", Model = "f_wlobbg", Bounds = new UiRect( 33, 1057, 447, 448 ), Anchor = UiAnchor.BottomLeft } );
		UiButton PanelButton( string id, string model, float x0, float y0, float x1, float y1, int help, Action clicked ) => screen.Add( new UiButton
		{
			Id = id, Model = model, Help = strings.Help( help ), Clicked = clicked,
			Bounds = new UiRect( x0, y0, x1 - x0, y1 - y0 ), Anchor = UiAnchor.BottomLeft
		} );
		var findButton = find == null ? null : PanelButton( "findParks", "b_findprk", 49, 731, 202, 885, 435, find );
		if ( back != null )
			PanelButton( "ownLobby", "b_bck2lob", 49, 898, 202, 1051, 431, back );
		var zoom = PanelButton( "zoom", "b_zoom", 41, 1070, 143, 1173, 428, GlobeCameraMode.ToggleZoom );
		PanelButton( "rotateUp", "b_lobup", 182, 1077, 336, 1231, 426, () => GlobeCameraMode.Turn( 0, 1 ) );
		PanelButton( "rotateLeft", "b_lobleft", 61, 1201, 214, 1355, 424, () => GlobeCameraMode.Turn( -1, 0 ) );
		PanelButton( "rotateRight", "b_lobright", 307, 1201, 460, 1355, 425, () => GlobeCameraMode.Turn( 1, 0 ) );
		PanelButton( "rotateDown", "b_lobdown", 184, 1327, 337, 1481, 427, () => GlobeCameraMode.Turn( 0, -1 ) );
		return (findButton, zoom);
	}

	// ---- Chat parks (the satellites) -----------------------------------------------------------

	/// <summary>The package author of a chat park built from an original level (<see cref="ParkSharing.PrepareLevelVisit"/>).</summary>
	public const string ChatParkAuthor = "OpenTPW";

	/// <summary>
	/// A satellite's click on <paramref name="world"/>: its chat park's details (<see cref="ShowChatParkDetails"/>) from the scenes the server lists for the
	/// satellites, while the globe is still open; a server without scenes (an older OpenTPW server) enters the satellite's room in the overhead chat view.
	/// </summary>
	// [BIN:TPW-EXE:0x4C56E0 satellite click] satellite i (controls 0x16008-0x1600C, the index stored by 0x4C4FB0) runs 0x5B7910(i - 1): entry i of the chat satellite
	// list, read from the client's copy without a request, the globe's panels hidden and the park details 0x4C0AE0 opened for it; their cancel (0x4C4FA0) brings
	// the globe back
	// [EXT:online-chat-park-scene] OpenTPW asks the server for the satellites' scenes at the click
	private void ShowChatPark( int number, UiScreen world ) => Session.FetchChatParkScenes( scenes =>
	{
		if ( !ReferenceEquals( stack.Top, world ) )
			return;
		if ( scenes == null )
			Guard( () => EnterChatRoom( number ) );
		else if ( scenes.FirstOrDefault( scene => scene.Id == number ) is { } scene )
			ShowChatParkDetails( scene );
		else
			// the server keeps no chat park for this satellite (and refuses its room)
			localStatus = OnlineStrings.ChatNotice( (int)Online.Chat.ChatNotice.NoPark, null );
	} );

	/// <summary>
	/// A chat park's details as the original showed a satellite's park: the chat park's name as the title, its theme, its description and Enter (to its
	/// park, <see cref="EnterChatPark"/>); the owner's action is shown disabled and the statistics rows stay empty.
	/// </summary>
	// [BIN:TPW-EXE:0x4C0AE0 park details] at 0x4C0C06 the dialog (table 0x759170, the Mac's data:0x51150) looks up the park's city (0x5BC9C0); with that city's flag
	// +0x60 set it fills only the first row (0xE5937) with "%s : %s" (0x7593D4) of UITEXT 271 and the theme's name (0x5BD270), besides the description (0xE5930)
	// and the park's name as the title (0xE5931), and disables the owner action (0xE592F); the statistics rows get no text
	// [EXT:online-chat-park-details] the satellites' details are always this variant, titled with the chat park's name: whether city 1 carried the flag is not
	// known, and OpenTPW's server names its chat parks, not their parks
	public void ShowChatParkDetails( ChatParkScene scene )
	{
		host.Advise?.Invoke( RestoredAdvice.ParkDetailsHelp );
		var screen = new UiScreen( ChatParkDetailsScreenName );
		stack.RemoveWhere( other => other.Name == ChatParkDetailsScreenName );
		var window = new UiRect( 248, 30, 1552, 977 );
		screen.Add( new UiModelImage { Id = "window", Model = "w_big", Bounds = window, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "parkName", Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center,
			Bounds = new UiRect( 512, 75, 1024, 80 ), Anchor = UiAnchor.Center, Text = () => scene.Name } );
		screen.Add( new UiLabel { Id = "theme", Font = fonts => fonts.Label, Bounds = new UiRect( 380, 233, 481, 45 ), Anchor = UiAnchor.Center,
			Text = () => scene.Level is { } level ? ChatParkThemeRow( level ) : "" } );
		screen.Add( new UiLabel { Id = "description", Wrap = true, Font = fonts => fonts.Small,
			Bounds = new UiRect( 922, 207, 680, 448 ), Anchor = UiAnchor.Center, Text = () => scene.Description } );
		var enter = screen.Add( new UiButton { Id = "visit", Text = () => OnlineStrings.Get( OnlineLabel.VisitReadOnly ),
			Clicked = () => Guard( () => EnterChatPark( scene ) ),
			Font = fonts => fonts.Label, Bounds = new UiRect( 1481, 800, 154, 153 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "unpublish", Text = () => OnlineStrings.Get( OnlineLabel.Unpublish ), Enabled = false,
			Font = fonts => fonts.Label, Bounds = new UiRect( 1322, 800, 154, 153 ), Anchor = UiAnchor.Center } );
		AddCloseButton( screen, 1671, 820 );
		AddStatus( screen, window );
		screen.Updating += _ => enter.Enabled = Session.IsLoggedIn && Session.Busy == 0;
		screen.Back = stack.Pop;
		Push( screen );
	}

	/// <summary>The chat park details' screen name.</summary>
	public const string ChatParkDetailsScreenName = "chatParkDetails";

	/// <summary>The chat park details' theme row: "Theme : " and the theme's name, as tp.exe 0x4C0AE0 formats it ("%s : %s").</summary>
	internal static string ChatParkThemeRow( string level ) => $"{OnlineStrings.Ui( OnlineStrings.ThemeLabel, "Theme" )} : {OnlineStrings.Theme( level )}";

	/// <summary>
	/// Enter in a chat park's details: the visit of its park, in its satellite's room — the published park the server names, downloaded as any visit, or
	/// else the starting park of its original level, built when its files are there; without either (or outside the game flow, which starts visits) the
	/// room in the overhead chat view.
	/// </summary>
	// [BIN:TPW-EXE:0x4C0880 park details enter] Enter (0xE592E) runs the visit as for any park (0x5B86A0 → 0x5B8910: the download and MarkParkVisited, then game type 1
	// and the chat mode, 0x54ED1E → 0x4BF460), and the chat connection then creates the park's own room ("%d" of the park id, CreateRoom at 0x4E6A67)
	// [EXT:online-chat-park-scene] a chat park's visit joins its satellite's standing room, the room older games enter in the overhead view and the satellite's
	// count counts; a satellite without a published park visits the starting park of its server-named level
	private void EnterChatPark( ChatParkScene scene )
	{
		var room = Online.Chat.ChatProtocol.SatelliteRoom( scene.Id );
		if ( host.Visit != null && scene.Park is { } park )
			Session.Download( park, path => Inspect( path, start: true, park.Id, room ) );
		// the level comes from the server: only a theme's level is ever queued or read
		else if ( host.VisitLevel is { } visitLevel && scene.Level is { } level && ParkSharing.IsThemeLevel( level ) )
			visitLevel( level, () => ParkSharing.PrepareLevelVisit( level, scene.Name, scene.Description, ChatParkAuthor ) with { ChatRoom = room } );
		else
			EnterChatRoom( scene.Id );
	}

	/// <summary>
	/// A satellite's room in the overhead chat view (<see cref="ShowChat"/>). [EXT:online-coop-session] In a shared park the chat stays the session's: the view shows
	/// its room.
	/// </summary>
	private void EnterChatRoom( int number )
	{
		if ( !Session.InSharedPark )
			Session.JoinRoom( Online.Chat.ChatProtocol.SatelliteRoom( number ) );
		ShowChat();
	}

	// ---- City lobby (Mac UI table data:0x50172) -----------------------------------------------

	/// <summary>
	/// The lobby of <paramref name="city"/>: its ten most-voted parks as islands, with the citylob panel (other parks,
	/// back to your own lobby, the online world, enter this park, previous/next island), the selected park's rank at the
	/// top and its name at the bottom.
	/// </summary>
	// [DATA:Mac UI table data:0x50172] root 18900 citylob (28,541,476,1521); b_listpark 18901 (66,642,219,795) help 349, b_bck2lob 18902 (66,799,219,953) help 348, b_worldview 18903 (66,957,219,1110) help 346, b_entpark 18904 (66,1113,219,1267) help 345, b_lobleft 18907 (95,1299,249,1453) help 343, b_lobright 18908 (260,1299,413,1453) help 344; labels 0x49D9 name (477,1282,1571,1457) and 0x49DA rank (844,118,1204,253)
	public void ShowCityLobby( CityInfo city )
	{
		if ( !Session.IsLoggedIn )
		{
			localStatus = OnlineStrings.Ui( OnlineStrings.OnlineLogin, "Online login" );
			ShowLogin();
			return;
		}
		var screen = new CityLobbyScreen( city );
		screen.Add( new UiModelImage { Id = "cityPanel", Model = "citylob", Bounds = new UiRect( 28, 541, 448, 980 ), Anchor = UiAnchor.BottomLeft } );
		// [BIN:TPW-EXE:0x757D88 citylob table] root 18900 (28,541,476,1521): command 1 binds root citylob, command 2 the root lobbutbg of f_lobbutbg.MD2 and command 3 its region (67,1273,438,1484)
		screen.Add( new UiModelImage { Id = "cityButtons", Model = "f_lobbutbg", Bounds = new UiRect( 67, 1273, 371, 211 ), Anchor = UiAnchor.BottomLeft } );
		UiButton PanelButton( string id, string model, float x0, float y0, float x1, float y1, int help, Action clicked ) => screen.Add( new UiButton
		{
			Id = id, Model = model, Help = strings.Help( help ), Clicked = clicked,
			Bounds = new UiRect( x0, y0, x1 - x0, y1 - y0 ), Anchor = UiAnchor.BottomLeft
		} );
		PanelButton( "otherParks", "b_listpark", 66, 642, 219, 795, 349, () => ShowOtherParks( city ) );
		PanelButton( "ownLobby", "b_bck2lob", 66, 799, 219, 953, 348, () => { while ( stack.Top != null && stack.Top.Name is "cityLobby" or "onlineWorld" ) stack.Pop(); } );
		PanelButton( "worldView", "b_worldview", 66, 957, 219, 1110, 346, stack.Pop );
		var enter = PanelButton( "enterPark", "b_entpark", 66, 1113, 219, 1267, 345,
			() => { if ( screen.SelectedPark is { } park ) Guard( () => Session.Download( park, path => Inspect( path, start: true, park.Id ) ) ); } );
		PanelButton( "previousIsland", "b_lobleft", 95, 1299, 249, 1453, 343, () => screen.Step( -1 ) );
		PanelButton( "nextIsland", "b_lobright", 260, 1299, 413, 1453, 344, () => screen.Step( 1 ) );
		// [BIN:STP-PPC:0x101834A0 item detail overlay] label 0x49D9: the selected item's name, font slot 5, colour (252,232,7); label 0x49DA: "%d" of its index + 1, font slot 0; both hidden without a selection
		var name = screen.Add( new UiLabel { Id = "parkName", Font = fonts => fonts.Title, Color = new Veldrid.RgbaByte( 252, 232, 7, 255 ), Align = UiAlign.Center,
			Bounds = new UiRect( 477, 1282, 1094, 175 ), Anchor = UiAnchor.Bottom, Text = () => screen.SelectedPark?.Name ?? "" } );
		var rank = screen.Add( new UiLabel { Id = "rank", Font = fonts => fonts.Menu, Color = UiColors.Text, Align = UiAlign.Center, Bounds = new UiRect( 844, 118, 360, 135 ), Anchor = UiAnchor.Top,
			Text = () => (screen.Selected + 1).ToString( System.Globalization.CultureInfo.InvariantCulture ) } );
		// [EXT:ONLINE-UI] the city's name and the fetch status while no park is shown; a city without parks says so (UITEXT 306)
		var cityStatus = StatusText( screen, busy: false );
		var status = screen.Add( new UiLabel { Id = "cityStatus", Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center, Bounds = new UiRect( 524, 118, 1000, 135 ), Anchor = UiAnchor.Top,
			Text = () => $"{city.Name}\n{(cityStatus() is { Length: > 0 } text ? text : Session.CityParks is { Count: 0 } && ReferenceEquals( stack.Top, screen ) ? OnlineStrings.Ui( OnlineStrings.NoSearchResults, "No search results" ) : "")}" } );
		screen.Updating += _ =>
		{
			enter.Enabled = screen.SelectedPark != null && Session.Busy == 0 && host.Visit != null;
			name.Visible = rank.Visible = screen.SelectedPark != null;
			status.Visible = screen.SelectedPark == null;
			if ( Session.CityParks is { } parks && !ReferenceEquals( parks, screen.Parks ) )
			{
				screen.Parks = parks;
				screen.Selected = 0;
			}
		};
		screen.Back = stack.Pop;
		Guard( () => Session.FetchCityParks( city.Id ) );
		Push( screen );
	}

	// ---- OpenTPW online menu -------------------------------------------------------------------

	/// <summary>The online menu's buttons: the first one's top below the window's title, the step between buttons and their height (ten buttons and the postcard line under them fit the window; the height keeps the label box large enough for the bubbly menu font).</summary>
	internal const float MenuButtonsTop = 170, MenuButtonStep = 100, MenuButtonHeight = 96;

	/// <summary>OpenTPW's online functions that have no place on the original globe screen.</summary>
	public void ShowMenu()
	{
		// [EXT:ONLINE-UI] the menu has no original counterpart: a window opened from it takes its place, so the menu's buttons do not show around that window
		var screen = new UiScreen( "onlineMenu" ) { HiddenWhenCovered = true };
		var window = UiDialogs.CenteredWindow( 1200, 1460 );
		UiDialogs.AddWindow( screen, window, "w_big", () => OnlineStrings.Get( OnlineLabel.OnlineMenu ) );
		var level = host.Level();
		// [EXT:ONLINE-UI] a status belongs to the screen it was reported on: a note of an earlier screen does not stay in the menu
		localStatus = "";
		var entries = new List<(string Id, Func<string> Label, Action Clicked, Func<bool> Enabled, Func<string?>? Why)>
		{
			("login", () => Session.IsLoggedIn ? OnlineStrings.Get( OnlineLabel.LogOut ) : OnlineStrings.Ui( OnlineStrings.OnlineLogin, "Online login" ),
				() => { if ( Session.IsLoggedIn ) Guard( () => { Session.Disconnect(); localStatus = ""; } ); else ShowLogin(); }, () => true, null),
			("news", () => OnlineStrings.Get( OnlineLabel.News ), ShowNews, () => Session.Client != null, null),
			("findParks", () => OnlineStrings.Ui( OnlineStrings.FindParks, "Find parks" ), ShowFindParks, () => Session.IsLoggedIn, null),
			// [EXT:mods] a greyed Publish park says why on hover: no park open, a visit, a shared park's guest copy, or community mods in the park (ParkSharing.PublishRefusal)
			("publish", () => OnlineStrings.Ui( OnlineStrings.PublishPark, "Publish park" ), ShowPublish, () => ParkSharing.PublishRefusal( level ) == null, () => ParkSharing.PublishRefusal( level )),
			("sendPostcard", () => OnlineStrings.Ui( OnlineStrings.SendPostcard, "Send postcard" ), ShowSendPostcard, () => true, null),
			("outbox", () => OnlineStrings.Ui( OnlineStrings.UnsentPostcards, "Unsent postcards" ), ShowOutbox, () => true, null),
			("inbox", () => OnlineStrings.Get( OnlineLabel.Inbox ), ShowInbox, () => true, null),
			("chat", () => OnlineStrings.Get( OnlineLabel.Chat ), ShowChat, () => Session.IsLoggedIn, null),
			("import", () => OnlineStrings.Get( OnlineLabel.ImportFile ), ShowImport, () => true, null),
			// [EXT:ONLINE-UI] OpenTPW servers let players delete their own account (docs/SERVER.md).
			("deleteAccount", () => OnlineStrings.Get( OnlineLabel.DeleteAccount ), ShowDeleteAccount, () => Session.IsLoggedIn, null),
		};
		UiButton? lastButton = null;
		for ( var index = 0; index < entries.Count; index++ )
		{
			var entry = entries[index];
			var button = screen.Add( new UiButton
			{
				Id = entry.Id,
				Text = entry.Label,
				Clicked = entry.Clicked,
				Bounds = new UiRect( window.X + 130, window.Y + MenuButtonsTop + index * MenuButtonStep, window.Width - 260, MenuButtonHeight ),
				Anchor = UiAnchor.Center
			} );
			if ( entry.Why is { } why )
			{
				button.HelpSource = why;
				button.HelpWhenDisabled = true;
			}
			screen.Updating += _ => button.Enabled = entry.Enabled() && Session.Busy == 0;
			lastButton = button;
		}
		// The original mail icon (i_mail) at its authored place while postcards wait in the inbox.
		screen.Add( new UiModelImage { Id = "mail", Model = "i_mail", Bounds = Authored( "i_mail", new UiRect( 1500, 1180, 120, 110 ) ), Anchor = UiAnchor.BottomRight } );
		// [DATA:Language/English/UITEXT.str:472] the original reminded the player of unsent postcards before going online ("... go online to send them"); it shows while logged out only. Logged in, nothing sends the
		// outbox by itself, so the line says where to send it. It sits right under the last button, whatever their number.
		screen.Add( new UiLabel { Id = "waiting", Wrap = true, Font = fonts => fonts.Small, Align = UiAlign.Center,
			Bounds = new UiRect( window.X + 120, lastButton!.Bounds.Bottom + 14, window.Width - 240, 90 ), Anchor = UiAnchor.Center,
			Text = () => Session.Folders.List( Session.Folders.Outbox ).Any( entry => entry.Card != null )
				? Session.IsLoggedIn ? OnlineStrings.Get( OnlineLabel.OutboxReadyToSend ) : OnlineStrings.Ui( OnlineStrings.OutboxWaiting, "You have postcards in your outbox, waiting to be sent." ) : "" } );
		screen.Updating += _ => screen.Find( "mail" )!.Visible = Session.Inbox.Count > 0;
		// [EXT:mods] the reason Publish park is greyed also stands in the status line while there is nothing else to say: the hover help needs a pointer and popup help
		AddStatus( screen, window, () => ParkSharing.PublishRefusal( level ) is { } why ? $"{OnlineStrings.Ui( OnlineStrings.PublishPark, "Publish park" )}: {why}" : "" );
		AddBack( screen, window );
		Push( screen );
	}

	// ---- Build together: sharing the open park (OpenTPW, C2d-2) --------------------------------

	/// <summary>The Build Together screen's name.</summary>
	internal const string BuildTogetherScreenName = "buildTogether";

	/// <summary>The lobby's Build Together screen's name (C2d-2b).</summary>
	internal const string ShareSavedParkScreenName = "shareSavedPark";

	/// <summary>
	/// [EXT:online-coop-session] The choices of a shared park (C2d-2), on both Build Together screens: its title (by default the player's park, by the online name, until
	/// the player writes one), whether it is on the open list (by default not: code and invitation only), and how many players it takes (2 to 4, by default 4).
	/// </summary>
	private sealed class ShareChoices
	{
		private readonly Func<string> defaultTitle;
		private bool titleChosen;
		private bool listed;
		private int players = CoopProtocol.MaximumPlayers;
		private readonly UiLabel titleLabel;
		private readonly UiButton listedButton;
		private readonly UiButton playersButton;

		/// <summary>The choices from <paramref name="top"/> down, as <paramref name="initial"/> when given (the player's choices kept for another try).</summary>
		public ShareChoices( UiScreen screen, UiStringTable strings, Func<string> defaultTitle, float x, float top, float width, CoopShareRequest? initial )
		{
			this.defaultTitle = defaultTitle;
			titleLabel = screen.Add( new UiLabel { Id = "titleLabel", Font = fonts => fonts.Label, Text = () => strings.Extra( OpenTpwText.SharedParkTitle ),
				Bounds = new UiRect( x, top + 10, 300, 90 ), Anchor = UiAnchor.Center } );
			Title = screen.Add( new UiTextField { Id = "parkTitle", MaximumLength = CoopProtocol.MaximumTitleLength, Model = "f_text1",
				Bounds = new UiRect( x + 320, top, width - 320, 93 ), Anchor = UiAnchor.Center } );
			Title.Changed = _ => titleChosen = true;
			listedButton = screen.Add( new UiButton { Id = "listed", Clicked = () => listed = !listed,
				Text = () => strings.Extra( listed ? OpenTpwText.SharedParkListed : OpenTpwText.SharedParkUnlisted ),
				Bounds = new UiRect( x, top + 130, width, 100 ), Anchor = UiAnchor.Center } );
			playersButton = screen.Add( new UiButton { Id = "players", Clicked = () => players = players >= CoopProtocol.MaximumPlayers ? 2 : players + 1,
				Text = () => string.Format( strings.Extra( OpenTpwText.SharedParkPlayers ), players ),
				Bounds = new UiRect( x, top + 250, width, 100 ), Anchor = UiAnchor.Center } );
			if ( initial != null )
			{
				Title.Text = initial.Title;
				titleChosen = true;
				listed = initial.Listed;
				players = Math.Clamp( initial.MaximumPlayers, 2, CoopProtocol.MaximumPlayers );
			}
			else
				Title.Text = defaultTitle();
		}

		/// <summary>The title's field (the screens submit with it and focus it).</summary>
		public UiTextField Title { get; }

		/// <summary>Shows or hides the choices; the title follows the player's name (known after a login on the screen) until the player writes one.</summary>
		public void Update( bool visible )
		{
			if ( !titleChosen )
				Title.Text = defaultTitle();
			titleLabel.Visible = Title.Visible = listedButton.Visible = playersButton.Visible = visible;
		}

		/// <summary>The player's choices; null while the title is empty or has a control character.</summary>
		public CoopShareRequest? Request()
		{
			var chosen = Title.Text.Trim();
			return chosen.Length == 0 || chosen.Any( char.IsControl ) ? null : new CoopShareRequest( chosen, listed, players );
		}
	}

	/// <summary>A shared park's default title: the player's park, by the online name (empty while no name is known).</summary>
	private string DefaultShareTitle() => (Session.Client?.Session?.Name ?? Session.Settings.PlayerName ?? "").Trim() is { Length: > 0 } name
		? string.Format( strings.Extra( OpenTpwText.SharedParkDefaultTitle ), name ) : "";

	/// <summary>
	/// [EXT:online-coop-session] Build Together (C2d-2; docs/COOP.md, "Sharing the park from the game"): shares the open park with other players, who watch the host
	/// build, walk around and chat, and build too once the host lets them (C3a-2). Before sharing: why the park cannot be shared, or a login, then the shared park's title, whether
	/// it is on the open list, and how many players it takes. While it is shared: its code, the players with a switch that lets each guest build or only watch and a removal, an
	/// invitation by name, and its end for everyone (the park stays the host's). Every line is in the player's language.
	/// </summary>
	/// <param name="choices">The shared park's choices to start from (a share from the lobby that could not start keeps the player's); the defaults when null.</param>
	// [EXT:own-window-sizes] the window and the places of its controls are OpenTPW's: the original has no shared parks
	public void ShowBuildTogether( CoopShareRequest? choices = null )
	{
		if ( host.Hosting is not { } hosting )
			return;
		var screen = new UiScreen( BuildTogetherScreenName );
		var window = UiDialogs.CenteredWindow( 1400, 1300 );
		UiDialogs.AddWindow( screen, window, "w_big", () => strings.Extra( OpenTpwText.BuildTogether ) );
		var x = window.X + 120;
		var width = window.Width - 240;
		CoopShare? Attempt() => hosting.Sharing is { } share && (share.Stage != CoopShareStage.Done || share.Outcome is not (CoopShareOutcome.Cancelled or CoopShareOutcome.None)
			&& !(share.Outcome == CoopShareOutcome.Ended && share.EndedBySelf)) ? share : null;
		bool Starting() => Attempt() is null;
		bool Hosting() => Attempt() is { Stage: CoopShareStage.Hosting };
		bool Waiting() => Attempt() is { Stage: CoopShareStage.Preparing or CoopShareStage.Creating or CoopShareStage.Cancelling };
		bool Finished() => Attempt() is { Stage: CoopShareStage.Done };

		// before sharing
		var intro = screen.Add( new UiLabel { Id = "intro", Wrap = true, Font = fonts => fonts.Small, Text = () => strings.Extra( OpenTpwText.ShareParkIntro ),
			Bounds = new UiRect( x, window.Y + 150, width, 190 ), Anchor = UiAnchor.Center } );
		var message = screen.Add( new UiLabel { Id = "message", Wrap = true, Font = fonts => fonts.Small, Color = UiColors.Value,
			Bounds = new UiRect( x, window.Y + 350, width, 120 ), Anchor = UiAnchor.Center,
			Text = () => Attempt() is { } share ? ShareMessage( share ) : !Session.IsLoggedIn ? strings.Extra( OpenTpwText.LogInToShare )
				: hosting.ShareRefusal is { } refusal and not CoopShareRefusal.None ? RefusalText( refusal ) : "" } );
		var form = new ShareChoices( screen, strings, DefaultShareTitle, x, window.Y + 490, width, choices );
		void Share()
		{
			if ( form.Request() is { } request )
				hosting.StartSharing( request );
		}
		form.Title.Submitted = Share;
		var share = screen.Add( new UiButton { Id = "share", Text = () => strings.Extra( OpenTpwText.SharePark ), Clicked = Share,
			Bounds = new UiRect( x, window.Y + 880, 560, 110 ), Anchor = UiAnchor.Center } );
		var login = screen.Add( new UiButton { Id = "login", Text = () => OnlineStrings.Ui( OnlineStrings.OnlineLogin, "Online login" ), Clicked = ShowLogin,
			Bounds = new UiRect( x + 600, window.Y + 880, 560, 110 ), Anchor = UiAnchor.Center } );
		// while it is being shared
		var cancel = screen.Add( new UiButton { Id = "cancel", Text = () => strings.Extra( OpenTpwText.Cancel ), Clicked = () => hosting.Sharing?.Cancel(),
			Bounds = new UiRect( x, window.Y + 880, 560, 110 ), Anchor = UiAnchor.Center } );
		// after a failure: the start again, or another game's link taken over
		var again = screen.Add( new UiButton { Id = "again", Text = () => strings.Extra( OpenTpwText.Back ), Clicked = hosting.DismissSharing,
			Bounds = new UiRect( x, window.Y + 880, 560, 110 ), Anchor = UiAnchor.Center } );
		var takeOver = screen.Add( new UiButton { Id = "takeOver", Text = () => strings.Extra( OpenTpwText.SharedParkTakeOver ),
			Clicked = () => { hosting.DismissSharing(); Session.Coop.Open( takeOver: true ); },
			Bounds = new UiRect( x + 600, window.Y + 880, 560, 110 ), Anchor = UiAnchor.Center } );
		// while it is hosted
		var code = screen.Add( new UiLabel { Id = "code", Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center,
			Text = () => Attempt()?.State is { } state ? string.Format( strings.Extra( OpenTpwText.SharedParkCode ), state.Code ) : "",
			Bounds = new UiRect( x, window.Y + 150, width, 110 ), Anchor = UiAnchor.Center } );
		var visibility = screen.Add( new UiLabel { Id = "visibility", Font = fonts => fonts.Small, Align = UiAlign.Center,
			Text = () => Attempt()?.State is { } state ? strings.Extra( state.Listed ? OpenTpwText.SharedParkListed : OpenTpwText.SharedParkUnlisted ) : "",
			Bounds = new UiRect( x, window.Y + 260, width, 70 ), Anchor = UiAnchor.Center } );
		var rows = new List<(UiLabel Name, UiButton Rights, UiButton Remove)>();
		for ( var index = 0; index < CoopProtocol.MaximumPlayers; index++ )
		{
			var row = index;
			CoopMemberInfo? Member() => Attempt()?.State is { } state && row < state.Members.Count ? state.Members.OrderBy( member => member.Slot ).ElementAt( row ) : null;
			var name = screen.Add( new UiLabel { Id = $"member{row}", Font = fonts => fonts.Label,
				Text = () => Member() is { } member && Attempt()?.State is { } state ? MemberText( member, state ) : "",
				Bounds = new UiRect( x, window.Y + 350 + row * 90, width - 840, 80 ), Anchor = UiAnchor.Center } );
			// [EXT:online-coop-permissions] the host sets each guest's rights (Q7, C4b): the button names the rights the shared park's applied state gives the guest ("Watches" with none), and
			// a click opens the guest's rights dialog
			var rights = screen.Add( new UiButton { Id = $"rights{row}",
				Text = () => Member() is { } member && Attempt() is { } share ? RightsText( share.Rights( member.Slot ) ) : "",
				Clicked = () =>
				{
					if ( Member() is { } member && Attempt() is { } share )
						ShowSeatRights( share, member.Id, member.Slot, member.Name );
				},
				Bounds = new UiRect( x + width - 820, window.Y + 345 + row * 90, 400, 85 ), Anchor = UiAnchor.Center } );
			var remove = screen.Add( new UiButton { Id = $"remove{row}", Text = () => strings.Extra( OpenTpwText.RemovePlayer ),
				Clicked = () =>
				{
					if ( Member() is not { } member )
						return;
					UiScreen? confirm = null;
					confirm = UiDialogs.Message( "confirmRemovePlayer", () => string.Format( strings.Extra( OpenTpwText.ConfirmRemovePlayer ), member.Name ),
						(() => strings.Value( UIStrings.Yes ), () => { stack.Pop(); hosting.Sharing?.Kick( member.Id ); }),
						(() => strings.Value( UIStrings.No ), () => stack.Pop()) );
					stack.Push( confirm );
				},
				Bounds = new UiRect( x + width - 400, window.Y + 345 + row * 90, 400, 85 ), Anchor = UiAnchor.Center } );
			rows.Add( (name, rights, remove) );
		}
		var inviteField = screen.Add( new UiTextField { Id = "inviteName", MaximumLength = CoopProtocol.MaximumInviteNameLength, Model = "f_text1",
			Bounds = new UiRect( x, window.Y + 730, width - 420, 93 ), Anchor = UiAnchor.Center } );
		// one invitation at a time (CoopShare.InviteAnswerSeconds): the line below speaks of the latest, and a late refusal of an older one has its own line
		void Invite()
		{
			var name = inviteField.Text.Trim();
			if ( name.Length == 0 || hosting.Sharing?.Invite( name ) != true )
				return;
			inviteField.Text = "";
		}
		inviteField.Submitted = Invite;
		var invite = screen.Add( new UiButton { Id = "invite", Text = () => strings.Extra( OpenTpwText.InvitePlayer ), Clicked = Invite,
			Bounds = new UiRect( x + width - 400, window.Y + 725, 400, 100 ), Anchor = UiAnchor.Center } );
		var inviteResult = screen.Add( new UiLabel { Id = "inviteResult", Wrap = true, Font = fonts => fonts.Small, Color = UiColors.Value,
			Text = () => Attempt()?.LastInvite is { } notice ? InviteText( notice ) : "",
			Bounds = new UiRect( x, window.Y + 835, width, 70 ), Anchor = UiAnchor.Center } );
		var inviteLate = screen.Add( new UiLabel { Id = "inviteLate", Wrap = true, Font = fonts => fonts.Small, Color = UiColors.Value,
			Text = () => Attempt()?.LateRefusal is { } notice ? InviteText( notice ) : "",
			Bounds = new UiRect( x, window.Y + 905, width, 70 ), Anchor = UiAnchor.Center } );
		// a removal is answered by its player leaving the list: of its refusals only too many requests is said, on the screen's status line
		CoopError? shownKickRefusal = null;
		var end = screen.Add( new UiButton { Id = "endSharing", Text = () => strings.Extra( OpenTpwText.EndSharing ),
			Clicked = () =>
			{
				UiScreen? confirm = null;
				confirm = UiDialogs.Message( "confirmEndSharing", () => strings.Extra( OpenTpwText.ConfirmEndSharing ),
					(() => strings.Value( UIStrings.Yes ), () => { stack.Pop(); hosting.Sharing?.End(); }),
					(() => strings.Value( UIStrings.No ), () => stack.Pop()) );
				stack.Push( confirm );
			},
			Bounds = new UiRect( x, window.Y + 985, 560, 110 ), Anchor = UiAnchor.Center } );
		screen.Updating += _ =>
		{
			if ( Attempt()?.KickRefusal is { } refused && !ReferenceEquals( refused, shownKickRefusal ) )
			{
				shownKickRefusal = refused;
				localStatus = refused.Reason == CoopReason.Rate ? strings.Extra( OpenTpwText.SharedParkTryLater ) : "";
			}
			var starting = Starting();
			var hosted = Hosting();
			var waiting = Waiting();
			var finished = Finished();
			form.Update( starting );
			intro.Visible = share.Visible = starting;
			login.Visible = starting && !Session.IsLoggedIn;
			share.Enabled = Session.IsLoggedIn && Session.Busy == 0 && hosting.ShareRefusal == CoopShareRefusal.None && form.Request() != null;
			message.Visible = !hosted;
			cancel.Visible = waiting && Attempt() is { Stage: not CoopShareStage.Cancelling };
			again.Visible = finished;
			takeOver.Visible = finished && Attempt() is { Outcome: CoopShareOutcome.LinkStopped, Stop: CoopStop.InUseElsewhere or CoopStop.Replaced };
			code.Visible = visibility.Visible = inviteField.Visible = invite.Visible = inviteResult.Visible = inviteLate.Visible = end.Visible = hosted;
			invite.Enabled = Attempt() is { Inviting: null };
			for ( var index = 0; index < rows.Count; index++ )
			{
				var member = Attempt()?.State is { } state && index < state.Members.Count ? state.Members.OrderBy( info => info.Slot ).ElementAt( index ) : null;
				rows[index].Name.Visible = hosted && member != null;
				rows[index].Rights.Visible = rows[index].Remove.Visible = hosted && member != null && member.Id != Attempt()!.State!.Host;
			}
		};
		AddStatus( screen, window );
		AddBack( screen, window );
		screen.Focus( form.Title );
		Push( screen );
	}

	/// <summary>[EXT:online-coop-permissions] The name of the rights dialog's screen (<see cref="ShowSeatRights"/>).</summary>
	internal const string SeatRightsScreenName = "seatRights";

	/// <summary>A guest's rights as its row's button names them: the offered rights it holds, joined, or "Watches" with none.</summary>
	private string RightsText( CoopRights rights ) => (rights & CoopRightsOffer.Offered) == CoopRights.None ? strings.Extra( OpenTpwText.CoopWatches )
		: string.Join( ", ", CoopRightsOffer.Each.Where( right => rights.HasFlag( right ) ).Select( right => strings.Extra( RightName( right ) ) ) );

	/// <summary>The name of one right.</summary>
	private static OpenTpwText RightName( CoopRights right ) => right switch
	{
		CoopRights.Build => OpenTpwText.CoopRightBuilding,
		CoopRights.Attractions => OpenTpwText.CoopRightAttractions,
		CoopRights.Staff => OpenTpwText.CoopRightStaff,
		CoopRights.Research => OpenTpwText.CoopRightResearch,
		_ => OpenTpwText.CoopRightFinance
	};

	/// <summary>
	/// [EXT:online-coop-permissions] A guest's rights dialog on the Build Together screen (C4b): one switch per offered right, each drawing the shared park's applied bit; a toggle issues the
	/// seat's rights with that bit turned over from the applied ones (<see cref="CoopShare.SetRights"/>). Bound to the sharing attempt <paramref name="share"/>, the member and its seat: once the
	/// attempt no longer hosts or the member left that seat (checked every frame and again at a toggle), it closes and sends nothing.
	/// </summary>
	// [EXT:own-window-sizes] the dialog and the places of its controls are OpenTPW's: the original has no shared parks
	private void ShowSeatRights( CoopShare share, uint memberId, int slot, string name )
	{
		var screen = new UiScreen( SeatRightsScreenName );
		var window = UiDialogs.CenteredWindow( 1100, 300 + CoopRightsOffer.Each.Count * 130 );
		UiDialogs.AddWindow( screen, window, "w_big", () => string.Format( strings.Extra( OpenTpwText.CoopRightsTitle ), name ) );
		bool Bound() => share.Stage == CoopShareStage.Hosting && share.State is { } state && state.Members.Any( member => member.Id == memberId && member.Slot == slot );
		void Close() => stack.RemoveWhere( shown => ReferenceEquals( shown, screen ) );
		var y = window.Y + 170;
		foreach ( var right in CoopRightsOffer.Each )
		{
			var each = right;
			screen.Add( new UiButton { Id = $"right{each}", Text = () => strings.Extra( RightName( each ) ), Selected = () => share.Rights( slot ).HasFlag( each ),
				Clicked = () =>
				{
					if ( !Bound() )
					{
						Close();
						return;
					}
					share.SetRights( slot, share.Rights( slot ) ^ each );
				},
				Bounds = new UiRect( window.X + 140, y, window.Width - 280, 100 ), Anchor = UiAnchor.Center } );
			y += 130;
		}
		screen.Updating += _ =>
		{
			if ( !Bound() )
				Close();
		};
		AddBack( screen, window, Close );
		Push( screen );
	}

	/// <summary>
	/// [EXT:online-coop-session] The lobby's Build Together screen (C2d-2b; docs/COOP.md, "Sharing a saved park from the lobby"): the player's own saved parks (not the
	/// sandbox, not a shipped park) and the same choices as the park's screen, or a login. Share loads the chosen park (its extensions asked about first) and shares
	/// it once it is the game's park; the park's Build Together screen then shows the attempt.
	/// </summary>
	// [EXT:own-window-sizes] the window and the places of its controls are OpenTPW's: the original has no shared parks
	public void ShowShareSavedPark()
	{
		if ( host.Hosting is not { } hosting )
			return;
		var parks = hosting.SavedParks();
		var screen = new UiScreen( ShareSavedParkScreenName );
		var window = UiDialogs.CenteredWindow( 1400, 1400 );
		UiDialogs.AddWindow( screen, window, "w_big", () => strings.Extra( OpenTpwText.BuildTogether ) );
		var x = window.X + 120;
		var width = window.Width - 240;
		screen.Add( new UiLabel { Id = "intro", Wrap = true, Font = fonts => fonts.Small, Text = () => strings.Extra( OpenTpwText.ShareSavedParkIntro ),
			Bounds = new UiRect( x, window.Y + 150, width, 160 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "message", Wrap = true, Font = fonts => fonts.Small, Color = UiColors.Value,
			Bounds = new UiRect( x, window.Y + 320, width, 80 ), Anchor = UiAnchor.Center,
			Text = () => parks.Count == 0 ? strings.Extra( OpenTpwText.NoSavedParks ) : !Session.IsLoggedIn ? strings.Extra( OpenTpwText.LogInToShare ) : "" } );
		// the rows read as on the Load Park screen: the park's name (marked when cheated or when it needs extensions) and when it was saved
		var list = screen.Add( new UiScrollList
		{
			Id = "savedParks",
			Rows = () => parks.Select( park => FrontEndMenu.SavedParkText( strings, park ) ).ToArray(),
			Trailing = index => parks[index].SavedAt is { } saved
				? strings.Format( UiTextRecord.SaveDate, saved.Day.ToString(), saved.Month.ToString(), saved.Year.ToString() ) + $" {saved.Hour:00}:{saved.Minute:00}" : "",
			Selected = parks.Count == 0 ? -1 : 0,
			Enabled = parks.Count != 0,
			Visible = parks.Count != 0,
			Bounds = new UiRect( x, window.Y + 410, width, 200 ),
			Anchor = UiAnchor.Center
		} );
		var form = new ShareChoices( screen, strings, DefaultShareTitle, x, window.Y + 640, width, null );
		bool Ready() => Session.IsLoggedIn && Session.Busy == 0 && list.Selected >= 0 && list.Selected < parks.Count && form.Request() != null;
		void Share()
		{
			if ( Ready() && form.Request() is { } request )
				hosting.ShareSavedPark( parks[list.Selected], request );
		}
		form.Title.Submitted = Share;
		var share = screen.Add( new UiButton { Id = "share", Text = () => strings.Extra( OpenTpwText.SharePark ), Clicked = Share,
			Bounds = new UiRect( x, window.Y + 1020, 560, 110 ), Anchor = UiAnchor.Center } );
		var login = screen.Add( new UiButton { Id = "login", Text = () => OnlineStrings.Ui( OnlineStrings.OnlineLogin, "Online login" ), Clicked = ShowLogin,
			Bounds = new UiRect( x + 600, window.Y + 1020, 560, 110 ), Anchor = UiAnchor.Center } );
		screen.Updating += _ =>
		{
			form.Update( parks.Count != 0 );
			share.Visible = parks.Count != 0;
			share.Enabled = Ready();
			login.Visible = !Session.IsLoggedIn;
		};
		AddStatus( screen, window );
		// Back gives up a share that waits for its load, with that load
		AddBack( screen, window, () => { hosting.CancelSavedParkShare(); stack.Pop(); } );
		screen.Focus( form.Title );
		Push( screen );
	}

	/// <summary>The lobby's Build Together hub's name (C2d-3).</summary>
	internal const string BuildTogetherLobbyScreenName = "buildTogetherLobby";

	/// <summary>The join screen's name (C2d-3).</summary>
	internal const string JoinSharedParkScreenName = "joinSharedPark";

	/// <summary>
	/// [EXT:online-coop-session] The lobby's Build Together hub (C2d-3): sharing one of the player's saved parks (<see cref="ShowShareSavedPark"/>), or joining a park another player
	/// shares, with its code (<see cref="ShowJoinSharedPark"/>) or from the open list (<see cref="ShowSharedParks"/>, C2d-3b).
	/// </summary>
	// [EXT:own-window-sizes] the window and the places of its controls are OpenTPW's: the original has no shared parks
	public void ShowBuildTogetherLobby()
	{
		var screen = new UiScreen( BuildTogetherLobbyScreenName );
		var window = UiDialogs.CenteredWindow( 1200, 1040 );
		UiDialogs.AddWindow( screen, window, "w_big", () => strings.Extra( OpenTpwText.BuildTogether ) );
		var x = window.X + 120;
		var width = window.Width - 240;
		screen.Add( new UiLabel { Id = "intro", Wrap = true, Font = fonts => fonts.Small, Text = () => strings.Extra( OpenTpwText.BuildTogetherLobbyIntro ),
			Bounds = new UiRect( x, window.Y + 150, width, 200 ), Anchor = UiAnchor.Center } );
		var share = screen.Add( new UiButton { Id = "shareSaved", Text = () => strings.Extra( OpenTpwText.ShareASavedPark ), Clicked = ShowShareSavedPark,
			Enabled = host.Hosting != null, Bounds = new UiRect( x, window.Y + 390, width, 110 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "joinWithCode", Text = () => strings.Extra( OpenTpwText.JoinWithCode ), Clicked = ShowJoinSharedPark,
			Enabled = host.Joining != null, Bounds = new UiRect( x, window.Y + 530, width, 110 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "findShared", Text = () => strings.Extra( OpenTpwText.FindSharedPark ), Clicked = ShowSharedParks,
			Enabled = host.Joining != null, Bounds = new UiRect( x, window.Y + 670, width, 110 ), Anchor = UiAnchor.Center } );
		AddStatus( screen, window );
		AddBack( screen, window );
		screen.Focus( share );
		Push( screen );
	}

	/// <summary>
	/// [EXT:online-coop-session] Joining a shared park with its code (C2d-3; docs/COOP.md, "Joining a shared park from the lobby"): the code the host gave (case, spaces and dashes do
	/// not matter), or a login. While it joins: the stage in words (joining, getting the host's park, building it) and a cancel; a refusal in words, with a way back to the code.
	/// Once the game took the join, its own end dialog says why a session ended; the park, once built, replaces the lobby. A join from the open list or an invitation (C2d-3b)
	/// gives its <paramref name="code"/>, which the field keeps for another try after a refusal.
	/// </summary>
	// [EXT:own-window-sizes] the window and the places of its controls are OpenTPW's: the original has no shared parks
	public void ShowJoinSharedPark() => ShowJoinSharedPark( null );

	/// <inheritdoc cref="ShowJoinSharedPark()"/>
	public void ShowJoinSharedPark( string? code )
	{
		if ( host.Joining is not { } joining )
			return;
		var screen = new UiScreen( JoinSharedParkScreenName );
		var window = UiDialogs.CenteredWindow( 1400, 1100 );
		UiDialogs.AddWindow( screen, window, "w_big", () => strings.Extra( OpenTpwText.JoinSharedParkTitle ) );
		var x = window.X + 120;
		var width = window.Width - 240;
		// what the screen says of a code it would not send
		var localMessage = "";
		CoopJoinAttempt? Attempt() => joining.Joining is { } attempt && (attempt.Stage != CoopJoinStage.Done || attempt.Outcome is not (CoopJoinOutcome.Cancelled or CoopJoinOutcome.None)
			&& !(attempt.Outcome == CoopJoinOutcome.Ended && (attempt.Taken || attempt.EndCause == CoopSessionEnd.Left))) ? attempt : null;
		bool Running() => Attempt() is { Stage: not CoopJoinStage.Done };
		bool Failed() => Attempt() is { Stage: CoopJoinStage.Done, Outcome: not CoopJoinOutcome.Joined };

		var intro = screen.Add( new UiLabel { Id = "intro", Wrap = true, Font = fonts => fonts.Small, Text = () => strings.Extra( OpenTpwText.JoinIntro ),
			Bounds = new UiRect( x, window.Y + 150, width, 160 ), Anchor = UiAnchor.Center } );
		var codeLabel = screen.Add( new UiLabel { Id = "codeLabel", Font = fonts => fonts.Label, Text = () => strings.Extra( OpenTpwText.JoinCodeLabel ),
			Bounds = new UiRect( x, window.Y + 340, 300, 90 ), Anchor = UiAnchor.Center } );
		// room for a code typed with spaces and dashes between its letters (the server's normalization drops them)
		var codeField = screen.Add( new UiTextField { Id = "code", MaximumLength = 32, Model = "f_text1",
			Bounds = new UiRect( x + 320, window.Y + 330, width - 320, 93 ), Anchor = UiAnchor.Center } );
		codeField.Changed = _ => localMessage = "";
		if ( code != null )
			codeField.Text = code;
		screen.Add( new UiLabel { Id = "message", Wrap = true, Font = fonts => fonts.Small, Color = UiColors.Value,
			Bounds = new UiRect( x, window.Y + 460, width, 120 ), Anchor = UiAnchor.Center,
			Text = () => Attempt() is { } attempt ? JoinMessage( attempt ) : localMessage.Length > 0 ? localMessage
				: CoopSession.JoinRefusalKind is var refusal and not CoopJoinRefusal.None ? strings.Extra( GameFlow.JoinNotAllowedText( refusal ) )
				: !Session.IsLoggedIn ? strings.Extra( OpenTpwText.LogInToJoin ) : "" } );
		void Join()
		{
			var code = codeField.Text.Trim();
			if ( code.Length == 0 || !Session.IsLoggedIn || Running() )
				return;
			if ( CoopProtocol.NormalizeCode( code ) == null )
			{
				localMessage = strings.Extra( OpenTpwText.JoinNotACode );
				return;
			}
			localMessage = "";
			joining.StartJoining( code );
		}
		codeField.Submitted = Join;
		var join = screen.Add( new UiButton { Id = "join", Text = () => strings.Extra( OpenTpwText.JoinButton ), Clicked = Join,
			Bounds = new UiRect( x, window.Y + 620, 560, 110 ), Anchor = UiAnchor.Center } );
		var login = screen.Add( new UiButton { Id = "login", Text = () => OnlineStrings.Ui( OnlineStrings.OnlineLogin, "Online login" ), Clicked = ShowLogin,
			Bounds = new UiRect( x + 600, window.Y + 620, 560, 110 ), Anchor = UiAnchor.Center } );
		var cancel = screen.Add( new UiButton { Id = "cancel", Text = () => strings.Extra( OpenTpwText.Cancel ), Clicked = joining.CancelJoining,
			Bounds = new UiRect( x, window.Y + 620, 560, 110 ), Anchor = UiAnchor.Center } );
		var again = screen.Add( new UiButton { Id = "again", Text = () => strings.Extra( OpenTpwText.Back ), Clicked = joining.DismissJoining,
			Bounds = new UiRect( x, window.Y + 620, 560, 110 ), Anchor = UiAnchor.Center } );
		// another game of the player's holds the link: taking it over is the player's choice (it ends a park that game shares), and joins with the same code
		bool InUseElsewhere() => Attempt() is { Stage: CoopJoinStage.Done, Outcome: CoopJoinOutcome.LinkStopped, Stop: CoopStop.InUseElsewhere or CoopStop.Replaced };
		var takeOver = screen.Add( new UiButton { Id = "takeOver", Text = () => strings.Extra( OpenTpwText.SharedParkTakeOver ),
			Clicked = () =>
			{
				if ( !InUseElsewhere() || joining.Joining is not { } stopped )
					return;
				var code = stopped.Code;
				joining.DismissJoining();
				joining.StartJoining( code, takeOver: true );
			},
			Bounds = new UiRect( x + 600, window.Y + 620, 560, 110 ), Anchor = UiAnchor.Center } );
		screen.Updating += _ =>
		{
			var running = Running();
			var failed = Failed();
			var starting = !running && !failed;
			intro.Visible = codeLabel.Visible = codeField.Visible = join.Visible = starting;
			join.Enabled = Session.IsLoggedIn && Session.Busy == 0 && codeField.Text.Trim().Length > 0 && CoopSession.JoinRefusalKind == CoopJoinRefusal.None;
			login.Visible = starting && !Session.IsLoggedIn;
			cancel.Visible = running && Attempt() is { Stage: not CoopJoinStage.Cancelling };
			again.Visible = failed;
			takeOver.Visible = InUseElsewhere();
		};
		AddStatus( screen, window );
		// Back gives up a join on its way (its seat, or its session, is left)
		AddBack( screen, window, () => { if ( Running() ) joining.CancelJoining(); stack.Pop(); } );
		screen.Focus( codeField );
		Push( screen );
	}

	/// <summary>[EXT:online-coop-session] What the join screen says of an attempt: its stage while it runs, its outcome once over (C2d-3).</summary>
	private string JoinMessage( CoopJoinAttempt attempt ) => attempt.Stage switch
	{
		CoopJoinStage.Opening or CoopJoinStage.Joining or CoopJoinStage.Cancelling or CoopJoinStage.Answered => strings.Extra( OpenTpwText.JoiningThePark ),
		CoopJoinStage.Building => strings.Extra( attempt.Session is { IsBuilding: true } ? OpenTpwText.JoinBuildingPark : OpenTpwText.JoinGettingPark ),
		CoopJoinStage.Done => attempt.Outcome switch
		{
			CoopJoinOutcome.Refused => strings.Extra( GameFlow.JoinRefusalText( attempt.Refusal ) ),
			CoopJoinOutcome.LinkStopped when attempt.Stop is CoopStop.InUseElsewhere or CoopStop.Replaced
				=> strings.Extra( OpenTpwText.SharedParkLinkInUse ) + " " + strings.Extra( OpenTpwText.JoinTakeOverWarning ),
			CoopJoinOutcome.LinkStopped => strings.Extra( LinkStopText( attempt.Stop ) ),
			CoopJoinOutcome.NotACode => strings.Extra( OpenTpwText.JoinNotACode ),
			CoopJoinOutcome.NotAllowed => strings.Extra( GameFlow.JoinNotAllowedText( attempt.NotAllowedBecause ) ),
			CoopJoinOutcome.InSession => strings.Extra( OpenTpwText.JoinInSession ),
			CoopJoinOutcome.Ended when GameFlow.JoinEndText( attempt.EndCause, attempt.ServerReason ) is { } words => strings.Extra( words ),
			CoopJoinOutcome.Joined or CoopJoinOutcome.Cancelled or CoopJoinOutcome.Ended => "",
			_ => strings.Extra( OpenTpwText.JoinRefused ),
		},
		_ => "",
	};

	/// <summary>The open list's name (C2d-3b).</summary>
	internal const string SharedParksScreenName = "sharedParks";

	/// <summary>
	/// [EXT:online-coop-session] The open list of shared parks (C2d-3b; docs/COOP.md, "Invitations and the open list"): the parks of this build their hosts list for everyone, with
	/// their hosts and players, and a Refresh. Join takes the chosen park's code to the join screen's attempt; a park that ended or filled since the list came is refused there,
	/// in words. The rows belong to the login that listed them (another login clears them and lists again), and only the latest request's answer is shown.
	/// </summary>
	// [EXT:own-window-sizes] the window and the places of its controls are OpenTPW's: the original has no shared parks
	public void ShowSharedParks()
	{
		if ( host.Joining is not { } joining )
			return;
		var screen = new UiScreen( SharedParksScreenName );
		var window = UiDialogs.CenteredWindow( 1400, 1300 );
		UiDialogs.AddWindow( screen, window, "w_big", () => strings.Extra( OpenTpwText.SharedParksTitle ) );
		var x = window.X + 120;
		var width = window.Width - 240;
		IReadOnlyList<CoopListedSession> rows = Array.Empty<CoopListedSession>();
		// the login the latest request is for (the rows are its), and that request's number
		(Uri Server, string Account)? listedFor = null;
		var request = 0;
		var looking = false;
		var failed = false;
		void List( (Uri Server, string Account) login )
		{
			var mine = ++request;
			listedFor = login;
			looking = true;
			failed = false;
			Session.Coop.List( ( sessions, error ) =>
			{
				// a later request, or another login, owns the rows now
				if ( mine != request || Session.LoginIdentity != login )
					return;
				looking = false;
				failed = sessions == null;
				rows = sessions ?? Array.Empty<CoopListedSession>();
				if ( error != null )
					Log.Warning( $"The shared parks could not be listed: {error}" );
			} );
		}
		CoopListedSession? Chosen( UiScrollList list ) => list.Selected >= 0 && list.Selected < rows.Count ? rows[list.Selected] : null;
		static bool Full( CoopListedSession park ) => park.Players >= park.MaximumPlayers;

		screen.Add( new UiLabel { Id = "intro", Wrap = true, Font = fonts => fonts.Small, Text = () => strings.Extra( OpenTpwText.SharedParksIntro ),
			Bounds = new UiRect( x, window.Y + 150, width, 120 ), Anchor = UiAnchor.Center } );
		var list = screen.Add( new UiScrollList
		{
			Id = "parks",
			Rows = () => rows.Select( park => $"{park.Title} ({park.Host})" ).ToArray(),
			Trailing = index => index < rows.Count ? $"{rows[index].Players}/{rows[index].MaximumPlayers}" : "",
			Bounds = new UiRect( x, window.Y + 290, width, 440 ),
			Anchor = UiAnchor.Center
		} );
		screen.Add( new UiLabel { Id = "message", Wrap = true, Font = fonts => fonts.Small, Color = UiColors.Value,
			Bounds = new UiRect( x, window.Y + 750, width, 120 ), Anchor = UiAnchor.Center,
			Text = () => CoopSession.JoinRefusalKind is var refusal and not CoopJoinRefusal.None ? strings.Extra( GameFlow.JoinNotAllowedText( refusal ) )
				: !Session.IsLoggedIn ? strings.Extra( OpenTpwText.LogInToJoin )
				: looking ? strings.Extra( OpenTpwText.SharedParksLooking )
				: failed ? strings.Extra( OpenTpwText.SharedParksFailed )
				: rows.Count == 0 ? strings.Extra( OpenTpwText.SharedParksNone )
				: Chosen( list ) is { } park && Full( park ) ? strings.Extra( OpenTpwText.JoinFull ) : "" } );
		bool CanJoin() => Session.IsLoggedIn && Session.Busy == 0 && CoopSession.JoinRefusalKind == CoopJoinRefusal.None && Chosen( list ) is { } park && !Full( park )
			&& joining.Joining is not { Stage: not CoopJoinStage.Done };
		void Join()
		{
			if ( CanJoin() && Chosen( list ) is { } park && joining.StartJoining( park.Code ) )
				ShowJoinSharedPark( park.Code );
		}
		list.RowActivated = _ => Join();
		var join = screen.Add( new UiButton { Id = "join", Text = () => strings.Extra( OpenTpwText.JoinButton ), Clicked = Join,
			Bounds = new UiRect( x, window.Y + 900, 360, 110 ), Anchor = UiAnchor.Center } );
		var refresh = screen.Add( new UiButton { Id = "refresh", Text = () => OnlineStrings.Get( OnlineLabel.Refresh ),
			Clicked = () => { if ( Session.LoginIdentity is { } login ) List( login ); },
			Bounds = new UiRect( x + 400, window.Y + 900, 360, 110 ), Anchor = UiAnchor.Center } );
		var login = screen.Add( new UiButton { Id = "login", Text = () => OnlineStrings.Ui( OnlineStrings.OnlineLogin, "Online login" ), Clicked = ShowLogin,
			Bounds = new UiRect( x + 800, window.Y + 900, 360, 110 ), Anchor = UiAnchor.Center } );
		screen.Updating += _ =>
		{
			// another login (or none) has none of these rows: they go, and the new login's list comes
			var current = Session.LoginIdentity;
			if ( current != listedFor )
			{
				request++;
				rows = Array.Empty<CoopListedSession>();
				looking = false;
				failed = false;
				listedFor = null;
				if ( current is { } now )
					List( now );
			}
			if ( list.Selected >= rows.Count )
				list.Selected = rows.Count - 1;
			if ( list.Selected < 0 && rows.Count > 0 )
				list.Selected = 0;
			list.Visible = list.Enabled = rows.Count > 0;
			join.Enabled = CanJoin();
			refresh.Enabled = current != null && !looking;
			login.Visible = current == null;
		};
		AddStatus( screen, window );
		AddBack( screen, window );
		screen.Focus( list );
		Push( screen );
	}

	/// <summary>
	/// [EXT:online-coop-session] A host's invitation over the lobby (C2d-3b; the game keeps it, <see cref="GameFlow"/>'s lobby step): who invites the player to which shared park,
	/// with Join and Not now (also the screen's Back). The protocol has no answer to an invitation: Not now only puts it aside.
	/// </summary>
	internal UiScreen ShowInvitation( Func<CoopInvited?> invitation, Action join, Action notNow )
	{
		var screen = UiDialogs.Message( GameFlow.InvitationScreenName,
			() => invitation() is { } shown ? string.Format( strings.Extra( OpenTpwText.CoopInvitation ), shown.Host, shown.Title ) : "",
			(() => strings.Extra( OpenTpwText.JoinButton ), join), (() => strings.Extra( OpenTpwText.InvitationNotNow ), notNow) );
		Push( screen );
		return screen;
	}

	/// <summary>[EXT:online-coop-session] Why the link to shared parks stopped, in the player's language.</summary>
	private static OpenTpwText LinkStopText( CoopStop stop ) => stop switch
	{
		CoopStop.InUseElsewhere or CoopStop.Replaced => OpenTpwText.SharedParkLinkInUse,
		CoopStop.ServerTooOld => OpenTpwText.SharedParkServerTooOld,
		_ => OpenTpwText.SharedParkLinkLost,
	};

	/// <summary>[EXT:online-coop-session] What the Build Together screen says of an attempt: its stage while it runs, its outcome once over.</summary>
	private string ShareMessage( CoopShare share ) => share.Stage switch
	{
		CoopShareStage.Preparing or CoopShareStage.Creating or CoopShareStage.Cancelling => strings.Extra( OpenTpwText.SharingThePark ),
		CoopShareStage.Done => share.Outcome switch
		{
			CoopShareOutcome.Refused => RefusalText( share.Refusal ),
			CoopShareOutcome.LinkStopped => strings.Extra( LinkStopText( share.Stop ) ),
			CoopShareOutcome.CreateRefused => strings.Extra( share.CreateRefusal switch
			{
				Online.Coop.CoopReason.ServerFull => OpenTpwText.SharedParkServerFull,
				Online.Coop.CoopReason.Rate => OpenTpwText.SharedParkTryLater,
				Online.Coop.CoopReason.TitleRefused => OpenTpwText.SharedParkTitleRefused,
				Online.Coop.CoopReason.Unknown => OpenTpwText.SharedParkLinkLost,
				_ => OpenTpwText.SharedParkFailed,
			} ),
			CoopShareOutcome.Ended => strings.Extra( OpenTpwText.SharedParkEnded ),
			_ => strings.Extra( OpenTpwText.SharedParkFailed ),
		},
		_ => "",
	};

	/// <summary>[EXT:online-coop-session] Why the open park cannot be shared, in the player's language.</summary>
	private string RefusalText( CoopShareRefusal refusal ) => strings.Extra( refusal switch
	{
		CoopShareRefusal.CheatPending => OpenTpwText.ShareRefusedCheat,
		CoopShareRefusal.DeveloperOptions => OpenTpwText.ShareRefusedDeveloperOptions,
		_ => OpenTpwText.ShareRefused,
	} );

	/// <summary>[EXT:online-coop-session] A member's line: the name, marked as the host or as away.</summary>
	private string MemberText( CoopMemberInfo member, CoopSessionState state ) =>
		member.Id == state.Host ? string.Format( strings.Extra( OpenTpwText.SharedParkHost ), member.Name )
		: !member.Connected ? string.Format( strings.Extra( OpenTpwText.SharedParkAway ), member.Name )
		: member.Name;

	/// <summary>
	/// [EXT:online-coop-session] What the host heard of an invitation, named for its player when known: it went out, its player is not online for shared parks (give
	/// them the code), too many invitations, or another refusal.
	/// </summary>
	private string InviteText( CoopInviteNotice notice ) => (notice.Refusal?.Reason, notice.Player) switch
	{
		(null, { } player) => string.Format( strings.Extra( OpenTpwText.InviteSent ), player ),
		(CoopReason.NotOnline, { } player) => string.Format( strings.Extra( OpenTpwText.InviteNotOnline ), player ),
		(CoopReason.Rate, { } player) => string.Format( strings.Extra( OpenTpwText.InviteTooMany ), player ),
		(CoopReason.Rate, null) => strings.Extra( OpenTpwText.SharedParkTryLater ),
		_ => strings.Extra( OpenTpwText.InviteRefused ),
	};

	// ---- News (UI-MAP "Online news panel") ------------------------------------------------------

	/// <summary>
	/// The online news panel in its original composition (Mac UI table data:0x50738): a window (186,30)-(2018,1007)
	/// titled "News from ThemeParkWorld.com" (0x5b84, UITEXT 266), the news body (0x5b7f) at (375,197)-(1733,939)
	/// with its scroll bar (0x5b80) at the right edge, and the two column tabs (0x5b82 Game News 0x5b83 System News)
	/// at (1024,224)-(1348,294) and (1372,224)-(1695,294) inside the body's top right corner.
	/// </summary>
	public void ShowNews()
	{
		var screen = new UiScreen( "onlineNews" );
		var window = new UiRect( 186, 30, 1832, 977 );
		screen.Add( new UiModelImage { Id = "window", Model = "w_big", Bounds = window, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "title", Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center,
			Bounds = new UiRect( 822, 98, 473, 79 ), Anchor = UiAnchor.Center,
			// [DATA:Language/English/UITEXT.str:266] the original news panel's heading
			Text = () => OnlineStrings.Ui( OnlineStrings.NewsFromThemeParkWorld, "News from ThemeParkWorld.com" ) } );
		var showSystem = false;
		screen.Add( new UiButton { Id = "gameNews", Text = () => OnlineStrings.Ui( OnlineStrings.GameNews, "Game News" ), Clicked = () => showSystem = false,
			Bounds = new UiRect( 1024, 224, 324, 70 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "systemNews", Text = () => OnlineStrings.Ui( OnlineStrings.SystemNews, "System News" ), Clicked = () => showSystem = true,
			Bounds = new UiRect( 1372, 224, 323, 70 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel
		{
			Id = "text",
			Wrap = true,
			Font = fonts => fonts.Small,
			Bounds = new UiRect( 375, 197, 1358, 742 ),
			Anchor = UiAnchor.Center,
			Text = () => Session.News is { } news ? (showSystem ? news.System : news.Game) : ""
		} );
		AddCloseButton( screen, 1889, 820 );
		AddStatus( screen, window );
		screen.Back = stack.Pop;
		Push( screen );
		Guard( Session.FetchNews );
	}

	// ---- Delete account (OpenTPW) --------------------------------------------------------------

	public void ShowDeleteAccount()
	{
		var screen = new UiScreen( "onlineDeleteAccount" );
		var window = UiDialogs.CenteredWindow( 1150, 1060 );
		UiDialogs.AddWindow( screen, window, "w_med", () => OnlineStrings.Get( OnlineLabel.DeleteAccount ) );
		var x = window.X + 120;
		var width = window.Width - 340;
		screen.Add( new UiLabel { Id = "warning", Wrap = true, Font = fonts => fonts.Small, Text = () => OnlineStrings.Get( OnlineLabel.DeleteAccountWarning ),
			Bounds = new UiRect( x, window.Y + 150, width, 190 ), Anchor = UiAnchor.Center } );
		var name = AddField( screen, "name", () => OnlineStrings.Ui( OnlineStrings.LoginName, "Login name" ), x, window.Y + 360, width, 16 );
		var password = AddField( screen, "password", () => OnlineStrings.Ui( OnlineStrings.Password, "Password" ), x, window.Y + 530, width, 16 );
		password.Password = true;
		void Delete()
		{
			Guard( () => Session.DeleteAccount( name.Text.Trim(), password.Text ) );
			password.Text = "";
		}
		name.Submitted = () => screen.Focus( password );
		password.Submitted = Delete;
		var delete = screen.Add( new UiButton { Id = "delete", Text = () => OnlineStrings.Get( OnlineLabel.DeleteAccount ), Clicked = Delete,
			Bounds = new UiRect( x, window.Y + 740, 560, 110 ), Anchor = UiAnchor.Center } );
		// Closes once the account is gone; the Online World screen shows the outcome.
		screen.Updating += _ =>
		{
			delete.Enabled = Session.IsLoggedIn && Session.Busy == 0;
			if ( !Session.IsLoggedIn && Session.Busy == 0 && stack.Top == screen )
				stack.Pop();
		};
		AddStatus( screen, window );
		AddBack( screen, window );
		screen.Focus( name );
		Push( screen, passOutcomeDown: true );
	}

	// ---- Login (UI-MAP "Online login dialog") ------------------------------------------------

	// OpenTPW's own panel under the original login window: its own frame (w_med), with the server address, the authenticator code, register and recovery inside it.
	// [EXT:own-window-sizes] the panel's size and the places of its controls are OpenTPW's; the original window (table 0x7585a8) has no room for them
	internal static readonly UiRect LoginPanelBounds = new( 500, 868, 1016, 500 );
	internal static readonly UiRect LoginServerLabelBounds = new( 560, 920, 790, 60 );
	internal static readonly UiRect LoginServerBounds = new( 560, 984, 790, 84 );
	internal static readonly UiRect LoginCodeLabelBounds = new( 560, 1100, 400, 60 );
	internal static readonly UiRect LoginCodeBounds = new( 560, 1164, 400, 84 );
	internal static readonly UiRect LoginRegisterBounds = new( 990, 1110, 360, 100 );
	internal static readonly UiRect LoginForgotBounds = new( 990, 1230, 360, 100 );
	/// <summary>The status line: in OpenTPW's panel under the code field (the original window's green area ends at about y=736, its frame at about y=768: no room for a line).</summary>
	internal static readonly UiRect LoginStatusBounds = new( 560, 1256, 400, 74 );

	/// <summary>
	/// The login dialog from tp.exe's own table (0x7585a8, built by 0x4bb070, handler 0x4bb030, UITEXT 0xec-0xee, 16-character fields; <see cref="OriginalLayoutTables.LoginWindow"/>, the twin of the dial-up details dialog 0x7583e8): the wdialogw window (500,233)-(1516,830) with the
	/// Login title (0x4d) at (606,304)-(1333,384), the 16-character name field (0x49) and password field (0x4b) with their labels (0x4a/0x4c) inside the fields, the check
	/// button (-1, b_okay) at (1376,524)-(1460,608) and the cross (-2, b_exit) at (1387,613)-(1470,696). Enter moves from the name to the password and submits from there
	/// (0x10184924). OpenTPW's own additions (server address, authenticator code, register, recovery) sit in a panel of their own under the original window ([EXT:ONLINE-UI]).
	/// </summary>
	public void ShowLogin()
	{
		var table = OriginalLayoutTables.LoginWindow;
		var screen = new UiScreen( "onlineLogin" );
		var window = table.Root.Rect;
		screen.Add( new UiModelImage { Id = "window", Model = table.Root.Model!, Bounds = window, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "title", Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center,
			Bounds = table.Control( 0x4D ).Rect, Anchor = UiAnchor.Center, Text = () => OnlineStrings.Ui( OnlineStrings.OnlineLogin, "Online login" ) } );
		// The original fields: 16 characters each, labels inside the field's frame.
		UiTextField Field( string id, uint control, bool secret )
		{
			var node = table.Control( control );
			return screen.Add( new UiTextField { Id = id, MaximumLength = 16, Model = node.Model, Password = secret, Bounds = node.Rect, Anchor = UiAnchor.Center,
				TextInset = node.Inner is { } inner ? inner.X - node.Rect.X : 0 } );
		}
		var name = Field( "name", 0x49, false );
		name.Text = Session.Settings.PlayerName ?? "";
		var password = Field( "password", 0x4B, true );
		screen.Add( new UiLabel { Id = "nameLabel", Text = () => OnlineStrings.Ui( OnlineStrings.LoginName, "Login name" ),
			Bounds = table.Control( 0x4A ).Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "passwordLabel", Text = () => OnlineStrings.Ui( OnlineStrings.Password, "Password" ),
			Bounds = table.Control( 0x4C ).Rect, Anchor = UiAnchor.Center } );
		// [EXT:ONLINE-UI] in a panel of their own under the original window: the server address (the original service address was built in),
		// the authenticator code of two-step login, register and recovery.
		screen.Add( new UiModelImage { Id = "ownWindow", Model = "w_med", Bounds = LoginPanelBounds, Anchor = UiAnchor.Center } );
		UiTextField OwnField( string id, Func<string> label, UiRect labelBounds, UiRect bounds, int maximumLength )
		{
			screen.Add( new UiLabel { Id = id + "Label", Text = label, Fit = true, Bounds = labelBounds, Anchor = UiAnchor.Center } );
			return screen.Add( new UiTextField { Id = id, MaximumLength = maximumLength, Model = "f_text1", Bounds = bounds, Anchor = UiAnchor.Center } );
		}
		var server = OwnField( "server", () => OnlineStrings.Get( OnlineLabel.ServerAddress ), LoginServerLabelBounds, LoginServerBounds, 256 );
		server.Text = Session.Settings.ServerUrl ?? OnlineSession.SuggestedServerUrl ?? "";
		// [EXT:online-two-step] accounts without two-step login leave the code empty.
		var code = OwnField( "code", () => OnlineStrings.Get( OnlineLabel.AuthenticatorCode ), LoginCodeLabelBounds, LoginCodeBounds, Totp.Digits );
		void Submit( bool register )
		{
			try
			{
				Session.UseServer( server.Text.Trim(), name.Text.Trim() );
				if ( register )
					Session.Register( name.Text.Trim(), password.Text );
				else
					Session.Login( name.Text.Trim(), password.Text, code.Text.Trim().Length == 0 ? null : code.Text.Trim() );
				localStatus = "";
			}
			// UseServer's refusals carry their reason in the game's language (OnlineSession.ParseServerUrl)
			catch ( Exception exception ) when ( exception is ArgumentException or InvalidOperationException or InvalidDataException )
			{
				localStatus = exception.Message;
			}
			// The password stays while the dialog is open, so a login that asks for a code only needs the code.
			code.Text = "";
		}
		server.Submitted = () => screen.Focus( name );
		name.Submitted = () => screen.Focus( password );
		password.Submitted = () => Submit( register: false );
		code.Submitted = () => Submit( register: false );
		// The check (-1) and the cross (-2) are bare round buttons: a text label does not fit them; the check's hover text is the login label.
		var tick = table.Model( "b_okay" );
		screen.Add( new UiButton { Id = "login", Model = tick.Model, Help = OnlineStrings.Get( OnlineLabel.LogIn ), Clicked = () => Submit( register: false ),
			Bounds = tick.Rect, Anchor = UiAnchor.Center } );
		var cross = table.Model( "b_exit" );
		screen.Add( new UiButton { Id = "close", Model = cross.Model, Help = strings.Help( 2 ), Clicked = stack.Pop, Bounds = cross.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "register", Text = () => OnlineStrings.Get( OnlineLabel.Register ), Clicked = () => Submit( register: true ),
			Bounds = LoginRegisterBounds, Anchor = UiAnchor.Center } );
		// [EXT:online-email] recovery by e-mail; the server answers the same whether the account exists
		screen.Add( new UiButton { Id = "forgot", Text = () => OnlineStrings.Get( OnlineLabel.ForgotPassword ),
			Clicked = () => Guard( () => { Session.UseServer( server.Text.Trim(), name.Text.Trim() ); ShowRecovery( name.Text.Trim() ); } ),
			Bounds = LoginForgotBounds, Anchor = UiAnchor.Center } );
		// Closes itself once the login succeeded; opens the two-step setup when the server sends one; moves to the code
		// field when the server asks for a code.
		var codeRequested = false;
		screen.Updating += _ =>
		{
			if ( stack.Top != screen )
				return;
			if ( Session.IsLoggedIn )
			{
				password.Text = "";
				stack.Pop();
			}
			else if ( Session.Enrollment != null )
				ShowEnrollment();
			else if ( Session.CodeRequested && !codeRequested )
				screen.Focus( code );
			codeRequested = Session.CodeRequested;
		};
		screen.Add( new UiLabel { Id = "status", Wrap = true, Font = fonts => fonts.Small, Color = UiColors.Value, Bounds = LoginStatusBounds, Anchor = UiAnchor.Center,
			Text = StatusText( screen ) } );
		screen.Back = stack.Pop;
		screen.Focus( name.Text.Length == 0 ? name : password );
		Push( screen, passOutcomeDown: true );
	}

	// ---- Two-step setup (OpenTPW) -------------------------------------------------------------

	/// <summary>
	/// [EXT:online-two-step] Shows the authenticator secret of <see cref="OnlineSession.Enrollment"/> as a QR code and as
	/// text, and confirms it with the first code; the account exists (or its reset ends) only after that.
	/// </summary>
	public void ShowEnrollment()
	{
		var enrollment = Session.Enrollment;
		if ( enrollment == null )
			return;
		var screen = new UiScreen( "twoStepSetup" );
		var window = UiDialogs.CenteredWindow( 1500, 1450 );
		UiDialogs.AddWindow( screen, window, "w_med", () => OnlineStrings.Get( OnlineLabel.TwoStepLogin ) );
		var x = window.X + 120;
		screen.Add( new UiLabel { Id = "instructions", Wrap = true, Font = fonts => fonts.Small, Text = () => OnlineStrings.Get( OnlineLabel.TwoStepInstructions ),
			Bounds = new UiRect( x, window.Y + 140, window.Width - 300, 170 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiQrCode { Id = "qr", Text = enrollment.Uri, Bounds = new UiRect( x, window.Y + 330, 600, 600 ), Anchor = UiAnchor.Center } );
		var right = x + 660;
		var rightWidth = window.Right - 180 - right;
		screen.Add( new UiLabel { Id = "keyLabel", Text = () => enrollment.Name, Bounds = new UiRect( right, window.Y + 340, rightWidth, 60 ), Anchor = UiAnchor.Center } );
		// The key in the label font and the plain text colour: it is typed over by hand when scanning is not possible.
		screen.Add( new UiLabel { Id = "key", Wrap = true, Text = () => Totp.Grouped( enrollment.Secret ),
			Bounds = new UiRect( right, window.Y + 420, rightWidth, 200 ), Anchor = UiAnchor.Center } );
		// The authenticator code under the QR code; on servers that require e-mail, the address and its code on the right.
		var code = AddField( screen, "code", () => OnlineStrings.Get( OnlineLabel.AuthenticatorCode ), x, window.Y + 960, 420, Totp.Digits );
		UiTextField? email = null, emailCode = null;
		if ( enrollment.EmailRequired == true )
		{
			// [EXT:online-email] the recovery address of the new account, confirmed with a code before the account exists
			email = AddField( screen, "email", () => OnlineStrings.Get( OnlineLabel.EmailAddress ), right, window.Y + 590, rightWidth, 254 );
			screen.Add( new UiButton { Id = "sendCode", Text = () => OnlineStrings.Get( OnlineLabel.SendCode ),
				Clicked = () => Guard( () => Session.SendEnrollmentEmail( email.Text.Trim() ) ),
				Bounds = new UiRect( right, window.Y + 750, 360, 90 ), Anchor = UiAnchor.Center } );
			emailCode = AddField( screen, "emailCode", () => OnlineStrings.Get( OnlineLabel.EmailCode ), right, window.Y + 845, 360, 8 );
			email.Submitted = () => Guard( () => Session.SendEnrollmentEmail( email.Text.Trim() ) );
			emailCode.Submitted = () => screen.Focus( code );
		}
		void Confirm()
		{
			Guard( () => Session.ConfirmEnrollment( code.Text.Trim(), emailCode?.Text.Trim() ) );
			code.Text = "";
		}
		code.Submitted = Confirm;
		screen.Add( new UiButton { Id = "confirm", Text = () => OnlineStrings.Get( OnlineLabel.Confirm ), Clicked = Confirm,
			Bounds = new UiRect( right, window.Y + 1000, 400, 110 ), Anchor = UiAnchor.Center } );
		void Cancel()
		{
			Session.CancelEnrollment();
			stack.Pop();
		}
		screen.Updating += _ =>
		{
			// Done (logged in) or given up by the server (expired setup): back to the login dialog, which closes itself on success.
			if ( stack.Top == screen && (Session.IsLoggedIn || Session.Enrollment == null) )
				stack.Pop();
		};
		AddStatus( screen, window );
		AddBack( screen, window, Cancel );
		screen.Back = Cancel;
		screen.Focus( (UiElement?)email ?? code );
		Push( screen, passOutcomeDown: true );
	}

	// ---- Account recovery (OpenTPW) -----------------------------------------------------------

	/// <summary>
	/// [EXT:online-email] Recovery by e-mail: a code sent to the account's address sets a new password, and on request sets up
	/// two-step login again. Then the player logs in as usual from the login dialog.
	/// </summary>
	public void ShowRecovery( string nameOrEmail = "" )
	{
		var screen = new UiScreen( "recovery" );
		var window = UiDialogs.CenteredWindow( 1150, 1300 );
		UiDialogs.AddWindow( screen, window, "w_med", () => OnlineStrings.Get( OnlineLabel.AccountRecovery ) );
		var x = window.X + 120;
		var width = window.Width - 340;
		var who = AddField( screen, "nameOrEmail", () => OnlineStrings.Get( OnlineLabel.NameOrEmail ), x, window.Y + 160, width, 254 );
		who.Text = nameOrEmail;
		screen.Add( new UiButton { Id = "sendCode", Text = () => OnlineStrings.Get( OnlineLabel.SendCode ),
			Clicked = () => Guard( () => Session.RequestRecovery( who.Text.Trim() ) ), Bounds = new UiRect( x, window.Y + 320, 400, 90 ), Anchor = UiAnchor.Center } );
		var code = AddField( screen, "code", () => OnlineStrings.Get( OnlineLabel.EmailCode ), x, window.Y + 440, 360, 8 );
		var password = AddField( screen, "password", () => OnlineStrings.Get( OnlineLabel.NewPassword ), x, window.Y + 610, width, 16 );
		password.Password = true;
		var resetTwoStep = false;
		screen.Add( new UiButton { Id = "twoStep", Text = () => OnlineStrings.Get( resetTwoStep ? OnlineLabel.ResetTwoStep : OnlineLabel.KeepTwoStep ),
			Clicked = () => resetTwoStep = !resetTwoStep, Bounds = new UiRect( x, window.Y + 790, width, 90 ), Anchor = UiAnchor.Center } );
		void Confirm() => Guard( () => Session.ConfirmRecovery( who.Text.Trim(), code.Text.Trim(), password.Text, resetTwoStep ) );
		who.Submitted = () => Guard( () => Session.RequestRecovery( who.Text.Trim() ) );
		code.Submitted = () => screen.Focus( password );
		password.Submitted = Confirm;
		screen.Add( new UiButton { Id = "confirm", Text = () => OnlineStrings.Get( OnlineLabel.Confirm ), Clicked = Confirm,
			Bounds = new UiRect( x, window.Y + 930, 400, 110 ), Anchor = UiAnchor.Center } );
		screen.Updating += _ =>
		{
			// Done: back to the login dialog, which shows the result in its status line.
			if ( stack.Top == screen && Session.PasswordReset )
			{
				Session.PasswordReset = false;
				password.Text = "";
				stack.Pop();
			}
		};
		AddStatus( screen, window );
		AddBack( screen, window );
		screen.Focus( who.Text.Length == 0 ? who : code );
		Push( screen, passOutcomeDown: true );
	}

	// ---- Find Parks with park details (UI-MAP "Find Parks", "Park details dialog") --------------

	/// <summary>
	/// Find Parks in its original composition (Mac UI table data:0x50f84): a window (108,30)-(1940,1007) titled
	/// "Find Parks" (0xe05c), the six-column results list (0xe05b) at (196,164)-(1735,800) with the header buttons
	/// Name/Theme/City/Position/Visits/Votes at their authored places, the player-name search field (0xe05a) at
	/// (380,839)-(1122,938), the Search button (0xe059) at (1179,810)-(1332,964) and the close button (-2) at
	/// (1808,820)-(1891,903). Selecting a row opens the park details dialog.
	/// </summary>
	public void ShowFindParks() => ShowFindParks( null );

	public void ShowFindParks( int? cityId )
	{
		host.Advise?.Invoke( RestoredAdvice.FindPlayerHelp );
		var screen = new UiScreen( "findParks" );
		var window = new UiRect( 108, 30, 1832, 977 );
		screen.Add( new UiModelImage { Id = "window", Model = "w_big", Bounds = window, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "title", Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center,
			Bounds = new UiRect( 758, 74, 472, 80 ), Anchor = UiAnchor.Center, Text = () => OnlineStrings.Ui( OnlineStrings.FindParks, "Find Parks" ) } );
		// The original search field (0xe05a) holds the player whose parks are listed and starts with the player's own name.
		var search = screen.Add( new UiTextField { Id = "search", MaximumLength = 64, Model = "f_text1", Bounds = new UiRect( 380, 839, 742, 99 ), Anchor = UiAnchor.Center } );
		search.Text = cityId == null ? Session.Settings.PlayerName ?? "" : "";
		void Refresh() => Guard( () => Session.RefreshParks( null, cityId == null ? "recent" : "votes", cityId,
			search.Text.Trim().Length == 0 ? null : search.Text.Trim() ) );
		var searchButton = screen.Add( new UiButton { Id = "searchButton", Text = () => OnlineStrings.Ui( OnlineStrings.SearchName, "Search name" ), Clicked = Refresh,
			Bounds = new UiRect( 1179, 810, 153, 154 ), Anchor = UiAnchor.Center } );
		// [BIN:TPW-EXE:0x4bfc04 search Enter] Enter in the search field (0x802) makes Find Parks' procedure 0x4bf9f0 post 0x100 with the search button's id 0xe059 to its own window (0x4bfc29) unless that button is held down (+0x138), so the hook clicks 0x1F by the window's style 1 and the search runs, as a click on the button does
		search.Submitted = searchButton.Activate;
		// [DATA:Mac UI table data:0x50f84] the six column headers (ids 16-21) at their authored x extents
		var columns = new (float Offset, int Text)[]
		{
			(225 - 196, OnlineStrings.Name), (565 - 196, OnlineStrings.ThemeLabel), (881 - 196, OnlineStrings.City),
			(1175 - 196, OnlineStrings.Position), (1377 - 196, OnlineStrings.Visits), (1509 - 196, OnlineStrings.Votes),
		};
		var list = screen.Add( new UiScrollList
		{
			Id = "parks",
			Model = "list_findprks",
			Bounds = new UiRect( 196, 164, 1539, 636 ),
			Anchor = UiAnchor.Center,
			RowHeight = 84,
			// [BIN:TPW-EXE:0x4bfb5a Find Parks row] notification 0x400 opens Park Details (0x4c0ae0), without message 0x100 or a dedicated row sound.
			ClicksOnActivate = false,
			Columns = columns.Select( column => ((float Offset, Func<string> Text))(column.Offset, () => OnlineStrings.Ui( column.Text, "" )) ).ToArray(),
			Rows = () => Session.Parks.Count == 0
				? new[] { OnlineStrings.Ui( OnlineStrings.NoSearchResults, "No search results" ) }
				: Session.Parks.Select( ( park, index ) => $"{park.Name}\t{OnlineStrings.Theme( park.Level )}\t{CityName( park.CityId )}\t{index + 1}\t{park.Visits}\t{park.Votes}" ).ToArray(),
		} );
		ParkSummary? Selected() => list.Selected >= 0 && list.Selected < Session.Parks.Count ? Session.Parks[list.Selected] : null;
		// Selecting a result opens the park details dialog (notify 0x400 → 0x1018b474).
		list.RowActivated = index => { if ( Selected() is { } park ) ShowParkDetails( park, index ); };
		list.SelectionChanged = _ => { if ( Selected() is { } park ) ShowParkDetails( park, list.Selected ); };
		// [EXT:ONLINE-070] the original's Platinum Tickets, won when others visit the player's published parks
		// (manual p. 62): the server awards one per recorded visit, whatever list is open here.
		screen.Add( new UiLabel { Id = "platinum", Font = fonts => fonts.Label, Color = UiColors.Value, Align = UiAlign.Right,
			Bounds = new UiRect( 1300, 74, 560, 70 ), Anchor = UiAnchor.Center,
			Text = () => Session.Profile is { } profile ? $"{OnlineStrings.Get( OnlineLabel.PlatinumTickets )}: " +
				profile.PlatinumTickets.ToString( System.Globalization.CultureInfo.InvariantCulture ) : "" } );
		AddCloseButton( screen, 1808, 820 );
		AddStatus( screen, window );
		screen.Back = stack.Pop;
		if ( cityId != null )
			Refresh();
		else if ( Session.IsLoggedIn )
			Guard( () => Session.RefreshParks( null ) );
		if ( Session.IsLoggedIn && Session.Profile == null )
			Guard( Session.FetchProfile );
		screen.Focus( search );
		Push( screen );
	}

	/// <summary>The city's name in the Find Parks list; the server's cities hold it.</summary>
	private string CityName( int cityId ) => Session.Cities.FirstOrDefault( city => city.Id == cityId )?.Name ?? "";

	/// <summary>
	/// Park details in its original composition (Mac UI table data:0x51150): a window (248,30)-(1800,1007) with the
	/// park's name (0xe5931) at (512,75)-(1536,155), eight label rows (0xe5937 created by, 0xe5934 date published,
	/// 0xe5939 theme, 0xe5936 email, 0xe593a chart position, 0xe5938 votes, 0xe5933 visits, 0xe5935 the last row) in
	/// the left column at x 380-861, the description (0xe5930) at (922,207)-(1602,655), the enter-park button
	/// (0xe592e) at (1481,800)-(1635,953), the owner action (0xe592f) at (1322,800)-(1476,953) and the close button
	/// (-2) at (1671,820)-(1754,903).
	/// </summary>
	public void ShowParkDetails( ParkSummary park, int chartPosition )
	{
		host.Advise?.Invoke( RestoredAdvice.ParkDetailsHelp );
		var screen = new UiScreen( "parkDetails" );
		// One details dialog at a time: selecting another row replaces it.
		stack.RemoveWhere( other => other.Name == "parkDetails" );
		var window = new UiRect( 248, 30, 1552, 977 );
		screen.Add( new UiModelImage { Id = "window", Model = "w_big", Bounds = window, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "parkName", Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center,
			Bounds = new UiRect( 512, 75, 1024, 80 ), Anchor = UiAnchor.Center, Text = () => park.Name } );
		// The eight original label rows, top to bottom (data:0x51150); each shows its original UITEXT label.
		var rows = new (float Y, Func<string> Label, Func<string> Value)[]
		{
			(233, () => OnlineStrings.Ui( OnlineStrings.CreatedBy, "Created by" ), () => park.Author),
			(283, () => OnlineStrings.Ui( OnlineStrings.DatePublished, "Date published" ), () => park.PublishedUtc.LocalDateTime.ToShortDateString()),
			(333, () => OnlineStrings.Ui( OnlineStrings.ThemeLabel, "Theme" ), () => OnlineStrings.Theme( park.Level )),
			(382, () => OnlineStrings.Ui( OnlineStrings.Email, "Email" ),
				// The original fell back to "None supplied" (UITEXT 283); OpenTPW never publishes players' addresses.
				() => OnlineStrings.Ui( OnlineStrings.NoneSupplied, "None supplied" )),
			(432, () => OnlineStrings.Ui( OnlineStrings.ChartPosition, "Chart position" ),
				() => chartPosition.ToString( System.Globalization.CultureInfo.InvariantCulture )),
			(482, () => OnlineStrings.Ui( OnlineStrings.NumberOfVotes, "Number of Votes" ), () => park.Votes.ToString( System.Globalization.CultureInfo.InvariantCulture )),
			(532, () => OnlineStrings.Ui( OnlineStrings.NumberOfVisits, "Number of visits" ), () => park.Visits.ToString( System.Globalization.CultureInfo.InvariantCulture )),
			// [EXT:online-park-details-players] OpenTPW's last row shows the players currently in the park's chat room
			// (UITEXT 273); the original's last row showed the Instant action mode flag (UITEXT 282), which the package
			// does not record
			(582, () => OnlineStrings.Ui( OnlineStrings.PlayersAtThePark, "Players at the park" ), () => park.PlayersAtPark.ToString( System.Globalization.CultureInfo.InvariantCulture )),
		};
		foreach ( var row in rows )
		{
			var current = row;
			screen.Add( new UiLabel { Id = $"row{(int)current.Y}", Text = current.Label, Font = fonts => fonts.Label,
				Bounds = new UiRect( 380, current.Y, 481, 45 ), Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = $"row{(int)current.Y}Value", Text = current.Value, Font = fonts => fonts.Label, Color = UiColors.Value,
				Bounds = new UiRect( 380, current.Y, 481, 45 ), Anchor = UiAnchor.Center, Align = UiAlign.Right } );
		}
		screen.Add( new UiLabel { Id = "description", Wrap = true, Font = fonts => fonts.Small,
			Bounds = new UiRect( 922, 207, 680, 448 ), Anchor = UiAnchor.Center, Text = () => park.Description } );
		var visit = screen.Add( new UiButton { Id = "visit", Text = () => OnlineStrings.Get( OnlineLabel.VisitReadOnly ),
			Clicked = () => Guard( () => Session.Download( park, path => Inspect( path, start: true, park.Id ) ) ),
			Font = fonts => fonts.Label, Bounds = new UiRect( 1481, 800, 154, 153 ), Anchor = UiAnchor.Center } );
		// The owner action button is enabled only for the player's own parks (0x1018b474); OpenTPW unpublishes.
		var unpublish = screen.Add( new UiButton { Id = "unpublish", Text = () => OnlineStrings.Get( OnlineLabel.Unpublish ),
			Clicked = () => Guard( () => { Session.Unpublish( park ); stack.Pop(); } ),
			Font = fonts => fonts.Label, Bounds = new UiRect( 1322, 800, 154, 153 ), Anchor = UiAnchor.Center } );
		var vote = screen.Add( new UiButton { Id = "vote", Model = "b_vote", Text = () => OnlineStrings.Get( OnlineLabel.Vote ),
			Clicked = () => Guard( () => Session.Vote( park ) ), Bounds = new UiRect( 922, 700, 230, 110 ), Anchor = UiAnchor.Center } );
		var chat = screen.Add( new UiButton { Id = "parkChat", Text = () => OnlineStrings.Get( OnlineLabel.Chat ),
			Clicked = () => Guard( () => { Session.JoinRoom( OpenTPW.Online.Chat.ChatProtocol.ParkRoom( park.Id ) ); ShowChat(); } ),
			Bounds = new UiRect( 1170, 700, 230, 110 ), Anchor = UiAnchor.Center } );
		AddCloseButton( screen, 1671, 820 );
		AddStatus( screen, window );
		screen.Updating += _ =>
		{
			visit.Enabled = Session.Busy == 0 && host.Visit != null;
			vote.Enabled = !park.VotedFor && Session.Busy == 0;
			// [EXT:online-coop-session] in a shared park the chat stays the session's
			chat.Enabled = Session.IsLoggedIn && !Session.InSharedPark;
			unpublish.Enabled = Session.IsLoggedIn && Session.Busy == 0
				&& string.Equals( park.Author, Session.Settings.PlayerName, StringComparison.OrdinalIgnoreCase );
		};
		screen.Back = stack.Pop;
		Push( screen );
	}

	/// <summary>
	/// "Other Parks In &lt;city&gt;" (UITEXT 446), the city lobby's list of the parks outside its top 10: the original
	/// chart window (Mac UI table data:0x5137e) with its five columns Name/Theme/Position/Visits/Votes.
	/// </summary>
	public void ShowOtherParks( CityInfo city )
	{
		host.Advise?.Invoke( RestoredAdvice.OtherParksHelp );
		var screen = new UiScreen( "otherParks" );
		var window = new UiRect( 248, 30, 1552, 977 );
		screen.Add( new UiModelImage { Id = "window", Model = "w_big", Bounds = window, Anchor = UiAnchor.Center } );
		// [DATA:Language/English/UITEXT.str:446] "Other Parks In " + the location name (0x1018c880)
		screen.Add( new UiLabel { Id = "title", Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center,
			Bounds = new UiRect( 767, 67, 472, 79 ), Anchor = UiAnchor.Center,
			Text = () => OnlineStrings.Ui( OnlineStrings.OtherParksIn, "Other Parks In " ) + city.Name } );
		var columns = new (float Offset, int Text)[]
		{
			(372 - 349, OnlineStrings.ListParksName), (707 - 349, OnlineStrings.ListParksTheme), (1022 - 349, OnlineStrings.ListParksPosition),
			(1225 - 349, OnlineStrings.ListParksVisits), (1360 - 349, OnlineStrings.ListParksVotes),
		};
		var list = screen.Add( new UiScrollList
		{
			Id = "parks",
			Model = "list_findprks",
			Bounds = new UiRect( 349, 192, 1235, 636 ),
			Anchor = UiAnchor.Center,
			RowHeight = 105,
			// [BIN:TPW-EXE:0x4c1aec Other Parks row] notification 0x400 opens Park Details (0x4c0ae0), without message 0x100 or a dedicated row sound.
			ClicksOnActivate = false,
			Columns = columns.Select( column => ((float Offset, Func<string> Text))(column.Offset, () => OnlineStrings.Ui( column.Text, "" )) ).ToArray(),
			Rows = () => (Session.CityParks ?? Array.Empty<ParkSummary>()).Count == 0
				? new[] { OnlineStrings.Ui( OnlineStrings.NoSearchResults, "No search results" ) }
				: (Session.CityParks ?? Array.Empty<ParkSummary>()).Select( ( park, index ) => $"{park.Name}\t{OnlineStrings.Theme( park.Level )}\t{index + 1}\t{park.Visits}\t{park.Votes}" ).ToArray(),
		} );
		ParkSummary? Selected()
		{
			var parks = Session.CityParks ?? Array.Empty<ParkSummary>();
			return list.Selected >= 0 && list.Selected < parks.Count ? parks[list.Selected] : null;
		}
		list.RowActivated = index => { if ( Selected() is { } park ) ShowParkDetails( park, index ); };
		list.SelectionChanged = _ => { if ( Selected() is { } park ) ShowParkDetails( park, list.Selected ); };
		AddCloseButton( screen, 1671, 820 );
		AddStatus( screen, window );
		screen.Back = stack.Pop;
		Push( screen );
	}

	// ---- Publish Park (UI-MAP "Publish Park dialog") -----------------------------------------

	/// <summary>
	/// Publish Park: first the globe to choose the city to publish in (the original's PublishGlobe, UITEXT 419), then the
	/// park's name and description. A server without cities goes straight to the form.
	/// </summary>
	public void ShowPublish()
	{
		var level = host.Level();
		if ( level == null )
			return;
		// [EXT:mods] a park that records mods cannot be published yet: say so before the globe, as the form's status column would
		if ( ParkSharing.ModsRefusal( level ) is { } refusal )
		{
			stack.Push( UiDialogs.Message( "publishRefused", () => refusal, (() => strings.Extra( OpenTpwText.Back ), stack.Pop) ) );
			return;
		}
		host.Advise?.Invoke( RestoredAdvice.PublishHelp );
		if ( Session.Client == null )
		{
			ShowPublishForm( null );
			return;
		}
		ShowPublishGlobe();
	}

	// [BIN:STP-PPC:CLBScreenComponentPublishGlobe] a click on a hovered city's pin stores that city (Mac DAT_101ED220) and opens the Publish Park dialog (0x1018CE80)
	// [BIN:TPW-EXE:0x5DD910 publish globe] its activation builds the worldlob table with argument 1 (0x4C4FB0), which hides the satellites
	// (90119), Find Parks (90115) and back to the lobby (90112), leaving zoom and the four arrows, and shows the city labels (0x4B9A70);
	// the Mac twin (CLBScreenComponentPublishGlobe vf7, 0x10092F30) passes 1 to its builder 0x101B9068 too
	// [EXT:online-publish-globe-prompt] UITEXT 419 ("Choose the city to publish in") shows in the name label while no city is
	// hovered; where the original showed UITEXT 419 is not known and not reproduced (neither tp.exe nor the Mac publish globe
	// code loads id 419 as an immediate)
	private void ShowPublishGlobe()
	{
		var screen = new OnlineGlobeScreen( host.Globe, "publishGlobe" );
		screen.CityClicked = city => { stack.Pop(); ShowPublishForm( city ); };
		var (_, zoom) = AddGlobePanel( screen, null, null );
		AddCityLabels( screen, () => OnlineStrings.Ui( OnlineStrings.ChooseCityToPublish, "Choose the city to publish in" ) );
		OnlineClient? citiesFor = null;
		screen.Updating += _ =>
		{
			zoom.Help = strings.Help( GlobeCameraMode.ZoomedIn ? 429 : 428 );
			if ( Session.Client is { } client && client != citiesFor && Session.Busy == 0 )
			{
				citiesFor = client;
				Guard( Session.FetchCities );
			}
			// A server without cities (older OpenTPW servers): publish without one.
			else if ( citiesFor != null && Session.Busy == 0 && Session.Cities.Count == 0 && stack.Top == screen )
			{
				stack.Pop();
				ShowPublishForm( null );
			}
		};
		screen.Back = stack.Pop;
		Push( screen );
	}

	// OpenTPW's own controls of the Publish Park form: the strip below the description field (ends y=892) inside the window's green area (ends about y=955),
	// and the free column left of the fields (the original form has no status line)
	internal static readonly UiRect PublishWebsiteBounds = new( 330, 898, 760, 50 );
	internal static readonly UiRect PublishExportBounds = new( 1100, 898, 540, 50 );
	internal static readonly UiRect PublishStatusBounds = new( 320, 279, 350, 613 );

	/// <summary>
	/// The Publish Park form (published into <paramref name="city"/>, none on servers without cities) from tp.exe's own table (0x7595b0, <see cref="OriginalLayoutTables.PublishWindow"/>): the window2 window with the title (0x11e5), the
	/// park-name field (0x11e4), the description field (0x11e3), the check button (-1, b_okay) and the close button (-2, b_exit).
	/// </summary>
	// [BIN:TPW-EXE:0x7595b0 publish window] root 0x11e2 window2 (248,30)-(1800,1007); b_exit -2 (1671,820)-(1754,903) and b_okay -1 (1660,731)-(1743,815), both with help -1;
	// description 0x11e3 !frame (684,444)-(1364,892) inner (707,463)-(1346,870); name 0x11e4 !frame (682,279)-(1366,372) inner (710,303)-(1340,348); title 0x11e5 (788,75)-(1260,155)
	// [BIN:TPW-EXE:0x4c4670 publish build] the name field takes a buffer of 0xf characters (0x6662b7, 0x7cd850) and starts with the localized theme name of the park's level
	// (0x4099b0 over the level-name global 0x786b68 through UITEXT); the description field takes 0x7e characters (0x7cd480) and starts with UITEXT 0xdc; the title is UITEXT 0xda
	// [BIN:TPW-EXE:0x4c4480 publish handler] the tick (-1) has no enabled test: it hands the two buffers to the publisher (0x48b390, 0x5b7ba0) and closes the window (message 4);
	// the cross (-2) and Escape (0x804, from the edit control's key handler 0x667fee) close it; Enter in the single-line name field (0x802, not sent by the multi-line description,
	// attribute 0x80) and Tab (0x803) move the focus from the name to the description, Tab from the description back to the name
	// [EXT:ONLINE-UI] the website toggle, the export button and the status column are OpenTPW's and sit inside the window where no original control is; the hover help of both
	// buttons is OpenTPW's too (the table gives help -1, none); a click on the tick while logged out opens the login dialog (the original had no offline publish)
	private void ShowPublishForm( CityInfo? city )
	{
		var level = host.Level();
		if ( level == null )
			return;
		localStatus = "";
		var table = OriginalLayoutTables.PublishWindow;
		var screen = new UiScreen( "publishPark" );
		screen.Add( new UiModelImage { Id = "window", Model = table.Root.Model!, Bounds = table.Root.Rect, Anchor = UiAnchor.Center } );
		var title = table.Control( 0x11E5 );
		screen.Add( new UiLabel { Id = "title", Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center,
			Bounds = title.Rect, Anchor = UiAnchor.Center,
			Text = () => { var text = OnlineStrings.Ui( OnlineStrings.PublishParkTitle, OnlineStrings.Ui( OnlineStrings.PublishPark, "Publish Park" ) ); return city == null ? text : $"{text}: {city.Name}"; } } );
		UiTextField Field( string id, uint control, int maximumLength, string text )
		{
			var node = table.Control( control );
			return screen.Add( new UiTextField { Id = id, MaximumLength = maximumLength, Model = node.Model, Text = text.Length > maximumLength ? text[..maximumLength] : text,
				TextInset = node.Inner is { } inner ? inner.X - node.Rect.X : 0, Bounds = node.Rect, Anchor = UiAnchor.Center } );
		}
		var name = Field( "parkName", 0x11E4, PublishNameLength, OnlineStrings.Theme( level.LevelName ) );
		var description = Field( "description", 0x11E3, PublishDescriptionLength, OnlineStrings.Ui( OnlineStrings.Description, "" ) );
		name.Submitted = () => screen.Focus( description );
		// [EXT:ONLINE-067] off unless the player chooses it: the website list makes the park and the player name public on the web.
		var showOnWebsite = false;
		screen.Add( new UiButton { Id = "website", Text = () => OnlineStrings.Get( showOnWebsite ? OnlineLabel.ShownOnWebsite : OnlineLabel.NotShownOnWebsite ),
			Clicked = () => showOnWebsite = !showOnWebsite, Bounds = PublishWebsiteBounds, Anchor = UiAnchor.Center } );
		void Close()
		{
			if ( !screen.IsRemoved && ReferenceEquals( stack.Top, screen ) ) stack.Pop();
		}
		// The tick (-1) and the cross (-2) are bare round buttons; the tick publishes and closes the form once the server has the park.
		var tick = table.Model( "b_okay" );
		var publish = screen.Add( new UiButton { Id = "publish", Model = tick.Model, Help = OnlineStrings.Ui( OnlineStrings.PublishPark, "Publish Park" ),
			Clicked = () =>
			{
				if ( Session.Busy > 0 )
					return;
				if ( !Session.IsLoggedIn )
				{
					ShowLogin();
					return;
				}
				Guard( () => Session.Publish( ParkSharing.ExportLevel( level, name.Text, Session.Settings.PlayerName ?? "", description.Text ), showOnWebsite, city?.Id ?? 0, Close ) );
			},
			Bounds = tick.Rect, Anchor = UiAnchor.Center } );
		var export = screen.Add( new UiButton { Id = "export", Text = () => OnlineStrings.Get( OnlineLabel.ExportPark ),
			Clicked = () => Guard( () =>
			{
				var package = ParkSharing.ExportLevel( level, name.Text, Session.Settings.PlayerName ?? "", description.Text );
				var path = Path.Combine( Session.Folders.Parks, package.Manifest.PackageId.ToString( "N" ) + ParkPackage.FileExtension );
				package.Save( path );
				localStatus = OnlineStrings.Get( OnlineLabel.SavedTo ) + path;
			} ),
			Bounds = PublishExportBounds, Anchor = UiAnchor.Center } );
		var cross = table.Model( "b_exit" );
		screen.Add( new UiButton { Id = "close", Model = cross.Model, Help = strings.Help( 2 ), Clicked = Close, Bounds = cross.Rect, Anchor = UiAnchor.Center } );
		// The status (and the reason a park cannot be shared) goes in the free column left of the fields.
		var formStatus = StatusText( screen );
		Func<string> publishStatus = () => ParkSharing.ModsRefusal( level ) ?? formStatus();
		screen.Add( new UiLabel { Id = "status", Wrap = true, Font = fonts => fonts.Small, Color = UiColors.Value, Bounds = PublishStatusBounds, Anchor = UiAnchor.Center,
			Text = publishStatus } );
		// [EXT:mods] a park that records mods cannot be exported or published yet (ParkSharing.ModsRefusal): both buttons are hidden and the status column says why
		screen.Updating += _ => publish.Visible = export.Visible = ParkSharing.ModsRefusal( level ) == null;
		screen.Back = Close;
		screen.Focus( name );
		Push( screen );
	}

	/// <summary>The park-name field's buffer (tp.exe 0x4c4719) and the description field's (0x4c47e1).</summary>
	internal const int PublishNameLength = 0xF, PublishDescriptionLength = 0x7E;

	// ---- Postcards (UI-MAP "Send Postcard dialog", "Unsent Postcards screen") ------------------

	/// <summary>
	/// Send Postcard in its original composition (tp.exe table 0x7540d8, built by 0x4a8c00): the window4 window with the title (UITEXT 208), the photo
	/// frame, the message, title and recipients fields and the preview, clear, address book, tick and X buttons.
	/// </summary>
	// [BIN:TPW-EXE:0x7540d8 postcard window] root 0x5d9d window4 (186,30)-(2018,1007); title 0x5d9e (809,66)-(1281,146); photo frame 0x5d9f !frame (336,144)-(1671,675);
	// message field 0x5da3 (341,691)-(970,952) help 319, title field 0x5da4 (981,689)-(1670,781) help 320, recipients field 0x5da5 (983,788)-(1672,953) help 321;
	// b_preview 0x5da2 help 322, b_clear 0x5da1 help 323, b_address 0x5da0 help 324, b_okay help 325 and b_exit help 2
	// [BIN:TPW-EXE:0x4a8c00 postcard build] the title gets UITEXT 0xd0; the recipients field holds at most 0x1ff characters and starts with UITEXT 0xd3, the title field 0x5f and
	// UITEXT 0xd1, the message field 0xff and UITEXT 0xd2, each in font slot 11 and white (tp.exe's UITEXT ids here are OpenTPW's + 1)
	// [BIN:TPW-EXE:0x4a7d70 postcard handler] an edit of the message or title field (message 0x801) sets its flag (0x7cbf40, 0x7cbf3c); b_clear empties the three fields
	// without clearing the flags; the tick (-1) takes UITEXT 0xd9 for an unedited message and 0xd8 for an unedited title (0x4a7ee1, 0x4a7f07), splits the recipients at
	// ';', ',', ' ' and newlines (0x754448) and, when there is none or one is refused, shows message box 0x47f020 with UITEXT 0xd6 and keeps the window; otherwise it sends
	// (0x4a8600: to the outbox while offline) and closes the window; b_exit closes it
	// [EXT:postcard-player-names] OpenTPW accepts player names and e-mail addresses together. Tokens with '@' use the shared address validator;
	// other tokens use the player-name rules (the original only accepted addresses, 0x4a7f6c).
	// [APPROX:UI-042] the photo frame stays empty and the preview and address book buttons are disabled: the 640x240 camcorder photo (0x4a9280), its preview redraw
	// (0x4a84c0) and the address book window (table 0x7542b8, 0x4a8140) are not ported
	public void ShowSendPostcard()
	{
		var screen = new UiScreen( "sendPostcard" );
		var table = OriginalLayoutTables.PostcardWindow;
		LayoutNode Control( uint id ) => table.Control( id );
		screen.Add( new UiModelImage { Id = "window", Model = table.Root.Model!, Bounds = table.Root.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "heading", Text = () => OnlineStrings.Ui( OnlineStrings.SendPostcard, "Send Postcard" ), Font = fonts => fonts.Title, Color = UiColors.Title,
			Align = UiAlign.Center, Fit = true, Bounds = Control( 0x5D9E ).Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiModelImage { Id = "photo", Model = Control( 0x5D9F ).Model!, Bounds = Control( 0x5D9F ).Rect, Anchor = UiAnchor.Center } );
		var edited = new HashSet<string>( StringComparer.Ordinal );
		// a field counts as edited once typed in (message 0x801), or when it holds text that was set without typing
		bool Edited( UiTextField field ) => edited.Contains( field.Id ) || field.Text.Length > 0;
		// [EXT:postcard-player-names] the prompts are shown while a field is empty instead of being its text, so typing does not append to them, and the title takes at
		// most the online card's 64 characters (Postcard.MaximumTitleLength) of the original's 95
		UiTextField Field( string id, uint control, int maximumLength, Func<string> prompt )
		{
			var node = Control( control );
			return screen.Add( new UiTextField { Id = id, MaximumLength = maximumLength, Placeholder = prompt(), Help = strings.Help( node.Help ), Model = node.Model,
				TextInset = node.Inner is { } inner ? inner.X - node.Rect.X : 0, Bounds = node.Rect, Anchor = UiAnchor.Center, Changed = _ => edited.Add( id ) } );
		}
		var message = Field( "message", 0x5DA3, 0xFF, () => OnlineStrings.Ui( OnlineStrings.MessageText, "Message text" ) );
		var title = Field( "title", 0x5DA4, Math.Min( 0x5F, Postcard.MaximumTitleLength ), () => OnlineStrings.Ui( OnlineStrings.MessageTitle, "Message title" ) );
		var to = Field( "to", 0x5DA5, 0x1FF, () => OnlineStrings.Ui( OnlineStrings.EmailAddresses, "Email address(es)" ) );
		// The original attached a photo of the park the card is sent from (manual p. 51/67). [EXT:online-postcard-photo]
		// OpenTPW attaches the park's generated map view: the camcorder snapshot (content and encoding not decoded) is not
		// reproduced, and no park reference is attached because the live level has no package identity.
		byte[]? ParkImage()
		{
			var level = host.Level();
			if ( level == null )
				return null;
			try
			{
				var payload = MinimalParkPayload.FromSnapshot( ParkSnapshotBuilder.FromLevel( level ) );
				return ParkSnapshotBuilder.RenderThumbnail( level.OriginalPark?.Map, payload, level.OriginalPark == null ? null : ( px, py ) =>
					OriginalParkPlacement.TryGetCell( level.OriginalPark.Heightfield, px, py, out var cx, out var cy ) ? (cx, cy) : null );
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException )
			{
				return null;
			}
		}
		void Close()
		{
			if ( !screen.IsRemoved && ReferenceEquals( stack.Top, screen ) ) stack.Pop();
		}
		void Send()
		{
			var recipients = PostcardRecipients( to.Text );
			try { PostcardRecipient.RequireValid( recipients ); }
			catch ( Exception exception ) when ( exception is InvalidDataException or ArgumentException )
			{
				stack.Push( UiDialogs.Message( "postcardRecipients", () => OnlineStrings.Ui( OnlineStrings.InvalidEmailAddresses, "Invalid email addresses in the send to field" ),
					(() => strings.Extra( OpenTpwText.Back ), stack.Pop) ) );
				return;
			}
			try
			{
				var card = Postcard.Create( Session.Settings.PlayerName ?? "", recipients,
					Edited( title ) ? title.Text : OnlineStrings.Ui( OnlineStrings.PostcardTitle, "Postcard from Theme Park World" ),
					Edited( message ) ? message.Text : OnlineStrings.Ui( OnlineStrings.PostcardGreeting, "Greetings from Theme Park World!" ),
					GameLanguage.IsSelected ? GameLanguage.Current.Name : GameLanguage.DefaultLanguage, image: ParkImage() );
				localStatus = OnlineStrings.Get( OnlineLabel.SavedTo ) + Session.Folders.Store( Session.Folders.Outbox, card );
				Close();
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or PlatformNotSupportedException )
			{
				// the window has no status line: a refused card or a failed store is shown in a message box and the window stays
				stack.Push( UiDialogs.Message( "postcardFailed", () => exception.Message, (() => strings.Extra( OpenTpwText.Back ), stack.Pop) ) );
			}
		}
		UiButton Button( string id, string model, Action? clicked, bool enabled = true )
		{
			var node = table.Model( model );
			return screen.Add( new UiButton { Id = id, Model = node.Model, Help = strings.Help( node.Help ), Clicked = clicked, Enabled = enabled, Bounds = node.Rect,
				Anchor = UiAnchor.Center } );
		}
		Button( "preview", "b_preview", null, enabled: false );
		Button( "clear", "b_clear", () => message.Text = title.Text = to.Text = "" );
		Button( "addressBook", "b_address", null, enabled: false );
		Button( "ok", "b_okay", Send );
		Button( "back", "b_exit", Close );
		screen.Back = Close;
		screen.Focus( to );
		Push( screen );
	}

	/// <summary>The recipients of a postcard: the field's text split at the original's separators but the space (';', ',' and newlines, tp.exe 0x754448).</summary>
	// [EXT:postcard-player-names] player names may hold spaces, so the space the original also splits its e-mail addresses at is not a separator
	public static string[] PostcardRecipients( string text ) =>
		text.Split( new[] { ';', ',', '\n' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );

	/// <summary>
	/// Unsent Postcards in tp.exe's outbox window (table 0x758878, built by 0x4bc710, handled by 0x4bbc20): the title (0xee9), the selected card's
	/// picture frame (0xee4) and its addresses (0xee2, list_emailaddr), the bulk buttons mark all for sending (0xee5, b_okall), mark all for deletion
	/// (0xee6, b_delall) and unmark all (0xee7, b_nuall), the four-column list (0xee8) with its Title/Date/Send/Delete headers (ids 16-19) and the
	/// tick (-1). Every card starts marked for sending; a click on a row's Send or Delete cell toggles its mark, and the tick deletes the cards marked
	/// for deletion, sends the ones marked for sending and closes the window. Escape closes it without either.
	/// </summary>
	// [BIN:TPW-EXE:0x4bcaec outbox rows] the builder adds each card's row with the send mark 0x1000000 or-ed into its index; 0x2000000 is the delete mark, and no row holds both
	// [BIN:TPW-EXE:0x4bbf2c outbox cells] the list's cell message 0x404 for column 2 clears a set send mark, or sets it and clears the delete mark; column 3 does the same with the delete mark
	// [BIN:TPW-EXE:0x4bbd2d outbox bulk] 0xee5 marks every row for sending, 0xee6 every row for deletion (0x4bbe7a) and 0xee7 clears both marks of every row (0x4bbddf)
	// [BIN:TPW-EXE:0x4bbcbd outbox tick] the tick first applies the marks (0x4bc430: a card marked for deletion is deleted, one marked for sending is flagged), then, with a card to send and the service refused by the account's settings (0x5b6460), posts message 0x4e (ERRORMSG 78, the childlock refusal) and keeps the window; otherwise it closes the window and sends (0x4bc500)
	// [EXT:ONLINE-UI] OpenTPW sends through its server, which needs a login: in the original's place of the account check the tick opens the login dialog over the window, and it waits while another online request runs
	// Not ported yet: the card's picture in its frame (the postcard photo, which the postcard window leaves out too) and the header buttons' sort (the list's attribute 0x10, 0x665a44; help texts 0x187-0x18a)
	public void ShowOutbox()
	{
		host.Advise?.Invoke( RestoredAdvice.OutboxHelp );
		var screen = new UiScreen( "outbox" );
		var table = OriginalLayoutTables.OutboxWindow;
		LayoutNode Control( uint id ) => table.Control( id );
		screen.Add( new UiModelImage { Id = "window", Model = table.Root.Model!, Bounds = table.Root.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "title", Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center,
			Bounds = Control( 0xEE9 ).Rect, Anchor = UiAnchor.Center, Text = () => OnlineStrings.Ui( OnlineStrings.UnsentPostcards, "Unsent Postcards" ) } );
		IReadOnlyList<(string Path, Postcard? Card, string? Error)> cards = Array.Empty<(string, Postcard?, string?)>();
		void Reload() => cards = Session.Folders.List( Session.Folders.Outbox );
		Reload();
		// Each card's mark by its file; a card without an entry has the send mark every row starts with. A file that is no readable card is never sent.
		var marks = new Dictionary<string, OutboxMark>( StringComparer.Ordinal );
		OutboxMark MarkOf( (string Path, Postcard? Card, string? Error) entry ) =>
			marks.TryGetValue( entry.Path, out var mark ) ? mark : entry.Card != null ? OutboxMark.Send : OutboxMark.None;
		void MarkAll( OutboxMark mark )
		{
			foreach ( var entry in cards )
				marks[entry.Path] = mark == OutboxMark.Send && entry.Card == null ? OutboxMark.None : mark;
		}
		var listNode = Control( 0xEE8 );
		var content = listNode.Inner!.Value;
		screen.Add( new UiModelImage { Id = "listFrame", Model = listNode.Model!, Bounds = listNode.Rect, Anchor = UiAnchor.Center } );
		// [BIN:TPW-EXE:0x4bc8ea outbox headers] the header buttons 16-19 show UITEXT 0x136-0x139 (OpenTPW's 309-312: Title, Date, Send, Delete) in font slot 8 with the help texts 0x187-0x18a,
		// through the attractions headers' helper 0x486660
		var headers = new (uint Id, int Text, string Fallback)[]
		{
			(0x10, OnlineStrings.Title, "Title"), (0x11, OnlineStrings.DateLabel, "Date"), (0x12, OnlineStrings.Send, "Send"), (0x13, OnlineStrings.Delete, "Delete"),
		};
		// The headers show their text; their sort and its help texts (UIHELPTEXT 391-394, "Left-click to sort the list by ...") wait for the sort's port.
		foreach ( var header in headers )
		{
			var text = header;
			screen.Add( new UiLabel { Id = $"header{text.Id - 0x10}", Font = fonts => fonts.Label, Color = UiColors.Text, Align = UiAlign.Center, Fit = true,
				Bounds = Control( text.Id ).Rect, Anchor = UiAnchor.Center, Text = () => OnlineStrings.Ui( text.Text, text.Fallback ) } );
		}
		// [BIN:TPW-EXE:0x4bcbc0 outbox row] the row factory makes each row as high as font slot 7's line height x 1536 / screen height + 6, the buy list's rule (0x4ad240), with a text cell
		// and two type-6 cells holding the i_boxtick model (0x4bcca0); the list fills a column marked 1 (0x6636b2: the Send and Delete columns) by setting its cell's value (0x6634b2), which
		// selects the model's frame (0x66da4d): 0 the empty box, 1 the tick (0x4bbfbc sets 1 for a set mark, 0 otherwise)
		// [BIN:TPW-EXE:0x4bcca0 outbox row cells] the title is the row's own text, from the row's left; the date cell is centred (0x65c428, 0x65c4b9: the headers' centring); both in font slot 7, white
		// [BIN:TPW-EXE:0x4bca6a outbox date] the date is UITEXT 0x1c0 (OpenTPW's 447) filled with the card's day, month and year (parameters 16-18), the save list's date format
		// [EXT:list-row-pitch] the rows take the buy list's fixed pitch, a design choice (see ParkHud.BuyRowHeight)
		var dateColumn = Control( 0x11 ).Rect;
		var markColumns = new[] { Control( 0x12 ).Rect, Control( 0x13 ).Rect };
		string Mark( (string Path, Postcard? Card, string? Error) entry, OutboxMark mark ) => MarkOf( entry ) == mark ? "1" : "0";
		string Title( (string Path, Postcard? Card, string? Error) entry ) => (entry.Card?.Manifest.Title ?? entry.Error ?? "?").Replace( '\t', ' ' );
		string Date( Postcard card )
		{
			var date = card.Manifest.CreatedUtc.LocalDateTime;
			return strings.FormatByType( UiTextRecord.SaveDate, new Dictionary<int, string> { [16] = date.Day.ToString(), [17] = date.Month.ToString(), [18] = date.Year.ToString() } );
		}
		var list = screen.Add( new UiScrollList
		{
			Id = "cards",
			Bounds = content,
			Anchor = UiAnchor.Center,
			Margin = 0,
			Background = false,
			PositionBar = false,
			RowHeight = OpenTPW.Hud.ParkHud.BuyRowHeight,
			HeaderRow = false,
			Columns = new (float Offset, Func<string> Text)[] { (0, () => ""), (dateColumn.X - content.X, () => ""), (markColumns[0].X - content.X, () => ""), (markColumns[1].X - content.X, () => "") },
			// a row's cells: title, date, and the Send and Delete cells' values (an unreadable card's row has no Send cell)
			Rows = () => cards.Select( entry => entry.Card is { } card
				? $"{Title( entry )}\t{Date( card )}\t{Mark( entry, OutboxMark.Send )}\t{Mark( entry, OutboxMark.Delete )}"
				: $"{Title( entry )}\t\t\t{Mark( entry, OutboxMark.Delete )}" ).ToArray(),
		} );
		// The cells drawn from the card itself: the title fitted into its column (to the date's), as the buy list fits its names ([EXT:fit-button-text]: how tp.exe's row
		// text treats a title too long for the row is not traced, 0x65f5b7), the date centred in its column, and the Send and Delete cells' i_boxtick at its own
		// 51-unit size, centred in its column (the buy list's box cell); an unreadable card's row has no Send cell.
		list.DrawCell = ( context, row, column, cell ) =>
		{
			if ( row < 0 || row >= cards.Count )
				return true;
			var entry = cards[row];
			var scale = cell.Height / list.RowHeight;
			var color = row == list.Selected ? UiColors.Highlight : UiColors.Text;
			switch ( column )
			{
				case 0:
					context.DrawFittedText( context.Fonts.Small, Title( entry ), cell with { Width = (dateColumn.X - content.X) * scale }, color, UiAlign.Left );
					return true;
				case 1:
					if ( entry.Card is { } card )
						context.DrawText( context.Fonts.Small, Date( card ), cell with { Width = dateColumn.Width * scale }, color, UiAlign.Center );
					return true;
				default:
					if ( column == 2 && entry.Card == null )
						return true;
					var box = OpenTPW.Hud.ParkHud.TickBoxSize * scale;
					var width = markColumns[column - 2].Width * scale;
					context.DrawModel( "i_boxtick", MarkOf( entry ) == (column == 2 ? OutboxMark.Send : OutboxMark.Delete) ? 1 : 0,
						new UiRect( cell.X + (width - box) / 2, cell.Y + (cell.Height - box) / 2, box, box ) );
					return true;
			}
		};
		// a click on a row's Send (column 2) or Delete (column 3) cell makes the row the current one and toggles that mark
		list.CellClicked = ( index, column ) =>
		{
			if ( column < 2 || index < 0 || index >= cards.Count )
				return false;
			var entry = cards[index];
			list.Select( index );
			var cell = column == 2 ? OutboxMark.Send : OutboxMark.Delete;
			if ( cell == OutboxMark.Delete || entry.Card != null )
				marks[entry.Path] = ToggledMark( MarkOf( entry ), cell );
			return true;
		};
		// The list's slider (type 3, id 1) and its arrows show only while the rows do not fit (0x664495); the list_outbox art holds the track.
		var slider = listNode.Children.Single( node => node.Type == 3 );
		var scrollBar = screen.Add( new UiListScrollBar( list, slider.Rect, slider.Children.Single( node => node.Model == "b_scroller" ).Rect, null, "b_scroller" )
			{ Id = "cardsScroll", Bounds = slider.Rect, Anchor = UiAnchor.Center } );
		UiButton ScrollArrow( string model, int step )
		{
			var node = slider.Children.Single( child => child.Model == model );
			void Scroll() => list.Scroll( step );
			return screen.Add( new UiButton { Id = step < 0 ? "cardsUp" : "cardsDown", Model = node.Model, ClickSound = table.ClickSoundOf( node ),
				Clicked = Scroll, Repeat = UiButton.ScrollArrowRepeat( Scroll ), Bounds = node.Rect, Anchor = UiAnchor.Center } );
		}
		var scrollers = new UiElement[] { scrollBar, ScrollArrow( "b_up", -1 ), ScrollArrow( "b_down", 1 ) };
		void ShowScrollers()
		{
			foreach ( var scroller in scrollers )
				scroller.Visible = cards.Count > list.VisibleRows;
		}
		ShowScrollers();
		Postcard? Selected() => list.Selected >= 0 && list.Selected < cards.Count ? cards[list.Selected].Card : null;
		// The selected card's picture frame (0xee4) and its addresses (0xee2), a line each inside the frame's inner rectangle.
		// [BIN:TPW-EXE:0x4bc100 outbox addresses] a selected row's recipients are joined with '\n' into one text in font slot 6, wrapped to the inner width, whose slider (0xee3) shows
		// only when the text overflows; here each line is a row of a list, in the small font, and that slider is not drawn
		var picture = Control( 0xEE4 );
		screen.Add( new UiModelImage { Id = "picture", Model = picture.Model!, Bounds = picture.Rect, Anchor = UiAnchor.Center } );
		var addresses = Control( 0xEE2 );
		screen.Add( new UiModelImage { Id = "addressFrame", Model = addresses.Model!, Bounds = addresses.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiScrollList { Id = "addresses", Background = false, Margin = 0, Bounds = addresses.Inner ?? addresses.Rect, Anchor = UiAnchor.Center,
			Rows = () => Selected() is { } card ? card.Manifest.To.ToArray() : Array.Empty<string>() } );
		UiButton Button( string id, LayoutNode node, Action clicked ) => screen.Add( new UiButton
		{
			Id = id, Model = node.Model, Help = strings.Help( node.Help ), Clicked = clicked, Bounds = node.Rect, Anchor = UiAnchor.Center
		} );
		Button( "markAllSend", Control( 0xEE5 ), () => MarkAll( OutboxMark.Send ) );
		Button( "markAllDelete", Control( 0xEE6 ), () => MarkAll( OutboxMark.Delete ) );
		Button( "unmarkAll", Control( 0xEE7 ), () => MarkAll( OutboxMark.None ) );
		void Apply()
		{
			// an earlier window's message would hide the send's progress on the menu's status line
			localStatus = "";
			// Both lists come from the marks before any file goes: a deleted card's mark is dropped with its file.
			var deleting = cards.Where( entry => MarkOf( entry ) == OutboxMark.Delete ).Select( entry => entry.Path ).ToList();
			var sending = cards.Where( entry => entry.Card != null && MarkOf( entry ) == OutboxMark.Send ).Select( entry => entry.Path ).ToList();
			foreach ( var path in deleting )
			{
				try
				{
					File.Delete( path );
					marks.Remove( path );
				}
				// a card that cannot be deleted stays in the outbox with its mark, and the menu's status line gives the reason
				catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
				{
					localStatus = exception.Message;
				}
			}
			list.Selected = -1;
			Reload();
			if ( sending.Count > 0 && !Session.IsLoggedIn )
			{
				ShowLogin();
				return;
			}
			if ( ReferenceEquals( stack.Top, screen ) )
				stack.Pop();
			if ( sending.Count > 0 )
				Session.SendOutbox( sending );
		}
		var ok = Button( "ok", table.Model( "b_okay" ), () => Guard( Apply ) );
		var refreshTimer = 0f;
		screen.Updating += context =>
		{
			ok.Enabled = Session.Busy == 0;
			// A send started earlier removes cards from the outbox folder; re-read it now and then.
			refreshTimer += context.Delta;
			if ( refreshTimer > 1 )
			{
				refreshTimer = 0;
				Reload();
			}
			ShowScrollers();
		};
		screen.Back = stack.Pop;
		Push( screen );
	}

	/// <summary>A card's mark after a click on its Send or Delete cell (<paramref name="cell"/>): the same mark comes off, the other mark gives way to it.</summary>
	internal static OutboxMark ToggledMark( OutboxMark current, OutboxMark cell ) => current == cell ? OutboxMark.None : cell;

	public void ShowInbox()
	{
		var screen = new UiScreen( "inbox" );
		var window = UiDialogs.CenteredWindow( 1700, 1200 );
		UiDialogs.AddWindow( screen, window, "w_big", () => OnlineStrings.Get( OnlineLabel.Inbox ) );
		IReadOnlyList<(string Path, Postcard? Card, string? Error)> cards = Session.Folders.List( Session.Folders.Inbox );
		var list = screen.Add( new UiScrollList
		{
			Id = "cards",
			Model = "list_outbox",
			Bounds = new UiRect( window.X + 100, window.Y + 170, 760, 680 ),
			Anchor = UiAnchor.Center,
			Rows = () => cards.Select( entry => entry.Card is { } card ? $"{card.Manifest.From}: {card.Manifest.Title}" : entry.Error ?? "?" ).ToArray(),
		} );
		screen.Add( new UiLabel
		{
			Id = "text",
			Wrap = true,
			Font = fonts => fonts.Small,
			Bounds = new UiRect( window.X + 900, window.Y + 170, window.Width - 1080, 680 ),
			Anchor = UiAnchor.Center,
			Text = () => list.Selected >= 0 && list.Selected < cards.Count && cards[list.Selected].Card is { } card ? $"{card.Manifest.Title}\n\n{card.Manifest.Text}" : ""
		} );
		var fetch = screen.Add( new UiButton { Id = "fetch", Text = () => OnlineStrings.Get( OnlineLabel.Refresh ), Clicked = () => Guard( Session.FetchInbox ),
			Bounds = new UiRect( window.X + 100, window.Y + 880, 420, 110 ), Anchor = UiAnchor.Center } );
		var refreshTimer = 0f;
		screen.Updating += context =>
		{
			fetch.Enabled = Session.IsLoggedIn && Session.Busy == 0;
			refreshTimer += context.Delta;
			if ( refreshTimer > 1 )
			{
				refreshTimer = 0;
				cards = Session.Folders.List( Session.Folders.Inbox );
			}
		};
		AddStatus( screen, window );
		AddBack( screen, window );
		Push( screen );
	}

	// ---- Chat (the original chat overlay, tp.exe table 0x758AE8) ----------------------------------

	/// <summary>
	/// The current room in the overhead chat view: the original chat overlay (<see cref="ChatPanel"/>: the people list with its tabs, the postcard, vote
	/// and earmuffs buttons, the message list, the entry and the name popups) over the chat park view, with the room's name over the park. A visit's chat
	/// mode shows the same overlay over the visited park, where a satellite's park is visited too (<see cref="EnterChatPark"/>).
	/// </summary>
	public void ShowChat()
	{
		if ( Session.Chat == null && Session.IsLoggedIn )
			Guard( Session.ConnectChat );
		var panel = new ChatPanel( strings, stack, () => Session ) { Postcard = ShowSendPostcard };
		var screen = new ChatPanel.ChatScreen( ChatScreenName, panel );
		// The park view over which the original drew its chat overlay; [EXT:online-chat-park-view] a room entered without a park to visit (an older server,
		// a satellite without a scene, a published park's chat from its details) is shown from above.
		var view = screen.Add( new UiChatParkView( Session ) { Id = "park", Bounds = new UiRect( 543, 30, 1462, 1120 ), Anchor = UiAnchor.TopLeft } );
		panel.AddTo( screen );
		// The room's name over the park view, where the original showed the park it sat on.
		screen.Add( new UiLabel { Id = "room", Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center,
			Bounds = new UiRect( 543, 1050, 1462, 90 ), Anchor = UiAnchor.TopLeft, Text = () => Session.CurrentRoomName } );
		screen.Updating += context =>
		{
			view.Enabled = Session.Chat != null;
			view.Walk( context.Delta );
		};
		// a name popup over the screen closes itself first
		screen.Back = () => { if ( ReferenceEquals( stack.Top, screen ) ) stack.Pop(); };
		Push( screen );
		screen.Focus( panel.Entry );
	}

	/// <summary>The chat park screen's name (the corner chat box hides while it is open).</summary>
	public const string ChatScreenName = "chat";

// ---- Import a local park file ---------------------------------------------------------------

	public void ShowImport()
	{
		var screen = new UiScreen( "import" );
		var window = UiDialogs.CenteredWindow( 1500, 1100 );
		UiDialogs.AddWindow( screen, window, "w_med", () => OnlineStrings.Get( OnlineLabel.ImportFile ) );
		var path = AddField( screen, "path", () => OnlineStrings.Get( OnlineLabel.ImportFile ), window.X + 120, window.Y + 170, window.Width - 340, 2048 );
		path.Submitted = () => Inspect( path.Text, start: false );
		path.Changed = _ => inspected = null;
		screen.Add( new UiLabel { Id = "description", Wrap = true, Font = fonts => fonts.Small, Bounds = new UiRect( window.X + 120, window.Y + 360, window.Width - 340, 380 ), Anchor = UiAnchor.Center,
			Text = () => inspected == null ? "" : ParkSharing.Describe( inspected ) } );
		screen.Add( new UiButton { Id = "inspect", Text = () => OnlineStrings.Get( OnlineLabel.ImportFile ), Clicked = () => Inspect( path.Text, start: false ),
			Bounds = new UiRect( window.X + 120, window.Bottom - 360, 480, 110 ), Anchor = UiAnchor.Center } );
		var visit = screen.Add( new UiButton { Id = "visit", Text = () => OnlineStrings.Get( OnlineLabel.VisitReadOnly ), Clicked = () => { if ( inspected != null ) host.Visit?.Invoke( inspected ); },
			Bounds = new UiRect( window.X + 640, window.Bottom - 360, 480, 110 ), Anchor = UiAnchor.Center } );
		screen.Updating += _ => visit.Enabled = inspected != null && host.Visit != null;
		AddStatus( screen, window );
		AddBack( screen, window );
		screen.Focus( path );
		Push( screen );
	}

	/// <param name="parkId">The published park's id when the file was just downloaded from the server: the visit joins the park's chat room.</param>
	/// <param name="room">The chat room the visit joins instead (a chat satellite's, for the visit of its park).</param>
	private void Inspect( string path, bool start, string? parkId = null, string? room = null )
	{
		inspected = null;
		Guard( () =>
		{
			inspected = ParkSharing.PrepareVisit( ParkPackage.Load( path ) ) with { OnlineParkId = parkId, ChatRoom = room };
			if ( start && host.Visit != null )
				host.Visit( inspected );
		} );
	}

	// ---- Shared pieces ---------------------------------------------------------------------------

	/// <param name="passOutcomeDown">The screen closes itself when its work is done (login, two-step setup, account deletion, recovery): what it reported stays for the screen below to show.</param>
	private void Push( UiScreen screen, bool passOutcomeDown = false )
	{
		// what was reported while the new screen was open belongs to it: the screen below shows only what comes after it is on top again (StatusText)
		if ( stack.Top is { } below && !passOutcomeDown )
			screen.Removed += () =>
			{
				if ( statusBaselines.TryGetValue( below, out var baseline ) )
					baseline.Value = Session.StatusSerial;
			};
		stack.Push( screen );
	}

	private UiTextField AddField( UiScreen screen, string id, Func<string> label, float x, float y, float width, int maximumLength )
	{
		screen.Add( new UiLabel { Id = id + "Label", Text = label, Bounds = new UiRect( x, y, width, 60 ), Anchor = UiAnchor.Center } );
		return screen.Add( new UiTextField { Id = id, MaximumLength = maximumLength, Model = "f_text1", Bounds = new UiRect( x, y + 64, width, 84 ), Anchor = UiAnchor.Center } );
	}

	/// <summary>
	/// The status line of <paramref name="screen"/>: its text now, empty while another screen is on top (a status is shown once, on the top screen) and while nothing has been reported since the screen was
	/// opened or came back to the top (a stale message of an earlier search or request says nothing here); the screen's own <see cref="localStatus"/> and, while a request runs, the progress text it set
	/// (<see cref="OnlineSession.Progress"/>, the original's progress messages) or "..." come first.
	/// </summary>
	// [EXT:ONLINE-UI] the original's status box showed the progress message of the request that ran; nothing in it outlived the screen
	private Func<string> StatusText( UiScreen screen, bool busy = true )
	{
		var baseline = statusBaselines.GetValue( screen, _ => new System.Runtime.CompilerServices.StrongBox<long>() );
		baseline.Value = Session.StatusSerial;
		return () =>
		{
			if ( !ReferenceEquals( stack.Top, screen ) )
				return "";
			if ( busy && Session.Busy > 0 )
				return Session.Progress ?? "...";
			if ( localStatus.Length > 0 )
				return localStatus;
			return Session.StatusSerial != baseline.Value ? Session.Status : "";
		};
	}

	/// <summary>The <see cref="OnlineSession.StatusSerial"/> each screen's status line (<see cref="StatusText"/>) counts messages from: its opening, or the last time a screen above it closed.</summary>
	private readonly System.Runtime.CompilerServices.ConditionalWeakTable<UiScreen, System.Runtime.CompilerServices.StrongBox<long>> statusBaselines = new();

	/// <param name="idle">What the status line shows while it has no status to report (the online menu's reason Publish park is greyed, which the pointer's help does not give a keyboard or a gamepad).</param>
	private void AddStatus( UiScreen screen, UiRect window, Func<string>? idle = null )
	{
		var status = StatusText( screen );
		screen.Add( new UiLabel
		{
			Id = "status",
			Wrap = true,
			Font = fonts => fonts.Small,
			Color = UiColors.Value,
			Bounds = new UiRect( window.X + 120, window.Bottom - 175, window.Width - 700, 110 ),
			Anchor = UiAnchor.Center,
			Text = () => status() is { Length: > 0 } text ? text : idle?.Invoke() ?? ""
		} );
	}

	/// <summary>The Back button's place: the original close button's offsets from the window's bottom right corner (the publish window's cross, 129 and 187 in).</summary>
	// [EXT:own-window-sizes] OpenTPW's own windows have no table, so the cross takes the original dialogs' offsets from the corner
	internal static UiRect BackBounds( UiRect window ) => new( window.Right - 129, window.Bottom - 187, 83, 83 );

	private void AddBack( UiScreen screen, UiRect window ) => AddBack( screen, window, stack.Pop );

	/// <summary>The screen's Back button and key, doing <paramref name="back"/> (which closes the screen).</summary>
	private void AddBack( UiScreen screen, UiRect window, Action back )
	{
		// the cross (b_exit) is a bare round button at the window's bottom right corner (as in the original dialogs): a text label does not fit it
		screen.Add( new UiButton { Id = "back", Model = "b_exit", Help = strings.Help( 2 ), Clicked = back, Bounds = BackBounds( window ), Anchor = UiAnchor.Center } );
		screen.Back = back;
	}

	/// <summary>
	/// The original dialogs' close button (-1/-2): a small button at the window's bottom right corner, at the authored
	/// place of the decoded dialog tables (e.g. data:0x50784 at (1889,820)-(1972,903)).
	/// </summary>
	private void AddCloseButton( UiScreen screen, float x, float y )
	{
		// the cross (b_exit) is a bare round button: a text label does not fit it; its hover help is the original help 2
		screen.Add( new UiButton { Id = "close", Model = "b_exit", Help = strings.Help( 2 ), Clicked = stack.Pop,
			Bounds = new UiRect( x, y, 83, 83 ), Anchor = UiAnchor.Center } );
	}

	private void Guard( Action action )
	{
		try
		{
			action();
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or ArgumentException or InvalidOperationException or UnauthorizedAccessException )
		{
			localStatus = exception.Message;
		}
	}
}

/// <summary>A card's mark in the outbox: tp.exe's row flags 0x1000000 (send) and 0x2000000 (delete), of which a row holds one at most.</summary>
internal enum OutboxMark { None, Send, Delete }
