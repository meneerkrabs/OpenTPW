using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// The global save's session sections for a player file written before any was loaded (docs/reverse/TPI-CAREER.md §3.3): the options, <c>mFirstHourTime</c>, the <c>0xA6C0A0</c> block, the
/// shares, the <c>0xAA0180</c> flags and the ambient tags as a new Theme Park Inc session holds them.
/// </summary>
// [EXT:tpi-capability-gates] Theme Park Inc's options, ambient-tag and 0xA6C0A0/0xAA0180 systems are not ported: OpenTPW carries their sections through from the last player file it
// loaded and gives a new player file their start-up state (the shares game is ported and kept with the career, ThemeParkIncSharesGame, which writes its own section); the start-up's hardware match of Config.sam (0x423120, CPU, video and main memory against Config[i]) is not made, so its
// DefaultConfig entry applies, as on a machine no entry matches
public static class ThemeParkIncSession
{
	/// <summary>The session sections of a new session, as a global save whose career sections are the defaults (the caller writes the career over them).</summary>
	/// <param name="read">The game file reader (for <c>sound.sam</c> and <c>Config.sam</c>).</param>
	/// <param name="companyName">The text of <c>TAG_SYSTEM.str</c> entry 172 in the game's language (English "Theme Park Inc.").</param>
	public static ThemeParkIncGlobalSave New( Func<string, byte[]?> read, string companyName )
	{
		ArgumentNullException.ThrowIfNull( read );
		ArgumentNullException.ThrowIfNull( companyName );
		var mapFlags = new byte[4 + 17 + 1 + 12];
		// [BIN:TPI-EXE:0x622800 map flags constructor] 22 entries less the five kinds the byte map 0x622904 skips: n = 17, every flag byte 0, +0x118 and the three dwords 0
		BinaryPrimitives.WriteInt32LittleEndian( mapFlags, 17 );
		var ambient = new byte[4];
		return new ThemeParkIncGlobalSave
		{
			OptionsBytesRaw = Options( read ),
			// [BIN:TPI-EXE:0xBFCC4C first hour time] a BSS dword with no initialiser (0x64947F counts it up per game hour, 0x6A4442 resets it at a level start)
			FirstHourTime = 0,
			// [BIN:TPI-EXE:0x591F10 0xA6C0A0 initialiser] +0x60 = 0 and +0x04..+0x5F zero-filled
			Block592720 = new byte[ThemeParkIncGlobalSave.Block592720Bytes],
			SharesRaw = Shares( companyName ),
			MapFlagsRaw = mapFlags,
			// [BIN:TPI-EXE:0x5BD7B0 ambient list constructor] the list head +0x11C is 0: the writer's count is 0
			AmbientRaw = ambient
		};
	}

	/// <summary>The options' 59 bytes after the start-up (<c>0x459520</c> over <c>sound.sam</c>, then <c>Config.sam</c>'s default entry).</summary>
	// [BIN:TPI-EXE:0x459520 options constructor] the four (on, level) pairs from sound.sam's SoundInfo.SOUNDON and DefaultVolume.SFX/MUSIC/SPEECH/MOVIE (the SoundInfo object 0xA03B20, keys
	// that are missing read 0), the eight flags 01 01 01 01 00 01 00 01, GameSpeed 100, +0x40 = 1, zoom 40/300, FPS clip 10, MusicEnabled 1; [BIN:TPI-EXE:0x423120 config defaults] then
	// Config[k] with k = Defaults.DefaultConfig: MinZoom, MaxZoom, DefaultGameSpeed clamped to 10..400, FPSClip when above 0, PlayMusic (0 also zeroes the music level, 0x45BA78)
	private static byte[] Options( Func<string, byte[]?> read )
	{
		var sound = Settings( read, "/sound.sam" );
		var config = Settings( read, "/Config.sam" );
		var on = sound.GetLong( "SoundInfo.SOUNDON", 0L ) != 0 ? (byte)1 : (byte)0;
		var music = (int)sound.GetLong( "DefaultVolume.MUSIC", 0L );
		var index = (int)config.GetLong( "Defaults.DefaultConfig", 0L );
		var speed = (int)Math.Clamp( config.GetLong( $"Config[{index}].DefaultGameSpeed", 0L ), 10, 400 );
		var fps = config.GetLong( $"Config[{index}].FPSClip", 0L ) is var clip and > 0 ? clip : 10;
		var playMusic = (int)config.GetLong( $"Config[{index}].PlayMusic", 0L );
		if ( playMusic == 0 )
			music = 0;
		var raw = new byte[ThemeParkIncGlobalSave.OptionsBytes];
		void Volume( int at, long level )
		{
			raw[at] = on;
			BinaryPrimitives.WriteInt32LittleEndian( raw.AsSpan( at + 4 ), (int)level );
		}
		Volume( 0, sound.GetLong( "DefaultVolume.SFX", 0L ) );
		Volume( 8, music );
		Volume( 16, sound.GetLong( "DefaultVolume.SPEECH", 0L ) );
		Volume( 24, sound.GetLong( "DefaultVolume.MOVIE", 0L ) );
		var span = raw.AsSpan();
		new byte[] { 1, 1, 1, 1, 0, 1, 0, 1 }.CopyTo( span[32..] );
		BinaryPrimitives.WriteUInt16LittleEndian( span[40..], (ushort)speed );
		span[42] = 1;
		BinaryPrimitives.WriteSingleLittleEndian( span[43..], config.GetLong( $"Config[{index}].MinZoom", 0L ) );
		BinaryPrimitives.WriteSingleLittleEndian( span[47..], config.GetLong( $"Config[{index}].MaxZoom", 0L ) );
		BinaryPrimitives.WriteSingleLittleEndian( span[51..], fps );
		BinaryPrimitives.WriteInt32LittleEndian( span[55..], playMusic );
		return raw;
	}

	/// <summary>The shares' 182 bytes after the reset: no company slot, the holding and value -1 and the company name.</summary>
	// [BIN:TPI-EXE:0x621210 shares reset] *obj (0x48 bytes) = (-1, -1, L"" copied from 0xA0B714), obj[1] (0x44) = (-1, L""), the three slot pointers 0; [BIN:TPI-EXE:0x620A30 shares write]
	// a null slot writes 36 zero bytes, then mNumberOwned and mPctCompanySharesOwned (*obj +0, +4), mCompanyName[31] (an empty name is first formatted from TAG_SYSTEM entry 0xAC, at most 0x1E
	// characters, 0x620CEF..0x620D0A) and mCurrentValue (obj[1] +0)
	// [EXT:original-undefined-reads] the name's characters after its terminator are uninitialised heap in the original: 0 here
	private static byte[] Shares( string companyName )
	{
		var raw = new byte[ThemeParkIncGlobalSave.SharesBytes];
		var span = raw.AsSpan();
		BinaryPrimitives.WriteInt32LittleEndian( span[108..], -1 );
		BinaryPrimitives.WriteInt32LittleEndian( span[112..], -1 );
		var name = companyName.Length > 0x1E ? companyName[..0x1E] : companyName;
		for ( var index = 0; index < name.Length; index++ )
			BinaryPrimitives.WriteUInt16LittleEndian( span[(116 + 2 * index)..], name[index] );
		BinaryPrimitives.WriteInt32LittleEndian( span[178..], -1 );
		return raw;
	}

	private static SamSettings Settings( Func<string, byte[]?> read, string path ) => new( read( path ) is { } bytes
		? new[] { SamDocument.Parse( System.Text.Encoding.Latin1.GetString( bytes ), path ) }
		: Array.Empty<SamDocument>() );
}
