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
		Park.Economy.ObjectThingId = ThingIdOfInstance;
		Park.Economy.ArrivalBusStopCell = ArrivalBusStopCellWord;
		Park.Economy.ThemeParkIncScriptOf = ScriptVariablesOfInstance;
		Park.Economy.ThemeParkIncObjectOnUnownedLand = ObjectOnUnownedLand;
	}

	/// <summary>Whether the object linked to an economy instance stands on a square the park does not own (its thing square's flag 0x40); false for an object the level does not hold.</summary>
	// [BIN:TPI-EXE:0x67ECA0 square flag] the special rides' slot walks (0x62402B, 0x62420D) test the flag 0x40 of the square at the thing's bytes +5 (x) and +7 (y): ParkCellFlags.Unowned
	private bool ObjectOnUnownedLand( int instance )
	{
		if ( ObjectOfInstance( instance ) is not { } item || Guests?.Grid.Cells is not { } cells )
			return false;
		var (x, y) = item.ThingCell;
		return cells.InBounds( x, y ) && (cells.FlagsAt( x, y ) & ParkCellFlags.Unowned) != 0;
	}

	/// <summary>
	/// The script variables of the object linked to an economy instance (the prototype's included), for Theme Park Inc's engine, which reads and writes them by index (<c>0x6B0C90</c>, <c>0x6B0780</c>):
	/// any linked object with a script, a plant (not choosable) included, and the object being removed until its record is sold; null for none.
	/// </summary>
	private int[]? ScriptVariablesOfInstance( int instance ) =>
		ObjectOfInstance( instance ) is { } item ? item.Runtime.Script?.Variables : prototypeInstance == instance ? PlacedRide?.Runtime.Script?.Variables : null;

	/// <summary>The thing id (the original's pool slot) of the attraction with this economy instance id; 0 for an object the level does not hold. The advisor's records carry it for the tube's locate button.</summary>
	private int ThingIdOfInstance( int instance )
	{
		foreach ( var item in Objects.Objects )
			if ( !item.IsDeleted && item.Runtime.IsAttraction && GetEconomyInstance( item ) == instance )
				return item.ThingId;
		return 0;
	}

	/// <summary>The cell word of the level's second bus stop (tp.exe <c>0x004D8650</c> with the flag 1: <c>z × 128 + x + 1</c> of <c>FixedItemInfo.BusStopB</c>), null when the level's settings name none (nothing is made up).</summary>
	// [BIN:TPW-EXE:0x004D8650 bus stop cell word] the flag 1 returns the word (0x7855B8 << 7) + 0x7855B4 + 1 (lea ecx, [eax + ecx + 1], stored as a word)
	private int? ArrivalBusStopCellWord() => Guests?.Settings.BusStopB is { } cell ? (ushort)((cell.Y << 7) + cell.X + 1) : null;

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
