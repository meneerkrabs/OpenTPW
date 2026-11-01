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
///   n bytes - Each character: a 1-based index into the language's BFMU table (MBToUni.dat), or, in the two-byte tables
///             (Polish, Korean), a lead byte and a trail byte for the characters beyond the single-byte range (see BFMUReader); n counts bytes
///   Padding to a multiple of four bytes, then further chunks with the same 4-byte header: 1 another text run,
///   2 a parameter (the 24-bit field is the parameter's type, no payload), 0 the end of the record
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

	public string[] ReadFile() => Read().Entries;

	/// <summary>Every entry as its first text run (<see cref="ReadFile"/>) and as the whole record with its parameter slots.</summary>
	public (string[] Entries, StringTemplate[] Templates) Read()
	{
		if ( buffer.Length < HeaderLength || Encoding.ASCII.GetString( buffer, 0, 4 ) != "BFST" )
			throw new InvalidDataException( "String table magic number did not match BFST." );

		var stringCount = BinaryPrimitives.ReadInt32LittleEndian( buffer.AsSpan( 8, 4 ) );
		if ( stringCount < 0 || HeaderLength + (long)stringCount * 4 > buffer.Length )
			throw new InvalidDataException( $"String table declares {stringCount} strings, which do not fit the file." );

		var output = new string[stringCount];
		var templates = new StringTemplate[stringCount];
		var starts = Enumerable.Range( 0, stringCount ).Select( i => HeaderLength + (long)BinaryPrimitives.ReadInt32LittleEndian( buffer.AsSpan( HeaderLength + i * 4, 4 ) ) )
			.Distinct().Order().ToArray();
		var builder = new StringBuilder();
		for ( var i = 0; i < stringCount; i++ )
		{
			var offset = BinaryPrimitives.ReadInt32LittleEndian( buffer.AsSpan( HeaderLength + i * 4, 4 ) );
			var position = (long)HeaderLength + offset;
			// The next distinct record start bounds this record: its 4-byte header and its first text run must fit before it,
			// so a length that overruns the record cannot read the next record's bytes (least of all a two-byte trail byte).
			var next = NextRecordStart( starts, position );
			if ( offset < 0 || position + 4 > buffer.Length )
				throw new InvalidDataException( $"String {i} offset {offset} is outside the file." );

			if ( position + 4 > next )
				throw new InvalidDataException( $"String {i} header at {position} does not fit before the next record at {next}." );

			if ( buffer[position] != 1 )
				throw new InvalidDataException( $"String {i} marker is {buffer[position]} instead of 1 at position {position}." );

			var length = buffer[position + 1] | buffer[position + 2] << 8 | buffer[position + 3] << 16;
			position += 4;
			if ( position + length > buffer.Length )
				throw new InvalidDataException( $"String {i} length {length} runs past the end of the file." );

			if ( position + length > next )
				throw new InvalidDataException( $"String {i} length {length} runs past the next record at {next}." );

			builder.Clear();
			for ( var j = 0; j < length; )
			{
				try
				{
					j += table.Decode( buffer.AsSpan( (int)position, length ), j, out var character );
					builder.Append( character );
				}
				catch ( InvalidDataException exception )
				{
					throw new InvalidDataException( $"String {i}: {exception.Message}" );
				}
			}

			output[i] = builder.ToString();
			templates[i] = ReadTemplate( output[i], position + length, next );
		}

		return (output, templates);
	}

	/// <summary>The next distinct record start after <paramref name="position"/>, or the end of the file when this is the last record.</summary>
	private long NextRecordStart( long[] starts, long position )
	{
		var at = Array.BinarySearch( starts, position );
		return at >= 0 && at + 1 < starts.Length ? starts[at + 1] : buffer.Length;
	}

	private bool TryDecode( int start, int length, int offset, out char character, out int taken )
	{
		try
		{
			taken = table.Decode( buffer.AsSpan( start, length ), offset, out character );
			return true;
		}
		catch ( InvalidDataException )
		{
			character = default;
			taken = 0;
			return false;
		}
	}

	/// <summary>
	/// The parts of a record after its first text run: further text runs and parameter slots up to the end chunk. A chunk that
	/// does not fit before the next record or has another type ends the record, so a table without such chunks reads exactly as before.
	/// </summary>
	// [BIN:TPW-EXE:0x6acd60 string format] the formatter fills a UITEXT record's parameter chunks (type 2) with the caller's typed values (the 'MRAV' argument records)
	private StringTemplate ReadTemplate( string first, long position, long end )
	{
		var parts = new List<StringTemplatePart> { new( first, -1 ) };
		var builder = new StringBuilder();
		while ( true )
		{
			position = (position + 3) & ~3L;
			if ( position + 4 > end )
				break;
			var type = buffer[position];
			var field = buffer[position + 1] | buffer[position + 2] << 8 | buffer[position + 3] << 16;
			position += 4;
			if ( type == 2 )
			{
				parts.Add( new( "", field ) );
				continue;
			}
			if ( type != 1 || position + field > end )
				break;
			builder.Clear();
			for ( var j = 0; j < field; )
			{
				if ( !TryDecode( (int)position, field, j, out var character, out var taken ) )
					return new StringTemplate( parts );
				builder.Append( character );
				j += taken;
			}
			parts.Add( new( builder.ToString(), -1 ) );
			position += field;
		}
		return new StringTemplate( parts );
	}
}

/// <summary>One part of a <see cref="StringTemplate"/>: a text run (<see cref="ParameterType"/> -1) or a parameter slot of a type.</summary>
public readonly record struct StringTemplatePart( string Text, int ParameterType )
{
	public bool IsParameter => ParameterType >= 0;
}

/// <summary>A whole string-table record: its text runs and parameter slots in order (the first part is the entry's first text run).</summary>
public sealed class StringTemplate
{
	public StringTemplate( IReadOnlyList<StringTemplatePart> parts ) => Parts = parts;

	public IReadOnlyList<StringTemplatePart> Parts { get; }
	public int ParameterCount => Parts.Count( part => part.IsParameter );

	/// <summary>
	/// The record with each parameter slot filled by the value of its own type (the slot's field is the index of the argument it takes; a type without a value fills nothing).
	/// </summary>
	public string FillByType( IReadOnlyDictionary<int, string> values )
	{
		ArgumentNullException.ThrowIfNull( values );
		var builder = new StringBuilder();
		foreach ( var part in Parts )
		{
			if ( !part.IsParameter )
				builder.Append( part.Text );
			else if ( values.TryGetValue( part.ParameterType, out var value ) )
				builder.Append( value );
		}
		return builder.ToString();
	}

	/// <summary>The record with its parameter slots filled in order by <paramref name="values"/> (a missing value fills nothing).</summary>
	public string Fill( params string[] values )
	{
		var builder = new StringBuilder();
		var next = 0;
		foreach ( var part in Parts )
		{
			if ( !part.IsParameter )
				builder.Append( part.Text );
			else if ( next < values.Length )
				builder.Append( values[next++] );
			else
				next++;
		}
		return builder.ToString();
	}
}
