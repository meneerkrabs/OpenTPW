using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// WALK-003: one chain per cell holds every thing, guests and staff interleaved by the time each entered, head insertion; positions change only in the walk step and the teleport, so a guest on a
/// ride or carried keeps its last walked point and stays a neighbour (tp.exe 0x004D91F0, 0x0050B6A0, 0x004FA2A0, 0x004FA930, 0x005102F0).
/// </summary>
[TestClass]
public class WalkChainTests
{
	private static GuestSimulation Simulation() => new( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };

	private static List<GuestNavigation> Chain( GuestSimulation simulation, int x, int y )
	{
		var chain = new List<GuestNavigation>();
		simulation.AppendCellChain( x, y, chain );
		return chain;
	}

	[TestMethod]
	public void GuestsAndStaffAreInterleavedByTheTimeEachEnteredTheCell()
	{
		var simulation = Simulation();
		var first = simulation.SpawnInPark( 2, 5 );
		var staff = new GuestNavigation { X = first.Navigation.X + (1 << 12), Y = first.Navigation.Y };
		simulation.StaffChain.Follow( 7, staff );
		var last = simulation.SpawnInPark( 2, 5 );
		CollectionAssert.AreEqual( new[] { last.Navigation, staff, first.Navigation }, Chain( simulation, 2, 5 ), "head first: the guest that entered last, the staff member, the first guest" );
		// the staff member leaves and comes back: it is the newest entrant now
		var away = new GuestNavigation { X = staff.X + (1 << 16), Y = staff.Y };
		simulation.StaffChain.Follow( 7, away );
		Assert.AreEqual( 0, Chain( simulation, 2, 5 ).Count( walk => walk == staff || walk == away ) );
		simulation.StaffChain.Follow( 7, staff );
		CollectionAssert.AreEqual( new[] { staff, last.Navigation, first.Navigation }, Chain( simulation, 2, 5 ) );
	}

	[TestMethod]
	public void AMoveInsideTheCellKeepsTheEntryAndTheDestroyUnlinksIt()
	{
		var simulation = Simulation();
		var staff = new GuestNavigation { X = (2 << 16) + (1 << 14), Y = (5 << 16) };
		simulation.StaffChain.Follow( 3, staff );
		var guest = simulation.SpawnInPark( 2, 5 );
		var serial = simulation.StaffChain.Of( 3 )!.Serial;
		staff.X += 1 << 12;
		simulation.StaffChain.Follow( 3, staff );
		Assert.AreEqual( serial, simulation.StaffChain.Of( 3 )!.Serial, "the move inside the cell only rewrites the position (0x0050B6A0)" );
		CollectionAssert.AreEqual( new[] { guest.Navigation, staff }, Chain( simulation, 2, 5 ) );
		simulation.StaffChain.Remove( 3 );
		CollectionAssert.AreEqual( new[] { guest.Navigation }, Chain( simulation, 2, 5 ) );
	}

	[TestMethod]
	public void AGuestOnARideOrCarriedStaysOnTheChainAtItsLastPoint()
	{
		var simulation = Simulation();
		var riding = simulation.SpawnInPark( 2, 5 );
		var carried = simulation.SpawnInPark( 2, 5 );
		var walking = simulation.SpawnInPark( 2, 5 );
		var (x, y) = (riding.Navigation.X, riding.Navigation.Y);
		riding.State = GuestState.Using;
		carried.State = GuestState.HeldByGuard;
		simulation.Tick( GuestSnapshotTests.Tick );
		CollectionAssert.AreEqual( new[] { walking.Navigation, carried.Navigation, riding.Navigation }, Chain( simulation, 2, 5 ), "no state test: the riding and the carried guest are neighbours" );
		Assert.AreEqual( (x, y), (riding.Navigation.X, riding.Navigation.Y), "nothing moves a riding guest" );
		var separation = (Func<GuestNavigation, (int X, int Y)>)Delegate.CreateDelegate( typeof( Func<GuestNavigation, (int X, int Y)> ), simulation,
			typeof( GuestSimulation ).GetMethod( "SeparationForStep", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )! );
		Assert.AreNotEqual( (0, 0), separation( walking.Navigation ), "the walker is pushed away from the guests it passes" );
		// a guest that is gone is unlinked at once
		riding.State = GuestState.Gone;
		Assert.IsFalse( Chain( simulation, 2, 5 ).Contains( riding.Navigation ) );
	}

	[TestMethod]
	public void AStaffMemberThatEnteredLaterHeadsTheCell()
	{
		var simulation = Simulation();
		var guest = simulation.SpawnInPark( 2, 5 );
		Assert.AreSame( guest, simulation.HeadOfCell( 2, 5 ) );
		simulation.StaffChain.Follow( 1, new GuestNavigation { X = guest.Navigation.X, Y = guest.Navigation.Y } );
		Assert.IsNull( simulation.HeadOfCell( 2, 5 ), "the head thing is the staff member's" );
		var other = simulation.SpawnInPark( 2, 5 );
		Assert.AreSame( other, simulation.HeadOfCell( 2, 5 ) );
	}

	[TestMethod]
	public void TheStaffWorldFilesItsMembersAndKeepsALiftedOneOnItsCell()
	{
		var park = EconomyTestData.Park();
		var grid = GuestTests.CreateCross();
		var simulation = new GuestSimulation( grid, GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var world = new StaffWorldSimulation( grid, () => park.Staff.Members, _ => null, (2, 5) ) { Guests = simulation };
		var member = EconomyTestData.HireBest( park, StaffType.Guard );
		var guest = simulation.SpawnInPark( 2, 5 );
		member.Navigation = new GuestNavigation { X = GuestNavigation.Centre( (2, 5) ).X, Y = GuestNavigation.Centre( (2, 5) ).Y };
		world.BeginStaffTurn( park, 1 );
		Assert.AreEqual( (2, 5), simulation.StaffChain.Of( member.Id )!.Cell );
		Assert.IsNull( simulation.HeadOfCell( 2, 5 ), "the member entered after the guest" );
		// lifted: no walk, but the thing stays on the cell it was lifted from (nothing writes its position until the drop)
		member.Navigation = null;
		world.BeginStaffTurn( park, 2 );
		Assert.AreEqual( (2, 5), simulation.StaffChain.Of( member.Id )!.Cell );
		Assert.IsTrue( Chain( simulation, 2, 5 ).Count == 2 );
		// dropped on another cell: the head of that cell's chain
		member.Navigation = new GuestNavigation { X = GuestNavigation.Centre( (3, 5) ).X, Y = GuestNavigation.Centre( (3, 5) ).Y };
		world.BeginStaffTurn( park, 3 );
		Assert.AreEqual( (3, 5), simulation.StaffChain.Of( member.Id )!.Cell );
		Assert.AreSame( guest, simulation.HeadOfCell( 2, 5 ) );
		// a fired member leaves the chains
		park.Staff.Fire( member.Id );
		world.BeginStaffTurn( park, 4 );
		Assert.IsNull( simulation.StaffChain.Of( member.Id ) );
	}

	[TestMethod]
	public void TheEntryNumbersOfTheStaffSurviveASnapshotAndAnOlderSaveFilesThemOldest()
	{
		var park = EconomyTestData.Park();
		var grid = GuestTests.CreateCross();
		var simulation = new GuestSimulation( grid, GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var world = new StaffWorldSimulation( grid, () => park.Staff.Members, _ => null, (2, 5) ) { Guests = simulation };
		var a = EconomyTestData.HireBest( park, StaffType.Guard );
		var b = EconomyTestData.HireBest( park, StaffType.Handyman );
		simulation.SpawnInPark( 2, 5 );
		a.Navigation = new GuestNavigation { X = GuestNavigation.Centre( (2, 5) ).X, Y = GuestNavigation.Centre( (2, 5) ).Y };
		b.Navigation = new GuestNavigation { X = GuestNavigation.Centre( (2, 5) ).X, Y = GuestNavigation.Centre( (2, 5) ).Y };
		world.BeginStaffTurn( park, 1 );
		var snapshot = world.CaptureSnapshot();
		var serials = snapshot.Actors.ToDictionary( actor => actor.Id, actor => actor.CellSerial );
		Assert.IsTrue( serials[b.Id] > serials[a.Id] && serials[a.Id] > 0 );
		world.RestoreSnapshot( snapshot );
		Assert.AreEqual( serials[a.Id], world.CaptureSnapshot().Actors.Single( actor => actor.Id == a.Id ).CellSerial );
		var older = new StaffWorldSimulation.StaffWorldStateSnapshot( snapshot.Actors.Select( actor => actor with { CellSerial = 0 } ).ToArray() );
		world.RestoreSnapshot( older );
		var legacy = world.CaptureSnapshot().Actors.ToDictionary( actor => actor.Id, actor => actor.CellSerial );
		Assert.IsTrue( legacy.Values.All( serial => serial == 0 ), "an older save files the members oldest: the entry numbers below 0 are written as 0, so the next save reloads" );
		// restore the older save, capture, validate and restore again: the second save loads into the same order
		var again = world.CaptureSnapshot();
		world.ValidateSnapshot( again );
		world.RestoreSnapshot( again );
		var order = simulation.StaffChain.At( (2, 5) )!.Select( entry => entry.Id ).ToArray();
		CollectionAssert.AreEqual( new[] { b.Id, a.Id }, order, "the first member is head-most of the legacy order, last in the oldest-first list" );
		var broken = new StaffWorldSimulation.StaffWorldStateSnapshot( snapshot.Actors.Select( actor => actor with { CellSerial = -5 } ).ToArray() );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => world.ValidateSnapshot( broken ) );
	}

	[TestMethod]
	public void AGoneGuestStillHeadsItsCellForTheHeadTestsButNotForTheSeparationQuery()
	{
		// [BIN:TPW-EXE:0x0050B780] a destroyed thing is kind 2 until the turn's deferred destroys unlink it
		var simulation = Simulation();
		var first = simulation.SpawnInPark( 2, 5 );
		var last = simulation.SpawnInPark( 2, 5 );
		last.State = GuestState.Gone;
		Assert.AreSame( last, simulation.HeadOfCell( 2, 5 ), "the head test still meets it" );
		CollectionAssert.AreEqual( new[] { first.Navigation }, Chain( simulation, 2, 5 ), "the separation query skips it" );
	}

	[TestMethod]
	public void AGuardsSetDownIsATeleportThatFilesTheGuestAndResetsTheDrawnStart()
	{
		var simulation = Simulation();
		var guest = simulation.SpawnInPark( 2, 5 );
		((IGuardGuests)simulation).Release( guest.Id, (5, 2), null );
		Assert.AreSame( guest, simulation.HeadOfCell( guest.CellX, guest.CellY ), "filed on its new cell's chain at once" );
		Assert.AreEqual( guest.X, guest.StepFromX, 0.0001f, "the drawn start is the new point: no slide from the old cell" );
		Assert.AreEqual( guest.Y, guest.StepFromY, 0.0001f );
	}
}
