namespace OpenTPW;

/// <summary>One category of <see cref="ParkPriceStatistics"/> as saved: the head index, the full flag and the 50 value bytes.</summary>
public sealed record PriceStatisticRing( int Category, int Head, bool Full, byte[] Values );

/// <summary>
/// The park's perception statistics (tp.exe park thing <c>+0x216FC</c>): 20 rings of 50 values, one per category. The price test at a
/// stand point records what a guest thinks of a shop's or sideshow's price there (<see cref="QueueJoinRules.PriceStatistics"/>): categories
/// 1..4 by the shop's special ingredient, 6 and 7 by its appearance effect, 9 for a sideshow. Only the advisor's conditions read them.
/// </summary>
// [BIN:TPW-EXE:0x004CE9E0 statistic ring] each of the 20 records of 0x40 bytes (constructed by the park 0x004C5B3C) starts with the head +0x34 at -1, the size +0x38 at 0x32 and the full flag +0x3C clear; the values are bytes at +1
// [BIN:TPW-EXE:0x004C9830 statistic mean] the advisor conditions (0x0059E75B, 0x0059E7BB, with the size test 0x004C9920) read the mean of the last min(count, 50) values
public sealed class ParkPriceStatistics
{
	public const int Categories = 20;
	public const int Capacity = 50;

	private readonly int[] heads = Enumerable.Repeat( -1, Categories ).ToArray();
	private readonly bool[] full = new bool[Categories];
	private readonly byte[][] values = Enumerable.Range( 0, Categories ).Select( _ => new byte[Capacity] ).ToArray();

	/// <summary>
	/// Records <paramref name="value"/> in <paramref name="category"/> (tp.exe <c>0x004C74B0</c>): the head moves on, wrapping to the first slot
	/// and setting the full flag at the end, and the value is stored there as a byte.
	/// </summary>
	// [BIN:TPW-EXE:0x004C74B0 statistic record] head = head + 1; head >= size: head = 0 and the full flag = 1; byte [record + 1 + head] = value
	public void Record( int category, int value )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( category );
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual( category, Categories );
		var head = heads[category] + 1;
		if ( head >= Capacity )
		{
			head = 0;
			full[category] = true;
		}
		heads[category] = head;
		values[category][head] = unchecked( (byte)value );
	}

	/// <summary>The values a category holds: the whole ring once it is full, else the first head + 1.</summary>
	public int Count( int category ) => full[category] ? Capacity : heads[category] + 1;

	/// <summary>The category's values in the order they were recorded, oldest first.</summary>
	public IReadOnlyList<byte> Values( int category )
	{
		var head = heads[category];
		if ( !full[category] )
			return values[category].AsSpan( 0, head + 1 ).ToArray();
		var ordered = new byte[Capacity];
		for ( var index = 0; index < Capacity; index++ )
			ordered[index] = values[category][(head + 1 + index) % Capacity];
		return ordered;
	}

	/// <summary>The categories that hold a value, for a park save.</summary>
	public IReadOnlyList<PriceStatisticRing> Capture() => Enumerable.Range( 0, Categories ).Where( category => heads[category] >= 0 || full[category] )
		.Select( category => new PriceStatisticRing( category, heads[category], full[category], values[category].ToArray() ) ).ToArray();

	/// <summary>Whether <paramref name="rings"/> describe valid categories, once each.</summary>
	public static bool IsValid( IReadOnlyList<PriceStatisticRing> rings ) =>
		rings.All( ring => ring is { Values.Length: Capacity, Category: >= 0 and < Categories } && ring.Head >= -1 && ring.Head < Capacity && (ring.Head >= 0 || !ring.Full) )
		&& rings.Select( ring => ring.Category ).Distinct().Count() == rings.Count;

	/// <summary>Replaces every category with <paramref name="rings"/> (absent categories start empty).</summary>
	public void Restore( IReadOnlyList<PriceStatisticRing> rings )
	{
		if ( !IsValid( rings ) )
			throw new InvalidDataException( "Park price statistics are inconsistent." );
		Array.Fill( heads, -1 );
		Array.Clear( full );
		foreach ( var ring in rings )
		{
			heads[ring.Category] = ring.Head;
			full[ring.Category] = ring.Full;
			ring.Values.CopyTo( values[ring.Category], 0 );
		}
		foreach ( var category in Enumerable.Range( 0, Categories ).Where( category => heads[category] < 0 ) )
			Array.Clear( values[category] );
	}
}
