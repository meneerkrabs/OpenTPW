namespace OpenTPW;

/// <summary>
/// The coaster boarding generator <c>CoasterRng</c> (DET-plan §2): one per world, <c>s = s·214013 + 2531011</c>,
/// output <c>s &gt;&gt; 16</c> (logical).
/// </summary>
public sealed class CoasterRandom
{
	public CoasterRandom( uint state ) => State = state;

	public uint State { get; private set; }

	// [BIN:STP-PPC:data:622280 coaster boarding RNG] the state at app global data 622280 (TOC 0xcc0) steps s = s × 214013 + 2531011 and draws s >> 16 (logical); it is seeded by one C-library rand() at 0x10054BF4 (DET-plan §2)
	public int Next()
	{
		State = unchecked(State * 214013u + 2531011u);
		return (int)(State >> 16);
	}

	internal void Restore( uint state ) => State = state;
}

/// <summary>
/// The coasters of one park: their controllers in creation order, the shared <see cref="CoasterRandom"/>, and the
/// pylon lookup the placement validator's stacking test reads across coasters. Owned by the park's
/// <see cref="RideScriptWorld"/>, so every object runtime of the park reaches it.
/// </summary>
// [EXT:coaster-own-save] the own-format park save holds the tracks, trains, riders and the coaster generator state (CoasterSave, since the park-resume coaster slice); the original's own coaster chunks between EMAK and SAOC are read structurally only (SaveCoasterBlock: header, auxiliary links, sections, links; train payload raw; nonempty TPW runtime import refused) and never written; importing them is a non-goal until someone needs it (the evidence for it would be the train payload layout in the save writer next to the loader 0x437b60)
public sealed class CoasterWorld
{
	/// <summary>Key XORed into the script stream seed for the coaster generator's start state.</summary>
	private const ulong StreamKey = 0x434F_4153_5452_4E47; // "COASTRNG"
	private readonly List<CoasterRide> rides = new();

	/// <param name="seed">The park's script stream seed (<see cref="WorldSeed.ScriptStream"/>).</param>
	// [EXT:coaster-rng-seed] the coaster generator starts from the world seed (folded script stream seed) instead of the original's one C-library rand() at world init (0x457a90 → 0x67b5c0, stored at 0x7b0958); the boarding step itself is the original's s = s·0x343fd + 0x269ec3 with (s >> 16) % n (0x43b67f..0x43b69d): a determinism design decision (DET-I2 owns ClibRng)
	public CoasterWorld( ulong seed = 0 )
	{
		var folded = seed ^ StreamKey;
		Random = new CoasterRandom( (uint)(folded ^ (folded >> 32)) );
	}

	public CoasterRandom Random { get; }
	/// <summary>The park's script world, which takes the head-hang draw (<see cref="RideScriptWorld.HeadHung"/>); null for a world of its own.</summary>
	internal RideScriptWorld? Scripts { get; set; }
	public IReadOnlyList<CoasterRide> Rides => rides;

	internal void Add( CoasterRide ride )
	{
		if ( !rides.Contains( ride ) )
			rides.Add( ride );
	}

	internal void Remove( CoasterRide ride )
	{
		ride.Track.DetachCells();
		rides.Remove( ride );
	}

	/// <summary>Every pylon (not a pylon-less station or station end node) standing on (x, y), with its coaster.</summary>
	public IEnumerable<(CoasterRide? Ride, CoasterNode Node)> PylonsAt( int x, int y )
	{
		foreach ( var ride in rides )
		{
			foreach ( var node in ride.Track.Nodes )
			{
				if ( !node.IsPylonless && node.X == x && node.Y == y )
					yield return (ride, node);
			}
		}
	}

	/// <summary>Whether any coaster has a pylon on (x, y) (paths, queues and objects keep off pylon cells).</summary>
	public bool HasPylon( int x, int y ) => PylonsAt( x, y ).Any();

	/// <summary>Coaster part of <see cref="WorldStateHash"/>: the generator, then every coaster in creation order.</summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( (long)Random.State );
		hash.Add( rides.Count );
		foreach ( var ride in rides )
			ride.AddCanonicalState( hash );
	}
}
