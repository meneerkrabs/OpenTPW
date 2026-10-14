using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The coaster part of the park save on the original PC data (inconclusive without OPENTPW_GAME_PATH): a real coaster
/// station's save validates against the real catalog, a doctored part is refused with a reason, and two stations — one
/// running a completed circuit, one mid-build — save through the real runtimes and script world, restore into fresh
/// candidate runtimes and continue deterministically (the canonical coaster hash, the shared generator included, is
/// equal at the boundary and after further simulation of both parks). Runtimes are built headless (no entities, no
/// GPU), exactly like the other park-resume fixtures.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CoasterSaveAssetTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	private BaseFileSystem previousFileSystem = null!;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Init()
	{
		previousFileSystem = FileSystem;
		previousLog = Log;
		Log = new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original coaster archives." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => Path.GetFileName( path ).Equals( "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	// Other tests read the global FileSystem, so the original game data must not outlive the test.
	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previousFileSystem;
		Log = previousLog;
	}

	/// <summary>Builds a circuit on a station's controller over a flat site (the headless build API; a level's own site only exists in play).</summary>
	private static void BuildCircuit( CoasterBuilder builder, (int X, int Y) depart, int height = 40 )
	{
		builder.SetStation( depart, (depart.X - 3, depart.Y) );
		builder.Height = height;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( depart.X + dx, depart.Y + dy ), $"pylon at offset ({dx}, {dy})" );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
	}

	/// <summary>A real Jungle envelope around two placed coaster stations (attractions 1 and 2), each with a completed circuit.</summary>
	private sealed class CoasterEnvelope
	{
		public readonly ObjectCatalog Catalog = ObjectCatalog.Load( "jungle" );
		public readonly BalanceSettings Settings = BalanceSettings.Load( "jungle", false );
		public readonly EconomyObjectCatalog EconomyCatalog = EconomyObjectCatalog.Load( "jungle", false );
		public readonly ObjectCatalogEntry Coaster;
		public readonly OriginalObjectRuntime First;
		public readonly OriginalObjectRuntime Second;
		public readonly IReadOnlyDictionary<string, string> Manifest;
		public readonly ParkResumeSnapshot Snapshot;

		public CoasterEnvelope()
		{
			Coaster = Catalog.Entries.First( entry => CoasterDefinition.IsCoaster( entry.Settings ) );
			var park = OriginalPark.Load( "jungle" );
			var seed = WorldSeed.Default;
			var objects = new ParkObjects( Catalog, new OriginalParkGrid( park ), seed );
			var world = new RideScriptWorld( seed.ScriptStream );
			First = new OriginalObjectRuntime( Coaster, world, seed: 1 ^ seed.ObjectScriptKey, open: true );
			Second = new OriginalObjectRuntime( Coaster, world, seed: 2 ^ seed.ObjectScriptKey, open: true );
			BuildCircuit( new CoasterBuilder( First.Coaster!, new CoasterSite( 128, 128 ) ), (20, 20) );
			BuildCircuit( new CoasterBuilder( Second.Coaster!, new CoasterSite( 128, 128 ) ), (60, 60) );
			for ( var tick = 0; tick < 90; tick++ )
			{
				First.Simulate( ParkResumeSaveFileTests.MiniaturePark.Tick );
				Second.Simulate( ParkResumeSaveFileTests.MiniaturePark.Tick );
			}

			var scripts = world.CaptureSnapshot();
			var miniature = new ParkResumeSaveFileTests.MiniaturePark();
			for ( var tick = 0; tick < 1800; tick++ )
				miniature.Step();
			var guests = miniature.Simulation.CaptureSnapshot();
			// A coaster the jungle economy lists needs an economy instance; build the economy on the real info so
			// the binding the validator demands can exist.
			ParkEconomy economy;
			var bindings = new List<ParkResumeEconomyBinding>();
			if ( EconomyCatalog.TryGet( Coaster.InfoId, out var info ) )
			{
				economy = new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( new[] { info } ), ParkGameMode.FullSimulation, 7 );
				bindings.Add( new ParkResumeEconomyBinding( 1, economy.RegisterExisting( Coaster.InfoId ).Id ) );
				bindings.Add( new ParkResumeEconomyBinding( 2, economy.RegisterExisting( Coaster.InfoId ).Id ) );
			}
			else
				economy = EconomyTestData.Park();

			string Hash( string path )
			{
				using var stream = Coaster.FileSystem.OpenRead( path );
				return ParkResumeAssets.Sha256( stream );
			}
			Manifest = new Dictionary<string, string>
			{
				[$"script:{Coaster.ScriptPath}"] = Hash( Coaster.ScriptPath! ),
				[$"model:{Coaster.ModelPath}"] = Hash( Coaster.ModelPath )
			};
			ParkLayoutSnapshot.PlacedObjectData Record( int ordinal, int x, int y ) => new()
			{
				InfoId = Coaster.InfoId,
				Archive = Coaster.ArchivePath,
				X = x,
				Y = y,
				Rotation = 0,
				Open = true,
				CreationSeed = ordinal ^ seed.ObjectScriptKey,
				AttractionId = ordinal,
				BaseHeight = objects.GetBaseHeight( Coaster, x, y, 0 ),
				IsFixedItem = false
			};
			Snapshot = new ParkResumeSnapshot
			{
				Format = ParkResumeSaveFile.FormatName,
				Version = ParkResumeSaveFile.CurrentVersion,
				Theme = "jungle",
				StartKind = ParkStartKind.FullSimulation,
				Seed = seed.Value,
				DisplayName = "Coaster park",
				SavedUtc = new DateTimeOffset( 2026, 10, 13, 9, 0, 0, TimeSpan.Zero ),
				Economy = ParkSaveFile.Capture( economy, new WorldRandomState( seed.Value, guests.RandomState, scripts.RandomState, 42u ) ),
				Layout = ParkLayoutSnapshot.CaptureSnapshot( ParkCellMap.FromOriginal( park.Map, park.Save ), objects ) with
				{
					Objects = new List<ParkLayoutSnapshot.PlacedObjectData> { Record( 1, 20, 62 ), Record( 2, 40, 62 ) },
					NextSeed = 3,
					NextAttractionId = 3
				},
				Guests = guests,
				Scripts = scripts,
				Runtimes = new List<ParkResumeRuntime> { new( First.Script!.ScriptId, First.CaptureSnapshot() ), new( Second.Script!.ScriptId, Second.CaptureSnapshot() ) },
				EconomyBindings = bindings,
				Clock = new FixedStepClockSnapshot( 2700, 0, 0 ),
				SoundSeed = 42,
				Coasters = CoasterSave.Capture( world.Coasters, new[] { (1, First.Coaster!), (2, Second.Coaster!) } ),
				AssetHashes = Manifest
			};
		}
	}

	[TestMethod]
	public void ARealCoasterStationValidatesRoundTripsAndIsRefusedWithoutItsPart()
	{
		var fixture = new CoasterEnvelope();
		var snapshot = fixture.Snapshot;
		Assert.IsNotNull( snapshot.Coasters, "a park with a coaster station saves a coaster part" );
		Assert.AreEqual( 2, snapshot.Coasters.Rides.Count );
		ParkResumeSaveFile.ValidateSnapshot( snapshot, fixture.Catalog, fixture.Settings, fixture.EconomyCatalog, fixture.Manifest );

		var restored = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( snapshot ) );
		ParkResumeSaveFile.ValidateSnapshot( restored, fixture.Catalog, fixture.Settings, fixture.EconomyCatalog, fixture.Manifest );
		Assert.IsTrue( restored.Coasters!.Rides[0].Track.IsComplete, "the completed circuit survives the codec" );
		Assert.IsTrue( restored.Coasters.Rides[0].Trains.Count >= 1, "the placed trains survive the codec" );

		void Rejects( ParkResumeSnapshot corrupted, string message ) =>
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>(
				() => ParkResumeSaveFile.ValidateSnapshot( corrupted, fixture.Catalog, fixture.Settings, fixture.EconomyCatalog, fixture.Manifest ) ).Message, message );
		// The same stations without the part are refused (a pre-coaster save never held one).
		Rejects( snapshot with { Coasters = null }, "no coaster part" );
		// A part that saves only one of the two live stations does not match the park.
		Rejects( snapshot with { Coasters = snapshot.Coasters! with { Rides = snapshot.Coasters.Rides.Take( 1 ).ToList() } },
			"places 2 live coaster stations" );
		// Definition bounds: the coaster's train and car counts (both entries stay saved, so the count check passes).
		var definition = CoasterDefinition.FromSettings( fixture.Coaster.Settings );
		var tooManyTrains = Enumerable.Range( 0, definition.MaxTrains + 1 )
			.Select( _ => snapshot.Coasters!.Rides[0].Trains[0] with { Cars = Enumerable.Range( 0, definition.MaxCars ).Select( _ => new List<int>() ).ToList() } )
			.ToList();
		Rejects( snapshot with { Coasters = snapshot.Coasters! with { Rides = new List<CoasterRideSaveData> { snapshot.Coasters.Rides[0] with { Trains = tooManyTrains }, snapshot.Coasters.Rides[1] } } },
			$"above the definition's {definition.MaxTrains}" );
		var wrongCars = snapshot.Coasters!.Rides[0].Trains.Select( train => train with { Cars = train.Cars.Take( definition.MaxCars - 1 ).ToList() } ).ToList();
		Rejects( snapshot with { Coasters = snapshot.Coasters! with { Rides = new List<CoasterRideSaveData> { snapshot.Coasters.Rides[0] with { Trains = wrongCars }, snapshot.Coasters.Rides[1] } } },
			$"not the definition's {definition.MaxCars}" );
	}

	[TestMethod]
	public void TwoStationsResumeIntoFreshRuntimesAndContinueDeterministically()
	{
		var catalog = ObjectCatalog.Load( "jungle" );
		var entry = catalog.Entries.First( candidate => CoasterDefinition.IsCoaster( candidate.Settings ) );
		var seed = WorldSeed.Default;
		var world = new RideScriptWorld( seed.ScriptStream );
		var running = new OriginalObjectRuntime( entry, world, seed: 1 ^ seed.ObjectScriptKey, open: true );
		var building = new OriginalObjectRuntime( entry, world, seed: 2 ^ seed.ObjectScriptKey, open: true );
		BuildCircuit( new CoasterBuilder( running.Coaster!, new CoasterSite( 128, 128 ) ), (20, 20) );
		var openChain = new CoasterBuilder( building.Coaster!, new CoasterSite( 128, 128 ) );
		openChain.SetStation( (60, 20), (57, 20) );
		openChain.Height = 30;
		foreach ( var (dx, dy) in CoasterTests.Circuit.Take( 3 ) )
			Assert.AreEqual( CoasterPlaceResult.Ok, openChain.Place( 60 + dx, 20 + dy ) );
		for ( var tick = 0; tick < 1800; tick++ )
		{
			running.Simulate( Tick );
			building.Simulate( Tick );
		}

		// The save: the runtimes, the script world (with two live coaster controllers) and the coaster part, all
		// through the codec.
		var runtimes = new[] { new ParkResumeRuntime( running.Script!.ScriptId, running.CaptureSnapshot() ),
			new ParkResumeRuntime( building.Script!.ScriptId, building.CaptureSnapshot() ) };
		var scripts = ParkResumeJson.Deserialize<RideScriptWorldSnapshot>( ParkResumeJson.Serialize( world.CaptureSnapshot() ) );
		var coasters = ParkResumeJson.Deserialize<CoasterWorldSaveData>( ParkResumeJson.Serialize(
			CoasterSave.Capture( world.Coasters, new[] { (running.AttractionId, running.Coaster!), (building.AttractionId, building.Coaster!) } )! ) );
		Assert.AreEqual( 2, coasters.Rides.Count, "both stations saved, in creation (attraction-id) order" );
		Assert.IsTrue( coasters.Rides[0].Track.IsComplete && !coasters.Rides[1].Track.IsComplete );

		// The candidate: the restore order of a resumed level — the stations are placed first (each placement builds a
		// fresh controller and resets its track to the station node), then the runtimes, then the script world, then
		// the coaster part over the fresh controllers.
		var candidateWorld = new RideScriptWorld( seed.ScriptStream );
		var stations = new Dictionary<int, OriginalObjectRuntime>();
		foreach ( var (runtime, saved) in new[] { (running, runtimes[0]), (building, runtimes[1]) } )
		{
			var candidate = OriginalObjectRuntime.CreateForRestore( entry, candidateWorld, SeedOf( runtime ), open: true, runtime.AttractionId );
			var gaps = candidate.RestoreSnapshot( saved.State );
			Assert.AreEqual( 0, gaps.Count, $"no restore gap: {string.Join( "; ", gaps )}" );
			stations[runtime.AttractionId] = candidate;
		}
		var roots = new Dictionary<int, RideVM>();
		foreach ( var saved in runtimes )
			if ( saved.RootScriptId != 0 )
				roots.Add( saved.RootScriptId, stations[saved.State.AttractionId].Script! );
		candidateWorld.RestoreSnapshot( scripts, roots );
		foreach ( var saved in coasters.Rides )
		{
			var ride = stations[saved.AttractionId].Coaster!;
			Assert.IsTrue( candidateWorld.Coasters.Rides.Contains( ride ), "the restored station's controller is live in the candidate world" );
			// The station placement's reset, then the saved state over it.
			var fresh = new CoasterBuilder( ride, new CoasterSite( 128, 128 ) );
			var original = saved.AttractionId == running.AttractionId ? running.Coaster! : building.Coaster!;
			fresh.SetStation( original.Track.DepartCell!.Value, original.Track.ReturnCell!.Value );
			ride.RestoreSnapshot( saved );
		}

		static ulong HashOf( RideScriptWorld scripts )
		{
			var hash = new StateHasher();
			scripts.Coasters.AddCanonicalState( hash );
			return hash.Value;
		}
		Assert.AreEqual( HashOf( world ), HashOf( candidateWorld ),
			"the reconstructed coasters hold the persisted state, the shared generator included" );
		// Deterministic continuation: the same ticks move both parks alike.
		for ( var tick = 0; tick < 600; tick++ )
		{
			running.Simulate( Tick );
			building.Simulate( Tick );
			foreach ( var candidate in stations.Values )
				candidate.Simulate( Tick );
		}
		Assert.AreEqual( HashOf( world ), HashOf( candidateWorld ), "the continuation is deterministic" );
	}

	/// <summary>
	/// Real guests boarding a completed coaster through the real station script stay save-consistent at every tick
	/// boundary — wherever a capture lands (it runs between ticks), every rider the coaster part would hold, seated in
	/// a car or waiting in the leaving queue, must be a guest the validation accepts: state <see cref="GuestState.Using"/>
	/// and aboard, or still in the boarding hand-over (WaitingToBoard, called with VAR_LETMEON written while the script
	/// has already handed the guest to a car with <c>COAST 1</c>). The hand-over window is real — the bridge marks a
	/// guest aboard only one host step after the script takes it — so this pins both the window's existence and the
	/// save's acceptance of it.
	/// </summary>
	[TestMethod]
	public void GuestsBoardingARealCoasterStaySaveConsistentAtEveryTickBoundary()
	{
		var park = OriginalPark.Load( "jungle" );
		var simulation = new GuestSimulation( GuestPathGrid.FromOriginal( park.Map, park.Save ), GuestSettings.Load( "jungle" ), 99 ) { ArrivalsEnabled = false };
		var entry = ObjectCatalog.Load( "jungle" ).Entries.First( candidate => CoasterDefinition.IsCoaster( candidate.Settings ) );
		var runtime = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: 1, open: true );
		var ride = runtime.Coaster!;
		BuildCircuit( new CoasterBuilder( ride, new CoasterSite( 128, 128 ) ), (20, 20) );
		runtime.Visitors.EntranceCell = runtime.Visitors.ExitCell = (39, 21);
		runtime.Visitors.HasCells = true;
		simulation.Register( runtime.Visitors );
		foreach ( var (x, y) in new[] { (39, 22), (39, 23), (40, 21), (41, 21), (39, 24), (42, 21), (39, 25), (43, 21) } )
			simulation.SpawnInPark( x, y );

		// The rider rule of ParkResumeSaveFile.ValidateCoasters, live: Using and aboard, or the boarding hand-over.
		bool Accepted( int rider )
		{
			var guest = simulation.Find( rider );
			return guest != null && guest.AttractionId == runtime.AttractionId
				&& (guest.State == GuestState.Using && runtime.Visitors.Riders.Contains( rider )
					|| guest.State == GuestState.WaitingToBoard && runtime.Visitors.CalledGuest == rider);
		}

		var maximumAboard = 0;
		var handOverRiderTicks = 0;
		var handOverClosed = false;
		GuestSimulationSnapshot? boundarySnapshot = null;
		CoasterRideSaveData? boundaryCoaster = null;
		for ( var tick = 0; tick < 90 * 60; tick++ )
		{
			// Level.AdvanceSimulation's order: the guests first, then the objects (HostStep, script, coaster).
			simulation.Tick( Tick );
			runtime.Simulate( Tick );
			maximumAboard = Math.Max( maximumAboard, runtime.Visitors.Riders.Count );
			var inHandOver = false;
			foreach ( var rider in ride.Trains.SelectMany( train => train.Cars ).SelectMany( car => car ).Concat( ride.LeavingRiders ) )
			{
				if ( Accepted( rider ) )
				{
					var guest = simulation.Find( rider )!;
					inHandOver |= guest.State == GuestState.WaitingToBoard;
					continue;
				}
				Assert.Fail( $"tick {tick}: rider {rider} would be refused by the save's validation "
					+ $"(guest {simulation.Find( rider )?.State}, attraction {simulation.Find( rider )?.AttractionId})" );
			}
			if ( inHandOver )
			{
				handOverRiderTicks++;
				// Once inside the window, prove the real snapshot pair carries exactly the fields the rule reads.
				boundarySnapshot ??= simulation.CaptureSnapshot();
				boundaryCoaster ??= CoasterSave.CaptureRide( ride, runtime.AttractionId );
			}
			else
				handOverClosed |= handOverRiderTicks > 0;
		}
		Assert.AreNotEqual( RideVMState.Faulted, runtime.Script!.State, runtime.Script.FaultMessage );
		Assert.IsTrue( ride.Initialised, "the station script initialised its controller (COAST 8)" );
		Assert.IsTrue( maximumAboard > 0, "guests boarded the running coaster through the real script" );
		Assert.IsTrue( handOverRiderTicks > 0, "the boarding hand-over window is real (a rider in a car before the bridge marks it aboard)" );
		Assert.IsTrue( handOverClosed, "the hand-over closes: the script consumes VAR_LETMEON and the rider is marked aboard" );
		// The snapshotted pair agrees with the live rule: the hand-over rider is the called guest, WaitingToBoard,
		// aboard nobody, while the coaster part holds it in a car.
		Assert.IsNotNull( boundarySnapshot );
		var bridge = boundarySnapshot!.Attractions.Single( attraction => attraction.AttractionId == runtime.AttractionId );
		var handOverRider = bridge.CalledGuest;
		Assert.IsTrue( handOverRider != 0 && bridge.LetMeOnWritten, "VAR_LETMEON is written for the called guest" );
		var savedGuest = boundarySnapshot.Guests.Single( guest => guest.Id == handOverRider );
		Assert.AreEqual( GuestState.WaitingToBoard, savedGuest.State );
		Assert.AreEqual( runtime.AttractionId, savedGuest.AttractionId );
		CollectionAssert.Contains( bridge.Queue.ToArray(), handOverRider );
		Assert.IsTrue( boundaryCoaster!.Trains
			.Any( train => train.Cars.Any( car => car.Contains( handOverRider ) ) ), "the coaster part holds the hand-over rider in a car" );
	}

	private static int SeedOf( OriginalObjectRuntime runtime ) => runtime.Script!.Seed;
}
