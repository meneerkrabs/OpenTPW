namespace OpenTPW;

/// <summary>
/// The park's arrival vehicles as the arrival update sees them (<see cref="GuestSimulation.ArrivalVehicles"/>): one placed
/// object per kind (0 the bus, 1 the seaplane, 2 the Ferry), created on first use and kept, and the two script variables the
/// original reads and writes (0 the trigger, 1 the status). The level backs this with the park's objects and their RSE scripts.
/// </summary>
public interface IArrivalVehicleHost
{
	/// <summary>Whether the park already holds the vehicle of this kind (the original's cached handle, world +0x1DA72C + 2 × kind).</summary>
	bool Exists( int kind );
	/// <summary>Places the vehicle of this kind; false when the level has no definition of that name.</summary>
	bool Create( int kind );
	/// <summary>A script variable of the vehicle; 0 for a variable the script does not have.</summary>
	int GetVariable( int kind, int index );
	/// <summary>Writes a script variable of the vehicle; a variable the script does not have is ignored.</summary>
	void SetVariable( int kind, int index, int value );
	/// <summary>The thing id of the vehicle of this kind, the handle the world caches (world +0x1DA72C + 2 × kind); 0 when the park holds none or the host has no thing ids.</summary>
	int ThingId( int kind ) => 0;
}

/// <summary>The vehicle kinds and their definition names (tp.exe wide literals 0x00762420 "bus", 0x00762428 "seaplane", 0x0076243C "Ferry").</summary>
public static class ArrivalVehicleKind
{
	public const int Bus = 0;
	public const int Seaplane = 1;
	public const int Ferry = 2;
	public static readonly IReadOnlyList<string> Names = new[] { "bus", "seaplane", "Ferry" };
	/// <summary>The trigger the arrival update sets to send a parked vehicle on (script variable 0, VAR_TRIGGER in the shipped scripts).</summary>
	public const int TriggerVariable = 0;
	/// <summary>The status the arrival update reads (script variable 1, VAR_STATUS): 2 at the stop, 4 waiting for leaving guests, 6 gone.</summary>
	public const int StatusVariable = 1;
}
