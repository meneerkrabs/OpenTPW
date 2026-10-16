using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The litter drop's and the handyman's sweep's reseeds of the world generator with the cell id (tp.exe 0x004D94E2, 0x004D71CA; DET-014, DET-015).</summary>
[TestClass]
public class LitterReseedTests
{
	// ---- Litter ------------------------------------------------------------------------------------

	/// <summary>Dropping litter reseeds the generator with the cell id and draws twice (0x004D94E2); the draws are the sprite offset and the park's stream keeps their state.</summary>
	[TestMethod]
	public void ALitterDropReseedsTheGeneratorWithTheCellAndKeepsItsState()
	{
		var rng = new WorldRng( 0xABCDEF01 );
		var litter = new ParkLitter { World = () => rng };
		Assert.IsTrue( litter.Drop( 20, 30, (LitterType)1 ) );
		var mirror = new WorldRng( 0 );
		var (x, z) = ParkLitter.SpriteOffset( mirror, 20, 30 );
		Assert.AreEqual( (2, 9), (x, z), "the offset of the cell, as before" );
		Assert.AreEqual( mirror.State, rng.State, "the stream continues from the state the two draws left" );
		var untouched = rng.State;
		Assert.IsFalse( litter.Drop( 20, 30, (LitterType)1 ), "a cell that holds litter takes nothing and draws nothing" );
		Assert.AreEqual( untouched, rng.State );
		Assert.AreEqual( ParkLitter.CellId( 20, 30 ), 30u * 128 + 20 + 1 );
	}

	[TestMethod]
	public void TheEconomysLitterDropsDrawFromTheParksGenerator()
	{
		var economy = EconomyTestData.Park();
		var before = economy.World.State;
		Assert.IsTrue( economy.Litter.Drop( 3, 4, (LitterType)1 ) );
		Assert.AreNotEqual( before, economy.World.State );
		var mirror = new WorldRng( 0 );
		ParkLitter.SpriteOffset( mirror, 3, 4 );
		Assert.AreEqual( mirror.State, economy.World.State );
	}

	[TestMethod]
	public void AHandymansSweepSpotTakesTheTwoDrawsAfterTheReseed()
	{
		var rng = new WorldRng( 99 );
		var spot = ParkLitter.SweepSpot( rng, 5, 6 );
		Assert.AreEqual( ParkLitter.SweepSpot( 5, 6 ), spot, "the spot depends on the cell alone" );
		var mirror = new WorldRng( 0 );
		ParkLitter.SweepSpot( mirror, 5, 6 );
		Assert.AreEqual( mirror.State, rng.State );
	}
}
