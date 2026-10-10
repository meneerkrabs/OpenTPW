using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>One sample choice of a sound: a 1-based sample number in one of the category's banks, chosen by cumulative weight.</summary>
/// <param name="SampleNumber">1-based entry number in the bank (the original passes it to <c>TbSoundSampleInfo::SetNumber</c>).</param>
/// <param name="CumulativeWeight">Running weight out of 65,535; a sample is chosen when a random draw is at most this value.</param>
/// <param name="Value">Third word; for the music segments it is 17,457 throughout (meaning not traced).</param>
/// <param name="BankNumber">1-based index into the category's <c>cat_*BANK.map</c> list.</param>
public readonly record struct SoundSample( uint SampleNumber, uint CumulativeWeight, uint Value, ushort BankNumber, ushort Reserved );

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
		var eventCounts = new List<uint>();
		for ( var category = 0; category < categoryCount; category++ )
		{
			Require( data, position, CategoryBytes );
			eventCounts.Add( U32( data, position ) );
			position += CategoryBytes;
		}
		var events = new List<SoundEvent>();
		foreach ( var eventCount in eventCounts )
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
				events.Add( new SoundEvent( header.Id, header.Priority, header.Flags, header.Extra, entries ) );
			}
		}
		if ( position != data.Length )
			throw new InvalidDataException( $"Sound catalogue has {data.Length - position} bytes after its last record." );
		Events = events;
	}

	public SoundEvent? Find( uint id ) => Events.FirstOrDefault( item => item.Id == id );

	/// <summary>Reads a <c>cat_*BANK.map</c>: the bank names (for example <c>Sound\Ambient</c>), in the order sample bank numbers refer to.</summary>
	public static IReadOnlyList<string> ReadBankNames( byte[] data )
	{
		ArgumentNullException.ThrowIfNull( data );
		if ( data.Length > MaximumFileBytes || data.Length < HeaderBytes )
			throw new InvalidDataException( $"Sound bank map is {data.Length} bytes." );
		var count = U32( data, 24 );
		if ( count > 256 )
			throw new InvalidDataException( $"Sound bank map claims {count} banks." );
		// [BIN:STP-PPC:0x10015100 TbMapStreamer::ReadBankDataFromMap] 11-byte records (rewritten at load), then per bank a u32 length and the name
		var position = HeaderBytes + (int)count * 11;
		var names = new List<string>();
		for ( var index = 0; index < count; index++ )
		{
			Require( data, position, 4 );
			var length = (int)U32( data, position );
			Require( data, position + 4, length );
			names.Add( Encoding.Latin1.GetString( data, position + 4, length ).TrimEnd( '\0' ) );
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

/// <summary>
/// A sample bank (<c>.sdt</c>) read by entry number: entries keep their position in the offset table even
/// when a damaged entry is skipped, because catalogues address samples by number.
/// </summary>
public sealed class SoundBank
{
	private readonly byte[] data;
	private readonly int count;

	public SoundBank( byte[] data )
	{
		this.data = data ?? throw new ArgumentNullException( nameof( data ) );
		count = data.Length >= 4 ? Math.Clamp( BinaryPrimitives.ReadInt32LittleEndian( data ), 0, Math.Min( SdtArchive.MaximumEntryCount, (data.Length - 4) / 4 ) ) : 0;
	}

	public int Count => count;

	/// <summary>The MPEG audio of 1-based entry <paramref name="number"/>, or null when the entry is missing or damaged.</summary>
	public byte[]? GetMpeg( uint number )
	{
		if ( number == 0 || number > count )
			return null;
		var offset = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 4 + (int)(number - 1) * 4 ) );
		if ( offset < 4 + count * 4 || offset > data.Length - SdtArchive.MinimumEntryHeaderBytes )
			return null;
		var header = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( offset ) );
		var size = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( offset + 4 ) );
		if ( header < SdtArchive.MinimumEntryHeaderBytes || size < 0 || (long)offset + header + size > data.Length )
			return null;
		return data.AsSpan( offset + header, size ).ToArray();
	}

	/// <summary>The name stored in an entry's header (16 bytes, NUL padded).</summary>
	public string? GetName( uint number )
	{
		if ( number == 0 || number > count )
			return null;
		var offset = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 4 + (int)(number - 1) * 4 ) );
		if ( offset < 0 || offset > data.Length - SdtArchive.MinimumEntryHeaderBytes )
			return null;
		return Encoding.Latin1.GetString( data, offset + 8, 16 ).TrimEnd( '\0' );
	}
}
