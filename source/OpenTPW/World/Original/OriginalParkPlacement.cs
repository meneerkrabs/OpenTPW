namespace OpenTPW;

/// <summary>Why a footprint cannot be built on in an imported original level.</summary>
public enum OriginalPlacementResult
{
	Allowed,
	OutsideTerrain,
	NoTerrainSurface,
	Blocked,
	Water,
	EntranceArea,
	FixedWalkway,
	Path,
	Occupied,
	/// <summary>The park does not own the cell (map flag 0x40; Buy Land adds land).</summary>
	NotOwned,
	/// <summary>An entrance's or exit's outside cell refuses the access the original lays there (tp.exe <c>0x528A70</c>).</summary>
	AccessBlocked,
	/// <summary>[EXT:pools-changing-room] A changing room needs pool water beside its back (docs/POOLS.md).</summary>
	NeedsPool,
	/// <summary>[EXT:pools-extras] A pool extra needs its spot: a straight pool edge, a changing room beside it, or water beside it (docs/POOLS.md, "Extras").</summary>
	NeedsPoolEdge,
	/// <summary>[EXT:pools-attractions] A water attraction (rope swing, jets, wave machine, spa) needs pool water on its water side (docs/POOLS.md).</summary>
	NeedsPoolWater
}

/// <summary>
/// Grid mapping and build rules for imported original levels. One original cell is 10 MD2 units,
/// which is <see cref="CellSize"/> engine units at the prototype ride's 0.2 model scale; the
/// heightfield is centred on the engine origin. A cell's terrain rule is the original's: the type
/// the map start gives it (holes, the initial path; docs/MAP.md) and imported save paths/objects.
/// Ownership and the object rules live with the callers (ParkObjects, the path and queue builders).
/// </summary>
public static class OriginalParkPlacement
{
	public const float ModelScale = PrototypeRide.ModelScale;
	public const float CellSize = 10 * ModelScale;

	public static System.Numerics.Vector2 GetOrigin( ModelHeightfield field ) =>
		new( field.CellCountX * field.CellSizeX * ModelScale / 2, field.CellCountZ * field.CellSizeZ * ModelScale / 2 );

	/// <summary>Converts an MD2 world position (Y up) to engine space (Z up), centred on the heightfield.</summary>
	public static System.Numerics.Vector3 ToEngine( ModelHeightfield field, System.Numerics.Vector3 modelPosition )
	{
		var origin = GetOrigin( field );
		return new System.Numerics.Vector3( modelPosition.X * ModelScale - origin.X, modelPosition.Z * ModelScale - origin.Y, modelPosition.Y * ModelScale );
	}

	/// <summary>
	/// [EXT:online-coop-presence] The park position, in cells (0.5 = a cell's centre), of the engine ground point (<paramref name="engineX"/>, <paramref name="engineY"/>):
	/// <see cref="ToEngine"/> undone on the ground plane, not clamped.
	/// </summary>
	public static (float X, float Y) ToCells( ModelHeightfield field, float engineX, float engineY )
	{
		var origin = GetOrigin( field );
		return ((engineX + origin.X) / (field.CellSizeX * ModelScale), (engineY + origin.Y) / (field.CellSizeZ * ModelScale));
	}

	public static bool TryGetCell( ModelHeightfield field, float engineX, float engineY, out int x, out int y )
	{
		var origin = GetOrigin( field );
		x = (int)MathF.Floor( (engineX + origin.X) / (field.CellSizeX * ModelScale) );
		y = (int)MathF.Floor( (engineY + origin.Y) / (field.CellSizeZ * ModelScale) );
		return float.IsFinite( engineX ) && float.IsFinite( engineY ) && x >= 0 && y >= 0 && x < field.CellCountX && y < field.CellCountZ;
	}

	/// <summary>Engine position of the centre of cell (x, y) at the mean of its corner heights.</summary>
	public static System.Numerics.Vector3 GetCellCenter( ModelHeightfield field, int x, int y )
	{
		var height = (field.GetCornerHeight( x, y ) + field.GetCornerHeight( x + 1, y ) + field.GetCornerHeight( x, y + 1 ) + field.GetCornerHeight( x + 1, y + 1 )) / 4;
		return ToEngine( field, new System.Numerics.Vector3( (x + 0.5f) * field.CellSizeX, height, (y + 0.5f) * field.CellSizeZ ) );
	}

	/// <summary>The engine height of a heightfield corner (the landscape grid vertex the original's instance height reads, 0x466D70); the index is clamped to the grid.</summary>
	public static float GetCornerEngineHeight( ModelHeightfield field, int x, int y ) =>
		field.GetCornerHeight( Math.Clamp( x, 0, field.CellCountX ), Math.Clamp( y, 0, field.CellCountZ ) ) * ModelScale;

	public static OriginalPlacementResult Check( OriginalPark park, int minX, int minY, int maxX, int maxY ) =>
		Check( park.Map, park.Heightfield, park.Save, minX, minY, maxX, maxY );

	public static OriginalPlacementResult Check( MapFile map, ModelHeightfield field, OriginalParkImport? save, int minX, int minY, int maxX, int maxY )
	{
		if ( minX < 0 || minY < 0 || minX > maxX || minY > maxY || maxX >= Math.Min( field.CellCountX, map.CellCountX ) || maxY >= Math.Min( field.CellCountZ, map.CellCountY ) )
			return OriginalPlacementResult.OutsideTerrain;
		for ( var x = minX; x <= maxX; x++ )
		{
			for ( var y = minY; y <= maxY; y++ )
			{
				var result = CheckCell( map, field, save, x, y );
				if ( result != OriginalPlacementResult.Allowed )
					return result;
			}
		}
		return OriginalPlacementResult.Allowed;
	}

	// [BIN:TPW-EXE:0x535670 cell validator] the placement 0x528a70 tests each footprint cell through 0x532fc0 (mode 0x200) with 0x535670, which reads the cell's type (+8), flags (+0xE: 0x40 unowned, 0x20 path) and links, never its MAP byte (+0x26)
	// [BIN:TPW-EXE:0x536490 map init] the cell types come from the map start: holes are 2, 0x1e or 7 (refused by type), a surface cell is 1 for InitialPath or else 0 whatever its other MAP bits (Blocked, Water, EntranceArea and FixedWalkway on a surface pick types the init overwrites, 0x536524..0x536548)
	private static OriginalPlacementResult CheckCell( MapFile map, ModelHeightfield field, OriginalParkImport? save, int x, int y )
	{
		var flags = map.GetFlagsAt( x, y );
		var surface = (field.GetCellWord( x, y ) & 1) == 0;
		switch ( ParkLand.OriginalCellType( flags, surface ) )
		{
			case 2:
				return OriginalPlacementResult.Water;
			case 0x1e:
				return OriginalPlacementResult.FixedWalkway;
			case 7:
				// the reason only names the hole; a Blocked (rock) hole keeps its own reason, which the coaster site does not count as missing ground
				return flags.HasFlag( MapCellFlags.EntranceArea ) ? OriginalPlacementResult.EntranceArea
					: flags.HasFlag( MapCellFlags.Blocked ) ? OriginalPlacementResult.Blocked : OriginalPlacementResult.NoTerrainSurface;
			case 1:
				return OriginalPlacementResult.Path;
		}
		if ( save != null && save.Cells[x, y].IsPath )
			return OriginalPlacementResult.Path;
		if ( save != null && (save.Cells[x, y].IsOccupied || save.IsOccupiedByObject( x, y )) )
			return OriginalPlacementResult.Occupied;
		return OriginalPlacementResult.Allowed;
	}
}
