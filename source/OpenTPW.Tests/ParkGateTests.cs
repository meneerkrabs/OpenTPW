using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// The park door (OpenTPW <see cref="ParkGate"/>): the entrance gate's script command, the open/close
/// walk over the attractions, the guest census and the deferred closure retry, as the original's door
/// routine (Mac 0x108ee4, PC FUN_00519ef0 — see docs/GATES.md) does them. The gate-script tests need the
/// original data (OPENTPW_GAME_PATH); the census tests are asset-free.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParkGateTests
{
	// ---------------------------------------------------------------- census (asset-free)

	[TestMethod]
	public void CensusCountsExactlyTheOriginalCellTypes()
	{
		// [BIN:STP-PPC:0xc3684 census] guests count on cell types 0, 1, 3, 9 and 10 (predicates 0x85190–0x85258);
		// the type-30 approach road and type-7 outside cells do not count.
		foreach ( var counted in new byte[] { 0, 1, 3, 9, 10 } )
			Assert.IsTrue( ParkGate.CountsCellType( counted ), $"type {counted} counts" );
		foreach ( var rejected in new byte[] { 2, 4, 7, 21, 30 } )
			Assert.IsFalse( ParkGate.CountsCellType( rejected ), $"type {rejected} does not count" );
	}

	[TestMethod]
	public void CensusCountsGuestsOnCountedCellsOnly()
	{
		var (simulation, _) = InsideGuests();
		var inside = simulation.SpawnInPark( 2, 0 ); // path (type 1)
		var queueing = simulation.SpawnInPark( 3, 0 );
		queueing.CellX = 4;
		queueing.CellY = 4; // a queue cell (type 3): the census counts it like the original does
		var economy = new ParkEconomy( EconomyTestData.Settings(), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
		var gate = new ParkGate( economy, () => null, () => Array.Empty<OriginalObjectRuntime>(), simulation );
		Assert.AreEqual( 2, gate.GuestCensus );

		var gone = simulation.SpawnInPark( 3, 0 );
		gone.State = GuestState.Gone; // a departed guest is not a thing on the map
		Assert.AreEqual( 2, gate.GuestCensus, "a Gone guest does not count" );
		Assert.AreNotEqual( inside.Id, gone.Id );
		Assert.AreNotEqual( inside.Id, queueing.Id );
	}

	// ---------------------------------------------------------------- gate script (original data)

	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestMethod]
	public void GateScriptDeclaresCommandAndStatusVariablesInEveryTheme()
	{
		UseOriginalData();
		foreach ( var theme in ObjectCatalog.Themes )
		{
			var runtime = GateRuntime( theme );
			Assert.IsNotNull( runtime?.Script, $"{theme} has a Gates script" );
			// [DATA:gates.RSE] VAR_COMMAND is the first declared variable and VAR_STATUS the second;
			// command 0 idles, 1 opens, 2 is the end-of-park sequence, status 1 = open.
			Assert.AreEqual( 0, runtime!.Script.GetVariableIndex( ParkGate.CommandVariable ), $"{theme} VAR_COMMAND index" );
			Assert.AreEqual( 1, runtime.Script.GetVariableIndex( ParkGate.StatusVariable ), $"{theme} VAR_STATUS index" );
		}
	}

	[TestMethod]
	public void DoorCommandsTheGateAndClosesAndOpensTheRides()
	{
		UseOriginalData();
		var (gate, gates, totem, economy) = Park();
		economy.OpenPark();
		gate.InitializeFromParkState();
		// A park that starts open commands its gate open; the ride starts open (placed open).
		Assert.AreEqual( 1, gates.GetVariableByName( ParkGate.CommandVariable ) );
		Assert.IsTrue( totem.IsOpen );

		// Closing with nobody inside: the gate is commanded shut and every attraction closes. The gate script
		// reports itself open (VAR_STATUS 1) while it stands open; the script does not run in this test.
		gates.SetVariable( ParkGate.StatusVariable, ParkGate.GateIsOpen );
		economy.OpenPark();
		economy.ClosePark();
		gate.DoorMoved( closed: true );
		Assert.AreEqual( 0, gates.GetVariableByName( ParkGate.CommandVariable ), "empty park: gate commanded shut" );
		Assert.IsFalse( totem.IsOpen, "the door closes the rides" );

		// Opening: the gate is commanded open and the ride opens again.
		economy.OpenPark();
		gate.DoorMoved( closed: false );
		Assert.AreEqual( 1, gates.GetVariableByName( ParkGate.CommandVariable ) );
		Assert.IsTrue( totem.IsOpen );
	}

	[TestMethod]
	public void PopulatedClosureHoldsTheGateUntilTheLastGuestLeaves()
	{
		UseOriginalData();
		var (gate, gates, totem, economy) = Park();
		var (simulation, grid) = InsideGuests();
		var gateObject = GateObject();
		var populated = new ParkGate( economy, () => gateObject, () => new[] { totem }, simulation );
		economy.OpenPark();
		populated.InitializeFromParkState();
		var inside = simulation.SpawnInPark( 2, 0 );
		gateObject.SetVariable( ParkGate.StatusVariable, ParkGate.GateIsOpen );

		economy.ClosePark();
		populated.DoorMoved( closed: true );
		Assert.AreEqual( 1, gateObject.GetVariableByName( ParkGate.CommandVariable ), "a guest inside holds the gate open" );
		Assert.IsFalse( totem.IsOpen, "the rides still close at once" );

		// The retry (Mac 0x10536c) only fires every 30 park turns (248 ms each) and only once the census is empty.
		inside.State = GuestState.Gone;
		Assert.AreEqual( 0, populated.GuestCensus );
		var retryTick = ParkCalendar.TickOfTurn( ParkGate.RetryPeriodTurns );
		for ( var tick = 0; tick < retryTick - 1; tick++ )
		{
			economy.Advance( 1 );
			populated.Tick();
		}
		Assert.AreEqual( 1, gateObject.GetVariableByName( ParkGate.CommandVariable ), "before park turn 30: not a retry boundary" );
		economy.Advance( 1 );
		populated.Tick();
		Assert.AreEqual( 0, gateObject.GetVariableByName( ParkGate.CommandVariable ), "park turn 30: the retry closes the gate" );
	}

	[TestMethod]
	public void StaffOutsideTheCountedCellsBlockTheRetry()
	{
		UseOriginalData();
		var (gate, gates, totem, economy) = Park();
		var (simulation, grid) = InsideGuests();
		var gateObject = GateObject();
		grid.Cells.SetType( 40, 40, (byte)2 ); // water: a cell the census does not count
		gateObject.SetVariable( ParkGate.StatusVariable, ParkGate.GateIsOpen );
		gateObject.SetVariable( ParkGate.CommandVariable, 1 );
		economy.OpenPark();
		economy.ClosePark();

		var blocked = new ParkGate( economy, () => gateObject, () => new[] { totem }, simulation,
			() => new[] { Staff( 40f, 40f ) } );
		economy.Advance( ParkCalendar.TickOfTurn( ParkGate.RetryPeriodTurns ) );
		blocked.Tick();
		Assert.AreEqual( 1, gateObject.GetVariableByName( ParkGate.CommandVariable ), "staff outside blocks the close" );
		Assert.AreEqual( 1, blocked.StaffOutside );

		var unblocked = new ParkGate( economy, () => gateObject, () => new[] { totem }, simulation,
			() => new[] { Staff( 2f, 0f ) } );
		economy.Advance( ParkCalendar.TickOfTurn( 2 * ParkGate.RetryPeriodTurns ) - economy.Tick );
		unblocked.Tick();
		Assert.AreEqual( 0, gateObject.GetVariableByName( ParkGate.CommandVariable ), "staff inside does not block" );

		var nowhere = new ParkGate( economy, () => gateObject, () => new[] { totem }, simulation, () => new[] { Staff( null, null ) } );
		nowhere.Tick();
		Assert.AreEqual( 0, nowhere.StaffOutside, "a staff member without a cell stands nowhere, not outside" );
	}

	[TestMethod]
	public void LevelDoorReadsTheCurrentPrototypeAfterSetup()
	{
		UseOriginalData();
		var (_, _, totem, economy) = Park();
		var objects = new ParkObjects( ObjectCatalog.Load( "jungle" ), new SandboxParkGrid() );
		OriginalObjectRuntime? prototype = null;
		var gate = ParkGate.ForLevel( economy, objects, prototype: () => prototype );
		Assert.AreEqual( 0, gate.Attractions.Count() );
		prototype = totem;
		economy.OpenPark();
		economy.ClosePark();
		gate.DoorMoved( closed: true );
		Assert.IsFalse( totem.IsOpen, "the prototype is outside ParkObjects but still closes" );
		Assert.IsFalse( totem.Visitors.IsOpen, "the prototype stops admitting guests" );
		economy.OpenPark();
		gate.DoorMoved( closed: false );
		Assert.IsTrue( totem.IsOpen );
		prototype = null;
		Assert.AreEqual( 0, gate.Attractions.Count(), "a removed prototype is no longer visited" );
	}

	[TestMethod]
	public void AttractionWalkDeduplicatesRuntimeAliases()
	{
		UseOriginalData();
		var (_, gates, totem, economy) = Park();
		var gate = new ParkGate( economy, () => gates, () => new[] { totem, totem, gates } );
		Assert.AreEqual( 1, gate.Attractions.Count(), "duplicate references and non-attraction gate are filtered" );
	}

	[DataTestMethod]
	[DataRow( GameSpeed.Normal )]
	[DataRow( GameSpeed.Fast )]
	[DataRow( GameSpeed.Fastest )]
	public void RetryUsesCrossedParkTurnsAtEverySpeedAndOnlyOncePerBoundary( GameSpeed speed )
	{
		UseOriginalData();
		var (gate, gates, _, economy) = Park();
		economy.Speed = speed;
		gates.SetVariable( ParkGate.StatusVariable, ParkGate.GateIsOpen );
		gates.SetVariable( ParkGate.CommandVariable, 1 );
		while ( economy.Turn < ParkGate.RetryPeriodTurns )
		{
			// A faster game gets several fixed ticks per frame; the gate sees only the state after each step.
			economy.Advance( (int)speed );
			gate.Tick();
			if ( economy.Turn < ParkGate.RetryPeriodTurns )
				Assert.AreEqual( 1, gates.GetVariableByName( ParkGate.CommandVariable ), $"speed {speed}, turn {economy.Turn}: no early retry" );
		}
		Assert.AreEqual( 0, gates.GetVariableByName( ParkGate.CommandVariable ), "first crossed 30-turn boundary closes" );
		gates.SetVariable( ParkGate.CommandVariable, 1 );
		gate.Tick();
		Assert.AreEqual( 1, gates.GetVariableByName( ParkGate.CommandVariable ), "repeated calls at the same tick do not duplicate the retry" );
		economy.Speed = GameSpeed.Paused;
		economy.AdvanceFixedTick();
		gate.Tick();
		Assert.AreEqual( 1, gates.GetVariableByName( ParkGate.CommandVariable ), "pause at the retry boundary cannot reissue commands" );
		economy.Advance( ParkCalendar.TickOfTurn( ParkGate.RetryPeriodTurns + 1 ) - economy.Tick );
		gate.Tick();
		Assert.AreEqual( 1, gates.GetVariableByName( ParkGate.CommandVariable ), "the rest of the qualifying turn does not retry" );
	}

	[TestMethod]
	public void RetryFindsBoundaryWhenUpdatesSkipItsFirstFixedTick()
	{
		UseOriginalData();
		var (gate, gates, _, economy) = Park();
		gates.SetVariable( ParkGate.StatusVariable, ParkGate.GateIsOpen );
		gates.SetVariable( ParkGate.CommandVariable, 1 );
		economy.Advance( ParkCalendar.TickOfTurn( ParkGate.RetryPeriodTurns + 1 ) );
		gate.Tick();
		Assert.AreEqual( 0, gates.GetVariableByName( ParkGate.CommandVariable ), "turn 30 was crossed although no call landed on its first fixed tick" );
	}

	[TestMethod]
	public void CensusRejectsPositionsOutsideTheMapBeforeReadingTypeZeroFallback()
	{
		var (simulation, grid) = InsideGuests();
		var guest = simulation.SpawnInPark( 2, 0 );
		var economy = new ParkEconomy( EconomyTestData.Settings(), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
		var gate = new ParkGate( economy, () => null, () => Array.Empty<OriginalObjectRuntime>(), simulation );
		foreach ( var cell in new[] { (-1, 0), (0, -1), (grid.CountX, 0), (0, grid.CountY) } )
		{
			guest.CellX = cell.Item1;
			guest.CellY = cell.Item2;
			Assert.AreEqual( 0, gate.GuestCensus, $"{cell} is outside, despite RawTypeAt returning counted type 0" );
		}
	}

	[TestMethod]
	public void StaffFractionalAndNonFinitePositionsCannotAliasCountedCells()
	{
		var (simulation, grid) = InsideGuests();
		grid.Cells.SetType( 1, 0, (byte)7 );
		var economy = new ParkEconomy( EconomyTestData.Settings(), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
		var positions = new[] { Staff( -0.25f, 0 ), Staff( 0, -0.25f ), Staff( grid.CountX, 0 ),
			Staff( 0, grid.CountY ), Staff( float.NaN, 0 ), Staff( 0, float.PositiveInfinity ),
			Staff( 1.75f, 0.5f ), Staff( 0.75f, 0.5f ), Staff( null, null ) };
		var gate = new ParkGate( economy, () => null, () => Array.Empty<OriginalObjectRuntime>(), simulation, () => positions );
		Assert.AreEqual( 7, gate.StaffOutside, "seven invalid/outside cells block; one valid path cell and one unplaced member do not" );
	}

	private static StaffMember Staff( float? x, float? y ) =>
		new() { Id = 1, Type = StaffType.Mechanic, NameIndex = 0, Grade = 1, CellX = x, CellY = y };

	/// <summary>A path row y = 0 with a queue cell at (4, 4): cells the census counts and cells it does not.</summary>
	private static (GuestSimulation Simulation, GuestPathGrid Grid) InsideGuests()
	{
		var grid = new GuestPathGrid( 64, 64 );
		for ( var x = 0; x < 10; x++ )
			grid.SetPath( x, 0, true );
		grid.Cells.SetType( 4, 4, ParkCellType.Queue );
		var simulation = new GuestSimulation( grid, GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false };
		return (simulation, grid);
	}

	/// <summary>The Gates fixed item's runtime of a theme, with its script.</summary>
	private static OriginalObjectRuntime? GateRuntime( string theme )
	{
		var entry = ObjectCatalog.Load( theme ).Entries.FirstOrDefault( entry => entry.SettingsName.Equals( "Gates", StringComparison.OrdinalIgnoreCase ) );
		return entry == null ? null : new OriginalObjectRuntime( entry );
	}

	private static OriginalObjectRuntime GateObject() => GateRuntime( "jungle" )!;

	/// <summary>The door of a park with the jungle Gates and the Inca Totem (a scripted HasQueue attraction).</summary>
	private (ParkGate Gate, OriginalObjectRuntime Gates, OriginalObjectRuntime Totem, ParkEconomy Economy) Park()
	{
		var catalog = ObjectCatalog.Load( "jungle" );
		var gates = GateObject();
		var totem = new OriginalObjectRuntime( catalog.Entries.Single( entry => entry.InfoId == 1110 ) );
		var economy = ParkEconomy.CreateForTheme( "jungle", easy: false );
		var (simulation, _) = InsideGuests();
		return (new ParkGate( economy, () => gates, () => new[] { totem }, simulation ), gates, totem, economy);
	}

	private void UseOriginalData()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original gate script." );
		originalFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( OriginalParkImportTests.OriginalDataPath() );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		initialized = true;
	}

	[TestInitialize]
	public void Initialize() => Log = new(); // the door traces through the static logger, which tests set up themselves

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
	}
}
