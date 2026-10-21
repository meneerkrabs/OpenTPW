using System.Text.Json;
using OpenTPW.Mods;

namespace OpenTPW;

/// <summary>
/// OpenTPW's own-format world save: one versioned, bounded, atomic envelope around every snapshot of a running
/// park (JSON through <see cref="ParkResumeJson"/>, <c>"Format": "opentpw-world"</c>). The economy is embedded by
/// value as the legacy <see cref="ParkSaveFile.Data"/> (never re-implemented), the layout, guests, script world,
/// runtimes, economy associations, clock and streams join by identity, the coasters (tracks, controller state and
/// trains) ride along when the park held them, the developer Totem rides along as the prototype part, and the
/// optional weather/gate/staff/advisor
/// snapshots ride along. Serialization checks the envelope's shape before a byte is written; loading checks the
/// format and version, then the shape, before anything is used; full validation against the current catalog,
/// balance and asset manifest is <see cref="ValidateSnapshot"/> (root's step, before a candidate level is built).
/// Writes are atomic (temporary file + flush + move) with the own-format suffix only — original TPWS/TPWI files
/// are never written or read as world saves.
/// </summary>
internal static class ParkResumeSaveFile
{
	public const string FormatName = "opentpw-world";
	public const int CurrentVersion = 2;
	/// <summary>
	/// Hard cap on one world save: the compact envelope of a bounded initial park (the legacy economy-only save
	/// caps at 16 MB indented — ≈ 4 MB compact; the whole world adds every guest, script and cell record) fits
	/// several times over, while no read or parse is ever handed an unbounded body.
	/// </summary>
	public const int MaximumFileSize = 64 * 1024 * 1024;

	/// <summary>Serializes a shape-checked envelope; anything invalid fails before a byte exists.</summary>
	public static byte[] Serialize( ParkResumeSnapshot snapshot )
	{
		ValidateShape( snapshot );
		var bytes = ParkResumeJson.Serialize( snapshot );
		if ( bytes.Length > MaximumFileSize ) throw new InvalidDataException( "World save exceeds the size limit." );
		return bytes;
	}

	/// <summary>
	/// [EXT:periodic-autosave] Starts the time-boxed serialization of a snapshot: <paramref name="validate"/> (default: the shape check of <see cref="Serialize"/>) is its first step, then the bytes
	/// are the same as <see cref="Serialize"/>'s, produced a slice per frame. The size limit applies to the result (<see cref="CheckSize"/>).
	/// </summary>
	internal static SlicedJsonSerializer BeginSlicedSerialize( ParkResumeSnapshot snapshot, Action? validate = null ) =>
		ParkResumeJson.Slice( snapshot, validate ?? ( () => ValidateShape( snapshot ) ) );

	/// <summary>The size limit of <see cref="Serialize"/> for bytes serialized in slices.</summary>
	internal static byte[] CheckSize( byte[] bytes ) => bytes.Length > MaximumFileSize ? throw new InvalidDataException( "World save exceeds the size limit." ) : bytes;

	/// <summary>
	/// Parses a world save: the size is checked before parsing, the format and version before anything is used,
	/// then the whole envelope's shape (header, embedded economy, layout, script world, runtime and binding joins,
	/// guests, optional parts, asset manifest). Malformed input becomes <see cref="InvalidDataException"/>;
	/// nothing is mutated. Metadata for a load menu (theme, name, date, start kind, seed) is the returned
	/// snapshot's header fields.
	/// </summary>
	public static ParkResumeSnapshot Deserialize( ReadOnlySpan<byte> bytes )
	{
		if ( bytes.Length > MaximumFileSize )
			throw new InvalidDataException( "World save exceeds the size limit." );
		ParkResumeSnapshot? snapshot;
		try
		{
			snapshot = ParkResumeJson.Deserialize<ParkResumeSnapshot>( bytes );
		}
		catch ( JsonException exception )
		{
			throw new InvalidDataException( $"World save is malformed: {exception.Message}", exception );
		}
		if ( snapshot == null )
			throw new InvalidDataException( "The world save is null." );
		if ( snapshot.Format != FormatName )
			throw new InvalidDataException( $"Not an OpenTPW world save: '{snapshot.Format}'." );
		if ( snapshot.Version == 1 )
		{
			if ( snapshot.Layout?.Cells == null || snapshot.Layout.Cells.QueuePassSchemaVersion != 0 || snapshot.Layout.Cells.QueuePassResidues != null )
				throw new InvalidDataException( "Legacy world save contains unsupported queue pass fields." );
			snapshot = snapshot with { Version = CurrentVersion };
		}
		if ( snapshot.Version != CurrentVersion )
			throw new InvalidDataException( $"Unsupported world save version {snapshot.Version}." );
		ValidateShape( snapshot );
		return snapshot;
	}

	/// <summary>Writes atomically (a complete temporary file is flushed and moved over the destination), so a
	/// failed write leaves the previous save in place. The destination must carry the own-format suffix.</summary>
	public static void Save( string path, ParkResumeSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		SaveBytes( path, Serialize( snapshot ) );
	}

	/// <summary>The atomic write of <see cref="Save"/> for bytes that <see cref="Serialize"/> already produced (the periodic autosave serializes on the game thread and writes in the background).</summary>
	public static void SaveBytes( string path, byte[] bytes )
	{
		ArgumentNullException.ThrowIfNull( bytes );
		var destination = GetPath( path, forWriting: true );
		var temporary = destination + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
		var ownsTemporary = false;
		try
		{
			using ( var stream = new FileStream( temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None ) )
			{
				ownsTemporary = true;
				stream.Write( bytes );
				stream.Flush( true );
			}
			File.Move( temporary, destination, true );
		}
		finally
		{
			if ( ownsTemporary )
				File.Delete( temporary );
		}
	}

	public static ParkResumeSnapshot Load( string path )
	{
		var destination = GetPath( path, forWriting: false );
		using var stream = new FileStream( destination, FileMode.Open, FileAccess.Read, FileShare.Read );
		if ( stream.Length > MaximumFileSize )
			throw new InvalidDataException( "World save exceeds the size limit." );
		var bytes = new byte[stream.Length];
		stream.ReadExactly( bytes );
		return Deserialize( bytes );
	}

	/// <summary>
	/// Validates the complete envelope against the loading park: the shape (see <see cref="ValidateShape"/>),
	/// the layout against the current <paramref name="catalog"/>, the theme, balance layer and mode against
	/// <paramref name="settings"/>, every live coaster station saved with a matching coaster part, every economy
	/// binding's instance against
	/// the economy catalogue, and the asset manifest's exact key and digest agreement with
	/// <paramref name="currentAssetHashes"/> (computed from verified catalog/game-data paths — a path saved in
	/// the file is never read back). Pure: no live object is touched, so root can reject a save before building a
	/// candidate level.
	/// </summary>
	public static void ValidateSnapshot( ParkResumeSnapshot snapshot, ObjectCatalog catalog, BalanceSettings settings,
		IEconomyObjectCatalog economyCatalog, IReadOnlyDictionary<string, string> currentAssetHashes, ModSet? activeMods = null )
	{
		ArgumentNullException.ThrowIfNull( catalog );
		ArgumentNullException.ThrowIfNull( settings );
		ArgumentNullException.ThrowIfNull( economyCatalog );
		foreach ( var _ in ValidateSnapshotSteps( snapshot, catalog, settings, economyCatalog, currentAssetHashes, activeMods ) ) { }
	}

	/// <summary>
	/// [EXT:restart-snapshot-deferred-validation] <see cref="ValidateSnapshot"/> in steps: the same checks in the same order, with a pause between groups of them, so a caller
	/// can spread them over frames (the restart snapshot's write, docs/BROWSER-PERFORMANCE.md "Event hitches"). The validation is complete, and has accepted the snapshot,
	/// only when the enumeration ends; a refusal throws from the step that finds it.
	/// </summary>
	internal static IEnumerable<bool> ValidateSnapshotSteps( ParkResumeSnapshot snapshot, ObjectCatalog catalog, BalanceSettings settings,
		IEconomyObjectCatalog economyCatalog, IReadOnlyDictionary<string, string> currentAssetHashes, ModSet? activeMods = null )
	{
		ArgumentNullException.ThrowIfNull( catalog );
		ArgumentNullException.ThrowIfNull( settings );
		ArgumentNullException.ThrowIfNull( economyCatalog );
		foreach ( var step in ValidateShapeSteps( snapshot ) )
			yield return step;
		// [EXT:mods] the mods the park was saved with must be the ones that are active, before anything else is judged (docs/MODS.md)
		ParkResumeMods.Require( snapshot.Mods, activeMods ?? OpenTpwExtensions.Active );
		// [EXT:script-mods] the section exists exactly when a script mod is active (and so recorded), and names exactly those mods
		if ( ScriptModsSave.ActiveProblem( snapshot.ScriptMods, activeMods ?? OpenTpwExtensions.Active ) is { } scriptMismatch )
			throw new InvalidDataException( $"World save is inconsistent: {scriptMismatch}." );
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"World save is inconsistent: {message}." );
		}
		yield return true;
		ParkLayoutSnapshot.ValidateSnapshot( snapshot.Layout, catalog, activeMods );
		yield return true;
		Require( string.Equals( catalog.Theme, snapshot.Theme, StringComparison.OrdinalIgnoreCase ),
			$"the object catalog is for theme '{catalog.Theme}', not the saved '{snapshot.Theme}'" );
		Require( string.Equals( settings.Theme, snapshot.Theme, StringComparison.OrdinalIgnoreCase ) && settings.IsEasy == snapshot.Economy.Easy,
			$"the balance is {settings.Theme}{( settings.IsEasy ? " (easy)" : "" )}, not the saved {snapshot.Economy.Theme}{( snapshot.Economy.Easy ? " (easy)" : "" )}" );
		// Coasters: every live (not stopped) coaster station must be saved — a stopped station's controller was torn
		// down at capture, so it holds no state — and every saved entry must fit its entry's coaster definition.
		var liveCoasterStations = new HashSet<int>();
		var definitionsByAttraction = new Dictionary<int, CoasterDefinition>();
		for ( var index = 0; index < snapshot.Layout.Objects.Count; index++ )
		{
			var record = snapshot.Layout.Objects[index]!;
			var entry = catalog.Find( record.InfoId )!;
			if ( entry.Settings == null || !CoasterDefinition.IsCoaster( entry.Settings ) )
				continue;
			if ( !snapshot.Runtimes[index].State.Stopped )
			{
				liveCoasterStations.Add( record.AttractionId );
				definitionsByAttraction[record.AttractionId] = CoasterDefinition.FromEntry( entry );
			}
		}
		if ( snapshot.Coasters == null )
		{
			foreach ( var attractionId in liveCoasterStations )
				Require( false, $"attraction {attractionId} is a live coaster station, but the save carries no coaster part" );
		}
		else
		{
			Require( snapshot.Coasters.Rides.Count == liveCoasterStations.Count
				&& snapshot.Coasters.Rides.All( ride => liveCoasterStations.Contains( ride.AttractionId ) ),
				$"the save holds {snapshot.Coasters.Rides.Count} coasters, but the park places {liveCoasterStations.Count} live coaster stations" );
			foreach ( var ride in snapshot.Coasters.Rides )
			{
				var definition = definitionsByAttraction[ride.AttractionId];
				Require( ride.Trains.Count <= definition.MaxTrains,
					$"coaster {ride.AttractionId} holds {ride.Trains.Count} trains, above the definition's {definition.MaxTrains}" );
				foreach ( var train in ride.Trains )
				{
					Require( train.Cars.Count == definition.MaxCars,
						$"a train of coaster {ride.AttractionId} holds {train.Cars.Count} cars, not the definition's {definition.MaxCars}" );
					Require( train.BoardingCars == null || train.BoardingCars.Count == definition.MaxCars,
						$"a train of coaster {ride.AttractionId} holds {train.BoardingCars?.Count} boarding lists, not the definition's {definition.MaxCars}" );
					Require( train.ReferenceCar == null || train.ReferenceCar == definition.ReferenceCar,
						$"a train of coaster {ride.AttractionId} is placed by car {train.ReferenceCar}, not the definition's reference car {definition.ReferenceCar}" );
				}
			}
		}
		yield return true;
		// Tracks: every live track ride (water or kart) must be saved with a controller of its template's type, and no controller may exceed its template's vehicle limit.
		var liveTrackRides = new Dictionary<int, BumperTemplate>();
		var raisedRides = new HashSet<int>();
		for ( var index = 0; index < snapshot.Layout.Objects.Count; index++ )
		{
			var record = snapshot.Layout.Objects[index]!;
			var entry = catalog.Find( record.InfoId )!;
			if ( snapshot.Runtimes[index].State.VehiclesRaised == true )
				raisedRides.Add( record.AttractionId );
			if ( entry.Settings != null && !snapshot.Runtimes[index].State.Stopped
				&& BumperTemplates.ForRide( entry.Settings.GetInt( "Bumper.WhichTrackType" ), entry.Settings.GetInt( "Bumper.BumperType" ) ) is { } template )
				liveTrackRides[record.AttractionId] = template;
		}
		var savedControllers = snapshot.Tracks?.Controllers ?? new List<BumpControllerSaveData>();
		// [EXT:cheats-ride-vehicles] a live raised bumper or kart ride carries its raised limit in its saved controller (a kart ride may lack a controller only in a save from before them, which no raised ride is)
		foreach ( var (id, template) in liveTrackRides )
			if ( raisedRides.Contains( id ) )
				Require( savedControllers.Any( controller => controller.AttractionId == id && controller.VehicleLimit != null ), $"raised ride {id} has no saved controller with its raised vehicle limit" );
		Require( TrackControllersMatch( liveTrackRides, savedControllers.Select( controller => controller.AttractionId ).ToList() ),
			$"the save holds {savedControllers.Count} track controllers, but the park places {liveTrackRides.Count} live track rides" );
		foreach ( var controller in savedControllers )
		{
			var template = liveTrackRides[controller.AttractionId];
			Require( controller.Type == template.Type, $"track controller {controller.AttractionId} has type {controller.Type}, not its ride's {template.Type}" );
			// [EXT:cheats-ride-vehicles] a bumper or kart ride the cheat raised holds up to its raised limit
			var limit = controller.VehicleLimit ?? template.MaxVehicles;
			Require( controller.VehicleLimit == null || (template.IsBumper || template.IsKart) && controller.VehicleLimit == BumpController.RaisedVehicleLimit,
				$"track controller {controller.AttractionId} has the vehicle limit {controller.VehicleLimit}, which only a raised bumper or kart ride has" );
			Require( controller.Vehicles.Count <= limit, $"track controller {controller.AttractionId} holds {controller.Vehicles.Count} boats, above {limit}" );
			Require( (controller.VehicleLimit != null) == raisedRides.Contains( controller.AttractionId ), $"track controller {controller.AttractionId}'s vehicle limit does not match its ride's raised flag" );
		}
		yield return true;
		var instancesById = snapshot.Economy.Objects.ToDictionary( item => item.Id );
		var bindingsByAttraction = snapshot.EconomyBindings.ToDictionary( binding => binding.AttractionId );
		foreach ( var record in snapshot.Layout.Objects )
			if ( economyCatalog.TryGet( record.InfoId, out _ ) )
				Require( bindingsByAttraction.ContainsKey( record.AttractionId ),
					$"placed attraction {record.AttractionId} is missing its economy binding" );
		foreach ( var binding in snapshot.EconomyBindings )
		{
			var instance = instancesById[binding.InstanceId];
			Require( economyCatalog.TryGet( instance.InfoId, out _ ),
				$"economy object {instance.Id} has Info.Id {instance.InfoId}, which the {settings.Theme} economy catalogue does not know" );
		}
		// The prototype's instance must be one this theme's economy knows too (a live Totem only got one when its
		// catalogue listed the Totem, so anything else is not a state this format could have saved).
		if ( snapshot.Prototype?.EconomyInstanceId is { } prototypeInstance )
		{
			var prototypeObject = instancesById[prototypeInstance];
			Require( economyCatalog.TryGet( prototypeObject.InfoId, out _ ),
				$"the prototype's economy object {prototypeInstance} has Info.Id {prototypeObject.InfoId}, which the {settings.Theme} economy catalogue does not know" );
		}
		yield return true;
		Require( currentAssetHashes != null && currentAssetHashes.Count > 0, "the current asset manifest is empty" );
		RequireAssets( snapshot, catalog, currentAssetHashes! );
	}

	/// <summary>
	/// The asset manifest's exact key and digest agreement. [EXT:pools-save] The pools extension's keys count only for the extension objects the save places (<see cref="ParkResumeMods.AssetsForUse"/>), in
	/// both directions: an unplaced object of the extension, in the park's assets or in the save, is ignored, so adding objects to the extension never invalidates an older save, while a placed object keeps the exact-hash check.
	/// </summary>
	internal static void RequireAssets( ParkResumeSnapshot snapshot, ObjectCatalog catalog, IReadOnlyDictionary<string, string> currentAssetHashes )
	{
		void require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"World save is inconsistent: {message}." );
		}
		var placed = snapshot.Layout.Objects.Select( record => catalog.Find( record.InfoId ) ).OfType<ObjectCatalogEntry>().ToArray();
		var park = ParkResumeMods.AssetsForUse( currentAssetHashes, placed );
		var saved = ParkResumeMods.AssetsForUse( snapshot.AssetHashes, placed );
		foreach ( var (key, hash) in saved )
		{
			require( park.TryGetValue( key, out var current ),
				$"the save expects asset '{key}', which the park's assets do not provide" );
			require( string.Equals( hash, current, StringComparison.Ordinal ),
				$"asset '{key}' hashes to '{current}', not the saved '{hash}'" );
		}
		foreach ( var key in park.Keys )
			require( saved.ContainsKey( key ),
				$"the park provides asset '{key}', which the save does not expect" );
	}

	/// <summary>
	/// The envelope's own consistency, with no catalog or settings: header bounds, the embedded economy's format
	/// and invariants, the layout's pure allocator/identity rules, the script world's graph, one runtime per
	/// layout object with matching entry/attraction/script identity and every world root claimed (the prototype
	/// part claims the developer Totem's the same way, and its guest cells join the guests' records both ways), the
	/// attraction-to-economy-instance associations, the guests' structural cross-references and map bounds, the
	/// coaster part's shape (when the park held coasters), the optional parts' bounds, and the asset manifest's
	/// shape. This is what <see cref="Serialize"/> checks before
	/// writing; the catalog/theme/balance/asset checks that need the loading park are
	/// <see cref="ValidateSnapshot"/>'s.
	/// </summary>
	internal static void ValidateShape( ParkResumeSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		foreach ( var _ in ValidateShapeSteps( snapshot ) ) { }
	}

	/// <summary><see cref="ValidateShape"/> in steps (see <see cref="ValidateSnapshotSteps"/>): the same checks in the same order, complete only when the enumeration ends.</summary>
	internal static IEnumerable<bool> ValidateShapeSteps( ParkResumeSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"World save is inconsistent: {message}." );
		}
		Require( snapshot.Format == FormatName, $"the format is '{snapshot.Format}', not '{FormatName}'" );
		Require( snapshot.Version == CurrentVersion, $"version {snapshot.Version} is not {CurrentVersion}" );
		Require( snapshot.Theme != null && ObjectCatalog.Themes.Contains( snapshot.Theme, StringComparer.OrdinalIgnoreCase ),
			$"theme '{snapshot.Theme}' is not a Theme Park World theme" );
		Require( Enum.IsDefined( snapshot.StartKind ), $"start kind {snapshot.StartKind} is not defined" );
		Require( !string.IsNullOrWhiteSpace( snapshot.DisplayName ), "the display name is empty" );
		Require( snapshot.DisplayName!.Length <= ParkResumeSnapshot.MaximumDisplayNameLength,
			$"the display name has {snapshot.DisplayName.Length} characters, above {ParkResumeSnapshot.MaximumDisplayNameLength}" );
		Require( snapshot.DisplayName.All( character => !char.IsControl( character ) ), "the display name holds control characters" );
		Require( snapshot.Economy != null, "the economy snapshot is missing" );
		Require( snapshot.Economy!.Format == ParkSaveFile.FormatName, $"the embedded economy is '{snapshot.Economy.Format}', not '{ParkSaveFile.FormatName}'" );
		Require( snapshot.Economy.Version == ParkSaveFile.CurrentVersion,
			$"the embedded economy is version {snapshot.Economy.Version}, not {ParkSaveFile.CurrentVersion}" );
		Require( string.Equals( snapshot.Economy.Theme, snapshot.Theme, StringComparison.OrdinalIgnoreCase ),
			$"the economy is for theme '{snapshot.Economy.Theme}', not '{snapshot.Theme}'" );
		// Start kind pins the economy mode (ParkStart.Resolve's map); only an Instant Action park runs the
		// easy layer (Full Simulation never does; the reference path's easy flag needs the level's files, root's).
		Require( snapshot.Economy.Mode == ( snapshot.StartKind == ParkStartKind.InstantAction ? ParkGameMode.InstantAction : ParkGameMode.FullSimulation ),
			$"start kind {snapshot.StartKind} cannot run in economy mode {snapshot.Economy.Mode}" );
		Require( snapshot.StartKind != ParkStartKind.FullSimulation || !snapshot.Economy.Easy,
			"a full-simulation park is never on the easy balance layer" );
		Require( snapshot.Economy.Objects != null, "the economy holds no object list" );
		Require( snapshot.Economy.Staff != null && snapshot.Economy.Staff.Members != null, "the economy holds no staff list" );
		ValidateInner( () => ParkSaveFile.Validate( snapshot.Economy ), "embedded economy" );
		yield return true;
		Require( snapshot.Layout != null, "the layout snapshot is missing" );
		Require( snapshot.Guests != null, "the guest snapshot is missing" );
		Require( snapshot.Scripts != null, "the script-world snapshot is missing" );
		Require( snapshot.Scripts!.Scripts != null, "the script world holds no script list" );
		Require( snapshot.Runtimes != null, "the runtime list is missing" );
		Require( snapshot.EconomyBindings != null, "the economy-binding list is missing" );
		Require( snapshot.Clock != null, "the clock snapshot is missing" );
		Require( snapshot.AssetHashes != null, "the asset manifest is missing" );
		// [EXT:mods] the recorded mods: bounded, valid ids and digests, and every mod: asset key names a recorded mod
		if ( ParkResumeMods.ShapeProblem( snapshot.Mods, snapshot.AssetHashes ) is { } modProblem )
			Require( false, modProblem );
		// [EXT:pools-save] a park with pool cells records the pools extension (a park without them does not), so a save cannot hide them from the mod check
		if ( snapshot.Layout?.Cells?.Cells?.Any( cell => cell?.Type == ParkCellTypes.PoolWater ) == true
			&& snapshot.Mods?.Any( mod => mod?.Id == OfficialExtensions.Pools.Id ) != true )
			Require( false, $"the layout has pool cells but the save does not record {OfficialExtensions.Pools.Id}" );
		// [EXT:umbrellas-save] a park with a guest who has an umbrella records the umbrellas extension (a park without one does not), so a save cannot hide the umbrella state from the mod check
		if ( snapshot.Guests?.Guests?.Any( guest => guest != null && guest.UmbrellaState != 0 ) == true
			&& snapshot.Mods?.Any( mod => mod?.Id == OfficialExtensions.Umbrellas.Id ) != true )
			Require( false, $"a guest has an umbrella but the save does not record {OfficialExtensions.Umbrellas.Id}" );
		// [EXT:winter-save] a park with a guest who has a warm item records the winter extension (a park without one does not), so a save cannot hide the items from the mod check
		if ( snapshot.Guests?.Guests?.Any( guest => guest != null && (guest.WarmItems != 0 || guest.WarmUntilDay != 0) ) == true
			&& snapshot.Mods?.Any( mod => mod?.Id == OfficialExtensions.Winter.Id ) != true )
			Require( false, $"a guest has a warm item but the save does not record {OfficialExtensions.Winter.Id}" );
		// [EXT:cheats-save] a park that used one of the cheats extension's own cheats (or has one waiting) records the extension (a park cheated only through the original's cheats does not), so a save cannot hide them from the mod check
		if ( snapshot.Economy?.Cheats is { } cheats && (cheats.Extension || cheats.Pending?.Any( action => action != null && ParkCheats.IsExtensionKind( action.Kind ) ) == true)
			&& snapshot.Mods?.Any( mod => mod?.Id == OfficialExtensions.Cheats.Id ) != true )
			Require( false, $"the park used the extension's cheats but the save does not record {OfficialExtensions.Cheats.Id}" );
		// [EXT:smarter-guests] the extension's section: present exactly when the extension is recorded, with ids that fit the guests the save holds
		if ( SmarterGuestsSave.ShapeProblem( snapshot.SmarterGuests, snapshot.Guests?.Guests?.Select( guest => guest.Id ), snapshot.Mods, snapshot.ScriptMods != null, snapshot.Guests?.Attractions?.Select( attraction => attraction.AttractionId ) ) is { } smarterProblem )
			Require( false, smarterProblem );
		// [EXT:script-mods] the script mods' section: every key a recorded mod, the versions this build runs, canonical state and counters that agree with the disabled flag; it needs the identity section above
		if ( ScriptModsSave.ShapeProblem( snapshot.ScriptMods, snapshot.Mods ) is { } scriptProblem )
			Require( false, scriptProblem );
		Require( snapshot.ScriptMods == null || snapshot.SmarterGuests != null, "the save has script mod state but no guest identity state (the SmarterGuests section)" );
		yield return true;

		// Layout: the pure identity and allocator rules (catalog resolution is ValidateSnapshot's).
		var layout = snapshot.Layout!;
		Require( layout.Cells != null, "the layout has no cell map" );
		Require( layout.Cells!.Width > 0 && layout.Cells.Height > 0 && layout.Cells.Width <= 1024 && layout.Cells.Height <= 1024,
			$"the cell map is {layout.Cells.Width}×{layout.Cells.Height}" );
		Require( layout.Cells.Version >= 0, "the cell map version is negative" );
		Require( layout.Cells.Cells != null, "the cell map has no cell records" );
		Require( layout.Cells.Cells!.Count <= (long)layout.Cells.Width * layout.Cells.Height, "more cell records than cells" );
		Require( layout.Objects != null, "the layout has no object list" );
		Require( layout.NextSeed >= 1, "the placement-seed allocator is before the first placement" );
		Require( layout.NextSeed < int.MaxValue, "the placement-seed allocator is exhausted" );
		Require( layout.NextAttractionId >= 1, "the attraction-id allocator is before the first allocation" );
		Require( layout.NextAttractionId < int.MaxValue, "the attraction-id allocator is exhausted" );
		Require( layout.ObjectScriptKey == new WorldSeed( snapshot.Seed ).ObjectScriptKey,
			$"the layout's object-script key {layout.ObjectScriptKey} does not derive from world seed {snapshot.Seed}" );
		var objectsByAttraction = new Dictionary<int, ParkLayoutSnapshot.PlacedObjectData>();
		var ordinals = new HashSet<int>();
		var previousAttractionId = 0;
		for ( var index = 0; index < layout.Objects!.Count; index++ )
		{
			var record = layout.Objects[index];
			Require( record != null, $"layout object {index} is missing" );
			var ordinal = record!.CreationSeed ^ layout.ObjectScriptKey;
			Require( ordinal >= 1 && ordinal < layout.NextSeed,
				$"object {index}'s creation seed is not the {layout.ObjectScriptKey}-keyed ordinal of a placement before {layout.NextSeed}" );
			Require( ordinals.Add( ordinal ), "two objects share one placement ordinal" );
			Require( record.AttractionId >= 1 && record.AttractionId < layout.NextAttractionId,
				$"object {index}'s attraction id {record.AttractionId} is outside the saved allocator (1..{layout.NextAttractionId})" );
			Require( record.AttractionId > previousAttractionId,
				$"object {index}'s attraction id {record.AttractionId} does not follow {previousAttractionId} in placement order" );
			previousAttractionId = record.AttractionId;
			Require( objectsByAttraction.TryAdd( record.AttractionId, record ), $"attraction {record.AttractionId} is placed twice" );
		}
		yield return true;

		// Script world: its own graph validation, and the same attraction-id allocator as the layout.
		foreach ( var script in snapshot.Scripts.Scripts! )
			Require( script != null, "the script world holds a missing script" );
		ValidateInner( snapshot.Scripts.ValidateSnapshot, "script world" );
		Require( layout.NextAttractionId == snapshot.Scripts.NextAttractionId,
			$"the layout's next attraction id {layout.NextAttractionId} is not the script world's {snapshot.Scripts.NextAttractionId}" );
		yield return true;

		// Runtimes: one per layout object, in order, with matching identity, and every world root a live object's.
		Require( snapshot.Runtimes!.Count == layout.Objects.Count,
			$"the park places {layout.Objects.Count} objects but carries {snapshot.Runtimes.Count} runtime snapshots" );
		var scriptsById = snapshot.Scripts.Scripts!.ToDictionary( script => script.ScriptId );
		var worldRoots = snapshot.Scripts.Scripts.Where( script => script!.ParentScriptId == 0 ).Select( script => script!.ScriptId ).ToHashSet();
		var claimedRoots = new HashSet<int>();
		for ( var index = 0; index < snapshot.Runtimes.Count; index++ )
		{
			var runtime = snapshot.Runtimes[index];
			Require( runtime != null, $"runtime {index} is missing" );
			Require( runtime!.State != null, $"runtime {index} has no state" );
			ValidateInner( runtime.State!.ValidateSnapshot, $"runtime {index}" );
			var record = layout.Objects[index];
			Require( runtime.State.AttractionId == record!.AttractionId,
				$"runtime {index} is attraction {runtime.State.AttractionId}, not the layout's {record.AttractionId}" );
			Require( runtime.State.EntryInfoId == record.InfoId,
				$"runtime {index} is Info.Id {runtime.State.EntryInfoId}, not the layout's {record.InfoId}" );
			Require( string.Equals( runtime.State.EntryArchivePath, record.Archive, StringComparison.OrdinalIgnoreCase ),
				$"runtime {index} is archive '{runtime.State.EntryArchivePath}', not the layout's '{record.Archive}'" );
			if ( runtime.State.HasScript && !runtime.State.Stopped )
			{
				Require( runtime.RootScriptId != 0, $"runtime {index} runs a script but names no root script" );
				Require( scriptsById.TryGetValue( runtime.RootScriptId, out var root ) && root!.ParentScriptId == 0,
					$"runtime {index} names root script {runtime.RootScriptId}, which the world does not hold as a root" );
				// a script of the world generator has no seed of its own; an older save's script was created with the placement's seed
				Require( root!.UsesWorldRandom || root.Seed == record.CreationSeed,
					$"runtime {index}'s root script was created with seed {root.Seed}, not the layout's creation seed {record.CreationSeed}" );
				Require( claimedRoots.Add( runtime.RootScriptId ), $"root script {runtime.RootScriptId} is claimed by two runtimes" );
			}
			else
				Require( runtime.RootScriptId == 0, $"runtime {index} names root script {runtime.RootScriptId} without a live script" );
		}
		// The prototype part (null means the park held no developer Totem — saves from before the prototype slice):
		// it joins the envelope beside the placed objects — its root-script claim, its allocator-made attraction id
		// (the Totem draws from the same allocator, so the layout's saved frontier covers it and no placed object
		// shares it), its entry identity (the part describes exactly the Inca Totem; any other entry is refused, not
		// accepted as opaque state) and its position.
		if ( snapshot.Prototype is { } prototype )
		{
			Require( prototype.Runtime != null, "the prototype part holds no runtime state" );
			ValidateInner( prototype.Runtime!.ValidateSnapshot, "prototype part" );
			Require( prototype.Runtime.AttractionId == prototype.AttractionId,
				$"the prototype names attraction {prototype.AttractionId}, but its runtime is attraction {prototype.Runtime.AttractionId}" );
			Require( prototype.Runtime.EntryInfoId == PrototypeRide.InfoId
				&& string.Equals( prototype.Runtime.EntryArchivePath, PrototypeRide.ArchivePath, StringComparison.OrdinalIgnoreCase ),
				$"the prototype part is Info.Id {prototype.Runtime.EntryInfoId} ('{prototype.Runtime.EntryArchivePath}'), not the Inca Totem ({PrototypeRide.InfoId}, '{PrototypeRide.ArchivePath}')" );
			Require( prototype.AttractionId >= 1 && prototype.AttractionId < layout.NextAttractionId,
				$"the prototype's attraction id {prototype.AttractionId} is outside the saved allocator (1..{layout.NextAttractionId})" );
			Require( !objectsByAttraction.ContainsKey( prototype.AttractionId ),
				$"the prototype's attraction id {prototype.AttractionId} is also a placed object's" );
			Require( float.IsFinite( prototype.PositionX ) && float.IsFinite( prototype.PositionY ) && float.IsFinite( prototype.PositionZ )
				&& Math.Abs( prototype.PositionX ) <= PrototypeRideSave.MaximumCoordinate
				&& Math.Abs( prototype.PositionY ) <= PrototypeRideSave.MaximumCoordinate
				&& Math.Abs( prototype.PositionZ ) <= PrototypeRideSave.MaximumCoordinate,
				"the prototype stands at a position that is not finite or outside the map-scale bound" );
			// The guest cells the live bridge queued at, saved verbatim (their placement-time derivation is history:
			// the live park keeps it across later grid edits, so it cannot be re-derived from this save's grid). Both
			// or neither — and present cells sit inside the cell map the guests walk.
			Require( prototype.EntranceCell.HasValue == prototype.ExitCell.HasValue,
				"the prototype holds one guest cell but not the other" );
			if ( prototype.EntranceCell is { } entrance )
			{
				Require( (uint)entrance.X < (uint)layout.Cells.Width && (uint)entrance.Y < (uint)layout.Cells.Height,
					$"the prototype's entrance cell ({entrance.X}, {entrance.Y}) is outside the cell map" );
				var exit = prototype.ExitCell!.Value;
				Require( (uint)exit.X < (uint)layout.Cells.Width && (uint)exit.Y < (uint)layout.Cells.Height,
					$"the prototype's exit cell ({exit.X}, {exit.Y}) is outside the cell map" );
			}
			// A live Totem only ever got an economy instance through the guest registration (LinkPrototypeToEconomy
			// runs after the entrance derivation), so an instance without cells is not a state this format saved.
			Require( prototype.EconomyInstanceId == null || prototype.EntranceCell != null,
				"the prototype holds an economy instance without the guest cells its registration derived" );
			if ( prototype.Runtime.HasScript && !prototype.Runtime.Stopped )
			{
				Require( prototype.RootScriptId != 0, "the prototype runs a script but names no root script" );
				Require( scriptsById.TryGetValue( prototype.RootScriptId, out var prototypeRoot ) && prototypeRoot!.ParentScriptId == 0,
					$"the prototype names root script {prototype.RootScriptId}, which the world does not hold as a root" );
				// a script of the world generator has no seed (0); an older save's was created with the part's seed
				Require( prototypeRoot!.UsesWorldRandom ? prototype.ScriptSeed == 0 : prototypeRoot.Seed == prototype.ScriptSeed,
					$"the prototype's root script was created with seed {prototypeRoot.Seed}, not the part's {prototype.ScriptSeed}" );
				Require( claimedRoots.Add( prototype.RootScriptId ), $"root script {prototype.RootScriptId} is claimed by two runtimes" );
			}
			else
				Require( prototype.RootScriptId == 0, $"the prototype names root script {prototype.RootScriptId} without a live script" );
		}
		Require( claimedRoots.Count == worldRoots.Count,
			$"the script world holds {worldRoots.Count} root scripts, but {claimedRoots.Count} live objects name one" );
		yield return true;

		// Economy associations: the direct exact mapping, never re-derived by first Info.Id match.
		var instancesById = snapshot.Economy.Objects!.ToDictionary( item => item.Id );
		var boundAttractions = new HashSet<int>();
		var boundInstances = new HashSet<int>();
		for ( var index = 0; index < snapshot.EconomyBindings!.Count; index++ )
		{
			var binding = snapshot.EconomyBindings[index];
			Require( binding != null, $"economy binding {index} is missing" );
			Require( binding!.AttractionId > 0 && binding.InstanceId > 0,
				$"economy binding {index} maps attraction {binding.AttractionId} to instance {binding.InstanceId}" );
			Require( boundAttractions.Add( binding.AttractionId ), $"attraction {binding.AttractionId} is bound twice" );
			Require( boundInstances.Add( binding.InstanceId ), $"economy object {binding.InstanceId} is bound twice" );
			Require( objectsByAttraction.TryGetValue( binding.AttractionId, out var record ),
				$"economy binding {index} names attraction {binding.AttractionId}, which the park does not place" );
			Require( instancesById.TryGetValue( binding.InstanceId, out var instance ),
				$"economy binding {index} names economy object {binding.InstanceId}, which the economy does not hold" );
			Require( instance!.InfoId == record!.InfoId,
				$"economy binding {index} maps attraction {binding.AttractionId} (Info.Id {record.InfoId}) to instance {binding.InstanceId} (Info.Id {instance.InfoId})" );
		}
		// The prototype's economy association: the direct exact instance the live park linked (a Totem without one —
		// no reachable entrance, or an economy that does not list it — saves no instance), never re-derived.
		if ( snapshot.Prototype?.EconomyInstanceId is { } prototypeInstance )
		{
			Require( prototypeInstance > 0, $"the prototype's economy instance {prototypeInstance} is not positive" );
			Require( !boundInstances.Contains( prototypeInstance ),
				$"economy object {prototypeInstance} is bound to a placed attraction and the prototype" );
			Require( instancesById.TryGetValue( prototypeInstance, out var prototypeObject ),
				$"the prototype names economy object {prototypeInstance}, which the economy does not hold" );
			Require( prototypeObject!.InfoId == PrototypeRide.InfoId,
				$"the prototype's economy object {prototypeInstance} has Info.Id {prototypeObject.InfoId}, not the Inca Totem's {PrototypeRide.InfoId}" );
		}

		yield return true;
		ValidateGuests( snapshot );
		yield return true;
		ValidateThings( snapshot );
		yield return true;
		ValidateCoasters( snapshot );
		ValidateTracks( snapshot );
		ValidateTours( snapshot );
		yield return true;
		// [EXT:pools-slides] the slides part: shape only (the rules need the towers, which the level checks while it restores)
		if ( PoolBridgeWorld.ShapeProblem( snapshot.PoolBridges, snapshot.Mods ) is { } bridgeProblem )
			throw new InvalidDataException( bridgeProblem );
		if ( snapshot.Layout.Cells.QueuePassResidues is { } residues && snapshot.PoolBridges is { } bridges )
			Require( !residues.Any( residue => bridges.Bridges.Any( bridge => bridge.Contains( residue.X, residue.Y ) ) ),
				"queue pass residues overlap an unsupported pool bridge layer" );
		if ( PoolSlideSaves.ShapeProblem( snapshot.PoolSlides, snapshot.Mods ) is { } slideProblem )
			Require( false, slideProblem );

		// Clock: the clock's own pure validator, on a fresh instance it never mutates.
		new FixedStepClock().ValidateSnapshot( snapshot.Clock! );

		// The world generator the economy carries must be the one the guests and the script world carry (a snapshot from before the generator was ported carries neither marker), and the
		// world streams, when the economy carried them, must agree with the typed fields.
		if ( snapshot.Economy.WorldGenerator is { } generator )
		{
			Require( snapshot.Guests!.WorldGenerator is not { } guestGenerator || guestGenerator == generator,
				$"the economy's world generator {generator} is not the guest snapshot's {snapshot.Guests.WorldGenerator}" );
			Require( snapshot.Scripts.WorldGenerator is not { } scriptGenerator || scriptGenerator == generator,
				$"the economy's world generator {generator} is not the script world's {snapshot.Scripts.WorldGenerator}" );
		}
		if ( snapshot.Economy.World is { } world )
		{
			Require( world.Seed == snapshot.Seed, $"the economy's world seed {world.Seed} is not the envelope's {snapshot.Seed}" );
			Require( world.SoundSeed == snapshot.SoundSeed,
				$"the economy's sound seed {world.SoundSeed} is not the envelope's {snapshot.SoundSeed}" );
		}
		yield return true;

		// Optional parts: present means structurally valid; the destination park re-validates against its settings.
		if ( snapshot.Particles != null )
		{
			var particleReason = ParticleSystem.ValidateState( snapshot.Particles );
			Require( particleReason == null, particleReason ?? "" );
		}
		if ( snapshot.Weather is { } weather )
		{
			Require( weather.PendingStrikes != null, "the weather snapshot has no pending-strike list" );
			Require( weather.PendingStrikes!.Count <= ParkWeather.ParkWeatherSnapshot.MaximumPendingStrikes,
				$"the weather snapshot holds {weather.PendingStrikes.Count} pending thunder strikes, above the bound" );
			Require( float.IsFinite( weather.LastTop.X ) && float.IsFinite( weather.LastTop.Y ) && float.IsFinite( weather.LastTop.Z ),
				"the weather snapshot's last bolt top is not finite" );
			Require( weather.PendingStrikes.All( strike => float.IsFinite( strike.X ) && float.IsFinite( strike.Y ) && float.IsFinite( strike.Z ) ),
				"the weather snapshot holds a bolt top that is not finite" );
			if ( !weather.Started )
				Require( weather.Quality == 0 && !weather.Snow && !weather.Rain && !weather.Lightning
					&& weather.CurrentDrops == 0 && weather.TargetDrops == 0 && weather.LightningCountdown == 0 && weather.Bolts == 0
					&& weather.ThunderCountdown == 0 && weather.LastTurn == 0 && weather.LastChanged == 0 && weather.NextRollDay == 0
					&& weather.ApplyDay == 0 && weather.PendingQuality is null && weather.PendingStrikes.Count == 0,
					"the weather snapshot never started but is not pristine" );
			else
			{
				Require( weather.Quality is >= 1 and <= 100, $"the weather quality {weather.Quality} is outside 1..100" );
				Require( !weather.Snow || !weather.Rain, "the weather is both snowy and rainy" );
				Require( weather.CurrentDrops >= 0 && weather.TargetDrops >= 0, "the weather holds negative drop counts" );
				Require( weather.LastTurn >= 0 && weather.LastChanged >= 0 && weather.NextRollDay >= 0 && weather.ApplyDay >= 0,
					"the weather schedule is negative" );
				Require( weather.NextRollDay >= weather.LastChanged, "the weather's next roll precedes its last change" );
				Require( weather.PendingQuality is null or >= 1 and <= 100, "the weather's pending quality is outside 1..100" );
				Require( weather.PendingQuality is null || weather.ApplyDay >= weather.LastChanged,
					"the weather's forecast applies before its last change" );
				Require( weather.LightningCountdown >= 0 && weather.Bolts >= 0 && weather.ThunderCountdown >= 0,
					"the weather holds a negative lightning clock" );
			}
		}
		if ( snapshot.SnowCover is { } snowCover )
			Require( SnowCover.IsValidSavedCover( snowCover ), $"the snow cover {snowCover} is not a number above 0 and at most 1" );
		if ( snapshot.Gate is { } gate )
			Require( gate.LastObservedTurn >= 0, $"the park gate's last observed turn {gate.LastObservedTurn} is negative" );
		if ( snapshot.ParkName is { } parkName )
			Require( ParkNameLines.IsValidLine( parkName.Line1 ) && ParkNameLines.IsValidLine( parkName.Line2 ),
				$"the park name lines are missing, longer than {ParkNameLines.MaximumLength} characters or hold control characters" );
		if ( snapshot.Staff is { } staff )
		{
			Require( staff.Actors != null, "the staff-world snapshot has no actor list" );
			var actorIds = new HashSet<int>();
			var membersById = snapshot.Economy.Staff!.Members!.ToDictionary( member => member.Id );
			foreach ( var actor in staff.Actors! )
			{
				Require( actor != null, "the staff-world snapshot holds a missing actor" );
				Require( actor!.Id > 0, $"the staff world holds actor {actor.Id}" );
				Require( actorIds.Add( actor.Id ), $"the staff world holds actor {actor.Id} twice" );
				Require( actor.CellX.HasValue == actor.CellY.HasValue, $"staff actor {actor.Id} has one cell coordinate but not the other" );
				Require( !actor.CellX.HasValue || ( float.IsFinite( actor.CellX.Value ) && float.IsFinite( actor.CellY!.Value ) ),
					$"staff actor {actor.Id} stands at a position that is not finite" );
				Require( double.IsFinite( actor.TravelSeconds ) && actor.TravelSeconds >= 0 && actor.TravelSeconds < StaffWorldSimulation.SecondsPerCell,
					$"staff actor {actor.Id} has travelled {actor.TravelSeconds}s, outside one cell step" );
				Require( membersById.TryGetValue( actor.Id, out var member ),
					$"the staff world holds actor {actor.Id}, whom the economy does not employ" );
				Require( actor.CellX == member!.CellX && actor.CellY == member.CellY && actor.TravelSeconds == member.TravelSeconds,
					$"staff member {actor.Id} is at ({actor.CellX}, {actor.CellY}, {actor.TravelSeconds}s) in the world, but ({member.CellX}, {member.CellY}, {member.TravelSeconds}s) in the economy" );
			}
			Require( actorIds.SetEquals( membersById.Keys ),
				$"the staff world holds {actorIds.Count} actors, but the economy employs {membersById.Count}" );
		}
		// A staff walk of the original kind keeps the stamp counter of its set-up, as a guest's does (WALK-007).
		if ( snapshot.Layout?.Cells is { } layoutCells )
			foreach ( var member in snapshot.Economy.Staff!.Members! )
				Require( member.WorldState?.Navigation?.OriginalWalk is not { } staffWalk || staffWalk.Stamp <= layoutCells.StampCounter,
					$"staff {member.Id} walk stamp {member.WorldState?.Navigation?.OriginalWalk?.Stamp} is above the layout's stamp counter {layoutCells.StampCounter}" );
		if ( snapshot.Advisor is { } advisor )
		{
			Require( advisor.Controller != null, "the advisor snapshot has no controller state" );
			Require( !advisor.SpeechWasActive, "the advisor was speaking when the park was saved; active speech cannot be restored" );
		}
		yield return true;

		// Asset manifest: non-empty, bounded, and every digest a real SHA-256 hex string.
		Require( snapshot.AssetHashes!.Count > 0, "the asset manifest is empty" );
		Require( snapshot.AssetHashes.Count <= ParkResumeAssets.MaximumManifestEntries,
			$"the asset manifest holds {snapshot.AssetHashes.Count} entries, above {ParkResumeAssets.MaximumManifestEntries}" );
		foreach ( var (key, value) in snapshot.AssetHashes )
		{
			Require( !string.IsNullOrEmpty( key ), "the asset manifest has an empty key" );
			Require( key!.Length <= ParkResumeAssets.MaximumKeyLength, $"the asset key '{key}' exceeds {ParkResumeAssets.MaximumKeyLength} characters" );
			Require( value != null && ParkResumeAssets.IsValidSha256( value! ),
				$"asset '{key}' has digest '{value}', not 64 lowercase hex characters" );
		}
	}

	/// <summary>
	/// The guests' structural cross-checks, pure and without a live simulation (which only exists once root builds
	/// the candidate): identity and order, per-attraction queue invariants (the bridge validator), queue-cell and
	/// guest-position bounds against the layout's cell map (the guests' walk grid derives from it), the queue and
	/// rider membership both ways, and every attraction reference resolving to a placed object. The destination's
	/// own <see cref="GuestSimulation.ValidateSnapshot"/> re-checks everything against the restored park.
	/// </summary>
	/// <summary>
	/// The thing pool and the things that hold its slots: with a pool every placed object, guest and staff member names a slot of its own model, nothing else holds a slot of those models, and the
	/// pool itself is well formed; without one (a save from before it) no object or member names a slot.
	/// </summary>
	private static void ValidateThings( ParkResumeSnapshot snapshot )
	{
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"Park save is inconsistent: {message}." );
		}
		var objects = snapshot.Layout.Objects!;
		var members = snapshot.Economy.Staff.Members;
		if ( snapshot.Things == null )
		{
			Require( objects.All( record => record!.ThingId == 0 ), "an object names a thing id but the save holds no thing pool" );
			Require( members.All( member => member.ThingId == 0 ), "a staff member names a thing id but the save holds no thing pool" );
			return;
		}
		var problems = ThingPool.Problems( snapshot.Things );
		Require( problems.Count == 0, $"the thing pool: {(problems.Count == 0 ? "" : problems[0])}" );
		var models = snapshot.Things.Used.ToDictionary( slot => slot.Id, slot => (SaveThingModel)slot.Model );
		// a thing demolished or dismissed since the turn's last end still holds its slot, and nothing names it
		var pending = (snapshot.Things.Pending ?? Array.Empty<int>()).ToHashSet();
		var named = new HashSet<int>();
		void Names( int id, SaveThingModel model, string what )
		{
			Require( models.TryGetValue( id, out var held ) && held == model, $"{what} names thing id {id}, which is not a {model} slot of the thing pool" );
			Require( named.Add( id ), $"two things name thing id {id}" );
		}
		foreach ( var record in objects )
			Names( record!.ThingId, SaveThingModel.Attraction, $"the object at ({record.X}, {record.Y})" );
		foreach ( var member in members )
			Names( member.ThingId, ParkStaff.ThingModelOf( member.Type ), $"staff member {member.Id}" );
		if ( snapshot.Guests!.SchemaVersion >= GuestSimulationSnapshot.ThingPoolSchemaVersion )
			foreach ( var guest in snapshot.Guests.Guests! )
				Names( guest!.Id, SaveThingModel.Guest, $"guest {guest.Id}" );
		Require( !named.Overlaps( pending ), "a thing waiting for its destruction is named by a thing" );
		// a placeholder slot (a thing of an imported original save that OpenTPW does not rebuild) is named by no thing either
		var placeholders = (snapshot.Things.Held ?? Array.Empty<int>()).ToHashSet();
		Require( !named.Overlaps( placeholders ), "a placeholder slot of the thing pool is named by a thing" );
		var held = snapshot.Things.Used.Count( slot => slot.Model is 1 or 3 or (>= 4 and <= 8) && !pending.Contains( slot.Id ) && !placeholders.Contains( slot.Id ) );
		Require( held == named.Count || snapshot.Guests.SchemaVersion < GuestSimulationSnapshot.ThingPoolSchemaVersion, "the thing pool holds a guest, attraction or staff slot no thing names" );
	}

	private static void ValidateGuests( ParkResumeSnapshot snapshot )
	{
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"World save is inconsistent: {message}." );
		}
		var guests = snapshot.Guests!;
		foreach ( var problem in GuestSimulation.DisplayAgeProblems( guests ) )
			Require( false, problem );
		var width = snapshot.Layout!.Cells!.Width;
		var height = snapshot.Layout.Cells.Height;
		bool InBounds( int x, int y ) => (uint)x < (uint)width && (uint)y < (uint)height;
		Require( GuestSimulationSnapshot.AcceptedSchemaVersions.Contains( guests.SchemaVersion ),
			$"the guest snapshot's schema version {guests.SchemaVersion} is not one of {string.Join( ", ", GuestSimulationSnapshot.AcceptedSchemaVersions )}" );
		Require( guests.Guests != null, "the guest snapshot has no guest list" );
		Require( guests.Attractions != null, "the guest snapshot has no attraction list" );
		Require( guests.SeenQueueEdits != null, "the guest snapshot has no seen-queue-edit list" );
		Require( guests.TickCount >= 0, $"the guest clock's tick count {guests.TickCount} is negative" );
		Require( double.IsFinite( guests.TimeSeconds ) && guests.TimeSeconds >= 0,
			$"the guest clock's {guests.TimeSeconds}s is negative or not finite" );
		Require( guests.NextGuestId >= 1, $"the next guest id {guests.NextGuestId} is not positive" );
		Require( guests.Admissions >= 0 && guests.Departed >= 0, "the guest snapshot holds negative admission counters" );
		Require( float.IsFinite( guests.SpawnTimer ), "the arrival spawn timer is not finite" );
		Require( guests.LaneToggle >= 0, $"the arrival lane toggle {guests.LaneToggle} is negative" );

		// Attractions: queue invariants, unique ids the park really places, and cells inside the cell map.
		var attractionIds = new HashSet<int>();
		for ( var index = 0; index < guests.Attractions!.Length; index++ )
		{
			var attraction = guests.Attractions[index];
			Require( attraction != null, $"attraction snapshot {index} is missing" );
			Require( attractionIds.Add( attraction!.AttractionId ), $"attraction {attraction.AttractionId} has two queue snapshots" );
			Require( attraction.Queue != null && attraction.OnRide != null && attraction.WalkingOff != null && attraction.Hopped != null
				&& attraction.Limbo != null && attraction.Bounce != null && attraction.QueueCells != null,
				$"attraction {attraction.AttractionId}'s queue snapshot is missing a list" );
			var problems = RideVisitorBridge.ValidateSnapshot( attraction );
			Require( problems.Count == 0,
				$"attraction {attraction.AttractionId}'s queue is inconsistent{( problems.Count == 0 ? "" : $": {problems[0]}" )}" );
			Require( snapshot.Layout.Objects!.Any( record => record != null && record.AttractionId == attraction.AttractionId )
				|| ( snapshot.Prototype != null && snapshot.Prototype.AttractionId == attraction.AttractionId ),
				$"attraction {attraction.AttractionId} is registered with the guests, but the park does not place it" );
			foreach ( var (x, y) in attraction.QueueCells! )
				Require( InBounds( x, y ), $"attraction {attraction.AttractionId}'s queue cell ({x}, {y}) is outside the cell map" );
			if ( attraction.JoinCell is { } join )
				Require( InBounds( join.X, join.Y ), $"attraction {attraction.AttractionId}'s join cell ({join.X}, {join.Y}) is outside the cell map" );
		}
		// The prototype's guest record and its part's guest cells join both ways: the live park registered the Totem's
		// bridge exactly when it derived its cells (one placement step), and the capture wrote both sides of that.
		// A record without cells would die on the restore's missing bridge; cells without a record would register a
		// queue the saved guests never knew.
		if ( snapshot.Prototype is { } prototype )
		{
			var record = guests.Attractions!.Any( item => item!.AttractionId == prototype.AttractionId );
			Require( record == (prototype.EntranceCell != null),
				record
					? $"the guests hold attraction {prototype.AttractionId}'s record, but the prototype part carries no guest cells"
					: $"the prototype part carries guest cells, but the guests hold no attraction {prototype.AttractionId} record" );
		}
		var seenEdits = new HashSet<int>();
		for ( var index = 0; index < guests.SeenQueueEdits!.Length; index++ )
		{
			var (attractionId, seen) = guests.SeenQueueEdits[index];
			Require( seenEdits.Add( attractionId ), $"SeenQueueEdits[{index}] repeats attraction {attractionId}" );
			Require( attractionIds.Contains( attractionId ), $"SeenQueueEdits[{index}] names attraction {attractionId}, which has no snapshot" );
			Require( seen >= -1, $"attraction {attractionId}'s seen queue-edit count is {seen}" );
		}
		foreach ( var attractionId in attractionIds )
			Require( seenEdits.Contains( attractionId ), $"attraction {attractionId} has no seen queue-edit counter" );

		// Guests: identity and order, defined states, positions inside the cell map.
		var guestIds = new HashSet<int>();
		var previousId = 0;
		for ( var index = 0; index < guests.Guests!.Length; index++ )
		{
			var guest = guests.Guests[index];
			Require( guest != null, $"guest snapshot {index} is missing" );
			// before the thing pool a guest's id is a counter (ascending, below the next id); with it the id is a slot of the pool, so ids are reused and the list is in build order
			if ( guests.SchemaVersion >= GuestSimulationSnapshot.ThingPoolSchemaVersion )
				Require( guest!.Id >= 1 && guest.Id <= ThingPool.Capacity, $"guest {guest.Id} is not a slot of the thing pool (1..{ThingPool.Capacity})" );
			else
			{
				Require( guest!.Id >= 1 && guest.Id < guests.NextGuestId,
					$"guest {guest.Id} is not below the next guest id {guests.NextGuestId}" );
				Require( guest.Id > previousId, $"guest {guest.Id} does not follow guest {previousId}; guests are saved in ascending id order" );
			}
			previousId = guest.Id;
			Require( guestIds.Add( guest.Id ), $"guest {guest.Id} appears more than once" );
			Require( Enum.IsDefined( guest.State ), $"guest {guest.Id} state {(int)guest.State} is not a guest state" );
			Require( guest.State != GuestState.Gone, $"guest {guest.Id} is already gone; gone guests are removed at the end of their tick" );
			Require( Enum.IsDefined( guest.Thought ), $"guest {guest.Id} thought {(int)guest.Thought} is not a guest thought" );
			Require( guest.Type >= 0, $"guest {guest.Id} type {guest.Type} is negative" );
			Require( double.IsFinite( guest.ExitLevel ) && Math.Truncate( guest.ExitLevel ) is >= int.MinValue and <= int.MaxValue,
				$"guest {guest.Id} exit counter {guest.ExitLevel} does not fit the truncated counter" );
			Require( guests.SchemaVersion == 1 || Math.Truncate( guest.ExitLevel ) == guest.ExitLevel,
				$"guest {guest.Id} exit counter {guest.ExitLevel} is not a whole number of decisions" );
			// Schema 2 writes the histories; schema 1 has none (they start empty). When present they must be whole and non-negative.
			foreach ( var (name, history) in new[] { (nameof( guest.UsedHistory ), guest.UsedHistory), (nameof( guest.TemporaryHistory ), guest.TemporaryHistory) } )
			{
				if ( history == null )
					Require( guests.SchemaVersion == 1, $"guest {guest.Id} has no {name}" );
				else
					Require( history.Length == Guest.HistorySlots && !history.Any( id => id < 0 ),
						$"guest {guest.Id} {name} is not {Guest.HistorySlots} non-negative attraction ids" );
			}
			Require( guest.TimeInParkSeconds is not { } elapsed || (double.IsFinite( elapsed ) && elapsed >= 0 && elapsed <= Guest.MaximumTimeInParkSeconds),
				$"guest {guest.Id} time in park is negative, not finite or exceeds the supported duration" );
			Require( guest.Money >= 0 && guest.RidesTaken >= 0 && guest.ShopsVisited >= 0 && guest.SideshowsVisited >= 0 && guest.SideshowsWon >= 0, $"guest {guest.Id} holds negative money or counts" );
			Require( float.IsFinite( guest.Hunger ) && float.IsFinite( guest.Thirst ) && float.IsFinite( guest.Toilet )
				&& float.IsFinite( guest.Energy ) && float.IsFinite( guest.Happiness ) && float.IsFinite( guest.Nausea )
				&& float.IsFinite( guest.QueueJoinHappiness ) && float.IsFinite( guest.SecondsSinceRide )
				&& float.IsFinite( guest.StateTimer ) && (guest.DecisionTimer is not { } decisionTimer || float.IsFinite( decisionTimer )) && float.IsFinite( guest.SmoothedSpeed ),
				$"guest {guest.Id} holds a need or timer that is not finite" );
			Require( float.IsFinite( guest.HeadingX ) && float.IsFinite( guest.HeadingY ) && float.IsFinite( guest.DistanceWalked )
				&& float.IsFinite( guest.DrawX ) && float.IsFinite( guest.DrawY ) && float.IsFinite( guest.DrawDistanceWalked )
				&& float.IsFinite( guest.StepFromX ) && float.IsFinite( guest.StepFromY ) && float.IsFinite( guest.StepFromDistance )
				&& float.IsFinite( guest.OffsetX ) && float.IsFinite( guest.OffsetY ) && float.IsFinite( guest.QueueTargetX )
				&& float.IsFinite( guest.QueueTargetY ),
				$"guest {guest.Id} holds a position or draw value that is not finite" );
			Require( guest.BaseSpeed is >= 60 and <= 140, $"guest {guest.Id} base speed {guest.BaseSpeed} is not one of the walk speeds" );
			Require( guest.Navigation != null && guest.Navigation.Route != null && guest.Navigation.AlsoOpen != null,
				$"guest {guest.Id} has no navigation state" );
			var navigation = guest.Navigation!;
			Require( Math.Abs( (long)navigation.VelocityX ) <= GuestSpeed.VelocityBound && Math.Abs( (long)navigation.VelocityY ) <= GuestSpeed.VelocityBound,
				$"guest {guest.Id} navigation velocity ({navigation.VelocityX}, {navigation.VelocityY}) exceeds the speed cap" );
			foreach ( var problem in navigation.Problems( guest.Id, InBounds ) )
				Require( false, problem );
			// A walk keeps the stamp counter of its set-up; the layout's counter can only have grown since (WALK-007).
			Require( navigation.OriginalWalk is not { } walk || walk.Stamp <= snapshot.Layout.Cells.StampCounter,
				$"guest {guest.Id} walk stamp {navigation.OriginalWalk?.Stamp} is above the layout's stamp counter {snapshot.Layout.Cells.StampCounter}" );
			Require( InBounds( guest.CellX, guest.CellY ), $"guest {guest.Id} cell ({guest.CellX}, {guest.CellY}) is outside the cell map" );
			if ( guest.PreviousCellX >= 0 || guest.PreviousCellY >= 0 )
				Require( InBounds( guest.PreviousCellX, guest.PreviousCellY ),
					$"guest {guest.Id} previous cell ({guest.PreviousCellX}, {guest.PreviousCellY}) is outside the cell map" );
			Require( InBounds( navigation.X >> 16, navigation.Y >> 16 ),
				$"guest {guest.Id} navigation position ({navigation.X >> 16}, {navigation.Y >> 16}) is outside the cell map" );
			Require( InBounds( navigation.TargetX >> 16, navigation.TargetY >> 16 ),
				$"guest {guest.Id} navigation target ({navigation.TargetX >> 16}, {navigation.TargetY >> 16}) is outside the cell map" );
			// a guest saved before guest schema 9 may walk an arrival lane off the grid (the restore turns it into one on the cell map); from schema 9 on no guest carries a lane
			if ( guest.Lane != null )
			{
				var laneState = guest.State is GuestState.CrossingRoad or GuestState.CrossingRoadToPark or GuestState.GoingToTicketBooth or GuestState.AtTicketBooth or GuestState.EnteringPark
					or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome or GuestState.LeavingPark;
				Require( guests.SchemaVersion < GuestSimulationSnapshot.ArrivalWalkSchemaVersion, $"guest {guest.Id} carries an arrival lane, which a schema {guests.SchemaVersion} save does not hold" );
				Require( laneState, $"guest {guest.Id} is in state {guest.State} but still carries an arrival lane" );
				Require( guest.Lane.Length >= 2, $"guest {guest.Id} is in state {guest.State} with an arrival lane of fewer than two cells" );
				Require( (uint)guest.LaneIndex < (uint)guest.Lane.Length, $"guest {guest.Id} lane index {guest.LaneIndex} is outside its {guest.Lane.Length}-cell lane" );
				Require( !guest.OnGrid && navigation.OffGrid, $"guest {guest.Id} walks a lane but is marked on the path grid" );
				foreach ( var (x, y) in guest.Lane )
					Require( InBounds( x, y ), $"guest {guest.Id} lane cell ({x}, {y}) is outside the cell map" );
			}
			else
				Require( guest.OnGrid && !navigation.OffGrid, $"guest {guest.Id} is in state {guest.State} but is not on the path grid" );
		}

		// Membership both ways: queue and rider lists against guest states, exactly as the live validator does.
		var queuedIn = new Dictionary<int, int>();
		var aboardOf = new Dictionary<int, int>();
		foreach ( var attraction in guests.Attractions! )
		{
			foreach ( var guestId in attraction.Queue )
				Require( queuedIn.TryAdd( guestId, attraction.AttractionId ),
					$"guest {guestId} is queued at attractions {queuedIn.GetValueOrDefault( guestId )} and {attraction.AttractionId}" );
			foreach ( var guestId in attraction.OnRide )
				Require( aboardOf.TryAdd( guestId, attraction.AttractionId ),
					$"guest {guestId} is aboard attractions {aboardOf.GetValueOrDefault( guestId )} and {attraction.AttractionId}" );
		}
		foreach ( var guest in guests.Guests! )
		{
			var inQueue = guest.State is GuestState.MovingUpQueue or GuestState.Queueing or GuestState.Boarding or GuestState.WaitingToBoard;
			if ( inQueue )
			{
				Require( queuedIn.TryGetValue( guest.Id, out var queueAttraction ),
					$"guest {guest.Id} is in state {guest.State} but no attraction's queue holds it" );
				Require( queueAttraction == guest.AttractionId,
					$"guest {guest.Id} queues for attraction {guest.AttractionId} but attraction {queueAttraction}'s queue holds it" );
			}
			else
				Require( !queuedIn.TryGetValue( guest.Id, out _ ),
					$"guest {guest.Id} is in state {guest.State} but is still held in a queue" );
			if ( guest.State == GuestState.Using )
			{
				Require( aboardOf.TryGetValue( guest.Id, out var rideAttraction ),
					$"guest {guest.Id} rides but no attraction holds it aboard" );
				Require( rideAttraction == guest.AttractionId,
					$"guest {guest.Id} rides attraction {guest.AttractionId} but is held aboard attraction {rideAttraction}" );
			}
			else
				Require( !aboardOf.TryGetValue( guest.Id, out _ ),
					$"guest {guest.Id} is in state {guest.State} but is still held aboard an attraction" );
			if ( guest.AttractionId != 0 )
				Require( attractionIds.Contains( guest.AttractionId ),
					$"guest {guest.Id} names attraction {guest.AttractionId}, which has no queue snapshot" );
			else
				Require( guest.State != GuestState.GoingToRide, $"guest {guest.Id} heads for a ride without naming an attraction" );
		}
		foreach ( var (guestId, attractionId) in queuedIn )
			Require( guestIds.Contains( guestId ), $"attraction {attractionId}'s queue holds guest {guestId}, which does not exist" );
		foreach ( var (guestId, attractionId) in aboardOf )
			Require( guestIds.Contains( guestId ), $"attraction {attractionId}'s riders hold guest {guestId}, which does not exist" );
	}

	/// <summary>
	/// The track part's pure consistency (null means the park held no track tile and no controller): every cell in the map with a known track type and a
	/// parent that is a tile origin, every controller joined to one placed object by a strictly increasing attraction id, the bounded scalars, sections,
	/// boats (unique slots, finite integers) and guest lists. The catalog checks (which rides are water rides, the vehicle limit) are
	/// <see cref="ValidateSnapshot"/>'s, and the restore re-walks every circuit against the saved sections.
	/// </summary>
	private static void ValidateTracks( ParkResumeSnapshot snapshot )
	{
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"World save is inconsistent: {message}." );
		}
		if ( snapshot.Tracks is not { } tracks )
			return;
		Require( tracks.Cells != null && tracks.Controllers != null, "the track part holds no cell or controller list" );
		Require( tracks.Cells!.Count <= TrackSave.MaximumCells, $"the track part holds {tracks.Cells.Count} cells, above {TrackSave.MaximumCells}" );
		Require( tracks.Controllers!.Count <= TrackSave.MaximumControllers, $"the track part holds {tracks.Controllers.Count} controllers, above {TrackSave.MaximumControllers}" );
		var width = snapshot.Layout!.Cells!.Width;
		var height = snapshot.Layout.Cells.Height;
		var seen = new HashSet<(int X, int Y)>();
		var mapTypes = snapshot.Layout.Cells.Cells!.ToDictionary( item => (item.X, item.Y), item => item.Type );
		foreach ( var cell in tracks.Cells )
		{
			Require( cell != null, "a track cell is missing" );
			Require( (uint)cell.X < (uint)width && (uint)cell.Y < (uint)height, $"track cell ({cell.X}, {cell.Y}) is outside the cell map" );
			Require( seen.Add( (cell.X, cell.Y) ), $"track cell ({cell.X}, {cell.Y}) is saved twice" );
			Require( TrackCellTypes.IsWalkable( cell.Type ) || cell.Type == TrackCellTypes.Extension, $"track cell ({cell.X}, {cell.Y}) has type {cell.Type}, which is no track type" );
			Require( (cell.Mask & ~0x55) == 0, $"track cell ({cell.X}, {cell.Y}) has connection bits 0x{cell.Mask:X}, outside 0x55" );
			Require( cell.Type != TrackCellTypes.Extension || (uint)cell.Parent < (uint)(width * height), $"track extension cell ({cell.X}, {cell.Y}) names parent {cell.Parent}, outside the cell map" );
			Require( mapTypes.TryGetValue( (cell.X, cell.Y), out var mapType ) && mapType == cell.Type, $"track cell ({cell.X}, {cell.Y}) of type {cell.Type} is not that type in the saved cell map" );
			Require( (cell.Link & ~0x55) == 0 && (cell.Family is 0 or (int)TrackFamily.Kart or (int)TrackFamily.Water) && cell.Piece is >= 0 and < TrackPieceSelector.WaterPieceCount
				&& cell.Rotation is 0 or 90 or 180 or 270 && cell.Overlap >= 0 && cell.Segment >= 0 && (uint)cell.Owner <= (uint)(width * height),
				$"track cell ({cell.X}, {cell.Y}) holds a link, piece, rotation, overlap, segment or owner outside the layer's ranges" );
		}
		var trackTypes = tracks.Cells.ToDictionary( cell => cell.Y * width + cell.X, cell => cell.Type );
		foreach ( var cell in tracks.Cells )
			Require( cell.BaseType == 0 || TrackCellTypes.IsStation( cell.Type )
				|| cell.Type == TrackCellTypes.Extension && trackTypes.TryGetValue( cell.Parent, out var parentType ) && TrackCellTypes.IsStation( parentType ),
				$"track cell ({cell.X}, {cell.Y}) preserves a base type without a station origin" );
		var placedAttractions = snapshot.Layout.Objects!.Select( record => record!.AttractionId ).ToHashSet();
		var guestIds = snapshot.Guests!.Guests!.Select( guest => guest!.Id ).ToHashSet();
		var listedGuests = new HashSet<int>();
		// positions and targets lie in the park's footprint in section units, plus one tile of margin for a boat pressed against the edge
		var limitX = (long)width * TrackStation.CellUnits + 0x1800;
		var limitY = (long)height * TrackStation.CellUnits + 0x1800;
		bool Inside( int x, int y ) => x >= -0x1800L && x <= limitX && y >= -0x1800L && y <= limitY;
		bool Small( int value, int limit ) => value >= -limit && value <= limit;
		Require( tracks.FrameCount is null or >= 0 && (tracks.TickRemainder is not { } clock || double.IsFinite( clock ) && clock >= 0 && clock < BumpController.MaximumSavedRemainder ),
			"the track module's frame counter or clock is out of range" );
		var previous = 0;
		// a save of the shared module (it carries the module clock) is restored as saved: every vehicle keeps its module pool slot and every object its pool slot, unique across the rides
		var shared = tracks.FrameCount != null;
		var moduleVehicleSlots = new HashSet<int>();
		var moduleObjectSlots = new HashSet<int>();
		foreach ( var controller in tracks.Controllers )
		{
			Require( controller != null, "a track controller is missing" );
			Require( controller.AttractionId > previous, $"track controller {controller.AttractionId} does not follow {previous} in attraction order" );
			Require( placedAttractions.Contains( controller.AttractionId ), $"the track part names attraction {controller.AttractionId}, which the park does not place" );
			previous = controller.AttractionId;
			Require( controller.Sections.Count <= TrackSave.MaximumSections, $"track controller {controller.AttractionId} holds {controller.Sections.Count} sections" );
			Require( controller.Phase is >= 0 and <= 3 && controller.State is >= 0 and <= 2, $"track controller {controller.AttractionId} has phase {controller.Phase} or state {controller.State} out of range" );
			Require( controller.Performance is >= 0 and <= 100, $"track controller {controller.AttractionId} has performance {controller.Performance} outside 0..100" );
			Require( controller.TickCount >= 0 && double.IsFinite( controller.TickRemainder ) && controller.TickRemainder >= 0 && controller.TickRemainder < BumpController.MaximumSavedRemainder, $"track controller {controller.AttractionId} has a bad tick clock" );
			Require( controller.Pending.Count + controller.Leaving.Count <= TrackSave.MaximumGuests && controller.Pending.Concat( controller.Leaving ).All( guest => guestIds.Contains( guest ) && listedGuests.Add( guest ) ),
				$"track controller {controller.AttractionId} holds a guest list with a guest the park does not hold, or one listed twice" );
			Require( controller.Laps is >= -1000 and <= 0x10000, $"track controller {controller.AttractionId} has laps {controller.Laps} out of range" );
			Require( controller.ObjectSlots == null || controller.ObjectSlots.Count <= BumpWorld.ObjectPool && controller.ObjectSlots.All( slot => slot is >= 0 and < BumpWorld.ObjectPool ),
				$"track controller {controller.AttractionId} places collision objects outside the module's pool" );
			Require( !shared || controller.ObjectSlots != null && controller.ObjectSlots.All( moduleObjectSlots.Add ),
				$"track controller {controller.AttractionId} names no collision-object slots, or a slot another object of the module holds" );
			Require( controller.BuoyCount is null or >= 0 and <= BumpWorld.BuoyPool && (!shared || controller.BuoyCount != null),
				$"track controller {controller.AttractionId} holds no or a bad buoy count" );
			Require( controller.StationLayout is 0 or 1, $"track controller {controller.AttractionId} has the unknown station layout {controller.StationLayout}" );
			var slots = new HashSet<int>();
			// the engine voice belongs to one bumper car at most: the first carrying car of a running ride (never to a kart; every boat holds one)
			var engines = controller.Vehicles.Where( car => car.Engine ).ToList();
			Require( engines.Count == 0 || BumperTemplates.TryGet( controller.Type ) is { IsWater: true } || BumperTemplates.TryGet( controller.Type ) is { IsBumper: true } && engines.Count == 1 && controller.Phase == BumpController.PhaseRunning
				&& (engines[0].Flags & (BumpVehicle.First | BumpVehicle.Steerable)) == (BumpVehicle.First | BumpVehicle.Steerable),
				$"track controller {controller.AttractionId} holds an engine voice that no running bumper ride's first carrying car owns" );
			foreach ( var vehicle in controller.Vehicles )
			{
				Require( vehicle != null && vehicle.Slot is >= 0 and < BumpController.VehiclePool && slots.Add( vehicle.Slot ), $"track controller {controller.AttractionId} holds a boat with a bad or repeated slot" );
				Require( !shared || moduleVehicleSlots.Add( vehicle.Slot ), $"a vehicle of track controller {controller.AttractionId} names module pool slot {vehicle.Slot}, which another ride's vehicle holds" );
				Require( vehicle.Heading is >= 0 and < BumpMath.Turn && vehicle.Riders.Count <= TrackSave.MaximumGuests && vehicle.Riders.All( guest => guestIds.Contains( guest ) && listedGuests.Add( guest ) ),
					$"a boat of track controller {controller.AttractionId} has a bad heading or a rider the park does not hold, or one listed twice" );
				// a bumper car always sits in the arena's one object (the creation search finds it from the station, and nothing but the keep-in step changes it), or in another ride's object the module-wide search found first;
				// an arena the shared pools could not hold leaves its cars in no object
				Require( BumperTemplates.TryGet( controller.Type ) is not { IsBumper: true } || vehicle.ObjectIndex == 0 || vehicle.ObjectAttraction >= 0
					|| vehicle.ObjectIndex == -1 && controller.ObjectSlots is { Count: 0 }, $"a bumper car of track controller {controller.AttractionId} is not in the arena object" );
				// an object of another ride names that ride (the park restore checks the object exists)
				Require( vehicle.ObjectAttraction == -1 || vehicle.ObjectAttraction != controller.AttractionId && placedAttractions.Contains( vehicle.ObjectAttraction ) && vehicle.ObjectIndex >= 0,
					$"a boat of track controller {controller.AttractionId} sits in an object of attraction {vehicle.ObjectAttraction}, which is not another placed ride" );
				Require( vehicle.ObjectIndex >= -1 && vehicle.TargetBuoy >= -1, $"a boat of track controller {controller.AttractionId} has a negative object or buoy index" );
				Require( !vehicle.Smoke || BumperTemplates.TryGet( controller.Type ) is { IsWater: false }, $"a boat of track controller {controller.AttractionId} holds a smoke emitter, which only karts and bumper cars have" );
				Require( (vehicle.Flags & BumpVehicle.Live) != 0, $"a boat of track controller {controller.AttractionId} is not live" );
				Require( Inside( vehicle.X, vehicle.Y ) && Inside( vehicle.TargetX, vehicle.TargetY ), $"a boat of track controller {controller.AttractionId} lies outside the park" );
				Require( Small( vehicle.VX, 0x10000 ) && Small( vehicle.VY, 0x10000 ) && Small( vehicle.VX0, 0x10000 ) && Small( vehicle.VY0, 0x10000 ) && Small( vehicle.Speed, 0x100000 ),
					$"a boat of track controller {controller.AttractionId} has a velocity out of range" );
				Require( Small( vehicle.Spin, 0x100000 ) && Small( vehicle.Roll, 0x100000 ) && Small( vehicle.AccelBonus, 0x100000 ) && Small( vehicle.JitterX, 0x10000 ) && Small( vehicle.JitterY, 0x10000 )
					&& vehicle.Radius is >= 0 and <= 0x10000 && Small( vehicle.Laps, 0x10000 ) && vehicle.Variant is >= 0 and < 256 && vehicle.Age >= 0 && vehicle.WallHits >= -1 && vehicle.WallWindow >= 0 && vehicle.ChaseTarget is >= -1 and < BumpController.VehiclePool && vehicle.Sprays >= 0
					&& Small( vehicle.BuoyCountdown, 0x10000 ),
					$"a boat of track controller {controller.AttractionId} has a scalar out of range" );
				Require( vehicle.VisualHeading is >= 0 and < BumpMath.Turn && Small( vehicle.YawRate, 0x10000 ) && Small( vehicle.YawDelta, 0x10000 ) && vehicle.Animation is 0 or 5 or 12
					&& vehicle.EffectRequest is >= 0 and <= 5 && vehicle.EffectLatch is >= 0 and <= 5 && float.IsFinite( vehicle.Height ) && Math.Abs( vehicle.Height ) < 1e6f,
					$"a vehicle of track controller {controller.AttractionId} has a kart field out of range" );
			}
		}
	}

	/// <summary>
	/// The tour part's pure consistency (null means no ride held a tour controller): controllers joined to placed attractions by strictly increasing attraction id,
	/// each controller's words, cars and riders bounded and consistent, every rider a guest the park holds, listed once.
	/// </summary>
	private static void ValidateTours( ParkResumeSnapshot snapshot )
	{
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"World save is inconsistent: {message}." );
		}
		if ( snapshot.Tours is not { } tours )
			return;
		Require( tours.Controllers != null, "the tour part holds no controller list" );
		Require( tours.Controllers!.Count is > 0 and <= TourWorld.MaximumControllers, $"the tour part holds {tours.Controllers.Count} controllers" );
		var placedAttractions = snapshot.Layout!.Objects!.Select( record => record!.AttractionId ).ToHashSet();
		var guestIds = snapshot.Guests!.Guests!.Select( guest => guest!.Id ).ToHashSet();
		var listedGuests = new HashSet<int>();
		var previous = 0;
		foreach ( var controller in tours.Controllers )
		{
			Require( controller != null, "a tour controller is missing" );
			Require( controller.AttractionId > previous, $"tour controller {controller.AttractionId} does not follow {previous} in attraction order" );
			Require( placedAttractions.Contains( controller.AttractionId ), $"the tour part names attraction {controller.AttractionId}, which the park does not place" );
			previous = controller.AttractionId;
			TourSave.Validate( controller, guestIds.Contains, listedGuests );
			// [EXT:cheats-ride-vehicles] a raised tour ride's controller has the raised slots, every other one the original's
			var index = snapshot.Layout.Objects.ToList().FindIndex( record => record!.AttractionId == controller.AttractionId );
			Require( (controller.CarSlots != null) == (snapshot.Runtimes[index].State.VehiclesRaised == true), $"tour controller {controller.AttractionId}'s car slots do not match its ride's raised flag" );
			// the riders of a car are guests the ride's script holds: the guest snapshot lists each of them aboard that attraction
			var aboard = snapshot.Guests.Attractions.FirstOrDefault( bridge => bridge.AttractionId == controller.AttractionId )?.OnRide.ToHashSet() ?? new HashSet<int>();
			// (only the seats of the riders aboard: the words after them are stale)
			foreach ( var rider in controller.Cars.SelectMany( car => car.Riders.Take( Math.Abs( car.RiderCount ) ) ).Concat( controller.Ejected ) )
				Require( aboard.Contains( rider ), $"tour controller {controller.AttractionId} carries guest {rider}, whom the guest snapshot does not hold aboard that attraction" );
		}
	}

	/// <summary>
	/// Whether the saved controllers fit the live track rides: every water ride has one and none belongs to a ride the park does not place; a kart ride may lack one,
	/// because a save from before the kart controllers carries none (the restore migrates it, TRACK-027).
	/// </summary>
	internal static bool TrackControllersMatch( IReadOnlyDictionary<int, BumperTemplate> liveRides, IReadOnlyCollection<int> savedAttractions ) =>
		liveRides.Where( ride => ride.Value.IsWater ).All( ride => savedAttractions.Contains( ride.Key ) ) && savedAttractions.All( liveRides.ContainsKey );

	/// <summary>
	/// The coaster part's pure consistency (null means the park held no coasters — saves from before the coaster slice):
	/// every entry joined to one placed object by a strictly increasing attraction id, the track's shape (station node,
	/// pylons, one station end node exactly when the track completed, the closure rules, cell bounds, known node flag
	/// bits only), the controller scalars' ranges, the trains (only on a completed track, defined states, bounded
	/// riders), and every rider — seated or leaving — a guest the snapshot holds aboard that very attraction, or one
	/// still in the boarding hand-over (called, VAR_LETMEON written, waiting at the stand point while the script has
	/// already handed it to a car). The catalog checks (which stations are coaster stations, the definition's train and
	/// car bounds) are <see cref="ValidateSnapshot"/>'s.
	/// </summary>
	private static void ValidateCoasters( ParkResumeSnapshot snapshot )
	{
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"World save is inconsistent: {message}." );
		}
		if ( snapshot.Coasters is not { } coasters )
			return;
		Require( coasters.Rides != null, "the coaster part holds no ride list" );
		Require( coasters.Rides!.Count <= CoasterSave.MaximumRides,
			$"the coaster part holds {coasters.Rides.Count} rides, above {CoasterSave.MaximumRides}" );
		var width = snapshot.Layout!.Cells!.Width;
		var height = snapshot.Layout.Cells.Height;
		bool InBounds( int x, int y ) => (uint)x < (uint)width && (uint)y < (uint)height;
		var placedAttractions = snapshot.Layout.Objects!.Select( record => record!.AttractionId ).ToHashSet();
		var bridgesById = snapshot.Guests!.Attractions!.ToDictionary( attraction => attraction!.AttractionId );
		var aboardByAttraction = snapshot.Guests.Attractions!.ToDictionary( attraction => attraction!.AttractionId, attraction => attraction.OnRide!.ToHashSet() );
		var guestsById = snapshot.Guests.Guests!.ToDictionary( guest => guest!.Id );
		var ridersEverywhere = new HashSet<int>();
		var previousAttractionId = 0;
		for ( var index = 0; index < coasters.Rides.Count; index++ )
		{
			var ride = coasters.Rides[index];
			Require( ride != null, $"coaster entry {index} is missing" );
			Require( ride!.AttractionId > previousAttractionId,
				$"coaster entry {index} names attraction {ride.AttractionId}, which does not follow {previousAttractionId} in creation order" );
			previousAttractionId = ride.AttractionId;
			Require( placedAttractions.Contains( ride.AttractionId ),
				$"the coaster part names attraction {ride.AttractionId}, which the park does not place" );
			Require( ride.Track != null && ride.Track.Nodes != null, $"coaster {ride.AttractionId} holds no track" );
			var track = ride.Track!;
			Require( track.Nodes!.Count <= CoasterSave.MaximumNodes,
				$"coaster {ride.AttractionId}'s track holds {track.Nodes.Count} nodes, above {CoasterSave.MaximumNodes}" );
			Require( track.Version >= 0, $"coaster {ride.AttractionId}'s track version {track.Version} is negative" );
			Require( track.DepartCell.HasValue == track.ReturnCell.HasValue,
				$"coaster {ride.AttractionId}'s track names one station end but not the other" );
			if ( track.DepartCell is { } depart && track.ReturnCell is { } @return )
			{
				Require( InBounds( depart.X, depart.Y ) && InBounds( @return.X, @return.Y ),
					$"coaster {ride.AttractionId}'s station end ({depart.X}, {depart.Y})/({@return.X}, {@return.Y}) is outside the cell map" );
				Require( depart != @return, $"coaster {ride.AttractionId}'s station ends share one cell" );
			}
			else
				Require( track.Nodes.Count == 0 && !track.IsClosed && !track.IsComplete,
					$"coaster {ride.AttractionId}'s track holds nodes without station ends" );
			var pylons = 0;
			var stationNodes = 0;
			var stationNodesAtHead = 0;
			for ( var node = 0; node < track.Nodes.Count; node++ )
			{
				var saved = track.Nodes[node];
				Require( saved != null, $"coaster {ride.AttractionId}'s node {node} is missing" );
				Require( InBounds( saved!.X, saved.Y ),
					$"coaster {ride.AttractionId}'s node {node} stands at ({saved.X}, {saved.Y}), outside the cell map" );
				Require( float.IsFinite( saved.Height ) && float.IsFinite( saved.T ) && float.IsFinite( saved.Bank ),
					$"coaster {ride.AttractionId}'s node {node} holds a height, parameter or bank that is not finite" );
				// the height drag's range ends at t + (fMaxHeight − the stack's top)/(h1 − h0), which passes 1 when fMaxHeight is above the pylon model's top (0x441a00, 0x434680)
				Require( saved.T is >= 0 and <= CoasterSave.MaximumLoft, $"coaster {ride.AttractionId}'s node {node} height parameter {saved.T} is outside 0..{CoasterSave.MaximumLoft}" );
				// the loop-tilt drag keeps a first pylon of a pair in [−0.35, 0.35] and any other in [0.15, 0.85] (0x441cd0); a save from before it holds 0..1
				Require( saved.LoopRotate is null || (float.IsFinite( saved.LoopRotate.Value ) && saved.LoopRotate.Value is >= -0.35f and <= 1),
					$"coaster {ride.AttractionId}'s node {node} loop rotate {saved.LoopRotate} is outside −0.35..1" );
				Require( saved.Yaw is null || float.IsFinite( saved.Yaw.Value ), $"coaster {ride.AttractionId}'s node {node} yaw {saved.Yaw} is not finite" );
				Require( saved.Incline is null || (float.IsFinite( saved.Incline.Value ) && Math.Abs( saved.Incline.Value ) <= 3.1417f ),
					$"coaster {ride.AttractionId}'s node {node} incline {saved.Incline} is outside ±π" );
				Require( saved.StackIndex >= 0, $"coaster {ride.AttractionId}'s node {node} stack index {saved.StackIndex} is negative" );
				Require( (saved.Flags & ~CoasterSave.KnownNodeFlags) == 0,
					$"coaster {ride.AttractionId}'s node {node} carries flag bits 0x{saved.Flags:X}, which this format does not know" );
				Require( (saved.ExactX is null) == (saved.ExactY is null) && (saved.ExactY is null) == (saved.ExactZ is null),
					$"coaster {ride.AttractionId}'s node {node} holds a partial exact position" );
				Require( saved.ExactX is null || (float.IsFinite( saved.ExactX.Value ) && float.IsFinite( saved.ExactY!.Value ) && float.IsFinite( saved.ExactZ!.Value )),
					$"coaster {ride.AttractionId}'s node {node} holds an exact position that is not finite" );
				Require( saved.ExactX is null || saved.IsStation,
					$"coaster {ride.AttractionId}'s node {node} holds an exact position but is not a station node" );
				if ( saved.IsStation )
				{
					stationNodes++;
					// The station is one node (a save from before COAST-029) or its three nodes at the head of the list.
					Require( node < CoasterTrack.StationNodeCount && (node == 0 || stationNodes == node + 1),
						$"coaster {ride.AttractionId}'s node {node} is a second station node away from the head of the track" );
					if ( node == 0 )
						stationNodesAtHead = 1;
				}
				if ( IsPylonlessNode( saved ) )
				{
					Require( !saved.LoopPylon && saved.StackIndex == 0,
						$"coaster {ride.AttractionId}'s pylon-less node {node} is a stacked loop pylon" );
					Require( saved.IsStationEnd ? node == track.Nodes.Count - 1 : saved.ExactX is not null,
						$"coaster {ride.AttractionId}'s node {node} is a pylon-less node outside its place in the track" );
				}
				if ( !saved.IsStation && !saved.IsStationEnd )
					pylons++;
			}
			var threeNodeStation = stationNodes == CoasterTrack.StationNodeCount;
			if ( track.Nodes.Count > 0 )
			{
				Require( stationNodesAtHead == 1 && (stationNodes == 1 || threeNodeStation), "a track with nodes holds no station" );
				Require( !threeNodeStation || (track.Nodes[1]!.ExactX is not null && track.Nodes[0]!.ExactX is null && track.Nodes[2]!.ExactX is null),
					$"coaster {ride.AttractionId}'s station holds its middle node on a pylon or a pylon-less end" );
				Require( threeNodeStation || track.DepartCell == (track.Nodes[0]!.X, track.Nodes[0].Y),
					$"coaster {ride.AttractionId}'s station node does not stand at its depart end" );
			}
			Require( !track.IsComplete || track.IsClosed || track.CutAfter != null, $"coaster {ride.AttractionId}'s track completed without closing" );
			if ( track.CutAfter is int cut )
			{
				Require( track.IsComplete && !track.IsClosed && cut >= CoasterTrack.StationNodeCount - 1 && cut < track.Nodes.Count - 2,
					$"coaster {ride.AttractionId}'s circuit is cut open after node {cut}, which does not fit a finished track of {track.Nodes.Count} nodes" );
				Require( (track.Nodes[cut]!.Flags & (uint)CoasterNodeFlags.CutEnd) != 0 && (track.Nodes[cut + 1]!.Flags & (uint)CoasterNodeFlags.CutStart) != 0,
					$"coaster {ride.AttractionId}'s cut ends carry no cut marks" );
			}
			Require( !track.IsClosed || pylons >= 2, $"coaster {ride.AttractionId}'s circuit closes over {pylons} pylons" );
			if ( track.IsComplete )
			{
				var end = track.Nodes[^1]!;
				Require( end.IsStationEnd, $"coaster {ride.AttractionId}'s completed track holds no station end node" );
				Require( threeNodeStation ? (end.X, end.Y) == (track.Nodes[0]!.X, track.Nodes[0].Y) : track.ReturnCell == (end.X, end.Y),
					$"coaster {ride.AttractionId}'s station end node does not stand at the head of its station" );
				Require( track.Nodes.Count( node => node!.IsStationEnd ) == 1,
					$"coaster {ride.AttractionId}'s completed track holds more than one station end node" );
			}
			else
				Require( track.Nodes.All( node => !node!.IsStationEnd ),
					$"coaster {ride.AttractionId}'s open track holds a station end node" );
			Require( ride.State is null or 1 or 2 or 4 or 8, $"coaster {ride.AttractionId}'s controller state {ride.State} is not 1, 2, 4 or 8" );
			Require( ride.State is null or 1 || (track.IsComplete && track.CutAfter == null), $"coaster {ride.AttractionId}'s controller state {ride.State} needs a finished track" );
			Require( ride.BreakdownState is >= 0 and <= 2, $"coaster {ride.AttractionId}'s breakdown state {ride.BreakdownState} is not 0, 1 or 2" );
			Require( ride.SpeedSetting >= 0, $"coaster {ride.AttractionId}'s speed setting {ride.SpeedSetting} is negative" );
			Require( ride.SeatsPerTrain >= 0, $"coaster {ride.AttractionId}'s trains seat {ride.SeatsPerTrain}" );
			Require( float.IsFinite( ride.SpeedCap ) && ride.SpeedCap >= 0, $"coaster {ride.AttractionId}'s speed cap {ride.SpeedCap} is negative or not finite" );
			Require( ride.LeadCarCoordinate is null || (float.IsFinite( ride.LeadCarCoordinate.Value ) && ride.LeadCarCoordinate >= 0),
				$"coaster {ride.AttractionId}'s lead car coordinate {ride.LeadCarCoordinate} is negative or not finite" );
			Require( ride.KnotPass is null || ride.KnotPass == CoasterRide.KnotPassVersion,
				$"coaster {ride.AttractionId} was saved on knot pass {ride.KnotPass}, this version builds {CoasterRide.KnotPassVersion}" );
			Require( ride.PaidCost >= 0, $"coaster {ride.AttractionId} paid {ride.PaidCost} for its track" );
			Require( double.IsFinite( ride.TimeSeconds ) && ride.TimeSeconds >= 0, $"coaster {ride.AttractionId}'s clock {ride.TimeSeconds}s is negative or not finite" );
			Require( ride.LastDispatch is null || double.IsFinite( ride.LastDispatch.Value ),
				$"coaster {ride.AttractionId}'s last dispatch is not finite" );
			Require( ride.Trains != null && ride.Leaving != null, $"coaster {ride.AttractionId} holds no train or leaving list" );
			Require( ride.Trains!.Count <= CoasterSave.MaximumTrains,
				$"coaster {ride.AttractionId} holds {ride.Trains.Count} trains, above {CoasterSave.MaximumTrains}" );
			Require( (track.IsComplete && track.CutAfter == null) || ride.Trains!.Count == 0,
				$"coaster {ride.AttractionId} holds {ride.Trains!.Count} trains on a track that never completed" );
			void RequireRider( int guestId, string where )
			{
				Require( guestsById.TryGetValue( guestId, out var guest ),
					$"{where} of coaster {ride.AttractionId} holds guest {guestId}, which the guest snapshot does not hold" );
				if ( guest!.State == GuestState.Using )
				{
					Require( guest.AttractionId == ride.AttractionId,
						$"guest {guestId} rides coaster {ride.AttractionId} in its trains, but the guest snapshot holds it aboard attraction {guest.AttractionId}" );
					Require( aboardByAttraction.TryGetValue( ride.AttractionId, out var aboard ) && aboard.Contains( guestId ),
						$"guest {guestId} rides coaster {ride.AttractionId} in its trains, but is not aboard it in the guest snapshot" );
				}
				else
				{
					// The boarding hand-over: the station script took the guest from VAR_LETMEON into a car with COAST 1,
					// but the bridge marks it aboard only one host step later, when the script consumes VAR_LETMEON. A save between the two holds the guest still waiting at the stand point: WaitingToBoard,
					// bound to this coaster, still queued, called with VAR_LETMEON written (proved against the real
					// Jungle station script — see GuestsBoardingARealCoasterStaySaveConsistentAtEveryTickBoundary).
					Require( guest.State == GuestState.WaitingToBoard && guest.AttractionId == ride.AttractionId
						&& bridgesById.TryGetValue( ride.AttractionId, out var bridge ) && bridge.CalledGuest == guestId && bridge.LetMeOnWritten,
						$"guest {guestId} rides coaster {ride.AttractionId} in its trains, but walks the park in state {guest.State}" );
				}
				Require( ridersEverywhere.Add( guestId ),
					$"guest {guestId} rides two coasters in the save" );
			}
			for ( var train = 0; train < ride.Trains!.Count; train++ )
			{
				var saved = ride.Trains[train];
				Require( saved != null, $"coaster {ride.AttractionId}'s train {train} is missing" );
				Require( saved!.State is >= 0 and <= 3,
					$"coaster {ride.AttractionId}'s train {train} state {saved.State} is not a train state" );
				Require( saved.Flags is null || (saved.Flags.Value & ~(int)(CoasterTrainFlags.Braking | CoasterTrainFlags.InStation | CoasterTrainFlags.Bumped | CoasterTrainFlags.Headway)) == 0,
					$"coaster {ride.AttractionId}'s train {train} carries unknown flags {saved.Flags}" );
				Require( saved.ReferenceCar is null || (saved.ReferenceCar >= 0 && saved.ReferenceCar < Math.Max( 1, saved.Cars?.Count ?? 0 )),
					$"coaster {ride.AttractionId}'s train {train} is placed by car {saved.ReferenceCar}, outside its cars" );
				Require( saved.FirstBoardingCar >= 0 && saved.LastBoardingCar >= saved.FirstBoardingCar && saved.LastBoardingCar < Math.Max( 1, saved.Cars?.Count ?? 0 ),
					$"coaster {ride.AttractionId}'s train {train} boards cars {saved.FirstBoardingCar}..{saved.LastBoardingCar}, outside its cars" );
				Require( float.IsFinite( saved.Position ) && saved.Position >= 0,
					$"coaster {ride.AttractionId}'s train {train} stands at {saved.Position}, negative or not finite" );
				Require( float.IsFinite( saved.Speed ) && saved.Speed >= 0,
					$"coaster {ride.AttractionId}'s train {train} runs at {saved.Speed}, negative or not finite" );
				Require( double.IsFinite( saved.BoardingSince ) && saved.BoardingSince >= 0,
					$"coaster {ride.AttractionId}'s train {train} boarded at {saved.BoardingSince}s, negative or not finite" );
				Require( saved.Cars != null, $"coaster {ride.AttractionId}'s train {train} holds no car list" );
				Require( saved.Cars!.Count <= CoasterSave.MaximumCarsPerTrain,
					$"coaster {ride.AttractionId}'s train {train} holds {saved.Cars.Count} cars, above {CoasterSave.MaximumCarsPerTrain}" );
				var riders = 0;
				for ( var car = 0; car < saved.Cars!.Count; car++ )
				{
					Require( saved.Cars[car] != null, $"coaster {ride.AttractionId}'s train {train} car {car} is missing" );
					riders += saved.Cars[car]!.Count;
					foreach ( var guestId in saved.Cars[car]! )
						RequireRider( guestId, $"train {train} car {car}" );
					if ( saved.BoardingCars == null || car >= saved.BoardingCars.Count )
						continue;
					Require( saved.BoardingCars[car] != null, $"coaster {ride.AttractionId}'s train {train} boarding list {car} is missing" );
					riders += saved.BoardingCars[car]!.Count;
					foreach ( var guestId in saved.BoardingCars[car]! )
						RequireRider( guestId, $"train {train} boarding car {car}" );
				}
				Require( riders <= ride.SeatsPerTrain,
					$"coaster {ride.AttractionId}'s train {train} holds {riders} riders above its {ride.SeatsPerTrain} seats" );
			}
			foreach ( var guestId in ride.Leaving! )
				RequireRider( guestId, "the leaving queue" );
			foreach ( var guestId in ride.Admission ?? new List<int>() )
				RequireRider( guestId, "the admission ring" );
		}
	}

	/// <summary>Whether a saved node is the old shape's station node, the middle station node (an exact position) or the station end node.</summary>
	private static bool IsPylonlessNode( CoasterNodeSaveData node ) => node.IsStationEnd || node.ExactX is not null;

	/// <summary>
	/// Runs one inner validator and converts the crashes a null or malformed nested part can cause (the lane
	/// validators assume their DTOs came from a capture) into <see cref="InvalidDataException"/>; a null part
	/// inside a save is never silently accepted and never escapes as another exception type.
	/// </summary>
	private static void ValidateInner( Action validate, string what )
	{
		try
		{
			validate();
		}
		catch ( InvalidDataException )
		{
			throw;
		}
		catch ( Exception exception ) when ( exception is NullReferenceException or ArgumentNullException
			or ArgumentException or ArgumentOutOfRangeException or InvalidOperationException or KeyNotFoundException )
		{
			throw new InvalidDataException( $"World save is inconsistent: the {what} holds a missing or malformed part.", exception );
		}
	}

	/// <summary>Original TPWS/TPWI are never written or read as world saves; writing also requires the own-format suffix.</summary>
	private static string GetPath( string path, bool forWriting )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		var extension = Path.GetExtension( path );
		if ( extension.Equals( ".tpws", StringComparison.OrdinalIgnoreCase ) || extension.Equals( ".tpwi", StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidDataException( "Original TPWS/TPWI files are never written or read as OpenTPW world saves." );
		if ( forWriting && !extension.Equals( ".otpw", StringComparison.OrdinalIgnoreCase ) && !extension.Equals( ".json", StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidDataException( $"World saves are written with their own .otpw or .json suffix, not '{extension}'." );
		return Path.GetFullPath( path );
	}
}
