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
	/// <summary>
	/// The cells of the test station's last node S2 (where the track leaves the platform) and first node S0 (where it returns onto it) on the flat test site: a two-cell
	/// station '&lt;&gt;' on (18, 20) and (19, 20), whose outer nodes stand one cell beyond, so every link after S2 has the geometry the single station node gave it.
	/// </summary>
	private static readonly (int X, int Y) Depart = (20, 20), Return = (17, 20);
	/// <summary>Builds the two-cell test station '&lt;&gt;' whose last node S2 stands on <paramref name="s2"/>: its '&gt;' cell is one before, its '&lt;' cell two before (S0 stands three before).</summary>
	internal static void PlaceStation( CoasterBuilder builder, (int X, int Y) s2 ) => builder.SetStation( (s2.X - 1, s2.Y), (s2.X - 2, s2.Y) );
	/// <summary>The index of the first pylon the player places: the station has three nodes.</summary>
	private const int First = CoasterTrack.StationNodeCount;
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
		PlaceStation( builder, start );
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
		Assert.AreEqual( First + 1, builder.Track.Nodes.Count );
		Assert.IsTrue( builder.Track.Nodes.Skip( First - 1 ).All( node => node.Flags.HasFlag( CoasterNodeFlags.Invalid ) ), "node and predecessor (S2) get flag 0x4000" );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Move( First, 23, 20 ) );
		Assert.IsFalse( builder.Track.HasInvalidNodes, "the flag clears once validation passes" );
		Assert.AreEqual( CoasterPlaceResult.TooClose, builder.Place( 25, 21 ), "a too-short link is placed and marked too" );
		Assert.IsTrue( builder.Track.Nodes[^1].Flags.HasFlag( CoasterNodeFlags.Invalid ) && builder.Track.Nodes[^2].Flags.HasFlag( CoasterNodeFlags.Invalid ) );
		Assert.IsTrue( builder.RemoveLast() );
		Assert.IsFalse( builder.Track.HasInvalidNodes );
		Assert.AreEqual( CoasterPlaceResult.OutsideMap, builder.Place( -1, 20 ), "only structural refusals leave the track unchanged" );
		Assert.AreEqual( First + 1, builder.Track.Nodes.Count );
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
		for ( var station = 0; station < First; station++ )
			Assert.AreEqual( CoasterPlaceResult.NotEditable, builder.Move( station, 21, 25 ), "a station node (descriptor flag 2) cannot be picked up" );
		Assert.AreEqual( CoasterPlaceResult.BreaksNextLink, builder.Move( First, 25, 22 ), "P2 at (27,24) would be √8 cells away (below 3)" );
		Assert.AreEqual( (25, 22), (builder.Track.Nodes[First].X, builder.Track.Nodes[First].Y), "the drop is kept" );
		Assert.IsTrue( builder.Track.Nodes[First + 1].Flags.HasFlag( CoasterNodeFlags.Invalid ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Move( First, 25, 20 ) );
		Assert.AreEqual( (25, 20), (builder.Track.Nodes[First].X, builder.Track.Nodes[First].Y) );
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
		Assert.AreEqual( 100f, builder.Track.Nodes[First].Height );
		Assert.AreEqual( 1f, builder.Track.Nodes[First].T );
	}

	[TestMethod]
	public void TheCostHeightRoundsEveryOperationToASingleAsTheX87DoesAt24Bits()
	{
		// 0x4346a0: (h1 − h0) × t + h0 with a rounding after the subtraction, the product and the sum; the Mac's fused multiply-add differs in the last bit for these inputs
		var pylon = new CoasterPylonControl { MeshHeightLow = 10f, MeshHeightHigh = 25f };
		const float t = 0.5605103373527527f;
		Assert.AreEqual( 18.40765380859375f, CoasterTrack.CostHeight( pylon, t ) );
		Assert.AreNotEqual( CoasterTrack.CostHeight( pylon, t ), CoasterTrack.PylonHeight( pylon, t ), "the fused form of the Mac rounds once" );
		var low = new CoasterPylonControl { MeshHeightLow = 5f, MeshHeightHigh = 40f };
		Assert.AreEqual( 17.382545471191406f, CoasterTrack.CostHeight( low, 0.35378697514533997f ) );
	}

	[TestMethod]
	public void TheCostLengthSumsTheSquaresInSinglePrecisionBeforeTheRoot()
	{
		// 0x4478e3..0x44791f: fsqrt(dz² + dx²), the squares and their sum single-rounded, the root rounded to a single
		Assert.AreEqual( MathF.Sqrt( 200f ), CoasterTrack.CostLength( 10f, 10f ) );
		var dx = 16777.217f;
		var dz = 3.3333333f;
		Assert.AreEqual( MathF.Sqrt( dx * dx + dz * dz ), CoasterTrack.CostLength( dx, dz ) );
		Assert.AreEqual( 0f, CoasterTrack.CostLength( 0, 0 ) );
	}

	[TestMethod]
	public void ThePylonCostMultipliesByAtenthAndAddsInTheRoutinesOrder()
	{
		// ((rate × (length × 0.1f)) + unit) + (rate × (height × 0.1f)), a rounding per operation
		var definition = CoasterDefinition.FromValues( PcSettings() );
		var pylon = definition.StandardPylon;
		var length = 23.456789f;
		var height = 17.382545f;
		var expected = pylon.CostPer2DLength * (length * 0.1f);
		expected += pylon.CostPerUnit;
		expected += pylon.CostPerHeight * (height * 0.1f);
		Assert.AreEqual( expected, CoasterTrack.PylonCost( pylon, length, height ) );
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
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Move( First, 24, 20 ), "same cell" );
		Assert.AreEqual( balance, economy.Balance, "a drop on its own cell moves no money" );
		var old = builder.Track.NodeCost( First );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Move( First, 25, 20 ) );
		var now = builder.Track.NodeCost( First );
		Assert.AreEqual( (90f, 100f), (old, now) );
		Assert.AreEqual( balance + CoasterTrack.Truncate( old ) - CoasterTrack.RoundUp( now ), economy.Balance, "open track: the node only" );
		balance = economy.Balance;
		old = builder.Track.NodeCost( First + 4 );
		Assert.IsTrue( builder.SetHeight( First + 4, 55 ) );
		Assert.AreEqual( 55f, builder.Track.Nodes[First + 4].Height );
		Assert.AreEqual( balance + CoasterTrack.Truncate( old ) - CoasterTrack.RoundUp( builder.Track.NodeCost( First + 4 ) ), economy.Balance );
		Assert.IsTrue( builder.RemoveLast() );
		Assert.AreEqual( balance + CoasterTrack.Truncate( old ) - CoasterTrack.RoundUp( builder.Track.NodeCost( First + 4 ) ) + 0, economy.Balance + 0, "deleting a pylon refunds nothing" );
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
		Assert.AreEqual( 90.5f, builder.Track.NodeCost( First ), "the place path: 40.5 + 10 × 4 + 10 × 1" );
		Assert.AreEqual( 50f, builder.Track.NodeCost( First, chargeUnit: false ) );
		var balance = economy.Balance;
		var (income, costs) = (Total( economy, LedgerCategory.OtherIncome ), Total( economy, LedgerCategory.OtherCosts ));
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Move( First, 25, 20 ) );
		Assert.AreEqual( 60f, builder.Track.NodeCost( First, chargeUnit: false ) );
		Assert.AreEqual( 50, Total( economy, LedgerCategory.OtherIncome ) - income, "credit trunc(50), not trunc(90.5)" );
		Assert.AreEqual( 60, Total( economy, LedgerCategory.OtherCosts ) - costs, "debit ceil(60), not ceil(100.5)" );
		Assert.AreEqual( balance - 10, economy.Balance );
		balance = economy.Balance;
		Assert.IsTrue( builder.SetHeight( First, 15 ) );
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
		Assert.AreEqual( First + 1, builder.Track.Nodes.Count );
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
		Assert.AreEqual( 110f, flume.Track.NodeCost( First ), "a trough pylon on the ground: StdPylon rates" );
	}

	[TestMethod]
	public void ACircuitClosedThroughAFailingLinkOpensAgainForTheTool()
	{
		// an older OpenTPW closed a fresh circuit without checking the closing link: such a track can never complete
		var (builder, _) = NewCoaster();
		foreach ( var (dx, dy) in Circuit.Take( 4 ) )
			builder.Place( Depart.X + dx, Depart.Y + dy );
		Assert.IsTrue( builder.Close(), "the old close step" );
		Assert.AreEqual( CoasterPlaceResult.TooFar, builder.Track.CheckClosure() );
		Assert.IsTrue( builder.Track.Nodes[^1].Flags.HasFlag( CoasterNodeFlags.Invalid ), "its last pylon is marked" );
		Assert.AreEqual( CoasterPlaceResult.InvalidNodes, builder.Complete() );
		// the tool's entry opens it again, keeping every pylon, and building goes on from the last one
		Assert.IsTrue( builder.OpenFailingClosure() );
		Assert.IsFalse( builder.Track.IsClosed );
		Assert.AreEqual( First + 4, builder.Track.Nodes.Count );
		Assert.IsFalse( builder.Track.HasInvalidNodes, "an open chain does not check the closing link" );
		Assert.IsFalse( builder.OpenFailingClosure(), "once" );
		foreach ( var (dx, dy) in Circuit.Skip( 4 ) )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( Depart.X + dx, Depart.Y + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		Assert.IsTrue( builder.Track.IsRunnable );
		// a circuit closed through a passing link stays closed
		var (valid, _) = NewCoaster();
		BuildCircuit( valid );
		Assert.AreEqual( CoasterPlaceResult.Ok, valid.CloseCircuit() );
		Assert.IsFalse( valid.OpenFailingClosure() );
		Assert.IsTrue( valid.Track.IsClosed );
	}

	[TestMethod]
	public void TheClosingTargetShowsWhatItsClickAnswers()
	{
		var (builder, _) = NewCoaster();
		BuildCircuit( builder );
		var panel = CoasterPanelRig.Create( builder );
		panel.Hover( Return.X, Return.Y );
		Assert.IsTrue( panel.Cursor!.Value.Closing );
		Assert.AreEqual( CoasterPlaceResult.Ok, panel.Cursor.Value.Result );
		// a pylon marked invalid elsewhere refuses the closing click, and the closing target says so before the click
		builder.Track.Nodes[First + 1].Flags |= CoasterNodeFlags.Invalid;
		panel.Hover( Return.X, Return.Y );
		Assert.AreEqual( CoasterPlaceResult.InvalidNodes, panel.Cursor!.Value.Result );
		Assert.AreEqual( CoasterPlaceResult.InvalidNodes, builder.Place( Return.X, Return.Y ) );
		Assert.IsFalse( builder.Track.IsClosed );
	}

	[TestMethod]
	public void ClosingThenCompletingAppendsTheStationEndNodes()
	{
		var (builder, _) = NewCoaster();
		foreach ( var (dx, dy) in Circuit.Take( 4 ) )
			builder.Place( Depart.X + dx, Depart.Y + dy );
		Assert.AreEqual( CoasterPlaceResult.NotClosed, builder.Complete() );
		// a closing link that fails the validator leaves the circuit open, and no node is added or marked
		Assert.AreEqual( CoasterPlaceResult.TooFar, builder.Place( Return.X, Return.Y ), "(16,28) → (17,20) is 8.06 cells" );
		Assert.IsFalse( builder.Track.IsClosed );
		Assert.AreEqual( First + 4, builder.Track.Nodes.Count );
		Assert.IsFalse( builder.Track.HasInvalidNodes );
		Assert.AreEqual( CoasterPlaceResult.NotClosed, builder.Complete() );
		// a closing link that passes closes the circuit: no node is added, the chain takes no new pylon, and deleting a pylon opens it again
		foreach ( var (dx, dy) in Circuit.Skip( 4 ) )
			builder.Place( Depart.X + dx, Depart.Y + dy );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( Return.X, Return.Y ), "the click closes, no node is added" );
		Assert.IsTrue( builder.Track.IsClosed );
		Assert.AreEqual( First + Circuit.Length, builder.Track.Nodes.Count );
		Assert.AreEqual( CoasterPlaceResult.NotEditable, builder.Check( 12, 24 ), "a closed chain takes no new pylon" );
		Assert.IsTrue( builder.RemoveLast() && !builder.Track.IsClosed, "deleting a pylon opens the circuit" );
		builder.Place( Depart.X + Circuit[^1].X, Depart.Y + Circuit[^1].Y );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		Assert.IsTrue( builder.Track.IsRunnable );
		var ends = builder.Track.Nodes.Where( node => node.IsStationEnd ).ToArray();
		Assert.AreEqual( 1, ends.Length, "the loader always sets 0x20: one end node even without bVisibleEndPylon" );
		Assert.AreEqual( (Return.X, Return.Y), (ends[0].X, ends[0].Y) );
		Assert.AreEqual( CoasterNodeFlags.NoPreviousLink | CoasterNodeFlags.EndNextIsSecond, ends[0].Flags & ~CoasterNodeFlags.Winch, "0x435570 passes the creation parameters 0x144: node flags 0x80000 | 0x100000, no loop flag 0x40, no 0x4, no 0x100" );
		var knots = builder.Track.BuildKnots( builder.Site );
		Assert.AreEqual( knots[0].Position, knots[^1].Position, "the end node stands on the knot of S0 (0x435570)" );
		Assert.AreEqual( knots[0].Direction, knots[^1].Direction, "its tangent has no previous neighbour and its next is S1: along the platform like S0's" );
		Assert.AreEqual( new SVector3( 1, 0, 0 ), knots[^1].Direction, "an end node without node flag 0x40 takes its neighbour's tangent (here straight along +x), not the vertical (0, −1, 0) of a loop node" );
		Assert.AreEqual( 1, builder.Ride.World.PylonsAt( Return.X, Return.Y ).Count(), "the end node is no pylon: only S0's pylon stands on its cell" );
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
		Assert.IsTrue( ride.WinchSections.Contains( 0 ), "the platform is a chain lift" );
		Assert.IsTrue( ride.Track.Nodes[0].Flags.HasFlag( CoasterNodeFlags.Winch ) );
		Assert.AreEqual( 0, ride.GuestExcitement, "no controller before COAST 8" );

		ride.Command( 8, 0 );
		Assert.AreEqual( CoasterRating.GuestExcitement( rating.Excitement ), ride.GuestExcitement );
		ride.Command( 6, 6 );
		Assert.AreEqual( 6, ride.Command( 2, 0 ), "the room is the capacity of one train: no state or train enters it (0x43b1f0)" );
		ride.Command( 5, 0 );
		ride.Board( 101 );
		ride.Board( 102 );
		Assert.AreEqual( 4, ride.Command( 2, 0 ) );
		Assert.AreEqual( (2, 0), (ride.AdmissionCount, ride.RidersOnBoard), "COAST 1 seats nobody: the guests wait for a train in the station" );
		var train = ride.Trains[0];

		// The train never stops: it seats the waiting guests while its cars roll through the window, runs the lap with them and unloads them on arrival.
		var seated = false;
		var returned = false;
		var minimumSpeed = float.MaxValue;
		var maximum = 0f;
		for ( var tick = 0; tick < 240 * 60 && !returned; tick++ )
		{
			ride.Advance( Tick );
			minimumSpeed = MathF.Min( minimumSpeed, train.Speed );
			maximum = MathF.Max( maximum, train.Position );
			seated |= ride.RidersOnBoard == 2;
			returned = seated && ride.LeavingCount == 2;
		}
		Assert.IsTrue( seated && returned, "the guests boarded, rode a lap and were unloaded on arrival" );
		Assert.IsTrue( minimumSpeed > 0, $"a train never stops, its lowest speed was {minimumSpeed}" );
		Assert.IsTrue( maximum > ride.Samples.Count * 0.9f );
		Assert.AreEqual( 0, ride.RidersOnBoard );
		var off = new[] { ride.Command( 3, 0 ), ride.Command( 3, 0 ), ride.Command( 3, 0 ) };
		CollectionAssert.AreEquivalent( new[] { 101, 102, 0 }, off );

		// Closing moves the guests still waiting for a seat to the leaving ring; trains keep circulating, empty.
		ride.Board( 103 );
		Assert.AreEqual( 1, ride.AdmissionCount );
		ride.Command( 5, 1 );
		Assert.AreEqual( (0, 1), (ride.AdmissionCount, ride.LeavingCount), "0x43b390 moves the admission ring to the leaving ring" );
		Assert.AreEqual( 103, ride.Command( 3, 0 ) );
		for ( var tick = 0; tick < 60 * 60; tick++ )
		{
			ride.Advance( Tick );
			Assert.IsTrue( ride.Trains.All( other => other.Speed > 0 ), "closed trains keep circulating" );
		}
		Assert.AreEqual( 6, ride.Command( 2, 0 ), "the room does not look at the state either" );

		// Breakdown (COAST 4 1) is mode 64.
		ride.Command( 5, 0 );
		ride.Command( 4, 1 );
		Assert.IsTrue( ride.IsBroken && ride.ControllerStateFlag == 64 && ride.PowerFactor == 0, "COAST 4 1 is mode 64" );
		ride.Command( 4, 2 );
		Assert.IsTrue( !ride.IsBroken && ride.ControllerStateFlag == 32 && ride.PowerFactor == 1, "COAST 4 2 is mode 32" );
		ride.Command( 4, 3 );
		Assert.AreEqual( 32, ride.ControllerStateFlag, "COAST 4 3 changes nothing" );
		ride.Command( 4, 0 );
		Assert.AreEqual( 16, ride.ControllerStateFlag );

		// Reopening is not refused for riders (the PC has no rider test on this path): the trains go with them; deleting lets the rest off through COAST 3.
		ride.Board( 104 );
		for ( var tick = 0; tick < 120 * 60 && ride.RidersOnBoard == 0; tick++ )
			ride.Advance( Tick );
		Assert.AreEqual( 1, ride.RidersOnBoard, "the next train that passes the station seats the guest" );
		Assert.IsTrue( builder.Reopen(), "no rider test: the trains are taken off (state 1)" );
		Assert.AreEqual( 0, ride.RidersOnBoard );
		builder.Delete();
		Assert.IsFalse( ride.IsOperational );
		Assert.AreEqual( CoasterTrack.StationNodeCount, ride.Track.Nodes.Count, "only the station's three nodes are left" );
		Assert.IsFalse( ride.World.HasPylon( 24, 20 ) );
		Assert.AreEqual( 104, ride.Command( 3, 0 ) );
		Assert.AreEqual( 0, ride.RidersOnBoard );
	}

	/// <summary>The trains of a station circulate side by side; none ever stops, and each one unloads and seats in turn as its cars pass the window.</summary>
	[TestMethod]
	public void TwoTrainsPassTheStationWithoutStopping()
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
		var arrivals = new int[ride.Trains.Count];
		var wasIn = new bool[ride.Trains.Count];
		for ( var tick = 0; tick < 240 * 60; tick++ )
		{
			ride.Advance( Tick );
			for ( var index = 0; index < ride.Trains.Count; index++ )
			{
				Assert.IsTrue( ride.Trains[index].Speed > 0, $"train {index} never stops" );
				var inStation = ride.Trains[index].Flags.HasFlag( CoasterTrainFlags.InStation );
				if ( inStation && !wasIn[index] )
					arrivals[index]++;
				wasIn[index] = inStation;
			}
		}
		Assert.IsTrue( arrivals.All( count => count >= 1 ), $"every train passed the station window: {string.Join( ", ", arrivals )}" );
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
		// A placed train starts at the mean rated speed under its cars (0x43cc8a); this test starts one from rest.
		train.Speed = 0;
		// The controller's first motion tick takes no time (0x43ce85): the train at rest stays at rest.
		ride.Advance( Tick );
		Assert.AreEqual( 0f, train.Speed, "the first motion tick takes no time" );
		ride.Advance( Tick );
		var minimum = ride.Definition.MinSpeed * ride.PowerFactor;
		Assert.IsTrue( train.Speed > 0 && train.Speed < minimum / 2, $"first frame: speed {train.Speed}, M {minimum}" );
	}

	/// <summary>
	/// The first motion tick of a newly built controller takes no time (<c>0x43ce85</c>: the clock word is set on the tick that finds bit 0x10 clear): every train makes
	/// one partial hop of no length, so it stays where the rating pass laid it out, while its speed is capped, decays ×0.95 below 2.0 and stops below 0.01. The next tick
	/// moves the trains by its whole step.
	/// </summary>
	[TestMethod]
	public void TheFirstMotionTickOfANewlyBuiltCoasterTakesNoTime()
	{
		var (builder, _) = NewCoaster();
		BuildCircuit( builder );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		var ride = builder.Ride;
		Assert.IsFalse( ride.MotionClockStarted, "a new controller's motion tick has not run" );
		Assert.IsTrue( ride.Trains.Count >= 2, $"the circuit places several trains ({ride.Trains.Count})" );
		ride.Trains[0].Speed = 1.5f;
		ride.Trains[1].Speed = 0.005f;
		var expected = ride.Trains.Select( train =>
		{
			var speed = MathF.Min( train.Speed, ride.SpeedCap );
			if ( speed < CoasterRide.DecayBelow )
				speed *= CoasterRide.Decay;
			return (train.Position % ride.Samples.Count, speed < CoasterRide.StopBelow ? 0f : speed);
		} ).ToList();
		ride.Advance( Tick );
		Assert.IsTrue( ride.MotionClockStarted );
		CollectionAssert.AreEqual( expected, ride.Trains.Select( train => (train.Position, train.Speed) ).ToList(), "no train moved; the speeds were capped and decayed" );
		var before = ride.Trains[0].Position;
		ride.Advance( Tick );
		Assert.AreNotEqual( before, ride.Trains[0].Position, "the second tick moves the train" );
	}

	/// <summary>
	/// A track that is cut open runs no motion tick (state 1, <c>0x435ac9</c>), so the controller's clock word stands while the clock runs on; the first motion tick
	/// after the track is completed again takes all the time since the last tick as its frame time, in one tick (<c>0x43ce20</c>: (now − ctl+0x120) × 0.001,
	/// no clamp, no substeps), and the tick after it takes one step again.
	/// </summary>
	[TestMethod]
	public void AReopenedTrackCatchesTheTimeSinceItsLastMotionTickUpInOneTick()
	{
		var (builder, _) = NewCoaster();
		BuildCircuit( builder );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		var ride = builder.Ride;
		ride.Command( 8, 0 );
		ride.Command( 5, 0 );
		for ( var tick = 0; tick < 10; tick++ )
			ride.Advance( Tick );
		Assert.AreEqual( Tick, ride.LastMotionFrameSeconds, "a running coaster's tick takes one step" );
		Assert.AreEqual( 0d, ride.MotionClockLagSeconds );

		Assert.IsTrue( builder.Reopen() );
		Assert.AreEqual( CoasterControllerState.Incomplete, ride.State );
		const int Cut = 150;
		var expected = 0d;
		for ( var tick = 0; tick < Cut; tick++ )
		{
			ride.Advance( Tick );
			expected += Tick;
		}
		Assert.AreEqual( expected, ride.MotionClockLagSeconds, "no motion tick runs in state 1: the clock word stands" );
		Assert.AreEqual( 0, ride.Trains.Count );

		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		Assert.AreEqual( CoasterControllerState.Built, ride.State );
		var fullHops = 0;
		ride.HopObserved = ( train, _, full ) => { if ( train == 0 && full ) fullHops++; };
		ride.Advance( Tick );
		expected += Tick;
		Assert.AreEqual( (float)expected, ride.LastMotionFrameSeconds, "the first tick after the completion takes the whole stretch" );
		Assert.AreEqual( 0d, ride.MotionClockLagSeconds );
		var caughtUp = fullHops;
		fullHops = 0;
		ride.Advance( Tick );
		Assert.AreEqual( Tick, ride.LastMotionFrameSeconds, "the next tick takes one step again" );
		Assert.IsTrue( caughtUp > 1 && caughtUp > fullHops, $"the catch-up tick hops further ({caughtUp} full hops, then {fullHops})" );
	}

	/// <summary>A controller whose motion tick never ran keeps the zero-time first tick (<c>0x43ce85</c>) however long its track stood unfinished.</summary>
	[TestMethod]
	public void ATrackFinishedLateStillTakesNoTimeOnItsFirstTick()
	{
		var (builder, _) = NewCoaster();
		BuildCircuit( builder );
		var ride = builder.Ride;
		for ( var tick = 0; tick < 120; tick++ )
			ride.Advance( Tick );
		Assert.IsFalse( ride.MotionClockStarted );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		ride.Advance( Tick );
		Assert.AreEqual( 0f, ride.LastMotionFrameSeconds, "the first motion tick sets the clock word and takes no time" );
		ride.Advance( Tick );
		Assert.AreEqual( Tick, ride.LastMotionFrameSeconds );
	}

	/// <summary>
	/// The speed setter's gate (<c>0x43b2b0</c>): a speed that a layout replay set (controller bit <c>0x8000</c>, written only by the replayed action 0x45, <c>0x447e30</c>)
	/// is not changed by the script's speed word (COAST 8 or a slice tail) unless a layout replay runs. OpenTPW runs none, so the bit is never set on its own.
	/// </summary>
	[TestMethod]
	public void AReplayedSpeedHoldsAgainstTheScriptSpeedWordOutsideAReplay()
	{
		var (builder, _) = NewCoaster();
		BuildCircuit( builder );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		var ride = builder.Ride;
		ride.Command( 8, 0, 50 );
		Assert.AreEqual( 50, ride.SpeedSetting );
		Assert.IsFalse( ride.SpeedSetByReplay, "nothing outside a layout replay sets bit 0x8000" );
		ride.ApplyScriptSpeed( 70 );
		Assert.AreEqual( 70, ride.SpeedSetting, "bit 0x8000 clear: the script's word applies" );

		ride.ApplyReplayedSpeed( 85, layoutReplayRuns: true );
		Assert.AreEqual( (85, true), (ride.SpeedSetting, ride.SpeedSetByReplay), "0x447e30: the speed applies, then bit 0x8000" );
		var cap = ride.SpeedCap;
		ride.ApplyScriptSpeed( 40 );
		ride.Command( 8, 0, 40 );
		Assert.AreEqual( (85, cap), (ride.SpeedSetting, ride.SpeedCap), "bit 0x8000 set and no replay: the script's word is skipped" );
		ride.ApplySpeed( 40, layoutReplayRuns: true );
		Assert.AreEqual( 40, ride.SpeedSetting, "while a layout replay runs (0x785a2c) the gate is open" );
		Assert.AreEqual( CoasterRide.SpeedCapFor( ride.Definition, 40 ), ride.SpeedCap );
	}

	/// <summary>
	/// Built coasters' fixed runs, pinned on every runtime (docs/DETERMINISM.md, "Cross-platform coaster pins"): the synthetic circuit run open for a minute with a
	/// guest on board, and the same circuit reopened for three seconds and completed again, whose first tick after the completion catches the three seconds up.
	/// </summary>
	[TestMethod]
	public void BuiltCoasterRunsArePinned()
	{
		ulong Run( bool reopen )
		{
			var scripts = new RideScriptWorld( 99 );
			var ride = new CoasterRide( CoasterDefinition.FromValues( PcSettings() ), scripts.Coasters );
			var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
			PlaceStation( builder, Depart );
			BuildCircuit( builder );
			CloseAndComplete( builder );
			ride.Command( 8, 0 );
			ride.Command( 5, 0 );
			ride.Board( 7 );
			for ( var tick = 0; tick < 60 * 60; tick++ )
			{
				if ( reopen && tick == 20 * 60 )
				{
					Assert.IsTrue( builder.Reopen() );
					for ( var cut = 0; cut < 3 * 60; cut++ )
						ride.Advance( Tick );
					Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
					ride.Command( 5, 0 );
				}
				ride.Advance( Tick );
			}
			return WorldStateHash.Compute( new WorldStateSources { Scripts = scripts } );
		}
		Assert.AreEqual( "1ABA458D0079E09C", Run( reopen: false ).ToString( "X16" ), "the built coaster's fixed run" );
		Assert.AreEqual( "045BFFE59994F3E7", Run( reopen: true ).ToString( "X16" ), "the reopened coaster's fixed run" );
	}

	[TestMethod]
	public void CoasterStateIsInTheCanonicalHashAndReproducible()
	{
		ulong Run( int guest )
		{
			var scripts = new RideScriptWorld( 99 );
			var ride = new CoasterRide( CoasterDefinition.FromValues( PcSettings() ), scripts.Coasters );
			var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
			PlaceStation( builder, Depart );
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
		// the PC twin 0x432430 rounds the table index to the nearest (3482 here; the Mac lookup truncated to 3481 and gave 0x3FCDCC67) and rounds every operation to a single (24-bit precision, COAST-032): 0x3FCDD46F worked out apart from the C# code with one single rounding per operation (carrying x² and w in double gave 0x3FCDD470)
		Assert.AreEqual( 0x3FCDD46Fu, Bits( HermiteCoasterCurve.LoopKnotFactor( 1.25f, 0.6f, 0.3f ) ) );
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
		Assert.AreEqual( new SVector3( Return.X * 10 + 5, 10, Return.Y * 10 + 5 ), knots[0].Position, "S0: the cell centre 10x + 5 at the loft height" );
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
		// The station and end nodes bank like pylons (the PC build path sets no node flag 0x4); on the straight station row their turn is π and the bank is 0 up to rounding.
		Assert.IsTrue( builder.Track.Nodes.Where( node => node.IsPylonless ).All( node => Math.Abs( node.Bank ) < 1e-6f ) );
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
		// The trains of a 1000 ms release rate stand closer than a train's length (0x43c1f0), so the ones that roll into the braking zone stop and the
		// ones behind bump them on again (0x43cd60) for a while before the last of them comes to rest.
		for ( var tick = 0; tick < 900 * 60 && !ride.Trains.All( train => train.Speed == 0 ); tick++ )
			ride.Advance( Tick );
		Assert.IsTrue( ride.Trains.All( train => train.Speed == 0 ), "without floors the trains coast and decay to a stop" );
		var flags = ride.Trains.Select( train => train.Flags ).ToArray();
		for ( var tick = 0; tick < 30 * 60; tick++ )
			ride.Advance( Tick );
		CollectionAssert.AreEqual( flags, ride.Trains.Select( train => train.Flags ).ToArray(), "the station step is skipped in mode 64" );
		ride.Command( 4, 0 );
		Assert.AreEqual( 1f, ride.PowerFactor );
		Assert.IsTrue( ride.Trains.All( train => train.Flags.HasFlag( CoasterTrainFlags.Headway ) && !train.Flags.HasFlag( CoasterTrainFlags.Bumped ) ), "leaving mode 64 flags every train 0x80 (0x435947)" );
		var moved = false;
		for ( var tick = 0; tick < 30 * 60 && !moved; tick++ )
		{
			ride.Advance( Tick );
			moved = ride.Trains.Any( train => train.Speed > 0 );
		}
		Assert.IsTrue( moved, "power back: trains run again" );
	}

	/// <summary>COAST 5 is gated (0x43b2f0): a repeated open or close changes nothing; COAST 6 floors at 0 and caps at one train's head sockets (0x43b330), and 0 means those sockets (0x43ba90).</summary>
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
		ride.Command( 6, 3 );
		Assert.AreEqual( 3, ride.SeatsPerTrain );
		ride.Command( 6, -5 );
		Assert.AreEqual( 16, ride.SeatsPerTrain, "the lower bound is the literal 0, and a capacity of 0 is one train's head sockets (0x43bb1b)" );
		ride.Command( 6, 3 );
		ride.Command( 6, 0 );
		Assert.AreEqual( 16, ride.SeatsPerTrain );
		ride.Command( 6, 100 );
		Assert.AreEqual( 16, ride.SeatsPerTrain, "the upper bound is one train's head sockets (the first train definition's +8)" );
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

	/// <summary>In mode 64 the station step is skipped (0x43b8b0): a train on the platform does not unload until the power is back.</summary>
	[TestMethod]
	public void BreakdownSkipsTheStationStep()
	{
		var (builder, _) = NewCoaster();
		BuildCircuit( builder );
		Assert.AreEqual( CoasterPlaceResult.Ok, CloseAndComplete( builder ) );
		var ride = builder.Ride;
		ride.Command( 8, 0 );
		// A built ride (state 2) runs no station step; the script opens and closes it, and a closed ride unloads arriving trains.
		ride.Command( 5, 0 );
		ride.Command( 5, 1 );
		var train = ride.Trains[0];
		ride.Command( 4, 1 );
		train.CarLists[0].Add( 7 );
		for ( var tick = 0; tick < 120; tick++ )
			ride.Advance( Tick );
		Assert.AreEqual( (1, 0), (train.Riders, ride.LeavingCount), "the rider stays on the train in mode 64" );
		Assert.IsFalse( train.Flags.HasFlag( CoasterTrainFlags.InStation ) );
		ride.Command( 4, 0 );
		ride.Advance( Tick );
		Assert.AreEqual( (0, 1), (train.Riders, ride.LeavingCount), "power back: the train unloads on its arrival" );
	}

	/// <summary>The PC runtime table (tp.exe 0x4708d0): 4096 entries of f32(fsin(k/4096 · f32(2π))), scale f32(4096 · f32(1/2π)); the swing rounds the index to nearest, the intersection pass truncates it.</summary>
	[TestMethod]
	public void ThePcSineTableAndItsNearestAndTruncatedIndices()
	{
		Assert.AreEqual( 4096f * 0.15915494f, CoasterTrig.PcScale );
		Assert.AreEqual( 0f, CoasterTrig.PcEntry( 0 ) );
		Assert.AreEqual( 1f, CoasterTrig.PcEntry( 1024 ), 1e-7f );
		Assert.AreEqual( (float)Math.Sin( (300 / 4096.0) * (double)6.2831855f ), CoasterTrig.PcEntry( 300 ), "T[k] = f32(sin(k/4096 · f32(2π)))" );
		// An angle whose scaled index is 100.6: the swing's fistp rounds to 101, the pass's truncating one gives 100.
		var angle = 100.6f / CoasterTrig.PcScale;
		Assert.AreEqual( CoasterTrig.PcEntry( 101 ), CoasterTrig.PcSin( angle ), "round to nearest (0x43c7be)" );
		Assert.AreEqual( CoasterTrig.PcEntry( 100 ), CoasterTrig.PcSinTruncated( angle ), "truncate (0x43a2b0 under the control word of 0x439ac4)" );
		// The cosine is the sine of the angle plus π/2, the sum carried unrounded by the swing and stored as single by the pass.
		Assert.AreEqual( CoasterTrig.PcEntry( (int)Math.Round( (angle + 1.5707964f) * (double)CoasterTrig.PcScale ) ), CoasterTrig.PcCos( angle ), 1e-6f );
		Assert.AreEqual( CoasterTrig.PcSin( -0.3f ), CoasterTrig.PcEntry( (int)Math.Round( -0.3f * CoasterTrig.PcScale ) ), "negative angles wrap by the mask" );
	}

	/// <summary>
	/// A 24-bit x87 stack for the independent evaluation of the swing's instruction sequence: every register result is a single, as at the thread's precision control
	/// (COAST-032); loads from memory and stores to memory are the singles themselves.
	/// </summary>
	private sealed class X87Stack
	{
		private readonly List<float> stack = new();
		public void Fld( float value ) => stack.Add( value );
		public void FldSt( int index ) => stack.Add( stack[stack.Count - 1 - index] );
		public void FMul( float memory ) => stack[^1] = stack[^1] * memory;
		public void FMulSt( int index ) => stack[^1] = stack[^1] * stack[stack.Count - 1 - index];
		public void FAddp() { var top = stack[^1]; stack.RemoveAt( stack.Count - 1 ); stack[^1] = stack[^1] + top; }
		public void FSubp() { var top = stack[^1]; stack.RemoveAt( stack.Count - 1 ); stack[^1] = stack[^1] - top; }
		public void FXch() => (stack[^1], stack[^2]) = (stack[^2], stack[^1]);
		public float Fstp() { var top = stack[^1]; stack.RemoveAt( stack.Count - 1 ); return top; }
		public void Pop() => stack.RemoveAt( stack.Count - 1 );
	}

	/// <summary>The suspended swing at the thread's 24-bit precision (tp.exe 0x43c76c..0x43ca45): one sample with all three angles set, evaluated again as the instruction sequence on a stack of singles.</summary>
	[TestMethod]
	public void SuspendedSwingRoundsEveryX87OperationToASingle()
	{
		var settings = PcSettings();
		settings["sCoasterType.bIsSuspendedCoaster"] = "1";
		settings["sCoasterType.fGravityX"] = "0.3";
		settings["sCoasterType.fGravityY"] = "-9.81";
		settings["sCoasterType.fGravityZ"] = "0.1";
		settings["sCoasterType.fForceMultiplier"] = "0.37";
		var definition = CoasterDefinition.FromValues( settings );
		var samples = new[] { new CoasterSample { Force = new SVector3( 1.37f, 4.21f, -2.19f ), Yaw = 0.81f, Pitch = -0.33f, Bank = 0.47f } };
		var force = samples[0].Force;
		CoasterRating.Swing( definition, samples, brakingStart: 100f, totalSamples: 1f );

		const float Pi = MathF.PI;
		var x = force.X + definition.GravityX;
		var y = force.Y + definition.GravityY;
		var z = force.Z + definition.GravityZ;
		float sin = CoasterTrig.PcSin( -0.81f ), cos = CoasterTrig.PcCos( -0.81f );
		var fpu = new X87Stack();
		fpu.Fld( sin ); fpu.Fld( cos ); // st0 = cos, st1 = sin
		fpu.Fld( x ); fpu.FldSt( 2 ); fpu.FMul( z ); fpu.FldSt( 2 ); fpu.FMul( x ); fpu.FAddp(); var newX = fpu.Fstp();
		fpu.FXch(); fpu.FMul( z ); fpu.FXch(); fpu.FMulSt( 2 ); fpu.FSubp(); var newZ = fpu.Fstp(); fpu.Pop();
		(x, z) = (newX, newZ);
		sin = CoasterTrig.PcSin( 0.33f ); cos = CoasterTrig.PcCos( 0.33f );
		fpu.Fld( sin ); fpu.Fld( cos );
		fpu.Fld( y ); fpu.FldSt( 1 ); fpu.FMul( y ); fpu.FldSt( 3 ); fpu.FMul( z ); fpu.FSubp(); var newY = fpu.Fstp();
		fpu.FXch(); fpu.FMul( z ); fpu.FXch(); fpu.FMulSt( 2 ); fpu.FAddp(); newZ = fpu.Fstp(); fpu.Pop();
		(y, z) = (newY, newZ);
		sin = CoasterTrig.PcSin( -0.47f ); cos = CoasterTrig.PcCos( -0.47f );
		fpu.Fld( sin ); fpu.Fld( cos );
		fpu.Fld( x ); fpu.FldSt( 1 ); fpu.FMul( x ); fpu.FldSt( 3 ); fpu.FMul( y ); fpu.FSubp(); newX = fpu.Fstp();
		fpu.FXch(); fpu.FMul( y ); fpu.FXch(); fpu.FMulSt( 2 ); fpu.FAddp(); newY = fpu.Fstp(); fpu.Pop();
		(x, y) = (newX, newY);

		var squares = x * x;
		squares = squares + y * y;
		squares = squares + z * z;
		var rate = MathF.Sqrt( squares ) * definition.ForceMultiplier;
		rate = rate > 1f ? 1f : rate;
		var theta = Math.Atan2( -x, y );
		var turned = (float)(theta - Pi); // fsub of the unrounded fpatan result, rounded to a single
		turned = (float)((double)turned + 9.42477822303772); // fsub qword [0x6fe048] (−3π)
		var d = (float)(turned % 6.2831854820251465 - Pi); // fmod (exact), then fsub π rounded to a single
		var weighted = d * rate;
		weighted = weighted + Pi;
		weighted = (float)((double)weighted + 9.42477822303772);
		var w = (float)(weighted % 6.2831854820251465 - Pi);
		var roll = -(Pi - w);
		if ( roll < -Pi )
			roll += 6.2831855f;
		Assert.AreEqual( BitConverter.SingleToInt32Bits( roll ), BitConverter.SingleToInt32Bits( samples[0].Bank ) );
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
