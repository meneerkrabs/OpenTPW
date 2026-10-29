namespace OpenTPW;

public sealed partial class OriginalObjectRuntime
{
	private QueueParameters? catalogueParameters;
	// the constructor's queue parameters, the basis the ride words fall back on
	private QueueParameters constructorParameters = QueueParameters.Default;
	private int catalogueFallbackCapacity;
	private bool catalogueInitialized;
	/// <summary>Derived catalogue metadata; operating VM state remains separate.</summary>
	public int? CatalogueTier { get; private set; }

	private void RememberCatalogueDefaults()
	{
		constructorParameters = Visitors.Parameters;
		catalogueParameters = Visitors.Parameters;
		catalogueFallbackCapacity = Visitors.Capacity;
	}

	internal bool SupportsCatalogueTier => GameEditions.Current == GameEdition.ThemeParkWorld
		&& Script != null && ObjectCatalog.Themes.Contains( Entry.Theme ) && Entry.Category == ObjectCategory.Ride && IsAttraction;

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

	/// <summary>
	/// A completed (or restored) economy tier: the catalogue metadata, and on <paramref name="completion"/> the ride words of tp.exe's completion
	/// <c>0x4df8f0</c> (its state-2 branch): SPEED from the level's <c>InitSpeed</c>, CAPACITY through the setter <c>0x4dd7f0</c> from <c>InitCapacity</c>
	/// and DURATION, clamped, from <c>InitDuration</c>, each only when above 0, so a word whose value is not above 0 keeps the ride's setting.
	/// No queue, rider, head, animator or scheduler state changes.
	/// </summary>
	// [BIN:TPW-EXE:0x4df8f0 upgrade completion] the state-2 branch raises the level (+0x50), then: Upgrades[level].InitSpeed (def +0x1a8 + level·0x40) when above 0 → the bias (0x55a300), the log line and ride +0x58, as SetSpeed 0x4dd6e0; InitCapacity (+0x198) when above 0 → SetCapacity 0x4dd7f0 with its low byte (script variable 2, +0x5d); InitDuration (+0x1a0) when above 0 → its low byte clamped to Min/MaxDuration (+0x12c/+0x130), script variable 3 and +0x5c
	internal bool TryApplyCatalogueTierDefaults( int tier, out string reason, bool completion = false )
	{
		if ( !CanConfigureCatalogue( out reason ) ) return false;
		if ( CatalogueTier is int current && tier < current )
		{ reason = "completed catalogue tiers cannot regress on a live owner"; return false; }
		if ( !ResolveCatalogueTier( tier, out var row, out var parameters, out reason ) ) return false;
		if ( catalogueInitialized && CatalogueTier == tier ) return true;
		if ( completion )
		{
			// the completion writes only the words whose level value is above 0; the others keep the ride's current value over the new tier
			var live = Settings;
			speedWord ??= live.Speed;
			capacityWord ??= live.Capacity;
			durationWord ??= live.Duration;
		}
		ApplyCatalogueMetadata( tier, row, parameters );
		if ( !completion )
			return true;
		if ( row.GetInt( "InitSpeed" ) is > 0 and var speed )
			SetSpeed( speed );
		if ( row.GetInt( "InitCapacity" ) is > 0 and var capacity )
			SetCapacity( capacity );
		if ( row.GetInt( "InitDuration" ) is > 0 and var duration )
			SetDuration( duration );
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
