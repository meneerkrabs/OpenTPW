namespace OpenTPW;

/// <summary>Why a slide, or a piece of one, is refused (<see cref="PoolSlideRules"/>); <see cref="Ok"/> is none.</summary>
public enum PoolSlideResult
{
	Ok,
	/// <summary>The extension is off.</summary>
	ExtensionOff,
	/// <summary>The tower is not there (not placed, or not a slide tower).</summary>
	NoTower,
	/// <summary>The tower has no pool beside its front.</summary>
	NoPoolSide,
	/// <summary>The slide has no pieces.</summary>
	Empty,
	/// <summary>The last piece is not a splash piece, or a splash piece is not the last.</summary>
	NoSplash,
	/// <summary>More than <see cref="PoolSlide.MaximumPieces"/> pieces.</summary>
	TooLong,
	/// <summary>The first piece is not beside the tower, or the track does not leave the tower outwards.</summary>
	BadStart,
	/// <summary>A cell outside the map, or land the park does not own.</summary>
	OutsideLand,
	/// <summary>Two pieces in one cell, or a piece on the tower.</summary>
	Overlap,
	/// <summary>A piece in a cell that is not empty ground or pool water (a path, an object, a track).</summary>
	Occupied,
	/// <summary>The splash piece is not over pool water, or it is not the pool the tower faces.</summary>
	NotOverPool,
	/// <summary>The slope is steeper than <see cref="PoolSlideRules.MaximumSlope"/>: the slide is too short for its drop.</summary>
	TooSteep,
	/// <summary>The slope is gentler than <see cref="PoolSlideRules.MinimumSlope"/>: the slide is too long for its drop.</summary>
	TooGentle,
	/// <summary>The track is too close to the ground or the water below it.</summary>
	TooLow,
	/// <summary>The recorded heights are not those of the tower and the pool.</summary>
	BadHeights,
	/// <summary>The cell is not the next one of the track (a piece is added beside the end only).</summary>
	NotNext,
	/// <summary>The park cannot pay for the piece.</summary>
	NotEnoughMoney
}

/// <summary>
/// What the slide rules ask of the park, as plain functions so the rules run on a fake park in tests: the cell map's types, the land's ownership, the ground, the water level of a pool cell and the
/// cells the host reserves (an object other than the tower, a prototype).
/// </summary>
public sealed class PoolSlideSite
{
	public required ParkCellMap Cells { get; init; }
	/// <summary>The ground height at a point in cell units, engine units.</summary>
	public required Func<float, float, float> Ground { get; init; }
	/// <summary>The water surface of a pool cell, engine units; null when the cell holds no water.</summary>
	public required Func<int, int, float?> WaterLevel { get; init; }
	/// <summary>Whether the host keeps the cell for something else (a placed object, the prototype ride). The slide's own tower is not asked.</summary>
	public Func<int, int, bool> IsReserved { get; init; } = static ( _, _ ) => false;
}

/// <summary>
/// The rules of water slides (docs/POOLS.md, "Water slides"; docs/TRACKS.md, "Slides"): an open track from a tower beside a pool to a splash piece over that pool. Every other track type keeps
/// its closed-circuit rule (<see cref="TrackCircuitWalker"/>); a slide never goes through it.
/// </summary>
// [EXT:pools-slides] OpenTPW's own rules; the original game has no pools
public static class PoolSlideRules
{
	/// <summary>The steepest slope, drop over run (both in engine units): 0.8, about 39 degrees.</summary>
	public const int MaximumSlope = 800;
	/// <summary>The gentlest slope in thousandths: 0.2, about 11 degrees; below it a rider would barely move.</summary>
	public const int MinimumSlope = 200;
	/// <summary>The least height of a piece's lowest point above the ground, or the water, under its cell, engine units: the track never cuts the ground (it comes down to the pool's edge at its end).</summary>
	public const float Clearance = 0.15f;
	/// <summary>How far above the water the track ends, engine units: the riders drop into the pool from there.</summary>
	public const float SplashHeight = 0.2f;
	/// <summary>The most the splash end may stand above the water, engine units (the pool's level may drop under a built slide).</summary>
	public const float MaximumSplashHeight = 1.5f;

	/// <summary>The track's height at the tower: <see cref="PoolSlideTowers.TopHeight"/> above the highest ground at the footprint's corners, thousandths of an engine unit.</summary>
	public static int TopHeightOf( PoolSlideTower tower, PoolSlideSite site )
	{
		var highest = float.NegativeInfinity;
		foreach (var (x, y) in tower.Footprint)
			foreach ( var (dx, dy) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) } )
				highest = MathF.Max( highest, site.Ground( x + dx, y + dy ) );
		return (int)MathF.Round( (highest + PoolSlideTowers.TopHeight) * PoolSlide.HeightUnit );
	}

	/// <summary>The track's height at a splash cell: <see cref="SplashHeight"/> above the water, thousandths of an engine unit; null when the cell holds no water.</summary>
	public static int? EndHeightOf( int x, int y, PoolSlideSite site ) =>
		site.WaterLevel( x, y ) is { } level ? (int)MathF.Round( (level + SplashHeight) * PoolSlide.HeightUnit ) : null;

	/// <summary>
	/// Whether the slide is valid for the tower and the site. Order: extension, tower, pieces, start, cells, the splash, the heights, the slope, the clearance. A complete slide is also the pool the
	/// tower faces: its splash cell must be in the water connected to the tower's pool-side cell.
	/// </summary>
	public static PoolSlideResult Validate( PoolSlide slide, PoolSlideTower? tower, PoolSlideSite site, bool requireComplete = true )
	{
		if ( !FeatureGates.Pools )
			return PoolSlideResult.ExtensionOff;
		if ( tower == null || tower.X != slide.TowerX || tower.Y != slide.TowerY )
			return PoolSlideResult.NoTower;
		if ( tower.PoolSideCell( site.Cells ) is not { } poolSide )
			return PoolSlideResult.NoPoolSide;
		var result = ValidateTrack( slide, tower, site );
		if ( result != PoolSlideResult.Ok )
			return result;
		if ( !slide.IsComplete )
			return requireComplete ? PoolSlideResult.NoSplash : PoolSlideResult.Ok;
		var landing = slide.LandingCell!.Value;
		if ( site.Cells.RawTypeAt( landing.X, landing.Y ) != ParkCellTypes.PoolWater )
			return PoolSlideResult.NotOverPool;
		var region = PoolSwimRegion.Build( site.Cells, poolSide );
		if ( region == null || !region.Contains( landing.X, landing.Y ) )
			return PoolSlideResult.NotOverPool;
		if ( EndHeightOf( landing.X, landing.Y, site ) is not { } expectedEnd || slide.EndHeight < expectedEnd - (int)(SplashHeight * PoolSlide.HeightUnit) || slide.EndHeight > expectedEnd + (int)(MaximumSplashHeight * PoolSlide.HeightUnit) )
			return PoolSlideResult.BadHeights;
		if ( slide.TopHeight != TopHeightOf( tower, site ) )
			return PoolSlideResult.BadHeights;
		var slope = slide.SlopePermille;
		if ( slope > MaximumSlope )
			return PoolSlideResult.TooSteep;
		if ( slope < MinimumSlope )
			return PoolSlideResult.TooGentle;
		return Clear( slide, site );
	}

	/// <summary>The checks that hold for a track under construction as well: pieces, start, cells.</summary>
	public static PoolSlideResult ValidateTrack( PoolSlide slide, PoolSlideTower tower, PoolSlideSite site )
	{
		var pieces = slide.Pieces;
		if ( pieces.Count == 0 )
			return PoolSlideResult.Empty;
		if ( pieces.Count > PoolSlide.MaximumPieces )
			return PoolSlideResult.TooLong;
		for ( var index = 0; index < pieces.Count - 1; index++ )
			if ( pieces[index] == PoolSlidePiece.Splash )
				return PoolSlideResult.NoSplash;
		// the first piece lies beside the tower, outside the pool-side face, and the track leaves the footprint
		var (dx, dy) = PoolSlide.Direction( slide.StartHeading );
		if ( !tower.Footprint.Contains( (slide.StartX - dx, slide.StartY - dy) ) || tower.Footprint.Contains( (slide.StartX, slide.StartY) ) || tower.PoolSide.Contains( (slide.StartX, slide.StartY) ) )
			return PoolSlideResult.BadStart;
		var seen = new HashSet<(int X, int Y)>();
		foreach ( var cell in slide.Cells )
		{
			if ( !site.Cells.InBounds( cell.X, cell.Y ) || site.Cells.FlagsAt( cell.X, cell.Y ).HasFlag( ParkCellFlags.Unowned ) )
				return PoolSlideResult.OutsideLand;
			if ( !seen.Add( cell ) || tower.Footprint.Contains( cell ) )
				return PoolSlideResult.Overlap;
			var type = site.Cells.RawTypeAt( cell.X, cell.Y );
			if ( (type != 0 && type != ParkCellTypes.PoolWater) || site.IsReserved( cell.X, cell.Y ) )
				return PoolSlideResult.Occupied;
		}
		return PoolSlideResult.Ok;
	}

	/// <summary>Whether the track stays <see cref="Clearance"/> above the ground, and the water, below it.</summary>
	private static PoolSlideResult Clear( PoolSlide slide, PoolSlideSite site )
	{
		for ( var index = 0; index < slide.Pieces.Count - 1; index++ )
		{
			var (x, y) = slide.Cells[index];
			var start = slide.StartOf( index );
			// the lowest point of a piece is its exit
			var height = slide.HeightAt( start + PoolSlide.LengthOf( slide.Pieces[index] ) ) / (float)PoolSlide.HeightUnit;
			var below = site.WaterLevel( x, y ) ?? HighestGround( x, y, site );
			if ( height < below + Clearance )
				return PoolSlideResult.TooLow;
		}
		return PoolSlideResult.Ok;
	}

	private static float HighestGround( int x, int y, PoolSlideSite site )
	{
		var highest = float.NegativeInfinity;
		foreach ( var (dx, dy) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) } )
			highest = MathF.Max( highest, site.Ground( x + dx, y + dy ) );
		return highest;
	}
}
