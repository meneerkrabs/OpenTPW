namespace OpenTPW;

/// <summary>
/// The per-object height map of the original (object +0xCC): the <c>.hmp</c> that ships beside an object's mesh, read through its per-cell maxima table (one byte per
/// map cell of the object's footprint). The original's ground lookup 0x00452AE0 returns, for a map cell owned by such an object, that byte × 100/255 plus the object's
/// base height; the tour cars clear it (docs/TOURS.md, TOUR-005). Other consumers of the maps are not traced (TRACK-002).
/// </summary>
// [BIN:TPW-EXE:0x00451640 height map loader] the file's header words at +0xC, +0x10 and +0x14 (profile, per-cell maxima, per-cell flags) are made absolute pointers; +8 is the width in cells. The lookup 0x00452AE0 reads the byte at maxima − width × row − column, with row and column the truncated tenths of the cell's offset from the object's instance origin, turned by the instance's rotation
public sealed class ObjectHeightMaps
{
	/// <summary>The byte's scale: 100/255 (0x006FE408), model units per byte.</summary>
	public const float ByteScale = 0.39215687f;

	private readonly Dictionary<ObjectCatalogEntry, HmpFile?> maps = new();

	/// <summary>The height map shipped beside the entry's mesh (<c>&lt;mesh&gt;.hmp</c> in its archive), or null when the archive has none or it is malformed.</summary>
	public HmpFile? MapOf( ObjectCatalogEntry entry )
	{
		if ( maps.TryGetValue( entry, out var cached ) )
			return cached;
		HmpFile? map = null;
		var name = Path.GetFileNameWithoutExtension( entry.ModelPath ) + ".hmp";
		try
		{
			var path = entry.FileSystem.GetFiles( entry.ArchivePath ).FirstOrDefault( file => string.Equals( Path.GetFileName( file ), name, StringComparison.OrdinalIgnoreCase ) );
			if ( path != null )
			{
				using var stream = entry.FileSystem.OpenRead( path );
				map = new HmpFile( stream );
			}
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or IOException )
		{
			map = null;
		}
		maps[entry] = map;
		return map;
	}

	/// <summary>
	/// The ground the original's lookup returns for a map cell owned by <paramref name="item"/>, in model units: the object's height-map byte × 100/255 plus its base height.
	/// Null when the object has no map or the cell lies outside it (the original reads unchecked memory there; the caller then takes the terrain).
	/// </summary>
	// [BIN:TPW-EXE:0x00452B64 object ground] the height is the byte × 0.39215687 (0x006FE408) plus the instance's height (+0x44); the cell's offset from the instance origin is (10 × cell − instance position) and the instance origin is OpenTPW's model-to-engine translation (the corner offsets 0, 10 of the rotated frames)
	// [APPROX:TOUR-005] only the maps that ship as files are read (every catalogue object has one); the original rebuilds a missing or mis-sized map from the mesh (0x00451880), takes the cell's owner from 0x0053BF30 (track cells redirected) and also reads the map of a type-3 thing on the cell, none of which is modelled, so those cells give the terrain
	public float? Height( OriginalObject item, int cellX, int cellY )
	{
		if ( item.IsDeleted || item.Entry.IsFixedItem || MapOf( item.Entry ) is not { } map )
			return null;
		var placement = item.Placement;
		var (originX, originZ) = InstanceOrigin( placement );
		if ( CellIndex( map, placement.Rotation, originX, originZ, cellX, cellY ) is not { } index )
			return null;
		return map.CellMaxima.Span[index] * ByteScale + placement.BaseHeight / ObjectPlacement.ModelScale;
	}

	/// <summary>The instance position of a placed object on the map's two ground axes, in model units: the anchor cell × 10 plus the rotated frame's corner offset.</summary>
	internal static (int X, int Z) InstanceOrigin( ObjectPlacement placement ) => placement.Rotation switch
	{
		90 => (placement.X * 10, placement.Y * 10 + 10),
		180 => (placement.X * 10 + 10, placement.Y * 10 + 10),
		270 => (placement.X * 10 + 10, placement.Y * 10),
		_ => (placement.X * 10, placement.Y * 10)
	};

	/// <summary>
	/// The index into the per-cell maxima table of the map cell (<paramref name="cellX"/>, <paramref name="cellY"/>), or null when it lies outside the table. The offsets (a, b) of the cell
	/// corner from the object's origin are turned by the instance rotation (the +10 terms reflect a cell's width), and row and column are the truncated tenths, as the lookup computes them.
	/// </summary>
	internal static int? CellIndex( HmpFile map, int rotation, float originX, float originZ, int cellX, int cellY )
	{
		var a = cellX * 10f - originX;
		var b = cellY * 10f - originZ;
		var (rowArgument, columnArgument) = rotation switch
		{
			90 => (a, -b - 10f),
			180 => (-b - 10f, -a - 10f),
			270 => (-a - 10f, b),
			_ => (b, a)
		};
		const double scale = -0.10000000149011612;
		var row = (int)(scale * rowArgument);
		var column = (int)(scale * columnArgument);
		// the address is maxima − width × row − column: the row and column count downwards in the original's terms, so a cell inside the map has both at or below 0
		var mapRow = -row;
		var mapColumn = -column;
		return mapRow >= 0 && mapRow < map.Height && mapColumn >= 0 && mapColumn < map.Width ? mapRow * map.Width + mapColumn : null;
	}
}
