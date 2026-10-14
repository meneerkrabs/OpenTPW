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
	/// <summary>The loop edit mode (bit 0x40): new pylons are LoopPylons.</summary>
	public bool LoopMode { get; set; }
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

	/// <summary>Starts the track at a station (removing any pylons).</summary>
	public void SetStation( (int X, int Y) depart, (int X, int Y) @return )
	{
		Ride.ClearTrains();
		Track.SetStation( depart, @return );
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
		if ( Track.IsChainStart( x, y ) && !Track.IsClosed && !Track.IsComplete )
			return Close() ? CoasterPlaceResult.Ok : CoasterPlaceResult.NotEditable;
		var result = Check( x, y );
		if ( result is CoasterPlaceResult.OutsideMap or CoasterPlaceResult.NotEditable or CoasterPlaceResult.NoLoopPylon )
			return result;
		// [APPROX:COAST-028] the generic tool's affordability test (accumulator data:0x84ac4 against the balance, 0x1006F664 → global 0x7c50c) refuses nothing here: its consumers 0x10070DB0, 0x1007118C and 0x100712A8 are not traced — evidence needed: what those consumers do with 0x7c50c on a coaster click
		Spend( CoasterTrack.Truncate( PreviewCost( x, y ) ), force: true );
		Track.Append( Ride.World, Ride, x, y, Height, LoopMode );
		Track.Revalidate( Site, Ride.World, Ride );
		return result;
	}

	/// <summary>Closes the circuit (no node is added).</summary>
	// [APPROX:COAST-030] closing needs two pylons, and the closing link and the arrival turn into the station are validated (the last pylon is marked invalid when they fail); 0x100470C4 itself tests neither — evidence needed: the closure tests, if any, of the generic tool engine 0x10080C50
	public bool Close()
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
	// [APPROX:COAST-030] moving a pylon or its height is refused while riders are on board a complete track; the pick-up 0x1004A1FC tests only the station node (descriptor flag 2) — evidence needed: the guest predicate's callers in the build tick 0x10047E4C
	public CoasterPlaceResult Move( int index, int x, int y )
	{
		if ( index <= 0 || index >= Track.Nodes.Count || Track.Nodes[index].IsPylonless || (Track.IsComplete && Ride.RidersOnBoard != 0) )
			return CoasterPlaceResult.NotEditable;
		var node = Track.Nodes[index];
		if ( (node.X, node.Y) == (x, y) )
			return CoasterPlaceResult.Ok;
		if ( x < 0 || y < 0 || x >= Site.Width || y >= Site.Height )
			return CoasterPlaceResult.OutsideMap;
		var result = Track.CheckPlacement( Site, Ride.World, Ride, x, y, node.LoopPylon, moving: index );
		var stack = Ride.World.PylonsAt( x, y ).Count( pylon => !ReferenceEquals( pylon.Node, node ) );
		Settle( index, () => Track.Replace( index, node with { X = x, Y = y, StackIndex = stack } ) );
		if ( Track.IsComplete )
			Ride.Rebuild();
		return result;
	}

	/// <summary>Releases the height slider of pylon <paramref name="index"/> (<c>0x45fc4</c>): refunds trunc(old cost), charges ceil(new cost).</summary>
	// [BIN:STP-PPC:0x10045FC4 height slider] releasing the height slider credits trunc(stored cost) and debits ceil(new cost)
	public bool SetHeight( int index, float requested )
	{
		if ( index <= 0 || index >= Track.Nodes.Count || Track.Nodes[index].IsPylonless || (Track.IsComplete && Ride.RidersOnBoard != 0) )
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
	public CoasterPlaceResult CheckComplete() => Track.IsComplete ? CoasterPlaceResult.NotEditable
		: !Track.IsClosed ? CoasterPlaceResult.NotClosed
		: Track.HasInvalidNodes ? CoasterPlaceResult.InvalidNodes : CoasterPlaceResult.Ok;

	/// <summary>
	/// Completes a closed track (<c>0x37ef0</c>): appends the station end nodes, then fits the curve, rates the track and
	/// places the trains. Completion moves no money.
	/// </summary>
	// [APPROX:COAST-016] completion is refused while a node is marked invalid (flag 0x4000) or the circuit is open, and the build click completes on a station end cell of a closed circuit; 0x10037EF0 itself completes unconditionally and tests neither, so these refusals stand only as a guess at the tool layer; the original's click decision is cell action 129 (0x10084EA4 via 0x10073A3C) of the generic tool engine 0x10080C50, whose test is not decoded — evidence needed: cell action 129 in 0x10080C50 and what the original does with invalid nodes on completion
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
	// [BIN:STP-PPC:0x10045EAC reopen guard] the Mac predicate adds riders +320 == 0 (written by passenger removal 0x3d6f8..0x3d85c); the PC predicate 0x441970 reads controller +0x140 instead, which is the self-intersection count (its only writers are 0x439c40 and 0x43ad50), so the rider test is the Mac's reading and OpenTPW keeps it as its own edit guard (COAST-030)
	public bool Reopen()
	{
		if ( !Track.IsComplete || Ride.RidersOnBoard != 0 )
			return false;
		Ride.ClearTrains();
		Track.Reopen();
		Track.Revalidate( Site, Ride.World, Ride );
		return true;
	}

	/// <summary>Removes the track and every pylon (the station stays) without a refund; riders on board are let off through <c>COAST 3</c>.</summary>
	public void Delete()
	{
		Ride.ClearTrains();
		Track.Clear();
		Ride.RaiseTrackChanged();
	}

	// [BIN:STP-PPC:0x100CBFDC bank debit] coaster charges debit the bank (+12) directly; credits go through 0x100CBF50
	// [BIN:TPW-EXE:0x4d01f0 bank debit] the debit (callers 0x435020, 0x441a00, 0x445a70; credit 0x4d0190 for refunds): bank +0x114 (mWithdrawalsEnabled, serializer 0x4cf920) 0 → nothing; else balance (+0xC) down, turn-entered-red (+0x120) stamped from game +0x1da70c when it crosses below 0 from the last balance (+0x11C) ≥ 0, last balance set, park total costs (+0x1F5A0; "Other costs" = total − loan payments) up, year profit (+0x124) down; the credit adds to balance, money-in (+0x1FC90) and year profit
	// [APPROX:COAST-031] track money is posted as other costs and other income (ParkEconomy), which matches the debit and credit totals, and joins the station's total spent, which is OpenTPW's own; the withdrawals gate (ECON-051), the last balance and the turn-entered-red stamp (SAVE-008) are not modelled — they belong to the economy and save owners
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
