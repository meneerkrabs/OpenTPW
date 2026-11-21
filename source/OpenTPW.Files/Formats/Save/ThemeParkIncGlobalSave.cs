using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>One entry of the CDK registration table (career <c>+0x5C</c>, 256 × 16 bytes; <c>0x74B1F0</c>). The constructor's empty slot is (0, 4, <see cref="int.MaxValue"/>, -1).</summary>
public readonly record struct ThemeParkIncCdkEntry( int Id, int Level, int Flag, int Amount )
{
	/// <summary>The value the career constructor gives every slot (<c>0x86FF04</c> with the element constructor <c>0x406CB7</c>).</summary>
	public static readonly ThemeParkIncCdkEntry Empty = new( 0, 4, int.MaxValue, -1 );

	public bool IsEmpty => Flag == int.MaxValue;
}

/// <summary>A volume setting stored as an on/off word and a level.</summary>
public readonly record struct ThemeParkIncVolume( bool Enabled, int Level );

/// <summary>
/// One zone record of the global save as the file holds it (<c>0x74FEF0</c>, 0xF8 bytes in memory, 215 in the file after its name). Every field keeps its raw value so a file round-trips
/// byte for byte.
/// </summary>
public sealed class ThemeParkIncZoneData
{
	public const int SignCharacters = 33;

	/// <summary>The map key's bytes as the file holds them (the original's string ends at the first NUL).</summary>
	public byte[] NameBytes { get; set; } = Array.Empty<byte>();
	/// <summary>The six one-shot bytes <c>+0x04</c>.</summary>
	public byte[] OneShot { get; set; } = new byte[6];
	/// <summary>The four optional values (<c>+0x0A + i</c> flag, <c>+0x10 + 4i</c> value).</summary>
	public (byte Flag, int Value)[] Optional { get; set; } = new (byte, int)[4];
	/// <summary>The eight park awards this zone holds and the value each was won with (<c>+0x20 + i</c>, <c>+0x28 + 4i</c>).</summary>
	public (byte Flag, int Value)[] Awards { get; set; } = new (byte, int)[8];
	public ushort[] SignA { get; set; } = new ushort[SignCharacters];
	public ushort[] SignB { get; set; } = new ushort[SignCharacters];
	/// <summary><c>d_available</c> (<c>+0x61</c>).</summary>
	public byte Available { get; set; }
	/// <summary><c>d_open</c> (<c>+0x60</c>).</summary>
	public byte Open { get; set; }
	public int ParkNumber { get; set; }
	public byte NameChanged { get; set; }
	public byte AllResearchCompleted { get; set; }
	public int ChallengesDone { get; set; }
	public byte AllChallengesDone { get; set; }
	public int CurrentTechLevel { get; set; }

	/// <summary>The name as the original's string holds it: the bytes up to the first NUL, Latin-1.</summary>
	public string Name
	{
		get
		{
			var end = Array.IndexOf( NameBytes, (byte)0 );
			return Encoding.Latin1.GetString( NameBytes, 0, end < 0 ? NameBytes.Length : end );
		}
	}

	public ThemeParkIncZoneData Clone() => new()
	{
		NameBytes = (byte[])NameBytes.Clone(), OneShot = (byte[])OneShot.Clone(), Optional = ((byte, int)[])Optional.Clone(), Awards = ((byte, int)[])Awards.Clone(),
		SignA = (ushort[])SignA.Clone(), SignB = (ushort[])SignB.Clone(), Available = Available, Open = Open, ParkNumber = ParkNumber, NameChanged = NameChanged,
		AllResearchCompleted = AllResearchCompleted, ChallengesDone = ChallengesDone, AllChallengesDone = AllChallengesDone, CurrentTechLevel = CurrentTechLevel
	};
}

/// <summary>Game options (<c>0x45A130</c> on <c>0xA101E0</c>), decoded from their 59 bytes.</summary>
public sealed record ThemeParkIncOptions(
	ThemeParkIncVolume VolumeA, ThemeParkIncVolume VolumeB, ThemeParkIncVolume Speech, ThemeParkIncVolume Movie,
	bool AdvisorOn, bool TutorialOn, bool TooltipsOn, bool ConfirmDeleteOn, bool RmbScrollOn, bool RmbCancelOn,
	bool IsometricOn, bool AnimateMenus, int GameSpeed, float ZoomMin, float ZoomMax, float FpsClip, int MusicEnabled );

/// <summary>The shares game (<c>0x620E30</c>), decoded from its 182 bytes: three company slots and the player's holding.</summary>
public sealed record ThemeParkIncShares( IReadOnlyList<bool> SlotsPresent, int NumberOwned, int PercentCompanySharesOwned, string CompanyName, int CurrentValue );

/// <summary>How a global save load ended.</summary>
public enum ThemeParkIncGlobalSaveOutcome
{
	/// <summary>Every section was read (trailing bytes are ignored, as the original does).</summary>
	Complete,
	/// <summary>The load succeeded but stopped early: a marker differed or was cut short, <c>mFirstHourTime</c> was cut short, or an ignored sub-serializer ran out of bytes; the sections after the stop are null.</summary>
	Stopped,
	/// <summary>The load failed: the version is below 12, or a field the body checks was cut short, or a zone name repeats.</summary>
	Failed
}

/// <summary>
/// Theme Park Inc's Global Master Save (<c>.GMS</c>, and its <c>.GMI</c> refresh twin; docs/reverse/TPI-CAREER.md §3): the career object <c>0xBFC484</c> and six session objects, in the order of the
/// body <c>0x74D840</c>. Every section is kept, the untraced ones as raw bytes, so <see cref="Write"/> reproduces a read file byte for byte. <see cref="Read"/> follows the original's lenient
/// loader: a marker that differs is a successful load that stops there, the shares and <c>0xAA0180</c> sub-serializers' errors are ignored, trailing bytes are ignored, any header version from
/// 12 up is taken.
/// </summary>
// [BIN:TPI-EXE:0x00745720 GMI refresh snapshot] the save routine writes .GMI instead of .GMS (and .TPWI instead of .TPWS) when its flag is set; both go through the writer 0x0074C8F0, which keeps the flag at +0x14EC and writes the same version-12 body
public sealed class ThemeParkIncGlobalSave
{
	/// <summary>The version the writer stores (<c>0x74C8F0</c>); the loader takes any version from 12 up (<c>0x74D110</c>).</summary>
	public const int SupportedVersion = 12;
	public const int CardSlots = 256;
	public const int HistoryEntries = 0x284;
	public const int HistoryEntryBytes = 0x14;
	public const int StatusWords = 5 * 12;
	public const int OptionsBytes = 59;
	public const int Block592720Bytes = 96;
	public const int SharesBytes = 182;
	/// <summary>The flag entries the <c>0xAA0180</c> object holds (12-byte entries from <c>+4</c> to <c>+0x10B</c>; its constructor <c>0x622800</c> starts the count at 0x16).</summary>
	public const int MaximumMapFlags = 22;
	private const int CompanyNameCharacters = 31;

	public int Theme { get; set; } = 3;
	public int Version { get; set; } = SupportedVersion;
	public int GoldenTickets { get; set; }
	/// <summary>Career <c>+0x2A..+0x31</c>: the park awards held.</summary>
	public byte[] Awards { get; set; } = new byte[8];
	public ThemeParkIncCdkEntry[] Cards { get; set; } = Enumerable.Repeat( ThemeParkIncCdkEntry.Empty, CardSlots ).ToArray();
	public int CardCount { get; set; }
	public int CardCursor { get; set; }
	public byte SwearFilter { get; set; }
	/// <summary>Career <c>+0x50</c>: the new-career flag (the prebuilt Science start is taken once).</summary>
	public byte NewCareer { get; set; }
	/// <summary>Career <c>+0x20..+0x29</c>.</summary>
	public byte[] Flags { get; set; } = new byte[10];
	public int Mission { get; set; }
	public int Rank { get; set; }
	public int FiringLine { get; set; }
	public byte[] Abilities { get; set; } = new byte[15];
	public byte ObstacleResearched { get; set; }
	public byte ObstacleRemoved { get; set; }
	/// <summary>The five exit-statistic status blocks (<c>+0x13E8</c>, 12 dwords each); null when the load stopped before them.</summary>
	public int[]? Statuses { get; set; } = new int[StatusWords];
	/// <summary>The opened map regions (<c>+0x1078</c>, 15 dwords); null when the load stopped before them.</summary>
	public int[]? Regions { get; set; } = new int[15];
	/// <summary>The zone records in file order; null when the load stopped before them.</summary>
	public List<ThemeParkIncZoneData>? Zones { get; set; } = new();
	/// <summary>The options' 59 bytes (<c>0x45A130</c>); null when the load stopped before them.</summary>
	public byte[]? OptionsBytesRaw { get; set; } = new byte[OptionsBytes];
	public byte GmiFlag { get; set; }
	public byte GameFinished { get; set; }
	/// <summary><c>mFirstHourTime</c> (global <c>0xBFCC4C</c>); null when the load stopped before it.</summary>
	public int? FirstHourTime { get; set; } = 0;
	/// <summary><c>mHistory[0x284]</c> (<c>+0x14F0</c>) raw; null when the load stopped before it.</summary>
	public byte[]? History { get; set; } = new byte[HistoryEntries * HistoryEntryBytes];
	/// <summary>The 96 bytes of <c>0x592720</c> on <c>0xA6C0A0</c>; null when the load stopped before them.</summary>
	public byte[]? Block592720 { get; set; } = new byte[Block592720Bytes];
	/// <summary>The shares' 182 bytes (<c>0x620A30</c>); null when the load stopped before them.</summary>
	public byte[]? SharesRaw { get; set; } = new byte[SharesBytes];
	/// <summary>The <c>0xAA0180</c> section (<c>u32 n</c>, n bytes, a byte, three dwords) raw; null when the load stopped before it.</summary>
	public byte[]? MapFlagsRaw { get; set; } = new byte[4 + 1 + 12];
	/// <summary>The ambient tag section (<c>u32 k</c>, then k × 16 bytes as written, raw <c>next</c> pointers included); null when the load stopped before it.</summary>
	public byte[]? AmbientRaw { get; set; } = new byte[4];

	/// <summary>How the load that made this object ended (<see cref="ThemeParkIncGlobalSaveOutcome.Complete"/> for one built in memory).</summary>
	public ThemeParkIncGlobalSaveOutcome Outcome { get; private set; }
	/// <summary>Why a load stopped or failed, for the log and the player.</summary>
	public string? OutcomeDetail { get; private set; }

	/// <summary>The options decoded, or null when not read.</summary>
	public ThemeParkIncOptions? Options => OptionsBytesRaw is { } raw ? DecodeOptions( raw ) : null;
	/// <summary>The shares decoded, or null when not read.</summary>
	public ThemeParkIncShares? Shares => SharesRaw is { } raw ? DecodeShares( raw ) : null;
	/// <summary>The ambient tags' count, or null when not read.</summary>
	public int? AmbientTagCount => AmbientRaw is { } raw ? BinaryPrimitives.ReadInt32LittleEndian( raw ) : null;

	/// <summary>
	/// Reads a global save as the original's loader <c>0x74D110</c> and body <c>0x74D840</c> do. A file the loader would fail on comes back <see cref="ThemeParkIncGlobalSaveOutcome.Failed"/>; one
	/// whose load stops early (a marker that differs) comes back <see cref="ThemeParkIncGlobalSaveOutcome.Stopped"/> with the later sections null. Throws only for a file that would make the
	/// original write outside an object (an <c>0xAA0180</c> count above its capacity).
	/// </summary>
	// [BIN:TPI-EXE:0x74D110 global save load] the header's theme and version words (a version below 0xC fails; any higher one is taken; the theme is not checked), then the body
	// [BIN:TPI-EXE:0x74D840 global save body] read branch: a field read that comes back short returns 1 (the load fails), a marker that differs or is short returns 0 (success, the rest keeps
	// its reset values), mFirstHourTime short returns 0; the 256 CDK entries' results are ignored (0x74B1F0), as are the shares' (0x6209F0 masks 0x620E30) and 0xAA0180's (0x622970 masks
	// 0x622D70), so their short reads only leave the stream where they stopped; 0x592720 and the ambient tags (0x5BF470) fail the load; the loader does not look past the last section
	public static ThemeParkIncGlobalSave Read( ReadOnlySpan<byte> data )
	{
		var save = new ThemeParkIncGlobalSave();
		var reader = new Reader( data );
		if ( !reader.TryI32( out var theme ) || !reader.TryI32( out var version ) )
			return save.Fail( "the header is cut short" );
		save.Theme = theme;
		save.Version = version;
		if ( version < SupportedVersion )
			return save.Fail( $"version {version} is below {SupportedVersion}" );
		// career body, fields up to +0x13E5: short = failure
		if ( !reader.TryI32( out var tickets ) || !reader.TryBytes( 8, out var awards ) )
			return save.Fail( "the career is cut short" );
		save.GoldenTickets = tickets;
		save.Awards = awards;
		for ( var index = 0; index < CardSlots; index++ )
		{
			// a short CDK entry is ignored by the body; only an end of file shortens it, and the count that follows then fails
			if ( reader.TryI32( out var id ) & reader.TryI32( out var level ) & reader.TryI32( out var flag ) & reader.TryI32( out var amount ) )
				save.Cards[index] = new ThemeParkIncCdkEntry( id, level, flag, amount );
		}
		if ( !reader.TryI32( out var count ) || !reader.TryI32( out var cursor ) || !reader.TryU8( out var swear ) || !reader.TryU8( out var newCareer )
			|| !reader.TryBytes( 10, out var flags ) || !reader.TryI32( out var mission ) || !reader.TryI32( out var rank ) || !reader.TryI32( out var firing )
			|| !reader.TryBytes( 15, out var abilities ) || !reader.TryU8( out var researched ) || !reader.TryU8( out var removed ) )
			return save.Fail( "the career is cut short" );
		(save.CardCount, save.CardCursor, save.SwearFilter, save.NewCareer, save.Flags, save.Mission, save.Rank, save.FiringLine, save.Abilities, save.ObstacleResearched, save.ObstacleRemoved) =
			(count, cursor, swear, newCareer, flags, mission, rank, firing, abilities, researched, removed);
		var statuses = new int[StatusWords];
		for ( var index = 0; index < StatusWords; index++ )
		{
			if ( !reader.TryI32( out statuses[index] ) )
				return save.Fail( "the exit statistics are cut short" );
		}
		save.Statuses = statuses;
		save.Regions = null;
		save.Zones = null;
		save.OptionsBytesRaw = null;
		save.FirstHourTime = null;
		save.History = null;
		save.Block592720 = null;
		save.SharesRaw = null;
		save.MapFlagsRaw = null;
		save.AmbientRaw = null;
		if ( !reader.Marker( "TATS" ) )
			return save.Stop( "TATS" );
		var regions = new int[15];
		for ( var index = 0; index < regions.Length; index++ )
		{
			if ( !reader.TryI32( out regions[index] ) )
				return save.Fail( "the regions are cut short" );
		}
		save.Regions = regions;
		if ( !reader.Marker( "AMTA" ) )
			return save.Stop( "AMTA" );
		if ( !reader.TryI32( out var zoneCount ) )
			return save.Fail( "the zone count is cut short" );
		var zones = new List<ThemeParkIncZoneData>();
		for ( var index = 0; index < zoneCount; index++ )
		{
			if ( ReadZone( ref reader ) is not { } zone )
				return save.Fail( "a zone record is cut short" );
			// [BIN:TPI-EXE:0x74EA30 duplicate zone] the map insert 0x751020 refuses a repeated name: "Something went wrong when trying...", the load fails
			if ( zones.Any( other => other.Name == zone.Name ) )
				return save.Fail( $"the zone '{zone.Name}' repeats" );
			zones.Add( zone );
		}
		save.Zones = zones;
		if ( !reader.Marker( "MEHT" ) )
			return save.Stop( "MEHT" );
		if ( !reader.TryBytes( OptionsBytes, out var options ) )
			return save.Fail( "the options are cut short" );
		save.OptionsBytesRaw = options;
		if ( !reader.TryU8( out var gmi ) || !reader.TryU8( out var finished ) )
			return save.Fail( "the career flags are cut short" );
		save.GmiFlag = gmi;
		save.GameFinished = finished;
		if ( !reader.TryI32( out var firstHour ) )
			return save.Stop( "mFirstHourTime" );
		save.FirstHourTime = firstHour;
		if ( !reader.TryBytes( HistoryEntries * HistoryEntryBytes, out var history ) )
			return save.Fail( "the history is cut short" );
		save.History = history;
		if ( !reader.TryBytes( Block592720Bytes, out var block ) )
			return save.Fail( "the 0xA6C0A0 block is cut short" );
		save.Block592720 = block;
		if ( !reader.Marker( "DTOT" ) )
			return save.Stop( "DTOT" );
		// the shares' errors are ignored: a short section leaves the stream at the end of the file, where the next marker read comes back short
		if ( !reader.TryBytes( SharesBytes, out var shares ) )
			return save.Stop( "shares" );
		save.SharesRaw = shares;
		if ( !reader.Marker( "RAHS" ) )
			return save.Stop( "RAHS" );
		if ( ReadMapFlags( ref reader ) is not { } mapFlags )
			return save.Stop( "0xAA0180" );
		save.MapFlagsRaw = mapFlags;
		if ( !reader.Marker( "MAPS" ) )
			return save.Stop( "MAPS" );
		if ( !reader.TryI32( out var ambientCount ) )
			return save.Fail( "the ambient tag count is cut short" );
		var ambientBytes = Math.Max( 0, ambientCount );
		if ( (long)ambientBytes * 16 > reader.Remaining || !reader.TryBytes( ambientBytes * 16, out var ambient ) )
			return save.Fail( "the ambient tags are cut short" );
		var ambientRaw = new byte[4 + ambient.Length];
		BinaryPrimitives.WriteInt32LittleEndian( ambientRaw, ambientCount );
		ambient.CopyTo( ambientRaw, 4 );
		save.AmbientRaw = ambientRaw;
		save.Outcome = ThemeParkIncGlobalSaveOutcome.Complete;
		return save;
	}

	private ThemeParkIncGlobalSave Fail( string reason )
	{
		Outcome = ThemeParkIncGlobalSaveOutcome.Failed;
		OutcomeDetail = reason;
		return this;
	}

	private ThemeParkIncGlobalSave Stop( string where )
	{
		Outcome = ThemeParkIncGlobalSaveOutcome.Stopped;
		OutcomeDetail = $"the load stopped at {where}";
		return this;
	}

	// [BIN:TPI-EXE:0x74FEF0 zone record] read branch: u32 name length and its bytes (0x74D840 reads them byte by byte), then six bytes, four (byte, dword) and eight (byte, dword) pairs,
	// 33 (A, B) UTF-16 pairs, d_available, d_open, mParkNumber, mNameChanged, mAllResearchCompleted, mChallengesDone, mAllChallengesDone, mCurrentTechLevel; any short field fails
	private static ThemeParkIncZoneData? ReadZone( ref Reader reader )
	{
		if ( !reader.TryI32( out var nameLength ) || (uint)nameLength > reader.Remaining || !reader.TryBytes( nameLength, out var name ) )
			return null;
		var zone = new ThemeParkIncZoneData { NameBytes = name };
		if ( !reader.TryBytes( 6, out var oneShot ) )
			return null;
		zone.OneShot = oneShot;
		for ( var index = 0; index < 4; index++ )
		{
			if ( !reader.TryU8( out var flag ) || !reader.TryI32( out var value ) )
				return null;
			zone.Optional[index] = (flag, value);
		}
		for ( var index = 0; index < 8; index++ )
		{
			if ( !reader.TryU8( out var flag ) || !reader.TryI32( out var value ) )
				return null;
			zone.Awards[index] = (flag, value);
		}
		for ( var index = 0; index < ThemeParkIncZoneData.SignCharacters; index++ )
		{
			if ( !reader.TryU16( out zone.SignA[index] ) || !reader.TryU16( out zone.SignB[index] ) )
				return null;
		}
		if ( !reader.TryU8( out var available ) || !reader.TryU8( out var open ) || !reader.TryI32( out var park ) || !reader.TryU8( out var nameChanged ) || !reader.TryU8( out var allResearch )
			|| !reader.TryI32( out var challenges ) || !reader.TryU8( out var allChallenges ) || !reader.TryI32( out var tech ) )
			return null;
		(zone.Available, zone.Open, zone.ParkNumber, zone.NameChanged, zone.AllResearchCompleted, zone.ChallengesDone, zone.AllChallengesDone, zone.CurrentTechLevel) =
			(available, open, park, nameChanged, allResearch, challenges, allChallenges, tech);
		return zone;
	}

	// [BIN:TPI-EXE:0x622D70 map flags read] u32 n into the object's first word, n bytes at +0xC + 12i, the byte +0x118 and the dwords +0x13C, +0x144, +0x14C; the object holds 22 entries
	// (0x622800), so a larger n writes past them: such a file is refused
	private static byte[]? ReadMapFlags( ref Reader reader )
	{
		if ( !reader.TryI32( out var count ) )
			return null;
		if ( count > MaximumMapFlags )
			throw new InvalidDataException( $"Global save map flag count {count} is above the {MaximumMapFlags} the original object holds." );
		var flags = Math.Max( 0, count );
		if ( !reader.TryBytes( flags + 1 + 12, out var rest ) )
			return null;
		var raw = new byte[4 + rest.Length];
		BinaryPrimitives.WriteInt32LittleEndian( raw, count );
		rest.CopyTo( raw, 4 );
		return raw;
	}

	/// <summary>
	/// The writer <c>0x74C8F0</c> with the body's write branch: the header (theme, version 12), then every section. Throws when a section is missing (a model from a load that stopped early
	/// must have them filled first).
	/// </summary>
	// [BIN:TPI-EXE:0x74C8F0 global save write] the theme word (3 when there is none), the version constant 0xC, then 0x74D840(file, 1, 0xC) in the same order as the read branch, the markers
	// written as dwords 0x53544154 "TATS", 0x41544D41 "AMTA", 0x5448454D "MEHT", 0x544F5444 "DTOT", 0x53484152 "RAHS", 0x5350414D "MAPS"
	public byte[] Write()
	{
		if ( Statuses is not { Length: StatusWords } || Regions is not { Length: 15 } || Zones == null || OptionsBytesRaw is not { Length: OptionsBytes } || FirstHourTime == null
			|| History is not { Length: HistoryEntries * HistoryEntryBytes } || Block592720 is not { Length: Block592720Bytes } || SharesRaw is not { Length: SharesBytes } || MapFlagsRaw == null || AmbientRaw == null
			|| Awards is not { Length: 8 } || Cards is not { Length: CardSlots } || Flags is not { Length: 10 } || Abilities is not { Length: 15 } )
			throw new InvalidOperationException( "The global save is missing a section." );
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		writer.Write( Theme );
		writer.Write( SupportedVersion );
		writer.Write( GoldenTickets );
		writer.Write( Awards );
		foreach ( var card in Cards )
		{
			writer.Write( card.Id );
			writer.Write( card.Level );
			writer.Write( card.Flag );
			writer.Write( card.Amount );
		}
		writer.Write( CardCount );
		writer.Write( CardCursor );
		writer.Write( SwearFilter );
		writer.Write( NewCareer );
		writer.Write( Flags );
		writer.Write( Mission );
		writer.Write( Rank );
		writer.Write( FiringLine );
		writer.Write( Abilities );
		writer.Write( ObstacleResearched );
		writer.Write( ObstacleRemoved );
		foreach ( var word in Statuses )
			writer.Write( word );
		writer.Write( "TATS"u8 );
		foreach ( var region in Regions )
			writer.Write( region );
		writer.Write( "AMTA"u8 );
		writer.Write( Zones.Count );
		foreach ( var zone in Zones )
		{
			writer.Write( zone.NameBytes.Length );
			writer.Write( zone.NameBytes );
			writer.Write( zone.OneShot );
			foreach ( var (flag, value) in zone.Optional )
			{
				writer.Write( flag );
				writer.Write( value );
			}
			foreach ( var (flag, value) in zone.Awards )
			{
				writer.Write( flag );
				writer.Write( value );
			}
			for ( var index = 0; index < ThemeParkIncZoneData.SignCharacters; index++ )
			{
				writer.Write( zone.SignA[index] );
				writer.Write( zone.SignB[index] );
			}
			writer.Write( zone.Available );
			writer.Write( zone.Open );
			writer.Write( zone.ParkNumber );
			writer.Write( zone.NameChanged );
			writer.Write( zone.AllResearchCompleted );
			writer.Write( zone.ChallengesDone );
			writer.Write( zone.AllChallengesDone );
			writer.Write( zone.CurrentTechLevel );
		}
		writer.Write( "MEHT"u8 );
		writer.Write( OptionsBytesRaw );
		writer.Write( GmiFlag );
		writer.Write( GameFinished );
		writer.Write( FirstHourTime.Value );
		writer.Write( History );
		writer.Write( Block592720 );
		writer.Write( "DTOT"u8 );
		writer.Write( SharesRaw );
		writer.Write( "RAHS"u8 );
		writer.Write( MapFlagsRaw );
		writer.Write( "MAPS"u8 );
		writer.Write( AmbientRaw );
		writer.Flush();
		return stream.ToArray();
	}

	private static ThemeParkIncOptions DecodeOptions( byte[] raw )
	{
		var reader = new Reader( raw );
		ThemeParkIncVolume Volume( ref Reader source )
		{
			source.TryI32( out var on );
			source.TryI32( out var level );
			return new ThemeParkIncVolume( (on & 0xFF) != 0, level );
		}
		var a = Volume( ref reader );
		var b = Volume( ref reader );
		var speech = Volume( ref reader );
		var movie = Volume( ref reader );
		reader.TryBytes( 8, out var flags );
		reader.TryU16( out var speed );
		reader.TryU8( out _ );
		reader.TryF32( out var zoomMin );
		reader.TryF32( out var zoomMax );
		reader.TryF32( out var fps );
		reader.TryI32( out var music );
		return new ThemeParkIncOptions( a, b, speech, movie, flags[0] != 0, flags[1] != 0, flags[2] != 0, flags[3] != 0, flags[4] != 0, flags[5] != 0, flags[6] != 0, flags[7] != 0,
			speed, zoomMin, zoomMax, fps, music );
	}

	private static ThemeParkIncShares DecodeShares( byte[] raw )
	{
		var reader = new Reader( raw );
		var present = new bool[3];
		for ( var index = 0; index < present.Length; index++ )
		{
			reader.TryI32( out var flag );
			present[index] = flag == 1;
			reader.TryBytes( 4 * 4 + 8 + 8, out _ );
		}
		reader.TryI32( out var owned );
		reader.TryI32( out var percent );
		var name = new char[CompanyNameCharacters];
		for ( var index = 0; index < name.Length; index++ )
		{
			reader.TryU16( out var unit );
			name[index] = (char)unit;
		}
		reader.TryI32( out var value );
		return new ThemeParkIncShares( Array.AsReadOnly( present ), owned, percent, Text( name ), value );
	}

	/// <summary>A UTF-16 field up to its first NUL; an unpaired surrogate before the NUL is refused.</summary>
	public static string Text( char[] characters )
	{
		var end = Array.IndexOf( characters, '\0' );
		if ( end < 0 )
			end = characters.Length;
		for ( var index = 0; index < end; index++ )
		{
			if ( char.IsHighSurrogate( characters[index] ) )
			{
				if ( index + 1 >= end || !char.IsLowSurrogate( characters[index + 1] ) )
					throw new InvalidDataException( $"Global save text has an unpaired UTF-16 surrogate at code unit {index}." );
				index++;
			}
			else if ( char.IsLowSurrogate( characters[index] ) )
				throw new InvalidDataException( $"Global save text has an unpaired UTF-16 surrogate at code unit {index}." );
		}
		return new string( characters, 0, end );
	}

	private ref struct Reader
	{
		private readonly ReadOnlySpan<byte> data;
		private int position;

		public Reader( ReadOnlySpan<byte> data ) => this.data = data;

		public int Remaining => data.Length - position;

		public bool TryBytes( int count, out byte[] bytes )
		{
			if ( count < 0 || Remaining < count )
			{
				// a short read consumes what is left, as ReadFile does at the end of a file
				position = data.Length;
				bytes = Array.Empty<byte>();
				return false;
			}
			bytes = data.Slice( position, count ).ToArray();
			position += count;
			return true;
		}

		public bool TryU8( out byte value )
		{
			var ok = TryBytes( 1, out var bytes );
			value = ok ? bytes[0] : (byte)0;
			return ok;
		}

		public bool TryU16( out ushort value )
		{
			var ok = TryBytes( 2, out var bytes );
			value = ok ? BinaryPrimitives.ReadUInt16LittleEndian( bytes ) : (ushort)0;
			return ok;
		}

		public bool TryI32( out int value )
		{
			var ok = TryBytes( 4, out var bytes );
			value = ok ? BinaryPrimitives.ReadInt32LittleEndian( bytes ) : 0;
			return ok;
		}

		public bool TryF32( out float value )
		{
			var ok = TryBytes( 4, out var bytes );
			value = ok ? BinaryPrimitives.ReadSingleLittleEndian( bytes ) : 0;
			return ok;
		}

		// [BIN:TPI-EXE:0x74E7EE marker read] the marker dword is read without a count check into a stack word that holds an earlier value (a loop count, the version, the previous marker, or after
		// the zones the last record's heap address, inferred to be below 0x54000000), whose missing bytes never complete the marker: differing or short both stop the load
		public bool Marker( string tag ) => TryBytes( 4, out var bytes ) && bytes.AsSpan().SequenceEqual( Encoding.ASCII.GetBytes( tag ) );
	}
}
