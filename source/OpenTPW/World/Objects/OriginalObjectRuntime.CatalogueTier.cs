namespace OpenTPW;

public sealed partial class OriginalObjectRuntime
{
	private QueueParameters? catalogueParameters;
	private int catalogueFallbackCapacity;
	private bool catalogueInitialized;
	/// <summary>Derived catalogue metadata; operating VM state remains separate.</summary>
	public int? CatalogueTier { get; private set; }

	private void RememberCatalogueDefaults()
	{
		catalogueParameters = Visitors.Parameters;
		catalogueFallbackCapacity = Visitors.Capacity;
	}

	internal bool SupportsCatalogueTier => GameEditions.Current == GameEdition.ThemeParkWorld
		&& Script != null && ObjectCatalog.Themes.Contains( Entry.Theme ) && Entry.Category == ObjectCategory.Ride && IsAttraction && Coaster == null;

	private bool CanConfigureCatalogue( out string reason )
	{
		reason = "";
		if ( !SupportsCatalogueTier ) reason = "catalogue tier binding supports ordinary scripted Theme Park World rides only";
		else if ( stopped || Script is { State: RideVMState.Faulted } ) reason = "the owning runtime is stopped or faulted";
		else if ( !ReferenceEquals( Visitors.Parameters, PublishedParameters ) || Visitors.Capacity != catalogueFallbackCapacity ) reason = "the runtime has custom queue/default parameters";
		return reason.Length == 0;
	}

	private bool ResolveCatalogueTier( int tier, out ObjectUpgradeLevel row, out QueueParameters parameters, out string reason )
	{
		row = null!; parameters = null!; reason = "";
		if ( tier < 0 || Entry.Upgrades.FirstOrDefault( entry => entry.Level == tier ) is not { } found )
		{ reason = $"{Entry.ArchivePath} has no exact SAM upgrade tier {tier}"; return false; }
		row = found;
		parameters = QueueParametersFor( Entry, row );
		return true;
	}

	/// <summary>Pure purchase preflight; no catalogue metadata or live VM state is changed.</summary>
	internal bool CanBuyCatalogueTier( int currentTier, int nextTier )
	{
		if ( !CanConfigureCatalogue( out _ ) || CatalogueTier != currentTier || currentTier < 0
			|| currentTier == int.MaxValue || nextTier != currentTier + 1 ) return false;
		for ( var index = 0; index < Entry.Upgrades.Count; index++ )
			if ( Entry.Upgrades[index].Level == nextTier ) return true;
		return false;
	}

	/// <summary>Before execution/restore only: seed requested catalogue defaults, then saved VM state may replace them.</summary>
	internal bool TryInitializeCatalogueTier( int tier, out string reason )
	{
		if ( !CanConfigureCatalogue( out reason ) ) return false;
		if ( catalogueInitialized || Script!.ExecutedInstructions != 0 )
		{ reason = "catalogue VM seeds may only be initialized once before script execution"; return false; }
		if ( Script.GetVariableIndex( nameof( RideVariables.VAR_CAPACITY ) ) < 0 || Script.GetVariableIndex( nameof( RideVariables.VAR_DURATION ) ) < 0 )
		{ reason = "the script is missing the common CAP/DUR seed variables"; return false; }
		if ( !ResolveCatalogueTier( tier, out var row, out var parameters, out reason ) ) return false;
		ApplyCatalogueMetadata( tier, row, parameters );
		// The same CAP/DUR/SPEED initialization as the constructor: CAP and DUR are the clamped low bytes (0x4dd7f0, 0x4db565), each written when above 0.
		if ( row.GetInt( "InitCapacity" ) is > 0 and var capacity )
			SetVariable( nameof( RideVariables.VAR_CAPACITY ), ClampCapacity( Entry.Settings, capacity ) );
		if ( ClampInitDuration( Entry.Settings, row.GetInt( "InitDuration" ) ) is { } duration )
			SetVariable( nameof( RideVariables.VAR_DURATION ), duration );
		if ( row.GetInt( "InitSpeed" ) is > 0 and var speed )
			Script.SpeedBias = speed;
		return true;
	}

	/// <summary>Completed tier metadata only: no live VM, queue/rider/head, animator or scheduler mutation.</summary>
	// [APPROX:QUEUE-011] exact completed economy tiers drive factory-owned catalogue defaults and the speed bias; tp.exe's completion 0x004DF8F0 (its state-2 branch) also writes live VAR_CAPACITY through 0x004DD7F0 (InitCapacity +0x198, clamped to UsageInfo Min/MaxCapacity +0x124/+0x128 when their sum is above 0) and VAR_DURATION (InitDuration +0x1A0, clamped to Min/MaxDuration +0x12C/+0x130), each only when above 0, while OpenTPW keeps live CAP/DUR authored — evidence needed: none for the writes (read); the routine's common entry (gauge 100, VAR_WORN 0) and reopen tail are not compared with OpenTPW's completion path
	internal bool TryApplyCatalogueTierDefaults( int tier, out string reason, bool completion = false )
	{
		if ( !CanConfigureCatalogue( out reason ) ) return false;
		if ( CatalogueTier is int current && tier < current )
		{ reason = "completed catalogue tiers cannot regress on a live owner"; return false; }
		if ( !ResolveCatalogueTier( tier, out var row, out var parameters, out reason ) ) return false;
		if ( catalogueInitialized && CatalogueTier == tier ) return true;
		ApplyCatalogueMetadata( tier, row, parameters );
		// [BIN:TPW-EXE:0x4df8f0 upgrade completion speed] completing an upgrade sets the script speed bias to the new level's InitSpeed (Upgrades[level] at def+0x180+level·0x40, +0x28) when above 0, the same three steps as the ride-settings setter 0x4dd6e0 (bias 0x55a300, ride+0x58, log line); the same routine sets capacity from +0x198 (0x4dd7f0) and duration from +0x1a0 (QUEUE-011 keeps those authored)
		if ( completion && row.GetInt( "InitSpeed" ) is > 0 and var speed )
			SetSpeed( speed );
		return true;
	}

	private void ApplyCatalogueMetadata( int tier, ObjectUpgradeLevel row, QueueParameters parameters )
	{
		Visitors.SetCatalogueFallbackCapacity( row.GetInt( "InitCapacity" ) );
		catalogueParameters = parameters;
		// the ride words a setter wrote stay over the tier's values (a restored binding keeps the saved settings)
		PublishSettingWords();
		catalogueFallbackCapacity = Visitors.Capacity;
		catalogueInitialized = true;
		CatalogueTier = tier;
	}

	/// <summary>Pure no-schema snapshot eligibility; stopped matching owners remain representable.</summary>
	internal bool CanCaptureCatalogueDefaults( int economyTier, out string reason )
	{
		reason = "";
		if ( !SupportsCatalogueTier ) return true;
		if ( !ReferenceEquals( Visitors.Parameters, PublishedParameters ) || Visitors.Capacity != catalogueFallbackCapacity )
			reason = "custom catalogue parameters cannot be reconstructed from the existing snapshot";
		else if ( economyTier < 0 || (CatalogueTier ?? 0) != economyTier )
			reason = "the live catalogue defaults do not match the saved economy tier";
		// The legacy constructor's unbound tier0 defaults (including absent row0) reconstruct unchanged.
		return reason.Length == 0;
	}

	/// <summary>Exact row overload; missing/sparse tiers never select a different row.</summary>
	internal static QueueParameters CreateQueueParameters( ObjectCatalogEntry entry, int tier )
	{
		if ( tier < 0 || entry.Upgrades.FirstOrDefault( row => row.Level == tier ) is not { } found )
			throw new InvalidDataException( $"{entry.ArchivePath} has no exact SAM upgrade tier {tier}." );
		return QueueParametersFor( entry, found );
	}
}
