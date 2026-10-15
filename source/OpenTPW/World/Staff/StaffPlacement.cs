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
	// [APPROX:GUEST-013] the original refuses a cell whose structure record (+0 → table at 0x8023a0 + 0x10ffd8) is of kind 0xb, 0xd, 0x10, 0x12 or 0x19 (following 0xc and 0x11 to the parent record), most likely ride, shop and scenery footprints; OpenTPW refuses every cell under a placed object or the prototype ride — evidence needed: the structure-record kinds
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
	/// (states 4 and 5), already picked up (7) or escorting (18; the state 13 has no job here), and when the member's own cell is not of a pick-up type.
	/// </summary>
	// [APPROX:ECON-052] the original also refuses staff state 13 and plays script 0x14 for state 6: StaffJob has neither, so only the jobs it has are refused — evidence needed: what the staff states 6 and 13 are
	public static bool CanBePickedUp( StaffMember member, ParkCellMap cells )
	{
		if ( member.State is StaffState.OnStrike or StaffState.PickedUp
			|| member.Job is StaffJob.ToStrike or StaffJob.Striking or StaffJob.PickedUp or StaffJob.Escorting )
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

	public StaffPlacement( Func<ParkEconomy?> economy, Func<ParkCellMap?> cells, Func<int, int, bool> occupied )
	{
		this.economy = economy;
		this.cells = cells;
		this.occupied = occupied;
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
		StaffToolKind.Place when Candidate is { } candidate => StaffSpriteRules.VariantKey( candidate.NameIndex, candidate.Id ),
		StaffToolKind.Carry when Carried is { } member => StaffSpriteRules.VariantKey( member.NameIndex, member.Id ),
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
		Cancel();
		Kind = StaffToolKind.Place;
		Candidate = candidate;
		park.Staff.PendingPlacementId = candidate.Id;
		HoverCell = null;
		HoverValid = false;
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
		ReleaseAssignments( park, member );
		member.State = StaffState.PickedUp;
		// state 7 takes the thought bubble away (0x005054D0 → 0x0050C0D0)
		member.BubbleThought = 0;
		Kind = StaffToolKind.Carry;
		Carried = member;
		Origin = origin;
		HoverCell = null;
		HoverValid = false;
		return true;
	}

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
			park.Staff.PendingPlacementId = 0;
			member = park.Hire( candidate.Id );
			// [BIN:TPW-EXE:0x46c8e0 new member] position = cell centre (sub-cell 0x80), happiness and energy 100.0 (the member's own defaults), patrol rectangle = the whole map (+0x20a = 1, +0x20c = 0x4000: PatrolArea's default); each role then decides its first job (state 0 here, the state machines' search runs on the next update)
			member.PatrolArea = (0, 0, ParkLitter.MapSize - 1, ParkLitter.MapSize - 1);
		}
		else
		{
			member = Carried!;
			ReleaseAssignments( park, member );
		}
		PutDown( member, x, y );
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
		if ( Kind == StaffToolKind.Carry && Carried is { } member && economy() is { } park && Origin is { } origin )
		{
			ReleaseAssignments( park, member );
			PutDown( member, origin.X, origin.Y );
		}
		else if ( Kind == StaffToolKind.Place && economy() is { } pool )
			pool.Staff.PendingPlacementId = 0;
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
		Ended?.Invoke();
	}

	/// <summary>
	/// A saved member still in state <see cref="StaffState.PickedUp"/> with no tool carrying it (an imported original save: nothing re-creates
	/// the carry tool) is put back to <see cref="StaffState.Idle"/>, so the gate spawn and the state machines take it over.
	/// </summary>
	public void Reconcile()
	{
		if ( economy() is not { } park )
			return;
		var members = park.Staff.Members;
		for ( var index = 0; index < members.Count; index++ )
		{
			var member = members[index];
			if ( member.State == StaffState.PickedUp && !ReferenceEquals( member, Carried ) )
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
		if ( wasActive )
			Ended?.Invoke();
	}

	/// <summary>
	/// The drop (tp.exe <c>0x509af0</c> → <c>0x505ea0</c>): the cell centre, no steering state (the state machines place it again), idle, and no
	/// job fields. The patrol rectangle is never touched.
	/// </summary>
	// [BIN:TPW-EXE:0x505ea0 drop] teleport to the cell centre (0x4fa930), SetState(0) idle and +0x200 = 0 (idle stamp reset); the pool's +0x60 is cleared
	private static void PutDown( StaffMember member, int x, int y )
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
	private static void ReleaseAssignments( ParkEconomy park, StaffMember member )
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
