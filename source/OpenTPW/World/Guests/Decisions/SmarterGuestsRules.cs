namespace OpenTPW;

/// <summary>
/// The constants and pure helpers of the smarter guests' rules (docs/SMARTER-GUESTS.md). Every number here is OpenTPW's own, tuned against the measurement of that page; none comes from
/// the original game or from OpenRCT2. The version of the <c>opentpw.smarter-guests</c> extension is the identity of this set: changing a constant changes behaviour and bumps the version.
/// </summary>
// [EXT:smarter-guests-value] phase 2, value for money
public static class SmarterGuestsRules
{
	/// <summary>
	/// How many price-emptied decisions in a row send a guest home: its price rule turned away the stands it wanted, and it found nothing else, this many times. The guest goes home with the "too expensive"
	/// thought (the original's thought 6) instead of wandering and trying again. Tuned by measurement (docs/SMARTER-GUESTS.md): 1 gives the fewest stuck guests and the shortest waits of 1, 2, 3 and 5.
	/// </summary>
	public const int StallsBeforeGoingHome = 1;

	/// <summary>The index of the price sensitivity trait (<see cref="DecisionContext.Trait"/>).</summary>
	public const int PriceSensitivityTrait = 0;

	/// <summary>The least price sensitivity, in percent: the guest judges a price by willingness × 100 / sensitivity, so 90 is a guest who thinks a stand cheaper than average.</summary>
	public const int SensitivityMinimumPercent = 90;

	/// <summary>How many distinct sensitivities the trait gives: 90 to 110 percent.</summary>
	public const int SensitivityValues = 21;

	/// <summary>The booth's value of a park with no open ride or sideshow, in percent of the rating-based part of the park value: the original's, so the rule never judges a park harsher than the original does.</summary>
	public const int BoothVarietyBasePercent = 100;

	/// <summary>What each open ride or sideshow adds to the booth's percentage.</summary>
	public const int BoothVarietyStepPercent = 3;

	/// <summary>The open rides and sideshows beyond which the booth sees no more variety: 100 + 5 × 3 = 115 percent at most.</summary>
	public const int BoothVarietyCap = 5;

	/// <summary>The price sensitivity of a guest, in percent, from its stateless trait word: 90 to 110, the same for the guest's whole visit.</summary>
	public static int SensitivityPercent( ulong trait ) => SensitivityMinimumPercent + (int)(trait % SensitivityValues);

	/// <summary>Whether the attraction carries a price the stand-point test applies: a shop or sideshow with a price above 0.</summary>
	public static bool IsPriced( in AttractionView attraction ) => attraction.Kind is RideVisitorKind.Shop or RideVisitorKind.Sideshow && attraction.Price != 0;

	/// <summary>
	/// Whether a guest with this sensitivity expects the stand to refuse it: the stand-point test's own rule (<see cref="QueueJoinRules.TooExpensive"/>: the price is above the willingness to pay or above the
	/// guest's money, unsigned), with the willingness divided by the sensitivity.
	/// </summary>
	public static bool WouldRefuse( in AttractionView attraction, int money, int sensitivityPercent )
	{
		var price = unchecked( (uint)attraction.Price );
		var perceived = (uint)Math.Min( uint.MaxValue, (ulong)attraction.Willingness * 100UL / (ulong)Math.Max( 1, sensitivityPercent ) );
		return !(price <= perceived && price <= unchecked( (uint)money ));
	}

	/// <summary>
	/// The park value the booth weighs the fee against: the original's value (<see cref="BoothVerdict.ParkValue"/>) with its rating part (the value above the minimum entry fee) scaled by the number of open
	/// rides and sideshows: <c>BoothVarietyBasePercent + BoothVarietyStepPercent × min(open, BoothVarietyCap)</c> percent, 100 to 115. A park with no rating, or with none open, keeps the original value; a park of
	/// many rides is judged a little kinder, never harsher.
	/// </summary>
	public static uint BoothParkValue( uint originalValue, int openAttractions, GuestSettings settings )
	{
		var minimum = (uint)Math.Max( 0, settings.MinimumEntryFee );
		if ( originalValue <= minimum )
			return originalValue;
		var percent = (ulong)(BoothVarietyBasePercent + BoothVarietyStepPercent * Math.Clamp( openAttractions, 0, BoothVarietyCap ));
		var scaled = (ulong)(originalValue - minimum) * percent / 100UL;
		return (uint)Math.Min( uint.MaxValue, minimum + scaled );
	}

	// [EXT:smarter-guests-spread] phase 3, spreading

	/// <summary>
	/// The expected wait, in park turns, at which the queue match reaches 0 (the match is <c>100 - 100 * wait / scale</c>, clamped to 0..100). Tuned by measurement (docs/SMARTER-GUESTS.md): the original's queue match
	/// goes from 100 (empty) to 0 at four guests per queue cell, which for a ride that boards one guest per cycle is a wait of several cycles.
	/// </summary>
	public const int QueueWaitScaleTurns = 120;

	/// <summary>The index of the per-guest, per-attraction preference trait (<see cref="DecisionContext.Trait"/>).</summary>
	public const int PreferenceTrait = 1;

	/// <summary>The least a guest's preference multiplies an attraction's score by, in percent.</summary>
	public const int PreferenceMinimumPercent = 94;

	/// <summary>How many distinct preferences the term gives: 94 to 106 percent.</summary>
	public const int PreferenceValues = 13;

	/// <summary>How much better, in percent of the target's score, a candidate must score for a walking guest to switch to it (the minor decision's hysteresis).</summary>
	public const int HysteresisPercent = 20;

	/// <summary>The least margin of the hysteresis, in score points, so a small score does not switch on a one-point difference.</summary>
	public const int HysteresisMinimumPoints = 2;

	/// <summary>A candidate kept at the choice floor ranks as the floor plus its own score over this: 0 to 1 for a score below 10.</summary>
	public const uint FloorOrderDivisor = 5;

	/// <summary>The guests ahead beyond which the expected wait no longer grows: far above any queue limit.</summary>
	public const long MaximumGuestsAhead = 100_000;

	/// <summary>
	/// The expected wait of a guest who joins now, in 1/256 park turn: the guests ahead of it (the queue plus the guests walking to the attraction), each taking the attraction's observed time per guest
	/// (<see cref="AttractionView.TurnsPerGuest256"/>, i.e. divided by its throughput). The guest itself is not counted: an empty attraction with nobody on the way is a wait of 0. Null when the view has no estimate (0).
	/// </summary>
	public static long? ExpectedWait256( in AttractionView attraction )
	{
		if ( attraction.TurnsPerGuest256 <= 0 )
			return null;
		// bounded before it is multiplied, so a corrupt count or interval saturates instead of wrapping
		var ahead = Math.Min( (long)Math.Max( 0, attraction.QueueLength ) + Math.Max( 0, attraction.Reservations ), MaximumGuestsAhead );
		return Math.Min( ahead * Math.Min( attraction.TurnsPerGuest256, RideThroughputs.MaximumInterval256 ), RideThroughputs.MaximumInterval256 * 4 );
	}

	/// <summary>The queue match (0..100) of an expected wait in 1/256 turn: 100 for no wait, falling linearly to 0 at <see cref="QueueWaitScaleTurns"/>.</summary>
	public static uint QueueMatchOf( long wait256 ) => (uint)(100 - Math.Min( 100L, wait256 * 100 / (QueueWaitScaleTurns * 256L) ));

	/// <summary>The preference of a guest for one attraction instance, in percent (94 to 106): its stateless preference trait mixed with the attraction's stable id; the same for the guest's whole visit.</summary>
	public static int PreferencePercent( ulong trait, int attractionId ) =>
		PreferenceMinimumPercent + (int)(SmarterGuestsState.Mix( trait ^ SmarterGuestsState.Mix( (ulong)(uint)attractionId + 0x9E3779B97F4A7C15UL ) ) % PreferenceValues);

	/// <summary>A score scaled by a preference in percent (rounded down); 0 stays 0.</summary>
	public static uint ApplyPreference( uint score, int percent ) => (uint)Math.Min( uint.MaxValue, (ulong)score * (ulong)percent / 100UL );

	/// <summary>
	/// Whether a candidate replaces the target a guest is walking to: the target scores below the choice floor (nothing to keep), or the candidate beats it by <c>max(HysteresisMinimumPoints, targetScore * HysteresisPercent / 100)</c>.
	/// </summary>
	public static bool BeatsTargetByMargin( uint targetScore, uint candidateScore )
	{
		if ( targetScore < AttractionScore.CandidateFloor )
			return true;
		var margin = Math.Max( (ulong)HysteresisMinimumPoints, (ulong)targetScore * HysteresisPercent / 100UL );
		return (ulong)candidateScore >= (ulong)targetScore + margin;
	}

	// [EXT:smarter-guests-patience] phase 4, queue patience

	/// <summary>The index of the patience trait (<see cref="DecisionContext.Trait"/>).</summary>
	public const int PatienceTrait = 2;

	/// <summary>
	/// The expected wait, in park turns, a guest of average patience accepts when it joins a queue. Derived from the measured baseline (docs/SMARTER-GUESTS.md, phase 4), not from the original's boredom threshold
	/// (whose unit is not proven and whose test cannot fire): the original guests' boarded waits in the scenario have a p90 of 73 and a p99 of 108 turns, so 90 is a wait the original park gives to its unluckiest tenth of guests.
	/// </summary>
	public const int JoinPatienceTurns = 90;

	/// <summary>The total time, in park turns from joining, an average guest waits before it gives up. Above <see cref="JoinPatienceTurns"/> and above the baseline's p99 of 108, so the rule only touches waits the original does not produce.</summary>
	public const int GiveUpTurns = 120;

	/// <summary>The least a guest's patience is, in percent of the base: 70 to 130 percent.</summary>
	public const int PatienceMinimumPercent = 70;

	/// <summary>How many distinct patience values the trait gives: 70 to 130 percent.</summary>
	public const int PatienceValues = 61;

	/// <summary>The patience of a guest, in percent of the base (70 to 130), from its stateless trait word; the same for the whole visit.</summary>
	public static int PatiencePercent( ulong trait ) => PatienceMinimumPercent + (int)(trait % PatienceValues);

	/// <summary>A base number of park turns scaled by a patience percentage, in 1/256 turn (the unit of <see cref="ExpectedWait256"/>).</summary>
	public static long PatienceTurns256( int baseTurns, int patiencePercent ) => (long)baseTurns * patiencePercent * 256L / 100L;

	/// <summary>The patience rules concern rides only: a shop, sideshow or toilet is a service the guest wants for a reason no other attraction answers, and the alternatives the rules weigh are rides.</summary>
	public static bool Concerns( in AttractionView attraction ) => attraction.Kind == RideVisitorKind.Ride;

	/// <summary>A ride that is in neither of the guest's histories, is not of the type it used last and whose excitement the admission gate would let it through to: a ride the guest could choose next.</summary>
	public static bool IsUnusedAdmissibleRide( in DecisionContext context, in GuestView guest, in AttractionView attraction )
	{
		if ( attraction.Kind != RideVisitorKind.Ride || attraction.SameTypeAsLastUsed || guest.UsedHistory.Contains( attraction.Id ) || guest.TemporaryHistory.Contains( attraction.Id ) )
			return false;
		var type = context.Settings.Types[Math.Min( guest.Type, context.Settings.Types.Count - 1 )];
		return AttractionScore.ExcitementMatch( type.PreferredExcitement, attraction.LiveExcitement ) > 10;
	}

	/// <summary>Whether the expected wait of a guest joining the back of this queue is above its patience (no estimate: it is not).</summary>
	public static bool WaitExceedsPatience( in AttractionView attraction, long bestAlternative256, int patiencePercent ) =>
		ExpectedWait256( attraction ) is { } wait && wait > PatienceTurns256( JoinPatienceTurns, patiencePercent ) && IsClearlyShorter( bestAlternative256, wait );

	/// <summary>Whether another ride's wait is clearly shorter: it exists (not -1) and is at most <see cref="AlternativePercent"/> percent of <paramref name="wait256"/>.</summary>
	public static bool IsClearlyShorter( long bestAlternative256, long wait256 ) => bestAlternative256 >= 0 && bestAlternative256 * 100 <= wait256 * AlternativePercent;

	/// <summary>How much of the wait here another ride's wait may be, in percent, for it to count as a reason to leave or not to join.</summary>
	public const int AlternativePercent = 80;


	/// <summary>Whether a guest that has waited <paramref name="waitedTurns"/> park turns in a queue, at <paramref name="position"/> (0 is the front), gives up.</summary>
	public static bool GivesUp( long waitedTurns, in AttractionView attraction, int position, long bestAlternative256, int patiencePercent ) =>
		position > 0 && waitedTurns * 256L > PatienceTurns256( GiveUpTurns, patiencePercent )
		&& attraction.TurnsPerGuest256 > 0 && IsClearlyShorter( bestAlternative256, position * Math.Min( attraction.TurnsPerGuest256, RideThroughputs.MaximumInterval256 ) );

	// [EXT:smarter-guests-planning] phase 5, planning needs

	/// <summary>
	/// The longest look ahead of the planning rule, in park turns: the give-up patience of the queue rules (<see cref="GiveUpTurns"/>, a guest does not wait longer than this) plus one default ride cycle
	/// (<see cref="RideThroughputs.DefaultCycleTurns"/>). A commitment longer than that is not a plan, the guest would have left the queue first.
	/// </summary>
	public const int PlanningCapTurns = GiveUpTurns + RideThroughputs.DefaultCycleTurns;

	/// <summary>
	/// The toilet level the planning looks at: the byte above which a queueing guest leaves for a ride that is not a toilet (<see cref="GuestSimulation.ToiletLeaveLevel"/>, tp.exe <c>0x005003AA</c>), the first level at
	/// which the toilet need costs the guest something it cannot avoid.
	/// </summary>
	public const int ToiletSeekLevel = (int)GuestSimulation.ToiletLeaveLevel;

	/// <summary>
	/// The hunger and thirst level the planning looks at: the byte 100, where the needs block charges the guest happiness every phase turn (<see cref="NeedsUpdate.FullNeedPenalty"/>) and the thoughts "hungry" and
	/// "thirsty" start (<see cref="GuestThoughts.Pick(float, float, float, float, float, float)"/>: above 99).
	/// </summary>
	public const int FullNeedLevel = 100;

	/// <summary>
	/// What the detour adds to the hunger or thirst a service on the guest's way serves, in meter points: one step of the original's match table, which reads the need in tens (<see cref="AttractionScore.NeedMatch"/>).
	/// At a need of 60 and a shop effect of 40 that is about 1 to 2 points of score; below a need of 20 it is none.
	/// </summary>
	public const int DetourStep = 10;

	/// <summary>
	/// What the detour adds to the toilet a relief object on the guest's way serves: one step of the relief curve, which the original indexes in fives (<c>(byte + 4) / 5</c>, <see cref="AttractionScore.ReliefMatch"/>), so 5 and not
	/// the 10 of the tens table (which would move two entries of the curve, 60 to 100 at a need of 81).
	/// </summary>
	public const int ReliefDetourStep = 5;

	/// <summary>
	/// The cycle of an attraction for the plan, in 1/256 turn: its time per guest times its capacity, capped at a nominal cycle (<see cref="RideThroughputs.DefaultCycleTurns"/>). The product overestimates for a ride that
	/// boards partly loaded or has stalled (the interval is then the gap per guest of a half-empty cycle, or the silence since the last boarding), and the plan is a lower bound on how long the guest is busy: it errs short, so
	/// the rule acts only when even that leaves a need to cross. A ride whose real cycle is longer than the nominal one is therefore read short too.
	/// </summary>
	public static long CycleTurns256Of( long interval256, int capacity ) =>
		Math.Min( interval256 * Math.Max( 1, capacity ), RideThroughputs.DefaultCycleTurns * 256L );

	/// <summary>
	/// How long after a meal the stomach is still full, in park turns: the 20 hunger points a typical meal takes off (the shipped Fries Shop's <c>HungerEffect</c>; burgers and restaurants 25, ice cream 15) grow back at the
	/// only rate the needs block has, 2 points per growth step of 16 turns, so 20 / 2 x 16 = 160 turns. That rate belongs to one guest in four (<see cref="NeedsUpdate.GrowthSteps"/>); for the others the meter does not grow back
	/// on its own and the window is the rule's own limit.
	/// </summary>
	public const int MealWindowTurns = 20 * (NeedsUpdate.GrowthMask + 1) / (int)NeedsUpdate.ThirstHungerGrowth;

	/// <summary>
	/// The illness a ride has to add, in meter points, for the meal rule to find it too much: a quarter of the meter, so four such rides in a row make a guest vomit (the vomit test is a meter byte of 100,
	/// <see cref="GuestDecision.Decide"/>). The ride's gain is the original's own (<see cref="RideOutcome.IllnessGain"/>: its excitement over <see cref="GuestSettings.RideVomitDivisor"/> times how empty the stomach is not).
	/// </summary>
	public const int MealGainLimit = 25;

	/// <summary>The divisor of a ride's excess over the choice floor when it is too much after a meal: 2, the weakest of the original's own history divisors (the used history's fourth slot divides by 2).</summary>
	public const uint MealScoreDivisor = 2;

	/// <summary>The attraction serves a need the planning looks at: a shop with a thirst or hunger effect, or a relief object.</summary>
	public static bool ServesNeed( in AttractionView attraction ) => attraction.ThirstEffect > 0 || attraction.HungerEffect > 0 || attraction.ProvidesRelief;

	/// <summary>A hunger or thirst meter that is below the full level now and at or above it then.</summary>
	public static bool CrossesFullNeed( float now, float then ) => MeterMath.Byte( now ) < FullNeedLevel && MeterMath.Byte( then ) >= FullNeedLevel;

	/// <summary>A toilet meter that is at or below the toilet level now and above it then.</summary>
	public static bool CrossesToiletLevel( float now, float then ) => MeterMath.Byte( now ) <= ToiletSeekLevel && MeterMath.Byte( then ) > ToiletSeekLevel;

	/// <summary>The meters of <paramref name="guest"/> (the growth of the needs block only: the projection leaves out the environment words of the guest's cell that the same block adds to hunger, illness and happiness (RegionFX entertainers, vomit and the like), which are 0 on most cells and of either sign where they are not, so the projection can err either way there and the plan is approximate) after <paramref name="turns"/> more park turns of the needs block's growth (<see cref="NeedsUpdate.GrowthSteps"/>), from the park turn <paramref name="fromTurn"/>.</summary>
	public static (float Thirst, float Hunger, float Toilet) ProjectedMeters( in GuestView guest, long fromTurn, long turns )
	{
		var steps = NeedsUpdate.GrowthSteps( guest.Id, fromTurn, turns );
		return (MeterMath.Add( guest.Thirst, steps * NeedsUpdate.ThirstHungerGrowth ), MeterMath.Add( guest.Hunger, steps * NeedsUpdate.ThirstHungerGrowth ), MeterMath.Add( guest.Toilet, steps * NeedsUpdate.ToiletGrowth ));
	}

	/// <summary>
	/// Whether any meter of the guest will cross the level the planning looks at within the longest look ahead (<see cref="PlanningCapTurns"/>). The host asks it before it scans the park's rides for a commitment,
	/// so the scan is paid only by a guest the rule can act on: with the needs block's growth that is a guest whose id is a multiple of 4 and whose hunger or thirst is within 22 to 24 of 100 or whose toilet is within
	/// 11 to 12 of 80 (11 or 12 growth steps in 180 turns, by where the multiples of 16 fall), and nobody else.
	/// </summary>
	public static bool MayCross( in GuestView guest, long parkTurn )
	{
		// only a guest whose thing id is a multiple of 4 ever grows (NeedsUpdate.GrowthSteps), so no other guest has a meter that will cross
		if ( (guest.Id & NeedsUpdate.PhaseMask) != 0 )
			return false;
		var (thirst, hunger, toilet) = ProjectedMeters( guest, parkTurn, PlanningCapTurns );
		return CrossesFullNeed( guest.Thirst, thirst ) || CrossesFullNeed( guest.Hunger, hunger ) || CrossesToiletLevel( guest.Toilet, toilet );
	}

	/// <summary>The look ahead of a commitment of <paramref name="planTurns256"/> (1/256 turn), in whole park turns rounded up and capped at <see cref="PlanningCapTurns"/>; 0 for none (-1).</summary>
	public static long PlanningTurns( long planTurns256 ) => planTurns256 < 0 ? 0 : Math.Min( PlanningCapTurns, (planTurns256 + 255) / 256 );

	/// <summary>
	/// The guest as it scores <paramref name="attraction"/>: for a service, the needs it serves are the planned ones. With <paramref name="planning"/>, a need that will cross its level before the guest's commitment
	/// ends (<see cref="GuestView.PlanTurns256"/>) is read at the value it will have by then; with <paramref name="detour"/>, a service on the way (<see cref="AttractionView.OnTheWay"/>) has each need it serves read
	/// <see cref="DetourStep"/> points higher (at most 100). Everything else, and every ride, is scored on the guest as it is. Pure, and the guest itself is not changed.
	/// </summary>
	public static GuestView Planned( in GuestView guest, in AttractionView attraction, long parkTurn, bool planning, bool detour )
	{
		if ( !ServesNeed( attraction ) || (!detour || !attraction.OnTheWay) && (!planning || guest.PlanTurns256 < 0) )
			return guest;
		float thirst = guest.Thirst, hunger = guest.Hunger, toilet = guest.Toilet;
		if ( planning && guest.PlanTurns256 >= 0 )
		{
			var (thirstThen, hungerThen, toiletThen) = ProjectedMeters( guest, parkTurn, PlanningTurns( guest.PlanTurns256 ) );
			if ( attraction.ThirstEffect > 0 && CrossesFullNeed( thirst, thirstThen ) )
				thirst = thirstThen;
			if ( attraction.HungerEffect > 0 && CrossesFullNeed( hunger, hungerThen ) )
				hunger = hungerThen;
			if ( attraction.ProvidesRelief && CrossesToiletLevel( toilet, toiletThen ) )
				toilet = toiletThen;
		}
		if ( detour && attraction.OnTheWay )
		{
			if ( attraction.ThirstEffect > 0 )
				thirst = MeterMath.Add( thirst, DetourStep );
			if ( attraction.HungerEffect > 0 )
				hunger = MeterMath.Add( hunger, DetourStep );
			if ( attraction.ProvidesRelief )
				toilet = MeterMath.Add( toilet, ReliefDetourStep );
		}
		return thirst == guest.Thirst && hunger == guest.Hunger && toilet == guest.Toilet ? guest : guest with { Thirst = thirst, Hunger = hunger, Toilet = toilet };
	}

	/// <summary>Whether the park turn is within <see cref="MealWindowTurns"/> after the guest's last meal (<paramref name="mealTurn"/>, -1 for none).</summary>
	public static bool InMealWindow( long parkTurn, long mealTurn ) => mealTurn >= 0 && parkTurn >= mealTurn && parkTurn - mealTurn < MealWindowTurns;

	/// <summary>The illness the ride would add to this guest now (<see cref="RideOutcome.IllnessGain"/>, the original's formula over the guest's own stomach).</summary>
	public static int RideIllnessGain( in DecisionContext context, in GuestView guest, in AttractionView ride ) =>
		RideOutcome.IllnessGain( guest.Hunger, ride.LiveExcitement, context.Settings.RideVomitDivisor );

	/// <summary>A ride that, shortly after the guest's meal, would add at least <see cref="MealGainLimit"/> illness.</summary>
	public static bool TooMuchAfterMeal( in DecisionContext context, in GuestView guest, in AttractionView ride ) =>
		ride.Kind == RideVisitorKind.Ride && InMealWindow( context.ParkTurn, guest.MealTurn ) && RideIllnessGain( context, guest, ride ) >= MealGainLimit;

	/// <summary>
	/// A score brought halfway (<see cref="MealScoreDivisor"/>) to the choice floor: <c>floor + (score - floor) / 2</c> for a score at or above it, so a candidate stays a candidate (a guest that has nothing else to do still
	/// rides, as in the other rules that lower a score), never ends above the score it had, and two rides keep their order (a score of 40 ends at 25, 50 at 30). A score below the floor was nothing to keep and stays.
	/// </summary>
	public static uint LowerAfterMeal( uint score ) =>
		score < AttractionScore.CandidateFloor ? score : AttractionScore.CandidateFloor + (score - AttractionScore.CandidateFloor) / MealScoreDivisor;
}
