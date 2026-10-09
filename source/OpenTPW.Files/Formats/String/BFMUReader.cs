using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// Reader for the per-language <c>MBToUni.dat</c> character table (magic <c>BFMU</c>).
/// </summary>
/// <remarks>
/// Layout: 4-byte magic "BFMU", 2 bytes that are zero in every shipped table, a little-endian
/// uint16 character count, then that many UTF-16LE code units. String-table bytes are 1-based
/// indices into this list. The table differs per language (Danish and Swedish ship 248 entries,
/// English/French/German 249), so a string table must be decoded with the table from its own
/// language folder.
/// </remarks>
public sealed class BFMUReader : BaseFormat
{
	private char[] characters = Array.Empty<char>();

	public BFMUReader( string path )
	{
		ReadFromFile( path );
	}

	public BFMUReader( Stream stream )
	{
		ReadFromStream( stream );
	}

	/// <summary>
	/// Characters in table order; string byte <c>n</c> maps to <c>Characters[n - 1]</c>.
	/// </summary>
	public IReadOnlyList<char> Characters => characters;

	protected override void ReadFromStream( Stream stream )
	{
		using var memory = new MemoryStream();
		stream.CopyTo( memory );
		var data = memory.ToArray();

		if ( data.Length < 8 || data[0] != 'B' || data[1] != 'F' || data[2] != 'M' || data[3] != 'U' )
			throw new InvalidDataException( "Character table magic number did not match BFMU." );

		var count = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 6, 2 ) );
		if ( data.Length < 8 + count * 2 )
			throw new InvalidDataException( $"Character table declares {count} characters but holds {(data.Length - 8) / 2}." );

		characters = new char[count];
		for ( var i = 0; i < count; i++ )
			characters[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 8 + i * 2, 2 ) );
	}

	public List<char> CharacterArray() => new( characters );

	/// <summary>
	/// Maps a 1-based string-table byte to its character.
	/// </summary>
	public char GetCharacter( int character )
	{
		// Characters are offset by 0x01 in the BFMU!
		if ( character < 1 || character > characters.Length )
			throw new InvalidDataException( $"Character index {character} is outside the {characters.Length}-entry BFMU table." );
		return characters[character - 1];
	}
}
