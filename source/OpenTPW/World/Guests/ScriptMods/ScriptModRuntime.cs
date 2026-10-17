using OpenTPW.Mods;
using OpenTPW.Scripting;

namespace OpenTPW;

/// <summary>
/// The script mods of one guest simulation, in the resolved, saved mod order (docs/SCRIPT-MODS.md): built once when the simulation is, from the active mod set's retained and compiled programs.
/// Hooks chain through it in this order. Its state is simulation state: <see cref="ScriptModsSave"/> keeps it and <see cref="AddCanonicalState"/> joins <see cref="WorldStateHash"/>.
/// </summary>
// [EXT:script-mods] OpenTPW's own scripted mods (TPW Script); the original game had no mods
internal sealed class ScriptModRuntime
{
	/// <summary>Mixed into the world hash ahead of the section (the ASCII "SCRM").</summary>
	internal const long HashMarker = 0x5343524DL;

	public ScriptModRuntime( IEnumerable<ResolvedMod> mods, ScriptHostOptions? options = null )
	{
		Mods = mods.Where( mod => mod.Scripts != null ).Select( mod => new ScriptModInstance( mod, options ) ).ToArray();
		if ( Mods.Count == 0 )
			throw new ArgumentException( "A script runtime needs at least one script mod.", nameof( mods ) );
	}

	/// <summary>The runtime for the script mods of <paramref name="active"/>, or null when none is enabled (the common case: nothing is built, nothing is saved, nothing is hashed).</summary>
	public static ScriptModRuntime? ForActive( ModSet active )
	{
		ArgumentNullException.ThrowIfNull( active );
		return active.Mods.Any( mod => mod.Scripts != null ) ? new ScriptModRuntime( active.Mods ) : null;
	}

	/// <summary>The script mods in the order their hooks chain.</summary>
	public IReadOnlyList<ScriptModInstance> Mods { get; }

	public void BeginTick()
	{
		foreach ( var mod in Mods )
			mod.BeginTick();
	}

	/// <summary>A candidate loop of <paramref name="hook"/> starts: each mod that implements it is admitted or skipped for the whole decision.</summary>
	public void BeginDecision( string hook )
	{
		foreach ( var mod in Mods )
			mod.BeginDecision( hook );
	}

	public void EndDecision()
	{
		foreach ( var mod in Mods )
			mod.EndDecision();
	}

	/// <summary>Whether any enabled mod still implements <paramref name="hook"/> (a disabled one does not count).</summary>
	public bool Runs( string hook )
	{
		foreach ( var mod in Mods )
			if ( !mod.Disabled && mod.HasHook( hook ) )
				return true;
		return false;
	}

	public IReadOnlyList<ScriptModStats> Stats() => Mods.Select( mod => mod.Stats() ).ToArray();

	/// <summary>The section's part of <see cref="WorldStateHash"/>: every mod in id order (the chain order is the mod set's, which the saved mod records already pin).</summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( HashMarker );
		hash.Add( Mods.Count );
		foreach ( var mod in Mods.OrderBy( mod => mod.Id, StringComparer.Ordinal ) )
			mod.AddCanonicalState( hash );
	}
}
