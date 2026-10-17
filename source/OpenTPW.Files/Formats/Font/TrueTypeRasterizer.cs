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

/// <summary>How a rasterized pixel's coverage is measured.</summary>
public enum CoverageModel
{
	/// <summary>
	/// The GDI ANTIALIASED_QUALITY model (<see cref="TrueTypeRasterizer.GdiOversampling"/>): 4x4 point samples per pixel, so a pixel
	/// takes one of 17 levels (0..16), written as <c>(level * 255 + 8) / 16</c>. No hinting, no gamma.
	/// </summary>
	GdiGray4,
	/// <summary>Resolution independent linear coverage: 16 sub-scanlines with exact fractional span ends, no quantisation.</summary>
	Linear
}

/// <summary>
/// Scanline rasterizer for TrueType outlines: quadratic contours are flattened to lines, spans are filled with the
/// non-zero winding rule and measured per <see cref="CoverageModel"/>. The default model is the documented GDI grayscale
/// smoothing (<see cref="CoverageModel.GdiGray4"/>); <see cref="CoverageModel.Linear"/> keeps the exact linear coverage.
/// The result is deterministic (plain IEEE float/double arithmetic, fixed sampling) so its hashes can be pinned.
/// </summary>
/// <remarks>
/// The GDI model, from the sources in docs/COMPATIBILITY.md (COMPAT-003): GetGlyphOutline documents the grayscale bitmaps as
/// GGO_GRAY2/4/8 = 5/17/65 levels (n x n oversampling, levels 0..n*n), and Microsoft's typography notes describe smoothing as
/// grid-fit, overscale, downsample; the 17-level, 4x4 form is the one used here. Hinting is NOT applied (the repository has no TrueType
/// bytecode interpreter; the 17 shipped fonts carry hinting programs and no <c>gasp</c> table). The level to byte mapping,
/// the sample positions and the absence of gamma are the model's own and are not verified against a GDI capture.
/// </remarks>
public static class TrueTypeRasterizer
{
	/// <summary>Oversampling per axis of the GDI grayscale model (the GGO_GRAY4 form: 4 x 4 samples, 17 levels).</summary>
	public const int GdiOversampling = 4;
	/// <summary>Sub-scanlines per pixel row of the <see cref="CoverageModel.Linear"/> model.</summary>
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
	public static CoverageBitmap Rasterize( IEnumerable<(IReadOnlyList<IReadOnlyList<TrueTypePoint>> Outline, float PenX)> placed, float scaleX, float scaleY ) =>
		Rasterize( placed, scaleX, scaleY, CoverageModel.GdiGray4 );

	/// <summary>As the overload without a model, measuring coverage with <paramref name="model"/>.</summary>
	public static CoverageBitmap Rasterize( IEnumerable<(IReadOnlyList<IReadOnlyList<TrueTypePoint>> Outline, float PenX)> placed, float scaleX, float scaleY, CoverageModel model )
	{
		var gdi = model == CoverageModel.GdiGray4;
		var samplesPerRow = gdi ? GdiOversampling : SubScanlines;
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
		// Edges sorted by top; tops/bottoms are precomputed once from the same normalized doubles the
		// per-sample tests below compare against, so every window decision sees a full scan's values.
		var sorted = edges.Select( edge => edge with { X0 = edge.X0 - minX, X1 = edge.X1 - minX, Y0 = edge.Y0 - minY, Y1 = edge.Y1 - minY } )
			.OrderBy( edge => Math.Min( edge.Y0, edge.Y1 ) ).ToArray();
		var tops = new double[sorted.Length];
		var bottoms = new double[sorted.Length];
		for ( var i = 0; i < sorted.Length; i++ )
		{
			tops[i] = Math.Min( sorted[i].Y0, sorted[i].Y1 );
			bottoms[i] = Math.Max( sorted[i].Y0, sorted[i].Y1 );
		}
		// Active-edge window per row: indices into sorted, kept in ascending sorted order (ended edges
		// compact out in place, candidates entering the row append in sorted order). The per-sample scan
		// therefore appends crossings in exactly the original full-scan order — tops are non-decreasing
		// along the list, so the early break stays valid — and the crossing arithmetic still reads the
		// same sorted doubles.
		var active = new int[sorted.Length];
		var activeCount = 0;
		var nextCandidate = 0;
		for ( var row = 0; row < height; row++ )
		{
			Array.Clear( accumulator );
			var kept = 0;
			for ( var i = 0; i < activeCount; i++ )
				if ( bottoms[active[i]] > row )
					active[kept++] = active[i];
			activeCount = kept;
			while ( nextCandidate < sorted.Length && tops[nextCandidate] < row + 1 )
				active[activeCount++] = nextCandidate++;
			for ( var sub = 0; sub < samplesPerRow; sub++ )
			{
				var y = row + (sub + 0.5) / samplesPerRow;
				crossings.Clear();
				for ( var i = 0; i < activeCount; i++ )
				{
					var index = active[i];
					var top = tops[index];
					if ( top > y )
						break;
					var bottom = bottoms[index];
					// Half-open [top, bottom) so shared vertices are counted once.
					if ( y < top || y >= bottom )
						continue;
					var edge = sorted[index];
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
					{
						if ( gdi )
							AddSamples( accumulator, crossings[i].X, crossings[i + 1].X, width );
						else
							AddSpan( accumulator, crossings[i].X, crossings[i + 1].X, width );
					}
				}
			}
			for ( var x = 0; x < width; x++ )
			{
				if ( gdi )
				{
					// [APPROX:COMPAT-003] level 0..16 -> byte as (level * 255 + 8) / 16: the level count is documented (GGO_GRAY4), the palette mapping of the 8-bit DIB is not verified
					var level = (int)Math.Clamp( accumulator[x], 0, GdiOversampling * GdiOversampling );
					coverage[row * width + x] = (byte)((level * 255 + 8) / (GdiOversampling * GdiOversampling));
					continue;
				}
				var value = accumulator[x] / SubScanlines;
				coverage[row * width + x] = (byte)Math.Round( Math.Clamp( value, 0, 1 ) * 255, MidpointRounding.AwayFromZero );
			}
		}
		return new CoverageBitmap( width, height, (int)-minX, (int)-minY, coverage );
	}

	/// <summary>Counts the 4 horizontal sample points per pixel (at (k + 0.5) / 4) inside the half-open span [x0, x1).</summary>
	private static void AddSamples( double[] accumulator, double x0, double x1, int width )
	{
		x0 = Math.Clamp( x0, 0, width );
		x1 = Math.Clamp( x1, 0, width );
		var first = (int)Math.Ceiling( x0 * GdiOversampling - 0.5 );
		var end = (int)Math.Ceiling( x1 * GdiOversampling - 0.5 );
		for ( var pixel = first / GdiOversampling; first < end; pixel++ )
		{
			var stop = Math.Min( end, (pixel + 1) * GdiOversampling );
			accumulator[pixel] += stop - first;
			first = stop;
		}
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
