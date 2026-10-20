using System.Text.Json;

namespace OpenTPW;

/// <summary>
/// Where a guest's head and hands are in one sprite frame, in pixels from the frame's hotspot (the feet), x right and y down (docs/WINTER.md, "Anchors"). The head's top is where a hat goes and the head's centre
/// where earmuffs go; a mitten goes to the end point of its hand (<see cref="HasHands"/>: only the 2x kid sets know them), turned by the forearm's screen angle (stored as its sine and cosine) and drawn only while
/// its hand is seen (<see cref="Seen"/>).
/// </summary>
public readonly record struct WinterAnchor( float HeadTopX, float HeadTopY, float HeadX, float HeadY, bool HasHands, float RightX, float RightY, float RightSin, float RightCos, float LeftX, float LeftY, float LeftSin, float LeftCos, int Seen )
{
	/// <summary>The bit of <see cref="Seen"/> for the kid's right hand.</summary>
	public const int RightSeen = 1;
	/// <summary>The bit of <see cref="Seen"/> for the kid's left hand.</summary>
	public const int LeftSeen = 2;
}

/// <summary>
/// The anchors of the warm items (docs/WINTER.md, "Anchors"): two tables of the extension's own content (<c>content/winter</c>). <c>anchors.json</c> (schema 2) is exact, from the rig of the 2x kid sets (256 pixels per unit): for
/// every land slot, frame and direction the head's top and centre, both hands' end points with the forearm's angle and whether the hand is seen. <c>anchors-original.json</c> (schema 2, head only) is measured on the game's own 1x kid
/// sets and the four costume sets by <c>opentpw-kit measure-head-anchors</c> (128 pixels per unit): numbers only, no mittens (an original frame has no reliable hand point). Directions 0 to 4 are stored; a mirrored direction (5 to 7)
/// uses its mirror's point with x negated (the caller does that, see <see cref="WinterAnchorTable.Mirrored"/>). Everything is parsed into arrays at load, so a lookup in a frame allocates nothing.
/// </summary>
// [EXT:winter-anchors] OpenTPW's own tables; the 2x one is exact by construction, the 1x one is measured numbers only (user-approved 2026-10-18)
public sealed class WinterAnchorTable
{
	/// <summary>The pixels per unit of the rig table's frames (the 2x kid sets).</summary>
	public const int RigPixelsPerUnit = 256;

	/// <summary>One set's animations by slot number (null: the set has no table for that slot).</summary>
	private sealed class SetTable
	{
		public readonly WinterAnchor[][]?[] Slots = new WinterAnchor[][]?[32];
	}

	private readonly Dictionary<(string Set, int PixelsPerUnit), SetTable> sets = new();
	// The last set asked for: a frame asks for the same few sets again and again, so the string-keyed lookup is skipped when the name is the same instance.
	private string? lastSet;
	private int lastPixelsPerUnit;
	private SetTable? lastTable;

	/// <summary>The number of sets the tables hold.</summary>
	public int Count => sets.Count;

	/// <summary>The anchor of a point that is mirrored: x negated (and a forearm's angle too, which is its sine here).</summary>
	public static WinterAnchor Mirrored( in WinterAnchor anchor ) => anchor with
	{
		HeadTopX = -anchor.HeadTopX, HeadX = -anchor.HeadX,
		RightX = -anchor.RightX, RightSin = -anchor.RightSin, LeftX = -anchor.LeftX, LeftSin = -anchor.LeftSin
	};

	/// <summary>Reads both tables of a content folder; a missing or broken file leaves its sets out (a warning says so).</summary>
	public static WinterAnchorTable Load( string folder )
	{
		var table = new WinterAnchorTable();
		table.Read( Path.Combine( folder, "anchors.json" ), RigPixelsPerUnit, name => "SPR_" + name );
		table.Read( Path.Combine( folder, "anchors-original.json" ), GuestAtlasFrame.ReferencePixelsPerUnit, name => name );
		return table;
	}

	/// <summary>Reads one table text (tests): its sets are stored under <paramref name="pixelsPerUnit"/>.</summary>
	internal static WinterAnchorTable Parse( string json, int pixelsPerUnit, Func<string, string>? setName = null )
	{
		var table = new WinterAnchorTable();
		table.Add( json, pixelsPerUnit, setName ?? (name => name), "table" );
		return table;
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
			Log?.Warning( $"The warm item anchors in {file} are not available: {exception.Message}" );
		}
	}

	private static int FieldIndex( string[] fields, string name ) => Array.IndexOf( fields, name );

	private void Add( string json, int pixelsPerUnit, Func<string, string> setName, string origin )
	{
		using var document = JsonDocument.Parse( json );
		var root = document.RootElement;
		if ( root.GetProperty( "schema" ).GetInt32() != 2 || root.GetProperty( "pixelsPerUnit" ).GetInt32() != pixelsPerUnit )
			throw new InvalidDataException( $"{origin} is not a schema 2 table at {pixelsPerUnit} pixels per unit." );
		var fields = root.GetProperty( "fields" ).EnumerateArray().Select( field => field.GetString() ?? "" ).ToArray();
		int headTopX = FieldIndex( fields, "headTopX" ), headTopY = FieldIndex( fields, "headTopY" ), headX = FieldIndex( fields, "headX" ), headY = FieldIndex( fields, "headY" );
		int rightX = FieldIndex( fields, "rightHandX" ), rightY = FieldIndex( fields, "rightHandY" ), rightAngle = FieldIndex( fields, "rightHandAngle" );
		int leftX = FieldIndex( fields, "leftHandX" ), leftY = FieldIndex( fields, "leftHandY" ), leftAngle = FieldIndex( fields, "leftHandAngle" ), seen = FieldIndex( fields, "visible" );
		if ( headTopX < 0 || headTopY < 0 || headX < 0 || headY < 0 )
			throw new InvalidDataException( $"{origin} has no head fields." );
		var hands = rightX >= 0 && rightY >= 0 && rightAngle >= 0 && leftX >= 0 && leftY >= 0 && leftAngle >= 0 && seen >= 0;
		foreach ( var set in root.GetProperty( "sets" ).EnumerateObject() )
		{
			var table = new SetTable();
			foreach ( var slot in set.Value.GetProperty( "slots" ).EnumerateObject() )
			{
				var number = slot.Value.GetProperty( "slot" ).GetInt32();
				if ( number is < 0 or >= 32 )
					throw new InvalidDataException( $"{origin}: the set {set.Name} has a slot {number} outside 0 to 31." );
				var directions = slot.Value.GetProperty( "points" ).EnumerateArray().Select( steps => steps.EnumerateArray().Select( point =>
				{
					float Get( int index ) => point[index].GetSingle() is var value && float.IsFinite( value ) ? value : throw new InvalidDataException( $"{origin}: a point of {set.Name} {slot.Name} is not finite." );
					if ( !hands )
						return new WinterAnchor( Get( headTopX ), Get( headTopY ), Get( headX ), Get( headY ), false, 0, 0, 0, 1, 0, 0, 0, 1, 0 );
					var (rightSin, rightCos) = SinCosOfDegrees( Get( rightAngle ) );
					var (leftSin, leftCos) = SinCosOfDegrees( Get( leftAngle ) );
					return new WinterAnchor( Get( headTopX ), Get( headTopY ), Get( headX ), Get( headY ), true, Get( rightX ), Get( rightY ), rightSin, rightCos, Get( leftX ), Get( leftY ), leftSin, leftCos, (int)Get( seen ) );
				} ).ToArray() ).ToArray();
				if ( directions.Length == 0 || directions.Any( steps => steps.Length == 0 ) )
					throw new InvalidDataException( $"{origin}: the set {set.Name} has an empty {slot.Name} table." );
				table.Slots[number] = directions;
			}
			if ( table.Slots.Any( slot => slot != null ) )
				sets[(setName( set.Name ), pixelsPerUnit)] = table;
		}
	}

	private static (float Sin, float Cos) SinCosOfDegrees( float degrees ) => MathF.SinCos( degrees * (MathF.PI / 180f) );

	/// <summary>
	/// The slot whose table a frame in animation slot <paramref name="slot"/> follows when the set has none for it: walking for a moving body (a ride's pose, running), standing when there is no walking table either.
	/// </summary>
	private static WinterAnchor[][]? Animation( SetTable table, int slot )
	{
		if ( (uint)slot < 32u && table.Slots[slot] is { } exact )
			return exact;
		return table.Slots[GuestSpriteAtlas.WalkSlot] ?? table.Slots[GuestSpriteAtlas.StandSlot];
	}

	/// <summary>
	/// The anchor of a frame, or false for a set the tables do not know. <paramref name="direction"/> is the stored direction the frame was drawn with (0 to 4, mirrored or not; the caller mirrors the point), <paramref name="step"/> the
	/// animation's step (wrapped to the table's length).
	/// </summary>
	public bool TryGet( string set, int pixelsPerUnit, int slot, int direction, int step, out WinterAnchor anchor )
	{
		anchor = default;
		SetTable? table;
		if ( ReferenceEquals( set, lastSet ) && pixelsPerUnit == lastPixelsPerUnit )
			table = lastTable;
		else
		{
			sets.TryGetValue( (set, pixelsPerUnit), out table );
			(lastSet, lastPixelsPerUnit, lastTable) = (set, pixelsPerUnit, table);
		}
		if ( table == null || Animation( table, slot ) is not { } animation )
			return false;
		var steps = animation[Math.Clamp( direction, 0, animation.Length - 1 )];
		anchor = steps[(int)((uint)Math.Max( step, 0 ) % (uint)steps.Length)];
		return true;
	}
}
