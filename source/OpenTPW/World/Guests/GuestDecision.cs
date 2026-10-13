namespace OpenTPW;

/// <summary>The short activity a guest in state 6 can start (the original's state 8, ten park turns); the values are the animation selectors passed to <c>0x004FC800</c>.</summary>
public enum GuestActivity : byte
{
	None = 0,
	/// <summary>No attraction found at a major decision (the "can't decide" pause).</summary>
	CantDecide = 4,
	Cheering = 5,
	Vomiting = 7
}

/// <summary>
/// The decision state (state 6, "Doing nothing", <c>0x004FEC90</c>, docs/reverse/WANDER-plan.md §2): once per park turn the guest
/// runs these tests in order and acts on the first that applies. One draw <c>R</c> serves the vomit test and the final choice.
/// </summary>
public static class GuestDecision
{
	/// <summary>Park turns after an activity before a happy guest can cheer again (<c>T − +0x208 &gt; 100</c>).</summary>
	public const uint CheerGapTurns = 100;
	/// <summary>Park turns after the last decision stamp before the next major decision (<c>T &gt; +0x1FC + 30</c>).</summary>
	public const uint MajorDecisionGapTurns = 30;
	/// <summary>Park turns an activity lasts: state 8 ends once the turn passes <c>+0x208 + 10</c>.</summary>
	public const uint ActivityTurns = 10;

	public enum Outcome
	{
		/// <summary>Nothing changed this turn.</summary>
		None,
		Cheer,
		Vomit,
		/// <summary>The leaving test passed (<see cref="GuestLifecycle.ShouldLeave"/>) and a way out was found (state 18).</summary>
		Leave,
		/// <summary>The major decision found an attraction (state 10).</summary>
		GoToRide,
		/// <summary>The major decision found nothing: the "can't decide" activity, the small happiness loss and a new stamp.</summary>
		CantDecide,
		/// <summary>A wander destination was set (state 7).</summary>
		Wander,
		/// <summary>No wander destination: a new stamp only.</summary>
		WanderFailed
	}

	/// <summary>The fields state 6 reads. Turns are the original's unsigned 32-bit words.</summary>
	public readonly record struct Input( uint Turn, uint ActivityStamp, uint DecisionStamp, float Happiness, float Illness, int ExitLevel, bool ParkClosed, uint Draw );

	/// <summary>
	/// Runs the state-6 tests in the original order. <paramref name="leave"/> is the leaving gate: it takes the big happiness change and
	/// returns true when it found a way out (the guest leaves); without one the tests go on. <paramref name="chooseRide"/> is the major
	/// decision (true when it picked an attraction) and <paramref name="wander"/> the wander destination (true when it set one). Each
	/// callback is called only on its branch.
	/// </summary>
	// [BIN:TPW-EXE:0x004FEC90 state 6] one draw at entry; cheering when (unsigned) turn − +0x208 > 100 and the happiness byte > 80 (activity 5); vomiting when the illness byte is above 80 and equal to 100 and R % 3 == 0 (activity 7, illness 0); the leaving gate on happiness byte 0, +0x1BC == 0 or the closed word (0x0051A280) takes the big happiness change (0x004FEA70 selector 2) and tries a route to the two gate cells (0x004FA530, in an order by R & 1, twice): a route sets state 18, none goes on; then R % 3: 0 → when +0x1FC + 30 < turn (unsigned) the choice 0x004FCB10, a target sets state 10, none starts activity 4, takes the small happiness change (0x004FEA70 selector 0) and stamps +0x1FC; 1 → 0x004F9490, success sets state 7, failure stamps +0x1FC; 2 → nothing
	// [APPROX:GUEST-010] steps 3, 5, 6 and 7 of the original order are not run: the litter meter step (+0x1B4 at 90 or more: a bin within radius 3 or litter on the cell; GUEST-005 and ECON-020 model neither), the two presentation-only turns towards a show or a performer, and the pranks (GUEST-007); the vomit leaves no litter, and the leaving gate's route test asks whether a path reaches an arrival lane's end (OpenTPW's way out) instead of routing to the two gate cells — evidence needed: none for the order (read in full); the litter and prank steps wait for their own items
	public static Outcome Decide( in Input input, Func<bool> leave, Func<bool> chooseRide, Func<bool> wander )
	{
		var r = input.Draw;
		if ( unchecked(input.Turn - input.ActivityStamp) > CheerGapTurns && MeterMath.Byte( input.Happiness ) > 80 )
			return Outcome.Cheer;
		var illness = MeterMath.Byte( input.Illness );
		if ( illness > 80 && illness == 100 && r % 3 == 0 )
			return Outcome.Vomit;
		if ( GuestLifecycle.ShouldLeave( input.Happiness, input.ExitLevel, input.ParkClosed ) && leave() )
			return Outcome.Leave;
		switch ( r % 3 )
		{
			case 0:
				if ( unchecked(input.DecisionStamp + MajorDecisionGapTurns) < input.Turn )
					return chooseRide() ? Outcome.GoToRide : Outcome.CantDecide;
				return Outcome.None;
			case 1:
				return wander() ? Outcome.Wander : Outcome.WanderFailed;
			default:
				return Outcome.None;
		}
	}
}
