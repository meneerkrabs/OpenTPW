namespace OpenTPW;

public partial class Level
{
	// This follows script/coaster attachment: a restored script can clear BROKEN while the attraction
	// still retains its broken state. Script presence alone does not establish original VM continuation.
	private void ImportOriginalRetainedRideStates()
	{
		if ( resumeState != null || visitLayout != null || GameEditions.IsThemeParkInc || Park?.Import is not { } import || OriginalPark?.Save is not { } save )
			return;
		foreach ( var saved in save.Attractions.Where( item => item.State is 1 or 4 ) )
		{
			if ( !import.AttractionInstances.TryGetValue( saved.Id, out var instance ) || !Park.Economy.TryGetObject( instance, out var state ) )
				continue;
			var reason = RestoreOriginalRetainedRideOwner( saved, state,
				economyInstances.Select( pair => (pair.Key.Runtime, pair.Key.ThingId, pair.Value) ) );
			if ( reason != null )
				import.Report( $"Attraction thing {saved.Id} retained broken state not attached: {reason}." );
		}
	}

	internal static string? RestoreOriginalRetainedRideOwner( SaveAttraction saved, ParkObjectState state,
		IEnumerable<(OriginalObjectRuntime Runtime, int ThingId, int InstanceId)> candidates )
	{
		var owners = candidates.Where( item => item.InstanceId == state.Id ).ToArray();
		return owners.Length == 1
			? RestoreOriginalRetainedRideState( saved, state, owners[0].Runtime, owners[0].ThingId )
			: "no unique runtime owner";
	}

	/// <summary>Shared original-import seam; all guards precede writes, and no script word is changed.</summary>
	internal static string? RestoreOriginalRetainedRideState( SaveAttraction saved, ParkObjectState state, OriginalObjectRuntime runtime, int thingId )
	{
		if ( GameEditions.IsThemeParkInc || saved.State is not (1 or 4) )
			return "unsupported edition or saved state";
		if ( thingId != saved.Id || !state.Imported || state.InfoId != saved.InfoId || runtime.Entry.InfoId != saved.InfoId )
			return "the saved thing and economy/runtime identities do not match";
		var normalized = ParkEconomy.ObjectState( state );
		if ( !state.IsBrokenDown || normalized is not (RideObjectStates.BrokenDown or RideObjectStates.Condemned) )
			return "the economy did not import a broken attraction";
		if ( saved.CanLoad != 0 || runtime.Script == null || runtime.IsOpen )
			return "the independent saved open word cannot be represented by this runtime's script state";
		runtime.Visitors.RestoreOriginalBrokenState( normalized == RideObjectStates.Condemned, state.LifeGauge );
		state.ScriptStateBroken = true;
		return null;
	}
}
