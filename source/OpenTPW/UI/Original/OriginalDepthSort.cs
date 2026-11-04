using System.Collections.Concurrent;

namespace OpenTPW.UI.Original;

/// <summary>
/// The quicksort tp.exe sorts deferred draw records (<c>0x5829E0</c>) and the triangles of a stashed record (<c>0x565490</c>) with: descending by a float key, Hoare partitions around the middle element,
/// not stable. Every UI control model vertex ends at depth 0 (<see cref="UiModel"/>), so every key of a UI record or triangle is equal and the order the sort leaves is the permutation of its ties.
/// </summary>
// [BIN:TPW-EXE:0x5829E0 record sort] i advances while key[i] > pivot (bounded by the original high end), j retreats while key[j] < pivot (bounded by the original low end), the pair is swapped and both move on, the left part [lo, j] is a recursive call and the right part [i, hi] the next round of the loop
// [BIN:TPW-EXE:0x565490 triangle sort] the same loop moving a six byte triangle record beside each float key; ORACLE.md X70 runs both unpatched
public static class OriginalDepthSort
{
	private static readonly ConcurrentDictionary<int, int[]> tieOrders = new();

	/// <summary>
	/// The element indices in the order the sort leaves <paramref name="count"/> elements of equal keys: position p of the result holds the element that started at that index.
	/// Cached per count, so a draw reads it without allocating.
	/// </summary>
	public static int[] TieOrder( int count ) => tieOrders.GetOrAdd( Math.Max( 0, count ), static count =>
	{
		var order = Identity( count );
		Sort( new float[count], order );
		return order;
	} );

	private static int[] Identity( int count )
	{
		var order = new int[Math.Max( 0, count )];
		for ( var index = 0; index < order.Length; index++ )
			order[index] = index;
		return order;
	}

	/// <summary>Sorts <paramref name="order"/> descending by <paramref name="keys"/>[order[i]] exactly as tp.exe does (ties included).</summary>
	public static void Sort( ReadOnlySpan<float> keys, Span<int> order )
	{
		if ( order.Length > 1 )
			Sort( keys, order, 0, order.Length - 1 );
	}

	private static void Sort( ReadOnlySpan<float> keys, Span<int> order, int low, int high )
	{
		while ( true )
		{
			int i = low, j = high;
			var pivot = keys[order[(low + high) >> 1]];
			while ( true )
			{
				while ( i <= high && keys[order[i]] > pivot )
					i++;
				while ( j > low && keys[order[j]] < pivot )
					j--;
				if ( i > j )
					break;
				(order[i], order[j]) = (order[j], order[i]);
				i++;
				j--;
				if ( i > j )
					break;
			}
			if ( low < j )
				Sort( keys, order, low, j );
			if ( i >= high )
				return;
			low = i;
		}
	}
}
