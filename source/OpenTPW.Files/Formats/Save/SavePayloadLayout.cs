using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>
/// The 19 blocks of a decoded TPWI/TPWS payload, in the original writer's order (writer 0x11cb60,
/// loader 0x11b5ac; docs/reverse/SAVE-plan.md §2). The first and last blocks have no delimiter.
/// </summary>
public enum SaveBlock
{
	ActionRecording,
	World,
	Scripts,
	Particles,
	MessageCentre,
	Clock,
	VanillaTime,
	GameSystem,
	RideSystem,
	TrackRides,
	FlyingRides,
	Rsse,
	Camera,
	Coasters,
	Advisor,
	Sound,
	Cheat,
	AdvisorScoring,
	UI
}

/// <summary>
/// One block of a decoded payload. <see cref="Offset"/>/<see cref="Length"/> cover the block's own bytes;
/// <see cref="DelimiterOffset"/> is the 4-byte trailing delimiter, or -1 for the two unframed blocks.
/// <see cref="Tag"/> is the delimiter constant as the original code names it (the file stores it byte-reversed).
/// </summary>
public readonly record struct SavePayloadBlock( SaveBlock Block, string Tag, int Offset, int Length, int DelimiterOffset )
{
	public int End => Offset + Length;
}

/// <summary>
/// Bounded split of a decoded save payload into its writer-order blocks. The original writes each
/// subsystem block and then a 4-byte delimiter; its loader reads the block and aborts with
/// "The SAD_&lt;module&gt; module did not load the same number of bytes that it saved" unless the next four
/// bytes are that delimiter. Delimiters are therefore trailing length sentinels, not chunk headers.
/// <list type="bullet">
/// <item>ActionRecording: <c>u32 mLoadedPublishedPark, u32 size</c>, then <c>size</c> bytes.</item>
/// <item>The 17 delimiters must each occur exactly once after the previous one, in writer order.</item>
/// <item>Blocks whose width is traced are size-checked like the original loader (<see cref="FixedBlockBytes"/>).
/// The World block is checked by its thing chain (<see cref="SaveWorld"/>).</item>
/// <item>UI is everything after the AdvisorScoring delimiter.</item>
/// </list>
/// </summary>
public sealed class SavePayloadLayout
{
	public const int MaximumPayloadBytes = SaveReader.MaximumDecodedBytes;
	public const int DelimiterBytes = 4;
	public const int ActionRecordingHeaderBytes = 8;
	/// <summary>Coasters block with no coaster: count and three globals (rides lane, empty fixture only).</summary>
	public const int EmptyCoastersBytes = 16;

	/// <summary>Delimiter constants of the 17 framed blocks, in writer order (the file holds each byte-reversed).</summary>
	public static IReadOnlyList<(SaveBlock Block, string Tag)> Delimiters { get; } = Array.AsReadOnly( new[]
	{
		(SaveBlock.World, "WRLD"), (SaveBlock.Scripts, "SPSC"), (SaveBlock.Particles, "PART"), (SaveBlock.MessageCentre, "MESS"),
		(SaveBlock.Clock, "CLOK"), (SaveBlock.VanillaTime, "VANT"), (SaveBlock.GameSystem, "GSYS"), (SaveBlock.RideSystem, "RSYS"),
		(SaveBlock.TrackRides, "TRAK"), (SaveBlock.FlyingRides, "FLYR"), (SaveBlock.Rsse, "RSSE"), (SaveBlock.Camera, "KAME"),
		(SaveBlock.Coasters, "COAS"), (SaveBlock.Advisor, "ADVS"), (SaveBlock.Sound, "SOUN"), (SaveBlock.Cheat, "CHTS"),
		(SaveBlock.AdvisorScoring, "ADSC"),
	} );

	/// <summary>
	/// Blocks whose saved width is fixed by their traced writer: Clock two 4-byte clock words (0x10e944),
	/// VanillaTime one <c>LbTime_GetClock</c> word (0x127ac8), Camera 3×4 + 12 + 12 + 4 (0x2cb90).
	/// </summary>
	public static IReadOnlyDictionary<SaveBlock, int> FixedBlockBytes { get; } = new Dictionary<SaveBlock, int>
	{
		[SaveBlock.Clock] = 8,
		[SaveBlock.VanillaTime] = 4,
		[SaveBlock.Camera] = 40,
	};

	private readonly SavePayloadBlock[] blocks;

	private SavePayloadLayout( int payloadLength, bool loadedPublishedPark, SavePayloadBlock[] blocks )
	{
		PayloadLength = payloadLength;
		LoadedPublishedPark = loadedPublishedPark;
		this.blocks = blocks;
	}

	public int PayloadLength { get; }

	/// <summary>The action recording's <c>mLoadedPublishedPark</c> word (0 or 1).</summary>
	public bool LoadedPublishedPark { get; }

	/// <summary>All 19 blocks in writer order.</summary>
	public IReadOnlyList<SavePayloadBlock> Blocks => blocks;

	public SavePayloadBlock this[SaveBlock block] => blocks[(int)block];

	/// <summary>Bytes of the action recording after its 8-byte header.</summary>
	public int ActionRecordingBytes => this[SaveBlock.ActionRecording].Length - ActionRecordingHeaderBytes;

	public static SavePayloadLayout Parse( SaveReader reader )
	{
		ArgumentNullException.ThrowIfNull( reader );
		return Parse( reader.ReadFile() );
	}

	public static SavePayloadLayout Parse( ReadOnlySpan<byte> payload )
	{
		if ( payload.Length > MaximumPayloadBytes )
			throw new InvalidDataException( $"Save payload exceeds the {MaximumPayloadBytes}-byte limit." );
		if ( payload.Length < ActionRecordingHeaderBytes )
			throw new InvalidDataException( "Save payload is truncated inside the action recording header." );
		var published = BinaryPrimitives.ReadUInt32LittleEndian( payload );
		if ( published > 1 )
			throw new InvalidDataException( $"Save action recording has a non-boolean published-park flag {published}." );
		var recording = BinaryPrimitives.ReadUInt32LittleEndian( payload[4..] );
		if ( recording > (uint)(payload.Length - ActionRecordingHeaderBytes) )
			throw new InvalidDataException( $"Save action recording claims {recording} bytes; only {payload.Length - ActionRecordingHeaderBytes} remain." );
		var result = new SavePayloadBlock[Delimiters.Count + 2];
		var position = ActionRecordingHeaderBytes + (int)recording;
		result[0] = new SavePayloadBlock( SaveBlock.ActionRecording, "", 0, position, -1 );
		Span<byte> stored = stackalloc byte[DelimiterBytes];
		foreach ( var (block, tag) in Delimiters )
		{
			Encoding.ASCII.GetBytes( tag, stored );
			stored.Reverse();
			var remainder = payload[position..];
			var found = remainder.IndexOf( stored );
			if ( found < 0 )
				throw new InvalidDataException( payload[..position].IndexOf( stored ) >= 0
					? $"Save payload delimiter {tag} ({block}) is out of writer order."
					: $"Save payload is missing the {tag} delimiter of the {block} block." );
			if ( remainder[(found + 1)..].IndexOf( stored ) >= 0 )
				throw new InvalidDataException( $"Save payload contains the {tag} delimiter more than once after the previous block; the {block} block is ambiguous." );
			result[(int)block] = new SavePayloadBlock( block, tag, position, found, position + found );
			position += found + DelimiterBytes;
		}
		result[^1] = new SavePayloadBlock( SaveBlock.UI, "", position, payload.Length - position, -1 );
		foreach ( var (block, bytes) in FixedBlockBytes )
		{
			if ( result[(int)block].Length != bytes )
				throw new InvalidDataException( $"Save {block} block is {result[(int)block].Length} bytes; its writer saves exactly {bytes} (the original loader would report SAD_{block})." );
		}
		var coasters = result[(int)SaveBlock.Coasters];
		if ( coasters.Length < EmptyCoastersBytes )
			throw new InvalidDataException( $"Save Coasters block is {coasters.Length} bytes; its count and globals alone take {EmptyCoastersBytes}." );
		if ( BinaryPrimitives.ReadUInt32LittleEndian( payload[coasters.Offset..] ) == 0 && coasters.Length != EmptyCoastersBytes )
			throw new InvalidDataException( $"Save Coasters block has no coaster but {coasters.Length} bytes; an empty block is {EmptyCoastersBytes}." );
		return new SavePayloadLayout( payload.Length, published != 0, result );
	}

	/// <summary>The bytes of <paramref name="block"/> in <paramref name="payload"/>, which must be the parsed payload.</summary>
	public ReadOnlySpan<byte> Slice( ReadOnlySpan<byte> payload, SaveBlock block )
	{
		if ( payload.Length != PayloadLength )
			throw new ArgumentException( "Layout does not describe this payload.", nameof( payload ) );
		var item = this[block];
		return payload.Slice( item.Offset, item.Length );
	}
}
