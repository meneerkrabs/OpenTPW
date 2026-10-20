using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic TPW saves restore the known lost-world state without replaying its transition.</summary>
[TestClass]
public class ImportedLostParkTests
{
	[TestInitialize]
	public void InitializeLog() => Log ??= new();

	private static ParkEconomy Import( uint state, uint closed, List<ParkEvent> events )
	{
		var settings = OriginalSaveImportTests.Settings();
		var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		park.EventRaised += events.Add;
		var save = OriginalSaveImportTests.Park( settings, parkClosed: closed, worldState: state );
		Assert.AreEqual( state, save.World.Variables.WorldState );
		OriginalEconomyImport.Apply( park, save );
		return park;
	}

	[TestMethod]
	public void LostImportKeepsBothSavedGateStatesThroughAttachmentWithoutTransitionEvents()
	{
		foreach ( var closed in new uint[] { 0, 1 } )
		{
			var events = new List<ParkEvent>();
			var park = Import( 4, closed, events );
			Assert.IsTrue( park.IsBankrupt );
			Assert.AreEqual( closed == 0, park.IsParkOpen );
			var guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 1 );
			// ForResume has no Import record: this exercises the shared callback attachment, not deferred original opening.
			var runtime = ParkEconomyRuntime.ForResume( park, ParkStartKind.FullSimulation );
			runtime.AttachGuests( guests );
			Assert.IsTrue( guests.ArrivalsHalted!(), "the actual runtime attachment reads the imported loss flag" );
			Assert.AreEqual( closed == 0, park.IsParkOpen );
			park.OpenPark();
			Assert.AreEqual( closed == 0, park.IsParkOpen, "attachment or a subsequent open command cannot replace saved loss state" );
			Assert.IsFalse( events.Any( e => e.Kind is ParkEventKind.ParkOpened or ParkEventKind.Bankrupt or ParkEventKind.BankruptcyMessage ), "loading does not replay the transition" );
		}
	}

	[TestMethod]
	public void LostImportStopsThingWearAndResearchButKeepsTheCalendarAndBankTail()
	{
		var lostEvents = new List<ParkEvent>();
		var lost = Import( 4, 0, lostEvents );
		var normal = Import( 0, 0, new() );
		foreach ( var park in new[] { lost, normal } )
		{
			var ride = park.Objects.Single( item => item.InfoId == 1100 );
			ride.IsOpen = true;
			var operations = new EconomyTestData.RideOperations();
			operations.Set( ride.Id, new RideOperation( true, 5, 100 ) );
			park.RideOperations = operations;
		}
		var initialRepair = lost.Objects.Single( item => item.InfoId == 1100 ).Repair;
		var research = ParkResumeJson.Serialize( ParkSaveFile.Capture( lost ).Research );
		var turn = lost.Turn;
		EconomyTestData.AdvanceToTurn( lost, turn + 64 );
		EconomyTestData.AdvanceToTurn( normal, turn + 64 );
		Assert.AreEqual( initialRepair, lost.Objects.Single( item => item.InfoId == 1100 ).Repair );
		Assert.IsTrue( normal.Objects.Single( item => item.InfoId == 1100 ).Repair < initialRepair, "the normal-world control really runs wear" );
		CollectionAssert.AreEqual( research, ParkResumeJson.Serialize( ParkSaveFile.Capture( lost ).Research ) );
		Assert.IsTrue( lostEvents.Any( e => e.Kind == ParkEventKind.DayEnded ) );
		var month = lost.Ledger.CurrentMonthIndex;
		var balance = lost.Balance;
		var wages = lost.Staff.TotalMonthlyWages;
		Assert.IsTrue( lost.Staff.Members.Count > 0 && wages > 0 );
		lost.AdvanceMonths( 1 );
		Assert.IsTrue( lost.Ledger.CurrentMonthIndex > month );
		Assert.IsTrue( lostEvents.Any( e => e.Kind == ParkEventKind.MonthEnded ) );
		Assert.IsTrue( lostEvents.Any( e => e.Kind == ParkEventKind.WagesPaid && e.Amount == wages ) );
		Assert.IsTrue( lost.Balance < balance, "the bank still pays the saved staff's monthly wages" );
		Assert.IsFalse( lostEvents.Any( e => e.Kind == ParkEventKind.Bankrupt ) );
	}

	private sealed class Sweep( List<bool> losses ) : IThingSweep
	{
		public void Run( ParkEconomy economy, long turn, bool lost ) => losses.Add( lost );
	}

	[TestMethod]
	public void TheFirstHostedTurnReceivesTheImportedLossFlag()
	{
		var park = Import( 4, 0, new() );
		var losses = new List<bool>();
		park.ThingSweep = new Sweep( losses );
		EconomyTestData.AdvanceToTurn( park, park.Turn + 1 );
		CollectionAssert.AreEqual( new[] { true }, losses );
	}

	[TestMethod]
	public void BothSavedLossGateStatesRoundTripInOwnSaves()
	{
		foreach ( var closed in new uint[] { 0, 1 } )
		{
			var park = Import( 4, closed, new() );
			var hash = WorldStateHash.Compute( new WorldStateSources { Economy = park } );
			var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) ), park.Settings, park.Catalog );
			Assert.IsTrue( restored.IsBankrupt );
			Assert.AreEqual( closed == 0, restored.IsParkOpen );
			Assert.AreEqual( hash, WorldStateHash.Compute( new WorldStateSources { Economy = restored } ) );
		}
	}

	[TestMethod]
	public void OtherWorldStatesKeepTheirExistingDeferredOpenBehavior()
	{
		foreach ( var state in new uint[] { 0, 1, 2, 3, 5, uint.MaxValue } )
		{
			var events = new List<ParkEvent>();
			var park = Import( state, 0, events );
			Assert.IsFalse( park.IsBankrupt );
			Assert.IsFalse( park.IsParkOpen );
			Assert.IsFalse( events.Any( e => e.Kind is ParkEventKind.Bankrupt or ParkEventKind.ParkOpened ) );
		}
	}
	[TestMethod]
	public void ThemeParkIncRulesDoNotAdoptTheTpwLossState()
	{
		var settings = OriginalSaveImportTests.Settings();
		var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1, EconomyRules.ThemeParkIncMoney );
		OriginalEconomyImport.Apply( park, OriginalSaveImportTests.Park( settings, worldState: 4 ) );
		Assert.IsFalse( park.IsBankrupt );
		Assert.IsFalse( park.IsParkOpen );
	}

}
