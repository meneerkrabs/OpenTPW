namespace OpenTPW;

public sealed partial class ParkEconomy
{
	/// <summary>
	/// Theme Park Inc's career (docs/reverse/TPI-CAREER.md): the mission counter, the rank, the firing line, the golden tickets and the zone records the park's challenges and staff read and
	/// write. The original's career is a global shared by every zone (<c>0xBFC484</c>); a game attaches the player's career here (<see cref="AttachThemeParkIncCareer"/>), a new park starts with a
	/// new one. Null in Theme Park World.
	/// </summary>
	public ThemeParkIncCareer? TpiCareer { get; private set; }

	/// <summary>The game files the career reads (the mission files and the zones' <c>global.sam</c>), through the game file system.</summary>
	internal sealed class GameCareerFiles : IThemeParkIncCareerFiles
	{
		public static readonly GameCareerFiles Instance = new();

		public byte[]? Read( string path ) => FileSystem is { } files && files.FileExists( path ) ? files.ReadAllBytes( path ) : null;

		public bool HasCdkRegistrations => FileSystem is { } files && files.DirectoryExists( "/cdk registration" )
			&& files.GetFiles( "/cdk registration" ).Any( file => file.EndsWith( ".cdk", StringComparison.OrdinalIgnoreCase ) );
	}

	[ThreadStatic]
	private static ThemeParkIncCareer? enteringCareer;

	/// <summary>
	/// Makes the Theme Park Inc parks built on this thread until the scope ends take <paramref name="career"/> (the player's) as their career from their construction on, as the original's world
	/// reads the global career object while it builds (the staff pool's rank). Null builds parks with a new career.
	/// </summary>
	internal static IDisposable EnterThemeParkIncCareer( ThemeParkIncCareer? career )
	{
		var previous = enteringCareer;
		enteringCareer = career;
		return new CareerScope( previous );
	}

	private sealed class CareerScope( ThemeParkIncCareer? previous ) : IDisposable
	{
		public void Dispose() => enteringCareer = previous;
	}

	/// <summary>The career of a Theme Park Inc park: the player's (<see cref="EnterThemeParkIncCareer"/>) or a new one, its current zone the park's.</summary>
	// [BIN:TPI-EXE:0x74B490 career constructor] the world reads the global career 0xBFC484 and constructs one when there is none (every career access site checks it, e.g. 0x5C42F8)
	private void CreateThemeParkIncCareer()
	{
		if ( !Rules.ThemeParkInc )
			return;
		var career = enteringCareer ?? new ThemeParkIncCareer( GameCareerFiles.Instance, Settings.ThemeParkIncFiringLineLimit );
		EnterThemeParkIncZone( career );
		TpiCareer = career;
	}

	/// <summary>Makes <paramref name="career"/> the park's (the player's career, loaded from the global save or a park save) and its current zone the park's.</summary>
	public void AttachThemeParkIncCareer( ThemeParkIncCareer career )
	{
		ArgumentNullException.ThrowIfNull( career );
		if ( !Rules.ThemeParkInc )
			throw new InvalidOperationException( "Only a Theme Park Inc park has a career." );
		EnterThemeParkIncZone( career );
		TpiCareer = career;
		AttachThemeParkIncMissions( career );
	}

	/// <summary>
	/// [EXT:headless-economy] Whether the economy runs without Theme Park Inc's game files (tests and tools: no mission file in the file system), where the career has no current zone record.
	/// </summary>
	internal bool TpiHeadlessCareer { get; private set; }

	// [BIN:TPI-EXE:0x42A360 set theme] the world's zone change calls the career's ChangeTheme (0x74F330) with the zone's name, which makes the zone's record the current one; with the game's
	// files a zone whose global.sam does not load is refused here, at the park's construction or load
	private void EnterThemeParkIncZone( ThemeParkIncCareer career )
	{
		InitializeThemeParkIncShares( career );
		TpiHeadlessCareer = GameCareerFiles.Instance.Read( ThemeParkIncMission.PathOf( 0 ) ) == null;
		if ( TpiHeadlessCareer )
			return;
		career.ChangeTheme( Settings.Theme );
		if ( career.CurrentZone == null )
			throw new InvalidDataException( $"The Theme Park Inc zone '{Settings.Theme}' has no record in the career: its global.sam does not load." );
		// a won challenge counts in the record of the current zone's park number (0x5C4514 -> 0x74F4B0), which has no record outside the name table: a career whose record says otherwise
		// (a player file or save edited by hand) is refused here rather than at the first win
		if ( career.CurrentZone.ParkNumber < 0 || career.CurrentZone.ParkNumber >= ThemeParkIncCareer.ZoneNames.Count )
			throw new InvalidDataException( $"The Theme Park Inc zone '{Settings.Theme}' has park number {career.CurrentZone.ParkNumber} in the career, outside the zone table." );
	}

	/// <summary>
	/// Restores a saved career into the park's (a park save) and makes the park's zone its current one, as the original's career load ends (<c>0x74D110</c> finds the current zone's record by
	/// the theme name): the saved current zone is not trusted, so a challenge win always finds its record.
	/// </summary>
	internal void RestoreThemeParkIncCareer( ThemeParkIncCareerSnapshot state )
	{
		var career = TpiCareer ?? throw new InvalidOperationException( "Only a Theme Park Inc park has a career." );
		career.Restore( state );
		EnterThemeParkIncZone( career );
	}

	/// <summary>
	/// The Skip Mission cheat (<see cref="CheatKind.SkipMission"/>): the debug dialog's button runs the mission controller's completion tick, whose career side is
	/// <see cref="ThemeParkIncCareer.CompleteMission"/>. The outcome names the mission the career is on afterwards, or "finished".
	/// </summary>
	// [BIN:TPI-EXE:0x532B39 skip mission] the dialog proc 0x532A40 takes button 0x186A1 ("Skip Mission", 0x9B79C8) to the park's controller (0x64ED00) and its completion tick 0x5F0940, then closes
	private string SkipThemeParkIncMission()
	{
		var career = TpiCareer!;
		var completion = TpiMissions!.CompleteMission( Turn );
		return completion.Finished || career.GameFinished ? "finished" : career.MissionCounter.ToString( System.Globalization.CultureInfo.InvariantCulture );
	}

	/// <summary>
	/// The Get Promoted cheat (<see cref="CheatKind.GetPromoted"/>): a promotion to the career's rank plus one, as the mission controller's handler of message 0x25 applies it. The outcome is the new
	/// rank (with "unread limit" when the word it compares with is one OpenTPW cannot read).
	/// </summary>
	// [BIN:TPI-EXE:0x532A9E get promoted] button 0x186A2 ("Get Promoted", 0x9B79A8) reads the mission's Promotion.Number and the career's rank (0x5F0150), builds message 0x25 with the rank + 1
	// (0x532AC0 incl, 0x4361C0) and posts it (0x436DD0); the controller's handler calls 0x74C450 with it (the panel 0x1A4 it opens is presentation)
	private string PromoteThemeParkIncCareer()
	{
		var career = TpiCareer!;
		var rank = unchecked( career.Rank + 1 );
		var read = TpiMissions!.Promotion( rank );
		return rank.ToString( System.Globalization.CultureInfo.InvariantCulture ) + (read ? "" : " (unread limit)");
	}

	/// <summary>
	/// The Win The Game cheat (<see cref="CheatKind.WinTheGame"/>): the debug menu's button gives the player 51 % of the company's issued shares, as the shares game's gift (<c>0x621CE0</c>) adds them.
	/// The outcome is the shares owned afterwards.
	/// </summary>
	// [BIN:TPI-EXE:0x53835D win the game] the debug menu's procedure 0x538030 (built by 0x537B7F with the text "Win the game..." at 0x9B81F4) takes [0xA02168] × 51 (shll 4, add, lea × 3), ÷ 100 toward
	// zero (0x51EB851F, sar 5 plus the sign bit), to 0x621CE0 on 0xAA0128 (0x538384); then a 0x24-byte end object 0x5CD6E0(0x5D, 6, 1) is posted to the message centre (0x5383A9..)
	// [EXT:tpi-capability-gates] the end object (the majority shareholding's ending, as the completion tick's end-of-game object) is not posted: the ending sequence is not ported
	private string WinThemeParkIncGame()
	{
		var career = TpiCareer!;
		var issued = Settings.ThemeParkIncShares.CompanySharesIssued;
		career.Shares.AddShares( unchecked( issued * 51 ) / 100, Settings.ThemeParkIncShares );
		return career.Shares.Owned.ToString( System.Globalization.CultureInfo.InvariantCulture );
	}
}
