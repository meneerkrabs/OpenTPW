namespace OpenTPW;

/// <summary>
/// The set of live scripts that can see each other through FINDSCRIPTRAND, GETREMOTEVAR and SETREMOTEVAR.
/// Script IDs are positive; 0 means "no script".
/// </summary>
public sealed class RideScriptWorld
{
	private readonly Dictionary<int, RideVM> scripts = new();
	private int nextId = 1;

	public IReadOnlyCollection<RideVM> Scripts => scripts.Values;

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
