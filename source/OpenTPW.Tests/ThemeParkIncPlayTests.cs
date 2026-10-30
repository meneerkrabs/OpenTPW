using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The experimental Theme Park Inc slice (docs/THEME-PARK-INC.md): the edition capability policy on synthetic data,
/// and the supported gameplay route over the original retail corpus when <c>OPENTPW_TPI_PATH</c> names it. The route is
/// the headless composition the slice drives — the build rules of <see cref="ParkObjects.Check"/>, an
/// <see cref="OriginalObjectRuntime"/> with its original script, a <see cref="ParkPathBuilder"/> (free without an economy, charging the
/// Theme Park Inc money economy with one) and <see cref="GuestSimulation"/> — composed like the M3 gate and the determinism tests, because placed
/// <see cref="OriginalObject"/>s build GPU models. None of this compares with the original game; it checks that the
/// slice's own systems keep working on Theme Park Inc data.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncPlayTests
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

	// ---- Capability policy (synthetic; no corpus needed) ---------------------------------

	[TestMethod]
	public void ThemeParkWorldRunsTheFullGame()
	{
		GameEditions.Select( GameEdition.ThemeParkWorld );
		var capabilities = GameEditions.Capabilities;
		Assert.IsTrue( capabilities.Economy && capabilities.Money && capabilities.Research && capabilities.Challenges && capabilities.OriginalHud && capabilities.Building && capabilities.Guests
			&& capabilities.Staff && capabilities.Advisor && capabilities.FrontEnd && capabilities.ParkSaves && capabilities.Sharing,
			"every implemented system runs for Theme Park World" );
		Assert.IsFalse( capabilities.ExperimentalHud, "the experimental HUD is the Theme Park Inc slice's" );
		Assert.IsFalse( capabilities.PersonnelFileFrontEnd, "the Personnel File is the Theme Park Inc slice's start screen" );
		Assert.IsNull( capabilities.ExperimentalNote, "the full game needs no experimental caveat" );
	}

	[TestMethod]
	public void TheSliceGivesBuildingGuestsAndMoneyAndRefusesTheUntracedSystems()
	{
		GameEditions.Select( GameEdition.ThemeParkInc );
		var capabilities = GameEditions.Capabilities;
		Assert.IsTrue( capabilities.ExperimentalHud && capabilities.Building && capabilities.Guests, "the slice builds and routes guests" );
		Assert.IsTrue( capabilities.PersonnelFileFrontEnd, "the slice starts at its own Personnel File screen" );
		Assert.IsTrue( capabilities.Money, "the money runs on Theme Park Inc's own balance keys" );
		Assert.IsFalse( capabilities.Economy, "the whole Theme Park World economy needs keys Theme Park Inc's Standard.sam lacks" );
		Assert.IsFalse( capabilities.Staff || capabilities.Research || capabilities.Challenges, "staff, research and challenges stay refused" );
		Assert.IsFalse( capabilities.OriginalHud || capabilities.Advisor || capabilities.FrontEnd
			|| capabilities.ParkSaves || capabilities.Sharing, "the untraced systems stay refused" );
		var note = capabilities.ExperimentalNote;
		Assert.IsNotNull( note );
		StringAssert.Contains( note, "experimental" );
		StringAssert.Contains( note, "money only", "the note names what runs" );
		foreach ( var refused in new[] { "staff", "research", "challenges", "advisor", "missions", "Global Master Save", "COS", "saves", "sharing" } )
			StringAssert.Contains( note, refused, $"the note names the refused '{refused}'" );
		// the slice HUD's limits line shows the note in the selected language; its English column is the note itself (the --tpi-smoke-test compares them on screen)
		Assert.AreEqual( note, SupplementaryStrings.Get( OpenTpwText.TpiLimits, "English" ) );
		foreach ( var language in SupplementaryStrings.SupportedLanguages )
			StringAssert.Contains( SupplementaryStrings.Get( OpenTpwText.TpiLimits, language ), "Personnel", $"the {language} limits line names the Personnel File start screen" );
		StringAssert.Contains( GameEditions.SliceNotice, "not verified" );
		StringAssert.Contains( GameEditions.SliceNotice, "money" );
	}

	[TestMethod]
	public void StartupRefusesSaveAndSharingCommandsOnThemeParkIncData()
	{
		GameEditions.Select( GameEdition.ThemeParkInc );
		Assert.IsNull( GameEditions.ViewerRefusal( new[] { "--load-original-level", "science", "--capture-world", "a.png" } ),
			"the slice itself starts (captures included)" );
		foreach ( var refused in new[] { "--park-resume-smoke", "--export-park", "--import-park", "--visit-park", "--sandbox", "--smoke-test", "--front-end", "--setup" } )
		{
			var args = new[] { "--load-original-level", "science", refused, "value" };
			var message = GameEditions.ViewerRefusal( args );
			Assert.IsNotNull( message, $"{refused} is refused on Theme Park Inc data" );
			StringAssert.Contains( message, refused, $"the refusal names {refused}" );
			StringAssert.Contains( message, "experimental slice" );
		}
	}

	// ---- Supported gameplay route over the corpus ----------------------------------------

	private string UseTpiData()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		previousLog = Log;
		Log = new();
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		GameEditions.Select( GameEdition.ThemeParkInc );
		ObjectCatalog.BonusDataRoot = null;
		return data;
	}

	/// <summary>
	/// The composition the slice drives, without the economy and without GPU models: the world's paths (its prebuilt
	/// save in science, its MAP initial path elsewhere), its catalog under the build rules, a free path builder and the
	/// guest simulation over one shared script world. The prebuilt save's own objects run as headless runtimes, exactly
	/// as <see cref="ParkObjects.ImportOriginal"/> selects and places them.
	/// </summary>
	private sealed class SlicePark
	{
		public readonly OriginalPark Park;
		public readonly ObjectCatalog Catalog;
		public readonly OriginalParkGrid BuildGrid;
		public readonly GuestPathGrid Grid;
		public readonly GuestSimulation Guests;
		public readonly ParkPathBuilder Paths;
		public readonly RideScriptWorld Scripts = new();
		public readonly List<OriginalObjectRuntime> Runtimes = new();
		/// <summary>Cells the world's own records hold (placed objects and fixed items of a prebuilt save).</summary>
		public readonly HashSet<(int X, int Y)> Occupied = new();

		/// <summary>The Theme Park Inc money economy, when <c>withMoney</c> is set (the path builder then charges it).</summary>
		public readonly ParkEconomyRuntime? Money;

		public SlicePark( string world, int? arrivalTimeBetween = null, bool withMoney = false )
		{
			Park = OriginalPark.Load( world );
			if ( withMoney )
				Money = ParkEconomyRuntime.ForThemeParkIncLevel( Park );
			Catalog = ObjectCatalog.Load( world );
			BuildGrid = new OriginalParkGrid( Park );
			Grid = GuestPathGrid.FromOriginal( Park.Map, Park.Save );
			var settings = GuestSettings.Load( world );
			if ( arrivalTimeBetween is int interval )
				settings.ArrivalTimeBetween = interval;
			Guests = new GuestSimulation( Grid, settings, Level.GuestSeed );
			Paths = new ParkPathBuilder( Grid.Cells, Grid, () => Money?.Economy, BuildGrid, ( x, y ) => Occupied.Contains( (x, y) ) );
			if ( Park.Save == null )
				return;
			foreach ( var record in Park.Save.PlacedObjects.Select( item => item.Record ).Concat( Park.Save.UnresolvedObjects ).Concat( Park.Save.FixedItems ) )
			{
				var entry = Catalog.Find( record.InfoId );
				if ( entry == null || entry.IsTool )
					continue;
				foreach ( var cell in ObjectFootprint.GetCells( entry.Shape, record.X, record.Y, record.Rotation ) )
					Occupied.Add( (cell.X, cell.Y) );
				var runtime = new OriginalObjectRuntime( entry, Scripts, open: true );
				Runtimes.Add( runtime );
				Level.RegisterWithGuests( runtime, ObjectFootprint.GetAccessPoints( entry.Shape, record.X, record.Y, record.Rotation ), Guests, record.Rotation );
			}
		}

		public bool IsBlocked( int x, int y ) => Occupied.Contains( (x, y) ) || Grid.IsWalkable( x, y ) || Grid.IsQueue( x, y );

		/// <summary>One simulation step of the slice's frame (Level.AdvanceSimulation without the economy systems).</summary>
		public void Step( double deltaSeconds )
		{
			Guests.Tick( (float)deltaSeconds );
			foreach ( var runtime in Runtimes )
				runtime.Simulate( deltaSeconds );
		}
	}

	[TestMethod]
	public void ScienceBuildsPlacesAndRemovesACatalogObjectWithoutAnEconomy()
	{
		UseTpiData();
		var park = new SlicePark( "science" );
		Assert.IsTrue( park.Grid.WalkableCount > 0, "the prebuilt save's paths are walkable" );
		Assert.IsTrue( park.Runtimes.Count > 0, "the prebuilt save's objects run as headless runtimes" );
		// A buildable ride with a queue, placed through the HUD build flow's rules beside an existing path.
		var entry = park.Catalog.Buildable.First( candidate => candidate.HasQueue && candidate.ScriptPath != null );
		var placed = PlaceBesidePath( park, entry, out var anchor, out var rotation );
		Assert.IsNotNull( placed, $"{entry} has no buildable site beside the imported paths" );
		Assert.AreNotEqual( 0, placed!.Visitors.AttractionId );
		Assert.IsTrue( park.Guests.Attractions.Contains( placed.Visitors ), "the placed ride registers with the guests" );
		// The placed object and the guests advance together for ten simulated seconds; the script must not fault.
		for ( var step = 0; step < 10 * FixedStepClock.TicksPerSecond; step++ )
			park.Step( FixedStepClock.TickDuration );
		Assert.AreNotEqual( RideVMState.Faulted, placed.Script?.State, placed.Script?.FaultMessage );
		// The remove flow's guest side (Level wires this to Objects.ObjectRemoved): the attraction unregisters.
		park.Guests.Unregister( placed.Visitors );
		park.Runtimes.Remove( placed );
		Assert.IsFalse( park.Guests.Attractions.Contains( placed.Visitors ), "a removed object unregisters with the guests" );
		// Its cells are buildable again once the footprint is freed.
		park.Occupied.ExceptWith( ObjectFootprint.GetCells( entry.Shape, anchor.X, anchor.Y, rotation ).Select( cell => (cell.X, cell.Y) ) );
		Assert.AreEqual( OriginalPlacementResult.Allowed, ParkObjects.Check( park.BuildGrid, park.IsBlocked, entry, anchor.X, anchor.Y, rotation ),
			"the freed site builds again" );
	}

	[TestMethod]
	public void EveryWorldHasPlantsThatRequireWateringAndNoRideDoes()
	{
		// [BIN:TPI-EXE:0x5DB654 thing flag +0x43 bit 0x10] the flag comes from the type's UsageInfo.RequiresWatering (the plants' bushes, trees and flower beds), which selects the scrap percentage's script branch (ECON-061)
		UseTpiData();
		foreach ( var world in new[] { "arabian", "science", "water" } )
		{
			var flagged = EconomyObjectCatalog.Load( world, easy: false ).Objects.Where( info => info.RequiresWatering ).ToList();
			Assert.IsTrue( flagged.Count >= 5, $"{world}: {flagged.Count} flagged objects" );
			Assert.IsTrue( flagged.All( info => info.Kind == ParkObjectKind.Feature ), $"{world}: only features require watering" );
		}
	}

	[TestMethod]
	public void EveryWorldHasABuildableQueueRideBesideItsPaths()
	{
		UseTpiData();
		foreach ( var world in new[] { "arabian", "science", "water" } )
		{
			var park = new SlicePark( world );
			var entry = park.Catalog.Buildable.FirstOrDefault( candidate => candidate.ScriptPath != null && candidate.HasQueue );
			Assert.IsNotNull( entry, $"{world}: the catalog offers a buildable ride with a queue" );
			var placed = PlaceBesidePath( park, entry!, out _, out _ );
			Assert.IsNotNull( placed, $"{world}: {entry} builds beside a path under the world's own terrain rules" );
		}
	}

	[TestMethod]
	public void PathsLayFreeWithoutAnEconomy()
	{
		UseTpiData();
		var park = new SlicePark( "science" );
		// Extend the imported paths by a straight line somewhere: without an economy nothing is charged.
		var line = StraightExtension( park );
		Assert.IsNotNull( line, "a buildable line exists beside the imported paths" );
		var (start, end) = line!.Value;
		var before = park.Grid.WalkableCount;
		var result = park.Paths.BuildSegment( start, end );
		Assert.IsTrue( result.Completed, $"the free builder lays from ({start.X},{start.Y}) to ({end.X},{end.Y}): {result.StoppedBy}" );
		Assert.AreEqual( 0, result.Charged, "without an economy nothing is charged" );
		Assert.AreEqual( before + result.Built.Count, park.Grid.WalkableCount, "the built cells are walkable" );
		// Removal through the remove tool's route clears a built cell again.
		var built = result.Built[0];
		Assert.AreEqual( CellBuildResult.Ok, park.Paths.Remove( built.X, built.Y ) );
		Assert.AreNotEqual( ParkCellType.Path, park.Grid.Cells.TypeAt( built.X, built.Y ) );
	}

	/// <summary>A straight path extension of at least two cells off an imported path cell, in any direction.</summary>
	private static ((int X, int Y) Start, (int X, int Y) End)? StraightExtension( SlicePark park )
	{
		for ( var y = 0; y < park.Grid.CountY; y++ )
		{
			for ( var x = 0; x < park.Grid.CountX; x++ )
			{
				if ( park.Grid.Cells.TypeAt( x, y ) != ParkCellType.Path )
					continue;
				foreach ( var (dx, dy) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) } )
				{
					var end = (X: x + dx, Y: y + dy);
					var length = 0;
					while ( park.Grid.Cells.InBounds( end.X, end.Y ) && park.Grid.Cells.TypeAt( end.X, end.Y ) == ParkCellType.Empty
						&& park.Paths.CheckCell( end.X, end.Y ) == CellBuildResult.Ok )
					{
						length++;
						end = (end.X + dx, end.Y + dy);
					}
					if ( length >= 2 )
						return ((x, y), (end.X - dx, end.Y - dy));
				}
			}
		}
		return null;
	}

	[TestMethod]
	public void GuestsArriveWalkAndQueueAtThePrebuiltRidesInTheSciencePark()
	{
		UseTpiData();
		// Arrivals sped up as in the M3 gate's ArrivalTimeBetween option; the slice runs the same guest systems.
		var park = new SlicePark( "science", arrivalTimeBetween: 2 );
		Assert.IsTrue( park.Grid.WalkableCount > 0, "the prebuilt save's paths are walkable" );
		Assert.IsTrue( park.Guests.Attractions.Any(), "the prebuilt save's attractions register with the guests" );
		for ( var step = 0; step < 90 * FixedStepClock.TicksPerSecond && park.Guests.GetStatistics().InPark == 0; step++ )
			park.Step( FixedStepClock.TickDuration );
		var statistics = park.Guests.GetStatistics();
		Assert.IsTrue( statistics.InPark > 0, "guests arrive from the bus stop through the ticket booth" );
		// Arrived guests choose attractions and queue: what the live slice shows by about forty seconds.
		var queueing = 0;
		for ( var step = 0; step < 120 * FixedStepClock.TicksPerSecond; step++ )
		{
			park.Step( FixedStepClock.TickDuration );
			if ( step % FixedStepClock.TicksPerSecond == 0 )
				queueing = Math.Max( queueing, park.Guests.GetStatistics().Queueing );
		}
		Assert.IsTrue( queueing > 0, "an arrived guest queues at a prebuilt attraction within two minutes" );
	}

	// ---- The money over the corpus (docs/THEME-PARK-INC.md, "Economy") --------------------

	[TestMethod]
	public void TheMoneySettingsLoadFromTheCorpusForEveryWorld()
	{
		UseTpiData();
		foreach ( var world in new[] { "arabian", "science", "water" } )
		{
			var settings = BalanceSettings.LoadThemeParkInc( world );
			Assert.AreEqual( GameEdition.ThemeParkInc, settings.Edition, world );
			Assert.AreEqual( 50000L, settings.InitialCash, $"{world}: BankAccountInfo.InitialCash" );
			Assert.AreEqual( 25, settings.InitialAdmissionFee, $"{world}: BankAccountInfo.InitialAdmissionFee" );
			Assert.AreEqual( new CellCosts( 25, 10, 40, 50, 75 ), settings.Costs, $"{world}: Costs.*" );
			Assert.AreEqual( 8, settings.Loans.Count, $"{world}: eight of LoanInfo's ten slots are used" );
			Assert.AreEqual( new LoanOffer( 0, 100000, 20, 36, 0 ), settings.Loans[0], world );
			Assert.AreEqual( new LoanOffer( 7, 65000, 21, 30, 7 ), settings.Loans[7], world );
			Assert.AreEqual( 0, settings.Roles.Count, $"{world}: no staff keys are invented" );
			Assert.IsFalse( settings.Standard.Contains( "PerGradeStaffConsts[0].BaseWage" ), $"{world}: Theme Park Inc's file lacks the staff wages" );
			Assert.IsFalse( settings.CanSpendTickets || settings.CanEarnTickets, world );
		}
		Assert.AreEqual( 8, BalanceSettings.LoadThemeParkInc( "arabian" ).Loans[1].LenderNameIndex, "the theme layer renames lenders over the global file" );
	}

	[TestMethod]
	public void TheEntryFeeJudgementUsesTheDataDivisor()
	{
		UseTpiData();
		// levels/Standard.sam holds 6 where Theme Park World's holds 4; arabian's own layer sets 4 again. The booth reads the same words as the balance.
		foreach ( var (world, divisor) in new[] { ("science", 6), ("water", 6), ("arabian", 4) } )
		{
			Assert.AreEqual( divisor, BalanceSettings.LoadThemeParkInc( world ).EntryFee.ExcitementToCostDivisor, $"{world}: balance" );
			var guests = GuestSettings.Load( world );
			Assert.AreEqual( divisor, guests.ExcitementToCostDivisor, $"{world}: guest settings" );
			Assert.AreEqual( 20u + 600 / (uint)divisor, BoothVerdict.ParkValue( 600, 0, guests ), $"{world}: park value = points / (divisor + draw % (divisor / 2 + 1)) + minimum fee" );
		}
	}

	[TestMethod]
	public void TheCatalogueHoldsTheThemeParkIncObjectKeysAndEveryBuildableObject()
	{
		UseTpiData();
		var park = new SlicePark( "science", withMoney: true );
		var economy = park.Money!.Economy;
		var ride = economy.Catalog.Objects.First( info => info.Kind == ParkObjectKind.Ride && info.Upgrades[0].RunningCost > 0 );
		Assert.IsTrue( ride.ExpectedPrice > 0, "Info.ExpectedPrice is read (rides carry 25)" );
		Assert.IsTrue( ride.Upgrades[0].CostOfUpgrade > 0 );
		var unpriced = park.Catalog.Buildable.Where( entry => !economy.Catalog.TryGet( entry.InfoId, out _ ) ).Select( entry => entry.DisplayName ).ToList();
		Assert.AreEqual( 0, unpriced.Count, $"every object of the build list has a price: {string.Join( ", ", unpriced )}" );
	}

	[TestMethod]
	public void ASciencePark_ChargesBuildsAndPaths_PaysRunningCostsMonthlyAndAdvancesItsCalendar()
	{
		UseTpiData();
		var park = new SlicePark( "science", withMoney: true );
		var economy = park.Money!.Economy;
		var events = new List<ParkEvent>();
		economy.EventRaised += events.Add;
		Assert.AreEqual( 50000L, economy.Balance, "starting cash" );
		Assert.AreEqual( 25, economy.EntranceFee, "starting admission fee" );
		Assert.AreEqual( new DateTime( 2000, 1, 1 ), ParkCalendar.DateTimeAt( economy.Tick ), "the calendar starts on January 1, 2000" );
		Assert.AreEqual( 0, economy.Staff.Members.Count + economy.Staff.Candidates.Count, "no staff pool is drawn" );

		// a path line charges Costs.PathCell per cell through the park's path builder
		var line = StraightExtension( park );
		Assert.IsNotNull( line, "a buildable line exists beside the imported paths" );
		var segment = park.Paths.BuildSegment( line!.Value.Start, line.Value.End );
		Assert.IsTrue( segment.Completed, segment.StoppedBy.ToString() );
		Assert.AreEqual( segment.Built.Count * 10L, segment.Charged, "Costs.PathCell 10 per cell" );
		Assert.AreEqual( 50000L - segment.Charged, economy.Balance );

		// an object charges Upgrades[0].CostOfUpgrade, after the placement rules
		var entry = park.Catalog.Buildable
			.Where( candidate => candidate.HasQueue && candidate.ScriptPath != null && economy.Catalog.TryGet( candidate.InfoId, out var priced )
				&& priced.PurchaseCost is > 0 and <= 20000 && priced.Upgrades[0].RunningCost > 0 )
			.OrderBy( candidate => candidate.InfoId ).FirstOrDefault();
		Assert.IsNotNull( entry, "a priced ride with a running cost exists" );
		economy.Catalog.TryGet( entry!.InfoId, out var info );
		var before = economy.Balance;
		var placed = PlaceBesidePath( park, entry, out _, out _ );
		Assert.IsNotNull( placed, $"{entry} builds beside a path" );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( entry.InfoId, out var state ) );
		Assert.AreEqual( before - info.PurchaseCost, economy.Balance, "the purchase price is debited" );
		Assert.AreEqual( info.PurchaseCost, state!.TotalSpent );
		Assert.AreEqual( info.Upgrades[0].RunningCost, economy.RunningCost( state ), "the running cost of level 0" );

		// the first month end debits the running cost and moves the calendar on
		var runningBefore = economy.Balance;
		economy.AdvanceMonths( 1 );
		Assert.AreEqual( new DateTime( 2000, 2, 1 ), ParkCalendar.DateTimeAt( economy.Tick ).Date, "the calendar advanced a month (to the first park turn of the day)" );
		Assert.AreEqual( runningBefore - info.Upgrades[0].RunningCost, economy.Balance, "one month of running costs" );
		Assert.IsTrue( events.Any( item => item.Kind == ParkEventKind.RunningCostsPaid && item.Amount == info.Upgrades[0].RunningCost ), "the debit is announced" );
		Assert.AreEqual( info.Upgrades[0].RunningCost, state.CostsLastMonth, "and enters the object's cost statistic" );
		economy.AdvanceMonths( 1 );
		Assert.AreEqual( runningBefore - 2 * info.Upgrades[0].RunningCost, economy.Balance, "every month pays again" );
	}

	[TestMethod]
	public void ThePlacementFlowDebitsThePriceExactlyOnceAndALongRunDropsNoLitter()
	{
		UseTpiData();
		var park = new SlicePark( "science", arrivalTimeBetween: 2, withMoney: true );
		var runtime = park.Money!;
		var economy = runtime.Economy;
		runtime.AttachGuests( park.Guests );
		Assert.IsTrue( economy.IsParkOpen, "the Theme Park Inc park opens when its guests attach" );
		// the sequence of Level.PlaceObject: the placement rules, one TryBuild, the placement, then the link to the bought record (the link charges nothing)
		var entry = park.Catalog.Buildable
			.Where( candidate => candidate.HasQueue && candidate.ScriptPath != null && economy.Catalog.TryGet( candidate.InfoId, out var priced ) && priced.PurchaseCost is > 0 and <= 20000 )
			.OrderBy( candidate => candidate.InfoId ).First();
		economy.Catalog.TryGet( entry.InfoId, out var info );
		var placed = PlaceBesidePath( park, entry, out _, out _ );
		Assert.IsNotNull( placed );
		var before = economy.Balance;
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( entry.InfoId, out var bought ) );
		runtime.LinkAttraction( placed!.Visitors, entry.InfoId );
		Assert.AreEqual( before - info.PurchaseCost, economy.Balance, "exactly one debit of the purchase price, linking included" );
		Assert.AreEqual( info.PurchaseCost, bought!.TotalSpent );
		// a long run of guests: no litter drops, as no handyman would ever sweep it (Facilities, the toilets' directory, is not in the slice's catalogue; the toilet wear is a synthetic test)
		var cashAfterBuilds = economy.Balance;
		for ( var step = 0; step < 15 * 60 * FixedStepClock.TicksPerSecond; step++ )
		{
			park.Step( FixedStepClock.TickDuration );
			runtime.FixedTick();
		}
		Assert.IsTrue( park.Guests.GetStatistics().InPark > 0, "guests came" );
		Assert.IsFalse( economy.Objects.Any( item => ParkEconomy.IsDirtyToilet( item ) && economy.Catalog.TryGet( item.InfoId, out var relief ) && relief.ProvidesRelief ), "no toilet is dirty" );
		Assert.AreEqual( 0, economy.LitterItems, "no litter drops" );
		Assert.IsFalse( economy.IsBankrupt );
		Assert.IsTrue( economy.Balance > cashAfterBuilds - 100000, "the run only earned and paid running costs" );
	}

	[TestMethod]
	public void TheMoneyRulesRefuseStaffResearchAndLand()
	{
		UseTpiData();
		var park = new SlicePark( "science", withMoney: true );
		var economy = park.Money!.Economy;
		Assert.IsFalse( economy.Rules.Staff || economy.Rules.Research || economy.Rules.Challenges );
		Assert.IsFalse( economy.Features.GoldenTickets || economy.Features.Challenges || economy.Features.ResearchPanel || economy.Features.Upgrades );
		Assert.ThrowsException<InvalidOperationException>( () => economy.Hire( 1 ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NoLandForSale, economy.TryBuyCells( CellPurchase.Land, 1 ), "Theme Park Inc has no Buy Land" );
		Assert.ThrowsException<InvalidOperationException>( () => economy.ChargeLand( 100, 1 ) );
		Assert.AreEqual( 50000L, economy.Balance, "neither charged anything" );
	}

	/// <summary>Places <paramref name="entry"/> through the HUD build flow's rules (Level.PlaceObject without the purchase) beside a walkable path cell.</summary>
	private static OriginalObjectRuntime? PlaceBesidePath( SlicePark park, ObjectCatalogEntry entry, out (int X, int Y) anchor, out int rotation )
	{
		// The HUD centres the footprint on the clicked cell, so the click sits on free ground beside a path (the
		// object's own entrance cell must still open onto one, which RegisterWithGuests enforces).
		var clicks = CellsNearPaths( park );
		foreach ( var (x, y) in clicks )
		{
			foreach ( var turn in new[] { 0, 90, 180, 270 } )
			{
				(anchor, rotation) = (Level.GetCentredAnchor( entry, x, y, turn ), turn);
				if ( ParkObjects.Check( park.BuildGrid, park.IsBlocked, entry, anchor.X, anchor.Y, rotation ) != OriginalPlacementResult.Allowed )
					continue;
				var runtime = new OriginalObjectRuntime( entry, park.Scripts, open: true );
				var points = ObjectFootprint.GetAccessPoints( entry.Shape, anchor.X, anchor.Y, rotation );
				if ( Level.RegisterWithGuests( runtime, points, park.Guests, rotation ) || !runtime.IsAttraction )
				{
					foreach ( var cell in ObjectFootprint.GetCells( entry.Shape, anchor.X, anchor.Y, rotation ) )
						park.Occupied.Add( (cell.X, cell.Y) );
					park.Runtimes.Add( runtime );
					return runtime;
				}
				runtime.Stop();
			}
		}
		anchor = default;
		rotation = 0;
		return null;
	}

	/// <summary>Free cells whose Chebyshev distance to a walkable path cell is 1 to 4 (a click beside the paths).</summary>
	private static IEnumerable<(int X, int Y)> CellsNearPaths( SlicePark park )
	{
		var paths = new List<(int X, int Y)>();
		for ( var y = 0; y < park.Grid.CountY; y++ )
			for ( var x = 0; x < park.Grid.CountX; x++ )
				if ( park.Grid.IsWalkable( x, y ) )
					paths.Add( (x, y) );
		for ( var y = 0; y < park.Grid.CountY; y++ )
			for ( var x = 0; x < park.Grid.CountX; x++ )
		{
			if ( park.Grid.IsWalkable( x, y ) || park.Grid.IsQueue( x, y ) || park.Occupied.Contains( (x, y) ) )
				continue;
			if ( paths.Any( path => Math.Max( Math.Abs( path.X - x ), Math.Abs( path.Y - y ) ) is >= 1 and <= 4 ) )
				yield return (x, y);
		}
	}
}
