using OpenTPW.Online.Api;
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

	public void ShowWorld()
	{
		var screen = new UiScreen( "onlineWorld" );
		var window = UiDialogs.CenteredWindow( 1200, 1460 );
		UiDialogs.AddWindow( screen, window, "w_big", () => OnlineStrings.Ui( OnlineStrings.OnlineWorld, "Online world" ) );
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
		var window = UiDialogs.CenteredWindow( 1150, 1000 );
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
		void Submit( bool register )
		{
			try
			{
				Session.UseServer( server.Text.Trim(), name.Text.Trim() );
				if ( register )
					Session.Register( name.Text.Trim(), password.Text );
				else
					Session.Login( name.Text.Trim(), password.Text );
				localStatus = "";
			}
			catch ( Exception exception ) when ( exception is ArgumentException or InvalidOperationException or UriFormatException or InvalidDataException )
			{
				localStatus = exception.Message;
			}
			password.Text = "";
		}
		// Enter moves from the name to the password and submits from there (UI-MAP, 0x10184924).
		server.Submitted = () => screen.Focus( name );
		name.Submitted = () => screen.Focus( password );
		password.Submitted = () => Submit( register: false );
		var buttonY = window.Y + 690;
		screen.Add( new UiButton { Id = "login", Model = "b_login", Text = () => OnlineStrings.Get( OnlineLabel.LogIn ), Clicked = () => Submit( register: false ),
			Bounds = new UiRect( window.X + 120, buttonY, 400, 110 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "register", Text = () => OnlineStrings.Get( OnlineLabel.Register ), Clicked = () => Submit( register: true ),
			Bounds = new UiRect( window.X + 560, buttonY, 400, 110 ), Anchor = UiAnchor.Center } );
		// Closes itself once the login succeeded.
		screen.Updating += _ =>
		{
			if ( Session.IsLoggedIn && stack.Top == screen )
				stack.Pop();
		};
		AddStatus( screen, window );
		AddBack( screen, window );
		screen.Focus( name.Text.Length == 0 ? name : password );
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
		var publish = screen.Add( new UiButton { Id = "publish", Text = () => OnlineStrings.Ui( OnlineStrings.PublishPark, "Publish park" ),
			Clicked = () => Guard( () => Session.Publish( ParkSharing.ExportLevel( level, name.Text, Session.Settings.PlayerName ?? "", description.Text ) ) ),
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
