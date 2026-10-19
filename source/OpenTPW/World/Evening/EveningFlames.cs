using System.Numerics;

namespace OpenTPW;

// [EXT:evening-torches-flicker] user-approved 2026-10-19 ("Alleen licht en sfeer"; a deterministic flicker preferred): the torches' flames move and their light dips, presentation only. Nothing here reads or writes
// the simulation, a save or a state hash: the clock is the evening extension's own play-time clock and the seed is the torch's cell.

/// <summary>
/// The deterministic flicker of a torch: a fixed sum of three sines (0.9, 2.3 and 4.7 Hz, a gentle flicker: it changes by at most a tenth between two frames at 60 frames a second) whose phases come from a seed (the torch's cell), so torches side by side do not pulse together,
/// and the flame's local transform that follows it (taller and thinner as it flares, a slow sway about its base).
/// </summary>
public static class TorchFlicker
{
	/// <summary>How much the flame's height follows the flicker: 1 plus or minus this share (its width moves half as much the other way).</summary>
	public const float FlameStretch = 0.14f;
	/// <summary>The largest lean of the flame about its base, in radians (about 3.4 degrees).</summary>
	public const float FlameSway = 0.06f;

	/// <summary>A seed from a torch's cell: the same cell gives the same flicker in every run.</summary>
	public static int SeedOf( int x, int y ) => unchecked( x * 73856093 ^ y * 19349663 );

	private static double Phase( int seed, int index )
	{
		var hash = unchecked( (uint)seed * 2654435761u + (uint)index * 40503u + 0x9E3779B9u );
		hash ^= hash >> 15;
		hash = unchecked( hash * 2246822519u );
		hash ^= hash >> 13;
		hash = unchecked( hash * 3266489917u );
		hash ^= hash >> 16;
		return hash / (double)uint.MaxValue * 2 * Math.PI;
	}

	/// <summary>The flicker at <paramref name="seconds"/> of play time: 0 (the dimmest) to 1 (the brightest), 0.5 on average; a smooth function of time.</summary>
	public static float At( double seconds, int seed )
	{
		var t = 2 * Math.PI * seconds;
		var value = 0.5 + 0.28 * Math.Sin( 0.9 * t + Phase( seed, 1 ) ) + 0.14 * Math.Sin( 2.3 * t + Phase( seed, 2 ) ) + 0.08 * Math.Sin( 4.7 * t + Phase( seed, 3 ) );
		return (float)Math.Clamp( value, 0.0, 1.0 );
	}

	/// <summary>
	/// The flame node's local transform at <paramref name="seconds"/> (in the model's frame, Y up, about the node's origin, which is the flame's base): its height 1 plus or minus <see cref="FlameStretch"/> with the flicker, its width
	/// half that the other way, and a slow lean of at most <see cref="FlameSway"/>. The base stays where it is.
	/// </summary>
	public static Matrix4x4 FlameMatrix( double seconds, int seed )
	{
		var flare = 2f * At( seconds, seed ) - 1f;
		var height = 1f + FlameStretch * flare;
		var width = 1f - 0.5f * FlameStretch * flare;
		var t = 2 * Math.PI * seconds;
		var leanX = (float)( FlameSway * Math.Sin( 0.9 * t + Phase( seed, 4 ) ) );
		var leanZ = (float)( FlameSway * Math.Sin( 1.3 * t + Phase( seed, 5 ) ) );
		return Matrix4x4.CreateScale( width, height, width ) * Matrix4x4.CreateRotationZ( leanX ) * Matrix4x4.CreateRotationX( leanZ );
	}

	/// <summary>The lamp level below which a flame is not drawn (the lamps' own threshold): by day the torch shows an empty cup.</summary>
	public const float FlameLevelThreshold = 0.002f;

	/// <summary>
	/// The flame's local transform at <paramref name="seconds"/> scaled about its base by <paramref name="lampLevel"/> (it grows as the lamps come on at dusk), or null while the lamp level is below <see cref="FlameLevelThreshold"/> (hidden by day,
	/// as the torch's light is off).
	/// </summary>
	public static Matrix4x4? FlameAt( double seconds, int seed, float lampLevel ) =>
		lampLevel > FlameLevelThreshold ? Matrix4x4.CreateScale( Math.Min( lampLevel, 1f ) ) * FlameMatrix( seconds, seed ) : null;
}

/// <summary>
/// The torches' flames: every placed object whose lamp table row names a flame node (<see cref="EveningLampRule.Flame"/>) gets that node's part drawn with <see cref="TorchFlicker.FlameMatrix"/> each frame, scaled about its base by the
/// lamp level, so the flame is hidden by day (an empty cup), grows as the lamps come on at dusk and burns at full size at night, as the torch's light does. The objects are cached and rebuilt only when the placed set changes, as the lamp source
/// does. Presentation only: the override lives on the drawn parts, not in the animator or the runtime.
/// </summary>
public sealed class EveningFlames
{
	private readonly EveningLampTable table;
	private readonly Func<IReadOnlyList<OriginalObject>> objects;
	private readonly Func<int>? version;
	private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ObjectCatalogEntry, System.Runtime.CompilerServices.StrongBox<int>> nodes = new();
	private readonly List<(OriginalObject Item, int Node, int Seed)> flames = new();
	private IReadOnlyList<OriginalObject>? cachedList;
	private int cachedVersion = int.MinValue;
	private int cachedCount = -1;

	public EveningFlames( EveningLampTable table, Func<IReadOnlyList<OriginalObject>> objects, Func<int>? version = null )
	{
		this.table = table;
		this.objects = objects;
		this.version = version;
	}

	/// <summary>Test seam: the flame node of an object without a catalog entry and model (an object needs a graphics device to be built); -1 for none.</summary>
	internal Func<OriginalObject, int>? NodeOverride { get; set; }

	/// <summary>Test seam: receives each flame's transform (null: hidden) instead of the object (which needs a graphics device).</summary>
	internal Action<OriginalObject, int, Matrix4x4?>? ApplyOverride { get; set; }

	/// <summary>The number of flames in the cache (for tests).</summary>
	internal int Count => flames.Count;

	private int FlameNodeOf( OriginalObject item )
	{
		if ( NodeOverride != null )
			return NodeOverride( item );
		var entry = item.Entry;
		if ( nodes.TryGetValue( entry, out var known ) )
			return known.Value;
		var node = -1;
		if ( table.Find( entry ) is { Flame: { } name } )
			node = item.Runtime.Model.Nodes.FirstOrDefault( candidate => string.Equals( candidate.Name, name, StringComparison.OrdinalIgnoreCase ) )?.Index ?? -1;
		nodes.AddOrUpdate( entry, new System.Runtime.CompilerServices.StrongBox<int>( node ) );
		return node;
	}

	internal void Refresh()
	{
		var placed = objects();
		var current = version?.Invoke() ?? 0;
		if ( ReferenceEquals( placed, cachedList ) && current == cachedVersion && placed.Count == cachedCount )
			return;
		cachedList = placed;
		cachedVersion = current;
		cachedCount = placed.Count;
		flames.Clear();
		for ( var index = 0; index < placed.Count; index++ )
		{
			var item = placed[index];
			if ( item.IsDeleted )
				continue;
			var node = FlameNodeOf( item );
			if ( node >= 0 )
				flames.Add( (item, node, TorchFlicker.SeedOf( item.Placement.X, item.Placement.Y )) );
		}
	}

	/// <summary>Test seam: receives each flame whose override is taken away (<see cref="Clear"/>) instead of the object.</summary>
	internal Action<OriginalObject, int>? ClearOverride { get; set; }

	/// <summary>Gives every cached flame back its model pose (drawn as the animator says, not hidden), for when the evening look is switched off, and empties the cache.</summary>
	public void Clear()
	{
		for ( var index = 0; index < flames.Count; index++ )
		{
			var (item, node, _) = flames[index];
			if ( ClearOverride != null )
				ClearOverride( item, node );
			else
				item.ClearPresentationNodeTransform( node );
		}
		flames.Clear();
		cachedList = null;
		cachedVersion = int.MinValue;
		cachedCount = -1;
	}

	/// <summary>Moves every flame to its pose at <paramref name="seconds"/> of the presentation clock, at the size of <paramref name="lampLevel"/> (hidden at 0, by day).</summary>
	public void Animate( double seconds, float lampLevel )
	{
		Refresh();
		for ( var index = 0; index < flames.Count; index++ )
		{
			var (item, node, seed) = flames[index];
			var local = TorchFlicker.FlameAt( seconds, seed, lampLevel );
			if ( ApplyOverride != null )
				ApplyOverride( item, node, local );
			else
				item.SetPresentationNodeTransform( node, local );
		}
	}
}
