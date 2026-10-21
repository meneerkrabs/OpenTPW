using OpenTPW.Mods;

namespace OpenTPW;

public partial class Level
{
	/// <summary>Captures the level's particle pool for a park-resume save (set by the particle renderer module; null in headless hosts).</summary>
	internal static Func<Level, ParticleSystemState?>? ParticlesCaptured { get; set; }

	/// <summary>Rebuilds the saved particle pool in a resumed park (set by the particle renderer module; null in headless hosts).</summary>
	internal static Action<Level, ParticleSystemState>? ParticlesResumed { get; set; }
	/// <summary>Set by the particle module: gives the level the pool of an original save's Particles block (the state's emitters and effectors, the post-load pass applied).</summary>
	internal static Action<Level, SaveParticlesBlock>? ParticlesImported { get; set; }

	/// <summary>
	/// Gives every placed object the economy catalogue knows an economy record before a save is captured: a save must hold a binding for each of them (<see cref="ParkResumeSaveFile.ValidateSnapshot"/>).
	/// Every build path binds the object it places (<see cref="PlaceObject"/> does so even when a placement handler throws), so this only repairs a park that lost a binding anyway, and says so in the log.
	/// The object adopts the lowest-numbered record of its type no other object holds: that is the record its own purchase made, which a failed build left unowned. Only when no such record exists is an
	/// uncharged one registered, as the original-save import does (<see cref="ParkEconomy.RegisterExisting"/>), because nothing was bought for the object that the books could still refund or count.
	/// </summary>
	internal void RepairMissingEconomyBindings()
	{
		if ( Park == null )
			return;
		foreach ( var item in Objects.Objects.ToArray() )
		{
			if ( item.IsDeleted || item is PrototypeRide || economyInstances.ContainsKey( item ) || (PlacedRide != null && ReferenceEquals( item, PlacedRide ))
				|| !Park.Economy.Catalog.TryGet( item.Entry.InfoId, out _ ) )
				continue;
			// [EXT:online-coop-lockstep] a shared park changes only through its commands: the host's save refuses instead of repairing on its own machine
			if ( CoopRole != CoopRole.None )
				throw new InvalidOperationException( $"{item.Entry.DisplayName} (attraction {item.Visitors.AttractionId}) has no economy binding, which a shared park cannot repair; save it after the session." );
			var held = economyInstances.Values.ToHashSet();
			if ( prototypeInstance is int prototype )
				held.Add( prototype );
			var state = Park.Economy.Objects.Where( candidate => candidate.InfoId == item.Entry.InfoId && !held.Contains( candidate.Id ) ).OrderBy( candidate => candidate.Id ).FirstOrDefault();
			var adopted = state != null;
			state ??= Park.Economy.RegisterExisting( item.Entry.InfoId );
			Log.Warning( $"{item.Entry.DisplayName} (attraction {item.Visitors.AttractionId}) had no economy binding; it {(adopted ? "adopts the unowned" : "gets a new uncharged")} economy record {state.Id} so the park can be saved." );
			LinkEconomy( item, state.Id );
		}
	}

	private IReadOnlyDictionary<string, string>? autosaveAssets;
	private int autosaveAssetEntries = -1;
	private ObjectCatalog? autosaveCatalog;

	/// <summary>
	/// [EXT:periodic-autosave] The asset manifest of this park, hashed once and kept: it covers the whole pinned catalogue and balance files, which do not change while a park is played,
	/// and hashing them again for every autosave would be the most expensive part of a capture (the park start hashes them anyway, for its restart snapshot, and hands the result in as
	/// <paramref name="known"/>). The manifest is taken again when the catalogue or its entry count changed (the pools extension adds its objects while the player uses it).
	/// </summary>
	internal IReadOnlyDictionary<string, string> AutosaveAssets( IReadOnlyDictionary<string, string>? known = null )
	{
		var catalog = Objects.Catalog; var entries = catalog.Entries.Count;
		if ( autosaveAssets == null || autosaveAssetEntries != entries || !ReferenceEquals( autosaveCatalog, catalog ) )
		{
			autosaveAssets = known ?? ParkResumeAssetManifest.Capture( Objects.Catalog, Park?.Economy.Settings ?? throw new InvalidOperationException( "Only a writable Theme Park World park can be saved here." ) );
			autosaveAssetEntries = entries; autosaveCatalog = catalog;
		}
		return autosaveAssets;
	}

	/// <summary>
	/// <see cref="AutosaveAssets"/> hashed in steps (one catalogue archive each) for a park about to be entered, so its restart snapshot finds the manifest ready instead of
	/// hashing it all in the entering frame (docs/BROWSER-PERFORMANCE.md "Event hitches"); the result is the same dictionary the one-go call makes, kept the same way.
	/// </summary>
	internal IEnumerable<bool> PrepareAutosaveAssets()
	{
		var catalog = Objects.Catalog; var entries = catalog.Entries.Count;
		if ( autosaveAssets != null && autosaveAssetEntries == entries && ReferenceEquals( autosaveCatalog, catalog ) )
			yield break;
		var settings = Park?.Economy.Settings ?? throw new InvalidOperationException( "Only a writable Theme Park World park can be saved here." );
		var manifest = new SortedDictionary<string, string>( StringComparer.Ordinal );
		foreach ( var step in ParkResumeAssetManifest.CaptureSteps( catalog, settings.Theme, settings.IsEasy, manifest ) )
			yield return step;
		// the catalogue did not change while the steps ran between frames
		if ( catalog.Entries.Count == entries && ReferenceEquals( Objects.Catalog, catalog ) )
		{
			autosaveAssets = manifest; autosaveAssetEntries = entries; autosaveCatalog = catalog;
		}
	}

	/// <summary>
	/// [EXT:park-entry-preparation] Runs, one per step, the parts of <see cref="CaptureResumeSnapshotUnchecked(AutomaticAdvisor?, string, out Action, IReadOnlyDictionary{string, string}?, DateTimeOffset?, bool)"/>
	/// that only read the park (the economy, layout, guest, thing-pool, script-world, weather, gate, staff, ride, pool, tour and extension captures, which the periodic autosave runs on a live park without changing it) and drops what
	/// they make: the browser's runtime then initializes their code in a park build step instead of the frame that enters the park and takes the restart snapshot
	/// (docs/BROWSER-PERFORMANCE.md, "Event hitches"). A part that refuses is skipped; the snapshot meets the same refusal and reports it.
	/// </summary>
	internal IEnumerable<bool> WarmResumeCaptureSteps()
	{
		if ( Park == null || Guests == null )
			yield break;
		static void Read( Action read )
		{
			try { read(); }
			catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or InvalidOperationException or ArgumentException ) { }
		}
		// the economy capture's parts first, one per step: its first call alone was a long step in the browser
		using ( var parts = ParkSaveFile.CapturePartSteps( Park.Economy ).GetEnumerator() )
		{
			while ( true )
			{
				var more = false;
				Read( () => more = parts.MoveNext() );
				if ( !more )
					break;
				yield return true;
			}
		}
		Read( () => ParkSaveFile.Capture( Park.Economy, CaptureRandomState() ) );
		yield return true;
		Read( () => ParkLayoutSnapshot.CaptureSnapshot( Guests.Grid.Cells, Objects ) );
		yield return true;
		Read( () => Guests.CaptureSnapshot() );
		yield return true;
		if ( Objects.Things != null )
		{
			Read( () => Things.CaptureSnapshot() );
			yield return true;
		}
		Read( () => Objects.ScriptWorld.CaptureSnapshot() );
		yield return true;
		// the snapshot's extension and asset filters over the placed objects, with a one-key manifest of an extension's prefix: the filters' queries meet their first calls here
		Read( () =>
		{
			var placed = Objects.Objects.Where( item => !item.IsDeleted ).Select( item => item.Entry ).ToArray();
			_ = ParkResumeMods.InUse( Guests.Grid.Cells, placed.Any( ParkResumeMods.IsPoolsObject ), ParkResumeMods.UmbrellasInUse( placed, Guests ), ParkResumeMods.WinterInUse( placed, Guests ),
				Park.Economy.Cheats.UsesExtension, ParkResumeMods.EveningInUse( placed ) );
			// the manifest's own type (a sorted dictionary, as ParkResumeAssetManifest makes it): its enumerators' first calls are of that type
			_ = ParkResumeMods.AssetsForUse( new SortedDictionary<string, string>( StringComparer.Ordinal ) { [ContentRoot.Mod( OfficialExtensions.Pools.Id ).KeyPrefix] = "" }, placed );
		} );
		yield return true;
		Read( () =>
		{
			_ = Weather?.CaptureSnapshot(); _ = Gate?.CaptureSnapshot(); _ = StaffWorld?.CaptureSnapshot();
			_ = CaptureCoastersSnapshot(); _ = CaptureTracksSnapshot(); _ = PoolSlides?.CaptureSnapshot(); _ = PoolBridges?.CaptureSnapshot(); _ = CaptureToursSnapshot();
			_ = SmarterGuestsSave.Capture( Guests ); _ = ScriptModsSave.Capture( Guests );
		} );
		yield return true;
	}

	/// <summary>Drops the kept manifest, so the next <see cref="AutosaveAssets"/> hashes the files again.</summary>
	internal void ForgetAutosaveAssets() => autosaveAssets = null;

	/// <summary>Captures a writable fixed-ride park between simulation ticks, including its direct instance links.</summary>
	internal ParkResumeSnapshot CaptureResumeSnapshot( AutomaticAdvisor? advisor, string displayName,
		IReadOnlyDictionary<string, string>? verifiedAssets = null, DateTimeOffset? savedUtc = null, bool advisorMaySpeak = false )
	{
		var snapshot = CaptureResumeSnapshotUnchecked( advisor, displayName, out var validate, verifiedAssets, savedUtc, advisorMaySpeak );
		validate();
		return snapshot;
	}

	/// <summary>
	/// The capture of <see cref="CaptureResumeSnapshot"/> without its closing validation, which is handed back as <paramref name="validate"/> instead. The validation reads only the captured copy
	/// and the trusted catalogue and balance, so the periodic autosave runs it in a later frame than the capture, which alone has to see the park between two ticks.
	/// </summary>
	internal ParkResumeSnapshot CaptureResumeSnapshotUnchecked( AutomaticAdvisor? advisor, string displayName, out Action validate,
		IReadOnlyDictionary<string, string>? verifiedAssets = null, DateTimeOffset? savedUtc = null, bool advisorMaySpeak = false ) =>
		CaptureResumeSnapshotUnchecked( advisor, displayName, out validate, out _, verifiedAssets, savedUtc, advisorMaySpeak );

	/// <summary>
	/// <see cref="CaptureResumeSnapshotUnchecked(AutomaticAdvisor?, string, out Action, IReadOnlyDictionary{string, string}?, DateTimeOffset?, bool)"/> that also hands back the same validation
	/// in steps (<see cref="ParkResumeSaveFile.ValidateSnapshotSteps"/>), to be spread over frames; nothing of it runs until it is enumerated.
	/// </summary>
	internal ParkResumeSnapshot CaptureResumeSnapshotUnchecked( AutomaticAdvisor? advisor, string displayName, out Action validate, out IEnumerable<bool> validationSteps,
		IReadOnlyDictionary<string, string>? verifiedAssets = null, DateTimeOffset? savedUtc = null, bool advisorMaySpeak = false )
	{
		if ( IsReadOnlyVisit || GameEditions.IsThemeParkInc || OriginalPark == null || Park == null || Guests == null )
			throw new InvalidOperationException( "Only a writable Theme Park World park can be saved here." );
		using ( FrameMarks.Measure( "capture.prepare" ) )
		{
			RepairMissingEconomyBindings();
			RequireResumableCatalogueDefaults();
			StaffPlacement?.PrepareForSave();
		}
		var prototype = CapturePrototypeSnapshot();
		ParkResumeRuntime[] runtimes;
		using ( FrameMarks.Measure( "capture.runtimes" ) )
			runtimes = Objects.Objects.Select( item =>
			{
				var state = item.Runtime.CaptureSnapshot();
				return new ParkResumeRuntime( state.HasScript && !state.Stopped ? item.Runtime.Script!.ScriptId : 0, state );
			} ).ToArray();
		// [EXT:pools-save] the extension is recorded while the park has a pool cell or a changing room; its manifest keys are those of the objects the park placed (a changing room), not of its whole content.
		// [EXT:umbrellas-save] opentpw.umbrellas likewise: recorded while the park has a brolly shop or a guest with an umbrella, with the keys of the placed shops only
		var placedEntries = Objects.Objects.Where( item => !item.IsDeleted ).Select( item => item.Entry ).ToArray();
		// [EXT:winter-save] opentpw.winter likewise: recorded while the park has a winter kiosk or a glühwein stall or a guest with a warm item
		// [EXT:cheats-save] opentpw.cheats likewise: recorded once the park has used one of the extension's own cheats
		// [EXT:evening-torches-save] opentpw.evening likewise: recorded while the park holds a torch
		var extensionsInUse = ParkResumeMods.InUse( Guests.Grid.Cells, placedEntries.Any( ParkResumeMods.IsPoolsObject ), ParkResumeMods.UmbrellasInUse( placedEntries, Guests ), ParkResumeMods.WinterInUse( placedEntries, Guests ),
			Park.Economy.Cheats.UsesExtension, ParkResumeMods.EveningInUse( placedEntries ) );
		var assets = ParkResumeMods.AssetsForUse( verifiedAssets ?? ParkResumeAssetManifest.Capture( Objects.Catalog, Park.Economy.Settings ), placedEntries );
		var random = CaptureRandomState();
		// the parts in the order the snapshot lists them (the first five are measured on their own: docs/BROWSER-PERFORMANCE.md, "Event hitches")
		ParkSaveFile.Data economy; ParkLayoutSnapshot.Data layout; GuestSimulationSnapshot guests; ThingPoolSnapshot? things; RideScriptWorldSnapshot scripts;
		using ( FrameMarks.Measure( "capture.economy" ) )
			economy = ParkSaveFile.Capture( Park.Economy, random );
		using ( FrameMarks.Measure( "capture.layout" ) )
			layout = ParkLayoutSnapshot.CaptureSnapshot( Guests.Grid.Cells, Objects );
		using ( FrameMarks.Measure( "capture.guests" ) )
			guests = Guests.CaptureSnapshot();
		using ( FrameMarks.Measure( "capture.things" ) )
			// a level whose objects never took pool slots (a fixture built around the pool) has none to save, and loads as a save from before the pool
			things = Objects.Things == null ? null : Things.CaptureSnapshot();
		using ( FrameMarks.Measure( "capture.scripts" ) )
			scripts = Objects.ScriptWorld.CaptureSnapshot();
		using var restMark = FrameMarks.Measure( "capture.rest" );
		var snapshot = new ParkResumeSnapshot
		{
			Format = ParkResumeSaveFile.FormatName,
			Version = ParkResumeSaveFile.CurrentVersion,
			Theme = LevelName,
			StartKind = Park.Start.Kind,
			Seed = Seed.Value,
			DisplayName = displayName,
			SavedUtc = savedUtc ?? DateTimeOffset.UnixEpoch,
			Economy = economy,
			Layout = layout,
			Guests = guests,
			Things = things,
			Scripts = scripts,
			Runtimes = runtimes,
			EconomyBindings = Objects.Objects.Where( economyInstances.ContainsKey )
				.Select( item => new ParkResumeEconomyBinding( item.Visitors.AttractionId, economyInstances[item] ) ).ToArray(),
			Clock = simulationClock.CaptureSnapshot(),
			SoundSeed = random.SoundSeed,
			Weather = Weather?.CaptureSnapshot(),
			Gate = Gate?.CaptureSnapshot(),
			ParkName = ParkName is { } name ? new ParkNameSaveData( name.Line1, name.Line2 ) : null,
			// [EXT:winter-snow-save] optional and unhashed: null (left out of the file) without snow
			SnowCover = CaptureSnowCover(),
			Staff = StaffWorld?.CaptureSnapshot(),
			Advisor = advisor?.CaptureSnapshot( advisorMaySpeak ),
			Coasters = CaptureCoastersSnapshot(),
			Tracks = CaptureTracksSnapshot(),
			PoolSlides = PoolSlides?.CaptureSnapshot(),
			PoolBridges = PoolBridges?.CaptureSnapshot(),
			Tours = CaptureToursSnapshot(),
			Prototype = prototype,
			Particles = ParticlesCaptured?.Invoke( this ),
			// [EXT:mods] null unless a simulation-changing extension or mod is active, so unmodded saves keep their bytes; [EXT:pools-save] opentpw.pools is recorded only while the park has pool cells or a changing room, [EXT:umbrellas-save] opentpw.umbrellas only while it has a brolly shop or an umbrella
			Mods = ParkResumeMods.Capture( OpenTpwExtensions.Active, extensionsInUse ),
			// [EXT:smarter-guests] null while the extension is off for these guests, so such saves keep their bytes
			SmarterGuests = SmarterGuestsSave.Capture( Guests ),
			// [EXT:script-mods] null while no script mod is enabled, so such saves keep their bytes
			ScriptMods = ScriptModsSave.Capture( Guests ),
			AssetHashes = assets
		};
		var catalogue = Objects.Catalog; var settings = Park.Economy.Settings; var economyCatalog = Park.Economy.Catalog;
		validate = () => ParkResumeSaveFile.ValidateSnapshot( snapshot, catalogue, settings, economyCatalog, assets );
		validationSteps = ParkResumeSaveFile.ValidateSnapshotSteps( snapshot, catalogue, settings, economyCatalog, assets );
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
		RestoreResumeRoutingGeometry( snapshot.Guests, snapshot.PoolBridges, Guests );
		Guests.RestoreSnapshot( snapshot.Guests );
		// [EXT:smarter-guests] the extension's birth ids, keyed by the guest ids the restore just rebuilt
		SmarterGuestsSave.Restore( Guests, snapshot.SmarterGuests );
		// [EXT:script-mods] the script mods' state, counters and flags
		ScriptModsSave.Restore( Guests, snapshot.ScriptMods );
		simulationClock.RestoreSnapshot( snapshot.Clock );
		if ( snapshot.Weather != null )
		{
			Weather = new ParkWeather( Park.Economy.Settings.Standard, Seed.WeatherStream, Guests.Grid.CountX, Guests.Grid.CountY, FeatureGates.WinterWeather, FeatureGates.WinterAlways );
			Weather.RestoreSnapshot( snapshot.Weather );
			// [EXT:winter-snow-save] a resumed park keeps its snow (ignored while the extension is off)
			resumedSnowCover = snapshot.SnowCover;
			AttachSnow();
		}
		// the particle pool's emitters and effectors (no particles), rebuilt by the renderer module when the host draws
		if ( snapshot.Particles != null )
			ParticlesResumed?.Invoke( this, snapshot.Particles );
		if ( snapshot.Gate != null )
			(Gate ?? throw new InvalidDataException( "The saved park gate is unavailable." )).RestoreSnapshot( snapshot.Gate );
		if ( snapshot.Staff != null )
			(StaffWorld ?? throw new InvalidDataException( "The saved staff world is unavailable." )).RestoreSnapshot( snapshot.Staff );
		// The coasters go last: their stations were placed above (each placement reset the track to its station
		// node), and this restores the saved tracks, controller state, trains and riders over them.
		RestoreCoastersSnapshot( snapshot.Coasters );
		ValidateResumeBridgeCoasterGeometry( PoolBridges, Coasters );
		RestoreTracksSnapshot( snapshot.Tracks );
		// Rebuild only derived coaster tokens, after both primary and secondary layers exist.
		BindCoasterCellsAfterLoad();
		// [EXT:pools-slides] the slides go back over their towers and the pool cells the layout restored
		PoolSlides?.RestoreSnapshot( snapshot.PoolSlides );
		PoolSlides?.ValidateGuests( Guests.Guests );
		RestoreToursSnapshot( snapshot.Tours );
		// the resumed tiles and boats are drawn from the first frame, as they were when the park was saved
		UpdateTrackRendering();
		UpdateTourRendering();
		MarkCellsRendered( Guests.Grid.Cells );
	}

	/// <summary>
	/// Plays the scripts' held screams again once the resumed park's audio has started (<see cref="GameAudio.EnterPark"/>): the restore brings every
	/// handle and its operands back without a chain, because the candidate makes no sound before it replaces the live park and leaving that park stops
	/// every chain.
	/// </summary>
	internal void ResumeHeldScreams()
	{
		foreach ( var item in Objects.Objects )
			item.Runtime.ResumeHeldScream();
		PlacedRide?.Runtime.ResumeHeldScream();
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
			null, saved.Open, saved.AttractionId );
		var gaps = ride.Runtime.RestoreSnapshot( saved.Runtime );
		if ( gaps.Count != 0 ) throw new InvalidDataException( string.Join( "; ", gaps ) );
		var registered = PrototypeRideSave.RestoreGuestCells( saved, ride.Visitors, Guests! );
		// [EXT:developer-prototype] a prototype saved before the build laid its stand cell gets it at load (a cell that holds a type is left as it is)
		if ( registered )
		{
			var (standX, standY) = GuestSimulation.StandCellOf( ride.Visitors );
			ObjectAccessCells.LayStandCell( Guests!.Grid.Cells, standX, standY );
		}
		if ( registered && saved.EconomyInstanceId is int instance )
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

	/// <summary>Coaster nodes are restored after bridges; reject overlaps that the live placement tools would refuse.</summary>
	internal static void ValidateResumeBridgeCoasterGeometry( PoolBridgeWorld? world, IEnumerable<CoasterRide> coasters )
	{
		if ( world is not { Count: > 0 } ) return;
		foreach ( var ride in coasters )
			foreach ( var node in ride.Track.Nodes )
				if ( world.Reserves( node.X, node.Y ) )
					throw new InvalidDataException( $"A saved coaster node at {node.X},{node.Y} overlaps a pool bridge or its end connection." );
	}

	/// <summary>Restore the elevated graph before validating queues: an underlying path may no longer accept a queue join.</summary>
	internal static void RestoreResumeRoutingGeometry( GuestSimulationSnapshot snapshot, PoolBridgesSaveData? bridgeData, GuestSimulation target )
	{
		var world = target.Grid.Cells.PoolBridges;
		if ( bridgeData != null )
		{
			if ( world == null ) throw new InvalidDataException( "The saved park's pool bridge world is unavailable." );
			world.RestoreSnapshot( bridgeData );
			foreach ( var ride in target.Attractions.OfType<RideVisitorBridge>() )
				ride.RecomputeQueue( target.Grid );
		}
		ValidateResumeQueueGeometry( snapshot, target );
		// Keep the original empty-bridge validation and restore order, including the saved map version.
		if ( bridgeData == null ) world?.RestoreSnapshot( null );
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
			if ( !bridge.MatchesSnapshotQueueGeometry( target.Grid, saved ) )
				throw new InvalidDataException( $"Attraction {saved.AttractionId}'s queue does not match the saved cell map." );
		}
	}
}
