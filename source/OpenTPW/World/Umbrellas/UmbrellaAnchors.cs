using System.Text.Json;

namespace OpenTPW;

/// <summary>
/// Where a guest's hand is in a sprite frame (docs/UMBRELLAS.md, "Anchors"): the point an open umbrella's handle goes to. Two tables of the extension's own content (<c>content/umbrellas</c>): <c>anchors.json</c>, exact
/// numbers from the rig of the 2x kid sets (256 pixels per unit, the sets of the pools extension's guest art), and <c>anchors-original.json</c>, measured on the game's own 1x kid sets and the four costume sets by
/// <c>opentpw-kit measure-anchors</c> (128 pixels per unit). Both are pixels from the frame's hotspot (the feet), x right and y down, one point per step of the standing (slot 0), walking (1) and waiting (14) animations,
/// for the five stored directions; a mirrored direction (5 to 7) uses its mirror's point with x negated. A set the tables do not know gets TPI's rule instead (<see cref="FallbackLift"/>).
/// </summary>
// [EXT:umbrellas-content] OpenTPW's own tables; the rules around them are TPI's (docs/UMBRELLAS.md)
public sealed class UmbrellaAnchors
{
	/// <summary>The pixels per unit of the rig table's frames (the 2x kid sets).</summary>
	public const int RigPixelsPerUnit = 256;
	/// <summary>
	/// The hand height TPI gives a sprite set it has no entry for, in original units above the guest's feet (the shipped table's 5.5 to 6.5 for its kids; 5.7 is the middle one): the umbrella's handle is placed straight above the guest's
	/// position, not at a hand.
	/// </summary>
	// [BIN:TPI-EXE:0x60b29b umbrella placement] the default camera path adds the set's Y (field 1 of the 0x38 byte row of PersonSpriteUmbrellaModifiers, 0xA01E1C + 0x38 x type) to the guest's height before placing the sprite (call 0x40a6c8)
	public const float FallbackLift = 5.7f;

	/// <summary>One set's points: per slot name (0 stand, 1 walk, 14 wait), per direction, per step.</summary>
	private sealed class SetTable
	{
		public readonly Dictionary<int, Animation> Slots = new();
	}

	/// <summary>One animation's points per direction and step, with the steady hand (the mean over the steps) of each direction computed once, when the table loads.</summary>
	private sealed class Animation
	{
		public readonly (float X, float Y)[][] Points;
		public readonly (float X, float Y)[] Means;

		public Animation( (float X, float Y)[][] points )
		{
			Points = points;
			Means = points.Select( steps => (steps.Average( point => point.X ), steps.Average( point => point.Y )) ).ToArray();
		}
	}

	private readonly Dictionary<(string Set, int PixelsPerUnit), SetTable> sets = new();
	// The last set asked for: a frame asks for the same few sets again and again, so the string-keyed lookup is skipped when the name is the same instance.
	private string? lastSet;
	private int lastPixelsPerUnit;
	private SetTable? lastTable;

	/// <summary>The number of sets the tables hold.</summary>
	public int Count => sets.Count;

	/// <summary>The slot whose table the umbrella follows for a guest drawn in an animation slot: standing, walking, waiting; any other (running, a ride's pose) follows walking, the closest for a moving body.</summary>
	public static int TableSlot( int slot ) => slot switch
	{
		GuestSpriteAtlas.StandSlot => GuestSpriteAtlas.StandSlot,
		GuestSpriteAtlas.WaitSlot => GuestSpriteAtlas.WaitSlot,
		_ => GuestSpriteAtlas.WalkSlot
	};

	/// <summary>Reads both tables of a content folder; a missing or broken file leaves its sets out (a warning says so).</summary>
	public static UmbrellaAnchors Load( string folder )
	{
		var anchors = new UmbrellaAnchors();
		anchors.Read( Path.Combine( folder, "anchors.json" ), RigPixelsPerUnit, name => "SPR_" + name );
		anchors.Read( Path.Combine( folder, "anchors-original.json" ), GuestAtlasFrame.ReferencePixelsPerUnit, name => name );
		return anchors;
	}

	/// <summary>Reads one table text (tests): its sets are stored under <paramref name="pixelsPerUnit"/>.</summary>
	internal static UmbrellaAnchors Parse( string json, int pixelsPerUnit, Func<string, string>? setName = null )
	{
		var anchors = new UmbrellaAnchors();
		anchors.Add( json, pixelsPerUnit, setName ?? (name => name), "table" );
		return anchors;
	}

	private void Read( string file, int pixelsPerUnit, Func<string, string> setName )
	{
		if ( !File.Exists( file ) )
			return;
		try
		{
			Add( File.ReadAllText( file ), pixelsPerUnit, setName, file );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or FormatException )
		{
			Log?.Warning( $"The umbrella hand anchors in {file} are not available: {exception.Message}" );
		}
	}

	private void Add( string json, int pixelsPerUnit, Func<string, string> setName, string origin )
	{
		using var document = JsonDocument.Parse( json );
		var root = document.RootElement;
		if ( root.GetProperty( "schema" ).GetInt32() != 1 || root.GetProperty( "pixelsPerUnit" ).GetInt32() != pixelsPerUnit )
			throw new InvalidDataException( $"{origin} is not a schema 1 table at {pixelsPerUnit} pixels per unit." );
		foreach ( var set in root.GetProperty( "sets" ).EnumerateObject() )
		{
			var table = new SetTable();
			foreach ( var (name, slot) in new[] { ("stand", GuestSpriteAtlas.StandSlot), ("walk", GuestSpriteAtlas.WalkSlot), ("wait", GuestSpriteAtlas.WaitSlot) } )
			{
				if ( !set.Value.TryGetProperty( name, out var animation ) )
					continue;
				var directions = animation.GetProperty( "frames" ).EnumerateArray().Select( steps => steps.EnumerateArray()
					.Select( point => (point[0].GetSingle(), point[1].GetSingle()) ).ToArray() ).ToArray();
				if ( directions.Length == 0 || directions.Any( steps => steps.Length == 0 ) || directions.SelectMany( steps => steps ).Any( point => !float.IsFinite( point.Item1 ) || !float.IsFinite( point.Item2 ) ) )
					throw new InvalidDataException( $"{origin}: the set {set.Name} has an empty or invalid {name} table." );
				table.Slots[slot] = new Animation( directions );
			}
			if ( table.Slots.Count > 0 )
				sets[(setName( set.Name ), pixelsPerUnit)] = table;
		}
	}

	/// <summary>
	/// The hand of a frame in pixels from its hotspot, or null for a set the tables do not know. <paramref name="direction"/> is the stored direction the frame was drawn with (0 to 4, mirrored or not); a mirrored frame
	/// negates x. A slot the set has no table for follows its standing table. <paramref name="steady"/> takes the mean over the animation's steps instead of the step's own point: the walking hand swings with
	/// the arm, but an umbrella is held still.
	/// </summary>
	public (float X, float Y)? Hand( string set, int pixelsPerUnit, int slot, int direction, int step, bool mirror, bool steady )
	{
		SetTable? table;
		if ( ReferenceEquals( set, lastSet ) && pixelsPerUnit == lastPixelsPerUnit )
			table = lastTable;
		else
		{
			sets.TryGetValue( (set, pixelsPerUnit), out table );
			(lastSet, lastPixelsPerUnit, lastTable) = (set, pixelsPerUnit, table);
		}
		if ( table == null )
			return null;
		if ( !table.Slots.TryGetValue( TableSlot( slot ), out var animation ) && !table.Slots.TryGetValue( GuestSpriteAtlas.StandSlot, out animation ) )
			return null;
		var index = Math.Clamp( direction, 0, animation.Points.Length - 1 );
		var steps = animation.Points[index];
		var (x, y) = steady ? animation.Means[index] : steps[(int)((uint)Math.Max( step, 0 ) % (uint)steps.Length)];
		return ((float)(mirror ? -x : x), (float)y);
	}
}
