namespace OpenTPW;

/// <summary>
/// An 8-bit coverage image. <see cref="OriginX"/>/<see cref="OriginY"/> give the pixel position of
/// the pen origin (baseline start) inside the image; rows run top to bottom.
/// </summary>
public sealed record CoverageBitmap( int Width, int Height, int OriginX, int OriginY, byte[] Coverage )
{
	public static CoverageBitmap Empty { get; } = new( 0, 0, 0, 0, Array.Empty<byte>() );

	public byte this[int x, int y] => Coverage[y * Width + x];

	/// <summary>SHA-256 over width, height, origin and coverage (used to pin rasterizer output).</summary>
	public string ComputeHash()
	{
		var header = new byte[16];
		BitConverter.TryWriteBytes( header.AsSpan( 0 ), Width );
		BitConverter.TryWriteBytes( header.AsSpan( 4 ), Height );
		BitConverter.TryWriteBytes( header.AsSpan( 8 ), OriginX );
		BitConverter.TryWriteBytes( header.AsSpan( 12 ), OriginY );
		return Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( header.Concat( Coverage ).ToArray() ) );
	}
}

/// <summary>
/// Scanline rasterizer for TrueType outlines: quadratic contours are flattened to lines, every pixel
/// row is sampled with <see cref="SubScanlines"/> horizontal sub-scanlines, spans are filled with the
/// non-zero winding rule, and span ends contribute exact fractional horizontal coverage. The result
/// is deterministic (plain IEEE float/double arithmetic, fixed sampling) so its hashes can be pinned.
/// No hinting and no gamma: coverage is linear.
/// </summary>
public static class TrueTypeRasterizer
{
	// [APPROX:COMPAT-009] 16 sub-scanlines, unhinted linear coverage instead of GDI ANTIALIASED_QUALITY — evidence needed: captures of original sign text.
	public const int SubScanlines = 16;
	public const int MaximumDimension = 8192;
	private const double FlatnessPixels = 0.05;

	private readonly record struct Edge( double X0, double Y0, double X1, double Y1, int Winding );

	/// <summary>Rasterizes one glyph at <paramref name="scale"/> pixels per font unit.</summary>
	public static CoverageBitmap RasterizeGlyph( TrueTypeFont font, int glyph, float scale ) =>
		Rasterize( new[] { (font.GetOutline( glyph ), 0f) }, scale );

	/// <summary>
	/// Rasterizes outlines placed at horizontal pen positions (font units) on one baseline. Placing a
	/// whole string in one pass keeps overlapping glyphs (script fonts) correct under non-zero winding.
	/// </summary>
	public static CoverageBitmap Rasterize( IEnumerable<(IReadOnlyList<IReadOnlyList<TrueTypePoint>> Outline, float PenX)> placed, float scale ) =>
		Rasterize( placed, scale, scale );

	/// <summary>As <see cref="Rasterize(IEnumerable{ValueTuple{IReadOnlyList{IReadOnlyList{TrueTypePoint}}, float}}, float)"/> with separate horizontal and vertical scales (GDI-style condensed/expanded text).</summary>
	public static CoverageBitmap Rasterize( IEnumerable<(IReadOnlyList<IReadOnlyList<TrueTypePoint>> Outline, float PenX)> placed, float scaleX, float scaleY )
	{
		if ( !(scaleX > 0) || !float.IsFinite( scaleX ) || !(scaleY > 0) || !float.IsFinite( scaleY ) )
			throw new ArgumentOutOfRangeException( nameof( scaleX ), "Scales must be positive." );
		var scale = scaleY;
		var edges = new List<Edge>();
		foreach ( var (outline, penX) in placed )
		{
			foreach ( var contour in outline )
				Flatten( contour, penX, scaleX, scaleY, edges );
		}
		if ( edges.Count == 0 )
			return CoverageBitmap.Empty;

		// Pixel space: x right, y down, baseline at y = 0 before the origin shift.
		// A tiny tolerance keeps float scale noise (1000 * 0.004f = 4.0000002) from adding an empty row/column.
		const double Snap = 1e-5;
		var minX = Math.Floor( edges.Min( edge => Math.Min( edge.X0, edge.X1 ) ) + Snap );
		var maxX = Math.Ceiling( edges.Max( edge => Math.Max( edge.X0, edge.X1 ) ) - Snap );
		var minY = Math.Floor( edges.Min( edge => Math.Min( edge.Y0, edge.Y1 ) ) + Snap );
		var maxY = Math.Ceiling( edges.Max( edge => Math.Max( edge.Y0, edge.Y1 ) ) - Snap );
		var width = (int)(maxX - minX);
		var height = (int)(maxY - minY);
		if ( width <= 0 || height <= 0 )
			return CoverageBitmap.Empty;
		if ( width > MaximumDimension || height > MaximumDimension )
			throw new ArgumentOutOfRangeException( nameof( scale ), $"Rasterized size {width}x{height} exceeds {MaximumDimension}." );

		var coverage = new byte[width * height];
		var accumulator = new double[width + 1];
		var crossings = new List<(double X, int Winding)>();
		// Edges sorted by top so each row only scans active candidates.
		var sorted = edges.Select( edge => edge with { X0 = edge.X0 - minX, X1 = edge.X1 - minX, Y0 = edge.Y0 - minY, Y1 = edge.Y1 - minY } )
			.OrderBy( edge => Math.Min( edge.Y0, edge.Y1 ) ).ToArray();
		var firstCandidate = 0;
		for ( var row = 0; row < height; row++ )
		{
			Array.Clear( accumulator );
			while ( firstCandidate < sorted.Length && Math.Max( sorted[firstCandidate].Y0, sorted[firstCandidate].Y1 ) < row )
				firstCandidate++;
			for ( var sub = 0; sub < SubScanlines; sub++ )
			{
				var y = row + (sub + 0.5) / SubScanlines;
				crossings.Clear();
				for ( var i = firstCandidate; i < sorted.Length; i++ )
				{
					var edge = sorted[i];
					var top = Math.Min( edge.Y0, edge.Y1 );
					if ( top > y )
						break;
					var bottom = Math.Max( edge.Y0, edge.Y1 );
					// Half-open [top, bottom) so shared vertices are counted once.
					if ( y < top || y >= bottom )
						continue;
					var t = (y - edge.Y0) / (edge.Y1 - edge.Y0);
					crossings.Add( (edge.X0 + t * (edge.X1 - edge.X0), edge.Winding) );
				}
				if ( crossings.Count < 2 )
					continue;
				crossings.Sort( ( a, b ) => a.X.CompareTo( b.X ) );
				var winding = 0;
				for ( var i = 0; i < crossings.Count - 1; i++ )
				{
					winding += crossings[i].Winding;
					if ( winding != 0 )
						AddSpan( accumulator, crossings[i].X, crossings[i + 1].X, width );
				}
			}
			for ( var x = 0; x < width; x++ )
			{
				var value = accumulator[x] / SubScanlines;
				coverage[row * width + x] = (byte)Math.Round( Math.Clamp( value, 0, 1 ) * 255, MidpointRounding.AwayFromZero );
			}
		}
		return new CoverageBitmap( width, height, (int)-minX, (int)-minY, coverage );
	}

	private static void AddSpan( double[] accumulator, double x0, double x1, int width )
	{
		x0 = Math.Clamp( x0, 0, width );
		x1 = Math.Clamp( x1, 0, width );
		if ( x1 <= x0 )
			return;
		var first = (int)Math.Floor( x0 );
		var last = (int)Math.Floor( x1 );
		if ( first == last )
		{
			accumulator[first] += x1 - x0;
			return;
		}
		accumulator[first] += first + 1 - x0;
		for ( var x = first + 1; x < last; x++ )
			accumulator[x] += 1;
		if ( last < width )
			accumulator[last] += x1 - last;
	}

	private static void Flatten( IReadOnlyList<TrueTypePoint> contour, float penX, float scaleX, float scaleY, List<Edge> edges )
	{
		if ( contour.Count < 2 )
			return;
		(double X, double Y) ToPixel( TrueTypePoint point ) => ((point.X + penX) * (double)scaleX, -point.Y * (double)scaleY);
		(double X, double Y) Mid( (double X, double Y) a, (double X, double Y) b ) => ((a.X + b.X) / 2, (a.Y + b.Y) / 2);

		// Start at an on-curve point (or the midpoint of the first two control points).
		var count = contour.Count;
		var startIndex = -1;
		for ( var i = 0; i < count; i++ )
		{
			if ( contour[i].OnCurve )
			{
				startIndex = i;
				break;
			}
		}
		(double X, double Y) start;
		if ( startIndex < 0 )
		{
			start = Mid( ToPixel( contour[0] ), ToPixel( contour[1] ) );
			startIndex = 0;
		}
		else
			start = ToPixel( contour[startIndex] );

		var current = start;
		(double X, double Y)? control = null;
		for ( var step = 1; step <= count; step++ )
		{
			var point = contour[(startIndex + step) % count];
			var pixel = ToPixel( point );
			if ( step == count && contour[startIndex].OnCurve )
				pixel = start;
			if ( point.OnCurve || (step == count && contour[startIndex].OnCurve) )
			{
				if ( control is { } c )
					AddQuadratic( edges, current, c, pixel );
				else
					AddLine( edges, current, pixel );
				current = pixel;
				control = null;
			}
			else if ( control is { } previous )
			{
				var middle = Mid( previous, pixel );
				AddQuadratic( edges, current, previous, middle );
				current = middle;
				control = pixel;
			}
			else
				control = pixel;
		}
		// Close the contour (all-off-curve contours or a trailing control point).
		if ( control is { } last )
			AddQuadratic( edges, current, last, start );
		else if ( current != start )
			AddLine( edges, current, start );
	}

	private static void AddQuadratic( List<Edge> edges, (double X, double Y) p0, (double X, double Y) c, (double X, double Y) p1 )
	{
		var deviationX = p0.X - 2 * c.X + p1.X;
		var deviationY = p0.Y - 2 * c.Y + p1.Y;
		var deviation = Math.Sqrt( deviationX * deviationX + deviationY * deviationY );
		// Max distance of the curve from an n-segment polyline is deviation / (8 n^2).
		var segments = (int)Math.Clamp( Math.Ceiling( Math.Sqrt( deviation / (8 * FlatnessPixels) ) ), 1, 128 );
		var previous = p0;
		for ( var i = 1; i <= segments; i++ )
		{
			var t = (double)i / segments;
			var u = 1 - t;
			var next = i == segments ? p1 : (u * u * p0.X + 2 * u * t * c.X + t * t * p1.X, u * u * p0.Y + 2 * u * t * c.Y + t * t * p1.Y);
			AddLine( edges, previous, next );
			previous = next;
		}
	}

	private static void AddLine( List<Edge> edges, (double X, double Y) a, (double X, double Y) b )
	{
		if ( a.Y == b.Y )
			return;
		// TrueType outer contours run clockwise in Y-up space; in Y-down pixel space a downward edge counts +1.
		edges.Add( new Edge( a.X, a.Y, b.X, b.Y, b.Y > a.Y ? 1 : -1 ) );
	}
}
