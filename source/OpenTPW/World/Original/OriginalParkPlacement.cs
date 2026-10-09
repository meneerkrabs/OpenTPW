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
	Occupied
}

/// <summary>
/// Grid mapping and build rules for imported original levels. One original cell is 10 MD2 units,
/// which is <see cref="CellSize"/> engine units at the prototype ride's 0.2 model scale; the
/// heightfield is centred on the engine origin. The rules only use verified data: MAP flags
/// (docs/MAP.md), heightfield holes, and imported save paths/objects. They are OpenTPW sandbox
/// rules grounded in that data, not a reproduction of the original game's build checks.
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

	private static OriginalPlacementResult CheckCell( MapFile map, ModelHeightfield field, OriginalParkImport? save, int x, int y )
	{
		var flags = map.GetFlagsAt( x, y );
		if ( flags.HasFlag( MapCellFlags.Water ) )
			return OriginalPlacementResult.Water;
		if ( flags.HasFlag( MapCellFlags.EntranceArea ) )
			return OriginalPlacementResult.EntranceArea;
		if ( flags.HasFlag( MapCellFlags.FixedWalkway ) )
			return OriginalPlacementResult.FixedWalkway;
		if ( flags.HasFlag( MapCellFlags.Blocked ) )
			return OriginalPlacementResult.Blocked;
		if ( field.IsHole( x, y ) )
			return OriginalPlacementResult.NoTerrainSurface;
		if ( flags.HasFlag( MapCellFlags.InitialPath ) || (save != null && save.Cells[x, y].IsPath) )
			return OriginalPlacementResult.Path;
		if ( save != null && (save.Cells[x, y].IsOccupied || save.IsOccupiedByObject( x, y )) )
			return OriginalPlacementResult.Occupied;
		return OriginalPlacementResult.Allowed;
	}
}
