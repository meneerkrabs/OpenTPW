using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Guests held by a guard in an original save (state 17, held by 0x004FEB10; the guard's state 18, 0x004D6350): the import keeps the guest held and its escorting guard walking on.
/// No corpus save holds such a guest, so these are synthetic saves.
/// </summary>
[TestClass]
public class HeldGuestImportTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static ushort At( int cell, int sub = 128 ) => (ushort)(cell * 256 + sub);

	private static SaveGuest Decode( SaveTestPayloads.GuestFields fields, int id )
	{
		var body = SaveTestPayloads.Guest( fields );
		var payload = new byte[8 + body.Length];
		body.CopyTo( payload, 8 );
		return SaveGuest.Read( payload, new SaveThing( id, SaveThingModel.Guest, 0, 0, 8, body.Length ) );
	}

	private static GuestPathGrid Grid()
	{
		var grid = new GuestPathGrid( 20, 20 );
		for ( var x = 0; x < 16; x++ )
			grid.SetPath( x, 5, true );
		return grid;
	}

	private static GuestSimulation Simulation()
	{
		var settings = new GuestSettings { ArrivalLaneA = new[] { (10, 14), (10, 12), (10, 10), (10, 8), (10, 6) } };
		settings.Types.Add( new GuestType( 60, 600, 40 ) );
		return new GuestSimulation( Grid(), settings, 5 ) { ArrivalsEnabled = false };
	}

	[TestMethod]
	public void AGuestHeldByAGuardIsImportedHeldWithItsSavedBankAndNoBalloon()
	{
		var simulation = Simulation();
		var random = simulation.CaptureSnapshot().RandomState;
		var saved = new[]
		{
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 6 ), At( 5 )), State = 17, SpriteId = 5 }, 41 ),
			// a costume guest holds the costume bank in the field; the kid set it returns to is the draw its id gives again
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 7 ), At( 5 )), State = 17, EspSprite = 2, SpriteId = 0 }, 42 ),
			// the hold clears mBalloonScript (0x004FE950): a save that still names one is imported without the balloon
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 8 ), At( 5 )), State = 17, BalloonScript = 3, BalloonLife = 120 }, 43 ),
			// off the paths: the held guest stands nowhere, so the cell only has to be on the map
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 3 ), At( 2 )), State = 17 }, 44 ),
		};
		var report = simulation.ImportOriginal( saved, 755, _ => 0, _ => 0 );
		Guest Of( int id ) => simulation.Guests.Single( guest => guest.Id == id );
		Assert.AreEqual( 4, simulation.Guests.Count );
		Assert.IsTrue( simulation.Guests.All( guest => guest.State == GuestState.HeldByGuard && !guest.IsVisible ) );
		Assert.AreEqual( 5u, Of( 41 ).KidSetDraw, "the saved bank number, reduced by the loaded count at drawing (0x004F93AA)" );
		Assert.AreEqual( GuestAppearance.RawKidDraw( 42 ), Of( 42 ).KidSetDraw, "a costume guest's kid set is the id's own draw" );
		Assert.IsFalse( Of( 43 ).HasBalloon );
		Assert.AreEqual( 120, Of( 43 ).BalloonLife, "the life stays" );
		Assert.IsTrue( report.Any( line => line.Contains( "guest 43 held by a guard saved with a balloon script", StringComparison.Ordinal ) ) );
		Assert.IsFalse( report.Any( line => line.Contains( "Guests not imported", StringComparison.Ordinal ) ), "none of them is dropped any more" );
		StringAssert.Contains( report.Single( line => line.StartsWith( "Guests imported:", StringComparison.Ordinal ) ), "4 of 4" );
		Assert.AreEqual( random, simulation.CaptureSnapshot().RandomState, "the import draws nothing" );
		Assert.IsTrue( simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).IsValid, string.Join( "; ", simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).Errors ) );
		Assert.IsTrue( GuestSimulation.ImportsOriginalState( 17 ) );
	}

	[TestMethod]
	public void AHeldGuestIsKeptOnlyWhileAGuardEscortsItAndOtherwiseDestroyedAtItsFirstUpdate()
	{
		foreach ( var escorted in new[] { true, false } )
		{
			var simulation = Simulation();
			simulation.IsEscorted = id => escorted && id == 41;
			simulation.ImportOriginal( new[] { Decode( new SaveTestPayloads.GuestFields { Position = (At( 6 ), At( 5 )), State = 17 }, 41 ) }, 755, _ => 0, _ => 0 );
			for ( var tick = 0; tick < 600; tick++ )
				simulation.Tick( 1f / 60 );
			Assert.AreEqual( escorted, simulation.Find( 41 ) is { State: GuestState.HeldByGuard }, escorted ? "a guard in state 18 holds it" : "no guard holds it: destroyed (0x0050B780)" );
		}
	}

	[TestMethod]
	public void AGuardEscortingASavedGuestKeepsHisEscort()
	{
		var settings = OriginalSaveImportTests.Settings();
		foreach ( var guestState in new[] { 17u, 7u } )
		{
			var guard = SaveTestPayloads.Staff( SaveThingModel.Guard, state: 18, role: new byte[] { 41, 0, 9, 0, 0, 0 }, position: (At( 10 ), At( 5 )), destination: (At( 14 ), At( 5 )) );
			var guest = SaveTestPayloads.Guest( new SaveTestPayloads.GuestFields { State = guestState } );
			var save = OriginalSaveImportTests.Park( settings, extraThings: new[] { (43u, (uint)SaveThingModel.Guard, guard), (41u, (uint)SaveThingModel.Guest, guest) } );
			var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
			OriginalEconomyImport.Apply( economy, save );
			var member = economy.Staff.Members.Single( item => item.Type == StaffType.Guard );
			Assert.AreEqual( (StaffJob.Escorting, 41), (member.Job, member.TargetGuestId), "mPerp (+0x212) names the guest, whose thing id is its guest id, whatever state it is in (0x004D6A72 tests none)" );
			Assert.AreEqual( (14 * 256 + 128, 5 * 256 + 128), member.ResumeDestination, "the walk goes on to the saved destination (0x004F9407)" );
		}
	}

	[TestMethod]
	public void TheImportedGuardCarriesTheImportedGuestOnAndSetsItDown()
	{
		var settings = OriginalSaveImportTests.Settings();
		var guard = SaveTestPayloads.Staff( SaveThingModel.Guard, state: 18, role: new byte[] { 41, 0, 9, 0, 0, 0 }, position: (At( 10 ), At( 5 )), destination: (At( 14 ), At( 5 )) );
		var guest = SaveTestPayloads.Guest( new SaveTestPayloads.GuestFields { Position = (At( 9 ), At( 5 )), State = 17 } );
		var save = OriginalSaveImportTests.Park( settings, extraThings: new[] { (43u, (uint)SaveThingModel.Guard, guard), (41u, (uint)SaveThingModel.Guest, guest) } );
		var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		OriginalEconomyImport.Apply( park, save );
		var member = park.Staff.Members.Single( item => item.Type == StaffType.Guard );
		park.OpenPark();
		var grid = Grid();
		var guestSettings = GuestTests.CreateSettings();
		var guests = new GuestSimulation( grid, guestSettings, 5 ) { ArrivalsEnabled = false };
		guests.ImportOriginal( save.Guests, ParkCalendar.Turn( park.Tick ), _ => 0, _ => 0 );
		var world = new StaffWorldSimulation( grid, () => park.Staff.Members, _ => null, (0, 5) ) { Guests = guests };
		guests.IsEscorted = world.IsEscorting;
		park.StaffWorld = world;
		world.Synchronize();
		Assert.AreEqual( (StaffJob.Escorting, 41), (member.Job, member.TargetGuestId) );
		var held = guests.Find( 41 )!;
		Assert.AreEqual( GuestState.HeldByGuard, held.State );
		var heldTurns = 0;
		for ( var turn = 0; turn < 60 && member.Job == StaffJob.Escorting; turn++ )
		{
			var next = park.Turn + 1;
			while ( ParkCalendar.Turn( guests.TickCount ) < next )
				guests.Tick( GuestSnapshotTests.Tick );
			EconomyTestData.AdvanceToTurn( park, next );
			if ( member.Job == StaffJob.Escorting )
			{
				Assert.AreEqual( GuestState.HeldByGuard, guests.Find( 41 )?.State, "held for as long as the guard escorts it" );
				heldTurns++;
			}
		}
		Assert.IsTrue( heldTurns > 0, "the guard walked on with the guest" );
		Assert.AreNotEqual( StaffJob.Escorting, member.Job, "he arrived and set it down" );
		Assert.AreNotEqual( GuestState.HeldByGuard, guests.Find( 41 )?.State, "set down (or destroyed where no gate is reachable)" );
	}
	[TestMethod]
	public void AnEscortOnTheOffPathRouteKeepsItsWalkMaskAndReachesABoothOffTheConcretePaths()
	{
		// [BIN:TPW-EXE:0x004D6977 escort walk mask] the second try to the booth runs with +0x188 = 1, which the saved nav_mode keeps; the loader's route uses it
		foreach ( var navMode in new[] { 1, 0 } )
		{
			var settings = OriginalSaveImportTests.Settings();
			var guard = SaveTestPayloads.Staff( SaveThingModel.Guard, state: 18, role: new byte[] { 41, 0, 9, 0, 0, 0 }, position: (At( 10 ), At( 5 )), destination: (At( 12 ), At( 8 )), navMode: navMode );
			var guest = SaveTestPayloads.Guest( new SaveTestPayloads.GuestFields { Position = (At( 9 ), At( 5 )), State = 17 } );
			var save = OriginalSaveImportTests.Park( settings, extraThings: new[] { (43u, (uint)SaveThingModel.Guard, guard), (41u, (uint)SaveThingModel.Guest, guest) } );
			var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
			OriginalEconomyImport.Apply( park, save );
			var member = park.Staff.Members.Single( item => item.Type == StaffType.Guard );
			Assert.AreEqual( navMode == 1 ? 1 : (int?)null, member.ResumeMask );
			park.OpenPark();
			var grid = Grid();
			var guests = new GuestSimulation( grid, GuestTests.CreateSettings(), 5 ) { ArrivalsEnabled = false };
			guests.ImportOriginal( save.Guests, ParkCalendar.Turn( park.Tick ), _ => 0, _ => 0 );
			var world = new StaffWorldSimulation( grid, () => park.Staff.Members, _ => null, (0, 5) ) { Guests = guests };
			guests.IsEscorted = world.IsEscorting;
			park.StaffWorld = world;
			world.Synchronize();
			world.BeginStaffTurn( park, park.Turn );
			var walking = member.Job == StaffJob.Escorting && member.Navigation!.Mask == navMode;
			Assert.IsTrue( navMode == 1 ? walking : member.Navigation!.Mask == 0, "the mask the route runs with is the saved one" );
			if ( navMode == 1 )
			{
				Assert.AreEqual( StaffJob.Escorting, member.Job, "the route off the paths is found, so the escort goes on" );
				Assert.IsTrue( member.Navigation!.Cell != (12, 8) );
			}
		}
	}
	[TestMethod]
	public void EveryGuardNamingARestoredGuestEscortsItAndBothSetItDown()
	{
		// [BIN:TPW-EXE:0x004D6A50 escort arrival] the arrival sets down whatever thing +0x212 names without a state test, and 0x00501C94 never changes a guard's state
		var settings = OriginalSaveImportTests.Settings();
		byte[] Guard() => SaveTestPayloads.Staff( SaveThingModel.Guard, state: 18, role: new byte[] { 41, 0, 9, 0, 0, 0 }, position: (At( 10 ), At( 5 )), destination: (At( 14 ), At( 5 )) );
		var guest = SaveTestPayloads.Guest( new SaveTestPayloads.GuestFields { Position = (At( 9 ), At( 5 )), State = 17 } );
		var save = OriginalSaveImportTests.Park( settings, extraThings: new[] { (44u, (uint)SaveThingModel.Guard, Guard()), (43u, (uint)SaveThingModel.Guard, Guard()), (41u, (uint)SaveThingModel.Guest, guest) } );
		var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		OriginalEconomyImport.Apply( park, save );
		var guards = park.Staff.Members.Where( item => item.Type == StaffType.Guard ).ToList();
		Assert.AreEqual( 2, guards.Count( item => item.Job == StaffJob.Escorting && item.TargetGuestId == 41 ), "both guards escort" );
		park.OpenPark();
		var grid = Grid();
		var guests = new GuestSimulation( grid, GuestTests.CreateSettings(), 5 ) { ArrivalsEnabled = false };
		guests.ImportOriginal( save.Guests, ParkCalendar.Turn( park.Tick ), _ => 0, _ => 0 );
		Assert.AreEqual( 0, OriginalEconomyImport.IdleEscortsWithoutARestoredGuest( park, id => guests.Find( id ) != null ).Count, "a restored guest keeps its escorts" );
		var world = new StaffWorldSimulation( grid, () => park.Staff.Members, _ => null, (0, 5) ) { Guests = guests };
		guests.IsEscorted = world.IsEscorting;
		park.StaffWorld = world;
		world.Synchronize();
		var setDowns = 0;
		for ( var turn = 0; turn < 80 && guards.Any( item => item.Job == StaffJob.Escorting ); turn++ )
		{
			var next = park.Turn + 1;
			while ( ParkCalendar.Turn( guests.TickCount ) < next )
				guests.Tick( GuestSnapshotTests.Tick );
			EconomyTestData.AdvanceToTurn( park, next );
			setDowns += world.TakeVoices().Count( voice => voice.Event == StaffWorldSimulation.SetDownEvent );
		}
		Assert.IsTrue( guards.All( item => item.Job != StaffJob.Escorting ), "both arrived" );
		Assert.AreEqual( 2, setDowns, "both set the guest down (cat_kids 128 twice)" );
		// the guest import kept nobody: every escort goes idle
		Assert.AreEqual( 0, OriginalEconomyImport.IdleEscortsWithoutARestoredGuest( park, _ => true ).Count );
	}

	[TestMethod]
	public void AnEscortOfAGuestTheImportDidNotRestoreIsIdled()
	{
		var settings = OriginalSaveImportTests.Settings();
		var guard = SaveTestPayloads.Staff( SaveThingModel.Guard, state: 18, role: new byte[] { 41, 0, 9, 0, 0, 0 }, position: (At( 10 ), At( 5 )), destination: (At( 14 ), At( 5 )) );
		var guest = SaveTestPayloads.Guest( new SaveTestPayloads.GuestFields { Position = (At( 9 ), At( 5 )), State = 17 } );
		var save = OriginalSaveImportTests.Park( settings, extraThings: new[] { (43u, (uint)SaveThingModel.Guard, guard), (41u, (uint)SaveThingModel.Guest, guest) } );
		var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		OriginalEconomyImport.Apply( park, save );
		var member = park.Staff.Members.Single( item => item.Type == StaffType.Guard );
		var lines = OriginalEconomyImport.IdleEscortsWithoutARestoredGuest( park, _ => false );
		Assert.AreEqual( 1, lines.Count );
		Assert.AreEqual( (StaffJob.Idle, 0), (member.Job, member.TargetGuestId) );
	}
}
