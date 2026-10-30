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
/// <item>The 17 delimiters follow each other in writer order. Every block but two ends exactly where its reader stops
/// (<see cref="SaveBlockWidths"/>, read from tp.exe's block readers), like the original loader, which then compares the next
/// four bytes with the block's tag and aborts with "The SAD_&lt;module&gt; module did not load the same number of bytes that it
/// saved" otherwise; a delimiter's bytes inside a block are data. The empty Coasters block ends after its 16 bytes.</item>
/// <item>The World block ends where its loader's readers stop (<see cref="SaveBlockWidths.TryMeasureWorld"/>: fixed-width variables, control manager and
/// macro AI, 0x4000 status-byte cells and the self-closing thing list, see <see cref="SaveWorld"/>) when its bytes read that way and the WRLD tag follows. A Coasters block with a coaster
/// (its trains and cars depend on the coaster type's train and car counts) ends where its reader stops too when the caller knows the park's coaster types
/// (<see cref="Parse(ReadOnlySpan{byte}, Func{int, SaveCoasterTrainLayout?}?)"/>, which the park import passes) and the World reads as the original's; without them, and a World block that does not read
/// that way (a synthetic payload), are located by their delimiter's bytes. The payload must split into the blocks in exactly one way under these rules: a copy
/// of such a delimiter's bytes inside the block or a later one that forms a second valid split is rejected, even though the
/// count-based original loader would accept the save (SAVE-011). A payload with more than
/// <see cref="MaximumDelimiterOccurrences"/> copies of one of these delimiters is rejected too.</item>
/// <item>UI is everything after the AdvisorScoring delimiter (empty under <see cref="SavePayloadProfile.ThemeParkInc"/>, whose block widths and World block differ:
/// see <see cref="Parse(ReadOnlySpan{byte}, SavePayloadProfile)"/>).</item>
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
	/// Blocks whose saved width is fixed by their traced writer: Clock two 4-byte clock words (0x402e30), VanillaTime one
	/// clock word (0x403260), GameSystem nine words (0x5506e0), Camera 3×4 + 12 + 12 + 4 (0x42cec0), Cheat two bytes
	/// (0x405570) and AdvisorScoring one word (0x59c860). The other blocks' widths follow from their bytes (<see cref="SaveBlockWidths"/>).
	/// </summary>
	public static IReadOnlyDictionary<SaveBlock, int> FixedBlockBytes { get; } = new Dictionary<SaveBlock, int>
	{
		[SaveBlock.Clock] = 8,
		[SaveBlock.VanillaTime] = 4,
		[SaveBlock.GameSystem] = SaveBlockWidths.GameSystemBytes,
		[SaveBlock.Camera] = 40,
		[SaveBlock.Cheat] = SaveBlockWidths.CheatBytes,
		[SaveBlock.AdvisorScoring] = SaveBlockWidths.AdvisorScoringBytes,
	};

	private readonly SavePayloadBlock[] blocks;

	private SavePayloadLayout( int payloadLength, bool loadedPublishedPark, SavePayloadBlock[] blocks, SavePayloadProfile profile )
	{
		Profile = profile;
		PayloadLength = payloadLength;
		LoadedPublishedPark = loadedPublishedPark;
		this.blocks = blocks;
	}

	public int PayloadLength { get; }

	/// <summary>The block readers the payload was split with.</summary>
	public SavePayloadProfile Profile { get; }

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

	public static SavePayloadLayout Parse( ReadOnlySpan<byte> payload ) => Parse( payload, SavePayloadProfile.ThemeParkWorld );

	/// <summary>
	/// <see cref="Parse(ReadOnlySpan{byte})"/> for a caller that knows the park's coaster types: <paramref name="coasterTrains"/> gives the train slots and cars per train of the coaster
	/// type with an Info.Id, and a Coasters block whose coasters it knows all ends where its reader stops, like every other block, with no delimiter searched.
	/// </summary>
	public static SavePayloadLayout Parse( ReadOnlySpan<byte> payload, Func<int, SaveCoasterTrainLayout?>? coasterTrains ) =>
		Parse( payload, SavePayloadProfile.ThemeParkWorld, coasterTrains );

	/// <summary>
	/// Splits <paramref name="payload"/> with one game's block readers. Theme Park Inc (<see cref="SavePayloadProfile.ThemeParkInc"/>) differs in the World block
	/// (<see cref="ThemeParkIncSaveWorld.TryMeasure"/>, which must read exactly: there is no delimiter search for it), the message centre (45 lists), the camera (48 bytes)
	/// and the UI block (empty, so nothing may follow the AdvisorScoring delimiter); only a Coasters block with a coaster is still located by its delimiter.
	/// </summary>
	public static SavePayloadLayout Parse( ReadOnlySpan<byte> payload, SavePayloadProfile profile ) => Parse( payload, profile, null );

	/// <summary><see cref="Parse(ReadOnlySpan{byte}, SavePayloadProfile)"/> with the coaster types of <see cref="Parse(ReadOnlySpan{byte}, Func{int, SaveCoasterTrainLayout?}?)"/> (Theme Park World only).</summary>
	public static SavePayloadLayout Parse( ReadOnlySpan<byte> payload, SavePayloadProfile profile, Func<int, SaveCoasterTrainLayout?>? coasterTrains )
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
		if ( profile == SavePayloadProfile.ThemeParkInc && TracedEnd( payload, SaveBlock.World, start, profile ) < 0 )
			throw new InvalidDataException( "Save World block does not read as the Theme Park Inc World block followed by its WRLD delimiter." );
		// [EXT:delimiter-split-for-tools] (user-approved 2026-10-30, SAVE-011 converted: tools and synthetic payloads exist only in OpenTPW; the park import passes the coaster types and ends the block where its reader stops) a Coasters block with a coaster whose type the caller does not give (a payload split without the park's coaster types, or a station that is no single saved attraction of a World the readers measure), and a World block that does not read as the original's (a synthetic payload), are located by their delimiter's bytes: a payload that splits in more than one way there, or holds the COAS (or such a World's WRLD) delimiter's bytes more than MaximumDelimiterOccurrences times, is rejected, while tp.exe's loader 0x415270 runs each block's reader and then compares the next 4 bytes with its tag (0x415371), never searching, so a tool that splits a payload without the park's object catalogue takes this fallback
		var coasters = profile == SavePayloadProfile.ThemeParkWorld && coasterTrains != null ? CoasterTypes( payload, start, coasterTrains ) : null;
		var search = new DelimiterSearch( payload, start, profile, coasters );
		var splits = search.Count( 0, start );
		if ( splits == 0 )
		{
			Diagnose( payload, start, profile );
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
		// [BIN:TPI-EXE:0x4f8630 UI writer] a bare ret, and the loader reads nothing: a Theme Park Inc save has no UI bytes
		if ( profile == SavePayloadProfile.ThemeParkInc && position != payload.Length )
			throw new InvalidDataException( $"Theme Park Inc save has {payload.Length - position} bytes after the ADSC delimiter; its UI block is empty." );
		result[^1] = new SavePayloadBlock( SaveBlock.UI, "", position, payload.Length - position, -1 );
		return new SavePayloadLayout( payload.Length, published != 0, result, profile );
	}

	/// <summary>
	/// The coaster type of each station the World block lists: a coaster's header names its station by mesh instance id, which one saved attraction holds (<see cref="SaveCoasterBlock"/>); that
	/// attraction's Info.Id is the type <paramref name="trains"/> knows. Null when the World does not read as the original's, where the Coasters block stays located by its delimiter.
	/// </summary>
	private static Func<ushort, SaveCoasterTrainLayout?>? CoasterTypes( ReadOnlySpan<byte> payload, int worldStart, Func<int, SaveCoasterTrainLayout?> trains )
	{
		if ( !SaveBlockWidths.TryMeasureWorld( payload, worldStart, out var width, out var thingsStart ) )
			return null;
		try
		{
			var (_, things) = SaveThingChain.Walk( payload, thingsStart, worldStart + width );
			var byMesh = new Dictionary<uint, List<int>>();
			foreach ( var thing in things.Where( item => item.Model == SaveThingModel.Attraction ) )
			{
				var attraction = SaveAttraction.Read( payload[..thing.End], thing.BodyOffset, thing.Id );
				if ( !byMesh.TryGetValue( attraction.MeshInstanceId, out var infos ) )
					byMesh[attraction.MeshInstanceId] = infos = new List<int>();
				infos.Add( attraction.InfoId );
			}
			return mesh => byMesh.TryGetValue( mesh, out var infos ) && infos.Count == 1 ? trains( infos[0] ) : null;
		}
		catch ( InvalidDataException )
		{
			return null;
		}
	}

	/// <summary>Splits a Theme Park Inc payload (<see cref="SavePayloadProfile.ThemeParkInc"/>).</summary>
	public static SavePayloadLayout ParseThemeParkInc( ReadOnlySpan<byte> payload ) => Parse( payload, SavePayloadProfile.ThemeParkInc );

	/// <summary>Whether a delimiter-located block of <paramref name="length"/> bytes at <paramref name="offset"/> meets its traced width rules.</summary>
	private static string? WidthError( ReadOnlySpan<byte> payload, SaveBlock block, int offset, int length )
	{
		if ( block != SaveBlock.Coasters )
			return null;
		if ( length < EmptyCoastersBytes )
			return $"Save Coasters block is {length} bytes; its count and globals alone take {EmptyCoastersBytes}.";
		if ( BinaryPrimitives.ReadUInt32LittleEndian( payload[offset..] ) == 0 && length != EmptyCoastersBytes )
			return $"Save Coasters block has no coaster but {length} bytes; an empty block is {EmptyCoastersBytes}.";
		return null;
	}

	/// <summary>
	/// The end of a block whose width follows from its bytes (<see cref="SaveBlockWidths"/>, or the empty Coasters block), or -1 when the
	/// block is located by its delimiter; throws when the bytes do not read as the block.
	/// </summary>
	private const string WorldTag = "WRLD";

	private static int TracedEnd( ReadOnlySpan<byte> payload, SaveBlock block, int offset, SavePayloadProfile profile, Func<ushort, SaveCoasterTrainLayout?>? coasters = null )
	{
		if ( block == SaveBlock.World )
		{
			var measured = profile == SavePayloadProfile.ThemeParkInc ? ThemeParkIncSaveWorld.TryMeasure( payload, offset, out var worldWidth ) : SaveBlockWidths.TryMeasureWorld( payload, offset, out worldWidth );
			return measured && TagAt( payload, WorldTag, offset + worldWidth ) ? offset + worldWidth : -1;
		}
		if ( block == SaveBlock.Coasters )
		{
			if ( payload.Length - offset < EmptyCoastersBytes )
				throw new InvalidDataException( $"Save Coasters block is {payload.Length - offset} bytes; its count and globals alone take {EmptyCoastersBytes}." );
			if ( BinaryPrimitives.ReadUInt32LittleEndian( payload[offset..] ) == 0 )
				return offset + EmptyCoastersBytes;
			// [BIN:TPW-EXE:0x415270 block loader] the coasters' reader consumes the block by the types' train and car counts, and the tag must follow: with the types known no delimiter is searched
			return coasters != null && SaveCoasterBlock.TryMeasure( payload[offset..], coasters, out var coasterWidth ) ? offset + coasterWidth : -1;
		}
		return SaveBlockWidths.IsTraced( block ) && SaveBlockWidths.TryMeasure( payload, block, offset, out var width, profile ) ? offset + width : -1;
	}

	private static bool TagAt( ReadOnlySpan<byte> payload, string tag, int offset )
	{
		Span<byte> stored = stackalloc byte[DelimiterBytes];
		Stored( tag, stored );
		return offset >= 0 && payload.Length - offset >= DelimiterBytes && payload.Slice( offset, DelimiterBytes ).SequenceEqual( stored );
	}

	/// <summary>
	/// Names why a payload has no valid split, walking the blocks in writer order: a delimiter missing from the rest of the payload, a
	/// traced block that does not read as its block or is not followed by its delimiter, or (for the delimiter-located blocks) the first
	/// occurrence that breaks a width rule.
	/// </summary>
	private static void Diagnose( ReadOnlySpan<byte> payload, int position, SavePayloadProfile profile )
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
			var end = TracedEnd( payload, block, position, profile );
			if ( end >= 0 && block != SaveBlock.Coasters )
			{
				if ( !TagAt( payload, tag, end ) )
					throw new InvalidDataException( $"Save {block} block is {end - position} bytes, but the {tag} delimiter does not follow it (the original loader would report SAD_{block})." );
				position = end + DelimiterBytes;
				continue;
			}
			var length = found;
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
	/// Counts the splits of the framed blocks (capped at 2): a traced block ends where its reader stops, and its delimiter must follow;
	/// the World block and a Coasters block with a coaster may end at any later occurrence of their delimiter's bytes. Memoized per
	/// (delimiter, block start), so the search is bounded by the delimiter occurrences, not their combinations.
	/// </summary>
	private readonly ref struct DelimiterSearch
	{
		private readonly ReadOnlySpan<byte> payload;
		private readonly List<int>?[] occurrences = new List<int>?[Delimiters.Count];
		private readonly int start;
		private readonly Dictionary<(int, int), int> counts = new();
		private readonly SavePayloadProfile profile;
		private readonly Func<ushort, SaveCoasterTrainLayout?>? coasters;

		public DelimiterSearch( ReadOnlySpan<byte> payload, int start, SavePayloadProfile profile, Func<ushort, SaveCoasterTrainLayout?>? coasters = null )
		{
			this.payload = payload;
			this.profile = profile;
			this.start = start;
			this.coasters = coasters;
		}

		private List<int> Occurrences( int index )
		{
			if ( occurrences[index] is { } known )
				return known;
			// Discover delimiter candidates only when an actual block needs fallback. Exact
			// boundaries must not be rejected by delimiter-looking bytes in other blocks.
			var foundOffsets = new List<int>();
			Span<byte> stored = stackalloc byte[DelimiterBytes];
			Stored( Delimiters[index].Tag, stored );
			for ( var position = start; position + DelimiterBytes <= payload.Length; )
			{
				var found = payload[position..].IndexOf( stored );
				if ( found < 0 )
					break;
				if ( foundOffsets.Count == MaximumDelimiterOccurrences )
					throw new InvalidDataException( $"Save payload holds the {Delimiters[index].Tag} delimiter bytes more than {MaximumDelimiterOccurrences} times." );
				foundOffsets.Add( position + found );
				position += found + 1;
			}
			occurrences[index] = foundOffsets;
			return foundOffsets;
		}

		/// <summary>The delimiter offsets that can end block <paramref name="index"/> starting at <paramref name="offset"/>, in payload order.</summary>
		public List<int> Ends( int index, int offset )
		{
			var (block, tag) = Delimiters[index];
			var ends = new List<int>();
			int traced;
			try
			{
				traced = TracedEnd( payload, block, offset, profile, coasters );
			}
			catch ( InvalidDataException )
			{
				return ends;
			}
			if ( traced >= 0 )
			{
				if ( TagAt( payload, tag, traced ) )
					ends.Add( traced );
				return ends;
			}
			if ( block is not (SaveBlock.World or SaveBlock.Coasters) )
				return ends;
			var candidates = Occurrences( index );
			var first = candidates.BinarySearch( offset );
			for ( var item = first < 0 ? ~first : first; item < candidates.Count; item++ )
			{
				var end = candidates[item];
				if ( WidthError( payload, block, offset, end - offset ) == null )
					ends.Add( end );
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
