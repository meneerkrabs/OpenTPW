using System.Text.Json;

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
	public const int CurrentVersion = 1;
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
		var destination = GetPath( path, forWriting: true );
		var bytes = Serialize( snapshot );
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
		IEconomyObjectCatalog economyCatalog, IReadOnlyDictionary<string, string> currentAssetHashes )
	{
		ArgumentNullException.ThrowIfNull( catalog );
		ArgumentNullException.ThrowIfNull( settings );
		ArgumentNullException.ThrowIfNull( economyCatalog );
		ValidateShape( snapshot );
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"World save is inconsistent: {message}." );
		}
		ParkLayoutSnapshot.ValidateSnapshot( snapshot.Layout, catalog );
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
				definitionsByAttraction[record.AttractionId] = CoasterDefinition.FromSettings( entry.Settings );
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
					Require( train.Cars.Count == definition.MaxCars,
						$"a train of coaster {ride.AttractionId} holds {train.Cars.Count} cars, not the definition's {definition.MaxCars}" );
			}
		}
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
		Require( currentAssetHashes != null && currentAssetHashes.Count > 0, "the current asset manifest is empty" );
		foreach ( var (key, saved) in snapshot.AssetHashes )
		{
			Require( currentAssetHashes!.TryGetValue( key, out var current ),
				$"the save expects asset '{key}', which the park's assets do not provide" );
			Require( string.Equals( saved, current, StringComparison.Ordinal ),
				$"asset '{key}' hashes to '{current}', not the saved '{saved}'" );
		}
		foreach ( var key in currentAssetHashes!.Keys )
			Require( snapshot.AssetHashes.ContainsKey( key ),
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
		Require( snapshot.Layout != null, "the layout snapshot is missing" );
		Require( snapshot.Guests != null, "the guest snapshot is missing" );
		Require( snapshot.Scripts != null, "the script-world snapshot is missing" );
		Require( snapshot.Scripts!.Scripts != null, "the script world holds no script list" );
		Require( snapshot.Runtimes != null, "the runtime list is missing" );
		Require( snapshot.EconomyBindings != null, "the economy-binding list is missing" );
		Require( snapshot.Clock != null, "the clock snapshot is missing" );
		Require( snapshot.AssetHashes != null, "the asset manifest is missing" );

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

		// Script world: its own graph validation, and the same attraction-id allocator as the layout.
		foreach ( var script in snapshot.Scripts.Scripts! )
			Require( script != null, "the script world holds a missing script" );
		ValidateInner( snapshot.Scripts.ValidateSnapshot, "script world" );
		Require( layout.NextAttractionId == snapshot.Scripts.NextAttractionId,
			$"the layout's next attraction id {layout.NextAttractionId} is not the script world's {snapshot.Scripts.NextAttractionId}" );

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
				Require( root!.Seed == record.CreationSeed,
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
				Require( prototypeRoot!.Seed == prototype.ScriptSeed,
					$"the prototype's root script was created with seed {prototypeRoot.Seed}, not the part's {prototype.ScriptSeed}" );
				Require( claimedRoots.Add( prototype.RootScriptId ), $"root script {prototype.RootScriptId} is claimed by two runtimes" );
			}
			else
				Require( prototype.RootScriptId == 0, $"the prototype names root script {prototype.RootScriptId} without a live script" );
		}
		Require( claimedRoots.Count == worldRoots.Count,
			$"the script world holds {worldRoots.Count} root scripts, but {claimedRoots.Count} live objects name one" );

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

		ValidateGuests( snapshot );
		ValidateCoasters( snapshot );

		// Clock: the clock's own pure validator, on a fresh instance it never mutates.
		new FixedStepClock().ValidateSnapshot( snapshot.Clock! );

		// World streams, when the economy carried them, must agree with the typed fields.
		if ( snapshot.Economy.World is { } world )
		{
			Require( world.Seed == snapshot.Seed, $"the economy's world seed {world.Seed} is not the envelope's {snapshot.Seed}" );
			Require( world.GuestRandom == snapshot.Guests!.RandomState,
				$"the economy's guest stream {world.GuestRandom} is not the guest snapshot's {snapshot.Guests.RandomState}" );
			Require( world.ScriptRandom == snapshot.Scripts.RandomState,
				$"the economy's script stream {world.ScriptRandom} is not the script world's {snapshot.Scripts.RandomState}" );
			Require( world.SoundSeed == snapshot.SoundSeed,
				$"the economy's sound seed {world.SoundSeed} is not the envelope's {snapshot.SoundSeed}" );
		}

		// Optional parts: present means structurally valid; the destination park re-validates against its settings.
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
		if ( snapshot.Gate is { } gate )
			Require( gate.LastObservedTurn >= 0, $"the park gate's last observed turn {gate.LastObservedTurn} is negative" );
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
		if ( snapshot.Advisor is { } advisor )
		{
			Require( advisor.Controller != null, "the advisor snapshot has no controller state" );
			Require( !advisor.SpeechWasActive, "the advisor was speaking when the park was saved; active speech cannot be restored" );
		}

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
	private static void ValidateGuests( ParkResumeSnapshot snapshot )
	{
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"World save is inconsistent: {message}." );
		}
		var guests = snapshot.Guests!;
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
		Require( guests.PendingArrivals >= 0, $"the guest snapshot holds {guests.PendingArrivals} pending arrivals" );
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
			Require( guest!.Id >= 1 && guest.Id < guests.NextGuestId,
				$"guest {guest.Id} is not below the next guest id {guests.NextGuestId}" );
			Require( guest.Id > previousId, $"guest {guest.Id} does not follow guest {previousId}; guests are saved in ascending id order" );
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
			Require( guest.Money >= 0 && guest.RidesTaken >= 0, $"guest {guest.Id} holds negative money or rides" );
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
			Require( navigation.Cursor >= 0 && navigation.Cursor <= navigation.Route!.Length,
				$"guest {guest.Id} navigation cursor {navigation.Cursor} is outside its {navigation.Route!.Length}-cell route" );
			var buffered = Math.Min( GuestNavigation.BufferedWaypoints, navigation.Route.Length - navigation.Cursor );
			Require( navigation.Count == buffered,
				$"guest {guest.Id} buffers {navigation.Count} waypoints, but its route from cursor {navigation.Cursor} gives {buffered}" );
			Require( navigation.Count == 0 ? navigation.Index == 0 : (uint)navigation.Index < (uint)navigation.Count,
				$"guest {guest.Id} waypoint index {navigation.Index} is outside its {navigation.Count} buffered waypoints" );
			Require( navigation.History is >= 0 and <= 0x7fff, $"guest {guest.Id} progress history {navigation.History} is outside its 15-bit window" );
			Require( navigation.PreviousRemaining >= -1, $"guest {guest.Id} previous remaining distance {navigation.PreviousRemaining} is negative" );
			Require( navigation.Reroutes >= 0 && navigation.WalkCrossings >= 0,
				$"guest {guest.Id} navigation counters ({navigation.Reroutes} reroutes, {navigation.WalkCrossings} crossings) are negative" );
			Require( InBounds( guest.CellX, guest.CellY ), $"guest {guest.Id} cell ({guest.CellX}, {guest.CellY}) is outside the cell map" );
			if ( guest.PreviousCellX >= 0 || guest.PreviousCellY >= 0 )
				Require( InBounds( guest.PreviousCellX, guest.PreviousCellY ),
					$"guest {guest.Id} previous cell ({guest.PreviousCellX}, {guest.PreviousCellY}) is outside the cell map" );
			Require( InBounds( navigation.X >> 16, navigation.Y >> 16 ),
				$"guest {guest.Id} navigation position ({navigation.X >> 16}, {navigation.Y >> 16}) is outside the cell map" );
			Require( InBounds( navigation.TargetX >> 16, navigation.TargetY >> 16 ),
				$"guest {guest.Id} navigation target ({navigation.TargetX >> 16}, {navigation.TargetY >> 16}) is outside the cell map" );
			foreach ( var (x, y) in navigation.Route )
				Require( InBounds( x, y ), $"guest {guest.Id} route cell ({x}, {y}) is outside the cell map" );
			foreach ( var (x, y) in navigation.AlsoOpen! )
				Require( InBounds( x, y ), $"guest {guest.Id} extra open cell ({x}, {y}) is outside the cell map" );
			var onLane = guest.State is GuestState.CrossingRoad or GuestState.CrossingRoadToPark or GuestState.GoingToTicketBooth
				or GuestState.AtTicketBooth or GuestState.EnteringPark or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome;
			if ( onLane )
			{
				Require( guest.Lane != null && guest.Lane.Length >= 2,
					$"guest {guest.Id} is in state {guest.State} without an arrival lane of at least two cells" );
				Require( guest.Lane == null || (uint)guest.LaneIndex < (uint)guest.Lane.Length,
					$"guest {guest.Id} lane index {guest.LaneIndex} is outside its {guest.Lane!.Length}-cell lane" );
				Require( !guest.OnGrid && navigation.OffGrid, $"guest {guest.Id} is in state {guest.State} but is marked on the path grid" );
			}
			else if ( guest.State == GuestState.LeavingPark && guest.Lane != null )
			{
				Require( guest.Lane.Length >= 2 && (uint)guest.LaneIndex < (uint)guest.Lane.Length,
					$"guest {guest.Id} walks a lane home with index {guest.LaneIndex} of {guest.Lane.Length} cells" );
				Require( !guest.OnGrid && navigation.OffGrid, $"guest {guest.Id} walks a lane home but is marked on the path grid" );
			}
			else
			{
				Require( guest.OnGrid && !navigation.OffGrid, $"guest {guest.Id} is in state {guest.State} but is not on the path grid" );
				Require( guest.Lane == null, $"guest {guest.Id} is in state {guest.State} but still carries an arrival lane" );
			}
			if ( guest.Lane != null )
				foreach ( var (x, y) in guest.Lane )
					Require( InBounds( x, y ), $"guest {guest.Id} lane cell ({x}, {y}) is outside the cell map" );
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
			for ( var node = 0; node < track.Nodes.Count; node++ )
			{
				var saved = track.Nodes[node];
				Require( saved != null, $"coaster {ride.AttractionId}'s node {node} is missing" );
				Require( InBounds( saved!.X, saved.Y ),
					$"coaster {ride.AttractionId}'s node {node} stands at ({saved.X}, {saved.Y}), outside the cell map" );
				Require( float.IsFinite( saved.Height ) && float.IsFinite( saved.T ) && float.IsFinite( saved.Bank ),
					$"coaster {ride.AttractionId}'s node {node} holds a height, parameter or bank that is not finite" );
				Require( saved.T is >= 0 and <= 1, $"coaster {ride.AttractionId}'s node {node} height parameter {saved.T} is outside 0..1" );
				Require( saved.StackIndex >= 0, $"coaster {ride.AttractionId}'s node {node} stack index {saved.StackIndex} is negative" );
				Require( (saved.Flags & ~CoasterSave.KnownNodeFlags) == 0,
					$"coaster {ride.AttractionId}'s node {node} carries flag bits 0x{saved.Flags:X}, which this format does not know" );
				if ( saved.IsStation )
				{
					stationNodes++;
					Require( node == 0, $"coaster {ride.AttractionId}'s node {node} is a second station node" );
				}
				if ( IsPylonlessNode( saved ) )
				{
					Require( !saved.LoopPylon && saved.StackIndex == 0,
						$"coaster {ride.AttractionId}'s pylon-less node {node} is a stacked loop pylon" );
					Require( node == 0 || node == track.Nodes.Count - 1,
						$"coaster {ride.AttractionId}'s node {node} is a station end node outside the ends of the track" );
				}
				else
					pylons++;
			}
			if ( track.Nodes.Count > 0 )
			{
				Require( stationNodes == 1, "a track with nodes holds no station node" );
				Require( track.DepartCell == (track.Nodes[0]!.X, track.Nodes[0].Y),
					$"coaster {ride.AttractionId}'s station node does not stand at its depart end" );
			}
			Require( !track.IsComplete || track.IsClosed, $"coaster {ride.AttractionId}'s track completed without closing" );
			Require( !track.IsClosed || pylons >= 2, $"coaster {ride.AttractionId}'s circuit closes over {pylons} pylons" );
			if ( track.IsComplete )
			{
				var end = track.Nodes[^1]!;
				Require( end.IsStationEnd, $"coaster {ride.AttractionId}'s completed track holds no station end node" );
				Require( track.ReturnCell == (end.X, end.Y),
					$"coaster {ride.AttractionId}'s station end node does not stand at its return end" );
				Require( track.Nodes.Count( node => node!.IsStationEnd ) == 1,
					$"coaster {ride.AttractionId}'s completed track holds more than one station end node" );
			}
			else
				Require( track.Nodes.All( node => !node!.IsStationEnd ),
					$"coaster {ride.AttractionId}'s open track holds a station end node" );
			Require( ride.BreakdownState is >= 0 and <= 2, $"coaster {ride.AttractionId}'s breakdown state {ride.BreakdownState} is not 0, 1 or 2" );
			Require( ride.SpeedSetting >= 0, $"coaster {ride.AttractionId}'s speed setting {ride.SpeedSetting} is negative" );
			Require( ride.SeatsPerTrain >= 0, $"coaster {ride.AttractionId}'s trains seat {ride.SeatsPerTrain}" );
			Require( float.IsFinite( ride.SpeedCap ) && ride.SpeedCap >= 0, $"coaster {ride.AttractionId}'s speed cap {ride.SpeedCap} is negative or not finite" );
			Require( ride.PaidCost >= 0, $"coaster {ride.AttractionId} paid {ride.PaidCost} for its track" );
			Require( double.IsFinite( ride.TimeSeconds ) && ride.TimeSeconds >= 0, $"coaster {ride.AttractionId}'s clock {ride.TimeSeconds}s is negative or not finite" );
			Require( ride.LastDispatch is null || double.IsFinite( ride.LastDispatch.Value ),
				$"coaster {ride.AttractionId}'s last dispatch is not finite" );
			Require( ride.Trains != null && ride.Leaving != null, $"coaster {ride.AttractionId} holds no train or leaving list" );
			Require( ride.Trains!.Count <= CoasterSave.MaximumTrains,
				$"coaster {ride.AttractionId} holds {ride.Trains.Count} trains, above {CoasterSave.MaximumTrains}" );
			Require( track.IsComplete || ride.Trains!.Count == 0,
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
					// but the bridge marks it aboard only one host step later, when the script consumes VAR_LETMEON or
					// HUSHes it. A save between the two holds the guest still waiting at the stand point: WaitingToBoard,
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
				Require( Enum.IsDefined( (CoasterTrainState)saved!.State ),
					$"coaster {ride.AttractionId}'s train {train} state {saved.State} is not a train state" );
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
				}
				Require( riders <= ride.SeatsPerTrain,
					$"coaster {ride.AttractionId}'s train {train} holds {riders} riders above its {ride.SeatsPerTrain} seats" );
			}
			foreach ( var guestId in ride.Leaving! )
				RequireRider( guestId, "the leaving queue" );
		}
	}

	/// <summary>Whether a saved node is a pylon-less station or station end node.</summary>
	private static bool IsPylonlessNode( CoasterNodeSaveData node ) => node.IsStation || node.IsStationEnd;

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
