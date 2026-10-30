namespace OpenTPW;

public partial class Level
{
	/// <summary>
	/// Restores the vehicles, riders and lists of the kart and water rides of an original save (the TrackRides block's chunks 5, 7, 8 and 9, loader <c>0x543560</c>) once the guests are imported: a rider of a vehicle or a list
	/// is a guest in state 16 that names the ride, so it is known only after the guest import. Only a park started from an original save does this; a resumed park restores its own vehicles.
	/// </summary>
	private void ImportOriginalTrackRiders()
	{
		if ( resumeState != null || Park?.Import is not { } import || OriginalPark?.Save is not { } save || GameEditions.IsThemeParkInc )
			return;
		var lines = ApplyOriginalTrackRiders( save.TrackRides ).Concat( DropRidersWithoutCarrier() ).ToList();
		// a guard chasing or escorting a rider who was just put off has no guest left: it is imported idle, as for a guest the import dropped at once
		if ( Guests != null && Park != null )
			lines.AddRange( OriginalEconomyImport.IdleEscortsWithoutARestoredGuest( Park.Economy, id => Guests.Find( id ) != null ) );
		foreach ( var line in lines )
		{
			import.Report( line );
			Log.Trace( $"Park track import: {line}" );
		}
	}

	/// <summary>The attractions whose vehicles <see cref="ApplyOriginalTrackRiders"/> restored.</summary>
	private HashSet<OriginalObject>? restoredVehicleRides;

	/// <summary>
	/// A guest in state 16 is a rider only while the ride's carrier state is restored: a ride whose rider tables did not read, a tour ride (its cars are not imported) and a kart, water or bumper ride
	/// whose vehicles were not imported leave their riders with nothing that would ever let them off, so the guests are put off the ride (<see cref="GuestSimulation.DropImportedRiders"/>) and the report says so.
	/// Coasters keep their riders: their trains restore, and the original keeps a rider of no car list aboard too.
	/// </summary>
	// [BIN:TPW-EXE:0x00501A08 state 16 update] the original's rider does nothing in state 16 on its own: the ride's script and its vehicles let it off, so a rider of a carrier that is not restored would stay aboard for good
	internal IReadOnlyList<string> DropRidersWithoutCarrier()
	{
		var lines = new List<string>();
		if ( Guests == null )
			return lines;
		foreach ( var item in Objects.Objects.Where( item => !item.IsDeleted && item.Visitors is { SavedRunStateImported: true } ).OrderBy( item => item.ThingId ).ToList() )
		{
			var bridge = item.Visitors!;
			if ( bridge.Riders.Count == 0 || item.Runtime.Coaster != null )
				continue;
			var reason = bridge.RidersRefusal
				?? (item.Runtime.UsesTour ? "its tour cars are not imported"
				: item.Runtime.Bump != null && restoredVehicleRides?.Contains( item ) != true ? "its track ride's vehicles were not imported" : null);
			if ( reason == null )
				continue;
			var dropped = Guests.DropImportedRiders( bridge );
			lines.Add( $"[APPROX:SAVE-002] Saved riders of attraction thing {item.ThingId} put off the ride: {reason}, so {dropped.Count} guests ({string.Join( ", ", dropped )}) have no carrier state to ride on (TRACK-032 for a track ride's vehicles)." );
		}
		return lines;
	}

	/// <summary>
	/// The vehicle and rider step of <see cref="ImportOriginalTracks"/> for the rides whose chunk it applied (<see cref="importedTrackRides"/>): the vehicles, the riders they carry and the joining and leaving lists
	/// of each, as <see cref="BumpController.ImportOriginalVehicles"/> reads them, once every guest they name is a rider of the ride the guest import restored. A ride whose chunks do not read is reported and keeps no
	/// vehicle; the guests it names stay riders of the ride.
	/// </summary>
	internal IReadOnlyList<string> ApplyOriginalTrackRiders( SaveTrackRideBlock? block )
	{
		var lines = new List<string>();
		restoredVehicleRides = new();
		var read = OriginalTrackVehicles.Read( block );
		if ( read.Problem != null )
		{
			lines.Add( $"[APPROX:TRACK-032] Track ride vehicles not restored: the TrackRides block does not lay out its chunks as the saver does ({read.Problem})." );
			return lines;
		}
		var made = new List<BumpController>();
		foreach ( var ride in read.Rides )
		{
			var match = importedTrackRides?.FirstOrDefault( item => item.Ride.Identity.Value == ride.Header.Identity.Value ) ?? default;
			var what = match.Item != null ? $"attraction thing {match.Item.ThingId}" : $"ride {ride.Header.Identity.Value:X8}";
			var any = ride.Vehicles.Count > 0 || ride.Joining.Count > 0 || ride.Leaving.Count > 0 || ride.DroppedJoining > 0 || ride.DroppedNodes > 0;
			if ( match.Item == null )
			{
				if ( any )
					lines.Add( $"[APPROX:TRACK-032] Track ride vehicles not restored for {what}: its ride chunk was not applied." );
				continue;
			}
			made.Add( match.Bump );
			var bridge = match.Item.Visitors;
			var stray = ride.Guests.Where( guest => !bridge.Riders.Contains( guest ) ).Distinct().Order().ToList();
			if ( stray.Count > 0 )
			{
				lines.Add( $"[APPROX:TRACK-032] Track ride vehicles not restored for {what}: its vehicles and lists name guests who are not riders of it ({string.Join( ", ", stray )})." );
				continue;
			}
			// the loader makes the rides in the file's order: a vehicle finds its collision object among the objects of the rides made so far
			var visible = new HashSet<BumpController>( made );
			var refusal = match.Bump.ImportOriginalVehicles( ride, item => item.Owner is { } owner && visible.Contains( owner ), out var result );
			if ( refusal != null )
			{
				lines.Add( $"[APPROX:TRACK-032] Track ride vehicles not restored for {what}: {refusal}." );
				continue;
			}
			restoredVehicleRides!.Add( match.Item );
			// the engine voices and break-smoke emitters of the vehicles the save brought start again, as after a park resume (presentation: nothing is drawn from the generator)
			match.Item.Runtime.ResumeBumpEffects();
			if ( !any )
				continue;
			var parts = new List<string> { $"{result!.Vehicles} vehicles with {result.Riders} riders, {result.Joining} joining and {result.Leaving} leaving guests, phase {match.Bump.Phase}" };
			if ( ride.DroppedJoining > 0 )
				parts.Add( $"{ride.DroppedJoining} joining guests dropped as the loader's join refuses a ride in phase 0 (0x54AA80)" );
			if ( ride.DroppedNodes > 0 )
				parts.Add( $"{ride.DroppedNodes} guests dropped as the peep list is full" );
			lines.Add( $"Track ride vehicles imported for {what}: {string.Join( "; ", parts )}." );
		}
		if ( read.OrphanChunks > 0 )
			lines.Add( $"Track ride chunks of unknown rides skipped: {read.OrphanChunks} (the loader reads nothing of a chunk whose handle names no ride)." );
		return lines;
	}
}
