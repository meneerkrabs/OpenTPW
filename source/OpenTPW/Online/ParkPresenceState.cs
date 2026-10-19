using OpenTPW.Online.Chat;

namespace OpenTPW;

/// <summary>The ground a park's online figures walk: the park's cell grid and which cells a figure may cross.</summary>
public interface IPresenceGround
{
	/// <summary>Cells across (the level MAP's cell count; the relayed positions are fractions of it).</summary>
	int Width { get; }
	/// <summary>Cells down.</summary>
	int Height { get; }
	bool IsPassable( int x, int y );
}

/// <summary>One player's figure in a visited park, at the place it is drawn (cell coordinates, 0.5 = a cell's centre).</summary>
public sealed class PresenceAvatar
{
	public PresenceAvatar( string name, bool own, float x, float y )
	{
		Name = name;
		Own = own;
		Look = ParkPresenceState.LookOf( name );
		X = x;
		Y = y;
	}

	public string Name { get; }
	/// <summary>The player's own figure (the original's online person with +0x3A0 set).</summary>
	public bool Own { get; }
	/// <summary>A number every client derives alike from the name, choosing the figure's sprite set.</summary>
	public int Look { get; }
	public float X { get; internal set; }
	public float Y { get; internal set; }
	/// <summary>The direction it last moved in (ground cells, unit length); it faces down the map at first.</summary>
	public float HeadingX { get; internal set; }
	public float HeadingY { get; internal set; } = 1f;
	/// <summary>Cells walked so far, which steps the walk animation as a guest's distance does.</summary>
	public float DistanceWalked { get; internal set; }
	/// <summary>Drawn walking (the walk animation) rather than standing.</summary>
	public bool Moving { get; internal set; }
	internal float SinceMoved = float.PositiveInfinity;
	internal (float X, float Y) Target;
	/// <summary>The jumps of the player's figure seen so far (<see cref="OnlineSession.RoomJumps"/>).</summary>
	internal int Jumps;

	/// <summary>Moves the figure by (<paramref name="dx"/>, <paramref name="dy"/>) cells, turning it to face the way it went.</summary>
	internal void Step( float dx, float dy )
	{
		var length = MathF.Sqrt( dx * dx + dy * dy );
		if ( length <= 0f )
			return;
		X += dx;
		Y += dy;
		HeadingX = dx / length;
		HeadingY = dy / length;
		DistanceWalked += length;
		SinceMoved = 0f;
	}

	/// <summary>Keeps the walk animation for a moment after the last step, so a figure moved in short hops does not flicker between walking and standing.</summary>
	internal void Settle( float delta )
	{
		SinceMoved += delta;
		Moving = SinceMoved < ParkPresenceState.WalkHoldSeconds;
	}
}

/// <summary>
/// The players of a visited park's chat room as figures in the park (docs/ONLINE.md "Players in a visited park"): the player's own figure,
/// walked by clicks and sent to the server, and the others at the places the server relays. Pure state without rendering or network, so the
/// rules are testable; <see cref="ParkPresence"/> connects it to the level, the renderer and the session.
/// </summary>
// [BIN:TPW-EXE:0x50B453 online person tick] the person tick's case for thing type 0x12: in the online game type it calls 0x4F7C60 (the sparkle, 0x50B495) and 0x4F7C90 (the walk) once a world turn
// [BIN:TPW-EXE:0x4E7265 BOSS_CHAT_MOVE] a relayed move finds the online person by name (0x4F7D70: the name at +0x19C), never the player's own, and its Z selects walk (0, 0x4F7DE0), teleport (1, 0x4F7E20), hide (2, 0x4F9ED0) or unhide and teleport (3)
// [EXT:online-visit-presence] OpenTPW's server relays positions as fractions of the park (ChatProtocol "move"), sent several times a second while a figure walks, where the original sent the destination
// and every client walked the figure there itself; a remote figure follows the relayed places, and one that jumps further than TeleportDistance, or whose movement was a teleport, is
// moved there at once
public sealed class ParkPresenceState
{
	/// <summary>The own figure's walking speed, cells per second.</summary>
	// [EXT:online-avatar-walk] about a quick guest's pace (GuestSpeed: 0.2 cell a 248 ms turn at base speed 100, 140 the fastest); the online person's own speed (the walk step 0x4FA2A0 of its route) is not read
	public const float WalkSpeed = 1.2f;
	/// <summary>Shortest gap between two positions sent while walking, seconds (the chat park view's interval).</summary>
	public const float SendInterval = UI.UiChatParkView.SendInterval;
	/// <summary>A relayed place further than this (cells) from where a figure is drawn moves it there at once.</summary>
	public const float TeleportDistance = 3f;
	/// <summary>The fastest a remote figure is moved toward its relayed place, cells per second.</summary>
	public const float CatchUpSpeed = 6f;
	/// <summary>A remote figure reaches its relayed place within about this long (the relay interval plus a frame or two).</summary>
	public const float CatchUpSeconds = 0.15f;
	/// <summary>How long a figure keeps its walk animation after its last step.</summary>
	public const float WalkHoldSeconds = 0.25f;
	/// <summary>A second click within this many seconds and <see cref="DoubleClickCells"/> of the first teleports (manual p. 68: "Double click to teleport").</summary>
	public const double DoubleClickSeconds = 0.4;
	public const float DoubleClickCells = 1.5f;
	/// <summary>The smallest movement (cells) worth sending.</summary>
	public const float SendThreshold = 0.02f;
	/// <summary>
	/// A figure standing still sends its place again after this many seconds: a dropped arrival (over the server's movement limit) or a server that did
	/// not answer the room's first movement with the others' places is repaired within it.
	/// </summary>
	public const float KeyframeSeconds = 10f;
	/// <summary>How far round an impassable clicked cell the walk looks for a passable one, in cells.</summary>
	public const int GoalSearchRadius = 3;
	/// <summary>
	/// Cells the route search expands at most before it gives up and walks straight. A spot that cannot be reached at all (an island, a fenced corner) is
	/// answered without a search, by the ground's connected areas (<see cref="RouteSearch"/>).
	/// </summary>
	public const int RouteSearchLimit = 40000;

	private readonly IPresenceGround ground;
	private readonly List<PresenceAvatar> others = new();
	private readonly HashSet<string> present = new( StringComparer.OrdinalIgnoreCase );
	private readonly Queue<(float X, float Y)> route = new();
	/// <summary>The route search's arrays and the ground's areas, made once for the park (a visit's ground does not change) and reused by every click.</summary>
	private RouteSearch? routeSearch;
	private (float X, float Y) lastSent;
	private bool sentOnce;
	private float sinceSent = float.PositiveInfinity;
	private bool arrived;
	private (double Time, float X, float Y)? lastClick;
	/// <summary>How the next place goes out when it is not a plain walk: a teleport, the hiding as a ride starts, the appearance as it ends.</summary>
	private ChatMoveMode? pendingMode;
	/// <summary>Where the ride being ridden lets the figure out (cells), when it has an exit.</summary>
	private (float X, float Y)? rideExit;

	public ParkPresenceState( IPresenceGround ground, string playerName, (float X, float Y) start )
	{
		this.ground = ground;
		Own = new PresenceAvatar( playerName, true, start.X, start.Y ) { Target = start };
		// [BIN:TPW-EXE:0x4E5EF0 own person] the figure enters the park as a teleport: MovePlayer(…, 1)
		pendingMode = ChatMoveMode.Teleport;
	}

	public PresenceAvatar Own { get; }
	/// <summary>The other players with a known place, in the order they first appeared.</summary>
	public IReadOnlyList<PresenceAvatar> Others => others;
	/// <summary>Every figure: the player's own first.</summary>
	public IEnumerable<PresenceAvatar> All => others.Prepend( Own );
	/// <summary>The figures drawn: every figure but the player's own while they ride.</summary>
	public IEnumerable<PresenceAvatar> Shown => Riding ? others : All;
	/// <summary>Whether the own figure is on its way somewhere.</summary>
	public bool Walking => route.Count > 0;
	/// <summary>The player rides a ride (<see cref="SetRiding"/>): the own figure is hidden where it stood.</summary>
	public bool Riding { get; private set; }
	/// <summary>How the place <see cref="Advance"/> returned last moved the figure.</summary>
	public ChatMoveMode SentMode { get; private set; }

	/// <summary>
	/// The player boards or leaves a ride: while they ride, the own figure is hidden where it stood and its walk is given up; when the ride ends it
	/// appears at the ride's exit (<paramref name="exit"/>, given as the ride starts), or where it stood when the ride has none. Each change goes out at once
	/// (<see cref="Advance"/>), and clicks do not move the figure meanwhile.
	/// </summary>
	// [BIN:TPW-EXE:0x4BD090 first-person entry] entering a ride's view hides the own person locally (0x4F9ED0) and sends BOSS_CHAT_MOVE mode 2; a ride entered from the walk ends at the ride's exit cell (0x42A1D0, the thing's +0x38), and the next tick sends mode 3 there (0x4EA410)
	// [EXT:online-ride-hiding] every ride ends with the figure shown again at the ride's exit (Unhide): the original's return to the overview from a ride boarded there sends only a teleport (mode 1, 0x4BD260 → 0x4EA2D0), which leaves the hidden person hidden for the others
	public void SetRiding( bool riding, (float X, float Y)? exit = null )
	{
		if ( riding == Riding )
			return;
		Riding = riding;
		route.Clear();
		lastClick = null;
		if ( riding )
			rideExit = exit;
		else if ( rideExit is { } place )
			(Own.X, Own.Y) = ClampToPark( place.X, place.Y );
		if ( !riding )
			rideExit = null;
		Own.Target = (Own.X, Own.Y);
		Own.SinceMoved = float.PositiveInfinity;
		Own.Moving = false;
		pendingMode = riding ? ChatMoveMode.Hide : ChatMoveMode.Unhide;
	}

	/// <summary>
	/// The park position of a relayed place: both axes 0 to 1 across the park's MAP cells (the chat park view's square), so a player in the
	/// overhead chat view and one in the visit stand at the same relative place.
	/// </summary>
	public (float X, float Y) FromWire( (float X, float Y) wire ) =>
		(Math.Clamp( wire.X, 0f, 1f ) * ground.Width, Math.Clamp( wire.Y, 0f, 1f ) * ground.Height);

	/// <summary>The relayed place of a park position (<see cref="FromWire"/> undone, clamped to the park).</summary>
	public (float X, float Y) ToWire( float x, float y ) =>
		(Math.Clamp( x / Math.Max( 1, ground.Width ), 0f, 1f ), Math.Clamp( y / Math.Max( 1, ground.Height ), 0f, 1f ));

	/// <summary>
	/// A click on the park's ground: the own figure walks there, or teleports when it is the second click of a double click.
	/// </summary>
	/// <param name="time">The click's time in seconds (any monotonic clock).</param>
	/// <returns>True when the click teleported the figure.</returns>
	public bool Click( float x, float y, double time )
	{
		if ( Riding )
			return false;
		var previous = lastClick;
		lastClick = (time, x, y);
		if ( previous is { } first && time - first.Time <= DoubleClickSeconds
			&& MathF.Abs( first.X - x ) <= DoubleClickCells && MathF.Abs( first.Y - y ) <= DoubleClickCells )
		{
			lastClick = null;
			TeleportTo( x, y );
			return true;
		}
		WalkTo( x, y );
		return false;
	}

	/// <summary>
	/// Plans the own figure's walk to (<paramref name="x"/>, <paramref name="y"/>): cell centre to cell centre round what blocks the way, then to the
	/// exact point; straight there when no route exists.
	/// </summary>
	// [BIN:TPW-EXE:0x50F8E0 person route] the walk goes through the route 0x511420 finds, its cells' centres in turn, with the octile distance |dx| + |dy| - min / 2, and moves straight to the point when there is none (0x70000000)
	// [EXT:online-avatar-route] the cells a figure may cross are OpenTPW's choice (ParkPresence.PresenceGround: land, paths, queues and entrances, not water, objects or the fence); the route map the original passes (+0xB4) is not read
	public void WalkTo( float x, float y )
	{
		if ( Riding )
			return;
		var (goalX, goalY) = ClampToPark( x, y );
		route.Clear();
		arrived = false;
		var start = ((int)MathF.Floor( Own.X ), (int)MathF.Floor( Own.Y ));
		var goal = ((int)MathF.Floor( goalX ), (int)MathF.Floor( goalY ));
		if ( !Passable( goal.Item1, goal.Item2 ) )
		{
			if ( NearestPassable( goal ) is not { } nearer )
				return;
			goal = nearer;
			(goalX, goalY) = (goal.Item1 + 0.5f, goal.Item2 + 0.5f);
		}
		routeSearch ??= new RouteSearch( ground.Width, ground.Height );
		var cells = routeSearch.Find( ground, start, goal );
		if ( cells != null )
			foreach ( var cell in Simplify( cells ) )
				if ( cell != start && cell != goal )
					route.Enqueue( (cell.X + 0.5f, cell.Y + 0.5f) );
		route.Enqueue( (goalX, goalY) );
		Own.Target = (goalX, goalY);
	}

	/// <summary>Moves the own figure to (<paramref name="x"/>, <paramref name="y"/>) at once.</summary>
	// [BIN:TPW-EXE:0x4F7E20 teleport] the person stops walking (+0x39C = 0), takes animation state 3 and is set at the point (0x4FA930)
	public void TeleportTo( float x, float y )
	{
		if ( Riding )
			return;
		var (goalX, goalY) = ClampToPark( x, y );
		route.Clear();
		Own.X = goalX;
		Own.Y = goalY;
		Own.Target = (goalX, goalY);
		Own.SinceMoved = float.PositiveInfinity;
		Own.Moving = false;
		arrived = true;
		pendingMode = ChatMoveMode.Teleport;
	}

	/// <summary>
	/// Walks the own figure on for <paramref name="delta"/> seconds and returns the relayed place to send now, or null. A place goes out when
	/// <paramref name="announce"/> asks (the first in a room on each connection: the server answers it with where the others stand and relays
	/// movement only to players that have moved), when the figure arrives or teleports, when a ride starts or ends, and every <see cref="SendInterval"/>
	/// while it walks. <see cref="SentMode"/> tells how the place moved the figure: hidden while riding, a teleport or the appearance after a ride once.
	/// </summary>
	/// <param name="canSend">The session is in the park's room and connected; without it nothing is sent and pending places wait.</param>
	public (float X, float Y)? Advance( float delta, bool canSend, bool announce )
	{
		var budget = WalkSpeed * Math.Max( 0f, delta );
		while ( budget > 0f && route.TryPeek( out var next ) )
		{
			var dx = next.X - Own.X;
			var dy = next.Y - Own.Y;
			var distance = MathF.Sqrt( dx * dx + dy * dy );
			if ( distance <= budget )
			{
				Own.Step( dx, dy );
				budget -= distance;
				route.Dequeue();
				if ( route.Count == 0 )
					arrived = true;
			}
			else
			{
				Own.Step( dx / distance * budget, dy / distance * budget );
				budget = 0f;
			}
		}
		Own.Settle( delta );
		sinceSent += Math.Max( 0f, delta );
		if ( !canSend )
			return null;
		var moved = MathF.Abs( Own.X - lastSent.X ) + MathF.Abs( Own.Y - lastSent.Y );
		var due = announce || !sentOnce || pendingMode != null
			|| (arrived && moved > 0f)
			|| (sinceSent >= SendInterval && moved > SendThreshold)
			|| sinceSent >= KeyframeSeconds;
		if ( !due )
			return null;
		// a figure hidden while riding stays hidden in every place it sends, the announcement and the keyframes included
		SentMode = Riding ? ChatMoveMode.Hide : pendingMode ?? ChatMoveMode.Walk;
		pendingMode = null;
		sentOnce = true;
		sinceSent = 0f;
		arrived = false;
		lastSent = (Own.X, Own.Y);
		return ToWire( Own.X, Own.Y );
	}

	/// <summary>
	/// Updates the other players' figures from the room's relayed places for <paramref name="delta"/> seconds: new players appear at their place,
	/// known ones move toward theirs, and players that left the room (no longer in <paramref name="players"/>) disappear.
	/// </summary>
	/// <param name="positions">Relayed places by player name (the session's room positions).</param>
	/// <param name="players">The room's roster; a roster cut short with "+N" (a busy room) does not say who is absent, so it removes nobody.</param>
	/// <param name="jumps">How often each figure jumped (the session's room jumps): a figure whose count changed is moved to its place at once.</param>
	public void Sync( IReadOnlyDictionary<string, (float X, float Y)> positions, IReadOnlyCollection<string> players, float delta, IReadOnlyDictionary<string, int>? jumps = null )
	{
		present.Clear();
		var complete = true;
		foreach ( var player in players )
		{
			if ( IsHidden( player ) )
				complete = false;
			else
				present.Add( player );
		}
		others.RemoveAll( avatar => (complete && !present.Contains( avatar.Name )) || !positions.ContainsKey( avatar.Name ) );
		foreach ( var (name, wire) in positions )
		{
			if ( (complete && !present.Contains( name )) || IsHidden( name ) || name.Equals( Own.Name, StringComparison.OrdinalIgnoreCase ) )
				continue;
			var target = FromWire( wire );
			var jumped = jumps != null && jumps.TryGetValue( name, out var count ) ? count : 0;
			var avatar = others.Find( other => other.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) );
			if ( avatar == null )
			{
				others.Add( new PresenceAvatar( name, false, target.X, target.Y ) { Target = target, Jumps = jumped } );
				continue;
			}
			avatar.Target = target;
			var dx = target.X - avatar.X;
			var dy = target.Y - avatar.Y;
			var distance = MathF.Sqrt( dx * dx + dy * dy );
			if ( distance > TeleportDistance || jumped != avatar.Jumps )
			{
				avatar.X = target.X;
				avatar.Y = target.Y;
				avatar.SinceMoved = float.PositiveInfinity;
				avatar.Jumps = jumped;
			}
			else if ( distance > 0f )
			{
				var step = Math.Min( distance, Math.Clamp( distance / CatchUpSeconds, WalkSpeed, CatchUpSpeed ) * Math.Max( 0f, delta ) );
				avatar.Step( dx / distance * step, dy / distance * step );
			}
			avatar.Settle( delta );
		}
	}

	/// <summary>The roster's "+N" entry for the names a busy room's roster leaves out (ChatHub.RosterEvent), which the chat lists hide.</summary>
	public static bool IsHidden( string name ) => name.StartsWith( "+", StringComparison.Ordinal );

	/// <summary>
	/// The figure's look from the player's name: FNV-1a over the lower-case UTF-8 name, so every client chooses the same sprite set for a player
	/// (string.GetHashCode differs between processes).
	/// </summary>
	// [EXT:online-avatar-look] the online person's own model (MODEL_ONLINE_PERSON) is not decoded; the figures wear the guests' kid sets, chosen from the name
	public static int LookOf( string name )
	{
		var hash = 2166136261u;
		foreach ( var value in System.Text.Encoding.UTF8.GetBytes( name.ToLowerInvariant() ) )
			hash = unchecked((hash ^ value) * 16777619u);
		return (int)(hash & 0x7FFFFFFF);
	}

	/// <summary>
	/// A route of cells from <paramref name="from"/> to <paramref name="to"/> (both included) over <see cref="IPresenceGround.IsPassable"/> cells, eight ways
	/// without cutting a blocked corner, shortest by the octile distance (a straight step 2, a diagonal one 3); the start cell is always left.
	/// Null when there is none or the search exceeds <see cref="RouteSearchLimit"/>.
	/// </summary>
	public static IReadOnlyList<(int X, int Y)>? Route( IPresenceGround ground, (int X, int Y) from, (int X, int Y) to ) =>
		new RouteSearch( ground.Width, ground.Height ).Find( ground, from, to );

	/// <summary>
	/// The route search (A* with the octile distance) over flat arrays the size of the park, made once and reused: a search marks the cells it reaches
	/// with its own number, so no array is cleared between clicks. The ground's connected areas are labelled on the first search, so a goal outside the
	/// start's area is answered at once; the ground must not change while the search is kept.
	/// </summary>
	internal sealed class RouteSearch
	{
		private readonly int width, height;
		private readonly int[] cost, parent, reached, closed;
		private readonly PriorityQueue<int, int> open = new();
		/// <summary>Each cell's connected area (0: a blocked cell), labelled once.</summary>
		private int[]? areas;
		private int search;

		public RouteSearch( int width, int height )
		{
			this.width = Math.Max( 0, width );
			this.height = Math.Max( 0, height );
			var cells = this.width * this.height;
			cost = new int[cells];
			parent = new int[cells];
			reached = new int[cells];
			closed = new int[cells];
		}

		/// <summary>The cells the last search expanded.</summary>
		public int Expanded { get; private set; }

		private bool Inside( int x, int y ) => (uint)x < (uint)width && (uint)y < (uint)height;

		/// <summary>Whether a figure may step from (<paramref name="x"/>, <paramref name="y"/>) by (<paramref name="dx"/>, <paramref name="dy"/>): onto a free cell, and diagonally only with both cells beside the step free.</summary>
		private bool Steps( IPresenceGround ground, int x, int y, int dx, int dy )
		{
			int nx = x + dx, ny = y + dy;
			if ( !Inside( nx, ny ) || !ground.IsPassable( nx, ny ) )
				return false;
			// a diagonal step needs both cells beside it free, so a figure does not squeeze between two blocked corners
			return dx == 0 || dy == 0 || (ground.IsPassable( x + dx, y ) && ground.IsPassable( x, y + dy ));
		}

		public IReadOnlyList<(int X, int Y)>? Find( IPresenceGround ground, (int X, int Y) from, (int X, int Y) to, int limit = RouteSearchLimit )
		{
			Expanded = 0;
			if ( ground.Width != width || ground.Height != height || !Inside( from.X, from.Y ) || !Inside( to.X, to.Y ) || !ground.IsPassable( to.X, to.Y ) )
				return null;
			if ( from == to )
				return new[] { from };
			if ( !Reaches( ground, from, to ) )
				return null;
			if ( ++search == int.MaxValue )
			{
				// the marks wrap after two billion searches: start them over
				Array.Clear( reached );
				Array.Clear( closed );
				search = 1;
			}
			int Heuristic( int x, int y )
			{
				int ax = Math.Abs( x - to.X ), ay = Math.Abs( y - to.Y );
				return 2 * (ax + ay) - Math.Min( ax, ay );
			}
			open.Clear();
			var first = from.Y * width + from.X;
			cost[first] = 0;
			parent[first] = -1;
			reached[first] = search;
			open.Enqueue( first, Heuristic( from.X, from.Y ) );
			while ( open.TryDequeue( out var current, out _ ) )
			{
				if ( closed[current] == search )
					continue;
				closed[current] = search;
				if ( ++Expanded > limit )
					return null;
				int cx = current % width, cy = current / width;
				if ( cx == to.X && cy == to.Y )
				{
					var cells = new List<(int X, int Y)>();
					for ( var at = current; at >= 0; at = parent[at] )
						cells.Add( (at % width, at / width) );
					cells.Reverse();
					return cells;
				}
				for ( var dy = -1; dy <= 1; dy++ )
					for ( var dx = -1; dx <= 1; dx++ )
					{
						if ( (dx == 0 && dy == 0) || !Steps( ground, cx, cy, dx, dy ) )
							continue;
						var next = (cy + dy) * width + cx + dx;
						var nextCost = cost[current] + (dx != 0 && dy != 0 ? 3 : 2);
						if ( reached[next] == search && cost[next] <= nextCost )
							continue;
						reached[next] = search;
						cost[next] = nextCost;
						parent[next] = current;
						open.Enqueue( next, nextCost + Heuristic( cx + dx, cy + dy ) );
					}
			}
			return null;
		}

		/// <summary>Whether <paramref name="to"/> lies in an area the figure at <paramref name="from"/> can walk into (from a blocked cell: by any step out of it).</summary>
		private bool Reaches( IPresenceGround ground, (int X, int Y) from, (int X, int Y) to )
		{
			areas ??= Label( ground );
			var goal = areas[to.Y * width + to.X];
			if ( goal == 0 )
				return false;
			if ( areas[from.Y * width + from.X] == goal )
				return true;
			for ( var dy = -1; dy <= 1; dy++ )
				for ( var dx = -1; dx <= 1; dx++ )
					if ( (dx != 0 || dy != 0) && Steps( ground, from.X, from.Y, dx, dy ) && areas[(from.Y + dy) * width + from.X + dx] == goal )
						return true;
			return false;
		}

		/// <summary>Numbers the ground's connected areas by the search's own steps (one flood per area).</summary>
		private int[] Label( IPresenceGround ground )
		{
			var labels = new int[width * height];
			var queue = new Queue<int>();
			var area = 0;
			for ( var start = 0; start < labels.Length; start++ )
			{
				if ( labels[start] != 0 || !ground.IsPassable( start % width, start / width ) )
					continue;
				labels[start] = ++area;
				queue.Enqueue( start );
				while ( queue.TryDequeue( out var current ) )
				{
					int cx = current % width, cy = current / width;
					for ( var dy = -1; dy <= 1; dy++ )
						for ( var dx = -1; dx <= 1; dx++ )
						{
							if ( (dx == 0 && dy == 0) || !Steps( ground, cx, cy, dx, dy ) )
								continue;
							var next = (cy + dy) * width + cx + dx;
							if ( labels[next] != 0 )
								continue;
							labels[next] = area;
							queue.Enqueue( next );
						}
				}
			}
			return labels;
		}
	}

	/// <summary>The route without the cells in the middle of a straight run (the figure walks the same line through fewer points).</summary>
	internal static IEnumerable<(int X, int Y)> Simplify( IReadOnlyList<(int X, int Y)> cells )
	{
		for ( var index = 0; index < cells.Count; index++ )
		{
			if ( index > 0 && index < cells.Count - 1 )
			{
				var (px, py) = cells[index - 1];
				var (cx, cy) = cells[index];
				var (nx, ny) = cells[index + 1];
				if ( cx - px == nx - cx && cy - py == ny - cy )
					continue;
			}
			yield return cells[index];
		}
	}

	private bool Passable( int x, int y ) => (uint)x < (uint)ground.Width && (uint)y < (uint)ground.Height && ground.IsPassable( x, y );

	/// <summary>The passable cell nearest <paramref name="goal"/> within <see cref="GoalSearchRadius"/> (ring by ring, then by distance), or null.</summary>
	private (int, int)? NearestPassable( (int X, int Y) goal )
	{
		for ( var radius = 1; radius <= GoalSearchRadius; radius++ )
		{
			(int, int)? best = null;
			var bestDistance = int.MaxValue;
			for ( var y = goal.Y - radius; y <= goal.Y + radius; y++ )
				for ( var x = goal.X - radius; x <= goal.X + radius; x++ )
				{
					if ( Math.Max( Math.Abs( x - goal.X ), Math.Abs( y - goal.Y ) ) != radius || !Passable( x, y ) )
						continue;
					var distance = (x - goal.X) * (x - goal.X) + (y - goal.Y) * (y - goal.Y);
					if ( distance < bestDistance )
					{
						best = (x, y);
						bestDistance = distance;
					}
				}
			if ( best != null )
				return best;
		}
		return null;
	}

	private (float X, float Y) ClampToPark( float x, float y ) =>
		(Math.Clamp( float.IsFinite( x ) ? x : Own.X, 0f, Math.Max( 0, ground.Width ) - 0.001f ),
		 Math.Clamp( float.IsFinite( y ) ? y : Own.Y, 0f, Math.Max( 0, ground.Height ) - 0.001f ));
}
