namespace OpenTPW;

/// <summary>
/// A world that moves the staff it drives (the handymen, entertainers and guards) and runs their state machines once per park turn
/// (tp.exe <c>0x004D73C0</c>, <c>0x004D4810</c>, <c>0x004D6410</c>; docs/reverse/WANDER-plan.md §4). While one is attached the economy dispatches no
/// handyman jobs of its own and leaves those members' vitals to their state machines.
/// </summary>
public interface IStaffWorld
{
	/// <summary>One park turn of every member the world drives, in member order.</summary>
	void UpdateStaff( ParkEconomy economy, long turn );
	/// <summary>An object was sold or removed (the removal message 10 every staff member hears); the economy's own release runs after it.</summary>
	void ObjectRemoved( ParkEconomy economy, int instanceId ) { }
}
