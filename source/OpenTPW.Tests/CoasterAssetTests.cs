using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Roller coasters on the original PC data (inconclusive without OPENTPW_GAME_PATH): the catalogue and a headless build on the Jungle level that guests ride.</summary>
[TestClass]
public class CoasterAssetTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	[TestInitialize]
	public void Init()
	{
		Log = new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original coaster archives." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	private static IEnumerable<ObjectCatalogEntry> PcCoasters() =>
		ObjectCatalog.Themes.SelectMany( theme => ObjectCatalog.Load( theme ).Entries ).Where( entry => !entry.IsBonus && CoasterDefinition.IsCoaster( entry.Settings ) );

	/// <summary>The twelve PC coasters of COAST-plan §3.3 with their coaster.sam values over the code defaults.</summary>
	[TestMethod]
	public void ThePcCatalogHasTwelveCoasters()
	{
		var coasters = PcCoasters().ToDictionary( entry => entry.InfoId );
		CollectionAssert.AreEquivalent( new[] { 1180, 1185, 1190, 2180, 2185, 2190, 3180, 3185, 3190, 4180, 4185, 4190 }, coasters.Keys.ToArray() );
		var definitions = coasters.ToDictionary( pair => pair.Key, pair => CoasterDefinition.FromSettings( pair.Value.Settings ) );
		CollectionAssert.AreEquivalent( new[] { 1185, 2185, 4180 }, definitions.Where( pair => pair.Value.IsTrough ).Select( pair => pair.Key ).ToArray(), "the three flumes" );
		Assert.AreEqual( 9, definitions.Values.Count( definition => definition.HasLoopPylon ) );
		foreach ( var (id, definition) in definitions )
		{
			Assert.AreEqual( (40f, 10f, 10f), (definition.StandardPylon.CostPerUnit, definition.StandardPylon.CostPer2DLength, definition.StandardPylon.CostPerHeight), $"{id} StdPylon rates" );
			Assert.AreEqual( (10f, 100f), (definition.StandardPylon.MinHeight, definition.StandardPylon.MaxHeight), $"{id} heights" );
			Assert.IsTrue( definition.StandardPylon.MaxStackedPylons is 3 or 4, $"{id} stack" );
			Assert.AreEqual( 5f, definition.WinchSpeed, "no PC coaster sets a motion key besides the flumes' acceleration and friction" );
			Assert.AreEqual( 3, definition.StandardPylon.MinPlaceDistance );
			Assert.AreEqual( 8, definition.StandardPylon.MaxPlaceDistance );
			if ( definition.HasLoopPylon )
				Assert.AreEqual( (60f, 15f, 15f), (definition.LoopPylon.CostPerUnit, definition.LoopPylon.CostPer2DLength, definition.LoopPylon.CostPerHeight), $"{id} LoopPylon rates" );
		}
		var chacAtak = definitions[1185];
		Assert.AreEqual( (0.8f, 0.95f, 20, 16000, 1), (chacAtak.AccelerationPerHeight, chacAtak.FrictionMultiplier, chacAtak.MaxTrains, chacAtak.TrainReleaseRate, chacAtak.MaxCars) );
		var temple = definitions[1180];
		Assert.AreEqual( (1.5f, 0.8f, 6, 30000, 4), (temple.AccelerationPerHeight, temple.FrictionMultiplier, temple.MaxTrains, temple.TrainReleaseRate, temple.MaxCars) );
		Assert.AreEqual( 10000, coasters[1180].BuildCost, "the station price" );
		Assert.AreEqual( 12500, coasters[1190].BuildCost );
	}

	/// <summary>Every PC coaster builds the same circuit on a flat site, completes, rates within [0, 100] and runs.</summary>
	[TestMethod]
	public void EveryPcCoasterBuildsRatesAndRuns()
	{
		foreach ( var entry in PcCoasters() )
		{
			var world = new CoasterWorld( 3 );
			var ride = new CoasterRide( CoasterDefinition.FromSettings( entry.Settings ), world, entry.DisplayName );
			var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
			builder.SetStation( (20, 20), (17, 20) );
			builder.Height = 40;
			foreach ( var (dx, dy) in CoasterTests.Circuit )
				Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + dx, 20 + dy ), $"{entry.InfoId}" );
			Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ), $"{entry.InfoId}" );
			var rating = ride.Rating!.Value;
			Assert.IsTrue( rating.Excitement is >= 0 and <= 100 && rating.Sickness is >= 0 and <= 100 && rating.LapTime > 0, $"{entry.InfoId}: {rating}" );
			Assert.IsTrue( rating.Trains >= 1 && rating.Trains <= ride.Definition.MaxTrains );
			Assert.AreEqual( ride.Definition.IsTrough, ride.Samples.Any( sample => sample.Flags.HasFlag( CoasterSampleFlags.Friction ) ), $"{entry.InfoId}: only troughs have friction samples" );
			ride.Command( 8, 0 );
			ride.Command( 5, 0 );
			for ( var tick = 0; tick < 20 * 60; tick++ )
				ride.Advance( Tick );
			Assert.IsTrue( ride.Trains.Any( train => train.State == CoasterTrainState.Running ), $"{entry.InfoId} dispatches" );
		}
	}

	/// <summary>
	/// A fresh Jungle park: the Temple Of Gloom station runs its original Coaster.RSE; the headless builder lays a
	/// circuit on free land (each pylon charged through the park economy), closes, completes and rates it; guests at the station's
	/// entrance queue, board through COAST 1, ride a lap and leave through COAST 3; closing lets the trains finish;
	/// deleting removes every pylon without a refund.
	/// </summary>
	[TestMethod]
	public void GuestsRideACoasterBuiltHeadlesslyOnJungle()
	{
		var park = OriginalPark.Load( "jungle", readShippedSave: false );
		var terrain = new OriginalParkGrid( park );
		var grid = GuestPathGrid.FromOriginal( park.Map, null );
		var guests = new GuestSimulation( grid, GuestSettings.Load( "jungle" ), 7 );
		var economy = ParkEconomy.CreateForTheme( "jungle", easy: false );
		var entry = ObjectCatalog.Load( "jungle" ).Get( 1180 );
		var scripts = new RideScriptWorld( 11 );

		// The station nearest the entrance walkway whose footprint and circuit cells are free land.
		var walkway = Enumerable.Range( 0, grid.CountX * grid.CountY ).Select( index => (X: index % grid.CountX, Y: index / grid.CountX) ).Where( cell => grid.IsWalkable( cell.X, cell.Y ) ).ToArray();
		Assert.IsTrue( walkway.Length > 0 );
		bool Free( int x, int y ) => terrain.CheckTerrain( x, y ) == OriginalPlacementResult.Allowed && !grid.IsWalkable( x, y ) && !grid.IsQueue( x, y );
		(int X, int Y)? anchor = null;
		HashSet<(int, int)> footprint = new();
		foreach ( var candidate in Enumerable.Range( 0, terrain.Width * terrain.Height ).Select( index => (X: index % terrain.Width, Y: index / terrain.Width) )
			.OrderBy( cell => walkway.Min( path => Math.Abs( path.X - cell.X ) + Math.Abs( path.Y - cell.Y ) ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X ) )
		{
			var cells = ObjectFootprint.GetCells( entry.Shape, candidate.X, candidate.Y, 0 ).Select( cell => (cell.X, cell.Y) ).ToHashSet();
			if ( !cells.All( cell => Free( cell.X, cell.Y ) ) )
				continue;
			var ends = CoasterBuilder.StationEnds( entry.Shape, candidate.X, candidate.Y, 0 )!.Value;
			if ( !CoasterTests.Circuit.All( offset => Free( ends.Depart.X + offset.X, ends.Depart.Y + offset.Y ) && !cells.Contains( (ends.Depart.X + offset.X, ends.Depart.Y + offset.Y) ) ) )
				continue;
			anchor = candidate;
			footprint = cells;
			break;
		}
		Assert.IsNotNull( anchor, "a free site for the station and the circuit" );

		var runtime = new OriginalObjectRuntime( entry, scripts, seed: 5, open: true );
		var ride = runtime.Coaster!;
		Assert.IsNotNull( ride );
		var builder = new CoasterBuilder( ride, CoasterSite.FromGrid( terrain ), () => economy );
		var (depart, back) = CoasterBuilder.StationEnds( entry.Shape, anchor!.Value.X, anchor.Value.Y, 0 )!.Value;
		builder.SetStation( depart, back );
		var balance = economy.Balance;
		builder.Height = 50;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
		{
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( depart.X + dx, depart.Y + dy ) );
			builder.Height = 20;
		}
		var cost = builder.Ride.PaidCost;
		Assert.AreEqual( balance - cost, economy.Balance, "each pylon is charged trunc(cost) through ParkEconomy when placed" );
		Assert.IsTrue( cost > 0 && cost <= builder.BuildCost );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		Assert.AreEqual( balance - cost, economy.Balance, "completion moves no money" );
		var rating = ride.Rating!.Value;
		Log.Trace( $"Jungle coaster at {anchor}: ${cost}, {ride.Samples.Count} samples, lap {rating.LapTime:F1} s, excitement {rating.Excitement:F1}, sickness {rating.Sickness:F1}, {rating.Trains} train(s), max speed {rating.MaxSpeed:F1}." );

		Assert.IsTrue( Level.RegisterWithGuests( runtime, ObjectFootprint.GetAccessPoints( entry.Shape, anchor.Value.X, anchor.Value.Y, 0 ), guests ), "the station is a guest attraction" );
		var entrance = runtime.Visitors.EntranceCell;
		var spawned = Enumerable.Range( 0, 8 ).Select( _ =>
		{
			var guest = guests.SpawnInPark( entrance.X, entrance.Y );
			guest.AttractionId = runtime.Visitors.AttractionId;
			guest.State = GuestState.GoingToRide;
			return guest;
		} ).ToArray();

		var peakRiders = 0;
		var ranWithRiders = false;
		for ( var tick = 0; tick < 240 * 60 && runtime.Visitors.ReleasedTotal < 2; tick++ )
		{
			guests.Tick( Tick );
			runtime.Simulate( Tick );
			peakRiders = Math.Max( peakRiders, ride.RidersOnBoard );
			ranWithRiders |= ride.Trains.Any( train => train.State == CoasterTrainState.Running && train.Riders > 0 );
		}
		Assert.IsTrue( ride.Initialised && !ride.Closed, "Coaster.RSE ran COAST 8 and opened the controller" );
		Assert.AreEqual( CoasterRating.GuestExcitement( rating.Excitement ), runtime.Visitors.ExcitementLevel, "guests see trunc(0.5 E + 50)" );
		Assert.IsTrue( runtime.Visitors.BoardedTotal >= 2 && peakRiders >= 1, $"boarded {runtime.Visitors.BoardedTotal}, peak riders {peakRiders}; guests {string.Join( ", ", spawned.Select( guest => guest.State ) )}" );
		Assert.IsTrue( ranWithRiders, "a train ran a lap with riders" );
		Assert.IsTrue( runtime.Visitors.ReleasedTotal >= 2, $"released {runtime.Visitors.ReleasedTotal}" );
		Assert.AreEqual( 0, runtime.Script!.UnimplementedEffects.GetValueOrDefault( Opcode.COAST ), "every COAST command reached the controller" );

		// Closing: no more boarding; trains finish to the station and every rider gets off.
		runtime.Close();
		for ( var tick = 0; tick < 180 * 60; tick++ )
		{
			guests.Tick( Tick );
			runtime.Simulate( Tick );
		}
		Assert.IsTrue( ride.Closed );
		Assert.AreEqual( 0, ride.RidersOnBoard );
		Assert.AreEqual( 0, runtime.GetVariable( RideVariables.VAR_ONRIDE ) );
		Assert.IsFalse( ride.Trains.Any( train => train.State == CoasterTrainState.Running ), "trains finished their lap" );
		Assert.AreEqual( runtime.Visitors.BoardedTotal, runtime.Visitors.ReleasedTotal );

		var beforeDelete = economy.Balance;
		builder.Delete();
		Assert.AreEqual( beforeDelete, economy.Balance );
		Assert.IsFalse( ride.IsOperational );
		Assert.IsFalse( CoasterTests.Circuit.Any( offset => scripts.Coasters.HasPylon( depart.X + offset.X, depart.Y + offset.Y ) ) );
		runtime.Stop();
		Assert.IsFalse( scripts.Coasters.Rides.Contains( ride ), "a removed station leaves the coaster world" );
	}
}
