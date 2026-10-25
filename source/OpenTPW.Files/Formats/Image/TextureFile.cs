using System.Buffers.Binary;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;

namespace OpenTPW;

public record struct TextureData( int Width, int Height, byte[] Data );

public partial class TextureFile : BaseFormat
{
	public TextureData Data { get; set; }

	static float ComputeDequantizationScaleY( int n ) => 1.0f - ((float)n * -0.5f);
	static float ComputeDequantizationScaleCbCr( int n ) => 1.0f - ((float)n * -0.25f);
	static float ComputeDequantizationScaleA( int n ) => (float)n + 1.0f;

	public TextureFile( Stream stream )
	{
		ReadFromStream( stream );
	}

	public TextureFile( string path )
	{
		ReadFromFile( path );
	}

	protected override void ReadFromStream( Stream stream )
	{
		if ( stream == null )
		{
			Data = new TextureData( 1, 1, new byte[] { 255, 0, 255, 255 } );
			return;
		}

		using var binaryReader = new BinaryReader( stream );

		//
		// Read header
		//
		TextureFileData fileData;
		try
		{
			fileData = new TextureFileData()
			{
				Flags = (TextureFlags)binaryReader.ReadByte(),
				AlphaByte = binaryReader.ReadByte(),
				BitsPerPixel = binaryReader.ReadByte(),
				Version = binaryReader.ReadByte(),
				Width = binaryReader.ReadInt16(),
				Height = binaryReader.ReadInt16(),
				YChannelQuantizationScale = binaryReader.ReadInt16(),
				// [BIN:TPW-EXE:0x564e20 header read, 0x593ab0 decode] the header's second short (offset 0xa) scales the second chroma plane and the third (0xc) the first: the loader pushes the
				// third short as the chroma scale the decoder (0x593ab0) applies to the plane it later multiplies by 1.772 (Cb), the second short for the plane it multiplies by 1.402 (Cr).
				// Read the other way round, saturated reds decoded as yellow-greens; muted art barely changed because its two scales are close or equal.
				CrChannelQuantizationScale = binaryReader.ReadInt16(),
				CbChannelQuantizationScale = binaryReader.ReadInt16(),
				AChannelQuantizationScale = binaryReader.ReadInt16(),

				ColorChunkSize = binaryReader.ReadInt32(),
				AlphaChunkSize = binaryReader.ReadInt32(),

				Checksum = binaryReader.ReadInt32()
			};
		}
		catch ( EndOfStreamException exception )
		{
			throw new InvalidDataException( "Truncated WCT header.", exception );
		}

		AlphaType = ClassifyAlphaType( (byte)fileData.Flags, fileData.AlphaByte, fileData.BitsPerPixel );
		if ( fileData.Width <= 0 || fileData.Height <= 0 )
			throw new InvalidDataException( "WCT dimensions must be positive." );
		var size = (int)Math.Max( GetAlignedSize( (uint)Math.Max( fileData.Height, fileData.Width ) ), 8 );
		var planeLength = (long)size * size;
		var rgbaLength = (long)fileData.Width * fileData.Height * 4;
		// These are the existing decoder's array requirements, not a new texture dimension policy.
		if ( planeLength * 2 > Array.MaxLength || rgbaLength > Array.MaxLength )
			throw new InvalidDataException( "WCT dimensions exceed the decoder's supported array lengths." );
		var colorCoefficientCount = (int)(planeLength + planeLength / 2);
		var alphaCoefficientCount = (int)planeLength;

		//
		// Read blocks
		//

		// Color
		if ( fileData.ColorChunkSize <= 0 )
			throw new InvalidDataException( "Texture does not have color data" );
		if ( fileData.AlphaChunkSize < 0 || (fileData.HasAlphaChannel && fileData.AlphaChunkSize == 0) )
			throw new InvalidDataException( "Invalid WCT alpha chunk length." );

		fileData.ColorChunk = ReadChunk( binaryReader, fileData.ColorChunkSize );

		// Alpha
		if ( fileData.AlphaChunkSize > 0 )
			fileData.AlphaChunk = ReadChunk( binaryReader, fileData.AlphaChunkSize );

		//
		// Decompress blocks
		//
		var alphaData = Array.Empty<byte>();
		var colorData = Decompress( fileData.ColorChunk, checked(colorCoefficientCount * 3) );

		if ( fileData.HasAlphaChannel )
			alphaData = Decompress( fileData.AlphaChunk, checked(alphaCoefficientCount * 3) );
		if ( colorData.Length < colorCoefficientCount || (fileData.HasAlphaChannel && alphaData.Length < alphaCoefficientCount) )
			throw new InvalidDataException( "Truncated WCT channel coefficients." );
		ValidateCoefficientData( colorData, colorCoefficientCount );
		if ( fileData.HasAlphaChannel )
			ValidateCoefficientData( alphaData, alphaCoefficientCount );

		//
		// Decode image
		//
		var outputY = new float[size * size];
		var outputCb = new float[size * size];
		var outputCr = new float[size * size];
		var outputA = new float[size * size];
		Array.Fill( outputA, 255f );

		var planarRows = !fileData.Flags.HasFlag( TextureFlags.InterleavedRows );
		var state = new ImageDecodeState( size );
		DecodeChannel( ref state, size, ref colorData, outputY, ComputeDequantizationScaleY( fileData.YChannelQuantizationScale ), planarRows );
		DecodeChannel( ref state, size / 2, ref colorData, outputCb, ComputeDequantizationScaleCbCr( fileData.CbChannelQuantizationScale ), planarRows );
		DecodeChannel( ref state, size / 2, ref colorData, outputCr, ComputeDequantizationScaleCbCr( fileData.CrChannelQuantizationScale ), planarRows );

		if ( fileData.HasAlphaChannel )
			DecodeChannel( ref state, size, ref alphaData, outputA, ComputeDequantizationScaleA( fileData.AChannelQuantizationScale ), planarRows );

		// The colour conversion is the other browser cold path next to the wavelet
		// loops, so it reads the decoded planes straight into the final byte
		// array. Storing a float into the old List<float> staging buffer and
		// reading it back was value-preserving, so clamping the same float here
		// yields bit-identical bytes - and drops two full-size intermediates.
		var textureData = new byte[(int)rgbaLength];

		for ( int y = 0; y < fileData.Height; y++ )
		{
			for ( int x = 0; x < fileData.Width; x++ )
			{
				// Convert from ycbcr to rgb
				float cy = outputY[y * size + x];
				float cb = outputCb[((y / 2) * (size / 2) + (x / 2))];
				float cr = outputCr[((y / 2) * (size / 2) + (x / 2))];

				float r = cy + 1.402f * (cr);
				float g = cy - 0.344136f * (cb) - 0.714136f * (cr);
				float b = cy + 1.772f * (cb);

				// Alpha channel remains untouched..
				float a = outputA[y * size + x];

				int dst = (y * fileData.Width + x) * 4;
				textureData[dst] = (byte)r.Clamp( 0, 255 );
				textureData[dst + 1] = (byte)g.Clamp( 0, 255 );
				textureData[dst + 2] = (byte)b.Clamp( 0, 255 );
				textureData[dst + 3] = (byte)a.Clamp( 0, 255 );
			}
		}

		Data = new TextureData( fileData.Width, fileData.Height, textureData );
	}

	private static void ValidateCoefficientData( ReadOnlySpan<byte> data, int count )
	{
		// Escapes need three bytes, so the minimum one-byte count alone cannot prove the
		// channels are complete. Validate their existing encoding before allocating image planes.
		var offset = 0;
		for ( var coefficient = 0; coefficient < count; coefficient++ )
		{
			if ( offset >= data.Length )
				throw new InvalidDataException( "Truncated WCT channel coefficients." );
			if ( data[offset++] != 0x80 )
				continue;
			if ( data.Length - offset < sizeof( short ) )
				throw new InvalidDataException( "Truncated WCT coefficient escape." );
			offset += sizeof( short );
		}
	}

	private static byte[] ReadChunk( BinaryReader reader, int length )
	{
		if ( reader.BaseStream.CanSeek )
		{
			if ( length > reader.BaseStream.Length - reader.BaseStream.Position )
				throw new InvalidDataException( "Truncated WCT chunk." );
			var bytes = reader.ReadBytes( length );
			if ( bytes.Length != length ) throw new InvalidDataException( "Truncated WCT chunk." );
			return bytes;
		}
		// An untrusted count on a nonseekable stream must not cause a count-sized allocation before reading.
		using var output = new MemoryStream();
		var buffer = new byte[Math.Min( length, 81920 )];
		while ( length > 0 )
		{
			var read = reader.Read( buffer, 0, Math.Min( length, buffer.Length ) );
			if ( read == 0 ) throw new InvalidDataException( "Truncated WCT chunk." );
			output.Write( buffer, 0, read );
			length -= read;
		}
		return output.ToArray();
	}

	private static byte[] Decompress( byte[] data, int maximumCoefficientBytes )
	{
		// Check if the file contains 'BILZ' as the first 4 bytes - if so then it is a zlib
		// compressed chunk
		if ( data.Length < 2 ) throw new InvalidDataException( "Truncated WCT compression header." );
		if ( data.Length >= 4 && data[0] == 0x42 && data[1] == 0x49 && data[2] == 0x4C && data[3] == 0x5A )
			return DecompressZLIB( data, maximumCoefficientBytes );

		// Not zlib, so must be data compressed with LZSS
		return DecompressLZSS( data );
	}

	private static byte[] DecompressZLIB( byte[] data, int maximumCoefficientBytes )
	{
		/*
		 * Theme Park World uses ZLIB 1.1.3
		 * We can therefore deduce that the custom ZLIB header is as follows:
		 * - 4 bytes: "BILZ" magic number
		 * - 4 bytes: decompressed size
		 * - 4 bytes: stream size (the whole chunk, these 28 header bytes included; every retail BILZ chunk agrees)
		 * - 4 bytes: Constant - MAX_WBITS (15) - from zlib
		 * - 4 bytes: Constant - DEF_MEM_LEVEL (9) - from zlib
		 * - 4 bytes: Unused
		 * - 4 bytes: Unused 
		 */

		if ( data.Length < 0x1C ) throw new InvalidDataException( "Truncated BILZ header." );
		var declaredOutput = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 4, 4 ) );
		var declaredInput = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 8, 4 ) );
		if ( declaredOutput < 0 || declaredInput <= 0x1C || declaredInput > data.Length )
			throw new InvalidDataException( "Invalid BILZ chunk lengths." );
		// Preserve padded streams, but retain only the prefix any coefficient encoding can consume:
		// each coefficient is one signed byte, or an escape byte followed by an Int16 (three bytes).
		using var outputStream = new MemoryStream();
		using var inputStream = new MemoryStream( data, 0x1C, declaredInput - 0x1C, false );
		try
		{
			using var compressed = new InflaterInputStream( inputStream );
			var buffer = new byte[81920];
			long produced = 0;
			int read;
			while ( (read = compressed.Read( buffer, 0, buffer.Length )) > 0 )
			{
				produced += read;
				if ( produced > declaredOutput ) throw new InvalidDataException( "BILZ output exceeds its declared size." );
				var keep = (int)Math.Min( read, maximumCoefficientBytes - outputStream.Length );
				if ( keep > 0 ) outputStream.Write( buffer, 0, keep );
			}
			if ( produced != declaredOutput ) throw new InvalidDataException( "BILZ output does not match its declared size." );
		}
		catch ( SharpZipBaseException exception )
		{
			throw new InvalidDataException( "Invalid BILZ compressed stream.", exception );
		}
		return outputStream.ToArray();
	}

	private static byte[] DecompressLZSS( byte[] data )
	{
		return LZSS.Decompress( new BitReader( data ) );
	}
}
