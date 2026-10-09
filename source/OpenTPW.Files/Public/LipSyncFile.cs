using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// Strict reader for advisor <c>.LIP</c> lip-sync files (see docs/LIPS.md).
/// The file is a strictly increasing list of little-endian <see cref="uint"/> marks
/// followed by a single <c>0xFFFFFFFF</c> terminator; every observed file is a multiple
/// of eight bytes (an odd number of marks). Marks are kept as raw values: the observed
/// scale is consistent with microseconds into the paired speech sample, but neither the
/// unit nor the open/closed meaning of each mark is verified against the original runtime.
/// </summary>
public sealed class LipSyncFile : BaseFormat
{
	public const int MaximumFileBytes = 64 * 1024;
	public const uint Terminator = uint.MaxValue;
	public IReadOnlyList<uint> Marks { get; private set; } = Array.Empty<uint>();

	public LipSyncFile( string path ) => ReadFromFile( path );
	public LipSyncFile( Stream stream ) => ReadFromStream( stream );

	protected override void ReadFromStream( Stream stream )
	{
		ArgumentNullException.ThrowIfNull( stream );
		using var input = new MemoryStream();
		var readBuffer = new byte[4096];
		while ( true )
		{
			var count = stream.Read( readBuffer, 0, (int)Math.Min( readBuffer.Length, MaximumFileBytes - input.Length + 1 ) );
			if ( count == 0 )
				break;
			if ( count > MaximumFileBytes - input.Length )
				throw new InvalidDataException( "LIP exceeds the input byte limit." );
			input.Write( readBuffer, 0, count );
		}
		var data = input.ToArray();
		if ( data.Length < 8 || data.Length % 8 != 0 )
			throw new InvalidDataException( "LIP length must be a non-zero multiple of eight bytes." );
		var wordCount = data.Length / 4;
		if ( BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( data.Length - 4 ) ) != Terminator )
			throw new InvalidDataException( "LIP terminator is missing." );
		var marks = new uint[wordCount - 1];
		for ( var index = 0; index < marks.Length; index++ )
		{
			var value = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( index * 4 ) );
			if ( value == Terminator )
				throw new InvalidDataException( $"LIP mark {index} is an early terminator." );
			if ( index > 0 && value <= marks[index - 1] )
				throw new InvalidDataException( $"LIP mark {index} is not strictly increasing." );
			marks[index] = value;
		}
		Marks = marks;
	}
}
