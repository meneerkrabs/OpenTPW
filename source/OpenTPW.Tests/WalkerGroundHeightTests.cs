using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>TOUR-011: the height a walking body stands at (tp.exe 0x4511A0 after 0x450EA0) and a level without its initialisers.</summary>
[TestClass]
public class WalkerGroundHeightTests
{
	private static TrackSurface Faces() => new( new[] { new TrackSurface.Triangle( new( 0, 0, 2 ), new( 10, 0, 2 ), new( 0, 10, 2 ) ) } );

	[TestMethod]
	public void AFaceUnderThePointGivesTheBaseHeightPlusTheFaceHeight()
	{
		var height = Level.WalkerGroundHeight( Faces(), Matrix4x4.Identity, 50f, 7f, 1f, 1f );
		Assert.AreEqual( 50f + 2f * ObjectPlacement.ModelScale, height, 1e-3f );
	}

	[TestMethod]
	public void APointNoFaceCoversIsTheSamplersMinusOneHundredWithoutTheBaseHeight()
	{
		// 0x451290 starts at -100.0 (0x4512A7) and 0x4511A0 adds +0xA0 (0, as the triangle-list surface has no header offset)
		var height = Level.WalkerGroundHeight( Faces(), Matrix4x4.Identity, 50f, 7f, 40f, 40f );
		Assert.AreEqual( -100f * ObjectPlacement.ModelScale, height, 1e-3f );
	}
}
