namespace OpenTPW;

/// <summary>
/// A world that moves the handymen and runs their state machine once per park turn (tp.exe <c>0x004D73C0</c>, docs/reverse/WANDER-plan.md §4).
/// While one is attached the economy dispatches no handyman jobs of its own.
/// </summary>
public interface IHandymanWorld
{
	/// <summary>One park turn of every handyman, in member order.</summary>
	void UpdateHandymen( ParkEconomy economy, long turn );
}
