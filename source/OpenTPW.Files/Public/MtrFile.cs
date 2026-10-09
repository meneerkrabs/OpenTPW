using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// Strict structural reader for the eleven <c>.mtr</c> files found only in the retail ISO's
/// per-language <c>Meshes</c> folders (see docs/MTR.md). Layout: a 36-byte header (magic
/// <c>AF 15 59 2E</c>, fixed words 6/1/1/0, trailer offset, three zero words), an opaque
/// <see cref="uint"/> table up to the trailer offset, then an 856-byte trailer: a 256-byte
/// NUL-padded name, a word that is always 1, 144 opaque floats and five footer words.
/// No field is assigned a material, matrix or mesh meaning; the upstream "Material" label
/// is not verified and the original runtime is not shown to load these files.
/// </summary>
public sealed class MtrFile : BaseFormat
{
	public const int MaximumFileBytes = 1024 * 1024;
	public const int HeaderBytes = 36;
	public const int NameBytes = 256;
	public const int TrailerFloatCount = 144;
	public const int FooterWordCount = 5;
	public const int TrailerBytes = NameBytes + 4 + TrailerFloatCount * 4 + FooterWordCount * 4;
	public static ReadOnlySpan<byte> Magic => new byte[] { 0xaf, 0x15, 0x59, 0x2e };

	public string Name { get; private set; } = "";
	public IReadOnlyList<uint> Table { get; private set; } = Array.Empty<uint>();
	public IReadOnlyList<float> TrailerFloats { get; private set; } = Array.Empty<float>();
	public IReadOnlyList<uint> Footer { get; private set; } = Array.Empty<uint>();

	public MtrFile( string path ) => ReadFromFile( path );
	public MtrFile( Stream stream ) => ReadFromStream( stream );

	protected override void ReadFromStream( Stream stream )
	{
		ArgumentNullException.ThrowIfNull( stream );
		using var input = new MemoryStream();
		var readBuffer = new byte[8192];
		while ( true )
		{
			var count = stream.Read( readBuffer, 0, (int)Math.Min( readBuffer.Length, MaximumFileBytes - input.Length + 1 ) );
			if ( count == 0 )
				break;
			if ( count > MaximumFileBytes - input.Length )
				throw new InvalidDataException( "MTR exceeds the input byte limit." );
			input.Write( readBuffer, 0, count );
		}
		var data = input.ToArray();
		if ( data.Length < HeaderBytes + TrailerBytes || !data.AsSpan( 0, 4 ).SequenceEqual( Magic ) )
			throw new InvalidDataException( "MTR header is missing or truncated." );
		uint Word( int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( offset ) );
		if ( Word( 4 ) != 6 || Word( 8 ) != 1 || Word( 12 ) != 1 || Word( 16 ) != 0 )
			throw new NotSupportedException( "MTR header words differ from the observed 6/1/1/0 variant." );
		if ( Word( 24 ) != 0 || Word( 28 ) != 0 || Word( 32 ) != 0 )
			throw new InvalidDataException( "MTR reserved header words are nonzero." );
		var trailerOffset = Word( 20 );
		if ( trailerOffset < HeaderBytes || (trailerOffset - HeaderBytes) % 4 != 0 || (long)trailerOffset + TrailerBytes != data.Length )
			throw new InvalidDataException( "MTR trailer offset is inconsistent with the file length." );

		var table = new uint[(trailerOffset - HeaderBytes) / 4];
		for ( var index = 0; index < table.Length; index++ )
			table[index] = Word( HeaderBytes + index * 4 );

		var nameSlot = data.AsSpan( (int)trailerOffset, NameBytes );
		var nameLength = nameSlot.IndexOf( (byte)0 );
		if ( nameLength <= 0 || nameSlot[nameLength..].IndexOfAnyExcept( (byte)0 ) >= 0 )
			throw new InvalidDataException( "MTR name is empty, unterminated or has nonzero padding." );
		foreach ( var character in nameSlot[..nameLength] )
		{
			if ( character < 0x20 || character > 0x7e )
				throw new InvalidDataException( "MTR name contains a non-printable byte." );
		}
		var cursor = (int)trailerOffset + NameBytes;
		if ( Word( cursor ) != 1 )
			throw new NotSupportedException( "MTR trailer word differs from the observed value 1." );
		cursor += 4;

		var floats = new float[TrailerFloatCount];
		for ( var index = 0; index < floats.Length; index++, cursor += 4 )
		{
			floats[index] = BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( cursor ) );
			if ( !float.IsFinite( floats[index] ) )
				throw new InvalidDataException( $"MTR trailer float {index} is not finite." );
		}

		var footer = new uint[FooterWordCount];
		for ( var index = 0; index < footer.Length; index++, cursor += 4 )
			footer[index] = Word( cursor );
		if ( footer[1] != 24 )
			throw new NotSupportedException( "MTR footer word 1 differs from the observed value 24." );

		Name = System.Text.Encoding.ASCII.GetString( nameSlot[..nameLength] );
		Table = table;
		TrailerFloats = floats;
		Footer = footer;
	}
}
