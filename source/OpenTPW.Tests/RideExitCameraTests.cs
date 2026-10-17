using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using V2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>What leaving the ride view does to the park camera (tp.exe 0x42a190, 0x42a1d0, 0x42a340; UI-095).</summary>
[TestClass, DoNotParallelize]
public class RideExitCameraTests
{
	private RideUpgradeIngressTests fixture = null!;
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

	[TestInitialize]
	public void Setup() { fixture = new(); fixture.Setup(); }
	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();

	private OriginalObject Owner( BuildCategory category, string extra )
	{
		object?[] args = { category, null, null, null, null, null, ParkGameMode.FullSimulation, extra };
		typeof( RideUpgradeIngressTests ).GetMethod( "Fixture", Private )!.Invoke( fixture, args );
		return (OriginalObject)args[3]!;
	}

	[TestMethod]
	public void OnlyAnObjectWhoseTypeHasCannotRideZeroIsTheExitThing()
	{
		// 0x42a340: type byte 3 and type record +0x118 (UsageInfo.CannotRide) == 0: the shipped rides, not the shops, sideshows and features, which set it to 1
		Assert.IsTrue( Level.IsRideExitThing( Owner( BuildCategory.Rides, "UsageInfo.CannotRide 0\n" ) ) );
		Assert.IsFalse( Level.IsRideExitThing( Owner( BuildCategory.Shops, "UsageInfo.CannotRide 1\n" ) ) );
		Assert.IsFalse( Level.IsRideExitThing( Owner( BuildCategory.Sideshows, "UsageInfo.CannotRide 1\n" ) ) );
		var deleted = Owner( BuildCategory.Rides, "UsageInfo.CannotRide 0\n" );
		deleted.Delete();
		Assert.IsFalse( Level.IsRideExitThing( deleted ), "a deleted object is no thing" );
	}

	[DataTestMethod]
	[DataRow( 1, 0 ), DataRow( 4, 1 ), DataRow( 0x10, 2 ), DataRow( 0x40, 3 )]
	[DataRow( 0, -1 ), DataRow( 5, -1 ), DataRow( 0x55, -1 ), DataRow( 2, -1 ), DataRow( 0xA0, -1 ), DataRow( 0x11, -1 )]
	public void TheExitsYawRuleIsASwitchOnTheLinkByteMaskedWith0x55WithNoDefault( int link, int direction )
	{
		Assert.AreEqual( direction, Level.RideExitDirection( link ) );
	}

	[TestMethod]
	public void TheLinkBitsLookAlongTheirOwnDirectionInTheOriginalsYaw()
	{
		// yaw π (bit 1), π/2 (4), 0 (0x10), 3π/2 (0x40) turn the original's camera to look along (sin yaw, cos yaw) in (x, z): -y, +x, +y, -x = GuestPathGrid.Directions
		foreach (var (link, yaw) in new[] { (1, MathF.PI), (4, MathF.PI / 2), (0x10, 0f), (0x40, 3 * MathF.PI / 2) })
		{
			var (dx, dy) = GuestPathGrid.Directions[Level.RideExitDirection( link )];
			Assert.AreEqual( MathF.Sin( yaw ), dx, 1e-5f, $"link {link} x" );
			Assert.AreEqual( MathF.Cos( yaw ), dy, 1e-5f, $"link {link} y" );
		}
	}

	[TestMethod]
	public void LookAlongPutsTheEyeBehindTheTargetAndKeepsTheTargetAndHeight()
	{
		var camera = new ParkCameraMode();
		camera.SetPose( new Vector3( 10, 20, 0 ), 90, -135 );
		camera.LookAlong( new V2( 1, 0 ) );
		Assert.AreEqual( new Vector3( 10, 20, 0 ), camera.Target );
		Assert.AreEqual( 90f, camera.Height );
		Assert.IsTrue( camera.Position.X < 10 && Math.Abs( camera.Position.Y - 20 ) < 1e-3f, "the eye is on the -X side, so the view runs along +X" );
		camera.LookAlong( new V2( 0, -1 ) );
		Assert.IsTrue( camera.Position.Y > 20 && Math.Abs( camera.Position.X - 10 ) < 1e-3f );
		var yaw = camera.Yaw;
		camera.LookAlong( V2.Zero );
		Assert.AreEqual( yaw, camera.Yaw, "no direction, no turn" );
	}
}
