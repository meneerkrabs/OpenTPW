using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Asset-free tests of the go-kart rides (docs/TRACKS.md, "Go-karts"): the grid, the race and its end, the thrust bias, the effects and the pose maths. The golden values freeze this
/// implementation of the traced rules; they are not captures of the original game.
/// </summary>
[TestClass]
public class KartControllerTests
{
	private sealed class FlatGrid : IParkGrid
	{
		public int Width => 40;
		public int Height => 40;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	/// <summary>The loop of the water tests laid with kart tiles, with a kart controller on it.</summary>
	private sealed class KartLoop
	{
		public readonly ParkCellMap Cells = new( 40, 40 );
		public readonly TrackBuilder Builder;
		public readonly TrackStation Station;
		public readonly BumpController Controller;
		public readonly BumpWorld World = new();
		public readonly TrackCircuit Circuit;
		public readonly List<BumpEffect> Effects = new();
		private uint rng = 777;

		public KartLoop( int type = -4, int rotation = 0, uint seed = 777 )
		{
			rng = seed;
			Builder = new TrackBuilder( Cells, TrackFamily.Kart, () => null, new FlatGrid() );
			Station = TrackStation.FromPlacement( 10, 10, 0 );
			Builder.PlaceStation( Station );
			var writer = new TrackLineWriter( Builder );
			writer.Commit( (14, 10), (14, 12) );
			writer.Commit( (14, 12), (8, 12) );
			writer.Commit( (8, 12), (8, 10) );
			Circuit = TrackCircuitWalker.WalkAndRefresh( Builder, Station );
			Controller = new BumpController( BumperTemplates.Get( type ), () => { rng = unchecked( rng * 1664525u + 1013904223u ); return rng >> 8; }, World );
			Controller.Effect += Effects.Add;
			var centre = TrackStation.Centre( Station.First );
			Controller.SetStation( centre.X, centre.Y, Station.Heading );
			Controller.Rebuild( Circuit );
		}

		public void Run( int ticks )
		{
			for ( var tick = 0; tick < ticks; tick++ )
				Controller.Tick();
		}

		/// <summary>Opens the ride, loads <paramref name="cars"/> karts with one guest each and lets them park on the grid.</summary>
		public void Load( int cars )
		{
			Controller.Command( 7, 0 );
			for ( var car = 0; car < cars; car++ )
			{
				Assert.AreEqual( 1, Controller.Command( 1, 100 + car ) );
				Assert.AreEqual( 1, Controller.Command( 4, 0 ) );
			}
			Run( 360 );
		}
	}

	// ---- The grid --------------------------------------------------------------------------------

	[TestMethod]
	public void TheGridBuoysLieInAStaggeredPairOfColumnsForEveryOrientation()
	{
		// orientation 0 (flow +X): slot i is (6 − i) × 0x400 along and 0x400 across, even slots on the positive side, then the finish buoy 0xC00 behind
		var east = TrackSections.KartStationBuoys( 1000, 2000, 0, 8 );
		Assert.AreEqual( 9, east.Count );
		var expected = new[] { (0x1800, 0x400), (0x1400, -0x400), (0x1000, 0x400), (0xC00, -0x400), (0x800, 0x400), (0x400, -0x400), (0, 0x400), (-0x400, -0x400) };
		for ( var slot = 0; slot < 8; slot++ )
		{
			Assert.AreEqual( (1000 + expected[slot].Item1, 2000 + expected[slot].Item2), (east[slot].X, east[slot].Y), $"slot {slot}" );
			Assert.AreEqual( Buoy.Used | Buoy.Skip, east[slot].Flags );
		}
		Assert.AreEqual( (1000 - 0xC00, 2000), (east[8].X, east[8].Y), "the finish buoy" );
		Assert.AreEqual( Buoy.Used | Buoy.Finish, east[8].Flags );
		// the other orientations swap the axes (bit 1) and flip x (bit 2) and y (bit 4) of the same local points
		foreach ( var heading in new[] { TrackHeading.PosX, TrackHeading.NegX, TrackHeading.PosY, TrackHeading.NegY } )
		{
			var orientation = TrackSections.OrientationOf( heading );
			var buoys = TrackSections.KartStationBuoys( 0, 0, orientation, 8 );
			var (dx, dy) = (TrackHeading.Dx( heading ), TrackHeading.Dy( heading ));
			for ( var slot = 0; slot < 8; slot++ )
			{
				var along = (6 - slot) * 0x400;
				var across = slot % 2 == 0 ? 0x400 : -0x400;
				// the along component is the flow direction; the across component is x for a flow along y and y for a flow along x, mirrored by the flip bits
				Assert.AreEqual( along, buoys[slot].X * dx + buoys[slot].Y * dy, $"heading {heading:X} slot {slot} lies along the flow" );
				Assert.AreEqual( Math.Abs( across ), Math.Abs( buoys[slot].X * dy + buoys[slot].Y * dx ), $"heading {heading:X} slot {slot} lies across" );
			}
			Assert.AreEqual( -0xC00, buoys[8].X * dx + buoys[8].Y * dy, $"heading {heading:X}: the finish buoy is behind the station" );
			Assert.AreEqual( 0, buoys[8].X * dy + buoys[8].Y * dx );
		}
		// the explicit words: 1 swaps, 2 flips x, 4 flips y
		Assert.AreEqual( (0x400, 0x1800), TrackSections.Oriented( 1, 0x1800, 0x400 ) );
		Assert.AreEqual( (-0x1800, 0x400), TrackSections.Oriented( 2, 0x1800, 0x400 ) );
		Assert.AreEqual( (0x1800, -0x400), TrackSections.Oriented( 4, 0x1800, 0x400 ) );
		Assert.AreEqual( (0x400, -0x1800), TrackSections.Oriented( 5, 0x1800, 0x400 ) );
	}

	[TestMethod]
	public void TheKartControllerBuildsTheGridAndTheFinishBuoyBeforeTheSectionBuoys()
	{
		var rig = new KartLoop();
		var c = rig.Controller;
		var centre = TrackStation.Centre( rig.Station.First );
		Assert.AreEqual( (centre.X, centre.Y), (c.StationX, c.StationY) );
		Assert.AreEqual( (c.StationX, c.StationY), (c.UnloadX, c.UnloadY), "karts spawn and unload at the station" );
		Assert.IsTrue( c.Buoys.Take( 8 ).All( buoy => buoy.IsSkipped ), "eight grid buoys first" );
		Assert.IsTrue( c.Buoys[8].IsFinish );
		Assert.AreEqual( 8, c.GridBuoys.Count() );
		Assert.AreEqual( 9, c.FirstAfterFinish!.Index, "the first section buoy follows the finish buoy" );
		Assert.IsTrue( c.Buoys.Skip( 9 ).All( buoy => !buoy.IsSkipped && !buoy.IsFinish ) );
	}

	[TestMethod]
	public void TheNthKartTargetsTheNthGridBuoyAndParksThere()
	{
		var rig = new KartLoop();
		var c = rig.Controller;
		c.Command( 7, 0 );
		Assert.AreEqual( BumpController.PhaseLoading, c.Phase );
		for ( var car = 0; car < 4; car++ )
		{
			c.Command( 1, 10 + car );
			Assert.AreEqual( 1, c.Command( 4, 0 ) );
			var vehicle = c.Vehicles.Last();
			Assert.AreEqual( car, vehicle.TargetBuoy, $"car {car + 1} aims at grid slot {car}" );
			Assert.AreEqual( BumpVehicle.Retarget | BumpVehicle.GridMode | BumpVehicle.LapMode, vehicle.Flags & (BumpVehicle.Retarget | BumpVehicle.GridMode | BumpVehicle.LapMode), "flags 0x10048" );
		}
		rig.Run( 400 );
		foreach ( var vehicle in c.Vehicles )
		{
			var grid = c.Buoys[vehicle.Slot];
			Assert.IsTrue( (vehicle.Flags & BumpVehicle.Slowed) != 0, $"slot {vehicle.Slot} parked (flag 0x80000)" );
			Assert.IsTrue( BumpMath.Distance( vehicle.X - grid.X, vehicle.Y - grid.Y ) <= c.Template.BuoyRadius, $"slot {vehicle.Slot} sits in its grid buoy's capture radius (the karts start at heading 0 and shove each other off their slots while they park, TRACK-012)" );
			Assert.AreEqual( c.FirstAfterFinish!.Index, vehicle.TargetBuoy, "a parked kart's target becomes the buoy after the finish" );
			Assert.IsTrue( vehicle.Speed < 40, "parked karts stand still" );
		}
		Assert.AreEqual( 1, c.Command( 11, 0 ) > 0 ? 1 : 0 );
		Assert.AreEqual( 4, c.Command( 11, 0 ) );
		Assert.IsTrue( rig.Effects.Any( effect => effect.Kind == BumpEffectKind.Particle && effect.Id == 15 ), "particle 15 at the spawn" );
		Assert.AreEqual( 4, rig.Effects.Count( effect => effect.Kind == BumpEffectKind.EngineStart ) );
	}

	[TestMethod]
	public void TheEightKartLimitRefusesTheNinthCar()
	{
		var rig = new KartLoop();
		rig.Controller.Command( 7, 0 );
		for ( var car = 0; car < 8; car++ )
		{
			rig.Controller.Command( 1, 20 + car );
			Assert.AreEqual( 1, rig.Controller.Command( 4, 0 ) );
		}
		rig.Controller.Command( 1, 99 );
		Assert.AreEqual( 0, rig.Controller.Command( 4, 0 ), "a template holds 8 vehicles" );
	}

	[TestMethod]
	public void TheModelIndexCyclesThroughTheTypesRange()
	{
		// the kart's mesh is count % +0x18 + +0x14, the count including the new kart: jungle and Halloween cycle four colours from 0, space cycles 1..4, fantasy has the one mesh 1
		foreach (var (type, expected) in new[]
		{
			(-4, new[] { 1, 2, 3, 0, 1, 2 }),
			(-7, new[] { 1, 2, 3, 0, 1, 2 }),
			(-9, new[] { 2, 3, 4, 1, 2, 3 }),
			(-10, new[] { 1, 1, 1, 1, 1, 1 })
		})
		{
			var rig = new KartLoop( type );
			rig.Controller.Command( 7, 0 );
			var seen = new List<int>();
			for ( var car = 0; car < 6; car++ )
			{
				rig.Controller.Command( 1, 30 + car );
				if ( rig.Controller.Command( 4, 0 ) == 0 )
				{
					// the eight-kart limit is not reached with six; a refusal would be a bug
					Assert.Fail( "launch refused" );
				}
				seen.Add( rig.Controller.Vehicles.Last().Variant );
			}
			CollectionAssert.AreEqual( expected, seen, $"type {type}" );
		}
	}

	[TestMethod]
	public void OnlyTheSpaceAndFantasyKartsHover()
	{
		foreach ( var (type, hover) in new[] { (-4, false), (-7, false), (-9, true), (-10, true) } )
		{
			var rig = new KartLoop( type );
			rig.Controller.Command( 7, 0 );
			rig.Controller.Command( 1, 1 );
			rig.Controller.Command( 4, 0 );
			var flags = rig.Controller.Vehicles.Single().Flags;
			Assert.AreEqual( hover ? BumpVehicle.HoverKartFlags : 0, flags & BumpVehicle.HoverKartFlags, $"type {type}: flags 0x3C00 are set at the first launch of the space and fantasy karts only" );
		}
	}

	// ---- The race --------------------------------------------------------------------------------

	[TestMethod]
	public void StartingTheRaceReleasesTheKartsWithAThrustBiasFromTheWorldGenerator()
	{
		var rig = new KartLoop();
		var c = rig.Controller;
		rig.Load( 4 );
		c.Command( 14, 3 );
		Assert.AreEqual( -3, c.Laps );
		c.Command( 3, 0 );
		Assert.AreEqual( BumpController.PhaseRunning, c.Phase );
		var accel = c.Accel;
		foreach ( var vehicle in c.Vehicles )
		{
			Assert.AreEqual( -3, vehicle.Laps, "+0x88 takes the controller's laps" );
			Assert.AreEqual( 0, vehicle.Flags & (BumpVehicle.Slowed | BumpVehicle.GridMode), "the start clears flags 0x80000 and 0x40" );
			Assert.IsTrue( vehicle.AccelBonus >= -4 * (accel / 9) && vehicle.AccelBonus <= 3 * (accel / 9), $"bias {vehicle.AccelBonus} lies within (3 − rand & 7) × accel / 9" );
			Assert.AreEqual( 0, vehicle.AccelBonus % (accel / 9) );
		}
		Assert.AreEqual( 0, c.Command( 3, 0 ), "BUMP 3 leaves the accumulator alone" );
		Assert.AreEqual( BumpController.PhaseRunning, c.Phase, "a second BUMP 3 does nothing" );
	}

	[TestMethod]
	public void TheBiasIsRolledAgainEveryLapAndSpansTheTemplateRange()
	{
		var rig = new KartLoop( seed: 4242 );
		var c = rig.Controller;
		rig.Load( 1 );
		c.Command( 14, 4 );
		c.Command( 3, 0 );
		var kart = c.Vehicles.Single();
		var biases = new HashSet<int> { kart.AccelBonus };
		var last = kart.AccelBonus;
		var rerolls = 0;
		for ( var tick = 0; tick < 60 * 30 && c.VehicleCount == 1; tick++ )
		{
			c.Tick();
			if ( kart.AccelBonus != last )
			{
				rerolls++;
				last = kart.AccelBonus;
				biases.Add( last );
			}
		}
		Assert.IsTrue( rerolls >= 2, $"the finish crossings roll the bias again ({rerolls} changes)" );
		Assert.IsTrue( biases.All( bias => bias >= -4 * (c.Accel / 9) && bias <= 3 * (c.Accel / 9) ), "the bias stays within the range" );
		Assert.IsTrue( biases.Count >= 2 );
	}

	[TestMethod]
	public void TheFirstKartHomeEndsTheRaceAndTheOthersLeaveAtTheirNextCrossing()
	{
		var rig = new KartLoop( seed: 31 );
		var c = rig.Controller;
		rig.Load( 3 );
		c.Command( 14, 2 );
		c.Command( 3, 0 );
		var phases = new List<int> { c.Phase };
		var order = new List<int>();
		var before = c.Vehicles.Select( vehicle => vehicle.Slot ).ToHashSet();
		for ( var tick = 0; tick < 120 * 30 && c.VehicleCount > 0; tick++ )
		{
			var live = c.Vehicles.Select( vehicle => vehicle.Slot ).ToHashSet();
			c.Tick();
			foreach ( var gone in live.Except( c.Vehicles.Select( vehicle => vehicle.Slot ) ) )
				order.Add( gone );
			if ( phases[^1] != c.Phase )
				phases.Add( c.Phase );
		}
		Assert.AreEqual( 0, c.VehicleCount, "every kart came home" );
		CollectionAssert.AreEqual( new[] { BumpController.PhaseRunning, BumpController.PhaseOver, BumpController.PhaseLoading }, phases, "the race runs (2), the first finisher ends it (3), the last kart out resets to loading (1)" );
		Assert.AreEqual( 3, order.Count );
		Assert.AreEqual( before.Count, order.Distinct().Count() );
		Assert.AreEqual( 3, c.Leaving.Count, "each kart's guest went to the leaving list" );
		// every kart's rider is handed back, the newest unloaded kart's guest first
		var handed = new List<int>();
		for ( var guest = c.Command( 2, 0 ); guest != 0; guest = c.Command( 2, 0 ) )
			handed.Add( guest );
		CollectionAssert.AreEquivalent( new[] { 100, 101, 102 }, handed );
		var unloadOrderGuests = order.Select( slot => 100 + slot ).Reverse().ToArray();
		CollectionAssert.AreEqual( unloadOrderGuests, handed, "BUMP 2 pops the head of the leaving list, which holds the last kart out first" );
		Assert.IsTrue( rig.Effects.Count( effect => effect.Kind == BumpEffectKind.EngineStop ) == 3 && rig.Effects.Count( effect => effect.Kind == BumpEffectKind.Particle && effect.Id == 15 ) >= 6, "each kart plays EventMap slot 1 and particle 15 at the unload" );
	}

	[TestMethod]
	public void ClosingTheRideEndsTheRaceAtTheNextFinishCrossing()
	{
		var rig = new KartLoop( seed: 9 );
		var c = rig.Controller;
		rig.Load( 2 );
		c.Command( 14, 50 );
		c.Command( 3, 0 );
		rig.Run( 30 * 6 );
		c.Command( 6, 0 );
		Assert.AreEqual( BumpController.PhaseClosed, c.Phase );
		Assert.AreEqual( 2, c.VehicleCount, "closing does not unload karts at once" );
		for ( var tick = 0; tick < 120 * 30 && c.VehicleCount > 0; tick++ )
			c.Tick();
		Assert.AreEqual( 0, c.VehicleCount, "both karts left at their next finish crossing" );
		Assert.AreEqual( BumpController.PhaseLoading, c.Phase, "the last kart out resets a kart ride to loading, as a closed ride's routine does" );
	}

	[TestMethod]
	public void TheLastKartOutResetsToLoadingForKartsButNotForWaterBoats()
	{
		var kart = new KartLoop();
		kart.Controller.Command( 7, 0 );
		kart.Controller.Command( 1, 5 );
		kart.Controller.Command( 4, 0 );
		kart.Controller.Command( 3, 0 );
		kart.Controller.Command( 10, 0 );
		Assert.AreEqual( BumpController.PhaseClosed, kart.Controller.Phase, "BUMP 10 ejects every kart and closes" );
		Assert.AreEqual( 5, kart.Controller.Command( 2, 0 ) );
		var water = new BumpController( BumperTemplates.Get( -5 ), () => 0 );
		water.Command( 7, 0 );
		Assert.AreEqual( BumpController.PhaseRunning, water.Phase );
		water.SetStation( 0, 0, TrackHeading.PosX );
		water.Command( 1, 6 );
		water.Command( 4, 0 );
		water.UnloadOne();
		Assert.AreEqual( BumpController.PhaseRunning, water.Phase, "a water ride keeps its phase when the last boat is gone" );
	}

	[TestMethod]
	public void BumpThirteenAndFourteenSetDurationAndLaps()
	{
		var rig = new KartLoop();
		var c = rig.Controller;
		Assert.AreEqual( -3, c.Laps, "the template starts a kart ride at three laps" );
		Assert.AreEqual( 4, c.Command( 14, 4 ) );
		Assert.AreEqual( -4, c.Laps );
		Assert.AreEqual( 5, c.Command( 13, 5 ) );
		Assert.AreEqual( 150, c.Laps, "BUMP 13 stores seconds × 30" );
		c.Command( 14, 0 );
		Assert.AreEqual( 0, c.Laps );
	}

	[TestMethod]
	public void ABrokenKartCoastsWithoutThrustOrTurning()
	{
		var rig = new KartLoop( seed: 5 );
		var c = rig.Controller;
		rig.Load( 1 );
		c.Command( 14, 50 );
		c.Command( 3, 0 );
		rig.Run( 120 );
		var kart = c.Vehicles.Single();
		Assert.IsTrue( kart.Speed > 60, $"the kart is running ({kart.Speed})" );
		c.Command( 8, 1 );
		Assert.AreEqual( BumpController.StateBroken, c.State );
		var heading = kart.Heading;
		var laps = kart.Laps;
		rig.Run( 90 );
		Assert.IsTrue( kart.Speed < 20, "a broken kart damps by 1000 / 1024 and neither thrusts nor turns" );
		Assert.AreEqual( laps, kart.Laps, "the countdown is paused" );
		Assert.AreEqual( heading, kart.Heading, "no turning" );
		c.Command( 8, 0 );
		rig.Run( 90 );
		Assert.IsTrue( kart.Speed > 40, "a repaired ride thrusts again" );
	}

	// ---- Effects ---------------------------------------------------------------------------------

	[TestMethod]
	public void TheHornSoundsEvery32TicksAboveSpeed230OneTimeInEight()
	{
		var rig = new KartLoop( seed: 11 );
		var c = rig.Controller;
		rig.Load( 1 );
		c.Command( 14, 50 );
		c.Command( 3, 0 );
		var toots = new List<long>();
		var requestTicks = new List<long>();
		var kart = c.Vehicles.Single();
		for ( var tick = 0; tick < 60 * 30 && c.VehicleCount > 0; tick++ )
		{
			var before = rig.Effects.Count;
			c.Tick();
			if ( kart.EffectRequest == BumpEffects.RequestToot || kart.EffectLatch == BumpEffects.RequestToot )
				requestTicks.Add( c.TickCount );
			foreach ( var effect in rig.Effects.Skip( before ).Where( effect => effect.Kind == BumpEffectKind.EventSound && effect.Id == BumpEffects.SlotToot ) )
				toots.Add( c.TickCount );
		}
		Assert.IsTrue( requestTicks.Count > 0, "a fast kart asks for the horn" );
		Assert.IsTrue( requestTicks.All( tick => tick >= 32 ), "requests start on a 32nd tick" );
		Assert.IsTrue( toots.Count >= 1, "the horn plays once the 1-in-8 draw hits" );
		Assert.IsTrue( toots.Count < requestTicks.Count || requestTicks.Count <= 2, "a latched horn waits for the draw, so it plays less often than it is requested" );
	}

	[TestMethod]
	public void TheCrashCodeIsCrunchForKartsAndSilentForBoats()
	{
		// two karts head-on at speed: the impulse is far above 130
		var rig = new KartLoop();
		var c = rig.Controller;
		c.Command( 7, 0 );
		c.Command( 1, 1 );
		c.Command( 4, 0 );
		c.Command( 1, 2 );
		c.Command( 4, 0 );
		var (a, b) = (c.Vehicles[0], c.Vehicles[1]);
		(a.X, a.Y, b.X, b.Y) = (c.StationX, c.StationY, c.StationX + 1500, c.StationY);
		(a.VX0, a.VY0, a.VX, a.VY) = (400, 0, 400, 0);
		(b.VX0, b.VY0, b.VX, b.VY) = (-400, 0, -400, 0);
		a.Flags &= ~BumpVehicle.Steerable;
		b.Flags &= ~BumpVehicle.Steerable;
		var before = rig.Effects.Count;
		c.Tick();
		Assert.IsTrue( rig.Effects.Skip( before ).Any( effect => effect.Kind == BumpEffectKind.GlobalSound && effect.Id == BumpEffects.SoundCrunch ), "a hard kart crash plays global cat_rides 12" );
		Assert.IsTrue( ((a.Flags | b.Flags) & BumpVehicle.Crunched) != 0 );
		Assert.AreEqual( 1, rig.Effects.Skip( before ).Count( effect => effect.Kind == BumpEffectKind.GlobalSound && effect.Id == BumpEffects.SoundCrunch ), "once per tick per pair" );
		// water boats make no sound
		var water = new BumpController( BumperTemplates.Get( -5 ), () => 0 );
		var raised = 0;
		water.Effect += _ => raised++;
		water.SetStation( 0, 0, TrackHeading.PosX );
		water.Command( 7, 0 );
		water.Command( 1, 1 );
		water.Command( 4, 0 );
		water.Command( 1, 2 );
		water.Command( 4, 0 );
		var boatEffects = new List<BumpEffect>();
		water.Effect += boatEffects.Add;
		water.Tick();
		Assert.AreEqual( 0, boatEffects.Count( effect => effect.Kind != BumpEffectKind.EngineParameter ), "a boat's tick raises no crash, horn or smoke effect (the water types are absent from 0x546C80's switch)" );
		Assert.AreEqual( 2, boatEffects.Count( effect => effect.Kind == BumpEffectKind.EngineParameter ), "only its engine voice's move and parameter (0x548104)" );
		Assert.AreEqual( 4, raised, "the launches' engine voices ([BIN:TPW-EXE:0x54A040], TRACK-016) and one voice tick per boat" );
	}

	[TestMethod]
	public void AWornKartPuffsEvery32TicksAboveSpeed80WithTheCosmeticGenerator()
	{
		var rig = new KartLoop( seed: 21 );
		var c = rig.Controller;
		rig.Load( 1 );
		c.Command( 14, 80 );
		c.Command( 3, 0 );
		c.Command( 9, 1 );
		Assert.AreEqual( BumpController.StateWorn, c.State );
		rig.Run( 30 * 40 );
		var sparks = rig.Effects.Count( effect => effect.Kind == BumpEffectKind.Particle && effect.Id == BumpEffects.ParticleSparks );
		var puffs = rig.Effects.Count( effect => effect.Kind == BumpEffectKind.Particle && effect.Id == BumpEffects.ParticleExhaustPuff );
		var exhaustSounds = rig.Effects.Count( effect => effect.Kind == BumpEffectKind.GlobalSound && effect.Id == BumpEffects.SoundExhaust );
		Assert.IsTrue( sparks > 0, "sparks (draw 0 or 1)" );
		Assert.IsTrue( puffs > 0, "exhaust puffs (draw 2)" );
		Assert.AreEqual( puffs, exhaustSounds, "each puff asks for global cat_rides 24" );
		var worn = new KartLoop( seed: 21 );
		worn.Load( 1 );
		worn.Controller.Command( 14, 80 );
		worn.Controller.Command( 3, 0 );
		worn.Run( 30 * 40 );
		Assert.AreEqual( 0, worn.Effects.Count( effect => effect.Kind == BumpEffectKind.Particle && effect.Id is BumpEffects.ParticleSparks or BumpEffects.ParticleExhaustPuff ), "a fine kart makes neither (no shipped kart script issues BUMP 9)" );
	}

	[TestMethod]
	public void TheAnimationSwitchesAtSpeed40AndTheHoverKartsStayOnFive()
	{
		foreach ( var (type, returns) in new[] { (-4, true), (-9, false) } )
		{
			var rig = new KartLoop( type, seed: 3 );
			var c = rig.Controller;
			c.Command( 7, 0 );
			c.Command( 1, 1 );
			c.Command( 4, 0 );
			Assert.AreEqual( 12, c.Vehicles.Single().Animation, "a new kart plays animation 12" );
			c.Command( 14, 50 );
			c.Command( 3, 0 );
			rig.Run( 90 );
			var kart = c.Vehicles.Single();
			Assert.AreEqual( 5, kart.Animation, $"type {type} drives on animation 5" );
			c.Command( 8, 1 );
			rig.Run( 200 );
			Assert.AreEqual( returns ? 12 : 5, kart.Animation, $"type {type}: a standing kart returns to 12 {(returns ? "" : "only for the ground karts")}" );
			Assert.IsTrue( rig.Effects.Any( effect => effect.Kind == BumpEffectKind.Animation && effect.Id == 5 ) );
		}
	}

	// ---- The pose --------------------------------------------------------------------------------

	[TestMethod]
	public void TheKartPoseTakesFourProbesAndHoversWithABob()
	{
		// a kart on level ground: height is the ground, no pitch or roll
		var flat = TrackSurface.KartPose( 50, 50, 0, 3, false, false, 0, 0, ( x, y ) => 3f );
		Assert.AreEqual( (3f, 0f, 0f, 3f), (flat.Height, flat.Pitch, flat.Roll, flat.ShadowHeight) );
		// a slope rising along +Y: facing +Y the nose is up by atan(rise / 2R), the height is the mean
		var slope = TrackSurface.KartPose( 50, 50, 0, 3, false, false, 0, 0, ( x, y ) => (y - 50) * 0.5f + 3 );
		Assert.AreEqual( MathF.Atan2( 3f, 6f ), slope.Pitch, 1e-5f );
		Assert.AreEqual( 0f, slope.Roll, 1e-5f );
		Assert.AreEqual( 3f, slope.Height, 1e-5f );
		// a slope rising along +X: facing +Y the right side is up
		var side = TrackSurface.KartPose( 50, 50, 0, 3, false, false, 0, 0, ( x, y ) => (x - 50) * 0.5f + 3 );
		Assert.AreEqual( MathF.Atan2( 3f, 6f ), side.Roll, 1e-5f );
		Assert.AreEqual( 0f, side.Pitch, 1e-5f );
		// turned a quarter turn (facing +X) the same slope is a pitch
		var turned = TrackSurface.KartPose( 50, 50, 128, 3, false, false, 0, 0, ( x, y ) => (x - 50) * 0.5f + 3 );
		Assert.AreEqual( MathF.Atan2( 3f, 6f ), turned.Pitch, 1e-4f );
		// a hover kart: 1.6 above the front/back mean, no roll of its own, the shadow on the four-probe mean
		var hover = TrackSurface.KartPose( 50, 50, 0, 3, true, false, 0, 0, ( x, y ) => 3f + (x - 50) * 0.5f );
		Assert.AreEqual( 3f + TrackSurface.HoverHeight, hover.Height, 1e-5f, "age 0: sin(0) = 0" );
		Assert.AreEqual( 0f, hover.Roll, 1e-6f, "no roll, whatever the surface does across" );
		Assert.AreEqual( 3f, hover.ShadowHeight, 1e-5f );
		// the bob follows the 256-scaled sine at age × 6 with amplitude 256 × 0.0016667
		var bobbed = TrackSurface.KartPose( 50, 50, 0, 3, true, false, 21, 0, ( x, y ) => 3f );
		Assert.AreEqual( 3f + TrackSurface.HoverHeight + BumpMath.Sin( 126 ) * 0.0016666667f, bobbed.Height, 1e-5f );
		Assert.IsTrue( Math.Abs( BumpMath.Sin( 126 ) * 0.0016666667f ) <= 0.43f );
		// banking into a right turn (positive yaw): the right side goes down; the original's term has no clamp (0x5469C3..0x5469ED, TRACK-024)
		var bank = TrackSurface.KartPose( 50, 50, 0, 3, true, true, 0, 4, ( x, y ) => 3f );
		Assert.AreEqual( -4 / 13f * MathF.PI / 2, bank.Roll, 1e-5f );
		var hard = TrackSurface.KartPose( 50, 50, 0, 3, true, true, 0, 90, ( x, y ) => 3f );
		Assert.AreEqual( -90 / 13f * MathF.PI / 2, hard.Roll, 1e-4f, "a hard yaw banks past a quarter turn: the original does not clamp" );
	}

	// ---- Saves -----------------------------------------------------------------------------------

	[TestMethod]
	public void AKartRaceSavesAndRestoresExactly()
	{
		var rig = new KartLoop( seed: 77 );
		rig.Load( 3 );
		rig.Controller.Command( 14, 3 );
		rig.Controller.Command( 3, 0 );
		rig.Run( 200 );
		var saved = TrackSave.CaptureController( rig.Controller, 7 );
		Assert.AreEqual( 3, saved.Vehicles.Count );
		Assert.IsTrue( saved.Vehicles.All( vehicle => vehicle.Animation is 5 or 12 ) );
		var second = new KartLoop( seed: 77 );
		second.Controller.Restore( saved );
		second.World.CosmeticState = rig.World.CosmeticState;
		var hashA = new StateHasher();
		var hashB = new StateHasher();
		rig.Controller.AddCanonicalState( hashA );
		second.Controller.AddCanonicalState( hashB );
		Assert.AreEqual( hashA.ToString(), hashB.ToString(), "the restored controller digests like the saved one, the kart fields included" );
		Assert.AreEqual( rig.Controller.Phase, second.Controller.Phase );
		foreach ( var (original, restored) in rig.Controller.Vehicles.Zip( second.Controller.Vehicles ) )
		{
			Assert.AreEqual( (original.Flags, original.Laps, original.AccelBonus, original.TargetBuoy, original.VisualHeading, original.Animation), (restored.Flags, restored.Laps, restored.AccelBonus, restored.TargetBuoy, restored.VisualHeading, restored.Animation) );
		}
		// the continuation matches tick by tick (the generators are not shared, so only the bias draws could differ: run to the next finish with the same seed state is not required)
		rig.Run( 5 );
		second.Run( 5 );
		Assert.AreEqual( rig.Controller.Vehicles[0].X, second.Controller.Vehicles[0].X );
	}

	[TestMethod]
	public void ABrokenKartFieldFailsTheShapeCheck()
	{
		var rig = new KartLoop();
		rig.Load( 1 );
		var saved = TrackSave.CaptureController( rig.Controller, 7 );
		Assert.AreEqual( 12, saved.Vehicles[0].Animation, "a parked kart plays animation 12" );
		var copy = saved with { Vehicles = saved.Vehicles.Select( vehicle => vehicle with { Height = float.NaN } ).ToList() };
		var restored = new KartLoop();
		restored.Controller.Restore( copy );
		Assert.AreEqual( 0f, restored.Controller.Vehicles[0].Height, "a NaN height is reset, as the original does" );
	}

	[TestMethod]
	public void ACarCreatedWhileTheRaceIsOverIsRefused()
	{
		var rig = new KartLoop( seed: 31 );
		var c = rig.Controller;
		rig.Load( 2 );
		c.Command( 14, 1 );
		c.Command( 3, 0 );
		for ( var tick = 0; tick < 120 * 30 && c.Phase != BumpController.PhaseOver; tick++ )
			c.Tick();
		Assert.AreEqual( BumpController.PhaseOver, c.Phase );
		c.Command( 1, 500 );
		Assert.AreEqual( 0, c.Command( 4, 0 ), "no kart is created once the race is over (TRACK-028)" );
		for ( var tick = 0; tick < 120 * 30 && c.VehicleCount > 0; tick++ )
			c.Tick();
		Assert.AreEqual( 0, c.VehicleCount );
		Assert.AreEqual( BumpController.PhaseLoading, c.Phase, "the phase resets, nothing is stuck on the station" );
	}

	[TestMethod]
	public void ACarCreatedMidRaceAimsAtTheBuoyAfterTheFinishAndRunsAFullCircuit()
	{
		var rig = new KartLoop( seed: 12 );
		var c = rig.Controller;
		rig.Load( 1 );
		c.Command( 14, 3 );
		c.Command( 3, 0 );
		rig.Run( 60 );
		c.Command( 1, 600 );
		Assert.AreEqual( 1, c.Command( 4, 0 ) );
		var late = c.Vehicles.Last();
		Assert.AreEqual( c.FirstAfterFinish!.Index, late.TargetBuoy, "0x54A040's phase-2 branch gives a new car the buoy after the finish (+0xA4)" );
		Assert.AreEqual( 0, late.Flags & BumpVehicle.GridMode );
		Assert.IsTrue( (late.Flags & BumpVehicle.LapMode) != 0 );
		Assert.AreEqual( -3, late.Laps );
	}

	[TestMethod]
	public void ClosingDuringLoadingKeepsTheParkedKartsAndBumpTenOrAReopenRecoversThem()
	{
		var rig = new KartLoop( seed: 8 );
		var c = rig.Controller;
		rig.Load( 2 );
		c.Command( 6, 0 );
		Assert.AreEqual( BumpController.PhaseClosed, c.Phase );
		Assert.AreEqual( 2, c.VehicleCount, "the close routine leaves the parked karts aboard (0x544A10)" );
		c.Command( 3, 0 );
		Assert.AreEqual( BumpController.PhaseClosed, c.Phase, "the start needs phase 1" );
		// a reopen leaves them parked for BUMP 3
		c.Command( 7, 0 );
		Assert.AreEqual( BumpController.PhaseLoading, c.Phase );
		c.Command( 3, 0 );
		Assert.AreEqual( BumpController.PhaseRunning, c.Phase );
		// the script's own recovery: BUMP 10 unloads every kart and closes
		var other = new KartLoop( seed: 8 );
		other.Load( 2 );
		other.Controller.Command( 6, 0 );
		other.Controller.Command( 10, 0 );
		Assert.AreEqual( 0, other.Controller.VehicleCount );
		Assert.AreEqual( 2, other.Controller.Leaving.Count, "both riders are handed back" );
		Assert.AreEqual( BumpController.PhaseClosed, other.Controller.Phase );
	}

	[TestMethod]
	public void EffectsCarryTheVehiclesPresentationHeight()
	{
		var rig = new KartLoop( seed: 3 );
		rig.Controller.Command( 7, 0 );
		rig.Controller.Command( 1, 1 );
		rig.Controller.Command( 4, 0 );
		rig.Controller.Vehicles.Single().Height = 4.25f;
		rig.Effects.Clear();
		rig.Run( 3 );
		Assert.IsTrue( rig.Effects.Count > 0 && rig.Effects.All( effect => effect.Height == 4.25f ), "the engine parameter effect carries the height the pose probes found" );
	}
}
