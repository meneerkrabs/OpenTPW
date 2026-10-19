using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// <c>SndReverb.map</c>: the sound service's reverb presets, indexed by the level SETREVERB stores. The loader is tp.exe <c>0x6C5D20</c>:
/// a dword that must be 2000, a dword entry count (32 in the shipped file), then per entry one presence byte and, when it is non-zero,
/// 13 dwords; an absent entry reads as the record whose first dword is -1 and cannot be selected.
/// </summary>
// [BIN:TPW-EXE:0x6C5D20 SndReverb.map loader] the stream reads are 4 bytes (checked against 2000), 4 bytes (the count), then per entry 1 byte and, when it is not 0, 13 reads of 4 bytes into a 0x34-byte record; an absent entry starts with 0xFFFFFFFF (0x6C6000 refuses it: index below the count and first dword not -1)
// [BIN:TPW-EXE:0x6C46B0 reverb command] the selected record is sent to the audio device as an EAX 2.0 listener property set (DSPROPSETID_EAX20_ListenerProperties {0306A6A8-B224-11D2-99E5-0000E8D8C722}, property 1 = all parameters, 0x38 bytes) by 0x6B7970 when the sound device has the EAX path (otherwise the backend object's vtable slot 0x50 is called with the same record, 0x6B79B4; QMixer.dll exports the QSWaveMixSetReverb* functions); the thirteen words are kept raw here
public sealed class SndReverbMap
{
	/// <summary>The first dword of the file.</summary>
	public const uint Version = 2000;
	/// <summary>Dwords of a present record.</summary>
	public const int RecordWords = 13;

	private readonly uint[]?[] records;

	private SndReverbMap( uint[]?[] records ) => this.records = records;

	/// <summary>The entry count (32 in the shipped file).</summary>
	public int Count => records.Length;

	/// <summary>Whether <paramref name="level"/> names a physically present entry.</summary>
	public bool Contains( int level ) => (uint)level < (uint)records.Length && records[level] != null;

	/// <summary>The 13 raw dwords of a selectable preset; null for an absent or out-of-range level, or a first-word <c>0xFFFFFFFF</c> record.</summary>
	public IReadOnlyList<uint>? Record( int level ) => Contains( level ) && records[level]![0] != uint.MaxValue ? records[level] : null;

	/// <summary>The float view of one dword of a record (the file stores EAX-style floats and integers side by side).</summary>
	public float Single( int level, int word ) => BitConverter.UInt32BitsToSingle( records[level]![word] );

	/// <summary>Parses the file; throws <see cref="InvalidDataException"/> for a wrong version, a truncated entry or trailing bytes.</summary>
	public static SndReverbMap Read( ReadOnlySpan<byte> data )
	{
		if ( data.Length < 8 )
			throw new InvalidDataException( "SndReverb.map is shorter than its header." );
		if ( BinaryPrimitives.ReadUInt32LittleEndian( data ) != Version )
			throw new InvalidDataException( "SndReverb.map does not start with the version 2000." );
		var count = BinaryPrimitives.ReadUInt32LittleEndian( data[4..] );
		// the entry count of a real file is 32; the bound only keeps a damaged header from allocating gigabytes
		if ( count > 65536 )
			throw new InvalidDataException( $"SndReverb.map declares {count} entries." );
		var records = new uint[]?[count];
		var position = 8;
		for ( var index = 0; index < records.Length; index++ )
		{
			if ( position >= data.Length )
				throw new InvalidDataException( $"SndReverb.map ends before entry {index}." );
			if ( data[position++] == 0 )
				continue;
			if ( data.Length - position < RecordWords * 4 )
				throw new InvalidDataException( $"SndReverb.map entry {index} is truncated." );
			var words = new uint[RecordWords];
			for ( var word = 0; word < RecordWords; word++ )
				words[word] = BinaryPrimitives.ReadUInt32LittleEndian( data[(position + word * 4)..] );
			records[index] = words;
			position += RecordWords * 4;
		}
		if ( position != data.Length )
			throw new InvalidDataException( $"SndReverb.map has {data.Length - position} bytes after its last entry." );
		return new SndReverbMap( records );
	}
}
