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

	/// <summary>The scale of the track layer's pixels per world unit: 48 / width on a theme map (a cell is 10 units, 480 / width pixels), the same share of the image on the sandbox's.</summary>
	// [BIN:TPW-EXE:0x5f300f track scale] the float 480.0 / (width * 10.0) (0x70304c, 0x703044) serves both axes: the pixel is ftol(x * scale) + 15 and ftol(z * scale) + 42
	public float TrackScale => (Original ? 480f : ImagePixels) / (Width * 10f);

	public float X( int column ) => OriginX + column * CellWidth;
	public float Y( int row ) => OriginY + row * CellHeight;

	/// <summary>The surface pixel of a position in cells: the original's items use the cell size as a float, 480 / width (<c>0x5F4180</c>), where its cells use the truncated one.</summary>
	public (float X, float Y) Position( float x, float y ) => Original ? (OriginX + x * (480f / Width), OriginY + y * (425f / Height)) : (OriginX + x * CellWidth, OriginY + y * CellHeight);
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
	/// <summary>The sprite items written when the window opened (guests, staff, litter and cameras), in the order of the original's item list.</summary>
	public List<MapDot> Dots { get; } = new();
	/// <summary>The item under the pointer (<c>[map+0x20060]</c>): its patrol area is drawn in the brighter blue.</summary>
	public MapDot? Hovered { get; set; }
	/// <summary>Whether the dot images (<see cref="ParkMapDots.Images"/>) are in the game data; without them no dot is drawn.</summary>
	public bool SpritesAvailable { get; set; }

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

	/// <summary>
	/// The items' part of a render: every item is coloured again and a staff member's area (kind 2 items whose filter holds) is drawn over the surface. The area is the rectangle of the member's two
	/// corner cells; one that starts in column or row 0, or ends in column or row 127 (the whole-map default), is not drawn.
	/// </summary>
	// [BIN:TPW-EXE:0x5f2dbc item loop] each item is passed to 0x5f44d0, then a kind 2 item whose filter holds (mode 1 the layer word & value, 2 the staff role == value, 3 the value mode == value) draws the rectangle of the cell words +0x20a and +0x20c of its thing: x from 15 + column * cell width to 15 + (column + 1) * cell width of the far corner, the same for y from 42, in 0xf86bb4 for the item under the pointer, else 0xf86b90; the word's column and row are tested (0x5f2e5e..0x5f2e94): the first corner's must be above 0, the second's below 0x7f
	public void RenderItems( uint layers, int staff, int valueMode )
	{
		var surface = Surface;
		foreach ( var dot in Dots )
		{
			dot.Color = ParkMapDots.ColorOf( dot );
			if ( dot.Kind != 2 || dot.Staff is not { } member || !ParkMapDots.Visible( dot, layers, staff, valueMode ) )
				continue;
			var (x0, y0, x1, y1) = member.PatrolArea;
			if ( x0 <= 0 || y0 <= 0 || x1 >= 0x7F || y1 >= 0x7F )
				continue;
			Prims.Add( new MapPrim( surface.X( x0 ), surface.Y( y0 ), (x1 + 1 - x0) * surface.CellWidth, (y1 + 1 - y0) * surface.CellHeight,
				ReferenceEquals( Hovered, dot ) ? ParkMapPalette.ZoneHovered : ParkMapPalette.Zone ) );
		}
	}

	/// <summary>
	/// Draws a coaster's track: the Bézier curve of every pair of consecutive knots at t = 0, 0.1, 0.2 and so on while t stays at or below 1 (in single precision, so the last sample is
	/// at 0.9), joined by the anti-aliased line of <see cref="ParkMapLine"/>; the pixel of a point is its ground position (x, z) times <see cref="MapSurface.TrackScale"/>, truncated, from (15, 42).
	/// </summary>
	// [BIN:TPW-EXE:0x5f2fd0 map track layer] per coaster (0x438720 counts them, 0x438730 gives the knot chain and the object whose repair +0x44 is the colour, through 0x5f09a0 and 0xf86bd8, 0xf86bfc, 0xf86c20): per pair 0x43e1b0 at t from 0, +0.1 while t <= 1.0 (0x703050, 0x703004); the first point of a coaster is only remembered, every later one is joined to the one before it (the line routine 0x5efb10 for 32 bit surfaces) over all pairs: the link between two pairs is the last sample of the first and the first of the second
	public void RenderTrack( IEnumerable<(System.Numerics.Vector3 P0, System.Numerics.Vector3 P1, System.Numerics.Vector3 P2, System.Numerics.Vector3 P3)> pairs, RgbaByte color )
	{
		var scale = Surface.TrackScale;
		var first = true;
		int previousX = 0, previousY = 0;
		foreach ( var (p0, p1, p2, p3) in pairs )
		{
			for ( var t = 0f; t <= 1f; t += 0.1f )
			{
				var point = HermiteCoasterCurve.Bezier( p0, p1, p2, p3, t );
				var x = (int)(point.X * scale) + (int)Surface.OriginX;
				var y = (int)(point.Z * scale) + (int)Surface.OriginY;
				if ( !first )
					AddLine( previousX, previousY, x, y, color );
				first = false;
				(previousX, previousY) = (x, y);
			}
		}
	}

	/// <summary>One anti-aliased line of the surface, clipped to the 512 pixel image: every pixel the routine writes with a coverage above 0 as a rectangle whose alpha is the coverage.</summary>
	private void AddLine( int x0, int y0, int x1, int y1, RgbaByte color )
	{
		ParkMapLine.Coverage( x0, y0, x1, y1, 0, 0, (int)MapSurface.ImagePixels, (int)MapSurface.ImagePixels, ( x, y, coverage ) =>
		{
			if ( coverage > 0 )
				Prims.Add( new MapPrim( x, y, 1, 1, new RgbaByte( color.R, color.G, color.B, (byte)(color.A * coverage / 255) ) ) );
		} );
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
