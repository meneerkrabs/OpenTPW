namespace OpenTPW;

/// <summary>
/// Theme Park Inc's staff economy: the 32-slot hiring pool and the hired members (docs/reverse/TPI-STAFF.md). The pool follows the executable's own routines,
/// which keep Theme Park World's skeleton with experience, wages and agencies in place of grades and a sixth role, the gardener.
/// </summary>
public sealed class ThemeParkIncStaff
{
	/// <summary>The pool's slots (0x20-byte records at the world's offset 0).</summary>
	public const int PoolSlots = 32;

	private readonly ThemeParkIncStaffSettings settings;
	private readonly Func<ThemeParkIncStaffRole, int, string> nameText;
	private readonly ThemeParkIncHireCandidate?[] slots = new ThemeParkIncHireCandidate?[PoolSlots];
	private readonly bool[] placing = new bool[PoolSlots];
	private readonly List<ThemeParkIncStaffMember> members = new();
	private readonly bool[] atParkMaximum = new bool[6];
	private readonly int[] hired = new int[6];
	private bool openingPoolMade;

	/// <param name="nameText">A role's name text by index (the per-role string tables at 0xA0D610..0xA0D624); the name test compares texts.</param>
	public ThemeParkIncStaff( ThemeParkIncStaffSettings settings, Func<ThemeParkIncStaffRole, int, string> nameText )
	{
		this.settings = settings ?? throw new ArgumentNullException( nameof( settings ) );
		this.nameText = nameText ?? throw new ArgumentNullException( nameof( nameText ) );
	}

	public ThemeParkIncStaffSettings Settings => settings;
	/// <summary>The pool's slots; null for an empty one.</summary>
	public IReadOnlyList<ThemeParkIncHireCandidate?> Slots => slots;
	public IEnumerable<ThemeParkIncHireCandidate> Candidates => slots.OfType<ThemeParkIncHireCandidate>();
	public IReadOnlyList<ThemeParkIncStaffMember> Members => members;
	/// <summary>The turn of the pool's last refill (pool +0x418).</summary>
	public long PoolStamp { get; private set; }
	/// <summary>The pool slot the hire list has selected, which never expires; -1 for none.</summary>
	public int SelectedSlot { get; set; } = -1;
	public int NextId { get; private set; } = 1;

	private int Free => slots.Count( slot => slot == null );

	private int FirstFreeSlot()
	{
		for ( var slot = 0; slot < PoolSlots; slot++ )
		{
			if ( slots[slot] == null )
				return slot;
		}
		return PoolSlots;
	}

	private int Pooled( ThemeParkIncStaffRole role ) => slots.Count( slot => slot?.Role == role );

	/// <summary>Whether a pool candidate or a member already carries the name text.</summary>
	// [BIN:TPI-EXE:0x631A30 name taken] every valid candidate's name formatted through its role's table, then every staff thing's name text (+0x1C0), compared exactly (0x8717B8)
	private bool NameTaken( ThemeParkIncStaffRole role, int index )
	{
		var text = nameText( role, index );
		return Candidates.Any( candidate => nameText( candidate.Role, candidate.NameIndex ) == text ) || members.Any( member => nameText( member.Role, member.NameIndex ) == text );
	}

	private void AddCandidate( ThemeParkIncStaffRole role, int slot, WorldRng world, long turn, int promotionLevel )
	{
		slots[slot] = ThemeParkIncStaffCandidates.Generate( settings, role, world, promotionLevel, NextId++, turn, NameTaken );
	}

	/// <summary>The opening pool of a new park.</summary>
	// [BIN:TPI-EXE:0x631F30 opening pool] the roles in the order handyman, mechanic, gardener (BeginningNumberOfHandymen), entertainer, guard, researcher, each with its
	// BeginningNumberOf* candidates in the first free slots, skipping a role at its park maximum and stopping at a full pool; a second call only logs (0x6320F2), and the caller
	// 0x630CB0 stamps the pool with the turn
	public void CreateOpeningPool( WorldRng world, long turn, int promotionLevel )
	{
		if ( !openingPoolMade )
		{
			foreach ( var role in new[] { ThemeParkIncStaffRole.Handyman, ThemeParkIncStaffRole.Mechanic, ThemeParkIncStaffRole.Gardener, ThemeParkIncStaffRole.Entertainer,
				ThemeParkIncStaffRole.Guard, ThemeParkIncStaffRole.Researcher } )
			{
				for ( var count = 0; count < settings.Roles[role].BeginningNumber; count++ )
				{
					var slot = FirstFreeSlot();
					if ( !atParkMaximum[(int)role] && slot != PoolSlots )
						AddCandidate( role, slot, world, turn, promotionLevel );
				}
			}
			openingPoolMade = true;
		}
		PoolStamp = turn;
	}

	/// <summary>
	/// One world turn of the pool: expired candidates leave; every 4 × TimeBetweenStaffUpdates turns the pool refills in proportion to each role's shortfall and is topped up to the minimums.
	/// </summary>
	// [BIN:TPI-EXE:0x631B40 pool update] a valid candidate expires when the turns since its stamp exceed 4 × its timeout (unsigned), unless it is being placed or the hire list has it
	// selected (0x540E70); the refill waits for more than 4 × TimeBetweenStaffUpdates turns since the pool stamp, takes cap = min(MaxNumberOfStaffPerUpdate, free slots), runs the park
	// maximum check (0x631490), computes each role's shortfall (Max<Role> less its pooled candidates, the gardener against MaxHandymen, 0 for a role at its park maximum; signed),
	// and while cap and the shortfalls' sum are positive picks a role with R % sum (unsigned) walked over the roles in order, builds its candidate in the first free slot and lowers
	// cap, that shortfall and the sum; then the minimum top-up (0x631690) and the new stamp
	public void UpdatePool( WorldRng world, long turn, int promotionLevel )
	{
		for ( var slot = 0; slot < PoolSlots; slot++ )
		{
			if ( slots[slot] is { } candidate && (uint)(turn - candidate.Stamp) > (uint)(4 * candidate.Timeout) && !placing[slot] && SelectedSlot != slot )
				slots[slot] = null;
		}
		if ( (uint)(turn - PoolStamp) <= (uint)(4 * settings.TimeBetweenStaffUpdates) )
			return;
		var cap = Math.Min( settings.MaxNumberOfStaffPerUpdate, Free );
		CheckParkMaximums();
		var shortfall = new int[6];
		foreach ( var role in Enum.GetValues<ThemeParkIncStaffRole>() )
			shortfall[(int)role] = atParkMaximum[(int)role] ? 0 : settings.Roles[role].MaxInPool - Pooled( role );
		var total = shortfall.Sum();
		while ( cap > 0 && total > 0 )
		{
			var remainder = (int)(world.NextWord() % (uint)total);
			var picked = -1;
			for ( var role = 0; role < 6; role++ )
			{
				if ( remainder < shortfall[role] )
				{
					picked = role;
					break;
				}
				remainder -= shortfall[role];
			}
			if ( picked < 0 || atParkMaximum[picked] )
				break;
			var slot = FirstFreeSlot();
			if ( slot == PoolSlots )
				break;
			AddCandidate( (ThemeParkIncStaffRole)picked, slot, world, turn, promotionLevel );
			cap--;
			shortfall[picked]--;
			total--;
		}
		TopUpToMinimums( world, turn, promotionLevel );
		PoolStamp = turn;
	}

	/// <summary>Counts the hired members per role and marks the roles at their park maximum.</summary>
	// [BIN:TPI-EXE:0x631490 park maximum check] every staff thing counts (no state filter); a role is at its maximum when its count reaches Max<Role>InPark (signed), the gardener
	// against its own MaxGardenersInPark
	private void CheckParkMaximums()
	{
		Array.Clear( hired );
		Array.Clear( atParkMaximum );
		foreach ( var member in members )
		{
			var role = (int)member.Role;
			hired[role]++;
			if ( hired[role] >= settings.Roles[member.Role].MaxInPark )
				atParkMaximum[role] = true;
		}
	}

	/// <summary>Tops every role up to its pool minimum, counting hired and pooled staff.</summary>
	// [BIN:TPI-EXE:0x631690 minimum top-up] each pass takes the roles mechanic, handyman, gardener (MinHandymenInPool), entertainer and guard: a role whose hired plus pooled count is below
	// its minimum (signed) gets one candidate unless it is at its park maximum or the pool is full, and the pass repeats while any was below; the researcher comes last (0x631970,
	// 0x631900) and a researcher shortfall restarts the pass whatever the adder did. A role below its minimum that cannot be served would loop forever in the executable; here the
	// loop stops once a pass adds nothing
	private void TopUpToMinimums( WorldRng world, long turn, int promotionLevel )
	{
		var order = new[] { ThemeParkIncStaffRole.Mechanic, ThemeParkIncStaffRole.Handyman, ThemeParkIncStaffRole.Gardener, ThemeParkIncStaffRole.Entertainer, ThemeParkIncStaffRole.Guard };
		bool more;
		do
		{
			more = false;
			var added = false;
			foreach ( var role in order )
			{
				if ( hired[(int)role] + Pooled( role ) < settings.Roles[role].MinInPool )
				{
					var slot = FirstFreeSlot();
					if ( !atParkMaximum[(int)role] && slot != PoolSlots )
					{
						AddCandidate( role, slot, world, turn, promotionLevel );
						added = true;
					}
					more = true;
				}
			}
			var researcher = ThemeParkIncStaffRole.Researcher;
			if ( hired[(int)researcher] + Pooled( researcher ) < settings.Roles[researcher].MinInPool )
			{
				var slot = FirstFreeSlot();
				if ( !atParkMaximum[(int)researcher] && slot != PoolSlots )
				{
					AddCandidate( researcher, slot, world, turn, promotionLevel );
					added = true;
				}
				more = true;
			}
			if ( !added )
				break;
		}
		while ( more );
	}

	/// <summary>Starts hiring the candidate in <paramref name="slot"/>: it is being placed and stays in the pool until it is placed or the placement is cancelled.</summary>
	// [BIN:TPI-EXE:0x630D10 hire] a valid slot below 32 is marked as being placed (+0x12), then the park maximum check runs and every other candidate of a role at its maximum
	// leaves the pool unless it is being placed or selected (0x631214..0x63125A); no hire fee is charged (the cost keys are read by nothing)
	public bool BeginHire( int slot )
	{
		if ( slot < 0 || slot >= PoolSlots || slots[slot] == null )
			return false;
		placing[slot] = true;
		CheckParkMaximums();
		for ( var other = 0; other < PoolSlots; other++ )
		{
			if ( slots[other] is { } candidate && atParkMaximum[(int)candidate.Role] && !placing[other] && SelectedSlot != other )
				slots[other] = null;
		}
		return true;
	}

	/// <summary>Ends the placements in progress: a placed hire becomes a member and leaves the pool, a cancelled one returns to it.</summary>
	// [BIN:TPI-EXE:0x6319E0 placement end] for every slot being placed: a completed placement clears its valid byte (the member was made by the role constructor on the drop, 0x4D4EB0 ->
	// 0x626190), a cancelled one only its being-placed byte
	public ThemeParkIncStaffMember? EndHire( int slot, bool placed )
	{
		if ( slot < 0 || slot >= PoolSlots || !placing[slot] || slots[slot] is not { } candidate )
			return null;
		placing[slot] = false;
		if ( !placed )
			return null;
		slots[slot] = null;
		var member = ThemeParkIncStaffMember.Hire( candidate, NextId++ );
		members.Insert( 0, member );
		return member;
	}

	/// <summary>A role's strike record (staff HQ +0x15C + 12 × role): the escalation level, whether it is on strike and the turn it was last considered.</summary>
	public sealed record StrikeRecord( int Level, bool Active, long Turn );

	/// <summary>A strike message (type 0x17): the role, its kind (2 warning or strike, 3 grievance gone, 5 strike over) and the reason (the grievance for a warning, else 0).</summary>
	public readonly record struct StrikeMessage( ThemeParkIncStaffRole Role, int Kind, int Reason );

	private readonly StrikeRecord[] strikes = Enumerable.Range( 0, 6 ).Select( _ => new StrikeRecord( 0, false, -1 ) ).ToArray();

	/// <summary>The roles' strike records, by role.</summary>
	public IReadOnlyList<StrikeRecord> Strikes => strikes;

	/// <summary>The strike the player forced (HQ +0x1A8): every role's grievance test answers 1.</summary>
	public bool StrikeForced { get; set; }

	/// <summary>A role's members in their list order (newest first).</summary>
	public IEnumerable<ThemeParkIncStaffMember> OfRole( ThemeParkIncStaffRole role ) => members.Where( member => member.Role == role );

	/// <summary>
	/// The role's grievance: 1 when forced, else 4 when its members' average energy, 2 when their average happiness and 3 when their average motivation is at or below the
	/// threshold while at least MinStaffOfTypeForStrike members work in the role; 0 for none.
	/// </summary>
	// [BIN:TPI-EXE:0x633740 grievance] the forced flag (+0x1A8) answers 1; three passes in the order energy (+0x238, EnergyPercentToStrike, 4), happiness (+0x230, HappyPercentToStrike, 2)
	// and motivation (+0x234, MotivationPercentToStrike, 3), each summing trunc(meter) & 0xFF in an unsigned word: count >= the minimum (unsigned) and sum / count (unsigned, floor)
	// <= the threshold (unsigned) answer its code
	public int Grievance( ThemeParkIncStaffRole role )
	{
		if ( StrikeForced )
			return 1;
		var list = OfRole( role ).ToList();
		int Pass( Func<ThemeParkIncStaffMember, float> meter, int threshold, int code )
		{
			var count = (uint)list.Count;
			var sum = 0u;
			foreach ( var member in list )
				sum += (uint)((int)meter( member ) & 0xFF);
			return count >= (uint)settings.MinStaffOfTypeForStrike && sum / count <= (uint)threshold ? code : 0;
		}
		var energy = Pass( member => member.Energy, settings.EnergyPercentToStrike, 4 );
		if ( energy != 0 )
			return energy;
		var happiness = Pass( member => member.Happiness, settings.HappyPercentToStrike, 2 );
		return happiness != 0 ? happiness : Pass( member => member.Motivation, settings.MotivationPercentToStrike, 3 );
	}

	/// <summary>The park's age in whole game days at <paramref name="turn"/>, as the wage demand counts it.</summary>
	// [BIN:TPI-EXE:0x628CC2 demand day count] the same 100 ns park age (0x608C70) divided by a day's 864000000000 as a signed 64-bit division (__alldiv 0x871A30), kept as an unsigned word
	public static uint GameDay( long turn ) => unchecked( (uint)(turn * 15000 / 4 * 10_000_000 / 864_000_000_000L) );

	/// <summary>The park's age in whole 30-day months at <paramref name="turn"/>: turn × 15000 / 4 game seconds.</summary>
	// [BIN:TPI-EXE:0x608C70 park age] floor(turn × SecsPerRealSec / 4) seconds in 100 ns units, divided by 30 days (0x1792F8648000) as a signed 64-bit division, the low word kept
	public static int ParkAgeMonths( long turn ) => (int)(turn * 15000 / 4 * 10_000_000 / 0x1792F8648000L);

	/// <summary>
	/// The month's strike consideration: a role on strike returns to work, the others escalate on a grievance (warning, then one week, two weeks, a month) or settle without one.
	/// </summary>
	// [BIN:TPI-EXE:0x632C20 strike month] the roles 0 to 5 in order, skipping a role without members or already considered this turn; a role on strike ends it (0x6342B0 with a
	// message of kind 5) and is not considered; [BIN:TPI-EXE:0x6330B0 strike consideration] nothing before MinMonthsForStrike months (signed); with a grievance the level 0 posts a
	// warning (kind 2, the grievance as the reason) and becomes 1, the levels 1, 2 and 3 strike (active, kind 2 with reason 0) and step up, the level 4 strikes again as level 2
	// without a message; without a grievance a level of 1 or more posts kind 3, and the level always returns to 0 with the strike ended quietly
	public IReadOnlyList<StrikeMessage> ConsiderStrikes( long turn )
	{
		var messages = new List<StrikeMessage>();
		foreach ( var role in Enum.GetValues<ThemeParkIncStaffRole>() )
		{
			var index = (int)role;
			var record = strikes[index];
			if ( !OfRole( role ).Any() || record.Turn == turn )
				continue;
			record = record with { Turn = turn };
			if ( record.Active )
			{
				strikes[index] = record with { Active = false };
				messages.Add( new StrikeMessage( role, 5, 0 ) );
				continue;
			}
			strikes[index] = record;
			if ( ParkAgeMonths( turn ) < settings.MinMonthsForStrike )
				continue;
			var grievance = Grievance( role );
			if ( grievance != 0 )
			{
				switch ( record.Level )
				{
					case 0:
						strikes[index] = record with { Level = 1 };
						messages.Add( new StrikeMessage( role, 2, grievance ) );
						break;
					case 1 or 2 or 3:
						strikes[index] = record with { Level = record.Level + 1, Active = true };
						messages.Add( new StrikeMessage( role, 2, 0 ) );
						break;
					case 4:
						strikes[index] = record with { Level = 2, Active = true };
						break;
				}
			}
			else
			{
				if ( record.Level >= 1 )
					messages.Add( new StrikeMessage( role, 3, 0 ) );
				strikes[index] = record with { Level = 0, Active = false };
			}
		}
		return messages;
	}

	/// <summary>A member's wage demand (the 0x5CEFB0 object, message type 0x19): 0xA when the role has a grievance in a park younger than MinMonthsForStrike months, else 0xB.</summary>
	public readonly record struct WageDemand( ThemeParkIncStaffMember Member, int Type, int Shortfall );

	/// <summary>The game day of the last wage demand (0xAA07F4, one for all staff); 0 means none, so a demand on day 0 does not block the next.</summary>
	public uint LastWageDemandDay { get; internal set; }

	/// <summary>The members in the order the message broadcaster reaches them: ascending thing handle (here the hire order).</summary>
	// [BIN:TPI-EXE:0x436E97 message order] the broadcaster 0xA09728 keeps a set per message type keyed by the thing handle and walks it in order, so the staff take the day and month
	// messages in ascending handle order
	private IEnumerable<ThemeParkIncStaffMember> InHandleOrder => members.OrderBy( member => member.Id );

	/// <summary>The month message: every member is paid its salary, then its wage is reviewed.</summary>
	// [BIN:TPI-EXE:0x6265DE staff payday] message 0x0C: the member's own salary (+0x214) goes to the bank's debit (0x64EBE0 -> 0x5C07B0) and the park's wage total (+0x1FA58),
	// then the wage review 0x62CDA0
	public void PayMonth( Action<ThemeParkIncStaffMember, int> debit, WorldRng world )
	{
		foreach ( var member in InHandleOrder.ToList() )
		{
			debit( member, member.Salary );
			member.ReviewWage( settings, world );
		}
	}

	/// <summary>
	/// The day message: every member gains the day's experience and, on its demand day at least 30 days after the last demand of any member, demands its shortfall.
	/// </summary>
	// [BIN:TPI-EXE:0x628CA0 staff day] message 0x0B: the experience gain 0x629650; then a demand day (+0x26C > 0) that is today's day of the month, with at least 30 game days since
	// the last demand (the global 0xAA07F4, unsigned), builds the demand (0x5CEFB0: type 0xA when 0x633740 finds a grievance for the role and the park is younger than
	// MinMonthsForStrike months, else 0xB) with the shortfall (+0x270) and clears both words; 0x62D1A0 then clears the day's counters
	public IReadOnlyList<WageDemand> EndDay( long turn, int dayOfMonth )
	{
		var gameDay = GameDay( turn );
		var demands = new List<WageDemand>();
		foreach ( var member in InHandleOrder.ToList() )
		{
			var shortfall = member.WageShortfall;
			var demandToday = member.WageDemandDay > 0 && member.WageDemandDay == dayOfMonth && (LastWageDemandDay == 0 || unchecked( gameDay - LastWageDemandDay ) >= 30u);
			member.EndDay( settings );
			if ( !demandToday )
				continue;
			LastWageDemandDay = gameDay;
			var type = Grievance( member.Role ) != 0 && ParkAgeMonths( turn ) < settings.MinMonthsForStrike ? 0xA : 0xB;
			demands.Add( new WageDemand( member, type, shortfall ) );
			member.WageDemandDay = 0;
			member.WageShortfall = 0;
		}
		return demands;
	}

	/// <summary>Whether the slot's candidate is being placed.</summary>
	public bool IsPlacing( int slot ) => slot >= 0 && slot < PoolSlots && placing[slot];
}
