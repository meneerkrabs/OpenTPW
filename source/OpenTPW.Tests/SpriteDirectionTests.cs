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
	[DataRow( 22f, 8 )]      // the sectors are centred on the 45 degree steps of the yaw
	[DataRow( 23f, 7 )]
	[DataRow( 45f, 7 )]
	[DataRow( 67f, 7 )]
	[DataRow( 68f, 6 )]
	[DataRow( 90f, 6 )]
	[DataRow( 180f, 4 )]
	[DataRow( 292f, 2 )]
	[DataRow( 293f, 1 )]
	[DataRow( 337f, 1 )]
	[DataRow( 338f, 8 )]     // back to the first sector below a full turn
	[DataRow( 359f, 8 )]
	public void CameraSectorsAreCentredOnTheYawSteps( float degrees, int sector )
	{
		Assert.AreEqual( sector, GuestSpriteAtlas.CameraSector( degrees * Degrees ) );
	}

	[TestMethod]
	public void AnotherCamerasNegativeYawIsTruncatedTowardsZero()
	{
		// the walk and ride cameras keep the arc tangent (-180 to 180 degrees): the term (yaw + 22.5) / 45 between -1 and 0 truncates to 0
		Assert.AreEqual( 8, GuestSpriteAtlas.CameraSector( -30f * Degrees ) );
		Assert.AreEqual( 8, GuestSpriteAtlas.CameraSector( -60f * Degrees ) );
		Assert.AreEqual( 1, GuestSpriteAtlas.CameraSector( -68f * Degrees ) );
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
	public void ARotatedCameraTurnsTheSpritesWithTheWorld()
	{
		// camera yaw 45 degrees looks towards +X and +Y: a walker along +Y moves up and to the left on the screen (the away-left frame, mirrored), a walker along +X up and to the right
		Assert.AreEqual( (1, true), GuestSpriteAtlas.SelectOriginalDirection( 0f, 1f, 45f * Degrees, 5 ) );
		Assert.AreEqual( (1, false), GuestSpriteAtlas.SelectOriginalDirection( 1f, 0f, 45f * Degrees, 5 ) );
		// a quarter turn: the walker along +X now walks straight away from the camera, the one along +Y goes left
		Assert.AreEqual( (0, false), GuestSpriteAtlas.SelectOriginalDirection( 1f, 0f, 90f * Degrees, 5 ) );
		Assert.AreEqual( (2, true), GuestSpriteAtlas.SelectOriginalDirection( 0f, 1f, 90f * Degrees, 5 ) );
	}

	[TestMethod]
	public void OnTheYawStepsTheSectorRuleAgreesWithTheExactCameraRelativeAngle()
	{
		// at a multiple of 45 degrees heading and yaw are both on their sector centres, so quantising them separately equals quantising the camera-relative angle: figures never walk backwards on the screen
		for ( var step = 0; step < 8; step++ )
		{
			var yaw = step * MathF.PI / 4;
			var (rightX, rightY, forwardX, forwardY) = (MathF.Cos( yaw ), -MathF.Sin( yaw ), MathF.Sin( yaw ), MathF.Cos( yaw ));
			for ( var sector = 0; sector < 8; sector++ )
			{
				var angle = sector * MathF.PI / 4;
				var (x, y) = (MathF.Sin( angle ), MathF.Cos( angle ));
				var exact = GuestSpriteAtlas.SelectDirection( x * rightX + y * rightY, x * forwardX + y * forwardY );
				Assert.AreEqual( exact, GuestSpriteAtlas.SelectOriginalDirection( x, y, yaw, 5 ), $"yaw {step * 45}, heading sector {sector}" );
			}
		}
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
