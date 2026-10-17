namespace OpenTPW;

/// <summary>One guest's run of price-emptied decisions: the guest (by birth id) and how many decisions in a row left it nothing to do because its price rule turned every worthwhile stand away.</summary>
public readonly record struct PriceStall( ulong BirthId, int Count );

/// <summary>
/// The count of consecutive "price-emptied" decisions per guest (docs/SMARTER-GUESTS.md, phase 2): a major decision that found nothing while a stand the guest would have chosen was left out because it
/// expects the stand to refuse it on price. The host increments it, resets it when the guest chooses something and asks the policy whether to send the guest home; the policy never touches it. Entries are
/// kept in birth-id order (binary search, canonical saving and hashing). Bounded by the guests in the park: a departing guest is forgotten, and a guest that goes home is reset.
/// </summary>
// [EXT:smarter-guests-value] OpenTPW's own bookkeeping; the original guests keep none
public sealed class PriceStalls
{
	/// <summary>A run counts no higher than this (a guest whose walk home cannot be routed keeps trying and must not overflow).</summary>
	public const int MaximumRun = 255;

	private readonly List<PriceStall> entries = [];

	/// <summary>The number of guests with a run in progress.</summary>
	public int Count => entries.Count;

	/// <summary>The runs in birth-id order.</summary>
	public IReadOnlyList<PriceStall> Entries => entries;

	private int LowerBound( ulong birthId )
	{
		int low = 0, high = entries.Count;
		while ( low < high )
		{
			var middle = low + ((high - low) >> 1);
			if ( entries[middle].BirthId < birthId )
				low = middle + 1;
			else
				high = middle;
		}
		return low;
	}

	/// <summary>The guest's run so far (0 for none).</summary>
	public int Of( ulong birthId )
	{
		var index = LowerBound( birthId );
		return index < entries.Count && entries[index].BirthId == birthId ? entries[index].Count : 0;
	}

	/// <summary>Adds one to the guest's run and returns it. A guest without a birth id has none and always gets 1.</summary>
	public int Increment( ulong birthId )
	{
		if ( birthId == 0 )
			return 1;
		var index = LowerBound( birthId );
		if ( index < entries.Count && entries[index].BirthId == birthId )
		{
			entries[index] = entries[index] with { Count = Math.Min( MaximumRun, entries[index].Count + 1 ) };
			return entries[index].Count;
		}
		entries.Insert( index, new PriceStall( birthId, 1 ) );
		return 1;
	}

	/// <summary>Ends the guest's run (it chose something, went home or left the park).</summary>
	public void Reset( ulong birthId )
	{
		if ( entries.Count == 0 || birthId == 0 )
			return;
		var index = LowerBound( birthId );
		if ( index < entries.Count && entries[index].BirthId == birthId )
			entries.RemoveAt( index );
	}

	/// <summary>Removes every run.</summary>
	public void Clear() => entries.Clear();

	/// <summary>Replaces the runs with <paramref name="saved"/>, which the section's validation has checked.</summary>
	internal void Load( IEnumerable<PriceStall> saved )
	{
		entries.Clear();
		entries.AddRange( saved );
	}
}
