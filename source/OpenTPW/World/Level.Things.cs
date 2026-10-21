namespace OpenTPW;

public partial class Level
{
	/// <summary>
	/// The park's one thing pool (tp.exe 0x00516270): the guests, the staff, the placed objects, the vehicles and the world's own singletons take their thing ids from it, in the order the park builds
	/// them, and a destroyed thing's id is taken again by the next (docs/GUESTS.md, "Thing ids").
	/// </summary>
	public ThingPool Things => things ??= new();

	private ThingPool? things;

	/// <summary>
	/// Fills the pool as the park starts. A new park builds the world's singletons first (ids 1 to 10 in the constructor order of 0x005156A0), then its objects take the next slots: Gates and
	/// Lights are 11 and 12, as in the shipped level saves. An original save puts the saved chain back in its own slots, the free list ascending (0x00518D43). A park-resume save puts back its
	/// own pool; one from before the pool keeps its guests' counter ids as slots and builds the singletons and objects from the free ones.
	/// </summary>
	// [BIN:TPW-EXE:0x005156A0 world setup] the world's constructor builds the singletons, then the gates and the lights; a loaded save replaces them with its chain (the loader 0x005179C0 → 0x00518D43)
	private void PrepareThings()
	{
		if ( resumeState is { } resume )
		{
			if ( resume.Things != null )
				Things.RestoreSnapshot( resume.Things );
			else
			{
				// a save from before the pool: the guests keep their counter ids as slots (those above the pool's size stay outside it), newest last
				// [EXT:online-coop-snapshot-join] kept for a join: a live park's pool is made on first use and every save writes it, so a join's snapshot never lacks it
				Things.Restore( resume.Guests.Guests.Reverse().Where( guest => guest.Id <= ThingPool.Capacity ).Select( guest => new ThingSlot( guest.Id, SaveThingModel.Guest ) ) );
				Things.AllocateWorldSetup();
			}
		}
		else if ( !GameEditions.IsThemeParkInc && OriginalPark?.Save is { } save && visitLayout == null )
		{
			var chain = save.World.Things.Where( thing => thing.Id <= ThingPool.Capacity ).Select( thing => new ThingSlot( thing.Id, thing.Model ) ).ToList();
			if ( chain.Count != save.World.Things.Count )
				Log.Warning( $"The saved thing chain holds {save.World.Things.Count - chain.Count} thing(s) above the pool's {ThingPool.Capacity} ids; they are not kept." );
			Things.Restore( chain );
		}
		else
			Things.AllocateWorldSetup();
		Objects.Things = Things;
		Park?.Economy.Staff.AttachThings( Things );
	}

	/// <summary>
	/// After an original save is imported: a saved thing OpenTPW does not rebuild (a guest the import drops, an object that did not resolve, a staff member that is not employed) is
	/// still a thing of the original's park, so its slot stays used as a placeholder and later things get the ids the original hands out (<see cref="ThingPool.Hold"/>). The world's
	/// singletons keep theirs.
	/// </summary>
	private void ReleaseUnimportedThings() => OriginalThingSlots.HoldUnbuilt( Things,
		Objects.Objects.Select( item => item.ThingId ).ToHashSet(),
		Guests?.Guests.Select( guest => guest.Id ).ToHashSet() ?? new HashSet<int>(),
		Park?.Economy.Staff.Members.Select( member => member.ThingId ).ToHashSet() ?? new HashSet<int>() );
}
