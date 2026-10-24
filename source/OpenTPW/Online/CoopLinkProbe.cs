using System.Security.Cryptography;
using System.Text.Json.Nodes;
using OpenTPW.Online.Coop;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-session] One side of the co-op link's transport check (C2c-2b-5; docs/COOP.md, "The browser's link to a real server"): an online session
/// of its own, in a folder of its own (never the player's), logs in to a server and opens the co-op link, then does what its driver asks, a request at a time,
/// and records what the link raises: its welcome, the session, its members, the frames and messages it receives, invitations (C2d-3b), refusals, the session's end
/// and the snapshot transfers, every payload by its length and SHA-256, and the open list it asks for. The driver checks those records against the bytes it chose. A page runs it in the browser over the
/// runtime's own <c>ClientWebSocket</c> and <c>HttpClient</c> (tools/web-coop-link.mjs runs a host page and a guest page against the real server);
/// <c>CoopLinkProbeTests</c> runs two probes against the loopback server. No game, park or game file is involved, and nothing here sees a token.
/// </summary>
internal sealed class CoopLinkProbe : IDisposable
{
	/// <summary>The records kept at most: a check needs a few dozen, so more means a link that keeps changing state.</summary>
	internal const int MaximumEvents = 500;

	private readonly OnlineSession session;
	private readonly List<JsonObject> events = new();
	private bool loginAsked;
	private bool opened;
	private bool welcomed;
	private bool disposed;

	/// <summary>Logs in to <paramref name="server"/> as <paramref name="name"/> with a session whose files live in <paramref name="folder"/>; <see cref="Step"/> carries it on.</summary>
	internal CoopLinkProbe( string folder, string server, string name, string password )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( folder );
		ArgumentException.ThrowIfNullOrWhiteSpace( server );
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		ArgumentNullException.ThrowIfNull( password );
		Directory.CreateDirectory( folder );
		session = new OnlineSession( new OnlineFolders( folder ) );
		var link = session.Coop;
		link.StateChanged += state =>
		{
			Record( "state", new JsonObject { ["state"] = state.ToString(), ["stopped"] = link.Stopped.ToString() } );
			if ( state == CoopLinkState.Open && !welcomed )
			{
				welcomed = true;
				Record( "welcome", new JsonObject { ["protocol"] = link.ServerProtocol } );
			}
			if ( link.Stopped != CoopStop.None )
				Fail( $"The co-op link stopped: {link.Stopped}." );
		};
		link.SessionStarted += state => Record( "session", Describe( state ) );
		link.Resumed += state => Fail( "The co-op link lost its socket and resumed the session." );
		link.MemberChanged += change => Record( "member", new JsonObject
		{
			["id"] = change.Member.Id,
			["slot"] = change.Member.Slot,
			["name"] = change.Member.Name,
			["state"] = change.State.ToString(),
		} );
		link.FrameReceived += frame => Record( "frame", Payload( frame.Payload.Span, new JsonObject { ["seq"] = frame.Seq } ) );
		link.MessageReceived += message => Record( "message", Payload( message.Payload.Span, new JsonObject { ["from"] = message.From } ) );
		link.FrameRefused += refused => Fail( $"The server refused a frame: it expected frame {refused.Expected}." );
		link.Invited += invited => Record( "invited", new JsonObject
		{
			["session"] = invited.SessionId,
			["code"] = invited.Code,
			["title"] = invited.Title,
			["host"] = invited.Host,
		} );
		link.Refused += error => Fail( $"The server refused the {error.Request} request ({error.Reason}): {error.Text}" );
		link.SessionEnded += ended =>
		{
			Record( "ended", new JsonObject { ["session"] = ended.SessionId, ["reason"] = ended.Reason.ToString(), ["text"] = ended.Text } );
			SessionEnded = true;
		};
		session.UseServer( server, name );
		session.Login( name, password );
		loginAsked = true;
	}

	/// <summary>Why the probe failed (the first failure; the records hold the rest), or null.</summary>
	internal string? Failure { get; private set; }

	/// <summary>Whether the link raised the end of a session (after this game's own leave, its host's, or the server's).</summary>
	internal bool SessionEnded { get; private set; }

	internal CoopLink Link => session.Coop;

	/// <summary>
	/// Where the probe stands: <c>login</c> while it logs in, <c>opening</c> until the server welcomes the link, <c>open</c> after, <c>closed</c> once
	/// disposed, <c>failed</c> after a failure.
	/// </summary>
	internal string Phase => Failure != null ? "failed" : disposed ? "closed" : !opened ? "login" : !welcomed ? "opening" : "open";

	/// <summary>
	/// Carries the probe on: what the session's background work posted runs here (a page runs no game loop that would pump it), and once logged in the
	/// link opens. A login that ended without a session fails the probe.
	/// </summary>
	internal void Step()
	{
		if ( disposed )
			return;
		session.Pump();
		if ( Failure != null || opened || !loginAsked )
			return;
		if ( session.IsLoggedIn )
		{
			opened = true;
			Record( "login", new JsonObject { ["name"] = session.Client?.Session?.Name } );
			Link.Open();
		}
		else if ( session.Busy == 0 )
			Fail( $"The login failed: {session.Status}" );
	}

	/// <summary>Creates a session this probe hosts, for two players: on the open list when <paramref name="listed"/>.</summary>
	internal bool Create( string title, bool listed = false ) => Asked( "create", Link.Create( listed, title, 2 ), new JsonObject { ["listed"] = listed } );

	/// <summary>[EXT:online-coop-session] The host invites <paramref name="player"/>, whose link must be open (C2d-3b).</summary>
	internal bool Invite( string player ) => Asked( "invite", Link.Invite( player ), new JsonObject { ["player"] = player } );

	/// <summary>[EXT:online-coop-session] Asks for the open list over HTTP (C2d-3b): its rows are recorded (<c>listed</c>), or the failure fails the probe.</summary>
	internal bool List()
	{
		Asked( "list", true );
		Link.List( ( sessions, error ) =>
		{
			if ( sessions == null )
			{
				Fail( $"The open list failed: {error}" );
				return;
			}
			var rows = new JsonArray();
			foreach ( var session in sessions )
				rows.Add( new JsonObject
				{
					["code"] = session.Code,
					["title"] = session.Title,
					["host"] = session.Host,
					["players"] = session.Players,
					["maximum"] = session.MaximumPlayers,
				} );
			Record( "listed", new JsonObject { ["sessions"] = rows } );
		} );
		return true;
	}

	/// <summary>Joins the session with <paramref name="code"/>.</summary>
	internal bool Join( string code ) => Asked( "join", Link.Join( code ) );

	/// <summary>The host's frame <paramref name="seq"/> with <paramref name="payload"/>, recorded by its length and SHA-256.</summary>
	internal bool SendFrame( ulong seq, byte[] payload ) =>
		Asked( "frame-sent", Link.SendFrame( seq, payload ), Payload( payload, new JsonObject { ["seq"] = seq } ) );

	/// <summary>A message with <paramref name="payload"/> to member <paramref name="to"/>, recorded by its length and SHA-256.</summary>
	internal bool Send( uint to, byte[] payload ) => Asked( "message-sent", Link.Send( to, payload ), Payload( payload, new JsonObject { ["to"] = to } ) );

	/// <summary>Leaves the session (a host's leave ends it for everyone).</summary>
	internal bool Leave( string reason ) => Asked( "leave", Link.Leave( reason ) );

	/// <summary>
	/// The host stores <paramref name="payload"/> as the session's snapshot over the link's own upload: recorded with the SHA-256 the server answered and the
	/// payload's own, or the failure.
	/// </summary>
	internal bool Upload( byte[] payload )
	{
		ArgumentNullException.ThrowIfNull( payload );
		if ( !Asked( "upload", Link.Session != null, Payload( payload, new JsonObject() ) ) )
			return false;
		((ICoopStreamTransport)Link).UploadSnapshot( payload, done =>
		{
			if ( done.Succeeded )
				Record( "uploaded", new JsonObject { ["sha256"] = done.Sha256 } );
			else
				Fail( $"The snapshot upload failed: {done.Failure}" );
		} );
		return true;
	}

	/// <summary>
	/// The guest downloads the session's snapshot <paramref name="sha256"/> of <paramref name="bytes"/> bytes over the link's own download: recorded by the
	/// downloaded bytes' length and SHA-256, or the failure.
	/// </summary>
	internal bool Download( string sha256, long bytes )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( sha256 );
		if ( !Asked( "download", Link.Session != null, new JsonObject { ["sha256"] = sha256, ["length"] = bytes } ) )
			return false;
		((ICoopStreamTransport)Link).DownloadSnapshot( sha256, bytes, done =>
		{
			if ( done.Succeeded && done.Payload is { } payload )
				Record( "downloaded", Payload( payload, new JsonObject() ) );
			else
				Fail( $"The snapshot download failed: {done.Failure}" );
		} );
		return true;
	}

	/// <summary>The probe's state and records as JSON: its phase, failure, server protocol, the session it holds and every record in order.</summary>
	internal JsonObject Status()
	{
		var records = new JsonArray();
		foreach ( var item in events )
			records.Add( item.DeepClone() );
		return new JsonObject
		{
			["phase"] = Phase,
			["failure"] = Failure,
			["protocol"] = Link.ServerProtocol,
			["state"] = Link.State.ToString(),
			["session"] = Link.Session is { } state ? Describe( state ) : null,
			["ended"] = SessionEnded,
			["events"] = records,
		};
	}

	/// <summary>Closes the link and the session: no leave (the driver leaves first), the login dropped with the session's client.</summary>
	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		session.Dispose();
	}

	private bool Asked( string kind, bool accepted, JsonObject? details = null )
	{
		details ??= new JsonObject();
		details["accepted"] = accepted;
		Record( kind, details );
		if ( !accepted )
			Fail( $"The link did not take the {kind} request." );
		return accepted;
	}

	private void Record( string kind, JsonObject details )
	{
		if ( events.Count >= MaximumEvents )
		{
			Fail( $"More than {MaximumEvents} records." );
			return;
		}
		details["kind"] = kind;
		details["at"] = Environment.TickCount64;
		events.Add( details );
	}

	private void Fail( string reason )
	{
		Failure ??= reason;
		if ( events.Count < MaximumEvents )
			events.Add( new JsonObject { ["kind"] = "failure", ["reason"] = reason, ["at"] = Environment.TickCount64 } );
	}

	/// <summary>A session as the probe records it: its id, code, who this game is and who hosts it, and its members.</summary>
	private static JsonObject Describe( CoopSessionState state )
	{
		var members = new JsonArray();
		foreach ( var member in state.Members )
			members.Add( new JsonObject { ["id"] = member.Id, ["slot"] = member.Slot, ["name"] = member.Name, ["connected"] = member.Connected } );
		return new JsonObject
		{
			["id"] = state.Id,
			["code"] = state.Code,
			["you"] = state.You,
			["host"] = state.Host,
			["lastFrame"] = state.LastFrameSeq,
			["members"] = members,
		};
	}

	/// <summary><paramref name="details"/> with the payload's length and SHA-256 (lowercase hexadecimal).</summary>
	private static JsonObject Payload( ReadOnlySpan<byte> payload, JsonObject details )
	{
		details["length"] = payload.Length;
		details["sha256"] = Convert.ToHexStringLower( SHA256.HashData( payload ) );
		return details;
	}

	/// <summary>
	/// The page that may run the probe: a loopback address opened with <c>?coopLinkSmoke=1</c> and neither of the page's other harness modes (<c>?perf=1</c>,
	/// <c>?coopSmoke=1</c>), the first value of each as the page reads its query.
	/// </summary>
	internal static bool IsPageAddress( string? href ) =>
		CoopJoinSmokeTest.LoopbackQuery( href ) is { } first && first( "coopLinkSmoke" ) == "1" && first( "perf" ) != "1" && first( "coopSmoke" ) != "1";
}
