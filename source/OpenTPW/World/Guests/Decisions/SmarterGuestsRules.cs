namespace OpenTPW;

/// <summary>
/// The constants and pure helpers of the smarter guests' rules (docs/SMARTER-GUESTS.md). Every number here is OpenTPW's own, tuned against the measurement of that page; none comes from
/// the original game or from OpenRCT2. The version of the <c>opentpw.smarter-guests</c> extension is the identity of this set: changing a constant changes behaviour and bumps the version.
/// </summary>
// [EXT:smarter-guests-value] phase 2, value for money
public static class SmarterGuestsRules
{
	/// <summary>How long a guest remembers a price refusal at an attraction: 2 in-game days of 23 park turns.</summary>
	public const int RefusalMemoryTurns = 46;

	/// <summary>The most refusals one guest remembers at a time; a newer one pushes out the one that ends soonest.</summary>
	public const int MaxRefusalsPerGuest = 4;

	/// <summary>The index of the price sensitivity trait (<see cref="DecisionContext.Trait"/>).</summary>
	public const int PriceSensitivityTrait = 0;

	/// <summary>The least price sensitivity, in percent: the guest judges a price by willingness × 100 / sensitivity, so 90 is a guest who thinks a stand cheaper than average.</summary>
	public const int SensitivityMinimumPercent = 90;

	/// <summary>How many distinct sensitivities the trait gives: 90 to 110 percent.</summary>
	public const int SensitivityValues = 21;

	/// <summary>The booth's value of a park with no open attraction, in percent of the rating-based part of the park value.</summary>
	public const int BoothVarietyBasePercent = 85;

	/// <summary>What each open attraction adds to the booth's percentage.</summary>
	public const int BoothVarietyStepPercent = 5;

	/// <summary>The open attractions beyond which the booth sees no more variety: 85 + 6 × 5 = 115 percent at most.</summary>
	public const int BoothVarietyCap = 6;

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
	/// attractions: <c>BoothVarietyBasePercent + BoothVarietyStepPercent × min(open, BoothVarietyCap)</c> percent, 85 to 115. A park with no rating keeps the original value; a park of few
	/// attractions is judged a little harsher and one of many a little kinder.
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
