using System;
using System.Linq;
using SVector3 = System.Numerics.Vector3;
using SVector4 = System.Numerics.Vector4;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The original's depth pull of a visitor's or a staff member's sprite (tp.exe 0x00589B50: the clip z of a sprite flagged 0x200 becomes z - 0.2 / (z + 1)), which keeps the feet
/// that hang below the hotspot from being cut by the ground in front of them. OpenTPW moves the quad along its view rays instead (<see cref="SpriteScale.PulledDepthShare"/>).
/// </summary>
[TestClass]
public class SpriteDepthPullTests
{
	/// <summary>The clip z and w of a view depth under the original's projection (0x00578BE0 at 90 degrees, near 0.1, far 1000), in double precision.</summary>
	private static (double Z, double W) OriginalClip( double viewDepth )
	{
		var s = Math.Sin( Math.PI / 4 );
		var a = s / (1 - 0.1 / 1000);
		return (a * (viewDepth - 0.1), s * viewDepth);
	}

	[TestMethod]
	public void ThePulledViewDepthHasTheClipDepthTheSpritePassWrites()
	{
		foreach ( var viewDepth in new[] { 1.0, 5, 20, 45, 70, 100, 160, 250, 600 } )
		{
			var (z, w) = OriginalClip( viewDepth );
			var unpulled = z / w;
			var original = (z - 0.2 / (z + 1)) / w;
			var (pulledZ, pulledW) = OriginalClip( viewDepth * SpriteScale.PulledDepthShare( (float)viewDepth ) );
			Assert.AreEqual( original, pulledZ / pulledW, 1e-3 * (unpulled - original), $"view depth {viewDepth}" );
		}
		Assert.AreEqual( 1f, SpriteScale.PulledDepthShare( 0.05f ), "a hotspot before the near plane is not moved" );
	}

	[TestMethod]
	public void InTheParkViewThePullIsAboutFourUnitsAndClearsTheFeet()
	{
		// the park camera's eye is the zoom value (70 to 130 units) from its target at a pitch of 45 to 65 degrees (0x42B1C0), so the sprites in view are about 40 to 250 units deep
		for ( var viewDepth = 40f; viewDepth <= 250f; viewDepth += 5f )
		{
			var pull = viewDepth * (1f - SpriteScale.PulledDepthShare( viewDepth ));
			Assert.IsTrue( pull is > 3.5f and < 3.95f, $"view depth {viewDepth}: pull {pull}" );
		}
		// a sole h pixels below the hotspot lies over ground h / tan(pitch) nearer the camera than the quad: at 45 degrees the nearest sprites clear 16 pixels, the new kid sets reach 9
		var cleared = 40f * (1f - SpriteScale.PulledDepthShare( 40f )) / SpriteScale.BillboardPixel;
		Assert.IsTrue( cleared > 15.9f, $"{cleared} pixels" );
		// the order of depths is kept, so the nearer of two sprites stays in front
		var shares = Enumerable.Range( 1, 400 ).Select( depth => depth * SpriteScale.PulledDepthShare( depth ) ).ToArray();
		Assert.IsTrue( shares.Zip( shares.Skip( 1 ) ).All( pair => pair.First < pair.Second ) );
	}

	[TestMethod]
	public void APulledQuadCoversTheSamePixelsNearerTheCamera()
	{
		var eye = new SVector3( 12f, 30f, -20f );
		var view = Matrix4x4.CreateLookAt( eye, new SVector3( 0f, 0f, 0f ), SVector3.UnitZ );
		var projection = OriginalProjection.Create( 16f / 9f );
		var right = new SVector3( view.M11, view.M21, view.M31 );
		var up = new SVector3( view.M12, view.M22, view.M32 );
		var forward = -new SVector3( view.M13, view.M23, view.M33 );
		Assert.AreEqual( 0f, SVector3.Distance( eye, GuestRenderer.Eye( view ) ), 1e-4f );
		var feet = new SVector3( 1.5f, -2f, 0.3f );
		var (pulled, share) = GuestRenderer.PullTowardsEye( feet, eye, forward );
		var depth = SVector3.Dot( feet - eye, forward );
		Assert.AreEqual( SpriteScale.PulledDepthShare( depth / ObjectPlacement.ModelScale ), share, 1e-6f );
		Assert.AreEqual( depth * share, SVector3.Dot( pulled - eye, forward ), 1e-4f, "the hotspot is nearer by the pull" );
		foreach ( var (x, y) in new[] { (-0.5f, 1.8f), (0.6f, 1.8f), (-0.5f, -0.2f), (0.6f, -0.2f) } )
		{
			var corner = feet + right * x + up * y;
			var pulledCorner = pulled + (right * x + up * y) * share;
			var a = SVector4.Transform( new SVector4( corner, 1f ), view * projection );
			var b = SVector4.Transform( new SVector4( pulledCorner, 1f ), view * projection );
			Assert.AreEqual( a.X / a.W, b.X / b.W, 1e-5f, "the same pixel across" );
			Assert.AreEqual( a.Y / a.W, b.Y / b.W, 1e-5f, "the same pixel down" );
			Assert.IsTrue( b.Z / b.W < a.Z / a.W, "nearer in the depth buffer" );
		}
		Assert.AreEqual( (feet, 1f), GuestRenderer.PullTowardsEye( feet, feet + forward, forward ), "a hotspot behind the eye stays" );
	}

	[TestMethod]
	public void SwimmersArePulledOnlyWhereTheirHotspotIsTheFeet()
	{
		CollectionAssert.AreEquivalent( new[] { PoolSwimSlots.Stand, PoolSwimSlots.Walk, PoolSwimSlots.Shower }, PoolSwimSlots.FeetAnchored.ToArray() );
		Assert.IsFalse( PoolSwimSlots.FeetAnchored.Overlaps( PoolSwimSlots.BottomAnchored ), "the torso slots keep their depth" );
		Assert.IsFalse( PoolSwimSlots.FeetAnchored.Contains( PoolSwimSlots.Crawl ) || PoolSwimSlots.FeetAnchored.Contains( PoolSwimSlots.TreadWater ), "the strokes keep their depth at the water line" );
	}
}
