namespace OpenTPW;

/// <summary>
/// The changing room of the pools extension (docs/POOLS.md, "Changing rooms"): an object whose .sam says <c>Pools.ChangingRoom 1</c>, shipped with OpenTPW as the extension's own content
/// (<see cref="PoolContent"/>). Its entrance faces the path, as a toilet's does; the cells beyond its back, opposite the entrance, are its pool-side cells, and a changing room may only
/// be placed with at least one of them holding pool water.
/// </summary>
// [EXT:pools-changing-room] OpenTPW's own object and placement rule; the original game has no pools
public static class PoolChangingRooms
{
	/// <summary>The .sam key that marks an object as a changing room.</summary>
	public const string SettingKey = "Pools.ChangingRoom";

	/// <summary>Whether the catalog entry is a changing room.</summary>
	public static bool IsChangingRoom( ObjectCatalogEntry entry ) => entry.Settings.GetBool( SettingKey );

	/// <summary>
	/// The pool-side candidates of a changing room anchored at (<paramref name="anchorX"/>, <paramref name="anchorY"/>) with the rotation: for every footprint cell whose neighbour opposite the entrance's
	/// outward direction is not part of the footprint, that neighbour, in footprint order (shape row by row). Empty for a shape without an entrance.
	/// </summary>
	public static IReadOnlyList<(int X, int Y)> AccessCandidates( ObjectShape shape, int anchorX, int anchorY, int rotation )
	{
		ArgumentNullException.ThrowIfNull( shape );
		var entrance = shape.Cells.FirstOrDefault( cell => cell.Kind == ObjectCellKind.Entrance );
		if ( entrance == default )
			return Array.Empty<(int X, int Y)>();
		var (du, dv) = ObjectFootprint.OutwardDirection( shape, entrance );
		var (bx, by) = ObjectFootprint.RotateDirection( rotation, -du, -dv );
		var footprint = ObjectFootprint.GetCells( shape, anchorX, anchorY, rotation ).Select( cell => (cell.X, cell.Y) ).ToHashSet();
		var candidates = new List<(int X, int Y)>();
		foreach ( var cell in ObjectFootprint.GetCells( shape, anchorX, anchorY, rotation ) )
		{
			var next = (X: cell.X + bx, Y: cell.Y + by);
			if ( !footprint.Contains( next ) && !candidates.Contains( next ) )
				candidates.Add( next );
		}
		return candidates;
	}

	/// <summary>The placement rule: a changing room needs pool water beside its back. Null (no opinion) for any other object and while the extension is off.</summary>
	public static OriginalPlacementResult? PlacementRule( ObjectCatalogEntry entry, ParkCellMap? cells, int anchorX, int anchorY, int rotation )
	{
		if ( !FeatureGates.Pools || cells == null || !IsChangingRoom( entry ) )
			return null;
		foreach ( var (x, y) in AccessCandidates( entry.Shape, anchorX, anchorY, rotation ) )
			if ( cells.InBounds( x, y ) && cells.RawTypeAt( x, y ) == ParkCellTypes.PoolWater )
				return null;
		return OriginalPlacementResult.NeedsPool;
	}
}
