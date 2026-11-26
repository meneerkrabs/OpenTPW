namespace OpenTPW;

/// <summary>
/// Theme Park Inc's entertainer (kind 6: the pre-update <c>0x5D1960</c>, the dispatch <c>0x5D1E20</c>, the search <c>0x5D1A30</c> and the setter <c>0x5D1D30</c>) and guard (kind 7: the pre-update
/// <c>0x5D3B40</c>, the dispatch <c>0x5D3C60</c>, the decision <c>0x5D3C10</c> and the setter <c>0x5D3E70</c>) in the park (docs/reverse/TPI-STAFF.md, "Entertainer" and "Guard").
/// </summary>
/// <remarks>
/// The guard's chase, carry and first aid (the states 0x1A, 0x1C, 0x1D and 0x1E, entered from the staff HQ's prank message <c>0x6323F9</c> and the first-aid loop <c>0x557E00</c>) are not ported: they
/// read the guests' own state numbers (<c>0x614720</c> tests the guest's <c>+0x2C0</c> against Theme Park Inc's set, 6..0xD, 0x16 and 0x1F upward; the first aid's patient is a guest in state 0x1B),
/// which a park's Theme Park World guests do not carry (<c>[EXT:tpi-capability-gates]</c>; docs/reverse/TPI-STAFF.md, "Guard"). The guests' reaction to a performing entertainer (<c>0x614D41</c>, in the guest's own
/// update: the heading and <c>+0x1B8</c>) runs for the guests of Theme Park Inc's native states (<see cref="Entertainers"/>, docs/reverse/TPI-GUESTS.md, phase P2); a park's Theme Park World guests
/// (<c>[EXT:tpi-twp-guest-rules]</c>, the default path) do not react, because Theme Park World's own turn towards a performer, 0x004FEFF4, reads Theme Park World's staff. The entertainer's row 0 on the cells
/// cheers the guests through the needs phase, as in Theme Park World.
/// </remarks>
public sealed partial class ThemeParkIncStaffWorld
{
	/// <summary>The entertainer's performing state (+0x1B4 = 0x17); the animation indices 0x13 to 0x16 are the templates the setter queues, not states.</summary>
	public const int Performing = 0x17;

	/// <summary>The entertainer's idle (0xA5) and walking (0xA4) voice events and the guard's idle (0xA7) and patrol (0xA6) ones (cat_staff), and the sound a performance ends with (0x87 = 135).</summary>
	public const int EntertainerIdleVoice = 0xA5, EntertainerWalkingVoice = 0xA4, GuardIdleVoice = 0xA7, GuardPatrolVoice = 0xA6, PerformanceEndSound = 0x87;

	/// <summary>The entertainer's performance templates 0x13 to 0x16 of the table <c>0x9CDA94</c>.</summary>
	public const int PerformanceTemplateA = 0x13, PerformanceTemplateB = 0x14, PerformanceTemplateC = 0x15, PerformanceTemplateD = 0x16;

	/// <summary>
	/// The park's theme index as the setter's theme record reads it (<c>0x74F4B0 +0x64</c>): the position of the level's theme in the table <c>0x9D53B4</c> ("arabian", "science", "water", "wIntro",
	/// "Unknown"), 4 for a level the table does not name.
	/// </summary>
	// [BIN:TPI-EXE:0x74FCE4 theme index] the record's constructor walks the nine-byte names from 0x9D53B4 ("arabian", "science", "water", "wIntro", "Unknown") until the theme's name matches and stores the
	// index of the match at +0x64 (0x74FD35)
	public int ThemeIndex { get; set; } = UnknownTheme;

	/// <summary>The index of the table's last entry.</summary>
	public const int UnknownTheme = 4;

	/// <summary>The theme index of a level by its folder name, <see cref="UnknownTheme"/> when the table does not name it.</summary>
	public static int ThemeIndexOf( string level )
	{
		var index = Array.FindIndex( ThemeNames, name => string.Equals( name, level, StringComparison.OrdinalIgnoreCase ) );
		return index < 0 ? UnknownTheme : index;
	}

	private static readonly string[] ThemeNames = ["arabian", "science", "water", "wIntro"];

	/// <summary>The cell effect's radius of an entertainer or a guard (0x62C900(0, MaxExperience, experience, the role's minimum, its maximum)).</summary>
	// [BIN:TPI-EXE:0x5D1988 effect radius] Entertainer.MinActiveRange and MaxActiveRange (0xA022EC, 0xA022F0); the guard's 0x5D3B68 takes Guard.MinDetectionRange and MaxDetectionRange (0xA02314, 0xA02318)
	private static int EffectRadiusOf( ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member )
	{
		var range = settings.Roles[member.Role];
		return ThemeParkIncStaffSettings.Lerp( member.Experience, 0, settings.MaxExperience, range.MinRange, range.MaxRange );
	}

	/// <summary>The region grid's row an entertainer (0) or a guard (3) spreads.</summary>
	private static int EffectRowOf( ThemeParkIncStaffRole role ) => role == ThemeParkIncStaffRole.Entertainer ? RegionEffect.Entertainer : RegionEffect.Guard;

	/// <summary>
	/// The member's cell effect goes down where he stands (the constructor): the row with the radius his experience gives, remembered with the cell.
	/// </summary>
	// [BIN:TPI-EXE:0x5D1446 entertainer effect] 0x5D7500(row 0, the cell word of the bytes +5 and +7, 0x62C900's radius); the guard's 0x5D32AE the same with row 3. The person constructor set +0xE4 to the cell
	// (0x608F74, 0x609029)
	private static void PutEffect( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member )
	{
		var cell = member.Navigation!.Cell;
		member.EffectCell = CellWord( cell );
		member.EffectRadius = EffectRadiusOf( settings, member );
		economy.Litter.Environment.ApplyRadius( EffectRowOf( member.Role ), cell.X, cell.Y, add: true, member.EffectRadius );
	}

	/// <summary>
	/// The pre-update's cell effect: when the cell word the member stands on is not the one 0x609E20 gave last time (it hands the stored word and stores the current), the row comes off the old cell and
	/// goes down on the new one, both with the radius his experience gives now.
	/// </summary>
	// [BIN:TPI-EXE:0x5D1960 entertainer pre-update] 0x609E20 returns +0xE4 and stores the cell word of +5, +7; unequal: radius = 0x62C900(0, MaxExperience, +0x210, 0xA022EC, 0xA022F0), 0x5D7530(row 0, the old
	// word, radius), 0x5D7500(row 0, the new word, radius); then the common 0x628050. The guard's 0x5D3B40 is the same with row 3 and Guard's range. The radius of the add that comes off may differ (experience
	// grows by the day), which leaves its difference in the grid (ParkEnvironment.KeepRadiusResidue)
	private void MoveEffect( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member )
	{
		var now = CellWord( member.Navigation!.Cell );
		var stored = member.EffectCell;
		member.EffectCell = now;
		if ( stored == now )
			return;
		var radius = EffectRadiusOf( settings, member );
		var row = EffectRowOf( member.Role );
		var environment = economy.Litter.Environment;
		var (oldX, oldY) = CellOf( stored );
		var (newX, newY) = CellOf( now );
		environment.ApplyRadius( row, oldX, oldY, add: false, radius );
		environment.ApplyRadius( row, newX, newY, add: true, radius );
		environment.KeepRadiusResidue( row, oldX, oldY, member.EffectRadius, radius );
		member.EffectRadius = radius;
	}

	/// <summary>A sound a member makes at his position whatever a draw says (the performance's end).</summary>
	private void Sound( ThemeParkIncStaffMember member, int eventId )
	{
		Voices.Add( (eventId, member.DrawX, member.DrawY) );
		if ( Voices.Count > 64 )
			Voices.RemoveRange( 0, Voices.Count - 64 );
	}

	/// <summary>The entertainer's state handler.</summary>
	// [BIN:TPI-EXE:0x5D1E20 entertainer dispatch] states 0..0x17 by the byte table 0x5D1F70 (00 01 02 03, then 05 for 4..0x16, 04 for 0x17) and the jump table 0x5D1F58: 0 0x5D1F1D, 1 0x5D1EC7,
	// 2 0x5D1F05 -> 0x629EF0 and 3 0x5D1F11 -> 0x62A1C0 (the rest area's, ThemeParkIncStaffWorld.Rest.cs), 4..0x16 and anything above 0x17 the common handler 0x6287A0 (entry 5, 0x5D1F4B; no training
	// state is reached: an entertainer learns nothing), 0x17 0x5D1E44
	private void DispatchEntertainer( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		switch ( member.State )
		{
			case Idle:
				// [BIN:TPI-EXE:0x5D1F1D state 0] a world draw whose low nibble 0 plays the idle voice 0xA5 (0x60BE90); then the idle end 0x62CA10: the turn above +0x23C less the idle term (unsigned) looks
				// for work (0x62D810, kind 6: the search 0x5D1A30)
				Voice( economy, member, EntertainerIdleVoice );
				if ( unchecked( (uint)turn ) > unchecked( (uint)(member.IdleStamp - IdleTerm( member.Experience, settings.MaxExperience )) ) )
					LookForWork( economy, settings, member, turn );
				break;
			case Walking:
				// [BIN:TPI-EXE:0x5D1EC7 state 1] a world draw whose low nibble 0 plays the walking voice 0xA4; the walk step 0x60B440: arrived (0) or stuck (2) searches (0x5D1A30), walking takes the
				// moving step 0x62B3E0
				Voice( economy, member, EntertainerWalkingVoice );
				if ( Walk( member ) is WalkResult.Arrived or WalkResult.Stuck )
					SearchEntertainer( economy, member, turn );
				else
					member.Step( settings );
				break;
			case Performing:
				// [BIN:TPI-EXE:0x5D1E44 performing] the working step 0x62B4D0 and the work duration 0x62E4C0 (Entertainer.MinWorkDuration and MaxWorkDuration); a turn at most +0x5D4 + the duration (unsigned,
				// jbe) performs on; else the sound 0x87 at the sprite's position (0x4E3D90, 0x652860) and the search
				member.Step( settings );
				var duration = settings.WorkDuration( ThemeParkIncStaffRole.Entertainer, member.Experience, member.Motivation, member.Salary );
				if ( unchecked( (uint)turn ) <= unchecked( (uint)(member.WorkStamp + duration) ) )
					return;
				Sound( member, PerformanceEndSound );
				SearchEntertainer( economy, member, turn );
				break;
			// [BIN:TPI-EXE:0x5D1E20 entertainer rest states] the byte table 0x5D1F70 sends 2 to the jump-table entry 2 (0x5D1F05 -> 0x629EF0) and 3 to entry 3 (0x5D1F11 -> 0x62A1C0), the handyman's
			// entries for the same states; 0x23 lies above 0x17 and goes to the common handler 0x6287A0, whose byte table 0x628B10 sends it (index 0x1F) to entry 10, 0x62897B -> 0x62CA90
			case ToRestArea:
				UpdateToRestArea( economy, settings, member, turn );
				break;
			case Resting:
				UpdateResting( economy, settings, member, turn );
				break;
			case RestSearch:
				UpdateRestSearch( economy, settings, member, turn );
				break;
			// [BIN:TPI-EXE:0x6287A0 strike states] the role's table sends 4 and 5 to the common handler: 4 -> 0x628855, 5 -> 0x628892 (ThemeParkIncStaffWorld.Strikes.cs)
			case ToStrike:
				UpdateToStrike( economy, member, turn );
				break;
			case Striking:
				UpdateStriking( economy, member, turn );
				break;
		}
	}

	/// <summary>
	/// The entertainer's search (0x5D1A30): the strike and rest gate; unless too tired, a world draw divisible by 3 and a guest in the square of his active range start a performance (0x17); otherwise
	/// every fourth turn he idles and on the others he patrols when a destination is found (else his state stays).
	/// </summary>
	// [BIN:TPI-EXE:0x5D1A30 entertainer search] 0x62BBA0 nonzero returns; tired (0x62B3B0, "Too tired to entertain") skips the performance; else the radius 0x62C900(0, MaxExperience, +0x210, 0xA022F0,
	// 0xA022F0) (always Entertainer.MaxActiveRange), a world draw % 3 (unsigned divl) == 0 and the finder 0x5B1B20(kind 1, the cell word, the radius) nonzero set 0x17 (0x5D1D30) and return; the tail:
	// a performing member whose theme (0x42A8D0, "a dodgy version of GetCurrentTheme") is 1 with +0x30 == 1 or 2 with +0x30 == 0 spawns a visual effect (0x60B700 -> 0x65BD10, a pool slot with no draw:
	// presentation, not ported), then the world turn & 3 == 0 sets 0, else the destination 0x609F70 sets 1, none leaves the state
	private void SearchEntertainer( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		var settings = economy.TpiStaff!.Settings;
		if ( Gate( economy, settings, member, turn ) )
			return;
		if ( !member.Tired( settings ) )
		{
			var range = settings.Roles[ThemeParkIncStaffRole.Entertainer];
			var radius = ThemeParkIncStaffSettings.Lerp( member.Experience, 0, settings.MaxExperience, range.MaxRange, range.MaxRange );
			if ( economy.World.NextWord() % 3 == 0 && AnyGuestWithin( member.Navigation!.Cell, radius ) )
			{
				SetEntertainerState( economy, member, Performing, turn );
				return;
			}
		}
		if ( (turn & 3) == 0 )
			SetEntertainerState( economy, member, Idle, turn );
		else if ( Patrol( economy, member ) )
			SetEntertainerState( economy, member, Walking, turn );
	}

	/// <summary>
	/// The finder 0x5B1B20(kind 1, cell, radius): whether a thing of kind 1 (a guest) stands in the square of the radius around the cell, clipped to the map, as Theme Park World's 0x004C8EB0 finds one.
	/// </summary>
	// [BIN:TPI-EXE:0x5B19C0 kind finder] the square [cell ± radius] clipped to 0..0x7F (only the cell word is masked to 16 bits; the radius is the argument as it is), the things of each cell's chain (0xADE6B4 + 0x1F038, nine bytes a cell) by the kind byte +2;
	// the flag 1 stops at the first. [EXT:tpi-twp-guest-rules] the guests are Theme Park World's, so the kind-1 things are the guests TPW's finder counts; that finder,
	// IGuardGuests.AnyGuestWithin, carries APPROX:ECON-022 (see there)
	private bool AnyGuestWithin( (int X, int Y) cell, int radius ) => GuestFinder?.Invoke( cell, radius ) ?? (Guests as IGuardGuests)?.AnyGuestWithin( cell, radius ) == true;

	/// <summary>A test's stand-in for the guests' finder (a world without guests); null reads <see cref="Guests"/>.</summary>
	internal Func<(int X, int Y), int, bool>? GuestFinder { get; set; }

	/// <summary>The performance template the setter queues: <paramref name="remainder"/> is the draw modulo the bank's performance count.</summary>
	internal static int PerformanceTemplateOf( uint remainder, int themeIndex, int variant ) => remainder == 0
		? themeIndex != 0 || variant != 0 ? PerformanceTemplateA : PerformanceTemplateB
		: themeIndex == 2 && variant == 1 ? PerformanceTemplateC : PerformanceTemplateD;

	/// <summary>
	/// The entertainer's setter (0x5D1D30), which his search and constructor use: it draws once from the world whatever the state, takes the state's own template for 0x17 (the draw modulo the bank's
	/// performance count n picks 0x13 or 0x14 on 0 and 0x15 or 0x16 else, by the theme and his variant) and every other state through the common setter.
	/// </summary>
	// [BIN:TPI-EXE:0x5D1D30 entertainer setter] n = 0x690330(+0x34, +0x30) (the bank's word +0x20C, the performance count), the theme record's +0x64 (0x42A3B0, 0x74F4B0), a world draw R (0x6493E0, before
	// the state is looked at) and R % n (unsigned divl: n is never 0 in the original, a world without banks takes 1); a state other than 0x17 goes to 0x628180; 0x17: R % n == 0 gives 0x13 when the theme
	// index or the variant is nonzero, else 0x14; otherwise a theme index of 2 with the variant 1 gives 0x15, any other 0x16; the template (0x5D0F90), +0x1B4 = 0x17 and +0x5D4 = the world turn
	private void SetEntertainerState( ParkEconomy economy, ThemeParkIncStaffMember member, int state, long turn )
	{
		var variant = VariantOf( member, SpriteBanks );
		var count = SpriteBanks?.AnimationsOf( StaffType.Entertainer, variant )?.PerformanceCount ?? 1;
		var draw = economy.World.NextWord() % (uint)Math.Max( 1, count );
		if ( state != Performing )
		{
			SetState( economy, member, state, turn );
			return;
		}
		RequestTemplate( member, PerformanceTemplateOf( draw, ThemeIndex, variant ) );
		member.State = Performing;
		member.WorkStamp = turn;
	}

	/// <summary>The guard's pre-update and dispatch run in <see cref="Update"/>; his state handler.</summary>
	// [BIN:TPI-EXE:0x5D3C60 guard dispatch] states 0..0x1E by the byte table 0x5D3DD8 and the jump table 0x5D3DB4: 0 0x5D3D7B, 1 0x5D3CEF, 2 0x629EF0 and 3 0x62A1C0 (the rest area's), 0x1A the chase
	// 0x5D4100, 0x1C the carry 0x5D4620, 0x1D 0x5D3CA0, 0x1E the first aid 0x5D43D0 (not ported, see the class remarks), every other state the common handler 0x6287A0 (training 7 and 8)
	private void DispatchGuard( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		switch ( member.State )
		{
			case Idle:
				// [BIN:TPI-EXE:0x5D3D7B state 0] a world draw whose low nibble 0 plays the idle voice 0xA7; the idle end 0x62CA10 (the decision 0x5D3C10 through 0x62D810, kind 7)
				Voice( economy, member, GuardIdleVoice );
				if ( unchecked( (uint)turn ) > unchecked( (uint)(member.IdleStamp - IdleTerm( member.Experience, settings.MaxExperience )) ) )
					LookForWork( economy, settings, member, turn );
				break;
			case Walking:
				// [BIN:TPI-EXE:0x5D3CEF state 1] a world draw whose low nibble 0 plays the patrol voice 0xA6; the walk step 0x60B440: walking takes the moving step 0x62B3E0, arrived or stuck decides (0x5D3D2B,
				// the decision's body)
				Voice( economy, member, GuardPatrolVoice );
				if ( Walk( member ) is WalkResult.Arrived or WalkResult.Stuck )
					DecideGuard( economy, member, turn );
				else
					member.Step( settings );
				break;
			case ThemeParkIncTraining.ToTrainingRoom:
				UpdateToTrainingRoom( economy, settings, member, turn );
				break;
			case ThemeParkIncTraining.Training:
				UpdateTraining( economy, settings, member, turn );
				break;
			// [BIN:TPI-EXE:0x5D3C60 guard rest states] the byte table 0x5D3DD8 (00 01 02 03, 08 for 4..0x19 and 0x1B, 04 05 06 07 for 0x1A, 0x1C, 0x1D, 0x1E) sends 2 to the jump-table entry 2
			// (0x5D3D67 -> 0x629EF0) and 3 to entry 3 (0x5D3D71 -> 0x62A1C0), the handyman's entries for the same states; 0x23 lies above 0x1E and goes to the common handler 0x6287A0 (0x5D3DA7), whose
			// byte table 0x628B10 sends it (index 0x1F) to entry 10, 0x62897B -> 0x62CA90
			case ToRestArea:
				UpdateToRestArea( economy, settings, member, turn );
				break;
			case Resting:
				UpdateResting( economy, settings, member, turn );
				break;
			case RestSearch:
				UpdateRestSearch( economy, settings, member, turn );
				break;
			// [BIN:TPI-EXE:0x6287A0 strike states] the role's table sends 4 and 5 to the common handler: 4 -> 0x628855, 5 -> 0x628892 (ThemeParkIncStaffWorld.Strikes.cs)
			case ToStrike:
				UpdateToStrike( economy, member, turn );
				break;
			case Striking:
				UpdateStriking( economy, member, turn );
				break;
		}
	}

	/// <summary>The guard's decision (0x5D3C10, also the constructor's end and the walk's arrival): the gate, then a patrol on three turns in four, else idle.</summary>
	// [BIN:TPI-EXE:0x5D3C10 guard decision] 0x62BBA0 nonzero returns; the world turn & 3 nonzero and the patrol destination 0x609F70 set 1 through the guard's setter 0x5D3E70, else 0
	private void DecideGuard( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		if ( Gate( economy, economy.TpiStaff!.Settings, member, turn ) )
			return;
		SetGuardState( economy, member, (turn & 3) != 0 && Patrol( economy, member ) ? Walking : Idle, turn );
	}

	/// <summary>
	/// The guard's setter (0x5D3E70) for the states he enters himself: the speed bonus 0 (0x60B3F0(0)) before the common setter; state 0 also clears the prankster he holds (none in this world).
	/// </summary>
	// [BIN:TPI-EXE:0x5D3F65 guard setter] the states 1..9, 0xB..0x19 and 0x1B: 0x60B3F0(0), then 0x628180(state); state 0 (0x5D3F48): +0x5D2 = 0 then 0x628180(0). The chase, carry and first-aid states have
	// their own cases (0x5D3E90, 0x5D3ED8, 0x5D3EB4: the timers) and are not ported
	private void SetGuardState( ParkEconomy economy, ThemeParkIncStaffMember member, int state, long turn )
	{
		member.SpeedBonus = BonusTable[0];
		SetState( economy, member, state, turn );
	}

	/// <summary>
	/// The placed entertainers in the entertainers' list order (newest first), as the guests' finder <c>0x619100</c> walks them: each one's thing id, cell and whether he performs (state 0x17).
	/// </summary>
	// [BIN:TPI-EXE:0x5D1380 entertainer list] the constructor puts the entertainer at the head of the list world +0x1EAB82 (0x64F050, next +0x5D0) at his drop, which is when ThemeParkIncStaff.EndHire puts
	// the member at the front of the members: the placed entertainers of the members, in order, are the list
	// [BIN:TPI-EXE:0x614D9B performing] the guest's reaction reads the member's state word +0x1B4 against 0x17
	/// <remarks>The list is this world's own buffer, filled again by every call (the guests' sweep asks once per idle guest): read it before the next call.</remarks>
	public IReadOnlyList<ThemeParkIncEntertainerView> Entertainers( ParkEconomy? economy )
	{
		entertainerViews.Clear();
		if ( economy?.TpiStaff is not { } staff )
			return entertainerViews;
		var members = staff.Members;
		for ( var index = 0; index < members.Count; index++ )
		{
			var member = members[index];
			if ( member.Role != ThemeParkIncStaffRole.Entertainer || member.Navigation is not { } navigation || member.ThingId is not (> 0 and <= ushort.MaxValue) )
				continue;
			entertainerViews.Add( new ThemeParkIncEntertainerView( (ushort)member.ThingId, navigation.Cell, member.State == Performing ) );
		}
		return entertainerViews;
	}

	private readonly List<ThemeParkIncEntertainerView> entertainerViews = new();

	/// <summary>The entertainer's and the guard's starts at the drop (0x5D1380, 0x5D3140 with 0x5D3230): the effect on the cell, then the first search.</summary>
	// [BIN:TPI-EXE:0x5D1380 entertainer constructor] 0x626190 (kind 6), +0x5D0 = 0 and the list's head (0x64F050), category 8 and the variant on the sprite source (0x5D1000(3, 8, variant)), 0x62C970(0) (the
	// base speed), the effect (PutEffect), +0x5D4 = 0, the search 0x5D1A30, then 0x629860 (false) picks the name buffer
	// [BIN:TPI-EXE:0x5D3230 guard start] the list (0x64F180), category 0xB and the variant (0x6902F0(0xB): one world draw, taken in Construct, (R >> 2) mod the loaded banks; 0x5D1000), the effect with row 3, +0x5D2, +0x5D4, +0x5D8 and +0x5DC = 0, the advisor's message 0x3A
	// (0x64EA60, 0x636B90) and the removal message 0x21 subscribed (0x436AC0), then the decision (0x62BBA0, the patrol or idle through 0x5D3E70)
	private void StartPerformer( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		PutEffect( economy, economy.TpiStaff!.Settings, member );
		member.WorkStamp = 0;
		if ( member.Role == ThemeParkIncStaffRole.Entertainer )
			SearchEntertainer( economy, member, turn );
		else
			DecideGuard( economy, member, turn );
	}
}
