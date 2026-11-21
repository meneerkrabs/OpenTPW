namespace OpenTPW;

/// <summary>
/// The Theme Park Inc player file (docs/reverse/TPI-CAREER.md §3.4): the player's career as a Global Master Save, <c>save\users\1New Player\New Player.GMS</c> in the save folder. Enter Game and Load A
/// Game take the career from it; saving the Science park and leaving a park write it.
/// </summary>
internal sealed partial class GameFlow
{
	/// <summary>
	/// The one player's name: the Personnel File's first slot, "New Player" (UITEXT 20 in English). The folder takes the English text in every language, so a language change keeps the career.
	/// </summary>
	// [EXT:tpi-front-end-one-player] OpenTPW has one player, the first slot's "New Player"; the original names the folder after the name the player typed
	internal const string ThemeParkIncPlayerName = "New Player";

	/// <summary>The session sections of the last player file loaded (the original keeps them in their own objects), or null before one was.</summary>
	private ThemeParkIncGlobalSave? incSession;

	/// <summary>Set while a load replaces the running park: the original's load discards it without writing the player file.</summary>
	private bool incReplacingPark;

	/// <summary>The player file's path: <c>users\&lt;slot + 1&gt;&lt;name&gt;\&lt;name&gt;.GMS</c> in the save folder.</summary>
	// [BIN:TPI-EXE:0x777B80 player file path] 0x7763E0(slot) = <root>\save\users\<slot+1><name> (0x776070, 0x776210, 0x776300), then "\" + the name + ".GMS" (".GMI" for the refresh flag)
	internal static string ThemeParkIncPlayerFilePath() =>
		SaveFileSystem.GetAbsolutePath( $"users/1{ThemeParkIncPlayerName}/{ThemeParkIncPlayerName}.GMS" );

	/// <summary>
	/// The player's career from the player file, its current zone <paramref name="zone"/>, or null when there is no file. A file whose load fails is refused; one whose load stops early is
	/// taken with the reset's values after the stop, as the original's loader leaves them.
	/// </summary>
	// [BIN:TPI-EXE:0x74D110 global save load] a failed load returns 0 to the caller; a marker that differs is a successful load
	private ThemeParkIncCareer? LoadThemeParkIncPlayerCareer( string zone )
	{
		if ( SaveFileSystem == null )
			return null;
		var path = ThemeParkIncPlayerFilePath();
		if ( !File.Exists( path ) )
			return null;
		var save = ThemeParkIncGlobalSave.Read( File.ReadAllBytes( path ) );
		var career = new ThemeParkIncCareer( ParkEconomy.GameCareerFiles.Instance, BalanceSettings.LoadThemeParkInc( zone ).ThemeParkIncFiringLines );
		var outcome = career.Load( save, zone );
		if ( outcome == ThemeParkIncGlobalSaveOutcome.Failed )
			throw new InvalidDataException( $"The Theme Park Inc player file could not be loaded ({save.OutcomeDetail}): {path}" );
		if ( outcome == ThemeParkIncGlobalSaveOutcome.Stopped )
			Log.Warning( $"Theme Park Inc player file: {save.OutcomeDetail}; the career keeps its reset values after that point." );
		var fresh = NewThemeParkIncSession();
		save.OptionsBytesRaw ??= fresh.OptionsBytesRaw;
		save.FirstHourTime ??= fresh.FirstHourTime;
		save.Block592720 ??= fresh.Block592720;
		save.SharesRaw ??= fresh.SharesRaw;
		save.MapFlagsRaw ??= fresh.MapFlagsRaw;
		save.AmbientRaw ??= fresh.AmbientRaw;
		incSession = save;
		Log.Trace( $"Theme Park Inc player file loaded: mission {career.MissionCounter}, rank {career.Rank}, firing line {career.FiringLine}, {career.GoldenTickets} golden tickets." );
		return career;
	}

	private static ThemeParkIncGlobalSave NewThemeParkIncSession()
	{
		// [BIN:TPI-EXE:0x620CFB company name] the shares writer formats TAG_SYSTEM's entry 0xAC into an empty company name
		var tags = GameLanguage.Current.LoadStrings( "TAG_SYSTEM.str" );
		return ThemeParkIncSession.New( ParkEconomy.GameCareerFiles.Instance.Read, tags.Entries.Length > 0xAC ? tags.Entries[0xAC] : "" );
	}

	/// <summary>
	/// Writes the player file from <paramref name="level"/>'s career (the original writes the global save before a park save's park files, and when a park is left): atomically, in the
	/// save folder.
	/// </summary>
	// [BIN:TPI-EXE:0x745720 park save] the global save is written first (0x777AF0) and the park files after it; [BIN:TPI-EXE:0x6A47B0 leave park] leaving a park saves it (0x744E10), outside the
	// coaster editor and online modes
	private void WriteThemeParkIncPlayerFile( Level level )
	{
		if ( OperatingSystem.IsBrowser() || level.IsReadOnlyVisit || level.CoopRole != CoopRole.None || level.Park?.Economy.TpiCareer is not { } career )
			return;
		var theme = ThemeParkIncCareer.ZoneNames.ToList().IndexOf( level.LevelName );
		var bytes = career.ToGlobalSave( incSession ??= NewThemeParkIncSession(), theme, refresh: false ).Write();
		var path = ThemeParkIncPlayerFilePath();
		Directory.CreateDirectory( Path.GetDirectoryName( path )! );
		var temporary = path + ".tmp";
		File.WriteAllBytes( temporary, bytes );
		File.Move( temporary, path, overwrite: true );
		Log.Trace( $"Theme Park Inc player file written: {path} (mission {career.MissionCounter})." );
	}

	/// <summary>Writes the player file as a Theme Park Inc park is left; a failure is logged, as leaving goes on.</summary>
	private void WriteThemeParkIncPlayerFileOnLeave()
	{
		if ( incReplacingPark || SaveFileSystem == null || Level is not { IsThemeParkIncSlice: true } level )
			return;
		try
		{
			WriteThemeParkIncPlayerFile( level );
		}
		catch ( Exception error ) when ( error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or NotSupportedException )
		{
			Log.Warning( $"Theme Park Inc player file not written: {error.Message}" );
		}
	}
}
