using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Asset-free tests of the track register round (October 14, docs/TRACKS.md): the 31 ms tick, the start heading, the water-crossing flag, the boats' spray,
/// engine voice and unload cues, and the break smoke emitter. The values follow the traced routines (BIN labels at the code sites), not captures.
/// </summary>
[TestClass]
public class TrackRegisterRoundTests
{
	private sealed class FlatGrid : IParkGrid
	{
		public int Width => 40;
		public int Height => 40;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	/// <summary>The eight-tile loop of the circuit tests with a controller of any type on it, recording its effects.</summary>
	private sealed class Rig
	{
		public readonly BumpController Controller;
		public readonly BumpWorld World = new();
		public readonly List<BumpEffect> Effects = new();
		private uint rng;

		public Rig( int type, uint seed = 12345, uint cosmetic = 0 )
		{
			rng = seed;
			World.CosmeticState = cosmetic;
			var template = BumperTemplates.Get( type );
			var cells = new ParkCellMap( 40, 40 );
			Controller = new BumpController( template, () => { rng = unchecked( rng * 1664525u + 1013904223u ); return rng >> 8; }, World );
			Controller.Effect += Effects.Add;
			if ( template.IsBumper )
				return;
			var builder = new TrackBuilder( cells, template.IsWater ? TrackFamily.Water : TrackFamily.Kart, () => null, new FlatGrid() );
			var station = TrackStation.FromPlacement( 10, 10, 0 );
			builder.PlaceStation( station );
			var writer = new TrackLineWriter( builder );
			writer.Commit( (14, 10), (14, 12) );
			writer.Commit( (14, 12), (8, 12) );
			writer.Commit( (8, 12), (8, 10) );
			var circuit = TrackCircuitWalker.WalkAndRefresh( builder, station );
			var centre = TrackStation.Centre( station.First );
			Controller.SetStation( centre.X, centre.Y, station.Heading );
			Controller.Rebuild( circuit );
		}

		public void Run( int ticks )
		{
			for ( var tick = 0; tick < ticks; tick++ )
				Controller.Tick();
		}

		public int Count( BumpEffectKind kind, int? id = null ) => Effects.Count( effect => effect.Kind == kind && (id == null || effect.Id == id) );
	}

	// ---- TRACK-011: the 31 ms tick -------------------------------------------------------------------

	[TestMethod]
	public void TheControllerTicksOncePer31MillisecondsWhateverTheStepSize()
	{
		foreach ( var step in new[] { 1.0 / 60, 1.0 / 30, 0.031, 0.5 } )
		{
			var rig = new Rig( -5 );
			var seconds = 0.0;
			for ( var index = 0; index < (int)(30 / step); index++ )
			{
				rig.Controller.Advance( step );
				seconds += step;
			}
			Assert.IsTrue( Math.Abs( rig.Controller.TickCount - seconds / 0.031 ) <= 1.0, $"step {step}: {rig.Controller.TickCount} ticks in {seconds:0.###} s" );
		}
		// the duration unit stays the original's 30 per second: BUMP 13 stores seconds × 30 ticks of 31 ms
		var timed = new Rig( -5 );
		Assert.AreEqual( 300, timed.Controller.Command( 13, 10 ) * 30 );
		Assert.AreEqual( 300, timed.Controller.Laps );
	}

	[TestMethod]
	public void ASaveFromBeforeThe31MillisecondTickStillRestores()
	{
		var rig = new Rig( -5 );
		rig.Controller.Command( 7, 0 );
		var saved = TrackSave.CaptureController( rig.Controller, 1 ) with { TickRemainder = 0.032 };
		rig.Controller.Restore( saved );
		Assert.AreEqual( 0.032, rig.Controller.TickRemainder, "a remainder of the old 1/30 s tick is carried" );
		rig.Controller.Advance( 0.001 );
		Assert.AreEqual( 1, rig.Controller.TickCount, "and runs off on the next advance" );
	}

	// ---- TRACK-012 / TRACK-023: the start heading ----------------------------------------------------

	[TestMethod]
	public void BoatsAndKartsStartAtHeadingZeroAndOnlyBumperCarsDrawARandomOne()
	{
		foreach ( var type in new[] { -5, -8, -4, -9, -10 } )
		{
			var rig = new Rig( type );
			rig.Controller.Command( 7, 0 );
			rig.Controller.Command( 1, 1 );
			Assert.AreEqual( 1, rig.Controller.Command( 4, 0 ) );
			var vehicle = rig.Controller.Vehicles.Single();
			Assert.AreEqual( (0, 0), (vehicle.Heading, vehicle.VisualHeading), $"type {type} starts at heading 0 whatever the station's flow (the zeroed 0xAC-byte record)" );
		}
		var headings = new HashSet<int>();
		for ( uint seed = 1; seed <= 12; seed++ )
		{
			var rig = new Rig( -6, seed );
			rig.Controller.Command( 7, 0 );
			rig.Controller.Command( 1, 1 );
			Assert.AreEqual( 1, rig.Controller.Command( 4, 0 ) );
			var car = rig.Controller.Vehicles.Single();
			Assert.AreEqual( car.Heading, car.VisualHeading, "a bumper car's steering and drawn heading take the same random value" );
			Assert.IsTrue( car.Heading is >= 0 and < 512 );
			headings.Add( car.Heading );
		}
		Assert.IsTrue( headings.Count > 6, "bumper cars take a random heading at their first launch" );
	}

	// ---- TRACK-017: the water crossing flag ----------------------------------------------------------

	private static TrackCircuit Crossing() => new( true, new[] { new TrackStep( 2, 3, TrackHeading.PosX, TrackHeading.PosX, 0x55, 1, false ) }, null );

	[TestMethod]
	public void OnlyTheWaterControllersFlagTheCentreCircleOfACrossing()
	{
		var water = TrackSections.Build( Crossing().Steps, 257, Array.Empty<Buoy>(), water: true ).Sections.Single();
		Assert.AreEqual( CollisionObject.Crossing, water.Objects[0].Flags & CollisionObject.Crossing, "the centre keep-in circle carries 0x80" );
		Assert.AreEqual( 0, water.Objects.Skip( 1 ).Count( item => (item.Flags & CollisionObject.Crossing) != 0 ), "the four keep-out circles do not" );
		Assert.AreEqual( CollisionObject.Used | CollisionObject.Sides, water.Objects[0].Flags & ~CollisionObject.Crossing );
		var kart = TrackSections.Build( Crossing().Steps, 1148, Array.Empty<Buoy>(), water: false ).Sections.Single();
		Assert.AreEqual( 0, kart.Objects.Count( item => (item.Flags & CollisionObject.Crossing) != 0 ), "karts get none" );
		foreach ( var type in new[] { -5, -8, -12, -13 } )
		{
			var controller = new BumpController( BumperTemplates.Get( type ), () => 0 );
			controller.Rebuild( Crossing() );
			Assert.AreEqual( CollisionObject.Crossing, controller.Objects.First( item => !item.IsKeepOut ).Flags & CollisionObject.Crossing, $"type {type}" );
		}
		var karts = new BumpController( BumperTemplates.Get( -4 ), () => 0 );
		karts.Rebuild( Crossing() );
		Assert.IsFalse( karts.Objects.Any( item => (item.Flags & CollisionObject.Crossing) != 0 ) );
	}

	[TestMethod]
	public void BoatsInAWaterCrossingPassThroughEachOtherWhereKartsInTheSameShapeCollide()
	{
		int Gap( int type )
		{
			var controller = new BumpController( BumperTemplates.Get( type ), () => 0 );
			var (cx, cy) = (2 * 3072 + 3072, 3 * 3072 + 3072);
			controller.SetStation( cx, cy, TrackHeading.PosX );
			controller.Rebuild( Crossing() );
			controller.Command( 7, 0 );
			Assert.AreEqual( 1, controller.Command( 4, 0 ) );
			Assert.AreEqual( 1, controller.Command( 4, 0 ) );
			var (first, second) = (controller.Vehicles[0], controller.Vehicles[1]);
			(first.X, first.Y) = (cx - 100, cy);
			(second.X, second.Y) = (cx + 100, cy);
			Assert.AreEqual( 0, first.ObjectIndex, "both boats are inside the crossing's centre circle" );
			first.ObjectIndex = second.ObjectIndex = 0;
			first.Flags = second.Flags = BumpVehicle.Live;
			controller.Tick();
			return BumpMath.Distance( second.X - first.X, second.Y - first.Y );
		}
		var boats = Gap( -5 );
		Assert.IsTrue( boats <= 200, $"the boats are not pushed apart (gap {boats})" );
		var karts = Gap( -4 );
		var radius = BumperTemplates.Get( -4 ).VehicleRadius;
		Assert.IsTrue( karts >= 2 * radius - 8, $"karts in the same shape are pushed apart to the sum of their radii (gap {karts})" );
	}

	// ---- TRACK-016: the boats' spray, engine voice and unload ----------------------------------------

	[TestMethod]
	public void ABoatLaunchStartsTheEngineVoiceAndTheSprayIsParticle96()
	{
		// find a cosmetic state whose first draw at the first 32nd tick is a spray: (s >> 16) & 7 == 2 after one LCG step
		uint state = 0;
		for ( uint candidate = 0; candidate < 100000; candidate++ )
		{
			if ( (((candidate * 214013u + 2531011u) >> 16) & 7) == 2 )
			{
				state = candidate;
				break;
			}
		}
		var rig = new Rig( -5, cosmetic: state );
		rig.Controller.Command( 7, 0 );
		rig.Controller.Command( 1, 1 );
		Assert.AreEqual( 1, rig.Controller.Command( 4, 0 ) );
		var boat = rig.Controller.Vehicles.Single();
		Assert.IsTrue( boat.Engine, "the first launch gives the boat an engine voice" );
		Assert.AreEqual( 1, rig.Effects.Count( effect => effect.Kind == BumpEffectKind.EngineStart && effect.Id == BumpEffects.SlotEngine ), "EventMap slot 0" );
		Assert.AreEqual( 0, rig.Count( BumpEffectKind.Particle ), "no particle at the launch (BeamUpCar is the kart launch's)" );
		// ticks 1..31 are not 32nd ticks, tick 32 is: TickCount & 0x1F == 0
		rig.Run( 31 );
		Assert.AreEqual( 0, rig.Count( BumpEffectKind.Particle, BumpEffects.ParticleSmallSplash ) );
		rig.Run( 1 );
		Assert.AreEqual( 32, rig.Controller.TickCount );
		Assert.AreEqual( 1, rig.Count( BumpEffectKind.Particle, BumpEffects.ParticleSmallSplash ), "the 32nd tick draws a spray: particle 96 (P_EFFECT_SmallSplash)" );
		Assert.AreEqual( 1, rig.Controller.Sprays );
		var spray = rig.Effects.Single( effect => effect.Id == BumpEffects.ParticleSmallSplash );
		Assert.AreEqual( (boat.Slot, boat.X, boat.Y), (spray.Slot, spray.X, spray.Y), "at the boat" );
		Assert.AreEqual( 96, BumpEffects.ParticleSmallSplash );
		// over a long run the sprays follow the counter, one in eight 32nd ticks on average
		var longRun = new Rig( -5 );
		longRun.Controller.Command( 7, 0 );
		longRun.Controller.Command( 1, 1 );
		longRun.Controller.Command( 4, 0 );
		longRun.Run( 32 * 80 );
		Assert.AreEqual( longRun.Controller.Sprays, longRun.Count( BumpEffectKind.Particle, BumpEffects.ParticleSmallSplash ), "every counted spray is a particle" );
		Assert.IsTrue( longRun.Controller.Sprays > 0 );
		Assert.AreEqual( 0, longRun.Count( BumpEffectKind.GlobalSound ) + longRun.Count( BumpEffectKind.EventSound ), "boats have no crash cue and no horn" );
		Assert.AreEqual( 0, longRun.Count( BumpEffectKind.EmitterStart ), "and no smoke" );
	}

	[TestMethod]
	public void AnUnloadedBoatPlaysBeamUpCarAndEndsItsVoiceWithoutAStopSound()
	{
		var rig = new Rig( -5 );
		rig.Controller.Command( 7, 0 );
		rig.Controller.Command( 1, 1 );
		rig.Controller.Command( 4, 0 );
		var boat = rig.Controller.Vehicles.Single();
		rig.Controller.Command( 16, 0 );
		Assert.AreEqual( 0, rig.Controller.VehicleCount );
		Assert.IsFalse( boat.Engine );
		Assert.AreEqual( 1, rig.Count( BumpEffectKind.Particle, BumpEffects.ParticleBeamUpCar ), "particle 15 at the unload" );
		Assert.AreEqual( 1, rig.Count( BumpEffectKind.EngineHalt ), "the voice stops silently" );
		Assert.AreEqual( 0, rig.Count( BumpEffectKind.EngineStop ), "the stop sound of EventMap slot 1 is the kart types' case" );
		// a boat that finishes its lap does the same
		var lap = new Rig( -5 );
		lap.Controller.Command( 7, 0 );
		lap.Controller.Command( 4, 0 );
		lap.Run( 900 );
		Assert.AreEqual( 0, lap.Controller.VehicleCount );
		Assert.AreEqual( 1, lap.Count( BumpEffectKind.Particle, BumpEffects.ParticleBeamUpCar ) );
		Assert.AreEqual( 1, lap.Count( BumpEffectKind.EngineHalt ) );
		// a re-walk ejects every boat the same way
		var ejected = new Rig( -8 );
		ejected.Controller.Command( 7, 0 );
		ejected.Controller.Command( 4, 0 );
		ejected.Controller.EjectAll();
		Assert.AreEqual( 1, ejected.Count( BumpEffectKind.EngineHalt ) );
	}

	[TestMethod]
	public void ABoatsVoiceAndSmokeFlagsAreSavedAndRestored()
	{
		var rig = new Rig( -5 );
		rig.Controller.Command( 7, 0 );
		rig.Controller.Command( 1, 1 );
		rig.Controller.Command( 4, 0 );
		var saved = TrackSave.CaptureController( rig.Controller, 1 );
		Assert.IsTrue( saved.Vehicles.Single().Engine );
		Assert.IsFalse( saved.Vehicles.Single().Smoke );
		rig.Controller.Restore( saved );
		Assert.IsTrue( rig.Controller.Vehicles.Single().Engine );
	}

	// ---- TRACK-023 / TRACK-025: the break smoke is an emitter ----------------------------------------

	[TestMethod]
	public void BUMP8GivesEveryCarryingKartAnEmitterThatFollowsItUntilTheFixOrTheUnload()
	{
		var rig = new Rig( -4, seed: 21 );
		var c = rig.Controller;
		c.Command( 7, 0 );
		c.Command( 1, 1 );
		c.Command( 4, 0 );
		c.Command( 1, 2 );
		c.Command( 4, 0 );
		c.Command( 14, 80 );
		c.Command( 3, 0 );
		rig.Run( 40 );
		Assert.AreEqual( 0, rig.Count( BumpEffectKind.EmitterStart ), "no emitter before the break" );
		c.Command( 8, 1 );
		Assert.AreEqual( BumpController.StateBroken, c.State );
		Assert.AreEqual( 2, rig.Count( BumpEffectKind.EmitterStart, BumpEffects.ParticleSmoke ), "one particle-2 emitter per carrying car" );
		Assert.IsTrue( c.Vehicles.All( car => car.Smoke ) );
		c.Command( 8, 1 );
		Assert.AreEqual( 2, rig.Count( BumpEffectKind.EmitterStart ), "a second break creates none: the cars already hold an emitter" );
		var before = rig.Count( BumpEffectKind.EmitterMove );
		rig.Run( 100 );
		Assert.AreEqual( before + 2 * 100, rig.Count( BumpEffectKind.EmitterMove ), "every tick moves each emitter to its car, not every 32nd tick" );
		Assert.AreEqual( 0, rig.Count( BumpEffectKind.Particle, BumpEffects.ParticleSmoke ), "the smoke is no one-shot particle any more" );
		// a car created after the break gets none
		c.Command( 1, 3 );
		Assert.AreEqual( 1, c.Command( 4, 0 ) );
		Assert.AreEqual( 2, rig.Count( BumpEffectKind.EmitterStart ), "cars created later do not get one" );
		Assert.IsFalse( c.Vehicles.Last().Smoke );
		// the unload of one smoking car destroys its emitter
		var first = c.Vehicles.First( car => car.Smoke );
		c.Retire( first );
		Assert.AreEqual( 1, rig.Count( BumpEffectKind.EmitterStop ) );
		Assert.AreEqual( first.Slot, rig.Effects.Single( effect => effect.Kind == BumpEffectKind.EmitterStop ).Slot );
		// the fix destroys the rest
		c.Command( 8, 0 );
		Assert.AreEqual( 2, rig.Count( BumpEffectKind.EmitterStop ), "the fix removes the emitter of the other smoking car" );
		Assert.IsTrue( c.Vehicles.All( car => !car.Smoke ) );
		var moves = rig.Count( BumpEffectKind.EmitterMove );
		rig.Run( 10 );
		Assert.AreEqual( moves, rig.Count( BumpEffectKind.EmitterMove ), "no more moves after the fix" );
	}

	[TestMethod]
	public void AKartWithoutASparksNodeAndABoatNeverSmoke()
	{
		var noNode = new Rig( -4 );
		noNode.Controller.HasSparksNode = false;
		noNode.Controller.Command( 7, 0 );
		noNode.Controller.Command( 1, 1 );
		noNode.Controller.Command( 4, 0 );
		noNode.Controller.Command( 8, 1 );
		Assert.AreEqual( 0, noNode.Count( BumpEffectKind.EmitterStart ) );
		var boat = new Rig( -5 );
		boat.Controller.Command( 7, 0 );
		boat.Controller.Command( 1, 1 );
		boat.Controller.Command( 4, 0 );
		boat.Controller.Command( 8, 1 );
		Assert.AreEqual( 0, boat.Count( BumpEffectKind.EmitterStart ), "water boats have no sparks node" );
		Assert.IsFalse( boat.Controller.Vehicles.Single().Smoke );
	}

	[TestMethod]
	public void ABrokenKartsEmitterSurvivesASaveAndTheRestoreKeepsTheFlag()
	{
		var rig = new Rig( -4, seed: 5 );
		var c = rig.Controller;
		c.Command( 7, 0 );
		c.Command( 1, 1 );
		c.Command( 4, 0 );
		c.Command( 8, 1 );
		var saved = TrackSave.CaptureController( c, 1 );
		Assert.IsTrue( saved.Vehicles.Single().Smoke );
		c.Restore( saved );
		Assert.IsTrue( c.Vehicles.Single().Smoke );
		// the smoke flag is presentation state: it is not part of the world hash
		var hashes = new List<ulong>();
		foreach ( var smoking in new[] { true, false } )
		{
			var scripts = new RideScriptWorld( 5 );
			var controller = new BumpController( BumperTemplates.Get( -4 ), () => 0, scripts.Bumpers );
			scripts.Bumpers.Add( controller );
			controller.Command( 7, 0 );
			controller.Command( 1, 1 );
			controller.Command( 4, 0 );
			controller.Vehicles.Single().Smoke = smoking;
			hashes.Add( WorldStateHash.Compute( new WorldStateSources { Scripts = scripts } ) );
		}
		Assert.AreEqual( hashes[0], hashes[1] );
	}

	// ---- TRACK-024: the bank has no clamp --------------------------------------------------------------

	[TestMethod]
	public void TheBankFollowsYawOver13WithoutAClamp()
	{
		foreach ( var yaw in new[] { 4, 20, 60, 120 } )
		{
			var pose = TrackSurface.KartPose( 50, 50, 0, 3, true, true, 0, yaw, ( x, y ) => 3f );
			Assert.AreEqual( -yaw / 13f * MathF.PI / 2, pose.Roll, 1e-4f, $"yaw {yaw}" );
		}
		var steep = TrackSurface.KartPose( 50, 50, 0, 3, true, true, 0, 120, ( x, y ) => 3f );
		Assert.IsTrue( steep.Roll < -2f, "past a quarter turn: the original's term has no clamp" );
	}
}
