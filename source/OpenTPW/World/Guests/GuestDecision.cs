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
		/// <summary>The litter meter was full and a bin is in reach: the guest walks to it (state 9).</summary>
		ToBin,
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
	public readonly record struct Input( uint Turn, uint ActivityStamp, uint DecisionStamp, float Happiness, float Illness, float Litter, int ExitLevel, bool ParkClosed, uint Draw,
		int PrankFlag = 0, int StinkbombLikelihood = 0 );

	/// <summary>A prankster's prank, by its flag: 100 a stinkbomb, 101 litter, 102 a balloon burst.</summary>
	public enum Prank { Stinkbomb, Litter, Balloon }

	/// <summary>The happiness byte below which a prankster pranks (hard-coded in tp.exe; <c>Standard.sam</c>'s PrankHappinessThreshold has no key).</summary>
	public const int PrankHappinessBelow = 15;

	/// <summary>
	/// Runs the state-6 tests in the original order. <paramref name="litter"/> is the litter step of a guest whose litter meter byte is at
	/// least 90: true when it found a bin to walk to, false when it dropped its litter on its cell (the tests then go on).
	/// <paramref name="prank"/> runs a prank of an unhappy prankster (with the litter type of a littering one) and returns true when it pranked; the
	/// tests go on either way. <paramref name="leave"/> is the leaving gate: it takes the big happiness change and
	/// returns true when it found a way out (the guest leaves); without one the tests go on. <paramref name="chooseRide"/> is the major
	/// decision (true when it picked an attraction) and <paramref name="wander"/> the wander destination (true when it set one). Each
	/// callback is called only on its branch. <paramref name="happiness"/> reads the guest's happiness as it is when the prank test runs (default: the
	/// input's).
	/// </summary>
	// [BIN:TPW-EXE:0x004FEC90 state 6] one draw at entry; cheering when (unsigned) turn − +0x208 > 100 and the happiness byte > 80 (activity 5); vomiting when the illness byte is above 80 and equal to 100 and R % 3 == 0 (activity 7, illness 0); the leaving gate on happiness byte 0, +0x1BC == 0 or the closed word (0x0051A280) takes the big happiness change (0x004FEA70 selector 2) and tries a route to the two gate cells (0x004FA530, in an order by R & 1, twice): a route sets state 18, none goes on; then R % 3: 0 → when +0x1FC + 30 < turn (unsigned) the choice 0x004FCB10, a target sets state 10, none starts activity 4, takes the small happiness change (0x004FEA70 selector 0) and stamps +0x1FC; 1 → 0x004F9490, success sets state 7, failure stamps +0x1FC; 2 → nothing
	// [BIN:TPW-EXE:0x004FEDA8 litter step] when the litter meter byte (+0x1B4) is at least 90 (and the state 6, 7 or 10): the bin search 0x00500DC0 (radius 3) sets +0x1DC and state 9 and returns; without a bin a second draw drops litter 1 + R2 % 5 on the cell (0x004D93B0) and the meter goes to 0
	// [BIN:TPW-EXE:0x004FF0F5 pranks] with the happiness byte below 15 and the prankery byte +0x1C0 above R % 101: 100 drops a stinkbomb (litter 8) and plays sound 0xCF when R % 100 is below StinkbombLikelihood (0x007850E0), 101 drops litter R % 5 + 1, 102 bursts the balloon of the first other guest on its cell that holds one (0x004FE950); a prank then records event 15, sends the prank message (type 14 on the bus 0x00788D3C: the advisor and the guards) and adds 1.0 to happiness; the step never returns
	// [APPROX:GUEST-010] steps 5 and 6 are not run: a show object within squared distance 5 (0x501020) or a kind-6 thing in the 3 × 3 cells with the nearest listed object in state 14 (0x500F00, also recording event 0xE) turns the guest when dx + dy is not 0, writing the heading +0x1C and ending its state-6 call before the pranks and the decision (0x4FF0D2..0x4FF0F4), so they are not presentation only; the leaving gate's route test asks whether a path reaches an arrival lane's end (OpenTPW's way out) instead of routing to the two park-side crossing cells (0x4D86D0, the second pass with the path-finder mode +0x188 set, 0x4FEF04) — evidence needed: the path-finder mode word 0x7CF3C0
	public static Outcome Decide( in Input input, Func<bool> litter, Func<bool> leave, Func<Prank, LitterType, bool> prank, Func<bool> chooseRide, Func<bool> wander,
		Func<float>? happiness = null )
	{
		var r = input.Draw;
		if ( unchecked(input.Turn - input.ActivityStamp) > CheerGapTurns && MeterMath.Byte( input.Happiness ) > 80 )
			return Outcome.Cheer;
		var illness = MeterMath.Byte( input.Illness );
		if ( illness > 80 && illness == 100 && r % 3 == 0 )
			return Outcome.Vomit;
		if ( MeterMath.Byte( input.Litter ) >= ParkLitter.MeterThreshold && litter() )
			return Outcome.ToBin;
		if ( GuestLifecycle.ShouldLeave( input.Happiness, input.ExitLevel, input.ParkClosed ) && leave() )
			return Outcome.Leave;
		// The prank test reads happiness again: a leaving gate that found no way out has just taken the big change.
		if ( MeterMath.Byte( happiness?.Invoke() ?? input.Happiness ) < PrankHappinessBelow && (uint)input.PrankFlag > r % 101 )
		{
			switch ( input.PrankFlag )
			{
				case 100:
					if ( r % 100 < unchecked( (uint)input.StinkbombLikelihood ) )
						prank( Prank.Stinkbomb, LitterType.Stinkbomb );
					break;
				case 101:
					prank( Prank.Litter, (LitterType)(r % ParkLitter.OrdinaryKinds + 1) );
					break;
				case 102:
					prank( Prank.Balloon, LitterType.None );
					break;
			}
		}
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

/// <summary>A litter bin for the guests' bin search: its economy object id, its cell and the path cell in front of it that guests walk to.</summary>
public readonly record struct LitterBin( int Id, (int X, int Y) Cell, (int X, int Y) Access );
