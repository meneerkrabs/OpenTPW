using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// Reader for the per-language <c>MBToUni.dat</c> character table (magic <c>BFMU</c>).
/// </summary>
/// <remarks>
/// Layout: 4-byte magic "BFMU", a little-endian uint16 <i>lead-byte count</i> (0 in the single-byte tables), then
/// two bytes <i>last byte</i> and <i>last trail count</i>, then the UTF-16LE code units. In the single-byte tables
/// (English, Danish, Dutch, French, German, Swedish, Spanish) the last two bytes read as the uint16 character
/// count (248 or 249) and a string byte <c>n</c> is the 1-based index of its character.
/// In the two-byte tables (Polish: 1 lead byte, 329 characters; Korean: 67 lead bytes, 17,270 characters) the
/// lead bytes are the top <c>count</c> byte values, <c>last byte - count + 1 .. last byte</c> (0xFF down to
/// 0xFF-count+1, ascending); a byte below them is a single-byte character (index = byte), a lead byte <c>L</c> is
/// followed by a trail byte <c>T</c> (1..255) and means index <c>(first lead - 1) + (L - first lead) * 255 + T</c>. The last lead
/// byte has only <i>last trail count</i> trails (Polish 75, Korean 252), which makes the character count
/// <c>(first lead - 1) + (leads - 1) * 255 + last trail count</c>. Characters in the table are in descending code order,
/// so a Korean string reads as KS X 1001 Hangul behind lead bytes 0xBD..0xFF.
/// The table differs per language (Danish and Swedish ship 248 entries, English/French/German 249), so a string table
/// must be decoded with the table from its own language folder.
/// </remarks>
// [DATA:Language/Polish/MBToUni.dat, Language/korean/MBToUni.dat] the lead/trail layout is confirmed by every string table of both languages
// decoding without an unknown code, and by UniToMB.dat, whose entries hold the same (lead, trail) pair for each Unicode code point;
// tp.exe 0x6adac7 reads this header (it accepts the magic BFMU and BFMH); its size arithmetic was not traced further
public sealed class BFMUReader : BaseFormat
{
	private char[] characters = Array.Empty<char>();
	private int leadByteCount;
	private int singleByteCount;

	public BFMUReader( string path )
	{
		ReadFromFile( path );
	}

	public BFMUReader( Stream stream )
	{
		ReadFromStream( stream );
	}

	/// <summary>
	/// Characters in table order; string byte <c>n</c> maps to <c>Characters[n - 1]</c> in a single-byte table, see <see cref="Decode"/> for the two-byte ones.
	/// </summary>
	public IReadOnlyList<char> Characters => characters;

	/// <summary>Number of lead bytes: 0 for a single-byte table, 1 for Polish, 67 for Korean.</summary>
	public int LeadByteCount => leadByteCount;

	/// <summary>Whether strings hold two-byte codes (a lead byte followed by a trail byte).</summary>
	public bool IsMultiByte => leadByteCount > 0;

	/// <summary>The first lead byte (0x100 when <see cref="IsMultiByte"/> is false): every byte from it up is a lead byte.</summary>
	public int FirstLeadByte => 256 - leadByteCount;

	/// <summary>Number of characters written as one byte.</summary>
	public int SingleByteCount => singleByteCount;

	protected override void ReadFromStream( Stream stream )
	{
		using var memory = new MemoryStream();
		stream.CopyTo( memory );
		var data = memory.ToArray();

		if ( data.Length < 8 || data[0] != 'B' || data[1] != 'F' || data[2] != 'M' || data[3] != 'U' )
			throw new InvalidDataException( "Character table magic number did not match BFMU." );

		var leads = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 4, 2 ) );
		int count;
		int singles;
		if ( leads == 0 )
		{
			count = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 6, 2 ) );
			singles = count;
		}
		else
		{
			var last = data[6];
			var lastTrails = data[7];
			if ( leads > 255 || last > 255 || leads > last || lastTrails < 1 )
				throw new InvalidDataException( $"Character table declares {leads} lead bytes ending at byte {last} with {lastTrails} trail bytes." );
			singles = last - leads;
			count = singles + (leads - 1) * 255 + lastTrails;
		}
		if ( data.Length < 8 + count * 2 )
			throw new InvalidDataException( $"Character table declares {count} characters but holds {(data.Length - 8) / 2}." );

		leadByteCount = leads;
		singleByteCount = singles;
		characters = new char[count];
		for ( var i = 0; i < count; i++ )
			characters[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 8 + i * 2, 2 ) );
	}

	public List<char> CharacterArray() => new( characters );

	/// <summary>
	/// Maps a 1-based table index to its character (the string byte of a single-byte table).
	/// </summary>
	public char GetCharacter( int character )
	{
		// Characters are offset by 0x01 in the BFMU!
		if ( character < 1 || character > characters.Length )
			throw new InvalidDataException( $"Character index {character} is outside the {characters.Length}-entry BFMU table." );
		return characters[character - 1];
	}

	/// <summary>Whether <paramref name="value"/> starts a two-byte code in this table.</summary>
	public bool IsLeadByte( byte value ) => leadByteCount > 0 && value >= FirstLeadByte;

	/// <summary>
	/// Decodes the character at <paramref name="position"/> of a string and returns how many bytes it took (1 or 2).
	/// A lead byte needs its trail byte inside <paramref name="bytes"/>.
	/// </summary>
	/// <exception cref="InvalidDataException">The code is outside the table, or a lead byte has no trail byte.</exception>
	public int Decode( ReadOnlySpan<byte> bytes, int position, out char character )
	{
		var value = bytes[position];
		if ( !IsLeadByte( value ) )
		{
			if ( value < 1 || value > singleByteCount )
				throw new InvalidDataException( $"Character byte {value} is outside the {singleByteCount}-entry single-byte part of the BFMU table." );
			character = characters[value - 1];
			return 1;
		}
		if ( position + 1 >= bytes.Length )
			throw new InvalidDataException( $"Lead byte {value} at position {position} has no trail byte." );
		var trail = bytes[position + 1];
		var index = singleByteCount + (value - FirstLeadByte) * 255 + trail;
		if ( trail < 1 || index > characters.Length )
			throw new InvalidDataException( $"Two-byte code {value:X2} {trail:X2} is outside the {characters.Length}-entry BFMU table." );
		character = characters[index - 1];
		return 2;
	}

	/// <summary>
	/// The table index (1-based) of a two-byte code, the counterpart of <see cref="Decode"/> for tools: <c>lead</c> is a lead byte, <c>trail</c> 1..255.
	/// </summary>
	public int TwoByteIndex( byte lead, byte trail ) => singleByteCount + (lead - FirstLeadByte) * 255 + trail;
}
