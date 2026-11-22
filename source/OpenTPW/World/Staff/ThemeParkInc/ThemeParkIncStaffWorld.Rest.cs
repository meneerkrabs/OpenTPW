namespace OpenTPW;

/// <summary>
/// Theme Park Inc's rest areas (docs/reverse/TPI-STAFF.md §13): the finder, the tired gate's and the slack test's walk to a staff room, the arrival with its capacity, the rest, its end, the search
/// beside a full room, the low-motivation rest and a rest area's removal. Every role runs them (the handyman's table 0x5D58C4, the researcher's 0x61B774, the mechanic's 0x5DA358, the gardener's 0x5D2B68,
/// the entertainer's 0x5D1F70 and the guard's 0x5D3DD8 send states 2 and 3 to 0x629EF0 and 0x62A1C0, the common handler 0x6287A0 sends 0x23 to 0x62CA90).
/// </summary>
public sealed partial class ThemeParkIncStaffWorld
{
	/// <summary>The rest states: walking to a rest area (2), resting in it (3), and looking for one beside a full room (0x23).</summary>
	public const int ToRestArea = 2, Resting = 3, RestSearch = 0x23;

	/// <summary>
	/// The rest-area finder (<c>0x62BA20</c>): the open rest area nearest to the member by squared cell distance, the first on a tie in the attraction list's order. Its thing, or 0.
	/// </summary>
	// [BIN:TPI-EXE:0x62BA20 rest-area finder] the list park+0x1EAB88 (next +0x14), a thing with the flag +0x42 bit 2 (the attraction builder sets it from the type record's +0x1B0, UsageInfo.ChillsYouOut,
	// 0x5DB6EA) and 0x5E3C20 answering 0 (its +0x7C, mCanLoad, the open word, nonzero: the constructor sets it to 1 at 0x5DB5FB and closes only rides, shops and sideshows); dx and dy are the absolute
	// differences of the cell words' columns and rows ((w - 1) & 0x7F and (w - 1) >> 7, from the bytes +5 and +7), the key dx² + dy² compared unsigned with jae against the best so far (0xFFFFFFFF to
	// start), so the first of equal keys stays; no kind, capacity or route test. It logs "Looking for rest area..." (0x4D15C0, a debug notice)
	internal ushort FindRestArea( ThemeParkIncStaffMember member )
	{
		if ( Rooms is not { } rooms )
			return 0;
		var (x, y) = member.Navigation!.Cell;
		ushort best = 0;
		var key = uint.MaxValue;
		foreach ( var area in rooms.RestAreas() )
		{
			if ( !area.CanLoad )
				continue;
			var dx = Math.Abs( area.Cell.X - x );
			var dy = Math.Abs( area.Cell.Y - y );
			var distance = unchecked( (uint)(dx * dx + dy * dy) );
			if ( distance < key )
				(best, key) = (area.Thing, distance);
		}
		return best;
	}

	/// <summary>The rest area of a thing id (its cells), or null when the list does not hold it.</summary>
	private ThemeParkIncRestArea? RestAreaOf( int thing ) =>
		Rooms?.RestAreas().Select( area => (ThemeParkIncRestArea?)area ).FirstOrDefault( area => area!.Value.Thing == thing );

	/// <summary>The staff route to the centre of the rest area's entrance cell (<c>0x62EAC0</c>, its word <c>+0x46</c>); false when it is refused or the area is gone.</summary>
	// [BIN:TPI-EXE:0x62EAC0 cell route] the cell word w becomes the bytes ((w - 1) & 0x7F, 0x80) and ((w - 1) >> 7, 0x80) for the staff route 0x62EB10
	private bool RouteToRestArea( ParkEconomy economy, ThemeParkIncStaffMember member, int thing ) =>
		RestAreaOf( thing ) is { } area && StaffRoute( economy, member, area.Entrance, GuestWander.CentreByte, GuestWander.CentreByte );

	/// <summary>
	/// The slack test of the state setter's state 1 (<c>0x62D650</c>): a member low on energy with a rest area within <c>MinDistanceForSlacking</c> (squared cells) whose entrance he can reach takes it
	/// as his rest area and walks there instead of starting his walk.
	/// </summary>
	// [BIN:TPI-EXE:0x62D650 slack test] (u8)trunc(energy) >= StaffSlackingEnergy (0xA02294, signed jl) answers 0; the finder 0x62BA20; none answers 0; dx² + dy² between the area's cell (bytes +5, +7
	// through 0x4A63F0) and the member's above MinDistanceForSlacking (0xA02298, unsigned jbe) answers 0; the staff route 0x62EB10 to the centre of the area's entrance cell: refused ("Staff: couldn't
	// get to nearest rest area...") answers 0, else +0x248 = the area ("Found a rest area!") and 1. No draw
	private bool Slack( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member )
	{
		if ( ((int)(long)member.Energy & 0xFF) >= settings.StaffSlackingEnergy )
			return false;
		var thing = FindRestArea( member );
		if ( thing == 0 || RestAreaOf( thing ) is not { } area )
			return false;
		var (x, y) = member.Navigation!.Cell;
		var (dx, dy) = (Math.Abs( area.Cell.X - x ), Math.Abs( area.Cell.Y - y ));
		if ( unchecked( (uint)(dx * dx + dy * dy) ) > unchecked( (uint)settings.MinDistanceForSlacking ) )
			return false;
		if ( !StaffRoute( economy, member, area.Entrance, GuestWander.CentreByte, GuestWander.CentreByte ) )
			return false;
		member.RestArea = thing;
		return true;
	}

	/// <summary>
	/// The tired gate's rest branch (<c>0x62BCA9</c>): the nearest rest area whose entrance the member reaches becomes his and he walks there (the gate answers 1); with none, or none reachable, a 1 in 16
	/// world draw costs happiness and a working member stops.
	/// </summary>
	// [BIN:TPI-EXE:0x62BCA9 tired gate rest] the thought 0x18 (presentation), the finder 0x62BA20; found: 0x62EAC0 to its entrance word +0x46, a route sets +0x248 ("Found a rest area!"), 0x628180(2) and
	// answers 1; a refused route ("Staff: couldn't get to nearest rest area...") and no area fall through to "Staff member couldn't find a rest area" (0x4D15A0, kind 2), a world draw whose low nibble 0
	// adds -HappyHitCosNoRestArea (0xA02258) to happiness (0x60D0D0, clamped), and a working member (display 2) is set to 0 and the gate answers 1; another goes on to the gate's end
	private bool TiredGate( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		var thing = FindRestArea( member );
		if ( thing != 0 && RouteToRestArea( economy, member, thing ) )
		{
			member.RestArea = thing;
			SetState( economy, member, ToRestArea, turn );
			return true;
		}
		if ( (economy.World.NextWord() & 0xF) == 0 )
			member.AddHappiness( -(float)settings.HappyHitNoRestArea );
		if ( DisplayCategory( member ) == 2 )
		{
			SetState( economy, member, Idle, turn );
			return true;
		}
		return false;
	}

	/// <summary>
	/// The low-motivation rest at the end of looking for work (<c>0x62D866</c>): a member whose motivation byte is below half of <c>MotivationPercentToStrike</c> and who is now working or researching walks
	/// to the nearest rest area he reaches, else stops.
	/// </summary>
	// [BIN:TPI-EXE:0x62D866 low motivation] (u8)trunc(motivation) < MotivationPercentToStrike (0xA022CC) / 2 (cltd, sub, sar: toward zero) and the display state (0x62B010) 2 or 7: the finder 0x62BA20;
	// none: 0x628180(0); found: the staff route 0x62EB10 to the centre of its entrance cell, a route sets +0x248 ("Found a rest area!") and 0x628180(2), a refused one ("Staff: couldn't get to nearest rest
	// area...") 0x628180(0). No draw and no happiness hit
	private void LowMotivationRest( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		if ( ((int)(long)member.Motivation & 0xFF) >= settings.MotivationPercentToStrike / 2 || DisplayCategory( member ) is not (2 or 7) )
			return;
		var thing = FindRestArea( member );
		if ( thing != 0 && RouteToRestArea( economy, member, thing ) )
		{
			member.RestArea = thing;
			SetState( economy, member, ToRestArea, turn );
			return;
		}
		SetState( economy, member, Idle, turn );
	}

	/// <summary>
	/// State 2's tick (<c>0x629EF0</c>): arrived at a room with space, the member takes a place (occupancy up, a customer) and rests; at a full room he steps back to its entrance and looks for another
	/// area (0x23); stuck, he loses happiness and idles.
	/// </summary>
	// [BIN:TPI-EXE:0x629EF0 walk to rest area] the walk step 0x60B440: 0 -> occ = the area's script variable 0 (0x6B0740 on +0x2C, 0x6B0C90); occ >= StaffRoomCapacity (0xA0229C, signed jge): the staff route
	// 0x62EB10 to the centre of the area's entrance cell (+0x46; its result dropped), +0x1A0 = 0, 0x628180(0x23); else variable 0 = occ + 1 (0x6B0780), 0x5E5DA0 (the area's customer counts, history
	// +0x110 and +0x1278), 0x628180(3) and message 0xF with the member and 1 to the queue 0xA09728, whose only listener (0x63A1E0 -> 0x4F8A50 -> 0x58DA50) adds him to an interface list (presentation, not
	// ported); 2 -> "Staff member couldn't get to his rest area", happiness = clamp(happiness - (float)HappyHitCosRoomInaccessible (0xA0225C)), the thought 0x1E, 0x628180(0); walking -> nothing (no
	// moving step)
	private void UpdateToRestArea( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		switch ( Walk( member ) )
		{
			case WalkResult.Arrived:
				var thing = (ushort)member.RestArea;
				var occupancy = Rooms?.Occupancy( thing ) ?? 0;
				if ( occupancy >= settings.StaffRoomCapacity )
				{
					if ( RestAreaOf( thing ) is { } area )
						StaffRoute( economy, member, area.Entrance, GuestWander.CentreByte, GuestWander.CentreByte );
					member.Navigation!.Mask = 0;
					SetState( economy, member, RestSearch, turn );
					return;
				}
				Rooms?.AddOccupancy( thing, +1 );
				Rooms?.RecordCustomer( thing );
				SetState( economy, member, Resting, turn );
				return;
			case WalkResult.Stuck:
				member.AddHappiness( -(float)settings.HappyHitRoomInaccessible );
				SetState( economy, member, Idle, turn );
				return;
		}
	}

	/// <summary>State 3's tick (<c>0x62A1C0</c>): the rest (<see cref="ThemeParkIncStaffMember.Rest"/>); once the energy byte is 100 the rest ends and the member looks for work.</summary>
	// [BIN:TPI-EXE:0x62A1C0 rest] (u8)trunc(energy) == 100 after the meters' update: the rest end 0x62C230, then 0x62D810. Nothing else ends a rest but the area's removal
	private void UpdateResting( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		if ( !member.Rest( settings ) )
			return;
		EndRest( economy, member, turn );
		LookForWork( economy, settings, member, turn );
	}

	/// <summary>The rest end (<c>0x62C230</c>): the area's occupancy falls, it is the member's no longer, he is shown again and idles.</summary>
	// [BIN:TPI-EXE:0x62C230 rest end] message 0xF with the member and 0 to the queue 0xA09728 (the interface list's removal, presentation); the area's script variable 0 -= 1 (no floor, the area's
	// thing as +0x248 names it); +0x248 = 0; 0x5D1000(3, +0x34, +0x30) makes the sprite source in template 3; 0x628180(0)
	private void EndRest( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		Rooms?.AddOccupancy( (ushort)member.RestArea, -1 );
		member.RestArea = 0;
		ShowAgain( member );
		SetState( economy, member, Idle, turn );
	}

	/// <summary>
	/// State 0x23's tick (<c>0x62CA90</c>): the nearest rest area becomes the member's; with room in it he walks to it when he can; beside a full one he stands about in his own cell; then a 1 in 16 world
	/// draw costs happiness. With no rest area at all he idles.
	/// </summary>
	// [BIN:TPI-EXE:0x62CA90 rest search] +0x248 = the finder's area; 0: 0x628180(0) and return (no draw). occ = its script variable 0; occ < StaffRoomCapacity: the finder again, 0x62EAC0 to its entrance
	// word, a route sets +0x248 ("Found a rest area!") and 0x628180(2), a refused one logs "Staff: couldn't get to nearest rest area..." and keeps the state; full: the walk step 0x60B440, and when it
	// answers 0 or 2 two world draws give the fractions of the member's own cell (the first the column's byte, the second the row's) for the staff route 0x62EB10 (its result dropped); then every branch
	// takes a world draw whose low nibble 0 adds -HappyHitCosNoRestArea (0xA02258) to happiness, clamped
	private void UpdateRestSearch( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		var thing = FindRestArea( member );
		member.RestArea = thing;
		if ( thing == 0 )
		{
			SetState( economy, member, Idle, turn );
			return;
		}
		if ( (Rooms?.Occupancy( thing ) ?? 0) < settings.StaffRoomCapacity )
		{
			var again = FindRestArea( member );
			if ( again != 0 && RouteToRestArea( economy, member, again ) )
			{
				member.RestArea = again;
				SetState( economy, member, ToRestArea, turn );
			}
		}
		else if ( Walk( member ) is WalkResult.Arrived or WalkResult.Stuck )
		{
			var cell = member.Navigation!.Cell;
			var byteX = (int)(economy.World.NextWord() & 0xFF);
			var byteY = (int)(economy.World.NextWord() & 0xFF);
			StaffRoute( economy, member, cell, byteX, byteY );
		}
		if ( (economy.World.NextWord() & 0xF) == 0 )
			member.AddHappiness( -(float)settings.HappyHitNoRestArea );
	}

	/// <summary>
	/// A thing removed (the staff message 0xA, <c>0x626638</c>): a member whose rest area it is leaves it. Resting, his rest ends and he takes the nearest other area he can reach (walking there idle);
	/// walking to it, he idles. Every member hears it in ascending thing handle order.
	/// </summary>
	// [BIN:TPI-EXE:0x626638 rest area removed] message 0xA whose word +8 is +0x248: state 3 -> the rest end 0x62C230, the finder 0x62BA20 (the removed thing is off the list by then), 0x62EAC0 to the
	// area's entrance: a route sets +0x248 ("Found a rest area!"), the state stays 0; state 2 -> +0x248 = 0 and 0x628180(0); any other state nothing. [BIN:TPI-EXE:0x436E97 message order] ascending handles
	public void RestAreaRemoved( ParkEconomy economy, ushort thing, long turn )
	{
		if ( economy.TpiStaff is not { } staff || thing == 0 )
			return;
		foreach ( var member in staff.Members.Where( member => member.Placed && member.RestArea == thing ).OrderBy( member => member.ThingId ).ToList() )
		{
			if ( member.State == Resting )
			{
				EndRest( economy, member, turn );
				var other = FindRestArea( member );
				if ( other != 0 && RouteToRestArea( economy, member, other ) )
					member.RestArea = other;
			}
			else if ( member.State == ToRestArea )
			{
				member.RestArea = 0;
				SetState( economy, member, Idle, turn );
			}
		}
	}
}
