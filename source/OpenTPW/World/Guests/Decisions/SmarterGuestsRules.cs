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
}
