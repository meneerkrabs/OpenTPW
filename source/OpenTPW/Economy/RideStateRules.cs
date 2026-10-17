namespace OpenTPW;

/// <summary>
/// The values of a ride object's state word (<c>CObject +408</c>, set by <c>CObject::SetState</c> <c>0xe0c3c</c>; docs/reverse/ORACLE.md F15 and F27).
/// OpenTPW keeps no such word: the bridge, the economy and the park door derive the state from what they have (the script's
/// <c>VAR_BROKEN</c>, the economy's breakdown flag and pending upgrade).
/// </summary>
public static class RideObjectStates
{
	/// <summary>Idle: the ride update runs the admission (<c>0xe1864</c>). Built for attractions (<c>0xdad24</c>) and set again by a repair.</summary>
	public const int Idle = 0;
	/// <summary>Broken down: the script reported <c>VAR_BROKEN</c> and the life gauge is not 0 (<c>0xe1864</c> → <c>SetState(1)</c>). Writes the open word and VAR_RIDECLOSED.</summary>
	public const int BrokenDown = 1;
	/// <summary>Waiting for or receiving an upgrade (<c>0xdf928</c> → <c>SetState(2)</c>). Writes the open word and VAR_RIDECLOSED.</summary>
	public const int Upgrading = 2;
	/// <summary>Not an attraction (type <c>+46</c> bit 0x4 clear, <c>0xdad34</c>): no admission, no wear, never opened by the door.</summary>
	public const int NotAnAttraction = 3;
	/// <summary>Condemned: the script reported <c>VAR_BROKEN</c> and the life gauge truncates to a zero byte (<c>0xe1864</c> → <c>SetState(4)</c>).</summary>
	public const int Condemned = 4;
}

/// <summary>
/// Whether a ride may be opened (<c>0xde654</c>, ORACLE.md F27, ECON-049): the guard of the park door's opening walk
/// (<c>0x108f40</c>) and of the open buttons. It reads the ride's state, not <c>VAR_BROKEN</c>.
/// </summary>
public static class RideOpening
{
	/// <summary>The ride's track type of a coaster (<c>Bumper.WhichTrackType</c> 3, <c>0xe2fe4</c>).</summary>
	public const int CoasterTrackType = 3;

	/// <summary>
	/// <c>0xde654</c>: the state is not 1 (broken down), 4 (condemned) or 2 (upgrading), the maintenance flag <c>+96</c> is clear, the back of
	/// the queue still joins a path (<c>0xdd744</c>), and a coaster has a complete circuit (<c>0x45eac</c>).
	/// </summary>
	// [BIN:STP-PPC:0x100DE654 reopen guard] executed by the oracle target ride-may-open (ORACLE.md F27); the four state tests are 0xdfe0c, 0xdfe20, 0xdfe88 (+96) and 0xdfe90
	public static bool MayOpen( int state, bool maintenanceFlag, bool queueConnected, int trackType, bool coasterComplete ) =>
		state != RideObjectStates.BrokenDown && state != RideObjectStates.Condemned && !maintenanceFlag && state != RideObjectStates.Upgrading
		&& queueConnected && (trackType != CoasterTrackType || coasterComplete);

	/// <summary>
	/// Whether an imported attraction is closed again: it is idle (state 0) and its saved open word (<c>mCanLoad</c>, +0x68) is clear. The broken, upgrading and condemned states clear the
	/// word themselves (SetState 0x4E0E60) and their import closes the ride already; a fixed item (state 3) is never opened or closed by the player.
	/// </summary>
	// [BIN:TPW-EXE:0x004E0E60 SetState] states 1, 2 and 4 clear the open word; the player's close and the maintenance call (0x4DFE30) clear it for an idle ride
	public static bool ClosedByImportedOpenWord( uint openWord, int state ) => openWord == 0 && state == RideObjectStates.Idle;
}

/// <summary>
/// The breakdown part of the original ride update (<c>0xe077c</c>, fragment <c>0xe0800..0xe0970</c>, ORACLE.md F27) as pure functions.
/// <see cref="ParkEconomy"/> applies them to the economy's rides; the oracle bridge calls them in the routine's order.
/// </summary>
public static class RideBreakdownRules
{
	/// <summary>The breakdown check runs when the world turn is a multiple of this (<c>0xe0838</c>: the low three bits are 0).</summary>
	public const long UpdatePeriodTurns = 8;
	/// <summary>The life gauge a breakdown takes when the wear gauge ran out first (data: TOC −11148).</summary>
	public const float LifeLoss = 5f;

	/// <summary>A ride in state 3 or 4 is not updated at all (<c>0xe0800..0xe0810</c>): no wear, no breakdown, no worn flag.</summary>
	public static bool SkipsUpdate( int state ) => state is RideObjectStates.NotAnAttraction or RideObjectStates.Condemned;

	/// <summary>
	/// Whether the wear step and the breakdown check run: not in state 1 or 3 or 4, the open word is set (a closed ride neither wears nor breaks
	/// down) and the world turn is a multiple of 8.
	/// </summary>
	public static bool RunsWear( int state, bool open, long turn ) =>
		!SkipsUpdate( state ) && state != RideObjectStates.BrokenDown && open && (turn & (UpdatePeriodTurns - 1)) == 0;

	/// <summary>
	/// <c>0xe0870..0xe0894</c>: a ride that is not a relief object (+46 bit 0x1) and whose VAR_BREAKSTAT is 0 breaks down when its life gauge
	/// (<c>+68</c>) or its wear gauge (<c>+64</c>), truncated to a byte, is 0. There is no random roll.
	/// </summary>
	public static bool BreaksDown( bool providesRelief, int breakStat, float life, float repair ) =>
		!providesRelief && breakStat == 0 && (GaugeByte( life ) == 0 || GaugeByte( repair ) == 0);

	/// <summary>
	/// The life gauge after the breakdown is announced (<c>0xe08a8..0xe0908</c>): unchanged when it is already 0, else it loses 5 (single
	/// precision) and is clamped to 0..100. NaN passes the clamps.
	/// </summary>
	public static float LifeAfterBreakdown( float life )
	{
		if ( GaugeByte( life ) == 0 )
			return life;
		var after = life + -LifeLoss;
		return after > 100f ? 100f : after < 0f ? 0f : after;
	}

	/// <summary>A gauge as the original tests it: <c>fctiwz</c>, then the low byte.</summary>
	public static int GaugeByte( float gauge ) => QueueJoinRules.Truncate( gauge ) & 0xFF;
}

/// <summary>What a repair writes (<c>0xdef2c</c>, the branch of a ride that is not being upgraded; ORACLE.md F27, ECON-024).</summary>
public static class RideRepair
{
	/// <summary>The wear gauge a repair writes into <c>+64</c>: the float at TOC −11016, 100.0 (checked in the binary).</summary>
	// [BIN:STP-PPC:0x100DEF78 repair] lfs f0,-11016(r2): 100.0; the life gauge (+68) is not touched
	public const float FullGauge = 100f;

	/// <summary>
	/// Whether the ride is open when the repair has finished: the repair opens it whatever the guard says (it only logs a warning when the guard
	/// fails), except that a coaster without a complete circuit is closed again (<c>0xdf4dc..0xdf588</c>).
	/// </summary>
	public static bool OpensAfterRepair( int trackType, bool coasterComplete ) => trackType != RideOpening.CoasterTrackType || coasterComplete;
}
