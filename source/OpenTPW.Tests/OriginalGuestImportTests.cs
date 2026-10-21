using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The guests of an original save (tp.exe guest serializer <c>0x004FB530</c> after the person block <c>0x004F8B10</c>): decoded field by field and
/// restored into <see cref="GuestSimulation"/> with their thing ids, meters, walk and state, without drawing from the guest stream.
/// </summary>
[TestClass]
public class OriginalGuestImportTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static SaveGuest Decode( SaveTestPayloads.GuestFields fields, int id = 40 )
	{
		var body = SaveTestPayloads.Guest( fields );
		var payload = new byte[8 + body.Length];
		body.CopyTo( payload, 8 );
		return SaveGuest.Read( payload, new SaveThing( id, SaveThingModel.Guest, 0, 0, 8, body.Length ) );
	}

	[TestMethod]
	public void TheGuestBodyDecodesInTheSerializersOrder()
	{
		var saved = Decode( new SaveTestPayloads.GuestFields
		{
			Position = (300, 400), Destination = (310, 420), BaseSpeed = 140, EspSprite = 2, Cash = 684, ExitLevel = 142, Happiness = 61, Hunger = 18, Litter = 7, Thirst = 36,
			Meter1B8 = 3, Toilet = 13, Illness = 4, MajorDest = 15, NumRides = 2, PersonType = 5, PrankeryIndex = 102, PreviousRides = new ushort[] { 15, 13, 0, 0 },
			PreviousTemporaryRides = new ushort[] { 14, 0, 0, 0 }, QueueMoveDelay = 3, BalloonLife = 90, BalloonScript = 7, State = 7, TimeOfLastSpotAnim = 700, TimeStartedIdling = 720
		} );
		Assert.AreEqual( SaveThingChain.FixedBodyBytes[SaveThingModel.Guest], SaveTestPayloads.Guest( new SaveTestPayloads.GuestFields() ).Length, "person 390 + guest 135" );
		var person = saved.Person;
		Assert.AreEqual( ((ushort)300, (ushort)400, (ushort)310, (ushort)420, (ushort)140, 2u), (person.Base.X, person.Base.Y, person.AccurateDestX, person.AccurateDestY, person.BaseSpeed, person.EspSprite) );
		Assert.AreEqual( (684, 142, 61f, 18f, 7f, 36f, 3f, 13f, 4f), (saved.Cash, saved.ExitLevel, saved.Happiness, saved.Hunger, saved.Litter, saved.Thirst, saved.Meter1B8, saved.Toilet, saved.Illness) );
		Assert.AreEqual( ((ushort)15, 2u, (byte)5, (byte)102, 3u, 90, 7u, 7u, 700u, 720u),
			(saved.MajorDest, saved.NumRides, saved.PersonType, saved.PrankeryIndex, saved.QueueMoveDelay, saved.RemainingBalloonLife, saved.BalloonScript, saved.State, saved.TimeOfLastSpotAnim, saved.TimeStartedIdling) );
		CollectionAssert.AreEqual( new ushort[] { 15, 13, 0, 0 }, saved.PreviousRides.ToArray(), "mPreviousRides[i] interleaved with mPreviousTemporaryRides[i]" );
		CollectionAssert.AreEqual( new ushort[] { 14, 0, 0, 0 }, saved.PreviousTemporaryRides.ToArray() );
		Assert.ThrowsException<InvalidDataException>( () => Decode( new SaveTestPayloads.GuestFields { Hunger = 300 } ), "a needs byte is at most 255" );
	}

	/// <summary>A 20 × 20 grid with a path along y = 5 and one cell (10, 6); arrival lane A comes up x = 10 from the road at y = 14.</summary>
	private static GuestSimulation Simulation()
	{
		var grid = new GuestPathGrid( 20, 20 );
		for ( var x = 0; x < 16; x++ )
			grid.SetPath( x, 5, true );
		grid.SetPath( 10, 6, true );
		var settings = new GuestSettings { ArrivalLaneA = new[] { (10, 14), (10, 12), (10, 10), (10, 8), (10, 6) } };
		settings.Types.Add( new GuestType( 60, 600, 40 ) );
		return new GuestSimulation( grid, settings, 5 ) { ArrivalsEnabled = false };
	}

	private static ushort At( int cell, int sub = 128 ) => (ushort)(cell * 256 + sub);

	[TestMethod]
	public void ParkGuestsKeepTheirIdsMetersAndWalks()
	{
		var simulation = Simulation();
		var shop = new RideVisitorBridge( 3, "Drinks Shop", RideVisitorKind.Shop, 1, 70, 30, GuestNeeds.Thirst ) { EntranceCell = (6, 4), ExitCell = (6, 4) };
		simulation.Register( shop );
		var random = simulation.CaptureSnapshot().RandomState;
		var saved = new[]
		{
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 3 ), At( 5 )), State = 6, TimeStartedIdling = 750, PrankeryIndex = 101, Cash = 345, ArrivalIndex = 7 }, 41 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 4 ), At( 5 )), Destination = (At( 12, 64 ), At( 5, 192 )), State = 7, BaseSpeed = 140, EspSprite = 2 }, 36 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 6 ), At( 5 )), State = 10, MajorDest = 15, PreviousRides = new ushort[] { 15, 99, 0, 0 } }, 38 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 7 ), At( 5 )), State = 9, MajorDest = 17, BalloonScript = 3, BalloonLife = 120 }, 39 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 8 ), At( 5 )), State = 18 }, 40 ),
		};
		var report = simulation.ImportOriginal( saved, 755, thing => thing == 15 ? 3 : 0, thing => thing == 17 ? 21 : 0, visitorsToDate: 9 );
		Assert.IsTrue( simulation.Guests.All( guest => guest.BodySpriteHistoryReconstructed ) );
		Assert.IsTrue( simulation.Guests.All( guest => guest.BodySprite.Player?.Output == null ), "import never claims a connected original sprite history" );
		CollectionAssert.AreEqual( new[] { 36, 38, 39, 40, 41 }, simulation.Guests.Select( guest => guest.Id ).ToArray(), "thing ids, in ascending order" );
		Assert.AreEqual( random, simulation.CaptureSnapshot().RandomState, "the import draws nothing from the guest stream" );
		Guest Of( int id ) => simulation.Guests.Single( guest => guest.Id == id );
		var deciding = Of( 41 );
		Assert.AreEqual( (GuestState.DoingNothing, 345, 101, 0L), (deciding.State, deciding.Money, deciding.PrankFlag, deciding.QueueStandingSinceTurn),
			"state 6; the decision stamp 750 lies before the save's turn 755 and counts from the import" );
		Assert.AreEqual( (50f, 20f, 30f, 10f, 2f, 5f, 90), (deciding.Happiness, deciding.Hunger, deciding.Thirst, deciding.Toilet, deciding.Nausea, deciding.Litter, deciding.ExitLevel) );
		Assert.AreEqual( (3, 5, true), (deciding.CellX, deciding.CellY, deciding.IsOnGridForTest()) );
		Assert.AreEqual( (7, 0, 9), (deciding.VisitorNumber, Of( 36 ).VisitorNumber, simulation.VisitorCount), "mArrivalIndex is the visitor number and mNumberOfVisitorsToDate the park's count (0x0051AAF0)" );
		var walker = Of( 36 );
		Assert.AreEqual( (GuestState.WalkingAround, 140, true, true), (walker.State, walker.BaseSpeed, walker.InCostume, walker.HasWaypointForTest()), "state 7 walks on to mAccurateDestX/Y" );
		var toRide = Of( 38 );
		Assert.AreEqual( (GuestState.GoingToRide, 3), (toRide.State, toRide.AttractionId) );
		CollectionAssert.AreEqual( new[] { 3, 0, 0, 0 }, toRide.UsedHistory, "remembered attractions map to their bridges; one the import does not know is empty" );
		var toBin = Of( 39 );
		Assert.AreEqual( (GuestState.ThrowingAwayLitter, 21, true, 120), (toBin.State, toBin.BinId, toBin.HasBalloon, toBin.BalloonLife) );
		Assert.AreEqual( GuestState.LeavingPark, Of( 40 ).State );
		Assert.IsTrue( simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).IsValid, string.Join( "; ", simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).Errors ) );
		StringAssert.Contains( report.Single( line => line.StartsWith( "Guests imported:", StringComparison.Ordinal ) ), "5 of 5" );
		var spawned = simulation.SpawnInPark( 2, 5 );
		Assert.AreEqual( 1, spawned.Id, "a new guest takes the lowest free slot of the pool: the imported guests keep their own slots, and a load rebuilds the free list ascending (0x00518D43)" );
		for ( var tick = 0; tick < 600; tick++ )
			simulation.Tick( 1f / 60 );
		Assert.IsTrue( simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).IsValid );
	}

	[TestMethod]
	public void ArrivingGuestsKeepTheirStatesOnTheCellMapAndQueuedGuestsAreReported()
	{
		var simulation = Simulation();
		var saved = new[]
		{
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 10 ), At( 11 )), State = 2 }, 30 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 10 ), At( 8 )), State = 3, ParkOpeningWaitingTime = 250 }, 31 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 10 ), At( 7 )), State = 5 }, 32 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 10 ), At( 8 )), State = 4 }, 33 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 10 ), At( 13 )), State = 19 }, 34 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 6 ), At( 5 )), State = 11 }, 35 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 6 ), At( 5 )), State = 16 }, 36 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 2 ), At( 2 )), State = 6 }, 37 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 10 ), At( 8 )), State = 3, PaidAdmission = 0 }, 38 ),
		};
		var report = simulation.ImportOriginal( saved, 755, _ => 0, _ => 0 );
		Guest Of( int id ) => simulation.Guests.Single( guest => guest.Id == id );
		// every arrival state stays what it was saved as, on the cell map (the original's guests walk it from their creation, tp.exe 0x004FB201)
		Assert.AreEqual( GuestState.GoingToTicketBooth, Of( 30 ).State );
		Assert.AreEqual( (GuestState.WaitingAtBooth, true, 250), (Of( 31 ).State, Of( 31 ).Paid, Of( 31 ).BoothWait), "state 3 keeps mPaidAdmission and the wait +0x200" );
		Assert.AreEqual( (GuestState.EnteringPark, GuestState.AtTicketBooth, GuestState.CrossingRoadHome), (Of( 32 ).State, Of( 33 ).State, Of( 34 ).State) );
		Assert.AreEqual( 0, Of( 34 ).Navigation.Mask, "state 19 keeps the saved nav_mode 0 (0x0050D150): a guest whose first gate route succeeded with mask 0 (0x004FEC90) crosses home with it" );
		Assert.AreEqual( (GuestState.WaitingAtBooth, false), (Of( 38 ).State, Of( 38 ).Paid), "an unpaid guest of state 3 goes back to the booth or home on the next update" );
		Assert.IsTrue( simulation.Guests.All( guest => guest.Lane == null && guest.OnGrid ) );
		Assert.AreEqual( 7, simulation.Guests.Count, "the deciding guest 37 off the paths is imported where it stands (0x004FB530)" );
		Assert.AreEqual( (GuestState.DoingNothing, 2, 2), (Of( 37 ).State, Of( 37 ).CellX, Of( 37 ).CellY) );
		StringAssert.EndsWith( report.Single( line => line.StartsWith( "[APPROX:SAVE-002] Guests not imported:", StringComparison.Ordinal ) ), "guest 35 (state 11), guest 36 (state 16)." );
		Assert.IsTrue( simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).IsValid, string.Join( "; ", simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).Errors ) );
		for ( var tick = 0; tick < 6000 && simulation.Guests.Any( guest => guest.Id is 30 or 31 or 32 && guest.State != GuestState.DoingNothing ); tick++ )
			simulation.Tick( 1f / 60 );
		Assert.IsTrue( new[] { 30, 31, 32 }.All( id => Of( id ).State is not (GuestState.GoingToTicketBooth or GuestState.EnteringPark) ), "they reached the booth and the park" );
		Assert.ThrowsException<InvalidOperationException>( () => simulation.ImportOriginal( saved, 755, _ => 0, _ => 0 ), "only into an empty simulation" );
	}

	[TestMethod]
	public void ALaneShorterThanTheFixedCellsAndAGuestOffTheMapAreDropped()
	{
		var grid = new GuestPathGrid( 20, 20 );
		grid.SetPath( 10, 6, true );
		var settings = new GuestSettings { ArrivalLaneA = new[] { (10, 8), (10, 6) } };
		var simulation = new GuestSimulation( grid, settings, 5 ) { ArrivalsEnabled = false };
		var saved = new[]
		{
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 10 ), At( 7 )), State = 18, PersonType = 0 }, 30 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 10 ), At( 7 )), State = 1, PersonType = 0 }, 31 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 30 ), At( 7 )), State = 2, PersonType = 0 }, 33 ),
		};
		var report = simulation.ImportOriginal( saved, 755, _ => 0, _ => 0 );
		Assert.AreEqual( 0, simulation.Guests.Count, "the arrival walk needs the level's five fixed cells (bus stop, both crossings, booth, entrance)" );
		StringAssert.Contains( report.Single( line => line.StartsWith( "[APPROX:SAVE-002] Guests not imported:", StringComparison.Ordinal ) ), "guest 30 (state 18, no arrival lane), guest 31 (state 1, no arrival lane), guest 33 (state 2 outside the map at (30, 7))" );
		Assert.IsTrue( simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).IsValid );
	}

	[TestMethod]
	public void GuestsOnTheWayHomeFromTheGateKeepTheirStatesOnTheCellMap()
	{
		var grid = new GuestPathGrid( 20, 20 );
		for ( var x = 0; x < 16; x++ )
			grid.SetPath( x, 5, true );
		grid.SetPath( 10, 6, true );
		var settings = new GuestSettings { ArrivalLaneA = new[] { (10, 14), (10, 12), (10, 10), (10, 8), (10, 6) } };
		settings.Types.Add( new GuestType( 60, 600, 40 ) );
		var simulation = new GuestSimulation( grid, settings, 5 ) { ArrivalsEnabled = false };
		var saved = new[]
		{
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 10 ), At( 7 )), Destination = (At( 10 ), At( 10 )), State = 18, PersonType = 0, NavMode = 1 }, 30 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 10 ), At( 10 )), State = 19, PersonType = 0, NavMode = 1 }, 31 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 10 ), At( 14 )), State = 21, PersonType = 0, NavMode = 1 }, 32 ),
		};
		simulation.ImportOriginal( saved, 755, _ => 0, _ => 0 );
		Assert.AreEqual( (GuestState.LeavingPark, GuestState.CrossingRoadHome, GuestState.WaitingToGoHome), (simulation.Guests[0].State, simulation.Guests[1].State, simulation.Guests[2].State) );
		Assert.IsTrue( simulation.Guests.All( guest => guest.Navigation.Mask == 1 && guest.Lane == null ), "the walk home has the leaving mask, as saved in nav_mode (0x0050D150)" );
		Assert.IsTrue( simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).IsValid );
	}

	/// <summary>
	/// Synthetic: a deciding park guest standing on a queue cell (type 3) that is not part of the path network. The load keeps it where it stands, as on grass (<c>0x004FB530</c>), and reports
	/// the cell type: it is imported, not dropped.
	/// </summary>
	[TestMethod]
	public void AParkGuestStandingOnAQueueCellIsImportedWhereItStands()
	{
		var simulation = Simulation();
		simulation.Grid.SetQueue( 3, 2, -1 );
		var saved = Decode( new SaveTestPayloads.GuestFields { Position = (At( 3 ), At( 2 )), Destination = (At( 8 ), At( 5 )), State = 6 }, 42 );
		var report = simulation.ImportOriginal( new[] { saved }, 755, _ => 0, _ => 0 );
		var guest = simulation.Guests.Single();
		Assert.AreEqual( (GuestState.DoingNothing, 3, 2), (guest.State, guest.CellX, guest.CellY), "imported on the queue cell where it stands" );
		Assert.IsTrue( report.Any( line => line.Contains( "guest 42 state 6 imported off the paths at (3, 2) (cell type 3)", StringComparison.Ordinal ) ), string.Join( "\n", report ) );
		Assert.IsFalse( report.Any( line => line.StartsWith( "[APPROX:SAVE-002] Guests not imported:", StringComparison.Ordinal ) ) );
	}

	/// <summary>
	/// Synthetic: a park guest of each state 6–10 standing on grass at (3, 2), three cells above the path y = 5. tp.exe's load leaves it there (<c>0x004FB530</c>) and routes a
	/// walking state again to its accurate destination (<c>0x004F9407</c>), which draws nothing; its own state handlers bring it back onto a path in its first updates.
	/// </summary>
	[DataTestMethod]
	[DataRow( 6u, GuestState.DoingNothing )]
	[DataRow( 7u, GuestState.WalkingAround )]
	[DataRow( 8u, GuestState.DoingNothing )]
	[DataRow( 9u, GuestState.ThrowingAwayLitter )]
	[DataRow( 10u, GuestState.GoingToRide )]
	public void AParkGuestOffThePathsIsImportedWhereItStandsAndWalksBackOntoAPath( uint state, GuestState imported )
	{
		var simulation = Simulation();
		simulation.Register( new RideVisitorBridge( 3, "Drinks Shop", RideVisitorKind.Shop, 1, 70, 30, GuestNeeds.Thirst ) { EntranceCell = (6, 4), ExitCell = (6, 4) } );
		var random = simulation.CaptureSnapshot().RandomState;
		var saved = Decode( new SaveTestPayloads.GuestFields
		{
			Position = (At( 3 ), At( 2 )), Destination = (At( 8 ), At( 5 )), State = state, MajorDest = state switch { 9 => 17, 10 => 15, _ => 0 }, StrandedTime = 1234
		}, 41 );
		var report = simulation.ImportOriginal( new[] { saved }, 755, thing => thing == 15 ? 3 : 0, thing => thing == 17 ? 21 : 0 );
		Assert.AreEqual( random, simulation.CaptureSnapshot().RandomState, "the import draws nothing: the walk set-up's search is deterministic" );
		var guest = simulation.Guests.Single();
		Assert.AreEqual( (imported, 3, 2, (3, 2)), (guest.State, guest.CellX, guest.CellY, guest.Navigation.Cell), "imported where it stands" );
		Assert.AreEqual( (0, 0), (guest.Navigation.Mask, guest.StrandedMark), "the saved nav_mode 0; the saved stranded mark is cleared as a fresh load clears it" );
		CollectionAssert.Contains( report.ToArray(), $"[BIN:TPW-EXE:0x004F8B10] guest 41 state {state} imported off the paths at (3, 2) (cell type 0); it recovers through its state handler (0x004F9490 off-path search)." );
		CollectionAssert.Contains( report.ToArray(), "[BIN:TPW-EXE:0x004F8B10] guest 41 saved stranded mark 1234 cleared as a fresh load clears it." );
		Assert.IsFalse( report.Any( line => line.StartsWith( "[APPROX:SAVE-002] Guests not imported:", StringComparison.Ordinal ) ) );
		if ( state is 7 or 9 or 10 )
			Assert.IsTrue( guest.HasWaypointForTest() && !guest.Navigation.Stuck && guest.Navigation.Original, "the walk to the accurate destination starts off the paths (0x004D8750)" );
		else
			Assert.IsFalse( guest.HasWaypointForTest(), "states 6 and 8 stand" );
		Assert.IsTrue( simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).IsValid, string.Join( "; ", simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).Errors ) );
		var ticks = 0;
		for ( ; ticks < 60 * 60 && !simulation.Grid.IsWalkable( guest.Navigation.Cell.X, guest.Navigation.Cell.Y ); ticks++ )
			simulation.Tick( 1f / 60 );
		Assert.IsTrue( simulation.Grid.IsWalkable( guest.Navigation.Cell.X, guest.Navigation.Cell.Y ), $"state {state} reached a path; it stands at {guest.Navigation.Cell} in state {guest.State}" );
		Assert.IsTrue( ticks <= MaximumRecoveryTicks, $"state {state} took {ticks} ticks" );
		Assert.IsTrue( simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).IsValid );
	}

	/// <summary>The most ticks an off-path guest of the synthetic grid takes to reach the path (ten seconds: the walk of a few cells, about 400 ticks here).</summary>
	private const int MaximumRecoveryTicks = 60 * 10;

	/// <summary>
	/// Synthetic: a wandering guest on grass walled in by water, with the leaving mask saved. The load's re-route fails without a draw and leaves the walk finished and stuck
	/// (<c>0x0050F8E0</c>: mCantReachDest); its first update reads the stuck walk step (2), draws and wanders or decides again (<c>0x00501ABB</c>).
	/// </summary>
	[TestMethod]
	public void AnEnclosedGuestOffThePathsCannotRenavAndRecoversOnItsFirstUpdate()
	{
		var simulation = Simulation();
		for ( var y = 1; y <= 3; y++ )
			for ( var x = 2; x <= 4; x++ )
				if ( (x, y) != (3, 2) )
					simulation.Grid.Cells.SetType( x, y, ParkCellTypes.Water );
		var random = simulation.CaptureSnapshot().RandomState;
		var saved = Decode( new SaveTestPayloads.GuestFields { Position = (At( 3 ), At( 2 )), Destination = (At( 8 ), At( 5 )), State = 7, NavMode = 1 }, 41 );
		var report = simulation.ImportOriginal( new[] { saved }, 755, _ => 0, _ => 0 );
		Assert.AreEqual( random, simulation.CaptureSnapshot().RandomState, "a failed re-route draws nothing" );
		var guest = simulation.Guests.Single();
		Assert.AreEqual( (GuestState.WalkingAround, 1, true, true), (guest.State, guest.Navigation.Mask, guest.HasWaypointForTest(), guest.Navigation.Stuck), "nav_mode as saved; the walk is stuck" );
		CollectionAssert.Contains( report.ToArray(), "[BIN:TPW-EXE:0x004F8B10] guest 41 could not renav to its accurate destination (8, 5); it recovers on its first update (0x004F9407)." );
		for ( var tick = 0; tick < 60 && random == simulation.CaptureSnapshot().RandomState; tick++ )
			simulation.Tick( 1f / 60 );
		Assert.AreNotEqual( random, simulation.CaptureSnapshot().RandomState, "the first update draws once the walk step answers stuck" );
		Assert.IsTrue( guest.State is GuestState.WalkingAround or GuestState.DoingNothing );
		Assert.AreEqual( (3, 2), (guest.CellX, guest.CellY), "walled in, it stays" );
		Assert.IsTrue( simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).IsValid );
	}

	[TestMethod]
	public void AGuardChasingAnImportedGuestKeepsHisChase()
	{
		var settings = OriginalSaveImportTests.Settings();
		var guard = SaveTestPayloads.Staff( SaveThingModel.Guard, state: 16, role: new byte[] { 41, 0, 9, 0, 0, 0 } );
		var guest = SaveTestPayloads.Guest( new SaveTestPayloads.GuestFields { State = 7 } );
		var save = OriginalSaveImportTests.Park( settings, extraThings: new[] { (43u, (uint)SaveThingModel.Guard, guard), (41u, (uint)SaveThingModel.Guest, guest) } );
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var import = OriginalEconomyImport.Apply( economy, save );
		var member = economy.Staff.Members.Single( item => item.Type == StaffType.Guard );
		Assert.AreEqual( (StaffJob.Chasing, 41, 9L), (member.Job, member.TargetGuestId, member.JobStamp), "mPerp (+0x212) names the guest, whose thing id is its guest id; the chase timer is +0x214" );
		StringAssert.Contains( import.Evidence.Single( line => line.StartsWith( "1 saved guests", StringComparison.Ordinal ) ), "1 in state 7" );
	}

	[TestMethod]
	public void TheVisitCountsAndTheArrivalStampAreImported()
	{
		var simulation = Simulation();
		var saved = new[]
		{
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 3 ), At( 5 )), State = 7, NumRides = 4, NumShops = 3, NumSideshows = 2, NumSideshowsWon = 1, ArrivalDate = 1_000 }, 41 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 4 ), At( 5 )), State = 7, ArrivalDate = 5_000 }, 42 ),
		};
		var report = simulation.ImportOriginal( saved, 1_100, _ => 0, _ => 0 );
		var first = simulation.Guests.Single( guest => guest.Id == 41 );
		Assert.AreEqual( (4, 3, 2, 1), (first.RidesTaken, first.ShopsVisited, first.SideshowsVisited, first.SideshowsWon) );
		// [BIN:TPW-EXE:0x4FD950] mArrivalDate is the game tick of the stamp: 100 turns before the save's turn of 1100
		Assert.AreEqual( 100 * ParkCalendar.TurnMilliseconds / 1000.0, first.TimeInParkSeconds!.Value, 0.001 );
		Assert.AreEqual( 100L, (long)Math.Floor( first.TimeInParkSeconds.Value * 1000 / ParkCalendar.TurnMilliseconds ), "the roster's floor gives the exact turn count" );
		Assert.IsNull( simulation.Guests.Single( guest => guest.Id == 42 ).TimeInParkSeconds, "a stamp after the save's turn is not a date: the time in park stays unknown" );
		Assert.IsFalse( report.Any( line => line.Contains( "shop/sideshow/win counts", StringComparison.Ordinal ) ), "the counts are no longer reported as not imported" );
	}

	[TestMethod]
	public void ImportedIntegerAgeKeepsItsWorldPhaseInsteadOfReflooringSeconds()
	{
		foreach ( var priorTicks in new[] { 0, 73 } )
		{
			var simulation = Simulation();
			for ( var tick = 0; tick < priorTicks; tick++ ) simulation.Tick( GuestSnapshotTests.Tick );
			var saved = new[]
			{
				Decode( new SaveTestPayloads.GuestFields { Position = (At( 3 ), At( 5 )), State = 7, ArrivalDate = 905 }, 41 ),
				Decode( new SaveTestPayloads.GuestFields { Position = (At( 4 ), At( 5 )), State = 7, ArrivalDate = 1006 }, 42 ),
				Decode( new SaveTestPayloads.GuestFields { Position = (At( 10 ), At( 8 )), State = 3, ArrivalDate = 0 }, 43 ),
			};
			simulation.ImportOriginal( saved, 1005, _ => 0, _ => 0 );
			var guest = simulation.Guests.Single( item => item.Id == 41 );
			Assert.AreEqual( 100L, guest.DisplayTimeInParkTurns );
			Assert.IsNull( simulation.Guests.Single( item => item.Id == 42 ).DisplayTimeInParkTurns, "future stamp policy is unchanged" );
			Assert.IsNull( simulation.Guests.Single( item => item.Id == 43 ).DisplayTimeInParkTurns, "on-map arrival has no previously known duration under the existing import policy" );
			for ( var tick = 0; tick < 59; tick++ ) simulation.Tick( GuestSnapshotTests.Tick );
			Assert.AreEqual( 104L, guest.DisplayTimeInParkTurns );
			Assert.AreEqual( 103L, (long)Math.Floor( guest.TimeInParkSeconds!.Value * 1000 / ParkCalendar.TurnMilliseconds ) );
			Assert.IsTrue( simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).IsValid, "import into an already-ticked empty simulation still stores a normalized phase" );
		}
	}

	[TestMethod]
	public void OriginalJungleEasymodeGuestsWalkIntoThePark()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		var original = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		try
		{
			var park = OriginalPark.Load( "jungle" );
			var save = park.Save!;
			Assert.AreEqual( 13, save.Guests.Count );
			var simulation = new GuestSimulation( GuestPathGrid.FromOriginal( park.Map, park.Save ), GuestSettings.Load( "jungle" ), 99 ) { ArrivalsEnabled = false };
			var report = simulation.ImportOriginal( save.Guests, save.World.Variables.GameTick, _ => 0, _ => 0 );
			// Easymode's guests are all on their way in: going to the booth (state 2), paid and waiting for the gate (3) or entering (5)
			Assert.AreEqual( 13, simulation.Guests.Count, string.Join( "; ", report ) );
			foreach ( var saved in save.Guests )
			{
				var guest = simulation.Guests.Single( item => item.Id == saved.Id );
				Assert.AreEqual( saved.State switch { 2 => GuestState.GoingToTicketBooth, 3 => GuestState.WaitingAtBooth, _ => GuestState.EnteringPark }, guest.State, $"guest {saved.Id} state {saved.State}" );
				Assert.AreEqual( (saved.Cash, (int)saved.PersonType, (int)saved.PrankeryIndex, saved.ExitLevel), (guest.Money, guest.Type, guest.PrankFlag, guest.ExitLevel) );
				Assert.AreEqual( (saved.Person.Base.X / 256f, saved.Person.Base.Y / 256f), (guest.X, guest.Y) );
			}
			Assert.IsTrue( simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).IsValid );
			for ( var tick = 0; tick < 60 * 120; tick++ )
				simulation.Tick( 1f / 60 );
			Assert.IsTrue( simulation.Guests.Count( guest => guest.State is GuestState.DoingNothing or GuestState.WalkingAround or GuestState.GoingToRide ) > 0, "the imported guests entered the park" );
			Assert.IsTrue( simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).IsValid );
		}
		finally
		{
			FileSystem = original;
		}
	}
}

internal static class GuestTestAccess
{
	public static bool IsOnGridForTest( this Guest guest ) => guest.OnGrid;
	public static bool HasWaypointForTest( this Guest guest ) => guest.HasWaypoint;
}
