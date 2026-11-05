namespace OpenTPW;

/// <summary>
/// The things the advisor's scan callbacks name for the message tube's locate button (the scan object's words +4 and +8, which the speech copies into the advice record):
/// the original thing ids of the dirtiest toilet, the scariest ride, the first unconnected queue, and the cell word of the bus stop. Pure reads of the park; a park state
/// without the view, or without a world that names its objects, leaves the words 0.
/// </summary>
internal interface IAdvisorThingsState
{
	/// <summary>The thing id of the dirtiest toilet (<c>0x004CA220</c>), 0 when there is none or no world names it.</summary>
	ushort DirtiestToiletThing { get; }
	/// <summary>The thing id of the scariest ride (<c>0x004CCD10</c>), 0 when there is none or no world names it.</summary>
	ushort ScariestRideThing { get; }
	/// <summary>The first queue ride that joins no path (<c>0x004CCB00</c>, the selected object left out) as one read: its economy instance (0 when there is none) and its thing id (0 when no world names it); null without a world that answers.</summary>
	AdvisorQueueProbe? UnconnectedQueue { get; }
	/// <summary>The cell word of the level's second bus stop (<c>0x004D8650</c> with the flag 1); null when the level names none or no world answers.</summary>
	int? ArrivalBusStopCell { get; }
}

/// <summary>What the first-unconnected-queue read found: the economy instance (0 for none) and the thing id the world names it by.</summary>
internal readonly record struct AdvisorQueueProbe( int Instance, ushort Thing );

internal sealed partial class EconomyParkState : IAdvisorThingsState
{
	public ushort DirtiestToiletThing => economy.DirtiestToilet() is { } toilet ? economy.ThingOf( toilet.Id ) : (ushort)0;

	public ushort ScariestRideThing => economy.ThingOf( economy.ScariestRide() );

	public AdvisorQueueProbe? UnconnectedQueue => economy.FirstUnconnectedQueueRide is { } probe ? Describe( probe() ) : null;

	private AdvisorQueueProbe Describe( int instance ) => new( instance, economy.ThingOf( instance ) );

	public int? ArrivalBusStopCell => economy.ArrivalBusStopCell?.Invoke();
}
