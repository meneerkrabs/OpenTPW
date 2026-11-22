using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's ride wear and its mechanics (docs/reverse/TPI-STAFF.md §11, "Mechanics and ride wear"): the type record's breakdown lines, the starting life, the wear amount
/// 0x5E21A0 and tick 0x5E25B0 (goldens from tools/ppc-analysis/oracle_x86/tpi_ride_wear_oracle.py, which runs the routines natively), the breakdown request 0x5E4790, and the mechanic's
/// search, walk, repair, claim lapse, removal message and save. The tests on the retail data need OPENTPW_TPI_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncMechanicTests
{
	private BaseFileSystem? previous;
	private Logger? previousLog;

	[TestInitialize]
	public void Initialize() => previous = FileSystem;

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previous!;
		if ( previousLog != null )
			Log = previousLog;
		previousLog = null;
		GameEditions.Select( GameEdition.ThemeParkWorld );
	}

	private void UseRetailData()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		previousLog ??= Log;
		Log = new();
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		GameEditions.Select( GameEdition.ThemeParkInc );
	}

	private ParkEconomy ScienceEconomy()
	{
		UseRetailData();
		return ParkEconomyRuntime.ForThemeParkIncLevel( OriginalPark.Load( "science" ) ).Economy;
	}

	/// <summary>The rides' running state, riders and speed a test sets (the level's ride scripts otherwise).</summary>
	private sealed class Operations : IRideOperations
	{
		public Dictionary<int, RideOperation> Rides { get; } = new();
		public bool TryGet( int instanceId, out RideOperation operation ) => Rides.TryGetValue( instanceId, out operation );
	}

	/// <summary>The Giant Robot Swing (Info.Id 1101): WorkingLife 75, Upgrades[0].WearRate 6, MaxCapacity 16, MaxSpeed 100, RideBreakDown.Imminent 10 and WarnPlayer 5.</summary>
	private const int RobotSwing = 1101;
	/// <summary>The Log Mill (Info.Id 1180), a special ride (Info.IsSpecial 1).</summary>
	private const int LogMill = 1180;

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

	/// <summary>A world whose objects stand at (9, 7) with their entrance on the row at (9, 5).</summary>
	private static ThemeParkIncStaffWorld World( ParkEconomy economy ) =>
		new( Row( economy ) ) { ObjectCells = instance => economy.TryGetObject( instance, out _ ) ? ((9, 7), (9, 5)) : null };

	/// <summary>Hires the first pooled mechanic and drops him on <paramref name="cell"/>.</summary>
	private static ThemeParkIncStaffMember HireMechanic( ParkEconomy economy, ThemeParkIncStaffWorld world, (int X, int Y) cell )
	{
		var staff = economy.TpiStaff!;
		var slot = Enumerable.Range( 0, ThemeParkIncStaff.PoolSlots ).First( index => staff.Slots[index]?.Role == ThemeParkIncStaffRole.Mechanic );
		Assert.IsTrue( staff.BeginHire( slot ) );
		var member = staff.EndHire( slot, placed: true )!;
		world.Construct( economy, member, cell, null );
		return member;
	}

	// ---- The type record and the starting life -------------------------------------------------

	[TestMethod]
	public void EveryWearingObjectHasTheBreakdownLinesAQueueAndTheMechanicsBank()
	{
		// [BIN:TPI-EXE:0x9A2CC0 type record keys] RideBreakDown.Imminent at +0x3C8 and WarnPlayer at +0x3CC; the repair tail's queue test needs Info.HasQueue on every object a mechanic repairs
		UseRetailData();
		var wearing = 0;
		foreach ( var world in new[] { "science", "water", "arabian" } )
		{
			var entries = ObjectCatalog.Load( world ).Entries;
			foreach ( var info in EconomyObjectCatalog.Load( world, easy: false ).Objects.Where( info => info.Upgrades.Any( level => level.WearRate != 0 ) ) )
			{
				wearing++;
				Assert.AreEqual( (10f, 5f), (info.BreakdownImminent, info.BreakdownWarnPlayer), $"{world} {info.Name}" );
				var entry = entries.Single( item => item.InfoId == info.InfoId );
				Assert.AreEqual( 1, entry.Settings.GetInt( "Info.HasQueue" ), $"{world} {info.Name} has a queue" );
				Assert.IsFalse( info.ProvidesRelief || info.HoldsLitter || info.RequiresWatering, $"{world} {info.Name} is a ride" );
			}
		}
		Assert.AreEqual( 66, wearing, "the 66 wearing rides of the three worlds" );
		// [BIN:TPI-EXE:0x5D9BB4 mechanic sprite] the mechanics' bank (Generic\Mechanics, Spr_om) loads with Theme Park World's loader
		Assert.IsTrue( StaffSpriteSet.Load( "science" ).BankCount( StaffType.Mechanic ) > 0, "the mechanics' bank" );
	}

	[TestMethod]
	[DataRow( 0, false )]
	[DataRow( 1, true )]
	[DataRow( 2, false )]
	[DataRow( -1, false )]
	public void OnlyAnIsSpecialOfOneMakesARideSpecial( int value, bool special )
	{
		// [BIN:TPI-EXE:0x5E26FB special ride] the wear tick and the breakdown request (0x5E493B) compare the type word +0x5C with 1
		var info = EconomyObjectCatalog.FromSettings( new SamSettings( new[] { SamDocument.Parse( $"Info.Id 1102\nInfo.IsSpecial {value}\nUpgrades[0].CostOfUpgrade 500\n", "ride.sam" ) } ),
			ParkObjectKind.Ride, "/x" );
		Assert.AreEqual( special, info.IsSpecial );
	}

	[TestMethod]
	public void ANewObjectStartsAtItsWorkingLifeAsThePrebuiltSciencePark()
	{
		// [BIN:TPI-EXE:0x5DB53A object life] +0x58 = Info.WorkingLife, or 100.0 when 0; the prebuilt Science park's choosable attractions were saved at exactly that
		var economy = ScienceEconomy();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( RobotSwing, out var built ) );
		Assert.AreEqual( 75.0, built!.LifeGauge );
		Assert.AreEqual( 100.0, built.Repair );
		var park = OriginalPark.Load( "science" );
		var checkedRides = 0;
		foreach ( var item in park.Save!.ThemeParkIncObjects )
		{
			if ( !economy.Catalog.TryGet( item.Record.InfoId, out var info ) || !info.IsChoosable )
				continue;
			checkedRides++;
			Assert.AreEqual( (float)ParkEconomy.ThemeParkIncStartingLife( info ), item.Attraction.LifeGauge, $"Info.Id {info.InfoId}" );
			Assert.AreEqual( 100f, item.Attraction.StateOfRepair );
		}
		Assert.IsTrue( checkedRides >= 4, "the prebuilt rides" );
	}

	// ---- The wear --------------------------------------------------------------------------------

	private static EconomyObjectInfo Ride( int maxCapacity, int maxSpeed, int redCapacity, int redSpeed, int wearRate ) =>
		new( 1, "ride", ParkObjectKind.Ride, ResearchCategory.Ride, 0, new[] { new UpgradeLevelInfo( 0, 0, 0, wearRate, 0, Array.Empty<int>(), redSpeed, redCapacity ) },
			null, null, null, 0, 0, 0, 0, 0, 0, "", MaxSpeed: maxSpeed, MaxCapacity: maxCapacity );

	[TestMethod]
	[DataRow( 59, 5, 5, 100, 0, 60, 1, 0x3F50C49Cu )]
	[DataRow( 0, 24, 24, 100, 6, 29, 9, 0x409E6667u )]
	[DataRow( 162, 0, 1, 162, 0, 52, 2, 0x3FC66666u )]
	[DataRow( 100, 0, 11, 100, 7, 60, 6, 0x40533334u )]
	[DataRow( 146, 0, 24, 100, 0, 147, 6, 0x40BC8B44u )]
	[DataRow( 29, 0, 12, 100, 4, 60, 6, 0x3FB10624u )]
	[DataRow( 100, 5, 2, 100, 2, 13, 1, 0x3F800000u )]
	[DataRow( 0, 1, 5, 22, 1, 60, 2, 0x3EC28F5Cu )]
	[DataRow( 75, 0, 8, 40, 2, 60, 4, 0x403F3333u )]
	[DataRow( 0, -1, 8, 85, 1, 125, 6, 0x3F19999Au )]
	public void TheWearAmountIsTheExecutables( int speed, int riders, int maxCapacity, int maxSpeed, int redCapacity, int redSpeed, int wearRate, uint expected )
	{
		// the oracle's native 0x5E21A0 at the 24-bit control word (tpi_ride_wear_oracle.py --golden)
		var info = Ride( maxCapacity, maxSpeed, redCapacity, redSpeed, wearRate );
		Assert.AreEqual( expected, BitConverter.SingleToUInt32Bits( ParkEconomy.ThemeParkIncWearAmount( info, info.Upgrades[0], new RideOperation( true, riders, speed ) ) ) );
	}

	/// <summary>
	/// The oracle's native wear tick 0x5E25B0 at the 24-bit control word (tpi_ride_wear_oracle.py --golden, 400 cases): the ride's speed, riders and type, its repair and life singles
	/// before and after, and whether VAR_WORN was written. The first ten cases, then the first repair clamped at 0, the first life clamped at 0, the first worn ride and the first
	/// wearing special ride (the life kept).
	/// </summary>
	[TestMethod]
	[DataRow( 162, 0, 1, 162, 0, 52, 2, 0x42C80000u, 0x42C80000u, false, 0x42C80000u, 0x42C80000u, false )]
	[DataRow( 59, 5, 5, 100, 0, 60, 1, 0x40A66666u, 0x408DD45Fu, true, 0x408C4DD2u, 0x408DD45Fu, true )] // worn, special
	[DataRow( 0, 24, 24, 100, 6, 29, 9, 0x411FD70Au, 0x42200000u, true, 0x40A147ADu, 0x42200000u, false )]
	[DataRow( 100, 0, 11, 100, 7, 60, 6, 0x42C80000u, 0x42200000u, false, 0x42C80000u, 0x42200000u, false )]
	[DataRow( 146, 0, 24, 100, 0, 147, 6, 0x414BDFC6u, 0x42200000u, false, 0x414BDFC6u, 0x42200000u, false )]
	[DataRow( 29, 0, 12, 100, 4, 60, 6, 0x3E99999Au, 0x42200000u, false, 0x3E99999Au, 0x42200000u, true )] // no riders: no wear, but the byte 0 is below WarnPlayer
	[DataRow( 100, 5, 2, 100, 2, 13, 1, 0x411FD70Au, 0x42200000u, false, 0x410FD70Au, 0x421FEB85u, false )]
	[DataRow( 0, 1, 5, 22, 1, 60, 2, 0x40A66666u, 0x3C23D70Au, false, 0x409A3D70u, 0x3B1D4952u, true )]
	[DataRow( 75, 0, 8, 40, 2, 60, 4, 0x3E99999Au, 0x3C23D70Au, false, 0x3E99999Au, 0x3C23D70Au, true )]
	[DataRow( 0, -1, 8, 85, 1, 125, 6, 0x42954747u, 0x3C23D70Au, false, 0x42941414u, 0x00000000u, false )] // life clamp
	[DataRow( 27, 1, 2, 109, 4, 27, 9, 0x40A66666u, 0x42B30914u, false, 0x00000000u, 0x42B2D141u, true )] // repair clamp
	public void TheWearTickIsTheExecutables( int speed, int riders, int maxCapacity, int maxSpeed, int redCapacity, int redSpeed, int wearRate, uint repair, uint life, bool special,
		uint expectedRepair, uint expectedLife, bool expectedWorn )
	{
		var economy = ScienceEconomy();
		var operations = new Operations();
		economy.RideOperations = operations;
		economy.TryBuild( RobotSwing, out var item );
		// the oracle's type record: RideBreakDown.Imminent 10 and WarnPlayer 5, Info.IsSpecial at +0x5C
		var info = Ride( maxCapacity, maxSpeed, redCapacity, redSpeed, wearRate ) with { IsSpecial = special, BreakdownImminent = 10f, BreakdownWarnPlayer = 5f };
		item!.Repair = BitConverter.UInt32BitsToSingle( repair );
		item.LifeGauge = BitConverter.UInt32BitsToSingle( life );
		item.ScriptHosted = true;
		operations.Rides[item.Id] = new RideOperation( true, riders, speed );
		var worn = false;
		economy.EventRaised += raised => worn |= raised.Kind == ParkEventKind.RideScriptVariable && raised.InstanceId == item.Id && raised.InfoId == (int)RideVariables.VAR_WORN && raised.Amount == 1;
		economy.WearThemeParkInc( item, info, 64 );
		Assert.AreEqual( (expectedRepair, expectedLife, expectedWorn), (BitConverter.SingleToUInt32Bits( (float)item.Repair ), BitConverter.SingleToUInt32Bits( (float)item.LifeGauge ), worn ) );
	}

	[TestMethod]
	public void TheWearTickRunsEverySixtyFourthTurnAndASpecialRideKeepsItsLife()
	{
		var economy = ScienceEconomy();
		var operations = new Operations();
		economy.RideOperations = operations;
		economy.TryBuild( RobotSwing, out var swing );
		economy.TryBuild( LogMill, out var mill );
		operations.Rides[swing!.Id] = new RideOperation( true, 5, 59 );
		operations.Rides[mill!.Id] = new RideOperation( true, 2, 59 );
		Assert.IsTrue( ((EconomyObjectCatalog)economy.Catalog)[LogMill].IsSpecial );
		var wear = ParkEconomy.ThemeParkIncWearAmount( ((EconomyObjectCatalog)economy.Catalog)[RobotSwing], ((EconomyObjectCatalog)economy.Catalog)[RobotSwing].Upgrades[0], operations.Rides[swing.Id] );
		// [BIN:TPI-EXE:0x5E2614 wear interval] every 8th turn runs the tick, which wears on every 64th only
		economy.UpdateRide( swing, 8 );
		Assert.AreEqual( 100.0, swing.Repair, "turn 8 is not a wear turn" );
		economy.UpdateRide( swing, 64 );
		Assert.AreEqual( 100f - wear, (float)swing.Repair );
		Assert.AreEqual( 75f - 0.02f * wear, (float)swing.LifeGauge );
		economy.UpdateRide( mill, 64 );
		Assert.IsTrue( mill.Repair < 100.0, "the special ride wears" );
		Assert.AreEqual( 95.0, mill.LifeGauge, "but keeps its life (Info.IsSpecial, 0x5E26FB)" );
		// a ride that does not run wears only with Info.RunsContinuously (the Robot Swing has none)
		operations.Rides[swing.Id] = new RideOperation( false, 5, 59 );
		var repair = swing.Repair;
		economy.UpdateRide( swing, 128 );
		Assert.AreEqual( repair, swing.Repair );
		// a closed ride is not updated
		operations.Rides[swing.Id] = new RideOperation( true, 5, 59 );
		swing.IsOpen = false;
		economy.UpdateRide( swing, 192 );
		Assert.AreEqual( repair, swing.Repair );
	}

	[TestMethod]
	public void TheWearTickTakesARideAsRunningWhileVarRunningIsAboveZero()
	{
		// [BIN:TPI-EXE:0x5E2602 wear tick running] testl, jg: a negative VAR_RUNNING is not running in Theme Park Inc; Theme Park World's bridge keeps its nonzero test (IsRunning)
		var economy = ScienceEconomy();
		economy.TryBuild( RobotSwing, out var swing );
		var guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 );
		var ride = new RideVisitorBridge( 1, "ride", RideVisitorKind.Ride, 4, 60, 10 );
		var script = GuestTests.CreateShopScript( new VisitorRideScriptEffects( ride, UnimplementedRideScriptEffects.Instance ) );
		ride.Attach( script, () => true );
		guests.Register( ride );
		var link = new GuestEconomyBridge( () => economy, guests );
		link.Link( ride.AttractionId, swing!.Id );
		foreach ( var (value, running) in new[] { (-1, false), (0, false), (1, true), (2, true) } )
		{
			script.Variables[script.GetVariableIndex( nameof( RideVariables.VAR_RUNNING ) )] = value;
			Assert.IsTrue( link.TryGet( swing.Id, out var operation ) );
			Assert.AreEqual( running, operation.Running, $"VAR_RUNNING {value}" );
			Assert.AreEqual( value != 0, ride.IsRunning, $"Theme Park World's test of VAR_RUNNING {value}" );
		}
	}

	[TestMethod]
	public void AWornRideBreaksDownAndLosesFiveLifeUnlessItIsSpecial()
	{
		// [BIN:TPI-EXE:0x5E48FB breakdown request] the life or repair byte 0 requests the breakdown; the life loses 5 unless its byte is 0 or the type is special; no draw
		var economy = ScienceEconomy();
		economy.TryBuild( RobotSwing, out var swing );
		economy.TryBuild( LogMill, out var mill );
		swing!.Repair = 0.5;
		mill!.Repair = 0.5;
		var draws = economy.World.State;
		economy.UpdateRide( swing, 8 );
		economy.UpdateRide( mill, 8 );
		Assert.IsTrue( swing.IsBrokenDown && mill.IsBrokenDown, "the headless economy breaks them down at once" );
		Assert.AreEqual( (70.0, 95.0), (swing.LifeGauge, mill.LifeGauge) );
		Assert.AreEqual( draws, economy.World.State, "no random number decides a breakdown" );
		var life = economy.TryBuild( RobotSwing, out var dead ) == ParkEconomy.PurchaseResult.Ok ? dead! : throw new InvalidOperationException();
		life.LifeGauge = 0.4;
		economy.UpdateRide( life, 8 );
		Assert.AreEqual( (true, RideObjectStates.Condemned, 0.4), (life.IsBrokenDown, ParkEconomy.ObjectState( life ), life.LifeGauge), "a life byte of 0 condemns the ride and loses nothing" );
	}

	// ---- The mechanic -----------------------------------------------------------------------------

	[TestMethod]
	public void AMechanicWalksToABrokenRideRepairsItAndSearchesAgain()
	{
		var economy = ScienceEconomy();
		var world = World( economy );
		economy.TryBuild( RobotSwing, out var swing );
		swing!.Repair = 37.4;
		swing.IsBrokenDown = true;
		var member = HireMechanic( economy, world, (2, 5) );
		// [BIN:TPI-EXE:0x5DA3D0 mechanic search] the finder takes the broken ride, a route reaches its entrance: the claim and state 21
		Assert.AreEqual( (ThemeParkIncStaffWorld.ToRide, swing.Id, member.ThingId, economy.Turn), (member.State, member.RideId, swing.MechanicId, swing.ClaimTurn) );
		Assert.IsFalse( swing.MaintenanceRequested, "a broken ride is not asked to request maintenance" );
		Assert.AreEqual( ThemeParkIncStaffWorld.WalkTemplate, member.Template );
		var turn = economy.Turn;
		for ( var step = 0; step < 400 && member.State == ThemeParkIncStaffWorld.ToRide; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( ThemeParkIncStaffWorld.Repairing, member.State, "he reached it" );
		Assert.AreEqual( ThemeParkIncStaffWorld.RepairTemplate, member.Template );
		var settings = economy.TpiStaff!.Settings;
		var duration = settings.WorkDuration( ThemeParkIncStaffRole.Mechanic, member.Experience, member.Motivation, member.Salary );
		Assert.AreEqual( (long)ThemeParkIncStaffWorld.RepairTimer( duration, 37.4f ), member.JobTimer, "(100 - 37) x the work duration / 100" );
		var timer = member.JobTimer;
		for ( var step = 0; step < timer; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( (0L, true), (member.JobTimer, swing.IsBrokenDown), "the timer ran out" );
		world.Update( economy, member, ++turn );
		Assert.AreEqual( (100.0, false, 0, 0L, false), (swing.Repair, swing.IsBrokenDown, swing.MechanicId, swing.ClaimTurn, swing.MaintenanceRequested), "repaired fully (0x5E2910)" );
		Assert.AreEqual( 0, member.RideId );
		Assert.IsTrue( member.State is ThemeParkIncStaffWorld.Idle or ThemeParkIncStaffWorld.Walking, "and he searched again" );
	}

	[TestMethod]
	public void ARideBelowItsImminentLineIsCalledForAndRepaired()
	{
		// [BIN:TPI-EXE:0x5DA4EF maintenance request] a ride not broken down, not called for and below RideBreakDown.Imminent (10) asks for maintenance (0x5E3400(1)), which closes it
		var economy = ScienceEconomy();
		var world = World( economy );
		economy.TryBuild( RobotSwing, out var swing );
		var requested = new List<int>();
		economy.EventRaised += item =>
		{
			if ( item.Kind == ParkEventKind.MaintenanceRequested )
				requested.Add( item.InstanceId );
		};
		swing!.Repair = 10.9;
		var member = HireMechanic( economy, world, (2, 5) );
		Assert.AreNotEqual( ThemeParkIncStaffWorld.ToRide, member.State, "a byte of 10 is not below the line" );
		Assert.AreEqual( 0, member.RideId );
		swing.Repair = 9.9;
		// [BIN:TPI-EXE:0x5DAAD0 ride finder] the cursor stopped on the list's last object (the only one), so the next search moves it past the end and finds nothing; the one after starts at the head
		world.Search( economy, member, economy.Turn );
		Assert.AreEqual( (0, false, 0), (member.RideId, swing.MaintenanceRequested, economy.MechanicCursor) );
		world.Search( economy, member, economy.Turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.ToRide, true), (member.State, swing.MaintenanceRequested) );
		CollectionAssert.AreEqual( new[] { swing.Id }, requested );
		var turn = economy.Turn;
		for ( var step = 0; step < 2000 && member.RideId != 0; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( (100.0, false, 0), (swing.Repair, swing.MaintenanceRequested, swing.MechanicId), "the call ends with the repair" );
	}

	[TestMethod]
	public void TheFinderWalksTheObjectListWithTheThingsThatHaveNoEconomyRecord()
	{
		// [BIN:TPI-EXE:0x5DAAD0 ride finder] the cursor moves on from the previous start; a start past the list's end (0) finds nothing. A thing without an economy record (a negated thing
		// id) is a step of the walk like any object, so it changes when a search finds nothing
		foreach ( var withThing in new[] { false, true } )
		{
			var economy = ScienceEconomy();
			var world = World( economy );
			economy.TryBuild( RobotSwing, out var swing );
			swing!.IsBrokenDown = true;
			var member = HireMechanic( economy, world, (2, 5) );
			swing.MechanicId = 0;
			swing.ClaimTurn = 0;
			CollectionAssert.AreEqual( new[] { swing.Id }, economy.ThemeParkIncObjectList.ToArray(), "a new object enters at the head" );
			if ( withThing )
				economy.RestoreThemeParkIncObjectList( new[] { swing.Id, -7 } );
			economy.MechanicCursor = 0;
			var found = Enumerable.Range( 0, 3 ).Select( _ => world.FindRide( economy, member, economy.Turn ) ).ToArray();
			CollectionAssert.AreEqual( withThing ? new[] { swing.Id, swing.Id, 0 } : new[] { swing.Id, 0, swing.Id }, found,
				withThing ? "the thing at the end takes a step: every third search finds nothing" : "every second search finds nothing" );
		}
	}

	[TestMethod]
	public void TheObjectListSurvivesASaveAndABrokenOneIsRefused()
	{
		var economy = ScienceEconomy();
		economy.TryBuild( RobotSwing, out var first );
		economy.TryBuild( LogMill, out var second );
		CollectionAssert.AreEqual( new[] { second!.Id, first!.Id }, economy.ThemeParkIncObjectList.ToArray(), "newest first (0x64F3E0)" );
		economy.RestoreThemeParkIncObjectList( new[] { first.Id, -32, second.Id } );
		economy.MechanicCursor = -32;
		var json = ParkSaveFile.Serialize( economy );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		CollectionAssert.AreEqual( new[] { first.Id, -32, second.Id }, restored.ThemeParkIncObjectList.ToArray() );
		Assert.AreEqual( -32, restored.MechanicCursor, "the cursor may rest on a thing without an economy record" );
		economy.Sell( first.Id );
		CollectionAssert.AreEqual( new[] { -32, second.Id }, economy.ThemeParkIncObjectList.ToArray(), "a sold object leaves the list" );
		foreach ( var broken in new[] { new[] { first.Id, second.Id, -32, -32 }, new[] { -32 }, new[] { first.Id, second.Id, 0, -32 }, new[] { first.Id, second.Id, 9999, -32 } } )
		{
			var node = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
			node["TpiObjectList"] = new System.Text.Json.Nodes.JsonArray( broken.Select( key => (System.Text.Json.Nodes.JsonNode?)key ).ToArray() );
			Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( node.ToJsonString() ) ), economy.Settings, economy.Catalog ),
				$"[{string.Join( ", ", broken )}]" );
		}
		// the HQ word may keep a dead thing's id (0x5DAAD0 reads one it does not find as "start at the head"), as a positive cursor may name a sold object; only an id outside the thing range is refused
		Restored( cursor: -5 );
		Assert.ThrowsException<InvalidDataException>( () => Restored( cursor: -ThingPool.Capacity - 1 ), "a cursor outside the thing range" );
		Assert.ThrowsException<InvalidDataException>( () => Restored( list: new[] { first.Id, second.Id, -ThingPool.Capacity - 1 } ), "a thing id outside the pool" );
		ParkEconomy Restored( int? cursor = null, int[]? list = null )
		{
			var node = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
			if ( cursor is { } word )
				node["MechanicCursor"] = word;
			if ( list != null )
				node["TpiObjectList"] = new System.Text.Json.Nodes.JsonArray( list.Select( key => (System.Text.Json.Nodes.JsonNode?)key ).ToArray() );
			return ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( node.ToJsonString() ) ), economy.Settings, economy.Catalog );
		}
		var missing = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
		missing.Remove( "TpiObjectList" );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( missing.ToJsonString() ) ), economy.Settings, economy.Catalog ) );
	}

	[TestMethod]
	public void TheSciencePrebuiltSeedsTheSavedObjectListOrder()
	{
		// [BIN:TPI-EXE:0x5DAAD0 ride finder] the saved list from FirstObject along mNext: 24 things, newest first; the 17 imported objects by their economy record, the 7 things OpenTPW does
		// not build (1798, 1750, 1703, 1700, 1701, 1702 and the tool entry 1710) by their negated thing ids
		var economy = ScienceEconomy();
		var park = OriginalPark.Load( "science" );
		var catalog = ObjectCatalog.Load( "science" );
		// the objects ParkObjects.ImportOriginal places (the save's record order, an entry that is no tool) and ConnectObjectsToEconomy registers in that order, by their saved thing
		var instances = new Dictionary<int, int>();
		foreach ( var record in park.Save!.ObjectsInRecordOrder )
			if ( catalog.Find( record.InfoId ) is { IsTool: false } && economy.Catalog.TryGet( record.InfoId, out _ ) )
				instances[park.Save.FindThemeParkIncAttraction( record )!.Id] = economy.RegisterExisting( record.InfoId ).Id;
		Assert.AreEqual( 17, instances.Count );
		economy.RestoreThemeParkIncObjectList( Level.ThemeParkIncObjectListOf( park.Save.ThemeParkIncWorld!, instances ) );
		var list = economy.ThemeParkIncObjectList.Select( key => key < 0 ? key : economy.TryGetObject( key, out var item ) ? item.InfoId : 0 ).ToArray();
		CollectionAssert.AreEqual( new[] { 1414, 1413, 1413, 1403, 1101, -32, -31, -30, -29, -28, -27, -26, 1707, 3708, 1709, 1706, 1705, 1110, 1170, 1160, 1180, 104, 103, 1601 }, list );
		Assert.AreNotEqual( string.Join( ",", list.Where( key => key > 0 ) ),
			string.Join( ",", instances.Values.OrderByDescending( id => id ).Select( id => economy.TryGetObject( id, out var item ) ? item.InfoId : 0 ) ), "the record order is not the list's" );
		var saved = park.Save.ThemeParkIncWorld!;
		Assert.ThrowsException<InvalidDataException>( () => Level.ThemeParkIncObjectListOf( saved, new Dictionary<int, int>( instances ) { [99] = 1 } ), "an imported object off the list" );
		// a chain whose mNext leads back to a thing met before (the Science list's oldest thing, 14, pointing at the newest, 37) is refused, not walked for ever
		var attractions = saved.Attractions.ToList();
		var oldest = attractions.FindIndex( attraction => attraction.Id == 14 );
		attractions[oldest] = attractions[oldest] with { Next = 37 };
		var looped = (ThemeParkIncSaveWorld)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( ThemeParkIncSaveWorld ) );
		foreach ( var field in typeof( ThemeParkIncSaveWorld ).GetFields( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ) )
			field.SetValue( looped, field.Name == "<Attractions>k__BackingField" ? attractions.AsReadOnly() : field.GetValue( saved ) );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Level.ThemeParkIncObjectListOf( looped, instances ) ).Message, "met twice" );
	}

	[TestMethod]
	public void AClaimOlderThanAHundredTurnsLapsesWhenItsClaimantLooksElsewhere()
	{
		// [BIN:TPI-EXE:0x5E37F0 claimant] past +0x74 + 100 a claimant whose +0x5D8 is not the ride loses the claim and goes idle (0x628180(0))
		var economy = ScienceEconomy();
		var world = World( economy );
		economy.TryBuild( RobotSwing, out var swing );
		var member = HireMechanic( economy, world, (2, 5) );
		swing!.MechanicId = member.ThingId;
		swing.ClaimTurn = 10;
		member.RideId = 0;
		member.State = ThemeParkIncStaffWorld.Walking;
		Assert.AreEqual( member.ThingId, world.Claimant( economy, swing, 110 ), "at most 100 turns on, the claim holds" );
		Assert.AreEqual( 0, world.Claimant( economy, swing, 111 ) );
		Assert.AreEqual( (0, 0L, ThemeParkIncStaffWorld.Idle, 111L), (swing.MechanicId, swing.ClaimTurn, member.State, member.IdleStamp), "the claimant goes idle (stamped: he was walking)" );
		swing.MechanicId = member.ThingId;
		swing.ClaimTurn = 10;
		member.RideId = swing.Id;
		Assert.AreEqual( member.ThingId, world.Claimant( economy, swing, 500 ), "a claimant still on his way keeps it" );
	}

	[TestMethod]
	public void TheRideBeingFixedIsSoldAndTheMechanicGoesIdle()
	{
		// [BIN:TPI-EXE:0x5D9DD0 mechanic message] message 10 for his +0x5D8: "ride I was fixing was deleted", +0x5D8 = 0, state 0
		var economy = ScienceEconomy();
		var world = World( economy );
		economy.TryBuild( RobotSwing, out var swing );
		swing!.IsBrokenDown = true;
		var member = HireMechanic( economy, world, (2, 5) );
		Assert.AreEqual( ThemeParkIncStaffWorld.ToRide, member.State );
		economy.Sell( swing.Id );
		Assert.AreEqual( (0, ThemeParkIncStaffWorld.Idle, ThemeParkIncStaffWorld.IdleTemplate), (member.RideId, member.State, member.Template) );
	}

	[TestMethod]
	public void AWorldSaveFromBeforeTheRideWearIsRefusedBeforeItsAssetsAreLookedAt()
	{
		// user decision 2026-11-21: the world save's embedded economy is validated (ParkSaveFile.Validate) before the continuation's own state and the asset manifest, so a Science world
		// save from before the mechanics and the ride wear gets the refusal, not an asset or shape complaint
		var envelope = ParkResumeSaveFileTests.Envelope();
		var economy = ScienceEconomy();
		var current = envelope with { Theme = "science", Economy = ParkSaveFile.Capture( economy ) };
		System.Text.Json.Nodes.JsonObject Save( bool rideWear )
		{
			var node = System.Text.Json.Nodes.JsonNode.Parse( ParkResumeJson.Serialize( current ) )!.AsObject();
			if ( !rideWear )
				node["Economy"]!.AsObject().Remove( "TpiRideWear" );
			// an empty asset manifest, which the shape check refuses once it reaches the manifest
			node["AssetHashes"] = new System.Text.Json.Nodes.JsonObject();
			return node;
		}
		var refusal = Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( Save( rideWear: false ).ToJsonString() ) ) );
		Assert.AreEqual( ParkSaveFile.ThemeParkIncRideWearMissing, refusal.Message );
		Assert.AreNotEqual( ParkSaveFile.ThemeParkIncRideWearMissing,
			Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( Save( rideWear: true ).ToJsonString() ) ) ).Message,
			"a save with the ride wear goes on to the other checks" );
	}

	[TestMethod]
	public void APlacedMechanicSurvivesASaveAndAnOlderSaveIsRefused()
	{
		var economy = ScienceEconomy();
		var world = World( economy );
		economy.TryBuild( RobotSwing, out var swing );
		swing!.IsBrokenDown = true;
		swing.Repair = 20.0;
		var member = HireMechanic( economy, world, (2, 5) );
		var turn = economy.Turn;
		for ( var step = 0; step < 400 && member.State == ThemeParkIncStaffWorld.ToRide; step++ )
			world.Update( economy, member, ++turn );
		world.Update( economy, member, ++turn );
		Assert.AreEqual( ThemeParkIncStaffWorld.Repairing, member.State );
		var json = ParkSaveFile.Serialize( economy );
		StringAssert.Contains( json, "\"TpiRideWear\"" );
		StringAssert.Contains( json, "\"JobTimer\"" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ), "the save round-trips exactly" );
		var restoredMember = restored.TpiStaff!.Members.Single();
		Assert.AreEqual( (member.RideId, member.JobTimer, member.State), (restoredMember.RideId, restoredMember.JobTimer, restoredMember.State) );
		var restoredWorld = World( restored );
		for ( var step = 0; step < 300; step++ )
		{
			turn++;
			world.Update( economy, member, turn );
			restoredWorld.Update( restored, restoredMember, turn );
		}
		Assert.AreEqual( ParkSaveFile.Serialize( economy ), ParkSaveFile.Serialize( restored ), "both go on alike" );
		Assert.AreEqual( 100.0, swing.Repair, "and the repair ended" );

		// user decision 2026-11-21: a Science save from before the mechanics and the ride wear is refused
		var node = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
		node.Remove( "TpiRideWear" );
		var older = Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( node.ToJsonString() ) ), economy.Settings, economy.Catalog ) );
		Assert.AreEqual( ParkSaveFile.ThemeParkIncRideWearMissing, older.Message );
		var saved = economy.TpiStaff!.Capture();
		Assert.IsFalse( ThemeParkIncStaff.IsValid( saved with { Members = saved.Members.Select( item => item with { World = item.World! with { PatrolArea = new[] { 1, 1, 9, 9 } } } ).ToArray() } ),
			"a mechanic's patrol area other than the whole map is refused (0x6256A0 is not ported)" );
		Assert.IsFalse( ThemeParkIncStaff.IsValid( saved with { Members = saved.Members.Select( item => item with { World = item.World! with { State = 0x0F } } ).ToArray() } ),
			"a handyman's state is not a mechanic's" );
	}
}
