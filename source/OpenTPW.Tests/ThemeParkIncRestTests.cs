using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's rest areas (docs/reverse/TPI-STAFF.md §13, ThemeParkIncStaffWorld.Rest.cs): the staff rooms of the facilities folder, the finder, the tired gate's and the slack test's walk, the
/// arrival with its capacity, the rest and its end, the search beside a full room, the low-motivation rest and a rest area's removal. Retail Science economy: needs OPENTPW_TPI_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncRestTests : ThemeParkIncScienceTestBase
{
	/// <summary>Rest areas in a fixed list order, with their occupancy and customers.</summary>
	private sealed class TestRooms : IThemeParkIncRooms
	{
		public List<ThemeParkIncRestArea> Areas { get; } = new();
		public Dictionary<ushort, int> Occupants { get; } = new();
		public List<ushort> Customers { get; } = new();
		public ThemeParkIncRoom? First( ThemeParkIncRoomKind kind ) => null;
		public void AddOccupancy( ushort thing, int delta ) => Occupants[thing] = Occupancy( thing ) + delta;
		public IReadOnlyList<ThemeParkIncRestArea> RestAreas() => Areas.ToArray();
		public int Occupancy( ushort thing ) => Occupants.TryGetValue( thing, out var value ) ? value : 0;
		public void RecordCustomer( ushort thing ) => Customers.Add( thing );
	}

	/// <summary>A path row along y = 5 from x = 0 to 11.</summary>
	private static GuestPathGrid Row( ParkEconomy economy )
	{
		var grid = new GuestPathGrid( 16, 16 );
		for ( var x = 0; x < 12; x++ )
			grid.SetPath( x, 5, true );
		economy.Litter.Cells = grid.Cells;
		economy.Litter.ThemeParkInc = true;
		return grid;
	}

	private static ThemeParkIncStaffMember Hire( ParkEconomy economy, ThemeParkIncStaffWorld world, ThemeParkIncStaffRole role, (int X, int Y) cell )
	{
		var staff = economy.TpiStaff!;
		var slot = Enumerable.Range( 0, ThemeParkIncStaff.PoolSlots ).First( index => staff.Slots[index]?.Role == role );
		Assert.IsTrue( staff.BeginHire( slot ) );
		var member = staff.EndHire( slot, placed: true )!;
		member.Experience = staff.Settings.MaxExperience / 2;
		world.Construct( economy, member, cell, null );
		return member;
	}

	/// <summary>The Science economy, a path row, its staff world with <paramref name="rooms"/> and a handyman on (2, 5).</summary>
	private (ParkEconomy Economy, ThemeParkIncStaffWorld World, ThemeParkIncStaffMember Member) Park( TestRooms rooms, ThemeParkIncStaffRole role = ThemeParkIncStaffRole.Handyman )
	{
		var economy = ScienceEconomy();
		var world = new ThemeParkIncStaffWorld( Row( economy ) ) { Rooms = rooms };
		return (economy, world, Hire( economy, world, role, (2, 5) ));
	}

	private static ThemeParkIncRestArea Area( ushort thing, int x, bool open = true ) => new( thing, (x, 4), (x, 5), open );

	private static void Walk( ParkEconomy economy, ThemeParkIncStaffWorld world, ThemeParkIncStaffMember member, int state, int limit = 400 )
	{
		var turn = economy.Turn;
		for ( var step = 0; step < limit && member.State == state; step++ )
			world.Update( economy, member, ++turn );
	}

	[TestMethod]
	public void TheStaffRoomsAreTheOnlyRestAreasOfTheThreeZones()
	{
		// [BIN:TPI-EXE:0x5DB6E2 rest-area flag] the thing builder sets +0x42 bit 2 only from the type record's +0x1B0 (UsageInfo.ChillsYouOut): each zone's facilities folder holds one staff room
		_ = ScienceEconomy();
		foreach ( var (zone, id) in new[] { ("science", 1400), ("water", 2420), ("arabian", 3418) } )
		{
			var resting = ObjectCatalog.Load( zone ).Entries.Where( entry => entry.Settings.GetInt( "UsageInfo.ChillsYouOut" ) != 0 ).ToArray();
			Assert.AreEqual( 1, resting.Length, zone );
			Assert.AreEqual( (id, ObjectCategory.Facility, "Staff Room"), (resting[0].InfoId, resting[0].Category, resting[0].SettingsName), zone );
			Assert.IsTrue( resting[0].IsBuildable, $"{zone}: the facilities tab offers it" );
			CollectionAssert.AreEqual( new[] { id }, EconomyObjectCatalog.Load( zone, easy: false ).Objects.Where( info => info.ChillsYouOut ).Select( info => info.InfoId ).ToArray(), zone );
		}
	}

	[TestMethod]
	public void TheFinderTakesTheNearestOpenAreaAndTheFirstOfATie()
	{
		// [BIN:TPI-EXE:0x62BA20 rest-area finder] squared cell distance, unsigned jae against the best so far, the open word +0x7C
		var rooms = new TestRooms();
		var (_, world, member) = Park( rooms );
		Assert.AreEqual( 0, world.FindRestArea( member ), "no rest area" );
		rooms.Areas.AddRange( new[] { Area( 30, 9 ), Area( 31, 0, open: false ), Area( 32, 6 ), Area( 33, 6 ) } );
		// from (2, 5): (9, 4) 50, (0, 4) closed, (6, 4) 17 twice: the first of the tie in list order
		Assert.AreEqual( 32, world.FindRestArea( member ) );
		rooms.Areas.Insert( 0, Area( 34, 2 ) );
		Assert.AreEqual( 34, world.FindRestArea( member ), "(2, 4) is 1 away" );
	}

	[TestMethod]
	public void ATiredMemberWalksToTheRoomRestsUntilFullAndLooksForWork()
	{
		var rooms = new TestRooms();
		var (economy, world, member) = Park( rooms );
		var settings = economy.TpiStaff!.Settings;
		rooms.Areas.Add( Area( 40, 9 ) );
		member.Energy = settings.RestLevel;
		member.Happiness = member.Motivation = 50f;
		// [BIN:TPI-EXE:0x62BCA9 tired gate rest] a reachable area: +0x248, state 2, no draw
		var state = economy.World.State;
		world.Search( economy, member, economy.Turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.ToRestArea, 40), (member.State, member.RestArea) );
		Assert.AreEqual( state, economy.World.State, "the gate's rest branch takes no draw when the area is reached" );
		Assert.AreEqual( 1, member.Template, "state 2 asks for the walk template" );
		// [BIN:TPI-EXE:0x629EF0 walk to rest area] arrived with room: occupancy + 1, a customer, state 3 and no sprite source
		Walk( economy, world, member, ThemeParkIncStaffWorld.ToRestArea );
		Assert.AreEqual( ThemeParkIncStaffWorld.Resting, member.State );
		Assert.AreEqual( 1, rooms.Occupancy( 40 ) );
		CollectionAssert.AreEqual( new ushort[] { 40 }, rooms.Customers );
		Assert.IsNull( member.Sprite.Source, "0x5D1050 deletes the sprite source at the rest's entry" );
		Assert.AreEqual( 3, ThemeParkIncStaffWorld.DisplayCategory( member ), "resting" );
		// [BIN:TPI-EXE:0x62A1C0 rest] [BIN:TPI-EXE:0x62C230 rest end] energy full ends it: occupancy - 1, +0x248 = 0, shown again, then the look for work
		var turn = economy.Turn;
		for ( var step = 0; step < 10000 && member.State == ThemeParkIncStaffWorld.Resting; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreNotEqual( ThemeParkIncStaffWorld.Resting, member.State );
		Assert.AreEqual( (100f, 0, 0), (member.Energy, rooms.Occupancy( 40 ), member.RestArea) );
		Assert.IsTrue( member.Happiness > 50f && member.Motivation > 50f, "happiness and motivation recover with the energy" );
		Assert.IsNotNull( member.Sprite.Source, "shown again" );
	}

	[TestMethod]
	public void AFullRoomSendsTheMemberBackToItsEntranceToWaitAndDrawsEveryTick()
	{
		var rooms = new TestRooms();
		var (economy, world, member) = Park( rooms );
		var settings = economy.TpiStaff!.Settings;
		rooms.Areas.Add( Area( 40, 9 ) );
		member.Energy = settings.RestLevel;
		world.Search( economy, member, economy.Turn );
		rooms.Occupants[40] = settings.StaffRoomCapacity;
		// [BIN:TPI-EXE:0x629EF0 walk to rest area] a full room: the route back to its entrance, +0x1A0 = 0, state 0x23
		Walk( economy, world, member, ThemeParkIncStaffWorld.ToRestArea );
		Assert.AreEqual( ThemeParkIncStaffWorld.RestSearch, member.State );
		Assert.AreEqual( 0, member.Navigation!.Mask );
		Assert.AreEqual( settings.StaffRoomCapacity, rooms.Occupancy( 40 ), "no place taken" );
		Assert.AreEqual( 0, rooms.Customers.Count );
		Assert.AreEqual( 2, ThemeParkIncStaffWorld.DisplayCategory( member ), "state 0x23 shows as working" );
		// [BIN:TPI-EXE:0x62CA90 rest search] full: the walk step, then the closing draw (and two draws for a point in his own cell once the walk has ended)
		var turn = economy.Turn;
		var draws = new HashSet<int>();
		for ( var step = 0; step < 40; step++ )
		{
			var before = new WorldRng( economy.World.State );
			world.Update( economy, member, ++turn );
			Assert.AreEqual( ThemeParkIncStaffWorld.RestSearch, member.State, "he waits while the room is full" );
			var count = 0;
			while ( before.State != economy.World.State && count < 4 )
			{
				before.NextWord();
				count++;
			}
			Assert.IsTrue( count is 1 or 3, $"the closing draw, after the own cell's two once the walk has ended (step {step}: {count})" );
			draws.Add( count );
		}
		CollectionAssert.AreEquivalent( new[] { 1, 3 }, draws.ToArray(), "both branches ran" );
		// room again: the walk to it and state 2 (the closing draw still taken)
		rooms.Occupants[40] = 0;
		world.Update( economy, member, ++turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.ToRestArea, 40), (member.State, member.RestArea) );
		// no area at all: state 0, no draw
		rooms.Areas.Clear();
		member.State = ThemeParkIncStaffWorld.RestSearch;
		var state = economy.World.State;
		world.Update( economy, member, ++turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.Idle, 0), (member.State, member.RestArea) );
	}

	[TestMethod]
	public void AMemberLowOnEnergyNearARoomSlacksInsteadOfStartingHisWalk()
	{
		// [BIN:TPI-EXE:0x62D650 slack test] energy byte below StaffSlackingEnergy and the area within MinDistanceForSlacking (squared): the setter's state 1 becomes 2
		var rooms = new TestRooms();
		var (economy, world, member) = Park( rooms );
		var settings = economy.TpiStaff!.Settings;
		rooms.Areas.Add( Area( 40, 3 ) );
		member.Energy = settings.StaffSlackingEnergy - 1;
		world.SetState( economy, member, ThemeParkIncStaffWorld.Walking, economy.Turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.ToRestArea, 40), (member.State, member.RestArea) );
		member.State = ThemeParkIncStaffWorld.Idle;
		member.RestArea = 0;
		member.Energy = settings.StaffSlackingEnergy;
		world.SetState( economy, member, ThemeParkIncStaffWorld.Walking, economy.Turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.Walking, 0), (member.State, member.RestArea), "at StaffSlackingEnergy he walks" );
		// too far: (11, 4) from (2, 5) is 82 cells squared
		rooms.Areas[0] = Area( 40, 11 );
		member.Energy = settings.StaffSlackingEnergy - 1;
		Assert.IsTrue( 82 > settings.MinDistanceForSlacking, "the retail MinDistanceForSlacking" );
		world.SetState( economy, member, ThemeParkIncStaffWorld.Walking, economy.Turn );
		Assert.AreEqual( ThemeParkIncStaffWorld.Walking, member.State );
	}

	[TestMethod]
	public void AWorkingMemberWithLowMotivationRestsAfterLookingForWork()
	{
		// [BIN:TPI-EXE:0x62D866 low motivation] below MotivationPercentToStrike / 2 and now working (display 2 or 7): the nearest reachable area, state 2
		var rooms = new TestRooms();
		var (economy, world, member) = Park( rooms, ThemeParkIncStaffRole.Handyman );
		var settings = economy.TpiStaff!.Settings;
		economy.Litter.Restore( new[] { new LitterCell( 6, 5, (LitterType)1, 0, 0 ) } );
		rooms.Areas.Add( Area( 40, 9 ) );
		member.Motivation = settings.MotivationPercentToStrike / 2;
		world.LookForWork( economy, settings, member, economy.Turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.ToLitter, 0), (member.State, member.RestArea), "at half of MotivationPercentToStrike he works" );
		member.Motivation = settings.MotivationPercentToStrike / 2 - 1;
		economy.Litter.Restore( new[] { new LitterCell( 6, 5, (LitterType)1, 0, 0 ) } );
		world.LookForWork( economy, settings, member, economy.Turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.ToRestArea, 40), (member.State, member.RestArea), "the litter walk (display 2) gave way to the rest" );
		// no area: he stops (state 0), silently
		rooms.Areas.Clear();
		member.RestArea = 0;
		economy.Litter.Restore( new[] { new LitterCell( 6, 5, (LitterType)1, 0, 0 ) } );
		world.LookForWork( economy, settings, member, economy.Turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.Idle, 0), (member.State, member.RestArea) );
	}

	[TestMethod]
	public void ARemovedRoomEndsTheRestOrTheWalkToIt()
	{
		var rooms = new TestRooms();
		var (economy, world, member) = Park( rooms );
		var settings = economy.TpiStaff!.Settings;
		rooms.Areas.Add( Area( 40, 9 ) );
		member.Energy = settings.RestLevel;
		world.Search( economy, member, economy.Turn );
		// [BIN:TPI-EXE:0x626638 rest area removed] walking to it: +0x248 = 0 and state 0
		rooms.Areas.Clear();
		world.RestAreaRemoved( economy, 40, economy.Turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.Idle, 0), (member.State, member.RestArea) );
		// resting in it: the rest end, then the nearest other area becomes his and he walks there idle
		rooms.Areas.AddRange( new[] { Area( 40, 9 ), Area( 41, 7 ) } );
		member.Energy = settings.RestLevel;
		world.Search( economy, member, economy.Turn );
		Assert.AreEqual( 41, member.RestArea );
		Walk( economy, world, member, ThemeParkIncStaffWorld.ToRestArea );
		Assert.AreEqual( (ThemeParkIncStaffWorld.Resting, 1), (member.State, rooms.Occupancy( 41 )) );
		rooms.Areas.RemoveAt( 1 );
		world.RestAreaRemoved( economy, 41, economy.Turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.Idle, 40, 0), (member.State, member.RestArea, rooms.Occupancy( 41 )) );
		Assert.IsNotNull( member.Sprite.Source );
		// another thing's removal leaves him be
		world.RestAreaRemoved( economy, 99, economy.Turn );
		Assert.AreEqual( 40, member.RestArea );
	}

	[TestMethod]
	public void AResearcherRestsLikeAHandymanAndARestingMemberSurvivesASave()
	{
		var rooms = new TestRooms();
		var (economy, world, member) = Park( rooms, ThemeParkIncStaffRole.Researcher );
		var settings = economy.TpiStaff!.Settings;
		rooms.Areas.Add( Area( 40, 9 ) );
		member.Energy = settings.RestLevel;
		world.LookForWork( economy, settings, member, economy.Turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.ToRestArea, 40), (member.State, member.RestArea), "the researcher's search runs the same gate" );
		Walk( economy, world, member, ThemeParkIncStaffWorld.ToRestArea );
		Assert.AreEqual( ThemeParkIncStaffWorld.Resting, member.State );
		var json = ParkSaveFile.Serialize( economy );
		StringAssert.Contains( json, "\"RestArea\": 40" );
		var directory = System.IO.Directory.CreateTempSubdirectory( "opentpw-tpi-rest-" );
		ParkEconomy restored;
		try
		{
			var path = System.IO.Path.Combine( directory.FullName, "science.json" );
			ParkSaveFile.Save( path, economy );
			restored = ParkSaveFile.Load( path );
		}
		finally
		{
			directory.Delete( recursive: true );
		}
		var copy = restored.TpiStaff!.Members.Single( other => other.Id == member.Id );
		Assert.AreEqual( (ThemeParkIncStaffWorld.Resting, 40), (copy.State, copy.RestArea) );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ), "the save round-trips exactly" );
	}
}
