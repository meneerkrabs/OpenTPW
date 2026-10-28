namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-undo] The host's undo journal (docs/COOP.md, "Conflicts and undo (C5)"; the user's decision Q14: undo of a player's own last commands under the normal refund rules): every
/// member's steps of the building tools, and for each cell a step lies on, the records that changed it and are still in effect. It decides every take-back (<see cref="TakeBackCommand"/>)
/// before it runs: the step must be the member's own and its last of the tool, no later record still in effect may have changed its cells, and its tool's own undo, predicted on a copy of
/// the park, must do exactly the step's work back. It lives on the host alone, owned by the host's stream for the session: peers apply the records it decides, so it is in no join state and
/// no digest.
/// </summary>
internal sealed class CoopUndoJournal
{
	/// <summary>The most steps a member keeps (the oldest go first).</summary>
	internal const int MaximumSteps = 16384;
	/// <summary>The most cells a member's steps keep, footprints and changes together, a step without cells counting one.</summary>
	internal const int MaximumCells = 262144;
	/// <summary>
	/// The value <see cref="HistoryAt"/> reports for a permanent marker: a cell's record no take-back can remove any more (a command that is no step, a step forgotten, dropped or departed, a
	/// departed member's residue). A list's entries are typed: a record (0 or more), or a marker (below 0, <see cref="NewResidue"/>): a member's residue or a permanent marker
	/// (<see cref="Nobody"/>'s); a list is kept in its normal form (<see cref="Normalize"/>).
	/// </summary>
	private const long Permanent = -1;

	/// <summary>The member a permanent marker is of: no one's, so no take-back's own exception passes it.</summary>
	internal const uint Nobody = uint.MaxValue;

	/// <summary>
	/// The markers in the cells' lists, each its own entry (below 0): the member whose taken-back step (or whose take-back's own write) left it, <see cref="Nobody"/> for a permanent marker,
	/// and what it left changed on its cell. A residue is that step's record on a cell where an effect of it stayed. Every other member's step meets a marker as a change; the member's own
	/// earlier step whose inverse clears that cell whole passes over its own residue, since that inverse leaves nothing of it behind; and on a cell a step only counted up, a marker that left
	/// only link bits the step did not change passes too, a permanent one included (another line's link into the cell from elsewhere, laid by a line that ended its tool).
	/// </summary>
	private readonly Dictionary<long, (uint Member, Stayed Stayed)> residueOf = new();
	private long nextResidue = Permanent - 1;

	/// <summary>A new marker of <paramref name="member"/>'s (<see cref="Nobody"/>'s for a permanent one), which left <paramref name="stayed"/> changed on its cell.</summary>
	private long NewResidue( uint member, Stayed stayed )
	{
		var entry = nextResidue--;
		residueOf[entry] = (member, stayed);
		return entry;
	}

	/// <summary>A new permanent marker, which left <paramref name="stayed"/> changed on its cell.</summary>
	private long NewPermanent( Stayed stayed ) => NewResidue( Nobody, stayed );

	/// <summary>Whether <paramref name="entry"/> is a member's residue: a marker of a member present.</summary>
	private bool IsResidue( long entry ) => entry < 0 && residueOf[entry].Member != Nobody;

	/// <summary>What a change from <paramref name="was"/> to <paramref name="now"/> left changed: every field and link bit that differs.</summary>
	private static Stayed Whole( CellFields was, CellFields now ) => Stayed.Of( was, now, Differing( was, now ) );

	/// <summary>The bounds a member's steps are kept within (<see cref="MaximumSteps"/>, <see cref="MaximumCells"/>; a test lowers them).</summary>
	internal int StepBound { get; set; } = MaximumSteps;
	internal int CellBound { get; set; } = MaximumCells;

	/// <summary>[EXT:online-coop-undo] The most cells of each list a pool stroke's one inverse takes (<see cref="ParkCommandCodec.MaximumCells"/>, C5-2; a test lowers it).</summary>
	internal int StrokeCellLimit { get; set; } = ParkCommandCodec.MaximumCells;

	private readonly Level level;
	private readonly uint host;
	/// <summary>The cells the running command changed, with their fields before its first change of each (<see cref="ParkCellMap.Changed"/> between <see cref="Begin"/> and <see cref="Applied"/>).</summary>
	private readonly Dictionary<(int X, int Y), CellFields> running = new();
	/// <summary>[EXT:online-coop-undo] The track tiles the running command changed, with their records before its first change of each (<see cref="TrackCellLayer.Changed"/>, C5-3).</summary>
	private readonly Dictionary<(int X, int Y), TrackCell> runningTracks = new();
	/// <summary>Whether the command the next record is of started (<see cref="Begin"/>): one the rights refused before it ran changed nothing.</summary>
	private bool begun;
	/// <summary>For each tracked cell, the records still in effect that changed it, oldest first.</summary>
	private readonly Dictionary<(int X, int Y), List<long>> histories = new();
	/// <summary>How many live steps' footprints include each tracked cell.</summary>
	private readonly Dictionary<(int X, int Y), int> tracked = new();
	private readonly Dictionary<uint, Member> members = new();
	/// <summary>A live step of each record still in the cells' lists (a batch's pieces share theirs and its effects).</summary>
	private readonly Dictionary<long, Step> live = new();
	/// <summary>
	/// What a marker left changed on its cell: link bits, any other field (type, flags but a wave machine's, counter, owner, queue link, residue), and [EXT:online-coop-undo] a wave
	/// machine's bits (<see cref="ParkCellFlags.PoolWaveBits"/>, C5-2), kept apart so a river mark's take-back can pass a later wave setting; and of the cell's track tile (C5-3) its identity,
	/// counter or arms (type, base type, parent, owner, overlap counter, mask: what a count-down reads), and its other fields (flow link, flags, piece, step number), kept apart so a line's
	/// count-down can pass a later walk's refresh.
	/// </summary>
	private readonly record struct Stayed( byte Links, bool Fields, bool Waves = false, bool TrackTile = false, bool TrackShape = false )
	{
		/// <summary>Everything, for a change whose fields are not known.</summary>
		public static readonly Stayed Everything = new( 0xFF, true, true, true, true );

		public Stayed With( Stayed other ) => new( (byte)(Links | other.Links), Fields || other.Fields, Waves || other.Waves, TrackTile || other.TrackTile, TrackShape || other.TrackShape );

		/// <summary>
		/// [EXT:online-coop-undo] What differs between the track tile records <paramref name="was"/> and <paramref name="now"/> (C5-3): of the fields <paramref name="after"/> differs from
		/// <paramref name="was"/> in when given (what a step changed, bit by bit for the mask, link and flags), else of every field.
		/// </summary>
		public static Stayed OfTrack( TrackCell was, TrackCell now, TrackCell? after = null )
		{
			var changed = after ?? default;
			var all = after == null;
			bool Moved( int before, int then, int later ) => before != then && (all || before != later);
			bool Bits( int before, int then, int later ) => ((before ^ then) & (all ? 0xFF : before ^ later)) != 0;
			var tile = Moved( was.Type, now.Type, changed.Type ) || Moved( was.BaseType, now.BaseType, changed.BaseType ) || Moved( was.Parent, now.Parent, changed.Parent )
				|| Moved( was.Owner, now.Owner, changed.Owner ) || Moved( was.Overlap, now.Overlap, changed.Overlap ) || Bits( was.Mask, now.Mask, changed.Mask );
			var shape = Bits( was.Link, now.Link, changed.Link ) || Bits( was.Flags, now.Flags, changed.Flags ) || Moved( was.Family, now.Family, changed.Family )
				|| Moved( was.Piece, now.Piece, changed.Piece ) || Moved( was.Rotation, now.Rotation, changed.Rotation ) || Moved( was.AltMask, now.AltMask, changed.AltMask )
				|| Moved( was.Segment, now.Segment, changed.Segment );
			return new( 0, false, false, tile, shape );
		}

		/// <summary>What differs between <paramref name="was"/> and <paramref name="now"/>, of the fields <paramref name="changed"/> marks (a field set there, a link bit set there).</summary>
		public static Stayed Of( CellFields was, CellFields now, CellFields changed )
		{
			var flags = changed.Flags != 0 ? (ushort)(was.Flags ^ now.Flags) : (ushort)0;
			return new( (byte)((was.Links ^ now.Links) & changed.Links),
				(changed.Type != 0 && was.Type != now.Type) || (flags & ~WaveBits) != 0 || (changed.Placements != 0 && was.Placements != now.Placements)
				|| (changed.QueueLink != 0 && was.QueueLink != now.QueueLink) || (changed.Owner != 0 && was.Owner != now.Owner) || (changed.Residue && was.Residue != now.Residue),
				(flags & WaveBits) != 0 );
		}
	}

	/// <summary>A wave machine's bits of a pool cell's flags.</summary>
	private const ushort WaveBits = (ushort)ParkCellFlags.PoolWaveBits;

	/// <summary>A mask of the fields that differ between <paramref name="a"/> and <paramref name="b"/>: each field nonzero where it differs, links bit by bit.</summary>
	private static CellFields Differing( CellFields a, CellFields b ) => new( (byte)(a.Type != b.Type ? 1 : 0), (ushort)(a.Flags != b.Flags ? 1 : 0), (short)(a.Placements != b.Placements ? 1 : 0),
		(byte)(a.Links ^ b.Links), (byte)(a.QueueLink != b.QueueLink ? 1 : 0), (ushort)(a.Owner != b.Owner ? 1 : 0), a.Residue != b.Residue );

	/// <summary>The take-back decided last and not yet applied: its steps, and the shared bend corners its inverse clears with their earlier own segments.</summary>
	private Decision? pending;

	/// <param name="level">The host's park.</param>
	/// <param name="host">The host's own member id, whose steps are seat 0's.</param>
	internal CoopUndoJournal( Level level, uint host )
	{
		this.level = level ?? throw new ArgumentNullException( nameof( level ) );
		this.host = host;
	}

	/// <summary>
	/// The tools a step belongs to: a take-back takes back the member's last step of its tool (lines: the path and queue tools together; a slide's pieces: per tower; [EXT:online-coop-undo] a
	/// track's lines: per ride; a coaster's pylons: per coaster, C5-3).
	/// </summary>
	private enum StepTool : byte { Line, SlidePiece, Stroke, Track, Pylon }

	/// <summary>
	/// What a step's undo is: [EXT:online-coop-undo] a kart or water track line's and a coaster pylon's included (C5-3), and their tombstones, a line or pylon whose claim ended or changed hands,
	/// or whose ride a cut changed (a coaster's also a node edit or a swap): its record and ride, no cells, kept in its member's list so a take-back naming it says why it is no longer taken back.
	/// </summary>
	private enum StepKind : byte { PathLine, QueueLine, QueueSegment, SlidePiece, PoolStroke, TrackLine, BuriedTrackLine, CoasterPylon, BuriedCoasterPylon }

	/// <summary>[EXT:online-coop-undo] A member's kart or water line tool on one ride as the host follows it from the member's records (C5-3): off (absent), on with its stack's depth, or closed.</summary>
	private readonly record struct LineTool( bool Closed, int Depth );

	private sealed class Step
	{
		public required long Record { get; init; }
		public int Part { get; init; } = -1;
		public required uint Member { get; init; }
		public required StepKind Kind { get; init; }
		/// <summary>The group of its tool: 0 for lines, the slide's key for a piece.</summary>
		public required int Group { get; init; }
		/// <summary>The cells its undo removes or counts down; a native segment's shared bend corner, cleared by its successor's take-back, leaves it.</summary>
		public required HashSet<(int X, int Y)> Footprint { get; init; }
		/// <summary>The cells its record changed.</summary>
		public required (int X, int Y)[] Changes { get; init; }
		/// <summary>The fields of each of <see cref="Changes"/> before and right after the step's record.</summary>
		public IReadOnlyDictionary<(int X, int Y), (CellFields Before, CellFields After)> Effects { get; set; } = new Dictionary<(int X, int Y), (CellFields Before, CellFields After)>();
		/// <summary>The cells of its footprint its inverse clears whole (a cell it created), where its member's own residue does not stop it.</summary>
		public IReadOnlySet<(int X, int Y)> ClearsWhole { get; init; } = new HashSet<(int X, int Y)>();
		/// <summary>
		/// The cells of its footprint it only counted up (a path line's raised crossing, a native segment's queue cell): its inverse counts them down and unlinks its own links there, so a
		/// later step's write that left the cell's type and those links alone is no dependency.
		/// </summary>
		public IReadOnlySet<(int X, int Y)> CountedUp { get; init; } = new HashSet<(int X, int Y)>();
		/// <summary>A native segment's footprint cells' types before it: a cell it found queue it counted up, any other it made queue.</summary>
		public IReadOnlyDictionary<(int X, int Y), byte> Before { get; init; } = new Dictionary<(int X, int Y), byte>();
		public SegmentResult? Segment { get; init; }
		public CellToolMode Mode { get; init; }
		public int Ride { get; init; } = -1;
		public (int X, int Y) Start { get; init; }
		public (int X, int Y) End { get; init; }
		public (int X, int Y) Tower { get; init; }
		/// <summary>A slide piece's index on its slide and its cell; [EXT:online-coop-undo] a coaster pylon's index on its chain and its cell (C5-3).</summary>
		public int PieceIndex { get; init; }
		public (int X, int Y) PieceCell { get; init; }
		/// <summary>[EXT:online-coop-undo] A coaster pylon's node, which the coaster's last node must be for its take-back (C5-3): a pylon taken off and another placed there since is no longer it.</summary>
		public CoasterNode? Pylon { get; init; }
		/// <summary>[EXT:online-coop-undo] A pool stroke's entry (C5-2): the stroke it belongs to, named by the record of its first command; −1 for any other step.</summary>
		public long Stroke { get; init; } = -1;
		/// <summary>A stroke entry's erase mode: its cells erased (its inverse paints them again), else painted or river-marked.</summary>
		public bool Erases { get; init; }
		/// <summary>A stroke entry's cells in the order it wrote them: painted or erased.</summary>
		public IReadOnlyList<(int X, int Y)> Written { get; init; } = Array.Empty<(int X, int Y)>();
		/// <summary>A paint entry's cells that already were pool water and took the river flag alone, in order: its inverse clears that flag and nothing else.</summary>
		public IReadOnlyList<(int X, int Y)> RiverMarked { get; init; } = Array.Empty<(int X, int Y)>();
		/// <summary>[EXT:online-coop-undo] The track tiles of <see cref="Changes"/> with their records before and right after the step's record (C5-3).</summary>
		public IReadOnlyDictionary<(int X, int Y), (TrackCell Before, TrackCell After)> TrackEffects { get; set; } = new Dictionary<(int X, int Y), (TrackCell Before, TrackCell After)>();
		/// <summary>[EXT:online-coop-undo] A track line's or a pylon's seat, whose claim of its ride it needs (C5-3).</summary>
		public int Player { get; init; }
		/// <summary>[EXT:online-coop-undo] The depth of the line tool's stack the line was laid on (C5-3): its take-back's first-line flag is set at depth one.</summary>
		public int Depth { get; init; }
		/// <summary>[EXT:online-coop-undo] The tiles a track line laid, and those whose overlap counter it raised by one (C5-3).</summary>
		public IReadOnlyList<(int X, int Y)> Laid { get; init; } = Array.Empty<(int X, int Y)>();
		public IReadOnlyList<(int X, int Y)> Raised { get; init; } = Array.Empty<(int X, int Y)>();
		/// <summary>[EXT:online-coop-undo] A track line visited a tile at the counter's limit, which rose by nothing: its clear would count it down all the same (C5-3).</summary>
		public bool Saturated { get; init; }
		public StepTool Tool => Kind switch
		{
			StepKind.SlidePiece => StepTool.SlidePiece,
			StepKind.PoolStroke => StepTool.Stroke,
			StepKind.TrackLine or StepKind.BuriedTrackLine => StepTool.Track,
			StepKind.CoasterPylon or StepKind.BuriedCoasterPylon => StepTool.Pylon,
			_ => StepTool.Line
		};
		/// <summary>[EXT:online-coop-undo] Whether it is a step that needs its ride's claim, held since it was laid: a track line or a coaster pylon (C5-3).</summary>
		public bool NeedsClaim => Kind is StepKind.TrackLine or StepKind.CoasterPylon;
		/// <summary>[EXT:online-coop-undo] Whether it is a tombstone (C5-3).</summary>
		public bool IsBuried => Kind is StepKind.BuriedTrackLine or StepKind.BuriedCoasterPylon;
		public int Weight => Math.Max( 1, Footprint.Count + Changes.Length );
	}

	private sealed class Member
	{
		public readonly List<Step> Steps = new();
		public int Cells;
		/// <summary>The newest record of a step the bounds dropped (−1 for none): an older record is forgotten.</summary>
		public long Forgot = -1;
		/// <summary>
		/// [EXT:online-coop-undo] The member's open pool stroke (C5-2): its name (the record of its first command), its mode and its river option. A command with the first marker opens a new
		/// one, accepted or not; one without continues it while the mode is its own; any other step of the member, its take-back and its departure close it.
		/// </summary>
		public (long Name, bool Erase, bool River)? OpenStroke;
		/// <summary>
		/// [EXT:online-coop-undo] The strokes the bounds took an entry of and that keep another: their take-back is too long ago (C5-2). A stroke leaves with its last entry, after which the
		/// newest record the bounds dropped (<see cref="Forgot"/>) answers its take-back, so the set holds only strokes still in the member's list.
		/// </summary>
		public readonly HashSet<long> Clipped = new();
		/// <summary>[EXT:online-coop-undo] How many entries of each of the member's strokes are steps (C5-2).</summary>
		public readonly Dictionary<long, int> StrokeEntries = new();
		/// <summary>[EXT:online-coop-undo] The member's kart and water line tool on each ride (C5-3): a ride whose tool is off has none.</summary>
		public readonly Dictionary<int, LineTool> TrackTools = new();
		/// <summary>
		/// [EXT:online-coop-undo] How many of the member's track lines and coaster pylons on each ride are steps, and how many are tombstones (C5-3): a kart or water ride's lines, a coaster's
		/// pylons, never both under one ride.
		/// </summary>
		public readonly Dictionary<int, int> LiveTracks = new();
		public readonly Dictionary<int, int> BuriedTracks = new();
		/// <summary>
		/// [EXT:online-coop-undo] The seat the member's track lines and pylons were laid by, whose claim they need (C5-3): a member keeps its seat (another member of the seat is another member).
		/// </summary>
		public int Seat = -1;
	}

	private sealed record Decision( TakeBackCommand Command, IReadOnlyList<Step> Steps, IReadOnlyList<((int X, int Y) Cell, Step Owner)> Bends );

	/// <summary>
	/// Whether a fault turned the journal off for the rest of the session (<see cref="Disable"/>): it records nothing and decides every take-back <see cref="TakeBackVerdict.Forgotten"/>, so no
	/// take-back is ever decided on incomplete histories.
	/// </summary>
	internal bool Disabled { get; private set; }

	/// <summary>A test's fault, run where the journal records a record and decides a take-back.</summary>
	internal Action? Fault { get; set; }

	/// <summary>The outermost command starts: its changes are collected afresh.</summary>
	internal void Begin( ParkCommand command )
	{
		if ( Disabled )
			return;
		running.Clear();
		runningTracks.Clear();
		begun = true;
	}

	/// <summary>A real change of a cell's fields inside the running command (<see cref="ParkCellMap.Changed"/>): it only adds to a set, so it cannot fail the command it observes.</summary>
	internal void Changed( int x, int y, CellFields before )
	{
		if ( !Disabled )
			running.TryAdd( (x, y), before );
	}

	/// <summary>[EXT:online-coop-undo] A real change of a track tile inside the running command (<see cref="TrackCellLayer.Changed"/>, C5-3): it only adds to a set.</summary>
	internal void TrackChanged( int x, int y, TrackCell before )
	{
		if ( !Disabled )
			runningTracks.TryAdd( (x, y), before );
	}

	/// <summary>The park's track layer, which every kart and water ride's tiles share.</summary>
	private TrackCellLayer? Tracks => level.Guests?.Grid.Cells.Tracks;

	/// <summary>A fault in the journal's recording or its decision: it logs it, empties itself and stays off for the session.</summary>
	private void Disable( Exception error )
	{
		Common.GlobalNamespace.Log?.Error( $"The host's undo journal failed and is off for the rest of the session: {error}" );
		Clear();
		Disabled = true;
	}

	/// <summary>The steps <paramref name="member"/> keeps, oldest first (tests).</summary>
	internal int StepsOf( uint member ) => members.TryGetValue( member, out var steps ) ? steps.Steps.Count : 0;

	/// <summary>[EXT:online-coop-undo] The strokes of <paramref name="member"/> the bounds clipped that keep an entry (tests, C5-2).</summary>
	internal int ClippedOf( uint member ) => members.TryGetValue( member, out var steps ) ? steps.Clipped.Count : 0;

	/// <summary>The host's own member id (tests).</summary>
	internal uint Host => host;

	/// <summary>[EXT:online-coop-undo] <paramref name="member"/>'s line tool on <paramref name="ride"/> as the journal follows it: null off, else closed or on with its depth (tests, C5-3).</summary>
	internal (bool Closed, int Depth)? LineToolOf( uint member, int ride ) =>
		members.TryGetValue( member, out var steps ) && steps.TrackTools.TryGetValue( ride, out var tool ) ? (tool.Closed, tool.Depth) : null;

	/// <summary>[EXT:online-coop-undo] <paramref name="member"/>'s track lines that are steps, and those that are tombstones (tests, C5-3).</summary>
	internal (int Live, int Buried) TrackEntriesOf( uint member ) => members.TryGetValue( member, out var steps )
		? (steps.Steps.Count( step => step.Kind == StepKind.TrackLine ), steps.Steps.Count( step => step.Kind == StepKind.BuriedTrackLine )) : (0, 0);

	/// <summary>[EXT:online-coop-undo] <paramref name="member"/>'s coaster pylons that are steps, and those that are tombstones (tests, C5-3).</summary>
	internal (int Live, int Buried) PylonEntriesOf( uint member ) => members.TryGetValue( member, out var steps )
		? (steps.Steps.Count( step => step.Kind == StepKind.CoasterPylon ), steps.Steps.Count( step => step.Kind == StepKind.BuriedCoasterPylon )) : (0, 0);

	/// <summary>[EXT:online-coop-undo] The depth a track line of record <paramref name="record"/> was laid on, null for none (tests, C5-3).</summary>
	internal int? DepthOf( long record ) => members.Values.SelectMany( member => member.Steps ).FirstOrDefault( step => step.Kind == StepKind.TrackLine && step.Record == record )?.Depth;

	/// <summary>Whether <paramref name="member"/>'s weight in the bounds is exactly its live steps' (tests).</summary>
	internal bool WeightAgrees( uint member ) => !members.TryGetValue( member, out var steps ) || steps.Cells == steps.Steps.Sum( step => step.Weight );

	/// <summary>The records still in effect on (<paramref name="x"/>, <paramref name="y"/>), oldest first, −1 for the permanent marker and −2 − member for a member's residue; null for a cell no step tracks (tests).</summary>
	internal IReadOnlyList<long>? HistoryAt( int x, int y ) => histories.TryGetValue( (x, y), out var list )
		? list.Select( entry => entry >= 0 ? entry : residueOf[entry].Member == Nobody ? Permanent : -2 - (long)residueOf[entry].Member ).ToArray() : null;

	/// <summary>The link bits the marker at <paramref name="index"/> of (<paramref name="x"/>, <paramref name="y"/>)'s list left changed, null for a record (tests).</summary>
	internal byte? ResidueLinksAt( int x, int y, int index ) => histories.TryGetValue( (x, y), out var list ) && list[index] < 0 ? residueOf[list[index]].Stayed.Links : null;

	/// <summary>Whether the marker at <paramref name="index"/> of (<paramref name="x"/>, <paramref name="y"/>)'s list left a field other than links changed, null for a record (tests).</summary>
	internal bool? MarkerFieldsAt( int x, int y, int index ) => histories.TryGetValue( (x, y), out var list ) && list[index] < 0 ? residueOf[list[index]].Stayed.Fields : null;

	/// <summary>The record the journal took last: the host's tools read the record of the command they just issued (<see cref="SegmentResult.Record"/>).</summary>
	internal long? LastRecord { get; private set; }

	/// <summary>The member a command's player is: the origin's for a guest's request, the host's for its own.</summary>
	private uint MemberOf( uint? origin ) => origin ?? host;

	// ---- the records ------------------------------------------------------------------------------

	/// <summary>
	/// A record applied on the host (<see cref="CoopHostStream"/>'s <c>OnApplied</c>), with its number and its member (<paramref name="origin"/>, null for the host's own): a decided take-back
	/// that ran accepted takes its steps out; an accepted step of a building tool becomes a step of its member; every record's changes go into the tracked cells' lists.
	/// </summary>
	internal void Applied( long record, uint? origin, ParkCommand command, ParkCommandResult result )
	{
		LastRecord = record;
		if ( Disabled )
			return;
		try
		{
			Fault?.Invoke();
			Record( record, origin, command, result );
		}
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			Disable( error );
		}
	}

	private void Record( long record, uint? origin, ParkCommand command, ParkCommandResult result )
	{
		// a command the rights refused before it ran changed nothing: what the simulation changed since the last record is no command's
		var before = begun ? new Dictionary<(int X, int Y), CellFields>( running ) : new Dictionary<(int X, int Y), CellFields>();
		var beforeTracks = begun ? new Dictionary<(int X, int Y), TrackCell>( runningTracks ) : new Dictionary<(int X, int Y), TrackCell>();
		var changes = before.Keys.Union( beforeTracks.Keys ).ToArray();
		running.Clear();
		runningTracks.Clear();
		begun = false;
		var decided = pending;
		pending = null;
		if ( command is TakeBackCommand { Verdict: TakeBackVerdict.Taken } && decided != null && result.Accepted && result.Error == null )
		{
			Complete( decided, before, beforeTracks, result );
			SweepTracks();
			return;
		}
		var member = MemberOf( origin );
		// [EXT:online-coop-undo] a pool stroke's command opens or continues its member's stroke, accepted or not (C5-2); a take-back of the member's closes it
		var stroke = command is IPoolStrokeCommand pool ? OpenStroke( member, record, pool ) : -1;
		if ( command is TakeBackCommand && members.TryGetValue( member, out var takingBack ) )
			takingBack.OpenStroke = null;
		// [EXT:online-coop-undo] the member's kart or water line tool follows its track commands, accepted or not; a line laid on it is a step at its depth (C5-3)
		var depth = FollowLineTool( member, command, result );
		var made = result.Accepted && result.Error == null ? StepsOf( record, member, origin != null, command, result, changes, before, beforeTracks, stroke, depth ) : Array.Empty<Step>();
		// any other step of the member closes its open stroke
		if ( made.Any( step => step.Kind != StepKind.PoolStroke ) && members.TryGetValue( member, out var stepping ) )
			stepping.OpenStroke = null;
		if ( made.Length > 0 && level.Guests?.Grid.Cells is { } map )
		{
			// each step keeps its effects, its cells' fields before and right after it, [EXT:online-coop-undo] and its track tiles' (C5-3): a take-back removes its record only where it
			// reversed every one (Reversed)
			var effects = before.ToDictionary( pair => pair.Key, pair => (pair.Value, map.FieldsAt( pair.Key.X, pair.Key.Y )) );
			var trackEffects = beforeTracks.ToDictionary( pair => pair.Key, pair => (pair.Value, map.Tracks.Get( pair.Key.X, pair.Key.Y )) );
			foreach ( var step in made )
			{
				step.Effects = effects;
				step.TrackEffects = trackEffects;
			}
		}
		foreach ( var step in made )
			Add( step );
		var removable = made.Length > 0;
		// the record goes into the list of every tracked cell it changed, and of every cell of its own steps' footprints; a record of no step as a permanent marker, with what it changed
		foreach ( var cell in changes.Concat( made.SelectMany( step => step.Footprint ) ).Distinct() )
		{
			if ( !histories.TryGetValue( cell, out var list ) )
				continue;
			if ( removable )
				Append( list, record );
			else
				Append( list, NewPermanent( WholeAt( cell, before, beforeTracks ) ) );
		}
		foreach ( var step in made )
			Bound( step.Member );
		// [EXT:online-coop-undo] a cut changes its ride's track under every line laid on it, and a coaster's node edit, swap or cut its chain under every pylon: they become tombstones (C5-3)
		if ( result.Accepted && result.Error == null && BuriedBy( command ) is { } changed )
			BuryRide( changed );
		SweepTracks();
	}

	/// <summary>
	/// [EXT:online-coop-undo] The ride an accepted command changes under every track line or pylon laid on it (C5-3): a kart or water ride's cut; a coaster's node edit (its press, its steps and
	/// its release), stack swap or cut.
	/// </summary>
	private static int? BuriedBy( ParkCommand command ) => command switch
	{
		CutTrackCommand cut => cut.Ride,
		BeginCoasterNodeEditCommand or EditCoasterNodeCommand or EndCoasterNodeEditCommand or SwapCoasterStackCommand or CutCoasterCommand => ((CoasterPanelCommand)command).Station,
		_ => null
	};

	/// <summary>
	/// What a record left changed on <paramref name="cell"/>: its fields from their values before it (<paramref name="before"/>) to now, [EXT:online-coop-undo] and its track tile's
	/// (<paramref name="beforeTracks"/>, C5-3); everything when neither is known.
	/// </summary>
	private Stayed WholeAt( (int X, int Y) cell, IReadOnlyDictionary<(int X, int Y), CellFields> before, IReadOnlyDictionary<(int X, int Y), TrackCell> beforeTracks )
	{
		if ( level.Guests?.Grid.Cells is not { } map )
			return Stayed.Everything;
		var known = false;
		var stayed = new Stayed( 0, false );
		if ( before.TryGetValue( cell, out var was ) )
		{
			stayed = stayed.With( Whole( was, map.FieldsAt( cell.X, cell.Y ) ) );
			known = true;
		}
		if ( beforeTracks.TryGetValue( cell, out var tile ) )
		{
			stayed = stayed.With( Stayed.OfTrack( tile, map.Tracks.Get( cell.X, cell.Y ) ) );
			known = true;
		}
		return known ? stayed : Stayed.Everything;
	}

	/// <summary>What <paramref name="step"/> changed on <paramref name="cell"/>: its fields and its track tile from before it to right after it; everything when neither is known.</summary>
	private static Stayed EffectOf( Step step, (int X, int Y) cell )
	{
		var known = false;
		var stayed = new Stayed( 0, false );
		if ( step.Effects.TryGetValue( cell, out var effect ) )
		{
			stayed = stayed.With( Whole( effect.Before, effect.After ) );
			known = true;
		}
		if ( step.TrackEffects.TryGetValue( cell, out var tile ) )
		{
			stayed = stayed.With( Stayed.OfTrack( tile.Before, tile.After ) );
			known = true;
		}
		return known ? stayed : Stayed.Everything;
	}

	/// <summary>What of <paramref name="step"/>'s changes on <paramref name="cell"/> stays now: each field and track field it changed that is not back to its value from before it.</summary>
	private Stayed StayedOf( Step step, (int X, int Y) cell )
	{
		if ( level.Guests?.Grid.Cells is not { } map )
			return Stayed.Everything;
		var known = false;
		var stayed = new Stayed( 0, false );
		if ( step.Effects.TryGetValue( cell, out var effect ) )
		{
			stayed = stayed.With( Stayed.Of( effect.Before, map.FieldsAt( cell.X, cell.Y ), Differing( effect.Before, effect.After ) ) );
			known = true;
		}
		if ( step.TrackEffects.TryGetValue( cell, out var tile ) )
		{
			stayed = stayed.With( Stayed.OfTrack( tile.Before, map.Tracks.Get( cell.X, cell.Y ), tile.After ) );
			known = true;
		}
		return known ? stayed : Stayed.Everything;
	}

	/// <summary>
	/// The steps an accepted record makes: a path or queue line that leaves its tool on, a native queue segment that does, the slide pieces it laid. A line that ends its tool has no
	/// take-back, as in single player (the user's decision 7 of the plan): it is no step, and its record stays in the cells' lists for good.
	/// </summary>
	private Step[] StepsOf( long record, uint member, bool guest, ParkCommand command, ParkCommandResult result, (int X, int Y)[] changes, IReadOnlyDictionary<(int X, int Y), CellFields> before,
		IReadOnlyDictionary<(int X, int Y), TrackCell> beforeTracks, long stroke, int? depth )
	{
		switch ( command )
		{
			// [EXT:online-coop-undo] a kart or water track line laid on its member's line tool (C5-3, FollowLineTool)
			case LayTrackLineCommand line when depth is { } laidOn && result.Value is TrackLineDone { Segment: { } laid }:
				return TrackLine( record, member, line, laid, laidOn, changes, beforeTracks );
			// [EXT:online-coop-undo] a pylon a coaster tool's click placed (C5-3)
			case CoasterClickCommand click when result.Value is CoasterClickDone { Completed: false, Placed: true }:
				return CoasterPylon( record, member, click, changes );
			// [EXT:online-coop-undo] a pool stroke's commands, each an entry of its stroke with the cells it wrote (C5-2): a host's cell, a guest's batch paired cell by cell with its writes
			case PaintPoolCellCommand paint when stroke >= 0 && result.Value is PoolCellWrite write:
				return StrokeEntry( record, member, stroke, false, write.Result == PoolBuildResult.Ok ? new[] { (paint.X, paint.Y) } : Array.Empty<(int X, int Y)>(),
					write.Result == PoolBuildResult.RiverMarked ? new[] { (paint.X, paint.Y) } : Array.Empty<(int X, int Y)>(), changes );
			case ErasePoolCellCommand erase when stroke >= 0 && result.Value is PoolCellWrite { Result: PoolBuildResult.Ok }:
				return StrokeEntry( record, member, stroke, true, new[] { (erase.X, erase.Y) }, Array.Empty<(int X, int Y)>(), changes );
			case PoolCellsCommand batch when stroke >= 0 && result.Value is PoolCellWrite[] writes:
			{
				var written = new List<(int X, int Y)>();
				var marked = new List<(int X, int Y)>();
				for ( var index = 0; index < writes.Length && index < batch.Cells.Count; index++ )
				{
					if ( writes[index].Result == PoolBuildResult.Ok )
						written.Add( batch.Cells[index] );
					else if ( !batch.Erase && writes[index].Result == PoolBuildResult.RiverMarked )
						marked.Add( batch.Cells[index] );
				}
				return StrokeEntry( record, member, stroke, batch.Erase, written, marked, changes );
			}
			case LayLineCommand line when result.Value is SegmentResult laid && level.Guests?.Grid.Cells is { } map:
			{
				if ( EndsItsTool( laid, (line.StartX, line.StartY), guest ) )
					return Array.Empty<Step>();
				// a path line counts down only the crossings whose counter it raised: one at the counter's limit rose by nothing (ParkPathBuilder.BumpPlacement); a path line of crossings
				// alone is a step too, so they can be taken back; a queue line's undo takes its built cells alone
				var segment = line.Mode == CellToolMode.Queue ? laid : laid with
				{
					Existing = laid.Existing.Where( cell => before.TryGetValue( cell, out var fields ) && map.PlacementCountAt( cell.X, cell.Y ) == fields.Placements + 1 ).ToArray()
				};
				if ( segment.Built.Count == 0 && (line.Mode == CellToolMode.Queue || segment.Existing.Count == 0) )
					return Array.Empty<Step>();
				var footprint = line.Mode == CellToolMode.Queue ? segment.Built.ToHashSet() : segment.Built.Concat( segment.Existing ).ToHashSet();
				return new[] { new Step { Record = record, Member = member, Kind = line.Mode == CellToolMode.Queue ? StepKind.QueueLine : StepKind.PathLine, Group = 0, Footprint = footprint,
					Changes = changes, Segment = segment, Mode = line.Mode, Ride = line.Ride, Start = (line.StartX, line.StartY), ClearsWhole = segment.Built.ToHashSet(),
					CountedUp = line.Mode == CellToolMode.Queue ? new HashSet<(int X, int Y)>() : segment.Existing.ToHashSet() } };
			}
			case QueueSegmentCommand line when result.Value is SegmentResult { QueueReceipt: { } receipt } laid && level.Guests?.Grid.Cells is { } cells:
			{
				// a segment with a receipt leaves its tool on unless its record cut a guest's short (one ending its tool has none)
				if ( EndsItsTool( laid, receipt.Start, guest ) )
					return Array.Empty<Step>();
				// the cells its commit really made queue or counted up, by their fields before it: its undo counts them down or clears them; a queue cell the commit never reached (past a
				// money stop) is none of them, and metadata past the stop is a change, not its footprint
				var footprint = ParkPathBuilder.LayLine( receipt.Start, ParkPathBuilder.SnapEnd( receipt.Start, receipt.End ) )
					.Where( cell => cells.RawTypeAt( cell.X, cell.Y ) == (byte)ParkCellType.Queue && before.TryGetValue( cell, out var fields )
						&& (fields.Type != (byte)ParkCellType.Queue || cells.PlacementCountAt( cell.X, cell.Y ) == fields.Placements + 1) ).ToHashSet();
				if ( footprint.Count == 0 )
					return Array.Empty<Step>();
				return new[] { new Step { Record = record, Member = member, Kind = StepKind.QueueSegment, Group = 0, Footprint = footprint, Changes = changes, Ride = line.Ride,
					Start = receipt.Start, End = receipt.End,
					Before = footprint.ToDictionary( cell => cell, cell => before.TryGetValue( cell, out var fields ) ? fields.Type : cells.RawTypeAt( cell.X, cell.Y ) ),
					ClearsWhole = footprint.Where( cell => before.TryGetValue( cell, out var fields ) && fields.Type != (byte)ParkCellType.Queue ).ToHashSet(),
					CountedUp = footprint.Where( cell => before.TryGetValue( cell, out var fields ) && fields.Type == (byte)ParkCellType.Queue ).ToHashSet() } };
			}
			case LayPoolSlidePieceCommand piece when result.Value is PoolSlideWrite { Result: PoolSlideResult.Ok }:
				return SlidePieces( record, member, (piece.TowerX, piece.TowerY), 1, changes );
			case LayPoolSlidePiecesCommand pieces when result.Value is PoolSlideBatch batch:
				return SlidePieces( record, member, (pieces.TowerX, pieces.TowerY), batch.Writes.Count( write => write.Result == PoolSlideResult.Ok ), changes );
			default:
				return Array.Empty<Step>();
		}
	}

	/// <summary>
	/// Whether the line <paramref name="laid"/> from <paramref name="start"/> ends its tool, as the tool's continuation decides (<see cref="CellBuildTool"/>): a guest's line its record cut
	/// short at a refused cell (the host's own never reaches its record so: its ghost ends the tool first), one whose end is its start, and one that reached an existing path or queue at
	/// its end (the original's end flag; a native segment's own flag). The tool's vertex stack, which ends a tool only once full (1,024), is the tool's alone.
	/// </summary>
	private static bool EndsItsTool( SegmentResult laid, (int X, int Y) start, bool guest )
	{
		if ( guest && laid is { Completed: false, StoppedByOwnLimit: false } )
			return true;
		if ( laid.SnappedEnd == start )
			return true;
		var reachedLast = laid.Completed || (laid.Line.Count > 0 && laid.StoppedAt == laid.Line[^1]);
		return laid.QueueFinalEndsTool ?? (laid.EndedOnExisting && reachedLast);
	}

	/// <summary>
	/// [EXT:online-coop-undo] The member's stroke a pool command belongs to (C5-2): a new one named by <paramref name="record"/> when the command is a stroke's first, the member has none open, or
	/// the open one's mode is another; otherwise the open one, which it continues.
	/// </summary>
	private long OpenStroke( uint id, long record, IPoolStrokeCommand pool )
	{
		if ( !members.TryGetValue( id, out var member ) )
			members[id] = member = new Member();
		if ( pool.First || member.OpenStroke is not { } open || open.Erase != pool.Erases || open.River != pool.PaintsRiver )
			member.OpenStroke = (record, pool.Erases, pool.PaintsRiver);
		return member.OpenStroke!.Value.Name;
	}

	/// <summary>
	/// [EXT:online-coop-undo] An entry of <paramref name="stroke"/> (C5-2): the cells it painted or erased (<paramref name="written"/>) and those it only river-marked, its footprint; its inverse
	/// erases a painted cell whole, paints an erased one again and takes a river mark's flag off. None when it wrote nothing.
	/// </summary>
	private static Step[] StrokeEntry( long record, uint member, long stroke, bool erase, IReadOnlyList<(int X, int Y)> written, IReadOnlyList<(int X, int Y)> marked, (int X, int Y)[] changes )
	{
		if ( written.Count == 0 && marked.Count == 0 )
			return Array.Empty<Step>();
		return new[] { new Step { Record = record, Member = member, Kind = StepKind.PoolStroke, Group = 0, Stroke = stroke, Erases = erase, Written = written, RiverMarked = marked,
			Footprint = written.Concat( marked ).ToHashSet(), Changes = changes, ClearsWhole = erase ? new HashSet<(int X, int Y)>() : written.ToHashSet() } };
	}

	/// <summary>The <paramref name="laid"/> pieces a record laid last on the slide of <paramref name="tower"/>, each a step with its ordinal within the record.</summary>
	private Step[] SlidePieces( long record, uint member, (int X, int Y) tower, int laid, (int X, int Y)[] changes )
	{
		if ( laid <= 0 || level.PoolSlides?.Find( PoolSlide.KeyOf( tower.X, tower.Y ) ) is not { } slide || slide.Cells.Count < laid )
			return Array.Empty<Step>();
		var first = slide.Cells.Count - laid;
		return Enumerable.Range( 0, laid ).Select( ordinal => new Step { Record = record, Part = ordinal, Member = member, Kind = StepKind.SlidePiece, Group = slide.Id,
			Footprint = new HashSet<(int X, int Y)>(), Changes = changes, Tower = tower, PieceIndex = first + ordinal,
			PieceCell = slide.Cells[first + ordinal] } ).ToArray();
	}

	/// <summary>
	/// [EXT:online-coop-undo] A kart or water track line of <paramref name="member"/>'s, laid on its tool at <paramref name="depth"/> (C5-3): the tiles it laid (all four cells of each) and the
	/// tiles whose overlap counter it raised by one, by their records before it, are its footprint, which its inverse, the directional clear, takes back; every other real write of its record
	/// (the arms it added to neighbours, the walk's refresh of earlier tiles and the station) is its changes, which a later step's dependency check sees and its own take-back does not compare
	/// with its footprint. A tile at the counter's limit rose by nothing, and the clear counts down every tile it visits: such a line is still a step, whose take-back is in the way. The ride's
	/// exit and entry tiles a line laid are spared by the clear, as single player's Backspace spares them: they are counted down, not cleared, and keep its record as its member's residue.
	/// </summary>
	private Step[] TrackLine( long record, uint member, LayTrackLineCommand line, SegmentResult laid, int depth, (int X, int Y)[] changes,
		IReadOnlyDictionary<(int X, int Y), TrackCell> beforeTracks )
	{
		if ( level.ResolveCommandObject( line.Ride ) is not { } station || !level.TrackStations.TryGetValue( station, out var layout ) || Tracks is not { } tracks )
			return Array.Empty<Step>();
		var raised = new List<(int X, int Y)>();
		var saturated = false;
		foreach ( var tile in laid.Existing )
		{
			if ( beforeTracks.TryGetValue( tile, out var was ) && tracks.Get( tile.X, tile.Y ).Overlap == was.Overlap + 1 )
				raised.Add( tile );
			else
				saturated = true;
		}
		var spared = laid.Built.Where( tile => tile == layout.Exit || tile == layout.Entry ).ToArray();
		return new[] { new Step { Record = record, Member = member, Kind = StepKind.TrackLine, Group = line.Ride, Ride = line.Ride, Player = line.Player,
			Footprint = laid.Built.SelectMany( tile => TrackStation.TileCellsOf( tile.X, tile.Y ) ).Concat( raised ).ToHashSet(), Changes = changes,
			Start = (line.StartX, line.StartY), End = laid.SnappedEnd, Depth = depth, Laid = laid.Built.ToArray(), Raised = raised, Saturated = saturated,
			ClearsWhole = laid.Built.Except( spared ).SelectMany( tile => TrackStation.TileCellsOf( tile.X, tile.Y ) ).ToHashSet(),
			CountedUp = raised.Concat( spared ).ToHashSet() } };
	}

	/// <summary>
	/// [EXT:online-coop-undo] A pylon <paramref name="member"/>'s click placed on a coaster (C5-3): its record, the station, its index on the chain (the node new pylons follow, which the placement
	/// made it, before a cut circuit's gap too), its cell and the node itself; no cells of the map, as a pylon lives in its coaster's builder. Its take-back is the coaster tool's own Backspace,
	/// which takes the last pylon off with no refund.
	/// </summary>
	private Step[] CoasterPylon( long record, uint member, CoasterClickCommand click, (int X, int Y)[] changes )
	{
		if ( level.ResolveCommandObject( click.Station ) is not { } station || level.GetCoasterBuilder( station ) is not { } builder )
			return Array.Empty<Step>();
		var track = builder.Track;
		var index = track.LastIndex;
		if ( index < CoasterTrack.StationNodeCount || index >= track.Nodes.Count || track.Nodes[index] is not { IsPlayerPylon: true } node || (node.X, node.Y) != (click.X, click.Y) )
			return Array.Empty<Step>();
		return new[] { new Step { Record = record, Member = member, Kind = StepKind.CoasterPylon, Group = click.Station, Ride = click.Station, Player = click.Player,
			Footprint = new HashSet<(int X, int Y)>(), Changes = changes, PieceIndex = index, PieceCell = (node.X, node.Y), Pylon = node } };
	}

	/// <summary>
	/// [EXT:online-coop-undo] The member's kart or water line tool on a ride, followed from its record (C5-3): every transition of the tool's stack is one of the member's commands, so the host
	/// knows the stack's depth without seeing it. An entry (the walk to the open end, <see cref="OpenTrackLineToolCommand"/>; a cut that leaves the circuit open, <see cref="CutTrackCommand"/>)
	/// loads the corners its open end gives, at least one point and at most the stack's 1,025 (<see cref="TrackEditor.EnterLineTool"/>): the tool is on at that depth; any other cut leaves no line
	/// tool. A line of the member on the ride is judged in this order: one whose walk found the circuit valid is no step, wherever it ended (the issuer's tool reads that first), and the tool is
	/// closed when the line carried the issuer's restart (<see cref="LayTrackLineCommand.KeepsTool"/>), off when it did not; one the park refused, or that ended the tool otherwise (it ended on its
	/// start, stopped short or ended on existing track, as <see cref="CellBuildTool"/> ends it; a full stack ends a shared park's tool before its line goes), is no step and the tool is off; one
	/// laid while the tool is off (no tool sends one: after its end a line needs an entry) is no step; any other is a step at the depth the tool is on, a restart's first line at depth one, and the
	/// tool is one point deeper. Ending or cancelling a tool is the issuer's own: the tool stays on here, and the member's next entry sets it again. Returns a step's depth, else null.
	/// </summary>
	private int? FollowLineTool( uint id, ParkCommand command, ParkCommandResult result )
	{
		var done = result.Accepted && result.Error == null;
		switch ( command )
		{
			case OpenTrackLineToolCommand entry:
				if ( done && result.Value is TrackOpenEnd end )
					MemberFor( id ).TrackTools[entry.Ride] = new LineTool( false, DepthOf( end ) );
				return null;
			case CutTrackCommand cut:
				if ( done && result.Value is TrackCutDone { Valid: false, Open: { } open } )
					MemberFor( id ).TrackTools[cut.Ride] = new LineTool( false, DepthOf( open ) );
				else if ( members.TryGetValue( id, out var cutting ) )
					cutting.TrackTools.Remove( cut.Ride );
				return null;
			case LayTrackLineCommand line:
			{
				if ( done && result.Value is TrackLineDone { Valid: true } )
				{
					if ( line.KeepsTool )
						MemberFor( id ).TrackTools[line.Ride] = new LineTool( true, 0 );
					else if ( members.TryGetValue( id, out var closing ) )
						closing.TrackTools.Remove( line.Ride );
					return null;
				}
				if ( !members.TryGetValue( id, out var member ) || !member.TrackTools.TryGetValue( line.Ride, out var tool ) )
					return null;
				if ( !done || result.Value is not TrackLineDone { Segment: { } laid } || laid.SnappedEnd == (line.StartX, line.StartY) || !laid.Completed || laid.EndedOnExisting )
				{
					member.TrackTools.Remove( line.Ride );
					return null;
				}
				var depth = tool.Closed ? 1 : tool.Depth;
				member.TrackTools[line.Ride] = new LineTool( false, depth + 1 );
				return depth;
			}
			default:
				return null;
		}
	}

	/// <summary>The depth of the line tool's stack an entry's open end loads: its corners, at least one point, at most the stack holds (<see cref="TrackEditor.CornerCapacity"/>).</summary>
	private static int DepthOf( TrackOpenEnd open ) => Math.Min( Math.Max( open.Corners.Count, 1 ), TrackEditor.CornerCapacity );

	/// <summary>The member of <paramref name="id"/>, made when it has none yet.</summary>
	private Member MemberFor( uint id )
	{
		if ( !members.TryGetValue( id, out var member ) )
			members[id] = member = new Member();
		return member;
	}

	/// <summary>Adds <paramref name="delta"/> to <paramref name="ride"/>'s count, which goes when it reaches zero.</summary>
	private static void Count( Dictionary<int, int> counts, int ride, int delta )
	{
		var count = counts.GetValueOrDefault( ride ) + delta;
		if ( count > 0 )
			counts[ride] = count;
		else
			counts.Remove( ride );
	}

	/// <summary>
	/// [EXT:online-coop-undo] Turns every member's track lines or pylons on <paramref name="ride"/> into tombstones (C5-3): a cut changed the ride's track under them, or a node edit or a swap the
	/// coaster's chain.
	/// </summary>
	private void BuryRide( int ride )
	{
		foreach ( var member in members.Values )
		{
			if ( !member.LiveTracks.ContainsKey( ride ) )
				continue;
			foreach ( var step in member.Steps.Where( step => step.NeedsClaim && step.Ride == ride ).ToArray() )
				Bury( member, step );
		}
	}

	/// <summary>
	/// [EXT:online-coop-undo] After every record (C5-3): a member's track lines and pylons on a ride whose claim its seat does not hold now (released, expired, taken by another, gone with its
	/// grant) become tombstones, so a take-back needs the claim held since the step, and a claim taken again brings none back; every entry of a ride that left the park goes, its tombstones with it.
	/// </summary>
	private void SweepTracks()
	{
		foreach ( var member in members.Values )
		{
			if ( member.LiveTracks.Count == 0 && member.BuriedTracks.Count == 0 )
				continue;
			foreach ( var ride in member.LiveTracks.Keys.Union( member.BuriedTracks.Keys ).ToArray() )
			{
				if ( level.ResolveCommandObject( ride ) is not { IsDeleted: false } )
				{
					foreach ( var step in member.Steps.Where( step => step.Tool is StepTool.Track or StepTool.Pylon && step.Ride == ride ).ToArray() )
						Leave( member, step, takenBack: false );
					member.TrackTools.Remove( ride );
					continue;
				}
				if ( !member.LiveTracks.ContainsKey( ride ) || level.ClaimOf( ride ) == member.Seat )
					continue;
				foreach ( var step in member.Steps.Where( step => step.NeedsClaim && step.Ride == ride ).ToArray() )
					Bury( member, step );
			}
		}
	}

	/// <summary>
	/// [EXT:online-coop-undo] Turns <paramref name="step"/>, a live track line or pylon, into its tombstone where it stood in its member's list (C5-3): it leaves as a step left for good (its records
	/// stay in the cells' lists for good), and the tombstone keeps its record and ride with no cells, weight one in the bounds, until its member's bounds, departure or ride take it.
	/// </summary>
	private void Bury( Member member, Step step )
	{
		var index = member.Steps.IndexOf( step );
		if ( index < 0 )
			return;
		Leave( member, step, takenBack: false );
		var kind = step.Kind == StepKind.CoasterPylon ? StepKind.BuriedCoasterPylon : StepKind.BuriedTrackLine;
		var tombstone = new Step { Record = step.Record, Part = step.Part, Member = step.Member, Kind = kind, Group = step.Group, Ride = step.Ride, Player = step.Player,
			Footprint = new HashSet<(int X, int Y)>(), Changes = Array.Empty<(int X, int Y)>() };
		member.Steps.Insert( index, tombstone );
		member.Cells += tombstone.Weight;
		Count( member.BuriedTracks, tombstone.Ride, 1 );
	}

	private void Add( Step step )
	{
		if ( !members.TryGetValue( step.Member, out var member ) )
			members[step.Member] = member = new Member();
		member.Steps.Add( step );
		member.Cells += step.Weight;
		if ( step.Stroke >= 0 )
			member.StrokeEntries[step.Stroke] = member.StrokeEntries.GetValueOrDefault( step.Stroke ) + 1;
		if ( step.NeedsClaim )
		{
			Count( member.LiveTracks, step.Ride, 1 );
			member.Seat = step.Player;
		}
		live.TryAdd( step.Record, step );
		foreach ( var cell in step.Footprint )
		{
			tracked[cell] = tracked.TryGetValue( cell, out var count ) ? count + 1 : 1;
			if ( !histories.ContainsKey( cell ) )
				histories[cell] = new List<long>();
		}
	}

	/// <summary>Drops the member's oldest steps beyond the bounds: their records stay in the cells' lists for good.</summary>
	private void Bound( uint id )
	{
		if ( !members.TryGetValue( id, out var member ) )
			return;
		while ( member.Steps.Count > 0 && (member.Steps.Count > StepBound || member.Cells > CellBound) )
		{
			var oldest = member.Steps[0];
			member.Forgot = Math.Max( member.Forgot, oldest.Record );
			// [EXT:online-coop-undo] a stroke that lost an entry is too long ago as a whole (C5-2)
			if ( oldest.Stroke >= 0 )
				member.Clipped.Add( oldest.Stroke );
			Leave( member, oldest, takenBack: false );
		}
	}

	/// <summary>
	/// Takes a step out of its member's list: taken back, its record leaves the list of each cell on which the take-back reversed every effect it had (<see cref="Reversed"/>); on any other cell,
	/// and when it was not taken back, it stays there for good.
	/// </summary>
	private void Leave( Member member, Step step, bool takenBack )
	{
		member.Steps.Remove( step );
		member.Cells -= step.Weight;
		// [EXT:online-coop-undo] a track line's or a pylon's counts (C5-3)
		if ( step.NeedsClaim )
			Count( member.LiveTracks, step.Ride, -1 );
		else if ( step.IsBuried )
			Count( member.BuriedTracks, step.Ride, -1 );
		// [EXT:online-coop-undo] a stroke's last entry takes the stroke out of the member's sets (C5-2)
		if ( step.Stroke >= 0 && member.StrokeEntries.TryGetValue( step.Stroke, out var entries ) )
		{
			if ( entries > 1 )
				member.StrokeEntries[step.Stroke] = entries - 1;
			else
			{
				member.StrokeEntries.Remove( step.Stroke );
				member.Clipped.Remove( step.Stroke );
			}
		}
		// a record shared by a batch's pieces stays while another of its pieces is a step
		var shared = member.Steps.Any( other => other.Record == step.Record );
		if ( shared )
		{
			if ( live.TryGetValue( step.Record, out var kept ) && ReferenceEquals( kept, step ) )
				live[step.Record] = member.Steps.First( other => other.Record == step.Record );
		}
		else
		{
			live.Remove( step.Record );
			foreach ( var cell in step.Changes )
			{
				if ( !histories.TryGetValue( cell, out var list ) )
					continue;
				if ( takenBack && Reversed( step, cell ) )
				{
					// the runs of markers on its two sides become one
					list.RemoveAll( entry => entry == step.Record );
					Normalize( list );
				}
				else if ( !takenBack )
					Mark( list, step.Record, NewPermanent( EffectOf( step, cell ) ) );
				else if ( list.Contains( step.Record ) )
					Mark( list, step.Record, NewResidue( step.Member, StayedOf( step, cell ) ) );
			}
		}
		foreach ( var cell in step.Footprint )
			Untrack( cell );
	}

	/// <summary>
	/// Whether every effect <paramref name="step"/> had on <paramref name="cell"/> is undone now: each field it changed back to its value before it, a link or flag bit by bit (so another player's
	/// link on the same neighbour does not count).
	/// </summary>
	private bool Reversed( Step step, (int X, int Y) cell )
	{
		if ( level.Guests?.Grid.Cells is not { } map )
			return false;
		var onMap = step.Effects.TryGetValue( cell, out var effect );
		// [EXT:online-coop-undo] and its track tile's, field by field, the mask, link and flags bit by bit (C5-3)
		var onTrack = step.TrackEffects.TryGetValue( cell, out var tile );
		if ( !onMap && !onTrack )
			return false;
		if ( onMap )
		{
			var (before, after) = effect;
			var now = map.FieldsAt( cell.X, cell.Y );
			if ( !((before.Type == after.Type || now.Type == before.Type)
				&& ((before.Flags ^ after.Flags) & (now.Flags ^ before.Flags)) == 0
				&& (before.Placements == after.Placements || now.Placements == before.Placements)
				&& ((before.Links ^ after.Links) & (now.Links ^ before.Links)) == 0
				&& (before.QueueLink == after.QueueLink || now.QueueLink == before.QueueLink)
				&& (before.Owner == after.Owner || now.Owner == before.Owner)
				&& (before.Residue == after.Residue || now.Residue == before.Residue)) )
				return false;
		}
		return !onTrack || Stayed.OfTrack( tile.Before, map.Tracks.Get( cell.X, cell.Y ), tile.After ) is { TrackTile: false, TrackShape: false };
	}

	private void Untrack( (int X, int Y) cell )
	{
		if ( !tracked.TryGetValue( cell, out var count ) )
			return;
		if ( count > 1 )
		{
			tracked[cell] = count - 1;
			return;
		}
		tracked.Remove( cell );
		if ( histories.Remove( cell, out var list ) )
		{
			foreach ( var entry in list )
				residueOf.Remove( entry );
		}
	}

	/// <summary>Appends <paramref name="entry"/>, a record or a marker, keeping the list in its normal form (<see cref="Normalize"/>).</summary>
	private void Append( List<long> list, long entry )
	{
		list.Add( entry );
		if ( entry < 0 )
			Normalize( list );
	}

	/// <summary>Turns the record <paramref name="record"/> of a list into <paramref name="marker"/>, keeping the list in its normal form (<see cref="Normalize"/>).</summary>
	private void Mark( List<long> list, long record, long marker )
	{
		var index = list.IndexOf( record );
		if ( index < 0 )
		{
			residueOf.Remove( marker );
			return;
		}
		list[index] = marker;
		Normalize( list );
	}

	/// <summary>Puts a list in its normal form (<see cref="Normalize(List{long}, Func{long, uint}, Action{long, long}, Action{long})"/>), its residues' provenance kept and merged.</summary>
	private void Normalize( List<long> list ) => Normalize( list, entry => residueOf[entry].Member,
		( kept, merged ) => residueOf[kept] = (residueOf[kept].Member, residueOf[kept].Stayed.With( residueOf[merged].Stayed )), entry => residueOf.Remove( entry ) );

	/// <summary>
	/// Puts a cell's list in its normal form. The changed-since rule reads the markers between two records as a set (any entry after a step's first record that is not its own and no
	/// exception refuses it), so each run of markers is kept as that set: a run with a permanent marker (<see cref="Nobody"/>'s, <paramref name="memberOf"/>) is that marker alone, every
	/// other marker of the run merged into it (no step's own exception passes it; what each left changed joins what it left, for the count-only exception), and any other run keeps one
	/// residue per member, the later ones of the same member merged into it (<paramref name="merged"/>: what each left changed, joined). A marker in another run, on the other side of a
	/// record, stays apart with what it left. A list so holds its records, each a live step's, and between two of them one permanent marker or at most one residue per member.
	/// <paramref name="dropped"/> hears each marker that left the list.
	/// </summary>
	internal static void Normalize( List<long> list, Func<long, uint> memberOf, Action<long, long>? merged = null, Action<long>? dropped = null )
	{
		var write = 0;
		// where the run of markers the output ends with starts
		var run = 0;
		foreach ( var entry in list.ToArray() )
		{
			if ( entry >= 0 )
			{
				list[write++] = entry;
				run = write;
				continue;
			}
			if ( write > run && memberOf( list[run] ) == Nobody )
			{
				if ( entry != list[run] )
				{
					merged?.Invoke( list[run], entry );
					dropped?.Invoke( entry );
				}
				continue;
			}
			var member = memberOf( entry );
			if ( member == Nobody )
			{
				for ( var index = run; index < write; index++ )
				{
					merged?.Invoke( entry, list[index] );
					dropped?.Invoke( list[index] );
				}
				write = run;
				list[write++] = entry;
				continue;
			}
			var same = -1;
			for ( var index = run; index < write && same < 0; index++ )
			{
				if ( memberOf( list[index] ) == member )
					same = index;
			}
			if ( same < 0 )
				list[write++] = entry;
			else if ( list[same] != entry )
			{
				merged?.Invoke( list[same], entry );
				dropped?.Invoke( entry );
			}
		}
		list.RemoveRange( write, list.Count - write );
	}

	/// <summary>
	/// A taken take-back ran: its steps leave, their records leave the cells where it reversed their effects, and a tracked cell it changed that none of them changed (<paramref name="run"/>:
	/// the cells its run changed, with their fields before it; its clear unlinking a neighbour's bit another command set) gets its member's residue marker, a write of that member's own, with
	/// what it changed there, unless it is the earlier own segment's whose shared bend corner it cleared; and that corner leaves the earlier segment's footprint and its weight.
	/// </summary>
	private void Complete( Decision decided, IReadOnlyDictionary<(int X, int Y), CellFields> run, IReadOnlyDictionary<(int X, int Y), TrackCell> runTracks, ParkCommandResult result )
	{
		foreach ( var step in decided.Steps )
		{
			if ( members.TryGetValue( step.Member, out var member ) )
				Leave( member, step, takenBack: true );
		}
		var by = decided.Steps[0].Member;
		if ( members.TryGetValue( by, out var taker ) )
		{
			taker.OpenStroke = null;
			// [EXT:online-coop-undo] a taken track line pops the member's line tool back to the depth it was laid on; a take-back whose walk found the circuit valid ends the tool, as the
			// tool's own undo does (its continuation runs outside a click) (C5-3)
			if ( decided.Steps[0] is { Kind: StepKind.TrackLine } line )
			{
				if ( result.Value is TrackLineDone { Valid: true } )
					taker.TrackTools.Remove( line.Ride );
				else
					taker.TrackTools[line.Ride] = new LineTool( false, line.Depth );
			}
		}
		foreach ( var cell in run.Keys.Union( runTracks.Keys ) )
		{
			if ( decided.Steps.Any( step => step.Effects.ContainsKey( cell ) || step.TrackEffects.ContainsKey( cell ) ) )
				continue;
			// the shared bend's clearing unlinks the member's own earlier segment beside it: that chain's own writes, no marker (another player's records there stay)
			if ( decided.Bends.Any( bend => bend.Owner.Effects.ContainsKey( cell ) || bend.Owner.Footprint.Contains( cell ) ) )
				continue;
			if ( !histories.TryGetValue( cell, out var list ) )
				continue;
			Append( list, NewResidue( by, WholeAt( cell, run, runTracks ) ) );
		}
		foreach ( var (bend, owner) in decided.Bends )
		{
			// the earlier segment's weight in its member's bounds shrinks with its footprint
			var weight = owner.Weight;
			if ( !owner.Footprint.Remove( bend ) )
				continue;
			if ( members.TryGetValue( owner.Member, out var holder ) && holder.Steps.Contains( owner ) )
				holder.Cells -= weight - owner.Weight;
			Untrack( bend );
		}
	}

	// ---- departures and towers ---------------------------------------------------------------------

	/// <summary>
	/// A member left its seat (or another took it): its steps leave as they are, never taken back; their records stay in the cells' lists for good, and so does its residue, now a
	/// permanent marker with what it left (no step of its passes it any more), so a list holds the residue of the members present alone.
	/// </summary>
	internal void Depart( uint id )
	{
		if ( Disabled || !members.Remove( id, out var member ) )
			return;
		try
		{
			foreach ( var step in member.Steps.ToArray() )
				Leave( member, step, takenBack: false );
			foreach ( var list in histories.Values )
			{
				var own = list.Where( entry => entry < 0 && residueOf[entry].Member == id ).ToArray();
				if ( own.Length == 0 )
					continue;
				foreach ( var entry in own )
					residueOf[entry] = (Nobody, residueOf[entry].Stayed);
				Normalize( list );
			}
		}
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			Disable( error );
		}
	}

	/// <summary>A slide tower left the park (sold, demolished, cleared, picked up): its pieces went with it, so every step of its slide leaves.</summary>
	internal void TowerRemoved( int towerX, int towerY )
	{
		if ( Disabled )
			return;
		try
		{
			DropSlide( PoolSlide.KeyOf( towerX, towerY ) );
		}
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			Disable( error );
		}
	}

	private void DropSlide( int key )
	{
		foreach ( var member in members.Values )
		{
			foreach ( var step in member.Steps.Where( step => step.Kind == StepKind.SlidePiece && step.Group == key ).ToArray() )
				Leave( member, step, takenBack: false );
		}
	}

	/// <summary>The session ends: the journal empties.</summary>
	internal void Clear()
	{
		members.Clear();
		live.Clear();
		residueOf.Clear();
		histories.Clear();
		tracked.Clear();
		running.Clear();
		runningTracks.Clear();
		begun = false;
		pending = null;
	}

	// ---- the decision -----------------------------------------------------------------------------

	/// <summary>
	/// Decides a take-back before its rights and its run (<see cref="ParkCommands.Prepare"/>): <paramref name="command"/> as the host decided it, of <paramref name="origin"/>'s member (null for
	/// the host's own). Any other command passes as it is. Whatever verdict, inverse and claimed ride a request carried are replaced: the claimed ride is the step's own, which the host
	/// puts there (the track and coaster steps', C5-3; no step of the path, queue and slide tools has one), so the rights never ask a ride a request named.
	/// </summary>
	internal ParkCommand Decide( ParkCommand command, uint? origin )
	{
		pending = null;
		if ( command is not TakeBackCommand request )
			return command;
		var asked = request with { Verdict = TakeBackVerdict.Undecided, Inverse = null, ClaimedRide = -1 };
		if ( Disabled )
			return asked with { Verdict = TakeBackVerdict.Forgotten };
		try
		{
			Fault?.Invoke();
			// [EXT:online-coop-undo] the claimed ride is the step's own, live or a tombstone, whatever verdict follows (C5-3): the rights ask its claim of the member
			asked = asked with { ClaimedRide = ClaimedRideOf( asked, MemberOf( origin ) ) };
			var verdict = Judge( asked, MemberOf( origin ), out var decision );
			if ( verdict != TakeBackVerdict.Taken || decision == null )
				return asked with { Verdict = verdict };
			pending = decision;
			return decision.Command;
		}
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			Disable( error );
			return asked with { Verdict = TakeBackVerdict.Forgotten };
		}
	}

	/// <summary>
	/// [EXT:online-coop-undo] The ride a take-back of <paramref name="id"/>'s is on (C5-3): the track line or the pylon it names, a step or a tombstone of the member's, puts its ride there (a
	/// coaster's station); any other step, a stroke and no step at all −1, so the rights never ask a ride a request named.
	/// </summary>
	private int ClaimedRideOf( TakeBackCommand request, uint id ) => !request.Stroke && members.TryGetValue( id, out var member )
		&& member.Steps.LastOrDefault( candidate => candidate.Record == request.Step && candidate.Part == request.Part ) is { Tool: StepTool.Track or StepTool.Pylon } step ? step.Ride : -1;

	private TakeBackVerdict Judge( TakeBackCommand request, uint id, out Decision? decision )
	{
		decision = null;
		// [EXT:online-coop-undo] a pool stroke's take-back takes its entries as one step set (C5-2)
		if ( request.Stroke )
			return JudgeStroke( request, id, out decision );
		if ( !members.TryGetValue( id, out var member ) )
			return TakeBackVerdict.NotOwn;
		var step = member.Steps.LastOrDefault( candidate => candidate.Record == request.Step && candidate.Part == request.Part );
		if ( step == null )
			return request.Step <= member.Forgot ? TakeBackVerdict.Forgotten : TakeBackVerdict.NotOwn;
		// [EXT:online-coop-undo] a track line or a pylon whose claim ended or changed hands, or whose ride a cut, a node edit or a swap changed, is a tombstone: changed, before the last-step
		// check (C5-3); the rights refuse it first, through its ride's claim, while the member holds no claim of it
		if ( step.IsBuried )
			return TakeBackVerdict.Changed;
		// a piece of a slide finished since (its own queued splash, say) is refused before anything else, quietly: a finished slide takes riders, its pieces go only with its tower (C3b-3)
		if ( step.Kind == StepKind.SlidePiece && level.PoolSlides?.Find( step.Group ) is { IsComplete: true } )
			return TakeBackVerdict.InTheWay;
		var last = member.Steps.LastOrDefault( candidate => candidate.Tool == step.Tool && candidate.Group == step.Group );
		if ( !ReferenceEquals( last, step ) )
			return TakeBackVerdict.NotLast;
		var records = new HashSet<long> { step.Record };
		foreach ( var cell in step.Footprint )
		{
			if ( ChangedSince( step, cell, records ) )
				return TakeBackVerdict.Changed;
		}
		var player = request.Player;
		switch ( step.Kind )
		{
			case StepKind.PathLine:
			case StepKind.QueueLine:
			{
				var trace = new HashSet<(int X, int Y)>();
				if ( !(step.Kind == StepKind.PathLine ? level.PredictPathLineUndo( step.Segment!, trace ) : level.PredictQueueLineUndo( step.Ride, step.Segment!, trace )) )
					return TakeBackVerdict.InTheWay;
				// [BIN:TPW-EXE:0x0052FFEC Back's re-evaluation] a path undo re-evaluates the issuer's selection: the host's own, never a guest's, and never a ride another seat claims
				var ride = step.Kind == StepKind.QueueLine ? step.Ride
					: id == host && request.SelectedRide >= 0 && !level.ClaimedAgainst( ParkCommands.LocalPlayer, request.SelectedRide ) ? request.SelectedRide : -1;
				var inverse = new UndoLineCommand( step.Mode, ride, step.Start.X, step.Start.Y, step.Segment!.Built.ToArray(), step.Segment.Existing.ToArray() ) { Player = player };
				return Decided( request, inverse, new[] { step }, Array.Empty<((int X, int Y), Step)>(), out decision );
			}
			case StepKind.QueueSegment:
			{
				var trace = new HashSet<(int X, int Y)>();
				var bends = new List<((int X, int Y) Cell, Step Owner)>();
				// a counted-up bend corner the original's undo clears with this segment may be the member's own earlier segment's, which created it and whose own chain alone wrote it since:
				// that segment's footprint then leaves it (an own chain going back); any other is in the way
				if ( !level.PredictQueueSegmentUndo( step.Ride, step.Start, step.End, step.Footprint, step.Before, trace, cell =>
				{
					var owner = member.Steps.LastOrDefault( other => !ReferenceEquals( other, step ) && other.Kind == StepKind.QueueSegment && other.Ride == step.Ride
						&& other.Footprint.Contains( cell ) && other.ClearsWhole.Contains( cell ) );
					if ( owner == null || !OwnChainSince( owner, cell ) )
						return false;
					bends.Add( (cell, owner) );
					return true;
				} ) )
					return TakeBackVerdict.InTheWay;
				var inverse = new UndoQueueSegmentCommand( step.Ride, step.Start.X, step.Start.Y, step.End.X, step.End.Y ) { Player = player };
				return Decided( request, inverse, new[] { step }, bends, out decision );
			}
			case StepKind.SlidePiece:
			{
				if ( level.PoolSlides?.Find( step.Group ) is not { } slide || slide.Cells.Count != step.PieceIndex + 1 || slide.Cells[^1] != step.PieceCell )
					return TakeBackVerdict.Changed;
				var inverse = new UndoPoolSlidePieceCommand( step.Tower.X, step.Tower.Y ) { Player = player };
				return Decided( request, inverse, new[] { step }, Array.Empty<((int X, int Y), Step)>(), out decision );
			}
			// [EXT:online-coop-undo] a kart or water track line (C5-3): its tool's own Backspace, the directional clear with the first-line flag its depth gives (the pop that leaves one point)
			case StepKind.TrackLine:
			{
				// a tile at the counter's limit rose by nothing, and the clear counts down every tile it visits
				if ( step.Saturated )
					return TakeBackVerdict.InTheWay;
				var inverse = new UndoTrackLineCommand( step.Ride, step.Start.X, step.Start.Y, step.End.X, step.End.Y, step.Depth == 1 ) { Player = player };
				if ( !level.PredictTrackLineUndo( inverse, step.Laid, step.Raised ) )
					return TakeBackVerdict.InTheWay;
				return Decided( request, inverse, new[] { step }, Array.Empty<((int X, int Y), Step)>(), out decision );
			}
			// [EXT:online-coop-undo] a coaster pylon (C5-3): the coaster tool's own Backspace, which takes the last pylon off
			case StepKind.CoasterPylon:
			{
				if ( level.ResolveCommandObject( step.Ride ) is not { } station || level.GetCoasterBuilder( station ) is not { } builder )
					return TakeBackVerdict.Changed;
				// the inverse's own preconditions first, so the host never decides a take-back its inverse refuses: the coaster in the tool (a tool closed while its claim stays, or a crafted
				// request after it closed), and a last pylon that can go (none on a complete track; the one before the cut on a cut circuit)
				if ( station.Runtime.Coaster is not { BeingEdited: true } || !builder.CanRemoveLast )
					return TakeBackVerdict.InTheWay;
				// the coaster's last pylon must be exactly the step's: the node it placed, where it placed it
				var track = builder.Track;
				if ( track.LastIndex != step.PieceIndex || !ReferenceEquals( track.Nodes[track.LastIndex], step.Pylon ) )
					return TakeBackVerdict.Changed;
				var inverse = new RemoveLastCoasterPylonCommand( step.Ride ) { Player = player };
				return Decided( request, inverse, new[] { step }, Array.Empty<((int X, int Y), Step)>(), out decision );
			}
		}
		return TakeBackVerdict.NotOwn;
	}

	/// <summary>
	/// [EXT:online-coop-undo] A pool stroke's take-back (C5-2): the member's entries of the stroke it names (its first command's record), taken as one step set when it is the member's newest
	/// stroke, none of its cells changed since by anyone else, and its one inverse (<see cref="UndoPoolStrokeCommand"/>: the erased cells painted again, or the river marks' flags off and the
	/// painted cells erased) predicted whole on a copy of the cells with a copy of the balance.
	/// </summary>
	private TakeBackVerdict JudgeStroke( TakeBackCommand request, uint id, out Decision? decision )
	{
		decision = null;
		if ( !members.TryGetValue( id, out var member ) )
			return TakeBackVerdict.NotOwn;
		if ( member.Clipped.Contains( request.Step ) )
			return TakeBackVerdict.Forgotten;
		// the live stroke is found before the age cutoff: a stroke whose refused first batch named it is older than its entries
		var entries = member.Steps.Where( step => step.Kind == StepKind.PoolStroke && step.Stroke == request.Step ).ToArray();
		if ( entries.Length == 0 )
			return request.Step <= member.Forgot ? TakeBackVerdict.Forgotten : TakeBackVerdict.NotOwn;
		if ( member.Steps.LastOrDefault( step => step.Tool == StepTool.Stroke ) is not { } newest || newest.Stroke != request.Step )
			return TakeBackVerdict.NotLast;
		var records = entries.Select( step => step.Record ).ToHashSet();
		foreach ( var entry in entries )
		{
			foreach ( var cell in entry.Footprint )
			{
				if ( ChangedSince( entry, cell, records ) )
					return TakeBackVerdict.Changed;
			}
		}
		var written = entries.SelectMany( step => step.Written ).ToArray();
		var marked = entries.SelectMany( step => step.RiverMarked ).ToArray();
		if ( written.Length > StrokeCellLimit || marked.Length > StrokeCellLimit )
			return TakeBackVerdict.TooLarge;
		var inverse = new UndoPoolStrokeCommand( entries[0].Erases, written, marked ) { Player = request.Player };
		if ( !level.PredictPoolStrokeUndo( inverse, entries.SelectMany( step => step.Footprint ).ToHashSet() ) )
			return TakeBackVerdict.InTheWay;
		return Decided( request, inverse, entries, Array.Empty<((int X, int Y), Step)>(), out decision );
	}

	/// <summary>
	/// Whether a command still in effect that is none of the step set's (<paramref name="records"/>) changed <paramref name="cell"/> since the set first did: its list must hold, from the set's
	/// first record on, only the set's records and these exceptions, so a step that wrote a cell on both sides of another player's write is refused (for a step of one record, its list ends
	/// with it): <paramref name="step"/>'s member's own residue marker on a cell its inverse clears whole, which leaves nothing of that residue behind; and on a cell the step only counted up,
	/// a later live step's record whose effect there left the cell's type, counter and other fields and the step's own links alone (another line's link into it from elsewhere): the
	/// count-down takes back the step's own increment and links only. A later crossing of that cell (its counter raised again) stays a dependency, so crossings go back newest first and each
	/// take-back finds the counter as its step left it; and on such a cell a marker, residue or permanent, that left only link bits the step did not change (a diagonal of a line taken
	/// back since, a link from a line that ended its tool). Any other marker (another member's residue, a permanent one that left a field or one of the step's links changed), a removal and any
	/// other record stay changes.
	/// </summary>
	private bool ChangedSince( Step step, (int X, int Y) cell, IReadOnlySet<long> records )
	{
		if ( !histories.TryGetValue( cell, out var list ) )
			return true;
		var first = list.FindIndex( records.Contains );
		if ( first < 0 )
			return true;
		var clearsWhole = step.ClearsWhole.Contains( cell );
		var countedUp = step.CountedUp.Contains( cell );
		var riverOnly = step.Kind == StepKind.PoolStroke && step.RiverMarked.Contains( cell );
		for ( var index = first + 1; index < list.Count; index++ )
		{
			var entry = list[index];
			if ( records.Contains( entry ) || (clearsWhole && IsResidue( entry ) && residueOf[entry].Member == step.Member)
				|| (countedUp && (LeavesCountAlone( step, cell, entry ) || ResidueLeavesCountAlone( step, cell, entry ))) || (riverOnly && LeavesRiverAlone( cell, entry )) )
				continue;
			return true;
		}
		return false;
	}

	/// <summary>
	/// Whether only <paramref name="owner"/>'s member's own live steps wrote <paramref name="cell"/> since <paramref name="owner"/> did: every entry of its list from the owner's record on is a
	/// record of a live step of that member, so clearing the cell takes back that member's own chain and no other player's work.
	/// </summary>
	private bool OwnChainSince( Step owner, (int X, int Y) cell )
	{
		if ( !histories.TryGetValue( cell, out var list ) )
			return false;
		var first = list.IndexOf( owner.Record );
		if ( first < 0 )
			return false;
		for ( var index = first + 1; index < list.Count; index++ )
		{
			if ( list[index] < 0 || !live.TryGetValue( list[index], out var other ) || other.Member != owner.Member )
				return false;
		}
		return true;
	}

	/// <summary>
	/// Whether <paramref name="entry"/>, a marker (a member's residue or a permanent marker), left a cell <paramref name="step"/> only counted up as the step's count-down needs it: what it
	/// left changed there is link bits alone, none of them one the step changed (a diagonal another member's taken-back line left, a link into the cell from a line that ended its tool).
	/// </summary>
	private bool ResidueLeavesCountAlone( Step step, (int X, int Y) cell, long entry )
	{
		if ( entry >= 0 || !residueOf.TryGetValue( entry, out var residue ) )
			return false;
		var stayed = residue.Stayed;
		// [EXT:online-coop-undo] on a track tile a marker that left the tile's identity, counter and arms alone (a walk's refresh: its flow link, step number, piece) leaves the count-down as
		// it needs it (C5-3)
		if ( stayed.Fields || stayed.Waves || stayed.TrackTile )
			return false;
		if ( stayed.Links == 0 )
			return true;
		return step.Effects.TryGetValue( cell, out var own ) && (stayed.Links & (own.Before.Links ^ own.After.Links)) == 0;
	}

	/// <summary>
	/// [EXT:online-coop-undo] Whether <paramref name="entry"/>, a later change of a cell a stroke only river-marked, leaves that mark's take-back exact (C5-2): the inverse takes the river flag off
	/// and nothing else, so a change of a wave machine's bits alone (its settings, a record of no step) passes, as a link would; anything else stays a change.
	/// </summary>
	private bool LeavesRiverAlone( (int X, int Y) cell, long entry )
	{
		if ( entry < 0 )
			return residueOf.TryGetValue( entry, out var marker ) && !marker.Stayed.Fields && !marker.Stayed.TrackTile && !marker.Stayed.TrackShape;
		if ( !live.TryGetValue( entry, out var other ) || !other.Effects.TryGetValue( cell, out var effect ) || other.TrackEffects.ContainsKey( cell ) )
			return false;
		var (before, after) = effect;
		return (before with { Flags = (ushort)(before.Flags & ~WaveBits) }) == (after with { Flags = (ushort)(after.Flags & ~WaveBits) });
	}

	/// <summary>
	/// Whether <paramref name="entry"/>, a live step's record, left a cell <paramref name="step"/> only counted up as the step's count-down needs it: the cell's type, flags, counter, owner,
	/// queue link and residue as they were, its links changed only on bits the step did not change.
	/// </summary>
	private bool LeavesCountAlone( Step step, (int X, int Y) cell, long entry )
	{
		if ( entry < 0 || !live.TryGetValue( entry, out var other ) )
			return false;
		var onMap = other.Effects.TryGetValue( cell, out var effect );
		var onTrack = other.TrackEffects.TryGetValue( cell, out var tile );
		if ( !onMap && !onTrack )
			return false;
		if ( onMap )
		{
			if ( !step.Effects.TryGetValue( cell, out var own ) )
				return false;
			var (before, after) = effect;
			if ( !(before.Type == after.Type && before.Flags == after.Flags && before.Placements == after.Placements && before.Owner == after.Owner && before.QueueLink == after.QueueLink
				&& before.Residue == after.Residue
				&& ((before.Links ^ after.Links) & (own.Before.Links ^ own.After.Links)) == 0) )
				return false;
		}
		// [EXT:online-coop-undo] a track tile's identity, counter and arms as they were: what the count-down reads (C5-3)
		return !onTrack || !Stayed.OfTrack( tile.Before, tile.After ).TrackTile;
	}

	private static TakeBackVerdict Decided( TakeBackCommand request, ParkCommand inverse, IReadOnlyList<Step> steps, IReadOnlyList<((int X, int Y) Cell, Step Owner)> bends,
		out Decision? decision )
	{
		decision = null;
		if ( ParkCommandCodec.Encode( inverse ).Length > TakeBackCommand.MaximumInverseBytes )
			return TakeBackVerdict.TooLarge;
		decision = new Decision( request with { Verdict = TakeBackVerdict.Taken, Inverse = inverse }, steps, bends );
		return TakeBackVerdict.Taken;
	}
}
