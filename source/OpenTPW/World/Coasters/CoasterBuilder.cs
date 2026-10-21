namespace OpenTPW;

/// <summary>
/// The headless coaster build API (COAST-plan §9.2, §9.7), CPU-only like <see cref="ParkPathBuilder"/>: the level's
/// coaster tool and tests both drive it. It places pylons (a failed validation marks them invalid instead of refusing
/// them), charges each pylon at once, settles moves and height changes, closes the circuit, completes and reopens the
/// track and deletes it.
/// </summary>
public sealed class CoasterBuilder
{
	private readonly Func<ParkEconomy?> economy;
	private readonly Func<int?> economyInstance;
	private float height;

	/// <param name="economy">The park economy that pays for the track, or null (money off: the generic sandbox and tests).</param>
	/// <param name="economyInstance">The station's economy instance, whose total spent the track cost joins.</param>
	public CoasterBuilder( CoasterRide ride, CoasterSite site, Func<ParkEconomy?>? economy = null, Func<int?>? economyInstance = null )
	{
		Ride = ride ?? throw new ArgumentNullException( nameof( ride ) );
		Site = site ?? throw new ArgumentNullException( nameof( site ) );
		ride.Site = site;
		this.economy = economy ?? (() => null);
		this.economyInstance = economyInstance ?? (() => null);
		height = ride.Definition.InitialPylonLoftHeight;
	}

	public CoasterRide Ride { get; }
	public CoasterSite Site { get; }
	public CoasterTrack Track => Ride.Track;
	public ParkEconomy? Economy => economy();

	/// <summary>The ride's upgrade level (the thing's level byte +0x50), or null when no park economy tracks the station (the generic sandbox and tests).</summary>
	public int? UpgradeLevel => Economy is { } park && economyInstance() is int id && park.TryGetObject( id, out var state ) ? state.Level : null;
	private bool loopMode;
	private int loopPylonsLeft;

	/// <summary>
	/// The loop edit mode (bit 0x40): new pylons are LoopPylons, and they come in pairs: the mode begins with two pylons to place, the first gets loop
	/// rotate 0 and node flag 0x80, the second loop rotate 0.5, and the mode ends after the second (counter 0x79c664).
	/// </summary>
	// [BIN:TPW-EXE:0x442cd0 loop mode] entering loop mode sets the counter 0x79c664 to 2; every placed pylon (0x435020) decrements it and the mode (0x79c658 bit 0x40) is left at 0
	public bool LoopMode
	{
		get => loopMode;
		set
		{
			if ( value == loopMode )
				return;
			loopMode = value;
			loopPylonsLeft = value ? LoopPair : 0;
		}
	}

	/// <summary>The loop edit mode is on and the next pylon is the first of the pair (the counter 0x79c664 is still 2).</summary>
	public bool FirstOfPair => loopMode && loopPylonsLeft == LoopPair;

	/// <summary>The loop pylons the loop edit mode still places before it ends (the counter 0x79c664), 0 outside the mode.</summary>
	public int LoopPylonsLeft => loopPylonsLeft;

	/// <summary>
	/// [EXT:online-coop-commands] The tool state this machine's coaster panel keeps in the builder: the requested height and the loop edit mode with its pylons still to place. A click carries it in its
	/// command (<see cref="CoasterClickCommand"/>), so every peer places the same pylon, and the builder of a peer keeps its own.
	/// </summary>
	public CoasterToolState ToolState => new( height, loopMode, loopPylonsLeft );

	/// <summary>Takes a tool state (<see cref="ToolState"/>): a click's placement reads the issuer's, and the issuer's builder takes the state the placement left.</summary>
	internal void SetToolState( CoasterToolState state )
	{
		Height = state.Height;
		loopMode = state.LoopMode;
		loopPylonsLeft = state.LoopMode ? Math.Clamp( state.LoopPylonsLeft, 0, LoopPair ) : 0;
	}

	/// <summary>The loop button exists: the coaster has a loop pylon and its ride is upgraded to level 1 or more (a station without an economy counts as unlocked).</summary>
	// [BIN:TPW-EXE:0x004980D1 loop button] the panel enables the loop button only when the ride's level byte (thing +0x50) is at least 1 (0x4980D6); see CoasterToolPanel.EnabledMask
	public bool LoopUnlocked => Ride.Definition.HasLoopPylon && (UpgradeLevel is not int level || level >= 1);

	/// <summary>The editable chain endpoint still needs the second pylon of a loop pair, even after the loop tool has been left.</summary>
	// [BIN:TPW-EXE:0x444c00 pending loop endpoint] an incomplete track tests its tail (+0x58), a complete cut track its cut predecessor (+0x60), and a complete uncut track returns false; only node flag 0x80 is tested, independently of mode bit 0x40
	public bool LoopPairPending => (!Track.IsComplete || Track.IsCut) && Track.LastIndex >= 0
		&& (Track.Nodes[Track.LastIndex].Flags & CoasterNodeFlags.LoopFirst) != 0;

	/// <summary>Resumes the endpoint's pending second loop pylon, or starts a new pair unless loop mode is already active.</summary>
	// [BIN:TPW-EXE:0x442d10 resume loop pair] a pending endpoint sets counter 0x79c664 to 1 and mode bit 0x40; the loop selection then calls 0x442cd0, which starts at 2 only when that mode bit is clear
	internal void StartOrResumeLoopPair()
	{
		if ( LoopPairPending )
		{
			loopMode = true;
			loopPylonsLeft = 1;
		}
		else
			LoopMode = true;
	}

	/// <summary>Loop pylons a loop edit mode places before it ends.</summary>
	public const int LoopPair = 2;
	/// <summary>The requested height of the next pylon (world units above the ground; clamped when placed).</summary>
	// [DATA:coaster.sam:sCoasterType.fInitialPylonLoftHeight] the height a new pylon starts at (10 by default, 20 for several PC coasters)
	public float Height
	{
		get => height;
		set => height = float.IsFinite( value ) ? value : height;
	}

	/// <summary>The station ends of a coaster station footprint: the '&gt;' cell (depart) and the '&lt;' cell (return).</summary>
	public static ((int X, int Y) Depart, (int X, int Y) Return)? StationEnds( ObjectShape shape, int anchorX, int anchorY, int rotation )
	{
		(int X, int Y)? depart = null, back = null;
		foreach ( var (x, y, cell) in ObjectFootprint.GetCells( shape, anchorX, anchorY, rotation ) )
		{
			if ( cell.Symbol == '>' )
				depart = (x, y);
			else if ( cell.Symbol == '<' )
				back = (x, y);
		}
		return depart is { } d && back is { } r ? (d, r) : null;
	}

	/// <summary>
	/// Starts the track at a station (removing any pylons): its three station nodes stand where the station model's dummies are (<paramref name="layout"/>),
	/// or in the cells beyond the '&gt;' and '&lt;' cells at the coaster's initial pylon loft height when the model is not at hand.
	/// </summary>
	public void SetStation( (int X, int Y) depart, (int X, int Y) @return, CoasterStationLayout? layout = null )
	{
		Ride.ClearTrains();
		Ride.EnterState( CoasterControllerState.Incomplete );
		Track.SetStation( depart, @return, layout ?? CoasterStationLayout.FromCells( depart, @return, Site, Ride.Definition.InitialPylonLoftHeight ) );
	}

	/// <summary>What the validator says about a pylon on (x, y) after the last node now (no writes).</summary>
	public CoasterPlaceResult Check( int x, int y ) => Track.CheckPlacement( Site, Ride.World, Ride, x, y, LoopMode );

	/// <summary>The float cost a pylon on (x, y) would have (what placing it charges, truncated).</summary>
	public float PreviewCost( int x, int y ) => Track.PreviewCost( x, y, Height, LoopMode );

	/// <summary>The build-cost display change of moving the cursor from one candidate cost to another: ceil(new) − ceil(old).</summary>
	// [BIN:STP-PPC:0x100483AC build cursor] the accumulator data:0x84ac4 += ceil(new) − ceil(old) for the cost display and the tool's affordability check (0x1006F664); the coaster code never debits it
	public static long PreviewDelta( float oldCost, float newCost ) => CoasterTrack.RoundUp( newCost ) - CoasterTrack.RoundUp( oldCost );

	/// <summary>
	/// A click in the build tool (<c>0x375c4</c>): on the open start of a chain (the station's end cells) it closes the
	/// circuit; otherwise it adds a pylon at <see cref="Height"/> and charges trunc(cost) at once. A pylon that fails
	/// the validator is still placed and marked invalid (with its predecessor); only structural refusals (outside the
	/// map, a complete or closed track, no loop pylon) leave the track unchanged. The charge is debited even when the
	/// balance cannot cover it. Returns the validator's verdict.
	/// </summary>
	// [BIN:STP-PPC:0x100375C4 add node] a click on a cell holding the chain's open start closes the circuit (0x100470C4); otherwise a node is created (0x10035FDC)
	// [BIN:STP-PPC:0x10037888 place charge] placing a pylon debits trunc(cost(node, unit charged)) at once (0x101C3FBC truncates); the balance is read (0x100CBF48 at 0x100378A0) and ignored, the debit 0x100CBFDC (0x100378C0) is unconditional; all coaster charges are skipped while money is off
	public CoasterPlaceResult Place( int x, int y )
	{
		if ( Track.IsChainStart( x, y ) && !Track.IsClosed && (!Track.IsComplete || Track.IsCut) )
			return CloseCircuit();
		var result = Check( x, y );
		if ( result is CoasterPlaceResult.OutsideMap or CoasterPlaceResult.NotEditable or CoasterPlaceResult.NoLoopPylon )
			return result;
		// [BIN:TPW-EXE:0x435020 place debit] the PC coaster click path (0x524960 → 0x4419b0 → 0x435020) reads the balance (0x6ad810) and ignores it, then debits ftol(cost) through 0x4d01f0 unless a layout replays (0x785a2c, the replay word set by 0x404140, always 0 here; "money off" is the bank's withdrawals flag +0x114 inside 0x4d01f0): there is no balance comparison on the path, so nothing here refuses a pylon for money
		Spend( CoasterTrack.Truncate( PreviewCost( x, y ) ), force: true );
		var secondOfPair = LoopMode && loopPylonsLeft == 1;
		Track.Append( Ride.World, Ride, x, y, Height, LoopMode, secondOfPair );
		if ( LoopMode && --loopPylonsLeft <= 0 )
			LoopMode = false;
		Track.Revalidate( Site, Ride.World, Ride );
		return result;
	}

	/// <summary>
	/// A click on the chain start: closes the circuit when the closing link and every node pass the validator; a circuit the demolish button cut open joins its new nodes to the rest and is
	/// complete again (state 2, the trains come back). A closing link that fails, or a node still marked invalid, leaves the circuit open, so the track can be fixed first.
	/// </summary>
	// [BIN:TPW-EXE:0x442c70 close circuit] the click on the open chain start joins the chain and sets controller state 2 (0x435730) without a validator test
	// [EXT:coaster-completion-click] OpenTPW closes a circuit, fresh or cut, only when the closing link passes the validator and no node is marked invalid, as it completes one only then (see Complete): the original's close click tests nothing; a fresh circuit closed through a failing link could never complete, and every click on it but a move was refused
	public CoasterPlaceResult CloseCircuit()
	{
		var check = CheckClose();
		if ( check != CoasterPlaceResult.Ok )
			return check;
		if ( !Track.IsCut )
			return Close() ? CoasterPlaceResult.Ok : CoasterPlaceResult.NotEditable;
		Track.Close();
		Track.Revalidate( Site, Ride.World, Ride );
		Ride.Rebuild();
		return CoasterPlaceResult.Ok;
	}

	/// <summary>What a click on the chain start of the open circuit answers (<see cref="CloseCircuit"/>), without closing it: the closing link's check, then the invalid marks.</summary>
	public CoasterPlaceResult CheckClose()
	{
		var closure = Track.CheckClosure();
		if ( closure != CoasterPlaceResult.Ok )
			return closure;
		return Track.HasInvalidNodes ? CoasterPlaceResult.InvalidNodes : CoasterPlaceResult.Ok;
	}

	/// <summary>Closes the circuit (no node is added) without the checks of <see cref="CloseCircuit"/>.</summary>
	// [BIN:TPW-EXE:0x435020 closing click] the original has a close click only on a reopened circuit (track flag 2): a click on a cell of the chain start the cut left (+0x64) joins the chain through 0x442c70 and takes the tool out of its mode (0x442880), with no validator test; a fresh circuit has no close step, it is completed by the click of 0x524960 → 0x435570
	// [EXT:coaster-closing-check] OpenTPW keeps a close step for a fresh circuit (a click on the station's cells, [EXT:coaster-station-end-click]) and requires two pylons; CloseCircuit validates the closing link and the arrival turn by the validator's rule first (CoasterTrack.CheckClosure), so completion never hands the sampler a closing link of any length; the original completes without such a test
	internal bool Close()
	{
		if ( !Track.Close() )
			return false;
		Track.Revalidate( Site, Ride.World, Ride );
		return true;
	}

	/// <summary>
	/// Drops pylon <paramref name="index"/> on (x, y) (<c>0x4a43c</c>): the same cell changes nothing; otherwise the pick-up
	/// costs (the node and, on a complete track, the next node) are refunded truncated and the new costs charged
	/// rounded up. The validator verdict is returned; the node is marked, not refused.
	/// </summary>
	// [BIN:STP-PPC:0x1004A43C drop node] dropping on another cell credits trunc(stored node cost) [+ trunc(stored next)] and debits ceil(cost(node)) [+ ceil(cost(next))], the bracketed terms only on a complete track; the same cell (0x1004A5A8..0x1004A5BC) moves no money
	// [BIN:STP-PPC:0x1004A228 pick up node] the station node (descriptor flag 2) cannot be picked up
	// [BIN:TPW-EXE:0x446300 pick up] the pick-up excludes only descriptor flag 2 and has no rider or state test: the tool's entry has put the controller in state 2 (0x447520) and its riders are off, so an edit is never refused for riders (OpenTPW's former rider guard is gone; Level.EnterCoasterTool is the tool entry)
	public CoasterPlaceResult Move( int index, int x, int y )
	{
		if ( index < CoasterTrack.StationNodeCount || index >= Track.Nodes.Count || !Track.Nodes[index].IsPlayerPylon )
			return CoasterPlaceResult.NotEditable;
		var node = Track.Nodes[index];
		if ( (node.X, node.Y) == (x, y) )
			return CoasterPlaceResult.Ok;
		if ( x < 0 || y < 0 || x >= Site.Width || y >= Site.Height )
			return CoasterPlaceResult.OutsideMap;
		var result = Track.CheckPlacement( Site, Ride.World, Ride, x, y, node.LoopPylon, moving: index );
		var stack = Ride.World.PylonsAt( x, y ).Count( pylon => !ReferenceEquals( pylon.Node, node ) );
		Settle( index, () => Track.Relocate( index, x, y, stack, null ) );
		if ( Track.IsComplete )
			Ride.Rebuild();
		return result;
	}

	/// <summary>Releases the height slider of pylon <paramref name="index"/> (<c>0x45fc4</c>): refunds trunc(old cost), charges ceil(new cost).</summary>
	// [BIN:STP-PPC:0x10045FC4 height slider] releasing the height slider credits trunc(stored cost) and debits ceil(new cost)
	public bool SetHeight( int index, float requested )
	{
		if ( index < CoasterTrack.StationNodeCount || index >= Track.Nodes.Count || !Track.Nodes[index].IsPlayerPylon )
			return false;
		var node = Track.Nodes[index];
		var (clamped, t) = CoasterTrack.ClampHeight( Track.Definition.Pylon( node.LoopPylon ), requested );
		Settle( index, () => Track.Replace( index, node with { Height = clamped, T = t } ), includeNext: false );
		if ( Track.IsComplete )
			Ride.Rebuild();
		return true;
	}

	private void Settle( int index, Action edit, bool includeNext = true )
	{
		var next = includeNext && Track.IsComplete && index + 1 < Track.Nodes.Count ? index + 1 : (int?)null;
		// Moves and the height slider ask 0x4b168 for the cost without CostPerUnit (r4 = 0).
		var refund = CoasterTrack.Truncate( Track.NodeCost( index, chargeUnit: false ) ) + (next is int n ? CoasterTrack.Truncate( Track.NodeCost( n, chargeUnit: false ) ) : 0);
		edit();
		var charge = CoasterTrack.RoundUp( Track.NodeCost( index, chargeUnit: false ) ) + (next is int m ? CoasterTrack.RoundUp( Track.NodeCost( m, chargeUnit: false ) ) : 0);
		Refund( refund );
		Spend( charge, force: true );
		Track.Revalidate( Site, Ride.World, Ride );
	}

	/// <summary>
	/// Opens a closed, incomplete circuit whose closing link fails the validator (closed before OpenTPW checked the closing link), so building goes on from its last pylon; false when
	/// there is none.
	/// </summary>
	// [EXT:coaster-failing-closure-opens] such a circuit could never complete and took no new pylon: the tool's entry opens it again
	public bool OpenFailingClosure()
	{
		if ( !Track.IsClosed || Track.IsComplete || Track.IsCut || Track.CheckClosure() == CoasterPlaceResult.Ok )
			return false;
		Track.OpenClosure();
		Track.Revalidate( Site, Ride.World, Ride );
		return true;
	}

	/// <summary>Removes the last pylon of an open chain. Nothing is refunded and there is no undo stack.</summary>
	// [BIN:STP-PPC:0x10047170 delete nodes] deleting pylons calls no money routine; the coaster module credits only on drops and the height slider
	public bool RemoveLast()
	{
		if ( !Track.RemoveLast() )
			return false;
		Track.Revalidate( Site, Ride.World, Ride );
		return true;
	}

	/// <summary>The build-cost display: the rounded-up cost of every pylon (<c>data:0x84ac4</c>).</summary>
	public long BuildCost => Track.BuildCost;

	/// <summary>Whether completion would succeed now: the circuit is closed and no node is marked invalid.</summary>
	public CoasterPlaceResult CheckComplete() => Track.IsComplete && !Track.IsCut ? CoasterPlaceResult.NotEditable
		: !Track.IsClosed ? CoasterPlaceResult.NotClosed
		: Track.HasInvalidNodes ? CoasterPlaceResult.InvalidNodes : CoasterPlaceResult.Ok;

	/// <summary>
	/// Completes a closed track (<c>0x37ef0</c>): appends the station end nodes, then fits the curve, rates the track and
	/// places the trains. Completion moves no money.
	/// </summary>
	// [EXT:coaster-completion-click] OpenTPW completes a coaster only when its circuit is closed and no node is marked invalid (flag 0x4000), by a click on a station end after the closing click: a design decision. The original completes on one click on any cell whose type is 4, 9 or 10 (ride and queue-end cells) or whose byte +0xf has bit 0 (cell evaluator default case 0x534584..0x5345d0, click 0x524960 → 0x435570) and the completion appends the end node and sets state 2 without testing the invalid marks or the open bit; a click elsewhere adds a pylon (0x4419b0 → 0x435020), or closes a reopened circuit on its open start. Completing an unclosed or invalid track would hand the sampler and the rating a closing link of any length, which OpenTPW's curve fit does not guard against
	public CoasterPlaceResult Complete()
	{
		var check = CheckComplete();
		if ( check != CoasterPlaceResult.Ok )
			return check;
		Track.Complete();
		Ride.Rebuild();
		return CoasterPlaceResult.Ok;
	}

	/// <summary>Reopens a completed track for editing (removes the station end nodes, opens the circuit); refused while riders are on board.</summary>
	// [BIN:STP-PPC:0x10045EAC reopen guard] the Mac predicate adds riders +320 == 0 (written by passenger removal 0x3d6f8..0x3d85c); the PC predicate 0x441970 reads controller +0x140 instead, which is the self-intersection count (its only writers are 0x439c40 and 0x43ad50), so the PC has no rider test on this path: reopening takes the trains off with their riders (state 1, 0x435730 → 0x43bf20)
	public bool Reopen()
	{
		if ( !Track.IsComplete )
			return false;
		Ride.EnterState( CoasterControllerState.Incomplete );
		Track.Reopen();
		Track.Revalidate( Site, Ride.World, Ride );
		return true;
	}

	/// <summary>
	/// Cuts the nodes between two pylon nodes out of a finished circuit (the demolish button, <c>0x446740</c>): the controller goes to state 1 (the trains are removed, their riders leave), the ends of the cut
	/// are marked and building continues in the gap; a cut that reaches the end of the chain leaves an incomplete track again. Nothing is refunded.
	/// </summary>
	// [BIN:TPW-EXE:0x446740 cut range] the cut removes the nodes between the two clicked ends without a refund (0x444cb0 calls no money routine), sets controller state 1 through 0x435730 and flag 2 (a cut that reaches the last node clears flags 1 and 2), and reselects the build button
	// [BIN:TPW-EXE:0x446740 cut guard] the cut has no rider test: the tool's state 2 has already let the riders off, and the cut's own state 1 takes the trains away
	public CoasterTrack.CutResult Cut( int first, int second )
	{
		if ( !Track.IsRunnable )
			return CoasterTrack.CutResult.Refused;
		var result = Track.Cut( first, second );
		if ( result == CoasterTrack.CutResult.Refused )
			return result;
		Ride.EnterState( CoasterControllerState.Incomplete );
		Track.Revalidate( Site, Ride.World, Ride );
		Ride.RaiseTrackChanged();
		return result;
	}

	/// <summary>Swaps pylon <paramref name="index"/> with the one above or below it in its cell's stack (<c>0x4423f0</c>, <c>0x4425c0</c>); no money moves.</summary>
	public bool SwapStack( int index, bool up )
	{
		if ( !Track.SwapStack( index, up ) )
			return false;
		Track.Revalidate( Site, Ride.World, Ride );
		if ( Track.IsRunnable )
			Ride.Rebuild();
		else
			Ride.RaiseTrackChanged();
		return true;
	}

	// ---- Hand edits of the coaster tool panel (COAST-028) --------------------------------------------

	/// <summary>Bank and incline range of the tilt drag: ±3.1416 (<c>0x40490e38</c>).</summary>
	public const float TiltLimit = 3.1416f;

	/// <summary>
	/// The range a press on pylon <paramref name="index"/> gives a drag of kind <paramref name="drag"/> (the panel's press <c>0x446300</c> and the handlers it starts), or null when the pylon takes no such
	/// drag: the height drag needs the loft control and stays within <see cref="CoasterTrack.LoftRange"/>, the rotate drag the rotate control and the automatic yaw ± the pylon's deviance, the loop tilt a
	/// loop pylon with the loop control and [−0.35, 0.35] on the first pylon of a pair, [0.15, 0.85] on any other, the tilt a pylon that is no loop pylon with the bank or the incline control, each within
	/// ±<see cref="TiltLimit"/> (a value without its control keeps its value); a move takes any cell.
	/// </summary>
	// [BIN:TPW-EXE:0x441a00 height drag] [BIN:TPW-EXE:0x441e80 rotate drag] [BIN:TPW-EXE:0x441cd0 loop tilt] [BIN:TPW-EXE:0x442010 tilt drag] the ranges each handler sets at the press (see CoasterToolPanel.Press)
	public CoasterDragRange? DragRange( int index, CoasterDrag drag )
	{
		if ( !CanEdit( index ) )
			return null;
		var node = Track.Nodes[index];
		var pylon = Track.Definition.Pylon( node.LoopPylon );
		var capabilities = pylon.Capabilities;
		var loop = (node.Flags & CoasterNodeFlags.Loop) != 0;
		switch ( drag )
		{
			case CoasterDrag.Move:
				return new CoasterDragRange();
			case CoasterDrag.Height when (capabilities & CoasterPylonCapabilities.Loft) != 0:
			{
				var (low, high) = Track.LoftRange( Site, index );
				return new CoasterDragRange( low, high, 0, 0 );
			}
			case CoasterDrag.Rotate when (capabilities & CoasterPylonCapabilities.Rotate) != 0:
			{
				var automatic = Track.AutomaticYaw( Site, index );
				return new CoasterDragRange( automatic - pylon.MaxRotationDeviance, automatic + pylon.MaxRotationDeviance, 0, 0 );
			}
			case CoasterDrag.LoopTilt when loop && (capabilities & CoasterPylonCapabilities.LoopTilt) != 0:
			{
				var first = (node.Flags & CoasterNodeFlags.LoopFirst) != 0;
				return new CoasterDragRange( first ? -0.35f : 0.15f, first ? 0.35f : 0.85f, 0, 0 );
			}
			case CoasterDrag.Tilt when !loop && (capabilities & (CoasterPylonCapabilities.Bank | CoasterPylonCapabilities.Incline)) != 0:
			{
				var bank = (capabilities & CoasterPylonCapabilities.Bank) != 0;
				var incline = (capabilities & CoasterPylonCapabilities.Incline) != 0;
				return new CoasterDragRange( bank ? -TiltLimit : node.Bank, bank ? TiltLimit : node.Bank, incline ? -TiltLimit : node.Incline, incline ? TiltLimit : node.Incline );
			}
			default:
				return null;
		}
	}

	/// <summary>Whether <see cref="RemoveLast"/> would delete a pylon.</summary>
	public bool CanRemoveLast => Track.CanRemoveLast;

	/// <summary>Whether <see cref="Cut"/> would cut between the two pylons.</summary>
	public bool CanCut( int first, int second ) => Track.IsRunnable && Track.CheckCut( first, second ) != CoasterTrack.CutResult.Refused;

	/// <summary>A node being edited by a drag: what it was and what it cost, so the release can settle the money.</summary>
	/// <param name="Refund">The truncated cost of the node (and, on a complete track, of the node after it) before the edit.</param>
	public sealed record NodeEdit( int Index, CoasterNode Start, long Refund, int? Next );

	/// <summary>Whether pylon <paramref name="index"/> may be edited by hand: a pylon the player placed (the pick-up 0x446300 excludes the station nodes only).</summary>
	public bool CanEdit( int index ) => index >= CoasterTrack.StationNodeCount && index < Track.Nodes.Count && Track.Nodes[index].IsPlayerPylon;

	/// <summary>Picks a pylon up for an edit (the press of a drag): its cost is noted so the release settles it.</summary>
	public NodeEdit? BeginEdit( int index, bool includeNext = false )
	{
		if ( !CanEdit( index ) )
			return null;
		var next = includeNext && Track.IsComplete && !Track.IsCut && index + 1 < Track.Nodes.Count ? index + 1 : (int?)null;
		var refund = CoasterTrack.Truncate( Track.NodeCost( index, chargeUnit: false ) ) + (next is int n ? CoasterTrack.Truncate( Track.NodeCost( n, chargeUnit: false ) ) : 0);
		return new NodeEdit( index, Track.Nodes[index], refund, next );
	}

	/// <summary>Changes the picked node while the drag runs (no money moves yet): the track is revalidated and a finished track fitted again.</summary>
	public void ApplyEdit( NodeEdit edit, Func<CoasterNode, CoasterNode> change )
	{
		Track.Replace( edit.Index, change( Track.Nodes[edit.Index] ) );
		AfterEdit( deferRebuild: true );
	}

	/// <summary>
	/// Revalidates after an edit. While a drag runs the controller is not fitted again (that resamples, rates and checks the whole circuit): the renderer draws the nodes as they are, and
	/// <see cref="FinishEdit"/> fits the track once on the release.
	/// </summary>
	// [APPROX:COAST-028] the original rebuilds the track every frame of a drag (0x43eb70); OpenTPW draws the track and its crossing markers live and fits the curve, rates the track and replaces the trains once, on the release
	private void AfterEdit( bool deferRebuild )
	{
		Track.Revalidate( Site, Ride.World, Ride );
		if ( Track.IsRunnable && !deferRebuild )
			Ride.Rebuild();
		else
			Ride.RaiseTrackChanged();
	}

	/// <summary>Ends a drag without money (rotate, tilt, loop tilt): the finished track is fitted again.</summary>
	public void FinishEdit()
	{
		if ( Track.IsRunnable )
			Ride.Rebuild();
	}

	/// <summary>Releases the drag: refunds the truncated cost the node had when it was picked and charges the rounded-up cost it has now.</summary>
	// [BIN:TPW-EXE:0x441a00 drag release] the release of the height drag credits ftol(the cost noted at the press) (0x4d0190) and debits the rounded-up new cost (0x4d01f0); the move release (0x445a70) does the same for the node and, on a complete track, the node after it
	public void EndEdit( NodeEdit edit )
	{
		var charge = CoasterTrack.RoundUp( Track.NodeCost( edit.Index, chargeUnit: false ) ) + (edit.Next is int n ? CoasterTrack.RoundUp( Track.NodeCost( n, chargeUnit: false ) ) : 0);
		Refund( edit.Refund );
		Spend( charge, force: true );
		FinishEdit();
	}

	/// <summary>Sets the loft parameter of the picked node within the drag's range (<c>0x441a00</c>).</summary>
	public void EditLoft( NodeEdit edit, float t )
	{
		var pylon = Track.Definition.Pylon( edit.Start.LoopPylon );
		var height = CoasterTrack.PylonHeight( pylon, t );
		ApplyEdit( edit, node => node with { T = t, Height = height } );
	}

	/// <summary>Sets the yaw of the picked node (<c>0x441e80</c>): the node takes the manual yaw flag.</summary>
	public void EditYaw( NodeEdit edit, float yaw ) => ApplyEdit( edit, node => { var copy = node with { Yaw = yaw }; copy.Flags |= CoasterNodeFlags.ManualYaw; return copy; } );

	/// <summary>Sets the bank of the picked node (<c>0x442010</c>): the node takes the no-auto-bank flag.</summary>
	public void EditBank( NodeEdit edit, float bank ) => ApplyEdit( edit, node => { var copy = node with { Bank = bank }; copy.Flags |= CoasterNodeFlags.NoAutoBank; return copy; } );

	/// <summary>Sets the incline of the picked node (<c>0x442010</c>): the node takes the manual incline flag.</summary>
	public void EditIncline( NodeEdit edit, float incline ) => ApplyEdit( edit, node => { var copy = node with { Incline = incline }; copy.Flags |= CoasterNodeFlags.ManualIncline; return copy; } );

	/// <summary>Sets the loop rotation of the picked loop pylon (<c>0x441cd0</c>).</summary>
	public void EditLoopRotate( NodeEdit edit, float rotate ) => ApplyEdit( edit, node => node with { LoopRotate = rotate } );

	/// <summary>Moves the picked node to a cell while the drag runs (<c>0x442f40</c>, no money yet); returns the validator's verdict for it.</summary>
	public CoasterPlaceResult EditMove( NodeEdit edit, int x, int y )
	{
		var node = Track.Nodes[edit.Index];
		if ( (node.X, node.Y) == (x, y) )
			return CoasterPlaceResult.Ok;
		if ( x < 0 || y < 0 || x >= Site.Width || y >= Site.Height )
			return CoasterPlaceResult.OutsideMap;
		var result = Track.CheckPlacement( Site, Ride.World, Ride, x, y, node.LoopPylon, moving: edit.Index );
		var stack = Ride.World.PylonsAt( x, y ).Count( pylon => !ReferenceEquals( pylon.Node, node ) );
		// back on the cell it was picked up from, the node takes its old place in the stack again
		int? restore = (x, y) == (edit.Start.X, edit.Start.Y) ? edit.Start.StackIndex : null;
		Track.Relocate( edit.Index, x, y, stack, restore );
		AfterEdit( deferRebuild: true );
		return result;
	}

	/// <summary>Removes the track and every pylon (the station stays) without a refund; riders on board are let off through <c>COAST 3</c>.</summary>
	public void Delete()
	{
		Ride.ClearTrains();
		Ride.EnterState( CoasterControllerState.Incomplete );
		Track.Clear();
		Ride.RaiseTrackChanged();
	}

	// [BIN:STP-PPC:0x100CBFDC bank debit] coaster charges debit the bank (+12) directly; credits go through 0x100CBF50
	// [BIN:TPW-EXE:0x4d01f0 bank debit] the debit (callers 0x435020, 0x441a00, 0x445a70; credit 0x4d0190 for refunds): bank +0x114 (mWithdrawalsEnabled, serializer 0x4cf920) 0 → nothing; else balance (+0xC) down, turn-entered-red (+0x120) stamped from game +0x1da70c when it crosses below 0 from the last balance (+0x11C) ≥ 0, last balance set, park total costs (+0x1F5A0; "Other costs" = total − loan payments) up, year profit (+0x124) down; the credit adds to balance, money-in (+0x1FC90) and year profit
	// [EXT:coaster-ledger] track money is posted as other costs and other income (ParkEconomy), which matches the debit and credit totals, and joins the station's total spent, which is OpenTPW's own extension; the debit's withdrawals gate, last balance and turn-entered-red stamp come from ParkEconomy's posting
	private bool Spend( long amount, bool force = false )
	{
		if ( amount <= 0 )
			return true;
		if ( Economy is { } park )
		{
			if ( park.TrySpendCoasterTrack( amount, economyInstance(), force ) != ParkEconomy.PurchaseResult.Ok )
				return false;
		}
		Ride.PaidCost += amount;
		return true;
	}

	private void Refund( long amount )
	{
		if ( amount <= 0 )
			return;
		Economy?.CreditCoasterTrack( amount, economyInstance() );
		Ride.PaidCost -= amount;
	}
}

/// <summary>[EXT:online-coop-commands] The coaster panel's build state a click reads (<see cref="CoasterBuilder.ToolState"/>): the requested height and the loop edit mode with its pylons still to place.</summary>
public readonly record struct CoasterToolState( float Height, bool LoopMode, int LoopPylonsLeft );
