namespace OpenTPW;

/// <summary>
/// One of an attraction's six 30-day daily rings: a running value of today and the last 30 completed days.
/// </summary>
// [BIN:TPW-EXE:0x004DB090 object constructor] a ring starts with head -1 (+0x224 and the others), size 30 (+0x228), the wrapped flag 0 (+0x22C) and a running value 0 (+0x1A8)
// [BIN:TPW-EXE:0x004DD320 object day message] on the day message 0xB each ring steps its head (wrapping to 0 at the size and setting the wrapped flag), stores the running value there and clears it
// [BIN:TPW-EXE:0x00495D40 history count] the count is the size once wrapped, else head + 1; [BIN:TPW-EXE:0x00495CF0 history read] read k (0 = the newest day) takes the entry head - k, wrapping once the ring has wrapped, and fails past the count
// [BIN:TPW-EXE:0x00495C20 history sum] the sum of the newest n entries adds each read as an unsigned 32-bit value in a double; n is cut to the count, with the debug line "CHistory - You asked for the sum of more elements" when it exceeds the size
public sealed class DailyRing
{
	/// <summary>The size of every ring (0x1E).</summary>
	public const int Size = 30;

	private readonly int[] data = new int[Size];

	/// <summary>The newest entry's slot, -1 before the first day closes (the original's head).</summary>
	public int Head { get; private set; } = -1;
	/// <summary>Whether the ring has gone round once (the original's full flag).</summary>
	public bool Wrapped { get; private set; }
	/// <summary>Today's running value, which the day message moves into the ring.</summary>
	public int Today { get; set; }

	/// <summary>The number of days the ring holds: 30 once wrapped, else the head plus one.</summary>
	public int Count => Wrapped ? Size : Head + 1;

	/// <summary>Moves today's value into the ring and clears it (the day message 0xB).</summary>
	public void Roll()
	{
		Head++;
		if ( Head >= Size )
		{
			Head = 0;
			Wrapped = true;
		}
		data[Head] = Today;
		Today = 0;
	}

	/// <summary>The entry <paramref name="back"/> days before the newest (0: the newest); false past the days the ring holds.</summary>
	public bool TryGet( int back, out int value )
	{
		value = 0;
		if ( back < 0 || back >= Size )
			return false;
		if ( back <= Head )
		{
			value = data[Head - back];
			return true;
		}
		if ( !Wrapped )
			return false;
		value = data[Head - back + Size];
		return true;
	}

	/// <summary>
	/// The sum of the newest <paramref name="days"/> entries (cut to the count) with each entry an unsigned 32-bit value, the way the original adds them; the
	/// 30-entry window of the windows' rows is <c>Sum( Count )</c>.
	/// </summary>
	public long Sum( int days )
	{
		long sum = 0;
		for ( var back = 0; back < Math.Min( days, Count ); back++ )
			if ( TryGet( back, out var value ) )
				sum += (uint)value;
		return sum;
	}

	/// <summary>The sum over every entry the ring holds (the 30-day window of the object windows and the lists).</summary>
	public long Total => Sum( Count );

	/// <summary>The sum of every entry as a signed value (the satisfaction ring's gauge adds its entries as integers, 0x004E1E30).</summary>
	public long SignedTotal
	{
		get
		{
			long sum = 0;
			for ( var back = 0; back < Count; back++ )
				if ( TryGet( back, out var value ) )
					sum += value;
			return sum;
		}
	}

	/// <summary>The entries oldest first, as far as the ring holds them.</summary>
	public IReadOnlyList<int> Entries
	{
		get
		{
			var list = new List<int>( Count );
			for ( var back = Count - 1; back >= 0; back-- )
				if ( TryGet( back, out var value ) )
					list.Add( value );
			return list;
		}
	}

	/// <summary>Restores a ring from its state; throws when the head or the entry count is out of range.</summary>
	public static DailyRing FromState( int head, bool wrapped, int today, IReadOnlyList<int> entriesOldestFirst )
	{
		if ( head is < -1 or >= Size || wrapped && head < 0 )
			throw new InvalidDataException( $"A daily ring head of {head} is out of range." );
		var ring = new DailyRing();
		var count = wrapped ? Size : head + 1;
		if ( entriesOldestFirst.Count != count )
			throw new InvalidDataException( $"A daily ring with head {head} holds {count} entries, not {entriesOldestFirst.Count}." );
		// oldest first: the newest is at the head, going back from it
		for ( var back = 0; back < count; back++ )
			ring.data[((head - back) % Size + Size) % Size] = entriesOldestFirst[count - 1 - back];
		ring.Head = head;
		ring.Wrapped = wrapped;
		ring.Today = today;
		return ring;
	}

	/// <summary>The original's raw slots 0..29 (what its serializer writes), for an import and a save.</summary>
	internal int[] Slots => (int[])data.Clone();

	/// <summary>Restores a ring from the raw slots of the original's serializer.</summary>
	internal static DailyRing FromSlots( int head, bool wrapped, int today, IReadOnlyList<int> slots )
	{
		if ( head is < -1 or >= Size || slots.Count != Size )
			throw new InvalidDataException( $"A daily ring with head {head} and {slots.Count} slots does not fit the original's 30." );
		var ring = new DailyRing { Head = head, Wrapped = wrapped, Today = today };
		for ( var index = 0; index < Size; index++ )
			ring.data[index] = slots[index];
		return ring;
	}
}
