namespace OpenTPW;

/// <summary>
/// The park information window's "Top 3 Thoughts": the three thought ids the original shows as icons, in the order its slots hold them
/// (<see cref="TopThoughtRanking"/>). A slot holds a thought id (slot 0 starts as id 0 with its count discarded, so a slot can hold id 0 only while nobody has a thought: it then reads as empty) or <see cref="TopThoughtRanking.NoThought"/>.
/// </summary>
/// <param name="First">Slot 0, drawn in the rightmost icon (control 0x4A68D4).</param>
/// <param name="Second">Slot 1, drawn in the middle icon (control 0x4A68D3).</param>
/// <param name="Third">Slot 2, drawn in the leftmost icon (control 0x4A68D2).</param>
public readonly record struct TopThoughts( byte First, byte Second, byte Third )
{
	/// <summary>The three slots in slot order.</summary>
	public byte Slot( int index ) => index switch { 0 => First, 1 => Second, 2 => Third, _ => throw new ArgumentOutOfRangeException( nameof( index ) ) };

	/// <summary>The thought a slot stands for, or null when the slot shows no icon.</summary>
	public GuestThought? ThoughtAt( int index ) => Slot( index ) is > 0 and < TopThoughtRanking.NoThought ? (GuestThought)Slot( index ) : null;

	/// <summary>The <c>ti_hungry</c> frame of a slot's icon (the thought less 1), or -1 for no icon.</summary>
	// [BIN:TPW-EXE:0x004A4ab0 icon frames] an id in 1..22 sets the icon's frame to the id less 1 (vtable +0x1C); anything else (0, 23) hides the icon (0x65D9DD with 0), and the original never shows it again while the window stays open (see ParkHud.InfoWindow)
	public int FrameAt( int index ) => Slot( index ) is > 0 and < TopThoughtRanking.NoThought ? Slot( index ) - 1 : -1;
}

/// <summary>The ranking behind the park information window's Top 3 Thoughts (tp.exe 0x004C8430, called by the window at 0x004A4AB0).</summary>
public static class TopThoughtRanking
{
	/// <summary>Thought ids 0..22 are counted (the guests use 0..17; 18..22 are the staff's, which this census never reaches).</summary>
	public const int IdCount = 23;

	/// <summary>The id of an empty slot: 0x17, which the window shows as no icon.</summary>
	public const byte NoThought = 23;

	/// <summary>
	/// Ranks the thoughts of the guests on park cells. It counts every guest by its thought id, discards the count of id 0, then keeps three slots that start as the ids 0, 1 and 2: each id from
	/// 3 to 22 in turn replaces the slot with the lowest count, but only when its own count is strictly higher (the first slot wins among equal lows, and an id that merely ties
	/// stays out, so on ties the lower id keeps its slot). The slots are not sorted. A slot whose count is 0 reads as <see cref="NoThought"/>.
	/// The count of thought 0 (no thought) is discarded before the slots are seeded, so those guests never take a slot.
	/// </summary>
	// [BIN:TPW-EXE:0x004C8430 top three thoughts] counts[thought +0x30] of every type-1 thing on a park cell (0x004FA990) over the thing list (0x516130 / 0x516160); the slots {0,1,2} then
	// take each id 3..22 by the largest count difference (signed, strictly above 0, first slot on equal differences); a slot with count 0 is written as 0x17; the three slots go to the caller's three byte pointers in order
	public static TopThoughts Rank( IEnumerable<int> thoughts )
	{
		var counts = new int[IdCount];
		foreach ( var thought in thoughts )
			if ( thought is >= 0 and < IdCount )
				counts[thought]++;
		// [BIN:TPW-EXE:0x004C84D7 no-thought count dropped] after the counting loop and before the slots are seeded the original writes 0 to counts[0] (mov [esp+0x20], 0), so the guests with no thought never hold a slot
		counts[0] = 0;
		var slots = new[] { 0, 1, 2 };
		for ( var candidate = 3; candidate < IdCount; candidate++ )
		{
			var bestDifference = 0;
			var bestSlot = -1;
			for ( var slot = 0; slot < slots.Length; slot++ )
			{
				var difference = counts[candidate] - counts[slots[slot]];
				if ( difference > bestDifference )
				{
					bestDifference = difference;
					bestSlot = slot;
				}
			}
			if ( bestSlot >= 0 )
				slots[bestSlot] = candidate;
		}
		byte Read( int slot ) => counts[slots[slot]] == 0 ? NoThought : (byte)slots[slot];
		return new TopThoughts( Read( 0 ), Read( 1 ), Read( 2 ) );
	}
}
