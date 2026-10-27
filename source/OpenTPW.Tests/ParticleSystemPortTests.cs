using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.ParticleSystemTests;

namespace OpenTPW.Tests;

/// <summary>
/// The rules of the particle pool as ported from tp.exe's instructions (docs/PARTICLES.md, PTCL-902/903): the random forms, LIFO slots
/// and the global generation, the rate quarter, burst and cap, the spawn shapes, colour, frame, size and spin, dead emitters, child
/// rules, effectors and the spiral, plus the optional save part. Synthetic templates; the corpus case reads the shipped file.
/// </summary>
[TestClass]
public class ParticleSystemPortTests
{
	private static uint Lcg( uint state, int steps )
	{
		for ( var index = 0; index < steps; index++ )
			state = unchecked( state * 214013u + 2531011u );
		return state;
	}

	private static ParticleEffectorTemplate Effector( params (int Offset, int Size, int Value)[] fields )
	{
		var bytes = new byte[ParticleEffectorTemplate.Size];
		foreach ( var (offset, size, value) in fields )
			BitConverter.GetBytes( value ).AsSpan( 0, size ).CopyTo( bytes.AsSpan( offset ) );
		if ( !fields.Any( field => field.Offset == 0x46 ) )
			BitConverter.GetBytes( (short)-1 ).CopyTo( bytes, 0x46 );
		return new ParticleEffectorTemplate( bytes );
	}

	private static List<ParticleSprite> Sprites( ParticleSystem system )
	{
		var list = new List<ParticleSprite>();
		system.Snapshot( list );
		return list;
	}

	// ---- random forms ----

	[TestMethod]
	public void RandomGeneratorIsTheMicrosoftLcgSeededWithOne()
	{
		var system = New( Pool() );
		Assert.AreEqual( 1u, system.RandomState );
		// type 1 draws the emitter velocity jitter: three draws when word +0x12 is not zero
		var jittered = New( Pool( (1, Template( Life( 3 ), (0x12, 2, 5) )) ) );
		jittered.Create( 1, Origin );
		Assert.AreEqual( Lcg( 1, 3 ), jittered.RandomState );
		var plain = New( Pool( (1, Template( Life( 3 )) ) ) );
		plain.Create( 1, Origin );
		Assert.AreEqual( 1u, plain.RandomState, "a draw is consumed only when the modulus is not zero" );
	}

	[TestMethod]
	public void EmitterJitterOfMinusOneAddsNothingButStillConsumesThreeDraws()
	{
		// 92 shipped templates have j = -1: sar(state) % -1 is 0
		var system = New( Pool( (1, Template( Life( 3 ), (0x12, 2, -1) )) ) );
		system.Create( 1, Origin );
		Assert.AreEqual( Lcg( 1, 3 ), system.RandomState, "three draws for the emitter velocity" );
		var moving = New( Pool( (1, Template( Life( 3 ), (0x12, 2, -1), Burst( 1 ), ParticleLife( 1000 ), (0x6C, 1, 1) )) ) );
		moving.Create( 1, Origin );
		Run( moving, 3 );
		Assert.AreEqual( 0, Sprites( moving )[0].X, "the emitter velocity stayed zero" );
	}

	[TestMethod]
	public void SignedFormGivesPlusMinusNMinusOneAndUnsignedFormStaysPositive()
	{
		// velocity jitter n = 5: -4..4; random rotation: (state >> 16 & 0x7FFF)
		var system = New( Pool( (1, Template( Life( 1 ), Burst( 400 ), Cap( 1000 ), ParticleLife( 1000 ), (0x50, 2, 5), (0x6D, 1, 1) )) ) );
		system.Create( 1, Origin );
		Run( system, 3 );
		var sprites = Sprites( system );
		Assert.AreEqual( 400, sprites.Count );
		var all = sprites.SelectMany( sprite => new[] { sprite.X, sprite.Y, sprite.Z } ).ToArray();
		Assert.IsTrue( all.All( value => value is >= -4 and <= 4 ), "+-(n - 1), never +-n" );
		Assert.IsTrue( all.Contains( 4 ) && all.Contains( -4 ), "both ends are reached" );
		Assert.IsTrue( all.Count( value => value == 0 ) > all.Count( value => value == 1 ), "zero is twice as likely as any other value" );
		Assert.IsTrue( sprites.All( sprite => sprite.Spin is >= 0 and <= 0x7FFF ) && sprites.Any( sprite => sprite.Spin > 0x4000 ), "the random rotation is the unsigned 15-bit form" );
	}

	// ---- slots and handles ----

	[TestMethod]
	public void EmitterSlotsAreLifoAndTheGenerationIsGlobal()
	{
		var system = New( Pool( (1, Template( Life( 0 ), Cap( 10 ), ParticleLife( 5 ) )), (2, Template( Life( 0 ), Immortal(), Cap( 10 ), ParticleLife( 5 ) )) ) );
		var first = system.Create( 1, Origin );
		var second = system.Create( 2, Origin );
		var third = system.Create( 2, Origin );
		Assert.AreEqual( (0, 1), (first & 0xFFFF, first >>> 16), "slot 0, generation 1" );
		Assert.AreEqual( (1, 2), (second & 0xFFFF, second >>> 16), "slot 1, generation 2: the counter is global, not per slot" );
		Assert.AreEqual( (2, 3), (third & 0xFFFF, third >>> 16) );
		Run( system, 3 ); // slot 0's emitter has life 0 and no particles: it dies and frees its slot
		Assert.IsFalse( system.IsAlive( first ) );
		Assert.IsTrue( system.Kill( second ) );
		Run( system, 2 ); // slot 1 frees after slot 0: the free list is LIFO, so slot 1 is the next one handed out, then slot 0
		var reuse = system.Create( 2, Origin );
		Assert.AreEqual( (1, 4), (reuse & 0xFFFF, reuse >>> 16), "the most recently freed slot comes back first, with the next generation" );
		var next = system.Create( 2, Origin );
		Assert.AreEqual( (0, 5), (next & 0xFFFF, next >>> 16) );
		Assert.IsFalse( system.IsAlive( second ), "a freed slot's generation is cleared: the old handle is dead even though the slot is reused" );
		Assert.IsTrue( system.IsAlive( third ) );
	}

	[TestMethod]
	public void NewestEmitterTicksAndDrawsLast()
	{
		// the active list is newest first for the tick; the draw walks slots 0..119 in ascending order
		var system = New( Pool( (1, Template( Life( 5 ), Burst( 1 ), Cap( 10 ), ParticleLife( 100 ), (0x38, 4, 1) )) ) );
		system.Create( 1, (1024, 0, 0) );
		system.Create( 1, (2048, 0, 0) );
		var sprites = Sprites( system );
		CollectionAssert.AreEqual( new[] { 0, 1 }, sprites.Select( sprite => sprite.Emitter ).ToArray(), "slot order" );
		CollectionAssert.AreEqual( new[] { 64, 128 }, sprites.Select( sprite => sprite.X ).ToArray() );
	}

	// ---- rate, burst and cap ----

	[TestMethod]
	public void RateQuarterComesFromTheRemainingLifeAndImmortalEmittersStayOnTheFirstRate()
	{
		// life 8: (life << 2) / 8 = 3 for life 7, 6 (the first rate), 2 for 5 and 4 (+0x69), 1 for 3 and 2 (+0x6A), 0 for 1 and 0 (+0x6B)
		var system = New( Pool( (1, Template( Life( 8 ), Cap( 1000 ), Rates( 1, 2, 4, 8 ), ParticleLife( 1000 ) )) ) );
		system.Create( 1, Origin );
		system.Tick();
		var counts = new List<int>();
		var last = 0;
		for ( var tick = 0; tick < 8; tick++ )
		{
			system.Tick();
			counts.Add( system.LiveParticles - last );
			last = system.LiveParticles;
		}
		// life after the decrement: 7, 6, 5, 4, 3, 2, 1, 0
		CollectionAssert.AreEqual( new[] { 1, 1, 2, 2, 4, 4, 8, 8 }, counts, "+0x68, +0x69, +0x6A, +0x6B in order of the remaining life" );

		var immortal = New( Pool( (1, Template( Life( 8 ), Immortal(), Cap( 1000 ), Rates( 3, 2, 4, 8 ), ParticleLife( 1000 ) )) ) );
		immortal.Create( 1, Origin );
		Run( immortal, 21 );
		Assert.AreEqual( 20 * 3, immortal.LiveParticles, "the life never falls, so +0x68 stays" );
	}

	[TestMethod]
	public void BurstIgnoresTheCapAndTheCapIsCheckedOncePerTick()
	{
		var system = New( Pool( (1, Template( Life( 10 ), Burst( 10 ), Cap( 3 ), Rates( 4, 4, 4, 4 ), ParticleLife( 1000 ) )) ) );
		system.Create( 1, Origin );
		Assert.AreEqual( 10, system.LiveParticles, "the burst is not capped" );
		Run( system, 10 );
		Assert.AreEqual( 10, system.LiveParticles, "a live count above the cap emits nothing" );
		var overshoot = New( Pool( (1, Template( Life( 10 ), Burst( 2 ), Cap( 5 ), Rates( 4, 4, 4, 4 ), ParticleLife( 1000 ) )) ) );
		overshoot.Create( 1, Origin );
		Run( overshoot, 2 );
		Assert.AreEqual( 2 + 4, overshoot.LiveParticles, "2 < 5: the whole rate of 4 is spawned" );
		Run( overshoot, 1 );
		Assert.AreEqual( 2 + 4, overshoot.LiveParticles, "6 is not below the cap of 5: nothing more is emitted" );
	}

	// ---- spawn shapes ----

	[TestMethod]
	public void ShapeZeroIsAHorizontalDiscAndShapeOneIsABox()
	{
		var extents = new[] { (0x44, 4, 100), (0x48, 4, 40), (0x4C, 4, 100) };
		ParticleSystem Build( params (int, int, int)[] extra ) =>
			New( Pool( (1, Template( new[] { Life( 1 ), Burst( 400 ), Cap( 1000 ), ParticleLife( 1000 ) }.Concat( extents ).Concat( extra ).ToArray() )) ) );
		var disc = Build();
		disc.Create( 1, Origin );
		var points = Sprites( disc );
		Assert.AreEqual( 400, points.Count );
		Assert.IsTrue( points.All( point => (long)point.X * point.X + (long)point.Z * point.Z <= 100L * 100 ), "the disc stays inside the radius" );
		Assert.IsTrue( points.All( point => Math.Abs( point.Y ) <= 39 ), "y is a signed draw of +-(extent - 1)" );
		Assert.IsTrue( points.Any( point => point.X > 30 ) && points.Any( point => point.X < -30 ) && points.Any( point => point.Z > 30 ) && points.Any( point => point.Z < -30 ) );

		var box = Build( (0xC2, 1, 1) );
		box.Create( 1, Origin );
		var corners = Sprites( box );
		Assert.IsTrue( corners.Any( point => (long)point.X * point.X + (long)point.Z * point.Z > 99L * 99 ), "a box reaches its corners" );
		Assert.IsTrue( corners.All( point => Math.Abs( point.X ) <= 99 && Math.Abs( point.Z ) <= 99 ) );

		var unsignedY = Build( (0xC2, 1, 1), (0xA0, 2, 1) );
		unsignedY.Create( 1, Origin );
		Assert.IsTrue( Sprites( unsignedY ).All( point => point.Y is >= 0 and <= 39 ), "word +0xA0 draws y unsigned" );
	}

	[TestMethod]
	public void AnOlderSaveWithoutAVolumeExtentKeepsTheTemplatesAndABadOneIsRefused()
	{
		var pool = Pool( (1, Template( Life( 1000 ), Burst( 10 ), Cap( 3000 ), ParticleLife( 1000 ), (0x44, 4, 100), (0x4C, 4, 100), (0xC2, 1, 1) )) );
		var system = New( pool );
		var handle = system.Create( 1, Origin );
		system.SetVolumeExtent( handle, 16 * 7, 0, 16 * 7 );
		var state = system.Capture()!;
		Assert.IsNotNull( state.Emitters.Single().VolumeExtent, "a set extent is saved" );
		var same = New( pool );
		same.Restore( state );
		CollectionAssert.AreEqual( state.Emitters.Single().VolumeExtent!.ToArray(), same.Capture()!.Emitters.Single().VolumeExtent!.ToArray(), "and restored" );
		var older = state with { Emitters = state.Emitters.Select( emitter => emitter with { VolumeExtent = null } ).ToArray() };
		var restored = New( pool );
		restored.Restore( older );
		Assert.IsNull( restored.Capture()!.Emitters.Single().VolumeExtent, "no field: the template's extent" );
		Assert.IsNotNull( ParticleSystem.ValidateState( state with { Emitters = state.Emitters.Select( emitter => emitter with { VolumeExtent = new[] { 1, 2 } } ).ToArray() } ), "a wrong length" );
		Assert.IsNotNull( ParticleSystem.ValidateState( state with { Emitters = state.Emitters.Select( emitter => emitter with { VolumeExtent = new[] { 1, 2, int.MaxValue } } ).ToArray() } ), "out of range" );
		Assert.IsNull( ParticleSystem.ValidateState( state ) );
	}

	[TestMethod]
	public void RingUsesTheExtentsAsTheRadiusAndTheVolumeRotatesAboutY()
	{
		var ring = New( Pool( (1, Template( Life( 1 ), Burst( 300 ), Cap( 1000 ), ParticleLife( 1000 ), (0x44, 4, 121), (0x4C, 4, 121), (0xA8, 2, 1) )) ) );
		ring.Create( 1, Origin );
		var points = Sprites( ring );
		Assert.IsTrue( points.All( point => Math.Sqrt( (double)point.X * point.X + (double)point.Z * point.Z ) is >= 118 and <= 122 ), "every particle sits on the ring" );
		Assert.IsTrue( points.Any( point => point.X > 100 ) && points.Any( point => point.X < -100 ) && points.Any( point => point.Z > 100 ) && points.Any( point => point.Z < -100 ) );

		// a box of x extent 100 rotated by 1024 / 4096 of a turn about Y: x becomes 0 and the extent moves onto z
		var rotated = New( Pool( (1, Template( Life( 1 ), Burst( 200 ), Cap( 1000 ), ParticleLife( 1000 ), (0x44, 4, 100), (0xC2, 1, 1), (0xAC, 4, 1024) )) ) );
		rotated.Create( 1, Origin );
		var turned = Sprites( rotated );
		Assert.IsTrue( turned.All( point => point.X == 0 ), "cos(quarter turn) is 0 in the table" );
		Assert.IsTrue( turned.All( point => Math.Abs( point.Z ) <= 99 ) && turned.Any( point => Math.Abs( point.Z ) > 50 ) );
	}

	[TestMethod]
	public void SpiralOffsetFollowsTheEmitterAngle()
	{
		// radius 100, 512 / 4096 of a turn per tick: the angle advances before the tick's spawn; table index = angle >> 3
		var system = New( Pool( (1, Template( Life( 100 ), Cap( 1000 ), Rates( 1, 1, 1, 1 ), ParticleLife( 1000 ), (0xC4, 2, 100), (0xC8, 4, 512) )) ) );
		system.Create( 1, Origin );
		Run( system, 3 );
		var sprites = Sprites( system );
		// newest first: tick 2 (angle 1024: sin 255, cos 0) then tick 1 (angle 512: sin 181, cos 181)
		Assert.AreEqual( (99, 0, 0), (sprites[0].X, sprites[0].Y, sprites[0].Z) );
		Assert.AreEqual( (70, 0, 70), (sprites[1].X, sprites[1].Y, sprites[1].Z) );
	}

	// ---- colour, frame, size, spin ----

	private static (int, int, int)[] Palette()
	{
		var fields = new (int, int, int)[16];
		for ( var index = 0; index < 16; index++ )
			fields[index] = (0xD8 + index * 4, 4, unchecked((int)(0xFF000000u | (uint)index)));
		return fields;
	}

	[TestMethod]
	public void OddRandomColourFlickersEveryTickAndEvenKeepsTheSpawnColour()
	{
		ParticleSystem Build( int mode ) =>
			New( Pool( (1, Template( new[] { Life( 1 ), Burst( 30 ), Cap( 1000 ), ParticleLife( 1000 ), (0x58, 4, mode) }.Concat( Palette() ).ToArray() )) ) );
		var flicker = Build( 1 );
		flicker.Create( 1, Origin );
		Run( flicker, 2 );
		var before = Sprites( flicker ).Select( sprite => sprite.Colour ).ToArray();
		flicker.Tick();
		var after = Sprites( flicker ).Select( sprite => sprite.Colour ).ToArray();
		Assert.IsTrue( after.All( colour => (colour & 0xFFFFFF00u) == 0xFF000000u && (colour & 0xFF) < 16 ) );
		Assert.IsTrue( before.Zip( after ).Any( pair => pair.First != pair.Second ), "the colour changes between ticks" );

		var steady = Build( 2 );
		steady.Create( 1, Origin );
		var born = Sprites( steady ).Select( sprite => sprite.Colour ).ToArray();
		Assert.IsTrue( born.Distinct().Count() > 4, "spawn colour is palette[u & 15]" );
		Run( steady, 5 );
		CollectionAssert.AreEqual( born, Sprites( steady ).Select( sprite => sprite.Colour ).ToArray(), "an even non-zero mode keeps the spawn colour" );
	}

	[TestMethod]
	public void SpinStepIsBEOrBEPlusRandomUpToC6AndAppliesOnTheSpawnTick()
	{
		var fixedSpin = New( Pool( (1, Template( Life( 1 ), Burst( 1 ), Cap( 10 ), ParticleLife( 1000 ), (0xBE, 2, 100), (0xC6, 2, 100) )) ) );
		fixedSpin.Create( 1, Origin );
		Run( fixedSpin, 2 );
		Assert.AreEqual( 100, Sprites( fixedSpin )[0].Spin, "the spawn tick already spins" );
		Run( fixedSpin, 3 );
		Assert.AreEqual( 400, Sprites( fixedSpin )[0].Spin );

		var random = New( Pool( (1, Template( Life( 1 ), Burst( 1 ), Cap( 10 ), ParticleLife( 1000 ), (0xBE, 2, 10), (0xC6, 2, 30) )) ) );
		random.Create( 1, Origin );
		Run( random, 1 );
		var previous = 0;
		var steps = new HashSet<int>();
		for ( var tick = 0; tick < 60; tick++ )
		{
			random.Tick();
			var spin = Sprites( random )[0].Spin;
			steps.Add( spin - previous );
			previous = spin;
		}
		Assert.IsTrue( steps.All( step => step is >= 10 and <= 29 ), "BE + u % (C6 - BE)" );
		Assert.IsTrue( steps.Count > 5 );
	}

	[TestMethod]
	public void FrameAndSizeFollowTheLifeFractionWithTruncation()
	{
		// life 100 exactly (no jitter: life >> 2 would draw, so use a life below 4)
		var system = New( Pool( (1, Template( Life( 1 ), Burst( 1 ), Cap( 10 ), ParticleLife( 3 ), (0x96, 2, 16), (0x94, 2, 11), (0x74, 4, 1000), (0xA6, 2, 400) )) ) );
		system.Create( 1, Origin );
		Run( system, 2 );
		// life 3 -> after the spawn tick life 2: frame (16 - 1) - 15 * 2 / 3 = 5, size (1000 - 400) * 2 / 3 + 400 = 800
		var sprite = Sprites( system )[0];
		Assert.AreEqual( (5, 800), (sprite.Frame, sprite.Size) );
		system.Tick();
		sprite = Sprites( system )[0];
		Assert.AreEqual( (10, 600 * 1 / 3 + 400), (sprite.Frame, sprite.Size), "life 1: frame 15 - 15 / 3, size 600 / 3 + 400" );
	}

	// ---- dead emitters and child rules ----

	[TestMethod]
	public void DeadEmitterStopsEmittingStartsItsDeathEffectOnceAndWaitsForItsParticles()
	{
		var pool = Pool( (1, Template( Life( 2 ), Cap( 100 ), Rates( 2, 2, 2, 2 ), ParticleLife( 1000 ), (0x10, 2, 5), (0x2C, 4, 100) )),
			(5, Template( Life( 1 ), Burst( 1 ), Cap( 10 ), ParticleLife( 1000 ) )) );
		var system = New( pool );
		system.Create( 1, Origin );
		Run( system, 12 );
		Assert.AreEqual( 4 + 1, system.LiveParticles, "life 2 emits on ticks 1 and 2 only; the death effect (5) adds its burst once" );
		Assert.AreEqual( 2, system.LiveEmitters, "the dead emitter waits for its particles, the death effect lives on" );
		Assert.AreEqual( 1, system.LiveEffects().Count( id => id == 5 ), "started once, not every tick" );
		Run( system, 5 );
		Assert.AreEqual( 5, system.LiveParticles );
	}

	[TestMethod]
	public void ParticlesOfADeadEmitterDieWhenTheTemplateAsksFor()
	{
		var system = New( Pool( (1, Template( Life( 2 ), Cap( 100 ), Rates( 2, 2, 2, 2 ), ParticleLife( 1000 ), (0xAA, 1, 1) )) ) );
		system.Create( 1, Origin );
		Run( system, 4 );
		Assert.AreEqual( 4, system.LiveParticles );
		Run( system, 3 );
		Assert.AreEqual( 0, system.LiveParticles, "+0xAA sets the life to -1 once the emitter's life is below zero" );
		Run( system, 2 );
		Assert.AreEqual( 0, system.LiveEmitters );
	}

	[TestMethod]
	public void ChildIsNotCreatedWhenItIsTheTemplatesOwnId()
	{
		var system = New( Pool( (1, Template( Life( 5 ), Cap( 10 ), ParticleLife( 5 ), (0xB4, 4, 1) )) ) );
		system.Create( 1, Origin );
		Assert.AreEqual( 1, system.LiveEmitters );
	}

	[TestMethod]
	public void FollowingChildStartsAtTheParentPlusItsVelocityVectorAndNeverMovesItself()
	{
		// the child's own +0x2C vector (here 64 on x) times 16 is added to the creation position; +0xAB makes the child skip its own motion
		var parent = Template( Life( 50 ), Cap( 10 ), ParticleLife( 5 ), (0xB4, 4, 2), (0xB8, 2, 1) );
		var child = Template( Life( 50 ), Burst( 1 ), Cap( 100 ), Rates( 1, 1, 1, 1 ), ParticleLife( 1000 ), (0x2C, 4, 64) );
		var system = New( Pool( (1, parent), (2, child) ) );
		system.Create( 1, Origin );
		Assert.AreEqual( 64, Sprites( system ).Single().X, "(0 + 64 * 16) >> 4" );
		Run( system, 4 );
		var xs = Sprites( system ).Select( sprite => sprite.X ).Distinct().OrderBy( x => x ).ToArray();
		CollectionAssert.AreEqual( new[] { 0, 64 }, xs, "later particles come from the parent's position: the child neither moved nor kept the offset" );
	}

	[TestMethod]
	public void DeadParentSetsItsChildsLifeToMinusOneWhichOnlyFiresAnImmortalChildsDeathEffect()
	{
		var pool = Pool( (1, Template( Life( 2 ), Cap( 10 ), ParticleLife( 5 ), (0xB4, 4, 2), (0xBC, 2, 1) )),
			(2, Template( Life( 0 ), Immortal(), Cap( 10 ), ParticleLife( 5 ), (0x10, 2, 3) )),
			(3, Template( Life( 100 ), Burst( 1 ), Cap( 10 ), ParticleLife( 1000 ) )),
			(4, Template( Life( 100 ), Cap( 10 ), ParticleLife( 5 ), (0x10, 2, 3) )),
			(5, Template( Life( 2 ), Cap( 10 ), ParticleLife( 5 ), (0xB4, 4, 4), (0xBC, 2, 1) )) );
		var immortal = New( pool );
		immortal.Create( 1, Origin );
		var seen = new HashSet<int>();
		for ( var tick = 0; tick < 12; tick++ )
		{
			immortal.Tick();
			foreach ( var id in immortal.LiveEffects() )
				seen.Add( id );
		}
		CollectionAssert.IsSubsetOf( new[] { 2, 3 }, seen.ToArray(), "the immortal child's life -1 starts its death effect" );
		Assert.IsFalse( immortal.LiveEffects().Contains( 2 ), "the child is gone" );

		// a child that counts its life down is decremented from -1 to -2 before it can see -1
		var mortal = New( pool );
		mortal.Create( 5, Origin );
		seen.Clear();
		for ( var tick = 0; tick < 12; tick++ )
		{
			mortal.Tick();
			foreach ( var id in mortal.LiveEffects() )
				seen.Add( id );
		}
		CollectionAssert.DoesNotContain( seen.ToArray(), 3, "tick order is newest first, so the child ticked before the parent set -1" );
	}

	[TestMethod]
	public void MoveRelocatesAnEmitterAndItsFollowingChildForTheBumpSmoke()
	{
		var parent = Template( Life( 0 ), Immortal(), Cap( 10 ), Rates( 1, 1, 1, 1 ), ParticleLife( 3 ), (0xB4, 4, 2), (0xB8, 2, 1) );
		var child = Template( Life( 0 ), Immortal(), Cap( 10 ), Rates( 1, 1, 1, 1 ), ParticleLife( 3 ) );
		var system = New( Pool( (1, parent), (2, child) ) );
		var handle = system.Create( 1, Origin );
		Assert.IsTrue( system.Move( handle, (10 * 1024, 0, 0) ) );
		Run( system, 3 );
		Assert.IsTrue( Sprites( system ).All( sprite => sprite.X == 640 ), "both emitters spawn at the moved position" );
		Assert.IsTrue( system.Kill( handle ) );
		Run( system, 10 );
		Assert.IsFalse( system.Move( handle, Origin ), "a dead handle is not moved" );
	}

	// ---- effectors ----

	[TestMethod]
	public void EffectorAttractsParticlesByTheNativeFormula()
	{
		// strength 100 toward x = 400 (1/64): d = (400 - 0) / 4 = 100, q = (100 * 100 >> 8) + 10 = 49, v += 100 * 100 / 49 = 204 per tick
		var effectors = new[] { Effector( (0x4, 4, -1), (0x4C, 4, 100), (0x40, 4, 100) ) };
		var system = New( Pool( (1, Template( Life( 100 ), Burst( 1 ), Cap( 10 ), ParticleLife( 1000 ) )) ), effectors: effectors );
		system.Create( 1, Origin );
		Assert.IsTrue( system.CreateEffector( 0, (6400, 0, 0) ) >= 0 );
		Assert.AreEqual( 1, system.LiveEffectors );
		Run( system, 3 );
		Assert.AreEqual( (408, 0, 0), (Sprites( system )[0].X, Sprites( system )[0].Y, Sprites( system )[0].Z), "204 on the spawn tick (no move), 408 and a move on the next" );
		Assert.AreEqual( -1, system.CreateEffector( 20, Origin ), "effector ids are 0..19" );
		Assert.AreEqual( -1, system.CreateEffector( 5, Origin ), "an id the file does not hold" );
	}

	[TestMethod]
	public void EffectorModeTwoKillsParticlesInsideItsRadiusAndGroupsFilter()
	{
		var effectors = new[]
		{
			Effector( (0x4, 4, -1), (0x4C, 4, 100), (0x40, 4, 1), (0x2C, 4, 100), (0x30, 4, 2) ),
			Effector( (0x4, 4, 1), (0x4C, 4, 100), (0x40, 4, 100) ),
			Effector( (0x4, 4, -1), (0x4C, 4, 100), (0x40, 4, 100) )
		};
		// target 100 (1/64): d = 25, q = 12 < radius^2 >> 8 = 39: killed
		var killing = New( Pool( (1, Template( Life( 100 ), Burst( 3 ), Cap( 10 ), ParticleLife( 1000 ) )) ), effectors: effectors );
		killing.Create( 1, Origin );
		killing.CreateEffector( 0, (1600, 0, 0) );
		Run( killing, 2 );
		Assert.AreEqual( 0, killing.LiveParticles, "mode 2 sets the life to 0 and the particle update takes it below zero" );

		// group 1 reaches only emitters whose template group (byte +4) is 1
		var grouped = New( Pool( (1, Template( Life( 100 ), Burst( 1 ), Cap( 10 ), ParticleLife( 1000 ), (0x4, 1, 1) )), (2, Template( Life( 100 ), Burst( 1 ), Cap( 10 ), ParticleLife( 1000 ) )) ), effectors: effectors );
		grouped.Create( 1, Origin );
		grouped.Create( 2, Origin );
		grouped.CreateEffector( 1, (6400, 0, 0) );
		Run( grouped, 3 );
		var xs = Sprites( grouped ).OrderBy( sprite => sprite.Emitter ).Select( sprite => sprite.X ).ToArray();
		Assert.AreEqual( 408, xs[0], "group 1" );
		Assert.AreEqual( 0, xs[1], "group 0 is not targeted" );

		// every-group effectors skip templates whose int +0x9C is set
		var skipped = New( Pool( (1, Template( Life( 100 ), Burst( 1 ), Cap( 10 ), ParticleLife( 1000 ), (0x9C, 4, 1) )), (2, Template( Life( 100 ), Burst( 1 ), Cap( 10 ), ParticleLife( 1000 ) )) ), effectors: effectors );
		skipped.Create( 1, Origin );
		skipped.Create( 2, Origin );
		skipped.CreateEffector( 2, (6400, 0, 0) );
		Run( skipped, 3 );
		var bySlot = Sprites( skipped ).OrderBy( sprite => sprite.Emitter ).Select( sprite => sprite.X ).ToArray();
		Assert.AreEqual( (0, 408), (bySlot[0], bySlot[1]) );
	}

	[TestMethod]
	public void EffectorJitterDrawsOncePerTickAndItsLifeEndsInADeathEffect()
	{
		var effectors = new[] { Effector( (0x4, 4, -1), (0x4C, 4, 2), (0x34, 4, 5), (0x46, 2, 7) ) };
		var system = New( Pool( (7, Template( Life( 1 ), Burst( 1 ), Cap( 10 ), ParticleLife( 1000 ) )) ), effectors: effectors );
		system.CreateEffector( 0, (1024, 2048, 3072) );
		Run( system, 2 );
		Assert.AreEqual( Lcg( 1, 1 ), system.RandomState, "one draw per tick for the one jittered axis, whatever the particle count" );
		Run( system, 3 );
		Assert.AreEqual( 0, system.LiveEffectors, "life 2: the effector ends and is freed" );
		Assert.AreEqual( 1, system.LiveEffects().Count( id => id == 7 ), "its death effect starts where it ended" );
	}

	[TestMethod]
	public void EffectorChildOfAnEmitterFollowsItAndEndsWithIt()
	{
		var effectors = new[] { Effector( (0x4, 4, -1), (0x4C, 4, 1000), (0x40, 4, 1), (0x18, 4, 10) ) };
		var parent = Template( Life( 4 ), Cap( 10 ), ParticleLife( 5 ), (0xB4, 4, 0), (0xBA, 1, 1), (0xB8, 2, 1), (0xBC, 2, 1), (0x2C, 4, 16) );
		var system = New( Pool( (1, parent) ), effectors: effectors );
		system.Create( 1, Origin );
		Assert.AreEqual( 1, system.LiveEffectors, "the +0xBA child is an effector" );
		Run( system, 4 );
		Assert.AreEqual( 1, system.LiveEffectors );
		Run( system, 4 );
		Assert.AreEqual( 0, system.LiveEffectors, "the dead parent's +0xBC sets the effector's life to -1" );
	}

	[TestMethod]
	public void TickAllocatesNothing()
	{
		// emitters, effectors, children, death effects and a pool that fills and drains: after a warm-up a tick allocates no bytes
		var effectors = new[] { Effector( (0x4, 4, -1), (0x4C, 4, 100000), (0x40, 4, 3), (0x34, 4, 20), (0x2C, 4, 50), (0x30, 4, 2) ) };
		var pool = Pool( (1, Template( Life( 400 ), Burst( 20 ), Cap( 200 ), Rates( 6, 4, 2, -3 ), ParticleLife( 60 ), (0x44, 4, 80), (0x4C, 4, 80), (0x5C, 4, 20), (0x58, 4, 1), (0x6D, 1, 1), (0x84, 4, 3),
			(0x98, 4, 2), (0xB4, 4, 3), (0xB8, 2, 1), (0xBC, 2, 1), (0xC4, 2, 30), (0xC8, 4, 100), (0x10, 2, 2) )),
			(2, Template( Life( 5 ), Burst( 3 ), Cap( 20 ), ParticleLife( 8 ) )),
			(3, Template( Life( 400 ), Cap( 100 ), Rates( 2, 2, 2, 2 ), ParticleLife( 30 ), (0x28, 4, 1), (0x2C, 4, 3) )) );
		var system = New( pool, effectors: effectors );
		system.Create( 1, (1024, 1024, 1024) );
		system.CreateEffector( 0, (1024, 1024, 1024) );
		Run( system, 50 );
		Assert.IsTrue( system.LiveParticles > 20, "the pool is busy while it is measured" );
		// tiered compilation (call counting, tier-up, OSR) can allocate on this thread for a while in a busy full run, so measure
		// windows of 100 ticks until one is clean; a real per-tick allocation never yields a clean window
		long allocated = -1;
		for ( var window = 0; window < 10 && allocated != 0; window++ )
		{
			if ( system.LiveParticles <= 20 )
				system.Create( 1, (1024, 1024, 1024) ); // keep the pool busy across windows
			var before = GC.GetAllocatedBytesForCurrentThread();
			Run( system, 100 );
			allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		}
		Assert.AreEqual( 0, allocated, "a tick allocates nothing" );
		Assert.IsTrue( system.LiveParticles > 20, "the pool was still busy" );
	}

	// ---- the corpus ----

	private static ParticleTemplateFile? ShippedFile()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			return null;
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		var particle = Directory.EnumerateDirectories( dataPath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "particle", StringComparison.OrdinalIgnoreCase ) );
		var plb = particle == null ? null : Directory.EnumerateFiles( particle ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "Tp2.plb", StringComparison.OrdinalIgnoreCase ) );
		if ( plb == null )
			return null;
		using var stream = File.OpenRead( plb );
		return new ParticleTemplateFile( stream );
	}

	[TestMethod]
	public void ShippedTemplatesUseTheFieldsTheTraceCoversAndRunToTheirEnd()
	{
		var file = ShippedFile();
		if ( file == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the shipped Tp2.plb." );
		var templates = file!.Templates;
		var ids = Enumerable.Range( 0, templates.Count );
		// features the port does not cover are unused by the data
		Assert.AreEqual( 0, ids.Count( id => templates[id].RingMode != 0 ), "no ring emitter (+0xE)" );
		Assert.AreEqual( 0, ids.Count( id => templates[id].Hold != 0 ), "no held emitter (+2)" );
		Assert.AreEqual( 0, ids.Count( id => templates[id].GroundBounce != 0 ), "no ground bounce (+0x76)" );
		// the rules the port covers are used
		CollectionAssert.AreEqual( new[] { 75, 76, 77, 78, 79, 80, 81, 82, 89, 99 }, ids.Where( id => templates[id].VolumeShape != 0 ).ToArray(), "box volumes" );
		CollectionAssert.AreEqual( new[] { 90 }, ids.Where( id => templates[id].VolumeRing ).ToArray(), "the one ring" );
		CollectionAssert.AreEqual( new[] { 51, 54, 87 }, ids.Where( id => templates[id].SpiralRadius != 0 ).ToArray(), "spirals" );
		Assert.AreEqual( 92, ids.Count( id => templates[id].EmitterVelocityJitter == -1 ) );
		Assert.AreEqual( 60, ids.Count( id => templates[id].Spin != templates[id].SpinMaximum ), "random spin steps" );
		CollectionAssert.AreEqual( new[] { 84, 86, 90, 91, 98 }, ids.Where( id => templates[id].ChildIsEffector ).ToArray(), "effector children" );
		CollectionAssert.AreEqual( new[] { 87 }, ids.Where( id => templates[id].DeathEffectIsEffector ).ToArray() );
		Assert.AreEqual( 20, file.EffectorTemplates.Count );
		CollectionAssert.AreEqual( new[] { 1, 1, 2, 3, 4, 4 }, file.EffectorTemplates.Take( 6 ).Select( effector => effector.Group ).ToArray() );

		// every template runs, deterministically, and finite effects end
		foreach ( var density in new[] { ParticleSystem.NativeDefaultDensity, 1024 } )
			foreach ( var id in ids )
			{
				var first = RunShipped( file, id, density );
				var second = RunShipped( file, id, density );
				Assert.AreEqual( first, second, $"effect {id} at density {density} replays" );
			}
		var firework = RunShippedFully( file, 3 );
		Assert.AreEqual( (0, 0), firework, "a finished firework frees every emitter and particle" );
	}

	private static (uint State, int Particles, int Emitters, int Effectors) RunShipped( ParticleTemplateFile file, int id, int density )
	{
		var system = new ParticleSystem( file.Templates, density, effectors: file.EffectorTemplates );
		system.Create( id, (1024 * 100, 1024 * 10, 1024 * 100) );
		var peak = 0;
		for ( var tick = 0; tick < 300; tick++ )
		{
			system.Tick();
			peak = Math.Max( peak, system.LiveParticles );
			Assert.IsTrue( system.LiveParticles >= 0 && system.LiveParticles <= system.Capacity && system.LiveEmitters is >= 0 and <= ParticleSystem.EmitterSlots );
		}
		return (system.RandomState, system.LiveParticles, system.LiveEmitters, system.LiveEffectors);
	}

	private static (int Particles, int Emitters) RunShippedFully( ParticleTemplateFile file, int id )
	{
		var system = new ParticleSystem( file.Templates, effectors: file.EffectorTemplates );
		system.Create( id, Origin );
		for ( var tick = 0; tick < 600; tick++ )
			system.Tick();
		return (system.LiveParticles, system.LiveEmitters);
	}

	[TestMethod]
	public void ShippedEffectorChainsStartTheirEffectors()
	{
		var file = ShippedFile();
		if ( file == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the shipped Tp2.plb." );
		// 84 Twinkle2 and 86 Twinkle2Sm have effector children, 87 Key an effector death effect, 90 and 98 effector children
		foreach ( var id in new[] { 84, 86, 90, 98, 87 } )
		{
			var system = new ParticleSystem( file!.Templates, 1024, effectors: file.EffectorTemplates );
			system.Create( id, Origin );
			var seen = system.LiveEffectors > 0;
			for ( var tick = 0; tick < 700 && !seen; tick++ )
			{
				system.Tick();
				seen |= system.LiveEffectors > 0;
			}
			Assert.IsTrue( seen, $"effect {id} reaches an effector" );
		}
	}

	// ---- saves ----

	[TestMethod]
	public void SaveHoldsEmittersEffectorsAndTheGeneratorButNoParticles()
	{
		var pool = Pool( (1, Template( Life( 0 ), Immortal(), Burst( 3 ), Cap( 50 ), Rates( 2, 2, 2, 2 ), ParticleLife( 1000 ), (0x12, 2, 5), (0x2C, 4, 8) )),
			(2, Template( Life( 200 ), Cap( 50 ), Rates( 1, 1, 1, 1 ), ParticleLife( 1000 ), (0x5, 1, 1) )) );
		var effectors = new[] { Effector( (0x4, 4, -1), (0x4C, 4, 500), (0x40, 4, 5) ), Effector( (0x4, 4, -1), (0x4C, 4, 500), (0x45, 1, 1) ) };
		var system = New( pool, effectors: effectors );
		Assert.AreEqual( 0, system.Capture().Emitters.Count, "an empty initialized pool still saves its settings and generator" );
		var smoke = system.Create( 1, (1024, 2048, 3072) );
		var screen = system.Create( 2, Origin );
		system.CreateEffector( 0, (1024, 0, 0) );
		system.CreateEffector( 1, (2048, 0, 0) );
		Run( system, 20 );
		Assert.IsTrue( system.LiveParticles > 20 );

		var saved = system.Capture()!;
		var bytes = ParkResumeJson.Serialize( saved );
		var loaded = ParkResumeJson.Deserialize<ParticleSystemState>( bytes );
		CollectionAssert.AreEqual( bytes, ParkResumeJson.Serialize( loaded ), "the state survives the envelope's JSON codec" );
		Assert.IsNull( ParticleSystem.ValidateState( loaded ) );

		var restored = New( pool, effectors: effectors );
		restored.Restore( loaded );
		Assert.AreEqual( 0, restored.LiveParticles, "no particles are saved" );
		Assert.AreEqual( system.RandomState, restored.RandomState );
		Assert.IsTrue( restored.IsAlive( smoke ), "the immortal emitter survives with its handle" );
		Assert.AreEqual( 2, restored.LiveEmitters );
		Assert.AreEqual( 2, restored.LiveEffectors );
		Run( restored, 5 );
		Assert.IsTrue( restored.LiveParticles > 0, "the restored emitter emits again" );
		Assert.AreEqual( 1, restored.LiveEffectors, "the effector flagged +0x45 is killed on load" );

		// the free lists and counters continue: the next emitter takes the slot the original would have
		var original = system.Create( 1, Origin );
		var again = New( pool, effectors: effectors );
		again.Restore( loaded );
		Assert.AreEqual( original, again.Create( 1, Origin ), "same slot, same generation" );

		// screen-space emitters are killed on load: life -2 and hidden
		var screenPool = Pool( (1, Template( Life( 0 ), Immortal(), Burst( 3 ), Cap( 50 ), ParticleLife( 1000 ), (0x5, 1, 1) )) );
		var screenSystem = New( screenPool );
		screenSystem.Create( 1, Origin );
		var screenRestored = New( screenPool );
		screenRestored.Restore( screenSystem.Capture()! );
		Run( screenRestored, 3 );
		Assert.AreEqual( 0, screenRestored.LiveEmitters, "killed on load, then freed (it holds no particles)" );
		Assert.AreEqual( 0, Sprites( screenRestored ).Count );
		Assert.IsFalse( screenRestored.IsAlive( screen ) );
	}

	[TestMethod]
	public void SavedStateIsValidatedBeforeItIsRestored()
	{
		var pool = Pool( (1, Template( Life( 0 ), Immortal(), Cap( 5 ), ParticleLife( 10 ) )) );
		var system = New( pool );
		system.Create( 1, Origin );
		var good = system.Capture()!;
		Assert.IsNull( ParticleSystem.ValidateState( good ) );
		Assert.IsNotNull( ParticleSystem.ValidateState( good with { FreeEmitters = good.FreeEmitters.Skip( 1 ).ToArray() } ), "a slot is missing" );
		Assert.IsNotNull( ParticleSystem.ValidateState( good with { FreeEmitters = good.FreeEmitters.Select( slot => slot == 5 ? 6 : slot ).ToArray() } ), "a slot twice" );
		Assert.IsNotNull( ParticleSystem.ValidateState( good with { Emitters = new[] { good.Emitters[0] with { EffectId = 200 } } } ), "an effect outside the table" );
		Assert.IsNotNull( ParticleSystem.ValidateState( good with { GenerationCounter = 70000 } ) );
		var emitter = good.Emitters[0];
		foreach ( var bad in new[] { emitter with { Life = 0x20000000, InitialLife = -1 }, emitter with { InitialLife = int.MinValue }, emitter with { Rates = new[] { 1, 2, 3, 300 } },
			emitter with { Control = 70000 }, emitter with { Cap = int.MaxValue }, emitter with { SpiralAngle = 5000 }, emitter with { RateCounter = 1 << 20 } } )
			Assert.IsNotNull( ParticleSystem.ValidateState( good with { Emitters = new[] { bad } } ), "out-of-range emitter fields are refused before they can overflow a tick" );
		Assert.ThrowsException<InvalidDataException>( () => New( pool ).Restore( good with { FreeEmitters = Array.Empty<int>() } ) );
		var smaller = New( new[] { pool[0] } );
		Assert.ThrowsException<InvalidDataException>( () => smaller.Restore( good with { Emitters = new[] { good.Emitters[0] with { EffectId = 50 } } } ), "the template file lacks the effect" );
	}

	// ---- renderer helpers ----

	[TestMethod]
	public void EngineConversionTruncatesTowardZero()
	{
		Assert.AreEqual( 2047, ParticleRendererModule.Fixed( 1.9999f ), "_ftol chops" );
		Assert.AreEqual( -2047, ParticleRendererModule.Fixed( -1.9999f ) );
		Assert.AreEqual( 0, ParticleRendererModule.Fixed( -0.0009f ) );
		Assert.AreEqual( (1023, 0, -1023), ParticleRendererModule.ToNativeDirection( new System.Numerics.Vector3( 0.9999f, -0.9999f, 0f ) ), "engine (x, y, z up) = original (x, z, y)" );
	}

	[TestMethod]
	public void QuadsAreDrawnInTheOriginalsOrderAndOnlyNeighboursOfOneKindMerge()
	{
		// [BIN:TPW-EXE:0x57C620] one primitive per particle in slot order, world and screen-space particles in one loop: alpha, alpha, one-plus-one, alpha again, a screen-space alpha
		var runs = new List<(int Kind, int First, int Count)>();
		var first = 0;
		foreach ( var (batch, screen) in new[] { (0, false), (0, false), (2, false), (0, false), (0, true), (0, true) } )
		{
			ParticleRenderer.AddToRuns( runs, ParticleRenderer.KindOf( batch, screen ), first, 6 );
			first += 6;
		}
		CollectionAssert.AreEqual( new[] { (0, 0, 12), (2, 12, 6), (0, 18, 6), (3, 24, 12) }, runs, "a later alpha quad does not jump ahead of the one-plus-one quad between them" );
	}

	[TestMethod]
	public void RenderFlagsPickTheBlendForUntexturedParticlesToo()
	{
		Assert.AreEqual( 1, ParticleRenderer.BatchIndexOf( 0x2004 ) );
		Assert.AreEqual( 2, ParticleRenderer.BatchIndexOf( 0x4 ) );
		Assert.AreEqual( 0, ParticleRenderer.BatchIndexOf( 0 ) );
		Assert.AreEqual( 0, ParticleRenderer.BatchIndexOf( 0x2000 ), "0x2000 without bit 4 is plain alpha" );
	}

	[TestMethod]
	public void QuadHalfHeightIsSizeOver1024AndOnlyTheWidthGetsAFactor()
	{
		var (width, height) = ParticleRenderer.HalfExtents( 2048, 0.2f, 16, 64, 32, displayAspect: 0.75f );
		Assert.AreEqual( 2048 / 1024f * 0.2f, height, 1e-6f, "frame pixels never scale the height" );
		// [BIN:TPW-EXE:0x57C71F quad width] a textured quad's half-width is the half-height x the frame's width / height in clip x, which spans the display's width: on a 4:3 display (aspect word 0.75) the quad is that much wider on the screen
		Assert.AreEqual( height * 2 / 0.75f, width, 1e-6f, "the width carries the frame's aspect and the display's width over its height" );
		var (flatWidth, flatHeight) = ParticleRenderer.HalfExtents( 512, 1f, 0, 4, 4, displayAspect: 0.75f );
		Assert.AreEqual( (0.5f, 0.5f), (flatWidth, flatHeight), "an untextured particle has factor 1 (the aspect word makes it square on the screen)" );
	}

	[TestMethod]
	public void ATexturedQuadsWidthOnTheScreenIsTheFrameAspectTimesTheDisplaysWidthOverHeight()
	{
		foreach ( var (displayWidth, displayHeight) in new[] { (640, 480), (1280, 720), (1000, 1000) } )
		{
			var aspect = (float)displayHeight / displayWidth;
			var (halfWidth, halfHeight) = ParticleRenderer.HalfExtents( 1024, 1f, 4, 48, 32, aspect );
			// the aspect-correct world turns a world width into pixels at the display's height over its width: pixels across / pixels up of the quad
			var pixelsAcross = halfWidth * aspect * displayWidth;
			var pixelsUp = halfHeight * displayHeight;
			Assert.AreEqual( 48f / 32f * displayWidth / displayHeight, pixelsAcross / pixelsUp, 1e-4f, $"{displayWidth}x{displayHeight}" );
		}
		Assert.AreEqual( 1f, ParticleRenderer.TexturedWidthFactor( 32, 32, 1f ), 1e-6f, "a square frame on a square display stays square" );
	}

	[TestMethod]
	public void AScreenSpaceQuadsHalfWidthIsTheFrameAspectWhenTexturedAndTheDisplayAspectWhenNot()
	{
		// [BIN:TPW-EXE:0x57C71F screen quad width] flag 0x40000 skips the transform: the half-width is half-height x float +8 or x the aspect word, in clip units
		Assert.AreEqual( 0.1f * 64 / 32, ParticleRenderer.ScreenHalfWidth( 0.1f, 5, 64, 32, 0.75f ), 1e-6f, "textured: the frame's width over its height, no display factor" );
		Assert.AreEqual( 0.1f * 0.75f, ParticleRenderer.ScreenHalfWidth( 0.1f, 0, 8, 8, 0.75f ), 1e-6f, "untextured: the display's height over its width" );
		Assert.AreEqual( 0.1f * 0.5625f, ParticleRenderer.ScreenHalfWidth( 0.1f, 0, 8, 8, 0.5625f ), 1e-6f );
	}
}
