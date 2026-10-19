using System.Globalization;
using System.Numerics;
using SVector3 = System.Numerics.Vector3;
using System.Text.Json;

namespace OpenTPW;

// [EXT:evening] The lamps of the evening extension (docs/EVENING.md): point lights (docs/LIGHTS.md) that OpenTPW attaches to objects from a data table,
// content/data/evening-lights.json, instead of changing any original model. They shine only as it gets dark. Presentation only.

/// <summary>Where on a model node a lamp's light stands.</summary>
public enum EveningAnchor
{
	/// <summary>The node's own origin (its pivot).</summary>
	Pivot,
	/// <summary>The middle of the node's mesh box (the pivot for a node without a mesh).</summary>
	Centre,
	/// <summary>The middle of the top of the node's mesh box (the pivot for a node without a mesh).</summary>
	Top
}

/// <summary>
/// One light of a lamp table row: the model node it follows, where on that node, an offset in model units (the node's frame with the ground in XY and up in Z), its colour and radius, and how much it flickers
/// (0 = steady; up to <see cref="EveningLampTable.MaximumFlicker"/>: the colour dips by up to that share with <see cref="TorchFlicker.At"/>).
/// </summary>
public sealed record EveningLampLight( string Node, EveningAnchor Anchor, SVector3 Offset, SVector3 Colour, float RadiusModelUnits, float Flicker = 0f );

/// <summary>
/// A row of the lamp table: which objects it is for (a theme, a category, an archive name, the pools extension's <c>Pools.Extra</c> key and/or the id of the extension or mod whose content root lists the object; every field given must match)
/// and the lights those objects get. <paramref name="Flame"/> names a model node the extension animates as a flame (<see cref="EveningFlames"/>), or is null.
/// </summary>
public sealed record EveningLampRule( string? Theme, ObjectCategory? Category, string? Archive, int? PoolsExtra, IReadOnlyList<EveningLampLight> Lights, string? Mod = null, string? Flame = null )
{
	/// <summary>Whether <paramref name="entry"/> is one of the objects of this row.</summary>
	public bool Matches( ObjectCatalogEntry entry ) =>
		( Mod == null || string.Equals( Mod, entry.ModId, StringComparison.Ordinal ) )
		&& ( Theme == null || string.Equals( Theme, entry.Theme, StringComparison.OrdinalIgnoreCase ) )
		&& ( Category == null || Category == entry.Category )
		&& ( Archive == null || string.Equals( Archive, entry.ArchiveName, StringComparison.OrdinalIgnoreCase ) )
		&& ( PoolsExtra == null || entry.Settings.GetInt( EveningLampTable.PoolsExtraKey ) == PoolsExtra );
}

/// <summary>
/// The lamp table (<c>content/data/evening-lights.json</c>): which original and pools objects carry a lamp that is lit at dusk. Loaded once; a damaged or missing file logs a warning and
/// gives an empty table (no lamps), never an error. The format is documented in docs/EVENING.md.
/// </summary>
public sealed class EveningLampTable
{
	public const string RelativePath = "content/data/evening-lights.json";
	/// <summary>The pools extension's own key in an object's .sam (1 ladder .. 5 pool lamp, 6 underwater light).</summary>
	public const string PoolsExtraKey = "Pools.Extra";
	/// <summary>The most lights a row may give one object.</summary>
	public const int MaximumLightsPerRule = 16;
	/// <summary>The largest radius in model units (200 model units are 40 engine units, twenty cells).</summary>
	public const float MaximumRadiusModelUnits = 200f;
	/// <summary>[EXT:evening-torches-flicker] The largest flicker of a light: its colour never dips below half.</summary>
	public const float MaximumFlicker = 0.5f;

	private static EveningLampTable? shipped;
	// weak: a park's catalog entries go away with the park, so nothing grows across parks
	private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ObjectCatalogEntry, Found> found = new();

	private sealed class Found( EveningLampRule? rule )
	{
		public EveningLampRule? Rule { get; } = rule;
	}

	public EveningLampTable( IReadOnlyList<EveningLampRule> rules ) => Rules = rules;

	public IReadOnlyList<EveningLampRule> Rules { get; }

	/// <summary>The table shipped with OpenTPW (read on first use).</summary>
	public static EveningLampTable Shipped => shipped ??= LoadShipped();

	/// <summary>The first row for <paramref name="entry"/>, or null (remembered per entry).</summary>
	public EveningLampRule? Find( ObjectCatalogEntry entry )
	{
		lock ( found )
			return found.GetValue( entry, key => new Found( Rules.FirstOrDefault( candidate => candidate.Matches( key ) ) ) ).Rule;
	}

	private static EveningLampTable LoadShipped()
	{
		var path = Path.Combine( AppContext.BaseDirectory, RelativePath );
		try
		{
			return Parse( File.ReadAllText( path ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException )
		{
			Log.Warning( $"Evening lamps unavailable ({path}): {exception.Message}" );
			return new EveningLampTable( Array.Empty<EveningLampRule>() );
		}
	}

	/// <summary>Reads a lamp table and checks every row: a bad row refuses the whole table (<see cref="InvalidDataException"/>).</summary>
	public static EveningLampTable Parse( string text )
	{
		ArgumentNullException.ThrowIfNull( text );
		using var document = JsonDocument.Parse( text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true } );
		var root = document.RootElement;
		if ( root.ValueKind != JsonValueKind.Object || !root.TryGetProperty( "schema", out var schema ) || schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != 1 )
			throw new InvalidDataException( "The lamp table needs \"schema\": 1." );
		if ( !root.TryGetProperty( "lamps", out var lamps ) || lamps.ValueKind != JsonValueKind.Array )
			throw new InvalidDataException( "The lamp table needs a \"lamps\" array." );
		var rules = new List<EveningLampRule>();
		var index = 0;
		foreach ( var row in lamps.EnumerateArray() )
		{
			index++;
			rules.Add( ParseRule( row, index ) );
		}
		return new EveningLampTable( rules );
	}

	private static EveningLampRule ParseRule( JsonElement row, int index )
	{
		string Where( string detail ) => $"Lamp row {index}: {detail}";
		if ( row.ValueKind != JsonValueKind.Object || !row.TryGetProperty( "match", out var match ) || match.ValueKind != JsonValueKind.Object )
			throw new InvalidDataException( Where( "needs a \"match\" object." ) );
		string? theme = null, archive = null, mod = null;
		ObjectCategory? category = null;
		int? poolsExtra = null;
		foreach ( var property in match.EnumerateObject() )
		{
			switch ( property.Name )
			{
				case "theme":
					theme = property.Value.GetString();
					if ( theme == null || !ObjectCatalog.Themes.Contains( theme ) )
						throw new InvalidDataException( Where( $"unknown theme \"{theme}\"." ) );
					break;
				case "category":
					category = property.Value.GetString() switch
					{
						"ride" => ObjectCategory.Ride,
						"shop" => ObjectCategory.Shop,
						"sideshow" => ObjectCategory.Sideshow,
						"feature" => ObjectCategory.Feature,
						var other => throw new InvalidDataException( Where( $"unknown category \"{other}\" (ride, shop, sideshow or feature)." ) )
					};
					break;
				case "archive":
					archive = property.Value.GetString();
					if ( string.IsNullOrWhiteSpace( archive ) )
						throw new InvalidDataException( Where( "an empty archive name." ) );
					break;
				case "mod":
					// [EXT:evening-torches] the extension's own torches are matched by their content root's id, so an original or a mod's object of the same archive name never gets their light
					mod = property.Value.GetString();
					if ( string.IsNullOrWhiteSpace( mod ) )
						throw new InvalidDataException( Where( "an empty mod id." ) );
					break;
				case "poolsExtra":
					poolsExtra = property.Value.GetInt32();
					if ( poolsExtra is < 1 or > 6 )
						throw new InvalidDataException( Where( "\"poolsExtra\" is 1 to 6." ) );
					break;
				default:
					throw new InvalidDataException( Where( $"unknown match key \"{property.Name}\"." ) );
			}
		}
		if ( theme == null && category == null && archive == null && poolsExtra == null && mod == null )
			throw new InvalidDataException( Where( "an empty \"match\" would light every object." ) );
		if ( !row.TryGetProperty( "lights", out var lights ) || lights.ValueKind != JsonValueKind.Array || lights.GetArrayLength() is 0 or > MaximumLightsPerRule )
			throw new InvalidDataException( Where( $"\"lights\" holds 1 to {MaximumLightsPerRule} lights." ) );
		var parsed = new List<EveningLampLight>();
		foreach ( var light in lights.EnumerateArray() )
			parsed.Add( ParseLight( light, Where ) );
		// [EXT:evening-torches-flicker] a node of the row's objects that the extension animates as a flame (presentation only)
		string? flame = null;
		if ( row.TryGetProperty( "flame", out var flameNode ) )
		{
			flame = flameNode.ValueKind == JsonValueKind.String ? flameNode.GetString() : null;
			if ( string.IsNullOrWhiteSpace( flame ) )
				throw new InvalidDataException( Where( "\"flame\" names a model node." ) );
		}
		return new EveningLampRule( theme, category, archive, poolsExtra, parsed, mod, flame );
	}

	private static EveningLampLight ParseLight( JsonElement light, Func<string, string> where )
	{
		if ( light.ValueKind != JsonValueKind.Object )
			throw new InvalidDataException( where( "a light is an object." ) );
		string? node = null;
		var anchor = EveningAnchor.Centre;
		var offset = SVector3.Zero;
		SVector3? colour = null;
		float? radius = null;
		var flicker = 0f;
		foreach ( var property in light.EnumerateObject() )
		{
			switch ( property.Name )
			{
				case "node":
					node = property.Value.GetString();
					break;
				case "anchor":
					anchor = property.Value.GetString() switch
					{
						"pivot" => EveningAnchor.Pivot,
						"centre" => EveningAnchor.Centre,
						"top" => EveningAnchor.Top,
						var other => throw new InvalidDataException( where( $"unknown anchor \"{other}\" (pivot, centre or top)." ) )
					};
					break;
				case "offset":
					offset = Triple( property.Value, "offset", where );
					break;
				case "colour":
					colour = Triple( property.Value, "colour", where );
					break;
				case "radius":
					radius = property.Value.GetSingle();
					break;
				case "flicker":
					// [EXT:evening-torches-flicker] the share by which the light dips as it flickers (presentation only)
					flicker = property.Value.ValueKind == JsonValueKind.Number ? property.Value.GetSingle() : float.NaN;
					if ( !float.IsFinite( flicker ) || flicker < 0 || flicker > MaximumFlicker )
						throw new InvalidDataException( where( $"\"flicker\" is 0 to {MaximumFlicker.ToString( CultureInfo.InvariantCulture )}." ) );
					break;
				default:
					throw new InvalidDataException( where( $"unknown light key \"{property.Name}\"." ) );
			}
		}
		if ( string.IsNullOrWhiteSpace( node ) )
			throw new InvalidDataException( where( "a light needs a \"node\"." ) );
		if ( colour is not { } channels || channels.X < 0 || channels.Y < 0 || channels.Z < 0 || channels.X > PointLightMath.ColourBound || channels.Y > PointLightMath.ColourBound || channels.Z > PointLightMath.ColourBound || channels == SVector3.Zero )
			throw new InvalidDataException( where( $"the colour of light \"{node}\" needs three channels from 0 to {PointLightMath.ColourBound.ToString( CultureInfo.InvariantCulture )}, not all 0." ) );
		if ( radius is not { } size || !float.IsFinite( size ) || size < PointLightMath.MinimumRadiusModelUnits || size > MaximumRadiusModelUnits )
			throw new InvalidDataException( where( $"the radius of light \"{node}\" is {PointLightMath.MinimumRadiusModelUnits.ToString( CultureInfo.InvariantCulture )} to {MaximumRadiusModelUnits.ToString( CultureInfo.InvariantCulture )} model units." ) );
		return new EveningLampLight( node, anchor, offset, channels, size, flicker );
	}

	private static SVector3 Triple( JsonElement value, string name, Func<string, string> where )
	{
		if ( value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3 )
			throw new InvalidDataException( where( $"\"{name}\" is three numbers." ) );
		var channels = value.EnumerateArray().Select( item => item.GetSingle() ).ToArray();
		if ( channels.Any( channel => !float.IsFinite( channel ) ) )
			throw new InvalidDataException( where( $"\"{name}\" is finite." ) );
		return new SVector3( channels[0], channels[1], channels[2] );
	}
}

/// <summary>One lamp of one model, resolved against its nodes: the node, the point in the node's frame (ground in XY, up in Z, model units), the colour, the radius and the flicker.</summary>
public readonly record struct ResolvedEveningLamp( int NodeIndex, SVector3 Local, SVector3 Colour, float RadiusModelUnits, float Flicker = 0f );

/// <summary>Resolves a lamp row against a model: finds each light's node by name and works out the point it stands at.</summary>
public static class EveningLampResolver
{
	/// <summary>
	/// The lamps of <paramref name="rule"/> on <paramref name="model"/>. A light whose node the model does not have is left out and named in <paramref name="missing"/> (the row was written for
	/// another version of the object). The local point is the anchor on the node's mesh box (the stored MD2 box, whose Y is up and which the engine turns into XY ground and Z up) plus the offset.
	/// </summary>
	public static IReadOnlyList<ResolvedEveningLamp> Resolve( ModelFile model, EveningLampRule rule, List<string>? missing = null )
	{
		var lamps = new List<ResolvedEveningLamp>();
		foreach ( var light in rule.Lights )
		{
			var node = model.Nodes.FirstOrDefault( candidate => string.Equals( candidate.Name, light.Node, StringComparison.OrdinalIgnoreCase ) );
			if ( node == null )
			{
				missing?.Add( light.Node );
				continue;
			}
			lamps.Add( new ResolvedEveningLamp( node.Index, AnchorPoint( model, node, light.Anchor ) + light.Offset, light.Colour, light.RadiusModelUnits, light.Flicker ) );
		}
		return lamps;
	}

	private static SVector3 AnchorPoint( ModelFile model, ModelNode node, EveningAnchor anchor )
	{
		if ( anchor == EveningAnchor.Pivot || node.MeshIndex < 0 || node.MeshIndex >= model.Meshes.Count )
			return SVector3.Zero;
		var mesh = model.Meshes[node.MeshIndex];
		var min = mesh.BoundsMin.GetSystemVector3();
		var max = mesh.BoundsMax.GetSystemVector3();
		if ( mesh.Vertices.Length == 0 )
			return SVector3.Zero;
		// MD2 Y is up: the engine frame swaps Y and Z (ObjectAssets.ConvertAxes)
		var centre = ObjectAssets.ConvertAxes( ( min + max ) * 0.5f );
		return anchor == EveningAnchor.Top ? new SVector3( centre.X, centre.Y, max.Y ) : centre;
	}
}

/// <summary>
/// The evening lamps of one park as a point-light source: every placed object the lamp table names shows its lights at its nodes' current pose, scaled by the lamp level of the
/// look of the moment (0 at day, so nothing is added and the park draws as it does without the extension). Asked once per frame by <see cref="PointLightSet.Gather"/>.
/// </summary>
public sealed class EveningLampSource : IPointLightSource
{
	private readonly EveningLampTable table;
	private readonly Func<IReadOnlyList<OriginalObject>> objects;
	private readonly Func<float> lampLevel;
	private readonly Func<int>? version;
	private readonly Func<double>? seconds;
	private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ObjectCatalogEntry, IReadOnlyList<ResolvedEveningLamp>> resolved = new();
	// the lamp-bearing objects of the placed set, rebuilt only when the set changes (not once per night frame), each with its flicker seed
	private readonly List<(OriginalObject Item, IReadOnlyList<ResolvedEveningLamp> Lamps, int Seed)> bearers = new();
	private IReadOnlyList<OriginalObject>? cachedList;
	private int cachedVersion = int.MinValue;
	private int cachedCount = -1;
	private readonly HashSet<string> warned = new( StringComparer.Ordinal );
	private bool reported;

	/// <summary>The lights this source put into the last frame's list (for tests and the performance notes).</summary>
	public int LastCount { get; private set; }

	/// <param name="seconds">[EXT:evening-torches-flicker] The presentation clock a flickering light follows (<see cref="TorchFlicker.At"/>); without it a flickering light is steady.</param>
	public EveningLampSource( EveningLampTable table, Func<IReadOnlyList<OriginalObject>> objects, Func<float> lampLevel, Func<int>? version = null, Func<double>? seconds = null )
	{
		this.version = version;
		this.seconds = seconds;
		this.table = table;
		this.objects = objects;
		this.lampLevel = lampLevel;
	}

	/// <summary>The lamps of an object, resolved once per catalog entry (its model and the table row do not change).</summary>
	private IReadOnlyList<ResolvedEveningLamp> LampsOf( OriginalObject item )
	{
		if ( ResolverOverride != null )
			return ResolverOverride( item );
		var entry = item.Entry;
		if ( resolved.TryGetValue( entry, out var lamps ) )
			return lamps;
		lamps = Array.Empty<ResolvedEveningLamp>();
		if ( table.Find( entry ) is { } rule )
		{
			var missing = new List<string>();
			lamps = EveningLampResolver.Resolve( item.Runtime.Model, rule, missing );
			foreach ( var node in missing.Where( node => warned.Add( entry.ArchivePath + ":" + node ) ) )
				Log.Warning( $"Evening lamp of {entry.SettingsName}: the model has no node \"{node}\"; that light is left out." );
		}
		resolved.AddOrUpdate( entry, lamps );
		return lamps;
	}

	/// <summary>Test seam: resolves an object's lamps without a catalog entry and model (an object needs a graphics device to be built).</summary>
	internal Func<OriginalObject, IReadOnlyList<ResolvedEveningLamp>>? ResolverOverride { get; set; }

	/// <summary>The number of objects in the lamp cache (for tests).</summary>
	internal int BearerCount => bearers.Count;

	/// <summary>
	/// Brings the cache of lamp-bearing objects up to date: it is rebuilt when the object list, the host's change counter or (without a counter) the list's length differs from last time. Without a counter a removal and a placement between two frames, which keep the length, go unseen: the level passes <see cref="ParkObjects.Version"/>.
	/// The order of the placed list is kept, so the lights come out in the same order as a walk over every object.
	/// </summary>
	private void Forget()
	{
		bearers.Clear();
		cachedList = null;
		cachedVersion = int.MinValue;
		cachedCount = -1;
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
		bearers.Clear();
		for ( var objectIndex = 0; objectIndex < placed.Count; objectIndex++ )
		{
			var item = placed[objectIndex];
			var lamps = LampsOf( item );
			if ( lamps.Count > 0 )
				bearers.Add( (item, lamps, TorchFlicker.SeedOf( item.Placement.X, item.Placement.Y )) );
		}
	}

	public void AppendPointLights( List<PointLight> into )
	{
		LastCount = 0;
		var level = lampLevel();
		if ( !( level > 0.002f ) )
		{
			// by day the cache would keep removed objects (and a replaced placed list) alive: drop it, the next night frame rebuilds it
			if ( cachedList != null )
				Forget();
			return;
		}
		var start = into.Count;
		Refresh();
		var now = seconds?.Invoke();
		for ( var bearerIndex = 0; bearerIndex < bearers.Count; bearerIndex++ )
		{
			var (item, lamps, seed) = bearers[bearerIndex];
			var nodes = item.NodeTransforms;
			for ( var lampIndex = 0; lampIndex < lamps.Count; lampIndex++ )
			{
				var lamp = lamps[lampIndex];
				if ( lamp.NodeIndex >= nodes.Count || item.Runtime.Animator.IsNodeHidden( lamp.NodeIndex ) )
					continue;
				var world = ObjectPlacement.SwapAxes( nodes[lamp.NodeIndex] ) * item.Placement.ModelToEngine;
				// [EXT:evening-torches-flicker] a flickering light dips with the flame it stands on (the same seed and clock), presentation only
				var strength = lamp.Flicker > 0f && now is { } time ? 1f - lamp.Flicker * ( 1f - TorchFlicker.At( time, seed ) ) : 1f;
				into.Add( new PointLight( SVector3.Transform( lamp.Local, world ), lamp.Colour * ( level * strength ), PointLightMath.ModelUnitsToEngine( lamp.RadiusModelUnits ) ) );
			}
		}
		LastCount = into.Count - start;
		if ( LastCount > 0 && !reported )
		{
			reported = true;
			Log.Trace( $"Evening lamps: {LastCount} lights from the placed objects the lamp table names ({table.Rules.Count} rows)." );
		}
	}
}

/// <summary>[EXT:debug-evening] A fixed set of lamp lights that follow the lamp level (the smoke-only <c>--debug-evening-lamps</c> option: many lamps for a performance run).</summary>
public sealed class DebugEveningLampSource( IReadOnlyList<PointLight> lamps, Func<float> lampLevel ) : IPointLightSource
{
	public void AppendPointLights( List<PointLight> into )
	{
		var level = lampLevel();
		if ( !( level > 0.002f ) )
			return;
		foreach ( var lamp in lamps )
			into.Add( lamp with { Colour = lamp.Colour * level } );
	}
}
