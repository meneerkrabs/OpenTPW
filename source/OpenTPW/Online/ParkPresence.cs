using System.Diagnostics;
using OpenTPW.Hud;
using OpenTPW.Online.Chat;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>What <see cref="ParkPresence"/> uses of the online session: the login, the chat connection, the room's players and places, and the chat overlay's state.</summary>
public interface IPresenceSession : IChatPanelSource
{
	bool IsLoggedIn { get; }
	/// <summary>The open chat connection (its identity tells a reconnection apart), or null.</summary>
	object? Connection { get; }
	/// <summary>A connection is being opened or requests are on their way (<see cref="OnlineSession.ChatBusy"/>).</summary>
	bool ChatBusy { get; }
	/// <summary>A chat connection is being opened (the first one or a reconnection, <see cref="OnlineSession.ChatConnecting"/>).</summary>
	bool ChatConnecting { get; }
	string CurrentRoom { get; }
	/// <summary>The room the session is in or on its way to (<see cref="OnlineSession.WantedRoom"/>).</summary>
	string WantedRoom { get; }
	/// <summary>[EXT:online-coop-session] Whether the session is in a shared park, whose room owns its chat (<see cref="OnlineSession.InSharedPark"/>).</summary>
	bool InSharedPark { get; }
	/// <summary>[EXT:online-coop-session] The shared park's room is the connection's, confirmed (<see cref="OnlineSession.SharedRoomReady"/>).</summary>
	bool SharedRoomReady { get; }
	/// <summary>The last room the server refused (<see cref="OnlineSession.RefusedRoom"/>).</summary>
	string? RefusedRoom { get; }
	/// <summary>The server's protocol version, or null before it is known (<see cref="OnlineSession.FetchServerInfo"/>).</summary>
	int? ServerProtocol { get; }
	IReadOnlyDictionary<string, (float X, float Y)> RoomPositions { get; }
	/// <summary>How often each player's figure jumped (<see cref="OnlineSession.RoomJumps"/>).</summary>
	IReadOnlyDictionary<string, int> RoomJumps { get; }
	void ConnectChat();
	void CloseChat();
	void FetchServerInfo();
	void JoinRoom( string room );
	void MoveTo( float x, float y, ChatMoveMode mode = ChatMoveMode.Walk );
}

/// <summary>
/// The players in a visited park (docs/ONLINE.md "Players in a visited park"): the visit enters the park's chat room — a published park's own, or a
/// chat satellite's standing room for the visit of its park — as the original's visit switched to chat mode (manual p. 65); the player's figure walks
/// where they click and the others walk where the server relays them, each with its name over its head and the original's avatar sparkle at its
/// feet. Presentation and chat only: nothing of the park's simulation sees the figures. Created by the game flow for a visit with a chat room while
/// logged in, disposed when the park is left. Walking runs on real time, whatever the park's speed or pause: the others keep walking while the park
/// stands. [EXT:online-coop-figures] A shared park's members walk the same way in its session's room (<see cref="ForSharedPark"/>, docs/COOP.md "Co-builders as
/// figures"), each following their own view.
/// </summary>
// [EXT:online-visit-presence] the visit joins the park's room of OpenTPW's chat server and draws its players (ParkPresenceState)
public sealed class ParkPresence : IVisitPresence, IChatPanelSource, IDisposable
{
	/// <summary>The server protocol that relays movement in park rooms and answers each room's first movement (OpenTPW.Server ProtocolVersion 4).</summary>
	public const int MovementProtocol = 4;
	/// <summary>How long the figures' sparkle effects are apart: one world turn.</summary>
	// [BIN:TPW-EXE:0x50B495 online person tick] the sparkle 0x4F7C60 runs in every turn's tick of an online person (the type 0x12 case at 0x50B453)
	public const float SparkleSeconds = ParkCalendar.TurnMilliseconds / 1000f;
	/// <summary>
	/// Most figures that sparkle in one turn (the player's own first, then the nearest to the camera). The sparkle's emitter lives 7 ticks but holds its
	/// slot until its last particle dies, about 30 ticks later (some 1.15 s, between four and five turns), so each sparkling figure keeps about five of the
	/// pool's 120 slots.
	/// </summary>
	// [DATA:Data/Particle/Tp2.plb:53,57] Avatar and OtherAvatar: emitter life 7 ticks of 31 ms, particle life 30 ticks
	public const int MaximumSparkles = 6;
	/// <summary>No sparkle starts while the park's pool has more live emitters than this, so the park's own effects (fountains, smoke) always find a slot.</summary>
	public const int SparkleEmitterBudget = 80;
	/// <summary>How high over a figure's feet its name is drawn, engine units (a kid sprite stands about 1.9).</summary>
	public const float NameHeight = 2.1f;
	/// <summary>The first and the longest wait between two attempts to open the chat connection, seconds.</summary>
	public const float FirstConnectWait = 2f;
	public const float MaximumConnectWait = 30f;
	/// <summary>How long the visit waits before asking the server's version again, seconds.</summary>
	public const float ServerInfoWait = 10f;

	private readonly IPresenceSession session;
	private readonly Func<double> clock;
	/// <summary>
	/// The visit opened the chat connection: the session had none, open or opening, when the visit began, or the visit opened it later (after the online
	/// screens' attempt failed). Leaving the park closes it again.
	/// </summary>
	private bool opensConnection;
	private object? announcedOn;
	private bool wasInRoom;
	private double nextConnect;
	private float connectWait = FirstConnectWait;
	private double nextServerInfo;
	private bool disposed;
	private Visuals? visuals;

	/// <summary>The presence without its drawing (tests and the host that adds <see cref="Visuals"/>).</summary>
	/// <param name="clock">Seconds of a monotonic clock (double clicks, connection waits).</param>
	/// <param name="sharedPark">
	/// [EXT:online-coop-figures] A shared park's presence (C2d-4): the online session owns the chat and its room, so this presence never joins or leaves a room and never opens
	/// or closes the chat; its figure follows the player's view (<see cref="View"/>), not clicks.
	/// </param>
	internal ParkPresence( IPresenceSession session, string room, string playerName, IPresenceGround ground, (float X, float Y) start, Func<double> clock, bool sharedPark = false )
	{
		this.session = session;
		this.clock = clock;
		Room = room;
		SharedPark = sharedPark;
		State = new ParkPresenceState( ground, playerName, start );
		if ( sharedPark )
			return;
		// a connection the online screens are still opening is theirs: leaving the park keeps it
		opensConnection = session.Connection == null && !session.ChatConnecting;
		session.JoinRoom( Room );
		session.FetchServerInfo();
	}

	/// <summary>[EXT:online-coop-figures] Whether this is a shared park's presence (C2d-4), whose figure follows the view and whose chat is the online session's.</summary>
	public bool SharedPark { get; }

	/// <summary>[EXT:online-coop-figures] Where the player's view is centred, in cells (the overview's target, or the free walk's place); null when it has none (C2d-4).</summary>
	internal Func<(float X, float Y)?>? View { get; init; }

	/// <summary>
	/// [EXT:online-coop-figures] Whether the shared park's session still runs (C2d-4): it can end during the level's update, after the game flow last looked. A presence whose session
	/// is over sends nothing and draws no figure until the flow disposes it.
	/// </summary>
	internal Func<bool>? Running { get; init; }

	/// <summary>The figures and names are drawn and places go out: the server relays movement, and a shared park's session still runs.</summary>
	private bool FiguresShown => ServerWalks && (!SharedPark || Running?.Invoke() != false);

	/// <summary>
	/// [EXT:online-coop-figures] The player looks through their own figure's eyes (a shared park's free walk, whose place the figure follows): it is not drawn on this
	/// screen, as the original hid its own person locally in a first-person view, while the others still see it walk.
	/// </summary>
	// [BIN:TPW-EXE:0x4BD090 first-person entry] entering a first-person view hides the own online person locally (0x4F9ED0)
	internal Func<bool>? FirstPerson { get; init; }

	/// <summary>The figures drawn on this screen: those shown (<see cref="ParkPresenceState.Shown"/>) but the player's own in a first-person view (<see cref="FirstPerson"/>).</summary>
	internal IEnumerable<PresenceAvatar> Drawn => FirstPerson?.Invoke() == true ? State.Shown.Where( avatar => !avatar.Own ) : State.Shown;

	/// <summary>
	/// [EXT:online-coop-figures] The presence of the shared park <paramref name="level"/> in its session's chat room <paramref name="room"/> (C2d-4): the members connected to
	/// the room walk as figures with their names, the player's own following the view (<paramref name="view"/>) while the session runs (<paramref name="running"/>); null
	/// without a player name, a shared park's room, or the level's terrain and cell map.
	/// </summary>
	internal static ParkPresence? ForSharedPark( IPresenceSession session, Level level, string room, Func<(float X, float Y)?> view, Func<bool> running )
	{
		if ( ChatProtocol.CoopSessionOf( room ) == null || session.PlayerName is not { Length: > 0 } name || level.OriginalPark?.Heightfield is not { } field
			|| level.Guests?.Grid.Cells is not { } cells || global::Global.Render == null )
			return null;
		var stopwatch = Stopwatch.StartNew();
		var start = view() is { } centre ? centre : StartOf( level.Guests.Settings, cells );
		var presence = new ParkPresence( session, room, name, new PresenceGround( cells ), start, () => stopwatch.Elapsed.TotalSeconds, sharedPark: true )
		{
			Ride = () => level.RideView is { } ride ? new PresenceRide( ExitOf( ride.Ride ) ) : null,
			View = view,
			Running = running,
			FirstPerson = () => level.FreeWalk != null,
		};
		presence.visuals = new Visuals( presence, level, field );
		Log.Trace( $"Online presence: the shared park {level.LevelName} as {name}, room {room}." );
		return presence;
	}

	/// <summary>
	/// The presence of a visit, or null when the visit has none: a park file opened without its server (no chat room), no login, or a level without the
	/// original terrain and cell map the figures walk on.
	/// </summary>
	public static ParkPresence? TryCreate( IPresenceSession? session, Level level ) =>
		session != null && level.Visit?.ChatRoom is { } room && session.IsLoggedIn ? CreateFor( session, level, room ) : null;

	/// <summary>
	/// The presence of <paramref name="level"/> in chat room <paramref name="room"/> on <paramref name="session"/>, whatever the visit's own room (the visit
	/// smoke's scripted players use it); null without a player name, a valid room, or the level's terrain and cell map.
	/// </summary>
	internal static ParkPresence? CreateFor( IPresenceSession session, Level level, string room )
	{
		if ( !ChatProtocol.IsValidRoom( room ) || room == ChatProtocol.LobbyRoom || session.PlayerName is not { Length: > 0 } name
			|| level.OriginalPark?.Heightfield is not { } field || level.Guests?.Grid.Cells is not { } cells || global::Global.Render == null )
			return null;
		var stopwatch = Stopwatch.StartNew();
		var presence = new ParkPresence( session, room, name, new PresenceGround( cells ), StartOf( level.Guests.Settings, cells ), () => stopwatch.Elapsed.TotalSeconds )
		{
			Ride = () => level.RideView is { } view ? new PresenceRide( ExitOf( view.Ride ) ) : null,
		};
		presence.visuals = new Visuals( presence, level, field );
		Log.Trace( $"Online presence: visiting {level.LevelName} as {name}, room {room}." );
		return presence;
	}

	/// <summary>The ride the player rides now (the level's ride view), or null: their figure is hidden meanwhile and comes out at its exit (<see cref="ParkPresenceState.SetRiding"/>).</summary>
	internal Func<PresenceRide?>? Ride { get; set; }

	/// <summary>A ride the player rides, as the presence needs it: where its exit lets them out (cells, 0.5 = a cell's centre), when it has one.</summary>
	internal readonly record struct PresenceRide( (float X, float Y)? Exit );

	/// <summary>The centre of the path cell in front of <paramref name="ride"/>'s exit, where guests leave it; null for a ride without visitor cells.</summary>
	internal static (float X, float Y)? ExitOf( OriginalObject ride ) =>
		ride.Visitors is { HasCells: true } visitors ? (visitors.ExitCell.X + 0.5f, visitors.ExitCell.Y + 0.5f) : null;
	/// <summary>The park's chat room, which every visitor of the park joins: a published park's own, or the standing room of the chat satellite whose park it is.</summary>
	public string Room { get; }
	public ParkPresenceState State { get; }
	/// <summary>
	/// The session is connected and in the park's room: chat is live. [EXT:online-coop-figures] A shared park's room counts once this connection's join of it is confirmed
	/// (<see cref="IPresenceSession.SharedRoomReady"/>): a movement before it could reach the room the server still has the connection in.
	/// </summary>
	public bool InRoom => SharedPark ? session.SharedRoomReady && session.CurrentRoom == Room : session.Connection != null && session.CurrentRoom == Room;
	public bool CanChat => !disposed && InRoom;
	/// <summary>
	/// The server relays movement in park rooms (<see cref="MovementProtocol"/>): without it the visit chats but shows and sends no figures. [EXT:online-coop-figures] A shared
	/// park's room is a server's with shared parks (<see cref="ChatProtocol.CoopRoomsVersion"/>), whose rooms all relay movement but the lobby.
	/// </summary>
	public bool ServerWalks => session.ServerProtocol >= (SharedPark ? ChatProtocol.CoopRoomsVersion : MovementProtocol);
	public bool Walks => !disposed && ServerWalks;
	/// <summary>The server refused the park's room (the park was unpublished, or the satellite is gone): the visit stops asking for it.</summary>
	public bool Refused => session.RefusedRoom == Room;
	public IReadOnlyList<string> ChatLines => session.ChatLines;
	/// <summary>The park room's chat for the chat overlay: the session's, sending only while the visit is in the room.</summary>
	public IChatPanelSource Chat => this;
	string? IChatPanelSource.PlayerName => session.PlayerName;
	bool IChatPanelSource.ChatOpen => CanChat;
	IReadOnlyList<string> IChatPanelSource.RoomPlayers => InRoom ? session.RoomPlayers : Array.Empty<string>();
	IReadOnlyList<(string Name, bool Online)> IChatPanelSource.Buddies => session.Buddies;
	IReadOnlyCollection<string> IChatPanelSource.Ignored => session.Ignored;
	bool IChatPanelSource.Earmuffs => session.Earmuffs;
	bool IChatPanelSource.CanVote => CanChat && session.CanVote;
	void IChatPanelSource.SendChatLine( string line ) => Say( line );
	void IChatPanelSource.SendCommand( ChatCommand command, string argument )
	{
		if ( CanChat )
			session.SendCommand( command, argument );
	}
	void IChatPanelSource.AskBuddies()
	{
		if ( CanChat )
			session.AskBuddies();
	}
	/// <summary>Sparkle effects started so far (0 without drawing).</summary>
	public int Sparkles => visuals?.SparklesStarted ?? 0;

	/// <summary>
	/// Whether the figures sparkle (the original's avatar effect): in a visit. [EXT:online-coop-figures] Never in a shared park: the level's particle pool is captured with the
	/// park and object runtimes keep handles into it, so a local effect would make the players' parks differ.
	/// </summary>
	internal bool SparkleAllowed => !SharedPark;

	/// <summary>Where the player's figure appears: at the centre of the park entrance's first cell, or the park's middle when the level has none.</summary>
	// [BIN:TPW-EXE:0x4E5EF0 own person] entering the park places the player's own online person at the centre of entrance 0's cell (0x4D8690(0): FixedItemInfo.EntranceA,
	// 0x7855BC/0x7855C0) with the teleport 0x4F7E20 and sends it as MovePlayer(x·256+128, y·256+128, 1), when that cell's type byte is 0x12 (0x4E5FFE; otherwise it
	// places and sends nothing); [EXT:online-avatar-start] OpenTPW always starts there and always sends the teleport
	// [EXT:online-avatar-start] a level whose entrance A lies outside its cell map starts the figure in the park's middle
	internal static (float X, float Y) StartOf( GuestSettings? settings, ParkCellMap cells ) =>
		settings?.EntranceA is { } a && cells.InBounds( a.X, a.Y ) ? (a.X + 0.5f, a.Y + 0.5f) : (cells.Width / 2f, cells.Height / 2f);

	/// <summary>A click on the park's ground (cells): the player's figure walks there, or teleports on a double click.</summary>
	public void Click( float cellX, float cellY )
	{
		// [EXT:online-coop-figures] a shared park's clicks are the park's (the host builds): its figure follows the view
		if ( !disposed && !SharedPark )
			State.Click( cellX, cellY, clock() );
	}

	/// <summary>
	/// A name popup's goto: the player's figure appears at the other player's place when they are in the park (the server's goto keeps the player in
	/// the park's room).
	/// </summary>
	// [EXT:online-chat-goto] the figure teleports to the player it went to; the original server's answer to a goto (command 0x13) is not read
	public void GoTo( string name )
	{
		// [EXT:online-coop-figures] a shared park's go to moves the view (the HUD's), which the figure follows
		if ( disposed || !Walks || SharedPark )
			return;
		var other = State.Others.FirstOrDefault( avatar => avatar.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) );
		if ( other != null )
			State.TeleportTo( other.X, other.Y );
	}

	/// <summary>Says a line in the park's room (or runs a chat command), as the chat overlay's entry does.</summary>
	public void Say( string line )
	{
		if ( CanChat )
			session.SendChatLine( line );
	}

	/// <summary>
	/// The figures' names and where they stand (engine, the feet), for the HUD to draw under its windows: none while the server does not relay
	/// movement, as no figure is drawn then.
	/// </summary>
	public IEnumerable<VisitPresenceName> Names => visuals == null || !FiguresShown ? Array.Empty<VisitPresenceName>()
		: Drawn.Select( avatar => new VisitPresenceName( avatar.Name, avatar.Own, visuals.Feet( avatar ) ) );

	/// <summary>Keeps the connection and the room, walks the player's figure, sends its place, follows the others and draws them; every frame.</summary>
	public void Update( float delta )
	{
		if ( disposed )
			return;
		var now = clock();
		// [EXT:online-coop-figures] a shared park's chat is the online session's to keep
		if ( !SharedPark )
			KeepConnected( now );
		var ride = Ride?.Invoke();
		State.SetRiding( ride != null, ride?.Exit );
		var shown = FiguresShown;
		if ( SharedPark && shown && View?.Invoke() is { } centre )
			State.FollowView( centre.X, centre.Y, now );
		var inRoom = InRoom && shown;
		var announce = inRoom && (!wasInRoom || !ReferenceEquals( session.Connection, announcedOn ));
		if ( State.Advance( delta, inRoom, announce ) is { } place )
		{
			session.MoveTo( place.X, place.Y, State.SentMode );
			announcedOn = session.Connection;
		}
		wasInRoom = inRoom;
		if ( inRoom )
			State.Sync( session.RoomPositions, session.RoomPlayers, delta, session.RoomJumps );
		else
			State.Sync( EmptyPositions, Array.Empty<string>(), delta );
		visuals?.Update( delta, shown );
	}

	private static readonly IReadOnlyDictionary<string, (float X, float Y)> EmptyPositions = new Dictionary<string, (float X, float Y)>();

	/// <summary>
	/// Opens the chat connection when there is none (waiting longer after each failure) and learns the server's version. The room is the session's to keep:
	/// it joins the park's room once the connection opens, again after a reconnection, and retries a join the server dropped.
	/// </summary>
	private void KeepConnected( double now )
	{
		if ( !session.IsLoggedIn )
			return;
		// a version not yet known is asked again; a failed read of the open connection is retried by the session
		if ( session.ServerProtocol == null && now >= nextServerInfo )
		{
			session.FetchServerInfo();
			nextServerInfo = now + ServerInfoWait;
		}
		if ( session.Connection == null )
		{
			if ( !session.ChatBusy && now >= nextConnect )
			{
				try
				{
					session.ConnectChat();
					opensConnection = true;
				}
				catch ( InvalidOperationException ) { }
				nextConnect = now + connectWait;
				connectWait = Math.Min( connectWait * 2, MaximumConnectWait );
			}
			return;
		}
		connectWait = FirstConnectWait;
	}

	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		visuals?.Dispose();
		visuals = null;
		// [EXT:online-coop-figures] a shared park's room and chat are the online session's: its presence leaves them as they are
		if ( SharedPark )
			return;
		// leaving the park leaves its room, so its other visitors see the parting; a connection the visit opened is closed again, so the
		// Online World's lines do not follow the player into their own park. [EXT:online-coop-session] A chat a shared park's session took over since is
		// no longer the visit's: it stays open, in the session's room.
		if ( session.InSharedPark )
			return;
		if ( opensConnection )
			session.CloseChat();
		else if ( session.WantedRoom == Room )
			session.JoinRoom( ChatProtocol.LobbyRoom );
	}

	/// <summary>The cells a figure may cross (<see cref="ParkPresenceState.WalkTo"/>).</summary>
	// [EXT:online-avatar-route] land, paths, queues, object entrances and exits, and the fixed walkways; not water, objects, cells without terrain, track,
	// pool water or land outside the park (the entrance complex excepted)
	internal sealed class PresenceGround( ParkCellMap cells ) : IPresenceGround
	{
		public int Width => cells.Width;
		public int Height => cells.Height;

		/// <summary>[EXT:online-coop-figures] The cell map's version: a shared park's host builds while the figures walk.</summary>
		public int Version => cells.Version;

		public bool IsPassable( int x, int y )
		{
			var type = cells.RawTypeAt( x, y );
			if ( type == ParkCellTypes.FixedWalkway )
				return true;
			if ( type is not ((byte)ParkCellType.Empty or (byte)ParkCellType.Path or (byte)ParkCellType.Queue or ParkCellTypes.Entrance or ParkCellTypes.Exit) )
				return false;
			var flags = cells.FlagsAt( x, y );
			return !flags.HasFlag( ParkCellFlags.Unowned ) || flags.HasFlag( ParkCellFlags.EntranceArea );
		}
	}

	/// <summary>
	/// The drawing of the figures in the level: the kids' renderer draws them with the guests' sprites (<see cref="GuestRenderer.Figures"/>) and each
	/// sparkles once a turn; their names are drawn by the HUD (<see cref="Names"/>).
	/// </summary>
	private sealed class Visuals : IDisposable
	{
		private readonly ParkPresence presence;
		private readonly Level level;
		private readonly ModelHeightfield field;
		private readonly GuestRenderer? renderer;
		private readonly List<SpriteFigure> figures = new();
		private readonly Func<IReadOnlyCollection<SpriteFigure>> source;
		private float sinceSparkle;

		public Visuals( ParkPresence presence, Level level, ModelHeightfield field )
		{
			this.presence = presence;
			this.level = level;
			this.field = field;
			source = () => figures;
			renderer = level.GuestRenderer;
			if ( renderer != null )
				renderer.Figures = source;
		}

		public int SparklesStarted { get; private set; }
		private ParkPresenceState State => presence.State;

		public void Update( float delta, bool shown )
		{
			figures.Clear();
			if ( !shown )
				return;
			foreach ( var avatar in presence.Drawn )
			{
				var (x, y) = OnTerrain( avatar );
				figures.Add( new SpriteFigure( x, y, avatar.HeadingX, avatar.HeadingY, avatar.DistanceWalked, avatar.Moving, avatar.Look ) );
			}
			if ( presence.SparkleAllowed )
				Sparkle( delta );
		}

		/// <summary>A figure's place within the terrain: the park's cell map can be larger than its heightfield, and a place relayed from the overhead chat view can lie anywhere on it.</summary>
		private (float X, float Y) OnTerrain( PresenceAvatar avatar ) =>
			(Math.Clamp( avatar.X, 0f, field.CellCountX ), Math.Clamp( avatar.Y, 0f, field.CellCountZ ));

		/// <summary>The engine position of a figure's feet, on the terrain.</summary>
		public SVector3 Feet( PresenceAvatar avatar )
		{
			var (x, y) = OnTerrain( avatar );
			return GuestRenderer.CellToEngine( field, x, y );
		}

		/// <summary>
		/// The original's avatar sparkle once a turn: the player's own figure gets effect 0x35 ("Avatar"), the others 0x39 ("OtherAvatar"), at the
		/// ground under the figure. Not while the park's pool is stopped (paused), whose emitters would not age and would fill the pool, nor while
		/// the pool is busy (<see cref="SparkleEmitterBudget"/>).
		/// </summary>
		// [BIN:TPW-EXE:0x4F7C60 avatar sparkle] an online person with +0x3A0 set (the player's own) starts effect 0x35, any other 0x39, through 0x4FA470
		// [BIN:TPW-EXE:0x4FA470 sparkle position] the person's model position with the terrain height under it (0x4527F0), times 1024, to the pool's create 0x521E60
		// [EXT:online-avatar-sparkle-cap] the original sparkles every online person every turn (its server capped a park's attendance); OpenTPW sparkles at most
		// MaximumSparkles of them and none while the pool is busy, so many players cannot crowd out the park's own effects
		private void Sparkle( float delta )
		{
			sinceSparkle += delta;
			if ( sinceSparkle < SparkleSeconds )
				return;
			sinceSparkle %= SparkleSeconds;
			if ( !(level.SimulationTimeScale > 0f) || level.HidesInterface || !ReferenceEquals( Level.Current, level ) || ParticleRendererModule.LiveEmitters() > SparkleEmitterBudget )
				return;
			var target = (Camera.Mode as ParkCameraMode)?.Target;
			foreach ( var avatar in State.Shown.OrderBy( avatar => !avatar.Own ).ThenBy( avatar => target is { } point ? DistanceSquared( Feet( avatar ), point ) : 0f ).Take( MaximumSparkles ) )
				if ( ParticleRendererModule.StartEmitter( avatar.Own ? (int)ParLib.P_EFFECT_Avatar : (int)ParLib.P_EFFECT_OtherAvatar, Feet( avatar ) ) != null )
					SparklesStarted++;
		}

		private static float DistanceSquared( SVector3 feet, Vector3 target ) =>
			(feet.X - target.X) * (feet.X - target.X) + (feet.Y - target.Y) * (feet.Y - target.Y);

		public void Dispose()
		{
			// a newer presence may have taken the renderer already (the game flow attaches before it disposes the old one)
			if ( renderer != null && ReferenceEquals( renderer.Figures, source ) )
				renderer.Figures = null;
			figures.Clear();
		}
	}
}
