using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The researchers' state machine (tp.exe <c>0x005029F0</c>) and the staff states every role shares (the gate <c>0x00506A40</c>, the walk to a rest area
/// <c>0x00505FE0</c>, resting <c>0x005061D0</c>, the strike walk <c>0x005056E0</c> and striking <c>0x00506300</c>; docs/reverse/WANDER-plan.md §4).
/// </summary>
[TestClass]
public class ResearcherRestTests
{
	private const int StaffRoomId = 1411;

	private sealed class Fixture
	{
		public readonly GuestPathGrid Grid = new( 20, 20 );
		public readonly ParkEconomy Park;
		public readonly GuestSimulation Guests;
		public readonly StaffWorldSimulation World;
		public readonly List<RestSite> Rests = new();
		public readonly Dictionary<int, int> Resting = new();
		public readonly List<StaffVoice> Sounds = new();
		public bool GatesOpen = true;

		public Fixture()
		{
			var room = new EconomyObjectInfo( StaffRoomId, "Staff Room", ParkObjectKind.Feature, ResearchCategory.Feature, 0,
				new[] { new UpgradeLevelInfo( 0, 100, 0, 0, 0, new[] { 50, 30, 20, 10 } ) }, null, null, null, 0, 0, 0, 0, 0, 0, "features/staff.wad", ChillsYouOut: true );
			Park = new ParkEconomy( EconomyTestData.Settings( 5_000_000 ), new EconomyObjectCatalog( EconomyTestData.Catalog().Objects.Append( room ) ), ParkGameMode.FullSimulation, 7 );
			for ( var x = 0; x < 16; x++ )
				Grid.SetPath( x, 5, true );
			for ( var y = 5; y < 11; y++ )
				Grid.SetPath( 10, y, true );
			Park.OpenPark();
			Park.Litter.Cells = Grid.Cells;
			Guests = new GuestSimulation( Grid, GuestTests.CreateSettings(), 5 ) { ArrivalsEnabled = false, Litter = Park.Litter };
			World = new StaffWorldSimulation( Grid, () => Park.Staff.Members, _ => null, (0, 5) )
			{
				Guests = Guests, RestAreas = () => Rests, GatesOpen = () => GatesOpen, RestingStaff = counts => { foreach ( var site in Rests ) Resting[site.Id] = counts.GetValueOrDefault( site.Id ); }
			};
			Park.StaffWorld = World;
		}

		public int BuildStaffRoom( (int X, int Y) cell, (int X, int Y) access )
		{
			Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, Park.TryBuild( StaffRoomId, out var room ) );
			Rests.Add( new RestSite( room!.Id, cell, access ) );
			return room.Id;
		}

		public StaffMember Hire( StaffType type )
		{
			var member = EconomyTestData.HireBest( Park, type );
			World.Synchronize();
			return member;
		}

		public void Turns( int turns, Action? each = null )
		{
			for ( var turn = 0; turn < turns; turn++ )
			{
				EconomyTestData.AdvanceToTurn( Park, Park.Turn + 1 );
				Sounds.AddRange( World.TakeVoices() );
				each?.Invoke();
			}
		}

		public void Strike( StaffType role, bool on ) =>
			Park.Staff.Strikes.Restore( new int[5], Enumerable.Range( 0, 5 ).Select( index => on && index == (int)role ).ToArray(), new uint[5] );
	}

	[TestMethod]
	public void AResearcherResearchesForWorkDurationThenMovesOn()
	{
		var f = new Fixture();
		var researcher = f.Hire( StaffType.Researcher );
		long started = -1, ended = -1;
		f.Turns( 400, () =>
		{
			if ( started < 0 && researcher.Job == StaffJob.Researching )
				started = researcher.JobStamp;
			else if ( started > 0 && ended < 0 && (researcher.Job != StaffJob.Researching || researcher.JobStamp != started) )
				ended = f.Park.Turn;
		} );
		Assert.IsTrue( started > 0, "he researched" );
		var work = f.Park.Settings[StaffType.Researcher].WorkDuration[researcher.Grade];
		Assert.AreEqual( started + work + 1, ended, "a spell lasts until the turn passes its start + WorkDuration" );
		Assert.IsTrue( f.Sounds.Any( sound => sound.Event == StaffWorldSimulation.ResearchingEvent ), "cat_staff 138 while he works" );
	}

	[TestMethod]
	public void ATiredMemberWalksToTheStaffRoomRestsAndComesBackRefreshed()
	{
		var f = new Fixture();
		var room = f.BuildStaffRoom( (12, 6), (12, 5) );
		var researcher = f.Hire( StaffType.Researcher );
		f.Turns( 2 );
		researcher.Energy = 1;
		var jobs = new List<StaffJob>();
		var counted = false;
		float? afterRest = null;
		f.Turns( 600, () =>
		{
			if ( afterRest == null && jobs.Count > 0 && jobs[^1] == StaffJob.Resting && researcher.Job != StaffJob.Resting )
				afterRest = researcher.Energy;
			if ( jobs.Count == 0 || jobs[^1] != researcher.Job )
				jobs.Add( researcher.Job );
			if ( researcher.Job == StaffJob.Resting )
			{
				Assert.AreEqual( (12, 5), researcher.Navigation!.Cell, "he rests at the room's door" );
				Assert.AreEqual( StaffState.Resting, researcher.State );
				counted |= f.Resting.TryGetValue( room, out var count ) && count == 1;
			}
		} );
		var walk = jobs.IndexOf( StaffJob.ToRestArea );
		Assert.IsTrue( walk >= 0 && jobs.IndexOf( StaffJob.Resting ) == walk + 1, string.Join( ", ", jobs ) );
		Assert.IsTrue( counted, "VAR_STAFFIN counts him in" );
		Assert.AreEqual( 0, f.Resting[room], "and out again" );
		Assert.AreNotEqual( StaffJob.Resting, researcher.Job );
		Assert.IsTrue( afterRest >= 99f, $"the rest ends at an energy byte of 100 ({afterRest})" );
		Assert.AreEqual( 0, researcher.RestAreaId );
	}

	[TestMethod]
	public void WithoutARestAreaATiredMemberLosesHappinessInPlace()
	{
		var f = new Fixture();
		var handyman = f.Hire( StaffType.Handyman );
		f.Turns( 2 );
		handyman.Energy = 1;
		var happiness = handyman.Happiness;
		f.Turns( 200, () => Assert.AreNotEqual( StaffJob.ToRestArea, handyman.Job ) );
		Assert.IsTrue( handyman.Happiness < happiness, "the no-rest-area hit" );
	}

	[TestMethod]
	public void AStrikerWalksOutAlongTheLaneAndBackWhenTheStrikeEnds()
	{
		var f = new Fixture();
		var handyman = f.Hire( StaffType.Handyman );
		f.Turns( 3 );
		f.Strike( StaffType.Handyman, true );
		var offGrid = false;
		f.Turns( 300, () => offGrid |= handyman.Navigation!.OffGrid );
		Assert.AreEqual( StaffJob.Striking, handyman.Job );
		Assert.AreEqual( StaffState.OnStrike, handyman.State );
		Assert.IsTrue( offGrid, "he left the paths" );
		var strikeArea = f.Park.Settings.StrikeArea;
		Assert.AreEqual( (strikeArea.X, strikeArea.Y), handyman.Navigation!.Cell, "at the strike area" );

		var json = ParkSaveFile.Serialize( f.Park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), f.Park.Settings, f.Park.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ) );

		f.Strike( StaffType.Handyman, false );
		f.Turns( 300 );
		Assert.IsFalse( handyman.Navigation.OffGrid, "back on the paths" );
		Assert.IsTrue( f.Grid.IsWalkable( handyman.Navigation.Cell.X, handyman.Navigation.Cell.Y ) );
		Assert.AreNotEqual( StaffState.OnStrike, handyman.State );
	}

	[TestMethod]
	public void WithoutLaneAAStrikerTakesLaneB()
	{
		var f = new Fixture();
		for ( var x = 16; x < 20; x++ )
			f.Grid.SetPath( x, 5, true );
		f.Guests.Settings.ArrivalLaneA = null;
		var handyman = f.Hire( StaffType.Handyman );
		f.Turns( 3 );
		f.Strike( StaffType.Handyman, true );
		var passed = false;
		f.Turns( 300, () => passed |= handyman.Navigation!.OffGrid && handyman.Navigation.Cell == (17, 2) );
		Assert.AreEqual( StaffJob.Striking, handyman.Job );
		Assert.IsTrue( passed, "out past lane B's park-side crossing" );
	}

	[TestMethod]
	public void WithTheGatesClosedNobodyWalksOutToStrike()
	{
		var f = new Fixture { GatesOpen = false };
		var handyman = f.Hire( StaffType.Handyman );
		f.Strike( StaffType.Handyman, true );
		f.Turns( 200, () => Assert.AreNotEqual( StaffState.OnStrike, handyman.State, "0x0051A290: the gates' VAR_STATUS must be 1" ) );
	}

	[TestMethod]
	public void SellingTheStaffRoomEndsTheRest()
	{
		var f = new Fixture();
		var room = f.BuildStaffRoom( (12, 6), (12, 5) );
		var researcher = f.Hire( StaffType.Researcher );
		f.Turns( 2 );
		researcher.Energy = 1;
		for ( var turn = 0; turn < 300 && researcher.Job != StaffJob.Resting; turn++ )
			f.Turns( 1 );
		Assert.AreEqual( StaffJob.Resting, researcher.Job );
		f.Park.Sell( room );
		f.Rests.Clear();
		Assert.AreEqual( (0, StaffJob.Idle), (researcher.RestAreaId, researcher.Job) );
		ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( f.Park ) ) ), f.Park.Settings, f.Park.Catalog );
	}
}
