using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The prototype part of the park save on the original PC data (inconclusive without OPENTPW_GAME_PATH): the real
/// Inca Totem — its original Totem.RSE script, animator and head slots — runs beside real guests that queue, board and
/// ride it; the captured part (runtime, script world, guests) survives the codec, rebuilds into a fresh runtime and
/// guest simulation, and both parks continue deterministically (the canonical world hash, bridges and script world
/// included, is equal at the boundary and after further simulation). A Totem whose nearest path changed after
/// placement (the placement's entrance derivation is history the saved grid cannot reproduce) resumes its own
/// entrance, not a fresh derivation. A full envelope around the Totem validates against the real catalog, balance
/// and asset manifest, and doctored parts are refused with their reason. Runtimes are built headless (no entities,
/// no GPU), exactly like the other park-resume fixtures.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PrototypeRideSaveAssetTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Init()
	{
		previousFileSystem = FileSystem;
		Log = new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original Totem archives." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => Path.GetFileName( path ).Equals( "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup() => FileSystem = previousFileSystem!;

	/// <summary>A live Totem beside real guests: the sandbox's own ride, placed at a cell centre, opened and left to run.</summary>
	private sealed class TotemRig
	{
		public readonly OriginalPark Park = OriginalPark.Load( "jungle" );
		public readonly ObjectCatalogEntry Entry;
		public readonly RideScriptWorld World;
		public readonly OriginalObjectRuntime Totem;
		public readonly GuestSettings Settings;
		public readonly GuestPathGrid Grid;
		public readonly GuestSimulation Simulation;
		public readonly Vector3 Position = new( 390f, 210f, 0f );
		public int MaximumRiders;

		public TotemRig( int ticks, ulong guestSeed = 99 )
		{
			Entry = ObjectCatalog.Load( "jungle" ).Find( PrototypeRide.InfoId )
				?? throw new InvalidDataException( "The jungle catalog does not hold the Inca Totem." );
			var seed = WorldSeed.Default;
			World = new RideScriptWorld( seed.ScriptStream );
			// The placement path: open false, the seed drawn from the world's stream (the prototype is placed without one).
			Totem = new OriginalObjectRuntime( Entry, World, seed: null, open: false );
			Totem.Open();
			Settings = GuestSettings.Load( "jungle" );
			Grid = GuestPathGrid.FromOriginal( Park.Map, Park.Save );
			Simulation = new GuestSimulation( Grid, Settings, guestSeed ) { ArrivalsEnabled = false };
			Totem.Visitors.EntranceCell = Totem.Visitors.ExitCell = (39, 21);
			Totem.Visitors.HasCells = true;
			Simulation.Register( Totem.Visitors );
			GuestTests.LayStandCell( Grid, Totem.Visitors );
			foreach ( var (x, y) in new[] { (39, 22), (39, 23), (40, 21), (41, 21), (39, 24), (42, 21), (39, 25), (43, 21) } )
				Simulation.SpawnInPark( x, y );
			for ( var tick = 0; tick < ticks; tick++ )
			{
				// Level.AdvanceSimulation's order: the guests first, then the objects (HostStep, script, animator).
				Simulation.Tick( Tick );
				Totem.Simulate( Tick );
				MaximumRiders = Math.Max( MaximumRiders, Totem.Visitors.Riders.Count );
			}
		}
	}

	[TestMethod]
	public void TheTotemSavesThroughTheCodecIntoAFreshRuntimeAndContinuesDeterministically()
	{
		var rig = new TotemRig( ticks: 90 * 60 );
		Assert.AreNotEqual( RideVMState.Faulted, rig.Totem.Script!.State, rig.Totem.Script.FaultMessage );
		Assert.IsTrue( rig.MaximumRiders > 0, "guests boarded and rode the Totem through its real script" );

		// The save: the prototype part, the script world (the Totem's VM) and the guests, all through the codec.
		var part = ParkResumeJson.Deserialize<PrototypeRideSaveData>( ParkResumeJson.Serialize(
			PrototypeRideSave.Capture( rig.Totem, rig.Position, rig.Totem.IsOpen, economyInstance: null, economyBought: false ) ) );
		var scripts = ParkResumeJson.Deserialize<RideScriptWorldSnapshot>( ParkResumeJson.Serialize( rig.World.CaptureSnapshot() ) );
		var guests = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ) );
		Assert.AreNotEqual( 0, part.RootScriptId, "the Totem's live script is claimed as a world root" );
		Assert.AreEqual( ((39, 21)), part.EntranceCell, "the part carries the bridge's guest cells" );
		Assert.AreEqual( ((39, 21)), part.ExitCell, "the part carries the bridge's exit cell" );

		// The candidate: the restore order of a resumed level — the fresh runtime (saved id, saved seed, no
		// allocation), its bridge registered with fresh guests through its saved cells, the script world over the
		// roots, the guests last.
		var candidateWorld = new RideScriptWorld( WorldSeed.Default.ScriptStream );
		var candidate = OriginalObjectRuntime.CreateForRestore( rig.Entry, candidateWorld, part.ScriptSeed, part.Open, part.AttractionId );
		var gaps = candidate.RestoreSnapshot( part.Runtime );
		Assert.AreEqual( 0, gaps.Count, $"no restore gap: {string.Join( "; ", gaps )}" );
		var candidateGuests = new GuestSimulation( GuestPathGrid.FromOriginal( rig.Park.Map, rig.Park.Save ), rig.Settings, 99 ) { ArrivalsEnabled = false };
		Assert.IsTrue( PrototypeRideSave.RestoreGuestCells( part, candidate.Visitors, candidateGuests ), "the saved cells register the bridge" );
		GuestTests.LayStandCell( candidateGuests.Grid, candidate.Visitors );
		Assert.AreEqual( ((39, 21)), (candidate.Visitors.EntranceCell.X, candidate.Visitors.EntranceCell.Y), "the bridge queues at the saved cell" );
		candidateWorld.RestoreSnapshot( scripts, new Dictionary<int, RideVM> { [part.RootScriptId] = candidate.Script! } );
		candidateGuests.RestoreSnapshot( guests );

		ulong HashOf( GuestSimulation simulation, RideScriptWorld world ) =>
			WorldStateHash.Compute( new WorldStateSources { Guests = simulation, Scripts = world } );
		Assert.AreEqual( HashOf( rig.Simulation, rig.World ), HashOf( candidateGuests, candidateWorld ),
			"the reconstructed park holds the persisted Totem, guests and script state at the boundary" );
		// Deterministic continuation: the same ticks move both parks alike.
		for ( var tick = 0; tick < 600; tick++ )
		{
			rig.Simulation.Tick( Tick );
			rig.Totem.Simulate( Tick );
			candidateGuests.Tick( Tick );
			candidate.Simulate( Tick );
		}
		Assert.AreEqual( HashOf( rig.Simulation, rig.World ), HashOf( candidateGuests, candidateWorld ), "the continuation is deterministic" );
	}

	[TestMethod]
	public void ATotemWhoseNearestPathChangedAfterPlacementResumesItsOwnEntrance()
	{
		// The regression the guest cells exist for: the live park derives the Totem's entrance once, at placement,
		// and never re-derives it (RecomputeQueue keeps the queue's front at that cell through every later grid
		// edit). A path built nearer the Totem afterwards changes what a fresh derivation over the saved grid would
		// pick — re-deriving on resume resumed a different park than the one saved (a boundary hash mismatch) or,
		// once the guest cache had caught up, made the restore refuse the park's own save at the queue-geometry
		// check ("Attraction N's queue does not match the saved cell map"). The part carries the cells verbatim.
		var park = OriginalPark.Load( "jungle" );
		var grid = GuestPathGrid.FromOriginal( park.Map, park.Save );
		int BoxDistance( int x, int y, int cx, int cy ) =>
			Math.Max( 0, Math.Max( x - 2 - cx, cx - (x + 2) ) ) + Math.Max( 0, Math.Max( y - 2 - cy, cy - (y + 2) ) );
		(int X, int Y) site = default;
		var best = -1;
		for ( var y = 2; y < grid.CountY - 2; y += 3 )
			for ( var x = 2; x < grid.CountX - 2; x += 3 )
			{
				if ( OriginalParkPlacement.Check( park, x - 2, y - 2, x + 2, y + 2 ) != OriginalPlacementResult.Allowed )
					continue;
				var nearest = int.MaxValue;
				for ( var cy = 0; cy < grid.CountY && nearest > 12; cy++ )
					for ( var cx = 0; cx < grid.CountX; cx++ )
						if ( grid.IsWalkable( cx, cy ) )
							nearest = Math.Min( nearest, BoxDistance( x, y, cx, cy ) );
				if ( nearest > best ) { best = nearest; site = (x, y); }
			}
		Assert.IsTrue( best >= 4, $"find a Totem site at least 4 cells from a walkable cell (best {best})" );
		var position = OriginalParkPlacement.GetCellCenter( park.Heightfield, site.X, site.Y );
		var live = Level.FindRideEntrance( grid, site.X - 2, site.Y - 2, site.X + 2, site.Y + 2 )!.Value;

		// The live rig: the Totem registered with the placement-time derivation, guests walking near it.
		var world = new RideScriptWorld( WorldSeed.Default.ScriptStream );
		var entry = ObjectCatalog.Load( "jungle" ).Find( PrototypeRide.InfoId )!;
		var totem = new OriginalObjectRuntime( entry, world, seed: null, open: false );
		totem.Open();
		var simulation = new GuestSimulation( grid, GuestSettings.Load( "jungle" ), 99 ) { ArrivalsEnabled = false };
		Assert.IsTrue( PrototypeRideSave.RestoreGuestCells( new PrototypeRideSaveData
		{
			AttractionId = totem.AttractionId, PositionX = position.X, PositionY = position.Y, PositionZ = position.Z,
			Open = true, ScriptSeed = 0, RootScriptId = 0, Runtime = totem.CaptureSnapshot(),
			EntranceCell = live, ExitCell = live
		}, totem.Visitors, simulation ), "the placement's registration shape" );
		foreach ( var index in new[] { 0, 1, 2 } )
			simulation.SpawnInPark( live.X, live.Y );
		for ( var tick = 0; tick < 120; tick++ )
		{
			simulation.Tick( Tick );
			totem.Simulate( Tick );
		}

		// The player builds a path right beside the Totem — nearer than the cell the placement derived.
		var fresh = (X: site.X, Y: site.Y + 3);
		Assert.IsFalse( grid.IsWalkable( fresh.X, fresh.Y ), "the fresh cell was not walkable before the edit" );
		grid.Cells.SetType( fresh.X, fresh.Y, ParkCellType.Path );
		grid.Cells.Touch();
		for ( var tick = 0; tick < 3; tick++ )
		{
			simulation.Tick( Tick );
			totem.Simulate( Tick );
		}
		Assert.AreEqual( (live.X, live.Y), (totem.Visitors.EntranceCell.X, totem.Visitors.EntranceCell.Y),
			"the live park keeps its placement-time entrance after the edit" );
		Assert.AreNotEqual( live, Level.FindRideEntrance( grid, site.X - 2, site.Y - 2, site.X + 2, site.Y + 2 ),
			"a fresh derivation over the edited grid would now pick a different cell" );

		// The save and the restore, both through the codec and the level's own steps.
		var part = ParkResumeJson.Deserialize<PrototypeRideSaveData>( ParkResumeJson.Serialize(
			PrototypeRideSave.Capture( totem, position, totem.IsOpen, economyInstance: null, economyBought: false ) ) );
		var guests = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( simulation.CaptureSnapshot() ) );
		var layout = ParkLayoutSnapshot.CaptureSnapshot( grid.Cells,
			new ParkObjects( ObjectCatalog.Load( "jungle" ), new OriginalParkGrid( park ), WorldSeed.Default ) );
		Assert.AreEqual( live, part.EntranceCell, "the part carries the live entrance, not a re-derivation" );
		var restoredGrid = new GuestPathGrid( ParkLayoutSnapshot.RestoreCellMap( layout ) );
		var candidate = OriginalObjectRuntime.CreateForRestore( entry, new RideScriptWorld( WorldSeed.Default.ScriptStream ),
			part.ScriptSeed, part.Open, part.AttractionId );
		Assert.AreEqual( 0, candidate.RestoreSnapshot( part.Runtime ).Count, "the runtime restores without gaps" );
		var candidateGuests = new GuestSimulation( restoredGrid, GuestSettings.Load( "jungle" ), 99 ) { ArrivalsEnabled = false };
		Assert.IsTrue( PrototypeRideSave.RestoreGuestCells( part, candidate.Visitors, candidateGuests ), "the saved cells register the restored bridge" );

		Level.ValidateResumeQueueGeometry( guests, candidateGuests );
		candidateGuests.RestoreSnapshot( guests );
		ulong Hash( GuestSimulation sim ) => WorldStateHash.Compute( new WorldStateSources { Guests = sim } );
		Assert.AreEqual( Hash( simulation ), Hash( candidateGuests ), "the resumed park holds the saved entrance, not the fresh derivation" );
		for ( var tick = 0; tick < 120; tick++ )
		{
			simulation.Tick( Tick );
			totem.Simulate( Tick );
			candidateGuests.Tick( Tick );
			candidate.Simulate( Tick );
		}
		Assert.AreEqual( Hash( simulation ), Hash( candidateGuests ), "the continuation is deterministic" );
	}

	[TestMethod]
	public void ARealPrototypeEnvelopeValidatesAndDoctoredPartsAreRefused()
	{
		var rig = new TotemRig( ticks: 1800 );
		var catalog = ObjectCatalog.Load( "jungle" );
		var settings = BalanceSettings.Load( "jungle", false );
		var economyCatalog = EconomyObjectCatalog.Load( "jungle", false );
		Assert.IsTrue( economyCatalog.TryGet( PrototypeRide.InfoId, out var totemInfo ),
			"the jungle economy lists the Totem (the prototype buys it like any object)" );
		var economy = new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( new[] { totemInfo! } ), ParkGameMode.FullSimulation, 7 );
		var instance = economy.RegisterExisting( PrototypeRide.InfoId ).Id;
		var guests = rig.Simulation.CaptureSnapshot();
		var scripts = rig.World.CaptureSnapshot();
		string Hash( string path )
		{
			using var stream = rig.Entry.FileSystem.OpenRead( path );
			return ParkResumeAssets.Sha256( stream );
		}
		var manifest = new Dictionary<string, string>
		{
			[$"script:{rig.Entry.ScriptPath}"] = Hash( rig.Entry.ScriptPath! ),
			[$"model:{rig.Entry.ModelPath}"] = Hash( rig.Entry.ModelPath )
		};
		var snapshot = new ParkResumeSnapshot
		{
			Format = ParkResumeSaveFile.FormatName,
			Version = ParkResumeSaveFile.CurrentVersion,
			Theme = "jungle",
			StartKind = ParkStartKind.FullSimulation,
			Seed = WorldSeed.Default.Value,
			DisplayName = "Totem park",
			SavedUtc = new DateTimeOffset( 2026, 10, 13, 12, 0, 0, TimeSpan.Zero ),
			Economy = ParkSaveFile.Capture( economy, new WorldRandomState( WorldSeed.Default.Value, (uint)guests.RandomState, 42u ) ),
			Layout = ParkLayoutSnapshot.CaptureSnapshot( rig.Grid.Cells, new ParkObjects( catalog, new OriginalParkGrid( rig.Park ), WorldSeed.Default ) ) with
			{
				Objects = new List<ParkLayoutSnapshot.PlacedObjectData>(),
				NextSeed = 2,
				NextAttractionId = 2
			},
			Guests = guests,
			Scripts = scripts with { NextAttractionId = 2, RandomState = guests.RandomState, WorldGenerator = guests.WorldGenerator },
			Runtimes = new List<ParkResumeRuntime>(),
			EconomyBindings = new List<ParkResumeEconomyBinding>(),
			Clock = new FixedStepClockSnapshot( 1800, 0, 0 ),
			SoundSeed = 42,
			Prototype = PrototypeRideSave.Capture( rig.Totem, rig.Position, rig.Totem.IsOpen, instance, economyBought: false ),
			AssetHashes = manifest
		};
		ParkResumeSaveFile.ValidateSnapshot( snapshot, catalog, settings, economyCatalog, manifest );
		var restored = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( snapshot ) );
		ParkResumeSaveFile.ValidateSnapshot( restored, catalog, settings, economyCatalog, manifest );
		Assert.AreEqual( instance, restored.Prototype!.EconomyInstanceId, "the economy association survives the codec" );

		void Rejects( ParkResumeSnapshot corrupted, string message ) =>
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>(
				() => ParkResumeSaveFile.ValidateSnapshot( corrupted, catalog, settings, economyCatalog, manifest ) ).Message, message );
		var part = snapshot.Prototype!;
		// Dropping the part is refused at the first join: with the guests' attraction record and the Totem's root
		// script both still saved, the part is what claims them.
		Rejects( snapshot with { Prototype = null }, "the script world holds 1 root scripts, but 0 live objects name one" );
		Rejects( snapshot with { Prototype = part with { AttractionId = 2, Runtime = part.Runtime with { AttractionId = 2 } } },
			"outside the saved allocator (1..2)" );
		Rejects( snapshot with { Prototype = part with { Runtime = part.Runtime with { EntryInfoId = 1100 } } }, "not the Inca Totem" );
		Rejects( snapshot with { Prototype = part with { PositionZ = float.PositiveInfinity } }, "not finite or outside the map-scale bound" );
		Rejects( snapshot with { Prototype = part with { RootScriptId = part.RootScriptId + 99 } }, "which the world does not hold as a root" );
		Rejects( snapshot with { Prototype = part with { ScriptSeed = part.ScriptSeed + 1 } }, "not the part's" );
		Rejects( snapshot with { Prototype = part with { EconomyInstanceId = instance + 99 } }, "which the economy does not hold" );
		Rejects( snapshot with { Prototype = part with { Runtime = part.Runtime with { AttractionId = 5 } } }, "its runtime is attraction 5" );
		Rejects( snapshot with { Prototype = part with { EntranceCell = (snapshot.Layout.Cells.Width * 4, 0), ExitCell = (snapshot.Layout.Cells.Width * 4, 0) } },
			"entrance cell" );
		Rejects( snapshot with { Prototype = part with { EntranceCell = null, ExitCell = null, EconomyInstanceId = null } },
			"carries no guest cells" );
		// the guests' record of the Totem keeps the cells too, and the two agree
		var record = snapshot.Guests.Attractions.Single( attraction => attraction.AttractionId == part.AttractionId );
		Assert.AreEqual( (part.EntranceCell, part.ExitCell), (record.EntranceCell, record.ExitCell) );
		Rejects( snapshot with { Prototype = part with { EntranceCell = (part.EntranceCell!.Value.X + 1, part.EntranceCell.Value.Y) } }, "name different guest cells" );
	}
}
