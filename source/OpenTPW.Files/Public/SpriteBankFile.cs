using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>One decoded sprite frame: 8-bit palette indices (0 = transparent) and its hotspot.</summary>
public sealed class SpriteFrame
{
	public SpriteFrame( int width, int height, int originX, int originY, ushort unknownA, ushort unknownB, byte[] indices )
	{
		Width = width;
		Height = height;
		OriginX = originX;
		OriginY = originY;
		UnknownA = unknownA;
		UnknownB = unknownB;
		Indices = indices;
	}

	public int Width { get; }
	public int Height { get; }
	/// <summary>Signed offset of the frame's left edge from the hotspot (negative: the hotspot lies inside the frame).</summary>
	public int OriginX { get; }
	/// <summary>Signed offset of the frame's top edge from the hotspot; kid frames put the hotspot at the feet.</summary>
	public int OriginY { get; }
	/// <summary>Two u16 header words, 128/128 in every kid frame; meaning unknown.</summary>
	public ushort UnknownA { get; }
	public ushort UnknownB { get; }
	/// <summary>Row-major palette indices; 0 is transparent, n ≥ 1 is palette entry n − 1.</summary>
	public byte[] Indices { get; }
}

/// <summary>
/// Paletted RLE sprite bank (<c>.FPC</c>/<c>.TPC</c> inside <c>esprites.wad</c>), see docs/GUESTS.md.
/// Layout (little-endian): u16 3, u16 3, u32 frame count, u32 0, 255 BGRA palette entries, then per frame
/// u32 data size, u16 width, u16 height, u16 ×2 (unknown), i32 origin X, i32 origin Y and the RLE rows.
/// Each row is a byte length followed by control bytes: c &lt; 0 repeats the next byte −c times, c &gt; 0
/// copies c literal bytes.
/// </summary>
public sealed class SpriteBankFile : BaseFormat
{
	public const int PaletteEntries = 255;
	public const int HeaderBytes = 12;
	public const int FrameHeaderBytes = 20;
	public const int MaximumFrames = 4096;
	public const int MaximumDimension = 1024;

	/// <summary>BGRA palette as stored (alpha varies: anti-aliased edges).</summary>
	public byte[] Palette { get; private set; } = Array.Empty<byte>();
	public IReadOnlyList<SpriteFrame> Frames { get; private set; } = Array.Empty<SpriteFrame>();

	public SpriteBankFile( string path ) => ReadFromFile( path );
	public SpriteBankFile( Stream stream ) => ReadFromStream( stream );

	/// <summary>Writes the frame as RGBA (palette alpha kept; index 0 transparent).</summary>
	public void DecodeRgba( SpriteFrame frame, Span<byte> destination, int destinationStride, int x, int y )
	{
		for ( var row = 0; row < frame.Height; row++ )
		{
			for ( var column = 0; column < frame.Width; column++ )
			{
				var index = frame.Indices[row * frame.Width + column];
				if ( index == 0 )
					continue;
				var entry = (index - 1) * 4;
				var target = (y + row) * destinationStride + (x + column) * 4;
				destination[target] = Palette[entry + 2];
				destination[target + 1] = Palette[entry + 1];
				destination[target + 2] = Palette[entry];
				destination[target + 3] = Palette[entry + 3];
			}
		}
	}

	protected override void ReadFromStream( Stream stream )
	{
		using var memory = new MemoryStream();
		stream.CopyTo( memory );
		var data = memory.GetBuffer().AsSpan( 0, (int)memory.Length );
		if ( data.Length < HeaderBytes + PaletteEntries * 4 )
			throw new InvalidDataException( "Sprite bank is shorter than its header and palette." );
		if ( BinaryPrimitives.ReadUInt16LittleEndian( data ) != 3 || BinaryPrimitives.ReadUInt16LittleEndian( data[2..] ) != 3 )
			throw new InvalidDataException( "Sprite bank does not start with the observed 03 00 03 00 signature." );
		var count = BinaryPrimitives.ReadInt32LittleEndian( data[4..] );
		if ( count < 0 || count > MaximumFrames )
			throw new InvalidDataException( $"Sprite bank frame count {count} is out of range." );
		Palette = data.Slice( HeaderBytes, PaletteEntries * 4 ).ToArray();

		var frames = new SpriteFrame[count];
		var offset = HeaderBytes + PaletteEntries * 4;
		for ( var index = 0; index < count; index++ )
		{
			if ( data.Length - offset < FrameHeaderBytes )
				throw new InvalidDataException( $"Sprite frame {index} header is truncated." );
			var size = BinaryPrimitives.ReadInt32LittleEndian( data[offset..] );
			var width = BinaryPrimitives.ReadUInt16LittleEndian( data[(offset + 4)..] );
			var height = BinaryPrimitives.ReadUInt16LittleEndian( data[(offset + 6)..] );
			var unknownA = BinaryPrimitives.ReadUInt16LittleEndian( data[(offset + 8)..] );
			var unknownB = BinaryPrimitives.ReadUInt16LittleEndian( data[(offset + 10)..] );
			var originX = BinaryPrimitives.ReadInt32LittleEndian( data[(offset + 12)..] );
			var originY = BinaryPrimitives.ReadInt32LittleEndian( data[(offset + 16)..] );
			offset += FrameHeaderBytes;
			if ( size < 0 || size > data.Length - offset )
				throw new InvalidDataException( $"Sprite frame {index} data is truncated." );
			if ( width > MaximumDimension || height > MaximumDimension )
				throw new InvalidDataException( $"Sprite frame {index} is {width}x{height}, larger than {MaximumDimension}." );
			frames[index] = new SpriteFrame( width, height, originX, originY, unknownA, unknownB, DecodeRows( data.Slice( offset, size ), width, height, index ) );
			offset += size;
		}
		if ( offset != data.Length )
			throw new InvalidDataException( $"Sprite bank has {data.Length - offset} trailing bytes." );
		Frames = frames;
	}

	private static byte[] DecodeRows( ReadOnlySpan<byte> data, int width, int height, int frame )
	{
		var pixels = new byte[width * height];
		var position = 0;
		for ( var row = 0; row < height; row++ )
		{
			if ( position >= data.Length )
				throw new InvalidDataException( $"Sprite frame {frame} ends before row {row}." );
			var rowEnd = position + 1 + data[position];
			position++;
			if ( rowEnd > data.Length )
				throw new InvalidDataException( $"Sprite frame {frame} row {row} is truncated." );
			var column = 0;
			while ( position < rowEnd )
			{
				var control = (sbyte)data[position++];
				if ( control < 0 )
				{
					if ( position >= rowEnd || column - control > width )
						throw new InvalidDataException( $"Sprite frame {frame} row {row} run overflows." );
					pixels.AsSpan( row * width + column, -control ).Fill( data[position++] );
					column -= control;
				}
				else
				{
					if ( position + control > rowEnd || column + control > width )
						throw new InvalidDataException( $"Sprite frame {frame} row {row} literal overflows." );
					data.Slice( position, control ).CopyTo( pixels.AsSpan( row * width + column ) );
					position += control;
					column += control;
				}
			}
			if ( column != width )
				throw new InvalidDataException( $"Sprite frame {frame} row {row} decodes to {column} pixels, not {width}." );
		}
		if ( position != data.Length )
			throw new InvalidDataException( $"Sprite frame {frame} has {data.Length - position} bytes after its last row." );
		return pixels;
	}
}

/// <summary>One animation slot of an <see cref="SpriteAnimationFile"/>: frames are direction-major.</summary>
public readonly record struct SpriteAnimation( int Slot, int FirstFrame, int FramesPerDirection, int Directions )
{
	public bool IsEmpty => FramesPerDirection == 0;
	/// <summary>Frames stored for this slot; slots with 0 directions store one frame per step.</summary>
	public int FrameCount => FramesPerDirection * Math.Max( 1, Directions );
	public int GetFrame( int direction, int step ) => FirstFrame + Math.Clamp( direction, 0, Math.Max( 1, Directions ) - 1 ) * FramesPerDirection + step % FramesPerDirection;
}

/// <summary>
/// Sprite animation table (<c>.ESP</c>, 350 bytes): magic <c>ESP_FILE2.00</c>, a NUL-terminated bank
/// name (<c>SPR_BE.TPS</c>), zero padding, two flag bytes at 0x10C, then from offset 0x10E twenty 4-byte slots
/// (u16 first frame, u8 frames per direction, u8 directions). See docs/GUESTS.md.
/// </summary>
public sealed class SpriteAnimationFile : BaseFormat
{
	public const string Magic = "ESP_FILE2.00";
	public const int FileBytes = 350;
	public const int SlotOffset = 0x10E;
	public const int SlotCount = 20;

	public string BankName { get; private set; } = "";
	/// <summary>Byte 0x10C: 1 for every walking character (kids, staff), 0 for heads, thoughts, particles. Meaning unknown.</summary>
	public byte FlagA { get; private set; }
	/// <summary>Byte 0x10D: 1 for kid sets CH, KI, SA, SU and their heads (girl sprites by visual inspection). Meaning unknown.</summary>
	public byte FlagB { get; private set; }
	public IReadOnlyList<SpriteAnimation> Animations { get; private set; } = Array.Empty<SpriteAnimation>();

	public SpriteAnimationFile( string path ) => ReadFromFile( path );
	public SpriteAnimationFile( Stream stream ) => ReadFromStream( stream );

	protected override void ReadFromStream( Stream stream )
	{
		using var memory = new MemoryStream();
		stream.CopyTo( memory );
		var data = memory.ToArray();
		if ( data.Length != FileBytes )
			throw new InvalidDataException( $"ESP files are {FileBytes} bytes; this one is {data.Length}." );
		if ( System.Text.Encoding.ASCII.GetString( data, 0, Magic.Length ) != Magic )
			throw new InvalidDataException( "ESP magic does not match ESP_FILE2.00." );
		var nameEnd = Array.IndexOf( data, (byte)0, Magic.Length );
		BankName = System.Text.Encoding.ASCII.GetString( data, Magic.Length, (nameEnd < 0 ? SlotOffset : nameEnd) - Magic.Length );
		FlagA = data[SlotOffset - 2];
		FlagB = data[SlotOffset - 1];
		var slots = new SpriteAnimation[SlotCount];
		for ( var slot = 0; slot < SlotCount; slot++ )
		{
			var offset = SlotOffset + slot * 4;
			slots[slot] = new SpriteAnimation( slot, BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( offset ) ), data[offset + 2], data[offset + 3] );
		}
		Animations = slots;
	}
}
