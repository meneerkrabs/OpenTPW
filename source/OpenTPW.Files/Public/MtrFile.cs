using System.Buffers.Binary;
using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Strict structural reader for the eleven <c>.mtr</c> files found only in the retail ISO's
/// per-language <c>Meshes</c> folders (see docs/MTR.md). Layout: a 36-byte header (magic
/// <c>AF 15 59 2E</c>, fixed words 6/1/1/0, trailer offset, three zero words), an opaque
/// <see cref="uint"/> table up to the trailer offset, then an 856-byte trailer: a 256-byte
/// NUL-padded name, a word that is always 1, 144 floats and five footer words.
/// <see cref="DecodeTopology"/> splits the table into mesh topology that is fully redundant
/// with the paired localized banner <c>.MD2</c> (verified for all eleven files), and
/// <see cref="Matrices"/> views the floats as nine 4×4 matrices, the first two equal to
/// that MD2's node matrix. It is not a material format (the upstream "Material" label is
/// not supported by the data) and the original runtime is not shown to load these files.
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

	/// <summary>
	/// Corner/face topology from <see cref="Table"/>, sized by footer word 0 (corner count)
	/// and footer word 3 (face count): the first face that uses each corner, that corner's
	/// slot in the face with the corner order reversed (0, 2, 1), and per face the three
	/// position indices in that reversed order. Corners 0–2 belong to face 0 implicitly
	/// and are not stored in the first part.
	/// </summary>
	public sealed record Topology( IReadOnlyList<uint> FirstFaceOfCorner, IReadOnlyList<uint> CornerSlot, IReadOnlyList<uint> FacePositions )
	{
		public int CornerCount => CornerSlot.Count;
		public int FaceCount => FacePositions.Count / 3;
	}

	/// <summary>The 144 trailer floats as nine row-major 4×4 matrices (translation in row 4).</summary>
	public IReadOnlyList<Matrix4x4> Matrices => Enumerable.Range( 0, TrailerFloatCount / 16 ).Select( index =>
	{
		var f = TrailerFloats.Skip( index * 16 ).Take( 16 ).ToArray();
		return new Matrix4x4( f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15] );
	} ).ToArray();

	public Topology DecodeTopology()
	{
		var corners = (long)Footer[0];
		var faces = (long)Footer[3];
		if ( corners < 3 || faces < 1 || Table.Count != 2 * corners - 3 + 3 * faces )
			throw new InvalidDataException( "MTR table length does not match its corner and face counts." );
		var firstFace = new uint[corners];
		var slots = new uint[corners];
		var positions = new uint[3 * faces];
		for ( var corner = 3; corner < corners; corner++ )
		{
			firstFace[corner] = Table[corner - 3];
			if ( firstFace[corner] >= faces || firstFace[corner] < firstFace[corner - 1] )
				throw new InvalidDataException( $"MTR first face of corner {corner} is out of order or range." );
		}
		for ( var corner = 0; corner < corners; corner++ )
		{
			slots[corner] = Table[(int)(corners - 3 + corner)];
			if ( slots[corner] > 2 )
				throw new InvalidDataException( $"MTR corner slot {corner} is not 0–2." );
		}
		for ( var index = 0; index < positions.Length; index++ )
			positions[index] = Table[(int)(2 * corners - 3 + index)];
		return new Topology( firstFace, slots, positions );
	}

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
