namespace OpenTPW;

// Theme Park Inc's ride choice, phase P3a of the native guest states (docs/reverse/TPI-GUESTS.md): the major decision 0x610080 (state 6's decision on A & 3 of 0 or 2), the minor decision 0x6114B0
// (state 13, every twelfth walking update) and the attraction score 0x6101E0 they rank by (ThemeParkIncAttractionScore). Theme Park World's twins are tp.exe's 0x004FCB10, 0x004FD570 and 0x004FCC30:
// the loops are the same, the score has Theme Park Inc's gates, weights and percentages, and no decision policy, extension or random bonus takes part (Q2 of the plan's decisions).
public sealed partial class GuestSimulation
{
	/// <summary>
	/// The major decision <c>0x610080</c>: the target is cleared, then every attraction of the object list with a back-of-queue cell is scored; a score of at least 10 that beats the best (a tie on an odd
	/// turn) wins when the ride passes the counts test and the route request to its back cell succeeds. The request's walk stays set for every candidate it is made for, as in the original.
	/// </summary>
	// [BIN:TPI-EXE:0x610080 major decision] +0x204 = 0; the list (world +0x1EAB88, next +0x14): +0x42 bit 4, 0x5E0520 nonzero, 0x6101E0 at least 10 (unsigned jb), above the best (signed jg) or equal on an
	// odd turn (world +0x1EAB40 bit 0), 0x5DFC90 (the counts test: bit 4, the state neither 1 nor 4, the open word +0x7C, a back cell, the length below 4 × +0x50, a coaster 0x485BB0 and the track words),
	// 0x60B7F0 to the back cell: the best and +0x204 = the thing. tp.exe's 0x004FCB10 is the same loop over its own score
	internal void ThemeParkIncMajorDecision( Guest guest, ThemeParkIncGuestRecord tpi, ThemeParkIncGuestSettings settings )
	{
		tpi.Target = 0;
		if ( ThemeParkIncWorld?.Attractions() is not { Count: > 0 } list )
			return;
		var best = 0;
		var oddTurn = (TpiTurn & 1) != 0;
		for ( var index = 0; index < list.Count; index++ )
		{
			var view = list[index];
			var ride = view.Ride;
			var back = TpiBackWord( ride );
			if ( back == 0 )
				continue;
			var score = ThemeParkIncScore( guest, tpi, settings, view, back );
			if ( score < AttractionScore.CandidateFloor )
				continue;
			var signed = unchecked( (int)score );
			if ( !(signed > best || signed == best && oddTurn) )
				continue;
			if ( !ThemeParkIncCountsTest( ride ) || !RouteToTpiCellWord( guest, back ) )
				continue;
			best = signed;
			tpi.Target = view.ThingId;
		}
	}

	/// <summary>The counts test <c>0x5DFC90</c>: open, not broken down or condemned, a back cell and room in the queue (the coaster's and the track's words through <see cref="IRideVisitorBridge.IsOpen"/>).</summary>
	// [BIN:TPI-EXE:0x5DFC90 counts test] +0x42 bit 4; +0xA0 neither 1 nor 4; +0x7C nonzero; 0x5E0550 nonzero; 0x5E0280(0) below 4 × +0x50; type +0x14C 3: 0x485BB0; 1 or 2: +0x34 nonzero. tp.exe's 0x004DD920
	private bool ThemeParkIncCountsTest( RideVisitorBridge ride ) =>
		ride.IsOpen && !ride.IsBrokenDown && ride.BackCell != null && TpiQueueHasRoom( ride );

	/// <summary>
	/// The attraction score <c>0x6101E0</c> of <paramref name="view"/> for the guest (<see cref="ThemeParkIncAttractionScore"/>); an information desk scores 0 (see below).
	/// </summary>
	private uint ThemeParkIncScore( Guest guest, ThemeParkIncGuestRecord tpi, ThemeParkIncGuestSettings settings, ThemeParkIncAttractionView view, ushort back )
	{
		var ride = view.Ride;
		// [BIN:TPI-EXE:0x6104D5 information desk] UsageInfo.IsInfoDesk (type +0x244): the desk's target word +0x140 (0x5E8FE0) 0 scores 0, else the target is scored in the desk's place
		// [EXT:tpi-capability-gates] the information desk is phase P5's: a desk is read as naming no target (its +0x140 is written by the desk's own update 0x5E8F00/0x5E9105, which is not ported), so it
		// scores 0; it draws nothing
		if ( view.Terms.IsInfoDesk )
			return 0;
		var (gx, gy) = TpiCell( guest );
		var (bx, by) = ThemeParkIncGuestSettings.CellOf( back );
		var last = guest.UsedHistory[0];
		// [BIN:TPI-EXE:0x61035F same type] the last used thing's slot in 0xAA18B0, whatever kind of thing it holds, and its type word +0x16 against the candidate's
		var sameType = last != 0 && ThemeParkIncWorld?.ThingTypeId( (ushort)last ) is { } lastType && lastType == ride.TypeId;
		// [BIN:TPI-EXE:0x6101FD awaiting activation] the score's first test: a thing 0x6242F0 does not free (a special ride whose record still names it) scores 0; the economy's special-ride table
		// (ParkEconomy.IsThemeParkIncSpecialRidePending through IGuestPayments.AwaitsActivation) is the one the Theme Park World rules' HostScore and candidate test read, so both choices refuse the same rides
		var special = Payments?.AwaitsActivation( ride ) == true;
		var input = new ThemeParkIncAttractionScoreInput( special, ThemeParkIncSpriteIsFemale( guest ), sameType, view.Terms, guest.UmbrellaState,
			ArrivalWeatherState?.Invoke() ?? UmbrellaRules.WeatherNone, tpi.HoldsBalloon, tpi.BalloonCountdown, gx, gy, bx, by, Litter?.Environment.At( bx, by ).Attraction ?? 0,
			TpiQueuePosition( ride, 0 ), ride.QueueSizeInCells, ride.LiveExcitement & 0xFF, (ride.StaticExcitementLevel & 0xFF) != 0, tpi.PreferredExcitement & 0xFF, guest.Thirst, guest.Hunger,
			guest.Toilet, guest.Nausea, ride.ThirstEffect, ride.HungerEffect, ride.ProvidesRelief, ride.AgeDays, ride.GoldenTicketCost, ride.UpgradeCost, tpi.Generation,
			unchecked( (uint)settings.DiscardBalloonPctModifier ), view.ThingId, guest.UsedHistory, guest.TemporaryHistory );
		return ThemeParkIncAttractionScore.Compute( input, settings.DecisionWeights, settings.DecisionVariables );
	}

	/// <summary>
	/// The minor decision <c>0x6114B0</c> of a guest walking to a ride: the attractions standing in the 4 × 4 cells round the guest (x − 2..x + 1, y − 2..y + 1) strictly nearer to the target's back cell than
	/// the guest, other than the target, that pass the counts test and score above 0 compete (a tie on an odd turn); the best replaces the target when the route from the target's access cell to it is shorter
	/// than to the guest, the old target going into <c>+0x206</c>, and the walk goes to the best's access cell.
	/// </summary>
	// [BIN:TPI-EXE:0x6114B0 minor decision] +0x204 0 returns; the target's back cell (0x5E0520) 0 returns; d0 its squared distance; x from −2 (outer), y from −2 (inner), four each, inside 0..0x7F and
	// strictly nearer than d0: the cell's thing chain (the cell record's head −0x20, next +0xA) of kind 3 (+2), not the target, 0x5DFC90, 0x6101E0 above 0 (signed) and above the best or equal on an odd
	// turn; then 0x5D8030 from the target's +0x46 to the guest's cell and to the best's +0x46 (mask +0x1A0), both found and the second shorter: "Minor Decision: OID=%d", +0x206 = +0x204, +0x204 = the
	// best, 0x60B7F0 to its +0x46. tp.exe's 0x004FD570
	internal void ThemeParkIncMinorDecision( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		if ( tpi.Target == 0 || TpiAttraction( tpi.Target ) is not { } target || ThemeParkIncWorld?.Attractions() is not { } list )
			return;
		var back = TpiBackWord( target.Ride );
		if ( back == 0 )
			return;
		var settings = NativeTpi!;
		var (gx, gy) = TpiCell( guest );
		var (bx, by) = ThemeParkIncGuestSettings.CellOf( back );
		var distance = (gx - bx) * (gx - bx) + (gy - by) * (gy - by);
		var oddTurn = (TpiTurn & 1) != 0;
		ThemeParkIncAttractionView? best = null;
		var bestScore = 0;
		for ( var x = gx - 2; x < gx + 2; x++ )
		{
			for ( var y = gy - 2; y < gy + 2; y++ )
			{
				if ( x < 0 || x >= 128 || y < 0 || y >= 128 || (x - bx) * (x - bx) + (y - by) * (y - by) >= distance )
					continue;
				for ( var index = 0; index < list.Count; index++ )
				{
					var candidate = list[index];
					if ( candidate.Cell != (x, y) || candidate.ThingId == tpi.Target || !ThemeParkIncCountsTest( candidate.Ride ) )
						continue;
					var score = unchecked( (int)ThemeParkIncScore( guest, tpi, settings, candidate, TpiBackWord( candidate.Ride ) ) );
					if ( score <= 0 || !(score > bestScore || score == bestScore && oddTurn) )
						continue;
					(best, bestScore) = (candidate, score);
				}
			}
		}
		if ( best is not { } chosen )
			return;
		var targetAccess = target.Ride.AccessCell ?? target.Ride.EntranceCell;
		var bestAccess = chosen.Ride.AccessCell ?? chosen.Ride.EntranceCell;
		var toGuest = RouteLength( guest, targetAccess, (gx, gy) );
		var toBest = RouteLength( guest, targetAccess, bestAccess );
		if ( toGuest == -1 || toBest == -1 || toGuest <= toBest )
			return;
		tpi.Word206 = tpi.Target;
		tpi.Target = chosen.ThingId;
		RouteToTpiCellWord( guest, ThemeParkIncGuestSettings.CellWord( bestAccess.X, bestAccess.Y ) );
	}
}
