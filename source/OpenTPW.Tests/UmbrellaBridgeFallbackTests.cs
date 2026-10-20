using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class UmbrellaBridgeFallbackTests
{
	[TestMethod]
	public void AnUnanchoredUmbrellaUsesElevatedFeetAndPreservesTheTerrainFallback()
	{
		var previous = Entity.All;
		Entity.All = new();
		try
		{
			var field = new ModelHeightfield( 10, 10, 10, 10, 0, 0, Array.Empty<uint>(), new float[121], new uint[100] );
			var renderer = new UmbrellaRenderer( null!, field, Array.Empty<GuestSpriteAtlas>(), Array.Empty<GuestRenderer>(), new UmbrellaAnchors() );
			var guest = new Guest( 1, 0 );
			var terrainFeet = GuestRenderer.CellToEngine( field, guest.DrawX, guest.DrawY );
			var mount = new GuestMount( terrainFeet, .8f, "unanchored-custom-set", new GuestAtlasFrame( 0, 0, 1, 1, 0, 0 ), 0, 0, 0, false );
			var eye = new SVector3( 0, -100, 10 );
			var forward = SVector3.UnitY;
			var legacy = renderer.Place( guest, mount, SVector3.UnitX, SVector3.UnitZ, forward, eye );
			var ordinary = renderer.Place( guest, mount, SVector3.UnitX, SVector3.UnitZ, forward, eye, _ => terrainFeet );
			Assert.AreEqual( legacy, ordinary, "An ordinary feet callback keeps the existing fallback placement." );
			var elevated = renderer.Place( guest, mount, SVector3.UnitX, SVector3.UnitZ, forward, eye, _ => terrainFeet + SVector3.UnitZ * 2 );
			Assert.AreEqual( ordinary.Hotspot.X, elevated.Hotspot.X );
			Assert.AreEqual( ordinary.Hotspot.Y, elevated.Hotspot.Y );
			var handDepth = SVector3.Dot( terrainFeet - eye, forward ) * mount.Share;
			var pullShare = UmbrellaRenderer.PullShare( handDepth );
			Assert.AreEqual( 2 * mount.Share * (1 - pullShare), elevated.Hotspot.Z - ordinary.Hotspot.Z, .00001f );
			Assert.AreEqual( ordinary.Scale, elevated.Scale );
			Assert.AreEqual( mount.Share * (1 - pullShare) * UmbrellaRenderer.SizeScale, ordinary.Scale );
			var anchors = UmbrellaAnchors.Parse( """{"schema":1,"pixelsPerUnit":128,"sets":{"SPR_X":{"stand":{"slot":0,"frames":[[[4,-8]],[[3,-7]],[[1,-6]],[[-3,-7]],[[-4,-8]]]}}}}""", 128 );
			var anchoredRenderer = new UmbrellaRenderer( null!, field, Array.Empty<GuestSpriteAtlas>(), Array.Empty<GuestRenderer>(), anchors );
			var anchoredMount = mount with { SetName = "SPR_X" };
			var anchored = anchoredRenderer.Place( guest, anchoredMount, SVector3.UnitX, SVector3.UnitZ, forward, eye );
			var unchanged = anchoredRenderer.Place( guest, anchoredMount, SVector3.UnitX, SVector3.UnitZ, forward, eye,
				_ => throw new InvalidOperationException( "The anchored path must not call the fallback feet chooser." ) );
			Assert.AreEqual( anchored, unchanged );
		}
		finally
		{
			// Empty-atlas renderers allocate no resources or subscriptions; GPU deletion needs a graphics host.
			Entity.All = previous;
		}
	}
}
