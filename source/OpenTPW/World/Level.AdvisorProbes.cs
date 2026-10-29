namespace OpenTPW;

public partial class Level
{
	/// <summary>
	/// Points the current economy's advisor reads at the level's rides: the live excitement of a ride and the first queue that joins no path (<see cref="ParkEconomy.RideLiveExcitement"/>,
	/// <see cref="ParkEconomy.FirstUnconnectedQueueRide"/>); a park load replaces the economy, so it runs again then. Both are pure reads: the advisor only speaks.
	/// </summary>
	private void AttachAdvisorProbes()
	{
		if ( Park == null )
			return;
		Park.Economy.RideLiveExcitement = RideLiveExcitement;
		Park.Economy.FirstUnconnectedQueueRide = FirstUnconnectedQueue;
	}

	/// <summary>The live excitement of the ride with this economy instance id (tp.exe <c>0x004E0860</c> with the argument 0); 0 for an object the level does not hold.</summary>
	private int RideLiveExcitement( int instance )
	{
		foreach ( var item in Objects.Objects )
			if ( !item.IsDeleted && item.Runtime.IsAttraction && GetEconomyInstance( item ) == instance )
				return item.Runtime.Visitors.LiveExcitement;
		return 0;
	}

	/// <summary>
	/// The economy instance id of the first attraction with a queue (<c>Info.HasQueue</c>) that the player may choose (<c>Info.IsChoosable</c>) and whose queue joins no path (<see cref="ParkGate.QueueConnected"/>),
	/// the object the player has selected left out; 0 when there is none.
	/// </summary>
	// [BIN:TPW-EXE:0x004CCB00 first unconnected queue] the walk over the things starts at the head of the thing list, the newest object (0x516130, 0x516160); an attraction counts when its flag word has the bits 4 and 8 and the back-of-queue test 0x4DE4A0 answers 0, unless it is the selected ride 0x8186AC (0x527930)
	private int FirstUnconnectedQueue()
	{
		if ( Guests == null || Park?.Economy is not { } economy )
			return 0;
		var selected = WindowTarget( Selection );
		for ( var index = Objects.Objects.Count - 1; index >= 0; index-- )
		{
			var item = Objects.Objects[index];
			if ( item.IsDeleted || !item.Runtime.IsAttraction || !item.Entry.HasQueue || ReferenceEquals( item, selected ) || GetEconomyInstance( item ) is not int instance )
				continue;
			if ( !economy.TryGetObject( instance, out var record ) || !economy.Catalog.TryGet( record.InfoId, out var info ) || !info.IsChoosable )
				continue;
			if ( !ParkGate.QueueConnected( Guests.Grid, item.Visitors ) )
				return instance;
		}
		return 0;
	}
}
