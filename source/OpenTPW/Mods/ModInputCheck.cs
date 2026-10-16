namespace OpenTPW.Mods;

/// <summary>What the check of a mod input folder found.</summary>
public enum ModInputState
{
	/// <summary>No folder is chosen.</summary>
	NotChosen,
	/// <summary>The folder does not exist or has no game <c>Data</c> folder.</summary>
	NotFound,
	/// <summary>The folder holds the other edition's data than the input asks for.</summary>
	WrongEdition,
	/// <summary>The folder holds the data the input asks for.</summary>
	Valid,
}

/// <summary>The result of checking an <c>original-data</c> input folder, with the edition found and how it was recognised.</summary>
public sealed record ModInputCheck( ModInputState State, GameEdition? Detected, GameEditionEvidence Evidence )
{
	public static ModInputCheck NotChosen { get; } = new( ModInputState.NotChosen, null, GameEditionEvidence.None );

	/// <summary>The edition a manifest's <c>edition</c> token names, or null for no requirement.</summary>
	public static GameEdition? EditionOf( ModInput input ) => input.Edition switch
	{
		"theme-park-inc" => GameEdition.ThemeParkInc,
		"theme-park-world" => GameEdition.ThemeParkWorld,
		_ => null,
	};

	/// <summary>
	/// Judges an inspected folder against an input: the folder needs a game <c>Data</c> folder, and when the input names
	/// an edition <see cref="GameEditions.Detect"/> must find that edition there (for Theme Park Inc by the known retail
	/// <c>Standard.sam</c>, <see cref="GameEditions.HasKnownStandardSam"/>, or by structure). Problems the inspection lists
	/// for the folder as a Theme Park World install do not matter here: only the data folder and its edition do.
	/// </summary>
	public static ModInputCheck Evaluate( InstallationReport report, ModInput input )
	{
		if ( report.DataDirectory == null )
			return new ModInputCheck( ModInputState.NotFound, null, GameEditionEvidence.None );
		var detected = GameEditions.Detect( report.DataDirectory );
		var wanted = EditionOf( input );
		var valid = wanted == null || detected.Edition == wanted;
		return new ModInputCheck( valid ? ModInputState.Valid : ModInputState.WrongEdition, detected.Edition, detected.Evidence );
	}

	/// <summary>Inspects <paramref name="path"/> on this thread and judges it (tests and tools; the screen inspects in a helper process).</summary>
	public static ModInputCheck Check( string path, ModInput input ) => Evaluate( GameInstallation.Inspect( path ), input );
}
