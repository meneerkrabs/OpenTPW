namespace OpenTPW;

/// <summary>The attraction things the obstacle code places and destroys: a level's objects, or a test's.</summary>
public interface IThemeParkIncObstacleThings
{
	/// <summary>The object type (<c>Info.Id</c>) of the live attraction thing <paramref name="thing"/>; null for none.</summary>
	int? TypeOf( ushort thing );
	/// <summary>Places a new attraction of <paramref name="type"/> (<c>0x638120</c>, <c>0x5DB1B0</c>), without cells of its own.</summary>
	void Place( int type );
	/// <summary>Destroys the thing <paramref name="thing"/> (<c>0x637C40</c>).</summary>
	void Destroy( ushort thing );
}

/// <summary>What an obstacle's opening (<c>0x5E8BD0</c>) leaves to do with the obstacle thing once its cells are written.</summary>
public enum ThemeParkIncObstacleFate
{
	/// <summary>The thing stays (no <c>ReplaceWithID</c>, <c>DeleteAfterUse</c> not 1); nothing is refreshed.</summary>
	Stays,
	/// <summary><c>ReplaceWithID</c>'s type is placed, then the obstacle thing is destroyed.</summary>
	Replaced,
	/// <summary><c>DeleteAfterUse</c> 1: the obstacle thing is destroyed.</summary>
	Deleted
}

/// <summary>
/// The cells of Theme Park Inc's obstacles (docs/reverse/TPI-MISSIONS.md §6): the footprint the placement in a new world writes from <c>Info.Shape</c> (<c>0x647BF0</c>), the one the opening writes from
/// <c>Obstacle.AfterShape</c> (<c>0x5E8BD0</c>), and the prebuilt park's saved obstacle cells. A cell's type, owner word and obstacle flag (<see cref="ParkCellMap.ObstacleFlagAt"/>) are written as the
/// original's square writers do; the tile refresh <c>0x67FB80</c> between them is drawing only.
/// </summary>
public static class ThemeParkIncObstacles
{
	/// <summary>
	/// The obstacle thing's anchor square word: <c>(MapOffsetY + ShapeOffsetY) × 128 + 1 + MapOffsetX + ShapeOffsetX</c>, the cell the placement constructs the thing at and the owner word of its
	/// footprint.
	/// </summary>
	// [BIN:TPI-EXE:0x647CF5 obstacle anchor] A = (info+0x38 + info+0x124) * 128 + 1 + info+0x34 + info+0x120, passed to the attraction constructor 0x5DB1B0(A, type, 0, 0, 0); the Shape cells are written
	// without the map offset, so the Science bridge's anchor (map offset 1, 1) lies one cell off its footprint's corner
	public static ushort AnchorWord( ThemeParkIncObstacleType type ) =>
		unchecked( (ushort)((type.MapOffsetY + type.ShapeOffsetY) * 128 + 1 + type.MapOffsetX + type.ShapeOffsetX) );

	/// <summary>
	/// The placement's cell writes for one obstacle in a new world (<c>0x647BF0</c>): every <c>Info.Shape</c> cell other than <c>%</c> at <c>(ShapeOffsetX + column, ShapeOffsetY + row)</c> is set to
	/// type 0 and then its value; a non-zero value also gets the obstacle flag and the anchor's owner word (<see cref="AnchorWord"/>).
	/// </summary>
	// [BIN:TPI-EXE:0x647D60 obstacle shape] for every Shape cell (rows bottom first, 0x140 apart) whose value != 0x21: square S = (ShapeOffsetY + row) * 128 + 1 + ShapeOffsetX + column; 0x67AD00(0, 0, 0), 0x67FB80,
	// 0x67AD00(value, 0, 0), 0x67FB80; value != 0: S+0x24 = 1 (0x647E01) and 0x67CDC0(A)
	public static void Place( ParkCellMap cells, ThemeParkIncObstacleType type )
	{
		ArgumentNullException.ThrowIfNull( cells );
		ArgumentNullException.ThrowIfNull( type );
		if ( type.Shape is not { } shape )
			return;
		var anchor = AnchorWord( type );
		for ( var row = 0; row < shape.Height; row++ )
		{
			for ( var column = 0; column < shape.Width; column++ )
			{
				var value = shape[column, row].Value;
				if ( value == ThemeParkIncMapGrid.Untouched )
					continue;
				var (x, y) = (type.ShapeOffsetX + column, type.ShapeOffsetY + row);
				SetSquareType( cells, x, y, 0 );
				SetSquareType( cells, x, y, value );
				if ( value == 0 )
					continue;
				cells.SetObstacleFlag( x, y, true );
				cells.SetOwner( x, y, anchor );
			}
		}
	}

	/// <summary>
	/// The opening of an obstacle (<c>0x5E8BD0</c>) on the cells: without <c>GateTo</c>, every <c>Obstacle.AfterShape</c> cell loses the obstacle flag, and one other than <c>%</c> is set to type 0, then its
	/// value, its owner word cleared, and on the map's edge (column or row 0, or equal to the map's width or height) set to 5 again. Then the fate of the thing: <c>ReplaceWithID</c>, else
	/// <c>DeleteAfterUse</c>. A gate (<c>GateTo</c> set) keeps its cells.
	/// </summary>
	// [BIN:TPI-EXE:0x5E8BD0 obstacle opening] GateTo (info+0x58) == 0: for each AfterShape (+0x11C) cell, S = (ShapeOffsetY + row) * 128 + 1 + ShapeOffsetX + column; S+0x24 = 0 (0x5E8CB4) for every cell; a value != 0x21:
	// 0x67AD00(0), 0x67AD00(value), 0x67FB80, 0x67CDC0(0), then x == 0 || y == 0 || x == 0x64E8D0() || y == 0x64E8F0() (the map's width and height words 0xA025A4/0xA025A8): 0x67AD00(5); then ReplaceWithID (+0x134,
	// u16) != 0: the replacement's thing at its own ShapeOffset (0x5DB1B0, no footprint), refresh, destroy (0x637C40); else DeleteAfterUse (+0x130) == 1: refresh, destroy; else return
	public static ThemeParkIncObstacleFate Open( ParkCellMap cells, ThemeParkIncObstacleType type )
	{
		ArgumentNullException.ThrowIfNull( cells );
		ArgumentNullException.ThrowIfNull( type );
		if ( type.GateTo == 0 && type.AfterShape is { } after )
		{
			for ( var row = 0; row < after.Height; row++ )
			{
				for ( var column = 0; column < after.Width; column++ )
				{
					var (x, y) = (type.ShapeOffsetX + column, type.ShapeOffsetY + row);
					cells.SetObstacleFlag( x, y, false );
					var value = after[column, row].Value;
					if ( value == ThemeParkIncMapGrid.Untouched )
						continue;
					SetSquareType( cells, x, y, 0 );
					SetSquareType( cells, x, y, value );
					cells.SetOwner( x, y, 0 );
					if ( x == 0 || y == 0 || x == cells.Width || y == cells.Height )
						SetSquareType( cells, x, y, 5 );
				}
			}
		}
		if ( type.ReplaceWithId != 0 )
			return ThemeParkIncObstacleFate.Replaced;
		return type.DeleteAfterUse ? ThemeParkIncObstacleFate.Deleted : ThemeParkIncObstacleFate.Stays;
	}

	/// <summary>
	/// Opens the obstacle thing <paramref name="thing"/> (<c>0x5E8BD0</c>): its cells (<see cref="Open(ParkCellMap, ThemeParkIncObstacleType)"/>), then <c>ReplaceWithID</c>'s type placed and the obstacle
	/// destroyed, or the obstacle destroyed, or nothing. Answers the fate.
	/// </summary>
	// [BIN:TPI-EXE:0x5E8D80 obstacle replacement] ReplaceWithID: 0x43C700(id), a new attraction (0x638120(0x148)) constructed at the replacement's ShapeOffsetY * 128 + 1 + ShapeOffsetX (0x5DB1B0(cell, id, 0, 0)), no cell
	// written; then 0x4A5E30, the refresh 0x648C90 and 0x637C40 on the obstacle
	public static ThemeParkIncObstacleFate OpenThing( ParkCellMap cells, IReadOnlyDictionary<int, ThemeParkIncObstacleType> types, IThemeParkIncObstacleThings things, ushort thing )
	{
		ArgumentNullException.ThrowIfNull( types );
		ArgumentNullException.ThrowIfNull( things );
		if ( things.TypeOf( thing ) is not { } id || !types.TryGetValue( id, out var type ) )
			throw new InvalidOperationException( $"Thing {thing} is no Theme Park Inc obstacle of this level." );
		var fate = Open( cells, type );
		if ( fate == ThemeParkIncObstacleFate.Replaced )
			things.Place( type.ReplaceWithId );
		if ( fate != ThemeParkIncObstacleFate.Stays )
			things.Destroy( thing );
		return fate;
	}

	/// <summary>
	/// The obstacle cells of a Theme Park Inc park save (the prebuilt Science park): every saved cell with the obstacle flag takes its saved type, owner word and the flag. The level map gives the
	/// other cells; a save never carries an obstacle flag elsewhere.
	/// </summary>
	// [BIN:TPI-EXE:0x5D6D40 map loader] the park loader reads every map cell's type (+8) and owner word (+0x10) back, and the placement 0x647BF0 does not run for a loaded park (0x6A380A..0x6A3D0D clear its flag)
	public static void ImportSavedCells( ParkCellMap cells, SaveCellGrid saved )
	{
		ArgumentNullException.ThrowIfNull( cells );
		ArgumentNullException.ThrowIfNull( saved );
		for ( var y = 0; y < Math.Min( cells.Height, saved.CountY ); y++ )
		{
			for ( var x = 0; x < Math.Min( cells.Width, saved.CountX ); x++ )
			{
				var cell = saved[x, y];
				if ( cell.ThemeParkIncObstacleFlag == 0 )
					continue;
				cells.SetType( x, y, unchecked( (byte)cell.MapType ) );
				cells.SetOwner( x, y, cell.MapParentId );
				cells.SetObstacleFlag( x, y, true );
			}
		}
	}

	/// <summary>
	/// Theme Park Inc's square type writer as the obstacle code calls it (<c>0x67AD00(type, 0, 0)</c>, whose default case is the setter <c>0x67CB70</c>): the same non-zero type counts one more placement,
	/// type 0 (over any square, a path or an object's too) or a cell of type 0 takes the type, anything else is refused. Only the type word changes: the owner word, the links and the placement counter stay, unlike Theme Park World's setter.
	/// </summary>
	// [BIN:TPI-EXE:0x67CB70 square type] 0x67E240(type) refuses (types 1, 3, 4 and 0x15 have exceptions there, which no obstacle grid holds); type != 0 and == +8: +0x20++ and return; types 1 and 3 are priced; then
	// +8 = type, the map change counter and block stamp (0x5D81F0, 0x5D8210); no other square word is written
	// [BIN:TPI-EXE:0x67E240 square type check] type 0 passes whatever the square holds (0x67E244); another type that is none of 1, 3, 4, 0x15 passes only over its own type or 0 (0x67E274..0x67E27D), whatever the
	// square holds: the exceptions belong to the written type alone
	internal static void SetSquareType( ParkCellMap cells, int x, int y, int type )
	{
		if ( type is 1 or 3 or 4 or 0x15 )
			throw new NotSupportedException( $"Theme Park Inc's square writer has rules for paths, queues and objects (0x67E240) that the obstacle port does not carry; type {type} at cell ({x}, {y}) would need them." );
		var current = cells.RawTypeAt( x, y );
		if ( type != 0 && current == type )
		{
			cells.SetPlacementCount( x, y, unchecked( (short)(cells.PlacementCountAt( x, y ) + 1) ) );
			return;
		}
		if ( type != 0 && current != 0 )
			return;
		var (owner, links, placements) = (cells.OwnerAt( x, y ), cells.LinksAt( x, y ), cells.PlacementCountAt( x, y ));
		cells.SetType( x, y, unchecked( (byte)type ) );
		// Theme Park World's setter clears these with type 0; Theme Park Inc's leaves them
		if ( cells.OwnerAt( x, y ) != owner )
			cells.SetOwner( x, y, owner );
		if ( cells.LinksAt( x, y ) != links )
			cells.SetLinks( x, y, links );
		if ( cells.PlacementCountAt( x, y ) != placements )
			cells.SetPlacementCount( x, y, placements );
	}
}
