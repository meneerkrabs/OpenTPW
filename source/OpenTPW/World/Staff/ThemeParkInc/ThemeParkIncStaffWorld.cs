namespace OpenTPW;

/// <summary>
/// Theme Park Inc's staff in the park (docs/reverse/TPI-STAFF.md §11): the placed members' things and their state machines, beside Theme Park World's
/// <see cref="StaffWorldSimulation"/>, which it does not touch. The handyman runs Theme Park Inc's own states 0 (idle), 1 (walk), 0x0F (to litter) and 0x10 (sweep) with its
/// meters, day counters and search; the other roles are refused at the hire (<c>[EXT:tpi-capability-gates]</c>).
/// </summary>
/// <remarks>
/// [EXT:tpi-twp-guest-rules] (user-approved 2026-11-21 for the staff's walk) the walk step 0x60B440, the route set-up 0x62EB10, the patrol destination 0x609F70 and the speed step 0x60BC90 are not
/// proven identical to Theme Park World's (WP0 movement: D1 the arrival heading, D2 the run flag, D5 the cell node stride; 0x609F70 is read as a call skeleton only), so a member walks
/// Theme Park World's walk (the guests' steering step with separation, the route search, the wander destination 0x004F9490 with its off-path search, the speed smoothing 0x004FA870 and the
/// caps 0x00510190), over Theme Park Inc's own speed words (+0xD4, +0xD6, +0xD8).
/// [EXT:tpi-no-maintenance-wear] (user-approved 2026-11-21) Theme Park Inc's toilet wear and the bins' fill are unported, so the loo finder 0x5D6230 and the bin finder 0x5D5F10 find nothing
/// to clean: the handymen sweep litter only (states 0x11 to 0x14 are not entered).
/// </remarks>
public sealed class ThemeParkIncStaffWorld
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

	/// <summary>The members' voices of the last updates: the handyman's walking (0xA0) and idle (0xA1) voice events at a member's position. Presentation, taken by the audio.</summary>
	public List<(int Event, float X, float Y)> Voices { get; } = new();

	/// <summary>Whether a staff member may be dropped on the cell (the drop's tests: owned, no fence post or track record, of type 0, 1, 3 or 9, and not under an object).</summary>
	// [BIN:TPI-EXE:0x4D4EB0 staff drop] the flag 0x40 (0x67ECA0), the fence post (0x67F920 -> 0x685710, kind 0x19), the track record (0x67F890 -> 0x6856D0, kinds 0xB, 0xD, 0x10, 0x12, 0x19) and
	// the type (1, 3 or 9, or 0) in that order, as tp.exe's; the M gate before them (a type-4 cell holding a kind-3 thing reads [0x5DF6D0(thing) + 0x600]) and the second gate 0x4D2180 are not
	// read: a type-4 cell is refused by the type test ([EXT:staff-drop-occupancy-refusal] the Theme Park Inc layout types no footprint cells, so the cells under an object refuse by occupancy)
	public static bool CanDrop( ParkCellMap cells, int x, int y, Func<int, int, bool>? occupied ) => StaffPlacementRules.IsValidDropCell( cells, x, y, occupied );

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
		if ( member.Role != ThemeParkIncStaffRole.Handyman )
			throw new InvalidOperationException( "Only Theme Park Inc handymen are ported to the park ([EXT:tpi-capability-gates])." );
		if ( economy.TpiStaff is not { } staff )
			throw new InvalidOperationException( "The park has no Theme Park Inc staff." );
		var settings = staff.Settings;
		member.ThingId = things?.Allocate( SaveThingModel.Handyman ) ?? NextOwnThingId( staff );
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
		member.Template = IdleTemplate;
		member.DrawX = navigation.X / (float)Fixed16.One;
		member.DrawY = navigation.Y / (float)Fixed16.One;
		FollowChain( member );
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

	/// <summary>The thing ids of the placed members (the cell chains keep their entries).</summary>
	public static IEnumerable<int> ChainIds( ParkEconomy? economy ) =>
		economy?.TpiStaff?.Members.Where( member => member.Navigation != null ).Select( member => member.ThingId ) ?? Enumerable.Empty<int>();

	private void FollowChain( ThemeParkIncStaffMember member )
	{
		if ( Guests is not { } guests || member.Navigation == null )
			return;
		guests.StaffChain.Follow( member.ThingId, member.Navigation, member.CellSerial );
		member.CellSerial = guests.StaffChain.Of( member.ThingId )?.Serial ?? member.CellSerial;
	}

	/// <summary>
	/// One thing update of a placed member (the kind table 0x637860): the handyman's pre-update, then its state handler.
	/// </summary>
	// [BIN:TPI-EXE:0x63762D handyman update] kind 5: the pre-update 0x5D5000, then the state dispatch 0x5D5710
	public void Update( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		if ( member.Navigation == null || member.Role != ThemeParkIncStaffRole.Handyman || economy.TpiStaff is not { } staff )
			return;
		PreUpdate( economy, staff.Settings, member, turn );
		Dispatch( economy, staff.Settings, member, turn );
		var navigation = member.Navigation!;
		member.DrawX = navigation.X / (float)Fixed16.One;
		member.DrawY = navigation.Y / (float)Fixed16.One;
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

	/// <summary>The handyman's pre-update: the common part (speed, day counter), then, while idle, a litter scan every search period.</summary>
	// [BIN:TPI-EXE:0x5D5000 handyman pre-update] 0x628050; p = trunc((1.0 - exp / MaxExperience) * 50.0f) (fildll of the unsigned experience, fidivl, fsubr 0x93B858, fmuls 0x93B89C, ftol);
	// a period of 0 or turn % p == 0 (unsigned) with the state 0: the radius lerp(exp, 0, MaxExperience, Handyman.MinDetectionRange, MaxDetectionRange), the litter finder 0x5B1B50 around the
	// member's cell, and a find runs the search 0x5D52A0; then 0x6388F0 on +0x44 (the thought's lapse, presentation)
	// [BIN:TPI-EXE:0x628050 staff pre-update] 0x60BC90 (the speed words' smoothing and the walk's caps), the thought check on the member's phase, then one tick of the display state's day counter
	private void PreUpdate( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		// [EXT:tpi-twp-guest-rules] 0x60BC90's smoothing of (+0xD4 + +0xD6 + +0xD8) / 100 into +0xDC and the decay of +0xD8 by 99/100 are taken as Theme Park World's 0x004FA870, the caps
		// 0x63EF90 as 0x00510190
		(member.SmoothedSpeed, member.Boost, member.MaxSpeed, member.MaxForce) = GuestSpeed.Update( member.SmoothedSpeed, member.BaseSpeed, member.SpeedBonus, member.Boost );
		member.CountTick( (ThemeParkIncStaffMember.DisplayState)Category( member.State ) );
		var period = SearchPeriod( member.Experience, settings.MaxExperience );
		var due = period == 0 || unchecked( (uint)turn ) % (uint)period == 0;
		if ( member.State == Idle && due && FindLitter( economy, settings, member ) != 0 )
			Search( economy, member, turn );
	}

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
	internal void Search( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
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
					if ( Route( member, cell, bx, by ) )
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
	/// one looks for a rest area (none: rest areas are not ported yet), may lose happiness and, while working, stops.
	/// </summary>
	// [BIN:TPI-EXE:0x62BBA0 strike gate] the strike branch needs the role's strike flag (HQ +0x160 + 12 × code, 0x634210) and 0x64FA50 == 1: the role strikes only through the month's strike
	// consideration, which a park does not run yet (strikes in the world are a later package), so no record is active and the branch is not taken
	// [BIN:TPI-EXE:0x62BC88 rest branch] energy byte (ftol & 0xFF) at most RestLevel: the thought 0x18, the rest-area finder 0x62BA20 ("Staff member couldn't find a rest area"; the finder and the walk to a
	// rest area are not read and not ported: it finds none), then a world draw whose low nibble 0 adds -HappyHitCosNoRestArea to happiness (0x60D0D0, clamped), then a working member (display 2) is set
	// to 0 and the gate answers 1
	// [BIN:TPI-EXE:0x62BD8D comfortable branch] energy byte above RestLevel: +0xD4 = lerp(exp, 0, MaxExperience, w, MaxSpeedDifference + w) with w = 140 at 100, else the word at 0x9CFB28 by the
	// tens; happiness byte at most 10 asks for the thought 0x17, above 97 a world draw whose low nibble 0 asks for 0x16 (presentation)
	// [BIN:TPI-EXE:0x62BE2B gate end] +0x27E is 0 for every member but a researcher listed for a room (only 0x62E240 writes it; the constructors clear it), so the gate answers 0
	private static bool Gate( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		var energy = (int)(long)member.Energy & 0xFF;
		if ( energy <= settings.RestLevel )
		{
			if ( (economy.World.NextWord() & 0xF) == 0 )
				member.AddHappiness( -(float)settings.HappyHitNoRestArea );
			if ( Category( member.State ) == 2 )
			{
				SetState( economy, member, Idle, turn );
				return true;
			}
			return false;
		}
		var w = energy >= 100 ? 140 : EnergySpeeds[energy / 10];
		member.BaseSpeed = ThemeParkIncStaffSettings.Lerp( member.Experience, 0, settings.MaxExperience, w, settings.MaxSpeedDifference + w ) & 0xFFFF;
		var happiness = (int)(long)member.Happiness & 0xFF;
		if ( happiness > 10 && happiness > 0x61 )
			_ = economy.World.NextWord();
		return false;
	}

	/// <summary>Looking for work (0x62D810): the role's search, then a member with low motivation who is now working stops again.</summary>
	// [BIN:TPI-EXE:0x62D810 look for work] kind 5 runs 0x5D52A0; then a motivation byte below MotivationPercentToStrike / 2 (cltd, sub, sar: toward zero) with the display state 2 or 7 looks for a
	// rest area (0x62BA20, not ported: none) and sets 0 (0x628180(0) at 0x62D94D)
	private void LookForWork( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		Search( economy, member, turn );
		if ( ((int)(long)member.Motivation & 0xFF) < settings.MotivationPercentToStrike / 2 && Category( member.State ) is 2 or 7 )
			SetState( economy, member, Idle, turn );
	}

	/// <summary>The handyman's state setter (0x5D5630) and the common one it falls back to (0x628180).</summary>
	// [BIN:TPI-EXE:0x5D5630 handyman state setter] 0x0F, 0x11, 0x13: 0x60B3F0(1) and template 0xF; 0x10, 0x12, 0x14: +0x5D4 = the turn and template 0x19; the others through 0x628180
	// [BIN:TPI-EXE:0x628180 staff state setter] 0 (and 0x23): +0x23C = the turn after state 1, else 0, then 0x60B3F0(0) and template 3 (0x60B6E0); 1: the slack test 0x62D650 (energy byte below
	// StaffSlackingEnergy looks for a rest area, 0x62BA20, not ported: none, so it answers false and no draw is taken), then template 0xF; the state is stored last (0x6285CD)
	internal static void SetState( ParkEconomy economy, ThemeParkIncStaffMember member, int state, long turn )
	{
		switch ( state )
		{
			case 0x0F or 0x11 or 0x13:
				member.SpeedBonus = BonusTable[1];
				member.Template = WalkTemplate;
				break;
			case 0x10 or 0x12 or 0x14:
				member.WorkStamp = turn;
				member.Template = WorkTemplate;
				break;
			case 0 or 0x23:
				member.IdleStamp = member.State == Walking ? turn : 0;
				member.SpeedBonus = BonusTable[0];
				member.Template = IdleTemplate;
				break;
			case Walking:
				member.Template = WalkTemplate;
				break;
			default:
				throw new InvalidOperationException( $"Theme Park Inc staff state {state} is not ported." );
		}
		member.State = state;
	}

	private enum WalkResult { Arrived, Walking, Stuck }

	/// <summary>One walk step: arrived, walking or stuck (Theme Park World's walk, see the class remarks).</summary>
	private WalkResult Walk( ThemeParkIncStaffMember member )
	{
		var navigation = member.Navigation!;
		if ( !navigation.Original )
			return WalkResult.Arrived;
		return navigation.Step( member.MaxSpeed, member.MaxForce, grid.Cells, static ( _, _ ) => false, Separation ) switch
		{
			OpenTPW.WalkResult.Walking => WalkResult.Walking,
			OpenTPW.WalkResult.Stuck => WalkResult.Stuck,
			_ => WalkResult.Arrived
		};
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

	/// <summary>Sets the walk to a point of a cell; false when no route reaches it (the walk then stands arrived and stuck).</summary>
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
			if ( grid.IsWalkable( x, y ) && Route( member, (x, y), GuestWander.CentreByte, GuestWander.CentreByte ) )
				return true;
		}
		return false;
	}
}
