namespace OpenTPW;

/// <summary>The desktop starter's explicit selection; inspection results belong to a path/edition revision.</summary>
internal sealed class DesktopSetupSelection( SetupSettings saved )
{
	public GameEdition Edition { get; private set; } = saved.SelectedEdition;
	public string Path { get; private set; } = saved.SelectedEdition == GameEdition.ThemeParkInc ? saved.ThemeParkIncPath ?? "" : saved.GamePath ?? "";
	public string? World { get; set; } = saved.ThemeParkIncWorld;
	public InstallationReport? Report { get; private set; }
	public int Revision { get; private set; }

	public void Select( GameEdition edition, string path )
	{
		Edition = edition;
		Path = path;
		Report = null;
		Revision++;
	}

	public bool AcceptInspection( int revision, InstallationReport report )
	{
		if ( revision != Revision || report.SelectedEdition != Edition ) return false;
		Report = report;
		if ( report.IsUsable ) Path = report.Path;
		return true;
	}

	public bool CanStart => Report is { IsUsable: true } && Enum.IsDefined( Edition )
		&& (Edition == GameEdition.ThemeParkWorld || World != null && GameEditions.IsViewableLevel( World ) && Report.Worlds.Contains( World, StringComparer.OrdinalIgnoreCase ));

	public string[] LaunchArguments( string[] args )
	{
		if ( !CanStart ) throw new InvalidOperationException( "Choose matching game files and an available world." );
		var launch = args.Where( argument => argument != "--setup" ).ToArray();
		if ( Edition == GameEdition.ThemeParkWorld ) return launch;
		if ( launch.Contains( "--load-original-level" ) ) throw new ArgumentException( "Choose a world either in setup or on the command line." );
		launch = [.. launch, "--load-original-level", World!.ToLowerInvariant()];
		if ( GameEditions.ViewerRefusal( launch ) is { } refusal ) throw new ArgumentException( refusal );
		return launch;
	}

	public SetupSettings Remember( SetupSettings previous ) => !CanStart ? throw new InvalidOperationException( "Selection is not ready." )
		: Edition == GameEdition.ThemeParkInc
			? previous with { SelectedEdition = Edition, ThemeParkIncPath = Path, ThemeParkIncWorld = World!.ToLowerInvariant() }
			: previous with { SelectedEdition = Edition, GamePath = Path };

	private static bool HasExplicitMode( string[] args ) => args.Any( argument => argument is
		"--sandbox" or "--front-end" or "--load-original-level" or "--play-movie" or "--park-resume-smoke"
		or "--m3-gate" or "--m4-gate"
		or "--advisor-say" or "--advisor-response" or "--visit-park" or "--park-resume-file" or "--park-resume-evidence"
		or "--tpi-smoke-test" or "--capture-camera" or "--capture-interface" or "--capture-sequence" );

	internal static bool HasHeadlessGate( string[] args ) => args.Any( argument => argument is "--m3-gate" or "--m4-gate" );

	public static bool CanUseRememberedEdition( string[] args, string? commandLine, string? environment ) =>
		GamePathResolution.IsInteractive( args ) && string.IsNullOrWhiteSpace( commandLine ) && string.IsNullOrWhiteSpace( environment )
		&& !HasExplicitMode( args );

	public static bool ShouldShowStarter( string[] args, string? commandLine, string? environment, SetupSettings saved ) =>
		GamePathResolution.IsInteractive( args ) && !HasHeadlessGate( args ) && (args.Contains( "--setup" ) ||
			string.IsNullOrWhiteSpace( commandLine ) && string.IsNullOrWhiteSpace( environment ) && saved.ShowAutorun
			&& !AutorunLauncher.IsDisabled( args, saved ) && !HasExplicitMode( args ));
}
