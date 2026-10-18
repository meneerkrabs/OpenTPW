namespace OpenTPW;

/// <summary>One guest's last meal: the guest (by birth id) and the park turn it ate (a use of an attraction with a hunger effect).</summary>
public readonly record struct MealTime( ulong BirthId, long Turn );

/// <summary>
/// The turn of each guest's latest meal (docs/SMARTER-GUESTS.md, phase 5): the host notes it when a guest uses an attraction with a hunger effect and reads it back into <see cref="GuestView.MealTurn"/>; the policy never
/// touches it. Entries are kept in birth-id order (binary search, canonical saving and hashing). Bounded by the guests in the park: a departing guest is forgotten.
/// </summary>
// [EXT:smarter-guests-planning] OpenTPW's own bookkeeping; the original guests keep none
public sealed class MealTimes
{
	private readonly List<MealTime> entries = [];

	/// <summary>The number of guests with a meal on record.</summary>
	public int Count => entries.Count;

	/// <summary>The meals in birth-id order.</summary>
	public IReadOnlyList<MealTime> Entries => entries;

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

	/// <summary>The park turn of the guest's latest meal, or -1 for none (also for a guest without a birth id).</summary>
	public long Of( ulong birthId )
	{
		if ( entries.Count == 0 || birthId == 0 )
			return -1;
		var index = LowerBound( birthId );
		return index < entries.Count && entries[index].BirthId == birthId ? entries[index].Turn : -1;
	}

	/// <summary>Records that the guest ate on park turn <paramref name="turn"/>, replacing an earlier meal. A guest without a birth id is not recorded.</summary>
	public void Note( ulong birthId, long turn )
	{
		if ( birthId == 0 )
			return;
		var index = LowerBound( birthId );
		if ( index < entries.Count && entries[index].BirthId == birthId )
			entries[index] = new MealTime( birthId, turn );
		else
			entries.Insert( index, new MealTime( birthId, turn ) );
	}

	/// <summary>Forgets the guest's meal (it left the park).</summary>
	public void Forget( ulong birthId )
	{
		if ( entries.Count == 0 || birthId == 0 )
			return;
		var index = LowerBound( birthId );
		if ( index < entries.Count && entries[index].BirthId == birthId )
			entries.RemoveAt( index );
	}

	/// <summary>Removes every meal.</summary>
	public void Clear() => entries.Clear();

	/// <summary>Replaces the meals with <paramref name="saved"/>, which the section's validation has checked.</summary>
	internal void Load( IEnumerable<MealTime> saved )
	{
		entries.Clear();
		entries.AddRange( saved );
	}
}
