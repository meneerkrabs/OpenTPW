using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
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

	private static StaffWorldSimulation AttachStaffWorld( ParkEconomy economy, out StaffPlacement placement )
	{
		var grid = new GuestPathGrid( 20, 20 );
		for ( var x = 0; x < 16; x++ )
			grid.SetPath( x, 5, true );
		economy.OpenPark();
		economy.Litter.Cells = grid.Cells;
		var world = new StaffWorldSimulation( grid, () => economy.Staff.Members, _ => null, (0, 5) );
		economy.StaffWorld = world;
		placement = StaffPlacementRig.Create( () => economy, () => grid.Cells, (_, _) => false );
		return world;
	}

	private static ParkEconomy RoundTrip( ParkEconomy economy )
	{
		var json = ParkSaveFile.Serialize( economy );
		Assert.IsFalse( json.Contains( nameof( StaffMember.HasInitializedBaseSpeed ) ), "the initialization latch is not a new save field" );
		return ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
	}

	[TestMethod]
	public void PlacementFirstUpdateAndPickUpDropKeepEveryRolesConstructorSpeed()
	{
		foreach ( var type in new[] { StaffType.Handyman, StaffType.Mechanic, StaffType.Entertainer, StaffType.Guard, StaffType.Researcher } )
		{
			var economy = PoolPark( out var random );
			var world = AttachStaffWorld( economy, out var placement );
			var candidate = economy.Staff.Candidates.First( item => item.Type == type );
			Assert.IsTrue( placement.BeginHire( candidate.Id ) );
			// the draw is consumed and not kept: 0x504B90 sets 60 below grade 3 and 100 from it (0x504C20), the entertainer constructor 60 (0x4D43EE)
			var expected = type == StaffType.Entertainer || candidate.Grade < 3 ? 60 : 100;
			Assert.AreEqual( StaffClickResult.Placed, placement.Click( 5, 5 ) );
			var member = placement.LastPlaced!;
			Assert.IsNull( member.Navigation, "the actual placement command leaves the first navigation update pending" );
			Assert.AreEqual( expected, member.BaseSpeed, type.ToString() );
			// Make the old grade fallback disagree, including a high-grade entertainer whose constructor fixes 60.
			member.Grade = expected == 100 ? 1 : 3;
			// Isolate placement from the legitimate search-time speed tier refresh above RestLevel.
			member.Energy = 0;
			world.UpdateStaff( economy, 1 );
			Assert.IsNotNull( member.Navigation );
			Assert.AreEqual( expected, member.BaseSpeed, $"{type}: first world update" );
			Assert.IsTrue( placement.PickUp( member ) );
			Assert.AreEqual( StaffClickResult.Dropped, placement.Click( 6, 5 ) );
			Assert.IsNull( member.Navigation );
			world.UpdateStaff( economy, 2 );
			Assert.AreEqual( expected, member.BaseSpeed, $"{type}: navigation recreated after drop" );
		}
	}

	[TestMethod]
	public void AnInitializedSpeedStillAllowsTheLegitimateSearchTimeEnergyRefresh()
	{
		var economy = PoolPark( out _ );
		var world = AttachStaffWorld( economy, out var placement );
		var candidate = economy.Staff.Candidates.First( item => item.Type == StaffType.Handyman );
		Assert.IsTrue( placement.BeginHire( candidate.Id ) );
		Assert.AreEqual( StaffClickResult.Placed, placement.Click( 5, 5 ) );
		var member = placement.LastPlaced!;
		member.BaseSpeed = 60;
		member.Energy = 100;
		world.UpdateStaff( economy, 1 );
		Assert.AreEqual( 140, member.BaseSpeed, "the initialization latch only guards Place, not the search-time energy tier" );
	}

	[TestMethod]
	public void AConstructorSpeedSurvivesASaveBeforeTheFirstWorldUpdate()
	{
		var economy = PoolPark( out _ );
		AttachStaffWorld( economy, out var placement );
		var candidate = economy.Staff.Candidates.First( item => item.Type == StaffType.Entertainer );
		Assert.IsTrue( placement.BeginHire( candidate.Id ) );
		Assert.AreEqual( StaffClickResult.Placed, placement.Click( 5, 5 ) );
		var member = placement.LastPlaced!;
		member.Grade = 3;
		member.Energy = 0; // do not conflate constructor preservation with a later search-time energy tier
		Assert.IsNull( member.Navigation );
		var captured = ParkSaveFile.Capture( economy ).Staff.Members.Single();
		Assert.IsNull( captured.WorldState, "do not invent a world state before navigation, a job or an effect exists" );
		Assert.AreEqual( (int?)60, captured.BaseSpeed, "the optional scalar preserves the constructor speed without inventing navigation" );
		var restored = RoundTrip( economy );
		var loaded = restored.Staff.Members.Single();
		Assert.IsTrue( loaded.HasInitializedBaseSpeed, "an explicit saved 60 is initialized, not the legacy fallback" );
		Assert.IsNull( loaded.Navigation );
		Assert.AreEqual( (member.CellX, member.CellY), (loaded.CellX, loaded.CellY) );
		var world = AttachStaffWorld( restored, out _ );
		world.UpdateStaff( restored, 1 );
		Assert.AreEqual( 60, loaded.BaseSpeed );
	}

	[TestMethod]
	public void AnUnplacedConstructorSpeedRoundTripsBeforeTheWorldSpawnsTheMember()
	{
		var economy = PoolPark( out _ );
		var member = EconomyTestData.HireBest( economy, StaffType.Mechanic );
		var speed = member.BaseSpeed;
		member.Grade = speed == 100 ? 1 : 3;
		var restored = RoundTrip( economy );
		var loaded = restored.Staff.Members.Single();
		Assert.IsNull( loaded.CellX );
		Assert.IsNull( loaded.CellY );
		Assert.IsNull( loaded.Navigation );
		Assert.AreEqual( speed, loaded.BaseSpeed );
		var world = AttachStaffWorld( restored, out _ );
		Assert.IsNotNull( loaded.CellX, "the existing world spawn, not deserialization, assigns the position" );
		Assert.IsNull( loaded.Navigation );
		world.UpdateStaff( restored, 1 );
		Assert.IsNotNull( loaded.Navigation );
		Assert.AreEqual( speed, loaded.BaseSpeed );
	}

	[TestMethod]
	public void AnUnplacedMemberInHandKeepsItsSavedSpeedWithoutInventingAPositionOrNavigation()
	{
		var economy = PoolPark( out _ );
		var member = EconomyTestData.HireBest( economy, StaffType.Mechanic );
		var speed = member.BaseSpeed;
		member.State = StaffState.PickedUp;
		Assert.IsNull( member.CellX );
		Assert.IsNull( member.CellY );
		var restored = RoundTrip( economy );
		var loaded = restored.Staff.Members.Single();
		Assert.AreEqual( speed, loaded.BaseSpeed );
		Assert.IsTrue( loaded.HasInitializedBaseSpeed );
		Assert.IsNull( loaded.CellX );
		Assert.IsNull( loaded.CellY );
		Assert.IsNull( loaded.Navigation );
		var world = AttachStaffWorld( restored, out _ );
		world.UpdateStaff( restored, 1 );
		Assert.IsNull( loaded.CellX, "Synchronize must not spawn an unplaced member in hand" );
		Assert.IsNull( loaded.Navigation );
		StaffPlacement.PutDown( loaded, 5, 5 );
		world.UpdateStaff( restored, 2 );
		Assert.AreEqual( speed, loaded.BaseSpeed );
	}

	[TestMethod]
	public void APooledHeadlessHandymansTimedLitterJobAndSpeedSurviveTheSave()
	{
		var economy = PoolPark( out _ );
		var member = EconomyTestData.HireBest( economy, StaffType.Handyman );
		var speed = member.BaseSpeed;
		economy.Litter.Drop( 2, 5, (LitterType)1 );
		economy.DispatchHandymen();
		Assert.AreEqual( StaffJob.Idle, member.Job, "headless dispatch has no world state machine" );
		Assert.IsTrue( member.BusyTicks > 0 );
		Assert.IsNotNull( member.LitterCell );
		var data = ParkSaveFile.Capture( economy );
		Assert.IsNull( data.Staff.Members.Single().WorldState, "a world Job=Idle would invalidate a timed headless litter job" );
		Assert.AreEqual( (int?)speed, data.Staff.Members.Single().BaseSpeed );
		ParkSaveFile.Validate( data );
		var loaded = RoundTrip( economy ).Staff.Members.Single();
		Assert.AreEqual( member.LitterCell, loaded.LitterCell );
		Assert.AreEqual( member.BusyTicks, loaded.BusyTicks );
		Assert.AreEqual( StaffJob.Idle, loaded.Job );
		Assert.AreEqual( speed, loaded.BaseSpeed );
	}

	[TestMethod]
	public void InvalidScalarSpeedsAndCompetingSpeedSourcesAreRejected()
	{
		var economy = PoolPark( out _ );
		EconomyTestData.HireBest( economy, StaffType.Mechanic );
		foreach ( var speed in new[] { -1, 256 } )
		{
			var json = JsonNode.Parse( ParkSaveFile.Serialize( economy ) )!;
			json["Staff"]!["Members"]![0]!["BaseSpeed"] = speed;
			Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) ) );
		}
		var world = AttachStaffWorld( economy, out _ );
		world.UpdateStaff( economy, 1 );
		var withWorld = JsonNode.Parse( ParkSaveFile.Serialize( economy ) )!;
		Assert.IsNotNull( withWorld["Staff"]!["Members"]![0]!["WorldState"] );
		withWorld["Staff"]!["Members"]![0]!["BaseSpeed"] = 60;
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( withWorld.ToJsonString() ) ) );
	}

	[TestMethod]
	public void ALegacyMemberWithoutWorldStateTakesTheOldGradeFallbackOnlyOnce()
	{
		var economy = EconomyTestData.Park(); // no person constructor / thing pool
		var member = EconomyTestData.HireBest( economy, StaffType.Mechanic );
		member.Grade = 3;
		Assert.IsFalse( member.HasInitializedBaseSpeed );
		Assert.IsNull( ParkSaveFile.Capture( economy ).Staff.Members.Single().WorldState );
		Assert.IsNull( ParkSaveFile.Capture( economy ).Staff.Members.Single().BaseSpeed, "old/headless members without a constructor speed add no field" );
		var restored = RoundTrip( economy );
		var loaded = restored.Staff.Members.Single();
		Assert.IsFalse( loaded.HasInitializedBaseSpeed );
		var world = AttachStaffWorld( restored, out _ );
		world.UpdateStaff( restored, 1 );
		Assert.AreEqual( 100, loaded.BaseSpeed, "legacy grade fallback is retained" );
		loaded.Grade = 1;
		StaffPlacement.PutDown( loaded, 6, 5 );
		world.UpdateStaff( restored, 2 );
		Assert.AreEqual( 100, loaded.BaseSpeed, "drop does not reinterpret an already initialized speed" );
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
