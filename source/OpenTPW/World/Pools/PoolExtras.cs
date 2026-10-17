namespace OpenTPW;

/// <summary>The pool extras of the pools extension (docs/POOLS.md, "Extras"); the values are the .sam key <see cref="PoolExtras.SettingKey"/>.</summary>
// [EXT:pools-extras] OpenTPW's own objects; the original game has no pools
public enum PoolExtraKind
{
	None = 0,
	/// <summary>A ladder on a straight pool edge.</summary>
	Ladder = 1,
	/// <summary>A diving board on a straight pool edge.</summary>
	DivingBoard = 2,
	/// <summary>A shower on a deck cell beside a changing room.</summary>
	Shower = 3,
	/// <summary>A sun lounger with two seats beside the pool.</summary>
	Lounger = 4,
	/// <summary>A decorative lamp beside the pool.</summary>
	Lamp = 5,
	/// <summary>A decorative light on a straight edge that makes the water glow.</summary>
	UnderwaterLight = 6
}

/// <summary>A placed pool extra as the swim session sees it: the thing id the activity targets, its kind, its rotation and the cells it stands on.</summary>
public sealed record PoolExtraSite( int ThingId, PoolExtraKind Kind, int Rotation, IReadOnlyList<(int X, int Y)> Cells );

/// <summary>
/// The pool extras: ladder, diving board, shower, sun lounger, pool lamp and underwater light (docs/POOLS.md, "Extras"). Objects whose .sam says <c>Pools.Extra n</c>, shipped as the
/// extension's own content (<see cref="PoolContent"/>). They stand on ordinary land cells (the pool's deck is empty owned ground; no new cell type), face the water with their back (the top of
/// <c>Info.Shape</c>, the same side as a changing room's back) and are placed by <see cref="PlacementRule"/>.
/// </summary>
// [EXT:pools-extras] OpenTPW's own objects and placement rules; the original game has no pools
public static class PoolExtras
{
	/// <summary>The .sam key that marks an object as a pool extra and says which (1 ladder, 2 diving board, 3 shower, 4 lounger, 5 lamp, 6 underwater light).</summary>
	public const string SettingKey = "Pools.Extra";

	/// <summary>The four directions of the grid in a fixed order (+Y, +X, -Y, -X), the index an activity keeps in its phase.</summary>
	public static readonly (int X, int Y)[] Directions = { (0, 1), (1, 0), (0, -1), (-1, 0) };

	/// <summary>The kind of the catalog entry (<see cref="PoolExtraKind.None"/> for any other object and while the extension is off).</summary>
	public static PoolExtraKind KindOf( ObjectCatalogEntry entry ) =>
		FeatureGates.Pools && entry.Settings.GetInt( SettingKey ) is var value and >= 1 and <= 6 ? (PoolExtraKind)value : PoolExtraKind.None;

	/// <summary>The grid direction from an extra's cell to the water: the object's back (+V of its shape) turned by the rotation.</summary>
	public static (int X, int Y) WaterDirection( int rotation ) => ObjectFootprint.RotateDirection( rotation, 0, 1 );

	/// <summary>Whether the cell holds pool water.</summary>
	public static bool IsWater( ParkCellMap cells, int x, int y ) => cells.InBounds( x, y ) && cells.RawTypeAt( x, y ) == ParkCellTypes.PoolWater;

	/// <summary>
	/// Whether the pool edge at a land cell is straight in the direction <paramref name="d"/>: the water cell beyond it, and the two beside that one along the edge, hold water (so the edge runs
	/// straight past the cell and no corner is here). With <paramref name="depth"/> 2 the next cell out and its two neighbours must hold water too (the diving board's landing).
	/// </summary>
	public static bool StraightEdge( ParkCellMap cells, (int X, int Y) land, (int X, int Y) d, int depth = 1 )
	{
		var (px, py) = (-d.Y, d.X);
		for ( var step = 1; step <= depth; step++ )
		{
			var (x, y) = (land.X + d.X * step, land.Y + d.Y * step);
			if ( !IsWater( cells, x, y ) || !IsWater( cells, x + px, y + py ) || !IsWater( cells, x - px, y - py ) )
				return false;
		}
		return true;
	}

	/// <summary>The directions (in <see cref="Directions"/> order, starting with <paramref name="first"/>) in which the cell has pool water next to it.</summary>
	public static IEnumerable<int> WaterSides( ParkCellMap cells, (int X, int Y) cell, int first )
	{
		for ( var offset = 0; offset < 4; offset++ )
		{
			var index = (first + offset) & 3;
			if ( IsWater( cells, cell.X + Directions[index].X, cell.Y + Directions[index].Y ) )
				yield return index;
		}
	}

	/// <summary>
	/// The placement rule: a ladder, a board and an underwater light need a straight pool edge in front of them (a board also two cells of water out), a lamp and a lounger need pool water beside a cell
	/// of the footprint, a shower needs a changing room beside it. Null (no opinion) for any other object and while the extension is off.
	/// </summary>
	public static OriginalPlacementResult? PlacementRule( ObjectCatalogEntry entry, ParkCellMap? cells, ParkObjects? objects, int anchorX, int anchorY, int rotation )
	{
		var kind = KindOf( entry );
		if ( kind == PoolExtraKind.None || cells == null )
			return null;
		var footprint = ObjectFootprint.GetCells( entry.Shape, anchorX, anchorY, rotation ).Select( cell => (cell.X, cell.Y) ).ToArray();
		var ok = kind switch
		{
			PoolExtraKind.Ladder or PoolExtraKind.UnderwaterLight => footprint.Length > 0 && StraightEdge( cells, footprint[0], WaterDirection( rotation ) ),
			PoolExtraKind.DivingBoard => footprint.Length > 0 && StraightEdge( cells, footprint[0], WaterDirection( rotation ), 2 ),
			PoolExtraKind.Shower => objects != null && footprint.Any( cell => Directions.Any( d => objects.FindAt( cell.X + d.X, cell.Y + d.Y ) is { IsDeleted: false } other && other.Visitors.IsChangingRoom ) ),
			_ => footprint.Any( cell => WaterSides( cells, cell, 0 ).Any() )
		};
		return ok ? null : OriginalPlacementResult.NeedsPoolEdge;
	}

	/// <summary>The placed extras of the park, in the order of the objects (the simulation reads them through <see cref="GuestSimulation.PoolExtraSites"/>).</summary>
	public static List<PoolExtraSite> Sites( ParkObjects objects )
	{
		var sites = new List<PoolExtraSite>();
		foreach ( var item in objects.Objects )
			if ( !item.IsDeleted && KindOf( item.Entry ) is var kind && kind != PoolExtraKind.None )
				sites.Add( new PoolExtraSite( item.ThingId, kind, item.Placement.Rotation, item.Cells.Select( cell => (cell.X, cell.Y) ).ToArray() ) );
		return sites;
	}

	/// <summary>
	/// Whether an extra needs the pool cell: the water beside the footprint of a ladder, board, lounger, lamp or underwater light (and the second cell out of a board) cannot be erased. A shower needs the
	/// changing room, which has its own guard, so it holds no water.
	/// </summary>
	public static bool NeedsWater( IEnumerable<PoolExtraSite> sites, int x, int y )
	{
		foreach ( var site in sites )
		{
			if ( site.Kind is PoolExtraKind.None or PoolExtraKind.Shower )
				continue;
			var board = site.Kind == PoolExtraKind.DivingBoard ? WaterDirection( site.Rotation ) : (0, 0);
			foreach ( var cell in site.Cells )
			{
				if ( Directions.Any( d => cell.X + d.X == x && cell.Y + d.Y == y ) || board != (0, 0) && (cell.X + board.Item1 * 2, cell.Y + board.Item2 * 2) == (x, y) )
					return true;
			}
		}
		return false;
	}
}
