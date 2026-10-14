namespace OpenTPW;

public partial class Level
{
	/// <summary>Captures a writable fixed-ride park between simulation ticks, including its direct instance links.</summary>
	internal ParkResumeSnapshot CaptureResumeSnapshot( AutomaticAdvisor? advisor, string displayName,
		IReadOnlyDictionary<string, string>? verifiedAssets = null, DateTimeOffset? savedUtc = null )
	{
		if ( IsReadOnlyVisit || GameEditions.IsThemeParkInc || OriginalPark == null || Park == null || Guests == null )
			throw new InvalidOperationException( "Only a writable Theme Park World park can be saved here." );
		RequireResumableCatalogueDefaults();
		StaffPlacement?.PrepareForSave();
		var prototype = CapturePrototypeSnapshot();
		var runtimes = Objects.Objects.Select( item =>
		{
			var state = item.Runtime.CaptureSnapshot();
			return new ParkResumeRuntime( state.HasScript && !state.Stopped ? item.Runtime.Script!.ScriptId : 0, state );
		} ).ToArray();
		var assets = verifiedAssets ?? ParkResumeAssetManifest.Capture( Objects.Catalog, Park.Economy.Settings );
		var random = CaptureRandomState();
		var snapshot = new ParkResumeSnapshot
		{
			Format = ParkResumeSaveFile.FormatName,
			Version = ParkResumeSaveFile.CurrentVersion,
			Theme = LevelName,
			StartKind = Park.Start.Kind,
			Seed = Seed.Value,
			DisplayName = displayName,
			SavedUtc = savedUtc ?? DateTimeOffset.UnixEpoch,
			Economy = ParkSaveFile.Capture( Park.Economy, random ),
			Layout = ParkLayoutSnapshot.CaptureSnapshot( Guests.Grid.Cells, Objects ),
			Guests = Guests.CaptureSnapshot(),
			Scripts = Objects.ScriptWorld.CaptureSnapshot(),
			Runtimes = runtimes,
			EconomyBindings = Objects.Objects.Where( economyInstances.ContainsKey )
				.Select( item => new ParkResumeEconomyBinding( item.Visitors.AttractionId, economyInstances[item] ) ).ToArray(),
			Clock = simulationClock.CaptureSnapshot(),
			SoundSeed = random.SoundSeed,
			Weather = Weather?.CaptureSnapshot(),
			Gate = Gate?.CaptureSnapshot(),
			Staff = StaffWorld?.CaptureSnapshot(),
			Advisor = advisor?.CaptureSnapshot(),
			Coasters = CaptureCoastersSnapshot(),
			Tracks = CaptureTracksSnapshot(),
			Tours = CaptureToursSnapshot(),
			Prototype = prototype,
			AssetHashes = assets
		};
		ParkResumeSaveFile.ValidateSnapshot( snapshot, Objects.Catalog, Park.Economy.Settings, Park.Economy.Catalog, assets );
		return snapshot;
	}

	/// <summary>
	/// The park-resume prototype part: the developer Totem's whole own state — runtime, script root, position, open
	/// flag and the exact economy association the live park holds — or null when the park has none. The Totem draws its
	/// attraction id from the same allocator as the placed objects, so the layout's saved allocator covers it.
	/// </summary>
	internal PrototypeRideSaveData? CapturePrototypeSnapshot()
	{
		if ( PlacedRide is not { } ride )
			return null;
		return PrototypeRideSave.Capture( ride.Runtime, ride.Position, ride.IsOpen, prototypeInstance, prototypeBought );
	}

	/// <summary>Called only during construction of a staged candidate, before it can simulate or replace the live park.</summary>
	private void RestoreResumeSimulation( ParkResumeSnapshot snapshot )
	{
		if ( Guests == null || Park == null || snapshot.Runtimes.Count != Objects.Objects.Count )
			throw new InvalidDataException( "The saved park cannot be rebuilt with the current objects." );
		var roots = new Dictionary<int, RideVM>();
		for ( var index = 0; index < Objects.Objects.Count; index++ )
		{
			var saved = snapshot.Runtimes[index];
			var runtime = Objects.Objects[index].Runtime;
			var gaps = runtime.RestoreSnapshot( saved.State );
			if ( gaps.Count != 0 ) throw new InvalidDataException( string.Join( "; ", gaps ) );
			if ( saved.RootScriptId != 0 ) roots.Add( saved.RootScriptId, runtime.Script! );
		}
		// The prototype Totem goes in beside the placed objects: before the script world (its fresh VM is a restore
		// root like every object's) and before the guests (its bridge must be registered for the queue-geometry check
		// and the guest restore to see it).
		PlacedRide = RestorePrototypeRideSnapshot( snapshot.Prototype, roots );
		Objects.ScriptWorld.RestoreSnapshot( snapshot.Scripts, roots );
		ValidateResumeQueueGeometry( snapshot.Guests, Guests );
		Guests.RestoreSnapshot( snapshot.Guests );
		simulationClock.RestoreSnapshot( snapshot.Clock );
		if ( snapshot.Weather != null )
		{
			Weather = new ParkWeather( Park.Economy.Settings.Standard, Seed.WeatherStream, Guests.Grid.CountX, Guests.Grid.CountY );
			Weather.RestoreSnapshot( snapshot.Weather );
		}
		if ( snapshot.Gate != null )
			(Gate ?? throw new InvalidDataException( "The saved park gate is unavailable." )).RestoreSnapshot( snapshot.Gate );
		if ( snapshot.Staff != null )
			(StaffWorld ?? throw new InvalidDataException( "The saved staff world is unavailable." )).RestoreSnapshot( snapshot.Staff );
		// The coasters go last: their stations were placed above (each placement reset the track to its station
		// node), and this restores the saved tracks, controller state, trains and riders over them.
		RestoreCoastersSnapshot( snapshot.Coasters );
		RestoreTracksSnapshot( snapshot.Tracks );
		RestoreToursSnapshot( snapshot.Tours );
		// the resumed tiles and boats are drawn from the first frame, as they were when the park was saved
		UpdateTrackRendering();
		renderedCellVersion = Guests.Grid.Cells.Version;
	}

	/// <summary>
	/// Rebuilds the saved developer Totem, or null when the save holds none: a fresh candidate with the saved
	/// position, script seed, open flag and attraction id (the id is adopted, nothing allocated or charged), its
	/// runtime restored like a placed object's, its bridge registered with the guests through its saved guest cells
	/// (restored verbatim — the placement's entrance derivation is history the saved grid cannot reproduce, so the
	/// resumed queue is exactly the saved one; a Totem the live park never registered stays unregistered), the saved
	/// economy instance re-linked when it registered (never bought), and its script handed to the world restore as a
	/// root.
	/// </summary>
	private PrototypeRide? RestorePrototypeRideSnapshot( PrototypeRideSaveData? saved, Dictionary<int, RideVM> roots )
	{
		if ( saved == null )
			return null;
		var ride = new PrototypeRide( new Vector3( saved.PositionX, saved.PositionY, saved.PositionZ ), Objects.ScriptWorld,
			saved.ScriptSeed, saved.Open, saved.AttractionId );
		var gaps = ride.Runtime.RestoreSnapshot( saved.Runtime );
		if ( gaps.Count != 0 ) throw new InvalidDataException( string.Join( "; ", gaps ) );
		if ( PrototypeRideSave.RestoreGuestCells( saved, ride.Visitors, Guests! ) && saved.EconomyInstanceId is int instance )
		{
			if ( Park == null || !Park.Economy.TryGetObject( instance, out var state ) || state.InfoId != ride.Entry.InfoId )
				throw new InvalidDataException( "The saved prototype does not match its economy instance." );
			prototypeInstance = instance;
			prototypeBought = saved.EconomyBought;
			Park.Guests?.Link( ride.Visitors.AttractionId, instance );
		}
		if ( saved.Runtime.HasScript && !saved.Runtime.Stopped )
			roots.Add( saved.RootScriptId, ride.Runtime.Script! );
		return ride;
	}

	/// <summary>Current cached queues must agree with the restored cells; a cache from before the last edit refreshes on the next tick.</summary>
	internal static void ValidateResumeQueueGeometry( GuestSimulationSnapshot snapshot, GuestSimulation target )
	{
		var validation = target.ValidateSnapshot( snapshot );
		if ( !validation.IsValid ) throw new InvalidDataException( string.Join( "; ", validation.Errors ) );
		if ( snapshot.SeenGridVersion > target.Grid.Version )
			throw new InvalidDataException( "The guests refer to a cell-map version newer than the saved layout." );
		if ( snapshot.SeenGridVersion != target.Grid.Version ) return;
		var bridges = target.Attractions.OfType<RideVisitorBridge>().ToDictionary( item => item.AttractionId );
		foreach ( var saved in snapshot.Attractions )
		{
			var bridge = bridges[saved.AttractionId];
			if ( !saved.QueueCells.SequenceEqual( bridge.QueueCells ) || saved.JoinCell != bridge.JoinCell )
				throw new InvalidDataException( $"Attraction {saved.AttractionId}'s queue does not match the saved cell map." );
		}
	}
}
