using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>A guest's creation draws, in the original's order (tp.exe 0x004F8940, 0x004FAEC0), and the reseeds of its appearance changes (DET-014).</summary>
[TestClass]
public class GuestCreationDrawTests
{
	// ---- Guests: the creation draws ---------------------------------------------------------------

	/// <summary>
	/// A guest made at an arrival entrance draws, in the original's order: the base constructor's speed index (R mod 5, 0x004F89E1), the eight starting values (0x004FAFF8..0x004FB114),
	/// then the reseed with its id (0x004FB19E), the sprite variant (0x00541F85), and the entrance side (R and 1) and the sideways byte (0x004FB201, 0x004FB21C).
	/// </summary>
	[TestMethod]
	public void ANewArrivalDrawsInTheOrderOfTheOriginalsConstructor()
	{
		var settings = GuestTests.CreateSettings();
		var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 3 ) { ArrivalsEnabled = false };
		var mirror = new WorldRng( simulation.World.State );
		var guest = simulation.SpawnArrival()!;

		var speed = GuestSpeed.BaseSpeeds[(int)(mirror.NextWord() % 5)];
		var start = GuestStart.FromDraws( settings.Types, settings.ExitLevelSeconds, settings.ExitLevelVarSeconds, settings.StartingCashVarPercent, () => mirror.NextWord(), settings.PrankeryLikelihood, guest.Id );
		mirror.Reseed( (ushort)guest.Id );
		mirror.NextWord();   // the sprite variant
		var side = mirror.NextWord() & 1;
		var sideways = mirror.NextWord() & 0xFF;

		Assert.AreEqual( speed, guest.BaseSpeed );
		Assert.AreEqual( (start.Type, start.Money, start.ExitLevel), (guest.Type, guest.Money, (int)guest.ExitLevel) );
		// 0x004CF720: the guest is made at the centre of the bus stop B cell (no vehicle at work: the bus's); 0x004D8710: R & 1 picks the crossing cell, the X of side A's or side B's
		// with side A's row, and the walk there (0x004FA5F0) ends at the sideways byte and 0xC8
		Assert.IsNull( guest.Lane );
		Assert.AreEqual( GuestNavigation.Centre( settings.ArrivalLaneB![0] ), (guest.Navigation.X, guest.Navigation.Y), "made at the bus stop B cell's centre" );
		var crossing = (X: (side == 0 ? settings.ArrivalLaneA! : settings.ArrivalLaneB!)[1].X, Y: settings.ArrivalLaneA![1].Y);
		Assert.AreEqual( GuestNavigation.BytePoint( crossing, (int)sideways, 0xC8 ), (guest.Navigation.TargetX, guest.Navigation.TargetY ), "R & 1 picks the crossing cell, the sideways byte and 0xC8 the point" );
		Assert.AreEqual( GuestState.CrossingRoad, guest.State, "a route to it makes state 0" );
		Assert.AreEqual( mirror.State, simulation.World.State, "no other draw" );
	}

	/// <summary>A guest made on a path cell (the park's own start, tests, imports) takes the construction draws and the variant, not the entrance's two.</summary>
	[TestMethod]
	public void AGuestPlacedOnAPathTakesNoEntranceDraws()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var mirror = new WorldRng( simulation.World.State );
		var guest = simulation.SpawnInPark( 10, 0 );
		mirror.NextWord();
		var settings = GuestTests.CreateSettings();
		GuestStart.FromDraws( settings.Types, settings.ExitLevelSeconds, settings.ExitLevelVarSeconds, settings.StartingCashVarPercent, () => mirror.NextWord(), settings.PrankeryLikelihood, guest.Id );
		mirror.Reseed( (ushort)guest.Id );
		mirror.NextWord();
		Assert.AreEqual( mirror.State, simulation.World.State );
	}

	/// <summary>
	/// The reseed takes the thing's id, a slot of the park's pool (0x0050B350 returns the u16 at +0): a guest built after the world's twelve first things is thing 13, and the generator
	/// continues from that id alone.
	/// </summary>
	[TestMethod]
	public void TheReseedTakesTheGuestsThingIdFromThePool()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var pool = new ThingPool();
		pool.AllocateWorldSetup();
		pool.Allocate( SaveThingModel.Attraction );
		pool.Allocate( SaveThingModel.Attraction );
		simulation.AttachThings( pool );
		var mirror = new WorldRng( simulation.World.State );
		var guest = simulation.SpawnInPark( 10, 0 );
		Assert.AreEqual( 13, guest.Id, "ids 1 to 12 are the world's singletons, the gates and the lights" );
		Assert.AreEqual( SaveThingModel.Guest, pool.ModelOf( 13 ) );
		mirror.NextWord();
		var settings = GuestTests.CreateSettings();
		GuestStart.FromDraws( settings.Types, settings.ExitLevelSeconds, settings.ExitLevelVarSeconds, settings.StartingCashVarPercent, () => mirror.NextWord(), settings.PrankeryLikelihood, 13 );
		mirror.Reseed( 13 );
		mirror.NextWord();
		Assert.AreEqual( mirror.State, simulation.World.State, "the stream after the guest's creation depends on thing id 13 alone" );
	}

	/// <summary>The balloon shop reseeds with the guest's id and draws the balloon's sprite set and colour; a costume returned reseeds the same way (0x004FE6FC, 0x004FE6A1).</summary>
	[TestMethod]
	public void AppearanceChangesReseedWithTheGuestsId()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 10, 0 );
		var apply = typeof( GuestSimulation ).GetMethod( "ApplyAppearance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance )!;
		var mirror = new WorldRng( simulation.World.State );
		apply.Invoke( simulation, new object[] { guest, GuestAppearance.Balloon, 50 } );
		mirror.Reseed( (ushort)guest.Id );
		mirror.NextWord();   // the set, 0x00541F70 with 10
		mirror.NextWord();   // the colour, 0x00541FD0
		Assert.AreEqual( mirror.State, simulation.World.State, "a balloon: the reseed, the set and the colour draws" );
		Assert.IsTrue( guest.HasBalloon );
		apply.Invoke( simulation, new object[] { guest, GuestAppearance.Costume, 50 } );
		mirror.NextWord();
		Assert.AreEqual( mirror.State, simulation.World.State, "a costume put on draws the costume's bank without a reseed" );
		apply.Invoke( simulation, new object[] { guest, GuestAppearance.Costume, 50 } );
		mirror.Reseed( (ushort)guest.Id );
		mirror.NextWord();
		Assert.AreEqual( mirror.State, simulation.World.State, "a costume returned reseeds and draws the guest's own bank" );
	}
}
