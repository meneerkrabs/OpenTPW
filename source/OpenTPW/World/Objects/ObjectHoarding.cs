using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace OpenTPW;

/// <summary>
/// The rows of an object's <c>Info.Shape</c> or <c>Info.Hoarding</c> block as the original map parser reads them: every non-space character is one cell,
/// looked up in the block's symbol table, a space takes no cell (the next character goes into the same column), and the rows are stored with the last text
/// row first. A grid holds at most 20 × 20 cells.
/// </summary>
// [BIN:TPW-EXE:0x00402720 map parser] each row is read to its end; a space is skipped without taking a column (the column counter advances only for a stored character), any other character is looked up in the table passed (12-byte rows: character, type dword, direction byte) and stored as (type, direction) at row × 20 + column, failing with "Illegal map character" when the table lacks it, "Map too wide" at a 21st column and "Map too high" at a 21st row with a character; the width is the longest row's column count, the height the row count, and the closing --- swaps row i with row height − 1 − i (0x00402858), so row 0 is the last text row
internal sealed class ObjectMapGrid
{
	/// <summary>The row stride of the original grid (and its widest row).</summary>
	public const int Stride = 20;
	/// <summary>The most rows a grid holds.</summary>
	public const int MaximumHeight = 20;

	private readonly int[] types = new int[Stride * MaximumHeight];
	private readonly byte[] directions = new byte[Stride * MaximumHeight];

	private ObjectMapGrid( int width, int height )
	{
		Width = width;
		Height = height;
	}

	public int Width { get; }
	public int Height { get; }

	/// <summary>The type dword of cell (<paramref name="column"/>, <paramref name="row"/>), 0 outside the stored cells.</summary>
	public int TypeAt( int column, int row ) => InGrid( column, row ) ? types[row * Stride + column] : 0;

	/// <summary>The direction byte of cell (<paramref name="column"/>, <paramref name="row"/>), 0 outside the stored cells.</summary>
	public byte DirectionAt( int column, int row ) => InGrid( column, row ) ? directions[row * Stride + column] : (byte)0;

	private static bool InGrid( int column, int row ) => column >= 0 && column < Stride && row >= 0 && row < MaximumHeight;

	/// <summary>Parses block rows (top text row first) with <paramref name="lookup"/>; null when the original parser fails.</summary>
	public static ObjectMapGrid? Parse( IReadOnlyList<string> rows, Func<char, (int Type, byte Direction)?> lookup )
	{
		var types = new int[Stride * MaximumHeight];
		var directions = new byte[Stride * MaximumHeight];
		var width = 0;
		for ( var row = 0; row < rows.Count; row++ )
		{
			var column = 0;
			foreach ( var symbol in rows[row] )
			{
				if ( symbol == ' ' )
					continue;
				if ( row >= MaximumHeight || lookup( symbol ) is not { } cell || column == Stride )
					return null;
				types[row * Stride + column] = cell.Type;
				directions[row * Stride + column] = cell.Direction;
				column++;
			}
			width = Math.Max( width, column );
		}
		var height = rows.Count;
		var grid = new ObjectMapGrid( width, height );
		for ( var row = 0; row < height && row < MaximumHeight; row++ )
		{
			var source = height - 1 - row;
			if ( source >= MaximumHeight )
				continue;
			Array.Copy( types, source * Stride, grid.types, row * Stride, Stride );
			Array.Copy( directions, source * Stride, grid.directions, row * Stride, Stride );
		}
		return grid;
	}

	/// <summary>The object's <c>Info.Shape</c> grid as the type loader parses it (the 19-row shape table), or null without one.</summary>
	public static ObjectMapGrid? ShapeOf( ObjectSettings settings ) =>
		settings.Blocks.TryGetValue( "Info.Shape", out var rows )
			? Parse( rows, symbol => ObjectShapeSymbols.TryGet( symbol, out var cell ) ? (cell.CellType, cell.Direction) : null )
			: null;

	/// <summary>The object's <c>Info.Hoarding</c> grid (the 16-row hoarding table), or null without one.</summary>
	public static ObjectMapGrid? HoardingOf( ObjectSettings settings ) =>
		settings.Blocks.TryGetValue( "Info.Hoarding", out var rows ) ? Parse( rows, ObjectHoardingSymbols.Lookup ) : null;
}

/// <summary>The symbol table of <c>Info.Hoarding</c>: the fence sides of a cell (0x01 the +V side, 0x04 +U, 0x10 −V, 0x40 −U).</summary>
public static class ObjectHoardingSymbols
{
	public const byte North = 0x01, East = 0x04, South = 0x10, West = 0x40;

	// [BIN:TPW-EXE:0x007397B0 hoarding symbol table] 16 rows of 12 bytes passed to the map parser for the key table's map index 2 (Info.Hoarding, 0x00744C20; 0x00401A3A): every type dword is 0 and the direction bytes are '.' 0, '^' 0x01, '_' 0x10, '[' 0x40, ']' 0x04, 'J' 0x14, 'F' 0x41, '7' 0x05, 'L' 0x50, '=' 0x11, 'H' 0x44, 'C' 0x51, 'U' 0x54, 'n' 0x45, '3' 0x15, 'O' 0x55
	private static readonly (char Symbol, byte Sides)[] Rows =
	{
		('.', 0), ('^', 0x01), ('_', 0x10), ('[', 0x40), (']', 0x04), ('J', 0x14), ('F', 0x41), ('7', 0x05),
		('L', 0x50), ('=', 0x11), ('H', 0x44), ('C', 0x51), ('U', 0x54), ('n', 0x45), ('3', 0x15), ('O', 0x55)
	};

	/// <summary>The (type, sides) a hoarding symbol stores, or null for a character the table lacks.</summary>
	public static (int Type, byte Direction)? Lookup( char symbol )
	{
		foreach ( var row in Rows )
			if ( row.Symbol == symbol )
				return (0, row.Sides);
		return null;
	}
}

/// <summary>The terrain grid the type loader measures a model's ground vertices against: the heightfield header's origin, cell sizes and cell counts.</summary>
public readonly record struct HoardingTerrain( float OriginX, float OriginZ, float CellSizeX, float CellSizeZ, int CountX, int CountZ )
{
	/// <summary>The grid of a level's heightfield: header +8 and +0xC are the origin (the second and third opaque words), +0x10/+0x14 the cell sizes, +0x18/+0x1C the counts.</summary>
	public static HoardingTerrain Of( ModelHeightfield field ) => new(
		field.OpaqueHeaderWords.Count > 2 ? BitConverter.UInt32BitsToSingle( field.OpaqueHeaderWords[2] ) : 0f,
		field.OpaqueHeaderWords.Count > 3 ? BitConverter.UInt32BitsToSingle( field.OpaqueHeaderWords[3] ) : 0f,
		field.CellSizeX, field.CellSizeZ, field.CellCountX, field.CellCountZ );
}

/// <summary>One board of an object's hoarding: four corners in model units (MD2 axes, Y up; 0 and 1 on top at Y 10, 2 and 3 at the bottom at Y 0), its outward normal and the cell corners its two ends stand on.</summary>
public sealed record ObjectHoardingStrip( NVector3[] Corners, NVector3 Normal, (int Column, int Row) EndA, (int Column, int Row) EndB );

/// <summary>
/// The hoarding of an object type: the boards the original raises around a closed, broken down, condemned or upgrading ride (docs/OBJECTS.md, "Hoarding").
/// Built once per type from the <c>Info.Hoarding</c> sides and the model's ground node, in the strip order the rise sweeps through.
/// </summary>
public sealed class ObjectHoardingGeometry
{
	/// <summary>A board's full height and the Y of its top before the terrain conform (model units).</summary>
	public const float BoardHeight = 10f;

	/// <summary>The texture names of the variants, in the order of the loaded table (variant 0 to 3).</summary>
	// [BIN:TPW-EXE:0x0074CFC8 hoarding textures] the texture descriptor 0x0074D008 (count 4, entries at 0x0074CFC8, 16 bytes each) names Closed.tga, Hoarding.tga, Condemn.tga and Upgrade.tga in that order; the model call 0x00454550 picks the loaded entry +0, +8, +0x10 or +0x18 (8 bytes per texture)
	public static readonly IReadOnlyList<string> TextureNames = new[] { "Closed", "Hoarding", "Condemn", "Upgrade" };

	/// <summary>The textures' directory of a level: <c>data\levels\%s\MiscMesh\textures</c> (the full set; the low-detail <c>stexture</c> set is not read).</summary>
	// [BIN:TPW-EXE:0x0045334D hoarding texture directory] the builder prints the format 0x0074D05C, "data\levels\%s\MiscMesh\textures", with the current level's name (0x00786B68 + 8) into the directory 0x0074D010 that the model builder 0x00424EF0 hands to the texture loader 0x0046E4E0
	public static string TextureDirectory( string level ) => $"/levels/{level}/miscmesh/textures";

	private ObjectHoardingGeometry( IReadOnlyList<ObjectHoardingStrip> strips, int width, int height )
	{
		Strips = strips;
		Width = width;
		Height = height;
	}

	/// <summary>The boards in the mesh's order (sorted by the angle of their middle around the grid's centre).</summary>
	public IReadOnlyList<ObjectHoardingStrip> Strips { get; }
	/// <summary>The hoarding grid's width and height in cells (the terrain conform's divisors).</summary>
	public int Width { get; }
	public int Height { get; }

	/// <summary>
	/// The hoarding of <paramref name="entry"/> over a level whose terrain grid is <paramref name="terrain"/>, or null when the type has none: a fixed item
	/// (<c>Info.DontApplyOffset</c>), a type without a parsable <c>Info.Hoarding</c>, a park without terrain, or a model with fewer than two ground vertices.
	/// </summary>
	// [BIN:TPW-EXE:0x00413C10 object loader] a type whose Info.DontApplyOffset (type record +0x38) is 0 is built by 0x004629D0 with the flags 0xB24A9 (0x4B24A9 with DoHeadProcessing), which include 1 (the ground node search 0x0046A5A0) and 8 (the hoarding builder 0x00452E70 with the Info.Hoarding grid, type +0x1C, 0x00462DAC); a fixed item gets 0x50C00, without either
	// [BIN:TPW-EXE:0x00452E70 hoarding builder] it returns without a hoarding unless the model instance's ground vertex count (+0xC, written by 0x0046A5A0) is at least 2 (0x00452E89) and its ground node (+0x2C) is set (0x004533DC)
	public static ObjectHoardingGeometry? Build( ObjectSettings settings, ModelFile model, HoardingTerrain? terrain )
	{
		if ( settings.GetBool( "Info.DontApplyOffset" ) || terrain is not { } grid || ObjectMapGrid.HoardingOf( settings ) is not { } hoarding )
			return null;
		var (count, node) = SelectGroundNode( model, ObjectMapGrid.ShapeOf( settings ), grid );
		if ( count < 2 || node < 0 )
			return null;
		return BuildStrips( hoarding, GroundVertices( model, node ) );
	}

	/// <summary>
	/// The type loader's ground node search (tp.exe <c>0x0046A5A0</c> → <c>0x00469A80</c> in its counting mode): the number of ground vertices over the whole
	/// model and the node with the most of them (the first such node in the walk), or -1.
	/// </summary>
	// [BIN:TPW-EXE:0x0046A5A0 footprint edge mask] unless the model header has flag 0x80, each Info.Shape cell (its type dword & 0xF nonzero is occupied) gets bit 1 when it and the cell at row − 1 differ in occupancy, 2 for column − 1, 4 for row + 1, 8 for column + 1 (a neighbour outside the grid is unoccupied); then 0x00469A80 walks the hierarchy from the root (header +0x78) with the identity matrix, the terrain heightfield and the counting mode (its fifth word 0), and its last ground node and count land in the instance's +0x2C and +0xC
	// [BIN:TPW-EXE:0x00469A80 ground vertices] each node's matrix is its stored one times its parent's (0x004702D0); a node with flag 0x200 is skipped (its children are not), one with flag 0x20000 counts nothing; every vertex is transformed (0x0046FC00) and, per axis, t = (coordinate − origin) / cell size (raised to 0), split into its whole part and fraction (0x0067B280): a fraction below 0.5 names the cell's low edge (bits 1 for X, 8 for Z), else its high edge (4, 2); a whole part outside 0 ≤ i < count + 1 (i + 1 for a high edge) ends the node and skips its children (0x0046A2A0); a cell past the shape's width (height) is the last one with a low edge turned high; an axis lies on a grid line when the fraction is within 0.01 of 0 or 1, and only a vertex on lines in both axes, within 0.01 of Y 0, whose edge is a boundary of its cell (low X with bit 2, high Z with 4, high X with 8, low Z with 1; any vertex with header flag 0x100) is counted; the node whose count first passes the best so far becomes the ground node (0x0046A0F1)
	internal static (int Count, int Node) SelectGroundNode( ModelFile model, ObjectMapGrid? shape, HoardingTerrain terrain )
	{
		var mask = new byte[ObjectMapGrid.Stride * ObjectMapGrid.MaximumHeight];
		int width = 0, height = 0;
		if ( (model.HeaderFlags & 0x80) == 0 && shape != null )
		{
			width = shape.Width;
			height = shape.Height;
			bool Occupied( int column, int row ) => (shape.TypeAt( column, row ) & 0xF) != 0;
			for ( var row = 0; row < height && row < ObjectMapGrid.MaximumHeight; row++ )
			{
				for ( var column = 0; column < width; column++ )
				{
					var self = Occupied( column, row );
					byte bits = 0;
					if ( self != (row < height - 1 && Occupied( column, row + 1 )) )
						bits |= 4;
					if ( self != (column >= 1 && Occupied( column - 1, row )) )
						bits |= 2;
					if ( self != (row >= 1 && Occupied( column, row - 1 )) )
						bits |= 1;
					if ( self != (column < width - 1 && Occupied( column + 1, row )) )
						bits |= 8;
					mask[row * ObjectMapGrid.Stride + column] = bits;
				}
			}
		}
		var selectAll = ((model.HeaderFlags >> 8) & 1) != 0;
		var total = 0;
		var best = 0;
		var bestNode = -1;
		byte Mask( int column, int row ) => column >= 0 && column < ObjectMapGrid.Stride && row >= 0 && row < ObjectMapGrid.MaximumHeight ? mask[row * ObjectMapGrid.Stride + column] : (byte)0;

		void Visit( int nodeIndex, Matrix4x4 parent )
		{
			var node = model.Nodes[nodeIndex];
			var matrix = Compose( node.Transform, parent );
			var counts = (node.Type & 0x20000) == 0;
			if ( (node.Type & 0x200) == 0 && node.MeshIndex >= 0 && node.MeshIndex < model.Meshes.Count )
			{
				var own = 0;
				foreach ( var stored in model.Meshes[node.MeshIndex].Positions )
				{
					var point = Transform( new NVector3( stored.X, stored.Y, stored.Z ), matrix );
					var edges = 0;
					int cellX = 0, cellZ = 0;
					bool onX = false, onZ = false;
					var lines = 1;
					for ( var axis = 0; axis < 2; axis++ )
					{
						var coordinate = axis == 0 ? point.X : point.Z;
						var t = (float)(((double)coordinate - (axis == 0 ? terrain.OriginX : terrain.OriginZ)) / (axis == 0 ? terrain.CellSizeX : terrain.CellSizeZ));
						if ( t < 0f )
							t = 0f;
						var whole = MathF.Truncate( t );
						var fraction = t - whole;
						var limit = (axis == 0 ? terrain.CountX : terrain.CountZ) + 1;
						if ( counts )
						{
							if ( fraction < 0.5f )
							{
								edges |= axis == 0 ? 1 : 8;
								if ( whole < 0f || limit <= whole )
									return;
							}
							else
							{
								edges |= axis == 0 ? 4 : 2;
								if ( whole + 1f < 0f || !(whole + 1f < limit) )
									return;
							}
							if ( axis == 0 )
								cellX = (int)whole;
							else
								cellZ = (int)whole;
							if ( axis == 0 && (uint)cellX >= (uint)width )
							{
								if ( (edges & 1) != 0 )
									edges = (edges & ~1) | 4;
								cellX = width - 1;
							}
							else if ( axis == 1 && (uint)cellZ >= (uint)height )
							{
								if ( (edges & 8) != 0 )
									edges = (edges & ~8) | 2;
								cellZ = height - 1;
							}
						}
						if ( MathF.Abs( fraction ) < 0.01f || Math.Abs( (double)fraction - 1.0 ) < 0.01f )
						{
							if ( axis == 0 )
								onX = true;
							else
								onZ = true;
						}
						else if ( lines != 0 )
							lines--;
					}
					if ( counts && lines != 0 && MathF.Abs( point.Y ) < 0.01f )
					{
						var bits = Mask( cellX, cellZ );
						var ground = selectAll
							|| (onX && (edges & 1) != 0 && (bits & 2) != 0)
							|| (onZ && (edges & 2) != 0 && (bits & 4) != 0)
							|| (onX && (edges & 4) != 0 && (bits & 8) != 0)
							|| (onZ && (edges & 8) != 0 && (bits & 1) != 0);
						if ( ground )
						{
							own++;
							total++;
						}
					}
					if ( own > best )
					{
						best = own;
						bestNode = nodeIndex;
					}
				}
			}
			for ( var child = node.FirstChildIndex; child >= 0; child = model.Nodes[child].NextSiblingIndex )
				Visit( child, matrix );
		}

		if ( model.RootNodeIndex >= 0 )
			Visit( model.RootNodeIndex, Matrix4x4.Identity );
		return (total, bestNode);
	}

	/// <summary>The positions of node <paramref name="node"/>'s mesh transformed by the node's own stored matrix (the builder's view of the ground node).</summary>
	// [BIN:TPW-EXE:0x004533E8 ground node vertices] the builder transforms every vertex of the ground node (count +0x58, positions +0x60) by that node's matrix +0x10..+0x4C alone, not by its parents'
	private static NVector3[] GroundVertices( ModelFile model, int node )
	{
		var mesh = model.Nodes[node].MeshIndex;
		if ( mesh < 0 || mesh >= model.Meshes.Count )
			return Array.Empty<NVector3>();
		var matrix = model.Nodes[node].Transform;
		return model.Meshes[mesh].Positions.Select( stored => Transform( new NVector3( stored.X, stored.Y, stored.Z ), matrix ) ).ToArray();
	}

	/// <summary>
	/// The boards of a hoarding grid (tp.exe <c>0x00452E70</c>): one per side bit of each cell, at the cell's edge in model units (a cell is 10 units),
	/// its ends drawn in towards the ground node's vertices, ordered by the angle of their middles round the grid's centre.
	/// </summary>
	// [BIN:TPW-EXE:0x00452F1C hoarding board order] every set side bit (1, 4, 0x10, 0x40 in that order, cells row by row) gets the key fmod(atan2(dy, dx) + 5π/4, 2π) (0x0067B24A) of its middle (column + 0.5, row + 1), (column + 1, row + 0.5), (column + 0.5, row) or (column, row + 0.5) less half the grid's width and height; the keys' pointers are merge-sorted ascending and stably by 0x00470630, and a board's rank is its strip in the mesh
	// [BIN:TPW-EXE:0x004535A0 hoarding corner pull] for a grid larger than 1 × 1 each cell corner (column·10, row·10 + 10), (+10, +10), (+10, row·10), (column·10, row·10) takes half the X/Z distance to the second nearest ground vertex (strict comparisons of the X/Z distance, the first held in extended precision, the second as a float), else no pull; a 1 × 1 grid takes the fixed pulls (0.3, −0.3), (−0.3, −0.3), (0.3, 0.3), (0.3, 0.3)
	// [BIN:TPW-EXE:0x0045372E hoarding boards] side 1: corners 0/2 at (column·10 + 10 + pull1.x if side 4, z1 + pull0.z), 1/3 at (column·10 + pull0.x if side 0x40, z1 + pull0.z), ends (column + 1, row + 1) and (column, row + 1), normal +Z; side 4: X column·10 + 10 + pull1.x, corners 0/2 at Z row·10 + pull2.z if side 0x10, 1/3 at z1 + pull1.z if side 1, ends (column + 1, row) and (column + 1, row + 1), normal +X; side 0x10: Z row·10 + pull3.z, corners 0/2 at X column·10 + pull3.x if side 0x40, 1/3 at column·10 + 10 + pull2.x if side 4, ends (column, row) and (column + 1, row), normal −Z; side 0x40: X column·10 + pull0.x, corners 0/2 at z1 + pull0.z if side 1, 1/3 at row·10 + pull3.z if side 0x10, ends (column, row + 1) and (column, row), normal −X; corners 0 and 1 are at Y 10, 2 and 3 at Y 0, and each board is drawn from both sides
	internal static ObjectHoardingGeometry BuildStrips( ObjectMapGrid grid, IReadOnlyList<NVector3> ground )
	{
		var halfWidth = grid.Width * 0.5;
		var halfHeight = grid.Height * 0.5;
		var keys = new List<(float Key, int Column, int Row, byte Side)>();
		for ( var row = 0; row < grid.Height; row++ )
		{
			for ( var column = 0; column < grid.Width; column++ )
			{
				var sides = grid.DirectionAt( column, row );
				foreach ( var (side, dx, dy) in new[] { ((byte)0x01, 0.5, 1.0), ((byte)0x04, 1.0, 0.5), ((byte)0x10, 0.5, 0.0), ((byte)0x40, 0.0, 0.5) } )
				{
					if ( (sides & side) == 0 )
						continue;
					// DetMath, not the platform's atan2: the board order is then the same on every runtime (the browser included)
					var angle = DetMath.Atan2( row + dy - halfHeight, column + dx - halfWidth );
					keys.Add( ((float)Fmod( angle + 3.9269909858703613, 6.2831854820251465 ), column, row, side) );
				}
			}
		}
		// List.Sort is not stable: a stable merge sort keeps equal keys in their cell order, as 0x00470630 does
		var order = keys.Select( ( key, index ) => (key, index) ).OrderBy( item => item.key.Key ).ThenBy( item => item.index ).Select( item => item.key ).ToArray();
		var strips = new ObjectHoardingStrip[order.Length];
		var single = grid.Width == 1 && grid.Height == 1;
		for ( var rank = 0; rank < order.Length; rank++ )
		{
			var (_, column, row, side) = order[rank];
			var sides = grid.DirectionAt( column, row );
			var pulls = single ? SinglePulls : CornerPulls( column, row, ground );
			float x0 = column * 10f, x1 = column * 10f + 10f, z0 = row * 10f;
			NVector2 Pull( int corner ) => pulls[corner];
			float If( byte bit, float value ) => (sides & bit) != 0 ? value : 0f;
			NVector3 A, B;
			NVector3 normal;
			(int, int) endA, endB;
			switch ( side )
			{
				case 0x01:
					A = new( F( x1 + (double)If( 0x04, Pull( 1 ).X ) ), 0, F( z0 + (double)Pull( 0 ).Y + 10 ) );
					B = new( F( x0 + (double)If( 0x40, Pull( 0 ).X ) ), 0, A.Z );
					normal = NVector3.UnitZ;
					(endA, endB) = ((column + 1, row + 1), (column, row + 1));
					break;
				case 0x04:
					A = new( F( x0 + (double)Pull( 1 ).X + 10 ), 0, F( z0 + (double)If( 0x10, Pull( 2 ).Y ) ) );
					B = new( A.X, 0, F( z0 + (double)If( 0x01, Pull( 1 ).Y ) + 10 ) );
					normal = NVector3.UnitX;
					(endA, endB) = ((column + 1, row), (column + 1, row + 1));
					break;
				case 0x10:
					A = new( F( x0 + (double)If( 0x40, Pull( 3 ).X ) ), 0, F( z0 + (double)Pull( 3 ).Y ) );
					B = new( F( x0 + (double)If( 0x04, Pull( 2 ).X ) + 10 ), 0, A.Z );
					normal = -NVector3.UnitZ;
					(endA, endB) = ((column, row), (column + 1, row));
					break;
				default:
					A = new( F( x0 + (double)Pull( 0 ).X ), 0, F( z0 + (double)If( 0x01, Pull( 0 ).Y ) + 10 ) );
					B = new( A.X, 0, F( z0 + (double)If( 0x10, Pull( 3 ).Y ) ) );
					normal = -NVector3.UnitX;
					(endA, endB) = ((column, row + 1), (column, row));
					break;
			}
			strips[rank] = new ObjectHoardingStrip(
				new[] { A with { Y = BoardHeight }, B with { Y = BoardHeight }, A, B }, normal, endA, endB );
		}
		return new ObjectHoardingGeometry( strips, grid.Width, grid.Height );
	}

	private static readonly NVector2[] SinglePulls = { new( 0.3f, -0.3f ), new( -0.3f, -0.3f ), new( 0.3f, 0.3f ), new( 0.3f, 0.3f ) };

	/// <summary>The four corner pulls of a cell: half the X/Z offset to the second nearest ground vertex, or none when there is no second vertex.</summary>
	private static NVector2[] CornerPulls( int column, int row, IReadOnlyList<NVector3> ground )
	{
		float c10 = column * 10f, r10 = row * 10f;
		var corners = new[] { new NVector2( c10, r10 + 10f ), new NVector2( c10 + 10f, r10 + 10f ), new NVector2( c10 + 10f, r10 ), new NVector2( c10, r10 ) };
		var pulls = new NVector2[4];
		for ( var corner = 0; corner < 4; corner++ )
		{
			var (cx, cz) = (corners[corner].X, corners[corner].Y);
			var nearest = float.MaxValue;
			var nearestIndex = -1;
			var second = float.MaxValue;
			var secondIndex = -1;
			for ( var index = 0; index < ground.Count; index++ )
			{
				var dx = (double)ground[index].X - cx;
				var dz = (double)ground[index].Z - cz;
				// dz is squared once as a float and once at full precision (0x004535FD: the float store, then the multiply by it), and the distance is compared at full precision first
				var distance = Math.Sqrt( dx * dx + (double)(float)dz * dz );
				var rounded = (float)distance;
				if ( distance < nearest )
				{
					second = nearest;
					secondIndex = nearestIndex;
					nearest = rounded;
					nearestIndex = index;
				}
				else if ( rounded < second )
				{
					second = rounded;
					secondIndex = index;
				}
			}
			pulls[corner] = secondIndex < 0 ? NVector2.Zero
				: new NVector2( F( ((double)ground[secondIndex].X - cx) * 0.5 ), F( ((double)ground[secondIndex].Z - cz) * 0.5 ) );
		}
		return pulls;
	}

	/// <summary>
	/// The heights of a placed object's boards (tp.exe <c>0x00454190</c>): each end stands on the terrain corner its cell corner maps to over the placed
	/// footprint's box, at half its height above the object's base, and the board's top is 10 units above that. Returns the bottom Y (model units) of each
	/// strip's ends A and B.
	/// </summary>
	// [BIN:TPW-EXE:0x00454190 hoarding terrain conform] for each strip end (column, row) the corner mapper 0x00454050 takes u = column / width and v = row / height of the hoarding grid and the footprint box +0xC0/+0xC4 (x0 << 16 | y0, x1 << 16 | y1, the last cells inclusive, written by 0x00467030) and truncates (1 − u)·x0 + u·x1 + 0.1 and (1 − v)·y0 + v·y1 + 0.1 at angle 0, (1 − v)·x0 + v·x1 and (1 − u)·y1 + u·y0 at 90, (1 − u)·x1 + u·x0 and (1 − v)·y1 + v·y0 at 180, (1 − v)·x1 + v·x0 and (1 − u)·y0 + u·y1 at 270; the end's bottom is (corner height (heights +0x28, row stride count + 1) − the root node's Y +0x44) · 0.5 and its top that plus 10; the instance constructor 0x00463060 runs it once (flag 4, 0x004635FC)
	public static (float BottomA, float BottomB)[] Conform( ObjectHoardingGeometry geometry, ModelHeightfield field, int rotation, (int X0, int Y0, int X1, int Y1) box, float baseHeight )
	{
		var result = new (float, float)[geometry.Strips.Count];
		float Bottom( (int Column, int Row) end )
		{
			var u = (float)end.Column / geometry.Width;
			var v = (float)end.Row / geometry.Height;
			var (x, y) = MapCorner( rotation, box, u, v );
			x = Math.Clamp( x, 0, field.CellCountX );
			y = Math.Clamp( y, 0, field.CellCountZ );
			return F( ((double)field.GetCornerHeight( x, y ) - baseHeight) * 0.5 );
		}
		for ( var index = 0; index < result.Length; index++ )
			result[index] = (Bottom( geometry.Strips[index].EndA ), Bottom( geometry.Strips[index].EndB ));
		return result;
	}

	/// <summary>The terrain corner of a hoarding end (tp.exe <c>0x00454050</c>), from its grid fractions and the footprint box.</summary>
	internal static (int X, int Y) MapCorner( int rotation, (int X0, int Y0, int X1, int Y1) box, float u, float v )
	{
		static int Lerp( float t, int from, int to ) => (int)((1.0 - t) * from + (double)t * to + 0.10000000149011612);
		return rotation switch
		{
			90 => (Lerp( v, box.X0, box.X1 ), Lerp( u, box.Y1, box.Y0 )),
			180 => (Lerp( u, box.X1, box.X0 ), Lerp( v, box.Y1, box.Y0 )),
			270 => (Lerp( v, box.X1, box.X0 ), Lerp( u, box.Y0, box.Y1 )),
			_ => (Lerp( u, box.X0, box.X1 ), Lerp( v, box.Y0, box.Y1 ))
		};
	}

	/// <summary>
	/// The rise of strip <paramref name="strip"/> of <paramref name="count"/> at hoarding level <paramref name="level"/> (tp.exe <c>0x004543C0</c>): the board's
	/// height above its bottom and the texture V of its bottom corners (the top corners keep V 1).
	/// </summary>
	// [BIN:TPW-EXE:0x004543C0 hoarding rise] strip i of n rises by r = clamp(1 − ((i / n)·0.8 − (level − 0.25))·4, 0, 1) times 1.0 for an even i and 0.8 for an odd one: its top corners stand that times 10 above its bottom corners, and the bottom corners take U 0 and 1 with V 1 − r·factor
	public static (double Height, float BottomV) Rise( int strip, int count, float level )
	{
		var factor = (strip & 1) == 0 ? 1.0 : 0.800000011920929;
		var rise = 1.0 - (((double)strip / count) * 0.800000011920929 - ((double)level - 0.25)) * 4.0;
		rise = Math.Clamp( rise, 0.0, 1.0 );
		var scaled = rise * factor;
		return (scaled * 10.0, F( 1.0 - scaled ));
	}

	private static float F( double value ) => (float)value;

	private static double Fmod( double value, double divisor ) => value - divisor * Math.Truncate( value / divisor );

	/// <summary>A node's world matrix: its stored matrix times its parent's, affine as the original composes it.</summary>
	private static Matrix4x4 Compose( Matrix4x4 local, Matrix4x4 parent )
	{
		static float Dot3( double a, double b, double c, double d, double e, double f ) => (float)(a * d + b * e + c * f);
		return new Matrix4x4(
			Dot3( local.M11, local.M12, local.M13, parent.M11, parent.M21, parent.M31 ), Dot3( local.M11, local.M12, local.M13, parent.M12, parent.M22, parent.M32 ), Dot3( local.M11, local.M12, local.M13, parent.M13, parent.M23, parent.M33 ), 0,
			Dot3( local.M21, local.M22, local.M23, parent.M11, parent.M21, parent.M31 ), Dot3( local.M21, local.M22, local.M23, parent.M12, parent.M22, parent.M32 ), Dot3( local.M21, local.M22, local.M23, parent.M13, parent.M23, parent.M33 ), 0,
			Dot3( local.M31, local.M32, local.M33, parent.M11, parent.M21, parent.M31 ), Dot3( local.M31, local.M32, local.M33, parent.M12, parent.M22, parent.M32 ), Dot3( local.M31, local.M32, local.M33, parent.M13, parent.M23, parent.M33 ), 0,
			(float)((double)local.M41 * parent.M11 + (double)local.M42 * parent.M21 + (double)local.M43 * parent.M31 + parent.M41 ),
			(float)((double)local.M41 * parent.M12 + (double)local.M42 * parent.M22 + (double)local.M43 * parent.M32 + parent.M42 ),
			(float)((double)local.M41 * parent.M13 + (double)local.M42 * parent.M23 + (double)local.M43 * parent.M33 + parent.M43 ), 1 );
	}

	/// <summary>A point times an affine matrix (row vector, translation in the fourth row), as 0x0046FC00 transforms a vertex.</summary>
	private static NVector3 Transform( NVector3 point, Matrix4x4 matrix ) => new(
		(float)((double)point.X * matrix.M11 + (double)point.Y * matrix.M21 + (double)point.Z * matrix.M31 + matrix.M41),
		(float)((double)point.X * matrix.M12 + (double)point.Y * matrix.M22 + (double)point.Z * matrix.M32 + matrix.M42),
		(float)((double)point.X * matrix.M13 + (double)point.Y * matrix.M23 + (double)point.Z * matrix.M33 + matrix.M43) );
}
