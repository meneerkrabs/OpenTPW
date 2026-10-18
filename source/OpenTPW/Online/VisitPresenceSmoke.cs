using OpenTPW.Hud;
using OpenTPW.Online.Chat;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// The visit smoke's online players (<c>--visit-park … --smoke-test --smoke-presence</c>): a session without a server whose room holds three scripted
/// players round the park entrance (one walks a circle, one stands, one teleports), so the native run checks that the figures are drawn with their
/// names and sparkles and that a click on the ground walks the player's own figure through the HUD and sends its places.
/// </summary>
internal sealed class VisitPresenceSmoke
{
	public const string PlayerName = "Smoke";
	public const string ParkId = "smokepark";
	private const string ChatLine = "hello park";
	private static readonly string[] Others = { "Ann", "Bob", "Carla" };

	private readonly Session session;
	private readonly ParkPresence presence;
	private readonly Level level;
	private readonly (float X, float Y) start;
	private readonly int width, height;
	private float time;
	private int phase;
	private int phaseFrames;
	private (float X, float Y) clickTarget;
	private int movesBeforeClick;
	private int clicks;

	private VisitPresenceSmoke( Level level, ParkPresence presence, ParkCellMap cells, Session session )
	{
		this.level = level;
		this.presence = presence;
		this.session = session;
		start = (presence.State.Own.X, presence.State.Own.Y);
		width = cells.Width;
		height = cells.Height;
		Script( 0f );
	}

	/// <summary>The scripted presence attached to the flow, or null when the level has no terrain for figures.</summary>
	public static VisitPresenceSmoke? Attach( Level level, Action<ParkPresence> attach )
	{
		var session = new Session();
		if ( level.Guests?.Grid.Cells is not { } cells )
			return null;
		var smoke = ParkPresence.CreateFor( session, level, ParkId ) is { } presence ? new VisitPresenceSmoke( level, presence, cells, session ) : null;
		if ( smoke != null )
			attach( smoke.presence );
		return smoke;
	}

	/// <summary>Whether the presence checks are done.</summary>
	public bool Completed { get; private set; }
	public string Report { get; private set; } = "";

	/// <summary>
	/// One smoke frame after the flow's update: moves the scripted players, then, once the visit's own checks are through, clicks the ground beside the
	/// player's figure through the HUD, waits for the walk and checks what was drawn and sent.
	/// </summary>
	public void Update( float delta, Action<UiInput>? injectInput, ParkHud? hud )
	{
		time += delta;
		Script( time );
		if ( Completed || level.HidesInterface )
			return;
		phaseFrames++;
		switch ( phase )
		{
			case 0:
				// the figures are drawn and named, and have sparkled at least once
				if ( phaseFrames < 30 )
					return;
				Require( presence.InRoom, "the scripted session is in the park's room" );
				Require( level.GuestRenderer?.DrawnFigures == 1 + Others.Length, $"every player of the room is drawn as a figure ({level.GuestRenderer?.DrawnFigures})" );
				Require( presence.Names.Count() == 1 + Others.Length, $"every figure has its name for the HUD ({presence.Names.Count()})" );
				Require( presence.Sparkles > 0, "the figures sparkle" );
				Require( session.Moves > 0, "the player's figure announced its place" );
				phase = 1;
				phaseFrames = 0;
				Log.Trace( $"Visit smoke: {level.GuestRenderer?.DrawnFigures} online figures drawn, {presence.Sparkles} sparkles." );
				return;
			case 1:
				// a click on free ground a few cells from the figure, through the HUD's park click
				if ( injectInput == null || hud == null )
				{
					phase = 3;
					return;
				}
				var ground = new ParkPresence.PresenceGround( level.Guests!.Grid.Cells );
				var target = Enumerable.Range( 3, 6 ).SelectMany( distance => new[] { (start.X, start.Y + distance), (start.X + distance, start.Y), (start.X - distance, start.Y), (start.X, start.Y - distance) } )
					.Select( cell => ((int)cell.Item1, (int)cell.Item2) )
					.Where( cell => ground.Width > cell.Item1 && cell.Item1 >= 0 && ground.Height > cell.Item2 && cell.Item2 >= 0 && ground.IsPassable( cell.Item1, cell.Item2 ) && level.Objects.FindAt( cell.Item1, cell.Item2 ) == null )
					.Skip( clicks ).FirstOrDefault( (-1, -1) );
				Require( target.Item1 >= 0, "a free cell near the park entrance to walk to" );
				var viewport = new System.Numerics.Vector2( Render.OverlayFramebuffer.Width, Render.OverlayFramebuffer.Height );
				Require( level.TryProjectCell( target.Item1, target.Item2, viewport, out var point ), "the walk target is on screen" );
				clickTarget = (target.Item1 + 0.5f, target.Item2 + 0.5f);
				movesBeforeClick = session.Moves;
				clicks++;
				injectInput( UiInput.Click( point ) );
				Log.Trace( FormattableString.Invariant( $"Visit smoke: clicking cell {target.Item1},{target.Item2} at {point.X:F0},{point.Y:F0}." ) );
				phase = 2;
				phaseFrames = 0;
				return;
			case 2:
				var own = presence.State.Own;
				if ( presence.State.Walking && phaseFrames < 900 )
					return;
				var distance = MathF.Sqrt( (own.X - clickTarget.X) * (own.X - clickTarget.X) + (own.Y - clickTarget.Y) * (own.Y - clickTarget.Y) );
				if ( distance > 1.5f && clicks < 3 && own.DistanceWalked == 0f )
				{
					// the click met a visitor or the HUD: try the next cell
					phase = 1;
					return;
				}
				Require( distance <= 1.5f, $"the HUD click walked the figure to the clicked ground ({own.X:F2},{own.Y:F2} for {clickTarget.X:F2},{clickTarget.Y:F2})" );
				Require( session.Moves > movesBeforeClick + 1, "the walk sent the figure's places" );
				// the capture looks back at the entrance from inside the park, so the gate stands behind the figures
				if ( Camera.Mode is ParkCameraMode camera && level.OriginalPark?.Heightfield is { } field )
				{
					var middle = GuestRenderer.CellToEngine( field, start.X, start.Y + 1.5f );
					camera.SetPose( new Vector3( middle.X, middle.Y, middle.Z ), camera.Height, camera.Yaw + 180 );
				}
				phase = 3;
				phaseFrames = 0;
				return;
			case 3:
				// Enter over the park opens the chat line, the typed line goes to the room and shows over the park
				if ( phaseFrames < 10 )
					return;
				if ( injectInput == null || hud == null )
				{
					phase = 7;
					return;
				}
				injectInput( UiInput.Key( UiKeys.Accept ) );
				phase = 4;
				phaseFrames = 0;
				return;
			case 4:
				if ( phaseFrames < 3 )
					return;
				Require( hud!.Stack.Top?.Name == "visitChat", $"Enter over the park opens the chat line (top screen {hud.Stack.Top?.Name})" );
				injectInput!( UiInput.Type( ChatLine ) );
				phase = 5;
				phaseFrames = 0;
				return;
			case 5:
				if ( phaseFrames < 3 )
					return;
				injectInput!( UiInput.Key( UiKeys.Accept ) );
				phase = 6;
				phaseFrames = 0;
				return;
			case 6:
				if ( phaseFrames < 5 )
					return;
				Require( session.Said.Contains( ChatLine ), "the chat line was said in the park's room" );
				Require( hud!.Stack.Top?.Name != "visitChat", "Enter in the chat line closes it" );
				phase = 7;
				phaseFrames = 0;
				return;
			case 7:
				if ( phaseFrames < 5 )
					return;
				phase = 8;
				return;
			default:
				Completed = true;
				Report = FormattableString.Invariant( $", {1 + Others.Length} online figures ({presence.Sparkles} sparkles, {session.Moves} places sent{(clicks > 0 ? $", walked {presence.State.Own.DistanceWalked:F1} cells after a HUD click" : "")}{(session.Said.Count > 0 ? ", a chat line said through Enter" : "")})" );
				return;
		}
	}

	/// <summary>The scripted players' places at <paramref name="seconds"/>: Ann circles the entrance, Bob stands beside it, Carla teleports after two seconds.</summary>
	private void Script( float seconds )
	{
		var angle = seconds * MathF.Tau / 8f;
		session.Place( "Ann", start.X + 3f * MathF.Cos( angle ), start.Y + 3f * MathF.Sin( angle ), width, height );
		session.Place( "Bob", start.X + 2f, start.Y + 1.5f, width, height );
		session.Place( "Carla", seconds < 2f ? start.X - 2f : start.X - 4f, seconds < 2f ? start.Y + 2f : start.Y + 4f, width, height );
	}

	private static void Require( bool condition, string step )
	{
		if ( !condition )
			throw new InvalidOperationException( "Read-only visit smoke failed (online players): " + step );
	}

	/// <summary>A connected session in the park's room, without a server.</summary>
	private sealed class Session : IPresenceSession
	{
		private readonly Dictionary<string, (float X, float Y)> positions = new( StringComparer.OrdinalIgnoreCase );
		private readonly object connection = new();
		public bool IsLoggedIn => true;
		public string? PlayerName => VisitPresenceSmoke.PlayerName;
		public object? Connection => connection;
		public bool ChatBusy => false;
		public string CurrentRoom { get; private set; } = ChatProtocol.LobbyRoom;
		public string WantedRoom => CurrentRoom;
		public string? RefusedRoom => null;
		public int? ServerProtocol => ParkPresence.MovementProtocol;
		public IReadOnlyDictionary<string, (float X, float Y)> RoomPositions => positions;
		public IReadOnlyList<string> RoomPlayers { get; } = Others.Prepend( VisitPresenceSmoke.PlayerName ).ToArray();
		private readonly List<string> lines = new() { "Ann: hello from the smoke", "Bob: walk over here" };
		public IReadOnlyList<string> ChatLines => lines;
		public List<string> Said { get; } = new();
		public int Moves { get; private set; }
		public void ConnectChat() { }
		public void CloseChat() { }
		public void FetchServerInfo() { }
		public void JoinRoom( string room ) => CurrentRoom = room;
		public void MoveTo( float x, float y ) { positions[VisitPresenceSmoke.PlayerName] = (x, y); Moves++; }
		public void SendChatLine( string line )
		{
			Said.Add( line );
			lines.Add( $"{VisitPresenceSmoke.PlayerName}: {line}" );
		}

		public void Place( string name, float cellX, float cellY, int width, int height ) =>
			positions[name] = (Math.Clamp( cellX / width, 0f, 1f ), Math.Clamp( cellY / height, 0f, 1f ));
	}
}
