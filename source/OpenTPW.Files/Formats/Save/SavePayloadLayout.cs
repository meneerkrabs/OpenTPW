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
/// <item>The 17 delimiters follow each other in writer order. A block whose width is traced (<see cref="FixedBlockBytes"/>)
/// ends exactly at that width, like the original loader; the empty Coasters block ends after its 16 bytes. The World
/// block is checked by its thing chain (<see cref="SaveWorld"/>).</item>
/// <item>The other blocks' widths are not traced, so their delimiter may be any later occurrence of its four bytes.
/// The payload must split into the blocks in exactly one way under these rules: delimiter bytes inside an earlier or
/// later block's data are accepted, but a split that stays ambiguous (the bytes of a block's own delimiter inside
/// that block, with every later delimiter still in order) is rejected, which the count-based original loader would
/// accept (SAVE-011), as is a payload with more than <see cref="MaximumDelimiterOccurrences"/> copies of one delimiter.</item>
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
	/// <summary>Bound on the occurrences of one delimiter's bytes after the action recording (keeps the split search small).</summary>
	public const int MaximumDelimiterOccurrences = 256;

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
		var start = ActionRecordingHeaderBytes + (int)recording;
		// [APPROX:SAVE-011] the delimiters of blocks whose width is not traced are located by their bytes: a payload that splits into its blocks in more than one way, or holds a delimiter's bytes more than MaximumDelimiterOccurrences times, is rejected, while the count-based original loader reads each block by its own width — evidence needed: the serialized widths of the remaining blocks
		var search = new DelimiterSearch( payload, start );
		var splits = search.Count( 0, start );
		if ( splits == 0 )
		{
			Diagnose( payload, start );
			throw new InvalidDataException( "Save payload does not split into its writer-order blocks." );
		}
		var result = new SavePayloadBlock[Delimiters.Count + 2];
		result[0] = new SavePayloadBlock( SaveBlock.ActionRecording, "", 0, start, -1 );
		var position = start;
		for ( var index = 0; index < Delimiters.Count; index++ )
		{
			var (block, tag) = Delimiters[index];
			var ends = new List<int>();
			foreach ( var end in search.Ends( index, position ) )
			{
				if ( search.Count( index + 1, end + DelimiterBytes ) != 0 )
					ends.Add( end );
			}
			if ( splits > 1 && ends.Count > 1 )
				throw new InvalidDataException( $"Save payload splits into its blocks in more than one way: the {tag} delimiter occurs more than once where it could end the {block} block." );
			result[(int)block] = new SavePayloadBlock( block, tag, position, ends[0] - position, ends[0] );
			position = ends[0] + DelimiterBytes;
		}
		result[^1] = new SavePayloadBlock( SaveBlock.UI, "", position, payload.Length - position, -1 );
		return new SavePayloadLayout( payload.Length, published != 0, result );
	}

	/// <summary>Whether a block of <paramref name="length"/> bytes at <paramref name="offset"/> meets its traced width rules.</summary>
	private static string? WidthError( ReadOnlySpan<byte> payload, SaveBlock block, int offset, int length )
	{
		if ( FixedBlockBytes.TryGetValue( block, out var bytes ) && length != bytes )
			return $"Save {block} block is {length} bytes; its writer saves exactly {bytes} (the original loader would report SAD_{block}).";
		if ( block != SaveBlock.Coasters )
			return null;
		if ( length < EmptyCoastersBytes )
			return $"Save Coasters block is {length} bytes; its count and globals alone take {EmptyCoastersBytes}.";
		if ( BinaryPrimitives.ReadUInt32LittleEndian( payload[offset..] ) == 0 && length != EmptyCoastersBytes )
			return $"Save Coasters block has no coaster but {length} bytes; an empty block is {EmptyCoastersBytes}.";
		return null;
	}

	/// <summary>Names why a payload has no valid split: the first delimiter missing or out of order after the first-occurrence split, or the first width rule it breaks.</summary>
	private static void Diagnose( ReadOnlySpan<byte> payload, int position )
	{
		Span<byte> stored = stackalloc byte[DelimiterBytes];
		foreach ( var (block, tag) in Delimiters )
		{
			Stored( tag, stored );
			var found = payload[position..].IndexOf( stored );
			if ( found < 0 )
				throw new InvalidDataException( payload[..position].IndexOf( stored ) >= 0
					? $"Save payload delimiter {tag} ({block}) is out of writer order."
					: $"Save payload is missing the {tag} delimiter of the {block} block." );
			var length = FixedBlockBytes.TryGetValue( block, out var bytes ) && position + bytes + DelimiterBytes <= payload.Length
				&& payload.Slice( position + bytes, DelimiterBytes ).SequenceEqual( stored ) ? bytes : found;
			if ( WidthError( payload, block, position, length ) is { } error )
				throw new InvalidDataException( error );
			position += length + DelimiterBytes;
		}
	}

	private static void Stored( string tag, Span<byte> stored )
	{
		Encoding.ASCII.GetBytes( tag, stored );
		stored.Reverse();
	}

	/// <summary>
	/// Counts the splits of the framed blocks (capped at 2): delimiter <c>k</c> may end its block at a fixed-width block's
	/// traced end or, otherwise, at any later occurrence of its bytes. Memoized per (delimiter, block start), so the search
	/// is bounded by the delimiter occurrences, not their combinations.
	/// </summary>
	private readonly ref struct DelimiterSearch
	{
		private readonly ReadOnlySpan<byte> payload;
		private readonly List<int>[] occurrences = new List<int>[Delimiters.Count];
		private readonly Dictionary<(int, int), int> counts = new();

		public DelimiterSearch( ReadOnlySpan<byte> payload, int start )
		{
			this.payload = payload;
			Span<byte> stored = stackalloc byte[DelimiterBytes];
			for ( var index = 0; index < Delimiters.Count; index++ )
			{
				Stored( Delimiters[index].Tag, stored );
				occurrences[index] = new List<int>();
				for ( var position = start; position + DelimiterBytes <= payload.Length; )
				{
					var found = payload[position..].IndexOf( stored );
					if ( found < 0 )
						break;
					if ( occurrences[index].Count == MaximumDelimiterOccurrences )
						throw new InvalidDataException( $"Save payload holds the {Delimiters[index].Tag} delimiter bytes more than {MaximumDelimiterOccurrences} times." );
					occurrences[index].Add( position + found );
					position += found + 1;
				}
			}
		}

		/// <summary>The delimiter offsets that can end block <paramref name="index"/> starting at <paramref name="offset"/>, in payload order.</summary>
		public List<int> Ends( int index, int offset )
		{
			var block = Delimiters[index].Block;
			var ends = new List<int>();
			var first = occurrences[index].BinarySearch( offset );
			for ( var item = first < 0 ? ~first : first; item < occurrences[index].Count; item++ )
			{
				var end = occurrences[index][item];
				if ( WidthError( payload, block, offset, end - offset ) == null )
					ends.Add( end );
				if ( FixedBlockBytes.TryGetValue( block, out var bytes ) && end - offset >= bytes )
					break;
			}
			return ends;
		}

		public int Count( int index, int offset )
		{
			if ( index == Delimiters.Count )
				return 1;
			if ( counts.TryGetValue( (index, offset), out var known ) )
				return known;
			var total = 0;
			foreach ( var end in Ends( index, offset ) )
			{
				total = Math.Min( 2, total + Count( index + 1, end + DelimiterBytes ) );
				if ( total == 2 )
					break;
			}
			counts[(index, offset)] = total;
			return total;
		}
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
