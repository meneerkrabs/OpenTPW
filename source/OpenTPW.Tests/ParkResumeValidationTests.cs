using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The world-save envelope's validation (save/load milestone). Part one is pure shape, no game data: every
/// join the envelope owns — header, embedded economy, layout allocators, script-world roots, one runtime per
/// layout object, attraction-to-economy-instance associations, guest cross-references, optional staff/advisor
/// parts — rejects a corrupt mutation with a reason while accepting the honest base line. Part two (skips
/// without OPENTPW_GAME_PATH) validates a real Jungle envelope against the real catalog, balance and economy
/// catalogue: two Drinks Shops with the same Info.Id but distinct prices stay distinct (no first-Info.Id
/// re-derivation), the asset manifest must agree exactly, and a coaster entry is refused explicitly.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParkResumeValidationTests
{
	private static void Rejects( ParkResumeSnapshot corrupted, string message )
	{
		var exception = Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( corrupted ) );
		StringAssert.Contains( exception.Message, message );
	}

	[TestMethod]
	public void ShapeValidationAcceptsTheBaseLineAndRejectsCorruptHeaders()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		ParkResumeSaveFile.ValidateShape( snapshot ); // the base line is accepted
		Rejects( snapshot with { Version = 7 }, "version 7" );
		Rejects( snapshot with { Format = "opentpw-park" }, "the format is 'opentpw-park'" );
		Rejects( snapshot with { Theme = "saturn" }, "not a Theme Park World theme" );
		Rejects( snapshot with { Theme = "jungle " }, "not a Theme Park World theme" );
		Rejects( snapshot with { StartKind = (ParkStartKind)99 }, "start kind 99" );
		Rejects( snapshot with { StartKind = ParkStartKind.InstantAction }, "cannot run in economy mode FullSimulation" );
		Rejects( snapshot with { DisplayName = new string( 'x', ParkResumeSnapshot.MaximumDisplayNameLength + 1 ) }, $"above {ParkResumeSnapshot.MaximumDisplayNameLength}" );
		Rejects( snapshot with { DisplayName = "bad\u0001name" }, "control characters" );
		Rejects( snapshot with { DisplayName = "" }, "display name is empty" );
		// OriginalSaveReference keeps Full Simulation rules, so it is accepted with this economy.
		ParkResumeSaveFile.ValidateShape( snapshot with { StartKind = ParkStartKind.OriginalSaveReference } );
	}

	[TestMethod]
	public void ShapeValidationRejectsMissingPartsAndBrokenClocks()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		Rejects( snapshot with { Economy = null! }, "economy snapshot is missing" );
		Rejects( snapshot with { Layout = null! }, "layout snapshot is missing" );
		Rejects( snapshot with { Guests = null! }, "guest snapshot is missing" );
		Rejects( snapshot with { Scripts = null! }, "script-world snapshot is missing" );
		Rejects( snapshot with { Runtimes = null! }, "runtime list is missing" );
		Rejects( snapshot with { Runtimes = new List<ParkResumeRuntime> { null! } }, "runtime 0 is missing" );
		Rejects( snapshot with { EconomyBindings = null! }, "economy-binding list is missing" );
		Rejects( snapshot with { Clock = null! }, "clock snapshot is missing" );
		Rejects( snapshot with { AssetHashes = null! }, "asset manifest is missing" );
		Rejects( snapshot with { AssetHashes = new Dictionary<string, string>() }, "asset manifest is empty" );
		Rejects( snapshot with { AssetHashes = new Dictionary<string, string> { ["asset"] = "not-a-digest" } }, "64 lowercase hex" );
		Rejects( snapshot with { AssetHashes = new Dictionary<string, string> { ["asset"] = new string( 'A', 64 ) } }, "64 lowercase hex" );
		Rejects( snapshot with { Clock = new FixedStepClockSnapshot( -1, 0, 0 ) }, "negative tick count" );
		Rejects( snapshot with { Clock = new FixedStepClockSnapshot( 0, double.NaN, 0 ) }, "pending" );
		Rejects( snapshot with { Clock = new FixedStepClockSnapshot( 0, 0, -1 ) }, "dropped seconds" );
	}

	[TestMethod]
	public void ShapeValidationRejectsBrokenLayoutIdentityAndRuntimeJoins()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		var key = snapshot.Layout.ObjectScriptKey;
		var record = snapshot.Layout.Objects[0];
		Rejects( snapshot with { Seed = 123456 }, "does not derive from world seed" );
		Rejects( snapshot with { Layout = snapshot.Layout with { ObjectScriptKey = key + 1 } }, "does not derive from world seed" );
		Rejects( snapshot with { Layout = snapshot.Layout with { NextAttractionId = 3 } }, "is not the script world's 2" );
		Rejects( snapshot with { Layout = snapshot.Layout with { NextAttractionId = snapshot.Scripts.NextAttractionId + 1 } }, "is not the script world's" );
		Rejects( snapshot with { Layout = snapshot.Layout with { Objects = new List<ParkLayoutSnapshot.PlacedObjectData> { record with { AttractionId = 2 } } } },
			"outside the saved allocator" );
		Rejects( snapshot with { Layout = snapshot.Layout with { Objects = new List<ParkLayoutSnapshot.PlacedObjectData> { record with { CreationSeed = 5 ^ key } } } },
			"placement" );
		Rejects( snapshot with { Scripts = snapshot.Scripts with { NextScriptId = 0 } }, "are not positive" );
		Rejects( snapshot with { Scripts = snapshot.Scripts with { ClockMilliseconds = double.NaN } }, "not finite" );

		Rejects( snapshot with { Runtimes = Array.Empty<ParkResumeRuntime>() }, "runtime snapshots" );
		Rejects( snapshot with { Runtimes = new List<ParkResumeRuntime> { new( 0, ParkResumeSaveFileTests.RuntimeSnapshot( 2, 1100, record.Archive ) ) } },
			"not the layout's 1" );
		Rejects( snapshot with { Runtimes = new List<ParkResumeRuntime> { new( 0, ParkResumeSaveFileTests.RuntimeSnapshot( record.AttractionId, 1101, record.Archive ) ) } },
			"not the layout's 1100" );
		Rejects( snapshot with { Runtimes = new List<ParkResumeRuntime> { new( 0, ParkResumeSaveFileTests.RuntimeSnapshot( record.AttractionId, 1100, "/levels/jungle/rides/other.wad" ) ) } },
			$"not the layout's '{record.Archive}'" );
		Rejects( snapshot with { Runtimes = new List<ParkResumeRuntime> { new( 3, snapshot.Runtimes[0].State ) } }, "names root script 3" );
	}

	[TestMethod]
	public void ShapeValidationRejectsBadEconomyAssociationsAndWorldStreams()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		Rejects( snapshot with { EconomyBindings = new List<ParkResumeEconomyBinding> { new( 1, 999 ) } }, "which the economy does not hold" );
		Rejects( snapshot with { EconomyBindings = new List<ParkResumeEconomyBinding> { new( 999, 1 ) } }, "which the park does not place" );
		Rejects( snapshot with { EconomyBindings = new List<ParkResumeEconomyBinding> { new( 1, 1 ), new( 1, 2 ) } }, "attraction 1 is bound twice" );
		Rejects( snapshot with { EconomyBindings = new List<ParkResumeEconomyBinding> { new( 0, 1 ) } }, "maps attraction 0 to instance 1" );

		// A guest stream that disagrees with the typed fields is rejected even when everything else is honest.
		var wrongGuests = ParkSaveFile.Capture( EconomyTestData.Park(), new WorldRandomState( WorldSeed.Default.Value, 999, snapshot.Scripts.RandomState, snapshot.SoundSeed ) );
		Rejects( snapshot with { Economy = wrongGuests, EconomyBindings = new List<ParkResumeEconomyBinding>() }, "guest stream" );
		var wrongScripts = ParkSaveFile.Capture( EconomyTestData.Park(), new WorldRandomState( WorldSeed.Default.Value, snapshot.Guests.RandomState, 888, snapshot.SoundSeed ) );
		Rejects( snapshot with { Economy = wrongScripts, EconomyBindings = new List<ParkResumeEconomyBinding>() }, "script stream" );
		var wrongSeed = ParkSaveFile.Capture( EconomyTestData.Park(), new WorldRandomState( 7, snapshot.Guests.RandomState, snapshot.Scripts.RandomState, snapshot.SoundSeed ) );
		Rejects( snapshot with { Economy = wrongSeed, EconomyBindings = new List<ParkResumeEconomyBinding>() }, "world seed" );
		Rejects( snapshot with { SoundSeed = 43 }, "sound seed" );
	}

	[TestMethod]
	public void ShapeValidationRejectsInconsistentGuestSnapshots()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		Rejects( snapshot with { Guests = snapshot.Guests with { SchemaVersion = 9 } }, "schema version" );
		Rejects( snapshot with { Guests = snapshot.Guests with { SeenQueueEdits = Array.Empty<(int AttractionId, int SeenEditCount)>() } }, "no seen queue-edit counter" );
		Rejects( snapshot with
		{
			Guests = snapshot.Guests with { Attractions = snapshot.Guests.Attractions.Select( attraction => attraction with { AttractionId = 77 } ).ToArray() }
		}, "but the park does not place it" );
		Rejects( snapshot with
		{
			Guests = snapshot.Guests with { Attractions = snapshot.Guests.Attractions.Select( attraction => attraction with { QueueCells = new[] { (99, 99) } } ).ToArray() }
		}, "queue cell (99, 99) is outside" );
		Rejects( snapshot with { Guests = snapshot.Guests with { NextGuestId = 1 } }, "not below the next guest id" );

		var first = snapshot.Guests.Guests[0];
		Rejects( snapshot with { Guests = snapshot.Guests with { Guests = new[] { first, first } } }, "does not follow guest" );
		Rejects( snapshot with { Guests = snapshot.Guests with { Guests = new[] { first with { State = GuestState.Gone } } } }, "already gone" );
		Rejects( snapshot with { Guests = snapshot.Guests with { Guests = new[] { first with { State = (GuestState)3 } } } }, "is not a guest state" );
		Rejects( snapshot with { Guests = snapshot.Guests with { Guests = new[] { first with { BaseSpeed = 200 } } } }, "base speed" );
		Rejects( snapshot with { Guests = snapshot.Guests with { Guests = new[] { first with { CellX = 500 } } } }, "cell (500" );

		// A guest that names an attraction no queue serves.
		var walker = snapshot.Guests.Guests.First( guest => guest.AttractionId == 0 );
		Rejects( snapshot with { Guests = snapshot.Guests with { Guests = snapshot.Guests.Guests.Select( guest => guest == walker ? guest with { AttractionId = 77 } : guest ).ToArray() } },
			"names attraction 77" );

		// Queue membership must agree with state both ways, whatever the states happen to be.
		var onGrid = snapshot.Guests.Guests.First( guest => guest.OnGrid && guest.State == GuestState.GoingToRide );
		Rejects( snapshot with { Guests = snapshot.Guests with { Guests = snapshot.Guests.Guests.Select( guest => guest == onGrid ? guest with { State = GuestState.Queueing } : guest ).ToArray() } },
			"is in state Queueing but no attraction's queue holds it" );
		var planted = snapshot.Guests.Attractions[0] with { Queue = snapshot.Guests.Attractions[0].Queue.Append( onGrid.Id ).ToArray() };
		Rejects( snapshot with { Guests = snapshot.Guests with { Attractions = new[] { planted } } },
			"is in state GoingToRide but is still held in a queue" );
		var ghost = snapshot.Guests.Attractions[0] with { Queue = snapshot.Guests.Attractions[0].Queue.Append( 4242 ).ToArray() };
		Rejects( snapshot with { Guests = snapshot.Guests with { Attractions = new[] { ghost }, SeenQueueEdits = new[] { (ghost.AttractionId, 0) } } },
			"holds guest 4242, which does not exist" );
	}

	[TestMethod]
	public void ShapeValidationChecksOptionalParts()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		Rejects( snapshot with { Gate = new ParkGate.ParkGateSnapshot( -5 ) }, "negative" );
		Rejects( snapshot with { Advisor = Advisor( speechWasActive: true ) }, "active speech cannot be restored" );
		ParkResumeSaveFile.ValidateShape( snapshot with { Advisor = Advisor( speechWasActive: false ) } ); // accepted while silent
		ParkResumeSaveFile.ValidateShape( snapshot with { Gate = new ParkGate.ParkGateSnapshot( 1_000_000 ) } );

		Rejects( snapshot with { Weather = Weather( started: true, quality: 500 ) }, "outside 1..100" );
		Rejects( snapshot with { Weather = Weather( started: false, quality: 3 ) }, "never started but is not pristine" );
		Rejects( snapshot with { Weather = Weather( started: true, quality: 50 ) with { NextRollDay = 4, LastChanged = 9 } }, "next roll precedes" );
		ParkResumeSaveFile.ValidateShape( snapshot with { Weather = Weather( started: true, quality: 50 ) } ); // a started, honest forecast is accepted
	}

	private static AutomaticAdvisor.AutomaticAdvisorSnapshot Advisor( bool speechWasActive ) => new(
		new AdvisorController.AdvisorControllerSnapshot( new AdvisorQueueSnapshot(
			new AdvisorPendingAdviceSnapshot?[ParkScoreQueueCapacity()], new Dictionary<int, AdvisorHistorySnapshot>(), 0, 0 ) ),
		0, speechWasActive );

	private static int ParkScoreQueueCapacity() => 8; // AdvisorScoreQueue.Capacity: eight pending records.

	private static ParkWeather.ParkWeatherSnapshot Weather( bool started, int quality ) => new()
	{
		RandomState = 1,
		Started = started,
		LastTurn = 9,
		LastChanged = 9,
		NextRollDay = 40,
		ApplyDay = 45,
		Quality = quality,
		Snow = false,
		Rain = false,
		Lightning = false,
		CurrentDrops = 0,
		TargetDrops = 0,
		LightningCountdown = 0,
		Bolts = 0,
		ThunderCountdown = 0,
		LastTop = new ParkWeather.ThunderStrikeSnapshot( 1, 2, 3 ),
		PendingStrikes = Array.Empty<ParkWeather.ThunderStrikeSnapshot>()
	};

	[TestMethod]
	public void StaffActorsMustMatchTheEconomyExactly()
	{
		var park = EconomyTestData.Park();
		var member = EconomyTestData.HireBest( park, StaffType.Mechanic );
		var snapshot = ParkResumeSaveFileTests.Envelope();
		// The hired park replaces the economy (its world streams are the fixture's; it holds no placed objects).
		var staffed = snapshot with
		{
			Economy = ParkSaveFile.Capture( park, ParkSaveFile.ToWorldState( snapshot.Economy ) ),
			EconomyBindings = new List<ParkResumeEconomyBinding>()
		};
		// The actor DTO is the world-side authority: identity, position and travel remainder must equal the economy's.
		var actor = new StaffWorldSimulation.StaffActorSnapshot( member.Id, member.CellX, member.CellY, member.TravelSeconds );
		ParkResumeSaveFile.ValidateShape( staffed with { Staff = new StaffWorldSimulation.StaffWorldStateSnapshot( new[] { actor } ) } );
		Rejects( staffed with { Staff = new StaffWorldSimulation.StaffWorldStateSnapshot( new[] { actor with { CellX = 9f, CellY = 9f } } ) }, "in the world, but" );
		Rejects( staffed with { Staff = new StaffWorldSimulation.StaffWorldStateSnapshot( new[] { actor with { TravelSeconds = 0.5 } } ) }, "in the world, but" );
		Rejects( staffed with { Staff = new StaffWorldSimulation.StaffWorldStateSnapshot( new[] { new StaffWorldSimulation.StaffActorSnapshot( 999, null, null, 0 ) } ) }, "whom the economy does not employ" );
		Rejects( staffed with { Staff = new StaffWorldSimulation.StaffWorldStateSnapshot( Array.Empty<StaffWorldSimulation.StaffActorSnapshot>() ) }, "the economy employs" );
	}
}

/// <summary>
/// Full <see cref="ParkResumeSaveFile.ValidateSnapshot"/> against the real Jungle game data (inconclusive
/// without OPENTPW_GAME_PATH): two Drinks Shops with one Info.Id but distinct prices and rotations save and restore
/// as distinct instances (a first-Info.Id re-derivation would swap them), the asset manifest must match the
/// park's current assets exactly, the balance layer and catalog theme must agree, and coaster entries are
/// refused explicitly instead of silently losing their state.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParkResumeCatalogValidationTests
{
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the world-save envelope corpus." );
		originalFileSystem = previous;
		initialized = true;
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
		ObjectCatalog.BonusDataRoot = null;
	}

	/// <summary>The accepted base line: a real Jungle envelope whose every part is real captured state.</summary>
	private sealed class JungleEnvelope
	{
		public readonly ObjectCatalog Catalog = ObjectCatalog.Load( "jungle" );
		public readonly BalanceSettings Settings = BalanceSettings.Load( "jungle", false );
		public readonly EconomyObjectCatalog EconomyCatalog = EconomyObjectCatalog.Load( "jungle", false );
		public readonly ObjectCatalogEntry Shop;
		public readonly int SideshowInstanceId;
		public readonly IReadOnlyDictionary<string, string> Manifest;
		public readonly ParkResumeSnapshot Snapshot;

		public JungleEnvelope()
		{
			var park = OriginalPark.Load( "jungle" );
			var grid = new OriginalParkGrid( park );
			var seed = WorldSeed.Default;
			var objects = new ParkObjects( Catalog, grid, seed );
			Shop = Catalog.Get( 1203 );
			ParkLayoutSnapshot.PlacedObjectData Record( int ordinal, int x, int y, int rotation ) => new()
			{
				InfoId = Shop.InfoId,
				Archive = Shop.ArchivePath,
				X = x,
				Y = y,
				Rotation = rotation,
				Open = true,
				CreationSeed = ordinal ^ seed.ObjectScriptKey,
				AttractionId = ordinal,
				BaseHeight = objects.GetBaseHeight( Shop, x, y, rotation ),
				IsFixedItem = false
			};

			// Two live shop runtimes in one world: scripts, animators and head slots are real captured state.
			var world = new RideScriptWorld( seed.ScriptStream );
			var first = new OriginalObjectRuntime( Shop, world, seed: 1 ^ seed.ObjectScriptKey, open: true );
			var second = new OriginalObjectRuntime( Shop, world, seed: 2 ^ seed.ObjectScriptKey, open: false );
			for ( var tick = 0; tick < 90; tick++ )
			{
				first.Simulate( ParkResumeSaveFileTests.MiniaturePark.Tick );
				second.Simulate( ParkResumeSaveFileTests.MiniaturePark.Tick );
			}

			// The economy: two same-Info.Id instances at distinct prices, plus a sideshow of another Info.Id for the cross-check.
			var testCatalog = new EconomyObjectCatalog( new[]
			{
				new EconomyObjectInfo( Shop.InfoId, "Drinks Shop", ParkObjectKind.Shop, ResearchCategory.Shop, 0,
					new[] { new UpgradeLevelInfo( 0, 500, 0, 0, 0, new[] { 50, 30, 20, 10 } ) }, 30, 20, null, 4, 3, 0, 0, 0, 50, Shop.ArchivePath ),
				new EconomyObjectInfo( 1303, "Jungle Spray", ParkObjectKind.Sideshow, ResearchCategory.Sideshow, 0,
					new[] { new UpgradeLevelInfo( 0, 1750, 0, 0, 0, new[] { 50, 30, 20, 10 } ) }, 20, 50, 70, 0, 0, 0, 0, 0, 0, "sideshow/junspray.wad" )
			} );
			var economy = new ParkEconomy( EconomyTestData.Settings(), testCatalog, ParkGameMode.FullSimulation, 7 );
			var shopOne = economy.RegisterExisting( Shop.InfoId );
			var shopTwo = economy.RegisterExisting( Shop.InfoId );
			var sideshow = economy.RegisterExisting( 1303 );
			SideshowInstanceId = sideshow.Id;
			economy.SetPrices( shopOne.Id, 100 );
			economy.SetPrices( shopTwo.Id, 250 );

			// Guests: the miniature park's one attraction is attraction 1, the first shop.
			var miniature = new ParkResumeSaveFileTests.MiniaturePark();
			for ( var tick = 0; tick < 1800; tick++ )
				miniature.Step();
			var guests = miniature.Simulation.CaptureSnapshot();
			var scripts = world.CaptureSnapshot();
			var layout = ParkLayoutSnapshot.CaptureSnapshot( ParkCellMap.FromOriginal( park.Map, park.Save ), objects ) with
			{
				Objects = new List<ParkLayoutSnapshot.PlacedObjectData> { Record( 1, 20, 62, 0 ), Record( 2, 40, 62, 90 ) },
				NextSeed = 3,
				NextAttractionId = 3
			};

			string Hash( string path )
			{
				using var stream = Shop.FileSystem.OpenRead( path );
				return ParkResumeAssets.Sha256( stream );
			}
			Manifest = new Dictionary<string, string>
			{
				[$"script:{Shop.ScriptPath}"] = Hash( Shop.ScriptPath! ),
				[$"model:{Shop.ModelPath}"] = Hash( Shop.ModelPath )
			};
			Snapshot = new ParkResumeSnapshot
			{
				Format = ParkResumeSaveFile.FormatName,
				Version = ParkResumeSaveFile.CurrentVersion,
				Theme = "jungle",
				StartKind = ParkStartKind.FullSimulation,
				Seed = seed.Value,
				DisplayName = "Drinks park",
				SavedUtc = new DateTimeOffset( 2026, 10, 12, 12, 0, 0, TimeSpan.Zero ),
				Economy = ParkSaveFile.Capture( economy, new WorldRandomState( seed.Value, guests.RandomState, scripts.RandomState, 42u ) ),
				Layout = layout,
				Guests = guests,
				Scripts = scripts,
				Runtimes = new List<ParkResumeRuntime>
				{
					new( first.Script!.ScriptId, first.CaptureSnapshot() ),
					new( second.Script!.ScriptId, second.CaptureSnapshot() )
				},
				EconomyBindings = new List<ParkResumeEconomyBinding> { new( 1, shopOne.Id ), new( 2, shopTwo.Id ) },
				Clock = new FixedStepClockSnapshot( 2700, 0, 0 ),
				SoundSeed = 42,
				AssetHashes = Manifest
			};
		}
	}

	[TestMethod]
	public void ValidationAcceptsDuplicateInfoIdsWithDistinctPricesAndRefusesCorruption()
	{
		var fixture = new JungleEnvelope();
		var snapshot = fixture.Snapshot;
		ParkResumeSaveFile.ValidateSnapshot( snapshot, fixture.Catalog, fixture.Settings, fixture.EconomyCatalog, fixture.Manifest );

		// The whole envelope survives the codec and validates again; the duplicate Info.Ids stay distinct.
		var restored = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( snapshot ) );
		ParkResumeSaveFile.ValidateSnapshot( restored, fixture.Catalog, fixture.Settings, fixture.EconomyCatalog, fixture.Manifest );
		var prices = restored.Economy.Objects.OrderBy( item => item.Id ).Take( 2 ).Select( item => item.Price ).ToArray();
		CollectionAssert.AreEqual( new[] { 100, 250 }, prices, "two same-Info.Id instances keep their distinct prices" );

		void Rejects( ParkResumeSnapshot corrupted, string message ) =>
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>(
				() => ParkResumeSaveFile.ValidateSnapshot( corrupted, fixture.Catalog, fixture.Settings, fixture.EconomyCatalog, fixture.Manifest ) ).Message, message );
		// A binding to an instance of another Info.Id is refused even though a matching-Info.Id instance exists:
		// the association is the saved direct mapping, never re-derived by first Info.Id match.
		Rejects( snapshot with { EconomyBindings = new List<ParkResumeEconomyBinding> { new( 1, fixture.SideshowInstanceId ), new( 2, fixture.SideshowInstanceId ) } },
			"Info.Id" );
		Rejects( snapshot with { Runtimes = new List<ParkResumeRuntime> { new( snapshot.Runtimes[0].RootScriptId, snapshot.Runtimes[0].State with { AttractionId = 999 } ), snapshot.Runtimes[1] } },
			"not the layout's 1" );
		Rejects( snapshot with { Runtimes = new List<ParkResumeRuntime> { new( 999, snapshot.Runtimes[0].State ), snapshot.Runtimes[1] } },
			"which the world does not hold as a root" );
		// Claiming the other shop's root fails the creation-seed identity, not just the id map.
		Rejects( snapshot with { Runtimes = new List<ParkResumeRuntime> { new( snapshot.Runtimes[1].RootScriptId, snapshot.Runtimes[0].State ), snapshot.Runtimes[1] } },
			"creation seed" );
		// Theme and balance mismatches.
		Assert.ThrowsException<InvalidDataException>( () =>
			ParkResumeSaveFile.ValidateSnapshot( snapshot, ObjectCatalog.Load( "space" ), fixture.Settings, fixture.EconomyCatalog, fixture.Manifest ) );
		Assert.ThrowsException<InvalidDataException>( () =>
			ParkResumeSaveFile.ValidateSnapshot( snapshot, fixture.Catalog, BalanceSettings.Load( "jungle", true ), fixture.EconomyCatalog, fixture.Manifest ) );
		// The asset manifest must agree exactly: flipped digest, missing and unexpected keys.
		var flipped = new Dictionary<string, string>( fixture.Manifest ) { [$"model:{fixture.Shop.ModelPath}"] = new string( 'b', 64 ) };
		Assert.ThrowsException<InvalidDataException>( () =>
			ParkResumeSaveFile.ValidateSnapshot( snapshot, fixture.Catalog, fixture.Settings, fixture.EconomyCatalog, flipped ) );
		Assert.ThrowsException<InvalidDataException>( () =>
			ParkResumeSaveFile.ValidateSnapshot( snapshot with { AssetHashes = new Dictionary<string, string>( fixture.Manifest ) { [$"model:{fixture.Shop.ModelPath}"] = new string( 'b', 64 ) } },
				fixture.Catalog, fixture.Settings, fixture.EconomyCatalog, fixture.Manifest ) );
		Assert.ThrowsException<InvalidDataException>( () =>
			ParkResumeSaveFile.ValidateSnapshot( snapshot with { AssetHashes = new Dictionary<string, string>( fixture.Manifest ) { ["script:elsewhere.rse"] = new string( 'c', 64 ) } },
				fixture.Catalog, fixture.Settings, fixture.EconomyCatalog, fixture.Manifest ) );
		Assert.ThrowsException<InvalidDataException>( () =>
			ParkResumeSaveFile.ValidateSnapshot( snapshot with { AssetHashes = fixture.Manifest.Where( pair => !pair.Key.StartsWith( "model:", StringComparison.Ordinal ) ).ToDictionary() },
				fixture.Catalog, fixture.Settings, fixture.EconomyCatalog, fixture.Manifest ) );
	}

	[TestMethod]
	public void CoasterEntriesAreRefusedExplicitly()
	{
		var fixture = new JungleEnvelope();
		var coaster = fixture.Catalog.Entries.FirstOrDefault( entry => entry.Settings != null && CoasterDefinition.IsCoaster( entry.Settings ) );
		if ( coaster == null )
			Assert.Inconclusive( "This game data lists no coaster entry." );
		var seed = WorldSeed.Default;
		var objects = fixture.Snapshot.Layout.Objects;
		var record = new ParkLayoutSnapshot.PlacedObjectData
		{
			InfoId = coaster!.InfoId,
			Archive = coaster.ArchivePath,
			X = 20,
			Y = 50,
			Rotation = 0,
			Open = true,
			CreationSeed = 3 ^ seed.ObjectScriptKey,
			AttractionId = 3,
			BaseHeight = 0,
			IsFixedItem = false
		};
		var withCoaster = fixture.Snapshot with
		{
			Layout = fixture.Snapshot.Layout with { Objects = new List<ParkLayoutSnapshot.PlacedObjectData>( objects ) { record }, NextSeed = 4, NextAttractionId = 4 },
			Scripts = fixture.Snapshot.Scripts with { NextAttractionId = 4 },
			Runtimes = new List<ParkResumeRuntime>( fixture.Snapshot.Runtimes ) { new( 0, ParkResumeSaveFileTests.RuntimeSnapshot( 3, coaster.InfoId, coaster.ArchivePath ) ) }
		};
		var exception = Assert.ThrowsException<InvalidDataException>(
			() => ParkResumeSaveFile.ValidateSnapshot( withCoaster, fixture.Catalog, fixture.Settings, fixture.EconomyCatalog, fixture.Manifest ) );
		StringAssert.Contains( exception.Message, "coaster" );
	}
}
