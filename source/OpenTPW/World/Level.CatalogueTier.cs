namespace OpenTPW;

public partial class Level
{
	private bool CatalogueOwner( OriginalObject owner, int instance, out ParkObjectState state, out string reason )
	{
		state = null!; reason = "";
		if ( IsReadOnlyVisit || GameEditions.Current != GameEdition.ThemeParkWorld || Park == null ) reason = "no editable TPW economy";
		else if ( owner is PrototypeRide || owner.IsDeleted || !Objects.Objects.Contains( owner ) ) reason = "removed or unsupported object owner";
		else if ( economyInstances.TryGetValue( owner, out var linked ) && linked != instance ) reason = "the object already owns a different economy instance";
		else if ( !Park.Economy.TryGetObject( instance, out state ) || state.InfoId != owner.Entry.InfoId || state.Kind != ParkObjectKind.Ride ) reason = "the mapped economy record does not match this ride";
		else if ( economyInstances.Any( pair => pair.Key != owner && pair.Value == instance ) ) reason = "the economy instance has multiple object owners";
		return reason.Length == 0;
	}

	/// <summary>Pure action preflight using the same owner/binding policy as completion and capture.</summary>
	internal bool CanBuyCatalogueUpgrade( OriginalObject owner, ParkObjectState state ) =>
		economyInstances.TryGetValue( owner, out var instance ) && instance == state.Id
		&& CatalogueOwner( owner, instance, out var current, out _ ) && ReferenceEquals( state, current )
		&& state.Level >= 0 && state.Level < int.MaxValue && owner.Runtime.CanBuyCatalogueTier( state.Level, state.Level + 1 );

	private void InitializeCatalogueBinding( OriginalObject owner, int instance, bool alreadyBound )
	{
		// Shops/features/prototypes keep their existing controllers and construction semantics; coasters bind like any ride (tp.exe 0x4df8f0 has no track-type branch).
		if ( IsReadOnlyVisit || Park == null || owner is PrototypeRide || !owner.Runtime.SupportsCatalogueTier ) return;
		if ( !CatalogueOwner( owner, instance, out var state, out var reason ) )
			throw new InvalidDataException( $"Catalogue tier binding refused: {reason}." );
		var applied = alreadyBound ? owner.Runtime.TryApplyCatalogueTierDefaults( state.Level, out reason )
			: owner.Runtime.TryInitializeCatalogueTier( state.Level, out reason );
		if ( applied ) return;
		// A nonzero imported/resumed tier cannot become a successful tier0 reconstruction.
		if ( state.Level > 0 ) throw new InvalidDataException( $"Catalogue tier {state.Level} cannot initialize {owner.Entry.ArchivePath}: {reason}." );
		Log.Warning( $"Catalogue tier0 metadata not initialized for {owner.Entry.ArchivePath}: {reason}." );
	}

	private void ApplyCompletedCatalogueTier( int instance )
	{
		var owners = economyInstances.Where( pair => pair.Value == instance ).Select( pair => pair.Key ).ToArray();
		if ( owners.Length != 1 ) { Log.Warning( $"Catalogue tier completion refused for instance {instance}: no unique owner." ); return; }
		var owner = owners[0];
		if ( !CatalogueOwner( owner, instance, out var state, out var reason )
			|| !owner.Runtime.TryApplyCatalogueTierDefaults( state.Level, out reason, completion: true ) )
			Log.Warning( $"Catalogue tier completion refused for instance {instance}: {reason}." );
	}
	private void RequireResumableCatalogueDefaults()
	{
		foreach ( var owner in Objects.Objects )
		{
			if ( owner is PrototypeRide || !owner.Runtime.SupportsCatalogueTier ) continue;
			if ( !economyInstances.TryGetValue( owner, out var instance ) )
				throw new NotSupportedException( "This ride has no reconstructable economy binding." );
			if ( !CatalogueOwner( owner, instance, out var state, out var reason )
				|| !owner.Runtime.CanCaptureCatalogueDefaults( state.Level, out reason ) )
				throw new NotSupportedException( $"This ride's catalogue defaults cannot be saved: {reason}." );
		}
	}

}
