using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// Player-built roller coasters without game data (docs/COASTERS.md): the definition loader, each placement rule of
/// <c>0x36ba4</c> with its boundaries, the cost and its rounding, the rating arithmetic against the COAST witness's
/// single-precision values, the train count, and a build → run → board → stop → delete cycle on a flat site.
/// </summary>
[TestClass]
public class CoasterTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	/// <summary>The station's departure end ('&gt;') and return end ('&lt;') on the flat test site.</summary>
	private static readonly (int X, int Y) Depart = (20, 20), Return = (17, 20);
	/// <summary>A closed circuit (offsets from the depart end) that passes every window of the PC coasters.</summary>
	internal static readonly (int X, int Y)[] Circuit = { (4, 0), (7, 4), (4, 8), (-4, 8), (-8, 4), (-7, 0) };

	[TestInitialize]
	public void Init() => Log = new();

	/// <summary>The settings of a PC coaster: the pylon block of the Jungle coaster.sam files, nothing else.</summary>
	internal static Dictionary<string, string> PcSettings( int stacked = 4 ) => new( StringComparer.OrdinalIgnoreCase )
	{
		["asPylonControls[0].pcMeshFilename"] = "StdPylon",
		["asPylonControls[0].uiMaxStackedPylons"] = stacked.ToString(),
		["asPylonControls[0].fMinHeight"] = "10",
		["asPylonControls[0].fMaxHeight"] = "100",
		["asPylonControls[0].fCostPerUnit"] = "40.0",
		["asPylonControls[0].fCostPer2DLength"] = "10.0",
		["asPylonControls[0].fCostPerHeight"] = "10.0",
		["asPylonControls[1].pcMeshFilename"] = "LoopPylon",
		["asPylonControls[1].uiMaxStackedPylons"] = stacked.ToString(),
		["asPylonControls[1].fMinHeight"] = "10",
		["asPylonControls[1].fMaxHeight"] = "100",
		["asPylonControls[1].fCostPerUnit"] = "60.0",
		["asPylonControls[1].fCostPer2DLength"] = "15.0",
		["asPylonControls[1].fCostPerHeight"] = "15.0",
		["sTrainType.uiMaxCars"] = "4",
		["sCoasterType.uiMaxTrains"] = "6",
		["sCoasterType.uiTrainReleaseRate"] = "30000"
	};

	private static (CoasterBuilder Builder, CoasterWorld World) NewCoaster( Dictionary<string, string>? settings = null, CoasterWorld? world = null, Func<int, int, bool>? blocked = null, int size = 64, (int X, int Y)? depart = null, ParkEconomy? economy = null )
	{
		world ??= new CoasterWorld( 1 );
		var ride = new CoasterRide( CoasterDefinition.FromValues( settings ?? PcSettings() ), world, "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( size, size, blocked ), () => economy );
		var start = depart ?? Depart;
		builder.SetStation( start, (start.X - 3, start.Y) );
		return (builder, world);
	}

	private static void BuildCircuit( CoasterBuilder builder, (int X, int Y)? depart = null )
	{
		var start = depart ?? Depart;
		foreach ( var (dx, dy) in Circuit )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( start.X + dx, start.Y + dy ), $"pylon at offset ({dx}, {dy})" );
	}

	/// <summary>Closes the circuit with a click on the station's return end, then completes it.</summary>
	internal static CoasterPlaceResult CloseAndComplete( CoasterBuilder builder )
	{
		var back = builder.Track.ReturnCell!.Value;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( back.X, back.Y ), "a click on the chain's start closes the circuit" );
		Assert.IsTrue( builder.Track.IsClosed );
		return builder.Complete();
	}

	// ---- Definition loader (0x2d274 defaults, 0x2fde8 nonzero override) -----------------------------------

	[TestMethod]
	public void LoaderKeepsTheCodeDefaultsForAbsentAndZeroValues()
	{
		var definition = CoasterDefinition.FromValues( new Dictionary<string, string>
		{
			["sCoasterType.fWinchSpeed"] = "0",
			["sCoasterType.uiMaxTrains"] = "0",
			["asPylonControls[0].uiMinPlaceDist"] = "0",
			["asPylonControls[0].fMinXZAngleAtMinDist"] = "0.0"
		} );
		Assert.AreEqual( 5f, definition.WinchSpeed, "zero keeps the default" );
		Assert.AreEqual( 5f, definition.MinSpeed );
		Assert.AreEqual( 1.5f, definition.AccelerationPerHeight );
		Assert.AreEqual( 0.8f, definition.FrictionMultiplier );
		Assert.AreEqual( -0.3f, definition.GravityY );
		Assert.AreEqual( 80f, definition.MaxSpeedAtMinSetting );
		Assert.AreEqual( 130f, definition.MaxSpeedAtMaxSetting );
		Assert.AreEqual( 1, definition.MaxTrains );
		Assert.AreEqual( 15000, definition.TrainReleaseRate );
		Assert.AreEqual( 1f, definition.InitialExcitement );
		Assert.AreEqual( 1.7f, CoasterRating.PeakExcitementWeight );
		Assert.AreEqual( 3, definition.StandardPylon.MinPlaceDistance );
		Assert.AreEqual( 8, definition.StandardPylon.MaxPlaceDistance );
		Assert.AreEqual( 9, definition.LoopPylon.MaxPlaceDistance );
		Assert.AreEqual( 80 * CoasterDefinition.DegreesToRadians, definition.StandardPylon.MinAngleAtMinDistance );
		Assert.AreEqual( 0f, definition.StandardPylon.CostPerUnit, "absent cost rates are 0" );
		Assert.IsFalse( definition.HasLoopPylon );
		Assert.AreEqual( CoasterDefinition.AlwaysFlags | CoasterDefinition.AutoBankFlag, definition.Flags );
	}

	[TestMethod]
	public void LoaderCopiesNonzeroValuesAndTheCostRatesUnconditionally()
	{
		var definition = CoasterDefinition.FromValues( new Dictionary<string, string>
		{
			["sCoasterType.fAccelerationPerHeight"] = "0.8",
			["sCoasterType.fFrictionMultiplier"] = "0.95",
			["sCoasterType.uiMaxTrains"] = "20",
			["sCoasterType.uiTrainReleaseRate"] = "16000",
			["sCoasterType.bSpecialIsTrough"] = "1",
			["sCoasterType.bNoAutoBanking"] = "1",
			["sCoasterType.bVisibleEndPylon"] = "1",
			["asPylonControls[0].fMinHeight"] = "10",
			["asPylonControls[0].fMaxHeight"] = "100",
			["asPylonControls[0].fCostPerUnit"] = "0",
			["asPylonControls[0].fCostPer2DLength"] = "10.0",
			["asPylonControls[0].fMinXZAngleAtMinDist"] = "90",
			["asPylonControls[1].fMaxLoopHeightDifference"] = "2"
		} );
		Assert.AreEqual( 0.8f, definition.AccelerationPerHeight );
		Assert.AreEqual( 0.95f, definition.FrictionMultiplier );
		Assert.AreEqual( 20, definition.MaxTrains );
		Assert.AreEqual( 16000, definition.TrainReleaseRate );
		Assert.AreEqual( 100f, definition.StandardPylon.MaxHeight );
		Assert.AreEqual( 0f, definition.StandardPylon.CostPerUnit, "a zero cost rate is copied" );
		Assert.AreEqual( 10f, definition.StandardPylon.CostPer2DLength );
		Assert.AreEqual( 90 * CoasterDefinition.DegreesToRadians, definition.StandardPylon.MinAngleAtMinDistance, "degrees × single(π/180)" );
		Assert.AreEqual( 2 * 10 * CoasterDefinition.DegreesToRadians, definition.LoopPylon.MaxLoopHeightDifference, "× 10 × π/180" );
		Assert.IsTrue( definition.IsTrough );
		Assert.IsFalse( definition.AutoBanks );
		Assert.AreEqual( CoasterDefinition.AlwaysFlags | CoasterDefinition.VisibleEndPylonFlag | CoasterDefinition.TroughFlag, definition.Flags );
		Assert.IsTrue( definition.LoopPylon.IsTrough && !definition.LoopPylon.IsLoop, "bSpecialIsTrough turns pylon 1 into a trough pylon" );
		Assert.IsTrue( definition.HasLoopPylon );
	}

	// ---- Placement validator 0x36ba4, in test order --------------------------------------------------------

	[TestMethod]
	public void MapBoundsComeFirst()
	{
		var (builder, _) = NewCoaster( size: 25, blocked: ( x, y ) => true );
		Assert.AreEqual( CoasterPlaceResult.OutsideMap, builder.Check( 25, 20 ), "x = width is outside, before the blocked test" );
		Assert.AreEqual( CoasterPlaceResult.OutsideMap, builder.Check( 23, -1 ) );
		Assert.AreEqual( CoasterPlaceResult.Blocked, builder.Check( 24, 20 ), "x = width − 1 is inside" );
	}

	[TestMethod]
	public void FailedValidationMarksThePylonAndItsPredecessorInsteadOfRefusing()
	{
		var blocked = new HashSet<(int, int)> { (24, 20) };
		var (builder, _) = NewCoaster( blocked: ( x, y ) => blocked.Contains( (x, y) ) );
		Assert.AreEqual( CoasterPlaceResult.Blocked, builder.Check( 24, 20 ) );
		Assert.AreEqual( CoasterPlaceResult.Blocked, builder.Place( 24, 20 ), "a no-ground cell: the pylon is still placed" );
		Assert.AreEqual( 2, builder.Track.Nodes.Count );
		Assert.IsTrue( builder.Track.Nodes.All( node => node.Flags.HasFlag( CoasterNodeFlags.Invalid ) ), "node and predecessor get flag 0x4000" );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Move( 1, 23, 20 ) );
		Assert.IsFalse( builder.Track.HasInvalidNodes, "the flag clears once validation passes" );
		Assert.AreEqual( CoasterPlaceResult.TooClose, builder.Place( 25, 21 ), "a too-short link is placed and marked too" );
		Assert.IsTrue( builder.Track.Nodes[^1].Flags.HasFlag( CoasterNodeFlags.Invalid ) && builder.Track.Nodes[^2].Flags.HasFlag( CoasterNodeFlags.Invalid ) );
		Assert.IsTrue( builder.RemoveLast() );
		Assert.IsFalse( builder.Track.HasInvalidNodes );
		Assert.AreEqual( CoasterPlaceResult.OutsideMap, builder.Place( -1, 20 ), "only structural refusals leave the track unchanged" );
		Assert.AreEqual( 2, builder.Track.Nodes.Count );
	}

	[TestMethod]
	public void TheSiteBlocksOnlyNoGroundCellsFrozenAtStart()
	{
		var grid = new TestGrid();
		grid.Results[(5, 5)] = OriginalPlacementResult.NoTerrainSurface;
		grid.Results[(6, 6)] = OriginalPlacementResult.Water;
		grid.Results[(7, 7)] = OriginalPlacementResult.Path;
		var site = CoasterSite.FromGrid( grid );
		grid.Results[(8, 8)] = OriginalPlacementResult.NoTerrainSurface;
		Assert.IsTrue( site.IsBlocked( 5, 5 ), "no ground" );
		Assert.IsFalse( site.IsBlocked( 6, 6 ), "water has ground" );
		Assert.IsFalse( site.IsBlocked( 7, 7 ), "paths do not block pylons" );
		Assert.IsFalse( site.IsBlocked( 8, 8 ), "the snapshot is taken once" );
		Assert.AreEqual( 15f, site.GroundHeight( 1, 1 ), "engine height / 0.2" );
	}

	private sealed class TestGrid : IParkGrid
	{
		public Dictionary<(int, int), OriginalPlacementResult> Results { get; } = new();
		public int Width => 16;
		public int Height => 16;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => Results.GetValueOrDefault( (x, y), OriginalPlacementResult.Allowed );
		public float GetCellHeight( int x, int y ) => 3;
	}

	[TestMethod]
	public void SameCellAsThePreviousNodeIsRefused()
	{
		var (builder, _) = NewCoaster();
		Assert.AreEqual( CoasterPlaceResult.SameCell, builder.Check( Depart.X, Depart.Y ) );
	}

	[TestMethod]
	public void DistanceWindowUsesTheSquaredDistanceWithInclusiveBounds()
	{
		var (builder, _) = NewCoaster();
		Assert.AreEqual( CoasterPlaceResult.TooClose, builder.Check( 22, 22 ), "d² = 8 < 9" );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Check( 23, 20 ), "d² = 9 = min²" );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Check( 28, 20 ), "d² = 64 = max²" );
		Assert.AreEqual( CoasterPlaceResult.TooFar, builder.Check( 28, 21 ), "d² = 65 > 64" );
		var definition = CoasterDefinition.Default;
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTrack.CheckLink( definition.LoopPylon, (9, 0), (0, 0), null, false ), "LoopPylon: d² = 81 = 9²" );
		Assert.AreEqual( CoasterPlaceResult.TooFar, CoasterTrack.CheckLink( definition.LoopPylon, (9, 1), (0, 0), null, false ) );
		Assert.AreEqual( CoasterPlaceResult.TooFar, CoasterTrack.CheckLink( definition.StandardPylon, (9, 0), (0, 0), null, false ), "StdPylon: 81 > 64" );
		Assert.AreEqual( CoasterPlaceResult.TooClose, CoasterTrack.CheckLink( definition.LoopPylon, (2, 2), (0, 0), null, false ) );
	}

	[TestMethod]
	public void EntranceWindowAppliesAfterTheStationNode()
	{
		var (builder, _) = NewCoaster();
		// 135° at d = √18: t = 0.249, lo = 140°(1 − t) + 110°t = 132.5° (witness: 2.3133 rad).
		Assert.AreEqual( 2.3561945f, CoasterTrack.TurnAngle( (23, 23), Depart, Return ), 1e-6f );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Check( 23, 23 ) );
		// 123.7° at d = √13: lo = 136.4° (witness: 2.1588 < 2.3800).
		Assert.AreEqual( CoasterPlaceResult.TurnTooSharp, builder.Check( 22, 23 ) );
		var (low, high) = CoasterTrack.AngleWindow( builder.Ride.Definition.StandardPylon, MathF.Sqrt( 18 ), entrance: true );
		Assert.AreEqual( 2.3133318f, low, 2e-6f );
		Assert.AreEqual( MathF.PI, high );
	}

	[TestMethod]
	public void NormalWindowAfterOtherNodesAndItsBoundaries()
	{
		var (builder, _) = NewCoaster();
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Check( 24, 24 ), "90° ≥ lo 74° at d = 4" );
		Assert.AreEqual( CoasterPlaceResult.TurnTooSharp, builder.Check( 21, 23 ), "45° < lo 72.5° at d = √18" );
		// The window is inclusive: an angle equal to lo passes, the next single above it fails.
		var pylon = CoasterDefinition.Default.StandardPylon.Clone();
		var angle = CoasterTrack.TurnAngle( (4, 4), (0, 0), (-4, 0) );
		pylon.MinAngleAtMinDistance = pylon.MinAngleAtMaxDistance = angle;
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTrack.CheckLink( pylon, (4, 4), (0, 0), (-4, 0), false ) );
		pylon.MinAngleAtMinDistance = pylon.MinAngleAtMaxDistance = MathF.BitIncrement( angle );
		Assert.AreEqual( CoasterPlaceResult.TurnTooSharp, CoasterTrack.CheckLink( pylon, (4, 4), (0, 0), (-4, 0), false ) );
		pylon.MinAngleAtMinDistance = pylon.MinAngleAtMaxDistance = 0;
		pylon.MaxAngleAtMinDistance = pylon.MaxAngleAtMaxDistance = angle;
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTrack.CheckLink( pylon, (4, 4), (0, 0), (-4, 0), false ), "an angle equal to hi passes" );
		pylon.MaxAngleAtMinDistance = pylon.MaxAngleAtMaxDistance = MathF.BitDecrement( angle );
		Assert.AreEqual( CoasterPlaceResult.TurnTooWide, CoasterTrack.CheckLink( pylon, (4, 4), (0, 0), (-4, 0), false ) );
	}

	[TestMethod]
	public void LoopPylonWindowAppliesInLoopModeAndAfterALoopNode()
	{
		var (builder, _) = NewCoaster();
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		builder.LoopMode = true;
		Assert.AreEqual( CoasterPlaceResult.TurnTooWide, builder.Check( 28, 20 ), "straight on (180°) is above the LoopPylon's 174°" );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Check( 24, 29 ), "a 9-cell link only the LoopPylon allows" );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 26 ) );
		builder.LoopMode = false;
		Assert.AreEqual( CoasterPlaceResult.TurnTooWide, builder.Check( 24, 30 ), "after a loop node the LoopPylon window still applies" );
		var (flume, _) = NewCoaster( new Dictionary<string, string> { ["sCoasterType.bSpecialIsTrough"] = "1" } );
		flume.LoopMode = true;
		Assert.AreEqual( CoasterPlaceResult.NoLoopPylon, flume.Check( 24, 20 ), "without asPylonControls[1] there is no loop pylon" );
	}

	/// <summary>A loop around the first pylon that ends on its cell again (every link passes its windows).</summary>
	private static readonly (int X, int Y)[] Revisit = { (24, 20), (28, 24), (24, 28), (20, 24) };

	[TestMethod]
	public void StackingNeedsTheSameCoasterAndPylonKindBelowTheLimit()
	{
		var (single, _) = NewCoaster( PcSettings( stacked: 1 ) );
		foreach ( var (x, y) in Revisit )
			Assert.AreEqual( CoasterPlaceResult.Ok, single.Place( x, y ) );
		Assert.AreEqual( CoasterPlaceResult.StackFull, single.Check( 24, 20 ), "uiMaxStackedPylons 1" );

		var (stacked, world) = NewCoaster( PcSettings( stacked: 3 ) );
		foreach ( var (x, y) in Revisit )
			Assert.AreEqual( CoasterPlaceResult.Ok, stacked.Place( x, y ) );
		stacked.LoopMode = true;
		Assert.AreEqual( CoasterPlaceResult.OtherPylonKind, stacked.Check( 24, 20 ) );
		stacked.LoopMode = false;
		Assert.AreEqual( CoasterPlaceResult.Ok, stacked.Place( 24, 20 ) );
		Assert.AreEqual( 1, stacked.Track.Nodes[^1].StackIndex );

		var (other, _) = NewCoaster( PcSettings( stacked: 3 ), world, depart: (20, 16) );
		Assert.AreEqual( CoasterPlaceResult.OtherCoaster, other.Check( 24, 20 ), "a cell holding another coaster's pylon" );
		Assert.AreEqual( CoasterPlaceResult.Ok, other.Check( 24, 16 ) );
	}

	[TestMethod]
	public void MovingRevalidatesTheNextTwoLinksAndTheStationStays()
	{
		var (builder, _) = NewCoaster();
		BuildCircuit( builder );
		Assert.AreEqual( CoasterPlaceResult.NotEditable, builder.Move( 0, 21, 25 ), "the station node cannot be picked up" );
		Assert.AreEqual( CoasterPlaceResult.BreaksNextLink, builder.Move( 1, 25, 22 ), "P2 at (27,24) would be √8 cells away (below 3)" );
		Assert.AreEqual( (25, 22), (builder.Track.Nodes[1].X, builder.Track.Nodes[1].Y), "the drop is kept" );
		Assert.IsTrue( builder.Track.Nodes[2].Flags.HasFlag( CoasterNodeFlags.Invalid ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Move( 1, 25, 20 ) );
		Assert.AreEqual( (25, 20), (builder.Track.Nodes[1].X, builder.Track.Nodes[1].Y) );
		Assert.IsFalse( builder.Track.HasInvalidNodes );
	}

	[TestMethod]
	public void HeightIsClampedNeverRefused()
	{
		var pylon = CoasterDefinition.FromValues( PcSettings() ).StandardPylon;
		Assert.AreEqual( (55f, 0.5f), CoasterTrack.ClampHeight( pylon, 55 ) );
		Assert.AreEqual( (10f, 0f), CoasterTrack.ClampHeight( pylon, 5 ) );
		Assert.AreEqual( (100f, 1f), CoasterTrack.ClampHeight( pylon, 150 ) );
		Assert.AreEqual( (10f, 0f), CoasterTrack.ClampHeight( CoasterDefinition.Default.StandardPylon, 40 ), "the code default 10..10 pins the height" );
		var (builder, _) = NewCoaster();
		builder.Height = 250;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		Assert.AreEqual( 100f, builder.Track.Nodes[1].Height );
		Assert.AreEqual( 1f, builder.Track.Nodes[1].T );
	}

	// ---- Cost 0x4b168 and rounding 0x41aa4 ------------------------------------------------------------------

	[TestMethod]
	public void PylonCostIsUnitPlusLengthAndHeightRatesRoundedUp()
	{
		var definition = CoasterDefinition.FromValues( PcSettings() );
		Assert.AreEqual( 80f, CoasterTrack.PylonCost( definition.StandardPylon, 30, 10 ), "40 + 10 × 3 + 10 × 1" );
		Assert.AreEqual( 80, CoasterTrack.RoundUp( 80f ) );
		var diagonal = CoasterTrack.PylonCost( definition.StandardPylon, MathF.Sqrt( 18 ) * 10, 10 );
		Assert.AreEqual( 92.426407f, diagonal, 1e-4f );
		Assert.AreEqual( 93, CoasterTrack.RoundUp( diagonal ) );
		Assert.AreEqual( 187.5f, CoasterTrack.PylonCost( definition.LoopPylon, 50, 35 ), "60 + 15 × 5 + 15 × 3.5" );
		Assert.AreEqual( 188, CoasterTrack.RoundUp( 187.5f ) );
		Assert.AreEqual( 8, CoasterTrack.RoundUp( MathF.BitIncrement( 7f ) ), "any fraction adds one" );
		Assert.AreEqual( 7, CoasterTrack.RoundUp( 7f ) );
		Assert.AreEqual( -1, CoasterTrack.RoundUp( -2.5f ), "modf keeps the sign of the fraction: −2 + 1" );
		Assert.AreEqual( 40f, CoasterTrack.PylonCost( definition.StandardPylon, 0, 0 ) );
		Assert.AreEqual( 0f, CoasterTrack.PylonCost( definition.StandardPylon, 0, 0, chargeUnit: false ) );
		Assert.AreEqual( 25f, definition.LoopPylon.MaxLoopHeightDifference, "the raw default 25.0 (0x2d5d4)" );
	}

	[TestMethod]
	public void PlacingChargesTheTruncatedCostAtOnceAndCompletionNothing()
	{
		var economy = EconomyTestData.Park( initialCash: 50000 );
		var (builder, _) = NewCoaster( economy: economy );
		builder.Height = 10;
		var costs = new List<float>();
		foreach ( var (dx, dy) in Circuit )
		{
			var before = economy.Balance;
			var preview = builder.PreviewCost( Depart.X + dx, Depart.Y + dy );
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( Depart.X + dx, Depart.Y + dy ) );
			Assert.AreEqual( CoasterTrack.Truncate( preview ), before - economy.Balance, "trunc(cost) at once" );
			costs.Add( preview );
		}
		// 40 + 4 × 10 + 10 = 90, then the 5-, 5-, 8-, √32-, √17-cell links: 100, 100, 130, 106.57, 91.23.
		CollectionAssert.AreEqual( new long[] { 90, 100, 100, 130, 106, 91 }, costs.Select( CoasterTrack.Truncate ).ToArray() );
		Assert.AreEqual( 50000 - 617, economy.Balance );
		Assert.AreEqual( 90 + 100 + 100 + 130 + 107 + 92, builder.BuildCost, "the display rounds each pylon up" );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		Assert.AreEqual( 50000 - 617, economy.Balance, "closing and completing move no money" );
		Assert.AreEqual( 617, builder.Ride.PaidCost );
		builder.Delete();
		Assert.AreEqual( 50000 - 617, economy.Balance, "deleting refunds nothing" );
	}

	[TestMethod]
	public void MovesAndHeightChangesRefundTruncatedAndChargeRoundedUp()
	{
		var economy = EconomyTestData.Park( initialCash: 50000 );
		var (builder, _) = NewCoaster( economy: economy );
		BuildCircuit( builder );
		var balance = economy.Balance;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Move( 1, 24, 20 ), "same cell" );
		Assert.AreEqual( balance, economy.Balance, "a drop on its own cell moves no money" );
		var old = builder.Track.NodeCost( 1 );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Move( 1, 25, 20 ) );
		var now = builder.Track.NodeCost( 1 );
		Assert.AreEqual( (90f, 100f), (old, now) );
		Assert.AreEqual( balance + CoasterTrack.Truncate( old ) - CoasterTrack.RoundUp( now ), economy.Balance, "open track: the node only" );
		balance = economy.Balance;
		old = builder.Track.NodeCost( 5 );
		Assert.IsTrue( builder.SetHeight( 5, 55 ) );
		Assert.AreEqual( 55f, builder.Track.Nodes[5].Height );
		Assert.AreEqual( balance + CoasterTrack.Truncate( old ) - CoasterTrack.RoundUp( builder.Track.NodeCost( 5 ) ), economy.Balance );
		Assert.IsTrue( builder.RemoveLast() );
		Assert.AreEqual( balance + CoasterTrack.Truncate( old ) - CoasterTrack.RoundUp( builder.Track.NodeCost( 5 ) ) + 0, economy.Balance + 0, "deleting a pylon refunds nothing" );
	}

	/// <summary>Moves and the height slider ask 0x4b168 without CostPerUnit (r4 = 0); a fractional unit shows it in the rounding.</summary>
	[TestMethod]
	public void MovesAndTheHeightSliderDoNotChargeTheUnit()
	{
		var settings = PcSettings();
		settings["asPylonControls[0].fCostPerUnit"] = "40.5";
		var economy = EconomyTestData.Park( initialCash: 50000 );
		var (builder, _) = NewCoaster( settings, economy: economy );
		BuildCircuit( builder );
		Assert.AreEqual( 90.5f, builder.Track.NodeCost( 1 ), "the place path: 40.5 + 10 × 4 + 10 × 1" );
		Assert.AreEqual( 50f, builder.Track.NodeCost( 1, chargeUnit: false ) );
		var balance = economy.Balance;
		var (income, costs) = (Total( economy, LedgerCategory.OtherIncome ), Total( economy, LedgerCategory.OtherCosts ));
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Move( 1, 25, 20 ) );
		Assert.AreEqual( 60f, builder.Track.NodeCost( 1, chargeUnit: false ) );
		Assert.AreEqual( 50, Total( economy, LedgerCategory.OtherIncome ) - income, "credit trunc(50), not trunc(90.5)" );
		Assert.AreEqual( 60, Total( economy, LedgerCategory.OtherCosts ) - costs, "debit ceil(60), not ceil(100.5)" );
		Assert.AreEqual( balance - 10, economy.Balance );
		balance = economy.Balance;
		Assert.IsTrue( builder.SetHeight( 1, 15 ) );
		Assert.AreEqual( balance - 5, economy.Balance, "slider: trunc(60) back, ceil(65) charged" );
	}

	private static long Total( ParkEconomy economy, LedgerCategory category ) => economy.Ledger.CurrentTotals.TryGetValue( category, out var value ) ? value : 0;

	/// <summary>The place path reads the balance, ignores it and debits (0x378a0, 0x378c0).</summary>
	[TestMethod]
	public void PlacingDebitsEvenWithoutTheMoney()
	{
		var economy = EconomyTestData.Park( initialCash: 50000 );
		var (builder, _) = NewCoaster( economy: economy );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TrySpendCoasterTrack( economy.Balance - 89 ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ), "90 > 89 is not refused" );
		Assert.AreEqual( 2, builder.Track.Nodes.Count );
		Assert.AreEqual( -1, economy.Balance );
		Assert.AreEqual( 90, builder.Ride.PaidCost );
	}

	/// <summary>A loop or trough pylon pays the StdPylon length and height rates unless it stands on a loop or trough pylon; a loop pylon always does here (0x4b294..0x4b2d4).</summary>
	[TestMethod]
	public void LoopPylonsPayTheStandardLengthAndHeightRates()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 10;
		builder.LoopMode = true;
		Assert.AreEqual( 60f + 40f + 10f, builder.PreviewCost( 24, 20 ), "LoopPylon unit 60, StdPylon rates 10 × 4 + 10 × 1" );
		var settings = PcSettings();
		settings["sCoasterType.bSpecialIsTrough"] = "1";
		var (flume, _) = NewCoaster( settings );
		flume.Height = 10;
		flume.LoopMode = true;
		flume.Place( 24, 20 );
		Assert.AreEqual( 110f, flume.Track.NodeCost( 1 ), "a trough pylon on the ground: StdPylon rates" );
	}

	[TestMethod]
	public void ClosingThenCompletingAppendsTheStationEndNodes()
	{
		var (builder, _) = NewCoaster();
		foreach ( var (dx, dy) in Circuit.Take( 4 ) )
			builder.Place( Depart.X + dx, Depart.Y + dy );
		Assert.AreEqual( CoasterPlaceResult.NotClosed, builder.Complete() );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( Return.X, Return.Y ), "the click closes, no node is added" );
		Assert.AreEqual( 5, builder.Track.Nodes.Count );
		Assert.AreEqual( CoasterPlaceResult.TooFar, builder.Track.CheckClosure(), "(16,28) → (17,20) is 8.06 cells" );
		Assert.IsTrue( builder.Track.Nodes[^1].Flags.HasFlag( CoasterNodeFlags.Invalid ) );
		Assert.AreEqual( CoasterPlaceResult.InvalidNodes, builder.Complete() );
		Assert.AreEqual( CoasterPlaceResult.NotEditable, builder.Check( 12, 24 ), "a closed chain takes no new pylon" );
		Assert.IsTrue( builder.RemoveLast() && !builder.Track.IsClosed, "deleting a pylon opens the circuit" );
		foreach ( var (dx, dy) in Circuit.Skip( 3 ) )
			builder.Place( Depart.X + dx, Depart.Y + dy );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		Assert.IsTrue( builder.Track.IsRunnable );
		var ends = builder.Track.Nodes.Where( node => node.IsStationEnd ).ToArray();
		Assert.AreEqual( 1, ends.Length, "the loader always sets 0x20: one end node even without bVisibleEndPylon" );
		Assert.AreEqual( (Return.X, Return.Y), (ends[0].X, ends[0].Y) );
		Assert.AreEqual( (CoasterNodeFlags)0x144, ends[0].Flags & ~CoasterNodeFlags.Winch, "0x37e34: flags 0x144" );
		Assert.AreEqual( new SVector3( 0, -1, 0 ), builder.Track.BuildKnots( builder.Site )[^1].Direction, "the 0x144 end node (0x40 set, 0x80 clear) points its knot down (0x32DFC)" );
		Assert.IsFalse( builder.Ride.World.HasPylon( Return.X, Return.Y ), "the end node is no pylon" );
		var settings = PcSettings();
		settings["sCoasterType.bVisibleEndPylon"] = "1";
		var (visible, _) = NewCoaster( settings );
		BuildCircuit( visible );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( visible ) );
		Assert.AreEqual( 1, visible.Track.Nodes.Count( node => node.IsStationEnd ), "one end node with bVisibleEndPylon" );
		Assert.IsTrue( visible.Reopen() && !visible.Track.IsClosed && visible.Track.Nodes.All( node => !node.IsStationEnd ) );
	}

	// ---- Rating 0x415b4 / 0x41900, speed, trains, ride excitement -------------------------------------------

	/// <summary>
	/// Five steps through decay, g-force, banking, an inversion with negative vertical force and both screams; expected
	/// bits from the PPC differential oracle's verified formula (ORACLE.md F7: outside the inverted arm the discomfort
	/// adds Fz; separate single multiplies and adds), which agrees with the original 0x415b4 on 509/509 inputs.
	/// </summary>
	[TestMethod]
	public void RatingStepAndFinalMatchTheWitnessInSinglePrecision()
	{
		var rating = new CoasterRating( CoasterDefinition.Default );
		var steps = new (int Index, float Speed, float Bank, SVector3 Force, uint Sickness, uint Excitement, CoasterSampleFlags Flags)[]
		{
			(1, 10, 0, new SVector3( 0, 0, 0 ), 0x00000000, 0x3FC00000, CoasterSampleFlags.None),
			(2, 20, 0.5f, new SVector3( 0.5f, 1, -0.25f ), 0x3F600000, 0x40D80000, CoasterSampleFlags.None),
			(3, 40, 2, new SVector3( 0.25f, -0.5f, 0 ), 0x40A3341C, 0x4114CBE4, CoasterSampleFlags.NormalScream | CoasterSampleFlags.TerrorScream),
			(4, 60, 0, new SVector3( 0, 2, 0 ), 0x4063341C, 0x414A65F2, CoasterSampleFlags.NormalScream | CoasterSampleFlags.TerrorScream),
			(5, 5, 0, new SVector3( 0, 0, 0 ), 0x3FE3341C, 0x40DA65F2, CoasterSampleFlags.None)
		};
		foreach ( var step in steps )
		{
			var flags = rating.Step( step.Index, step.Speed, step.Bank, step.Force );
			Assert.AreEqual( step.Sickness, BitConverter.SingleToUInt32Bits( rating.Sickness ), $"S after step {step.Index}: {rating.Sickness}" );
			Assert.AreEqual( step.Excitement, BitConverter.SingleToUInt32Bits( rating.Excitement ), $"E after step {step.Index}: {rating.Excitement}" );
			Assert.AreEqual( step.Flags, flags, $"screams at step {step.Index}" );
		}
		Assert.AreEqual( 0x40A3341Cu, BitConverter.SingleToUInt32Bits( rating.PeakSickness ) );
		Assert.AreEqual( 0x414A65F2u, BitConverter.SingleToUInt32Bits( rating.PeakExcitement ) );
		var (sickness, excitement) = rating.Final();
		Assert.AreEqual( 0x40DC0123u, BitConverter.SingleToUInt32Bits( sickness ), $"{sickness}" );
		Assert.AreEqual( 0x41C75699u, BitConverter.SingleToUInt32Bits( excitement ), $"{excitement}" );
		Assert.AreEqual( 62, CoasterRating.GuestExcitement( excitement ), "trunc(0.5 × 24.92 + 50)" );
		var final = CoasterRating.FinalFor( rating.Sickness, rating.Excitement, rating.PeakSickness, rating.PeakExcitement, new uint[] { 0, 0x40, 0x80, 0xC0, 0x1C0 }, 10, 3, 1, 2 );
		Assert.AreEqual( (0x40DC0123u, 0x41C75699u, 2, 30f), (BitConverter.SingleToUInt32Bits( final.Sickness ), BitConverter.SingleToUInt32Bits( final.Excitement ), final.Sections, final.Length ),
			"sections with both 0x40 and 0x80 (ORACLE.md F8)" );
	}

	[TestMethod]
	public void TheRatingStepIsAnOracleSeam()
	{
		// The discomfort's fallback term is Fz (signed): with u = 0, dU = |Fx| + Fz.
		var factors = CoasterRatingFactors.From( CoasterDefinition.Default ) with { Speed = 0, GForceExcitement = 0, BankExcitement = 0, GForceSickness = 0 };
		var rating = new CoasterRating( factors, 0, 0 );
		rating.Restore( 0, 4, 0, 4, 4, normalScream: true, terrorScream: false );
		var flags = rating.Step( 7, 0, 0, new SVector3( 0, 0, -3 ), (CoasterSampleFlags)0xFF );
		Assert.AreEqual( 2f + 3f, rating.Excitement, "½·4 + max(0 − (0 + −3), 0)" );
		Assert.AreEqual( (CoasterSampleFlags)0xF8 | CoasterSampleFlags.NormalScream, flags, "bits 1, 2, 4 replaced: a rise of 1 > 0.5 starts a normal scream; 1 ≤ 1.0 starts no terror scream" );
		Assert.IsTrue( rating.NormalScream && !rating.TerrorScream );
		Assert.AreEqual( 0f, rating.Sickness );
	}

	[TestMethod]
	public void FinalRatingsAreClampedToZeroAndOneHundred()
	{
		var rating = new CoasterRating( CoasterDefinition.Default );
		for ( var index = 1; index < 10; index++ )
			rating.Step( index, 1000, 0, new SVector3( 0, 100, 0 ) );
		Assert.AreEqual( (100f, 100f), rating.Final() );
	}

	[TestMethod]
	public void GuestExcitementIsHalfTheRatingPlusFifty()
	{
		Assert.AreEqual( 50, CoasterRating.GuestExcitement( 0 ) );
		Assert.AreEqual( 100, CoasterRating.GuestExcitement( 100 ) );
		Assert.AreEqual( 68, CoasterRating.GuestExcitement( 37.5f ), "68.75 truncates" );
	}

	[TestMethod]
	public void TrainCountIsLapOverHeadwayBetweenOneAndTheMaximum()
	{
		Assert.AreEqual( 1, CoasterRating.TrainCount( 20, 15000, 6 ) );
		Assert.AreEqual( 2, CoasterRating.TrainCount( 31, 15000, 6 ) );
		Assert.AreEqual( 2, CoasterRating.TrainCount( 30, 15000, 6 ), "exactly two headways" );
		Assert.AreEqual( 1, CoasterRating.TrainCount( 29.999f, 15000, 6 ) );
		Assert.AreEqual( 6, CoasterRating.TrainCount( 100, 15000, 6 ), "uiMaxTrains caps" );
		Assert.AreEqual( 1, CoasterRating.TrainCount( 0.5f, 15000, 6 ), "at least one train" );
		Assert.AreEqual( 1, CoasterRating.TrainCount( 1000, 15000, 1 ), "default uiMaxTrains" );
	}

	[TestMethod]
	public void SpeedStepFloorsAtTheWinchSpeedAndMarksWinchSections()
	{
		var definition = CoasterDefinition.Default;
		Assert.AreEqual( (20f, false), CoasterRating.NextSpeed( definition, 5, 10, 0, false, 80 ), "10 units down: 5 + 1.5 × 10" );
		Assert.AreEqual( (5f, true), CoasterRating.NextSpeed( definition, 5, 0, 10, false, 80 ), "uphill: floored at the winch speed" );
		Assert.AreEqual( (80f, false), CoasterRating.NextSpeed( definition, 70, 10, 0, false, 80 ), "capped by controller +52" );
		Assert.AreEqual( (16f, false), CoasterRating.NextSpeed( definition, 20, 0, 0, true, 80 ), "friction 0.8 on flag-8 samples" );
	}

	/// <summary>
	/// The pass's force is a = ((p₋ − p) + (p₊ − p))/spacing × −1 and F = a·(v/10) (0x3fcf0..0x3fe24, 0x3ff60); the first
	/// sample gets the winch speed and no force, the last the speed and force of the one before; max height starts at 0.
	/// </summary>
	[TestMethod]
	public void RatingPassForceSignAndEndSamples()
	{
		var definition = CoasterDefinition.Default;
		var samples = new CoasterSample[5];
		var heights = new[] { -5f, -10f, -20f, -10f, -5f };
		for ( var index = 0; index < samples.Length; index++ )
			samples[index] = new CoasterSample { Position = new SVector3( 3 * index, heights[index], 0 ), Speed = 99, Force = new SVector3( 7, 7, 7 ) };
		var (result, _) = CoasterRating.RatePass( definition, samples, 3, 110 );
		var dip = samples[2];
		var a = ((-10f - -20f) + (-10f - -20f)) / 3f * -1f;
		Assert.AreEqual( Bits( a * (dip.Speed / 10f) ), Bits( dip.Force.Y ), "a dip gives a negative Fy" );
		Assert.IsTrue( dip.Force.Y < 0 );
		Assert.AreEqual( (definition.WinchSpeed, SVector3.Zero), (samples[0].Speed, samples[0].Force) );
		Assert.AreEqual( (samples[3].Speed, samples[3].Force), (samples[4].Speed, samples[4].Force) );
		Assert.AreEqual( 0f, result.MaxHeight, "below ground the maximum stays at its 0.0 start" );
	}

	/// <summary>Trough coasters set sample flag 8 on a climbing next segment and clear it elsewhere (0x3fda4..0x3fdc8).</summary>
	[TestMethod]
	public void TroughFrictionFlagIsSetAndCleared()
	{
		var settings = PcSettings();
		settings["sCoasterType.bSpecialIsTrough"] = "1";
		var definition = CoasterDefinition.FromValues( settings );
		var samples = new CoasterSample[4];
		var heights = new[] { 0f, 0f, 2f, 0f };
		for ( var index = 0; index < samples.Length; index++ )
			samples[index] = new CoasterSample { Position = new SVector3( 3 * index, heights[index], 0 ), Flags = CoasterSampleFlags.Friction };
		CoasterRating.RatePass( definition, samples, 3, 110 );
		Assert.IsTrue( samples[1].Flags.HasFlag( CoasterSampleFlags.Friction ), "the next segment climbs" );
		Assert.IsFalse( samples[2].Flags.HasFlag( CoasterSampleFlags.Friction ), "the next segment falls: cleared" );
	}

	/// <summary>The braking speed is sample (tail +252) − 1: two before the first sample of the station end node's pair, one before it when that sample is on the knot.</summary>
	[TestMethod]
	public void BrakingSpeedSampleIsTheOneBeforeTheTailCoordinate()
	{
		var samples = Enumerable.Range( 0, 10 ).Select( index => new CoasterSample { Section = index < 7 ? 0 : 1, T = 0.25f } ).ToArray();
		Assert.AreEqual( 5, CoasterRide.BrakingSpeedSample( samples, 1 ) );
		Assert.AreEqual( 9, CoasterRide.BrakingSpeedSample( samples, 2 ), "no such section: the last sample" );
		samples[7].T = 0;
		Assert.AreEqual( 6, CoasterRide.BrakingSpeedSample( samples, 1 ), "a sample on the knot truncates to itself (COAST-V2 CV2-4)" );
	}

	// ---- Build → run → board → stop → delete --------------------------------------------------------------

	[TestMethod]
	public void ACompletedTrackIsRatedAndRunsItsTrainsThroughTheStation()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 60;
		builder.Place( 24, 20 );
		builder.Height = 20;
		foreach ( var (dx, dy) in Circuit.Skip( 1 ) )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( Depart.X + dx, Depart.Y + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		var ride = builder.Ride;
		Assert.IsTrue( ride.IsOperational && ride.Track.IsComplete );
		Assert.IsTrue( ride.Samples.Count > 60, $"{ride.Samples.Count} samples" );
		Assert.AreEqual( 3f, ride.Spacing, "controller +172" );
		Assert.AreEqual( 110f, ride.SpeedCap, "speed setting 60" );
		var rating = ride.Rating!.Value;
		Assert.IsTrue( rating.Excitement is > 0 and <= 100 && rating.Sickness is >= 0 and <= 100, $"E {rating.Excitement}, S {rating.Sickness}" );
		Assert.IsTrue( rating.LapTime > 0 && rating.MaxSpeed >= ride.Definition.WinchSpeed && rating.MaxHeight > 20 );
		Assert.AreEqual( CoasterRating.TrainCount( rating.LapTime, 30000, 6 ), rating.Trains );
		Assert.AreEqual( rating.Trains, ride.Trains.Count );
		Assert.IsTrue( ride.WinchSections.Contains( 0 ), "the climb out of the station is a chain lift" );
		Assert.IsTrue( ride.Track.Nodes[0].Flags.HasFlag( CoasterNodeFlags.Winch ) );
		Assert.AreEqual( 0, ride.GuestExcitement, "no controller before COAST 8" );

		ride.Command( 8, 0 );
		Assert.AreEqual( CoasterRating.GuestExcitement( rating.Excitement ), ride.GuestExcitement );
		Assert.AreEqual( 0, ride.Command( 2, 0 ), "closed: no room" );
		ride.Command( 5, 0 );
		ride.Command( 6, 6 );
		Assert.AreEqual( 6, ride.Command( 2, 0 ) );
		ride.Board( 101 );
		ride.Board( 102 );
		Assert.AreEqual( 4, ride.Command( 2, 0 ) );
		Assert.AreEqual( 2, ride.RidersOnBoard );
		var train = ride.Trains[0];
		Assert.AreEqual( CoasterTrainState.Boarding, train.State );

		var departed = false;
		var returned = false;
		var maximum = 0f;
		for ( var tick = 0; tick < 240 * 60 && !returned; tick++ )
		{
			ride.Advance( Tick );
			departed |= train.State == CoasterTrainState.Running;
			maximum = MathF.Max( maximum, train.Position );
			returned = departed && train.State == CoasterTrainState.Unloading;
		}
		Assert.IsTrue( departed && returned, "the train left after the boarding time and came back to unload" );
		Assert.IsTrue( maximum > ride.Samples.Count * 0.9f );
		Assert.AreEqual( 0, ride.Command( 2, 0 ), "no room while unloading" );
		var off = new[] { ride.Command( 3, 0 ), ride.Command( 3, 0 ), ride.Command( 3, 0 ) };
		CollectionAssert.AreEquivalent( new[] { 101, 102, 0 }, off );
		ride.Advance( Tick );
		Assert.AreEqual( CoasterTrainState.Boarding, train.State );

		// Closing stops departures; the boarding train stays.
		ride.Command( 5, 1 );
		for ( var tick = 0; tick < 60 * 60; tick++ )
			ride.Advance( Tick );
		Assert.AreEqual( CoasterTrainState.Boarding, train.State );
		Assert.AreEqual( 0, ride.Command( 2, 0 ) );

		// Breakdown (COAST 4 1) also blocks room.
		ride.Command( 5, 0 );
		ride.Command( 4, 1 );
		Assert.IsTrue( ride.IsBroken && ride.ControllerStateFlag == 64 && ride.PowerFactor == 0, "COAST 4 1 is mode 64" );
		Assert.AreEqual( 0, ride.Command( 2, 0 ) );
		ride.Command( 4, 2 );
		Assert.IsTrue( !ride.IsBroken && ride.ControllerStateFlag == 32 && ride.PowerFactor == 1, "COAST 4 2 is mode 32" );
		ride.Command( 4, 3 );
		Assert.AreEqual( 32, ride.ControllerStateFlag, "COAST 4 3 changes nothing" );
		ride.Command( 4, 0 );
		Assert.AreEqual( 16, ride.ControllerStateFlag );

		// Reopening is refused with riders on board; deleting lets them off through COAST 3.
		ride.Board( 103 );
		Assert.IsFalse( builder.Reopen() );
		builder.Delete();
		Assert.IsFalse( ride.IsOperational );
		Assert.AreEqual( 1, ride.Track.Nodes.Count, "only the station node is left" );
		Assert.IsFalse( ride.World.HasPylon( 24, 20 ) );
		Assert.AreEqual( 103, ride.Command( 3, 0 ) );
		Assert.AreEqual( 0, ride.RidersOnBoard );
	}

	[TestMethod]
	public void TwoTrainsKeepTheStationForOneAtATime()
	{
		var settings = PcSettings();
		settings["sCoasterType.uiTrainReleaseRate"] = "1000";
		var (builder, _) = NewCoaster( settings );
		BuildCircuit( builder );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		var ride = builder.Ride;
		Assert.IsTrue( ride.Trains.Count >= 2, $"{ride.Trains.Count} trains for a {ride.Rating!.Value.LapTime} s lap" );
		ride.Command( 8, 0 );
		ride.Command( 5, 0 );
		for ( var tick = 0; tick < 120 * 60; tick++ )
		{
			ride.Advance( Tick );
			Assert.IsTrue( ride.Trains.Count( train => train.State is CoasterTrainState.Boarding or CoasterTrainState.Unloading ) <= 1 );
		}
	}

	/// <summary>
	/// The partial hop has no speed floor (COAST-V2 CV2-1): <c>0x40D98</c> stores max(v_end, M), but <c>0x40D9C..0x40DC8</c>
	/// overwrite it with min(cap, v_end) from the unfloored v_end, so a train leaving at speed 0 builds up speed over
	/// several frames instead of reaching fMinSpeed in one.
	/// </summary>
	[TestMethod]
	public void ADepartureFromRestDoesNotReachTheMinimumSpeedInOneFrame()
	{
		var (builder, _) = NewCoaster();
		BuildCircuit( builder );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		var ride = builder.Ride;
		ride.Command( 8, 0 );
		ride.Command( 5, 0 );
		var train = ride.Trains[0];
		for ( var tick = 0; tick < 120 * 60 && !(train.State == CoasterTrainState.Running && train.Position > 0); tick++ )
			ride.Advance( Tick );
		Assert.AreEqual( CoasterTrainState.Running, train.State );
		var minimum = ride.Definition.MinSpeed * ride.PowerFactor;
		Assert.IsTrue( train.Speed > 0 && train.Speed < minimum / 2, $"first running frame: speed {train.Speed}, M {minimum}" );
	}

	[TestMethod]
	public void CoasterStateIsInTheCanonicalHashAndReproducible()
	{
		ulong Run( int guest )
		{
			var scripts = new RideScriptWorld( 99 );
			var ride = new CoasterRide( CoasterDefinition.FromValues( PcSettings() ), scripts.Coasters );
			var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
			builder.SetStation( Depart, Return );
			BuildCircuit( builder );
			CloseAndComplete( builder );
			ride.Command( 8, 0 );
			ride.Command( 5, 0 );
			ride.Board( guest );
			for ( var tick = 0; tick < 30 * 60; tick++ )
				ride.Advance( Tick );
			return WorldStateHash.Compute( new WorldStateSources { Scripts = scripts } );
		}
		Assert.AreEqual( Run( 5 ), Run( 5 ) );
		Assert.AreNotEqual( Run( 5 ), Run( 6 ) );
		var empty = new RideScriptWorld( 99 );
		var before = WorldStateHash.Compute( new WorldStateSources { Scripts = empty } );
		empty.Coasters.Random.Next();
		Assert.AreNotEqual( before, WorldStateHash.Compute( new WorldStateSources { Scripts = empty } ), "coaster_rng_state is hashed" );
	}

	[TestMethod]
	public void CoasterRandomIsTheOriginalLcg()
	{
		var random = new CoasterRandom( 1 );
		Assert.AreEqual( (int)((1u * 214013u + 2531011u) >> 16), random.Next() );
		Assert.AreEqual( 1u * 214013u + 2531011u, random.State );
	}

	[TestMethod]
	public void StationEndsComeFromTheShapeAndRotation()
	{
		var shape = new ObjectShape( new[] { "****", "<**>", "*2N*" } );
		Assert.AreEqual( ((13, 11), (10, 11)), CoasterBuilder.StationEnds( shape, 10, 10, 0 ) );
		var turned = CoasterBuilder.StationEnds( shape, 10, 10, 90 )!.Value;
		Assert.AreEqual( (11, 7), turned.Depart );
		Assert.AreEqual( (11, 10), turned.Return );
		Assert.IsNull( CoasterBuilder.StationEnds( new ObjectShape( new[] { "**" } ), 0, 0, 0 ) );
	}

	// ---- Curve, sampling and motion against the COAST-R2 witness (coast_geometry_evidence.py) -------------------

	private static uint Bits( float value ) => BitConverter.SingleToUInt32Bits( value );

	[TestMethod]
	public void SineTableMatchesTheWitness()
	{
		Assert.AreEqual( 0x00000000u, Bits( CoasterTrig.Entry( 0 ) ) );
		Assert.AreEqual( 0x3AC90FD6u, Bits( CoasterTrig.Entry( 1 ) ) );
		Assert.AreEqual( 0x3F3504F3u, Bits( CoasterTrig.Entry( 512 ) ) );
		Assert.AreEqual( 0x3F800000u, Bits( CoasterTrig.Entry( 1024 ) ) );
		Assert.AreEqual( 0xB3BBBD2Eu, Bits( CoasterTrig.Entry( 2048 ) ) );
		Assert.AreEqual( 0xBF7E70B0u, Bits( CoasterTrig.Entry( 3000 ) ) );
		Assert.AreEqual( 0x3F5739C7u, Bits( CoasterTrig.Sin( 1f ) ) );
		Assert.AreEqual( 0x3F0A9D3Bu, Bits( CoasterTrig.Cos( 1f ) ) );
		Assert.AreEqual( 0xBEF4CFABu, Bits( CoasterTrig.Sin( -0.5f ) ) );
		Assert.AreEqual( 0xBF4CEACCu, Bits( CoasterTrig.Cos( 2.5f ) ) );
	}

	[TestMethod]
	public void BezierChordAndKnotControlsMatchTheWitness()
	{
		var q = HermiteCoasterCurve.Bezier( new SVector3( 0, 0, 0 ), new SVector3( 10, 5, 0 ), new SVector3( 20, 5, 10 ), new SVector3( 30, 0, 10 ), 0.35f );
		Assert.AreEqual( (0x41280000u, 0x405A6666u, 0x403451EBu), (Bits( q.X ), Bits( q.Y ), Bits( q.Z )) );
		q = HermiteCoasterCurve.Bezier( new SVector3( 1.5f, -2, 7 ), new SVector3( 4.25f, 3, 7.5f ), new SVector3( 9, 1, -3 ), new SVector3( 12, 0.5f, -6 ), 0.05f );
		Assert.AreEqual( (0x3FF6A8F6u, 0xBFA69581u, 0x40DFD581u), (Bits( q.X ), Bits( q.Y ), Bits( q.Z )) );
		Assert.AreEqual( 0x4249F353u, Bits( HermiteCoasterCurve.Chord( new SVector3( 0, 0, 0 ), new SVector3( 30, 10, 40 ) ) ), "height weighted 0.7" );
		var (back, forward) = HermiteCoasterCurve.KnotControls( new SVector3( 5, 20, 5 ), new SVector3( 1, 0.5f, 0 ), 30, 40, 1.25f, 1.25f );
		Assert.IsTrue( SVector3.Distance( back, new SVector3( -6.1803398f, 14.40983f, 5 ) ) < 1e-4f, $"{back}" );
		Assert.IsTrue( SVector3.Distance( forward, new SVector3( 19.90712f, 27.45356f, 5 ) ) < 1e-4f, $"{forward}" );
		Assert.AreEqual( 0x40066666u, Bits( HermiteCoasterCurve.LoopKnotFactor( 1.25f, 0, 0.5f ) ), "2.1" );
		Assert.AreEqual( 0x3FCDCC67u, Bits( HermiteCoasterCurve.LoopKnotFactor( 1.25f, 0.6f, 0.3f ) ) );
	}

	[TestMethod]
	public void NodeTangentsTurnAngleAndAutoBankMatchTheWitness()
	{
		var (a, p, b) = (new SVector3( 5, 20, 5 ), new SVector3( 45, 20, 5 ), new SVector3( 75, 30, 45 ));
		var curve = CoasterTrack.ForwardTangent( a, p, b, 1.25f, 1.25f, -0.999f, false );
		Assert.IsTrue( SVector3.Distance( curve, new SVector3( 56.80082f, 7.0132957f, 28.053183f ) ) < 1e-3f, $"{curve}" );
		Assert.AreEqual( new SVector3( 32, 0, 0 ), CoasterTrack.ForwardTangent( a, p, new SVector3( 85, 20, 5 ), 1.25f, 1.25f, -0.999f, false ), "straight non-loop pylon: b/SD" );
		Assert.AreEqual( new SVector3( 32, 0, 0 ), CoasterTrack.ForwardTangent( a, p, new SVector3( 85, 20, 5 ), 1.25f, 1.25f, null, false ), "pylon-less straight: b/SD" );
		Assert.AreEqual( new SVector3( -32, 0, 0 ), CoasterTrack.ForwardTangent( a, p, new SVector3( 85, 20, 5 ), 1.25f, 1.25f, -0.8f, true ), "straight loop pylon, node flag 0x80 clear: |b|·½(â − b̂)/SD" );
		var level = CoasterTrack.ForwardTangent( a, p, b, 1.25f, 1.25f, -0.999f, false, loopNeighbour: true );
		Assert.AreEqual( (curve.X, 0f, curve.Z), (level.X, level.Y, level.Z), "a non-loop pylon next to a loop pylon is level" );
		Assert.AreNotEqual( 0f, CoasterTrack.ForwardTangent( a, p, b, 1.25f, 1.25f, null, false, loopNeighbour: true ).Y, "pylon-less nodes keep Y" );
		Assert.AreEqual( 0x400DB70Du, Bits( CoasterTrack.TurnAngleXZ( a, p, b ) ) );
		Assert.AreEqual( -2.2142975f, CoasterTrack.TurnAngleXZ( a, p, new SVector3( 75, 30, -35 ) ), 1e-6f );
		Assert.AreEqual( 0x3D5643C0u, Bits( CoasterTrack.AutoBank( 20, 2.2f, 40, 50 ) ) );
		Assert.AreEqual( 0xBD5643C0u, Bits( CoasterTrack.AutoBank( 20, -2.2f, 40, 50 ) ) );
		Assert.AreEqual( 0f, CoasterTrack.AutoBank( 5, 0, 1, 1 ) );
	}

	[TestMethod]
	public void SpeedCapBrakingAndHopSpeedMatchTheWitness()
	{
		var definition = CoasterDefinition.Default;
		Assert.AreEqual( 110f, CoasterRide.SpeedCapFor( definition, 60 ) );
		Assert.AreEqual( 80f, CoasterRide.SpeedCapFor( definition, 0 ) );
		Assert.AreEqual( 130f, CoasterRide.SpeedCapFor( definition, 100 ) );
		Assert.AreEqual( 89f, CoasterRide.BrakingStartFor( 100, 30, 3 ) );
		Assert.AreEqual( 0f, CoasterRide.BrakingStartFor( 5, 30, 3 ) );
		Assert.AreEqual( 0x42174000u, Bits( CoasterRide.BrakeBlend( 40, 5, 95, 90, 100 ) ) );
		Assert.AreEqual( 5f, CoasterRide.BrakeBlend( 40, 5, 80, 90, 100 ), "outside the zone x = 1" );
		Assert.AreEqual( 11.5f, CoasterRide.CarSpeed( definition, 10, 20, 19, 5 ) );
		Assert.AreEqual( 8.5f, CoasterRide.CarSpeed( definition, 10, 20, 21, 5 ) );
		Assert.AreEqual( 5f, CoasterRide.CarSpeed( definition, 0, 20, 21, 5 ), "floored" );
		Assert.AreEqual( 92, CoasterTrack.Truncate( 92.43f ) );
		Assert.AreEqual( 0, CoasterTrack.Truncate( -3f ), "negative gives 0" );
		Assert.AreEqual( 8, CoasterBuilder.PreviewDelta( 92.43f, 100.2f ) );
	}

	[TestMethod]
	public void TheSamplerPlacesASampleEveryThreeUnitsOfCurve()
	{
		var (builder, _) = NewCoaster();
		BuildCircuit( builder );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		var ride = builder.Ride;
		var knots = builder.Track.BuildKnots( builder.Site );
		Assert.AreEqual( knots[0].Position, ride.Samples[0].Position, "sample 0 is the first knot" );
		Assert.AreEqual( new SVector3( Depart.X * 10 + 5, 10, Depart.Y * 10 + 5 ), knots[0].Position, "cell centre 10x + 5 at the loft height" );
		for ( var index = 1; index < ride.Samples.Count; index++ )
		{
			var gap = SVector3.Distance( ride.Samples[index - 1].Position, ride.Samples[index].Position );
			Assert.IsTrue( gap <= 3.0001f && gap > 2.5f, $"chord {gap} between samples {index - 1} and {index}" );
		}
		Assert.IsTrue( knots.Select( knot => knot.Section ).SequenceEqual( Enumerable.Range( 0, knots.Count ) ) );
		CollectionAssert.AreEqual( Enumerable.Range( 0, knots.Count ).ToArray(), ride.Samples.Select( sample => sample.Section ).Distinct().ToArray(), "every pair is sampled in order" );
		Assert.IsTrue( ride.Samples.All( sample => Math.Abs( sample.Direction.Length() - 1 ) < 1e-4f ) );
		var (yaw, pitch) = HermiteCoasterCurve.Orientation( new SVector3( 0, -1, 1 ) );
		Assert.AreEqual( 0f, yaw );
		Assert.AreEqual( MathF.PI / 4, pitch, 1e-3f, "pitch = atan2(−T.y, T.z′)" );
		Assert.IsTrue( builder.Track.Nodes.Where( node => !node.IsPylonless ).Any( node => node.Bank != 0 ), "auto-bank at the pylons" );
		Assert.IsTrue( builder.Track.Nodes.Where( node => node.IsPylonless ).All( node => node.Bank == 0 ) );
	}

	[TestMethod]
	public void SuspendedCoastersDoNotAutoBank()
	{
		var settings = PcSettings();
		settings["sCoasterType.bIsSuspendedCoaster"] = "1";
		var definition = CoasterDefinition.FromValues( settings );
		Assert.IsTrue( definition.IsSuspended && !definition.AutoBanks );
		var (builder, _) = NewCoaster( settings );
		BuildCircuit( builder );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		Assert.IsTrue( builder.Track.Nodes.All( node => node.Bank == 0 ) );
	}

	[TestMethod]
	public void ModeSixtyFourRemovesThePowerAndTrainsCoastToAStop()
	{
		var settings = PcSettings();
		settings["sCoasterType.uiTrainReleaseRate"] = "1000";
		var (builder, _) = NewCoaster( settings );
		BuildCircuit( builder );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		var ride = builder.Ride;
		ride.Command( 8, 0 );
		ride.Command( 5, 0 );
		for ( var tick = 0; tick < 3 * 60; tick++ )
			ride.Advance( Tick );
		ride.Command( 4, 1 );
		Assert.AreEqual( (64, 0f), (ride.ControllerStateFlag, ride.PowerFactor), "the scripted breakdown cuts the power" );
		for ( var tick = 0; tick < 300 * 60; tick++ )
			ride.Advance( Tick );
		Assert.IsTrue( ride.Trains.All( train => train.Speed == 0 ), "without floors the trains brake and decay to a stop" );
		Assert.IsTrue( ride.Trains.Any( train => train.State is CoasterTrainState.Boarding or CoasterTrainState.Unloading ), "and nothing leaves the station" );
		ride.Command( 4, 0 );
		Assert.AreEqual( 1f, ride.PowerFactor );
		var moved = false;
		for ( var tick = 0; tick < 30 * 60 && !moved; tick++ )
		{
			ride.Advance( Tick );
			moved = ride.Trains.Any( train => train.State == CoasterTrainState.Running && train.Speed > 0 );
		}
		Assert.IsTrue( moved, "power back: trains run again" );
	}
	/// <summary>COAST 5 is gated (0x43b2f0): a repeated open or close changes nothing; COAST 6 floors at 0 (0x43b330).</summary>
	[TestMethod]
	public void CloseGateAndCapacityFloor()
	{
		var (builder, _) = NewCoaster();
		BuildCircuit( builder );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		var ride = builder.Ride;
		ride.Command( 8, 0 );
		Assert.IsTrue( ride.Closed );
		ride.Command( 5, 1 );
		Assert.IsTrue( ride.Closed, "close while closed: no change" );
		ride.Command( 5, 0 );
		Assert.IsFalse( ride.Closed );
		ride.Command( 5, 0 );
		Assert.IsFalse( ride.Closed, "open while open: no change" );
		ride.Command( 5, 7 );
		Assert.IsTrue( ride.Closed, "any nonzero closes" );
		ride.Command( 6, -5 );
		Assert.AreEqual( 0, ride.SeatsPerTrain, "the lower bound is the literal 0" );
		ride.Command( 6, 0 );
		Assert.AreEqual( 0, ride.SeatsPerTrain );
		ride.Command( 6, 3 );
		Assert.AreEqual( 3, ride.SeatsPerTrain );
	}

	/// <summary>COAST 8 applies the script speed bias 50 (0x5516b0 → 0x43b2b0 → 0x43c3d0): setting 50, cap = lerp(80, 130, 0.5).</summary>
	[TestMethod]
	public void CoasterEightAppliesTheScriptSpeedBias()
	{
		var (builder, _) = NewCoaster();
		var ride = builder.Ride;
		Assert.AreEqual( (60, 110f), (ride.SpeedSetting, ride.SpeedCap), "the constructor's setting 60 before the script runs" );
		ride.Command( 8, 0 );
		Assert.AreEqual( RideVM.DefaultSpeedBias, ride.SpeedSetting );
		Assert.AreEqual( CoasterRide.SpeedCapFor( ride.Definition, 50 ), ride.SpeedCap );
		Assert.AreEqual( 105f, ride.SpeedCap );
		ride.Command( 8, 0, 80 );
		Assert.AreEqual( (80, CoasterRide.SpeedCapFor( ride.Definition, 80 )), (ride.SpeedSetting, ride.SpeedCap ), "a station script whose ride has InitSpeed 80" );
	}

	/// <summary>In mode 64 the station step is skipped (0x43b8b0): an unloaded train does not move on to boarding until the power is back.</summary>
	[TestMethod]
	public void BreakdownSkipsTheStationStep()
	{
		var (builder, _) = NewCoaster();
		BuildCircuit( builder );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		var ride = builder.Ride;
		ride.Command( 8, 0 );
		var train = ride.Trains[0];
		ride.Command( 4, 1 );
		train.State = CoasterTrainState.Unloading;
		for ( var tick = 0; tick < 120; tick++ )
			ride.Advance( Tick );
		Assert.AreEqual( CoasterTrainState.Unloading, train.State );
		ride.Command( 4, 0 );
		ride.Advance( Tick );
		Assert.AreEqual( CoasterTrainState.Boarding, train.State );
	}

	/// <summary>The suspended swing (tp.exe 0x43c76c..0x43ca45): with zero sample angles the roll is the wrapped approach of π toward atan2(−X, Y) at rate min(|F + g|·fForceMultiplier, 1).</summary>
	[TestMethod]
	public void SuspendedSwingFollowsThePcRestatement()
	{
		var settings = PcSettings();
		settings["sCoasterType.bIsSuspendedCoaster"] = "1";
		var definition = CoasterDefinition.FromValues( settings );
		var samples = new CoasterSample[6];
		var heights = new[] { 0f, 0f, 3f, 1f, 4f, 0f };
		for ( var index = 0; index < samples.Length; index++ )
			samples[index] = new CoasterSample { Position = new SVector3( 3 * index, heights[index], 0 ), Bank = 0.7f };
		CoasterRating.RatePass( definition, samples, 3, 110, tailSection: 0 );
		double Wrap( double value ) => ((value + 3 * (double)MathF.PI) % (2 * (double)MathF.PI)) - MathF.PI;
		const double previous = MathF.PI; // the expectation is checked on the first sample, whose previous angle is the start value π
		for ( var index = 0; index < samples.Length; index++ )
		{
			// Zero yaw and pitch; the roll is rotated through the old roll 0.7 (the rating pass left it in place: no winch bank).
			var x = (double)samples[index].Force.X + definition.GravityX;
			var y = (double)samples[index].Force.Y + definition.GravityY;
			Assert.IsTrue( samples[index].Bank is >= -MathF.PI and <= MathF.PI, "the roll stays within ±π" );
			if ( index != 0 )
				continue;
			var (cos, sin) = (Math.Cos( -0.7 ), Math.Sin( -0.7 ));
			(x, y) = (cos * x - sin * y, sin * x + cos * y);
			var theta = Math.Atan2( -x, y );
			var d = Wrap( theta - previous );
			var rate = Math.Min( Math.Sqrt( x * x + y * y ) * definition.ForceMultiplier, 1 );
			var w = Wrap( previous + d * rate );
			var roll = w - Math.PI;
			if ( roll < -Math.PI )
				roll += 2 * Math.PI;
			Assert.AreEqual( roll, samples[index].Bank, 1e-3, "sample 0" );
		}
	}
}
