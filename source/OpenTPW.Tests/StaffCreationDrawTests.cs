using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A placed staff member takes the person base constructor's base-speed draw (tp.exe 0x004F89E1: R mod 5 into the speed table 0x0075C7F8) from the park's one world generator, right after
/// its thing id (0x00504B10 → 0x004F8940); the entertainer constructor then fixes the speed at the table's first entry (0x004D4340) and the guard constructor draws its own sprite variant
/// (0x004D5E02). There is no reseed with the thing id: that is the online avatar's constructor (0x004F7B20, 0x004F7B7E).
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
	public void AHiredMechanicDrawsItsBaseSpeedAndNothingElse()
	{
		var economy = PoolPark( out var world );
		var mirror = new WorldRng( world.State );
		var expected = GuestSpeed.BaseSpeeds[(int)mirror.Next( 5u )];
		var member = EconomyTestData.HireBest( economy, StaffType.Mechanic );
		Assert.AreEqual( (int)expected, member.BaseSpeed );
		Assert.AreEqual( mirror.State, world.State, "exactly one draw, no reseed" );
	}

	[TestMethod]
	public void AnEntertainerDrawsThenTakesTheTablesFirstSpeedAndAGuardDrawsItsVariantToo()
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
		var guardSpeed = GuestSpeed.BaseSpeeds[(int)check.Next( 5u )];
		var variantDraw = check.NextWord();
		Assert.AreEqual( (int)guardSpeed, guard.BaseSpeed );
		var banks = StaffSpriteRules.Banks( StaffType.Guard, economy.Settings.Theme )!.Banks.Count;
		Assert.AreEqual( (int)((variantDraw >> 2) % (uint)banks), guard.Variant );
		Assert.AreEqual( check.State, world.State, "two draws: the base speed and the variant" );
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
