namespace OpenTPW;

/// <summary>
/// The particle a stinkbomb on the ground carries: the effect starts when the litter is placed and ends when the cell is swept.
/// </summary>
internal sealed class ParkLitterEffects
{
	/// <summary>The particle effect of a stinkbomb (0x32).</summary>
	// [BIN:TPW-EXE:0x4D95BD stinkbomb particle] after the sprite is placed, type 8 calls 0x521E60 with effect 0x32 and the sprite's position in original units times 1024
	public const int StinkEffectId = 50;

	private readonly IAdvisorParticlePool pool;
	private readonly Dictionary<(int X, int Y), object> live = new();

	public ParkLitterEffects( ParkLitter litter, IAdvisorParticlePool pool )
	{
		this.pool = pool;
		litter.Changed += OnChanged;
	}

	/// <summary>Live stinkbomb particles by cell.</summary>
	public int Count => live.Count;

	/// <summary>
	/// The native position of the stinkbomb's particle: the sprite's position, (10 x cell + offset) with the offset 1..9 the cell id's two draws give, times 1024, at height 0.
	/// </summary>
	// [BIN:TPW-EXE:0x4D94E2 sprite position] x = cell x * 10 + (first draw % 9) + 1, z = cell z * 10 + (second draw % 9) + 1 (constants 10.0 at 0x700494, -1.0 at 0x700490); the particle is created at (ftol(x * 1024.0), 0, ftol(z * 1024.0)) (0x700498)
	public static (int X, int Y, int Z) NativePosition( int x, int y )
	{
		var (dx, dz) = ParkLitter.SpriteOffset( x, y );
		return ((x * 10 + dx) * 1024, 0, (y * 10 + dz) * 1024);
	}

	private void OnChanged( int x, int y, LitterType type )
	{
		if ( type == LitterType.Stinkbomb )
		{
			if ( pool.Create( StinkEffectId, NativePosition( x, y ) ) is { } handle )
				live[(x, y)] = handle;
		}
		// [BIN:TPW-EXE:0x4D9620 removing litter] a stinkbomb (type 8) ends its particle with 0x51FEB0 before the sprite goes
		else if ( type == LitterType.None && live.Remove( (x, y), out var removed ) )
			pool.Kill( removed );
	}
}
