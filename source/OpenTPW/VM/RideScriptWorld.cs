namespace OpenTPW;

/// <summary>
/// The set of live scripts that can see each other through FINDSCRIPTRAND, GETREMOTEVAR and SETREMOTEVAR.
/// Script IDs are positive; 0 means "no script". One world belongs to one park: it also allocates the park's
/// attraction ids and seeds scripts that are created without an explicit seed, so nothing about a run
/// depends on what else ran earlier in the process.
/// </summary>
public sealed class RideScriptWorld
{
	private readonly Dictionary<int, RideVM> scripts = new();
	private int nextId = 1;
	private int nextAttractionId = 1;

	/// <param name="seed">Start state of the seed stream for scripts without an explicit seed (<see cref="WorldSeed.ScriptStream"/>).</param>
	public RideScriptWorld( ulong seed = 0 ) => RandomState = seed;

	public IReadOnlyCollection<RideVM> Scripts => scripts.Values;

	/// <summary>SplitMix64 state of the seed stream (saved with the park; see <see cref="WorldRandomState"/>).</summary>
	public ulong RandomState { get; private set; }

	/// <summary>The next attraction id of this park (guests and the economy key attractions by it).</summary>
	public int NextAttractionId => nextAttractionId;

	internal int AllocateAttractionId() => nextAttractionId++;

	internal void RestoreRandomState( ulong state ) => RandomState = state;

	/// <summary>Seed for a script created without one.</summary>
	// [BIN:STP-PPC:0x100B07E4 RSE RAND] RAND draws from the world LCG (0x10105328 on the world at TOC −0x7580): (|s| >> 1) mod (bound + 1); FINDSCRIPTRAND (0x100B1B8C) picks match (|s| >> 1) mod count, drawing only when there is a match
	// [APPROX:DET-013] scripts without an explicit seed get a per-VM System.Random seeded from the park's SplitMix64 stream instead of the traced world-LCG draws — evidence needed: none; implementation waits for the DET-I2 port of the shared world generator (docs/reverse/DET-plan.md §4.3)
	internal int NextScriptSeed()
	{
		var z = RandomState += 0x9E3779B97F4A7C15UL;
		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
		return (int)(z ^ (z >> 31));
	}

	internal int Register( RideVM vm )
	{
		var id = nextId++;
		scripts.Add( id, vm );
		return id;
	}

	internal void Unregister( RideVM vm ) => scripts.Remove( vm.ScriptId );

	public RideVM? Find( int scriptId ) => scripts.TryGetValue( scriptId, out var vm ) ? vm : null;

	/// <summary>Picks a random live script whose NAME equals <paramref name="name"/>; returns its ID or 0.</summary>
	internal int FindRandom( string name, Random random )
	{
		var matches = scripts.Values.Where( x => x.State != RideVMState.Faulted && string.Equals( x.ScriptName, name, StringComparison.Ordinal ) ).OrderBy( x => x.ScriptId ).ToArray();
		return matches.Length == 0 ? 0 : matches[random.Next( matches.Length )].ScriptId;
	}
}
