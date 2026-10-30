namespace OpenTPW;

/// <summary>Which cursor tool of the staff placement is running.</summary>
public enum StaffToolKind
{
	None,
	/// <summary>The Place tool of a hire (tp.exe CMMPlaceStaff, ctor 0x46c6d0): the candidate becomes an employee on a valid click.</summary>
	Place,
	/// <summary>The Carry tool of a pick-up (vtable 0x6fea70, ctor 0x46cbc0): an employee is in the player's hand.</summary>
	Carry
}

/// <summary>What a click of <see cref="StaffPlacement.Click"/> did.</summary>
public enum StaffClickResult
{
	/// <summary>Nothing: no tool, or an invalid cell (the tool stays).</summary>
	None,
	/// <summary>The candidate was hired and stands on the clicked cell.</summary>
	Placed,
	/// <summary>The carried employee was put down on the clicked cell.</summary>
	Dropped
}

/// <summary>
/// The cell rules of the staff placement: where a staff member may be put down and when one may be picked up. CPU-only, over the
/// park's <see cref="ParkCellMap"/>.
/// </summary>
public static class StaffPlacementRules
{
	// [BIN:TPW-EXE:0x46cc40 drop validity] the cell is not unowned (flag 0x40 clear) and its type is 0 (grass), 1 (path), 3 (queue) or 9 (queue end); the place tool's click 0x46c8e0 uses the same set
	/// <summary>Cell types a member may stand on after a drop: grass, path, queue, queue end.</summary>
	public static bool IsDropCellType( byte type ) => type is 0 or 1 or 3 or 9;

	// [BIN:TPW-EXE:0x4fa990 pick-up cell types] CanBePickedUp (0x505bd0) also demands the member's own cell to be of type 0, 1, 3, 9 or 10
	/// <summary>Cell types a member may be picked up from: the drop types and type 10.</summary>
	public static bool IsPickUpCellType( byte type ) => type is 0 or 1 or 3 or 9 or 10;

	/// <summary>
	/// A cell a staff member may be put on: inside the map, owned, of a drop type and under no ride, shop or feature footprint
	/// (<paramref name="occupied"/>: any placed object or the prototype ride).
	/// </summary>
	// [APPROX:GUEST-013] tp.exe's drop 0x46cc40 (and the place click 0x46c8e0, inlined) also refuses a cell whose track record ([0x8023a0] + 0x10ffd8 + 40 × the cell's id word, kind at +8) is of kind 11, 13, 16, 18 or 25, directly or through the parent (+0x10) of a kind 12 or 17 record (0x5363f0, 0x536450, 0x53ad30, 0x53ad60, 0x53ad90; the circuit walk 0x52a970 uses the same kind tests); OpenTPW has no track-record test (its track tiles carry map types the type test refuses) — evidence needed: the track records (TRACK-002)
	// [EXT:staff-drop-occupancy-refusal] a park that types its footprint cells (ParkCellMap.WriteFootprint, 0x528A70: 4 and 10 refuse, the entrance's 9 takes a drop) needs no object list, while a world that keeps the cells it was given (a visit, a Theme Park Inc layout) has no such types and refuses every cell under a placed object or the prototype ride by occupancy instead (user-approved 2026-10-18)
	public static bool IsValidDropCell( ParkCellMap cells, int x, int y, Func<int, int, bool>? occupied )
	{
		if ( !cells.InBounds( x, y ) || (cells.FlagsAt( x, y ) & ParkCellFlags.Unowned) != 0 )
			return false;
		return IsDropCellType( cells.RawTypeAt( x, y ) ) && (occupied == null || !occupied( x, y ));
	}

	/// <summary>The cell a positioned member stands in, or null without a position.</summary>
	public static (int X, int Y)? CellOf( StaffMember member ) =>
		member.CellX is { } x && member.CellY is { } y ? ((int)MathF.Floor( x ), (int)MathF.Floor( y )) : null;

	/// <summary>
	/// The pick-up button's enable rule (tp.exe <c>CanBePickedUp</c> 0x505bd0): refused while the member is on strike or walking to one
	/// (states 4 and 5), already picked up (7), repairing a ride (13) or escorting (18), and when the member's own cell is not of a pick-up type.
	/// </summary>
	// [BIN:TPW-EXE:0x505bd0 CanBePickedUp] the state word +0x19c: 4, 5 (jump table 0x505c0c), 7 (0x505c2c), 13 and 18 refuse, then the cell test 0x4fa990; 13 is the
	// mechanic's repair (0x4da370(0xd), entered from 0x4da740), OpenTPW's StaffJob.Repairing
	public static bool CanBePickedUp( StaffMember member, ParkCellMap cells )
	{
		if ( member.State is StaffState.OnStrike or StaffState.PickedUp
			|| member.Job is StaffJob.ToStrike or StaffJob.Striking or StaffJob.PickedUp or StaffJob.Repairing or StaffJob.Escorting )
			return false;
		return CellOf( member ) is { } cell && cells.InBounds( cell.X, cell.Y ) && IsPickUpCellType( cells.RawTypeAt( cell.X, cell.Y ) );
	}
}

/// <summary>
/// Hiring places and the pick-up and drop of staff (tp.exe <c>0x507bf0</c> hire, <c>0x505c50</c> pick-up, <c>0x509af0</c> drop; docs/STAFF-PLACEMENT.md).
/// A hire starts the Place tool with the candidate still in the pool: the employee exists only after a valid click. A pick-up starts the Carry tool
/// with the member in <see cref="StaffState.PickedUp"/>. Only one tool runs at a time; a new one first ends the old one (a carried member is dropped
/// back where it was). The state machines (<see cref="StaffWorldSimulation"/>) pick the placed or dropped member up from its fields.
/// </summary>
public sealed class StaffPlacement
{
	private readonly Func<ParkEconomy?> economy;
	private readonly Func<ParkCellMap?> cells;
	private readonly Func<int, int, bool> occupied;
	private readonly Func<ParkCommand, ParkCommandResult> issue;
	private readonly Func<int, bool> isCarried;
	private readonly Func<int, (int Staff, int X, int Y)?>? handOf;
	private readonly Func<int> player;
	private readonly Func<bool>? guestExit;

	/// <param name="issue">[EXT:online-coop-commands] Hands the tool's writes to the park's command funnel: the hire, the placement, the pick-up and the drop are commands.</param>
	/// <param name="isCarried">Whether an employee is in a player's hand (<see cref="StaffHands.IsCarried"/>): <see cref="Reconcile"/> leaves those picked up.</param>
	/// <param name="handOf">[EXT:online-coop-commands] The employee a player holds (<see cref="StaffHands.Of"/>), which a Carry tool follows (<see cref="FollowPark"/>); null follows no hand.</param>
	/// <param name="player">[EXT:online-coop-commands] This machine's player (<see cref="ParkCommands.Player"/>), whose hand and pending hire the tool is; seat 0 when null.</param>
	/// <param name="guestExit">
	/// [EXT:online-coop-commands] A guest's exit (C4c-2): true when the guest's staff controller took the exit (<see cref="CoopGuestStaff.LetGo"/>), so <see cref="Cancel"/> sends nothing itself;
	/// null or false where the tool sends its own drop back or cancel.
	/// </param>
	public StaffPlacement( Func<ParkEconomy?> economy, Func<ParkCellMap?> cells, Func<int, int, bool> occupied, Func<ParkCommand, ParkCommandResult> issue, Func<int, bool> isCarried,
		Func<int, (int Staff, int X, int Y)?>? handOf = null, Func<int>? player = null, Func<bool>? guestExit = null )
	{
		this.economy = economy;
		this.cells = cells;
		this.occupied = occupied;
		this.issue = issue ?? throw new ArgumentNullException( nameof( issue ) );
		this.isCarried = isCarried ?? throw new ArgumentNullException( nameof( isCarried ) );
		this.handOf = handOf;
		this.player = player ?? (() => ParkCommands.LocalPlayer);
		this.guestExit = guestExit;
	}

	public StaffToolKind Kind { get; private set; }
	public bool IsActive => Kind != StaffToolKind.None;
	/// <summary>The candidate being placed (Place tool).</summary>
	public StaffCandidate? Candidate { get; private set; }
	/// <summary>The member in hand (Carry tool).</summary>
	public StaffMember? Carried { get; private set; }
	/// <summary>The cell a carried member was picked up from.</summary>
	public (int X, int Y)? Origin { get; private set; }
	/// <summary>The cell under the cursor, null while the cursor is outside the park view.</summary>
	public (int X, int Y)? HoverCell { get; private set; }
	/// <summary>
	/// The exact point under the pointer in cell units (0.5 = a cell's centre), refreshed every frame while a tool runs, also over the HUD; null when the pointer
	/// is outside the window or misses the ground. The dangling sprite hangs here, not at the hovered cell's centre.
	/// </summary>
	// [BIN:TPW-EXE:0x46c480 tool frame] every frame the tool draws its sprite (0x475480) at the world point under the mouse (0x45d610 → 0x7a1c90: x, y + 5, z), whatever is under the pointer
	public (float X, float Y)? Cursor { get; private set; }
	/// <summary>Whether a click on <see cref="HoverCell"/> would work.</summary>
	public bool HoverValid { get; private set; }
	/// <summary>The member placed or dropped by the last <see cref="Click"/>.</summary>
	public StaffMember? LastPlaced { get; private set; }
	/// <summary>Raised whenever the tool ends (a click that finished it, a cancel, an abandon).</summary>
	public event Action? Ended;

	/// <summary>The role being placed or carried.</summary>
	public StaffType? ToolType => Kind switch { StaffToolKind.Place => Candidate?.Type, StaffToolKind.Carry => Carried?.Type, _ => null };

	/// <summary>Key of the sprite bank of the person in the tool (<see cref="StaffSpriteRules.VariantKey"/>).</summary>
	public int ToolVariantKey => Kind switch
	{
		StaffToolKind.Place when Candidate is { } candidate => StaffSpriteRules.KeyOf( candidate ),
		StaffToolKind.Carry when Carried is { } member => StaffSpriteRules.KeyOf( member ),
		_ => 0
	};

	/// <summary>
	/// A hire: starts the Place tool for a pool candidate, which stays pending (not hired, not expiring) until a valid click. False when the
	/// park cannot hire it. A running tool ends first.
	/// </summary>
	// [BIN:TPW-EXE:0x507bf0 hire] the hire creates no object: it builds the CMMPlaceStaff tool (0x46c6d0), makes it current (0x46c350) and flags the candidate as being placed (+0xb)
	public bool BeginHire( int candidateId )
	{
		if ( economy() is not { IsBankrupt: false } park || park.Staff.Candidates.FirstOrDefault( item => item.Id == candidateId ) is not { } candidate
			|| !park.Staff.CanHire( candidate.Type ) )
			return false;
		// [EXT:online-coop-commands] a candidate another seat places refuses the hire before the running tool ends, as the hire's record would refuse it (C4c), so a refused hire keeps the
		// employee in hand or the pending hire this tool had
		if ( park.Staff.ReservedBy( candidate.Id ) is { } seat && seat != player() )
			return false;
		Cancel();
		// [EXT:online-coop-commands] the pending flag is set by the command
		if ( !issue( new BeginStaffHireCommand( candidate.Id ) ).Accepted )
			return false;
		Kind = StaffToolKind.Place;
		Candidate = candidate;
		HoverCell = null;
		HoverValid = false;
		Cursor = null;
		return true;
	}

	/// <summary>
	/// The pick-up button: puts <paramref name="member"/> in the player's hand when <see cref="StaffPlacementRules.CanBePickedUp"/> allows. A member
	/// already in hand is refused; a running tool ends first (a carried member goes back where it was).
	/// </summary>
	// [BIN:TPW-EXE:0x505c50 pick-up] an active carry tool is replaced first (the previous member drops back where it was); SetState(7) activates the carry tool, cursor 9
	public bool PickUp( StaffMember member )
	{
		if ( economy() is not { } park || cells() is not { } map || !park.Staff.Members.Contains( member ) || !StaffPlacementRules.CanBePickedUp( member, map ) )
			return false;
		var origin = StaffPlacementRules.CellOf( member )!.Value;
		Cancel();
		// [EXT:online-coop-commands] the pick-up's writes (the jobs released, the picked-up state) are the command's
		if ( !issue( new PickUpStaffCommand( member.Id ) ).Accepted )
			return false;
		Kind = StaffToolKind.Carry;
		Carried = member;
		Origin = origin;
		HoverCell = null;
		HoverValid = false;
		Cursor = null;
		return true;
	}

	/// <summary>Sets the exact pointer position in cell units (null: no ground under the pointer).</summary>
	public void SetCursor( float? x, float? y ) => Cursor = IsActive && x is { } cellX && y is { } cellY ? (cellX, cellY) : null;

	/// <summary>Cursor move: the hovered cell and whether it is valid; null leaves the park view.</summary>
	public void Hover( int? x, int? y )
	{
		if ( !IsActive || x is not { } cellX || y is not { } cellY )
		{
			HoverCell = null;
			HoverValid = false;
			return;
		}
		HoverCell = (cellX, cellY);
		HoverValid = IsValid( cellX, cellY );
	}

	/// <summary>Whether a member may be put on the cell now.</summary>
	public bool IsValid( int x, int y ) => cells() is { } map && StaffPlacementRules.IsValidDropCell( map, x, y, occupied );

	/// <summary>
	/// A click on a park cell. A valid cell completes the tool (hires and places the candidate, or drops the carried member there); an invalid one
	/// does nothing and the tool stays (the original only logs "Cannot place staff member here").
	/// </summary>
	// [BIN:TPW-EXE:0x46c8e0 place click] a valid cell creates the member by its role constructor at the cell centre, then back to the default tool; [BIN:TPW-EXE:0x46cc40 drop click] the carry tool's drop, same validity
	public StaffClickResult Click( int x, int y )
	{
		if ( !IsActive || economy() is not { } park )
			return StaffClickResult.None;
		if ( !IsValid( x, y ) )
			return StaffClickResult.None;
		var kind = Kind;
		StaffMember member;
		if ( kind == StaffToolKind.Place )
		{
			var candidate = Candidate!;
			if ( park.IsBankrupt || park.Staff.Candidates.All( item => item.Id != candidate.Id ) || !park.Staff.CanHire( candidate.Type ) )
			{
				Cancel();
				return StaffClickResult.None;
			}
			// [EXT:online-coop-commands] the hire and the placement are the command's (PlaceStaffCommand); one it refuses (a full thing pool) ends the tool
			if ( issue( new PlaceStaffCommand( candidate.Id, x, y ) ).Value is not StaffMember hired )
			{
				Cancel();
				return StaffClickResult.None;
			}
			member = hired;
		}
		else
		{
			if ( !issue( new DropStaffCommand( Carried!.Id, x, y ) ).Accepted )
				return StaffClickResult.None;
			member = Carried!;
		}
		LastPlaced = member;
		End();
		return kind == StaffToolKind.Place ? StaffClickResult.Placed : StaffClickResult.Dropped;
	}

	/// <summary>
	/// Right click or Escape: a Place tool hires nobody and the candidate stays in the pool; a Carry tool puts the member back on the cell it came from.
	/// </summary>
	// [BIN:TPW-EXE:0x46c890 tool exit] cancelled → 0x5083b0(1) only clears the pending flag; [BIN:TPW-EXE:0x46cdc0 carry exit] a member still in hand "drops where he was before he was picked up"
	public void Cancel()
	{
		// [EXT:online-coop-commands] a guest's exit is its staff controller's, whoever ends the tool (C4c-2): the tool ends here without a command, also while no tool is on yet and a hire or a
		// pick-up waits for the host, and the controller cleans the seat's state up after its open request, so no caller sends a second staff request or cancels a newly accepted hire
		if ( guestExit?.Invoke() == true )
		{
			End();
			return;
		}
		// [EXT:online-coop-commands] the drop back and the placement's end are commands
		if ( Kind == StaffToolKind.Carry && Carried is { } member && economy() is not null && Origin is { } origin )
			issue( new DropStaffCommand( member.Id, origin.X, origin.Y ) );
		else if ( Kind == StaffToolKind.Place && economy() is not null )
			issue( new CancelStaffHireCommand() );
		End();
	}

	/// <summary>
	/// A park save: a member in hand is saved dropped on its old cell, a pending hire is not saved (the candidate stays in the pool).
	/// </summary>
	// [BIN:TPW-EXE:0x516c80 world save] before its first field the save makes a new default tool current (0x46c350 at 0x516d13), whose switch runs the old tool's exit
	// (vtable slot +0x2c, 0x46c378): the carry tool's exit 0x46cdc0 drops the member where he was (0x509af0 → 0x505ea0) and clears the pool's mStaffMemberPickedUp
	public void PrepareForSave() => Cancel();

	/// <summary>Ends a tool without touching a member (the economy was replaced by a load).</summary>
	public void Abandon()
	{
		Kind = StaffToolKind.None;
		Candidate = null;
		Carried = null;
		Origin = null;
		HoverCell = null;
		HoverValid = false;
		Cursor = null;
		Ended?.Invoke();
	}

	/// <summary>
	/// [EXT:online-coop-commands] Arms the Place tool for <paramref name="candidate"/>, the pending hire the player's seat holds in the park, without a command (C4c-2): a guest's tool follows the
	/// hire its request made. A running tool gives way without its exit.
	/// </summary>
	internal void ArmPlace( StaffCandidate candidate )
	{
		ArgumentNullException.ThrowIfNull( candidate );
		Arm( StaffToolKind.Place, candidate, null, null );
	}

	/// <summary>
	/// [EXT:online-coop-commands] Arms the Carry tool for <paramref name="member"/>, in the player's hand in the park since it was picked up at <paramref name="origin"/>, without a command
	/// (C4c-2): a guest's tool follows the pick-up its request made. A running tool gives way without its exit.
	/// </summary>
	internal void ArmCarry( StaffMember member, (int X, int Y) origin )
	{
		ArgumentNullException.ThrowIfNull( member );
		Arm( StaffToolKind.Carry, null, member, origin );
	}

	private void Arm( StaffToolKind kind, StaffCandidate? candidate, StaffMember? member, (int X, int Y)? origin )
	{
		Kind = kind;
		Candidate = candidate;
		Carried = member;
		Origin = origin;
		HoverCell = null;
		HoverValid = false;
		Cursor = null;
	}

	/// <summary>
	/// [EXT:online-coop-commands] Ends the tool without a command and without a guest's exit (C4c-2): a guest's tool whose seat's state ended, or whose state is cleaned up instead.
	/// </summary>
	internal void Disarm() => End();

	/// <summary>
	/// [EXT:online-coop-commands] The tool follows the park (C4c): a Carry tool whose player's hand no longer holds its employee (dismissed from its window, released), or a Place tool whose
	/// player's pending hire is no longer its candidate, ends without a command. The HUD runs it every frame, outside the simulation tick, so a paused park runs it too.
	/// </summary>
	public void FollowPark()
	{
		if ( Kind == StaffToolKind.Carry && handOf != null && (handOf( player() ) is not { } hand || hand.Staff != Carried?.Id) )
			End();
		else if ( Kind == StaffToolKind.Place && (economy() is not { } park || park.Staff.PendingHireOf( player() ) != Candidate?.Id) )
			End();
	}

	/// <summary>
	/// A saved member still in state <see cref="StaffState.PickedUp"/> with no player carrying it (an imported original save: nothing re-creates
	/// the carry tool) is put back to <see cref="StaffState.Idle"/>, so the gate spawn and the state machines take it over. A member a
	/// <see cref="PickUpStaffCommand"/> put in a hand is the level's state, not the tool's, so a replayed or a peer's pick-up stays picked up.
	/// </summary>
	public void Reconcile()
	{
		if ( economy() is not { } park )
			return;
		var members = park.Staff.Members;
		for ( var index = 0; index < members.Count; index++ )
		{
			var member = members[index];
			if ( member.State == StaffState.PickedUp && !isCarried( member.Id ) )
				member.State = StaffState.Idle;
		}
	}

	private void End()
	{
		var wasActive = IsActive;
		Kind = StaffToolKind.None;
		Candidate = null;
		Carried = null;
		Origin = null;
		HoverCell = null;
		HoverValid = false;
		Cursor = null;
		if ( wasActive )
			Ended?.Invoke();
	}

	/// <summary>
	/// The drop (tp.exe <c>0x509af0</c> → <c>0x505ea0</c>): the cell centre, no steering state (the state machines place it again), idle, and no
	/// job fields. The patrol rectangle is never touched.
	/// </summary>
	// [BIN:TPW-EXE:0x505ea0 drop] teleport to the cell centre (0x4fa930), SetState(0) idle and +0x200 = 0 (idle stamp reset); the pool's +0x60 is cleared
	internal static void PutDown( StaffMember member, int x, int y )
	{
		member.CellX = x + 0.5f;
		member.CellY = y + 0.5f;
		member.TravelSeconds = 0;
		member.Navigation = null;
		member.State = StaffState.Idle;
		member.SpeedBonus = 0;
		member.Job = StaffJob.Idle;
		member.IdleStamp = 0;
		member.OffGridLeg.Clear();
	}

	/// <summary>
	/// Clears the job a member holds: a handyman's litter and toilet claims, a mechanic's ride assignment (so the dispatch runs again).
	/// </summary>
	// [BIN:TPW-EXE:0x4dad20 drop mechanic] a dropped mechanic loses its ride assignment, then 0x4da5b0 dispatches again; the handyman's LitterCell and ToiletId are cleared (OffGridLeg is the road leg outside the park)
	internal static void ReleaseAssignments( ParkEconomy park, StaffMember member )
	{
		if ( member.LitterCell is { } litter )
		{
			if ( park.Litter.CollectorAt( litter.X, litter.Y ) == member.Id )
				park.Litter.Claim( litter.X, litter.Y, 0, 0 );
			member.LitterCell = null;
		}
		if ( member.ToiletId != 0 )
		{
			if ( park.TryGetObject( member.ToiletId, out var toilet ) && toilet.MechanicId == member.Id )
				toilet.MechanicId = 0;
			member.ToiletId = 0;
		}
		if ( member.AssignedInstanceId != 0 )
		{
			if ( park.TryGetObject( member.AssignedInstanceId, out var job ) && job.MechanicId == member.Id )
				job.MechanicId = 0;
			member.AssignedInstanceId = 0;
		}
		// a world mechanic's ride (+0x218) and its claim go the same way
		if ( member.RideId != 0 )
		{
			if ( park.TryGetObject( member.RideId, out var ride ) && ride.MechanicId == member.Id )
				ride.MechanicId = 0;
			member.RideId = 0;
		}
		member.RestAreaId = 0;
		member.BusyTicks = 0;
		member.AwaitingJobArrival = false;
	}
}
