namespace OpenTPW;

public partial class Level
{
	/// <summary>Set by the particle module: starts the level's land effects (the type 2 records of <c>scape.omp</c>) in the level's pool; null in headless hosts.</summary>
	internal static Action<Level, IReadOnlyList<PlacedObjectsFile.Record>>? PlacedParticlesStarted { get; set; }

	/// <summary>
	/// The land's own particle effects: every type 2 record of the level's <c>scape.omp</c> starts its effect at the record's place when the level loads. A park resumed from a
	/// save, or imported from an original one, then replaces the pool with its saved one, which holds these emitters already.
	/// </summary>
	// [BIN:TPW-EXE:0x550e00 scape.omp type 2] the loader (called once per level load from 0x407f20, on <level>\Scape.omp) reads each record's fifteen words and, for a type 2, calls 0x521E60(word 1, word 3, word 4, word 5): the effect and its place in 1/1024 units, the handle dropped; Jungle's one record is effect 20 (P_EFFECT_WaterFall) at (545100, 5000, 538700), which every saved jungle pool holds as a live emitter
	private void StartPlacedParticles()
	{
		var path = $"/levels/{LevelName}/scape.omp";
		if ( PlacedParticlesStarted is not { } start || !FileSystem.FileExists( path ) )
			return;
		try
		{
			var records = new PlacedObjectsFile( FileSystem.ReadAllBytes( path ) ).Records.Where( record => record.Type == PlacedObjectsFile.ParticleType ).ToArray();
			if ( records.Length > 0 )
				start( this, records );
		}
		catch ( InvalidDataException exception )
		{
			Log.Warning( $"The land's particle effects were not started ({exception.Message})." );
		}
	}

	/// <summary>The particle of a guest beaming into or out of a ride that requires teleporting.</summary>
	public const int BeamUpEffectId = 19;

	/// <summary>
	/// The native place (1/1024 original unit) of a guest's beam: its position in original units with the ground height under it, each coordinate truncated.
	/// </summary>
	// [BIN:TPW-EXE:0x4fa470 guest particle] the guest thing's position (0x4754e0: +0x88 and +0x90) plus the world's two offset words (0x519410 and 0x519420, both 0 in the shipped data and never written) times 10, the height the ground sampler 0x4527f0 gives at that place, each times 1024 through _ftol, into 0x521E60(effect, x, y, z)
	internal static (int X, int Y, int Z) GuestBeamPlace( ModelHeightfield field, float cellX, float cellY ) =>
		((int)(cellX * field.CellSizeX * 1024f), (int)(field.GetHeight( cellX, cellY ) * 1024f), (int)(cellY * field.CellSizeZ * 1024f));

	/// <summary>Starts particle 19 where the guest stands (presentation only: nothing is saved or hashed).</summary>
	internal void StartGuestBeam( Guest guest )
	{
		if ( OriginalPark?.Heightfield is { } field )
			ParticleRendererModule.AdvisorPool.Create( BeamUpEffectId, GuestBeamPlace( field, guest.X, guest.Y ) );
	}
}
