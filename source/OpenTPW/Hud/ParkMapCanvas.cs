using Veldrid;

namespace OpenTPW.Hud;

/// <summary>One rectangle of the map's drawing surface, in the pixels of the 512 pixel map image.</summary>
internal readonly record struct MapPrim( float X, float Y, float Width, float Height, RgbaByte Color );

/// <summary>
/// Where the map's cells lie on its drawing surface. A theme map's surface is the level's <c>2dmap.tga</c>: the original draws cell (column, row) at x = 15 + column × trunc(480 / width) and
/// y = 42 + row × trunc(425 / height) (<c>0x5F23DC..0x5F2400</c>); the sandbox, which has no such image, fills the whole image with its cells.
/// </summary>
internal readonly record struct MapSurface( int Width, int Height, bool Original )
{
	public const float ImagePixels = 512f;

	/// <summary>The width of a cell in surface pixels (the original's integer, the sandbox's share of the image).</summary>
	public float CellWidth => Original ? (int)(480f / Width) : ImagePixels / Width;
	public float CellHeight => Original ? (int)(425f / Height) : ImagePixels / Height;
	public float OriginX => Original ? 15f : 0f;
	public float OriginY => Original ? 42f : 0f;

	public float X( int column ) => OriginX + column * CellWidth;
	public float Y( int row ) => OriginY + row * CellHeight;
}

/// <summary>
/// The map window's state between its render events, as the original keeps it in the map object and its drawing surface: the cell classes written when the window opens, and the rectangles the last
/// render drew over the level's art. The original draws the surface when the window opens and when a layer or mode button changes (<c>0x5F2380</c> from <c>0x5F0FEB</c>, <c>0x5F15D0</c>,
/// <c>0x5F1606</c>, <c>0x5F163C</c>), not on every frame; the rectangles are kept the same way.
/// </summary>
internal sealed class ParkMapCanvas
{
	public ParkMapCanvas( MapSurface surface )
	{
		Surface = surface;
		Classes = new int[surface.Width * surface.Height];
		Owners = new ParkMapThing?[surface.Width * surface.Height];
	}

	public MapSurface Surface { get; }
	/// <summary>The class of every cell (row-major, <see cref="MapSurface.Width"/> per row).</summary>
	public int[] Classes { get; }
	/// <summary>The object each object-class cell belongs to.</summary>
	public ParkMapThing?[] Owners { get; }
	/// <summary>The rectangles of the last render, in the order the original draws them.</summary>
	public List<MapPrim> Prims { get; } = new();

	/// <summary>Writes the cell classes (<see cref="ParkMapClasses.Write"/>) of <paramref name="cells"/>.</summary>
	public void WriteClasses( IParkMapCells cells ) => ParkMapClasses.Write( cells, Classes, Owners );

	/// <summary>The class of a cell; the cells beyond the map have none (the original's class table is 128 × 128 and the writer only fills the map's cells).</summary>
	private int ClassAt( int x, int y ) => (uint)x < (uint)Surface.Width && (uint)y < (uint)Surface.Height ? Classes[y * Surface.Width + x] : ParkMapClasses.None;
	private ParkMapThing? OwnerAt( int x, int y ) => (uint)x < (uint)Surface.Width && (uint)y < (uint)Surface.Height ? Owners[y * Surface.Width + x] : null;

	/// <summary>
	/// Draws the cells: every cell with a class in raster order, its fill (the value mode's ramp for the classes that take it, else the class's layer test and fill) and, for the classes 1 to 8, its
	/// outline against the neighbours of another class or object.
	/// </summary>
	public void RenderCells( uint layers, int staff, int valueMode )
	{
		Prims.Clear();
		var surface = Surface;
		var cw = surface.CellWidth;
		var ch = surface.CellHeight;
		for ( var y = 0; y < surface.Height; y++ )
		{
			for ( var x = 0; x < surface.Width; x++ )
			{
				var cls = Classes[y * surface.Width + x];
				var thing = Owners[y * surface.Width + x];
				var fill = FillOf( cls, thing, layers, staff, valueMode );
				var x0 = surface.X( x );
				var y0 = surface.Y( y );
				if ( fill is { } color )
					Prims.Add( new MapPrim( x0, y0, cw, ch, color ) );
				if ( cls is < ParkMapClasses.Ride or > ParkMapClasses.TrackTile )
					continue;
				// [BIN:TPW-EXE:0x5f2a94 map outline] the classes 1 to 7 take the closed style 0xf86a28 (black) when 0x4e0440 answers 1 (the open word +0x68 is 0), else 0xf86a4c (green); class 8 takes the open style; an edge is drawn on the four sides where the neighbour's class or object differs, in the order up, down, left, right
				var outline = cls <= ParkMapClasses.FeatureStaffRoom && thing?.Closed == true ? ParkMapPalette.OutlineClosed : ParkMapPalette.OutlineOpen;
				// [BIN:TPW-EXE:0x5f2ba2 outline lengths] all four edges take the cell width as their length (esi = [esp+0x28], 0x5f2b6f), the lower edge lies at y + width - 1 and the right one at x + width - 1, whatever the cell height
				if ( !Same( x, y - 1, cls, thing ) )
					Prims.Add( new MapPrim( x0, y0, cw, 1, outline ) );
				if ( !Same( x, y + 1, cls, thing ) )
					Prims.Add( new MapPrim( x0, y0 + cw - 1, cw, 1, outline ) );
				if ( !Same( x - 1, y, cls, thing ) )
					Prims.Add( new MapPrim( x0, y0, 1, cw, outline ) );
				if ( !Same( x + 1, y, cls, thing ) )
					Prims.Add( new MapPrim( x0 + cw - 1, y0, 1, cw, outline ) );
			}
		}
	}

	private bool Same( int x, int y, int cls, ParkMapThing? thing ) => ClassAt( x, y ) == cls && ReferenceEquals( OwnerAt( x, y ), thing );

	/// <summary>The fill of a cell of class <paramref name="cls"/>, null where the renderer draws nothing.</summary>
	internal static RgbaByte? FillOf( int cls, ParkMapThing? thing, uint layers, int staff, int valueMode )
	{
		if ( cls == ParkMapClasses.None )
			return null;
		// [BIN:TPW-EXE:0x5f24eb value mode fill] mode 2 fills the classes 1, 2, 3 and 8 with the ramp of the excitement 0x4e0860(0) & 0xff, mode 3 with the ramp of the satisfaction 0x4e1e30, through 0x5f09a0 and the records 0xf86bd8, 0xf86bfc, 0xf86c20; the layer test is skipped
		if ( ParkMapClasses.TakesValueMode( cls ) && valueMode is 2 or 3 )
		{
			var value = valueMode == 2 ? thing?.Excitement : thing?.Satisfaction;
			return value is { } byteValue ? ParkMapPalette.RepairRamp( byteValue & 255 ) : ParkMapPalette.UnavailableObject;
		}
		if ( !ParkMapClasses.Shown( cls, layers, staff ) )
			return null;
		return cls switch
		{
			ParkMapClasses.Fence => ParkMapPalette.Fence,
			ParkMapClasses.Path => ParkMapPalette.Path,
			ParkMapClasses.Queue => ParkMapPalette.Queue,
			ParkMapClasses.Unowned => ParkMapPalette.Unowned,
			_ => thing?.Repair ?? ParkMapPalette.UnavailableObject
		};
	}
}
