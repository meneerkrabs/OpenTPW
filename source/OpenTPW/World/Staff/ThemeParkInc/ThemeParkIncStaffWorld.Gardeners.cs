namespace OpenTPW;

/// <summary>
/// Theme Park Inc's gardener in the park (docs/reverse/TPI-STAFF.md, "Gardener and plants"): kind 0x14, the pre-update <c>0x5D2820</c> and the dispatch <c>0x5D2A60</c> (0 idle, 1 patrol, 0x1F to
/// the plant, 0x20 watering, the training states through the common handler), the search <c>0x5D28C0</c> with the plant chooser <c>0x5D2DC0</c> and the claim reader <c>0x5E37F0</c>, and the
/// watering's end (ParkEconomy.FinishThemeParkIncWatering).
/// </summary>
/// <remarks>
/// The states 2 and 3 (to a rest area and resting) are not reached: no Theme Park Inc object is a rest area. The facing the watering state asks for (<c>0x609E60</c>: the heading word +0x2C from the
/// queue link byte of the gardener's cell, 1000, 1500, 500 or 2000 in the 2048 units of <c>0x91E074</c>) is presentation and not ported: the gardener keeps the heading of his walk's last step
/// (<c>[EXT:tpi-twp-guest-rules]</c>, D1). The destructor's claim release (<c>0x5D2430</c> → <c>0x5E37B0</c>) waits for the firing of Theme Park Inc staff, which is not ported.
/// </remarks>
public sealed partial class ThemeParkIncStaffWorld
{
	/// <summary>The gardener's states this world runs besides idle and walking: 0x1F to the plant, 0x20 watering.</summary>
	public const int ToPlant = 0x1F, Watering = 0x20;

	/// <summary>The gardener's walking voice event (cat_staff), the handyman's: the gardener has no idle voice.</summary>
	public const int GardenerWalkingVoice = 0xA0;

	/// <summary>
	/// The plant's thing as the chooser reads it (its thing cell, the bytes +5 and +7) and as the drop's fallback <c>0x5E9320</c> takes it (its shape, angle and map offset); null for an object the level
	/// does not place.
	/// </summary>
	public Func<int, DropObject?>? ObjectThings { get; set; }

	/// <summary>The gardener's pre-update: the common part, then, while idle, the search every search period.</summary>
	// [BIN:TPI-EXE:0x5D2820 gardener pre-update] 0x628050; p = ftol((1.0 - exp / MaxExperience) * 50.0f) (fildll of the unsigned experience, fidivl 0xA02260, fsubrl 0x93B858, fmuls 0x93B89C); p 0 or the
	// world turn % p == 0 (unsigned divl) with the state +0x1B4 0 runs the search 0x5D28C0, with no finder before it; then 0x6388F0 on +0x44 (the thought's lapse, presentation)
	// Known x87 divergence (docs/reverse/TPI-STAFF.md, "Known divergence: x87 precision"): the original computes p at the game thread's 24-bit control word, the shared SearchPeriod at 53
	// bits (experience 34000 of 100000: 33 there, 32 here), as the handyman's [BIN:TPI-EXE:0x5BCD60 precision] reading has it; the planned 24-bit precision change moves both
	private void PreUpdateGardener( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		var period = SearchPeriod( member.Experience, settings.MaxExperience );
		var due = period == 0 || unchecked( (uint)turn ) % (uint)period == 0;
		if ( member.State == Idle && due )
			SearchGardener( economy, member, turn );
	}

	/// <summary>The gardener's state handler.</summary>
	// [BIN:TPI-EXE:0x5D2A60 gardener dispatch] states 0..0x20 by the byte table 0x5D2B68 and the jump table 0x5D2B4C: 0 the idle end 0x62CA10, 1 0x5D2A81, 2 0x629EF0 and 3 0x62A1C0 (the rest area's,
	// unreached), 0x1F 0x5D2C70, 0x20 0x5D2ACE, every other and anything above 0x20 the common handler 0x6287A0 (7 -> 0x62A7C0, 8 -> 0x62AA50)
	private void DispatchGardener( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		switch ( member.State )
		{
			case Idle:
				// [BIN:TPI-EXE:0x5D2B38 state 0] the idle end 0x62CA10 alone: no voice draw. The turn above +0x23C less the idle term (unsigned) looks for work (0x62D810, kind 0x14: the search 0x5D28C0)
				if ( unchecked( (uint)turn ) > unchecked( (uint)(member.IdleStamp - IdleTerm( member.Experience, settings.MaxExperience )) ) )
					LookForWork( economy, settings, member, turn );
				break;
			case Walking:
				// [BIN:TPI-EXE:0x5D2A81 state 1] a world draw (0x6493E0) whose low nibble 0 plays the walking voice 0xA0 (0x60BE90); the walk step 0x60B440: arrived (0) or stuck (2) searches, still
				// walking takes the moving step 0x62B3E0
				Voice( economy, member, GardenerWalkingVoice );
				if ( Walk( member ) is WalkResult.Arrived or WalkResult.Stuck )
					SearchGardener( economy, member, turn );
				else
					member.Step( settings );
				break;
			case ToPlant:
				UpdateToPlant( economy, member, turn );
				break;
			case Watering:
				UpdateWatering( economy, settings, member, turn );
				break;
			case ThemeParkIncTraining.ToTrainingRoom:
				UpdateToTrainingRoom( economy, settings, member, turn );
				break;
			case ThemeParkIncTraining.Training:
				UpdateTraining( economy, settings, member, turn );
				break;
		}
	}

	/// <summary>
	/// State 0x1F: walking, he walks on; stuck, he goes idle with his plant kept; arrived, the plant's claim must still be his and he starts to water it (0x20), else he goes idle.
	/// </summary>
	// [BIN:TPI-EXE:0x5D2C70 to the plant] the walk step 0x60B440 first: 1 asks for the thought 0x1D (0x638960, presentation) and returns, 2 sets 0 (0x628180(0), +0x5D8 kept); 0: the claimant
	// 0x5E37F0 of table[+0x5D8] against his thing id (0x6375C0): his own sets +0x5D4 = the world turn, template 0x19 (0x5D0F90) and the state 0x20 (0x5D2CD1), another one 0x628180(0)
	private void UpdateToPlant( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		switch ( Walk( member ) )
		{
			case WalkResult.Walking:
				return;
			case WalkResult.Stuck:
				SetState( economy, member, Idle, turn );
				return;
		}
		// the plant's removal message (0x5D2480) clears +0x5D8 and leaves this state, so the plant is there
		if ( !economy.TryGetObject( member.PlantId, out var plant ) )
			throw new InvalidOperationException( $"Theme Park Inc gardener {member.Id} walks to object {member.PlantId}, which is not in the park." );
		SetState( economy, member, Claimant( economy, plant, turn ) == member.ThingId ? Watering : Idle, turn );
	}

	/// <summary>
	/// State 0x20: the working step; once the work duration has passed since the watering began, the plant's watering ends (it is watered, its claim and request clear) and he goes idle.
	/// </summary>
	// [BIN:TPI-EXE:0x5D2ACE watering] the working step 0x62B4D0, the facing 0x609E60 (presentation), the work duration 0x62E4C0 (kind 0x14: Gardener.MinWorkDuration and MaxWorkDuration, 0xA022D0 and
	// 0xA022D4); a turn at most +0x5D4 + the duration (unsigned jbe) waters on; else 0x5E31B0 on table[+0x5D8], template 3 (0x5D0F90) and 0x628180(0). No tiredness test: a watering runs to its end
	// unless the plant goes (the removal message). The duplicate body 0x5D2D40 is reached only through the thunk 0x403A62, which nothing calls
	private void UpdateWatering( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		member.Step( settings );
		var duration = settings.WorkDuration( ThemeParkIncStaffRole.Gardener, member.Experience, member.Motivation, member.Salary );
		if ( unchecked( (uint)turn ) <= unchecked( (uint)(member.WorkStamp + duration) ) )
			return;
		if ( !economy.TryGetObject( member.PlantId, out var plant ) )
			throw new InvalidOperationException( $"Theme Park Inc gardener {member.Id} waters object {member.PlantId}, which is not in the park." );
		economy.FinishThemeParkIncWatering( plant );
		RequestTemplate( member, IdleTemplate );
		SetState( economy, member, Idle, turn );
	}

	/// <summary>
	/// The gardener's search (0x5D28C0): the strike and rest gate; unless too tired, the plant the chooser takes (which claimed it and set his walk), which asks for maintenance unless it did, and he
	/// sets off (0x1F); without one, a patrol, else idle.
	/// </summary>
	// [BIN:TPI-EXE:0x5D28C0 gardener search] 0x62BBA0 nonzero returns; tired (0x62B3B0, "Too tired to water plants") goes to the tail; +0x5D8 = the chooser 0x5D2DC0 whatever it answers; 0 goes to the
	// tail; "Gardener found dry plant, walking there now"; a plant without its request (0x5E3B70) gets 0x5E3400(1); then 0x60B3F0(1) (the speed bonus 25), template 0xF (0x5D0F90) and the state 0x1F
	// stored directly; the tail: the patrol destination 0x609F70 sets 1 (0x628180(1)), none sets 0
	private void SearchGardener( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		var settings = economy.TpiStaff!.Settings;
		if ( Gate( economy, settings, member, turn ) )
			return;
		if ( !member.Tired( settings ) )
		{
			member.PlantId = FindPlant( economy, member, turn );
			if ( member.PlantId != 0 && economy.TryGetObject( member.PlantId, out var plant ) )
			{
				if ( !plant.MaintenanceRequested )
					economy.RequestThemeParkIncMaintenance( plant );
				SetState( economy, member, ToPlant, turn );
				return;
			}
		}
		SetState( economy, member, Patrol( economy, member ) ? Walking : Idle, turn );
	}

	/// <summary>
	/// The plant chooser (0x5D2DC0): the object list from its head; a plant whose claim is free or his, inside his area: one that asked for maintenance, nearer than the best so far and not burning, or
	/// one that is dry, within his detection range and not burning, is taken when the path cell beside it (the drop's fallback, which sets that cell's queue link) has a route; the last taken wins, so
	/// a dry plant met later replaces a nearer one. He claims it. 0 for none.
	/// </summary>
	// [BIN:TPI-EXE:0x5D2DC0 plant chooser] the head word world +0x1EAB88, each thing's next +0x14; a thing with +0x43 bit 0x10; the claimant 0x5E37F0 nonzero and (read again) not his thing id
	// (0x6375C0) skips; the area 0x62C6B0; d = dx² + dy² of the cell words' columns and rows (cltd, xor, sub: absolute); R = 0x62C900(0, MaxExperience, exp, Gardener.MinDetectionRange 0xA022D8,
	// MaxDetectionRange 0xA022DC); the request 0x5E3B70 set with d below the best (unsigned jae) and 0x5E3B20 0 takes it; else the dry test 0x5E3990, d below R × R (imul, unsigned jae) and 0x5E3B20 0
	// take it; a taken plant: 0x5E9320(the plant, his cell word) answers the word, 0 skips, the route 0x62EAC0 (0x62EB10 with the point 0x80, 0x80) failing skips, else best = d and the handle; after the
	// list a handle claims the plant (0x5E3780: +0x72 his thing id, +0x74 the world turn). tools/ppc-analysis/oracle_x86/tpi_gardener_oracle.py runs the chooser natively against this reading
	internal int FindPlant( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		var settings = economy.TpiStaff!.Settings;
		var range = settings.Roles[ThemeParkIncStaffRole.Gardener];
		var (x, y) = member.Navigation!.Cell;
		var best = uint.MaxValue;
		var chosen = 0;
		foreach ( var key in economy.ThemeParkIncObjectList.ToList() )
		{
			// a thing without an economy record (an obstacle, the camera path, a tool entry) has no flag 0x1000
			if ( key <= 0 || !economy.TryGetObject( key, out var item ) || !economy.IsThemeParkIncPlant( item ) )
				continue;
			if ( Claimant( economy, item, turn ) != 0 && Claimant( economy, item, turn ) != member.ThingId )
				continue;
			if ( !InGardenerArea( member ) || ObjectThings?.Invoke( key ) is not { } thing )
				continue;
			var dx = Math.Abs( thing.ThingCell.X - x );
			var dy = Math.Abs( thing.ThingCell.Y - y );
			var distance = unchecked( (uint)(dx * dx + dy * dy) );
			var radius = ThemeParkIncStaffSettings.Lerp( member.Experience, 0, settings.MaxExperience, range.MinRange, range.MaxRange );
			var take = item.MaintenanceRequested && distance < best && economy.ThemeParkIncPlantOnFire( item ) == 0;
			if ( !take && (!economy.ThemeParkIncPlantDry( item ) || distance >= unchecked( (uint)(radius * radius) ) || economy.ThemeParkIncPlantOnFire( item ) != 0) )
				continue;
			var word = FallbackDropWord( grid.Cells, thing, CellWord( x, y ) );
			if ( word == 0 || !StaffRoute( economy, member, CellOfWord( word ), GuestWander.CentreByte, GuestWander.CentreByte ) )
				continue;
			best = distance;
			chosen = key;
		}
		if ( chosen != 0 && economy.TryGetObject( chosen, out var plant ) )
			economy.ClaimThemeParkInc( plant, member.ThingId, turn );
		return chosen;
	}

	/// <summary>The gardener's area test (0x62C6B0): true for the whole map, the only area a placed member has (no patrol area is set in the park; a saved gardener is refused with another).</summary>
	// [BIN:TPI-EXE:0x62C6B0 area test] the corner words of +0x24C..+0x258 equal to 1 and 0x4000 (the cells (0, 0) and (127, 127)) answer true; any other rectangle runs 0x6256A0, which needs the
	// patrol-area tool (not ported; ThemeParkIncStaffMember.IsValidWorld refuses another rectangle for a gardener)
	private static bool InGardenerArea( ThemeParkIncStaffMember member ) =>
		member.PatrolArea == (0, 0, ParkLitter.MapSize - 1, ParkLitter.MapSize - 1)
			? true
			: throw new InvalidOperationException( "A Theme Park Inc gardener's patrol area (0x6256A0) is not ported." );

	/// <summary>The gardener's own states in the state hook (0x5D29D0): 0x1F the walk with the speed bonus, 0x20 the watering's stamp and template.</summary>
	// [BIN:TPI-EXE:0x5D29D0 gardener state hook] 0x1F: 0x60B3F0(1) (+0xD6 = 25), template 0xF (0x5D0F90), the state; 0x20: +0x5D4 = the world turn, template 0x19, the state; any other the common
	// setter 0x628180. The search and the walk to the plant write the same words and the state themselves (0x5D2954..0x5D2967, 0x5D2CB5..0x5D2CD1)
	private void SetGardenerState( ThemeParkIncStaffMember member, int state, long turn )
	{
		if ( state == ToPlant )
		{
			member.SpeedBonus = BonusTable[1];
			RequestTemplate( member, WalkTemplate );
		}
		else
		{
			member.WorkStamp = turn;
			RequestTemplate( member, WorkTemplate );
		}
		member.State = state;
	}
}
