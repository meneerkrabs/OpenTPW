using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>One entry of the card table. Empty slots carry <see cref="Flag"/> = <see cref="int.MaxValue"/>.</summary>
/// <param name="Level">0–2 in the observed files, the same range as a park's <c>mCurrentTechLevel</c>; meaning unverified.</param>
/// <param name="Flag">0 or 1 in the observed files; meaning unverified.</param>
/// <param name="Amount">A money-like value (7,500–15,000 in the observed files); meaning unverified.</param>
public readonly record struct ThemeParkIncCard( int InfoId, int Level, int Flag, int Amount )
{
	public bool IsEmpty => Flag == int.MaxValue;
}

/// <summary>A volume setting stored as an on/off word and a level.</summary>
public readonly record struct ThemeParkIncVolume( bool Enabled, int Level );

/// <summary>
/// One park of the global save, in the order the original reads it (0x0074FEF0). Field names are the original
/// serializer's name strings where it passes one.
/// </summary>
public sealed record ThemeParkIncParkProgress(
	string Name, string SignLine1, string SignLine2, bool Available, bool Open, int ParkNumber, bool NameChanged,
	bool AllResearchCompleted, int ChallengesDone, bool AllChallengesDone, int CurrentTechLevel );

/// <summary>Game options (0x0045A130). <see cref="VolumeA"/>/<see cref="VolumeB"/> are written without a name string.</summary>
public sealed record ThemeParkIncOptions(
	ThemeParkIncVolume VolumeA, ThemeParkIncVolume VolumeB, ThemeParkIncVolume Speech, ThemeParkIncVolume Movie,
	bool AdvisorOn, bool TutorialOn, bool TooltipsOn, bool ConfirmDeleteOn, bool RmbScrollOn, bool RmbCancelOn,
	bool IsometricOn, bool AnimateMenus, int GameSpeed, float ZoomMin, float ZoomMax, float FpsClip, int MusicEnabled );

/// <summary>The shares game (0x00620E30): three company slots and the player's holding.</summary>
public sealed record ThemeParkIncShares( IReadOnlyList<bool> SlotsPresent, int NumberOwned, int PercentCompanySharesOwned, string CompanyName, int CurrentValue );

/// <summary>
/// Theme Park Inc's Global Master Save (<c>.GMS</c>): the player's progress across the worlds, not a park. The layout
/// follows the original's load path (save writer 0x0074C8F0, loader 0x0074D110, body 0x0074D840 in the Theme Park Inc
/// executable; docs/THEME-PARK-INC.md). The file is uncompressed little-endian and checks six markers; this reader
/// requires every one, version 12 and the exact file length. Sections whose meaning is not traced are read and
/// skipped.
/// </summary>
public sealed class ThemeParkIncGlobalSave
{
	public const int SupportedVersion = 12;
	public const int CardSlots = 256;
	public const int HistoryEntries = 0x284;
	public const int HistoryEntryBytes = 0x14;
	public const int MaximumParks = 64;
	public const int MaximumNameBytes = 1024;
	public const int MaximumMapFlags = 4096;
	public const int MaximumAmbientTags = 4096;
	private const int SignCharacters = 33;
	private const int CompanyNameCharacters = 31;

	private ThemeParkIncGlobalSave() { }

	/// <summary>World (0–2) the save was made in; the writer stores 3 when there is none.</summary>
	public int World { get; private init; }
	public int Version { get; private init; }
	/// <summary>Mission number (object +0x105C); the loader then reads <c>Miss%02d.sam</c> of the world.</summary>
	public int Mission { get; private init; }
	public IReadOnlyList<ThemeParkIncCard> Cards { get; private init; } = Array.Empty<ThemeParkIncCard>();
	public IReadOnlyList<ThemeParkIncParkProgress> Parks { get; private init; } = Array.Empty<ThemeParkIncParkProgress>();
	public ThemeParkIncOptions Options { get; private init; } = null!;
	public ThemeParkIncShares Shares { get; private init; } = null!;
	/// <summary><c>mFirstHourTime</c>.</summary>
	public int FirstHourTime { get; private init; }
	public int AmbientTagCount { get; private init; }

	// [BIN:TPI-EXE:0x0074D840 global save body] the loader 0x0074D110 reads the world and version words, then this body and its sub-serializers (0x0074B1F0 card, 0x0074FEF0 park, 0x0045A130 options, 0x00592720, 0x00620E30 shares, 0x00622D70, 0x005BF470 ambient tags) and rejects the file when a marker (TATS, AMTA, MEHT, DTOT, RAHS, MAPS) differs
	public static ThemeParkIncGlobalSave Read( ReadOnlySpan<byte> data )
	{
		var reader = new Reader( data );
		var world = reader.I32();
		var version = reader.I32();
		if ( version != SupportedVersion )
			throw new NotSupportedException( $"Global save version {version}; only version {SupportedVersion} is known." );
		if ( world is < 0 or > 3 )
			throw new InvalidDataException( $"Global save world {world} is out of range." );
		// Body (0x0074D840): +0x1074, eight bytes, the card table, two words, twelve bytes, +0x105C, +0x1060, +0, fifteen bytes, two bytes.
		reader.Skip( 4 + 8 );
		var cards = new ThemeParkIncCard[CardSlots];
		for ( var index = 0; index < cards.Length; index++ )
			cards[index] = new ThemeParkIncCard( reader.I32(), reader.I32(), reader.I32(), reader.I32() );
		reader.Skip( 4 + 4 + 1 + 1 + 10 );
		var mission = reader.I32();
		reader.Skip( 4 + 4 + 15 + 1 + 1 );
		reader.Skip( 5 * 0x30 );
		reader.Marker( "TATS" );
		reader.Skip( 15 * 4 );
		reader.Marker( "AMTA" );
		var parkCount = reader.I32();
		if ( parkCount is < 0 or > MaximumParks )
			throw new InvalidDataException( $"Global save park count {parkCount} is out of range." );
		var parks = new ThemeParkIncParkProgress[parkCount];
		for ( var index = 0; index < parks.Length; index++ )
			parks[index] = ReadPark( ref reader );
		reader.Marker( "MEHT" );
		var options = ReadOptions( ref reader );
		reader.Skip( 1 + 1 );
		var firstHourTime = reader.I32();
		reader.Skip( HistoryEntries * HistoryEntryBytes );
		// Shared tail: 0x00592720, 'DTOT', shares (0x00620E30), 'RAHS', 0x00622D70, 'MAPS', ambient tags (0x005BF470).
		reader.Skip( 4 + 0x5C );
		reader.Marker( "DTOT" );
		var shares = ReadShares( ref reader );
		reader.Marker( "RAHS" );
		var mapFlags = reader.I32();
		if ( mapFlags is < 0 or > MaximumMapFlags )
			throw new InvalidDataException( $"Global save map flag count {mapFlags} is out of range." );
		reader.Skip( mapFlags + 1 + 3 * 4 );
		reader.Marker( "MAPS" );
		var ambient = reader.I32();
		if ( ambient is < 0 or > MaximumAmbientTags )
			throw new InvalidDataException( $"Global save ambient tag count {ambient} is out of range." );
		reader.Skip( ambient * 16 );
		if ( !reader.AtEnd )
			throw new InvalidDataException( $"Global save has {reader.Remaining} bytes after its last section." );
		return new ThemeParkIncGlobalSave
		{
			World = world, Version = version, Mission = mission, Cards = Array.AsReadOnly( cards ), Parks = Array.AsReadOnly( parks ),
			Options = options, Shares = shares, FirstHourTime = firstHourTime, AmbientTagCount = ambient,
		};
	}

	private static ThemeParkIncParkProgress ReadPark( ref Reader reader )
	{
		var nameBytes = reader.I32();
		if ( nameBytes is < 0 or > MaximumNameBytes )
			throw new InvalidDataException( $"Global save park name length {nameBytes} is out of range." );
		var name = reader.Latin1( nameBytes );
		// 0x0074FEF0: six bytes, four and eight (byte, word) pairs, then the named fields.
		reader.Skip( 6 + 4 * 5 + 8 * 5 );
		var line1 = new char[SignCharacters];
		var line2 = new char[SignCharacters];
		for ( var index = 0; index < SignCharacters; index++ )
		{
			line1[index] = (char)reader.U16();
			line2[index] = (char)reader.U16();
		}
		var available = reader.U8() != 0;
		var open = reader.U8() != 0;
		var parkNumber = reader.I32();
		var nameChanged = reader.U8() != 0;
		var allResearch = reader.U8() != 0;
		var challenges = reader.I32();
		var allChallenges = reader.U8() != 0;
		var techLevel = reader.I32();
		return new ThemeParkIncParkProgress( name, Text( line1 ), Text( line2 ), available, open, parkNumber, nameChanged, allResearch, challenges, allChallenges, techLevel );
	}

	private static ThemeParkIncOptions ReadOptions( ref Reader reader )
	{
		ThemeParkIncVolume Volume( ref Reader source ) => new( source.I32() != 0, source.I32() );
		var a = Volume( ref reader );
		var b = Volume( ref reader );
		var speech = Volume( ref reader );
		var movie = Volume( ref reader );
		var flags = new bool[8];
		for ( var index = 0; index < flags.Length; index++ )
			flags[index] = reader.U8() != 0;
		var speed = reader.U16();
		reader.Skip( 1 );
		return new ThemeParkIncOptions( a, b, speech, movie, flags[0], flags[1], flags[2], flags[3], flags[4], flags[5], flags[6], flags[7],
			speed, reader.F32(), reader.F32(), reader.F32(), reader.I32() );
	}

	private static ThemeParkIncShares ReadShares( ref Reader reader )
	{
		var present = new bool[3];
		for ( var index = 0; index < present.Length; index++ )
		{
			present[index] = reader.I32() == 1;
			reader.Skip( 4 * 4 + 8 + 8 );
		}
		var owned = reader.I32();
		var percent = reader.I32();
		var name = new char[CompanyNameCharacters];
		for ( var index = 0; index < name.Length; index++ )
			name[index] = (char)reader.U16();
		return new ThemeParkIncShares( Array.AsReadOnly( present ), owned, percent, Text( name ), reader.I32() );
	}

	private static string Text( char[] characters )
	{
		var end = Array.IndexOf( characters, '\0' );
		return new string( characters, 0, end < 0 ? characters.Length : end );
	}

	private ref struct Reader
	{
		private readonly ReadOnlySpan<byte> data;
		private int position;

		public Reader( ReadOnlySpan<byte> data ) => this.data = data;

		public bool AtEnd => position == data.Length;
		public int Remaining => data.Length - position;

		private ReadOnlySpan<byte> Take( int count )
		{
			if ( count < 0 || data.Length - position < count )
				throw new InvalidDataException( $"Global save is truncated at byte {position}." );
			var slice = data.Slice( position, count );
			position += count;
			return slice;
		}

		public void Skip( int count ) => Take( count );
		public byte U8() => Take( 1 )[0];
		public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian( Take( 2 ) );
		public int I32() => BinaryPrimitives.ReadInt32LittleEndian( Take( 4 ) );
		public float F32() => BinaryPrimitives.ReadSingleLittleEndian( Take( 4 ) );

		public string Latin1( int count )
		{
			var bytes = Take( count );
			var end = bytes.IndexOf( (byte)0 );
			return Encoding.Latin1.GetString( end < 0 ? bytes : bytes[..end] );
		}

		public void Marker( string tag )
		{
			var at = position;
			if ( !Take( 4 ).SequenceEqual( Encoding.ASCII.GetBytes( tag ) ) )
				throw new InvalidDataException( $"Global save marker '{tag}' is missing at byte {at}." );
		}
	}
}
