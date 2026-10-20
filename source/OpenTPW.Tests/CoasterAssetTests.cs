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
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
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

	private static IEnumerable<ObjectCatalogEntry> PcCoasters() =>
		ObjectCatalog.Themes.SelectMany( theme => ObjectCatalog.Load( theme ).Entries ).Where( entry => !entry.IsBonus && CoasterDefinition.IsCoaster( entry.Settings ) );

	/// <summary>
	/// The car metrics come from the car models (<c>0x430130</c>): the header bounds around the root node plus the distance adds and the head sockets;
	/// the caterpillar (4185) seats only its four bodies, 4 heads each, and the trains of every coaster seat one train's head sockets.
	/// </summary>
	[TestMethod]
	public void CarMetricsComeFromTheCarModelsAndOnlyCentreTypeCarsSeat()
	{
		foreach ( var entry in PcCoasters() )
		{
			var definition = CoasterDefinition.FromCatalogEntry( entry );
			Assert.IsTrue( definition.HasCarModels, $"{entry.InfoId} reads its car models" );
			using var assets = new CoasterAssets( entry, definition );
			var rig = assets.GetCar( definition.CarTypes.CentreIndex )!;
			var type = definition.CarTypes.Types[definition.CarTypes.CentreIndex];
			var metrics = definition.CarTypeMetrics( definition.CarTypes.CentreIndex );
			var rootZ = rig.Model.Nodes[rig.Model.RootNodeIndex].Transform.M43;
			Assert.AreEqual( type.FrontAdd + rig.Model.BoundsMax.Z - rootZ, metrics.FrontExtent, 1e-4f, $"{entry.InfoId}: front extent" );
			Assert.AreEqual( type.RearAdd + rootZ - rig.Model.BoundsMin.Z, metrics.RearExtent, 1e-4f, $"{entry.InfoId}: rear extent" );
			Assert.AreEqual( rig.Model.DummyAttributes.Count( attribute => (attribute.Type & 0x80) != 0 ), metrics.Heads, $"{entry.InfoId}: head sockets" );
			Assert.IsTrue( definition.HeadsPerTrain > 0, $"{entry.InfoId} seats someone" );
		}
		var caterpillar = CoasterDefinition.FromCatalogEntry( PcCoasters().Single( entry => entry.InfoId == 4185 ) );
		Assert.AreEqual( (0, 4, 0), (caterpillar.CarTypeMetrics( 0 ).Heads, caterpillar.CarTypeMetrics( 1 ).Heads, caterpillar.CarTypeMetrics( 2 ).Heads), "caterbd has four records with flag 0x80, the head and the tail none" );
		Assert.AreEqual( (6, 16, 4, 2), (caterpillar.CarCount, caterpillar.HeadsPerTrain, caterpillar.SeatCarCount, caterpillar.ReferenceCar), "head, four bodies of four, tail; the third car is the reference" );
		var ride = new CoasterRide( caterpillar, new CoasterWorld( 1 ) );
		Assert.AreEqual( 16, ride.SeatsPerTrain );
		Assert.AreEqual( 0f, caterpillar.CarOffsets[2] );
		Assert.IsTrue( caterpillar.CarOffsets.Zip( caterpillar.CarOffsets.Skip( 1 ), ( ahead, behind ) => ahead > behind ).All( x => x ), "the offsets fall from the head to the tail" );
	}

	/// <summary>The twelve PC coasters of COAST-plan §3.3 with their coaster.sam values over the code defaults.</summary>
	[TestMethod]
	public void ThePcCatalogHasTwelveCoasters()
	{
		var coasters = PcCoasters().ToDictionary( entry => entry.InfoId );
		CollectionAssert.AreEquivalent( new[] { 1180, 1185, 1190, 2180, 2185, 2190, 3180, 3185, 3190, 4180, 4185, 4190 }, coasters.Keys.ToArray() );
		var definitions = coasters.ToDictionary( pair => pair.Key, pair => CoasterDefinition.FromEntry( pair.Value ) );
		CollectionAssert.AreEquivalent( new[] { 1185, 2185, 4180 }, definitions.Where( pair => pair.Value.IsTrough ).Select( pair => pair.Key ).ToArray(), "the three flumes" );
		Assert.AreEqual( 9, definitions.Values.Count( definition => definition.HasLoopPylon ) );
		foreach ( var (id, definition) in definitions )
		{
			Assert.AreEqual( (40f, 10f, 10f), (definition.StandardPylon.CostPerUnit, definition.StandardPylon.CostPer2DLength, definition.StandardPylon.CostPerHeight), $"{id} StdPylon rates" );
			Assert.AreEqual( (10f, 100f), (definition.StandardPylon.MinHeight, definition.StandardPylon.MaxHeight), $"{id} heights" );
			Assert.IsTrue( definition.StandardPylon.MaxStackedPylons is 3 or 4, $"{id} stack" );
			Assert.AreEqual( 5f, definition.WinchSpeed, "no PC coaster sets a motion key besides the flumes' acceleration and friction" );
			Assert.AreEqual( 0f, definition.EntranceSizeInterval, $"{id}: no PC coaster.sam names fEntranceSizeInterval, so the station window is the full span (copied unconditionally, 0x42f717)" );
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

	/// <summary>
	/// The pylon heights come from the pylon models (<c>0x430590</c>): h0 and h1 are the heights of the track dummy the model's attribute record names,
	/// fMinHeight is raised to h0, and the stack offset is the stack dummy's height above h0 (non-zero for two of the Space and Halloween standard pylons).
	/// </summary>
	[TestMethod]
	public void PylonHeightsAndStackOffsetsComeFromTheModelsTrackAndStackDummies()
	{
		var seen = 0;
		var skipped = 0;
		foreach ( var entry in PcCoasters() )
		{
			var definition = CoasterDefinition.FromCatalogEntry( entry );
			var plain = CoasterDefinition.FromSettings( entry.CoasterSettings );
			using var assets = new CoasterAssets( entry, definition );
			foreach ( var loop in new[] { false, true } )
			{
				if ( loop && !definition.HasLoopPylon )
					continue;
				var rig = assets.GetPylon( loop );
				if ( rig == null )
					continue;
				var pylon = definition.Pylon( loop );
				if ( rig.TagNode < 0 || rig.TagHigh <= rig.TagLow )
				{
					Assert.IsFalse( pylon.HasModelHeights, $"{entry.InfoId} loop {loop}: a model whose track dummy does not move with the loft keeps the settings' heights" );
					Assert.AreEqual( plain.Pylon( loop ).MinHeight, pylon.MinHeight );
					Assert.AreEqual( plain.Pylon( loop ).MeshHeightHigh, pylon.MeshHeightHigh );
					skipped++;
					continue;
				}
				Assert.IsTrue( pylon.HasModelHeights, $"{entry.InfoId} loop {loop}" );
				Assert.AreEqual( (rig.TagLow, rig.TagHigh, rig.StackOffset), (pylon.MeshHeightLow, pylon.MeshHeightHigh, pylon.StackOffset) );
				Assert.AreEqual( MathF.Max( plain.Pylon( loop ).MinHeight, rig.TagLow ), pylon.MinHeight, $"{entry.InfoId}: fMinHeight raised to h0 (0x4309a4)" );
				Assert.AreEqual( plain.Pylon( loop ).MaxHeight, pylon.MaxHeight );
				seen++;
			}
		}
		Assert.IsTrue( seen >= 18, $"{seen} pylon models read" );
		Assert.AreEqual( 0, skipped, "every PC pylon's track dummy (the node of its 0x800000 record) moves with the loft clip, Gorilla Thrilla's loop pylon too once the clips turn the nodes relatively (0x470d10)" );
		CoasterPylonRig Rig( int id, bool loop )
		{
			var entry = PcCoasters().Single( candidate => candidate.InfoId == id );
			var definition = CoasterDefinition.FromSettings( entry.CoasterSettings );
			using var assets = new CoasterAssets( entry, definition );
			return assets.GetPylon( loop )!;
		}
		var gorilla = Rig( 1190, false );
		Assert.AreEqual( (15.558132f, 95.55653f), (gorilla.TagLow, gorilla.TagHigh), "the Gorilla Thrilla pylon: the doc's 15.56 and 95.56" );
		Assert.AreEqual( "TrackDummyCentre", gorilla.Model.Nodes[gorilla.TagNode].Name );
		var skull = Rig( 3190, false );
		Assert.AreEqual( "TrackCentreDummy", skull.Model.Nodes[skull.TagNode].Name, "record 2 (0x03c00031) belongs to the node stored as header +0x46 + 2 (0x44ab90), the dummy the loft, incline and bank clips move" );
		Assert.AreEqual( "TrackHeightDummy", skull.Model.Nodes[skull.StackNode].Name, "record 1 (0x100011) is the stack dummy on the bridge" );
		Assert.AreEqual( (10f, 100f, 20f), (MathF.Round( skull.TagLow, 3 ), MathF.Round( skull.TagHigh, 3 ), MathF.Round( skull.StackOffset, 3 )) );
		var gorillaLoop = Rig( 3180, true );
		Assert.AreEqual( "LOOP_DUMMY", gorillaLoop.Model.Nodes[gorillaLoop.TagNode].Name );
		Assert.AreEqual( (8.81f, 98.81f), (MathF.Round( gorillaLoop.TagLow, 2 ), MathF.Round( gorillaLoop.TagHigh, 2 )), "the loft clip's +90 on the clamp is a rise in the stanchion's turned frame" );
		var spooky = Rig( 3185, false );
		Assert.AreEqual( "STACK_DUMMY", spooky.Model.Nodes[spooky.StackNode].Name );
		Assert.AreEqual( 7.776738f, spooky.StackOffset, 1e-3f, "stack dummy Y − h0" );
		Assert.AreEqual( 3.7566833f, Rig( 3180, false ).StackOffset, 1e-3f );
		Assert.AreEqual( 20f, Rig( 1180, false ).StackOffset, 1e-3f, "the mine cart's StackHeightDummy (record 4) stands 20 above its track dummy" );
	}

	/// <summary>
	/// The three station nodes stand where the station model's dummies do (<c>0x436ac0</c>): the first one half a cell before the '&lt;' cell and the last one half a cell
	/// past the '&gt;' cell along the platform, in every rotation, with the middle node between them; the cells and heights of the shipped stations are the ones of the dummies.
	/// </summary>
	[TestMethod]
	public void TheStationFrameComesFromTheModelsDummiesInEveryRotation()
	{
		var stations = 0;
		foreach ( var entry in PcCoasters() )
		{
			var scripts = new RideScriptWorld( 3 );
			var runtime = new OriginalObjectRuntime( entry, scripts, seed: 5, open: true );
			foreach ( var rotation in new[] { 0, 90, 180, 270 } )
			{
				var placement = new ObjectPlacement( 30, 30, rotation, System.Numerics.Vector2.Zero, 0f );
				var layout = CoasterStationLayout.FromModel( runtime.Model, runtime.Animator.LocalTransforms, placement );
				Assert.IsNotNull( layout, $"{entry.DisplayName}: the model holds dummies 1, 2 and 3 with attribute 0x400000" );
				// The plain composition (DetMatrix) agrees with System.Numerics' world matrices up to the last bits of rounding.
				var map = AdvisorNodeMap.From( runtime.Model );
				var mid = (ObjectPlacement.SwapAxes( runtime.Animator.NodeTransforms[map.FindNode( 0x400000, 2 )] ) * placement.ModelToEngine).Translation;
				Assert.AreEqual( mid.X / ObjectPlacement.ModelScale, layout!.Mid.X, 1e-2f, $"{entry.DisplayName} rotation {rotation}: Mid x" );
				Assert.AreEqual( mid.Y / ObjectPlacement.ModelScale, layout.Mid.Z, 1e-2f, $"{entry.DisplayName} rotation {rotation}: Mid z" );
				Assert.AreEqual( mid.Z / ObjectPlacement.ModelScale, layout.Mid.Y, 1e-2f, $"{entry.DisplayName} rotation {rotation}: Mid height" );
				var (depart, back) = CoasterBuilder.StationEnds( entry.Shape, 30, 30, rotation )!.Value;
				var step = (X: Math.Sign( depart.X - back.X ), Y: Math.Sign( depart.Y - back.Y ));
				Assert.AreEqual( (back.X - step.X, back.Y - step.Y), (CoasterTrack.CellOf( layout!.End.X ), CoasterTrack.CellOf( layout.End.Z )), $"{entry.DisplayName} rotation {rotation}: S0 stands beyond the '<' cell" );
				Assert.AreEqual( (depart.X + step.X, depart.Y + step.Y), (CoasterTrack.CellOf( layout.Start.X ), CoasterTrack.CellOf( layout.Start.Z )), $"{entry.DisplayName} rotation {rotation}: S2 stands beyond the '>' cell" );
				var along = step.X != 0 ? (layout.Mid.X - layout.End.X) * step.X : (layout.Mid.Z - layout.End.Z) * step.Y;
				var whole = step.X != 0 ? (layout.Start.X - layout.End.X) * step.X : (layout.Start.Z - layout.End.Z) * step.Y;
				Assert.IsTrue( along > 0 && along < whole, $"{entry.DisplayName} rotation {rotation}: S1 lies between the two end nodes" );
				if ( rotation == 0 )
					Log.Trace( $"station {entry.InfoId} {entry.DisplayName}: h0 {CoasterDefinition.FromCatalogEntry( entry ).StandardPylon.MeshHeightLow:F2} h1 {CoasterDefinition.FromCatalogEntry( entry ).StandardPylon.MeshHeightHigh:F2} adjust {CoasterDefinition.FromCatalogEntry( entry ).TrackHeightAdjust:F2} initial {CoasterDefinition.FromCatalogEntry( entry ).InitialPylonLoftHeight:F2}; End ({layout.End.X - 300:F2}, {layout.End.Y:F2}, {layout.End.Z - 300:F2}), Mid ({layout.Mid.X - 300:F2}, {layout.Mid.Y:F2}, {layout.Mid.Z - 300:F2}), Start ({layout.Start.X - 300:F2}, {layout.Start.Y:F2}, {layout.Start.Z - 300:F2})" );
			}
			runtime.Stop();
			stations++;
		}
		Assert.AreEqual( 12, stations );
	}

	/// <summary>
	/// The PC pylon models' tag tables (<c>0x430590</c>): three tags (In, Centre, Out by record value) for most standard pylons, one for the Gorilla, Skull and Ape pylons and
	/// every loop pylon, and two tables for a model with two 0x1000000 records (the Temple Of Gloom's: its centre tag alone, and the stacked In, Centre and Out tags). On the
	/// flat circuit every posed pylon's knots run along the heading of its node tangent (the rotate clip turns the tags by the node's yaw) and the centre knot stands by the node.
	/// </summary>
	[TestMethod]
	public void PcPylonModelsGiveTheKnotsOfTheirTagTables()
	{
		var tables = new Dictionary<int, (CoasterTagTable Single, CoasterTagTable? Stack)>();
		foreach ( var entry in PcCoasters() )
		{
			var definition = CoasterDefinition.FromCatalogEntry( entry );
			Assert.IsNotNull( definition.StandardPylon.TagModel, $"{entry.InfoId}: the standard pylon's model names its tags" );
			var rig = (CoasterPylonRig)definition.StandardPylon.TagModel!;
			tables[entry.InfoId] = (rig.SingleTable, rig.StackTable);
			if ( definition.HasLoopPylon )
				Assert.AreEqual( new CoasterTagTable( 0, 1, 1 ), ((CoasterPylonRig)definition.LoopPylon.TagModel!).SingleTable, $"{entry.InfoId}: a loop pylon has one tag" );
			var ride = new CoasterRide( definition, new CoasterWorld( 3 ), entry.DisplayName );
			var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
			CoasterTests.PlaceStation( builder, (20, 20) );
			builder.Height = 40;
			foreach ( var (dx, dy) in CoasterTests.Circuit )
				Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + dx, 20 + dy ), $"{entry.InfoId}" );
			Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ), $"{entry.InfoId}" );
			var pass = builder.Track.BuildKnotPass( builder.Site, writeBanks: false );
			for ( var index = CoasterTrack.StationNodeCount; index < builder.Track.Nodes.Count - 1; index++ )
			{
				Assert.AreEqual( rig.SingleTable.Count, pass.KnotCount[index], $"{entry.InfoId} node {index}" );
				var yaw = pass.AutomaticYaw[index];
				var centre = pass.Knots[pass.CentreKnot[index]];
				// The rotate clip turns a model about its own pivot (Moonshot's lies off the cell centre): the centre tag stays within 1.5 units of the node.
				Assert.IsTrue( System.Numerics.Vector3.Distance( pass.Positions[index], centre.Position ) < 1.5f, $"{entry.InfoId} node {index}: the centre knot {centre.Position} near the node {pass.Positions[index]}" );
				foreach ( var knot in pass.Knots.Skip( pass.FirstKnot[index] ).Take( pass.KnotCount[index] ) )
					Assert.AreEqual( 0f, MathF.Abs( MathF.IEEERemainder( MathF.Atan2( knot.Direction.X, knot.Direction.Z ) - yaw, 2 * MathF.PI ) ), 0.01f, $"{entry.InfoId} node {index}: the tag runs along the node's heading" );
			}
			Assert.AreEqual( ride.Samples.Count > 0, true );
			Assert.AreEqual( pass.Knots.Length, ride.KnotCoordinates.Count, $"{entry.InfoId}: the sampler walked every knot" );
		}
		Assert.AreEqual( (new CoasterTagTable( 1, 3, 1 ), (CoasterTagTable?)null), tables[1185], "Chac Atak: TrackDummyIn, Centre and Out (values 1..3), the centre's value 2" );
		Assert.AreEqual( (new CoasterTagTable( 0, 1, 1 ), (CoasterTagTable?)null), tables[1190], "Gorilla Thrilla: TrackDummyCentre alone" );
		Assert.AreEqual( (new CoasterTagTable( 0, 1, 2 ), (CoasterTagTable?)new CoasterTagTable( 1, 3, 1 )), tables[1180], "Temple Of Gloom: two 0x1000000 records give pylon flag 8 and two tables" );
	}

	/// <summary>Every PC coaster builds the same circuit on a flat site, completes, rates within [0, 100] and runs.</summary>
	[TestMethod]
	public void EveryPcCoasterBuildsRatesAndRuns()
	{
		foreach ( var entry in PcCoasters() )
		{
			var world = new CoasterWorld( 3 );
			var ride = new CoasterRide( CoasterDefinition.FromEntry( entry ), world, entry.DisplayName );
			var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
			CoasterTests.PlaceStation( builder, (20, 20) );
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
			Assert.IsTrue( ride.Trains.All( train => train.Speed > 0 ), $"{entry.InfoId}: every train circulates and none stops" );
		}
	}

	/// <summary>
	/// A fresh Jungle park: the Temple Of Gloom station runs its original Coaster.RSE; the headless builder lays a
	/// circuit on free land (each pylon charged through the park economy), closes, completes and rates it; guests at the station's
	/// entrance queue, board through COAST 1, ride a lap and leave through COAST 3; closing lets the trains finish;
	/// deleting removes every pylon without a refund.
	/// </summary>
	[TestMethod]
	public void GuestsRideACoasterBuiltHeadlesslyOnJungle() => RideScenario( deleteWhileRiding: false );

	/// <summary>
	/// Deleting a running coaster (and removing its station) while guests ride, sit seated at the station and wait on the admission ring releases every
	/// one of them: nobody stays in the Using state and the bridge's boarded and released totals match.
	/// </summary>
	[TestMethod]
	public void DeletingARunningCoasterReleasesItsRidersAndAdmittedGuests() => RideScenario( deleteWhileRiding: true );

	private static void RideScenario( bool deleteWhileRiding )
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

		if ( deleteWhileRiding )
		{
			// Run until guests are on board or seated and others are admitted or queued, then take the coaster away mid-ride.
			// Whether one of the eight is still on its way when a train fills depends on the guests' decision and wander draws
			// (seven of them are refused at the join cell on the first turn and choose again later), so once two ride and
			// nobody waits, one late guest is sent to the entrance to queue behind them.
			var cut = false;
			Guest? late = null;
			bool Waiting( Guest guest ) => guest.State is GuestState.Queueing or GuestState.MovingUpQueue or GuestState.GoingToRide;
			for ( var tick = 0; tick < 240 * 60 && !cut; tick++ )
			{
				guests.Tick( Tick );
				runtime.Simulate( Tick );
				if ( late == null && ride.RidersOnBoard >= 2 && ride.AdmissionCount == 0 && !spawned.Any( Waiting ) )
				{
					late = guests.SpawnInPark( entrance.X, entrance.Y );
					late.AttractionId = runtime.Visitors.AttractionId;
					late.State = GuestState.GoingToRide;
					spawned = spawned.Append( late ).ToArray();
					continue;
				}
				cut = ride.RidersOnBoard >= 2 && (ride.AdmissionCount > 0 || spawned.Any( Waiting ));
			}
			Assert.IsTrue( cut, "guests were riding while others waited" );
			Assert.IsTrue( spawned.Any( guest => guest.State == GuestState.Using ), "a guest is using the coaster" );
			builder.Delete();
			runtime.Stop();
			for ( var tick = 0; tick < 120 * 60; tick++ )
				guests.Tick( Tick );
			Assert.IsFalse( spawned.Any( guest => guest.State == GuestState.Using ), $"no guest stays in Using: {string.Join( ", ", spawned.Select( guest => guest.State ) )}" );
			Assert.AreEqual( runtime.Visitors.BoardedTotal, runtime.Visitors.ReleasedTotal, "every boarded guest was released" );
			return;
		}

		var peakRiders = 0;
		var ranWithRiders = false;
		for ( var tick = 0; tick < 240 * 60 && runtime.Visitors.ReleasedTotal < 2; tick++ )
		{
			guests.Tick( Tick );
			runtime.Simulate( Tick );
			peakRiders = Math.Max( peakRiders, ride.RidersOnBoard );
			ranWithRiders |= ride.Trains.Any( train => train.Speed > 0 && train.Riders > 0 );
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
		Assert.IsTrue( ride.Trains.All( train => train.Speed > 0 ), "closed trains keep circulating, empty" );
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
