using System;

namespace OpenTPW.Evidence;

/// <summary>
/// Isolated arithmetic recovered from the identified Feral PowerPC application.
/// This is an evidence helper, not GuestSimulation's rules or a complete attraction score.
/// </summary>
/// <remarks>
/// SimThemePark.data SHA256 04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5.
/// Code/TOC addresses and input dependencies: docs/reverse/PPC-guests.md and ../evidence.py.
/// Lookup tables must be supplied by the caller; no original assets are embedded.
/// Unsupported layouts, nonfinite needs, and zero divisors fail closed rather than inventing behavior.
/// </remarks>
public static class GuestOriginalRules
{
	/// <summary>Requested groups along a sufficiently long queue-cell chain (code 0xddd2c..0xddd50).</summary>
	public static (uint CellsBack, uint PositionInCell) QueueCellGroup( uint position ) => (position / 4, position % 4);

	/// <summary>Distance match at code 0xe9320..0xe9398; coordinates are cell displacements, not path distance.</summary>
	public static uint DistanceMatch( int dx, int dy, short cellAttractionDivisor = 0 )
	{
		var scaled = SquaredDistance( dx, dy ) * 100 / 450;
		var match = 100 - Math.Min( scaled, 100u );
		// The original sign-extends the cell short, then uses unsigned DIVWU.
		return cellAttractionDivisor == 0 ? match : match / unchecked((uint)(int)cellAttractionDivisor);
	}

	/// <summary>
	/// Active queue weight and match at code 0xe93c0..0xe9420. A far queue removes its weight too.
	/// More than four visitors per capacity produces a wrapped negative match; it is not clamped.
	/// </summary>
	public static (uint Weight, uint Match) QueueTerm( int dx, int dy, uint queueCount, uint capacityField, uint weight )
	{
		if ( SquaredDistance( dx, dy ) > 8 )
			return (0, 0);
		var capacity = capacityField == 0 ? 1u : capacityField;
		var denominator = unchecked(capacity * 4);
		if ( denominator == 0 )
			throw new ArgumentException( "Capacity produces an unsupported zero DIVWU denominator.", nameof( capacityField ) );
		var queuePercent = unchecked(queueCount * 100) / denominator;
		return (weight, unchecked(100u - queuePercent));
	}

	/// <summary>Byte operands, absolute difference capped at 50, then 2*(50-difference); code 0xe9424..0xe9470.</summary>
	public static uint ExcitementMatch( byte preferred, byte excitement ) => (uint)(2 * (50 - Math.Min( Math.Abs( preferred - excitement ), 50 )));

	/// <summary>Effect-major 11*floor(effect/10)+floor(need/10); code 0xe94b0..0xe9584.</summary>
	public static uint HungerOrThirstMatch( byte need, byte effect, ReadOnlySpan<byte> interpretedTable )
	{
		RequireNeedByte( need );
		if ( effect > 100 )
			throw new ArgumentOutOfRangeException( nameof( effect ), "Effect outside the bounded recovered lookup domain." );
		if ( interpretedTable.Length != 121 )
			throw new ArgumentException( "An interpreted 11 by 11 table is required.", nameof( interpretedTable ) );
		return interpretedTable[11 * (effect / 10) + need / 10];
	}

	/// <summary>Enabled toilet/illness table lookup at (need+4)/5; code 0xe9588..0xe96bc.</summary>
	public static uint ToiletOrIllnessMatch( byte need, bool effectEnabled, ReadOnlySpan<byte> interpretedTable )
	{
		if ( !effectEnabled )
			return 0;
		RequireNeedByte( need );
		if ( interpretedTable.Length != 21 )
			throw new ArgumentException( "An interpreted 21-entry table is required.", nameof( interpretedTable ) );
		return interpretedTable[(need + 4) / 5];
	}

	/// <summary>
	/// Seven active register weights/matches in distance, queue, excitement, thirst, hunger, toilet, illness order.
	/// Low-32-bit products/additions and unsigned division at code 0xe96c0..0xe9714 are preserved.
	/// Caller supplies register operands after weight truncation and category/distance gates.
	/// </summary>
	public static uint NormalizeBaseScore( ReadOnlySpan<uint> weights, ReadOnlySpan<uint> matches )
	{
		if ( weights.Length != 7 || matches.Length != 7 )
			throw new ArgumentException( "Seven active weight and match operands are required." );
		uint numerator = 0, denominator = 0;
		for ( var i = 0; i < 7; i++ )
		{
			numerator = unchecked(numerator + weights[i] * matches[i]);
			denominator = unchecked(denominator + weights[i]);
		}
		if ( denominator == 0 )
			throw new ArgumentException( "Unsupported zero or wrapped-zero DIVWU denominator.", nameof( weights ) );
		return numerator / denominator;
	}

	/// <summary>
	/// Used history stops at its first match; temporary history applies every matched 5,4,3,2 divisor.
	/// The score is a raw register bit pattern; code 0xe98c0..0xe9a30 reinterprets it as signed.
	/// </summary>
	public static uint ApplyHistory( uint score, ushort attractionId, ReadOnlySpan<ushort> usedHistory, ReadOnlySpan<ushort> temporaryHistory )
	{
		if ( usedHistory.Length != 4 || temporaryHistory.Length != 4 )
			throw new ArgumentException( "Two four-ID histories are required." );
		score = ApplyUsedHistory( score, attractionId, usedHistory );
		// Native temporary slots fall through after division; duplicates are cumulative.
		for ( var i = 0; i < temporaryHistory.Length; i++ )
			if ( temporaryHistory[i] == attractionId )
				score = unchecked((uint)(unchecked((int)score) / (5 - i)));
		return score;
	}

	/// <summary>Subtract signed shop metadata effect, clamp to [0,100]; code 0xeabc4..0xeaca4.</summary>
	public static float ApplyShopEffect( float currentNeed, int effectAmount )
	{
		RequireNeed( currentNeed );
		return Math.Clamp( currentNeed - (float)effectAmount, 0, 100 );
	}

	/// <summary>Integer excitation/divisor times integer (100-hunger)/20; code 0xea4d4..0xea580.</summary>
	public static int RideIllnessIncrement( byte excitement, int divisor, byte hunger )
	{
		RequireNeedByte( hunger );
		if ( divisor <= 0 )
			throw new ArgumentOutOfRangeException( nameof( divisor ), "Only a positive recovered divisor is supported." );
		return (excitement / divisor) * ((100 - hunger) / 20);
	}

	public static float ApplyRideIllness( float currentIllness, byte excitement, int divisor, byte hunger )
	{
		RequireNeed( currentIllness );
		return Math.Clamp( currentIllness + RideIllnessIncrement( excitement, divisor, hunger ), 0, 100 );
	}

	private static uint ApplyUsedHistory( uint score, ushort attractionId, ReadOnlySpan<ushort> history )
	{
		for ( var i = 0; i < history.Length; i++ )
			if ( history[i] == attractionId )
				return unchecked((uint)(unchecked((int)score) / (5 - i)));
		return score;
	}

	private static uint SquaredDistance( int dx, int dy )
	{
		if ( dx is < -127 or > 127 || dy is < -127 or > 127 )
			throw new ArgumentOutOfRangeException( nameof( dx ), "Only the bounded 128-cell coordinate domain is supported." );
		return (uint)(dx * dx + dy * dy);
	}

	private static void RequireNeedByte( byte need )
	{
		if ( need > 100 )
			throw new ArgumentOutOfRangeException( nameof( need ), "Only the recovered 0..100 need domain is supported." );
	}

	private static void RequireNeed( float need )
	{
		if ( !float.IsFinite( need ) || need is < 0 or > 100 )
			throw new ArgumentOutOfRangeException( nameof( need ), "Only finite needs in the recovered 0..100 domain are supported." );
	}
}
