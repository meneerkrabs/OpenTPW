namespace OpenTPW;

/// <summary>The sideshow words the live excitement reads: ride <c>+388</c> goods, <c>+400</c> price and <c>+396</c> win (docs/reverse/ORACLE.md F16).</summary>
/// <param name="CostOfGoods"><c>UsageInfo.InitCostOfGoods</c>, the prize, editable in play.</param>
/// <param name="PricePerUse"><c>UsageInfo.InitPricePerUse</c>, editable in play.</param>
/// <param name="ChanceOfLosingPercent"><c>UsageInfo.InitChanceOfLoosing</c>; the original keeps the win chance 100 − this as a byte.</param>
public readonly record struct SideshowTerms( int CostOfGoods, int PricePerUse, int ChanceOfLosingPercent );

/// <summary>
/// The original's join excitement rules as pure functions (docs/reverse/ORACLE.md F15 and F16, QUEUE-plan 4): the
/// relief predicate <c>0xdfe34</c>, the live excitement <c>0xe02ec(ride, 0)</c> with its formulas <c>0xdff70</c>, the
/// difference <c>0xe9a84</c> and the gate of the state-10 handler <c>0xeccb0</c>. <see cref="RideVisitorBridge"/>
/// feeds them from the object's data and live state; the oracle bridge calls them through a real bridge.
/// </summary>
/// <summary>The words the price test reads: the live price, cost of goods and win byte, the type record's InitCostOfGoods and effects, and the park's per-type word.</summary>
public readonly record struct PriceTerms( int Price, int CostOfGoods, int WinByte, bool IsSideshow, int InitCostOfGoods, int ThirstEffect, int HungerEffect,
	int VomitEffect, int HappinessEffect, int TypeWord );

public static class QueueJoinRules
{
	/// <summary>
	/// The price test when a guest is called forward (tp.exe <c>0x004FDE50</c>, from the state-13 handler <c>0x005006B0</c>; ORACLE.md X16):
	/// a price of 0 never fails; otherwise the guest refuses when the price is above its willingness to pay or above its money.
	/// </summary>
	// [BIN:TPW-EXE:0x004FDE50 price test] the willingness: needs as bytes h, t, u, i (happiness, thirst, hunger, illness); S = HappinessEffect·(100 − h)/100 + ThirstEffect·t/100 + HungerEffect·u/100 − VomitEffect·i/100 + 100 (signed divisions); × InitCostOfGoods·115/100, unsigned /100; plus, for a sideshow, cost of goods × win byte / 100; × (the park's per-type word + 100), unsigned /100; × (h + 100), unsigned /100. Price and money compare unsigned. Executed by the x86 oracle target tpw-exe-price-test
	public static bool TooExpensive( in PriceTerms terms, float happiness, float thirst, float hunger, float illness, long money )
	{
		var price = unchecked( (uint)terms.Price );
		if ( price == 0 )
			return false;
		var willingness = WillingnessToPay( terms, happiness, thirst, hunger, illness );
		return !(price <= willingness && price <= unchecked( (uint)money ));
	}

	/// <summary>The willingness to pay of <see cref="TooExpensive"/>, an unsigned 32-bit word.</summary>
	public static uint WillingnessToPay( in PriceTerms terms, float happiness, float thirst, float hunger, float illness )
	{
		unchecked
		{
			int Byte( float meter ) => (int)((long)meter & 0xFF);
			var h = Byte( happiness );
			var total = terms.HappinessEffect * (100 - h) / 100 + terms.ThirstEffect * Byte( thirst ) / 100 + terms.HungerEffect * Byte( hunger ) / 100
				- terms.VomitEffect * Byte( illness ) / 100 + 100;
			var value = (uint)(total * (terms.InitCostOfGoods * 115 / 100)) / 100;
			var sideshow = terms.IsSideshow ? terms.CostOfGoods * (terms.WinByte & 0xFF) / 100 : 0;
			value = (uint)((sideshow + (int)value) * (terms.TypeWord + 100)) / 100;
			return (uint)((int)value * (h + 100)) / 100;
		}
	}

	/// <summary>
	/// The queue part of the park points counts-test (tp.exe <c>0x004DD920</c>, ORACLE.md X15): the attraction has real queue cells
	/// (<c>0x004DE130</c> walks them from the front cell at the entrance; OpenTPW's one-cell fallback on a path cell does not count)
	/// and its queue list is shorter than 4 × those cells (<c>0x004DDF50</c> with guest 0 returns the list's length).
	/// </summary>
	public static bool QueueCountsForParkPoints( IRideVisitorBridge attraction, GuestPathGrid grid )
	{
		var cells = 0;
		foreach ( var (x, y) in attraction.QueueCells )
		{
			if ( grid.IsQueue( x, y ) )
				cells++;
		}
		return cells > 0 && attraction.QueueLength < RideVisitorBridge.PositionsPerCell * cells;
	}

	/// <summary>A refusal needs |difference| at least this.</summary>
	// [BIN:STP-PPC:0x100ECDA8 join gate] cmpwi 45 on the signed difference, taken by "bc 4,0" (the unclamped limit of ±50 leaves 45 reachable)
	public const int Gate = 45;
	/// <summary>The difference the original gives while the relief predicate holds, which refuses as "too exciting".</summary>
	// [BIN:STP-PPC:0x100E9A84 join difference] 0xdfe34 set: the result is 100 and no excitement is computed
	public const int FailedDifference = 100;
	/// <summary>A relief object whose wear gauge, truncated to a byte, is below this counts as failed.</summary>
	// [BIN:STP-PPC:0x100DFE34 ride failure] the low byte of the truncated float +64 is compared unsigned with 25
	public const int WornGauge = 25;

	/// <summary>
	/// <c>fctiwz</c>: truncation to a 32-bit integer that saturates, with NaN giving <c>0x80000000</c> (the C# cast is
	/// undefined there and differs between CPUs).
	/// </summary>
	public static int Truncate( double value )
	{
		if ( double.IsNaN( value ) )
			return int.MinValue;
		if ( value >= 2147483648.0 )
			return int.MaxValue;
		if ( value <= -2147483649.0 )
			return int.MinValue;
		return (int)value;
	}

	/// <summary>
	/// <c>0xdfe34</c> (QUEUE-008): the object is a relief object (<c>UsageInfo.ProvidesRelief</c>, ride <c>+46</c> bit 1)
	/// whose wear gauge <c>+64</c> (the state of repair) truncated and taken as a byte is below 25. It never reads the script.
	/// </summary>
	public static bool ReliefFailed( bool providesRelief, float wearGauge ) =>
		providesRelief && (Truncate( wearGauge ) & 0xFF) < WornGauge;

	/// <summary>
	/// A sideshow's excitement (<c>0xdff70</c>, <c>Info.WhichUIType</c> 2): <c>20 + trunc(0.8f · (win · sqrt(clamp(goods − price, 0, 100)) / 10))</c>,
	/// the product and quotient in double precision and the 0.8 factor in single precision.
	/// </summary>
	// [BIN:STP-PPC:0x100DFF70 sideshow excitement] win is the byte +396 (100 − ChanceOfLosing), goods +388 and price +400 are words; the sqrt import takes a double; 0.8f is TOC −11112
	public static int SideshowExcitement( SideshowTerms terms )
	{
		var win = (100 - terms.ChanceOfLosingPercent) & 0xFF;
		var margin = Math.Clamp( terms.CostOfGoods - terms.PricePerUse, 0, 100 );
		var shown = (float)(win * Math.Sqrt( margin ) / 10.0);
		return Truncate( (float)(0.8f * shown) ) + 20;
	}

	/// <summary>
	/// Any other ride or object (<c>0xdff70</c>): the data level scaled by the speed and duration ratios, each clamped to
	/// 0.75..1.25, in single precision and truncated. A zero divisor gives infinity (clamped) or NaN (truncated to 0x80000000).
	/// </summary>
	// [BIN:STP-PPC:0x100DFF70 ride excitement] base · clamp(speed / InitSpeed) · clamp(duration / InitDuration); speed is the word +84, duration the byte +88
	public static int ScaledExcitement( int level, int speed, int initialSpeed, int duration, int initialDuration )
	{
		var speedRatio = ClampRatio( (float)speed / (float)initialSpeed );
		var durationRatio = ClampRatio( (float)(duration & 0xFF) / (float)initialDuration );
		return Truncate( (float)((float)level * (float)(speedRatio * durationRatio)) );
	}

	/// <summary>Clamps to 0.75..1.25 with the original's compares: NaN is neither below nor above and passes.</summary>
	private static float ClampRatio( float ratio ) => ratio < 0.75f ? 0.75f : ratio > 1.25f ? 1.25f : ratio;

	/// <summary>
	/// <c>0xe9a84</c> (QUEUE-004): 100 while the relief predicate holds, else <c>clamp(excitement − preferred, −50, 50)</c> with
	/// both taken as bytes. A negative value is a ride below the guest's preference.
	/// </summary>
	public static int Difference( bool reliefFailed, int excitement, int preferredExcitement ) =>
		reliefFailed ? FailedDifference : Math.Clamp( (excitement & 0xFF) - (preferredExcitement & 0xFF), -50, 50 );

	/// <summary>
	/// The gate of the state-10 handler: a guest is tested only when the low byte of the type's <c>UsageInfo.ExcitementLevel</c>
	/// is nonzero (<c>0xe02ec(ride, 1)</c>), not by kind; |difference| ≥ 45 refuses. Below zero is "not exciting enough"
	/// (reason 5, thought 12, bored), otherwise "too exciting" (reason 4, thought 15, scared). Null when the guest may join.
	/// </summary>
	public static QueueJoinResult? Refusal( int excitementLevelWord, int difference )
	{
		if ( (excitementLevelWord & 0xFF) == 0 || Math.Abs( difference ) < Gate )
			return null;
		return difference < 0 ? QueueJoinResult.NotExcitingEnough : QueueJoinResult.TooExciting;
	}
}
