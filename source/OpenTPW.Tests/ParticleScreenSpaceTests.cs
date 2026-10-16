using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The screen-space particles: tp.exe's draw list 0x51F01A, quad builder 0x57C620 and quad rotation 0x591430. GPU free.</summary>
[TestClass]
public class ParticleScreenSpaceTests
{
	[TestInitialize]
	public void Init() => Log = new();

	
	[TestMethod]
	public void ANativeKeyPositionLandsAtTheTopRightOfTheScreen()
	{
		// [BIN:TPW-EXE:0x51F0C0] x = ((px - 0xC35) << 4) / 49, y = ((pz << 4) - 0x927C) / 37, then / 1024: native (90000, 0, 7000) is stored >> 4 as (5625, 437)
		var native = (X: 90000, Y: 0, Z: 7000);
		var screen = ParticleScreenSpace.Position( native.X >> 4, native.Z >> 4 );
		Assert.AreEqual( 816 / 1024f, screen.X, "40000 / 49 = 816" );
		Assert.AreEqual( -824 / 1024f, screen.Y, "-30508 / 37 truncates to -824 (y down: near the top)" );
		var centre = ParticleScreenSpace.Position( ParticleScreenSpace.CentreX >> 4, ParticleScreenSpace.CentreZ >> 4 );
		Assert.AreEqual( 0f, centre.X );
		Assert.AreEqual( 0f, centre.Y, "the native centre (50000, 37500) is the screen's" );
		Assert.AreEqual( -0.8046875f, ParticleScreenSpace.ToClip( screen ).Y * -1f, "clip space has y up" );
	}

	[TestMethod]
	public void AScreenSpaceQuadIsSizedInSixtyFourthsAndTurnsInStepsOfOne256thTurn()
	{
		// [BIN:TPW-EXE:0x51F112] size >> 6 (truncating), [BIN:TPW-EXE:0x57C66A] half-height = word / 1024 * 0.5
		Assert.AreEqual( 78 / 1024f * 0.5f, ParticleScreenSpace.HalfHeight( 5000 ) );
		Assert.AreEqual( 0f, ParticleScreenSpace.HalfHeight( 63 ) );
		var centre = new System.Numerics.Vector2( 0.5f, -0.25f );
		var (a, b, c, d) = ParticleScreenSpace.Corners( centre, 0.1f, 0.2f, 0 );
		foreach ( var (expected, actual) in new[] { ((0.4f, -0.45f), a), ((0.6f, -0.45f), b), ((0.6f, -0.05f), c), ((0.4f, -0.05f), d) } )
		{
			Assert.AreEqual( expected.Item1, actual.X, 1e-6f );
			Assert.AreEqual( expected.Item2, actual.Y, 1e-6f );
		}
		// [BIN:TPW-EXE:0x591430] a quarter turn (spin 0x4000, table index 64): (x, y) -> (y, -x) about the centre
		var (qa, _, _, _) = ParticleScreenSpace.Corners( System.Numerics.Vector2.Zero, 0.1f, 0.2f, 0x4000 );
		Assert.AreEqual( -0.2f, qa.X, 1e-6f );
		Assert.AreEqual( 0.1f, qa.Y, 1e-6f );
		var (fine, _, _, _) = ParticleScreenSpace.Corners( System.Numerics.Vector2.Zero, 0.1f, 0.2f, 255 );
		Assert.AreEqual( new System.Numerics.Vector2( -0.1f, -0.2f ), fine, "less than 1/256 turn does not turn" );
	}

	[TestMethod]
	public void TheSnapshotMarksScreenSpaceParticles()
	{
		var world = ParticleSystemTests.Template( ParticleSystemTests.Life( 10 ), ParticleSystemTests.Burst( 1 ), ParticleSystemTests.Cap( 10 ), ParticleSystemTests.ParticleLife( 10 ) );
		var screen = ParticleSystemTests.Template( (5, 1, 1), ParticleSystemTests.Life( 10 ), ParticleSystemTests.Burst( 1 ), ParticleSystemTests.Cap( 10 ), ParticleSystemTests.ParticleLife( 10 ) );
		Assert.IsTrue( screen.ScreenSpace );
		var system = ParticleSystemTests.New( ParticleSystemTests.Pool( (3, world), (87, screen) ) );
		system.Create( 3, (0, 0, 0) );
		system.Create( 87, (90000, 0, 7000) );
		var sprites = new List<ParticleSprite>();
		system.Snapshot( sprites );
		Assert.IsTrue( sprites.Any( sprite => sprite.ScreenSpace && sprite.X == 5625 && sprite.Z == 437 ) );
		Assert.IsTrue( sprites.Any( sprite => !sprite.ScreenSpace ) );
	}
}
