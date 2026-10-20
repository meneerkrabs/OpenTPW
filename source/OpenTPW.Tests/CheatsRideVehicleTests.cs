using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// The ride vehicle cheat of the <c>opentpw.cheats</c> extension (docs/CHEATS.md, phase K4): a bumper car or kart ride raised to 32 vehicles, a tour ride to 40 cars, the capacity maximum that
/// fills them, the saves, the turn boundary and the original-format export's refusal, and the pinned bumper session (<see cref="CheatsBumperRaisedRunHash"/>).
/// </summary>
[TestClass]
[DoNotParallelize]
public class CheatsRideVehicleTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	// ---- Bumper cars ------------------------------------------------------------------------------------

	private static BumpController Bumper( int type = -1, uint seed = 99 )
	{
		var state = seed;
		var controller = new BumpController( BumperTemplates.Get( type ), () => { state = unchecked( state * 1664525u + 1013904223u ); return state >> 8; }, new BumpWorld() );
		controller.SetStation( 5 * 0xC00, 5 * 0xC00, TrackHeading.PosY );
		return controller;
	}

	private static void Run( BumpController controller, int ticks )
	{
		for ( var tick = 0; tick < ticks; tick++ )
			controller.Tick();
	}

	/// <summary>The script's loading (BumperControllerTests): a car per guest through BUMP 4, the guests through BUMP 1 and 12, then the duration and the start.</summary>
	private static void LoadAndStart( BumpController controller, int cars, int seconds )
	{
		controller.Command( 7, 0 );
		for ( var car = 0; car < cars; car++ )
			Assert.AreEqual( 1, controller.Command( 4, 0 ), $"BUMP 4 makes car {car + 1}" );
		for ( var guest = 0; guest < cars; guest++ )
		{
			controller.Command( 1, 100 + guest );
			Assert.AreEqual( 1, controller.Command( 12, 0 ), "BUMP 12 loads the guest" );
		}
		controller.Command( 13, seconds );
		controller.Command( 3, 0 );
	}

	[TestMethod]
	public void ARaisedBumperRideLaunchesThirtyTwoCarsRunsItsSessionAndHandsEveryRiderBack()
	{
		var controller = Bumper();
		Assert.AreEqual( 8, controller.VehicleLimit, "the template's limit" );
		Assert.IsTrue( controller.CanRaiseVehicleLimit );
		Assert.IsTrue( controller.RaiseVehicleLimit() );
		Assert.IsFalse( controller.RaiseVehicleLimit(), "raised once" );
		Assert.AreEqual( 32, controller.VehicleLimit );
		LoadAndStart( controller, 32, 20 );
		Assert.AreEqual( 0, controller.Command( 4, 0 ), "a 33rd car is refused" );
		Assert.AreEqual( 32, controller.RiderCount );
		Assert.IsTrue( controller.Vehicles.All( car => BumpMath.Distance( car.X - 5 * 0xC00, car.Y - 5 * 0xC00 ) <= 0x1200 ), "every car dropped into the arena" );
		Run( controller, 20 * 30 + 4 );
		Assert.AreEqual( BumpController.PhaseLoading, controller.Phase, "the session ends and the ride loads again" );
		var released = new List<int>();
		for ( var guest = controller.Command( 2, 0 ); guest != 0; guest = controller.Command( 2, 0 ) )
			released.Add( guest );
		CollectionAssert.AreEquivalent( Enumerable.Range( 100, 32 ).ToArray(), released.ToArray(), "every rider is handed back once" );
		// a water ride's boats are limited only by the module's 64: nothing to raise
		Assert.IsFalse( BumperTemplates.Get( -5 ).IsBumper || BumperTemplates.Get( -5 ).IsKart );
		var water = new BumpController( BumperTemplates.Get( -5 ), () => 0, new BumpWorld() );
		Assert.IsFalse( water.CanRaiseVehicleLimit );
		Assert.IsFalse( water.RaiseVehicleLimit() );
		Assert.AreEqual( 64, water.VehicleLimit );
	}

	[TestMethod]
	public void TheRaisedLimitIsSavedOnlyForARaisedControllerAndARestoreKeepsIt()
	{
		var plain = Bumper();
		LoadAndStart( plain, 4, 30 );
		Assert.IsNull( TrackSave.CaptureController( plain, 7 ).VehicleLimit, "an unraised controller writes no limit, so its save keeps its bytes" );
		Assert.IsFalse( System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( TrackSave.CaptureController( plain, 7 ) ) ).Contains( "VehicleLimit", StringComparison.Ordinal ) );
		var raised = Bumper( -14 );
		raised.RaiseVehicleLimit();
		LoadAndStart( raised, 20, 30 );
		Run( raised, 300 );
		var data = TrackSave.CaptureController( raised, 7 );
		Assert.AreEqual( 32, data.VehicleLimit );
		var restored = Bumper( -14 );
		restored.Restore( data );
		Assert.AreEqual( 32, restored.VehicleLimit );
		var (a, b) = (new StateHasher(), new StateHasher());
		raised.AddCanonicalState( a );
		restored.AddCanonicalState( b );
		Assert.AreEqual( a.Value, b.Value, "the restored controller hashes like the original" );
		Assert.ThrowsException<InvalidDataException>( () => Bumper( -14 ).Restore( data with { VehicleLimit = 30 } ), "only the raised limit 32" );
		var water = new BumpController( BumperTemplates.Get( -5 ), () => 0, new BumpWorld() );
		Assert.ThrowsException<InvalidDataException>( () => water.Restore( TrackSave.CaptureController( water, 9 ) with { VehicleLimit = 32 } ), "a water ride is never raised" );
	}

	/// <summary>
	/// [EXT:cheats-ride-vehicles] The pinned run of the cheat's bumper scenario (docs/DETERMINISM.md, "Cheats"): the jungle bumper controller raised to 32 cars, loaded with 32 guests and run for
	/// a 30-second session and 600 more ticks with the world generator of seed 99. A deliberate change of the raised limit, the drop-in or the session changes this value.
	/// </summary>
	internal const ulong CheatsBumperRaisedRunHash = 0xDF2C86E99A8FB3C0UL;

	internal static ulong RunRaisedBumperSession()
	{
		var controller = Bumper( -1, 99 );
		controller.RaiseVehicleLimit();
		LoadAndStart( controller, 32, 30 );
		Run( controller, 30 * 30 + 600 );
		var hash = new StateHasher();
		controller.AddCanonicalState( hash );
		return hash.Value;
	}

	[TestMethod]
	public void CheatsBumperRaisedRunHashIsTheSameInEveryProcess()
	{
		var hash = RunRaisedBumperSession();
		Assert.AreEqual( hash, RunRaisedBumperSession(), "repeatable in the process" );
		Assert.AreEqual( CheatsBumperRaisedRunHash, hash, $"0x{hash:X16}" );
	}

	// ---- Karts ------------------------------------------------------------------------------------------

	private sealed class FlatGrid : IParkGrid
	{
		public int Width => 40;
		public int Height => 40;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	/// <summary>A kart loop with its controller (KartControllerTests).</summary>
	private static BumpController KartLoop( int type, uint seed )
	{
		var cells = new ParkCellMap( 40, 40 );
		var builder = new TrackBuilder( cells, TrackFamily.Kart, () => null, new FlatGrid() );
		var station = TrackStation.FromPlacement( 10, 9, 0 );
		builder.PlaceStation( station );
		var writer = new TrackLineWriter( builder );
		writer.Commit( (14, 10), (14, 12) );
		writer.Commit( (14, 12), (8, 12) );
		writer.Commit( (8, 12), (8, 10) );
		var circuit = TrackCircuitWalker.WalkAndRefresh( builder, station );
		var rng = seed;
		var controller = new BumpController( BumperTemplates.Get( type ), () => { rng = unchecked( rng * 1664525u + 1013904223u ); return rng >> 8; }, new BumpWorld() );
		controller.SetStation( station.Reference.X, station.Reference.Y, station.Heading );
		controller.Rebuild( circuit );
		return controller;
	}

	[TestMethod]
	public void ARaisedKartRideRacesThirtyTwoKartsTheOnesPastTheGridStartFromTheStation()
	{
		foreach ( var type in new[] { -4, -7 } )
		{
			var controller = KartLoop( type, 31 );
			Assert.IsTrue( controller.RaiseVehicleLimit() );
			controller.Command( 7, 0 );
			for ( var kart = 0; kart < 32; kart++ )
			{
				Assert.AreEqual( 1, controller.Command( 1, 100 + kart ) );
				Assert.AreEqual( 1, controller.Command( 4, 0 ), $"type {type}: kart {kart + 1}" );
			}
			Assert.AreEqual( 0, controller.Command( 4, 0 ), "a 33rd kart is refused" );
			Assert.AreEqual( 8, controller.GridBuoys.Count(), "the grid keeps its eight slots" );
			Run( controller, 360 );
			controller.Command( 14, 1 );
			controller.Command( 3, 0 );
			Assert.AreEqual( BumpController.PhaseRunning, controller.Phase );
			for ( var tick = 0; tick < 600 * 30 && controller.VehicleCount > 0; tick++ )
				controller.Tick();
			Assert.AreEqual( 0, controller.VehicleCount, $"type {type}: every kart came home" );
			var handed = new List<int>();
			for ( var guest = controller.Command( 2, 0 ); guest != 0; guest = controller.Command( 2, 0 ) )
				handed.Add( guest );
			CollectionAssert.AreEquivalent( Enumerable.Range( 100, 32 ).ToArray(), handed.ToArray(), $"type {type}: every racer is handed back" );
		}
	}

	// ---- Tours ------------------------------------------------------------------------------------------

	private static TourController Tour( TourWorld world )
	{
		var state = 1u;
		return new TourController( TourStation.Default, () => state = unchecked( state * 1664525u + 1013904223u ), world );
	}

	[TestMethod]
	public void ARaisedTourRideFliesFortyCarsAndSavesItsSlots()
	{
		var plain = Tour( new TourWorld() );
		plain.Command( TourController.CommandCars, 40 );
		for ( var tick = 0; tick < 200; tick++ )
			plain.Tick();
		Assert.AreEqual( TourController.CarSlots, plain.CarCount, "the original's twenty slots hold twenty cars at most" );
		Assert.IsNull( TourSave.Capture( plain ).CarSlots, "an unraised controller writes no slot count" );

		var world = new TourWorld();
		var raised = Tour( world );
		Assert.IsTrue( raised.RaiseCarSlots() );
		Assert.IsFalse( raised.RaiseCarSlots() );
		Assert.AreEqual( TourController.RaisedCarSlots, raised.SlotCount );
		raised.Command( TourController.CommandCars, 40 );
		for ( var tick = 0; tick < 200; tick++ )
			raised.Tick();
		Assert.AreEqual( 40, raised.CarCount, "ceil(360 / 9) = 40 cars fly" );
		var saved = TourSave.Capture( raised );
		Assert.AreEqual( 40, saved.CarSlots );
		TourSave.Validate( saved, _ => true, new HashSet<int>() );
		var restored = new TourController( saved, () => 0, new TourWorld() );
		Assert.AreEqual( 40, restored.SlotCount );
		var (a, b) = (new StateHasher(), new StateHasher());
		raised.AddCanonicalState( a );
		restored.AddCanonicalState( b );
		Assert.AreEqual( a.Value, b.Value );
		Assert.ThrowsException<InvalidDataException>( () => TourSave.Validate( saved with { CarSlots = 30 }, _ => true, new HashSet<int>() ), "only 20 or 40 slots" );
		Assert.ThrowsException<InvalidDataException>( () => TourSave.Validate( saved with { CarSlots = null }, _ => true, new HashSet<int>() ), "forty cars need the raised slots" );
	}

	// ---- The rides with their real data ----------------------------------------------------------------------

	[TestMethod]
	public void TheRaisedCapacityFillsTheRaisedVehiclesOfEveryShippedBumpKartAndTourRide()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original data." );
		try
		{
			foreach ( var (theme, infoId, shipped, raised, vehicles) in new[]
			{
				("jungle", 1140, 8, 32, 32), ("hallow", 2140, 8, 32, 32), ("fantasy", 4140, 8, 32, 32), ("space", 3140, 8, 32, 32),
				("jungle", 1150, 8, 32, 32), ("hallow", 2150, 16, 64, 32), ("fantasy", 4150, 8, 32, 32), ("space", 3150, 8, 32, 32),
				("jungle", 1170, 27, 360, 40), ("hallow", 2170, 27, 360, 40), ("fantasy", 4170, 27, 360, 40), ("space", 3170, 24, 320, 40),
			} )
			{
				var entry = ObjectCatalog.Load( theme ).Entries.Single( item => item.InfoId == infoId );
				var runtime = new OriginalObjectRuntime( entry, seed: 3, open: true );
				Assert.IsTrue( runtime.CanRaiseVehicles, $"{theme} {infoId}" );
				Assert.AreEqual( shipped, runtime.MaximumCapacity, $"{theme} {infoId}: the shipped maximum before the cheat" );
				Assert.AreEqual( vehicles, runtime.RaisedVehicleCount );
				runtime.SetCapacity( raised );
				Assert.AreEqual( shipped, runtime.Settings.Capacity, $"{theme} {infoId}: unraised, the setter keeps the shipped maximum" );
				Assert.IsTrue( runtime.RaiseVehicleLimit() );
				Assert.AreEqual( raised, runtime.MaximumCapacity, $"{theme} {infoId}: the raised maximum fills {vehicles} vehicles" );
				runtime.SetCapacity( raised + 50 );
				Assert.AreEqual( raised, runtime.Settings.Capacity, $"{theme} {infoId}: the setter clamps to the raised maximum, past the original's byte where it must" );
				Assert.AreEqual( raised, runtime.GetVariable( RideVariables.VAR_CAPACITY ) );
				if ( runtime.Bump is { } bump )
					Assert.AreEqual( 32, bump.VehicleLimit );
				var snapshot = runtime.CaptureSnapshot();
				Assert.AreEqual( true, snapshot.VehiclesRaised );
				snapshot.ValidateSnapshot();
			}
			// a water ride's boat count follows its riders, limited only by the module's 64: the cheat has nothing to raise there; nor on a coaster
			var water = new OriginalObjectRuntime( ObjectCatalog.Load( "jungle" ).Entries.Single( item => item.InfoId == 1160 ), seed: 3, open: true );
			Assert.IsFalse( water.CanRaiseVehicles );
			Assert.IsFalse( water.RaiseVehicleLimit() );
		}
		finally { FileSystem = previous; }
	}

	[TestMethod]
	public void TheJungleBumperScriptLaunchesThirtyTwoCarsOnceRaised()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original data." );
		try
		{
			var entry = ObjectCatalog.Load( "jungle" ).Entries.Single( item => item.ArchivePath == "/levels/jungle/rides/bumper" );
			var (runtime, _, simulation) = BumperCarAssetTests.BuildRide( "jungle", entry, 64 );
			Assert.IsTrue( runtime.RaiseVehicleLimit() );
			runtime.SetCapacity( 32 );
			var ride = runtime.Visitors;
			var maxCars = 0;
			for ( var tick = 0; tick < 300 * FixedStepClock.TicksPerSecond; tick++ )
			{
				simulation.Tick( Tick );
				runtime.Simulate( Tick );
				maxCars = Math.Max( maxCars, runtime.Bump!.VehicleCount );
			}
			Console.WriteLine( $"raised jungle bumper: boarded {ride.BoardedTotal} released {ride.ReleasedTotal} cars {maxCars}" );
			Assert.AreEqual( 32, maxCars, "the script makes a car per unit of capacity: 32 cars" );
			Assert.AreEqual( ride.BoardedTotal, ride.ReleasedTotal + ride.RidersOnBoard, "every boarded guest is released or still aboard" );
		}
		finally { FileSystem = previous; }
	}

	[TestMethod]
	public void TheRealTourScriptsAskForFortyCarsOnceRaised()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original data." );
		try
		{
			foreach ( var (theme, infoId) in new[] { ("jungle", 1170), ("hallow", 2170), ("space", 3170), ("fantasy", 4170) } )
			{
				var entry = ObjectCatalog.Load( theme ).Get( infoId );
				var runtime = new OriginalObjectRuntime( entry, new RideScriptWorld( 3 ), seed: 5, open: true );
				// raised before the script created its controller: the controller is made with the raised slots
				Assert.IsTrue( runtime.RaiseVehicleLimit() );
				runtime.SetCapacity( runtime.MaximumCapacity );
				var peak = 0;
				for ( var tick = 0; tick < 120 * 60; tick++ )
				{
					runtime.Simulate( Tick );
					peak = Math.Max( peak, runtime.Tour?.CarCount ?? 0 );
				}
				var tour = runtime.Tour!;
				Console.WriteLine( $"{theme} {infoId}: capacity {runtime.Settings.Capacity}, requested {tour.Requested}, wanted {tour.Wanted}, peak cars {peak}" );
				Assert.AreEqual( TourController.RaisedCarSlots, tour.SlotCount );
				Assert.AreEqual( 40, tour.Requested, $"{theme}: TOUR 5 asks for ceil(capacity / riders per car) = 40 cars" );
				Assert.AreEqual( 40, peak, $"{theme}: forty cars fly" );
				Assert.AreNotEqual( RideVMState.Faulted, runtime.Script!.State, runtime.Script.FaultMessage );
			}
		}
		finally { FileSystem = previous; }
	}

	/// <summary>A synthetic ride (the fixture of UpgradeCatalogueDefaultsTests): a one-triangle model and a script that sets nothing, with the given .sam lines.</summary>
	private static ObjectCatalogEntry SyntheticRide( string root, int infoId, string name, string extra )
	{
		var sam = $"Info.Id {infoId}\nInfo.Name {name}\nInfo.WhichUIType 0\nInfo.IsChoosable 1\nInfo.HasQueue 1\nUsageInfo.MinCapacity 1\nUsageInfo.MaxCapacity 8\nUsageInfo.MinDuration 1\nUsageInfo.MaxDuration 200\nUpgrades[0].InitCapacity 4\n{extra}";
		return new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/" + name, new ObjectSettings( new[] { new ObjectSettingsFile( sam, name + ".sam" ) } ), new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE",
			Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( root ), null );
	}

	[TestMethod]
	public void TheCheatsWindowsRideRowsPickAParkRideAndRaiseIt()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-cheat-rides-" ).FullName;
		var previousEntities = Entity.All;
		Entity.All = new();
		using var scope = OpenTpwExtensions.Use( CheatsTests.On() );
		try
		{
			var bytes = Md2ModelFileTests.CreateGeometry();
			System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x42 ), 1 ); System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x44 ), 0 );
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x268 ), 0 ); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x78 ), 0x264 );
			foreach ( var at in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( at ), 0 );
			File.WriteAllBytes( Path.Combine( root, "model.MD2" ), bytes );
			File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
			var hud = VisitorWorldSelectionTests.Fixture( out _, out _ );
			var level = (Level)typeof( OpenTPW.Hud.ParkHud ).GetField( "level", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.GetValue( hud )!;
			var objects = level.Objects;
			OriginalObject Place( ObjectCatalogEntry entry, int seed )
			{
				var item = new OriginalObject( entry, new ObjectPlacement( seed, seed, 0, System.Numerics.Vector2.Zero, 0 ), objects.ScriptWorld, seed: seed );
				((List<OriginalObject>)objects.Objects).Add( item );
				return item;
			}
			var first = Place( SyntheticRide( root, 1140, "bumper", "Bumper.WhichTrackType 0\nBumper.BumperType -1\n" ), 1 );
			var second = Place( SyntheticRide( root, 1140, "bumper", "Bumper.WhichTrackType 0\nBumper.BumperType -1\n" ), 2 );
			Place( SyntheticRide( root, 1160, "water", "Bumper.WhichTrackType 2\nBumper.BumperType -5\n" ), 3 );
			Place( SyntheticRide( root, 1100, "plain", "" ), 4 );
			CollectionAssert.AreEqual( new[] { first, second }, level.CheatRides.ToArray(), "the bumper rides; not the water ride or a ride without vehicles" );
			var economy = EconomyTestData.Park();
			economy.CheatHost = level;
			hud.GetType().GetField( "<Status>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( hud, new OpenTPW.Hud.EconomyParkStatus( () => economy ) );
			CommandTestLevel.AttachPark( (Level)typeof( OpenTPW.Hud.ParkHud ).GetField( "level", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.GetValue( hud )!, economy );
			var window = hud.CreateCheatsWindow();
			string Text( string id ) => ((OpenTPW.UI.Original.UiLabel)window.Find( id + "Label" )!).Text();
			OpenTPW.UI.Original.UiButton Button( string id ) => (OpenTPW.UI.Original.UiButton)window.Find( id )!;
			StringAssert.EndsWith( Text( "ride" ), "1", "two rides of a kind are numbered" );
			Assert.AreEqual( "Raise vehicles to 32", Text( "rideVehicles" ) );
			Button( "ride" ).Clicked!();
			StringAssert.EndsWith( Text( "ride" ), "2" );
			Button( "rideVehicles" ).Clicked!();
			Assert.AreEqual( 0, economy.Cheats.Pending.Count, "nothing acts before the cheat mode is on" );
			Button( "cheatMode" ).Clicked!();
			Button( "rideVehicles" ).Clicked!();
			CollectionAssert.AreEqual( new[] { new CheatAction( CheatKind.Mode ), new CheatAction( CheatKind.RideVehicles, second.Runtime.AttractionId ) }, economy.Cheats.Pending.ToArray() );
			while ( economy.Cheats.Pending.Count > 0 )
				economy.Advance( 1 );
			Assert.IsTrue( second.Runtime.VehiclesRaised && !first.Runtime.VehiclesRaised );
			Assert.AreEqual( 32, second.Runtime.Bump!.VehicleLimit );
			Assert.AreEqual( 32, second.Runtime.MaximumCapacity );
			Assert.AreEqual( "Vehicles raised to 32", Text( "rideVehicles" ) );
			Assert.AreEqual( 1, level.RaisedRideCount );
			Assert.AreEqual( 0, level.RidesAboveOriginalLimits, "raised, but its capacity is still within the original's eight: the original format could hold it" );
			second.Runtime.SetCapacity( 32 );
			Assert.AreEqual( 1, level.RidesAboveOriginalLimits, "above the original's eight: the export refuses the park" );
			Assert.IsTrue( economy.Cheats.UsesExtension );
		}
		finally
		{
			foreach ( var item in Entity.All.ToArray() ) item.Delete();
			Entity.All = previousEntities;
			Directory.Delete( root, true );
		}
	}

	// ---- The cheat at the turn boundary ----------------------------------------------------------------------

	private sealed class Host : IParkCheatHost
	{
		public readonly HashSet<int> Raisable = new() { 7 };
		public readonly List<int> Raised = new();
		public bool TryStartArrival( int vehicleKind ) => false;
		public bool TryApplyWeather( CheatWeather weather, long tick ) => false;
		public bool CanRaiseRideVehicles( int attractionId ) => Raisable.Contains( attractionId ) && !Raised.Contains( attractionId );
		public bool TryRaiseRideVehicles( int attractionId )
		{
			if ( !CanRaiseRideVehicles( attractionId ) )
				return false;
			Raised.Add( attractionId );
			return true;
		}
	}

	private static long NextTurn( ParkEconomy park )
	{
		var turn = park.Turn;
		while ( park.Turn == turn )
			park.Advance( 1 );
		return park.Turn;
	}

	[TestMethod]
	public void TheRideVehicleCheatQueuesForARideTheLevelCanRaiseAndAppliesAtTheNextTurn()
	{
		var park = EconomyTestData.Park();
		var host = new Host();
		park.CheatHost = host;
		var action = new CheatAction( CheatKind.RideVehicles, 7 );
		Assert.IsFalse( park.QueueCheat( action ), "the extension is off" );
		using var scope = OpenTpwExtensions.Use( CheatsTests.On() );
		Assert.IsFalse( park.QueueCheat( new CheatAction( CheatKind.RideVehicles, 8 ) ), "a ride the level cannot raise" );
		Assert.IsFalse( park.QueueCheat( new CheatAction( CheatKind.RideVehicles, 0 ) ), "no ride" );
		Assert.IsTrue( park.QueueCheat( action ) );
		Assert.IsFalse( park.QueueCheat( action ), "one waiting request per ride" );
		Assert.AreEqual( 0, host.Raised.Count, "nothing before the turn" );
		var turn = NextTurn( park );
		CollectionAssert.AreEqual( new[] { 7 }, host.Raised );
		Assert.AreEqual( new CheatLogEntry( turn, CheatKind.RideVehicles, 7 ), park.Cheats.Log.Single() );
		Assert.IsTrue( park.Cheats.Extension && park.Cheats.UsesExtension, "the extension's own cheat: the world save records the extension" );
		Assert.IsFalse( park.QueueCheat( action ), "raised already" );
		// a ride removed between the request and the turn: recorded, no effect
		host.Raisable.Add( 9 );
		Assert.IsTrue( park.QueueCheat( new CheatAction( CheatKind.RideVehicles, 9 ) ) );
		host.Raisable.Remove( 9 );
		NextTurn( park );
		Assert.AreEqual( 2, park.Cheats.Log.Count );
		CollectionAssert.AreEqual( new[] { 7 }, host.Raised );
	}

	[TestMethod]
	public void TheOriginalFormatExportRefusesAParkWithARaisedRide()
	{
		var settings = OriginalSaveImportTests.Settings();
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var template = OriginalSavePayloadFixtures.Blank( settings );
		OriginalParkExport.Export( template, ParkSaveFile.Capture( economy ), out _, new OriginalParkExportOptions( RidesAboveOriginalLimits: 0 ) );
		var refused = Assert.ThrowsException<InvalidDataException>( () => OriginalParkExport.Export( template, ParkSaveFile.Capture( economy ), out _, new OriginalParkExportOptions( RidesAboveOriginalLimits: 1 ) ) );
		StringAssert.Contains( refused.Message, "vehicle limit a cheat raised and whose capacity is above the original's limit" );
	}

	[TestMethod]
	public void TheCapacitySetterTakesARaisedMaximumPastTheByte()
	{
		var settings = new ObjectSettings( new[] { new ObjectSettingsFile( "UsageInfo.MinCapacity 1\nUsageInfo.MaxCapacity 27\n", "tour.sam" ) } );
		Assert.AreEqual( 27, OriginalObjectRuntime.ClampCapacity( settings, 300 ), "the original's setter: the byte, then the type's maximum" );
		Assert.AreEqual( 300 & 0xFF, OriginalObjectRuntime.ClampCapacity( new ObjectSettings( new[] { new ObjectSettingsFile( "UsageInfo.MinCapacity 0\nUsageInfo.MaxCapacity 0\n", "free.sam" ) } ), 300 ) );
		Assert.AreEqual( 300, OriginalObjectRuntime.ClampCapacity( settings, 300, raisedMaximum: 360 ) );
		Assert.AreEqual( 360, OriginalObjectRuntime.ClampCapacity( settings, 400, raisedMaximum: 360 ) );
		Assert.AreEqual( 1, OriginalObjectRuntime.ClampCapacity( settings, 0, raisedMaximum: 360 ) );
	}
}
