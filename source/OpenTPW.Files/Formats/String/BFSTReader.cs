using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>
/// Reader for <c>.str</c> string tables (magic <c>BFST</c>).
/// </summary>
/// <remarks>
/// Header
///   4 bytes: Magic number - "BFST"
///   4 bytes: Unknown
///   4 bytes: String count
/// For each string
///   4 bytes: String offset (relative to the end of the 12-byte header)
/// For each string (at offset)
///   1 byte  - Unknown, always 0x01
///   3 bytes - String length, little-endian 24-bit (two shipped UITEXT.str entries exceed 255)
///   n bytes - Each character, a 1-based index into the language's BFMU table (MBToUni.dat)
///   Padding
/// </remarks>
internal sealed class BFSTReader
{
	private const int HeaderLength = 12;
	private readonly byte[] buffer;
	private readonly BFMUReader table;

	public BFSTReader( Stream stream, BFMUReader table )
	{
		this.table = table ?? throw new ArgumentNullException( nameof( table ) );
		using var memory = new MemoryStream();
		stream.CopyTo( memory );
		buffer = memory.ToArray();
	}

	public string[] ReadFile()
	{
		if ( buffer.Length < HeaderLength || Encoding.ASCII.GetString( buffer, 0, 4 ) != "BFST" )
			throw new InvalidDataException( "String table magic number did not match BFST." );

		var stringCount = BinaryPrimitives.ReadInt32LittleEndian( buffer.AsSpan( 8, 4 ) );
		if ( stringCount < 0 || HeaderLength + (long)stringCount * 4 > buffer.Length )
			throw new InvalidDataException( $"String table declares {stringCount} strings, which do not fit the file." );

		var characters = table.Characters;
		var output = new string[stringCount];
		var builder = new StringBuilder();
		for ( var i = 0; i < stringCount; i++ )
		{
			var offset = BinaryPrimitives.ReadInt32LittleEndian( buffer.AsSpan( HeaderLength + i * 4, 4 ) );
			var position = (long)HeaderLength + offset;
			if ( offset < 0 || position + 4 > buffer.Length )
				throw new InvalidDataException( $"String {i} offset {offset} is outside the file." );

			if ( buffer[position] != 1 )
				throw new InvalidDataException( $"String {i} marker is {buffer[position]} instead of 1 at position {position}." );

			var length = buffer[position + 1] | buffer[position + 2] << 8 | buffer[position + 3] << 16;
			position += 4;
			if ( position + length > buffer.Length )
				throw new InvalidDataException( $"String {i} length {length} runs past the end of the file." );

			builder.Clear();
			for ( var j = 0; j < length; j++ )
			{
				var index = buffer[position + j];
				if ( index < 1 || index > characters.Count )
					throw new InvalidDataException( $"String {i} character byte {index} is outside the {characters.Count}-entry BFMU table." );
				builder.Append( characters[index - 1] );
			}

			output[i] = builder.ToString();
		}

		return output;
	}
}
