namespace OpenTPW;

/// <summary>
/// The thing slots of an imported original save. The original's load keeps every saved thing in its own slot and rebuilds the free list from the slots the save leaves unused
/// (tp.exe 0x00518D43), so the next thing built takes the lowest unused id whatever the loader later did with the saved things. OpenTPW rebuilds only part of them; the rest hold a placeholder.
/// </summary>
public static class OriginalThingSlots
{
	/// <summary>
	/// Holds the slot of every saved thing the import did not rebuild: an attraction no object took, a guest no guest took and a staff member no member took. The world's singletons and the
	/// other things are kept as they are. Returns the held ids in the pool's used-list order (newest first).
	/// </summary>
	// [BIN:TPW-EXE:0x00518D43 free list rebuild] the free list is rebuilt from the slots no saved thing occupies, so an unbuilt thing must not give its slot back
	public static IReadOnlyList<int> HoldUnbuilt( ThingPool pool, IReadOnlySet<int> objects, IReadOnlySet<int> guests, IReadOnlySet<int> staff )
	{
		ArgumentNullException.ThrowIfNull( pool );
		var held = new List<int>();
		foreach ( var slot in pool.Capture() )
		{
			var built = slot.Model switch
			{
				SaveThingModel.Attraction => objects.Contains( slot.Id ),
				SaveThingModel.Guest => guests.Contains( slot.Id ),
				SaveThingModel.Mechanic or SaveThingModel.Handyman or SaveThingModel.Entertainer or SaveThingModel.Guard or SaveThingModel.Researcher => staff.Contains( slot.Id ),
				_ => true
			};
			if ( !built && pool.Hold( slot.Id ) )
				held.Add( slot.Id );
		}
		return held;
	}
}
