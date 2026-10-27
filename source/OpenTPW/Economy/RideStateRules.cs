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
/// The broken-down part of a ride object's state word (<c>CObject +0x19C</c> on the PC): 0 idle, 1 broken down, 4 condemned. One instance belongs to the ride's economy record and is shared with
/// its visitor bridge, so the ride update that enters state 1 or 4 (<c>0x4E14E0</c> through <c>SetState</c> <c>0x4E0E60</c>), the repair and every opener (<c>SetState(0)</c>) write the one word the economy,
/// the guests, the object window and the opening guard read. State 2 (a purchased upgrade not yet installed) is read from the economy's pending level and state 3 (not an attraction) from the type.
/// </summary>
// [BIN:TPW-EXE:0x004E0E60 SetState] states 0 and 3 only store the word; 1, 2 and 4 close the object first; the ride update's state 0 is the only caller of SetState(1) and SetState(4) (0x4E154E, 0x4E1590) and the state is left through SetState(0) (0x4DE487, 0x4DF438, 0x4DFCB8, 0x4E003C, 0x4E01B6)
public sealed class RideStateWord
{
	/// <summary>0 idle, 1 broken down (<see cref="RideObjectStates.BrokenDown"/>) or 4 condemned (<see cref="RideObjectStates.Condemned"/>).</summary>
	public int Value { get; private set; }

	/// <summary>Whether the ride is in state 1 or 4 (what the choice's counts-test, the admission and the opening guard refuse).</summary>
	public bool IsBroken => Value is RideObjectStates.BrokenDown or RideObjectStates.Condemned;

	/// <summary>Raised when the word changes: the old value, then the new one.</summary>
	public event Action<int, int>? Changed;

	/// <summary><c>SetState</c>: the new state (0, 1 or 4); raises <see cref="Changed"/> when the word moves.</summary>
	public void Set( int state )
	{
		if ( state is not (RideObjectStates.Idle or RideObjectStates.BrokenDown or RideObjectStates.Condemned) )
			throw new ArgumentOutOfRangeException( nameof( state ), state, "The word holds 0, 1 or 4." );
		if ( state == Value )
			return;
		var before = Value;
		Value = state;
		Changed?.Invoke( before, state );
	}

	/// <summary>Stores a state a save or a snapshot holds, without the change event.</summary>
	public void Restore( int state )
	{
		if ( state is not (RideObjectStates.Idle or RideObjectStates.BrokenDown or RideObjectStates.Condemned) )
			throw new ArgumentOutOfRangeException( nameof( state ), state, "The word holds 0, 1 or 4." );
		Value = state;
	}

	/// <summary>The state the ride update's <c>0x4E14E0</c> enters for a life gauge: 4 when its byte is 0, else 1.</summary>
	public static int BrokenStateFor( double lifeGauge ) => RideBreakdownRules.GaugeByte( (float)lifeGauge ) == 0 ? RideObjectStates.Condemned : RideObjectStates.BrokenDown;
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

/// <summary>How far the service an upgrading or called object waits for has come, as the status banner reads it (<c>0x485f60</c>).</summary>
public enum RideServiceProgress
{
	/// <summary>Nobody of the role is hired (world <c>+0x1da73e</c> mechanics, <c>+0x1da73c</c> handymen).</summary>
	NoneHired,
	/// <summary>The role is on strike (<c>0x509990</c>).</summary>
	OnStrike,
	/// <summary>An employee has claimed the job (<c>+0x5e</c>) and is on the way.</summary>
	OnTheWay,
	/// <summary>The claimant works on the object (a mechanic in state 13 targeting it, a handyman in state 11).</summary>
	AtWork,
	/// <summary>Waiting for an employee.</summary>
	Waiting,
}

/// <summary>What the object window's status banner reads of an object (<c>0x485f60</c> with mode 2).</summary>
/// <param name="State">The ride state word (<see cref="RideObjectStates"/>).</param>
/// <param name="TrackIncomplete">A track ride whose track is not valid (<c>+0x2c</c>), or a coaster without a complete, closed circuit (<c>0x441910</c>).</param>
/// <param name="TrackInvalid">A coaster whose track crosses itself (controller <c>+0x140</c>, <c>0x441940</c>).</param>
/// <param name="ServiceCalled">The player called a mechanic or a handyman (<c>+0x64</c>).</param>
/// <param name="Relief">A relief object (a toilet, <c>+0x32</c> bit 0): its called service is a cleaning, not a repair.</param>
/// <param name="Service">How far the upgrade's or the call's service has come.</param>
/// <param name="QueueDetached">The object has a queue (<c>+0x32</c> bit 3) whose back no longer joins a path (<c>0x4de4a0</c>).</param>
/// <param name="ExitDetached">A ride that is no add-on whose exit leads nowhere (<c>0x4de5a0</c>).</param>
/// <param name="IsOpen">The open word (<c>+0x68</c>).</param>
public readonly record struct RideStatusFacts( int State, bool TrackIncomplete, bool TrackInvalid, bool ServiceCalled, bool Relief, RideServiceProgress Service,
	bool QueueDetached, bool ExitDetached, bool IsOpen );

/// <summary>
/// The status banner of the ride, shop and sideshow windows (<c>0x485f60</c> with mode 2): the first status that applies, as its UITEXT entry (table
/// <c>0x74fbc0</c> holds the 1-based ids), or none for an open object with nothing to report. The window shows it in its "chev" control and greys the
/// door for the two track statuses (<c>0x4ad4e0</c>).
/// </summary>
// [BIN:TPW-EXE:0x485f60 object status] condemned (state 4); TRACK INCOMPLETE (a track ride's +0x2c clear, a coaster failing 0x441910); TRACK INVALID (coaster +0x140 nonzero); an upgrade (state 2) by its mechanic: none hired, on strike, claimed (on the way), at work, waiting; a called service (+0x64) the same for the mechanics (a repair) or, for a relief object, the handymen (a cleaning); broken down (state 1), or REPAIRS IN PROGRESS while the claimant works; the queue (+0x32 bit 3, 0x4de4a0) open or closed; a ride's exit (0x4de5a0); CLOSED (+0x68 clear); the "waiting to be" entries 367, 373 and 379 are never returned
public static class RideWindowStatus
{
	public static UIStrings? Of( in RideStatusFacts facts )
	{
		if ( facts.State == RideObjectStates.Condemned )
			return UIStrings.Condemned;
		if ( facts.TrackIncomplete )
			return UIStrings.TrackIncomplete;
		if ( facts.TrackInvalid )
			return UIStrings.TrackInvalid;
		if ( facts.State == RideObjectStates.Upgrading )
			return facts.Service switch
			{
				RideServiceProgress.NoneHired => UIStrings.UpgradeNoMechanics,
				RideServiceProgress.OnStrike => UIStrings.UpgradeMechanicsStrike,
				RideServiceProgress.OnTheWay => UIStrings.UpgradeMechanicOnWay,
				RideServiceProgress.AtWork => UIStrings.UpgradeInProgress,
				_ => UIStrings.UpgradeWaitingForMechanic,
			};
		if ( facts.ServiceCalled )
			return (facts.Relief, facts.Service) switch
			{
				(true, RideServiceProgress.NoneHired) => UIStrings.CleaningNoCleaners,
				(true, RideServiceProgress.OnStrike) => UIStrings.CleaningCleanersStrike,
				(true, RideServiceProgress.OnTheWay) => UIStrings.CleaningCleanerOnWay,
				(true, RideServiceProgress.AtWork) => UIStrings.CleaningInProgress,
				(true, _) => UIStrings.CleaningWaitingForCleaner,
				(false, RideServiceProgress.NoneHired) => UIStrings.RepairNoMechanics,
				(false, RideServiceProgress.OnStrike) => UIStrings.RepairMechanicsStrike,
				(false, RideServiceProgress.OnTheWay) => UIStrings.RepairMechanicOnWay,
				(false, RideServiceProgress.AtWork) => UIStrings.RepairsInProgress,
				_ => UIStrings.RepairWaitingForMechanic,
			};
		if ( facts.State == RideObjectStates.BrokenDown )
			return facts.Service == RideServiceProgress.AtWork ? UIStrings.RepairsInProgress : UIStrings.BrokenDown;
		if ( facts.QueueDetached )
			return facts.IsOpen ? UIStrings.QueueNotConnected : UIStrings.ClosedQueueNotConnected;
		if ( facts.ExitDetached )
			return UIStrings.ExitNotConnected;
		return facts.IsOpen ? null : UIStrings.Closed;
	}

	/// <summary>Whether the window greys the door for <paramref name="status"/> whatever the open word (<c>0x4ad4e0</c>: the two track statuses).</summary>
	public static bool LocksDoor( UIStrings? status ) => status is UIStrings.TrackIncomplete or UIStrings.TrackInvalid;
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
