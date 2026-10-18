using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>One sample choice of a sound: a 1-based sample number in one of the category's banks, chosen by cumulative weight.</summary>
/// <param name="SampleNumber">1-based entry number in the bank (the original passes it to <c>TbSoundSampleInfo::SetNumber</c>).</param>
/// <param name="CumulativeWeight">Running weight out of 65,535; a sample is chosen when a random draw is at most this value.</param>
/// <param name="Value">Third word; for the music segments it is 17,457 throughout (meaning not traced).</param>
/// <param name="BankNumber">1-based index into the category's <c>cat_*BANK.map</c> list.</param>
public readonly record struct SoundSample( uint SampleNumber, uint CumulativeWeight, uint Value, ushort BankNumber, ushort Reserved );

/// <summary>A bank name from a <c>cat_*BANK.map</c> and whether its record carries the Global flag (0x20).</summary>
public readonly record struct SoundBankName( string Name, bool Global );

/// <summary>A link of a branching sentence: after this sound, the sound <see cref="TargetSound"/> may follow while the event parameter is within [<see cref="MinimumParameter"/>, <see cref="MaximumParameter"/>].</summary>
public readonly record struct SoundLink( uint TargetSound, ushort Reserved, byte MinimumParameter, byte MaximumParameter );

/// <summary>
/// One sound of an event (42 bytes in the file): its sample choices, its links, and its playback ranges.
/// Volume is a random percentage in [<see cref="MinimumVolume"/>, <see cref="MaximumVolume"/>]
/// (<c>CAudioPlaceHolder::GetRandomVolume</c>); pitch is a signed random value in
/// [<see cref="MinimumPitch"/>, <see cref="MaximumPitch"/>]; <see cref="Weight"/> chooses among the event's sounds.
/// </summary>
public sealed record SoundEntry( byte MinimumVolume, byte MaximumVolume, sbyte MinimumPitch, sbyte MaximumPitch,
	ushort MinimumDelay, ushort MaximumDelay, uint Weight, float Rolloff, IReadOnlyList<SoundSample> Samples, IReadOnlyList<SoundLink> Links, byte[] Raw );

/// <summary>A sound event (20 bytes in the file): the id the game sends, its flags (which select the player type) and its sounds.</summary>
public sealed record SoundEvent( uint Id, uint Priority, ushort Flags, ushort Extra, IReadOnlyList<SoundEntry> Sounds )
{
	/// <summary>Category group bits: master, music, speech, effects (category record +8).</summary>
	public uint GroupMask { get; init; }
	/// <summary>The player the original creates for this event (<c>CAudioPlaceHolderList::CreatePlaceHolder</c>).</summary>
	public SoundEventPlayer Player =>
		(Flags & 0x4) == 0 ? SoundEventPlayer.OneShot
		: (Flags & 0x10) != 0 ? SoundEventPlayer.LinearSentence
		: (Flags & 0x2) == 0 ? ((Flags & 0x400) != 0 ? SoundEventPlayer.OneShotBranchingSentence : SoundEventPlayer.OneShotSentence)
		: (Flags & 0x400) != 0 ? SoundEventPlayer.BranchingSentence
		: (Flags & 0x100) != 0 ? SoundEventPlayer.SentenceShuffle
		: SoundEventPlayer.Sentence;
}

public enum SoundEventPlayer
{
	OneShot,
	LinearSentence,
	OneShotSentence,
	OneShotBranchingSentence,
	Sentence,
	SentenceShuffle,
	BranchingSentence
}

/// <summary>
/// Reader for the original sound catalogues (<c>cat_*SFX.map</c>: events, sounds and samples;
/// <c>cat_*BANK.map</c>: the sample banks they refer to). The layout follows the Mac sound library's
/// map streamer (<c>TbMapStreamer::RegisterSFXData</c>, <c>ReadCatagory</c>, <c>ReadEvents</c>,
/// <c>ReadSounds</c>, <c>ReadBankDataFromMap</c>; little-endian, the Mac swaps every field): a 16-byte
/// type GUID, two words and a category count, then 24-byte categories, each followed by its 20-byte
/// events, each followed by its 42-byte sounds, each followed by its 16-byte samples and 8-byte links.
/// Every catalogue of the Mac data parses to its exact length.
/// </summary>
public sealed class SoundCatalog
{
	public const int MaximumFileBytes = 1024 * 1024;
	private const int HeaderBytes = 28;
	private const int CategoryBytes = 24;
	private const int EventBytes = 20;
	private const int SoundBytes = 42;
	private const int SampleBytes = 16;
	private const int LinkBytes = 8;

	public uint Flag { get; }
	public IReadOnlyList<SoundEvent> Events { get; }

	public SoundCatalog( byte[] data )
	{
		ArgumentNullException.ThrowIfNull( data );
		if ( data.Length > MaximumFileBytes || data.Length < HeaderBytes )
			throw new InvalidDataException( $"Sound catalogue is {data.Length} bytes." );
		Flag = U32( data, 16 );
		var categoryCount = U32( data, 24 );
		if ( categoryCount > 64 )
			throw new InvalidDataException( $"Sound catalogue claims {categoryCount} categories." );
		var position = HeaderBytes;
		var eventCounts = new List<(uint Count, uint GroupMask)>();
		for ( var category = 0; category < categoryCount; category++ )
		{
			Require( data, position, CategoryBytes );
			eventCounts.Add( (U32( data, position ), U32( data, position + 8 )) );
			position += CategoryBytes;
		}
		var events = new List<SoundEvent>();
		foreach ( var (eventCount, groupMask) in eventCounts )
		{
			var headers = new List<(uint Id, uint SoundCount, uint Priority, ushort Flags, ushort Extra)>();
			for ( var index = 0; index < eventCount; index++ )
			{
				Require( data, position, EventBytes );
				headers.Add( (U32( data, position ), U32( data, position + 4 ), U32( data, position + 12 ), U16( data, position + 16 ), U16( data, position + 18 )) );
				position += EventBytes;
			}
			foreach ( var header in headers )
			{
				var sounds = new List<(byte[] Raw, int SampleCount, uint LinkCount)>();
				for ( var index = 0; index < header.SoundCount; index++ )
				{
					Require( data, position, SoundBytes );
					var raw = data.AsSpan( position, SoundBytes ).ToArray();
					sounds.Add( (raw, U16( raw, 0 ), U32( raw, 4 )) );
					position += SoundBytes;
				}
				var entries = new List<SoundEntry>();
				foreach ( var (raw, sampleCount, linkCount) in sounds )
				{
					Require( data, position, sampleCount * SampleBytes );
					var samples = new SoundSample[sampleCount];
					for ( var index = 0; index < sampleCount; index++, position += SampleBytes )
						samples[index] = new SoundSample( U32( data, position ), U32( data, position + 4 ), U32( data, position + 8 ), U16( data, position + 12 ), U16( data, position + 14 ) );
					if ( linkCount > 4096 )
						throw new InvalidDataException( $"Sound has {linkCount} links." );
					Require( data, position, (int)linkCount * LinkBytes );
					var links = new SoundLink[linkCount];
					for ( var index = 0; index < linkCount; index++, position += LinkBytes )
						links[index] = new SoundLink( U32( data, position ), U16( data, position + 4 ), data[position + 6], data[position + 7] );
					entries.Add( new SoundEntry( raw[12], raw[13], (sbyte)raw[14], (sbyte)raw[15], U16( raw, 16 ), U16( raw, 18 ), U32( raw, 30 ),
						BinaryPrimitives.ReadSingleLittleEndian( raw.AsSpan( 34 ) ), samples, links, raw ) );
				}
				events.Add( new SoundEvent( header.Id, header.Priority, header.Flags, header.Extra, entries ) { GroupMask = groupMask } );
			}
		}
		if ( position != data.Length )
			throw new InvalidDataException( $"Sound catalogue has {data.Length - position} bytes after its last record." );
		Events = events;
	}

	public SoundEvent? Find( uint id ) => Events.FirstOrDefault( item => item.Id == id );

	/// <summary>Reads a <c>cat_*BANK.map</c>: the bank names (for example <c>Sound\Ambient</c>), in the order sample bank numbers refer to.</summary>
	public static IReadOnlyList<string> ReadBankNames( byte[] data ) => ReadBankMap( data ).Select( bank => bank.Name ).ToList();

	/// <summary>
	/// Reads a <c>cat_*BANK.map</c> with each bank's folder flag: <see cref="SoundBankName.Global"/> is bit 0x20 of the record's flag byte
	/// (the bank lives under the data root's <c>Global</c> folder, not under the category's own folder).
	/// </summary>
	// [BIN:TPW-EXE:0x6bff14] the bank-map reader tests the flag byte at record +9 for 0x20; set: the base is the data root plus "Global\" (0x782dd8, 0x6bff1b-0x6bff3d); clear: the category's own folder
	public static IReadOnlyList<SoundBankName> ReadBankMap( byte[] data )
	{
		ArgumentNullException.ThrowIfNull( data );
		if ( data.Length > MaximumFileBytes || data.Length < HeaderBytes )
			throw new InvalidDataException( $"Sound bank map is {data.Length} bytes." );
		var count = U32( data, 24 );
		if ( count > 256 )
			throw new InvalidDataException( $"Sound bank map claims {count} banks." );
		// [BIN:STP-PPC:0x10015100 TbMapStreamer::ReadBankDataFromMap] 11-byte records (rewritten at load), then per bank a u32 length and the name
		var position = HeaderBytes + (int)count * 11;
		var names = new List<SoundBankName>();
		for ( var index = 0; index < count; index++ )
		{
			Require( data, position, 4 );
			var length = (int)U32( data, position );
			Require( data, position + 4, length );
			var flags = data[Math.Min( data.Length - 1, HeaderBytes + index * 11 + 9 )];
			names.Add( new SoundBankName( Encoding.Latin1.GetString( data, position + 4, length ).TrimEnd( '\0' ), (flags & 0x20) != 0 ) );
			position += 4 + length;
		}
		if ( position != data.Length )
			throw new InvalidDataException( $"Sound bank map has {data.Length - position} bytes after its last name." );
		return names;
	}

	private static void Require( byte[] data, int position, int length )
	{
		if ( length < 0 || position < 0 || position > data.Length - length )
			throw new InvalidDataException( "Sound catalogue ends inside a record." );
	}

	private static uint U32( byte[] data, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( offset, 4 ) );
	private static ushort U16( byte[] data, int offset ) => BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( offset, 2 ) );
}

/// <summary>Fields of an SDT entry header that the game's sound objects read.</summary>
public static class SdtEntryHeader
{
	/// <summary>Type byte (+27) bit: the u32 at +32 is the playback length.</summary>
	public const byte PlaybackLengthFlag = 0x04;

	/// <summary>
	/// The number of 16-bit PCM bytes (all channels) the game plays, or null when the entry does not give one.
	/// [BIN:TPW-EXE:0x006C82C0 SDT playback length] the MPEG sound object copies the u32 at entry +0x20 when type byte +0x1B has
	/// bit 0x04, and its fill routine 0x6C83C0 delivers at most that many decoded bytes before the end/loop handler, so the
	/// decoder's tail past it (one frame plus the encoder delay in the corpus: every one of the 5,674 entries has the bit and a
	/// length no longer than its decoded PCM) is never heard.
	/// </summary>
	public static int? PlaybackBytes( ReadOnlySpan<byte> header )
	{
		if ( header.Length < 0x24 || (header[0x1B] & PlaybackLengthFlag) == 0 )
			return null;
		var bytes = BinaryPrimitives.ReadInt32LittleEndian( header[0x20..] );
		return bytes >= 0 ? bytes : null;
	}
}

/// <summary>
/// A sample bank (<c>.sdt</c>) read by entry number: entries keep their position in the offset table even
/// when a damaged entry is skipped, because catalogues address samples by number.
/// </summary>
/// <summary>
/// An SDT sound bank: a count, a table of entry offsets, and entries (a header with the size, then the MPEG audio). From a file it
/// keeps only the offset table and reads an entry when it is asked for: a bank is tens of megabytes, and the browser already holds
/// the file (docs/BROWSER-PERFORMANCE.md). Both sources check entries the same way, against the bank's length.
/// </summary>
public sealed class SoundBank
{
	private readonly byte[]? data;
	private readonly Func<Stream>? open;
	private readonly long length;
	private readonly int count;
	private readonly int[] offsets;

	public SoundBank( byte[] data )
	{
		this.data = data ?? throw new ArgumentNullException( nameof( data ) );
		length = data.Length;
		count = data.Length >= 4 ? Math.Clamp( BinaryPrimitives.ReadInt32LittleEndian( data ), 0, Math.Min( SdtArchive.MaximumEntryCount, (data.Length - 4) / 4 ) ) : 0;
		offsets = new int[count];
		for ( var index = 0; index < count; index++ )
			offsets[index] = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 4 + index * 4 ) );
	}

	/// <summary>A bank read from <paramref name="open"/> (a fresh stream of the whole bank per call) of <paramref name="length"/> bytes.</summary>
	public SoundBank( Func<Stream> open, long length )
	{
		this.open = open ?? throw new ArgumentNullException( nameof( open ) );
		this.length = length;
		count = length >= 4 ? (int)Math.Clamp( BinaryPrimitives.ReadInt32LittleEndian( Read( 0, 4 ) ), 0, Math.Min( SdtArchive.MaximumEntryCount, (length - 4) / 4 ) ) : 0;
		var table = Read( 4, count * 4 );
		offsets = new int[count];
		for ( var index = 0; index < count; index++ )
			offsets[index] = BinaryPrimitives.ReadInt32LittleEndian( table.AsSpan( index * 4 ) );
	}

	public int Count => count;

	/// <summary><paramref name="size"/> bytes at <paramref name="offset"/> (the caller has checked they lie inside the bank).</summary>
	private byte[] Read( long offset, int size )
	{
		if ( data != null )
			return data.AsSpan( (int)offset, size ).ToArray();
		if ( size == 0 )
			return Array.Empty<byte>();
		using var stream = open!();
		// A file of another length is not the bank whose offset table was read (replaced on disk): a clear error, not other bytes.
		if ( stream.Length != length )
			throw new IOException( $"The sound bank changed after its offset table was read ({stream.Length} bytes, was {length})." );
		stream.Seek( offset, SeekOrigin.Begin );
		var bytes = new byte[size];
		stream.ReadExactly( bytes );
		return bytes;
	}

	/// <summary>The MPEG audio of 1-based entry <paramref name="number"/>, or null when the entry is missing or damaged.</summary>
	public byte[]? GetMpeg( uint number )
		=> GetMpeg( number, int.MaxValue );

	/// <summary>Reads a selected entry within a caller's payload bound, checked before allocating its bytes.</summary>
	public byte[]? GetMpeg( uint number, int maximumBytes )
	{
		var result = GetMpeg( number, maximumBytes, out var rejectedPayloadBytes );
		if ( rejectedPayloadBytes > 0 )
			throw new InvalidDataException( $"Selected sound entry contains {rejectedPayloadBytes} bytes, exceeding the caller's {maximumBytes}-byte input bound." );
		return result;
	}

	/// <summary>Reads a selected entry, or returns null and its positive payload size when the caller's bound refuses it.</summary>
	/// <param name="rejectedPayloadBytes">Zero unless validated entry metadata exceeds the bound. Source errors still propagate.</param>
	public byte[]? GetMpeg( uint number, int maximumBytes, out int rejectedPayloadBytes )
		=> GetMpeg( number, maximumBytes, out rejectedPayloadBytes, out _ );

	/// <summary>
	/// <see cref="GetMpeg(uint, int, out int)"/>, also giving the entry's playback length (<see cref="SdtEntryHeader.PlaybackBytes"/>)
	/// from the same header read, so a file-backed bank cannot pair one entry's audio with another version's length.
	/// </summary>
	public byte[]? GetMpeg( uint number, int maximumBytes, out int rejectedPayloadBytes, out int? playbackBytes )
	{
		rejectedPayloadBytes = 0;
		playbackBytes = null;
		ArgumentOutOfRangeException.ThrowIfNegative( maximumBytes );
		if ( number == 0 || number > count )
			return null;
		var offset = offsets[number - 1];
		if ( offset < 4 + count * 4 || offset > length - SdtArchive.MinimumEntryHeaderBytes )
			return null;
		var head = Read( offset, SdtArchive.MinimumEntryHeaderBytes );
		var header = BinaryPrimitives.ReadInt32LittleEndian( head );
		var size = BinaryPrimitives.ReadInt32LittleEndian( head.AsSpan( 4 ) );
		if ( header < SdtArchive.MinimumEntryHeaderBytes || size < 0 || (long)offset + header + size > length )
			return null;
		playbackBytes = SdtEntryHeader.PlaybackBytes( head );
		if ( size > maximumBytes )
		{
			rejectedPayloadBytes = size;
			return null;
		}
		return Read( (long)offset + header, size );
	}

	/// <summary>The PCM byte length the game plays of entry <paramref name="number"/> (<see cref="SdtEntryHeader.PlaybackBytes"/>), or null.</summary>
	public int? GetPlaybackBytes( uint number )
	{
		if ( number == 0 || number > count )
			return null;
		var offset = offsets[number - 1];
		if ( offset < 4 + count * 4 || offset > length - SdtArchive.MinimumEntryHeaderBytes )
			return null;
		return SdtEntryHeader.PlaybackBytes( Read( offset, SdtArchive.MinimumEntryHeaderBytes ) );
	}

	/// <summary>The name stored in an entry's header (16 bytes, NUL padded).</summary>
	public string? GetName( uint number )
	{
		if ( number == 0 || number > count )
			return null;
		var offset = offsets[number - 1];
		if ( offset < 4 + count * 4 || offset > length - SdtArchive.MinimumEntryHeaderBytes )
			return null;
		return Encoding.Latin1.GetString( Read( (long)offset + 8, 16 ) ).TrimEnd( '\0' );
	}
}
