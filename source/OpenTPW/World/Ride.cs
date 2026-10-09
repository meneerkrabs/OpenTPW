namespace OpenTPW;

public class Ride : Entity
{
	public RideVM VM { get; private set; }

	public Ride( string rideArchive )
	{
		var rideName = Path.GetFileNameWithoutExtension( rideArchive );

		using var script = FileSystem.OpenRead( rideArchive + "\\" + rideName + ".rse" );
		VM = new RideVM( script, new RideVMOptions { SourceName = rideArchive } );
		var settingsFile = new SettingsFile( FileSystem.OpenRead( rideArchive + "\\" + rideName + ".sam" ) );

		Log.Trace( $"Loaded ride {settingsFile.Entries.First( x => x.Key == "Info.Name" ).Value}" );
	}

	/// <summary>Runs one fixed simulation tick of the ride script.</summary>
	internal void Simulate( float deltaTime ) => VM.Advance( deltaTime );
}
