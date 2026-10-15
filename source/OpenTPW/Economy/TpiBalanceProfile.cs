namespace OpenTPW;

/// <summary>A typed original value and the winning SAM entry, including source file and line.</summary>
public sealed record TpiBalanceValue<T>( T Value, SamEntry Provenance );

/// <summary>Data availability for the supported subset, separately from unimplemented TPI economy rules.</summary>
public sealed record TpiBalanceReadiness( IReadOnlyList<string> MissingSupportedKeys, IReadOnlyList<string> RuntimeBlockers )
{
	public bool SupportedSettingsReady => MissingSupportedKeys.Count == 0;
	public bool EconomyReady => SupportedSettingsReady && RuntimeBlockers.Count == 0;
}

/// <summary>
/// TPI's cash and cell-cost settings, without constructing the TPW staff/research/challenge profile.
/// Missing values stay absent; this data adapter does not authorize any edition capability.
/// </summary>
public sealed class TpiBalanceProfile
{
	private static readonly string[] SupportedKeys =
	{
		"BankAccountInfo.InitialCash", "BankAccountInfo.InitialAdmissionFee",
		"Costs.QueueCell", "Costs.PathCell", "Costs.KartTrackCell", "Costs.WaterTrackCell", "Costs.MapCell"
	};

	public TpiBalanceProfile( SamSettings standard )
	{
		ArgumentNullException.ThrowIfNull( standard );
		// Read exact keys only; layered entries retain their source and line. No renamed-field aliases are assumed.
		InitialCash = ReadLong( SupportedKeys[0] );
		InitialAdmissionFee = ReadInt( SupportedKeys[1] );
		QueueCell = ReadInt( SupportedKeys[2] );
		PathCell = ReadInt( SupportedKeys[3] );
		KartTrackCell = ReadInt( SupportedKeys[4] );
		WaterTrackCell = ReadInt( SupportedKeys[5] );
		MapCell = ReadInt( SupportedKeys[6] );
		Readiness = new TpiBalanceReadiness(
			Array.AsReadOnly( SupportedKeys.Where( key => !standard.Contains( key ) ).ToArray() ),
			Array.AsReadOnly( new[]
			{
				"TPI bank, admission, calendar and loan execution rules are unqualified.",
				"TPI staff wages, grades, training and staff-room rules are unqualified.",
				"TPI research progression and object availability are unqualified.",
				"TPI missions and progress replace the TPW challenge/golden-ticket system.",
				"The park runtime requires BalanceSettings and couples economy to staff, weather and park gates."
			} ) );

		TpiBalanceValue<long>? ReadLong( string key ) => standard.Find( key ) is { } entry ? new( standard.GetLong( key ), entry ) : null;
		TpiBalanceValue<int>? ReadInt( string key ) => standard.Find( key ) is { } entry ? new( standard.GetInt( key ), entry ) : null;
	}

	public GameEdition Edition => GameEdition.ThemeParkInc;
	public TpiBalanceValue<long>? InitialCash { get; }
	public TpiBalanceValue<int>? InitialAdmissionFee { get; }
	public TpiBalanceValue<int>? QueueCell { get; }
	public TpiBalanceValue<int>? PathCell { get; }
	public TpiBalanceValue<int>? KartTrackCell { get; }
	public TpiBalanceValue<int>? WaterTrackCell { get; }
	public TpiBalanceValue<int>? MapCell { get; }
	public TpiBalanceReadiness Readiness { get; }

	/// <summary>The existing build-cost contract, available only if all five original values exist.</summary>
	public CellCosts? Costs => QueueCell != null && PathCell != null && KartTrackCell != null && WaterTrackCell != null && MapCell != null
		? new CellCosts( QueueCell.Value, PathCell.Value, KartTrackCell.Value, WaterTrackCell.Value, MapCell.Value ) : null;

	/// <summary>
	/// Explicit layers selected by a caller. This does not infer a TPI campaign/difficulty layer order
	/// from TPW's Easy_Standard convention or from the process-wide selected edition.
	/// </summary>
	public static TpiBalanceProfile FromLayers( IEnumerable<SamDocument> layers ) => new( new SamSettings( layers ) );
}
