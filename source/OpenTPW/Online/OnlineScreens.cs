using OpenTPW.Online;
using OpenTPW.Online.Api;
using OpenTPW.Online.Client;
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
	/// <summary>The online globe behind the Online World screen; null where it is not shown (in a park).</summary>
	public Func<GlobeScene?> Globe { get; init; } = () => null;
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
	/// five chat satellites sit around the edges. OpenTPW adds the login and its own online menu at the top right.
	/// </summary>
	// [DATA:Mac UI table data:0x523f0] root 90111 worldlob (9,620,510,1531); buttons b_findprk 90115 (49,731,202,885) help 435, b_bck2lob 90112 (49,898,202,1051) help 431, b_zoom 90118 (41,1070,143,1173) help 428/429, b_lobup 90117 (182,1077,336,1231) help 426, b_lobleft 90114 (61,1201,214,1355) help 424, b_lobright 90116 (307,1201,460,1355) help 425, b_lobdown 90113 (184,1327,337,1481) help 427; i_sat 90120–90124 help 423
	public void ShowWorld()
	{
		var screen = new OnlineGlobeScreen( host.Globe );
		screen.Add( new UiModelImage { Id = "worldPanel", Model = "worldlob", Bounds = new UiRect( 9, 620, 501, 911 ), Anchor = UiAnchor.BottomLeft } );
		// [APPROX:ONLINE-011] f_wlobbg as the button background in the panel's second rectangle (33,1057,480,1505), like f_lobbutbg in the island lobby — evidence needed: the table field that names the panel's background model
		screen.Add( new UiModelImage { Id = "worldButtons", Model = "f_wlobbg", Bounds = new UiRect( 33, 1057, 447, 448 ), Anchor = UiAnchor.BottomLeft } );
		UiButton PanelButton( string id, string model, float x0, float y0, float x1, float y1, int help, Action clicked ) => screen.Add( new UiButton
		{
			Id = id, Model = model, Help = strings.Help( help ), Clicked = clicked,
			Bounds = new UiRect( x0, y0, x1 - x0, y1 - y0 ), Anchor = UiAnchor.BottomLeft
		} );
		var find = PanelButton( "findParks", "b_findprk", 49, 731, 202, 885, 435, ShowFindParks );
		PanelButton( "ownLobby", "b_bck2lob", 49, 898, 202, 1051, 431, stack.Pop );
		var zoom = PanelButton( "zoom", "b_zoom", 41, 1070, 143, 1173, 428, GlobeCameraMode.ToggleZoom );
		PanelButton( "rotateUp", "b_lobup", 182, 1077, 336, 1231, 426, () => GlobeCameraMode.Turn( 0, 1 ) );
		PanelButton( "rotateLeft", "b_lobleft", 61, 1201, 214, 1355, 424, () => GlobeCameraMode.Turn( -1, 0 ) );
		PanelButton( "rotateRight", "b_lobright", 307, 1201, 460, 1355, 425, () => GlobeCameraMode.Turn( 1, 0 ) );
		PanelButton( "rotateDown", "b_lobdown", 184, 1327, 337, 1481, 427, () => GlobeCameraMode.Turn( 0, -1 ) );
		// The chat satellites: chat-only parks around the globe; OpenTPW opens a chat room for each.
		var satellites = new (float X0, float Y0, float X1, float Y1, UiAnchor Anchor)[]
		{
			(131, 139, 438, 446, UiAnchor.TopLeft), (484, -51, 791, 256, UiAnchor.TopLeft), (1690, 344, 1997, 651, UiAnchor.TopRight),
			(1743, 620, 2050, 927, UiAnchor.Right), (1335, 1199, 1643, 1506, UiAnchor.BottomRight),
		};
		for ( var index = 0; index < satellites.Length; index++ )
		{
			var (x0, y0, x1, y1, anchor) = satellites[index];
			var room = $"satellite{index + 1}";
			var satellite = screen.Add( new UiButton { Id = room, Model = "i_sat", Help = strings.Help( 423 ), Bounds = new UiRect( x0, y0, x1 - x0, y1 - y0 ), Anchor = anchor,
				Clicked = () => Guard( () => { Session.JoinRoom( room ); ShowChat(); } ) } );
			screen.Updating += _ => satellite.Enabled = Session.IsLoggedIn && Session.Busy == 0;
		}
		// The hovered city's name and parks (the original's labels 0x16c11/0x16c12; their place is not decoded), else the title.
		// [APPROX:ONLINE-010] city name and park count drawn at the top centre — evidence needed: the original city labels' layout
		screen.Add( new UiLabel { Id = "city", Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center, Bounds = new UiRect( 524, 40, 1000, 90 ), Anchor = UiAnchor.Top,
			Text = () => screen.Hovered is { } city ? city.Name : OnlineStrings.Ui( OnlineStrings.OnlineWorld, "Online world" ) } );
		screen.Add( new UiLabel { Id = "cityParks", Font = fonts => fonts.Small, Align = UiAlign.Center, Bounds = new UiRect( 524, 130, 1000, 60 ), Anchor = UiAnchor.Top,
			Text = () => screen.Hovered is { } city ? OnlineStrings.Ui( OnlineStrings.CityParks, "Parks: " ) + city.Parks : "" } );
		// [EXT:ONLINE-UI] the login and OpenTPW's own online menu (news, postcards, chat, import, account)
		var login = screen.Add( new UiButton { Id = "login", Text = () => Session.IsLoggedIn ? OnlineStrings.Get( OnlineLabel.LogOut ) : OnlineStrings.Ui( OnlineStrings.OnlineLogin, "Online login" ),
			Clicked = () => { if ( Session.IsLoggedIn ) Guard( () => { Session.Disconnect(); localStatus = ""; } ); else ShowLogin(); },
			Bounds = new UiRect( 1560, 40, 440, 100 ), Anchor = UiAnchor.TopRight } );
		var menu = screen.Add( new UiButton { Id = "menu", Text = () => OnlineStrings.Get( OnlineLabel.OnlineMenu ), Clicked = ShowMenu,
			Bounds = new UiRect( 1560, 150, 440, 100 ), Anchor = UiAnchor.TopRight } );
		screen.Add( new UiLabel { Id = "status", Wrap = true, Font = fonts => fonts.Small, Color = UiColors.Value, Align = UiAlign.Center,
			Bounds = new UiRect( 574, 1400, 900, 100 ), Anchor = UiAnchor.Bottom, Text = () => localStatus.Length > 0 ? localStatus : Session.Status } );
		OnlineClient? citiesFor = null;
		screen.Updating += _ =>
		{
			find.Enabled = Session.IsLoggedIn && Session.Busy == 0;
			login.Enabled = menu.Enabled = Session.Busy == 0;
			zoom.Help = strings.Help( GlobeCameraMode.ZoomedIn ? 429 : 428 );
			// Each server's cities once, as soon as one is chosen (no login needed).
			if ( Session.Client is { } client && client != citiesFor && Session.Busy == 0 )
			{
				citiesFor = client;
				Guard( Session.FetchCities );
			}
		};
		screen.Back = stack.Pop;
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
			("publish", () => OnlineStrings.Ui( OnlineStrings.PublishPark, "Publish park" ), ShowPublish, () => level != null && !level.IsReadOnlyVisit),
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
		screen.Updating += _ => screen.Find( "mail" )!.Visible = Session.Inbox.Count > 0;
		AddStatus( screen, window );
		AddBack( screen, window );
		Push( screen );
	}

	// ---- News (UI-MAP "Online news panel") ------------------------------------------------------

	/// <summary>
	/// The server's Game News and System News, the two columns of the original news panel (UITEXT 267/268); the
	/// original headed it "News from ThemeParkWorld.com", so the window uses OpenTPW's own title.
	/// </summary>
	public void ShowNews()
	{
		var screen = new UiScreen( "onlineNews" );
		var window = UiDialogs.CenteredWindow( 1700, 1200 );
		UiDialogs.AddWindow( screen, window, "w_big", () => OnlineStrings.Get( OnlineLabel.News ) );
		var showSystem = false;
		screen.Add( new UiButton { Id = "gameNews", Text = () => OnlineStrings.Ui( OnlineStrings.GameNews, "Game News" ), Clicked = () => showSystem = false,
			Bounds = new UiRect( window.X + 100, window.Y + 170, 520, 110 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "systemNews", Text = () => OnlineStrings.Ui( OnlineStrings.SystemNews, "System News" ), Clicked = () => showSystem = true,
			Bounds = new UiRect( window.X + 660, window.Y + 170, 520, 110 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel
		{
			Id = "text",
			Wrap = true,
			Font = fonts => fonts.Small,
			Bounds = new UiRect( window.X + 100, window.Y + 320, window.Width - 280, 620 ),
			Anchor = UiAnchor.Center,
			Text = () => Session.News is { } news ? (showSystem ? news.System : news.Game) : ""
		} );
		AddStatus( screen, window );
		AddBack( screen, window );
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

	public void ShowLogin()
	{
		var screen = new UiScreen( "onlineLogin" );
		var window = UiDialogs.CenteredWindow( 1150, 1170 );
		UiDialogs.AddWindow( screen, window, "w_med", () => OnlineStrings.Ui( OnlineStrings.OnlineLogin, "Online login" ) );
		var x = window.X + 120;
		var width = window.Width - 340;
		// [EXT:ONLINE-UI] the server address is an OpenTPW field; the original service address was built in
		var server = AddField( screen, "server", () => OnlineStrings.Get( OnlineLabel.ServerAddress ), x, window.Y + 170, width, 256 );
		server.Text = Session.Settings.ServerUrl ?? OnlineSession.SuggestedServerUrl ?? "";
		// The original login name and password fields hold 16 characters (UI-MAP, 0x10184b48).
		var name = AddField( screen, "name", () => OnlineStrings.Ui( OnlineStrings.LoginName, "Login name" ), x, window.Y + 340, width, 16 );
		name.Text = Session.Settings.PlayerName ?? "";
		var password = AddField( screen, "password", () => OnlineStrings.Ui( OnlineStrings.Password, "Password" ), x, window.Y + 510, width, 16 );
		password.Password = true;
		// [EXT:online-two-step] the authenticator code is an OpenTPW field; accounts without two-step login leave it empty.
		var code = AddField( screen, "code", () => OnlineStrings.Get( OnlineLabel.AuthenticatorCode ), x, window.Y + 680, 420, Totp.Digits );
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
		// Enter moves from the name to the password and submits from there (UI-MAP, 0x10184924).
		server.Submitted = () => screen.Focus( name );
		name.Submitted = () => screen.Focus( password );
		password.Submitted = () => Submit( register: false );
		code.Submitted = () => Submit( register: false );
		var buttonY = window.Y + 860;
		screen.Add( new UiButton { Id = "login", Model = "b_login", Text = () => OnlineStrings.Get( OnlineLabel.LogIn ), Clicked = () => Submit( register: false ),
			Bounds = new UiRect( window.X + 120, buttonY, 400, 110 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "register", Text = () => OnlineStrings.Get( OnlineLabel.Register ), Clicked = () => Submit( register: true ),
			Bounds = new UiRect( window.X + 560, buttonY, 400, 110 ), Anchor = UiAnchor.Center } );
		// [EXT:online-email] recovery by e-mail; the server answers the same whether the account exists
		screen.Add( new UiButton { Id = "forgot", Text = () => OnlineStrings.Get( OnlineLabel.ForgotPassword ),
			Clicked = () => Guard( () => { Session.UseServer( server.Text.Trim(), name.Text.Trim() ); ShowRecovery( name.Text.Trim() ); } ),
			Bounds = new UiRect( window.X + 580, window.Y + 735, 400, 90 ), Anchor = UiAnchor.Center } );
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
		AddBack( screen, window );
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

	public void ShowFindParks()
	{
		var screen = new UiScreen( "findParks" );
		var window = UiDialogs.CenteredWindow( 1900, 1300 );
		UiDialogs.AddWindow( screen, window, "w_big", () => OnlineStrings.Ui( OnlineStrings.FindParks, "Find parks" ) );
		var search = AddField( screen, "search", () => OnlineStrings.Ui( OnlineStrings.SearchName, "Search name" ), window.X + 100, window.Y + 160, 760, 64 );
		search.Submitted = () => Guard( () => Session.RefreshParks( search.Text ) );
		screen.Add( new UiButton { Id = "refresh", Text = () => OnlineStrings.Get( OnlineLabel.Refresh ), Clicked = () => Guard( () => Session.RefreshParks( search.Text ) ),
			Bounds = new UiRect( window.X + 900, window.Y + 225, 380, 104 ), Anchor = UiAnchor.Center } );
		var list = screen.Add( new UiScrollList
		{
			Id = "parks",
			Model = "list_findprks",
			Bounds = new UiRect( window.X + 100, window.Y + 360, 1100, 680 ),
			Anchor = UiAnchor.Center,
			Rows = () => Session.Parks.Count == 0
				? new[] { OnlineStrings.Ui( OnlineStrings.NoSearchResults, "No parks found" ) }
				: Session.Parks.Select( park => $"{park.Name} - {park.Author}" ).ToArray(),
		} );
		ParkSummary? Selected() => list.Selected >= 0 && list.Selected < Session.Parks.Count ? Session.Parks[list.Selected] : null;
		// Park details (UI-MAP "Park details dialog"): creator, visits and votes beside the list.
		screen.Add( new UiLabel
		{
			Id = "details",
			Wrap = true,
			Font = fonts => fonts.Small,
			Bounds = new UiRect( window.X + 1240, window.Y + 360, window.Width - 1400, 520 ),
			Anchor = UiAnchor.Center,
			Text = () => Selected() is { } park
				? $"{park.Name}\n{OnlineStrings.Ui( OnlineStrings.CreatedBy, "Created by" )} {park.Author}\n{OnlineStrings.Theme( park.Level )}\n" +
				  $"{OnlineStrings.Ui( OnlineStrings.NumberOfVisits, "Visits" )} {park.Visits}\n{OnlineStrings.Ui( OnlineStrings.NumberOfVotes, "Votes" )} {park.Votes}\n" +
				  (park.VisitedBefore ? OnlineStrings.Ui( OnlineStrings.VisitedBefore, "Visited before" ) + "\n" : "") +
				  (park.VotedFor ? OnlineStrings.Ui( OnlineStrings.VotedFor, "Voted for" ) + "\n" : "") + park.Description
				: ""
		} );
		var visit = screen.Add( new UiButton { Id = "visit", Text = () => OnlineStrings.Get( OnlineLabel.VisitReadOnly ),
			Clicked = () => { if ( Selected() is { } park ) Guard( () => Session.Download( park, path => Inspect( path, start: true ) ) ); },
			Font = fonts => fonts.Label, Bounds = new UiRect( window.X + 1240, window.Y + 900, 560, 104 ), Anchor = UiAnchor.Center } );
		var vote = screen.Add( new UiButton { Id = "vote", Model = "b_vote", Text = () => OnlineStrings.Get( OnlineLabel.Vote ),
			Clicked = () => { if ( Selected() is { } park ) Guard( () => Session.Vote( park ) ); },
			Bounds = new UiRect( window.X + 1240, window.Y + 1020, 230, 104 ), Anchor = UiAnchor.Center } );
		var chat = screen.Add( new UiButton { Id = "parkChat", Text = () => OnlineStrings.Get( OnlineLabel.Chat ),
			Clicked = () => { if ( Selected() is { } park ) Guard( () => { Session.JoinRoom( OpenTPW.Online.Chat.ChatProtocol.ParkRoom( park.Id ) ); ShowChat(); } ); },
			Bounds = new UiRect( window.X + 1490, window.Y + 1020, 230, 104 ), Anchor = UiAnchor.Center } );
		list.RowActivated = _ => visit.Activate();
		screen.Updating += _ =>
		{
			var ready = Selected() != null && Session.Busy == 0;
			visit.Enabled = ready && host.Visit != null;
			vote.Enabled = ready && Selected()?.VotedFor == false;
			chat.Enabled = ready && Session.Chat != null;
		};
		AddStatus( screen, window );
		AddBack( screen, window );
		if ( Session.IsLoggedIn )
			Guard( () => Session.RefreshParks( null ) );
		screen.Focus( search );
		Push( screen );
	}

	// ---- Publish Park (UI-MAP "Publish Park dialog") -----------------------------------------

	public void ShowPublish()
	{
		var level = host.Level();
		if ( level == null )
			return;
		var screen = new UiScreen( "publishPark" );
		var window = UiDialogs.CenteredWindow( 1300, 1000 );
		UiDialogs.AddWindow( screen, window, "w_med", () => OnlineStrings.Ui( OnlineStrings.PublishPark, "Publish park" ) );
		var name = AddField( screen, "parkName", () => OnlineStrings.Ui( OnlineStrings.ParkName, "Park name" ), window.X + 120, window.Y + 170, window.Width - 340, 64 );
		name.Text = "My park";
		var description = AddField( screen, "description", () => OnlineStrings.Ui( OnlineStrings.Description, "Description" ), window.X + 120, window.Y + 340, window.Width - 340, 400 );
		// [EXT:ONLINE-067] off unless the player chooses it: the website list makes the park and the player name public on the web.
		var showOnWebsite = false;
		screen.Add( new UiButton { Id = "website", Text = () => OnlineStrings.Get( showOnWebsite ? OnlineLabel.ShownOnWebsite : OnlineLabel.NotShownOnWebsite ),
			Clicked = () => showOnWebsite = !showOnWebsite, Bounds = new UiRect( window.X + 120, window.Y + 510, window.Width - 340, 100 ), Anchor = UiAnchor.Center } );
		var publish = screen.Add( new UiButton { Id = "publish", Text = () => OnlineStrings.Ui( OnlineStrings.PublishPark, "Publish park" ),
			Clicked = () => Guard( () => Session.Publish( ParkSharing.ExportLevel( level, name.Text, Session.Settings.PlayerName ?? "", description.Text ), showOnWebsite ) ),
			Bounds = new UiRect( window.X + 120, window.Bottom - 360, 480, 110 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "export", Text = () => OnlineStrings.Get( OnlineLabel.ExportPark ),
			Clicked = () => Guard( () =>
			{
				var package = ParkSharing.ExportLevel( level, name.Text, Session.Settings.PlayerName ?? "", description.Text );
				var path = Path.Combine( Session.Folders.Parks, package.Manifest.PackageId.ToString( "N" ) + ParkPackage.FileExtension );
				package.Save( path );
				localStatus = OnlineStrings.Get( OnlineLabel.SavedTo ) + path;
			} ),
			Bounds = new UiRect( window.X + 640, window.Bottom - 360, 480, 110 ), Anchor = UiAnchor.Center } );
		screen.Updating += _ => publish.Enabled = Session.IsLoggedIn && Session.Busy == 0;
		AddStatus( screen, window );
		AddBack( screen, window );
		screen.Focus( name );
		Push( screen );
	}

	// ---- Postcards (UI-MAP "Send Postcard dialog", "Unsent Postcards screen") ------------------

	public void ShowSendPostcard()
	{
		var screen = new UiScreen( "sendPostcard" );
		var window = UiDialogs.CenteredWindow( 1300, 1120 );
		UiDialogs.AddWindow( screen, window, "w_med", () => OnlineStrings.Ui( OnlineStrings.SendPostcard, "Send postcard" ) );
		var x = window.X + 120;
		var width = window.Width - 340;
		var to = AddField( screen, "to", () => OnlineStrings.Get( OnlineLabel.To ), x, window.Y + 170, width, 256 );
		var title = AddField( screen, "title", () => OnlineStrings.Ui( OnlineStrings.MessageTitle, "Title" ), x, window.Y + 340, width, 64 );
		var message = AddField( screen, "message", () => OnlineStrings.Ui( OnlineStrings.MessageText, "Message" ), x, window.Y + 510, width, 1000 );
		screen.Add( new UiButton { Id = "outbox", Text = () => OnlineStrings.Get( OnlineLabel.PutInOutbox ),
			Clicked = () => Guard( () =>
			{
				var card = Postcard.Create( Session.Settings.PlayerName ?? "", to.Text.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ),
					title.Text, message.Text, GameLanguage.IsSelected ? GameLanguage.Current.Name : GameLanguage.DefaultLanguage );
				localStatus = OnlineStrings.Get( OnlineLabel.SavedTo ) + Session.Folders.Store( Session.Folders.Outbox, card );
				title.Text = message.Text = "";
			} ),
			Bounds = new UiRect( x, window.Bottom - 360, 560, 110 ), Anchor = UiAnchor.Center } );
		AddStatus( screen, window );
		AddBack( screen, window );
		screen.Focus( to );
		Push( screen );
	}

	public void ShowOutbox()
	{
		var screen = new UiScreen( "outbox" );
		var window = UiDialogs.CenteredWindow( 1500, 1200 );
		UiDialogs.AddWindow( screen, window, "w_big", () => OnlineStrings.Ui( OnlineStrings.UnsentPostcards, "Unsent postcards" ) );
		IReadOnlyList<(string Path, Postcard? Card, string? Error)> cards = Array.Empty<(string, Postcard?, string?)>();
		void Reload() => cards = Session.Folders.List( Session.Folders.Outbox );
		Reload();
		var list = screen.Add( new UiScrollList
		{
			Id = "cards",
			Model = "list_outbox",
			Bounds = new UiRect( window.X + 100, window.Y + 170, window.Width - 300, 700 ),
			Anchor = UiAnchor.Center,
			Rows = () => cards.Select( entry => entry.Card is { } card ? $"{string.Join( ", ", card.Manifest.To )}: {card.Manifest.Title}" : entry.Error ?? "?" ).ToArray(),
		} );
		var send = screen.Add( new UiButton { Id = "sendAll", Model = "b_sendall", Text = () => OnlineStrings.Ui( OnlineStrings.Send, "Send" ),
			Clicked = () => Guard( () => { Session.SendOutbox(); localStatus = OnlineStrings.Ui( OnlineStrings.SendingOutbox, "Sending" ); } ),
			Bounds = new UiRect( window.X + 100, window.Y + 910, 420, 110 ), Anchor = UiAnchor.Center } );
		var delete = screen.Add( new UiButton { Id = "delete", Text = () => OnlineStrings.Ui( OnlineStrings.Delete, "Delete" ),
			Clicked = () => Guard( () =>
			{
				if ( Session.Busy == 0 && list.Selected >= 0 && list.Selected < cards.Count )
					File.Delete( cards[list.Selected].Path );
				list.Selected = -1;
				Reload();
			} ),
			Bounds = new UiRect( window.X + 560, window.Y + 910, 420, 110 ), Anchor = UiAnchor.Center } );
		var refreshTimer = 0f;
		screen.Updating += context =>
		{
			send.Enabled = Session.IsLoggedIn && cards.Count > 0 && Session.Busy == 0;
			delete.Enabled = list.Selected >= 0 && list.Selected < cards.Count && Session.Busy == 0;
			// Sending removes cards from the outbox folder; re-read it now and then.
			refreshTimer += context.Delta;
			if ( refreshTimer > 1 )
			{
				refreshTimer = 0;
				Reload();
			}
		};
		AddStatus( screen, window );
		AddBack( screen, window );
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

	// ---- Chat (f_chat with the list_msgs message list) ----------------------------------------

	public void ShowChat()
	{
		if ( Session.Chat == null && Session.IsLoggedIn )
			Guard( Session.ConnectChat );
		var screen = new UiScreen( "chat" );
		var window = UiDialogs.CenteredWindow( 1800, 1400 );
		UiDialogs.AddWindow( screen, window, "w_big", () => OnlineStrings.Get( OnlineLabel.Chat ) );
		screen.Add( new UiScrollList
		{
			Id = "messages",
			Model = "list_msgs",
			Bounds = new UiRect( window.X + 100, window.Y + 170, window.Width - 300, 780 ),
			Anchor = UiAnchor.Center,
			RowHeight = 40,
			FollowEnd = true,
			Rows = () => Session.ChatLines,
		} );
		var line = AddField( screen, "line", () => "", window.X + 100, window.Y + 1000, window.Width - 760, 400 );
		void Send()
		{
			if ( line.Text.Trim().Length == 0 )
				return;
			Guard( () => Session.SendChatLine( line.Text ) );
			line.Text = "";
		}
		line.Submitted = Send;
		var send = screen.Add( new UiButton { Id = "send", Text = () => OnlineStrings.Ui( OnlineStrings.Send, "Send" ), Clicked = Send,
			Bounds = new UiRect( window.Right - 600, window.Y + 1060, 380, 104 ), Anchor = UiAnchor.Center } );
		screen.Updating += _ => send.Enabled = line.Enabled = Session.Chat != null;
		AddStatus( screen, window );
		AddBack( screen, window );
		screen.Focus( line );
		Push( screen );
	}

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

	private void Inspect( string path, bool start )
	{
		inspected = null;
		Guard( () =>
		{
			inspected = ParkSharing.PrepareVisit( ParkPackage.Load( path ) );
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
