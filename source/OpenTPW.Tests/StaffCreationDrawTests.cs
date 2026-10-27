using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A placed staff member consumes the person base constructor's base-speed draw (tp.exe 0x004F89E1) from the park's one world generator, right after its thing id (0x00504B10 → 0x004F8940),
/// and does not keep it: 0x00504B90 sets the speed to 60 below grade 3 and 100 from grade 3 (0x00504C20), the entertainer constructor fixes 60 (0x004D43EE), and the guard constructor draws
/// its own sprite variant (0x004D5E02). There is no reseed with the thing id: that is the online avatar's constructor (0x004F7B20, 0x004F7B7E).
/// </summary>
[TestClass]
public class StaffCreationDrawTests
{
	private static ParkEconomy PoolPark( out WorldRng world )
	{
		var economy = EconomyTestData.Park();
		var pool = new ThingPool();
		pool.AllocateWorldSetup();
		economy.Staff.AttachThings( pool );
		world = economy.World;
		return economy;
	}

	[TestMethod]
	public void AHiredMechanicConsumesTheSpeedDrawAndTakesTheGradeSpeed()
	{
		var economy = PoolPark( out var world );
		var mirror = new WorldRng( world.State );
		mirror.NextWord();
		var member = EconomyTestData.HireBest( economy, StaffType.Mechanic );
		Assert.AreEqual( (int)(member.Grade < 3 ? GuestSpeed.BaseSpeeds[0] : GuestSpeed.BaseSpeeds[2]), member.BaseSpeed, "60 below grade 3, else 100, not the draw" );
		Assert.AreEqual( mirror.State, world.State, "exactly one draw, no reseed" );
	}

	[TestMethod]
	public void AnEntertainerIsFixedAtSixtyAndAGuardDrawsItsVariantToo()
	{
		var economy = PoolPark( out var world );
		var mirror = new WorldRng( world.State );
		mirror.NextWord();
		var entertainer = EconomyTestData.HireBest( economy, StaffType.Entertainer );
		Assert.AreEqual( (int)GuestSpeed.BaseSpeeds[0], entertainer.BaseSpeed );
		Assert.AreEqual( mirror.State, world.State );
		var before = world.State;
		var guard = EconomyTestData.HireBest( economy, StaffType.Guard );
		var check = new WorldRng( before );
		check.NextWord();
		var variantDraw = check.NextWord();
		var banks = StaffSpriteRules.Banks( StaffType.Guard, economy.Settings.Theme )!.Banks.Count;
		Assert.AreEqual( (int)((variantDraw >> 2) % (uint)banks), guard.Variant );
		Assert.AreEqual( check.State, world.State, "two draws: the speed and the variant" );
	}

	[TestMethod]
	public void AParkWithoutAThingPoolTakesNoDraw()
	{
		var economy = EconomyTestData.Park();
		var before = economy.World.State;
		EconomyTestData.HireBest( economy, StaffType.Handyman );
		Assert.AreEqual( before, economy.World.State );
	}
}
