using OpenTPW.Hud;
using OpenTPW.Online.Chat;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// The visit smoke's online players (<c>--visit-park … --smoke-test --smoke-presence</c>): a session without a server whose room holds three scripted
/// players round the park entrance (one walks a circle, one stands, one teleports), so the native run checks that the figures are drawn with their
/// names and sparkles, that a click on the ground walks the player's own figure through the HUD and sends its places, and that a click on a ride
/// rides it with the figure hidden until the ride ends.
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
	private OriginalObject? riddenRide;
	private (Vector3 Target, float Height, float Yaw)? poseBeforeRide;
	private (float X, float Y) standBeforeRide;

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
		var smoke = ParkPresence.CreateFor( session, level, ChatProtocol.ParkRoom( ParkId ) ) is { } presence ? new VisitPresenceSmoke( level, presence, cells, session ) : null;
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
	public void Update( float delta, Action<UiInput>? injectInput, ParkHud? hud, UiContext? context = null )
	{
		time += delta;
		Script( time );
		// the ride view hides the interface: only the riding checks run meanwhile
		if ( Completed || (level.HidesInterface && phase != 14) )
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
				Require( hud == null || hud.ChatMode, "a visit with other players starts in the chat mode" );
				Require( hud?.VisitChatPanel is not { } panel || panel.People.Count == 1 + Others.Length, "the people list names the park's players" );
				phase = 1;
				phaseFrames = 0;
				Log.Trace( $"Visit smoke: {level.GuestRenderer?.DrawnFigures} online figures drawn, {presence.Sparkles} sparkles." );
				return;
			case 1:
				// a click on free ground a few cells from the figure, through the HUD's park click
				if ( injectInput == null || hud == null )
				{
					phase = 20;
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
				// Enter over the park focuses the chat overlay's entry, the typed line goes to the room and the entry keeps the keys
				if ( phaseFrames < 10 )
					return;
				if ( injectInput == null || hud == null || context == null )
				{
					phase = 20;
					return;
				}
				injectInput( UiInput.Key( UiKeys.Accept ) );
				phase = 4;
				phaseFrames = 0;
				return;
			case 4:
				if ( phaseFrames < 3 )
					return;
				Require( hud!.ChatMode && hud.ChatEntryFocused, $"Enter over the park focuses the chat entry (chat mode {hud.ChatMode}, top screen {hud.Stack.Top?.Name})" );
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
				Require( hud!.VisitChatPanel?.Entry.Text.Length == 0 && hud.ChatEntryFocused, "Enter empties the entry and keeps it" );
				// Tab leaves the entry
				injectInput!( UiInput.Key( UiKeys.Tab ) );
				phase = 7;
				phaseFrames = 0;
				return;
			case 7:
				if ( phaseFrames < 3 )
					return;
				Require( !hud!.ChatEntryFocused, "Tab leaves the chat entry" );
				// a right click on Bob, no buddy, opens the four-button popup at the pointer
				Require( RowPoint( hud, context!, "Bob" ) != null, "Bob's row is in the people list" );
				injectInput!( new UiInput( RowPoint( hud, context!, "Bob" )!.Value, false, false, false, true, UiKeys.None ) );
				phase = 8;
				phaseFrames = 0;
				return;
			case 8:
				if ( phaseFrames < 3 )
					return;
				Require( hud!.Stack.Top?.Name == UI.ChatPanel.PopupScreenName && hud.Stack.Top.Find( "popupBuddy" ) != null && hud.Stack.Top.Find( "popupPage" ) == null,
					$"the right click opens the player popup (top screen {hud.Stack.Top?.Name})" );
				Require( hud.Stack.Top!.Find( "popupIgnore" ) != null, "the popup has its ignore button" );
				injectInput!( UiInput.Click( hud.Stack.Top!.Find( "popupIgnore" )!.ScreenRect( context!.Canvas ).Center ) );
				phase = 9;
				phaseFrames = 0;
				return;
			case 9:
				if ( phaseFrames < 3 )
					return;
				Require( session.Commands.Contains( (ChatCommand.Ignore, "Bob") ), "the popup's ignore button sends the command for the name" );
				Require( hud!.Stack.Top?.Name != UI.ChatPanel.PopupScreenName, "a popup button closes the popup" );
				Require( hud.VisitChatPanel!.People.Single( person => person.Name == "Bob" ).Flags.HasFlag( ChatPersonFlags.Ignored ), "the ignored name is flagged" );
				// F4 switches to the HUD and back
				hud.ToggleChatMode();
				Require( !hud.ChatMode && hud.Stack.Screens.Count == 1 && ReferenceEquals( hud.Stack.Screens[0], hud.Screen ), "F4 switches the visit to the HUD" );
				hud.ToggleChatMode();
				Require( hud.ChatMode, "F4 switches back to the chat mode" );
				// a right click on Ann, a buddy in the park, opens the six-button popup
				injectInput!( new UiInput( RowPoint( hud, context!, "Ann" )!.Value, false, false, false, true, UiKeys.None ) );
				phase = 10;
				phaseFrames = 0;
				return;
			case 10:
				if ( phaseFrames < 4 )
					return;
				Require( hud!.Stack.Top?.Name == UI.ChatPanel.PopupScreenName && hud.Stack.Top.Find( "popupPage" ) != null && hud.Stack.Top.Find( "popupLocate" ) != null,
					"a buddy's right click opens the buddy popup" );
				SandboxSmokeTest.CaptureOutputFrame( "online-visit-chat-popup.png" );
				// a click beside the popup closes it
				injectInput!( new UiInput( new System.Numerics.Vector2( context!.Canvas.Width - 2, 2 ), false, true, false, false, UiKeys.None ) );
				phase = 11;
				phaseFrames = 0;
				return;
			case 11:
				if ( phaseFrames < 3 )
					return;
				Require( hud!.Stack.Top?.Name != UI.ChatPanel.PopupScreenName, "a click beside the popup closes it" );
				phase = 12;
				phaseFrames = 0;
				return;
			case 12:
				// a click on a rideable ride in the chat mode rides it (0x4BD7F4 → 0x4E15B0): the camera looks at the park's first one
				riddenRide = level.Objects.Objects.Select( ParkHud.ChatModeRide ).FirstOrDefault( ride => ride != null && RideCell( ride ) != null );
				Require( riddenRide != null && Camera.Mode is ParkCameraMode && level.OriginalPark?.Heightfield != null, "the park has a ride to ride from the chat mode" );
				var rideCamera = (ParkCameraMode)Camera.Mode;
				var rideField = level.OriginalPark!.Heightfield!;
				poseBeforeRide = (rideCamera.Target, rideCamera.Height, rideCamera.Yaw);
				standBeforeRide = (presence.State.Own.X, presence.State.Own.Y);
				var rideCell = RideCell( riddenRide! )!.Value;
				var centre = GuestRenderer.CellToEngine( rideField, rideCell.X + 0.5f, rideCell.Y + 0.5f );
				rideCamera.SetPose( new Vector3( centre.X, centre.Y, centre.Z ), rideCamera.Height, rideCamera.Yaw );
				phase = 13;
				phaseFrames = 0;
				return;
			case 13:
				if ( phaseFrames < 10 )
					return;
				var onScreen = RideCell( riddenRide! )!.Value;
				Require( level.TryProjectCell( onScreen.X, onScreen.Y, new System.Numerics.Vector2( Render.OverlayFramebuffer.Width, Render.OverlayFramebuffer.Height ), out var ridePoint ),
					"the ride is on screen" );
				injectInput!( UiInput.Click( ridePoint ) );
				Log.Trace( FormattableString.Invariant( $"Visit smoke: clicking {riddenRide!.Entry.DisplayName} at cell {onScreen.X},{onScreen.Y} in the chat mode." ) );
				phase = 14;
				phaseFrames = 0;
				return;
			case 14:
				// riding: the player's own figure is hidden for the room and not drawn, the others are; the ride view's screen covers the chat overlay
				if ( phaseFrames < 20 )
					return;
				Require( hud!.IsRiding && ReferenceEquals( level.RideView?.Ride, riddenRide ), $"a chat-mode click on the ride rides it (riding {hud.IsRiding})" );
				Require( hud.Stack.Top?.Name == "rideView", $"the ride view's screen covers the chat overlay (top screen {hud.Stack.Top?.Name})" );
				Require( presence.State.Riding && session.Modes.Contains( ChatMoveMode.Hide ), "boarding hides the player's figure for the room" );
				Require( level.GuestRenderer?.DrawnFigures == Others.Length, $"the riding player's own figure is not drawn ({level.GuestRenderer?.DrawnFigures})" );
				// the ride view's own exits work over the chat mode: a right click after two seconds (RMB cancel), else Escape
				if ( GameOptions.Current.RmbCancel && !ParkHud.RideRightClickReady( Time.Now - level.RideViewOpenedAt ) )
					return;
				var screenMiddle = new System.Numerics.Vector2( context!.Canvas.Width / 2, context.Canvas.Height / 2 );
				injectInput!( GameOptions.Current.RmbCancel ? new UiInput( screenMiddle, false, false, false, true, UiKeys.None ) : UiInput.Key( UiKeys.Back ) );
				phase = 15;
				phaseFrames = 0;
				return;
			case 15:
				if ( phaseFrames < 10 )
					return;
				Require( !hud!.IsRiding && !presence.State.Riding, $"the ride view's {(GameOptions.Current.RmbCancel ? "right click" : "Escape")} left the ride" );
				Require( session.Modes[^1] is ChatMoveMode.Unhide or ChatMoveMode.Walk && session.Modes.Contains( ChatMoveMode.Unhide ), "the ride's end shows the figure again" );
				if ( ParkPresence.ExitOf( riddenRide! ) is { } exit )
					Require( MathF.Abs( presence.State.Own.X - exit.X ) + MathF.Abs( presence.State.Own.Y - exit.Y ) < 0.01f, "the figure comes out at the ride's exit" );
				Require( hud.ChatMode && hud.Stack.Screens.Count == 1, "the chat overlay is back after the ride" );
				// the capture shows the entrance again, with the player's figure teleported back to where it stood
				if ( poseBeforeRide is { } pose && Camera.Mode is ParkCameraMode back )
					back.SetPose( pose.Target, pose.Height, pose.Yaw );
				presence.State.TeleportTo( standBeforeRide.X, standBeforeRide.Y );
				phase = 16;
				phaseFrames = 0;
				return;
			case 16:
				if ( phaseFrames < 5 )
					return;
				Require( session.Modes[^1] == ChatMoveMode.Teleport, $"a teleport goes out as one ({session.Modes[^1]})" );
				phase = 20;
				phaseFrames = 0;
				return;
			case 20:
				if ( phaseFrames < 5 )
					return;
				phase = 21;
				return;
			default:
				Completed = true;
				Report = FormattableString.Invariant( $", {1 + Others.Length} online figures ({presence.Sparkles} sparkles, {session.Moves} places sent{(clicks > 0 ? $", walked {presence.State.Own.DistanceWalked:F1} cells after a click" : "")}{(session.Said.Count > 0 ? ", a chat line said in the overlay's entry" : "")}{(session.Commands.Count > 0 ? $", {session.Commands.Count} popup command(s)" : "")}{(riddenRide != null ? $", rode {riddenRide.Entry.DisplayName} from the chat mode hidden" : "")})" );
				return;
		}
	}

	/// <summary>A cell of <paramref name="ride"/>'s footprint near its placement, where a click picks it, or null.</summary>
	private (int X, int Y)? RideCell( OriginalObject ride )
	{
		for ( var radius = 0; radius <= 3; radius++ )
			for ( var dy = -radius; dy <= radius; dy++ )
				for ( var dx = -radius; dx <= radius; dx++ )
					if ( Math.Max( Math.Abs( dx ), Math.Abs( dy ) ) == radius && ReferenceEquals( level.Objects.FindAt( ride.Placement.X + dx, ride.Placement.Y + dy ), ride ) )
						return (ride.Placement.X + dx, ride.Placement.Y + dy);
		return null;
	}

	/// <summary>A point of <paramref name="name"/>'s row in the chat overlay's people list (framebuffer pixels), or null.</summary>
	private static System.Numerics.Vector2? RowPoint( ParkHud hud, UiContext context, string name )
	{
		var index = hud.VisitChatPanel?.People.ToList().FindIndex( person => person.Name == name ) ?? -1;
		if ( index < 0 || hud.Stack.Screens[0].Find( "chatPeople" ) is not IUiRowList list || list is not UiElement element )
			return null;
		var rect = element.ScreenRect( context.Canvas );
		for ( var y = rect.Y + 1; y < rect.Bottom; y += 1 )
		{
			var point = new System.Numerics.Vector2( rect.X + rect.Width / 3, y );
			if ( list.RowAt( context.Canvas, point ) == index )
				return point with { Y = y + 3 };
		}
		return null;
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

	/// <summary>A connected session in the park's room, without a server: it answers the chat overlay's commands as the server would.</summary>
	private sealed class Session : IPresenceSession
	{
		private readonly Dictionary<string, (float X, float Y)> positions = new( StringComparer.OrdinalIgnoreCase );
		private readonly object connection = new();
		private readonly HashSet<string> ignored = new( StringComparer.OrdinalIgnoreCase );
		public bool IsLoggedIn => true;
		public string? PlayerName => VisitPresenceSmoke.PlayerName;
		public object? Connection => connection;
		public bool ChatBusy => false;
		public bool ChatConnecting => false;
		public bool ChatOpen => true;
		public string CurrentRoom { get; private set; } = ChatProtocol.LobbyRoom;
		public string WantedRoom => CurrentRoom;
		public bool InSharedPark => false;
		public string? RefusedRoom => null;
		public int? ServerProtocol => ParkPresence.MovementProtocol;
		public IReadOnlyDictionary<string, (float X, float Y)> RoomPositions => positions;
		public IReadOnlyDictionary<string, int> RoomJumps { get; } = new Dictionary<string, int>();
		public IReadOnlyList<string> RoomPlayers { get; } = Others.Prepend( VisitPresenceSmoke.PlayerName ).ToArray();
		/// <summary>Ann is a buddy in the park, Dave one who is offline.</summary>
		public IReadOnlyList<(string Name, bool Online)> Buddies { get; } = new[] { ("Ann", true), ("Dave", false) };
		public IReadOnlyCollection<string> Ignored => ignored;
		public bool Earmuffs { get; private set; }
		public bool CanVote { get; private set; } = true;
		private readonly List<string> lines = new() { "Ann: hello from the smoke", "Bob: walk over here" };
		public IReadOnlyList<string> ChatLines => lines;
		public List<string> Said { get; } = new();
		public List<(ChatCommand Command, string Argument)> Commands { get; } = new();
		public int Moves { get; private set; }
		public List<ChatMoveMode> Modes { get; } = new();
		public void ConnectChat() { }
		public void CloseChat() { }
		public void FetchServerInfo() { }
		public void JoinRoom( string room ) => CurrentRoom = room;
		public void MoveTo( float x, float y, ChatMoveMode mode = ChatMoveMode.Walk )
		{
			positions[VisitPresenceSmoke.PlayerName] = (x, y);
			Moves++;
			Modes.Add( mode );
		}
		public void SendChatLine( string line )
		{
			Said.Add( line );
			lines.Add( $"{VisitPresenceSmoke.PlayerName}: {line}" );
		}
		public void SendCommand( ChatCommand command, string argument )
		{
			Commands.Add( (command, argument) );
			if ( command == ChatCommand.Ignore && !ignored.Remove( argument ) )
				ignored.Add( argument );
			else if ( command == ChatCommand.Earmuffs )
				Earmuffs = !Earmuffs;
			else if ( command == ChatCommand.Vote )
				CanVote = false;
		}
		public void AskBuddies() { }

		public void Place( string name, float cellX, float cellY, int width, int height ) =>
			positions[name] = (Math.Clamp( cellX / width, 0f, 1f ), Math.Clamp( cellY / height, 0f, 1f ));
	}
}
