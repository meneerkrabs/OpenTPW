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
		var career = enteringCareer ?? new ThemeParkIncCareer( GameCareerFiles.Instance, Settings.ThemeParkIncFiringLines );
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
	}

	// [BIN:TPI-EXE:0x42A360 set theme] the world's zone change calls the career's ChangeTheme (0x74F330) with the zone's name, which makes the zone's record the current one
	// [EXT:headless-economy] a headless economy over settings without the zone's global.sam (tests and tools) leaves the career without a current zone
	private void EnterThemeParkIncZone( ThemeParkIncCareer career )
	{
		if ( GameCareerFiles.Instance.Read( $"/levels/{Settings.Theme}/global.sam" ) != null )
			career.ChangeTheme( Settings.Theme );
	}
}
