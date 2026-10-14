using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The original particle pool (docs/PARTICLES.md): template file layout, the 120-emitter pool, bursts and rates, life and
/// colour progression, chained effects, the type-2 direction, the control scale and deterministic replay. The synthetic
/// cases use hand-written templates; the corpus case reads the shipped Data/Particle/Tp2.plb (inconclusive without
/// OPENTPW_GAME_PATH). No GPU resources are created.
/// </summary>
[TestClass]
public class ParticleSystemTests
{
	[TestInitialize]
	public void Init() => Log = new();

	/// <summary>A template whose fields are set by (offset, size, value); everything else is zero.</summary>
	private static ParticleTemplate Template( params (int Offset, int Size, int Value)[] fields )
	{
		var bytes = new byte[ParticleTemplate.Size];
		foreach ( var (offset, size, value) in fields )
			BitConverter.GetBytes( value ).AsSpan( 0, size ).CopyTo( bytes.AsSpan( offset ) );
		// no death effects, no child unless stated
		foreach ( var offset in new[] { 0x10, 0x98, 0xB4 } )
			if ( !fields.Any( field => field.Offset == offset ) )
				BitConverter.GetBytes( (short)-1 ).CopyTo( bytes, offset );
		if ( !fields.Any( field => field.Offset == 0xA2 ) )
			BitConverter.GetBytes( (short)1000 ).CopyTo( bytes, 0xA2 );
		return new ParticleTemplate( bytes );
	}

	private static (int, int, int) Life( int value ) => (0x20, 4, value);
	private static (int, int, int) Immortal() => (0x26, 1, 1);
	private static (int, int, int) Burst( int value ) => (0x60, 4, value);
	private static (int, int, int) Cap( int value ) => (0x64, 4, value);
	private static (int, int, int) Rates( int a, int b, int c, int d ) => (0x68, 4, (a & 0xFF) | (b & 0xFF) << 8 | (c & 0xFF) << 16 | (d & 0xFF) << 24);
	private static (int, int, int) ParticleLife( int value ) => (0x78, 4, value);

	/// <summary>A list of 105 templates, every one inert except the given ones.</summary>
	private static List<ParticleTemplate> Pool( params (int Id, ParticleTemplate Template)[] given )
	{
		var list = Enumerable.Range( 0, 105 ).Select( _ => Template( Life( 1 ), Cap( 500 ), ParticleLife( 10 ) ) ).ToList();
		foreach ( var (id, template) in given )
			list[id] = template;
		return list;
	}

	private static ParticleSystem Run( ParticleSystem system, int ticks )
	{
		for ( var index = 0; index < ticks; index++ )
			system.Tick();
		return system;
	}

	private static readonly (int, int, int) Origin = (0, 0, 0);

	// ---- template file ----

	private static byte[] FileBytes( int templates, int effectors, bool trailer = true )
	{
		var stream = new MemoryStream();
		var writer = new BinaryWriter( stream );
		writer.Write( templates ); writer.Write( ParticleTemplate.Size );
		for ( var index = 0; index < templates; index++ )
		{
			var bytes = new byte[ParticleTemplate.Size];
			bytes[0x118] = (byte)'A';
			bytes[0x119] = (byte)('0' + index % 10);
			BitConverter.GetBytes( 7 + index ).CopyTo( bytes, 0x20 );
			writer.Write( bytes );
		}
		writer.Write( effectors ); writer.Write( ParticleTemplateFile.EffectorSize );
		for ( var index = 0; index < effectors; index++ )
			writer.Write( new byte[ParticleTemplateFile.EffectorSize] );
		if ( trailer )
		{
			writer.Write( 33 ); writer.Write( 1024 );
		}
		return stream.ToArray();
	}

	[TestMethod]
	public void TemplateFileParsesTemplatesEffectorsAndTrailer()
	{
		var file = new ParticleTemplateFile( new MemoryStream( FileBytes( 105, 20 ) ) );
		Assert.AreEqual( 105, file.Templates.Count );
		Assert.AreEqual( 20, file.Effectors.Count );
		Assert.AreEqual( "A3", file.Templates[3].Name );
		Assert.AreEqual( 10, file.Templates[3].EmitterLife );
		Assert.AreEqual( 20, new ParticleTemplateFile( new MemoryStream( FileBytes( 105, 20, trailer: false ) ) ).Effectors.Count, "the trailer is optional" );
	}

	[TestMethod]
	public void TemplateFileRejectsBadLayouts()
	{
		var good = FileBytes( 105, 20 );
		void Rejects( byte[] bytes, string why ) => Assert.ThrowsException<InvalidDataException>( () => new ParticleTemplateFile( new MemoryStream( bytes ) ), why );
		Rejects( good[..(good.Length - 1)], "a trailer cut short" );
		Rejects( good[..100], "a file ending inside its templates" );
		Rejects( good[..8], "no templates at all" );
		var wrongSize = (byte[])good.Clone();
		BitConverter.GetBytes( 300 ).CopyTo( wrongSize, 4 );
		Rejects( wrongSize, "a template size other than 320" );
		var wrongEffector = (byte[])good.Clone();
		BitConverter.GetBytes( 99 ).CopyTo( wrongEffector, 8 + 105 * ParticleTemplate.Size + 4 );
		Rejects( wrongEffector, "an effector size other than 104" );
		var huge = (byte[])good.Clone();
		BitConverter.GetBytes( int.MaxValue ).CopyTo( huge, 0 );
		Rejects( huge, "an absurd count" );
		Rejects( good.Concat( new byte[3] ).ToArray(), "stray trailing bytes" );
		Assert.ThrowsException<ArgumentException>( () => new ParticleTemplate( new byte[10] ) );
	}

	[TestMethod]
	public void ShippedTemplatesNameTheEffectsOfTheHeader()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the shipped Tp2.plb." );
		var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
		var particle = Directory.EnumerateDirectories( dataPath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "particle", StringComparison.OrdinalIgnoreCase ) );
		var plb = particle == null ? null : Directory.EnumerateFiles( particle ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "Tp2.plb", StringComparison.OrdinalIgnoreCase ) );
		if ( plb == null )
			Assert.Inconclusive( "The original Data/Particle/Tp2.plb is missing." );
		using var stream = File.OpenRead( plb! );
		var file = new ParticleTemplateFile( stream );
		Assert.AreEqual( 105, file.Templates.Count );
		Assert.AreEqual( 20, file.Effectors.Count );
		// the names of Data/Particle/par_lib.h
		Assert.AreEqual( "Sparks", file.Templates[1].Name );
		Assert.AreEqual( "Smoke", file.Templates[2].Name );
		Assert.AreEqual( "Firework1", file.Templates[3].Name );
		Assert.AreEqual( "FW1explosion", file.Templates[60].Name );
		Assert.AreEqual( "LaserRing", file.Templates[63].Name );
		var sparks = file.Templates[1];
		Assert.AreEqual( 12, sparks.EmitterLife );
		Assert.AreEqual( 37, sparks.RadialSpeed );
		CollectionAssert.AreEqual( new[] { 5, 3, 1, 1 }, Enumerable.Range( 0, 4 ).Select( sparks.Rate ).ToArray() );
		Assert.AreEqual( 8, sparks.ParticleLife );
		Assert.AreEqual( 0, sparks.FrameCount, "Sparks are untextured" );
		Assert.AreEqual( 0xFFFF6900u, sparks.Colour( 15 ), "opaque orange at birth" );
		Assert.AreEqual( 0u, sparks.Colour( 0 ) >> 24, "transparent at death" );
		var smoke = file.Templates[2];
		Assert.IsTrue( smoke.Immortal );
		Assert.AreEqual( (11, 16), (smoke.SpriteCode, smoke.FrameCount) );
		Assert.AreEqual( (800, 2200), (smoke.SizeAtBirth, smoke.SizeAtDeath) );
		var firework = file.Templates[3];
		Assert.AreEqual( 60, firework.DeathEffect );
		Assert.AreEqual( 109, firework.EmitterVelocity.Y );
		Assert.AreEqual( 63, file.Templates[60].ChildEffect );
		Assert.IsTrue( file.Templates.All( template => template.Control == 1000 || template.Name.Length == 0 ), "the loader default control is 1000" );

		// the shipped data drives the pool: a firework rocket explodes, its explosion chains the laser ring, and everything ends
		var system = new ParticleSystem( file.Templates );
		Assert.IsTrue( system.Create( 3, (0, 0, 0) ) >= 0 );
		var seen = new HashSet<int>();
		for ( var tick = 0; tick < 400; tick++ )
		{
			system.Tick();
			foreach ( var effect in system.LiveEffects() )
				seen.Add( effect );
		}
		CollectionAssert.IsSubsetOf( new[] { 3, 60, 63 }, seen.ToArray() );
		Assert.AreEqual( 0, system.LiveEmitters, "a finished firework frees its slots" );
		Assert.AreEqual( 0, system.LiveParticles );
	}

	// ---- pool ----

	[TestMethod]
	public void RefusesEffectsOutsideTheTableAndLowDetailTemplates()
	{
		var pool = Pool( (5, Template( Life( 3 ), (0xC1, 1, 1) )) );
		var system = new ParticleSystem( pool );
		Assert.AreEqual( -1, system.Create( -1, Origin ) );
		Assert.AreEqual( -1, system.Create( 105, Origin ) );
		Assert.AreEqual( -1, system.Create( 100000, Origin ) );
		Assert.IsTrue( system.Create( 104, Origin ) >= 0, "104 is the last valid effect" );
		Assert.IsTrue( system.Create( 5, Origin ) >= 0, "the low-detail flag only matters with low detail on" );
		var low = new ParticleSystem( pool, lowDetail: true );
		Assert.AreEqual( -1, low.Create( 5, Origin ), "template byte +0xC1 refuses under low detail" );
		Assert.IsTrue( low.Create( 6, Origin ) >= 0 );
		Assert.AreEqual( -1, new ParticleSystem( new[] { pool[0] } ).Create( 50, Origin ), "a table shorter than the id" );
	}

	[TestMethod]
	public void HandleIsSlotWithGeneration()
	{
		var system = new ParticleSystem( Pool( (1, Template( Life( 2 ) )) ) );
		var first = system.Create( 1, Origin );
		Assert.AreEqual( 0, first & 0xFFFF );
		Assert.IsTrue( system.IsAlive( first ) );
		Run( system, 8 );
		Assert.IsFalse( system.IsAlive( first ), "a finished emitter retires its handle" );
		var second = system.Create( 1, Origin );
		Assert.AreEqual( 0, second & 0xFFFF, "the slot is reused" );
		Assert.AreNotEqual( first, second, "with a new generation" );
		Assert.IsFalse( system.Kill( first ), "the stale handle does not kill the new effect" );
		Assert.IsTrue( system.IsAlive( second ) );
	}

	[TestMethod]
	public void BurstAndPerQuarterRatesCountParticles()
	{
		// Sparks-like: a 12 tick emitter, rates 5/3/1/1 per quarter; the particles outlive the test
		var system = new ParticleSystem( Pool( (1, Template( Life( 12 ), Burst( 4 ), Cap( 500 ), Rates( 5, 3, 1, 1 ), ParticleLife( 1000 ) )) ) );
		system.Create( 1, Origin );
		Assert.AreEqual( 4, system.LiveParticles, "the burst is emitted by the constructor" );
		system.Tick();
		Assert.AreEqual( 4, system.LiveParticles, "the very first tick does nothing" );
		Run( system, 12 );
		Assert.AreEqual( 4 + 3 * 5 + 3 * 3 + 3 * 1 + 3 * 1, system.LiveParticles, "3 ticks per quarter" );
		Run( system, 5 );
		Assert.AreEqual( 34, system.LiveParticles, "no emission after the emitter's life" );
	}

	[TestMethod]
	public void NegativeRateEmitsOneParticlePerPeriodAndCapsStop()
	{
		var system = new ParticleSystem( Pool( (1, Template( Life( 20 ), Cap( 500 ), Rates( -5, -5, -5, -5 ), ParticleLife( 1000 ) )) ) );
		system.Create( 1, Origin );
		Run( system, 21 );
		Assert.AreEqual( 4, system.LiveParticles, "one particle every five ticks over twenty: ages 0, 5, 10, 15" );

		var capped = new ParticleSystem( Pool( (1, Template( Life( 20 ), Cap( 3 ), Rates( 5, 5, 5, 5 ), ParticleLife( 1000 ) )) ) );
		capped.Create( 1, Origin );
		Run( capped, 10 );
		Assert.AreEqual( 3, capped.LiveParticles, "the per-emitter cap" );
	}

	[TestMethod]
	public void DensityScalesTheEmissionUnlessTheTemplateOptsOut()
	{
		var scaled = Template( Life( 5 ), Burst( 10 ), Cap( 500 ), ParticleLife( 1000 ) );
		var fixedBurst = Template( Life( 5 ), Burst( 10 ), Cap( 500 ), ParticleLife( 1000 ), (0xC0, 1, 1) );
		foreach (var (density, expected, expectedFixed) in new[] { (500, 5, 10), (1000, 10, 10), (2000, 20, 10) })
		{
			var system = new ParticleSystem( Pool( (1, scaled), (2, fixedBurst) ), density );
			system.Create( 1, Origin );
			Assert.AreEqual( expected, system.LiveParticles, $"density {density}" );
			system.Create( 2, Origin );
			Assert.AreEqual( expected + expectedFixed, system.LiveParticles, $"density {density}, template +0xC0" );
		}
		Assert.AreEqual( 500, new ParticleSystem( Pool(), 1 ).Density, "the density setting is clamped to 500..2000" );
	}

	[TestMethod]
	public void ParticleLifeJittersByAQuarter()
	{
		var system = new ParticleSystem( Pool( (1, Template( Life( 1 ), Burst( 200 ), Cap( 500 ), ParticleLife( 40 ) )) ) );
		system.Create( 1, Origin );
		Assert.AreEqual( 200, system.LiveParticles );
		system.Tick(); // the skipped first call
		Run( system, 29 );
		Assert.AreEqual( 200, system.LiveParticles, "no particle lives less than 30 ticks (40 - 25%)" );
		var alive = new List<int>();
		for ( var tick = 0; tick < 25; tick++ )
		{
			system.Tick();
			alive.Add( system.LiveParticles );
		}
		Assert.AreEqual( 0, system.LiveParticles, "none outlives 50 ticks (40 + 25%)" );
		Assert.IsTrue( alive.Any( count => count > 0 && count < 200 ), "the lives are spread, not one value" );
		Assert.AreEqual( 0, system.LiveEmitters, "the emitter ended too" );
	}

	[TestMethod]
	public void ColourAndFrameFollowTheParticleLife()
	{
		var colours = Enumerable.Range( 0, 16 ).SelectMany( index => new[] { (0xD8 + index * 4, 4, unchecked((int)(0xFF000000u | (uint)(index * 16 + 5)))) } ).ToArray();
		var fields = new List<(int, int, int)> { Life( 1 ), Burst( 1 ), Cap( 500 ), ParticleLife( 40 ), (0x94, 2, 11), (0x96, 2, 16), (0x74, 4, 800), (0xA6, 2, 2200) };
		fields.AddRange( colours );
		var system = new ParticleSystem( Pool( (1, Template( fields.ToArray() )) ) );
		system.Create( 1, Origin );
		var sprites = new List<ParticleSprite>();
		system.Snapshot( sprites );
		Assert.AreEqual( 1, sprites.Count );
		Assert.AreEqual( 15 * 16 + 5, (int)(sprites[0].Colour & 0xFF), "birth takes colour index 15" );
		Assert.AreEqual( 0, sprites[0].Frame );
		Assert.AreEqual( 800, sprites[0].Size );
		var lastColour = 15; var lastFrame = 0; var lastSize = 800; var frames = new HashSet<int>();
		while ( system.LiveParticles > 0 )
		{
			system.Tick();
			if ( system.Snapshot( sprites ) == 0 )
				break;
			var colour = (int)((sprites[0].Colour & 0xFF) - 5) / 16;
			Assert.IsTrue( colour <= lastColour, "the colour index only falls" );
			Assert.IsTrue( sprites[0].Frame >= lastFrame && sprites[0].Frame < 16, "frames advance within the animation" );
			Assert.IsTrue( sprites[0].Size >= lastSize && sprites[0].Size <= 2200, "the size grows from birth to death" );
			(lastColour, lastFrame, lastSize) = (colour, sprites[0].Frame, sprites[0].Size);
			frames.Add( sprites[0].Frame );
		}
		Assert.IsTrue( lastColour <= 1 && lastFrame >= 14 && lastSize > 2000, "the last visible tick is near the end of the life" );
		Assert.IsTrue( frames.Count >= 10, "most of the 16 frames show" );
	}

	[TestMethod]
	public void RandomColourKeepsTheAlphaOfTheLifeAndPicksAnotherColour()
	{
		var fields = new List<(int, int, int)> { Life( 1 ), Burst( 40 ), Cap( 500 ), ParticleLife( 1000 ), (0x58, 4, 1) };
		for ( var index = 0; index < 16; index++ )
			fields.Add( (0xD8 + index * 4, 4, unchecked((int)(0x80000000u | (uint)(index << 16)))) );
		var system = new ParticleSystem( Pool( (1, Template( fields.ToArray() )) ) );
		system.Create( 1, Origin );
		var sprites = new List<ParticleSprite>();
		system.Snapshot( sprites );
		Assert.IsTrue( sprites.All( sprite => (sprite.Colour >> 24) == 0x80 ), "the alpha is the palette's at the life index" );
		Assert.IsTrue( sprites.Select( sprite => (sprite.Colour >> 16) & 0xFF ).Distinct().Count() > 4, "the colour varies per particle" );
	}

	[TestMethod]
	public void ParticlesDoNotMoveOnTheirSpawnTickAndStoreSixtyFourthsOfAUnit()
	{
		// base particle velocity 12 on x, no drag or gravity
		var system = new ParticleSystem( Pool( (1, Template( Life( 1 ), Burst( 1 ), Cap( 500 ), ParticleLife( 1000 ), (0x38, 4, 12) )) ) );
		system.Create( 1, (10 * 1024, 20 * 1024, 30 * 1024) );
		var sprites = new List<ParticleSprite>();
		system.Snapshot( sprites );
		Assert.AreEqual( (640, 1280, 1920), (sprites[0].X, sprites[0].Y, sprites[0].Z), "native units are 1/1024, stored >> 4 as 1/64" );
		system.Tick();
		system.Tick();
		system.Snapshot( sprites );
		Assert.AreEqual( 640, sprites[0].X, "the spawn tick has no motion" );
		system.Tick();
		system.Snapshot( sprites );
		Assert.AreEqual( 652, sprites[0].X, "then the velocity is added each tick" );
	}

	[TestMethod]
	public void ParticleDragAndGravityActOnVelocity()
	{
		// drag 512/1024 halves the velocity per tick; gravity 1 lowers y by one per tick (Y is up)
		var system = new ParticleSystem( Pool( (1, Template( Life( 1 ), Burst( 1 ), Cap( 500 ), ParticleLife( 1000 ), (0x38, 4, 64), (0x7C, 4, 512), (0x80, 4, 1) )) ) );
		system.Create( 1, Origin );
		Run( system, 2 ); // the skipped call, then the spawn tick
		var sprites = new List<ParticleSprite>();
		system.Tick();
		system.Snapshot( sprites );
		Assert.AreEqual( 32, sprites[0].X, "64 - 64 * 512 / 1024, then x += 32" );
		Assert.AreEqual( -1, sprites[0].Y );
		system.Tick();
		system.Snapshot( sprites );
		Assert.AreEqual( 32 + 16, sprites[0].X );
		Assert.AreEqual( -1 - 2, sprites[0].Y, "vy falls by the gravity each tick" );
	}

	[TestMethod]
	public void DirectedEffectReplacesTheBaseParticleVelocity()
	{
		// +0xB0 = 12: the speed of a type-2 effect; a unit direction (1024 = 1) scales it
		var template = Template( Life( 1 ), Burst( 1 ), Cap( 500 ), ParticleLife( 1000 ), (0x38, 4, 99), (0xB0, 4, 12) );
		var plain = new ParticleSystem( Pool( (1, template) ) );
		plain.Create( 1, Origin );
		var directed = new ParticleSystem( Pool( (1, template) ) );
		Assert.IsTrue( directed.CreateDirected( 1, Origin, (0, 1024, 0) ) >= 0 );
		var sprites = new List<ParticleSprite>();
		Run( plain, 3 );
		plain.Snapshot( sprites );
		Assert.AreEqual( (99, 0, 0), (sprites[0].X, sprites[0].Y, sprites[0].Z), "type 1 keeps the template velocity" );
		Run( directed, 3 );
		directed.Snapshot( sprites );
		Assert.AreEqual( (0, 12, 0), (sprites[0].X, sprites[0].Y, sprites[0].Z), "type 2: direction * speed / 1024 set before the burst" );
		var diagonal = new ParticleSystem( Pool( (1, template) ) );
		diagonal.CreateDirected( 1, Origin, (724, 724, 0) );
		Run( diagonal, 3 );
		diagonal.Snapshot( sprites );
		Assert.AreEqual( (8, 8, 0), (sprites[0].X, sprites[0].Y, sprites[0].Z) );
	}

	[TestMethod]
	public void ControlScalesTheDrawnOffsetAroundTheEmitter()
	{
		var fields = new[] { Life( 1 ), Burst( 1 ), Cap( 500 ), ParticleLife( 1000 ), (0x38, 4, 100), (0x3C, 4, -50) };
		var sprites = new List<ParticleSprite>();
		ParticleSprite Drawn( int? control )
		{
			var system = new ParticleSystem( Pool( (1, Template( fields )) ) );
			system.Create( 1, (1024, 2048, 3072), control );
			Run( system, 3 );
			system.Snapshot( sprites );
			return sprites[0];
		}
		var identity = Drawn( null );
		Assert.AreEqual( (164, 128 - 50, 192), (identity.X, identity.Y, identity.Z), "the template's own control (1000) leaves the offset as is" );
		var half = Drawn( 500 );
		Assert.AreEqual( (64 + 50, 128 - 25, 192), (half.X, half.Y, half.Z), "drawn = emitter + (particle - emitter) * control / 1000" );
		var zero = Drawn( 0 );
		Assert.AreEqual( (64, 128, 192), (zero.X, zero.Y, zero.Z) );
	}

	[TestMethod]
	public void EmitterMovesUnderItsOwnVelocityAndGravity()
	{
		// the emitter drifts 10 on x per tick; particles stay where they are born
		var template = Template( Life( 100 ), Cap( 500 ), Rates( 1, 1, 1, 1 ), ParticleLife( 1000 ), (0x28, 4, 2), (0x2C, 4, 10) );
		var system = new ParticleSystem( Pool( (1, template) ) );
		system.Create( 1, Origin );
		Run( system, 6 );
		var sprites = new List<ParticleSprite>();
		system.Snapshot( sprites );
		var xs = sprites.Select( sprite => sprite.X ).OrderBy( x => x ).ToArray();
		CollectionAssert.AreEqual( new[] { 10, 20, 30, 40, 50 }, xs, "one particle per tick at the emitter's current place" );
		var heights = sprites.OrderBy( sprite => sprite.X ).Select( sprite => sprite.Y ).ToArray();
		CollectionAssert.AreEqual( new[] { -2, -6, -12, -20, -30 }, heights, "gravity 2 lowers the emitter's vertical speed by 2 per tick (Y is up)" );
	}

	[TestMethod]
	public void EmitterBouncesOnTheGroundCallback()
	{
		var template = Template( Life( 100 ), Cap( 500 ), Rates( 1, 1, 1, 1 ), ParticleLife( 1000 ), (0x28, 4, 2), (0x76, 2, 50) );
		var system = new ParticleSystem( Pool( (1, template) ), ground: ( x, z ) => 0 );
		system.Create( 1, Origin );
		Run( system, 30 );
		var sprites = new List<ParticleSprite>();
		system.Snapshot( sprites );
		Assert.IsTrue( sprites.Count >= 20 );
		Assert.IsTrue( sprites.All( sprite => sprite.Y >= 0 ), "particles are born on or above the ground the callback reports" );
		var free = new ParticleSystem( Pool( (1, template) ) );
		free.Create( 1, Origin );
		Run( free, 30 );
		free.Snapshot( sprites );
		Assert.IsTrue( sprites.Any( sprite => sprite.Y < 0 ), "without the callback the emitter falls through" );
	}

	[TestMethod]
	public void FinishedEmitterWaitsForItsParticlesAndThenFreesItsSlot()
	{
		var system = new ParticleSystem( Pool( (1, Template( Life( 2 ), Burst( 3 ), Cap( 500 ), ParticleLife( 20 ) )) ) );
		system.Create( 1, Origin );
		Run( system, 10 );
		Assert.AreEqual( 1, system.LiveEmitters, "the emitter's life ended, its particles did not" );
		Run( system, 30 );
		Assert.AreEqual( 0, system.LiveEmitters );
		Assert.AreEqual( 0, system.LiveParticles );
	}

	[TestMethod]
	public void ImmortalEmittersNeverEndUntilKilledAndKillSkipsTheDeathEffect()
	{
		var pool = Pool( (1, Template( Life( 0 ), Immortal(), Burst( 1 ), Cap( 500 ), ParticleLife( 5 ), (0x10, 2, 2) )), (2, Template( Life( 1 ), Burst( 1 ), ParticleLife( 5 ) )) );
		var system = new ParticleSystem( pool );
		var handle = system.Create( 1, Origin );
		Run( system, 200 );
		Assert.IsTrue( system.IsAlive( handle ), "an immortal emitter ignores its life" );
		Assert.IsTrue( system.Kill( handle ) );
		Run( system, 20 );
		Assert.IsFalse( system.IsAlive( handle ) );
		CollectionAssert.DoesNotContain( system.LiveEffects().ToArray(), 2, "RSE KILL sets life -2: no death effect" );
		Assert.AreEqual( 0, system.LiveEmitters );
	}

	[TestMethod]
	public void EmitterDeathStartsItsDeathEffect()
	{
		var pool = Pool( (3, Template( Life( 3 ), Cap( 500 ), ParticleLife( 5 ), (0x10, 2, 60) )), (60, Template( Life( 6 ), Cap( 500 ), ParticleLife( 5 ), (0xB4, 2, 63) )), (63, Template( Life( 20 ), Cap( 1 ), ParticleLife( 5 ) )) );
		var system = new ParticleSystem( pool );
		system.Create( 3, Origin );
		CollectionAssert.AreEqual( new[] { 3 }, system.LiveEffects().ToArray() );
		var seen = new List<int[]>();
		for ( var tick = 0; tick < 40; tick++ )
		{
			system.Tick();
			seen.Add( system.LiveEffects().ToArray() );
		}
		Assert.IsTrue( seen.Any( effects => effects.SequenceEqual( new[] { 60, 63 } ) || effects.OrderBy( e => e ).SequenceEqual( new[] { 60, 63 } ) ),
			"Firework1 (3) spawns FW1explosion (60) at its death, which chains LaserRing (63) at its construction" );
		Assert.IsFalse( seen.Any( effects => effects.Contains( 3 ) && effects.Contains( 60 ) ), "the rocket is gone when the explosion starts" );
		Assert.AreEqual( 0, system.LiveEmitters );
	}

	[TestMethod]
	public void ChildEmitterFollowsItsParentWhenTheTemplateAsksFor()
	{
		var parent = Template( Life( 30 ), Cap( 500 ), ParticleLife( 5 ), (0xB4, 2, 2), (0xB8, 1, 1), (0x2C, 4, 64) );
		var child = Template( Life( 30 ), Burst( 0 ), Cap( 500 ), Rates( 1, 1, 1, 1 ), ParticleLife( 1000 ) );
		var system = new ParticleSystem( Pool( (1, parent), (2, child) ) );
		system.Create( 1, Origin );
		Run( system, 11 );
		var sprites = new List<ParticleSprite>();
		system.Snapshot( sprites );
		Assert.IsTrue( sprites.Count > 5 );
		Assert.IsTrue( sprites.Max( sprite => sprite.X ) >= 64 * 5, "the child's particles are born where the moving parent is" );

		var loose = new ParticleSystem( Pool( (1, Template( Life( 30 ), Cap( 500 ), ParticleLife( 5 ), (0xB4, 2, 2), (0x2C, 4, 64) )), (2, child) ) );
		loose.Create( 1, Origin );
		Run( loose, 11 );
		loose.Snapshot( sprites );
		Assert.AreEqual( 0, sprites.Max( sprite => sprite.X ), "without +0xB8 the child stays where it was created" );
	}

	[TestMethod]
	public void SlotsRunOutAfterOneHundredTwentyEmittersAndComeBack()
	{
		var system = new ParticleSystem( Pool( (1, Template( Life( 0 ), Immortal(), Cap( 500 ), ParticleLife( 5 ) )) ) );
		var handles = Enumerable.Range( 0, ParticleSystem.EmitterSlots ).Select( _ => system.Create( 1, Origin ) ).ToArray();
		Assert.IsTrue( handles.All( handle => handle >= 0 ) );
		Assert.AreEqual( 120, system.LiveEmitters );
		Assert.AreEqual( -1, system.Create( 1, Origin ), "the 121st emitter is refused" );
		Assert.IsTrue( system.Kill( handles[7] ) );
		system.Tick();
		system.Tick();
		Assert.AreEqual( 119, system.LiveEmitters );
		Assert.IsTrue( system.Create( 1, Origin ) >= 0, "a freed slot is reusable" );
	}

	[TestMethod]
	public void ParticlePoolExhaustionStopsEmissionWithoutFailing()
	{
		var system = new ParticleSystem( Pool( (1, Template( Life( 3 ), Burst( 500 ), Cap( 500 ), Rates( 20, 20, 20, 20 ), ParticleLife( 1000 ) )) ), particleCount: 100 );
		Assert.AreEqual( 100, system.Capacity, "the pool clamps to at least 100 particles" );
		system.Create( 1, Origin );
		Run( system, 5 );
		Assert.AreEqual( 100, system.LiveParticles );
		Assert.AreEqual( 100, new ParticleSystem( Pool(), particleCount: 5 ).Capacity );
		Assert.AreEqual( 32767, new ParticleSystem( Pool(), particleCount: 99999 ).Capacity );
	}

	[TestMethod]
	public void ReplayIsDeterministicAndClearReseedsTheGenerator()
	{
		var fields = new[] { Life( 12 ), Burst( 6 ), Cap( 500 ), Rates( 5, 3, 1, 1 ), ParticleLife( 8 ), (0x5C, 4, 37), (0x44, 4, 30), (0x58, 4, 1), (0x84, 4, 3), (0x50, 4, 5) };
		List<ParticleSprite> Play( ParticleSystem system )
		{
			system.Create( 1, (100, 200, 300) );
			Run( system, 8 );
			var sprites = new List<ParticleSprite>();
			system.Snapshot( sprites );
			return sprites;
		}
		var a = Play( new ParticleSystem( Pool( (1, Template( fields )) ) ) );
		var b = Play( new ParticleSystem( Pool( (1, Template( fields )) ) ) );
		Assert.IsTrue( a.Count > 10 );
		CollectionAssert.AreEqual( a, b, "the same inputs give the same particles" );
		var reused = new ParticleSystem( Pool( (1, Template( fields )) ) );
		Play( reused );
		Run( reused, 50 );
		reused.Clear();
		Assert.AreEqual( 0, reused.LiveParticles );
		Assert.AreEqual( 0, reused.LiveEmitters );
		CollectionAssert.AreEqual( a, Play( reused ), "Clear restarts the private generator from 1" );
	}

	[TestMethod]
	public void RadialBurstSpreadsEveryDirection()
	{
		var system = new ParticleSystem( Pool( (1, Template( Life( 1 ), Burst( 200 ), Cap( 500 ), ParticleLife( 1000 ), (0x5C, 4, 37) )) ) );
		system.Create( 1, Origin );
		Run( system, 3 );
		var sprites = new List<ParticleSprite>();
		system.Snapshot( sprites );
		Assert.AreEqual( 200, sprites.Count );
		Assert.IsTrue( sprites.All( sprite => Math.Abs( Math.Sqrt( (double)sprite.X * sprite.X + (double)sprite.Y * sprite.Y + (double)sprite.Z * sprite.Z ) - 37 ) < 2.5 ), "speed 37 in every direction" );
		Assert.IsTrue( sprites.Any( sprite => sprite.X > 20 ) && sprites.Any( sprite => sprite.X < -20 ) && sprites.Any( sprite => sprite.Y > 20 ) && sprites.Any( sprite => sprite.Y < -20 ) );
	}

	[TestMethod]
	public void AdvanceRunsWholeThirtyOneMillisecondTicks()
	{
		var system = new ParticleSystem( Pool( (1, Template( Life( 1000 ), Cap( 500 ), ParticleLife( 5 ) )) ) );
		system.Create( 1, Origin );
		Assert.AreEqual( 0, system.Advance( 0.01f ) );
		Assert.AreEqual( 0, system.Advance( float.NaN ) );
		Assert.AreEqual( 0, system.Advance( -1 ) );
		Assert.AreEqual( 1, system.Advance( 0.025f ), "0.035 s in total: one tick, 4 ms carried over" );
		Assert.AreEqual( 8, system.Advance( 60f ), "a stalled frame is capped at a quarter second" );
	}

	[TestMethod]
	public void EmitterBoundsCoverTheLiveParticles()
	{
		var system = new ParticleSystem( Pool( (1, Template( Life( 1 ), Burst( 50 ), Cap( 500 ), ParticleLife( 1000 ), (0x44, 4, 100), (0x48, 4, 0), (0x4C, 4, 100) )) ) );
		system.Create( 1, (1024, 1024, 1024) );
		var bounds = new List<ParticleEmitterBounds>();
		Assert.AreEqual( 1, system.CollectBounds( bounds ) );
		var sprites = new List<ParticleSprite>();
		system.Snapshot( sprites );
		Assert.IsTrue( sprites.All( sprite => sprite.X >= bounds[0].MinX && sprite.X <= bounds[0].MaxX && sprite.Z >= bounds[0].MinZ && sprite.Z <= bounds[0].MaxZ ) );
		Assert.IsTrue( bounds[0].MaxX - bounds[0].MinX > 100, "the spawn volume is +-100 around the emitter" );
		Assert.AreEqual( 64, bounds[0].MinY + (bounds[0].MaxY - bounds[0].MinY) );
	}

	// ---- conversion of the engine pose to the native units ----

	[TestMethod]
	public void EnginePositionAndDirectionConvertBackToNativeUnits()
	{
		var level = (Level)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		var native = ParticleRendererModule.ToNative( level, new System.Numerics.Vector3( 2f, 4f, 1f ) );
		// no heightfield: engine (x, y, z up) = original (x, z, y up) * 0.2
		Assert.AreEqual( (10 * 1024, 5 * 1024, 20 * 1024), native, "engine z is the original height, engine y the original z" );
		var back = ParticleRendererModule.ToEngine( level, new System.Numerics.Vector3( 10, 5, 20 ) );
		Assert.AreEqual( new System.Numerics.Vector3( 2f, 4f, 1f ), back );
		Assert.AreEqual( (0, 1024, 0), ParticleRendererModule.ToNativeDirection( new System.Numerics.Vector3( 0, 0, 1 ) ), "up in the engine is +Y in the original" );
		Assert.AreEqual( (1024, 0, 0), ParticleRendererModule.ToNativeDirection( new System.Numerics.Vector3( 1, 0, 0 ) ) );
		Assert.AreEqual( (0, 0, -1024), ParticleRendererModule.ToNativeDirection( new System.Numerics.Vector3( 0, -1, 0 ) ) );
	}
}
