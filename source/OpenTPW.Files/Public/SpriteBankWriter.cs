using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>Writes the sprite bank (<c>.FPC</c>/<c>.TPC</c>) and animation table (<c>.ESP</c>) files read by <see cref="SpriteBankFile"/> and <see cref="SpriteAnimationFile"/>.</summary>
public static class SpriteBankWriter
{
	/// <summary>Longest repeat or literal a control byte can express (a signed byte: repeats −1…−128 would fit, the game's files stop at 127).</summary>
	public const int MaximumRun = 127;
	/// <summary>The row length prefix is one byte, so an encoded row is at most 255 bytes long.</summary>
	public const int MaximumEncodedRow = 255;
	/// <summary>Shortest run of equal indices written as a repeat. Every shipped bank encodes runs of two as repeats; 3 produces files 3 to 4 % smaller than the originals, so 2 is what the corpus roundtrip proves.</summary>
	public const int MinimumRepeat = 2;

	/// <summary>Writes a bank byte for byte as it was read, as far as the reader's fields go (see the roundtrip corpus test).</summary>
	public static byte[] Write( SpriteBankFile bank ) => Write( bank.Palette, bank.Frames, bank.HeaderWord );

	public static byte[] Write( byte[] palette, IReadOnlyList<SpriteFrame> frames, uint headerWord = 0 )
	{
		if ( palette.Length != SpriteBankFile.PaletteEntries * 4 )
			throw new ArgumentException( $"The palette must have {SpriteBankFile.PaletteEntries} BGRA entries.", nameof( palette ) );
		if ( frames.Count > SpriteBankFile.MaximumFrames )
			throw new ArgumentException( $"A bank holds at most {SpriteBankFile.MaximumFrames} frames.", nameof( frames ) );
		using var memory = new MemoryStream();
		Span<byte> header = stackalloc byte[SpriteBankFile.HeaderBytes];
		BinaryPrimitives.WriteUInt16LittleEndian( header, 3 );
		BinaryPrimitives.WriteUInt16LittleEndian( header[2..], 3 );
		BinaryPrimitives.WriteInt32LittleEndian( header[4..], frames.Count );
		BinaryPrimitives.WriteUInt32LittleEndian( header[8..], headerWord );
		memory.Write( header );
		memory.Write( palette );
		Span<byte> frameHeader = stackalloc byte[SpriteBankFile.FrameHeaderBytes];
		foreach ( var frame in frames )
		{
			if ( frame.Width > SpriteBankFile.MaximumDimension || frame.Height > SpriteBankFile.MaximumDimension )
				throw new ArgumentException( $"Sprite frames are at most {SpriteBankFile.MaximumDimension} pixels wide and high." );
			if ( frame.Indices.Length != frame.Width * frame.Height )
				throw new ArgumentException( "A frame's indices must be width x height." );
			var rows = EncodeRows( frame.Indices, frame.Width, frame.Height );
			BinaryPrimitives.WriteInt32LittleEndian( frameHeader, rows.Length );
			BinaryPrimitives.WriteUInt16LittleEndian( frameHeader[4..], (ushort)frame.Width );
			BinaryPrimitives.WriteUInt16LittleEndian( frameHeader[6..], (ushort)frame.Height );
			BinaryPrimitives.WriteUInt16LittleEndian( frameHeader[8..], frame.UnknownA );
			BinaryPrimitives.WriteUInt16LittleEndian( frameHeader[10..], frame.UnknownB );
			BinaryPrimitives.WriteInt32LittleEndian( frameHeader[12..], frame.OriginX );
			BinaryPrimitives.WriteInt32LittleEndian( frameHeader[16..], frame.OriginY );
			memory.Write( frameHeader );
			memory.Write( rows );
		}
		return memory.ToArray();
	}

	/// <summary>RLE rows: per row a length byte, then repeats (−n, value) and literals (n, bytes...).</summary>
	public static byte[] EncodeRows( byte[] indices, int width, int height )
	{
		var output = new List<byte>( indices.Length );
		var row = new List<byte>( width + 8 );
		for ( var y = 0; y < height; y++ )
		{
			row.Clear();
			var span = indices.AsMemory( y * width, width );
			var position = 0;
			var literalStart = 0;
			void FlushLiteral( int end )
			{
				while ( literalStart < end )
				{
					var count = Math.Min( MaximumRun, end - literalStart );
					row.Add( (byte)count );
					for ( var i = 0; i < count; i++ )
						row.Add( span.Span[literalStart + i] );
					literalStart += count;
				}
			}
			while ( position < width )
			{
				var run = 1;
				while ( position + run < width && span.Span[position + run] == span.Span[position] && run < MaximumRun )
					run++;
				if ( run >= MinimumRepeat )
				{
					FlushLiteral( position );
					row.Add( unchecked( (byte)(-run) ) );
					row.Add( span.Span[position] );
					position += run;
					literalStart = position;
				}
				else
					position += run;
			}
			FlushLiteral( width );
			if ( row.Count > MaximumEncodedRow )
				throw new InvalidDataException( $"Sprite row {y} encodes to {row.Count} bytes; a row holds at most {MaximumEncodedRow}." );
			output.Add( (byte)row.Count );
			output.AddRange( row );
		}
		return output.ToArray();
	}
}
