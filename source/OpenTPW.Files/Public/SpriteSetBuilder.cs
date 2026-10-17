namespace OpenTPW;

/// <summary>One authored frame: straight (non-premultiplied) RGBA8 pixels, row-major, and its hotspot offsets as in <see cref="SpriteFrame"/>.</summary>
public sealed record SpriteRgbaFrame( int Width, int Height, byte[] Rgba, int OriginX, int OriginY )
{
	/// <summary>Pixels per sprite unit, written to both header words; every shipped frame has 128.</summary>
	public ushort PixelsPerUnit { get; init; } = 128;
}

/// <summary>The files of one authored sprite set, by upper-case extension (<c>.ESP</c> plus the bank or banks the table selects).</summary>
public sealed class SpriteSet
{
	public SpriteSet( string name, IReadOnlyDictionary<string, byte[]> files )
	{
		Name = name;
		Files = files;
	}

	/// <summary>The set's stem, e.g. <c>SPR_ZZ</c>.</summary>
	public string Name { get; }
	/// <summary>File bytes by extension: <c>.ESP</c> and <c>.TPC</c>, plus <c>.FPC</c> for a full-detail set.</summary>
	public IReadOnlyDictionary<string, byte[]> Files { get; }

	/// <summary>Writes <c>NAME.ESP</c> and the banks into a folder (or a mod's sprite folder).</summary>
	public void WriteTo( string directory )
	{
		Directory.CreateDirectory( directory );
		foreach ( var (extension, bytes) in Files )
			File.WriteAllBytes( Path.Combine( directory, Name + extension ), bytes );
	}
}

/// <summary>
/// Authors a sprite set (<c>.ESP</c> animation table plus <c>.FPC</c>/<c>.TPC</c> pixel bank) from RGBA frames, in the format
/// <see cref="GuestSpriteAtlas"/> and the other sprite readers load. Animations are added slot by slot; each slot stores its frames
/// direction-major (<c>directions x framesPerDirection</c>), exactly as the shipped kid sets do (docs/GUESTS.md).
/// <para>
/// Pixel format: the bank is paletted. One 255-entry BGRA palette is shared by the whole set; pixels with alpha 0 become the transparent index 0,
/// every other colour (alpha kept) maps to an entry. Sets with at most 255 distinct translucent-or-opaque colours are stored exactly; more are
/// reduced by median cut over the RGBA cube (axis of widest range, split at the median pixel, entry = rounded mean), then each pixel takes the nearest entry (squared RGBA distance).
/// Colours are not premultiplied (the readers decode to straight RGBA, <see cref="SpriteBankFile.DecodeRgba"/>).
/// </para>
/// </summary>
public sealed class SpriteSetBuilder
{
	private readonly List<SpriteRgbaFrame> frames = new();
	private readonly SpriteAnimation[] slots = new SpriteAnimation[SpriteAnimationFile.SlotCount];
	private readonly byte[] performanceBytes = new byte[16];
	private int performanceCount;
	private long pixels;

	/// <summary>Widest frame <see cref="GuestSpriteAtlas"/> can place: its 1024-pixel atlas rows minus the 2-pixel padding on both sides.</summary>
	public const int MaximumFrameWidth = 1020;
	/// <summary>Total pixels of all frames in a set (bounds the palette histogram and the quantiser's work).</summary>
	public const long MaximumPixels = 1L << 28;

	public SpriteSetBuilder( string name )
	{
		if ( name.Length is 0 or > 7 || name.Any( c => !(char.IsAsciiLetterOrDigit( c ) || c == '_') ) )
			throw new ArgumentException( "A set name is 1 to 7 letters, digits or underscores (the shipped sets are SPR_xx).", nameof( name ) );
		Name = name.ToUpperInvariant();
		for ( var slot = 0; slot < slots.Length; slot++ )
			slots[slot] = new SpriteAnimation( slot, 0, 0, 0 );
	}

	public string Name { get; }

	/// <summary>
	/// ESP byte 0x10C: the game opens the <c>.FPC</c> bank (else the <c>.TPC</c>). With true the builder writes both files with the same content, as every
	/// shipped full-detail set ships both. Default true: the PC game draws the FPC of such sets and the TPC of the rest. Unverified: choosing false makes the
	/// game read only the TPC; a set the game must draw needs the file it opens.
	/// </summary>
	public bool FullDetail { get; set; } = true;

	/// <summary>ESP byte 0x10D: 1 for the kid sets that look like girls (CH, KI, SA, SU). Stored by the loader and read nowhere in tp.exe.</summary>
	public byte KidFlag { get; set; }

	/// <summary>Number of frames added so far.</summary>
	public int FrameCount => frames.Count;

	/// <summary>
	/// Adds an animation slot. <paramref name="directions"/> lists, per direction, the frames of one loop (all the same length); pass a single list with
	/// <paramref name="directional"/> false for a slot without directions (the table then stores 0 directions and one frame per step).
	/// </summary>
	public SpriteSetBuilder AddAnimation( int slot, IReadOnlyList<IReadOnlyList<SpriteRgbaFrame>> directions, bool directional = true )
	{
		if ( slot < 0 || slot >= SpriteAnimationFile.SlotCount )
			throw new ArgumentOutOfRangeException( nameof( slot ) );
		if ( !slots[slot].IsEmpty || slots[slot].Directions != 0 )
			throw new InvalidOperationException( $"Slot {slot} is already set." );
		if ( directions.Count is 0 or > 255 || (!directional && directions.Count != 1) )
			throw new ArgumentException( "A slot needs 1 to 255 directions (exactly one when not directional).", nameof( directions ) );
		var perDirection = directions[0].Count;
		if ( perDirection is 0 or > 255 || directions.Any( x => x.Count != perDirection ) )
			throw new ArgumentException( "Every direction needs the same 1 to 255 frames.", nameof( directions ) );
		var first = frames.Count;
		if ( first + directions.Count * perDirection > SpriteBankFile.MaximumFrames )
			throw new ArgumentException( $"A bank holds at most {SpriteBankFile.MaximumFrames} frames." );
		var added = new List<SpriteRgbaFrame>( directions.Count * perDirection );
		long addedPixels = 0;
		foreach ( var direction in directions )
			foreach ( var frame in direction )
			{
				if ( frame.Width is <= 0 or > MaximumFrameWidth || frame.Height is <= 0 or > SpriteBankFile.MaximumDimension )
					throw new ArgumentException( $"Frames are 1 to {MaximumFrameWidth} pixels wide and 1 to {SpriteBankFile.MaximumDimension} high." );
				if ( frame.Rgba.Length != frame.Width * frame.Height * 4 )
					throw new ArgumentException( "A frame's RGBA data must be width x height x 4 bytes." );
				addedPixels += (long)frame.Width * frame.Height;
				added.Add( frame );
			}
		if ( pixels + addedPixels > MaximumPixels )
			throw new ArgumentException( $"A set holds at most {MaximumPixels} pixels in total." );
		frames.AddRange( added );
		pixels += addedPixels;
		slots[slot] = new SpriteAnimation( slot, first, perDirection, directional ? directions.Count : 0 );
		return this;
	}

	/// <summary>
	/// Sets an entertainer performance entry (<see cref="SpriteAnimationFile.Performances"/>): program, slot, and the program's two registers.
	/// The shipped entertainers all use program 0 on slot 4 with registers 0 and 0 (Hallow SPR_DR: 2 and 40). Entries must be set in order from 0, on a slot added before.
	/// </summary>
	public SpriteSetBuilder SetPerformance( int entry, int program, int slot, int intro = 0, int hold = 0 )
	{
		if ( entry != performanceCount || entry > 3 )
			throw new ArgumentException( $"Performance entries are set in order from 0 (next is {performanceCount}, at most 3).", nameof( entry ) );
		if ( slot < 0 || slot >= SpriteAnimationFile.SlotCount || slots[slot].IsEmpty )
			throw new ArgumentException( "A performance plays an animation slot (0 to 15) that has been added.", nameof( slot ) );
		if ( program is < 0 or > 254 || intro is < 0 or > 255 || hold is < 0 or > 255 )
			throw new ArgumentOutOfRangeException( nameof( program ), "Program and slot are stored plus one in a byte." );
		performanceCount++;
		performanceBytes[entry * 4] = (byte)(program + 1);
		performanceBytes[entry * 4 + 1] = (byte)(slot + 1);
		performanceBytes[entry * 4 + 2] = (byte)intro;
		performanceBytes[entry * 4 + 3] = (byte)hold;
		return this;
	}

	/// <summary>Quantises the frames, encodes the bank and the table, and returns the files.</summary>
	public SpriteSet Build()
	{
		if ( frames.Count == 0 )
			throw new InvalidOperationException( "A set needs at least one frame." );
		var (palette, bankFrames) = Quantise( frames );
		var bank = SpriteBankWriter.Write( palette, bankFrames );
		var esp = SpriteAnimationWriter.Write( Name + ".TPS", (byte)(FullDetail ? 1 : 0), KidFlag, slots, performanceBytes );
		var files = new Dictionary<string, byte[]> { [".ESP"] = esp, [".TPC"] = bank };
		if ( FullDetail )
			files[".FPC"] = bank;
		return new SpriteSet( Name, files );
	}

	/// <summary>Builds the shared 255-entry BGRA palette and the paletted frames (index 0 transparent).</summary>
	internal static (byte[] Palette, SpriteFrame[] Frames) Quantise( IReadOnlyList<SpriteRgbaFrame> source )
	{
		var histogram = new Dictionary<uint, long>();
		foreach ( var frame in source )
			for ( var i = 0; i < frame.Rgba.Length; i += 4 )
			{
				if ( frame.Rgba[i + 3] == 0 )
					continue;
				var key = Pack( frame.Rgba[i], frame.Rgba[i + 1], frame.Rgba[i + 2], frame.Rgba[i + 3] );
				histogram[key] = checked( histogram.GetValueOrDefault( key ) + 1 );
			}

		var colours = histogram.Count <= SpriteBankFile.PaletteEntries
			? histogram.Keys.OrderBy( x => x ).ToList()
			: MedianCut( histogram, SpriteBankFile.PaletteEntries );
		var palette = new byte[SpriteBankFile.PaletteEntries * 4];
		for ( var entry = 0; entry < colours.Count; entry++ )
		{
			var colour = colours[entry];
			palette[entry * 4] = (byte)(colour >> 8);
			palette[entry * 4 + 1] = (byte)(colour >> 16);
			palette[entry * 4 + 2] = (byte)(colour >> 24);
			palette[entry * 4 + 3] = (byte)colour;
		}

		var lookup = new Dictionary<uint, byte>();
		var frames = new SpriteFrame[source.Count];
		for ( var index = 0; index < source.Count; index++ )
		{
			var frame = source[index];
			var indices = new byte[frame.Width * frame.Height];
			for ( var pixel = 0; pixel < indices.Length; pixel++ )
			{
				var i = pixel * 4;
				if ( frame.Rgba[i + 3] == 0 )
					continue;
				var key = Pack( frame.Rgba[i], frame.Rgba[i + 1], frame.Rgba[i + 2], frame.Rgba[i + 3] );
				if ( !lookup.TryGetValue( key, out var value ) )
					lookup[key] = value = Nearest( colours, key );
				indices[pixel] = value;
			}
			frames[index] = new SpriteFrame( frame.Width, frame.Height, frame.OriginX, frame.OriginY, frame.PixelsPerUnit, frame.PixelsPerUnit, indices );
		}
		return (palette, frames);
	}

	private static uint Pack( byte r, byte g, byte b, byte a ) => (uint)r << 24 | (uint)g << 16 | (uint)b << 8 | a;

	private static byte Nearest( List<uint> colours, uint key )
	{
		var best = 0;
		var bestDistance = long.MaxValue;
		for ( var entry = 0; entry < colours.Count; entry++ )
		{
			var distance = 0L;
			for ( var shift = 0; shift < 32; shift += 8 )
			{
				var delta = (int)((key >> shift) & 255) - (int)((colours[entry] >> shift) & 255);
				distance += delta * delta;
			}
			if ( distance < bestDistance )
			{
				bestDistance = distance;
				best = entry;
			}
		}
		return (byte)(best + 1);
	}

	/// <summary>Median cut over distinct colours weighted by pixel count: split the box with the most pixels along its widest channel at the weighted median.</summary>
	private static List<uint> MedianCut( Dictionary<uint, long> histogram, int target )
	{
		var boxes = new List<List<KeyValuePair<uint, long>>> { histogram.OrderBy( x => x.Key ).ToList() };
		while ( boxes.Count < target )
		{
			var index = -1;
			long most = 0;
			for ( var i = 0; i < boxes.Count; i++ )
			{
				var weight = boxes[i].Count < 2 ? 0 : boxes[i].Sum( x => x.Value );
				if ( weight > most )
				{
					most = weight;
					index = i;
				}
			}
			if ( index < 0 )
				break;
			var box = boxes[index];
			var channel = 0;
			var widest = -1;
			for ( var c = 0; c < 4; c++ )
			{
				var shift = c * 8;
				var low = box.Min( x => (int)((x.Key >> shift) & 255) );
				var high = box.Max( x => (int)((x.Key >> shift) & 255) );
				if ( high - low > widest )
				{
					widest = high - low;
					channel = shift;
				}
			}
			var sorted = box.OrderBy( x => (x.Key >> channel) & 255 ).ThenBy( x => x.Key ).ToList();
			var half = most / 2;
			long run = 0;
			var cut = 1;
			for ( var i = 0; i < sorted.Count - 1; i++ )
			{
				run += sorted[i].Value;
				cut = i + 1;
				if ( run >= half )
					break;
			}
			boxes[index] = sorted.GetRange( 0, cut );
			boxes.Add( sorted.GetRange( cut, sorted.Count - cut ) );
		}
		var result = new List<uint>();
		foreach ( var box in boxes )
		{
			var total = box.Sum( x => x.Value );
			uint packed = 0;
			for ( var shift = 0; shift < 32; shift += 8 )
			{
				var sum = box.Sum( x => ((x.Key >> shift) & 255) * x.Value );
				packed |= (uint)((sum + total / 2) / total) << shift;
			}
			result.Add( packed );
		}
		return result.Distinct().OrderBy( x => x ).ToList();
	}
}
