using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The staff of an original save (tp.exe staff serializer <c>0x00504DE0</c> and the role serializers): the member's own state word becomes its job with the
/// thing it works on, its inline name, idle stamp and patrol rectangle are kept, and its walk goes on to the person's accurate destination once a world places it.
/// </summary>
[TestClass]
public class OriginalStaffImportTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	/// <summary>A staff cell id: y × 128 + x + 1 (tp.exe 0x00506ED0).</summary>
	private static ushort CellId( int x, int y ) => (ushort)(y * 128 + x + 1);

	private static byte[] MechanicRole( uint timer, ushort ride )
	{
		var role = new byte[6];
		BinaryPrimitives.WriteUInt32LittleEndian( role, timer );
		BinaryPrimitives.WriteUInt16LittleEndian( role.AsSpan( 4 ), ride );
		return role;
	}

	private static byte[] HandymanRole( ushort litterCell, uint timer, ushort toilet )
	{
		var role = new byte[8];
		BinaryPrimitives.WriteUInt16LittleEndian( role, litterCell );
		BinaryPrimitives.WriteUInt32LittleEndian( role.AsSpan( 2 ), timer );
		BinaryPrimitives.WriteUInt16LittleEndian( role.AsSpan( 6 ), toilet );
		return role;
	}

	private static (ParkEconomy Economy, OriginalEconomyImport Import) Import( params (uint Id, SaveThingModel Model, byte[] Body)[] staff ) => ImportWithCells( null, staff );

	private static (ParkEconomy Economy, OriginalEconomyImport Import) ImportWithCells( Func<int, int, byte[]?>? cells, params (uint Id, SaveThingModel Model, byte[] Body)[] staff )
	{
		var settings = OriginalSaveImportTests.Settings();
		var save = OriginalSaveImportTests.Park( settings, extraThings: staff.Select( item => (item.Id, (uint)item.Model, item.Body) ), cellOverride: cells );
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		return (economy, OriginalEconomyImport.Apply( economy, save ));
	}

	/// <summary>A cell record of the test map with litter: the type word (+0x2C of the original's cell, +30 in the record), the claiming handyman's thing id (+34) and the turn of the claim (+47).</summary>
	private static byte[] LitteredCell( uint type, ushort collector = 0, uint markedTurn = 0, bool occupied = false )
	{
		var record = OriginalParkImportTests.CreateCellRecord( 0, occupied: occupied );
		BinaryPrimitives.WriteUInt32LittleEndian( record.AsSpan( SaveCell.LitterOffset ), type );
		BinaryPrimitives.WriteUInt16LittleEndian( record.AsSpan( SaveCell.LitterCollectorOffset ), collector );
		BinaryPrimitives.WriteUInt32LittleEndian( record.AsSpan( SaveCell.LitterMarkedOffset ), markedTurn );
		return record;
	}

	/// <summary>
	/// The cell grid's litter is imported (the cell serializer 0x4D8DD0): a handyman saved walking to or sweeping a littered cell takes the job and the claim again, the litter of other cells stays unclaimed,
	/// and a word that is no litter type is reported (SAVE-010).
	/// </summary>
	[TestMethod]
	public void TheCellGridsLitterIsImportedAndASweepingHandymanKeepsHisClaim()
	{
		var (economy, import) = ImportWithCells( ( x, y ) => (x, y) switch
		{
			(1, 0) => LitteredCell( 2, collector: 42, markedTurn: 300 ),
			(0, 2) => LitteredCell( 7 ),
			(2, 2) => LitteredCell( 9 ),
			_ => null
		},
			(42, SaveThingModel.Handyman, SaveTestPayloads.Staff( SaveThingModel.Handyman, state: 9, role: HandymanRole( CellId( 1, 0 ), 310, 0 ) )),
			(43, SaveThingModel.Handyman, SaveTestPayloads.Staff( SaveThingModel.Handyman, state: 8, role: HandymanRole( CellId( 0, 2 ), 0, 0 ) )) );
		Assert.AreEqual( LitterType.None, economy.Litter.TypeAt( 2, 2 ), "type 9 is no litter" );
		Assert.AreEqual( LitterType.Vomit, economy.Litter.TypeAt( 0, 2 ) );
		Assert.AreEqual( (LitterType)2, economy.Litter.TypeAt( 1, 0 ) );
		Assert.AreEqual( 2, economy.Litter.Count );
		var sweeper = economy.Staff.Members.Single( item => item.Job == StaffJob.Sweeping );
		Assert.AreEqual( ((1, 0) as (int, int)?), sweeper.LitterCell );
		Assert.AreEqual( 310L, sweeper.JobStamp );
		Assert.AreEqual( sweeper.Id, economy.Litter.CollectorAt( 1, 0 ), "the claim is the sweeper's again" );
		Assert.AreEqual( 300L, economy.Litter.ClaimTurnAt( 1, 0 ), "the saved claim turn" );
		var walker = economy.Staff.Members.Single( item => item.Job == StaffJob.ToLitter );
		Assert.AreEqual( ((0, 2) as (int, int)?), walker.LitterCell );
		Assert.AreEqual( walker.Id, economy.Litter.CollectorAt( 0, 2 ), "a handyman walking to litter claims it even where the cell's collector word is clear" );
		Assert.IsTrue( import.Evidence.Any( line => line.StartsWith( "Litter imported: 2 littered cells", StringComparison.Ordinal ) && line.Contains( "skipped (2, 2) type 9", StringComparison.Ordinal ) ) );
		Assert.IsFalse( import.Evidence.Any( line => line.Contains( "[APPROX:SAVE-010] Handyman" ) ) );
	}

	[TestMethod]
	public void TheRoleSuffixesAndThePersonBlockDecodeAsTheSerializersWriteThem()
	{
		var mechanic = SaveTestPayloads.Staff( SaveThingModel.Mechanic, role: MechanicRole( 7, 12 ), position: (300, 400), destination: (310, 420) );
		var handyman = SaveTestPayloads.Staff( SaveThingModel.Handyman, role: HandymanRole( CellId( 4, 2 ), 650, 21 ) );
		var settings = OriginalSaveImportTests.Settings();
		var save = OriginalSaveImportTests.Park( settings, extraThings: new[] { (40u, (uint)SaveThingModel.Mechanic, mechanic), (41u, (uint)SaveThingModel.Handyman, handyman) } );
		var savedMechanic = save.Staff.Single( item => item.Model == SaveThingModel.Mechanic );
		// 0x004DA110: mDurationOfRepair (+0x214) then mObjectToRepair (+0x218); 0x004D6D60: mTargetLitterCell, mTimeStartedCleaning, mToiletToClean
		Assert.AreEqual( (7u, (ushort)12, (ushort)0), (savedMechanic.RoleTimer, savedMechanic.RoleTarget, savedMechanic.RoleToilet) );
		var savedHandyman = save.Staff.Single( item => item.Model == SaveThingModel.Handyman );
		Assert.AreEqual( (650u, CellId( 4, 2 ), (ushort)21), (savedHandyman.RoleTimer, savedHandyman.RoleTarget, savedHandyman.RoleToilet) );
		Assert.AreEqual( ((4, 2) as (int, int)?), SaveStaffRecord.CellOf( savedHandyman.RoleTarget ) );
		// 0x004F8B10: the base, the sprite (12 bytes), then mAccurateDestX/Y
		var person = savedMechanic.Person;
		Assert.AreEqual( ((ushort)300, (ushort)400, (ushort)310, (ushort)420), (person.Base.X, person.Base.Y, person.AccurateDestX, person.AccurateDestY) );
		Assert.AreEqual( ((0, 0) as (int, int)?, (127, 127) as (int, int)?), (SaveStaffRecord.CellOf( 1 ), SaveStaffRecord.CellOf( 0x4000 )), "the clear's corners 1 and 0x4000 (0x00506E90)" );
		Assert.IsNull( SaveStaffRecord.CellOf( 0 ) );
	}

	[TestMethod]
	public void ImportsTheJobNamePatrolAreaIdleStampAndTheWalkOn()
	{
		var mechanic = SaveTestPayloads.Staff( SaveThingModel.Mechanic, grade: 3, name: "Mike Cooper", state: 13, patrolLow: CellId( 3, 4 ), patrolHigh: CellId( 10, 6 ),
			idle: 700, jobsDone: 4, role: MechanicRole( 7, 12 ), position: (1 * 256 + 100, 1 * 256 + 128), destination: (1 * 256 + 128, 1 * 256 + 140) );
		var (economy, import) = Import( (40, SaveThingModel.Mechanic, mechanic) );
		var bouncy = economy.Objects.Single( item => item.InfoId == 1100 );
		var member = economy.Staff.Members.Single( item => item.Type == StaffType.Mechanic );
		Assert.AreEqual( (StaffJob.Repairing, bouncy.Id, 7L, StaffState.Working), (member.Job, member.RideId, member.JobStamp, member.State),
			"state 13 repairs the attraction thing 12 with 7 turns left on its timer (0x004DA830)" );
		Assert.AreEqual( "Mike Cooper", member.Name );
		Assert.AreEqual( (3, 4, 10, 6), member.PatrolArea );
		Assert.AreEqual( 700L, member.IdleStamp );
		Assert.AreEqual( ((1 * 256 + 128, 1 * 256 + 140) as (int, int)?), member.ResumeDestination );
		Assert.AreEqual( 0, bouncy.MechanicId, "the claim is taken back by the world that places him, not by a headless economy" );
		StringAssert.Contains( import.Evidence.Single( line => line.StartsWith( "Mechanic thing 40 job imported:", StringComparison.Ordinal ) ), "state 13 (Repairing)" );
		StringAssert.Contains( import.Evidence.Single( line => line.StartsWith( "Staff jobs-done counts ", StringComparison.Ordinal ) ), "4/" );
		var researcher = economy.Staff.Members.Single( item => item.Type == StaffType.Researcher );
		Assert.AreEqual( ("Nicholas Ricks", StaffJob.Patrol, StaffState.Patrolling, StaffMember.WholeMap), (researcher.Name, researcher.Job, researcher.State, researcher.PatrolArea),
			"corners 1 and 0x4000 are the whole map" );
	}

	[TestMethod]
	public void AJobWhoseTargetIsNotImportedIsImportedIdleAndReported()
	{
		var (economy, import) = Import(
			(40, SaveThingModel.Mechanic, SaveTestPayloads.Staff( SaveThingModel.Mechanic, state: 12, role: MechanicRole( 0, 99 ) )),
			(41, SaveThingModel.Handyman, SaveTestPayloads.Staff( SaveThingModel.Handyman, state: 10, role: HandymanRole( 0, 0, 12 ) )),
			(42, SaveThingModel.Handyman, SaveTestPayloads.Staff( SaveThingModel.Handyman, state: 8, role: HandymanRole( CellId( 1, 0 ), 0, 0 ) )),
			(43, SaveThingModel.Guard, SaveTestPayloads.Staff( SaveThingModel.Guard, state: 16 )),
			(44, SaveThingModel.Entertainer, SaveTestPayloads.Staff( SaveThingModel.Entertainer, state: 3, restArea: 12 )),
			(45, SaveThingModel.Entertainer, SaveTestPayloads.Staff( SaveThingModel.Entertainer, state: 13, patrolLow: CellId( 9, 9 ), patrolHigh: CellId( 2, 2 ) )) );
		foreach ( var member in economy.Staff.Members.Where( item => item.Type != StaffType.Researcher ) )
			Assert.AreEqual( (StaffJob.Idle, StaffState.Idle, 0, 0, 0), (member.Job, member.State, member.RideId, member.ToiletId, member.RestAreaId), $"{member.Type} {member.Id}" );
		string Line( string start ) => import.Evidence.Single( line => line.StartsWith( start, StringComparison.Ordinal ) );
		StringAssert.Contains( Line( "Staff value adjusted on import: Mechanic thing 40" ), "thing 99, is no imported attraction" );
		StringAssert.Contains( Line( "Staff value adjusted on import: Handyman thing 41" ), "thing 12, is no imported relief object" );
		StringAssert.Contains( Line( "Staff value adjusted on import: Handyman thing 42" ), "its litter cell 2 holds no imported litter" );
		StringAssert.Contains( Line( "[APPROX:SAVE-002] Guard thing 43 guard job" ), "the guest is not imported" );
		StringAssert.Contains( Line( "Staff value adjusted on import: Entertainer thing 44" ), "thing 12, is no imported rest area" );
		StringAssert.Contains( Line( "Staff value adjusted on import: Entertainer thing 45 state word 13" ), "no state of a Entertainer" );
		StringAssert.Contains( Line( "Staff value adjusted on import: Entertainer thing 45 patrol corners" ), "imported as the whole map" );
		Assert.AreEqual( StaffMember.WholeMap, economy.Staff.Members.Last().PatrolArea );
	}

	[TestMethod]
	public void TheSavedWalkSpeedsAreKeptAndADestinationOffTheMapIsReported()
	{
		var mechanic = SaveTestPayloads.Staff( SaveThingModel.Mechanic, destination: (40000, 300) );
		BinaryPrimitives.WriteUInt16LittleEndian( mechanic.AsSpan( 26 ), 120 );
		// mPurposeSpeed follows mStrandedTime after the navigation block (person offset 228)
		BinaryPrimitives.WriteUInt16LittleEndian( mechanic.AsSpan( 228 ), 25 );
		var (economy, import) = Import( (40, SaveThingModel.Mechanic, mechanic) );
		var member = economy.Staff.Members.Single( item => item.Type == StaffType.Mechanic );
		Assert.AreEqual( (120, 25), (member.BaseSpeed, member.SpeedBonus), "mBaseSpeed (+0xC0) and mPurposeSpeed (+0xC2)" );
		Assert.IsNull( member.ResumeDestination );
		StringAssert.Contains( import.Evidence.Single( line => line.StartsWith( "Staff value adjusted on import: Mechanic thing 40 walk destination", StringComparison.Ordinal ) ), "outside the map" );
		// the economy's own save takes the member as it is
		ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( economy ) ) ), economy.Settings, economy.Catalog );
	}

	[TestMethod]
	public void TheNameAndTheWalkOnSurviveAnOwnSave()
	{
		var mechanic = SaveTestPayloads.Staff( SaveThingModel.Mechanic, name: "Mike Cooper", destination: (5 * 256 + 7, 6 * 256 + 9) );
		var (economy, _) = Import( (40, SaveThingModel.Mechanic, mechanic) );
		var json = ParkSaveFile.Serialize( economy );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		var member = restored.Staff.Members.Single( item => item.Type == StaffType.Mechanic );
		Assert.AreEqual( ("Mike Cooper", (5 * 256 + 7, 6 * 256 + 9) as (int, int)?), (member.Name, member.ResumeDestination) );
		var researcher = restored.Staff.Members.Single( item => item.Type == StaffType.Researcher );
		Assert.AreEqual( "Nicholas Ricks", researcher.Name );
		// a hand-edited save with a name longer than the original's 33 units is refused
		string Edited( Action<System.Text.Json.Nodes.JsonObject> edit )
		{
			var root = System.Text.Json.Nodes.JsonNode.Parse( json )!;
			var members = root["Staff"]!["Members"]!.AsArray();
			edit( members.Single( node => (string?)node!["Name"] == "Mike Cooper" )!.AsObject() );
			return root.ToJsonString();
		}
		void Refused( string edited ) =>
			Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( edited ) ), economy.Settings, economy.Catalog ) );
		// a hand-edited save with a name longer than the original's 33 units, or a destination off the map, is refused
		Refused( Edited( node => node["Name"] = new string( 'x', StaffMember.MaximumNameLength + 1 ) ) );
		Refused( Edited( node => node["ResumeDestination"] = new System.Text.Json.Nodes.JsonArray( 40000, 1 ) ) );
	}

	[DataTestMethod]
	[DataRow( "" )]
	[DataRow( "   " )]
	public void ExplicitBlankOrWhitespaceOriginalNamesSurviveImportAndResume( string name )
	{
		var (economy, _) = Import( (40, SaveThingModel.Mechanic, SaveTestPayloads.Staff( SaveThingModel.Mechanic, name: name )) );
		Assert.AreEqual( name, economy.Staff.Members.Single( member => member.Type == StaffType.Mechanic ).Name );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( economy ) ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( name, restored.Staff.Members.Single( member => member.Type == StaffType.Mechanic ).Name );
	}

	/// <summary>A world with a path along y = 5 and a ride at (12, 6) whose access is (12, 5).</summary>
	private sealed class World
	{
		public readonly GuestPathGrid Grid = new( 20, 20 );
		public readonly ParkEconomy Park = EconomyTestData.Park();
		public readonly StaffWorldSimulation Staff;
		public readonly ParkObjectState Ride;

		public World()
		{
			for ( var x = 0; x < 16; x++ )
				Grid.SetPath( x, 5, true );
			Park.OpenPark();
			Ride = Park.RegisterExisting( 1100 );
			Staff = new StaffWorldSimulation( Grid, () => Park.Staff.Members, id => id == Ride.Id ? (12, 5) : null, (0, 5) ) { RideCells = id => id == Ride.Id ? (12, 6) : null };
			Park.StaffWorld = Staff;
		}

		public void Turns( int turns )
		{
			for ( var turn = 0; turn < turns; turn++ )
				EconomyTestData.AdvanceToTurn( Park, Park.Turn + 1 );
		}
	}

	[TestMethod]
	public void AResumedMechanicClaimsHisRideAndFinishesTheSavedRepair()
	{
		var world = new World();
		world.Ride.IsBrokenDown = true;
		world.Ride.Repair = 40;
		var mechanic = world.Park.Staff.AddImported( StaffType.Mechanic, 2, 0, 90, StaffState.Working, 0 );
		(mechanic.CellX, mechanic.CellY) = (12.5f, 5.5f);
		(mechanic.Job, mechanic.RideId, mechanic.JobStamp, mechanic.ResumeDestination) = (StaffJob.Repairing, world.Ride.Id, 3, (12 * 256 + 128, 5 * 256 + 128));
		mechanic.ResumeClaimTurn = 1;
		world.Turns( 1 );
		Assert.AreEqual( mechanic.Id, world.Ride.MechanicId, "placing him takes back the ride's claim (mAssignedStaffMember)" );
		Assert.AreEqual( (1L, null), (world.Ride.ClaimTurn, mechanic.ResumeClaimTurn), "with the turn the save recorded (+0x60)" );
		Assert.IsNull( mechanic.ResumeDestination );
		Assert.AreEqual( StaffJob.Repairing, mechanic.Job );
		world.Turns( 4 );
		Assert.IsFalse( world.Ride.IsBrokenDown, "the saved timer counts down and the repair completes (0x004DA830)" );
		Assert.AreEqual( (0, 0), (world.Ride.MechanicId, mechanic.RideId) );
	}

	[TestMethod]
	public void AResumedPatrolWalksOnToTheSavedDestination()
	{
		var world = new World();
		var handyman = world.Park.Staff.AddImported( StaffType.Handyman, 2, 0, 90, StaffState.Patrolling, 0 );
		(handyman.CellX, handyman.CellY) = (2.5f, 5.5f);
		(handyman.Job, handyman.ResumeDestination) = (StaffJob.Patrol, (9 * 256 + 64, 5 * 256 + 192));
		world.Turns( 1 );
		var navigation = handyman.Navigation!;
		Assert.AreEqual( GuestNavigation.BytePoint( (9, 5), 64, 192 ), (navigation.TargetX, navigation.TargetY), "the walk goes on to mAccurateDestX/Y (0x004FA5F0 at 0x004F9407)" );
		Assert.AreEqual( StaffJob.Patrol, handyman.Job, "a resumed handyman keeps walking instead of searching at once" );
		var target = (navigation.TargetX, navigation.TargetY);
		var turns = 0;
		while ( turns++ < 200 && (handyman.Navigation!.TargetX, handyman.Navigation.TargetY) == target )
			world.Turns( 1 );
		Assert.AreEqual( (9, 5), handyman.Navigation!.Cell, "he walked to the saved destination before patrolling on" );
	}

	[TestMethod]
	public void OriginalJungleEasymodeStaffKeepTheirNamesPatrolAreasAndStates()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		var original = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		try
		{
			var level = OriginalPark.Load( "jungle" );
			var runtime = ParkEconomyRuntime.ForOriginalLevel( level, ParkStartKind.InstantAction );
			var staff = runtime.Economy.Staff.Members;
			var save = level.Save!;
			CollectionAssert.AreEqual( save.Staff.Select( item => item.Staff.Name ).ToArray(), staff.Select( member => member.Name ).ToArray() );
			foreach ( var (saved, member) in save.Staff.Zip( staff ) )
			{
				Assert.AreEqual( (StaffJob)saved.Staff.State, member.Job, saved.Staff.Name );
				Assert.AreEqual( (long)saved.Staff.TimeStartedIdling, member.IdleStamp );
				var person = saved.Person;
				Assert.AreEqual( ((int)person.AccurateDestX, (int)person.AccurateDestY), member.ResumeDestination!.Value );
			}
			// the guard patrols from (39, 21) to (56, 28): corner ids 2728 and 3641
			var guard = staff.Single( member => member.Type == StaffType.Guard );
			Assert.AreEqual( ((39, 21, 56, 28), 752L, StaffJob.Idle), (guard.PatrolArea, guard.IdleStamp, guard.Job) );
			Assert.AreEqual( StaffMember.WholeMap, staff.Single( member => member.Type == StaffType.Researcher ).PatrolArea );
			Assert.IsFalse( runtime.Import!.Evidence.Any( line => line.Contains( "adjusted on import" ) ), "the shipped staff need no adjustment" );
		}
		finally
		{
			FileSystem = original;
		}
	}
}
