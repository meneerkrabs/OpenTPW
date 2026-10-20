namespace OpenTPW;

public partial class Level
{
	// This follows script/coaster attachment: a restored script can clear BROKEN while the attraction
	// still retains its broken state. Script presence alone does not establish original VM continuation.
	// [BIN:TPW-EXE:0x004E0E60 SetState] states 1 (broken down) and 4 (condemned) close the object ("Object %d: Closing", 0x4e0450), and the per-tick
	// dispatch 0x4e0e00 keeps an object in state 1, 2 or 4 closed (0x4e0450, 0x4e1410) while only state 0 runs the breakdown update 0x4e14e0, the sole
	// caller of SetState(1) and SetState(4) (0x4e154e, 0x4e1590); the state is left through SetState(0) (0x4de487, 0x4df438, 0x4dfcb8, 0x4e003c, 0x4e01b6)
	// or through the maintenance request 0x4e0050, which sets state 2 without testing the current state (0x4e0086), so a retained 1 or 4 stays closed;
	// the attraction initialiser 0x4db090 also calls SetState (0 or 3, at 0x4db508), but only when an object is created
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
		// state 1 or 4 follows the economy's gauge normalization; a saved state that disagrees with its gauge byte is reported by OriginalEconomyImport.ImportAttractionState
		runtime.Visitors.RestoreOriginalBrokenState( normalized == RideObjectStates.Condemned, state.LifeGauge );
		state.ScriptStateBroken = true;
		return null;
	}
}
