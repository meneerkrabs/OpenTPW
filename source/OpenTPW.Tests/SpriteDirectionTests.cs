using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// The original's sprite pass picks a sprite's direction from its heading sector and the camera's yaw sector and never reads the camera's pitch (tp.exe <c>0x00542010</c>, <c>0x004FA030</c>, <c>0x00540AB0</c>,
/// <c>0x0046F1C0</c>): the art of every direction is pre-rendered from one angle. Pure rules, no assets.
/// </summary>
[TestClass]
public class SpriteDirectionTests
{
	private const float Degrees = MathF.PI / 180f;

	[DataTestMethod]
	[DataRow( 0f, 1f, 0 )]    // +Y
	[DataRow( 1f, 1f, 1 )]    // towards +X and +Y
	[DataRow( 1f, 0f, 2 )]    // +X
	[DataRow( 0f, -1f, 4 )]   // -Y
	[DataRow( -1f, 0f, 6 )]   // -X
	[DataRow( -1f, 1f, 7 )]
	public void HeadingSectorsTurnFromPlusYTowardsPlusX( float x, float y, int sector )
	{
		Assert.AreEqual( sector, GuestSpriteAtlas.HeadingSector( x, y ) );
	}

	[TestMethod]
	public void HeadingSectorsAreCentredOnTheEightHeadings()
	{
		Assert.AreEqual( 0, GuestSpriteAtlas.HeadingSector( MathF.Sin( 22f * Degrees ), MathF.Cos( 22f * Degrees ) ) );
		Assert.AreEqual( 1, GuestSpriteAtlas.HeadingSector( MathF.Sin( 23f * Degrees ), MathF.Cos( 23f * Degrees ) ) );
		Assert.AreEqual( 7, GuestSpriteAtlas.HeadingSector( MathF.Sin( -23f * Degrees ), MathF.Cos( -23f * Degrees ) ) );
		Assert.AreEqual( 0, GuestSpriteAtlas.HeadingSector( 0f, 0f ) );
	}

	[DataTestMethod]
	[DataRow( 0f, 8 )]       // the home view: no turn
	[DataRow( 30f, 8 )]      // the truncation keeps every yaw from 0 to 67.5 degrees in the first sector
	[DataRow( 45f, 8 )]
	[DataRow( 67f, 8 )]
	[DataRow( 68f, 7 )]      // one sector from 67.5 degrees
	[DataRow( 90f, 7 )]
	[DataRow( 112f, 7 )]
	[DataRow( 113f, 6 )]
	[DataRow( 180f, 5 )]
	[DataRow( 337f, 2 )]
	[DataRow( 338f, 1 )]     // the last 22.5 degrees below a full turn are the last sector
	[DataRow( 359f, 1 )]
	public void CameraSectorsTruncateTheYaw( float degrees, int sector )
	{
		Assert.AreEqual( sector, GuestSpriteAtlas.CameraSector( degrees * Degrees ) );
	}

	[TestMethod]
	public void TheParkCameraWrapsItsYawAndOtherCamerasKeepTheirs()
	{
		var forward = new Vector3( MathF.Sin( -30f * Degrees ), MathF.Cos( -30f * Degrees ), -0.5f );
		var right = new Vector3( MathF.Cos( -30f * Degrees ), -MathF.Sin( -30f * Degrees ), 0f );
		Assert.AreEqual( 330f * Degrees, GuestSpriteAtlas.CameraYaw( right, forward, parkCamera: true ), 1e-4f );
		Assert.AreEqual( -30f * Degrees, GuestSpriteAtlas.CameraYaw( right, forward, parkCamera: false ), 1e-4f );
		// looking steeply down the yaw comes from the right vector, as 0x0046F1C0 does
		var down = new Vector3( 0.05f, 0.05f, -0.99f );
		var rightWhenDown = new Vector3( MathF.Cos( 100f * Degrees ), -MathF.Sin( 100f * Degrees ), 0f );
		Assert.AreEqual( 100f * Degrees, GuestSpriteAtlas.CameraYaw( rightWhenDown, down, parkCamera: false ), 1e-4f );
	}

	[TestMethod]
	public void TheHomeCameraShowsTheHeadingsAsTheArtStoresThem()
	{
		// camera yaw 0 (the Home pose looks along +Y): away = 0, right = 2, towards = 4; left and the diagonals on the left are the mirrored right-hand frames
		(float X, float Y, int Direction, bool Mirror)[] cases = { (0, 1, 0, false), (1, 1, 1, false), (1, 0, 2, false), (1, -1, 3, false), (0, -1, 4, false), (-1, -1, 3, true), (-1, 0, 2, true), (-1, 1, 1, true) };
		foreach ( var (x, y, direction, mirror) in cases )
			Assert.AreEqual( (direction, mirror), GuestSpriteAtlas.SelectOriginalDirection( x, y, 0f, 5 ), $"heading {x},{y}" );
	}

	[TestMethod]
	public void ARotatedCameraStillShowsTheSpritesAsAtYawZeroUpToSixtySevenAndAHalfDegrees()
	{
		// the reported difference: at a yaw of 45 degrees a walker along +Y was drawn from three quarters (exact: 45 degrees from the view), the original shows the back
		Assert.AreEqual( (0, false), GuestSpriteAtlas.SelectOriginalDirection( 0f, 1f, 45f * Degrees, 5 ) );
		Assert.AreEqual( (2, false), GuestSpriteAtlas.SelectOriginalDirection( 1f, 0f, 45f * Degrees, 5 ) );
		// from 67.5 degrees on the camera turns the sprites a sector at a time, and a quarter turn leaves them 45 degrees off the exact angle
		Assert.AreEqual( (1, true), GuestSpriteAtlas.SelectOriginalDirection( 0f, 1f, 90f * Degrees, 5 ) );
	}

	[TestMethod]
	public void ASlotWithoutDirectionsAlwaysShowsDirectionZero()
	{
		Assert.AreEqual( (0, false), GuestSpriteAtlas.SelectOriginalDirection( 1f, 0f, 0f, 0 ) );
	}

	[TestMethod]
	public void AnEightDirectionSlotNeedsNoMirroring()
	{
		for ( var sector = 0; sector < 8; sector++ )
		{
			var angle = sector * MathF.PI / 4;
			Assert.AreEqual( (sector, false), GuestSpriteAtlas.SelectOriginalDirection( MathF.Sin( angle ), MathF.Cos( angle ), 0f, 8 ) );
		}
	}

	[TestMethod]
	public void ThePitchOfTheCameraDoesNotChangeTheDirection()
	{
		foreach ( var pitch in new[] { 45f, 58f, 65f } )
		{
			var yaw = 20f * Degrees;
			var forward = new Vector3( MathF.Sin( yaw ) * MathF.Cos( pitch * Degrees ), MathF.Cos( yaw ) * MathF.Cos( pitch * Degrees ), -MathF.Sin( pitch * Degrees ) );
			var right = new Vector3( MathF.Cos( yaw ), -MathF.Sin( yaw ), 0f );
			Assert.AreEqual( 20f * Degrees, GuestSpriteAtlas.CameraYaw( right, forward, true ), 1e-4f );
			Assert.AreEqual( (2, false), GuestSpriteAtlas.SelectOriginalDirection( 1f, 0f, GuestSpriteAtlas.CameraYaw( right, forward, true ), 5 ) );
		}
	}
}
