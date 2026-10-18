namespace OpenTPW;

/// <summary>
/// The pure rules of the clean and quiet paths (docs/SMARTER-GUESTS.md, phase 6): the wander goal is weighted by the litter and the crowd around it, and a guest standing in litter or in a crowd complains with a small
/// happiness cost, at most once per cooldown. Every number is OpenTPW's own and is derived from a number of the original or from another constant here; none is tuned (the page lists the sources).
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

	/// <summary>
	/// How much cheaper the cleaner cell must be before the wander goal moves to it: the cost of one complaint level (<see cref="LitterComplaintUnits"/> x <see cref="LitterWeight"/> = 10). A goal is changed only
	/// when it is at least a complaint's worth worse than another cell of the same walk, so the rule leaves a clean park's wander alone.
	/// </summary>
	public const int SwitchMargin = LitterComplaintUnits * LitterWeight;

	/// <summary>The park turns between two complaints of one guest: half a visit (the exit counter, 480 turns, is about 21 days), so a guest complains at most twice in a visit and never twice in a row.</summary>
	public const int ComplaintCooldownTurns = 240;

	/// <summary>The happiness a complaint costs is the original's smallest happiness step divided by this: a tenth (0.5 of the shipped 5), the cost of being noticed and not of being hurt.</summary>
	public const int CostDivisor = 10;

	/// <summary>The litter units of one cell's litter: 1 for a piece (the ordinary kinds and the fallback), 2 for vomit and for a stinkbomb (the two kinds that also spread a region effect over the cells around them), 0 for none.</summary>
	public static int LitterUnitsOf( LitterType type ) => type switch
	{
		LitterType.None => 0,
		LitterType.Vomit or LitterType.Stinkbomb => 2,
		_ => 1
	};

	/// <summary>The cost of a cell in the goal weighting, counting only the kinds the rule set switched on.</summary>
	public static int CostOf( in PathCellView cell, bool litter, bool crowd ) => (litter ? cell.LitterUnits * LitterWeight : 0) + (crowd ? cell.Crowd * CrowdWeight : 0);

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
