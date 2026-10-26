using System.Runtime.CompilerServices;

namespace OpenTPW;

/// <summary>
/// The original's procedural terrain texturer (option PROCEDURALTEXTURING, docs/LND.md): builds the N×N ground texture of one terrain cell from a
/// theme's <see cref="LndFile"/> (<c>base.lnd</c>). A cell's texture is a pure function of the file, N and the cell, so the texturer is
/// deterministic and keeps no per-cell state. One instance owns its scratch buffers (three weight planes of N² bytes) and is not thread-safe; use
/// one instance per thread. A build allocates nothing.
/// </summary>
/// <remarks>
/// The output reproduces the head of tp.exe's build routine byte for byte (verified against the routine run in the x86 oracle, docs/reverse/ORACLE.md):
/// pixels are four bytes <c>[0, R, G, B]</c>, the rows run bottom-up (source row Y is written to row N-1-Y), and an invalid cell is the grey
/// <c>[0, 0x7F, 0x7F, 0x7F]</c>. The alpha byte is 0 as in the original; a renderer must write alpha 255 itself: OpenTPW's terrain shader discards low
/// alpha, and the original's terrain pass is deduced to have no alpha test (its cache textures carry alpha 0), though the render state of 0x56F670 is not
/// traced. That is renderer work, not done here.
/// </remarks>
public sealed class ProceduralTerrainTexturer
{
	/// <summary>Bytes per output pixel.</summary>
	public const int BytesPerPixel = 4;

	private const int RunLength = 16;

	// Image roles in file order (docs/LND.md, [BIN:TPW-EXE:0x0055F470 set loader] places them into the set structure).
	private const int LayerA0 = 0, LayerA1 = 1, LayerB0 = 2, LayerB1 = 3, LayerC0 = 4, LayerC1 = 5;
	private const int DetailA = 6, DetailB = 7, DetailC = 8, Mask2A = 9, Mask2B = 10, Mask1A = 11, Mask1B = 12;

	private readonly LndFile lnd;
	private readonly LndImageSet set;
	private readonly int size;
	private readonly int sizeShift;
	private readonly byte[] rampA = new byte[256], rampB = new byte[256], rampC = new byte[256], rampMask1 = new byte[256], rampMask2 = new byte[256];
	private readonly byte[] weight1, weight2, weight3;
	private readonly Window[] windows = new Window[LndFile.ImagesPerSet];
	private readonly byte[][] imageData = new byte[LndFile.ImagesPerSet][];
	private readonly int[] imageBase = new int[LndFile.ImagesPerSet];
	/// <summary>Per image: the byte index of the current texel row's first pixel in the image (the window offset plus row × stride), set once per row.</summary>
	private readonly int[] rowBase = new int[LndFile.ImagesPerSet];

	/// <summary>The top left pixel of the current cell's window in an image, as a byte index into <see cref="imageData"/>, and the image width in pixels.</summary>
	private struct Window
	{
		public int Offset;
		public int Stride;
	}

	public ProceduralTerrainTexturer( LndFile lnd, int size )
	{
		ArgumentNullException.ThrowIfNull( lnd );
		this.lnd = lnd;
		set = lnd.SetFor( size );
		this.size = size;
		sizeShift = System.Numerics.BitOperations.Log2( (uint)size );
		BuildRamp( rampA, lnd.LayerA );
		BuildRamp( rampB, lnd.LayerB );
		BuildRamp( rampC, lnd.LayerC );
		BuildRamp( rampMask1, lnd.Mask1 );
		BuildRamp( rampMask2, lnd.Mask2 );
		for ( var index = 0; index < LndFile.ImagesPerSet; index++ )
			(imageData[index], imageBase[index]) = ImageBytes( set.Images[index] );
		weight1 = new byte[size * size];
		weight2 = new byte[size * size];
		weight3 = new byte[size * size];
	}

	/// <summary>The pixels of an image as an array; the file stores them as slices of one array, so this does not copy.</summary>
	private static (byte[] Array, int Offset) ImageBytes( LndImage image )
		=> System.Runtime.InteropServices.MemoryMarshal.TryGetArray( image.Pixels, out var segment ) ? (segment.Array!, segment.Offset) : (image.Pixels.ToArray(), 0);

	/// <summary>The texture size N (16, 32 or 64).</summary>
	public int Size => size;

	/// <summary>Bytes of one built texture, N × N × 4.</summary>
	public int TextureBytes => size * size * BytesPerPixel;

	/// <summary>Cells the weight grid holds across (95); the cell at this column is built from the grid's flat reads (see <see cref="Build"/>), beyond it is outside.</summary>
	public int CellsX => lnd.CellsX;

	/// <summary>Cells the file can texture down (85).</summary>
	public int CellsY => lnd.CellsY;

	/// <summary>
	/// The ramp table of a (centre, width) pair. [BIN:TPW-EXE:0x0055E710 ramp table] <c>h = (w + 1) / 2</c>; entry i is 0 below <c>c - h</c>, 255 above <c>c + h</c>
	/// and <c>255 * (i - (c - h)) / (w + 1)</c> (truncating) in between.
	/// </summary>
	public static void BuildRamp( Span<byte> table, LndRamp ramp )
	{
		if ( table.Length != 256 )
			throw new ArgumentException( "A ramp table has 256 entries.", nameof( table ) );
		var half = (ramp.Width + 1) >> 1;
		var low = ramp.Centre - half;
		var high = ramp.Centre + half;
		for ( var index = 0; index < 256; index++ )
			table[index] = index < low ? (byte)0 : index > high ? (byte)255 : (byte)(255 * (index - low) / (ramp.Width + 1));
	}

	/// <summary>Whether the cell (<paramref name="x"/>, <paramref name="z"/>) can be built: it lies in the weight grid's columns (the column after the last cell included) and its samples stay inside the grid.</summary>
	public bool CanBuild( int x, int z )
		=> (uint)x <= (uint)lnd.CellsX && (uint)z < (uint)lnd.CellsY && (4 * (lnd.CellsY - z - 1) + 4) * lnd.GridWidth + 4 * x + 4 < lnd.GridWidth * lnd.GridHeight;

	/// <summary>
	/// Builds the texture of the cell (<paramref name="x"/>, <paramref name="z"/>), z being the heightfield row (the file's rows run the other way, row 84 - z).
	/// Returns false when the cell is not valid in the file's weight grid and the grey fallback was written.
	/// </summary>
	/// <param name="destination">At least <see cref="TextureBytes"/> bytes; exactly that many are written.</param>
	// [BIN:TPW-EXE:0x0055F780 procedural terrain build] validity of the four inner samples, the three weight planes, per image windows, per 16 texel run layer decisions, bottom-up rows
	public bool Build( int x, int z, Span<byte> destination ) => BuildCore( x, z, destination, false );

	/// <summary>
	/// <see cref="Build"/> written straight as RGBA, <c>[R, G, B, 255]</c>, rows still bottom-up: the bytes of <see cref="Build"/> with the alpha byte moved last and set to 255 (the game's cache stores alpha 0, which OpenTPW's terrain pass would discard). No second pass.
	/// </summary>
	public bool BuildRgba( int x, int z, Span<byte> destination ) => BuildCore( x, z, destination, true );

	private bool BuildCore( int x, int z, Span<byte> destination, bool rgba )
	{
		if ( destination.Length < TextureBytes )
			throw new ArgumentException( "The destination is smaller than one texture.", nameof( destination ) );
		// The original reads the grid as one flat array and does not test the column, and the heightfield is one column wider than the weight grid (96 against 95 cells), so the column
		// CellsX is built from what the flat reads give: its last samples are the first samples of the next sample row (the x86 oracle pins this on the shipped jungle file).
		if ( (uint)x > (uint)lnd.CellsX || (uint)z >= (uint)lnd.CellsY )
			throw new ArgumentOutOfRangeException( (uint)x > (uint)lnd.CellsX ? nameof( x ) : nameof( z ), "The cell is outside the weight grid." );
		if ( !CanBuild( x, z ) )
			throw new ArgumentOutOfRangeException( nameof( x ), "The cell's samples run past the weight grid." );
		var destinationTexture = destination[..TextureBytes];
		var cellRow = lnd.CellsY - z - 1;
		var gridWidth = lnd.GridWidth;
		var grid = lnd.Grid.Span;

		// Valid only when the validity byte of all four samples at (4x + 1 .. 4x + 2, 4 row + 1 .. 4 row + 2) is 0xFF.
		var validity = ((4 * cellRow + 1) * gridWidth + 4 * x + 1) * LndFile.GridBytesPerSample;
		var rowStep = gridWidth * LndFile.GridBytesPerSample;
		if ( (grid[validity] & grid[validity + 4] & grid[validity + rowStep] & grid[validity + rowStep + 4]) != 0xFF )
		{
			for ( var index = 0; index < destinationTexture.Length; index += BytesPerPixel )
			{
				destinationTexture[index] = rgba ? (byte)0x7F : (byte)0;
				destinationTexture[index + 1] = 0x7F;
				destinationTexture[index + 2] = 0x7F;
				destinationTexture[index + 3] = rgba ? (byte)255 : (byte)0x7F;
			}
			return false;
		}

		BuildWeights( grid, gridWidth, x, cellRow, 1, weight1 );
		BuildWeights( grid, gridWidth, x, cellRow, 2, weight2 );
		BuildWeights( grid, gridWidth, x, cellRow, 3, weight3 );

		// Texel (X, Y) reads image pixel (ox + X, oy + Y); the images tile with the period (w - N, h - N), so the window never leaves the image.
		for ( var index = 0; index < LndFile.ImagesPerSet; index++ )
		{
			var image = set.Images[index];
			var offsetX = size * x % (image.Width - size);
			var offsetY = size * cellRow % (image.Height - size);
			windows[index] = new Window { Offset = imageBase[index] + (offsetY * image.Width + offsetX) * image.BytesPerPixel, Stride = image.Width * image.BytesPerPixel };
		}

		Span<byte> mask1 = stackalloc byte[RunLength];
		Span<byte> mask2 = stackalloc byte[RunLength];
		for ( var row = 0; row < size; row++ )
		{
			var outputRow = destinationTexture.Slice( (size - 1 - row) * size * BytesPerPixel, size * BytesPerPixel );
			// the window offsets of this texel row, once per row instead of per texel
			for ( var image = 0; image < LndFile.ImagesPerSet; image++ )
				rowBase[image] = windows[image].Offset + row * windows[image].Stride;
			for ( var run = 0; run < size; run += RunLength )
			{
				var plane = row * size + run;
				var all1 = true;
				var any1 = false;
				for ( var lane = 0; lane < RunLength; lane++ )
				{
					var w3 = weight3[plane + lane];
					var value = Math.Max( rampMask1[(w3 * Map( Mask1B, run + lane )) >> 8], rampMask1[(Map( Mask1A, run + lane ) * w3) >> 8] );
					mask1[lane] = value;
					all1 &= value == 255;
					any1 |= value != 0;
				}

				// [BIN:TPW-EXE:0x0055EAA0 three layer blend] never advances its weight 1 pointer, so every texel of a three layer run uses the weight 1 of the run's first texel
				var layers = 0;
				if ( !all1 )
				{
					var all2 = true;
					var any2 = false;
					for ( var lane = 0; lane < RunLength; lane++ )
					{
						var w2 = weight2[plane + lane];
						var value = Math.Max( rampMask2[(w2 * Map( Mask2B, run + lane )) >> 8], rampMask2[(Map( Mask2A, run + lane ) * w2) >> 8] );
						mask2[lane] = value;
						all2 &= value == 255;
						any2 |= value != 0;
					}
					layers = !any1 ? (all2 ? Layer.B : !any2 ? Layer.A : Layer.BA) : (all2 ? Layer.CB : !any2 ? Layer.CA : Layer.CBA);
				}
				else
				{
					layers = Layer.C;
				}

				var quirkWeight = weight1[plane];
				for ( var lane = 0; lane < RunLength; lane++ )
				{
					var texel = run + lane;
					var w1 = layers == Layer.CBA ? quirkWeight : weight1[plane + lane];
					int red, green, blue;
					switch ( layers )
					{
						case Layer.C:
							Layered( LayerC0, LayerC1, DetailC, rampC, texel, w1, out red, out green, out blue );
							break;
						case Layer.B:
							Layered( LayerB0, LayerB1, DetailB, rampB, texel, w1, out red, out green, out blue );
							break;
						case Layer.A:
							Layered( LayerA0, LayerA1, DetailA, rampA, texel, w1, out red, out green, out blue );
							break;
						case Layer.BA:
						{
							Layered( LayerB0, LayerB1, DetailB, rampB, texel, w1, out var br, out var bg, out var bb );
							Layered( LayerA0, LayerA1, DetailA, rampA, texel, w1, out var ar, out var ag, out var ab );
							var m2 = mask2[lane];
							red = Lerp( br, ar, m2 );
							green = Lerp( bg, ag, m2 );
							blue = Lerp( bb, ab, m2 );
							break;
						}
						case Layer.CB:
						{
							Layered( LayerC0, LayerC1, DetailC, rampC, texel, w1, out var cr, out var cg, out var cb );
							Layered( LayerB0, LayerB1, DetailB, rampB, texel, w1, out var br, out var bg, out var bb );
							var m1 = mask1[lane];
							red = Lerp( cr, br, m1 );
							green = Lerp( cg, bg, m1 );
							blue = Lerp( cb, bb, m1 );
							break;
						}
						case Layer.CA:
						{
							Layered( LayerC0, LayerC1, DetailC, rampC, texel, w1, out var cr, out var cg, out var cb );
							Layered( LayerA0, LayerA1, DetailA, rampA, texel, w1, out var ar, out var ag, out var ab );
							var m1 = mask1[lane];
							red = Lerp( cr, ar, m1 );
							green = Lerp( cg, ag, m1 );
							blue = Lerp( cb, ab, m1 );
							break;
						}
						default:
						{
							Layered( LayerC0, LayerC1, DetailC, rampC, texel, w1, out var cr, out var cg, out var cb );
							Layered( LayerB0, LayerB1, DetailB, rampB, texel, w1, out var br, out var bg, out var bb );
							Layered( LayerA0, LayerA1, DetailA, rampA, texel, w1, out var ar, out var ag, out var ab );
							var m2 = mask2[lane];
							var m1 = mask1[lane];
							// the intermediate B over A result is stored as a byte before the C blend
							var baRed = Lerp( br, ar, m2 );
							var baGreen = Lerp( bg, ag, m2 );
							var baBlue = Lerp( bb, ab, m2 );
							red = Lerp( cr, baRed, m1 );
							green = Lerp( cg, baGreen, m1 );
							blue = Lerp( cb, baBlue, m1 );
							break;
						}
					}
					var offset = texel * BytesPerPixel;
					if ( rgba )
					{
						outputRow[offset] = (byte)red;
						outputRow[offset + 1] = (byte)green;
						outputRow[offset + 2] = (byte)blue;
						outputRow[offset + 3] = 255;
					}
					else
					{
						outputRow[offset] = 0;
						outputRow[offset + 1] = (byte)red;
						outputRow[offset + 2] = (byte)green;
						outputRow[offset + 3] = (byte)blue;
					}
				}
			}
		}
		return true;
	}

	/// <summary>
	/// Builds the texture in a 32-bit A8R8G8B8 layout (little-endian bytes <c>[B, G, R, A]</c>) with alpha 0, as the original's cache stores it
	/// ([BIN:TPW-EXE:0x005725BC cache pixel reversal] reverses the four bytes of every pixel). Rows stay bottom-up. A renderer must set alpha to 255.
	/// </summary>
	public bool BuildArgb( int x, int z, Span<byte> destination )
	{
		var built = Build( x, z, destination );
		var texture = destination[..TextureBytes];
		for ( var index = 0; index < texture.Length; index += BytesPerPixel )
		{
			(texture[index], texture[index + 3]) = (texture[index + 3], texture[index]);
			(texture[index + 1], texture[index + 2]) = (texture[index + 2], texture[index + 1]);
		}
		return built;
	}

	/// <summary>
	/// The base texel an output texel of the composite reads (tp.exe <c>0x55F780</c>, tail from <c>0x5608B0</c>): the cell word's bits <c>0x78</c> (<paramref name="word"/> &amp; 0x78) pick one of eight
	/// readings of the N × N base buffer, tested in the order 0x40, 0x08, 0x10, 0x20. Returns the row and column of the base pixel for the output pixel (<paramref name="row"/>, <paramref name="column"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x005608B0 composite orientation] the pointer start, texel step and row step per orientation: 0x00 (0, +1, +N), 0x08 (N²−N, −N, +1), 0x10 (N²−1, −1, −N), 0x20 (N−1, +N, −1), 0x40 (0, +N, +1), 0x48 (N²−N, +1, −N), 0x50 (N²−1, −N, −1), 0x60 (N−1, −1, +N) pixels, tested 0x40, then 0x08, 0x10, 0x20 (a word with 0x40 and none of the others reads as 0x40; a word with 0x08 and 0x10 reads as 0x08)
	public static (int Row, int Column) BaseTexel( int word, int size, int row, int column )
	{
		int start, texelStep, rowStep;
		if ( (word & 0x40) == 0 )
		{
			if ( (word & 0x08) != 0 )
				(start, texelStep, rowStep) = (size * size - size, -size, 1);
			else if ( (word & 0x10) != 0 )
				(start, texelStep, rowStep) = (size * size - 1, -1, -size);
			else if ( (word & 0x20) != 0 )
				(start, texelStep, rowStep) = (size - 1, size, -1);
			else
				(start, texelStep, rowStep) = (0, 1, size);
		}
		else if ( (word & 0x08) != 0 )
			(start, texelStep, rowStep) = (size * size - size, 1, -size);
		else if ( (word & 0x10) != 0 )
			(start, texelStep, rowStep) = (size * size - 1, -size, -1);
		else if ( (word & 0x20) != 0 )
			(start, texelStep, rowStep) = (size - 1, -1, size);
		else
			(start, texelStep, rowStep) = (0, size, 1);
		var index = start + row * rowStep + column * texelStep;
		return (index / size, index % size);
	}

	/// <summary>
	/// The floor art blended over a cell's procedural base (tp.exe <c>0x55F780</c> with a floor descriptor, the tail from <c>0x5608B0</c>): the base (<paramref name="baseRgba"/>, the N × N pixels
	/// <see cref="BuildRgba"/> makes, rows bottom-up) is read in the orientation of the cell word's bits 0x78 (<see cref="BaseTexel"/>), the art (<paramref name="art"/>: RGBA bytes, rows in the order
	/// the art buffer of <c>0x590DA0</c> has them) is point sampled at row <c>Y · (height / N)</c> and column <c>X · (width / N)</c> (integer divisions), and each channel is
	/// <c>base + (((art - base) · alpha) &gt;&gt; 8)</c> truncated to a byte, so an opaque art pixel keeps 1/256 of the base. The output is RGBA with alpha 255 (the original writes <c>[0, R, G, B]</c> and its
	/// texture store sets alpha 0 for a state that ignores it).
	/// </summary>
	// [BIN:TPW-EXE:0x00560926 composite blend] 32-bit art (descriptor bytes per pixel 4): alpha, red, green and blue are bytes 3, 2, 1 and 0 of the art dword; the blend is (art - base) · alpha >> 8 added to the base and truncated to a byte; the output pixel is [0, R, G, B]; the art row pointer advances by (height / N) · width pixels per output row and its column by width / N per output pixel
	public static void Compose( ReadOnlySpan<byte> baseRgba, int size, ReadOnlySpan<byte> art, int artWidth, int artHeight, int word, Span<byte> destination )
	{
		if ( baseRgba.Length < size * size * BytesPerPixel || destination.Length < size * size * BytesPerPixel )
			throw new ArgumentException( "The base and the destination hold N × N pixels." );
		if ( art.Length < artWidth * artHeight * BytesPerPixel )
			throw new ArgumentException( "The art holds width × height pixels.", nameof( art ) );
		var columnStep = artWidth / size;
		var rowStep = artHeight / size;
		for ( var row = 0; row < size; row++ )
		{
			for ( var column = 0; column < size; column++ )
			{
				var (baseRow, baseColumn) = BaseTexel( word, size, row, column );
				var source = (baseRow * size + baseColumn) * BytesPerPixel;
				var pixel = ((row * rowStep) * artWidth + column * columnStep) * BytesPerPixel;
				int alpha = art[pixel + 3];
				var target = (row * size + column) * BytesPerPixel;
				for ( var channel = 0; channel < 3; channel++ )
				{
					int baseValue = baseRgba[source + channel];
					int artValue = art[pixel + channel];
					destination[target + channel] = (byte)(baseValue + (((artValue - baseValue) * alpha) >> 8));
				}
				destination[target + 3] = 255;
			}
		}
	}

	private static class Layer
	{
		public const int C = 0, B = 1, A = 2, BA = 3, CB = 4, CA = 5, CBA = 6;
	}

	/// <summary>The byte of a one byte map at texel X of the current texel row of the cell's window.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private byte Map( int image, int texelX ) => imageData[image][rowBase[image] + texelX];

	/// <summary>[BIN:TPW-EXE:0x0055EE10 two layer blend] <c>c1 + (((c0 - c1) * ramp[(detail * w1) >> 8]) >> 8)</c> per channel with an arithmetic shift, truncated to a byte.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private void Layered( int colour0, int colour1, int detail, byte[] ramp, int texelX, int w1, out int red, out int green, out int blue )
	{
		var t = ramp[(Map( detail, texelX ) * w1) >> 8];
		var offset0 = rowBase[colour0] + texelX * BytesPerPixel;
		var offset1 = rowBase[colour1] + texelX * BytesPerPixel;
		var pixels0 = imageData[colour0];
		var pixels1 = imageData[colour1];
		red = Lerp( pixels0[offset0 + 1], pixels1[offset1 + 1], t );
		green = Lerp( pixels0[offset0 + 2], pixels1[offset1 + 2], t );
		blue = Lerp( pixels0[offset0 + 3], pixels1[offset1 + 3], t );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static int Lerp( int first, int second, int t ) => (byte)(second + (((first - second) * t) >> 8));

	// [BIN:TPW-EXE:0x0055E780 weight plane] 16.16 forward differencing over the 4 × 4 sub blocks of n = N / 4 texels (s = log2 n):
	// the corners v00, v10 (right), v01 (below) and v11 (diagonal) of the sample byte k give start = v00 << 16, xs = (v10 - v00) << (16 - s),
	// rs = (v01 - v00) << (16 - s), dd = ((v11 - v01) << (16 - s)) - xs; per row the texel is (acc >> 16) & 0xFF with acc += xs, then start += rs and xs += dd >> s (arithmetic).
	private void BuildWeights( ReadOnlySpan<byte> grid, int gridWidth, int cellX, int cellRow, int channel, byte[] plane )
	{
		var block = size >> 2;
		var shift = sizeShift - 2;
		var rowStride = gridWidth * LndFile.GridBytesPerSample;
		for ( var blockRow = 0; blockRow < 4; blockRow++ )
		{
			for ( var blockColumn = 0; blockColumn < 4; blockColumn++ )
			{
				var sample = ((4 * cellRow + blockRow) * gridWidth + 4 * cellX + blockColumn) * LndFile.GridBytesPerSample + channel;
				int v00 = grid[sample], v10 = grid[sample + 4], v01 = grid[sample + rowStride], v11 = grid[sample + rowStride + 4];
				var start = v00 << 16;
				var step = (v10 - v00) << (16 - shift);
				var rowStep = (v01 - v00) << (16 - shift);
				var delta = ((v11 - v01) << (16 - shift)) - step;
				for ( var row = 0; row < block; row++ )
				{
					var accumulator = start;
					var offset = (blockRow * block + row) * size + blockColumn * block;
					for ( var column = 0; column < block; column++ )
					{
						plane[offset + column] = (byte)(accumulator >> 16);
						accumulator += step;
					}
					start += rowStep;
					step += delta >> shift;
				}
			}
		}
	}
}
