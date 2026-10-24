using System.Runtime.InteropServices.JavaScript;
using System.Text.Json.Nodes;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-session] The browser's link to a real server, the transport only (C2c-2b-5; docs/COOP.md, "The browser's link to a real server";
/// tools/web-coop-link.mjs). In the page's link check mode (<c>?coopLinkSmoke=1</c> on a loopback address) the page starts no game, reads no game file and opens
/// none of its stores: the harness runs one <see cref="CoopLinkProbe"/> against the server that serves the page, over the browser runtime's own WebSocket and
/// HttpClient. The probe logs in and opens the co-op link, the harness makes each request and reads the probe's records back. One probe a page.
/// </summary>
public static partial class Program
{
	/// <summary>The probe's own online folder in the page's file system, never the player's.</summary>
	private const string CoopLinkDirectory = "/coop-link";

	private static bool coopLinkMode;
	private static CoopLinkProbe? coopLink;

	/// <summary>The page's link check mode, chosen before the game or another harness mode starts: the check's exports work only in it.</summary>
	[JSExport]
	public static void CoopLinkSmokeMode()
	{
		if ( renderer != null || flow != null || coopSmokeMode || CoopSession.ChangesParkOutsideCommands )
			throw new InvalidOperationException( "The co-op link check's mode is chosen before the game or another harness mode starts." );
		// the page's own address, which no script can change, not the word of the script that calls this
		using var location = JSHost.GlobalThis.GetPropertyAsJSObject( "location" );
		if ( !CoopLinkProbe.IsPageAddress( location?.GetPropertyAsString( "href" ) ) )
			throw new InvalidOperationException( "The co-op link check is only for a page on a loopback address opened with ?coopLinkSmoke=1." );
		coopLinkMode = true;
	}

	/// <summary>
	/// Starts the page's probe: an online session in its own folder logs in as <paramref name="name"/> to the server that serves this page (its origin, which serves
	/// the API too), then opens the co-op link; <see cref="CoopLinkSmokeStatus"/> carries it on.
	/// </summary>
	[JSExport]
	public static void CoopLinkSmokeStart( string name, string password )
	{
		RequireCoopLinkMode();
		if ( coopLink != null )
			throw new InvalidOperationException( "One co-op link check a page: load the page again for another." );
		using var location = JSHost.GlobalThis.GetPropertyAsJSObject( "location" );
		var origin = location?.GetPropertyAsString( "origin" );
		if ( string.IsNullOrEmpty( origin ) )
			throw new InvalidOperationException( "The page has no origin to log in to." );
		coopLink = new CoopLinkProbe( CoopLinkDirectory, origin, name, password );
	}

	/// <summary>Creates a session the page's probe hosts (two players, on the open list when <paramref name="listed"/>); false when the link did not take the request.</summary>
	[JSExport]
	public static bool CoopLinkSmokeCreate( string title, bool listed ) => RunningCoopLink().Create( title, listed );

	/// <summary>The host invites <paramref name="player"/> (C2d-3b); false when the link did not take the request.</summary>
	[JSExport]
	public static bool CoopLinkSmokeInvite( string player ) => RunningCoopLink().Invite( player );

	/// <summary>Asks for the open list over HTTP (C2d-3b); its rows are a record.</summary>
	[JSExport]
	public static bool CoopLinkSmokeList() => RunningCoopLink().List();

	/// <summary>Joins the session with <paramref name="code"/>; false when the link did not take the request.</summary>
	[JSExport]
	public static bool CoopLinkSmokeJoin( string code ) => RunningCoopLink().Join( code );

	/// <summary>The host stores <paramref name="payload"/> as the session's snapshot; its outcome is a record.</summary>
	[JSExport]
	public static bool CoopLinkSmokeUpload( byte[] payload ) => RunningCoopLink().Upload( payload );

	/// <summary>The guest downloads the snapshot <paramref name="sha256"/> of <paramref name="bytes"/> bytes; its outcome is a record.</summary>
	[JSExport]
	public static bool CoopLinkSmokeDownload( string sha256, int bytes ) => RunningCoopLink().Download( sha256, bytes );

	/// <summary>The host's frame <paramref name="seq"/> with <paramref name="payload"/>.</summary>
	[JSExport]
	public static bool CoopLinkSmokeSendFrame( int seq, byte[] payload ) => seq > 0 && RunningCoopLink().SendFrame( (ulong)seq, payload );

	/// <summary>A message with <paramref name="payload"/> to member <paramref name="to"/>.</summary>
	[JSExport]
	public static bool CoopLinkSmokeSend( int to, byte[] payload ) => to > 0 && RunningCoopLink().Send( (uint)to, payload );

	/// <summary>Leaves the session (the host's leave ends it for the guest).</summary>
	[JSExport]
	public static bool CoopLinkSmokeLeave( string reason ) => RunningCoopLink().Leave( reason );

	/// <summary>[EXT:online-coop-session] Says a line in the room the probe's chat is in (<see cref="CoopLinkProbe.Say"/>).</summary>
	[JSExport]
	public static bool CoopLinkSmokeSay( string line ) => RunningCoopLink().Say( line );

	/// <summary>Closes the probe's link and session; its records stay readable.</summary>
	[JSExport]
	public static void CoopLinkSmokeEnd()
	{
		RequireCoopLinkMode();
		coopLink?.Dispose();
	}

	/// <summary>
	/// Carries the page's probe on (what its session's background work posted runs here: the page runs no game loop) and returns where it stands, as JSON: the mode,
	/// whether a probe started, and the probe's phase, failure, server protocol, session and records (<see cref="CoopLinkProbe.Status"/>).
	/// </summary>
	[JSExport]
	public static string CoopLinkSmokeStatus()
	{
		coopLink?.Step();
		var status = coopLink?.Status() ?? new JsonObject();
		status["mode"] = coopLinkMode;
		status["started"] = coopLink != null;
		return status.ToJsonString();
	}

	private static void RequireCoopLinkMode()
	{
		if ( !coopLinkMode )
			throw new InvalidOperationException( "The co-op link check runs only in the page's link check mode (?coopLinkSmoke=1 on a loopback address)." );
	}

	private static CoopLinkProbe RunningCoopLink()
	{
		RequireCoopLinkMode();
		return coopLink ?? throw new InvalidOperationException( "The co-op link check has not started (CoopLinkSmokeStart)." );
	}
}
