namespace OpenTPW;

/// <summary>One remembered price refusal: the guest (by birth id), the attraction instance and the park turn on which the memory ends.</summary>
/// <param name="BirthId">The refused guest's <see cref="Guest.BirthId"/>.</param>
/// <param name="AttractionId">The attraction instance that refused it at the stand point.</param>
/// <param name="ExpiresTurn">The memory holds while the park turn is below this (the turn of the refusal plus <see cref="SmarterGuestsRules.RefusalMemoryTurns"/>).</param>
public readonly record struct PriceRefusal( ulong BirthId, int AttractionId, long ExpiresTurn );

/// <summary>
/// The "too expensive" memory of the smarter guests (docs/SMARTER-GUESTS.md, phase 2): the attraction instances a guest was refused at for their price, each for
/// <see cref="SmarterGuestsRules.RefusalMemoryTurns"/> park turns, at most <see cref="SmarterGuestsRules.MaxRefusalsPerGuest"/> per guest. The host writes it (when it refuses a guest at the stand point) and reads it
/// into the candidate views; a policy never touches it. Entries are kept in one canonical order (birth id, then attraction id), so lookups are a binary search, saving needs no sorting and the hash is the same
/// for the same entries on any machine. The memory is bounded by the guests in the park: a departing guest is forgotten (<see cref="Forget"/>) and an expired entry is dropped (<see cref="Prune"/>).
/// </summary>
// [EXT:smarter-guests-value] OpenTPW's own memory; the original guests have none
public sealed class PriceMemory
{
	private readonly List<PriceRefusal> entries = [];

	/// <summary>The number of remembered refusals.</summary>
	public int Count => entries.Count;

	/// <summary>The entries in canonical order (birth id, then attraction id).</summary>
	public IReadOnlyList<PriceRefusal> Entries => entries;

	/// <summary>The index of the first entry at or after (<paramref name="birthId"/>, <paramref name="attractionId"/>).</summary>
	private int LowerBound( ulong birthId, int attractionId )
	{
		int low = 0, high = entries.Count;
		while ( low < high )
		{
			var middle = low + ((high - low) >> 1);
			var entry = entries[middle];
			var before = entry.BirthId != birthId ? entry.BirthId < birthId : entry.AttractionId < attractionId;
			if ( before )
				low = middle + 1;
			else
				high = middle;
		}
		return low;
	}

	/// <summary>Whether the guest is still remembering a refusal at the attraction on park turn <paramref name="parkTurn"/>.</summary>
	public bool Refused( ulong birthId, int attractionId, long parkTurn )
	{
		if ( entries.Count == 0 || birthId == 0 )
			return false;
		var index = LowerBound( birthId, attractionId );
		return index < entries.Count && entries[index].BirthId == birthId && entries[index].AttractionId == attractionId && entries[index].ExpiresTurn > parkTurn;
	}

	/// <summary>
	/// Remembers a refusal until park turn <paramref name="expiresTurn"/>. A second refusal at the same attraction extends the first. When the guest would hold more than
	/// <see cref="SmarterGuestsRules.MaxRefusalsPerGuest"/>, the entry that ends soonest goes (the lower attraction id on a tie).
	/// </summary>
	public void Remember( ulong birthId, int attractionId, long expiresTurn )
	{
		if ( birthId == 0 )
			return;
		var index = LowerBound( birthId, attractionId );
		if ( index < entries.Count && entries[index].BirthId == birthId && entries[index].AttractionId == attractionId )
		{
			entries[index] = entries[index] with { ExpiresTurn = Math.Max( entries[index].ExpiresTurn, expiresTurn ) };
			return;
		}
		entries.Insert( index, new PriceRefusal( birthId, attractionId, expiresTurn ) );
		var first = LowerBound( birthId, 0 );
		var count = 0;
		while ( first + count < entries.Count && entries[first + count].BirthId == birthId )
			count++;
		if ( count <= SmarterGuestsRules.MaxRefusalsPerGuest )
			return;
		var oldest = first;
		for ( var at = first + 1; at < first + count; at++ )
			if ( entries[at].ExpiresTurn < entries[oldest].ExpiresTurn )
				oldest = at;
		entries.RemoveAt( oldest );
	}

	/// <summary>Drops the entries that ended on or before <paramref name="parkTurn"/>.</summary>
	public void Prune( long parkTurn )
	{
		if ( entries.Count > 0 )
			entries.RemoveAll( entry => entry.ExpiresTurn <= parkTurn );
	}

	/// <summary>Drops everything the guest remembers (it left the park).</summary>
	public void Forget( ulong birthId )
	{
		if ( entries.Count == 0 || birthId == 0 )
			return;
		var first = LowerBound( birthId, 0 );
		var count = 0;
		while ( first + count < entries.Count && entries[first + count].BirthId == birthId )
			count++;
		if ( count > 0 )
			entries.RemoveRange( first, count );
	}

	/// <summary>Removes every entry.</summary>
	public void Clear() => entries.Clear();

	/// <summary>Replaces the memory with <paramref name="saved"/>, which the section's validation has checked to be in canonical order.</summary>
	internal void Load( IEnumerable<PriceRefusal> saved )
	{
		entries.Clear();
		entries.AddRange( saved );
	}
}
