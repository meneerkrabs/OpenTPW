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
		var slot = Enumerable.Range( 0, ThemeParkIncStaff.PoolSlots ).FirstOrDefault( index => staff.Slots[index]?.Role == role, -1 );
		if ( slot < 0 )
			Assert.Inconclusive( $"The Science pool holds no {role}." );
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
		var success = new WorldRng( economy.World.State );
		world.Update( economy, member, ++turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.ToRestArea, 40), (member.State, member.RestArea) );
		success.NextWord();
		Assert.AreEqual( success.State, economy.World.State, "the successful branch still takes the closing draw (0x62CB53 jumps to 0x62CBCF), and only it" );
		// no area at all: state 0, no draw
		rooms.Areas.Clear();
		member.State = ThemeParkIncStaffWorld.RestSearch;
		var state = economy.World.State;
		world.Update( economy, member, ++turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.Idle, 0), (member.State, member.RestArea) );
		Assert.AreEqual( state, economy.World.State, "no rest area: state 0 and no draw (0x62CC33)" );
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

	[TestMethod]
	public void AMechanicRestsLikeTheOthers()
	{
		// [BIN:TPI-EXE:0x5DA358 mechanic dispatch] states 2 and 3 go to 0x629EF0 and 0x62A1C0, 0x23 to the common handler
		var rooms = new TestRooms();
		var (economy, world, member) = Park( rooms, ThemeParkIncStaffRole.Mechanic );
		var settings = economy.TpiStaff!.Settings;
		rooms.Areas.Add( Area( 40, 9 ) );
		member.Energy = settings.RestLevel;
		world.LookForWork( economy, settings, member, economy.Turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.ToRestArea, 40), (member.State, member.RestArea), "the mechanic's search runs the rest gate" );
		Walk( economy, world, member, ThemeParkIncStaffWorld.ToRestArea );
		Assert.AreEqual( ThemeParkIncStaffWorld.Resting, member.State );
		Assert.IsTrue( ThemeParkIncStaffMember.IsPortedState( ThemeParkIncStaffRole.Mechanic, ThemeParkIncStaffWorld.RestSearch ) );
		Walk( economy, world, member, ThemeParkIncStaffWorld.Resting, 2000 );
		Assert.AreNotEqual( ThemeParkIncStaffWorld.Resting, member.State, "the rest ends and he goes back to work" );
	}

	[TestMethod]
	public void AGardenerRestsLikeTheOthersAndARestSavedMidwayGoesOnAlike()
	{
		// [BIN:TPI-EXE:0x5D2A60 gardener dispatch] the byte table 0x5D2B68 (00 01 02 03 ...) sends 2 to 0x629EF0 and 3 to 0x62A1C0, and 0x23 (above 0x20) to the common handler 0x6287A0, whose
		// case is 0x62CA90: a tired gardener walks to a staff room and rests as the others do, and a save taken mid-rest continues
		var rooms = new TestRooms();
		var (economy, world, member) = Park( rooms, ThemeParkIncStaffRole.Gardener );
		var settings = economy.TpiStaff!.Settings;
		foreach ( var state in new[] { ThemeParkIncStaffWorld.ToRestArea, ThemeParkIncStaffWorld.Resting, ThemeParkIncStaffWorld.RestSearch } )
			Assert.IsTrue( ThemeParkIncStaffMember.IsPortedState( ThemeParkIncStaffRole.Gardener, state ), $"state {state}" );
		rooms.Areas.Add( Area( 40, 9 ) );
		member.Energy = settings.RestLevel;
		world.LookForWork( economy, settings, member, economy.Turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.ToRestArea, 40), (member.State, member.RestArea), "the gardener's search (0x5D28C0) runs the rest gate" );
		Walk( economy, world, member, ThemeParkIncStaffWorld.ToRestArea );
		Assert.AreEqual( (ThemeParkIncStaffWorld.Resting, 1), (member.State, rooms.Occupancy( 40 )) );
		var turn = economy.Turn + 1000;
		for ( var step = 0; step < 5; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( ThemeParkIncStaffWorld.Resting, member.State, "still resting when the save is taken" );

		var json = ParkSaveFile.Serialize( economy );
		StringAssert.Contains( json, "\"RestArea\": 40" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ), "the save round-trips exactly" );
		var copy = restored.TpiStaff!.Members.Single( other => other.Id == member.Id );
		Assert.AreEqual( (ThemeParkIncStaffWorld.Resting, 40), (copy.State, copy.RestArea) );
		var restoredRooms = new TestRooms();
		restoredRooms.Areas.AddRange( rooms.Areas );
		restoredRooms.Customers.AddRange( rooms.Customers );
		foreach ( var (thing, count) in rooms.Occupants )
			restoredRooms.Occupants[thing] = count;
		var restoredWorld = new ThemeParkIncStaffWorld( Row( restored ) ) { Rooms = restoredRooms };
		for ( var step = 0; step < 10000 && (member.State == ThemeParkIncStaffWorld.Resting || copy.State == ThemeParkIncStaffWorld.Resting); step++ )
		{
			turn++;
			world.Update( economy, member, turn );
			restoredWorld.Update( restored, copy, turn );
		}
		Assert.AreNotEqual( ThemeParkIncStaffWorld.Resting, member.State, "the rest ends and he goes back to work" );
		Assert.AreEqual( (100f, 0, 0), (member.Energy, rooms.Occupancy( 40 ), member.RestArea) );
		for ( var step = 0; step < 20; step++ )
		{
			turn++;
			world.Update( economy, member, turn );
			restoredWorld.Update( restored, copy, turn );
		}
		Assert.AreEqual( ParkSaveFile.Serialize( economy ), ParkSaveFile.Serialize( restored ), "both runs end the same" );
		Assert.AreEqual( (0, 0), (restoredRooms.Occupancy( 40 ), copy.RestArea) );
		CollectionAssert.AreEqual( rooms.Customers, restoredRooms.Customers );
	}

	[DataTestMethod]
	[DataRow( ThemeParkIncStaffRole.Entertainer )]
	[DataRow( ThemeParkIncStaffRole.Guard )]
	public void AnEntertainerOrAGuardRestsLikeTheOthersAndARestSavedMidwayGoesOnAlike( ThemeParkIncStaffRole role )
	{
		// [BIN:TPI-EXE:0x5D1E20 entertainer dispatch] the byte table 0x5D1F70 (00 01 02 03 ...) sends 2 to 0x5D1F05 -> 0x629EF0 and 3 to 0x5D1F11 -> 0x62A1C0, and 0x23 (above 0x17) to the common
		// handler 0x6287A0; [BIN:TPI-EXE:0x5D3C60 guard dispatch] the byte table 0x5D3DD8 (00 01 02 03 ...) sends 2 to 0x5D3D67 -> 0x629EF0 and 3 to 0x5D3D71 -> 0x62A1C0, and 0x23 (above 0x1E) to the
		// common handler, whose case is 0x62CA90: a tired entertainer or guard walks to a staff room and rests as the others do, and a save taken mid-rest continues
		var rooms = new TestRooms();
		var (economy, world, member) = Park( rooms, role );
		var settings = economy.TpiStaff!.Settings;
		foreach ( var state in new[] { ThemeParkIncStaffWorld.ToRestArea, ThemeParkIncStaffWorld.Resting, ThemeParkIncStaffWorld.RestSearch } )
			Assert.IsTrue( ThemeParkIncStaffMember.IsPortedState( role, state ), $"state {state}" );
		rooms.Areas.Add( Area( 40, 9 ) );
		member.Energy = settings.RestLevel;
		world.LookForWork( economy, settings, member, economy.Turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.ToRestArea, 40), (member.State, member.RestArea), $"the {role}'s search runs the rest gate" );
		Walk( economy, world, member, ThemeParkIncStaffWorld.ToRestArea );
		Assert.AreEqual( (ThemeParkIncStaffWorld.Resting, 1), (member.State, rooms.Occupancy( 40 )) );
		Assert.IsTrue( ThemeParkIncStaffWorld.Hidden( member ), "a resting member is not drawn" );
		var turn = economy.Turn + 1000;
		for ( var step = 0; step < 5; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( ThemeParkIncStaffWorld.Resting, member.State, "still resting when the save is taken" );

		var json = ParkSaveFile.Serialize( economy );
		StringAssert.Contains( json, "\"RestArea\": 40" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ), "the save round-trips exactly" );
		var copy = restored.TpiStaff!.Members.Single( other => other.Id == member.Id );
		Assert.AreEqual( (ThemeParkIncStaffWorld.Resting, 40), (copy.State, copy.RestArea) );
		var restoredRooms = new TestRooms();
		restoredRooms.Areas.AddRange( rooms.Areas );
		restoredRooms.Customers.AddRange( rooms.Customers );
		foreach ( var (thing, count) in rooms.Occupants )
			restoredRooms.Occupants[thing] = count;
		var restoredWorld = new ThemeParkIncStaffWorld( Row( restored ) ) { Rooms = restoredRooms };
		for ( var step = 0; step < 10000 && (member.State == ThemeParkIncStaffWorld.Resting || copy.State == ThemeParkIncStaffWorld.Resting); step++ )
		{
			turn++;
			world.Update( economy, member, turn );
			restoredWorld.Update( restored, copy, turn );
		}
		Assert.AreNotEqual( ThemeParkIncStaffWorld.Resting, member.State, "the rest ends and he goes back to work" );
		Assert.AreEqual( (100f, 0, 0), (member.Energy, rooms.Occupancy( 40 ), member.RestArea) );
		for ( var step = 0; step < 20; step++ )
		{
			turn++;
			world.Update( economy, member, turn );
			restoredWorld.Update( restored, copy, turn );
		}
		Assert.AreEqual( ParkSaveFile.Serialize( economy ), ParkSaveFile.Serialize( restored ), "both runs end the same" );
		Assert.AreEqual( (0, 0), (restoredRooms.Occupancy( 40 ), copy.RestArea) );
		CollectionAssert.AreEqual( rooms.Customers, restoredRooms.Customers );
	}

	[DataTestMethod]
	[DataRow( ThemeParkIncStaffRole.Mechanic )]
	[DataRow( ThemeParkIncStaffRole.Gardener )]
	[DataRow( ThemeParkIncStaffRole.Entertainer )]
	[DataRow( ThemeParkIncStaffRole.Guard )]
	public void AMechanicOrAGardenerBesideAFullRoomWaitsInTheRestSearch( ThemeParkIncStaffRole role )
	{
		// [BIN:TPI-EXE:0x5DA280 mechanic dispatch] [BIN:TPI-EXE:0x5D2A60 gardener dispatch] [BIN:TPI-EXE:0x5D1E20 entertainer dispatch] [BIN:TPI-EXE:0x5D3C60 guard dispatch] 0x23 lies above every
		// one of these byte tables and goes to the common handler 0x6287A0, whose case is 0x62CA90: the role's dispatch runs the rest search, so a member beside a full room waits there, taking the
		// closing draw every step, and walks in once there is room
		var rooms = new TestRooms();
		var (economy, world, member) = Park( rooms, role );
		var settings = economy.TpiStaff!.Settings;
		rooms.Areas.Add( Area( 40, 9 ) );
		member.Energy = settings.RestLevel;
		world.Search( economy, member, economy.Turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.ToRestArea, 40), (member.State, member.RestArea) );
		rooms.Occupants[40] = settings.StaffRoomCapacity;
		Walk( economy, world, member, ThemeParkIncStaffWorld.ToRestArea );
		Assert.AreEqual( ThemeParkIncStaffWorld.RestSearch, member.State );
		var turn = economy.Turn + 1000;
		for ( var step = 0; step < 3; step++ )
		{
			var before = economy.World.State;
			world.Update( economy, member, ++turn );
			Assert.AreEqual( ThemeParkIncStaffWorld.RestSearch, member.State, "he waits while the room is full" );
			Assert.AreNotEqual( before, economy.World.State, "the rest search's closing draw (0x62CBCF)" );
		}
		rooms.Occupants[40] = 0;
		world.Update( economy, member, ++turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.ToRestArea, 40), (member.State, member.RestArea), "room again: the walk to it" );
	}

	[TestMethod]
	public void TheLevelsRestAreasFollowTheObjectListAndTheOpenWord()
	{
		// [BIN:TPI-EXE:0x62BA20 rest-area finder] the attraction list newest first, a thing with +0x42 & 2 and the open word; the level's IThemeParkIncRooms on placed objects of synthetic types
		var economy = ScienceEconomy();
		var root = System.IO.Directory.CreateTempSubdirectory( "opentpw-tpi-rest-rooms" ).FullName;
		var previousEntities = Entity.All;
		Entity.All = new();
		var (previousStarter, previousKiller) = (OriginalObjectRuntime.ParticleStarter, OriginalObjectRuntime.ParticleKiller);
		OriginalObjectRuntime.ParticleStarter = _ => 1;
		OriginalObjectRuntime.ParticleKiller = _ => { };
		var model = Md2ModelFileTests.CreateGeometry();
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x42 ), 1 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x44 ), 0 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x268 ), 0 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x78 ), 0x264 );
		foreach ( var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } )
			System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( offset ), 0 );
		System.IO.File.WriteAllBytes( System.IO.Path.Combine( root, "model.MD2" ), model );
		System.IO.File.WriteAllBytes( System.IO.Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
		try
		{
			ObjectCatalogEntry Entry( int id, string usage ) => new( "science", ObjectCategory.Facility, "/synthetic", new ObjectSettings( new[] { new ObjectSettingsFile( $"Info.Id {id}\n{usage}", "synthetic.sam" ) } ),
				new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(),
				new BaseFileSystem( root ), null );
			var staffRoom = Entry( 1400, "UsageInfo.ChillsYouOut 1\n" );
			var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null,
				new object[] { "science", Array.Empty<ObjectCatalogEntry>() }, null )!;
			var objects = new ParkObjects( catalog, new SandboxParkGrid(), new WorldSeed( 1 ) );
			var level = (Level)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
			void Backing( object target, string name, object value ) =>
				target.GetType().GetField( $"<{name}>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( target, value );
			var world = new ThemeParkIncStaffWorld( Row( economy ) );
			Backing( level, "Objects", objects );
			Backing( level, "TpiStaffWorld", world );
			var runtime = (ParkEconomyRuntime)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( ParkEconomyRuntime ) );
			Backing( runtime, "Economy", economy );
			Backing( level, "Park", runtime );
			typeof( Level ).GetMethod( "AttachThemeParkIncRooms", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.Invoke( level, null );

			// each thing enters the object list at its head (0x64F3E0), as these synthetic objects have no economy record: by its negated thing id
			var first = objects.TryPlace( staffRoom, 3, 9, 0, out _ )!;
			first.ThingId = level.Things.Allocate( SaveThingModel.Attraction );
			economy.AddThemeParkIncThing( first.ThingId );
			var second = objects.TryPlace( staffRoom, 9, 9, 0, out _ )!;
			second.ThingId = level.Things.Allocate( SaveThingModel.Attraction );
			economy.AddThemeParkIncThing( second.ThingId );
			var ride = objects.TryPlace( Entry( 1105, "" ), 6, 12, 0, out _ )!;
			ride.ThingId = level.Things.Allocate( SaveThingModel.Attraction );
			economy.AddThemeParkIncThing( ride.ThingId );
			var areas = world.Rooms!.RestAreas();
			CollectionAssert.AreEqual( new[] { (ushort)second.ThingId, (ushort)first.ThingId }, areas.Select( area => area.Thing ).ToArray(), "the staff rooms only, newest first" );
			Assert.AreEqual( second.ThingCell, areas[0].Cell );
			Assert.IsTrue( areas.All( area => area.CanLoad ), "a built facility is open (the constructor's open word, 0x5DB5FB)" );
			// the open word: OpenTPW's open state is the script's VAR_RIDECLOSED, the same word while nothing closes a facility
			Assert.IsTrue( second.Runtime.SetVariable( nameof( RideVariables.VAR_RIDECLOSED ), 1 ) );
			Assert.IsFalse( world.Rooms.RestAreas()[0].CanLoad );
			// the room being destroyed is off the list while its messages go out
			typeof( Level ).GetField( "tpiDestroyedRoom", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( level, first.ThingId );
			CollectionAssert.AreEqual( new[] { (ushort)second.ThingId }, world.Rooms.RestAreas().Select( area => area.Thing ).ToArray() );
			typeof( Level ).GetField( "tpiDestroyedRoom", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( level, 0 );
			// the finder walks the object list, not the thing pool: a saved list that names the older room first puts it first, and First() follows the same order
			economy.RestoreThemeParkIncObjectList( new[] { -ride.ThingId, -first.ThingId, -second.ThingId } );
			CollectionAssert.AreEqual( new[] { (ushort)first.ThingId, (ushort)second.ThingId }, world.Rooms.RestAreas().Select( area => area.Thing ).ToArray(), "the object list's order, not the pool's" );
		}
		finally
		{
			foreach ( var entity in Entity.All.ToArray() )
				entity.Delete();
			Entity.All = previousEntities;
			(OriginalObjectRuntime.ParticleStarter, OriginalObjectRuntime.ParticleKiller) = (previousStarter, previousKiller);
			System.IO.Directory.Delete( root, true );
		}
	}

	[TestMethod]
	public void AScienceContinuationWithoutTheFacilitiesIsRefusedBeforeTheAssetComparison()
	{
		var economy = ScienceEconomy();
		// the real manifest of the Science catalogue pins the facilities folder's facilities.sam
		var assets = ParkResumeAssetManifest.Capture( ObjectCatalog.Load( "science" ), "science", false );
		Assert.IsTrue( ParkResumeSaveFile.PinsThemeParkIncFacilities( assets ) );
		Assert.IsTrue( assets.Keys.Any( key => key.Contains( "/Facilities/resrch/", StringComparison.OrdinalIgnoreCase ) ), "and the facilities' archives" );
		var without = assets.Where( pair => !pair.Key.Contains( "/facilities/", StringComparison.OrdinalIgnoreCase ) ).ToDictionary( pair => pair.Key, pair => pair.Value );
		Assert.IsFalse( ParkResumeSaveFile.PinsThemeParkIncFacilities( without ) );
		var envelope = ParkResumeSaveFileTests.Envelope();
		envelope = envelope with
		{
			Theme = "science",
			StartKind = ParkStartKind.FullSimulation,
			Economy = ParkSaveFile.Capture( economy ),
			Staff = null, Gate = null, Advisor = null, Weather = null,
			TpiHudSpeed = GameSpeed.Normal,
			TpiCurrentObstacle = 1700,
			TpiWeatherClosingsSeen = 0,
			Layout = envelope.Layout! with { Cells = envelope.Layout.Cells! with { Land = null, TpiUnowned = Convert.ToBase64String( new byte[16 * 16 / 8] ) } }
		};
		byte[] Bytes( ParkResumeSnapshot snapshot ) => ParkResumeJson.Serialize( snapshot );
		var refused = Assert.ThrowsException<System.IO.InvalidDataException>( () => ParkResumeSaveFile.Deserialize( Bytes( envelope with { AssetHashes = without } ) ) );
		StringAssert.Contains( refused.Message, ParkResumeSaveFile.ThemeParkIncFacilitiesMissing );
		// with the facilities pinned the shape check passes this test and fails, if at all, on another part of the synthetic envelope
		try
		{
			ParkResumeSaveFile.Deserialize( Bytes( envelope with { AssetHashes = assets } ) );
		}
		catch ( System.IO.InvalidDataException other )
		{
			Assert.IsFalse( other.Message.Contains( "facilities", StringComparison.Ordinal ), other.Message );
		}
	}
}
