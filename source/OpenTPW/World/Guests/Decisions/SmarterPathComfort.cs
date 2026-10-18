namespace OpenTPW;

/// <summary>
/// The pure rules of the clean and quiet paths (docs/SMARTER-GUESTS.md, phase 6): the wander goal is weighted by the litter and the crowd around it, and a guest standing in litter or in a crowd complains with a tiny
/// happiness cost, at most once per visit. Every number is OpenTPW's own: the thresholds come from the original's, the weights, the margin and the neighbour term were chosen by the sweep of docs/SMARTER-GUESTS.md (phase 6).
/// </summary>
// [EXT:smarter-guests-paths] OpenTPW's own rules; the original wander ignores litter and crowding
public static class SmarterPathComfort
{
	/// <summary>The litter, in units, at which a guest complains: two pieces, or one vomit. The original thinks "Too messy" when more than 0.2 of the path cells of the guest's 4 x 4 block are littered (<see cref="GuestThoughts.MessyShare"/>); 0.2 of the nine cells a guest sees around it is 1.8, rounded up to 2.</summary>
	public const int LitterComplaintUnits = 2;

	/// <summary>
	/// The guests in one cell at which a guest complains of a crowd: one more than a queue cell holds (<see cref="RideVisitorBridge.PositionsPerCell"/> = 4, the density the game itself lays guests out at when they
	/// wait close together). A path with more guests than that in one cell is crowded by the game's own measure.
	/// </summary>
	public const int CrowdComplaintGuests = RideVisitorBridge.PositionsPerCell + 1;

	/// <summary>What one unit of litter costs a cell in the goal weighting: <see cref="CrowdComplaintGuests"/>, so a cell exactly at the litter complaint level (2 units, 5 each) costs what a cell exactly at the crowd complaint level (5 guests, 2 each) costs.</summary>
	public const int LitterWeight = CrowdComplaintGuests;

	/// <summary>What one guest in the cell costs it: <see cref="LitterComplaintUnits"/> (see <see cref="LitterWeight"/>).</summary>
	public const int CrowdWeight = LitterComplaintUnits;

	/// <summary>What one guest in the cells around a candidate costs it (the crowd a guest walks into on its way there): one, against <see cref="CrowdWeight"/> for the cell itself.</summary>
	public const int NearWeight = 1;

	/// <summary>
	/// How much cheaper another candidate must be before the wander goal moves to it: <see cref="CrowdWeight"/>, one more guest in the goal's own cell. The sweep (margins 1, 2, 3 and 10) is in the page: 10 (one complaint level)
	/// moves too few goals to change anything measurable, 1 lowers happiness a little, 2 keeps the crowd gain and leaves happiness inside the noise.
	/// </summary>
	public const int SwitchMargin = CrowdWeight;

	/// <summary>The park turns between two complaints of one guest: a whole visit (the exit counter, 480 turns, is about 21 days), so a guest complains at most once per visit.</summary>
	public const int ComplaintCooldownTurns = 480;

	/// <summary>The happiness a complaint costs is the original's smallest happiness step divided by this: a fiftieth (0.1 of the shipped 5), the cost of being noticed and not of being hurt.</summary>
	public const int CostDivisor = 50;

	/// <summary>The litter units of one cell's litter: 1 for a piece (the ordinary kinds and the fallback), 2 for vomit and for a stinkbomb (the two kinds that also spread a region effect over the cells around them), 0 for none.</summary>
	public static int LitterUnitsOf( LitterType type ) => type switch
	{
		LitterType.None => 0,
		LitterType.Vomit or LitterType.Stinkbomb => 2,
		_ => 1
	};

	/// <summary>The cost of a candidate in the goal weighting, counting only the kinds the rule set switched on: the litter around it, the guests in it and the guests around it.</summary>
	public static int CostOf( in PathCellView cell, bool litter, bool crowd ) =>
		(litter ? cell.LitterUnits * LitterWeight : 0) + (crowd ? cell.Crowd * CrowdWeight + cell.NearCrowd * NearWeight : 0);

	/// <summary>The happiness a complaint costs under these settings.</summary>
	public static float HappinessCost( GuestSettings settings ) => Math.Max( 0, settings.SmallHappinessChange ) / (float)CostDivisor;

	/// <summary>
	/// The verdict for a wandering guest: the cheapest cell of the walk replaces the original's goal (the last cell) when it is at least <see cref="SwitchMargin"/> cheaper (ties between cheaper cells go to the later
	/// one, nearer the goal); the guest complains of litter around it, else of a crowd in its cell, when its cooldown has passed. Pure: the host performs both and keeps the cooldown.
	/// </summary>
	public static PathDecision Decide( in DecisionContext context, in GuestView guest, in WanderPathView path, bool litter, bool crowd )
	{
		var walk = path.Walk;
		var goal = -1;
		if ( walk.Length > 1 )
		{
			var best = walk.Length - 1;
			var bestCost = CostOf( walk[best], litter, crowd );
			for ( var index = walk.Length - 2; index >= 0; index-- )
			{
				var cost = CostOf( walk[index], litter, crowd );
				if ( cost < bestCost )
					(best, bestCost) = (index, cost);
			}
			if ( best != walk.Length - 1 && CostOf( walk[^1], litter, crowd ) - bestCost >= SwitchMargin )
				goal = best;
		}
		if ( guest.BirthId == 0 || guest.ComplaintTurn >= 0 && context.ParkTurn - guest.ComplaintTurn < ComplaintCooldownTurns )
			return new PathDecision( goal );
		if ( litter && path.Here.LitterUnits >= LitterComplaintUnits )
			return new PathDecision( goal, PathComplaint.Litter, GuestThought.TooMessy, HappinessCost( context.Settings ) );
		if ( crowd && path.Here.Crowd >= CrowdComplaintGuests )
			return new PathDecision( goal, PathComplaint.Crowd, GuestThought.Dissatisfied, HappinessCost( context.Settings ) );
		return new PathDecision( goal );
	}
}
