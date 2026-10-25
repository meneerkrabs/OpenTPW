using ImGuiNET;
using OpenTPW.Online.Packages;

namespace OpenTPW.UI;

/// <summary>[EXT:ONLINE-066] Opt-in ImGui panel; original online panel layout is not reproduced.</summary>
internal sealed class OnlinePanel : IDisposable
{
	private readonly Level level;
	private readonly OnlineSession session;
	private readonly ChatOverlay overlay;
	private string server = "", name = "", password = "", code = "", email = "", emailCode = "", parkName = "My park", search = "", importPath = "", chatLine = "";
	private string recipients = "", title = "", message = "", localStatus = "";
	/// <summary>The server the park list was last asked of and the session's fetch count then: an empty list is a result once a fetch from that server has finished.</summary>
	private (OpenTPW.Online.Client.OnlineClient Client, int Fetched)? listedAt;
	private ParkVisitInfo? inspected;

	public OnlinePanel( Level level, OnlineFolders folders )
	{
		this.level = level;
		session = new OnlineSession( folders );
		server = session.Settings.ServerUrl ?? "";
		name = session.Settings.PlayerName ?? "";
		overlay = new ChatOverlay( session );
		global::Global.Render.OnOverlayRender += overlay.Draw;
	}

	public void Draw()
	{
		session.Pump();
		ImGui.SetNextWindowSize( new System.Numerics.Vector2( 420, 500 ), ImGuiCond.FirstUseEver );
		if ( ImGui.Begin( "OpenTPW Online (extension)" ) )
		{
			ImGui.TextWrapped( "OpenTPW sharing and chat extension. Economy, live multiplayer and the original online service are not reproduced." );
			if ( level.Visit != null )
				ImGui.TextWrapped( ParkSharing.Describe( level.Visit ) );
			if ( session.Busy > 0 )
				ImGui.Text( "Working…" );
			ImGui.BeginDisabled( session.Busy > 0 );
			try
			{
				DrawConnection();
				DrawSharing();
				if ( session.IsLoggedIn )
					DrawParks();
				DrawPostcards();
				DrawChat();
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or ArgumentException or InvalidOperationException )
			{
				localStatus = exception.Message;
			}
			ImGui.EndDisabled();
			if ( localStatus.Length > 0 )
				ImGui.TextWrapped( localStatus );
			if ( session.Status.Length > 0 )
				ImGui.TextWrapped( session.Status );
		}
		ImGui.End();
	}

	private void DrawConnection()
	{
		if ( !ImGui.CollapsingHeader( OnlineStrings.Ui( OnlineStrings.OnlineLogin, "Online login" ) ) )
			return;
		ImGui.InputText( OnlineStrings.Get( OnlineLabel.ServerAddress ), ref server, 1024 );
		ImGui.InputText( OnlineStrings.Ui( OnlineStrings.LoginName, "Login name" ), ref name, 64 );
		ImGui.InputText( OnlineStrings.Ui( OnlineStrings.Password, "Password" ), ref password, 256, ImGuiInputTextFlags.Password );
		ImGui.InputText( OnlineStrings.Get( OnlineLabel.AuthenticatorCode ), ref code, 8 );
		if ( ImGui.Button( OnlineStrings.Ui( OnlineStrings.GoOnline, "Go online" ) ) )
			session.UseServer( server, name );
		if ( session.Client == null )
			ImGui.TextWrapped( OnlineStrings.Get( OnlineLabel.OnlineOff ) );
		else if ( session.Enrollment is { } enrollment )
		{
			// Two-step setup: the key to type into an authenticator app (the game's own screen also shows a QR code).
			ImGui.TextWrapped( OnlineStrings.Get( OnlineLabel.TwoStepInstructions ) );
			ImGui.TextUnformatted( OpenTPW.Online.Totp.Grouped( enrollment.Secret ) );
			if ( enrollment.EmailRequired == true )
			{
				ImGui.InputText( OnlineStrings.Get( OnlineLabel.EmailAddress ), ref email, 254 );
				if ( ImGui.Button( OnlineStrings.Get( OnlineLabel.SendCode ) ) )
					session.SendEnrollmentEmail( email );
				ImGui.InputText( OnlineStrings.Get( OnlineLabel.EmailCode ), ref emailCode, 8 );
			}
			if ( ImGui.Button( OnlineStrings.Get( OnlineLabel.Confirm ) ) )
			{
				session.ConfirmEnrollment( code.Trim(), emailCode.Trim() );
				code = "";
			}
		}
		else if ( !session.IsLoggedIn )
		{
			if ( ImGui.Button( OnlineStrings.Get( OnlineLabel.LogIn ) ) )
			{
				// The password stays until logged in, so a login that asks for a code only needs the code.
				session.Login( name, password, code.Trim().Length == 0 ? null : code.Trim() );
				code = "";
			}
			ImGui.SameLine();
			if ( ImGui.Button( OnlineStrings.Get( OnlineLabel.Register ) ) )
			{
				session.Register( name, password );
				password = "";
			}
		}
		if ( session.IsLoggedIn )
			password = "";
		if ( session.Client != null && ImGui.Button( OnlineStrings.Ui( OnlineStrings.GoOffline, "Go offline" ) ) )
		{
			session.Disconnect();
			password = "";
		}
	}

	private void DrawSharing()
	{
		if ( !ImGui.CollapsingHeader( OnlineStrings.Get( OnlineLabel.FileSharing ) ) )
			return;
		ImGui.InputText( OnlineStrings.Ui( OnlineStrings.ParkName, "Park name" ), ref parkName, 128 );
		if ( !level.IsReadOnlyVisit && !level.IsCoopGuestPark && ParkSharing.ModsRefusal( level ) == null && ImGui.Button( OnlineStrings.Get( OnlineLabel.ExportPark ) ) )
		{
			var package = ParkSharing.ExportLevel( level, parkName, name );
			var path = Path.Combine( session.Folders.Parks, package.Manifest.PackageId.ToString( "N" ) + ParkPackage.FileExtension );
			package.Save( path );
			localStatus = OnlineStrings.Get( OnlineLabel.SavedTo ) + path;
		}
		if ( !level.IsReadOnlyVisit && !level.IsCoopGuestPark && ParkSharing.ModsRefusal( level ) == null && session.IsLoggedIn && ImGui.Button( OnlineStrings.Ui( OnlineStrings.PublishPark, "Publish park" ) ) )
			session.Publish( ParkSharing.ExportLevel( level, parkName, name ) );
		ImGui.InputText( "Local .tpwpark path", ref importPath, 2048 );
		if ( ImGui.Button( OnlineStrings.Get( OnlineLabel.ImportFile ) ) )
			Inspect( importPath );
		if ( inspected != null )
		{
			ImGui.TextWrapped( ParkSharing.Describe( inspected ) );
			ImGui.TextWrapped( "To visit read-only, restart OpenTPW with --visit-park followed by this local file path. Review missing content and layout warnings first." );
		}
	}

	private void Inspect( string path )
	{
		importPath = path;
		inspected = ParkSharing.PrepareVisit( ParkPackage.Load( path ) );
	}

	private void DrawParks()
	{
		if ( !ImGui.CollapsingHeader( OnlineStrings.Ui( OnlineStrings.ListParks, "List parks" ) ) )
			return;
		ImGui.InputText( OnlineStrings.Ui( OnlineStrings.SearchName, "Search name" ), ref search, 128 );
		if ( ImGui.Button( OnlineStrings.Get( OnlineLabel.Refresh ) ) )
		{
			session.RefreshParks( search );
			listedAt = session.Client is { } client ? (client, session.ParksFetched) : null;
		}
		// the park list's fetch ends its progress text, as the Find Parks screen shows an empty result as its row: this list says so itself once a fetch from this server
		// has finished (a failed fetch leaves the count, and its error stands as the status)
		if ( listedAt is { } at && ReferenceEquals( at.Client, session.Client ) && session.ParksFetched != at.Fetched && session.Parks.Count == 0 )
			ImGui.TextWrapped( OnlineStrings.Ui( OnlineStrings.NoSearchResults, "No search results" ) );
		foreach ( var park in session.Parks )
		{
			ImGui.PushID( park.Id );
			ImGui.TextWrapped( $"{park.Name} — {park.Author} ({OnlineStrings.Theme( park.Level )}); visits {park.Visits}, votes {park.Votes}" );
			if ( ImGui.Button( OnlineStrings.Get( OnlineLabel.VisitReadOnly ) ) )
				session.Download( park, Inspect );
			ImGui.SameLine();
			if ( ImGui.Button( OnlineStrings.Get( OnlineLabel.Vote ) ) )
				session.Vote( park );
			ImGui.SameLine();
			if ( ImGui.Button( OnlineStrings.Get( OnlineLabel.Chat ) ) )
				session.JoinRoom( OpenTPW.Online.Chat.ChatProtocol.ParkRoom( park.Id ) );
			ImGui.PopID();
		}
	}

	private void DrawPostcards()
	{
		if ( !ImGui.CollapsingHeader( OnlineStrings.Ui( OnlineStrings.SendPostcard, "Send postcard" ) ) )
			return;
		ImGui.InputText( OnlineStrings.Get( OnlineLabel.To ), ref recipients, 256 );
		ImGui.InputText( OnlineStrings.Ui( OnlineStrings.MessageTitle, "Title" ), ref title, 256 );
		ImGui.InputTextMultiline( OnlineStrings.Ui( OnlineStrings.MessageText, "Message" ), ref message, 4096, new System.Numerics.Vector2( 0, 80 ) );
		if ( ImGui.Button( OnlineStrings.Get( OnlineLabel.PutInOutbox ) ) )
		{
			var card = Postcard.Create( name, recipients.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ), title, message,
				GameLanguage.IsSelected ? GameLanguage.Current.Name : GameLanguage.DefaultLanguage );
			localStatus = OnlineStrings.Get( OnlineLabel.SavedTo ) + session.Folders.Store( session.Folders.Outbox, card );
		}
		// the outbox's send ends with no count (tp.exe 0x4bc660), as the Unsent Postcards window shows: here the number of cards still waiting confirms it
		ImGui.TextWrapped( $"{OnlineStrings.Ui( OnlineStrings.UnsentPostcards, "Unsent postcards" )}: {session.Folders.List( session.Folders.Outbox ).Count( entry => entry.Card != null )}" );
		if ( session.IsLoggedIn )
		{
			if ( ImGui.Button( OnlineStrings.Ui( OnlineStrings.SendingOutbox, "Send outbox" ) ) )
				session.SendOutbox();
			if ( ImGui.Button( OnlineStrings.Get( OnlineLabel.Inbox ) ) )
				session.FetchInbox();
		}
		foreach ( var (_, card, error) in session.Folders.List( session.Folders.Inbox ) )
			ImGui.TextWrapped( card == null ? error ?? "Unreadable postcard" : $"{card.Manifest.From}: {card.Manifest.Title}\n{card.Manifest.Text}" );
	}

	private void DrawChat()
	{
		if ( !ImGui.CollapsingHeader( OnlineStrings.Get( OnlineLabel.Chat ) ) )
			return;
		if ( session.IsLoggedIn && session.Chat == null && ImGui.Button( OnlineStrings.Get( OnlineLabel.Chat ) + "##connect" ) )
			session.ConnectChat();
		if ( session.Chat != null )
		{
			ImGui.InputText( "##chatline", ref chatLine, 1024 );
			if ( ImGui.Button( OnlineStrings.Ui( OnlineStrings.Send, "Send" ) + "##chat" ) )
			{
				session.SendChatLine( chatLine );
				chatLine = "";
			}
		}
	}

	public void Dispose()
	{
		global::Global.Render.OnOverlayRender -= overlay.Draw;
		overlay.Dispose();
		session.Dispose();
	}
}
