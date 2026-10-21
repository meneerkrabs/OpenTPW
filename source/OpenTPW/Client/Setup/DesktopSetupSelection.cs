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

	/// <summary>True when <paramref name="args"/> ask for the Theme Park Inc Personnel File (<c>--front-end</c>, no world) and this selection is usable Theme Park Inc data: no world is needed then.</summary>
	public bool CanLaunch( string[] args ) => CanStart || IsPersonnelFileLaunch( args ) && Report is { IsUsable: true };

	private bool IsPersonnelFileLaunch( string[] args ) => Edition == GameEdition.ThemeParkInc && args.Contains( "--front-end" ) && !args.Contains( "--load-original-level" );

	// [EXT:tpi-viewer] an experimental Theme Park Inc choice starts that world through the viewer slice
	public string[] LaunchArguments( string[] args )
	{
		// [EXT:tpi-front-end-phase1] --front-end on a remembered Theme Park Inc install opens its Personnel File: the zone is chosen there, so none is added
		if ( IsPersonnelFileLaunch( args ) && Report is { IsUsable: true } )
			return args.Where( argument => argument != "--setup" ).ToArray();
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

	internal static bool HasExplicitMode( string[] args ) => args.Any( argument => argument is
		"--sandbox" or "--front-end" or "--load-original-level" or "--play-movie" or "--park-resume-smoke"
		or "--m3-gate" or "--m4-gate" or "--export-signs"
		or "--advisor-say" or "--advisor-response" or "--visit-park" or "--park-resume-file" or "--park-resume-evidence"
		or "--tpi-smoke-test" or "--capture-camera" or "--capture-interface" or "--capture-sequence" );

	internal static bool HasHeadlessGate( string[] args ) => args.Any( argument => argument is "--m3-gate" or "--m4-gate" );

	public static bool CanUseRememberedEdition( string[] args, string? commandLine, string? environment ) =>
		GamePathResolution.IsInteractive( args ) && string.IsNullOrWhiteSpace( commandLine ) && string.IsNullOrWhiteSpace( environment )
		// --front-end alone is no tool mode for the remembered edition: Theme Park World's start is the front end anyway, and a remembered Theme Park Inc install opens its Personnel File
		&& !HasExplicitMode( args.Where( argument => argument != "--front-end" ).ToArray() );

	/// <summary>
	/// The wizard opens on request only (<c>--setup</c>); an ordinary start uses the saved choice and the wizard appears
	/// only when no usable folder resolves or the saved folder/world is no longer valid (docs/SETUP.md).
	/// </summary>
	public static bool ShouldShowStarter( string[] args ) =>
		GamePathResolution.IsInteractive( args ) && !HasExplicitMode( args ) && args.Contains( "--setup" );
}
