namespace OpenTPW;

/// <summary>What kind of attraction the Theme Park Inc price test prices: the type record's derived kind (<c>+0x600</c>) 1 is a shop and 2 a sideshow; every other value is a ride.</summary>
public enum TpiPriceKind
{
	Ride = 0,
	Shop = 1,
	Sideshow = 2,
}

/// <summary>
/// The words Theme Park Inc's price test <c>0x6120C0</c> reads. <paramref name="Price"/> is the thing's price per use (+0x94), <paramref name="CostOfGoods"/> its live goods (+0x88) and
/// <paramref name="WinByte"/> its win byte (+0x90, sideshows); <paramref name="ExpectedPrice"/> is <c>Info.ExpectedPrice</c> (type +0x88), the effects the type words +0x200..+0x20C, <paramref name="TypeWord"/>
/// the per-type record's word +0xC (<c>Info.RipOffOK</c>, copied by 0x5CFF3D), <paramref name="Rating"/> the attraction's rating byte (0x5E41D0, zero when it has none) and
/// <paramref name="PreferredExcitement"/> the guest's expected byte (+0x2A8, <c>PeepTypes[n].PreferredExcitement</c>).
/// </summary>
public readonly record struct TpiPriceTerms( int Price, TpiPriceKind Kind, int CostOfGoods, int WinByte, int ExpectedPrice, int ThirstEffect, int HungerEffect, int VomitEffect, int HappinessEffect,
	int TypeWord, int Rating, int PreferredExcitement, int SpecialIngredient, int AppearanceEffect );

/// <summary>What the price test decided and which of the attraction's daily history rings it raised (+0x5D8 cheap, +0x660 expensive, +0x4C8 declined: <see cref="ThemeParkIncAttractionRing"/>).</summary>
public readonly record struct TpiPriceVerdict( bool TooExpensive, bool CheapPrice, bool ExpensivePrice, bool Declined, (int Category, int Value)[] Statistics );

/// <summary>Theme Park Inc's guest price test as a pure function (Game.exe <c>0x6120C0</c>, called from the called-forward step <c>0x617F80</c> at <c>0x617FBF</c>).</summary>
public static class ThemeParkIncPriceTest
{
	/// <summary>
	/// The willingness to pay, an unsigned word. The needs bytes h (happiness +0x1BC), t (thirst +0x1C8), u (hunger +0x1CC) and i (illness +0x1D4) give
	/// S = Happiness·(100 − h)/100 − Vomit·i/100 + Hunger·u/100 + Thirst·t/100 + 100 (signed divisions); the base is Info.ExpectedPrice, or for a sideshow the live goods × win byte / 100.
	/// </summary>
	// [BIN:TPI-EXE:0x6120C0 willingness] base 0x6120CF..0x612142 (kind +0x600 == 2: goods 0x408A67 times win byte 0x401933 over 100, else type +0x88), the needs sum 0x612144..0x6121FD, S times the base over 100 (unsigned), times (the per-type word 0x404D77 +0xC, plus 100) over 100, times (h + 100) over 100
	public static uint Willingness( in TpiPriceTerms terms, float happiness, float thirst, float hunger, float illness )
	{
		unchecked
		{
			static int Byte( float meter ) => (int)((long)meter & 0xFF);
			var h = Byte( happiness );
			var baseValue = terms.Kind == TpiPriceKind.Sideshow ? (terms.WinByte & 0xFF) * terms.CostOfGoods / 100 : terms.ExpectedPrice;
			var sum = terms.HappinessEffect * (100 - h) / 100 - terms.VomitEffect * Byte( illness ) / 100 + terms.HungerEffect * Byte( hunger ) / 100
				+ terms.ThirstEffect * Byte( thirst ) / 100 + 100;
			var value = (uint)(sum * baseValue) / 100;
			value = (uint)((terms.TypeWord + 100) * (int)value) / 100;
			value = (uint)((int)value * (h + 100)) / 100;
			// [BIN:TPI-EXE:0x612252 rating fit] with a nonzero rating byte (0x5E41D0 through 0x403062): the distance to the guest's expected byte, clamped to 100, raises the willingness by 50 · (100 − distance) / 100 percent
			var rating = terms.Rating & 0xFF;
			if ( rating != 0 )
			{
				var distance = (terms.PreferredExcitement & 0xFF) - rating;
				if ( distance < 0 )
					distance = -distance;
				if ( distance > 100 )
					distance = 100;
				value = (uint)((50 * (100 - distance) / 100 + 100) * (int)value) / 100;
			}
			return value;
		}
	}

	/// <summary>
	/// The test: a price of 0 never fails (0x6120D4); otherwise the guest refuses when the price is above its willingness or above its cash (+0x1C0, unsigned: 0x612430, 0x61243A).
	/// <paramref name="cheapPercent"/> is <c>PeepInfo.PcOfGoodPriceIsCheap</c> (0xA01B18).
	/// </summary>
	// [BIN:TPI-EXE:0x612430 decision] price above the willingness (ja) or the guest's cash below the price (jb) refuses: the expensive ring +0x660 rises, and +0x4C8 as well for an Info.WhichUIType 2 type (0x612473..0x612488); otherwise PcOfGoodPriceIsCheap percent of the willingness above the price raises the cheap ring +0x5D8 (0x612442..0x61245D)
	public static TpiPriceVerdict Decide( in TpiPriceTerms terms, float happiness, float thirst, float hunger, float illness, long money, int cheapPercent )
	{
		var price = unchecked( (uint)terms.Price );
		if ( price == 0 )
			return new TpiPriceVerdict( false, false, false, false, [] );
		var willingness = Willingness( terms, happiness, thirst, hunger, illness );
		var statistics = Statistics( terms, willingness );
		if ( price > willingness || unchecked( (uint)money ) < price )
			return new TpiPriceVerdict( true, false, true, terms.Kind == TpiPriceKind.Sideshow, statistics );
		var cheap = unchecked( (uint)((uint)cheapPercent * (ulong)willingness / 100UL) ) > price;
		return new TpiPriceVerdict( false, cheap, false, false, statistics );
	}

	/// <summary>
	/// The perception values the test records for a shop or a sideshow, paid or refused: min(100, 10·W + 50 − 10·price) as an unsigned word. A sideshow records in category 10; a shop in the
	/// category of its special ingredient (1..4) and, for an appearance effect 1..3, in 6, 7 or 8. A ride records none.
	/// </summary>
	// [BIN:TPI-EXE:0x6122C4 statistics] kind 2: 0x6122DB (category 0xA); kind 1: the ingredient jump table 0x6124A0 (type +0x218: 1..4 record in that category) then +0x21C 1, 2, 3 in 6, 7, 8 (0x6123B0..0x612426), each through the park (0x40144C) and the ring record 0x5AEDE0
	public static (int Category, int Value)[] Statistics( in TpiPriceTerms terms, uint willingness )
	{
		if ( terms.Kind == TpiPriceKind.Ride )
			return [];
		var price = unchecked( (uint)terms.Price );
		var perceived = unchecked( willingness * 10 + 50 - price * 10 );
		var value = perceived >= 100 ? 100 : (int)perceived;
		if ( terms.Kind == TpiPriceKind.Sideshow )
			return [(10, value)];
		var shop = new List<(int, int)>( 2 );
		if ( terms.SpecialIngredient is >= 1 and <= 4 )
			shop.Add( (terms.SpecialIngredient, value) );
		if ( terms.AppearanceEffect is >= 1 and <= 3 )
			shop.Add( (terms.AppearanceEffect + 5, value) );
		return shop.ToArray();
	}
}
