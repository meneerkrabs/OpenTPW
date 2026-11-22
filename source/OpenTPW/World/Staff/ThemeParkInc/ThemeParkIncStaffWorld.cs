namespace OpenTPW;

/// <summary>
/// Theme Park Inc's staff in the park (docs/reverse/TPI-STAFF.md §11): the placed members' things and their state machines, beside Theme Park World's
/// <see cref="StaffWorldSimulation"/>, which it does not touch. The handyman runs Theme Park Inc's own states 0 (idle), 1 (walk), 0x0F (to litter) and 0x10 (sweep) with its
/// meters, day counters and search, and the training states 7 and 8; the mechanic runs 0, 1, 21 (to the ride) and 22 (repair) with the ride wear of ParkEconomy.UpdateThemeParkIncRide; the
/// gardener runs 0, 1, 0x1F (to the plant) and 0x20 (watering) with the plants of ParkEconomy.UpdateThemeParkIncPlant; the mechanic and the gardener train as the handyman does; the researcher
/// runs his states 0, 1, 24 (researching) and 25 (to his research room); the entertainer and the guard are refused at the hire (<c>[EXT:tpi-capability-gates]</c>).
/// </summary>
/// <remarks>
/// [EXT:tpi-twp-guest-rules] (user-approved 2026-11-21 for the staff's walk) the walk step 0x60B440, the route search behind the staff route 0x62EB10 (SetDest 0x63E3A0; the route's entrance refusal, walk mask and the cell steer 0x60B7F0 are ported, StaffRoute), the patrol destination 0x609F70 and the speed step 0x60BC90 are not
/// proven identical to Theme Park World's (WP0 movement: D1 the arrival heading, D2 the run flag, D5 the cell node stride; 0x609F70 is read as a call skeleton only), so a member walks
/// Theme Park World's walk (the guests' steering step with separation, the route search, the wander destination 0x004F9490 with its off-path search, the speed smoothing 0x004FA870 and the
/// caps 0x00510190), over Theme Park Inc's own speed words (+0xD4, +0xD6, +0xD8).
/// [EXT:tpi-no-maintenance-wear] (user-approved 2026-11-21) Theme Park Inc's toilet wear and the bins' fill are unported, so the loo finder 0x5D6230 and the bin finder 0x5D5F10 find nothing
/// to clean: the handymen sweep litter only (states 0x11 to 0x14 are not entered). The rides' wear and the mechanics are ported (ThemeParkIncStaffWorld.Mechanics,
/// ParkEconomy.UpdateThemeParkIncRide).
/// </remarks>
public sealed partial class ThemeParkIncStaffWorld
{
	/// <summary>The handyman's states this world runs (the state word +0x1B4).</summary>
	public const int Idle = 0, Walking = 1, ToLitter = 0x0F, Sweeping = 0x10;

	/// <summary>The sprite templates the state setters queue on the member's sprite source (+0x14, 0x5D0F90): idle 3, walk 0xF, work 0x19.</summary>
	public const int IdleTemplate = 3, WalkTemplate = 0xF, WorkTemplate = 0x19;

	/// <summary>The speed bonus table 0x9CFB20 (words): the index the state-entry helper 0x60B3F0 takes.</summary>
	// [BIN:TPI-EXE:0x60B3F0 speed bonus] +0xD6 = word 0x9CFB20[index], the index the argument for a staff kind (the kind-1 guest rule does not apply): 0 gives 0, 1 gives 25
	private static ReadOnlySpan<ushort> BonusTable => [0, 25];

	/// <summary>
	/// The base-speed words the strike gate reads by the energy byte's tens: 0x9CFB28 holds five (60 to 140) and the gate indexes past them with tens up to 9, reading the bytes that
	/// follow in the image (the float 2.5's and 6.0's high words at the indexes 7 and 9).
	/// </summary>
	// [BIN:TPI-EXE:0x62BDB4 speed by energy] index = energy byte / 10 (0x66666667, sar 3) into the words at 0x9CFB28: 60, 80, 100, 120, 140, 0, 0, 0x4020, 0, 0x40C0 (read from the image)
	private static ReadOnlySpan<ushort> EnergySpeeds => [60, 80, 100, 120, 140, 0, 0, 0x4020, 0, 0x40C0];

	private readonly GuestPathGrid grid;

	public ThemeParkIncStaffWorld( GuestPathGrid grid ) => this.grid = grid ?? throw new ArgumentNullException( nameof( grid ) );

	/// <summary>The guests: the members steer apart from them and share their cell chains (WALK-003); null in a test without guests.</summary>
	public GuestSimulation? Guests { get; set; }

	/// <summary>The staff sprite banks the level loaded from Theme Park Inc's <c>esprites.wad</c> (presentation); null: none (tests, a headless world).</summary>
	public IStaffSpriteBanks? SpriteBanks { get; set; }

	/// <summary>The sprite clock sample of the latest running tick, the time a new sprite source is made at (presentation).</summary>
	public int SpriteClockMilliseconds { get; set; }

	/// <summary>
	/// The Theme Park World program a Theme Park Inc template number runs: Theme Park Inc's table 0x9CDA94 holds the same sprite programs as tp.exe's 0x0075A418 under other numbers, opcode for opcode.
	/// </summary>
	// [BIN:TPI-EXE:0x9CDA94 staff sprite programs] 1 (0x9AFBA8) SET 0x10 = 0x1200, SLOT 1, FRAME 0..7, JMP: tp.exe's walk 1; 2 (0x9AFC04) SLOT 2, FRAME 0..7: the run 2; 3 (0x9AFC60) SLOT 0, FRAME 0: the stand 3;
	// 0xF (0x9AFB0C) SLOT 1, each FRAME 0..7 twice: the patrol walk 9; 0x19 (0x9B0630) SLOT 4, each FRAME 0..3 twice: the sweep 0x11; 0x1A (0x9B068C) SET 0x10 = 0x1200, SLOT 4, FRAME 0..7,
	// JMP: the repair 0x12 (tp.exe 0x0074EE38) (the opcodes 0x40748C -> 0x4E5A20 SET, 0x401091 -> 0x4E61F0 SLOT,
	// 0x404F43 -> 0x4E62B0 FRAME and 0x409291 -> 0x4E4FE0 JMP stand where tp.exe's 0x004766D0, 0x004768B0, 0x00476940 and 0x00476020 do, with the same operands)
	public static int ProgramOf( int template ) => template switch
	{
		1 => StaffSpriteEvents.Walk,
		2 => StaffSpriteEvents.Run,
		IdleTemplate => StaffSpriteEvents.Stand,
		WalkTemplate => StaffSpriteEvents.Patrol,
		WorkTemplate => StaffSpriteEvents.Sweep,
		RepairTemplate => StaffSpriteEvents.Repair,
		_ => throw new ArgumentOutOfRangeException( nameof( template ), $"Theme Park Inc sprite template {template} is not ported." )
	};

	/// <summary>The bank a member is drawn from: the candidate's sprite draw, (R >> 2) mod the loaded banks of the role's category (9 handymen, 0xA mechanics), kept as a byte.</summary>
	// [BIN:TPI-EXE:0x6902F0 sprite variant] (draw >> 2) divided (unsigned) by the category's loaded count (0x9D7008 + 12 × category), the remainder; the candidate keeps its low byte (+0x10) and the
	// member its variant word +0x30, which 0x5D1000 hands to the sprite; the count is the banks the level loaded from Generic\Handymen (one, Spr_ha, in the retail data)
	// [BIN:TPI-EXE:0x5D9BB4 mechanic sprite] the mechanic's constructor makes its source with the category 0xA and the variant byte it was given (+0x30); esprites.wad holds the mechanics' bank
	// in Generic\Mechanics (Spr_om), the folder Theme Park World's loader reads for its mechanics
	public static int VariantOf( ThemeParkIncStaffMember member, IStaffSpriteBanks? banks ) =>
		banks?.BankCount( StaffTypeOf( member.Role ) ) is > 0 and var count ? (int)((member.SpriteDraw >> 2) % (uint)count) & 0xFF : 0;

	/// <summary>The sprite bank family of a member's role: the handymen's (category 9), the mechanics' (0xA), the researchers' (0xC, <c>0x6902F0(0xC)</c>) or the gardeners' (0x12).</summary>
	internal static StaffType SpriteTypeOf( ThemeParkIncStaffMember member ) => StaffTypeOf( member.Role );

	/// <summary>The staff sprite set's role a placed member is drawn as.</summary>
	public static StaffType StaffTypeOf( ThemeParkIncStaffRole role ) => role switch
	{
		ThemeParkIncStaffRole.Handyman => StaffType.Handyman,
		ThemeParkIncStaffRole.Mechanic => StaffType.Mechanic,
		ThemeParkIncStaffRole.Researcher => StaffType.Researcher,
		ThemeParkIncStaffRole.Gardener => StaffSpriteRules.ThemeParkIncGardener,
		_ => throw new ArgumentOutOfRangeException( nameof( role ), $"Theme Park Inc's {role}s are not placed in the park." )
	};

	/// <summary>The thing model of a placed member's thing (the kind byte +0x2: 5 a handyman, 4 a mechanic, 8 a researcher, 0x14 a gardener).</summary>
	public static SaveThingModel ThingModelOf( ThemeParkIncStaffRole role ) => role switch
	{
		ThemeParkIncStaffRole.Handyman => SaveThingModel.Handyman,
		ThemeParkIncStaffRole.Mechanic => SaveThingModel.Mechanic,
		ThemeParkIncStaffRole.Researcher => SaveThingModel.Researcher,
		ThemeParkIncStaffRole.Gardener => SaveThingModel.Gardener,
		_ => throw new ArgumentOutOfRangeException( nameof( role ), $"Theme Park Inc's {role}s are not placed in the park." )
	};

	/// <summary>
	/// A state setter's template on the member's sprite wrapper: queued for the next pre-update, or a new source made in it when the member has none (the setters' tail, 0x628180: 0x5D1000 without a
	/// handle, else 0x5D0F90).
	/// </summary>
	private void RequestTemplate( ThemeParkIncStaffMember member, int template )
	{
		member.Template = template;
		if ( member.Sprite.Source == null )
			member.Sprite.Create( SpriteTypeOf( member ), VariantOf( member, SpriteBanks ), ProgramOf( template ), SpriteClockMilliseconds, SpriteBanks );
		else
			member.Sprite.QueueTemplate( ProgramOf( template ) );
	}

	/// <summary>The sprite scheduler's pass over the members' sources (Theme Park World's 0x00475360, on the even substeps): a source whose due time is before the clock sample takes one step.</summary>
	public void ScheduleSprites( ParkEconomy? economy, int nowMilliseconds )
	{
		if ( economy?.TpiStaff is not { } staff )
			return;
		foreach ( var member in staff.Members )
			member.Sprite.Tick( nowMilliseconds );
	}

	private const float TicksPerTurn = ParkCalendar.TicksPerSecond * ParkCalendar.TurnMilliseconds / 1000f;

	/// <summary>
	/// Presentation, every fixed tick: the point each member is drawn at, between his position at the start of his last update and his current one over the turn (Theme Park World's interpolation
	/// 0x004F9F00, as its staff world's).
	/// </summary>
	public void TickPresentation( ParkEconomy? economy )
	{
		if ( economy?.TpiStaff is not { } staff || Guests is not { } guests )
			return;
		foreach ( var member in staff.Members )
		{
			if ( member.Navigation is not { } navigation )
				continue;
			if ( member.TurnTick < 0 )
				(member.TurnFromX, member.TurnFromY, member.TurnTick) = (navigation.X, navigation.Y, guests.TickCount);
			var (x, y) = PersonInterpolation.Point( member.TurnFromX, member.TurnFromY, navigation.X, navigation.Y, (guests.TickCount - member.TurnTick) / TicksPerTurn, clamped: true );
			member.DrawX = x / (float)Fixed16.One;
			member.DrawY = y / (float)Fixed16.One;
		}
	}

	/// <summary>The members' voices of the last updates: the handyman's walking (0xA0) and idle (0xA1) voice events at a member's position. Presentation, taken by the audio.</summary>
	public List<(int Event, float X, float Y)> Voices { get; } = new();

	/// <summary>
	/// The object whose thing a dropped-on cell holds, as the drop's gate 0x4D1FC0 reads it: its <c>Info.WhichUIType</c> (type record <c>+0x600</c>), its thing cell (where its thing is
	/// filed), its placement angle, its shape and its map offset.
	/// </summary>
	public sealed record DropObject( int WhichUIType, (int X, int Y) ThingCell, int Rotation, ObjectShape Shape, int MapOffsetX, int MapOffsetY );

	/// <summary>The outcome of a drop: the cell the member is made on, or none with or without the refusal's message.</summary>
	public readonly record struct DropResult( (int X, int Y)? Cell, bool Silent );

	/// <summary>
	/// The drop of a staff member on cell (<paramref name="x"/>, <paramref name="y"/>) (0x4D4EB0): a type-4 cell whose chain holds an object's thing of a known kind is redirected to that
	/// object's exit cell (a ride), its entrance cell (a shop, a sideshow and kinds 4 and 5) or the path cell beside its footprint nearest the drop (a feature), and ends silently when there is
	/// none; every other cell takes the ladder (owned, no fence post or track record, of type 0, 1, 3 or 9) and is the member's cell, or is refused with the message. No occupancy is tested.
	/// </summary>
	// [BIN:TPI-EXE:0x4D4EB0 staff drop] the gate 0x4D1FC0 first; a nonzero M skips the ladder to 0x4D4F60. Otherwise the flag 0x40 (0x67ECA0), the fence post (0x67F920 -> 0x685710, kind 0x19),
	// the track record (0x67F890 -> 0x6856D0, kinds 0xB, 0xD, 0x10, 0x12, 0x19; a record of kind 0xC or 0x11 answers through its parent word +0x10) and the type (== 1 0x67F5B0, 3 or 9 0x67F5D0,
	// == 0 0x67F6D0) in that order, as tp.exe's 0x46CC40 (the fence kind is one of the track kinds, so StaffPlacementRules.IsValidDropCell without an occupancy callback is the same ladder); a
	// refusal prints "Cannot place staff member here - the cell is of type %d" (0x4D51E8). At 0x4D4F60 the gate runs again and 0x4D2180(M, the cursor's cell word, the thing) gives the word
	// stored at 0x14(%esp), the cell the role's constructor (the handyman's 0x5D48B0) receives; a word of 0 ends the drop at 0x4D51FB with no message and the tool stays. No occupancy or object
	// list is read anywhere in 0x4D4EB0..0x4D4F94
	// [BIN:TPI-EXE:0x4D1FC0 drop gate M] a cell of type 4 (+8) whose chain (+0x28, next +0xA, 0x5D9560) holds a kind-3 thing (byte +2) gives that thing and M from its type record's +0x600
	// (0x5DF6D0 = the type of the thing's +0x16): 0 -> 1, 1 -> 2, 2 -> 3, 3 -> 4, 4 and 5 -> 5, above 5 (unsigned) -> 0; any other cell M = 0. The type loader 0x43D014 copies +0x70 into +0x600,
	// and +0x70 is Info.WhichUIType by the Info key order (Id +0x10, Shape +0x1C, MapOffsetX/Y +0x34/+0x38, WhichUIType the 25th key): 0 ride, 1 shop, 2 sideshow, 3 feature
	// [BIN:TPI-EXE:0x4D2180 drop redirect] M 1 takes the thing's word +0x48, M 2, 3 and 5 its word +0x46, M 4 none, any other M the cursor's own cell word; a word of 0 with a thing falls back
	// to 0x5E9320 (FallbackDropWord)
	public static DropResult Drop( ParkCellMap cells, int x, int y, DropObject? thing )
	{
		if ( !cells.InBounds( x, y ) )
			return new( null, false );
		var m = cells.RawTypeAt( x, y ) == 4 && thing != null ? DropGate( thing.WhichUIType ) : 0;
		if ( m == 0 )
			return StaffPlacementRules.IsValidDropCell( cells, x, y, occupied: null ) ? new( (x, y), false ) : new( null, false );
		var word = m switch
		{
			1 => ExitWord( thing! ),
			2 or 3 or 5 => EntranceWord( thing! ),
			_ => (ushort)0
		};
		if ( word == 0 )
			word = FallbackDropWord( cells, thing!, CellWord( x, y ) );
		if ( word == 0 )
			return new( null, true );
		var cell = CellOfWord( word );
		// the words of a placed object lie on the map; a word off it would be the original's out-of-map record, which is not modelled
		return cells.InBounds( cell.X, cell.Y ) ? new( cell, false ) : new( null, true );
	}

	/// <summary>The drop gate's M of a kind-3 thing's <c>Info.WhichUIType</c> (0x4D1FC0's jump table 0x4D2084).</summary>
	internal static int DropGate( int whichUIType ) => (uint)whichUIType switch
	{
		0 => 1,
		1 => 2,
		2 => 3,
		3 => 4,
		4 or 5 => 5,
		_ => 0
	};

	/// <summary>A cell's word, <c>(y &lt;&lt; 7) + x + 1</c>, as the executable's 16-bit cell words are.</summary>
	internal static ushort CellWord( int x, int y ) => (ushort)((y << 7) + x + 1);

	/// <summary>The cell of a cell word.</summary>
	internal static (int X, int Y) CellOfWord( ushort word ) => ((word - 1) & 0x7F, (word - 1) >> 7);

	/// <summary>A cell offset turned by an angle (0x5D98D0, as tp.exe's 0x4D9CC0), as a word difference.</summary>
	private static ushort OffsetWord( int x, int y, int angle )
	{
		var (dx, dy) = OriginalObject.RotateOffset( x, y, angle );
		return (ushort)((dy << 7) + dx);
	}

	/// <summary>The thing's word +0x44: its own cell less its type's map offset turned by the angle plus 180 (0x5DB4B6), the placement anchor.</summary>
	// [BIN:TPI-EXE:0x5DB4B6 object cell words] the object's builder (0x5DB1B0; its first argument is the cell 0x637100 files the thing under, its third the angle) sets +0x44 = that cell +
	// the type's +0x604 (the map offset, copied from +0x34 and +0x38 by 0x43CFF0) turned by the angle + 0xB4 (0x5D98D0), +0x46 = +0x44 + the type's +0x5E8 turned by the angle and +0x48 =
	// +0x44 + the type's +0x5F4 turned by the angle (0x5DB4C1..0x5DB52B)
	private static ushort AnchorWord( DropObject thing ) =>
		(ushort)(CellWord( thing.ThingCell.X, thing.ThingCell.Y ) + OffsetWord( thing.MapOffsetX, thing.MapOffsetY, thing.Rotation + 180 ));

	/// <summary>The shape's entrance and exit cells as the type loader records them (type record <c>+0x5E8</c> and <c>+0x5F4</c>): (0, 0) for both without an entrance.</summary>
	// [BIN:TPI-EXE:0x43CF40 shape access cells] the scan runs the columns (U, a cell of 8 bytes, bounded by the shape's +0) in the outer loop and the rows (V, 0x140 bytes, bounded by +4) in the
	// inner one and stops at the first type 9 cell: +0x5E8 = its (U, V); then the first type 10 cell in the same order: +0x5F4, the entrance's (U, V) when there is none (0x43D0B6). A shape
	// without an entrance leaves both (0, 0) (0x43CFC4..0x43CFE2), whatever exit it has
	private static ((int U, int V) Entrance, (int U, int V) Exit) AccessOffsets( ObjectShape shape )
	{
		ObjectShapeCell? First( int type ) => shape.Cells.Where( cell => cell.CellType == type ).OrderBy( cell => cell.U ).ThenBy( cell => cell.V ).Cast<ObjectShapeCell?>().FirstOrDefault();
		if ( First( ObjectShapeSymbols.TypeEntrance ) is not { } entrance )
			return ((0, 0), (0, 0));
		var exit = First( ObjectShapeSymbols.TypeExit ) ?? entrance;
		return ((entrance.U, entrance.V), (exit.U, exit.V));
	}

	/// <summary>The thing's entrance word +0x46.</summary>
	internal static ushort EntranceWord( DropObject thing )
	{
		var (u, v) = AccessOffsets( thing.Shape ).Entrance;
		return (ushort)(AnchorWord( thing ) + OffsetWord( u, v, thing.Rotation ));
	}

	/// <summary>The thing's exit word +0x48.</summary>
	internal static ushort ExitWord( DropObject thing )
	{
		var (u, v) = AccessOffsets( thing.Shape ).Exit;
		return (ushort)(AnchorWord( thing ) + OffsetWord( u, v, thing.Rotation ));
	}

	/// <summary>
	/// The drop's fallback (0x5E9320): the path cell beside the object's footprint rectangle nearest the cursor's cell, which gets the queue link byte pointing from the footprint cell to it;
	/// 0 when no such cell is a path.
	/// </summary>
	// [BIN:TPI-EXE:0x5E9320 drop fallback] the corner (x0, y0) is +0x44 plus the map offset turned by the angle +0x18: the thing's own cell. The shape's +4 rows (i) outer and +0 columns (j) inner
	// give the cells (x0 + j, y0 + i) at 0, (x0 + i, y0 - j) at 90, (x0 - j, y0 + i) at 180 and (x0 + i, y0 + j) at 270 (0x5E9438..0x5E947E, exactly so: the 180 and 270 rectangles are not the
	// turned footprint), (0, 0) at any other angle; each cell's neighbours (y + 1), (x + 1), (y - 1), (x - 1) (the byte tables at 0x54 and 0x5C) as 16-bit words; a neighbour of type 1
	// (0x67F5B0) at the squared cell distance d from the cursor's word below the best (unsigned, 0xFFFFFFFF to start: the first wins a tie) is kept with its footprint cell. The kept neighbour's
	// record +0xD (the queue link) gets 0x5E9230(footprint, neighbour), the direction byte of the step (0x65CBA0), and its word is the answer; none answers 0
	internal static ushort FallbackDropWord( ParkCellMap cells, DropObject thing, ushort cursor )
	{
		var corner = (ushort)(AnchorWord( thing ) + OffsetWord( thing.MapOffsetX, thing.MapOffsetY, thing.Rotation ));
		var (x0, y0) = CellOfWord( corner );
		var (bx, by) = CellOfWord( cursor );
		var best = uint.MaxValue;
		ushort bestNeighbour = 0, bestCell = 0;
		ReadOnlySpan<(int Dy, int Dx)> steps = [(1, 0), (0, 1), (-1, 0), (0, -1)];
		for ( var i = 0; i < thing.Shape.Height; i++ )
		{
			for ( var j = 0; j < thing.Shape.Width; j++ )
			{
				var (px, py) = thing.Rotation switch
				{
					0 => (x0 + j, y0 + i),
					90 => (x0 + i, y0 - j),
					180 => (x0 - j, y0 + i),
					270 => (x0 + i, y0 + j),
					_ => (0, 0)
				};
				foreach ( var (dy, dx) in steps )
				{
					var neighbour = (ushort)(((py + dy) << 7) + px + dx + 1);
					var (nx, ny) = CellOfWord( neighbour );
					// a word of 0 or past the map would read the record array's sentinel or beyond it; no such record is a path here
					if ( neighbour == 0 || !cells.InBounds( nx, ny ) || cells.RawTypeAt( nx, ny ) != 1 )
						continue;
					var ex = (long)Math.Abs( bx - nx );
					var ey = (long)Math.Abs( by - ny );
					var distance = (uint)(ex * ex + ey * ey);
					if ( distance >= best )
						continue;
					best = distance;
					bestNeighbour = neighbour;
					bestCell = (ushort)((py << 7) + px + 1);
				}
			}
		}
		if ( bestNeighbour == 0 )
			return 0;
		var (fx, fy) = CellOfWord( bestCell );
		var (tx, ty) = CellOfWord( bestNeighbour );
		cells.SetQueueLink( tx, ty, StepDirection( fx, fy, tx, ty ) );
		return bestNeighbour;
	}

	/// <summary>The direction byte of a step from (<paramref name="fromX"/>, <paramref name="fromY"/>) to (<paramref name="toX"/>, <paramref name="toY"/>) (0x5E9230).</summary>
	// [BIN:TPI-EXE:0x5E9230 step direction] dx = to - from, dy likewise; by the quadrant 0x80, 0x20, 2 or 8, then 1 or 0x10 when 2|dx| < |dy| and 0x40 or 4 when 2|dy| < |dx|: (0, +1) is 1,
	// (+1, 0) 0x40, (0, -1) 0x10 and (-1, 0) 4
	internal static byte StepDirection( int fromX, int fromY, int toX, int toY )
	{
		var dx = toX - fromX;
		var dy = toY - fromY;
		byte direction;
		if ( dx >= 0 )
		{
			if ( dy >= 0 )
			{
				direction = 2 * dx < dy ? (byte)1 : (byte)0x80;
				return 2 * dy < dx ? (byte)0x40 : direction;
			}
			dy = -dy;
			direction = 2 * dx < dy ? (byte)0x10 : (byte)0x20;
			return 2 * dy < dx ? (byte)0x40 : direction;
		}
		dx = -dx;
		if ( dy >= 0 )
		{
			direction = 2 * dx < dy ? (byte)1 : (byte)2;
			return 2 * dy < dx ? (byte)4 : direction;
		}
		dy = -dy;
		direction = 2 * dx < dy ? (byte)0x10 : (byte)8;
		return 2 * dy < dx ? (byte)4 : direction;
	}

	/// <summary>
	/// A hired handyman dropped on <paramref name="cell"/>: its thing in the pool, the person's start (the world draw for the base speed, overwritten at once), the member's start
	/// (meters, patrol rectangle, base speed by experience) and the handyman's own start, which ends in its search.
	/// </summary>
	// [BIN:TPI-EXE:0x4D4FF7 drop handyman] the drop allocates the object and calls the handyman constructor 0x5D48B0 with the cell word, which runs 0x626190 (-> the person constructor 0x608F00
	// and the member start 0x626390) and the handyman start 0x5D4980
	// [BIN:TPI-EXE:0x637100 thing base] the thing takes the free list's head as its id and enters the thing list at its head (0x649270), so the newest thing is updated first
	// [BIN:TPI-EXE:0x608FA4 person start] one world draw: +0xD4 = word 0x9CFB28[R % 5], +0xD6 = word 0x9CFB20 (0); the position is the cell's centre (0x8000 in each 16.16 word, 0x63EE50)
	// [BIN:TPI-EXE:0x626390 member start] the meters 100.0, +0x23C = 0, the patrol rectangle (0, 0, 0x7F, 0x7F) at +0x24C, 0x60B3F0(0), +0xD4 = lerp(exp, 0, MaxExperience, 100, 100 + MaxSpeedDifference)
	// (the word 0x9CFB2C and 0xA022A8), the day counters 0
	// [BIN:TPI-EXE:0x5D4980 handyman start] +0x5D4, +0x5D8, +0x5DA, +0x5DC = 0, template 3 on the sprite source (0x5D1000), the removal message 10 subscribed, then the search 0x5D52A0
	public void Construct( ParkEconomy economy, ThemeParkIncStaffMember member, (int X, int Y) cell, ThingPool? things )
	{
		ArgumentNullException.ThrowIfNull( economy );
		ArgumentNullException.ThrowIfNull( member );
		if ( !ThemeParkIncStaffMember.IsPlacedRole( member.Role ) )
			throw new InvalidOperationException( "Only Theme Park Inc handymen, mechanics, researchers and gardeners are ported to the park ([EXT:tpi-capability-gates])." );
		if ( economy.TpiStaff is not { } staff )
			throw new InvalidOperationException( "The park has no Theme Park Inc staff." );
		var settings = staff.Settings;
		member.ThingId = things?.Allocate( ThingModelOf( member.Role ) ) ?? NextOwnThingId( staff );
		member.BaseSpeed = GuestSpeed.BaseSpeeds[(int)(economy.World.NextWord() % 5)];
		member.SpeedBonus = 0;
		member.Boost = 0;
		member.SmoothedSpeed = 0;
		var navigation = new GuestNavigation { X = (cell.X << 16) + 0x8000, Y = (cell.Y << 16) + 0x8000 };
		navigation.SetDestination( navigation.X, navigation.Y, [] );
		member.Navigation = navigation;
		member.Happiness = member.Motivation = member.Energy = 100f;
		member.IdleStamp = 0;
		member.PatrolArea = (0, 0, 0x7F, 0x7F);
		member.SpeedBonus = BonusTable[0];
		member.BaseSpeed = ThemeParkIncStaffSettings.Lerp( member.Experience, 0, settings.MaxExperience, 100, 100 + settings.MaxSpeedDifference ) & 0xFFFF;
		member.State = Idle;
		member.WorkStamp = 0;
		member.LitterCell = 0;
		member.RideId = 0;
		member.JobTimer = 0;
		// [BIN:TPI-EXE:0x5D22A3 gardener start] +0x5D0 = 0 and the gardener list's head (0x64F510), +0x5D8 and +0x5D4 = 0, template 3 on the sprite source (0x5D1000 with the category 0x12), the removal
		// message 10 subscribed (0x436AC0), then the search 0x5D28C0 with no setter before it; the list (world +0x1EAB7E through +0x5D0, newest first) is the members' gardeners in their order
		member.PlantId = 0;
		member.Template = IdleTemplate;
		member.DrawX = navigation.X / (float)Fixed16.One;
		member.DrawY = navigation.Y / (float)Fixed16.One;
		member.TurnTick = -1;
		member.Sprite.Reset();
		member.Sprite.Create( SpriteTypeOf( member ), VariantOf( member, SpriteBanks ), ProgramOf( IdleTemplate ), SpriteClockMilliseconds, SpriteBanks );
		FollowChain( member );
		// [BIN:TPI-EXE:0x5D9BC8 mechanic start] +0x5D8 and +0x5D4 = 0, then the setter 0x5DA150(0) (0x628180(0): the idle stamp 0, as the state is 0, the bonus 0 and template 3) before the search
		if ( member.Role == ThemeParkIncStaffRole.Mechanic )
			SetState( economy, member, Idle, economy.Turn );
		if ( member.Role == ThemeParkIncStaffRole.Researcher )
			StartResearcher( economy, member, economy.Turn );
		else
			Search( economy, member, economy.Turn );
		FollowChain( member );
	}

	/// <summary>A thing id for a member of a world without a thing pool (tests): above every member's.</summary>
	private static int NextOwnThingId( ThemeParkIncStaff staff ) => staff.Members.Select( member => member.ThingId ).DefaultIfEmpty( 0 ).Max() + 1;

	/// <summary>Files every placed member's thing on its cell's chain with the entry number it holds (a save's), before a turn's updates.</summary>
	public void SyncChain( ParkEconomy economy )
	{
		if ( economy.TpiStaff is not { } staff || Guests is not { } guests )
			return;
		foreach ( var member in staff.Members )
		{
			if ( member.Navigation == null )
				continue;
			guests.NoteCellSerial( member.CellSerial );
			FollowChain( member );
		}
	}

	/// <summary>
	/// Files the member's thing on its cell's chain: it enters at the head when it first stands in the park or moves to another cell. While the chain holds it the member keeps no number
	/// of its own (the chain's is the authority, <see cref="CaptureChain"/>), so the economy's state does not carry the chains' absolute counter.
	/// </summary>
	private void FollowChain( ThemeParkIncStaffMember member )
	{
		if ( Guests is not { } guests || member.Navigation == null )
			return;
		guests.StaffChain.Follow( member.ThingId, member.Navigation, member.CellSerial );
		if ( guests.StaffChain.Of( member.ThingId ) != null )
			member.CellSerial = 0;
	}

	/// <summary>The placed members' chain entries for a park save, as [thing id, entry number] pairs in member order; null when none is filed.</summary>
	public long[][]? CaptureChain( ParkEconomy economy )
	{
		if ( Guests is not { } guests || economy.TpiStaff is not { } staff )
			return null;
		var pairs = staff.Members.Where( member => member.Navigation != null ).Select( member => guests.StaffChain.Of( member.ThingId ) is { } entry ? new[] { (long)member.ThingId, entry.Serial } : null )
			.OfType<long[]>().ToArray();
		return pairs.Length == 0 ? null : pairs;
	}

	/// <summary>Files the restored members' things on their cells' chains with their saved entry numbers (a park save's <see cref="CaptureChain"/>), oldest entry first.</summary>
	public void RestoreChain( ParkEconomy economy, long[][] pairs )
	{
		if ( Guests is not { } guests || economy.TpiStaff is not { } staff )
			return;
		var serials = pairs.ToDictionary( pair => (int)pair[0], pair => pair[1] );
		foreach ( var member in staff.Members.Where( member => member.Navigation != null && serials.ContainsKey( member.ThingId ) ).OrderBy( member => serials[member.ThingId] ) )
		{
			guests.StaffChain.Remove( member.ThingId );
			guests.NoteCellSerial( serials[member.ThingId] );
			member.CellSerial = serials[member.ThingId];
			FollowChain( member );
		}
	}

	/// <summary>
	/// One thing update of a placed member (the kind table 0x637860): the handyman's pre-update, then its state handler.
	/// </summary>
	// [BIN:TPI-EXE:0x63762D handyman update] kind 5: the pre-update 0x5D5000, then the state dispatch 0x5D5710
	// [BIN:TPI-EXE:0x63761C mechanic update] kind 4: the pre-update 0x5DA3B0 (a jump to the common 0x628050), then the state dispatch 0x5DA280
	public void Update( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		if ( member.Navigation == null || !ThemeParkIncStaffMember.IsPlacedRole( member.Role ) || economy.TpiStaff is not { } staff )
			return;
		member.LastUpdateTurn = turn;
		(member.TurnFromX, member.TurnFromY, member.TurnTick) = (member.Navigation.X, member.Navigation.Y, Guests?.TickCount ?? -1);
		if ( member.Role == ThemeParkIncStaffRole.Researcher )
		{
			// [BIN:TPI-EXE:0x637690 researcher update] kind 8: the pre-update 0x61B600 (the common 0x628050), then the dispatch 0x61B620
			CommonPreUpdate( member );
			DispatchResearcher( economy, staff.Settings, member, turn );
		}
		else
		{
			PreUpdate( economy, staff.Settings, member, turn );
			Dispatch( economy, staff.Settings, member, turn );
		}
		if ( Guests == null )
		{
			var navigation = member.Navigation!;
			member.DrawX = navigation.X / (float)Fixed16.One;
			member.DrawY = navigation.Y / (float)Fixed16.One;
		}
		FollowChain( member );
	}

	/// <summary>The display category of a state (0x62B010): 0 idle, 1 looking for work, 2 working, 3 resting, 4 on strike, 5 picked up, 6 held, 7 researching, 8 training.</summary>
	// [BIN:TPI-EXE:0x62B010 display state] the byte table 0x62B09C by state and the targets 0x62B078: 0 -> 0, 1 -> 1, 2 and 3 -> 3, 4 and 5 -> 4, 7, 8, 0x25 and 0x26 -> 8, 0xA -> 5, 0xB and 0xC -> 6,
	// 0x18, 0x19, 0x21 and 0x22 -> 7 (a researcher not listed for a room counts 0), every other state 2
	public static int Category( int state ) => state switch
	{
		0 => 0,
		1 => 1,
		2 or 3 => 3,
		4 or 5 => 4,
		7 or 8 or 0x25 or 0x26 => 8,
		0x0A => 5,
		0x0B or 0x0C => 6,
		0x18 or 0x19 or 0x21 or 0x22 => 7,
		_ => 2
	};

	/// <summary>The common pre-update of every member (<c>0x628050</c>): the speed smoothing, the sprite's pending requests and one tick of the display state's day counter.</summary>
	// [BIN:TPI-EXE:0x628050 staff pre-update] 0x60BC90 (the speed words' smoothing and the walk's caps), the thought check on the member's phase, then one tick of the display state's day counter
	private void CommonPreUpdate( ThemeParkIncStaffMember member )
	{
		// [EXT:tpi-twp-guest-rules] 0x60BC90's smoothing of (+0xD4 + +0xD6 + +0xD8) / 100 into +0xDC and the decay of +0xD8 by 99/100 are taken as Theme Park World's 0x004FA870, the caps
		// 0x63EF90 as 0x00510190
		(member.SmoothedSpeed, member.Boost, member.MaxSpeed, member.MaxForce) = GuestSpeed.Update( member.SmoothedSpeed, member.BaseSpeed, member.SpeedBonus, member.Boost );
		// [BIN:TPI-EXE:0x5D1080 sprite requests] 0x60BC90 then applies the wrapper's pending template (+0xC, through the table 0x9CDA94, 0x4E49A0) and interval (+0x10, 0x4E42B0), clearing each, as tp.exe's
		// 0x004D4190; a member restored from a save (whose sprite is not saved) gets a source in his state's template first; a member inside a room (training, researching) has no source (0x5D1050 at the
		// state's entry) until he leaves it
		if ( member.Sprite.Source == null && !Hidden( member ) )
			member.Sprite.Create( SpriteTypeOf( member ), VariantOf( member, SpriteBanks ), ProgramOf( member.Template ), SpriteClockMilliseconds, SpriteBanks );
		member.Sprite.ApplyPending( SpriteTypeOf( member ), VariantOf( member, SpriteBanks ), SpriteBanks );
		member.CountTick( (ThemeParkIncStaffMember.DisplayState)DisplayCategory( member ) );
	}

	/// <summary>The handyman's pre-update: the common part (speed, day counter), then, while idle, a litter scan every search period; the mechanic's is the common part alone.</summary>
	// [BIN:TPI-EXE:0x5D5000 handyman pre-update] 0x628050; p = trunc((1.0 - exp / MaxExperience) * 50.0f) (fildll of the unsigned experience, fidivl, fsubr 0x93B858, fmuls 0x93B89C, ftol);
	// a period of 0 or turn % p == 0 (unsigned) with the state 0: the radius lerp(exp, 0, MaxExperience, Handyman.MinDetectionRange, MaxDetectionRange), the litter finder 0x5B1B50 around the
	// member's cell, and a find runs the search 0x5D52A0; then 0x6388F0 on +0x44 (the thought's lapse, presentation)
	private void PreUpdate( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		CommonPreUpdate( member );
		// [BIN:TPI-EXE:0x63763E gardener update] kind 0x14: the pre-update 0x5D2820, then the state dispatch 0x5D2A60
		if ( member.Role == ThemeParkIncStaffRole.Gardener )
		{
			PreUpdateGardener( economy, settings, member, turn );
			return;
		}
		// the mechanic's pre-update is the common part alone (0x5DA3B0 jumps to 0x628050)
		if ( member.Role != ThemeParkIncStaffRole.Handyman )
			return;
		var period = SearchPeriod( member.Experience, settings.MaxExperience );
		var due = period == 0 || unchecked( (uint)turn ) % (uint)period == 0;
		if ( member.State == Idle && due && FindLitter( economy, settings, member ) != 0 )
			Search( economy, member, turn );
	}

	/// <summary>Whether the member is inside a room without a sprite source: training (state 8 and its suspended 0x25) or a researcher researching (state 24).</summary>
	internal static bool Hidden( ThemeParkIncStaffMember member ) => member.State is ThemeParkIncTraining.Training or ThemeParkIncTraining.TrainingSuspended
		|| member.Role == ThemeParkIncStaffRole.Researcher && member.State == ThemeParkIncResearch.Researching;

	/// <summary>The handyman's search period and idle wait: trunc((1 − experience / MaxExperience) × 50).</summary>
	public static int SearchPeriod( int experience, int maxExperience ) => unchecked( (int)(long)((1.0 - (double)(uint)experience / maxExperience) * 50.0f) );

	/// <summary>The idle wait's term: trunc((1 − experience / MaxExperience) × −50), at most 0.</summary>
	// [BIN:TPI-EXE:0x62CA10 idle end] fmuls 0x93E878 (-50.0); the stamp less this term is the end of the wait
	public static int IdleTerm( int experience, int maxExperience ) => unchecked( (int)(long)((1.0 - (double)(uint)experience / maxExperience) * -50.0f) );

	/// <summary>The cell word of a cell (y × 128 + x + 1).</summary>
	private static int CellWord( (int X, int Y) cell ) => cell.Y * 128 + cell.X + 1;

	private static (int X, int Y) CellOf( int word ) => ((word - 1) & 0x7F, (word - 1) >> 7);

	/// <summary>The litter finder around the member's cell within its detection range; the cell word, 0 for none.</summary>
	// [BIN:TPI-EXE:0x5B1B50 litter finder] the box of the radius around the centre clipped to 0..0x7F, x outer and y inner, the records whose type (+0x30) is nonzero and whose claim word (+0x34) is
	// the member's thing id (0x6375C0 returns the thing's word +0) or 0, the first nearest by |dx| + |dy| (9999 to start, unsigned jae keeps the first on a tie), as Theme Park World's 0x004C8ED0
	private static int FindLitter( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member )
	{
		var range = settings.Roles[ThemeParkIncStaffRole.Handyman];
		var radius = ThemeParkIncStaffSettings.Lerp( member.Experience, 0, settings.MaxExperience, range.MinRange, range.MaxRange );
		return StaffWorldSimulation.FindLitter( economy.Litter, member.Navigation!.Cell, member.ThingId, radius ) is { } cell ? CellWord( cell ) : 0;
	}

	/// <summary>The handyman's state handler.</summary>
	// [BIN:TPI-EXE:0x5D5710 handyman dispatch] states 0..0x14 by the byte table 0x5D58C4 and the jump table 0x5D5898; above 0x14 the common handler 0x6287A0
	private void Dispatch( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		if ( member.Role == ThemeParkIncStaffRole.Mechanic )
		{
			DispatchMechanic( economy, settings, member, turn );
			return;
		}
		if ( member.Role == ThemeParkIncStaffRole.Gardener )
		{
			DispatchGardener( economy, settings, member, turn );
			return;
		}
		switch ( member.State )
		{
			case Idle:
				// [BIN:TPI-EXE:0x5D5863 state 0] a world draw; its low nibble 0 plays the idle voice 0xA1 (0x60BE90); then the idle end 0x62CA10
				Voice( economy, member, 0xA1 );
				// [BIN:TPI-EXE:0x62CA10 idle end] the turn above +0x23C less the idle term (unsigned) looks for work (0x62D810)
				if ( unchecked( (uint)turn ) > unchecked( (uint)(member.IdleStamp - IdleTerm( member.Experience, settings.MaxExperience )) ) )
					LookForWork( economy, settings, member, turn );
				break;
			case Walking:
				// [BIN:TPI-EXE:0x5D5731 state 1] a world draw for the walking voice 0xA0; the walk step: arrived (0) or stuck (2) searches, still walking takes the moving step 0x62B3E0
				Voice( economy, member, 0xA0 );
				if ( Walk( member ) is WalkResult.Arrived or WalkResult.Stuck )
					Search( economy, member, turn );
				else
					member.Step( settings );
				break;
			case ToLitter:
				UpdateToLitter( economy, member, turn );
				break;
			case Sweeping:
				UpdateSweeping( economy, settings, member, turn );
				break;
			// [BIN:TPI-EXE:0x5D588E common states] the handyman's table sends 4..0x0E to the common handler 0x6287A0 (byte table 0x628B10, jump table 0x628ADC): 7 -> 0x62A7C0, 8 -> 0x62AA50; above 0x14
			// (0x25, 0x26) the common handler has no case
			case ThemeParkIncTraining.ToTrainingRoom:
				UpdateToTrainingRoom( economy, settings, member, turn );
				break;
			case ThemeParkIncTraining.Training:
				UpdateTraining( economy, settings, member, turn );
				break;
		}
	}

	/// <summary>The walking and idle voice: a world draw whose low nibble is 0 plays the handyman's voice (the cat_staff event) at his position.</summary>
	// [BIN:TPI-EXE:0x60BE90 voice] no draw of its own; the caller's world draw and its 1-in-16 gate (test $0xF)
	private void Voice( ParkEconomy economy, ThemeParkIncStaffMember member, int eventId )
	{
		if ( (economy.World.NextWord() & 0xF) != 0 )
			return;
		Voices.Add( (eventId, member.DrawX, member.DrawY) );
		if ( Voices.Count > 64 )
			Voices.RemoveRange( 0, Voices.Count - 64 );
	}

	/// <summary>State 0x0F: the claim must still be his; arrived on litter, he sweeps; arrived where it was taken, blocked or claimed away, he searches again.</summary>
	// [BIN:TPI-EXE:0x5D59D0 to litter] the target record's claim word (+0x34) against the member's thing id first ("didn't get to litter in time, someone else cleaning": +0x5D8 = 0, the search);
	// then the walk step: 0 with the record's type (+0x30) nonzero puts him on the walk's point (0x60BD80 with +0x28, +0x2A) and sets 0x10 (0x5D5630), 0 without litter or 2 ("my route to the litter
	// was blocked") clears +0x5D8 and searches; the thought request 0x1D (0x638960) follows every branch but the first (presentation)
	private void UpdateToLitter( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		var target = CellOf( member.LitterCell );
		if ( economy.Litter.CollectorAt( target.X, target.Y ) != member.ThingId )
		{
			member.LitterCell = 0;
			Search( economy, member, turn );
			return;
		}
		switch ( Walk( member ) )
		{
			case WalkResult.Walking:
				return;
			case WalkResult.Arrived when economy.Litter.TypeAt( target.X, target.Y ) != LitterType.None:
				var navigation = member.Navigation!;
				(navigation.X, navigation.Y) = (navigation.TargetX, navigation.TargetY);
				SetState( economy, member, Sweeping, turn );
				return;
		}
		member.LitterCell = 0;
		Search( economy, member, turn );
	}

	/// <summary>State 0x10: the working step; once the work duration has passed, the litter of his target goes when his own cell holds litter, and he searches again.</summary>
	// [BIN:TPI-EXE:0x5D5AE0 sweep] the working step 0x62B4D0, then the work duration 0x62E4C0; a turn at most +0x5D4 + duration (unsigned) goes on sweeping; else when the record of the member's own
	// cell holds litter (+0x30) the target's record is removed (0x5D90D0, "cleaning litter in cell"), then +0x5D8 = 0 and the search
	private void UpdateSweeping( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		member.Step( settings );
		var duration = settings.WorkDuration( ThemeParkIncStaffRole.Handyman, member.Experience, member.Motivation, member.Salary );
		if ( unchecked( (uint)turn ) <= unchecked( (uint)(member.WorkStamp + duration) ) )
			return;
		var cell = member.Navigation!.Cell;
		if ( economy.Litter.TypeAt( cell.X, cell.Y ) != LitterType.None && member.LitterCell != 0 )
		{
			// [BIN:TPI-EXE:0x5D90D0 litter removal] the sprite, the RegionFX of vomit and a stinkbomb, its particle, the type and the claim go, as Theme Park World's 0x004D9620
			var target = CellOf( member.LitterCell );
			economy.Litter.Remove( target.X, target.Y );
		}
		member.LitterCell = 0;
		Search( economy, member, turn );
	}

	/// <summary>
	/// The handyman's search (0x5D52A0): the strike and rest gate, then, unless too tired, the nearest litter in reach, else the loo and the bin (none: their wear is unported), else a
	/// patrol, else idle.
	/// </summary>
	// [BIN:TPI-EXE:0x5D52A0 handyman search] 0x62BBA0 nonzero returns; tired (0x62B3B0, "Too tired to tidy stuff up") goes to the tail; the radius lerp and the finder 0x5B1B50 store +0x5D8; a find
	// inside the patrol rectangle (0x62C610) reseeds the world generator with the cell word and draws twice (& 0x7F, clamped 1..9, times 13: the point in the cell), and a route there (0x62EB10 == 1)
	// claims the record (0x5D8FE0) and sets 0x0F; no route ("Can't set destination to that litter cell!") goes to the tail (+0x5D8 kept); without litter a world draw R (bl = R & 1) picks between the
	// loo 0x5D6230 and the bin 0x5D5F10 when both are found; the tail: the patrol destination 0x609F70 sets 1, none sets 0
	private void SearchHandyman( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		var settings = economy.TpiStaff!.Settings;
		if ( Gate( economy, settings, member, turn ) )
			return;
		if ( !member.Tired( settings ) )
		{
			var found = FindLitter( economy, settings, member );
			member.LitterCell = found;
			if ( found != 0 )
			{
				var cell = CellOf( found );
				if ( InPatrolArea( member, found ) )
				{
					var (bx, by) = ParkLitter.SweepSpot( economy.World, cell.X, cell.Y );
					if ( StaffRoute( economy, member, cell, bx, by ) )
					{
						// [BIN:TPI-EXE:0x5D8FE0 litter claim] +0x34 = the member's thing id, +0x38 = the world turn, as Theme Park World's 0x004D9600
						economy.Litter.Claim( cell.X, cell.Y, member.ThingId, turn );
						SetState( economy, member, ToLitter, turn );
						return;
					}
				}
			}
			else
			{
				// the draw R decides between a dirty loo and a dirty or burning bin when both are found; [EXT:tpi-no-maintenance-wear] neither is ever dirty here
				_ = economy.World.NextWord();
			}
		}
		SetState( economy, member, Patrol( economy, member ) ? Walking : Idle, turn );
	}

	/// <summary>The role's search: the handyman's 0x5D52A0, the mechanic's 0x5DA3D0 or the gardener's 0x5D28C0.</summary>
	internal void Search( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		if ( member.Role == ThemeParkIncStaffRole.Mechanic )
			SearchMechanic( economy, member, turn );
		else if ( member.Role == ThemeParkIncStaffRole.Gardener )
			SearchGardener( economy, member, turn );
		else
			SearchHandyman( economy, member, turn );
	}

	/// <summary>The patrol rectangle test (0x62C610): the cell word's cell inside the member's rectangle, its corners ordered first.</summary>
	// [BIN:TPI-EXE:0x62C610 patrol rectangle] x = (w - 1) & 0x7F, y = (w - 1) >> 7 against +0x24C..+0x258 (each pair swapped when reversed), inclusive, signed
	private static bool InPatrolArea( ThemeParkIncStaffMember member, int word )
	{
		var (x, y) = CellOf( word );
		var (x0, y0, x1, y1) = member.PatrolArea;
		if ( x0 > x1 )
			(x0, x1) = (x1, x0);
		if ( y0 > y1 )
			(y0, y1) = (y1, y0);
		return x >= x0 && x <= x1 && y >= y0 && y <= y1;
	}

	/// <summary>
	/// The strike and rest gate (0x62BBA0) before every search: true when it set the member's state. A comfortable member's base speed follows his energy and his mood may show; a tired
	/// one looks for a rest area (none: no Theme Park Inc object is one), may lose happiness and, while working, stops.
	/// </summary>
	// [BIN:TPI-EXE:0x62BA20 rest-area finder] walks the thing list (W+0x1EAB88, next +0x14) for a thing with flag +0x42 bit 2 and +0x7C nonzero, the nearest by squared cell distance, the
	// first on a tie; the only writer of that bit is the thing builder from the type record's +0x1B0 (UsageInfo.ChillsYouOut, 0x5DB6E2), and no Science, Water or Arabian object sets the key
	// (ThemeParkIncHandymanTests.NoThemeParkIncObjectIsARestArea), so the finder answers none in every Theme Park Inc park and is exact as "none"; the walk to a rest area is unreachable
	// [BIN:TPI-EXE:0x62BBA0 strike gate] the strike branch needs the role's strike flag (HQ +0x160 + 12 × code, 0x634210) and 0x64FA50 == 1: the role strikes only through the month's strike
	// consideration, which a park does not run yet (strikes in the world are a later package), so no record is active and the branch is not taken
	// [BIN:TPI-EXE:0x62BC88 rest branch] energy byte (ftol & 0xFF) at most RestLevel: the thought 0x18, the rest-area finder 0x62BA20 ("Staff member couldn't find a rest area"), then a world draw whose low nibble 0 adds -HappyHitCosNoRestArea to happiness (0x60D0D0, clamped), then a working member (display 2) is set
	// to 0 and the gate answers 1
	// [BIN:TPI-EXE:0x62BD8D comfortable branch] energy byte above RestLevel: +0xD4 = lerp(exp, 0, MaxExperience, w, MaxSpeedDifference + w) with w = 140 at 100, else the word at 0x9CFB28 by the
	// tens; happiness byte at most 10 asks for the thought 0x17, above 97 a world draw whose low nibble 0 asks for 0x16 (presentation)
	// [BIN:TPI-EXE:0x62BE2B gate end] +0x27E is 0 for every member but a researcher listed for a room (only 0x62E240 writes it; the constructors clear it), so the gate answers 0
	private bool Gate( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		var energy = (int)(long)member.Energy & 0xFF;
		if ( energy <= settings.RestLevel )
		{
			if ( (economy.World.NextWord() & 0xF) == 0 )
				member.AddHappiness( -(float)settings.HappyHitNoRestArea );
			if ( DisplayCategory( member ) == 2 )
			{
				SetState( economy, member, Idle, turn );
				return true;
			}
			return false;
		}
		var w = energy >= 100 ? 140 : EnergySpeeds[energy / 10];
		member.BaseSpeed = ThemeParkIncStaffSettings.Lerp( member.Experience, 0, settings.MaxExperience, w, settings.MaxSpeedDifference + w ) & 0xFFFF;
		var happiness = (int)(long)member.Happiness & 0xFF;
		// at most 10 only asks for a thought (presentation); above 97 the draw happens
		if ( happiness > 0x61 )
			_ = economy.World.NextWord();
		return false;
	}

	/// <summary>Looking for work (0x62D810): the role's search, then a member with low motivation who is now working stops again.</summary>
	// [BIN:TPI-EXE:0x62D810 look for work] kind 5 runs 0x5D52A0, kind 4 0x5DA3D0, kind 0x14 0x5D28C0 (the byte table 0x62D974's index 16: 0x62D844); then a motivation byte below MotivationPercentToStrike / 2 (cltd, sub, sar: toward zero) with the display state 2 or 7 looks for a
	// rest area (0x62BA20: none) and sets 0 (0x628180(0) at 0x62D94D)
	private void LookForWork( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		// [BIN:TPI-EXE:0x62D974 look-for-work kinds] kind 5 0x5D52A0, kind 8 0x61B820
		if ( member.Role == ThemeParkIncStaffRole.Researcher )
			SearchResearchRoom( economy, settings, member, turn );
		else
			Search( economy, member, turn );
		if ( ((int)(long)member.Motivation & 0xFF) < settings.MotivationPercentToStrike / 2 && DisplayCategory( member ) is 2 or 7 )
			SetState( economy, member, Idle, turn );
	}

	/// <summary>The handyman's state setter (0x5D5630) and the common one it falls back to (0x628180).</summary>
	// [BIN:TPI-EXE:0x5D5630 handyman state setter] 0x0F, 0x11, 0x13: 0x60B3F0(1) and template 0xF; 0x10, 0x12, 0x14: +0x5D4 = the turn and template 0x19; the others through 0x628180
	// [BIN:TPI-EXE:0x628180 staff state setter] 0 (and 0x23): +0x23C = the turn after state 1, else 0, then 0x60B3F0(0) and template 3 (0x60B6E0); 1: the slack test 0x62D650 (energy byte below
	// StaffSlackingEnergy looks for a rest area, 0x62BA20: none, so it answers false and no draw is taken), then template 0xF; the state is stored last (0x6285CD)
	internal void SetState( ParkEconomy economy, ThemeParkIncStaffMember member, int state, long turn )
	{
		if ( member.Role == ThemeParkIncStaffRole.Mechanic && state is ToRide or Repairing )
		{
			SetMechanicState( economy, member, state );
			return;
		}
		if ( member.Role == ThemeParkIncStaffRole.Gardener && state is ToPlant or Watering )
		{
			SetGardenerState( member, state, turn );
			return;
		}
		switch ( state )
		{
			case 0x0F or 0x11 or 0x13 when member.Role == ThemeParkIncStaffRole.Handyman:
				member.SpeedBonus = BonusTable[1];
				RequestTemplate( member, WalkTemplate );
				break;
			case 0x10 or 0x12 or 0x14 when member.Role == ThemeParkIncStaffRole.Handyman:
				member.WorkStamp = turn;
				RequestTemplate( member, WorkTemplate );
				break;
			case 0 or 0x23:
				member.IdleStamp = member.State == Walking ? turn : 0;
				member.SpeedBonus = BonusTable[0];
				RequestTemplate( member, IdleTemplate );
				break;
			case Walking:
				RequestTemplate( member, WalkTemplate );
				break;
			case ThemeParkIncTraining.ToTrainingRoom:
				EnterToTrainingRoom( economy, member );
				break;
			case ThemeParkIncTraining.Training:
				EnterTraining( economy, member, turn );
				break;
			case ThemeParkIncResearch.Researching:
				EnterResearching( member, turn );
				break;
			case ToResearchRoom:
				EnterToResearchRoom( member );
				break;
			default:
				throw new InvalidOperationException( $"Theme Park Inc staff state {state} is not ported." );
		}
		member.State = state;
	}

	/// <summary>
	/// The common setter's state 0 (0x628180(0)) for a member outside a world update (a removal message): the idle stamp, the speed bonus, template 3 (queued on his source, or made in it at his
	/// next pre-update when he has none) and the state.
	/// </summary>
	private static void EnterIdle( ThemeParkIncStaffMember member, long turn )
	{
		member.IdleStamp = member.State == Walking ? turn : 0;
		member.SpeedBonus = BonusTable[0];
		member.Template = IdleTemplate;
		if ( member.Sprite.Source != null )
			member.Sprite.QueueTemplate( ProgramOf( IdleTemplate ) );
		member.State = Idle;
	}

	private enum WalkResult { Arrived, Walking, Stuck }

	/// <summary>One walk step: arrived, walking or stuck (Theme Park World's walk, see the class remarks).</summary>
	private WalkResult Walk( ThemeParkIncStaffMember member )
	{
		var navigation = member.Navigation!;
		if ( !navigation.Original )
			return WalkResult.Arrived;
		var (beforeX, beforeY) = (navigation.X, navigation.Y);
		var result = navigation.Step( member.MaxSpeed, member.MaxForce, grid.Cells, static ( _, _ ) => false, Separation ) switch
		{
			OpenTPW.WalkResult.Walking => WalkResult.Walking,
			OpenTPW.WalkResult.Stuck => WalkResult.Stuck,
			_ => WalkResult.Arrived
		};
		// the heading (+0x2C) follows the step's direction ([EXT:tpi-twp-guest-rules]: Theme Park World's walk; Theme Park Inc's arrival also writes it, D1, from the same differences)
		var (dx, dy) = ((long)navigation.X - beforeX, (long)navigation.Y - beforeY);
		if ( dx != 0 || dy != 0 )
		{
			var length = Math.Sqrt( dx * (double)dx + dy * (double)dy );
			(member.HeadingX, member.HeadingY) = ((float)(dx / length), (float)(dy / length));
		}
		// [BIN:TPI-EXE:0x60B541 walk request] a step that is still walking asks the sprite for the walk (template 1, 0x9AFBA8) unless its restart PC is there already (0x4E4AB0, then 0x5D0F90(1)): the run
		// (2, 0x9AFC04) is for a guest without +0x2A4 or a guard with a speed bonus, never a handyman; then the interval clamp(trunc(62 / v), 0, 250) (0x5D10F0) of the step's length times 0x93E5B4,
		// doubled without the run, as tp.exe's 0x004FA3F8
		if ( result == WalkResult.Walking )
		{
			if ( !member.Sprite.RestartsAt( StaffSpriteEvents.Walk ) )
			{
				member.Template = 1;
				member.Sprite.QueueTemplate( StaffSpriteEvents.Walk );
			}
			member.Sprite.QueueInterval( GuestSimulation.BodyIntervalForStep( beforeX, beforeY, navigation.X, navigation.Y, run: false ) );
		}
		return result;
	}

	private readonly List<GuestNavigation> neighbours = new();

	/// <summary>Separation's neighbours: the things on the 3 × 3 cells around the member's cell, as Theme Park World's staff query (0x005102F0).</summary>
	private (int X, int Y) Separation( GuestNavigation self )
	{
		neighbours.Clear();
		var (cellX, cellY) = self.Cell;
		for ( var y = cellY - 1; y <= cellY + 1; y++ )
			for ( var x = cellX - 1; x <= cellX + 1; x++ )
				if ( grid.InBounds( x, y ) )
					Guests?.AppendCellChain( x, y, neighbours );
		return GuestNavigation.Separation( self, neighbours );
	}

	/// <summary>
	/// The staff route (<c>0x62EB10</c>): a closed park refuses a walk to the entrance cells (47, 17) and (48, 17); else the walk mask is 1 when the member's cell or the target cell is empty (type 0),
	/// 0 otherwise, and the walk is set to the point (<c>0x60B8E0</c>). False when no route reaches it.
	/// </summary>
	// [BIN:TPI-EXE:0x62EB10 staff route] park+0x1EAB44 (0x64FA30, nonzero: the park is closed) with the target word's cell x (its high byte) 0x2F or 0x30 and the y word's cell 0x11 answers 0 and leaves the
	// walk; the member's cell record (+0x418 + 72 × (y << 7 + x + 1), from the bytes +5 and +7) of type 0 (0x67F6D0, its dword +8) or else the target's sets +0x1A0 = 1, else +0x1A0 = 0 (the walk's +0xB4,
	// the mask the search and the edge test read); then 0x60B8E0 with the two words
	// [BIN:TPI-EXE:0x60B8E0 staff steer] the stamp counter raised (0x5D81F0, ParkCellMap.StampCounter: OpenTPW counts the edits only); the stranded mark +0x1B0 is tested (0x60BB50) only while nonzero,
	// and a staff member's never is: its only setter with a stamp, 0x60A4AE in the wander, is reached by kind 1 (the guests) alone, the staff kinds 4, 5, 6, 8, 7 and 0x14 branching to 0x62C720 at
	// 0x60A454, and every other writer stores 0 (0x608F94, 0x60A56C, 0x60A84C, 0x60B4CA, 0x60B9FC); then +0x28, +0x2A = the words, +0x1B0 = 0 and SetDest 0x63EE80 (0x63E3A0 with the point << 8)
	private bool StaffRoute( ParkEconomy economy, ThemeParkIncStaffMember member, (int X, int Y) cell, int byteX, int byteY )
	{
		if ( !economy.IsParkOpen && cell.Y == 0x11 && cell.X is 0x2F or 0x30 )
			return false;
		var (x, y) = member.Navigation!.Cell;
		member.Navigation.Mask = grid.Cells.RawTypeAt( x, y ) == 0 || grid.Cells.RawTypeAt( cell.X, cell.Y ) == 0 ? 1 : 0;
		return Route( member, cell, byteX, byteY );
	}

	/// <summary>Sets the walk to a point of a cell with the walk's mask (SetDest, <c>0x63EE80</c>); false when no route reaches it (the walk then stands arrived and stuck).</summary>
	private bool Route( ThemeParkIncStaffMember member, (int X, int Y) cell, int byteX, int byteY )
	{
		var (x, y) = GuestNavigation.BytePoint( cell, byteX, byteY );
		return member.Navigation!.Navigate( grid.Cells, x, y );
	}

	/// <summary>The patrol destination: a wander inside the patrol rectangle, or the off-path search from a cell without links; true when a walk is set.</summary>
	private bool Patrol( ParkEconomy economy, ThemeParkIncStaffMember member )
	{
		var cell = member.Navigation!.Cell;
		var area = member.PatrolArea;
		bool InArea( int x, int y ) => x >= area.X0 && x <= area.X1 && y >= area.Y0 && y <= area.Y1;
		var keepToArea = true;
		if ( !InArea( cell.X, cell.Y ) )
		{
			if ( EnterPatrolArea( economy, member ) )
				return true;
			keepToArea = false;
		}
		uint Draw() => economy.World.NextWord();
		if ( GuestWander.Pick( grid, cell, Draw, keepToArea ? InArea : null, out var stranded ) is { } destination )
			return Route( member, destination.Cell, destination.ByteX, destination.ByteY );
		if ( (grid.Cells.LinksAt( cell.X, cell.Y ) & 0x55) == 0 )
			return GuestWander.OffPath( grid, cell, Draw, target => Route( member, target, GuestWander.CentreByte, GuestWander.CentreByte ) );
		return stranded && EnterPatrolArea( economy, member );
	}

	/// <summary>A walk to a random path cell of the patrol rectangle: up to 30 tries of two world draws.</summary>
	private bool EnterPatrolArea( ParkEconomy economy, ThemeParkIncStaffMember member )
	{
		var area = member.PatrolArea;
		for ( var tries = 0; tries < 30; tries++ )
		{
			var x = (int)(economy.World.NextWord() % (uint)(area.X1 - area.X0 + 1)) + area.X0;
			var y = (int)(economy.World.NextWord() % (uint)(area.Y1 - area.Y0 + 1)) + area.Y0;
			// [BIN:TPI-EXE:0x62C84E patrol entry route] the staff route 0x62EB10 to the cell's centre
			if ( grid.IsWalkable( x, y ) && StaffRoute( economy, member, (x, y), GuestWander.CentreByte, GuestWander.CentreByte ) )
				return true;
		}
		return false;
	}
}
