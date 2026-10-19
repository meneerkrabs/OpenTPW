namespace OpenTPW;

/// <summary>
/// The parts of a running level that its park save, load and canonical hash read. <see cref="Level"/>
/// builds one per call; it holds no state of its own, so tests reach the same save, load and hash code
/// without a renderer.
/// </summary>
public sealed record ParkWorldStreams( WorldSeed Seed, ParkEconomyRuntime? Park, GuestSimulation? Guests, RideScriptWorld Scripts, SoundEventSystem? Sound )
{
	/// <summary>The park's one world generator (the economy's when there is one, else the guests', else the scripts') and the sound seed, for the park save.</summary>
	public WorldRng Generator => Park?.Economy.World ?? Guests?.World ?? Scripts.World;

	/// <summary>Makes the guests and the scripts draw from <see cref="Generator"/> (the original has one generator for everything); returns this.</summary>
	public ParkWorldStreams LinkGenerator()
	{
		var generator = Generator;
		Guests?.AttachWorld( generator );
		Scripts.AttachWorld( generator );
		return this;
	}

	/// <summary>The world generator and the sound seed, for the park save.</summary>
	public WorldRandomState CaptureRandomState() => new( Seed.Value, Generator.State, Sound?.Seed ?? Seed.SoundStream );

	/// <summary>Continues the streams of a park save.</summary>
	public void RestoreRandomState( WorldRandomState state )
	{
		ArgumentNullException.ThrowIfNull( state );
		if ( state.Seed != Seed.Value )
			Log.Warning( $"The park save was made with world seed {state.Seed}, not this level's {Seed}; its streams continue under this level's seed." );
		Generator.Restore( state.Generator );
		Sound?.Reseed( state.SoundSeed );
	}

	/// <summary>Canonical state hash (<see cref="WorldStateHash"/>). The sound seed is saved but not hashed (docs/DETERMINISM.md).</summary>
	public ulong ComputeStateHash() => WorldStateHash.Compute( new WorldStateSources
	{
		Seed = Seed,
		Economy = Park?.Economy,
		Guests = Guests,
		Scripts = Scripts
	} );

	/// <summary>Writes the park economy and every random stream to an OpenTPW park save.</summary>
	public void SavePark( string path )
	{
		if ( Park == null )
			throw new InvalidOperationException( "Only original levels have a park economy to save." );
		Park.Save( path, CaptureRandomState() );
	}

	/// <summary>Loads an OpenTPW park save: the economy and, when the save has them, the random streams. Guests and object scripts keep running (the legacy economy-only save, [EXT:legacy-economy-save]). The loaded economy holds its own generator object; guests and scripts are attached to it.</summary>
	public void LoadPark( string path ) => LoadPark( path, validate: null );

	/// <summary>The caller may reject the detached economy before any live economy, guest/script RNG link or sound seed changes.</summary>
	internal void LoadPark( string path, Action<ParkEconomy>? validate )
	{
		if ( Park == null )
			throw new InvalidOperationException( "Only original levels have a park economy to load." );
		var world = Park.Load( path, validate );
		// the loaded economy has its own generator object: the guests and the scripts draw from it from now on, one stream as before the load
		Guests?.AttachWorld( Park.Economy.World );
		Scripts.AttachWorld( Park.Economy.World );
		if ( world is not null )
			RestoreRandomState( world );
	}
}
