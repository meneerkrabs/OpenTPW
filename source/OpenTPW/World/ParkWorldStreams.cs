namespace OpenTPW;

/// <summary>
/// The parts of a running level that its park save, load and canonical hash read. <see cref="Level"/>
/// builds one per call; it holds no state of its own, so tests reach the same save, load and hash code
/// without a renderer.
/// </summary>
public sealed record ParkWorldStreams( WorldSeed Seed, ParkEconomyRuntime? Park, GuestSimulation? Guests, RideScriptWorld Scripts, SoundEventSystem? Sound )
{
	/// <summary>The random streams besides the economy's, for the park save.</summary>
	public WorldRandomState CaptureRandomState() => new( Seed.Value, Guests?.RandomState ?? Seed.GuestStream,
		Scripts.RandomState, Sound?.Seed ?? Seed.SoundStream );

	/// <summary>Continues the streams of a park save.</summary>
	public void RestoreRandomState( WorldRandomState state )
	{
		ArgumentNullException.ThrowIfNull( state );
		if ( state.Seed != Seed.Value )
			Log.Warning( $"The park save was made with world seed {state.Seed}, not this level's {Seed}; its streams continue under this level's seed." );
		Guests?.RestoreRandomState( state.GuestRandom );
		Scripts.RestoreRandomState( state.ScriptRandom );
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

	/// <summary>Loads an OpenTPW park save: the economy and, when the save has them, the random streams. Guests and object scripts keep running (DET-016).</summary>
	public void LoadPark( string path )
	{
		if ( Park == null )
			throw new InvalidOperationException( "Only original levels have a park economy to load." );
		if ( Park.Load( path ) is { } world )
			RestoreRandomState( world );
	}
}
