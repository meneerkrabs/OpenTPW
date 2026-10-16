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
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 3 ), At( 5 )), State = 6, TimeStartedIdling = 750, PrankeryIndex = 101, Cash = 345 }, 41 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 4 ), At( 5 )), Destination = (At( 12, 64 ), At( 5, 192 )), State = 7, BaseSpeed = 140, EspSprite = 2 }, 36 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 6 ), At( 5 )), State = 10, MajorDest = 15, PreviousRides = new ushort[] { 15, 99, 0, 0 } }, 38 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 7 ), At( 5 )), State = 9, MajorDest = 17, BalloonScript = 3, BalloonLife = 120 }, 39 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 8 ), At( 5 )), State = 18 }, 40 ),
		};
		var report = simulation.ImportOriginal( saved, 755, thing => thing == 15 ? 3 : 0, thing => thing == 17 ? 21 : 0 );
		CollectionAssert.AreEqual( new[] { 36, 38, 39, 40, 41 }, simulation.Guests.Select( guest => guest.Id ).ToArray(), "thing ids, in ascending order" );
		Assert.AreEqual( random, simulation.CaptureSnapshot().RandomState, "the import draws nothing from the guest stream" );
		Guest Of( int id ) => simulation.Guests.Single( guest => guest.Id == id );
		var deciding = Of( 41 );
		Assert.AreEqual( (GuestState.DoingNothing, 345, 101, 0L), (deciding.State, deciding.Money, deciding.PrankFlag, deciding.QueueStandingSinceTurn),
			"state 6; the decision stamp 750 lies before the save's turn 755 and counts from the import" );
		Assert.AreEqual( (50f, 20f, 30f, 10f, 2f, 5f, 90), (deciding.Happiness, deciding.Hunger, deciding.Thirst, deciding.Toilet, deciding.Nausea, deciding.Litter, deciding.ExitLevel) );
		Assert.AreEqual( (3, 5, true), (deciding.CellX, deciding.CellY, deciding.IsOnGridForTest()) );
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
		Assert.AreEqual( 42, spawned.Id, "new guests take ids above the imported ones" );
		for ( var tick = 0; tick < 600; tick++ )
			simulation.Tick( 1f / 60 );
		Assert.IsTrue( simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).IsValid );
	}

	[TestMethod]
	public void ArrivingGuestsWalkOnAlongTheNearestLaneAndQueuedGuestsAreReported()
	{
		var simulation = Simulation();
		var saved = new[]
		{
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 10 ), At( 11 )), State = 2 }, 30 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 10 ), At( 8 )), State = 3 }, 31 ),
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
		Assert.AreEqual( GuestState.GoingToTicketBooth, Of( 30 ).State );
		Assert.AreEqual( (GuestState.EnteringPark, GuestState.EnteringPark, GuestState.AtTicketBooth, GuestState.CrossingRoadHome),
			(Of( 31 ).State, Of( 32 ).State, Of( 33 ).State, Of( 34 ).State), "a paid guest waiting for the gate (state 3) walks in" );
		Assert.AreEqual( GuestState.LeavingPark, Of( 38 ).State, "an unpaid guest at a closed park's booth (state 3) goes home" );
		Assert.AreEqual( 6, simulation.Guests.Count );
		StringAssert.Contains( report.Single( line => line.StartsWith( "[APPROX:SAVE-002] Guests not imported:", StringComparison.Ordinal ) ), "guest 35 (state 11), guest 36 (state 16), guest 37 (state 6 off the paths at (2, 2))" );
		Assert.IsTrue( report.Any( line => line.StartsWith( "Guest value adjusted on import: guest 31 state 3 (paid", StringComparison.Ordinal ) ) );
		Assert.IsTrue( simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).IsValid, string.Join( "; ", simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).Errors ) );
		for ( var tick = 0; tick < 3000 && simulation.Guests.Any( guest => guest.Id is 30 or 31 or 32 && guest.State != GuestState.DoingNothing ); tick++ )
			simulation.Tick( 1f / 60 );
		Assert.IsTrue( new[] { 30, 31, 32 }.All( id => Of( id ).State is not (GuestState.GoingToTicketBooth or GuestState.EnteringPark) ), "they reached the booth and the park" );
		Assert.ThrowsException<InvalidOperationException>( () => simulation.ImportOriginal( saved, 755, _ => 0, _ => 0 ), "only into an empty simulation" );
	}

	[TestMethod]
	public void AShortLaneAndAGuestOffTheMapAreHandled()
	{
		var grid = new GuestPathGrid( 20, 20 );
		grid.SetPath( 10, 6, true );
		var settings = new GuestSettings { ArrivalLaneA = new[] { (10, 8), (10, 6) } };
		var simulation = new GuestSimulation( grid, settings, 5 ) { ArrivalsEnabled = false };
		var saved = new[]
		{
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 10 ), At( 7 )), State = 18, PersonType = 0 }, 30 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 10 ), At( 7 )), State = 1, PersonType = 0 }, 31 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 10 ), At( 7 )), State = 4, PersonType = 0 }, 32 ),
			Decode( new SaveTestPayloads.GuestFields { Position = (At( 30 ), At( 7 )), State = 2, PersonType = 0 }, 33 ),
		};
		var report = simulation.ImportOriginal( saved, 755, _ => 0, _ => 0 );
		CollectionAssert.AreEqual( new[] { 30, 31, 32 }, simulation.Guests.Select( guest => guest.Id ).ToArray() );
		StringAssert.Contains( report.Single( line => line.StartsWith( "[APPROX:SAVE-002] Guests not imported:", StringComparison.Ordinal ) ), "guest 33 (state 2 outside the map at (30, 7))" );
		Assert.IsTrue( simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).IsValid, string.Join( "; ", simulation.ValidateSnapshot( simulation.CaptureSnapshot() ).Errors ) );
		for ( var tick = 0; tick < 600; tick++ )
			simulation.Tick( 1f / 60 );
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
				Assert.AreEqual( saved.State == 2 ? GuestState.GoingToTicketBooth : GuestState.EnteringPark, guest.State, $"guest {saved.Id} state {saved.State}" );
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
