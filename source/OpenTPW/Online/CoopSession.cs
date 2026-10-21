using OpenTPW.Online.Coop;

namespace OpenTPW;

/// <summary>[EXT:online-coop-lockstep] A level's part in a co-op session (<see cref="Level.CoopSession"/>).</summary>
public enum CoopRole
{
	None,
	Host,
	Guest
}

/// <summary>
/// [EXT:online-coop-lockstep] A level in a co-op session (docs/COOP.md, "The level in a session"): the stream it runs over the link (the host's, or a guest's), and the session's
/// rules on the level. Only the host builds (C2), and a guest's own click is refused with a message; local pause screens stop no park; cheats and the developer kinds are refused
/// alike on every peer; a game started with developer park options shares nothing; a guest's park is never saved, not even after its session. It ends when the level closes or
/// is replaced, when the link's session ends, or when the host's stream ends it. The screens that start one are C2d's; the join snapshot is C2c's.
/// </summary>
internal sealed class CoopSession : IDisposable
{
	private readonly Level level;
	private readonly ICoopStreamTransport link;

	private CoopSession( Level level, ICoopStreamTransport link, CoopRole role )
	{
		this.level = level;
		this.link = link;
		Role = role;
	}

	/// <summary>
	/// Whether this run may change the park outside the commands: the desktop game started with a developer option (every <c>--debug-*</c> and <c>--perf-*</c> option, and the smokes
	/// that build their parks directly: <c>--smoke-test</c>, <c>--tpi-smoke-test</c>, <c>--park-resume-smoke</c>, <c>--smoke-presence</c>) or the browser page of the performance
	/// harness, whose hooks stage crowds, cheats and research directly. Such a game shares no park, since a peer would never see those changes.
	/// </summary>
	internal static bool ChangesParkOutsideCommands { get; set; }

	public CoopRole Role { get; }
	public CoopHostStream? HostStream { get; private set; }
	public CoopGuestStream? GuestStream { get; private set; }

	/// <summary>Why the session ended for this game; null while it runs.</summary>
	public string? EndedWith { get; private set; }

	/// <summary>Raised once when the session ends for this game, with the reason.</summary>
	public event Action<string>? Ended;

	/// <summary>
	/// Whether <paramref name="level"/> can be shared (hosted, or joined from the host's park): a running Theme Park World park with its guests (decision Q3: never Theme Park Inc, and
	/// never the developer sandbox, whose load replaces the park outside the commands), not a visit, not in a session already, no cheat waiting for its park turn (decision Q17:
	/// it would apply inside the session), in a game not started with a developer option that changes the park outside the commands.
	/// </summary>
	public static bool CanShare( Level level, out string reason )
	{
		ArgumentNullException.ThrowIfNull( level );
		reason = level.CoopSession != null ? "The park is shared already."
			: level.IsCoopGuestPark ? "A guest's copy of a shared park is not shared again."
			: GameEditions.Current != GameEdition.ThemeParkWorld || level.IsThemeParkIncSlice || level.OriginalPark == null ? "Only Theme Park World parks are shared."
			: level.IsReadOnlyVisit ? "A visited park is not shared."
			: level.Park == null || level.Guests == null ? "Only a running park is shared."
			: level.Park.Economy.Cheats.Pending.Count > 0 ? "A cheat is waiting for the next park turn; share the park after it."
			: ChangesParkOutsideCommands ? "This game was started with developer options that change the park; restart it without them to share a park."
			: "";
		return reason.Length == 0;
	}

	/// <summary>Hosts the session <paramref name="link"/> is in with <paramref name="level"/>: the host's stream starts (the session clock with it).</summary>
	public static CoopSession Host( Level level, ICoopStreamTransport link, Func<double>? seconds = null, Func<DateTime>? localTime = null )
	{
		Require( level, link, CoopRole.Host );
		// a park that lost an economy binding is repaired while it is still this game's alone: once shared, it changes only through its commands
		level.RepairMissingEconomyBindings();
		var session = new CoopSession( level, link, CoopRole.Host );
		session.HostStream = new CoopHostStream( level, link, seconds, localTime );
		session.HostStream.Failed += session.OnStreamFailed;
		return session.Attach();
	}

	/// <summary>Joins the session <paramref name="link"/> is in with <paramref name="level"/>, which must be the host's park as it stands at the session's last frame (C2c).</summary>
	public static CoopSession Join( Level level, ICoopStreamTransport link, Func<double>? seconds = null )
	{
		Require( level, link, CoopRole.Guest );
		var session = new CoopSession( level, link, CoopRole.Guest );
		session.GuestStream = new CoopGuestStream( level, link, seconds );
		level.Commands.SessionRefused += session.OnRefused;
		return session.Attach();
	}

	private static void Require( Level level, ICoopStreamTransport link, CoopRole role )
	{
		ArgumentNullException.ThrowIfNull( link );
		if ( !CanShare( level, out var reason ) )
			throw new InvalidOperationException( reason );
		if ( link.Session is not { } session )
			throw new InvalidOperationException( "The link is in no session." );
		if ( (session.You == session.Host) != (role == CoopRole.Host) )
			throw new InvalidOperationException( role == CoopRole.Host ? "This game does not host the link's session." : "The host's game streams; it does not follow." );
	}

	private CoopSession Attach()
	{
		link.SessionEnded += OnSessionEnded;
		level.CoopSession = this;
		// a guest's park is the host's: nothing saves it, during the session or after it
		if ( Role == CoopRole.Guest )
			level.IsCoopGuestPark = true;
		return this;
	}

	/// <summary>Ends the session for this game with <paramref name="reason"/> and leaves it (a host's leave ends it for everyone).</summary>
	public void End( string reason ) => Finish( reason, leave: true );

	/// <summary>The level closes (a load, a restart, a replaced park, leaving it): the session ends for this game.</summary>
	public void Dispose() => End( "The park was closed." );

	private void OnSessionEnded( CoopEnded ended ) => Finish( ended.Text, leave: false );

	/// <summary>The host's stream ended the session (and left it) because it cannot go on without skipping or losing a record.</summary>
	private void OnStreamFailed( string reason ) => Finish( reason, leave: false );

	/// <summary>A guest's own click was refused by the session's rights: its HUD says only the host builds.</summary>
	private void OnRefused( ParkCommand command ) => level.NoteSessionRefusal();

	private void Finish( string reason, bool leave )
	{
		if ( EndedWith != null )
			return;
		EndedWith = reason;
		link.SessionEnded -= OnSessionEnded;
		level.Commands.SessionRefused -= OnRefused;
		if ( HostStream != null )
		{
			HostStream.Failed -= OnStreamFailed;
			HostStream.Dispose();
		}
		GuestStream?.Dispose();
		if ( level.CoopSession == this )
			level.CoopSession = null;
		if ( leave )
			link.Leave( reason );
		// a listener that throws keeps neither the other listeners nor a closing park from the rest of its teardown
		foreach ( var listener in Ended?.GetInvocationList() ?? Array.Empty<Delegate>() )
		{
			try { ((Action<string>)listener)( reason ); }
			catch ( Exception error ) when ( error is not OutOfMemoryException ) { Log.Warning( $"A co-op session's end listener failed: {error}" ); }
		}
	}
}

public partial class Level
{
	/// <summary>[EXT:online-coop-lockstep] The co-op session this level is in (<see cref="OpenTPW.CoopSession"/>), or null.</summary>
	internal CoopSession? CoopSession { get; set; }

	/// <summary>[EXT:online-coop-lockstep] This level's part in a co-op session.</summary>
	internal CoopRole CoopRole => CoopSession?.Role ?? CoopRole.None;

	/// <summary>[EXT:online-coop-lockstep] Whether this level joined a co-op session as a guest: its park is the host's, which nothing saves, also after the session ended.</summary>
	internal bool IsCoopGuestPark { get; set; }

	/// <summary>[EXT:online-coop-lockstep] How many of this machine's clicks the session refused (a guest: only the host builds); the HUD says so when it changes.</summary>
	internal int SessionRefusals { get; private set; }

	internal void NoteSessionRefusal() => SessionRefusals++;
}
