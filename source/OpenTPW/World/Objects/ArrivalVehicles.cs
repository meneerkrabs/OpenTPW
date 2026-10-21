namespace OpenTPW;

/// <summary>
/// The park's arrival vehicles for <see cref="GuestSimulation.ArrivalVehicles"/>: the bus, seaplane and Ferry fixed items placed in
/// <see cref="ParkObjects"/> and driven by their own level scripts (bus.RSE, seaplane.RSE, Ferry.RSE). A vehicle is placed the first
/// time it is sent and kept; the script's variables are read and written by index, as the original does.
/// </summary>
public sealed class ArrivalVehicles : IArrivalVehicleHost
{
	private readonly ParkObjects objects;
	private readonly Action<OriginalObject>? created;

	/// <param name="created">Called with each vehicle as it is placed: the level gives it its economy record (<see cref="Level"/>'s fixed items made in play).</param>
	public ArrivalVehicles( ParkObjects objects, Action<OriginalObject>? created = null )
	{
		this.objects = objects;
		this.created = created;
	}

	/// <summary>True when the level defines at least one of the vehicles (the original stops with a fatal error otherwise, 0x0051A601).</summary>
	public static bool Available( ObjectCatalog catalog ) => ArrivalVehicleKind.Names.Any( name => Definition( catalog, name ) != null );

	public bool Exists( int kind ) => Find( kind ) != null;

	public bool Create( int kind )
	{
		if ( Definition( objects.Catalog, ArrivalVehicleKind.Names[kind] ) is not { } entry )
			return false;
		// [BIN:TPW-EXE:0x0051A57E vehicle construction] the first trip constructs the vehicle with the common object constructor 0x004DB090, as every placed object
		created?.Invoke( objects.Add( entry, 0, 0, 0 ) );
		return true;
	}

	// [BIN:TPW-EXE:0x0055A390 script variable read] an index outside the script's variable count (+0x8C) logs and reads 0
	public int GetVariable( int kind, int index ) =>
		Find( kind )?.Runtime.Script is { } script && index >= 0 && index < script.Variables.Length ? script.Variables[index] : 0;

	// [BIN:TPW-EXE:0x0055A0B0 script variable write] an index outside the script's variable count (+0x8C) logs and writes nothing
	public void SetVariable( int kind, int index, int value )
	{
		if ( Find( kind )?.Runtime.Script is { } script && index >= 0 && index < script.Variables.Length )
			script.Variables[index] = value;
	}

	private OriginalObject? Find( int kind ) =>
		objects.Objects.FirstOrDefault( item => Named( item.Entry, ArrivalVehicleKind.Names[kind] ) );

	/// <summary>The first definition named like the vehicle: its archive's name, compared case-insensitively (the seaplane's Info.Name is "Plane" in three themes).</summary>
	// [BIN:TPW-EXE:0x0051A50D vehicle definition] the definition list 0x007890A0 is walked in order and the name at definition +0x390 compared with _wcsicmp (0x0067B140)
	// [BIN:TPW-EXE:0x00413D9B definition name] the object loader prints the archive object's name (+4, see ObjectCatalog.ArchiveKey) into definition +0x390
	internal static ObjectCatalogEntry? Definition( ObjectCatalog catalog, string name ) => catalog.Entries.FirstOrDefault( entry => Named( entry, name ) );

	private static bool Named( ObjectCatalogEntry entry, string name ) => string.Equals( ObjectCatalog.ArchiveKey( entry.ArchiveName ), name, StringComparison.OrdinalIgnoreCase );
}
