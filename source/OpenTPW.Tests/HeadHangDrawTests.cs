using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The head-hang draw of a theme without costume heads (tp.exe 0x0044B410 → 0x004FCAC0): every head hung on a ride reseeds the world generator with the rider's thing id and takes one draw.
/// </summary>
[TestClass]
public class HeadHangDrawTests
{
	private static ulong StateAfterReseedAndDraw( int visitor )
	{
		var generator = new WorldRng( 1 );
		generator.Reseed( (ushort)visitor );
		generator.NextWord();
		return generator.State;
	}

	[TestMethod]
	public void AHeadHungReseedsTheWorldGeneratorWithTheRiderAndDrawsOnceOnlyWithoutCostumeHeads()
	{
		var scripts = new RideScriptWorld( 5 );
		var before = scripts.World.State;
		scripts.HeadHung( 9 );
		Assert.AreEqual( before, scripts.World.State, "a theme with costume heads takes no draw" );
		scripts.HeadHangReseeds = true;
		scripts.HeadHung( 0 );
		Assert.AreEqual( before, scripts.World.State, "no rider: 0x0044B440 skips the pick" );
		scripts.HeadHung( 9 );
		Assert.AreEqual( StateAfterReseedAndDraw( 9 ), scripts.World.State );
		scripts.HeadHung( 0x10009 );
		Assert.AreEqual( StateAfterReseedAndDraw( 9 ), scripts.World.State, "the id is zero-extended to 16 bits, so 0x10009 reseeds as 9" );
	}

	[TestMethod]
	public void ADDHEADAndWALKONHangThroughTheHook()
	{
		var hung = new List<int>();
		var space = RideHeadSpace.Of( new[] { 3, 4, 5 }, () => 0u );
		space.HeadHung = hung.Add;
		space.Add( 41 );
		Assert.IsTrue( space.Hang( 2, 42 ) );
		Assert.IsFalse( space.Hang( 9, 43 ), "no head record of that id: nothing hangs" );
		CollectionAssert.AreEqual( new[] { 41, 42 }, hung );
	}

	[TestMethod]
	public void ATourBoardingHangsTheRidersHeadOnlyWhenTheCarMeshHasHeadNodes()
	{
		foreach ( var (nodes, reseeds) in new[] { (0, true), (3, true), (3, false) } )
		{
			var scripts = new RideScriptWorld( 5 ) { HeadHangReseeds = reseeds };
			var before = scripts.World.State;
			var controller = new TourController( TourStation.Default, scripts.NextWorldDraw, scripts.Tours ) { HeadNodes = nodes };
			var afterCreate = scripts.World.State;
			Assert.IsTrue( controller.Board( 7 ) );
			if ( nodes > 0 && reseeds )
				Assert.AreEqual( StateAfterReseedAndDraw( 7 ), scripts.World.State );
			else
				Assert.AreEqual( afterCreate, scripts.World.State );
			_ = before;
		}
	}
}
