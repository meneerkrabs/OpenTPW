using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// Reader for the per-language <c>UniToMB.dat</c> reverse character table (magic <c>BFUM</c>), the
/// counterpart of <see cref="BFMUReader"/> needed for text input (Unicode -> game codepage byte).
/// </summary>
/// <remarks>
/// Layout verified against all six shipped languages ([DATA:Language/*/UniToMB.dat]): magic
/// "BFUM", then ranges until the end of the file, each a uint16 first code unit, a uint16 that is
/// not part of the range (zero, or non-zero filler in the first English/French/German range), a
/// uint16 last code unit, another such uint16, then (last - first + 1) uint16 game-codepage indices.
/// English/French/German: U+0009..U+00FF, U+0007, U+2019 (English) / U+0153 (French) / U+0161
/// (German). Danish/Dutch/Swedish: U+0007, U+0009..U+00FF. Code points absent from the language's
/// <c>MBToUni.dat</c> still map to some index (descending filler values), so a mapping only counts
/// when it round-trips through <c>MBToUni.dat</c>.
/// </remarks>
public sealed class BFUMReader : BaseFormat
{
	private readonly Dictionary<char, int> indices = new();

	public BFUMReader( string path )
	{
		ReadFromFile( path );
	}

	public BFUMReader( Stream stream )
	{
		ReadFromStream( stream );
	}

	/// <summary>First and last code unit of each range, in file order.</summary>
	public IReadOnlyList<(char First, char Last)> Ranges { get; private set; } = Array.Empty<(char, char)>();

	/// <summary>Every (code unit -> code) entry, including filler entries: the 1-based codepage index in a single-byte table; in a two-byte table (Polish, Korean) a value above 0xFF is the lead byte (low byte) and trail byte (high byte).</summary>
	public IReadOnlyDictionary<char, int> Entries => indices;

	protected override void ReadFromStream( Stream stream )
	{
		using var memory = new MemoryStream();
		stream.CopyTo( memory );
		var data = memory.ToArray();
		if ( data.Length < 4 || data[0] != 'B' || data[1] != 'F' || data[2] != 'U' || data[3] != 'M' )
			throw new InvalidDataException( "Character table magic number did not match BFUM." );
		var ranges = new List<(char, char)>();
		var position = 4;
		while ( position < data.Length )
		{
			if ( data.Length - position < 8 )
				throw new InvalidDataException( $"BFUM range header at {position} is truncated." );
			var first = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( position, 2 ) );
			var last = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( position + 4, 2 ) );
			position += 8;
			if ( last < first )
				throw new InvalidDataException( $"BFUM range U+{first:X4}..U+{last:X4} is reversed." );
			var count = last - first + 1;
			if ( data.Length - position < count * 2 )
				throw new InvalidDataException( $"BFUM range U+{first:X4}..U+{last:X4} needs {count * 2} bytes; {data.Length - position} remain." );
			for ( var i = 0; i < count; i++ )
				indices[(char)(first + i)] = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( position + i * 2, 2 ) );
			position += count * 2;
			ranges.Add( ((char)first, (char)last) );
		}
		if ( ranges.Count == 0 )
			throw new InvalidDataException( "BFUM table has no ranges." );
		Ranges = ranges;
	}
}

/// <summary>
/// Converts between Unicode text and a language's game codepage (1-based indices into
/// <c>MBToUni.dat</c>), e.g. for park names typed by the player.
/// </summary>
public sealed class GameTextCodec
{
	private readonly BFMUReader decodeTable;
	// the game bytes of a character: one byte, or the lead byte then the trail byte of a two-byte table (Polish, Korean)
	private readonly Dictionary<char, (byte First, byte Second, bool TwoBytes)> encodeTable = new();

	public GameTextCodec( BFMUReader decodeTable, BFUMReader encodeTable )
	{
		this.decodeTable = decodeTable ?? throw new ArgumentNullException( nameof( decodeTable ) );
		ArgumentNullException.ThrowIfNull( encodeTable );
		foreach ( var (character, value) in encodeTable.Entries )
		{
			// Only mappings that round-trip are real; the rest are filler. A value above 0xFF holds the lead byte (low byte)
			// and the trail byte (high byte) of a two-byte table; in the single-byte tables it is the 1-based index itself.
			if ( value <= byte.MaxValue )
			{
				if ( value >= 1 && value <= decodeTable.SingleByteCount && decodeTable.Characters[value - 1] == character )
					this.encodeTable[character] = ((byte)value, 0, false);
				continue;
			}
			var lead = (byte)(value & 0xFF);
			var trail = (byte)(value >> 8);
			if ( decodeTable.IsLeadByte( lead ) && trail >= 1 )
			{
				var index = decodeTable.TwoByteIndex( lead, trail );
				if ( index <= decodeTable.Characters.Count && decodeTable.Characters[index - 1] == character )
					this.encodeTable[character] = (lead, trail, true);
			}
		}
	}

	/// <summary>Loads both tables from a language folder.</summary>
	public static GameTextCodec Load( GameLanguage language )
	{
		using var stream = language.OpenRead( "UniToMB.dat" );
		return new GameTextCodec( language.CharacterTable, new BFUMReader( stream ) );
	}

	public bool CanEncode( char character ) => encodeTable.ContainsKey( character );

	/// <summary>Characters the language can represent, in codepage order.</summary>
	public IReadOnlyList<char> Repertoire => decodeTable.Characters.Where( encodeTable.ContainsKey ).ToArray();

	/// <summary>
	/// Encodes text. Unrepresentable characters become the codepage's '?' when it has one, otherwise
	/// they are dropped; they are listed in <paramref name="unmappable"/>. [EXT:text-encode-helper] tp.exe never names
	/// UniToMB.dat or MBToUni.dat: its wide-to-ANSI conversion (0x424DB0) calls WideCharToMultiByte (CP_ACP, default
	/// replacement character), and this method has no caller outside the tests, so it is an OpenTPW tool and test helper.
	/// </summary>
	public byte[] Encode( string text, ICollection<char>? unmappable = null )
	{
		ArgumentNullException.ThrowIfNull( text );
		var result = new List<byte>( text.Length );
		foreach ( var character in text )
		{
			if ( encodeTable.TryGetValue( character, out var code ) || TryGetPlaceholder( character, unmappable, out code ) )
			{
				result.Add( code.First );
				if ( code.TwoBytes )
					result.Add( code.Second );
			}
		}
		return result.ToArray();
	}

	private bool TryGetPlaceholder( char character, ICollection<char>? unmappable, out (byte First, byte Second, bool TwoBytes) code )
	{
		unmappable?.Add( character );
		return encodeTable.TryGetValue( '?', out code );
	}

	public string Decode( ReadOnlySpan<byte> bytes )
	{
		var characters = new System.Text.StringBuilder( bytes.Length );
		for ( var position = 0; position < bytes.Length; )
		{
			position += decodeTable.Decode( bytes, position, out var character );
			characters.Append( character );
		}
		return characters.ToString();
	}
}
