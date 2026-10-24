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
		screen.Add( new UiLabel { Id = "status", Wrap = true, Font = fonts => fonts.Small, Color = UiColors.Value, Align = UiAlign.Left,
			Bounds = new UiRect( 1349, 1418, 630, 60 ), Anchor = UiAnchor.BottomRight, Text = () => localStatus.Length > 0 ? localStatus : Session.Status } );
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
		// [EXT:ONLINE-UI] the city's name and the fetch status while no park is shown
		var status = screen.Add( new UiLabel { Id = "cityStatus", Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center, Bounds = new UiRect( 524, 118, 1000, 135 ), Anchor = UiAnchor.Top,
			Text = () => $"{city.Name}\n{(localStatus.Length > 0 ? localStatus : Session.Status)}" } );
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

	/// <summary>OpenTPW's online functions that have no place on the original globe screen.</summary>
	public void ShowMenu()
	{
		var screen = new UiScreen( "onlineMenu" );
		var window = UiDialogs.CenteredWindow( 1200, 1460 );
		UiDialogs.AddWindow( screen, window, "w_big", () => OnlineStrings.Get( OnlineLabel.OnlineMenu ) );
		var level = host.Level();
		var entries = new List<(string Id, Func<string> Label, Action Clicked, Func<bool> Enabled)>
		{
			("login", () => Session.IsLoggedIn ? OnlineStrings.Get( OnlineLabel.LogOut ) : OnlineStrings.Ui( OnlineStrings.OnlineLogin, "Online login" ),
				() => { if ( Session.IsLoggedIn ) Guard( () => { Session.Disconnect(); localStatus = ""; } ); else ShowLogin(); }, () => true),
			("news", () => OnlineStrings.Get( OnlineLabel.News ), ShowNews, () => Session.Client != null),
			("findParks", () => OnlineStrings.Ui( OnlineStrings.FindParks, "Find parks" ), ShowFindParks, () => Session.IsLoggedIn),
			("publish", () => OnlineStrings.Ui( OnlineStrings.PublishPark, "Publish park" ), ShowPublish, () => level != null && !level.IsReadOnlyVisit && !level.IsCoopGuestPark && ParkSharing.ModsRefusal( level ) == null),
			("sendPostcard", () => OnlineStrings.Ui( OnlineStrings.SendPostcard, "Send postcard" ), ShowSendPostcard, () => true),
			("outbox", () => OnlineStrings.Ui( OnlineStrings.UnsentPostcards, "Unsent postcards" ), ShowOutbox, () => true),
			("inbox", () => OnlineStrings.Get( OnlineLabel.Inbox ), ShowInbox, () => true),
			("chat", () => OnlineStrings.Get( OnlineLabel.Chat ), ShowChat, () => Session.IsLoggedIn),
			("import", () => OnlineStrings.Get( OnlineLabel.ImportFile ), ShowImport, () => true),
			// [EXT:ONLINE-UI] OpenTPW servers let players delete their own account (docs/SERVER.md).
			("deleteAccount", () => OnlineStrings.Get( OnlineLabel.DeleteAccount ), ShowDeleteAccount, () => Session.IsLoggedIn),
		};
		for ( var index = 0; index < entries.Count; index++ )
		{
			var entry = entries[index];
			var button = screen.Add( new UiButton
			{
				Id = entry.Id,
				Text = entry.Label,
				Clicked = entry.Clicked,
				Bounds = new UiRect( window.X + 200, window.Y + 170 + index * 108, window.Width - 480, 96 ),
				Anchor = UiAnchor.Center
			} );
			screen.Updating += _ => button.Enabled = entry.Enabled() && Session.Busy == 0;
		}
		// The original mail icon (i_mail) at its authored place while postcards wait in the inbox.
		screen.Add( new UiModelImage { Id = "mail", Model = "i_mail", Bounds = Authored( "i_mail", new UiRect( 1500, 1180, 120, 110 ) ), Anchor = UiAnchor.BottomRight } );
		// [DATA:Language/English/UITEXT.str:472] the original reminded the player of unsent postcards before going online
		screen.Add( new UiLabel { Id = "waiting", Wrap = true, Font = fonts => fonts.Small, Align = UiAlign.Center,
			Bounds = new UiRect( window.X + 120, window.Bottom - 290, window.Width - 240, 100 ), Anchor = UiAnchor.Center,
			Text = () => Session.Folders.List( Session.Folders.Outbox ).Any( entry => entry.Card != null )
				? OnlineStrings.Ui( OnlineStrings.OutboxWaiting, "You have postcards in your outbox, waiting to be sent." ) : "" } );
		screen.Updating += _ => screen.Find( "mail" )!.Visible = Session.Inbox.Count > 0;
		AddStatus( screen, window );
		AddBack( screen, window );
		Push( screen );
	}

	// ---- Build together: sharing the open park (OpenTPW, C2d-2) --------------------------------

	/// <summary>The Build Together screen's name.</summary>
	internal const string BuildTogetherScreenName = "buildTogether";

	/// <summary>
	/// [EXT:online-coop-session] Build Together (C2d-2; docs/COOP.md, "Sharing the park from the game"): shares the open park with other players, who watch the host
	/// build, walk around and chat (C2: only the host builds). Before sharing: why the park cannot be shared, or a login, then the shared park's title, whether it is on
	/// the open list, and how many players it takes. While it is shared: its code, the players with a removal for each guest, an invitation by name, and its end for
	/// everyone (the park stays the host's). Every line is in the player's language.
	/// </summary>
	// [EXT:own-window-sizes] the window and the places of its controls are OpenTPW's: the original has no shared parks
	public void ShowBuildTogether()
	{
		if ( host.Hosting is not { } hosting )
			return;
		var screen = new UiScreen( BuildTogetherScreenName );
		var window = UiDialogs.CenteredWindow( 1400, 1300 );
		UiDialogs.AddWindow( screen, window, "w_big", () => strings.Extra( OpenTpwText.BuildTogether ) );
		var x = window.X + 120;
		var width = window.Width - 240;
		// the title starts as the player's park (by the online name, once known after a login here) until the player writes one
		var titleChosen = false;
		string DefaultTitle() => (Session.Client?.Session?.Name ?? Session.Settings.PlayerName ?? "").Trim() is { Length: > 0 } name
			? string.Format( strings.Extra( OpenTpwText.SharedParkDefaultTitle ), name ) : "";
		var listed = false;
		var players = CoopProtocol.MaximumPlayers;
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
		var titleLabel = screen.Add( new UiLabel { Id = "titleLabel", Font = fonts => fonts.Label, Text = () => strings.Extra( OpenTpwText.SharedParkTitle ),
			Bounds = new UiRect( x, window.Y + 500, 300, 90 ), Anchor = UiAnchor.Center } );
		var titleField = screen.Add( new UiTextField { Id = "parkTitle", MaximumLength = CoopProtocol.MaximumTitleLength, Model = "f_text1",
			Bounds = new UiRect( x + 320, window.Y + 490, width - 320, 93 ), Anchor = UiAnchor.Center } );
		titleField.Text = DefaultTitle();
		titleField.Changed = _ => titleChosen = true;
		var listedButton = screen.Add( new UiButton { Id = "listed", Clicked = () => listed = !listed,
			Text = () => strings.Extra( listed ? OpenTpwText.SharedParkListed : OpenTpwText.SharedParkUnlisted ),
			Bounds = new UiRect( x, window.Y + 620, width, 100 ), Anchor = UiAnchor.Center } );
		var playersButton = screen.Add( new UiButton { Id = "players", Clicked = () => players = players >= CoopProtocol.MaximumPlayers ? 2 : players + 1,
			Text = () => string.Format( strings.Extra( OpenTpwText.SharedParkPlayers ), players ),
			Bounds = new UiRect( x, window.Y + 740, width, 100 ), Anchor = UiAnchor.Center } );
		void Share()
		{
			var chosen = titleField.Text.Trim();
			if ( chosen.Length == 0 || chosen.Any( char.IsControl ) )
				return;
			hosting.StartSharing( new CoopShareRequest( chosen, listed, players ) );
		}
		titleField.Submitted = Share;
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
		var rows = new List<(UiLabel Name, UiButton Remove)>();
		for ( var index = 0; index < CoopProtocol.MaximumPlayers; index++ )
		{
			var row = index;
			CoopMemberInfo? Member() => Attempt()?.State is { } state && row < state.Members.Count ? state.Members.OrderBy( member => member.Slot ).ElementAt( row ) : null;
			var name = screen.Add( new UiLabel { Id = $"member{row}", Font = fonts => fonts.Label,
				Text = () => Member() is { } member && Attempt()?.State is { } state ? MemberText( member, state ) : "",
				Bounds = new UiRect( x, window.Y + 350 + row * 90, width - 420, 80 ), Anchor = UiAnchor.Center } );
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
			rows.Add( (name, remove) );
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
			if ( !titleChosen )
				titleField.Text = DefaultTitle();
			intro.Visible = titleLabel.Visible = titleField.Visible = listedButton.Visible = playersButton.Visible = share.Visible = starting;
			login.Visible = starting && !Session.IsLoggedIn;
			share.Enabled = Session.IsLoggedIn && Session.Busy == 0 && hosting.ShareRefusal == CoopShareRefusal.None && titleField.Text.Trim().Length > 0;
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
				rows[index].Remove.Visible = hosted && member != null && member.Id != Attempt()!.State!.Host;
			}
		};
		AddStatus( screen, window );
		AddBack( screen, window );
		screen.Focus( titleField );
		Push( screen );
	}

	/// <summary>[EXT:online-coop-session] What the Build Together screen says of an attempt: its stage while it runs, its outcome once over.</summary>
	private string ShareMessage( CoopShare share ) => share.Stage switch
	{
		CoopShareStage.Preparing or CoopShareStage.Creating or CoopShareStage.Cancelling => strings.Extra( OpenTpwText.SharingThePark ),
		CoopShareStage.Done => share.Outcome switch
		{
			CoopShareOutcome.Refused => RefusalText( share.Refusal ),
			CoopShareOutcome.LinkStopped => strings.Extra( share.Stop switch
			{
				CoopStop.InUseElsewhere or CoopStop.Replaced => OpenTpwText.SharedParkLinkInUse,
				CoopStop.ServerTooOld => OpenTpwText.SharedParkServerTooOld,
				_ => OpenTpwText.SharedParkLinkLost,
			} ),
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
		Push( screen );
	}

	// ---- Login (UI-MAP "Online login dialog") ------------------------------------------------

	/// <summary>
	/// The original login dialog's composition (Mac UI table data:0x505f4): a window (500,233)-(1516,830) with the
	/// Login title (0x4d) at (606,304)-(1333,384), the 16-character name field (0x49) and password field (0x4b) with
	/// their labels (0x4a/0x4c) inside them, and two buttons at the right edge (-1 at (1376,524)-(1460,608), -2 at
	/// (1387,613)-(1470,696)). Enter moves from the name to the password and submits from there (0x10184924).
	/// OpenTPW's own additions (server address, authenticator code, register, recovery) sit below the original
	/// window ([EXT:ONLINE-UI]).
	/// </summary>
	public void ShowLogin()
	{
		var screen = new UiScreen( "onlineLogin" );
		var window = new UiRect( 500, 233, 1016, 597 );
		screen.Add( new UiModelImage { Id = "window", Model = "w_med", Bounds = window, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "title", Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center,
			Bounds = new UiRect( 606, 304, 727, 80 ), Anchor = UiAnchor.Center, Text = () => OnlineStrings.Ui( OnlineStrings.OnlineLogin, "Online login" ) } );
		// The original fields: 16 characters each, labels inside the field's frame (data:0x505f4).
		var name = screen.Add( new UiTextField { Id = "name", MaximumLength = 16, Model = "f_text1", Bounds = new UiRect( 632, 463, 675, 93 ), Anchor = UiAnchor.Center, TextInset = 322 } );
		name.Text = Session.Settings.PlayerName ?? "";
		var password = screen.Add( new UiTextField { Id = "password", MaximumLength = 16, Model = "f_text1", Password = true, Bounds = new UiRect( 632, 593, 675, 92 ), Anchor = UiAnchor.Center, TextInset = 322 } );
		screen.Add( new UiLabel { Id = "nameLabel", Text = () => OnlineStrings.Ui( OnlineStrings.LoginName, "Login name" ),
			Bounds = new UiRect( 656, 487, 290, 45 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "passwordLabel", Text = () => OnlineStrings.Ui( OnlineStrings.Password, "Password" ),
			Bounds = new UiRect( 656, 617, 290, 44 ), Anchor = UiAnchor.Center } );
		// [EXT:ONLINE-UI] below the original window: the server address (the original service address was built in),
		// the authenticator code of two-step login, register and recovery.
		var server = AddField( screen, "server", () => OnlineStrings.Get( OnlineLabel.ServerAddress ), 560, 856, 900, 256 );
		server.Text = Session.Settings.ServerUrl ?? OnlineSession.SuggestedServerUrl ?? "";
		// [EXT:online-two-step] accounts without two-step login leave the code empty.
		var code = AddField( screen, "code", () => OnlineStrings.Get( OnlineLabel.AuthenticatorCode ), 560, 1006, 420, Totp.Digits );
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
			catch ( Exception exception ) when ( exception is ArgumentException or InvalidOperationException or UriFormatException or InvalidDataException )
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
		screen.Add( new UiButton { Id = "login", Model = "b_login", Text = () => OnlineStrings.Get( OnlineLabel.LogIn ), Clicked = () => Submit( register: false ),
			Bounds = new UiRect( 1376, 524, 84, 84 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "close", Model = "b_bck2lob", Text = () => strings.Extra( OpenTpwText.Back ), Clicked = stack.Pop,
			Bounds = new UiRect( 1387, 613, 83, 83 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "register", Text = () => OnlineStrings.Get( OnlineLabel.Register ), Clicked = () => Submit( register: true ),
			Bounds = new UiRect( 560, 1170, 420, 100 ), Anchor = UiAnchor.Center } );
		// [EXT:online-email] recovery by e-mail; the server answers the same whether the account exists
		screen.Add( new UiButton { Id = "forgot", Text = () => OnlineStrings.Get( OnlineLabel.ForgotPassword ),
			Clicked = () => Guard( () => { Session.UseServer( server.Text.Trim(), name.Text.Trim() ); ShowRecovery( name.Text.Trim() ); } ),
			Bounds = new UiRect( 1020, 1170, 420, 100 ), Anchor = UiAnchor.Center } );
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
		AddStatus( screen, window );
		screen.Back = stack.Pop;
		screen.Focus( name.Text.Length == 0 ? name : password );
		Push( screen );
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
		screen.Add( new UiButton { Id = "back", Model = "b_listpark", Text = () => strings.Extra( OpenTpwText.Back ), Clicked = Cancel,
			Bounds = new UiRect( window.Right - 540, window.Bottom - 175, 320, 110 ), Anchor = UiAnchor.Center } );
		screen.Back = Cancel;
		screen.Focus( (UiElement?)email ?? code );
		Push( screen );
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
		Push( screen );
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
		if ( host.Level() == null )
			return;
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

	/// <summary>The park's name and description, published into <paramref name="city"/> (none on servers without cities).</summary>
	/// <summary>
	/// The Publish Park form in its original composition (Mac UI table data:0x514de): a window (248,30)-(1800,1007)
	/// titled "Publish Park" (0x11e5) at (788,75)-(1260,155), the park-name field (0x11e4) at (682,279)-(1366,372),
	/// the description field (0x11e3) at (684,444)-(1364,892), the check button (-1) at (1660,731)-(1743,815) and
	/// the close button (-2) at (1671,820)-(1754,903).
	/// </summary>
	private void ShowPublishForm( CityInfo? city )
	{
		var level = host.Level();
		if ( level == null )
			return;
		var screen = new UiScreen( "publishPark" );
		var window = new UiRect( 248, 30, 1552, 977 );
		screen.Add( new UiModelImage { Id = "window", Model = "w_med", Bounds = window, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "title", Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center,
			Bounds = new UiRect( 788, 75, 472, 80 ), Anchor = UiAnchor.Center,
			Text = () => city == null ? OnlineStrings.Ui( OnlineStrings.PublishPark, "Publish Park" ) : $"{OnlineStrings.Ui( OnlineStrings.PublishPark, "Publish Park" )}: {city.Name}" } );
		// The original name field (0x11e4) starts with the park's own name; the description field (0x11e3) is the
		// multi-line area below it.
		var name = screen.Add( new UiTextField { Id = "parkName", MaximumLength = 64, Model = "f_text1", Bounds = new UiRect( 682, 279, 684, 93 ), Anchor = UiAnchor.Center } );
		name.Text = "My park";
		var description = screen.Add( new UiTextField { Id = "description", MaximumLength = 1024, Model = "f_text1", Bounds = new UiRect( 684, 444, 680, 448 ), Anchor = UiAnchor.Center } );
		// [EXT:ONLINE-067] off unless the player chooses it: the website list makes the park and the player name public on the web.
		var showOnWebsite = false;
		screen.Add( new UiButton { Id = "website", Text = () => OnlineStrings.Get( showOnWebsite ? OnlineLabel.ShownOnWebsite : OnlineLabel.NotShownOnWebsite ),
			Clicked = () => showOnWebsite = !showOnWebsite, Bounds = new UiRect( 500, 950, 1000, 100 ), Anchor = UiAnchor.Center } );
		// The original check button (-1) submits; the export-to-file action is OpenTPW's own beside it.
		var publish = screen.Add( new UiButton { Id = "publish", Text = () => OnlineStrings.Ui( OnlineStrings.PublishPark, "Publish Park" ),
			Clicked = () => Guard( () => Session.Publish( ParkSharing.ExportLevel( level, name.Text, Session.Settings.PlayerName ?? "", description.Text ), showOnWebsite, city?.Id ?? 0 ) ),
			Bounds = new UiRect( 1660, 731, 83, 84 ), Anchor = UiAnchor.Center } );
		var export = screen.Add( new UiButton { Id = "export", Text = () => OnlineStrings.Get( OnlineLabel.ExportPark ),
			Clicked = () => Guard( () =>
			{
				var package = ParkSharing.ExportLevel( level, name.Text, Session.Settings.PlayerName ?? "", description.Text );
				var path = Path.Combine( Session.Folders.Parks, package.Manifest.PackageId.ToString( "N" ) + ParkPackage.FileExtension );
				package.Save( path );
				localStatus = OnlineStrings.Get( OnlineLabel.SavedTo ) + path;
			} ),
			Bounds = new UiRect( 500, 1080, 480, 110 ), Anchor = UiAnchor.Center } );
		AddCloseButton( screen, 1671, 820 );
		// [EXT:mods] a park that records mods cannot be exported or published yet (ParkSharing.ModsRefusal): both buttons are hidden
		screen.Updating += _ => { publish.Enabled = Session.IsLoggedIn && Session.Busy == 0; publish.Visible = export.Visible = ParkSharing.ModsRefusal( level ) == null; };
		AddStatus( screen, window );
		screen.Back = stack.Pop;
		screen.Focus( name );
		Push( screen );
	}

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
	/// Unsent Postcards in its original composition (Mac UI table data:0x508bc): a window (186,30)-(2018,1007)
	/// titled "Unsent Postcards" (0xee9) at (809,66)-(1281,146), the card's picture (0xee2) at (246,154)-(777,404),
	/// its preview (0xee4) at (246,425)-(772,782), the three bulk buttons (0xee5 mark all send, 0xee6 mark all
	/// delete, 0xee7 unmark all) at (265/438/610,792)-(419/591/764,945) and the four-column list (0xee8) at
	/// (793,151)-(1823,944) with its Title/Date/Send/Delete headers (ids 16-19).
	/// </summary>
	public void ShowOutbox()
	{
		host.Advise?.Invoke( RestoredAdvice.OutboxHelp );
		var screen = new UiScreen( "outbox" );
		var window = new UiRect( 186, 30, 1832, 977 );
		screen.Add( new UiModelImage { Id = "window", Model = "w_big", Bounds = window, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "title", Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center,
			Bounds = new UiRect( 809, 66, 472, 80 ), Anchor = UiAnchor.Center, Text = () => OnlineStrings.Ui( OnlineStrings.UnsentPostcards, "Unsent Postcards" ) } );
		var marks = new Dictionary<string, int>(); // path -> 1 send, 2 delete
		IReadOnlyList<(string Path, Postcard? Card, string? Error)> cards = Array.Empty<(string, Postcard?, string?)>();
		void Reload() => cards = Session.Folders.List( Session.Folders.Outbox );
		Reload();
		// [DATA:Mac UI table data:0x508bc] the four columns (ids 16-19); the send/delete columns are narrow
		var columns = new (float Offset, Func<string> Text)[]
		{
			(816 - 793, () => OnlineStrings.Ui( OnlineStrings.MessageTitle, "Title" )),
			(1194 - 793, () => OnlineStrings.Ui( OnlineStrings.DateLabel, "Date" )),
			(1491 - 793, () => OnlineStrings.Ui( OnlineStrings.Send, "Send" )),
			(1608 - 793, () => OnlineStrings.Ui( OnlineStrings.Delete, "Delete" )),
		};
		var list = screen.Add( new UiScrollList
		{
			Id = "cards",
			Model = "list_outbox",
			Bounds = new UiRect( 793, 151, 1030, 793 ),
			Anchor = UiAnchor.Center,
			RowHeight = 76,
			Columns = columns,
			Rows = () => cards.Select( entry => entry.Card is { } card
				? $"{string.Join( ", ", card.Manifest.To )}\t{card.Manifest.CreatedUtc.LocalDateTime.ToShortDateString()}\t" +
					(marks.GetValueOrDefault( entry.Path ) == 1 ? "X" : "") + "\t" + (marks.GetValueOrDefault( entry.Path ) == 2 ? "X" : "")
				: entry.Error ?? "?" ).ToArray(),
		} );
		// The selected card's picture area (0xee2) and preview (0xee4): the card's sender and text.
		screen.Add( new UiLabel { Id = "picture", Wrap = true, Font = fonts => fonts.Small, Align = UiAlign.Center,
			Bounds = new UiRect( 246, 154, 531, 250 ), Anchor = UiAnchor.Center,
			Text = () => Selected() is { } card ? string.Join( "\n", card.Manifest.To ) : "" } );
		screen.Add( new UiLabel { Id = "preview", Wrap = true, Font = fonts => fonts.Small,
			Bounds = new UiRect( 246, 425, 526, 357 ), Anchor = UiAnchor.Center,
			Text = () => Selected() is { } card ? $"{card.Manifest.Title}\n\n{card.Manifest.Text}" : "" } );
		Postcard? Selected() => list.Selected >= 0 && list.Selected < cards.Count ? cards[list.Selected].Card : null;
		// Marking one card for sending clears its delete mark and the other way around (0x10185d5c).
		void Mark( int mark )
		{
			if ( list.Selected < 0 || list.Selected >= cards.Count )
				return;
			var path = cards[list.Selected].Path;
			marks[path] = marks.GetValueOrDefault( path ) == mark ? 0 : mark;
		}
		var markSend = screen.Add( new UiButton { Id = "markSend", Text = () => OnlineStrings.Get( OnlineLabel.MarkAllSend ), Clicked = () => Mark( 1 ),
			Bounds = new UiRect( 380, 1040, 340, 90 ), Anchor = UiAnchor.Center } );
		var markDelete = screen.Add( new UiButton { Id = "markDelete", Text = () => OnlineStrings.Get( OnlineLabel.MarkAllDelete ), Clicked = () => Mark( 2 ),
			Bounds = new UiRect( 740, 1040, 340, 90 ), Anchor = UiAnchor.Center } );
		// The three original bulk buttons (0xee5-0xee7).
		// [EXT:ONLINE-UI] the original's table gives these controls (and the dialogs' 83 x 83 close button) a rectangle and no text: they are art buttons, at about 96 pixels
		// wide on a 1280 x 720 screen, and the original's control text is not wrapped and is cut off at the control's bitmap. OpenTPW's labels do not fit that size on the
		// button, so each button carries its label as a wrapped caption under it, inside the window's frame; the close button's own label shrinks to fit (UiTextFit)
		void Bulk( string id, Func<string> label, Action clicked, UiRect rect )
		{
			screen.Add( new UiButton { Id = id, Clicked = clicked, Bounds = rect, Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = id + "Caption", Wrap = true, Font = fonts => fonts.Small, Align = UiAlign.Center, Text = label,
				Bounds = new UiRect( rect.X + rect.Width / 2 - 86, rect.Bottom + 2, 172, 60 ), Anchor = UiAnchor.Center } );
		}
		Bulk( "markAllSend", () => OnlineStrings.Get( OnlineLabel.MarkAllSend ), () => { foreach ( var entry in cards ) marks[entry.Path] = 1; }, new UiRect( 265, 792, 154, 153 ) );
		Bulk( "markAllDelete", () => OnlineStrings.Get( OnlineLabel.MarkAllDelete ), () => { foreach ( var entry in cards ) marks[entry.Path] = 2; }, new UiRect( 438, 792, 153, 153 ) );
		Bulk( "unmarkAll", () => OnlineStrings.Get( OnlineLabel.UnmarkAll ), () => marks.Clear(), new UiRect( 610, 792, 154, 153 ) );
		// The Send action sends the marked cards (all of them when none is marked) and deletes the marked-deleted ones;
		// "Sending mails from outbox, please wait ..." is UITEXT 313.
		var send = screen.Add( new UiButton { Id = "sendAll", Model = "b_sendall", Text = () => OnlineStrings.Ui( OnlineStrings.Send, "Send" ),
			Clicked = () => Guard( () =>
			{
				foreach ( var entry in cards.Where( entry => marks.GetValueOrDefault( entry.Path ) == 2 ).ToList() )
				{
					File.Delete( entry.Path );
					cards = cards.Where( item => item.Path != entry.Path ).ToList();
					marks.Remove( entry.Path );
				}
				var markedForSending = cards.Where( entry => marks.GetValueOrDefault( entry.Path ) == 1 ).Select( entry => entry.Path ).ToList();
				var sendable = markedForSending.Count > 0 ? markedForSending
					: cards.Where( entry => entry.Card != null && marks.GetValueOrDefault( entry.Path ) != 2 ).Select( entry => entry.Path ).ToList();
				Session.SendOutbox( sendable );
				localStatus = OnlineStrings.Ui( OnlineStrings.SendingOutbox, "Sending mails from outbox, please wait ..." );
			} ),
			Bounds = new UiRect( 500, 1160, 480, 110 ), Anchor = UiAnchor.Center } );
		var delete = screen.Add( new UiButton { Id = "delete", Text = () => OnlineStrings.Ui( OnlineStrings.Delete, "Delete" ),
			Clicked = () => Guard( () =>
			{
				if ( Session.Busy == 0 && list.Selected >= 0 && list.Selected < cards.Count )
					File.Delete( cards[list.Selected].Path );
				list.Selected = -1;
				Reload();
			} ),
			Bounds = new UiRect( 1020, 1160, 480, 110 ), Anchor = UiAnchor.Center } );
		var refreshTimer = 0f;
		screen.Updating += context =>
		{
			send.Enabled = Session.IsLoggedIn && cards.Count > 0 && Session.Busy == 0;
			delete.Enabled = list.Selected >= 0 && list.Selected < cards.Count && Session.Busy == 0;
			markSend.Enabled = markDelete.Enabled = list.Selected >= 0 && list.Selected < cards.Count;
			// Sending removes cards from the outbox folder; re-read it now and then.
			refreshTimer += context.Delta;
			if ( refreshTimer > 1 )
			{
				refreshTimer = 0;
				Reload();
			}
		};
		AddCloseButton( screen, 1889, 820 );
		AddStatus( screen, window );
		screen.Back = stack.Pop;
		Push( screen );
	}

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

	private void Push( UiScreen screen )
	{
		stack.Push( screen );
	}

	private UiTextField AddField( UiScreen screen, string id, Func<string> label, float x, float y, float width, int maximumLength )
	{
		screen.Add( new UiLabel { Id = id + "Label", Text = label, Bounds = new UiRect( x, y, width, 60 ), Anchor = UiAnchor.Center } );
		return screen.Add( new UiTextField { Id = id, MaximumLength = maximumLength, Model = "f_text1", Bounds = new UiRect( x, y + 64, width, 84 ), Anchor = UiAnchor.Center } );
	}

	private void AddStatus( UiScreen screen, UiRect window )
	{
		screen.Add( new UiLabel
		{
			Id = "status",
			Wrap = true,
			Font = fonts => fonts.Small,
			Color = UiColors.Value,
			Bounds = new UiRect( window.X + 120, window.Bottom - 175, window.Width - 700, 110 ),
			Anchor = UiAnchor.Center,
			Text = () => Session.Busy > 0 ? "..." : localStatus.Length > 0 ? localStatus : Session.Status
		} );
	}

	private void AddBack( UiScreen screen, UiRect window )
	{
		screen.Add( new UiButton { Id = "back", Model = "b_listpark", Text = () => strings.Extra( OpenTpwText.Back ), Clicked = stack.Pop,
			Bounds = new UiRect( window.Right - 540, window.Bottom - 175, 320, 110 ), Anchor = UiAnchor.Center } );
		screen.Back = stack.Pop;
	}

	/// <summary>
	/// The original dialogs' close button (-1/-2): a small button at the window's bottom right corner, at the authored
	/// place of the decoded dialog tables (e.g. data:0x50784 at (1889,820)-(1972,903)).
	/// </summary>
	private void AddCloseButton( UiScreen screen, float x, float y )
	{
		screen.Add( new UiButton { Id = "close", Model = "b_listpark", Text = () => strings.Extra( OpenTpwText.Back ), Clicked = stack.Pop,
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
