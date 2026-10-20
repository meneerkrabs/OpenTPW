using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] The park's setting commands (docs/COOP.md, "Settings") on a level with only the pieces the tick and the economy touch: the economy settings change
/// what they name, a refusal changes nothing and is logged, a malformed one never reaches the log, a visit refuses every one, and a log of settings replays tick for tick.
/// </summary>
[TestClass]
public class ParkSettingCommandTests
{
	/// <summary>A setting whose park call throws, as a hostile peer's or a damaged log's command could make one throw.</summary>
	private sealed record ThrowingSettingCommand : ParkSettingCommand
	{
		public override ParkCommandKind Kind => ParkCommandKind.SetEntranceFee;
		internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( 0 );
		internal override ParkCommandResult Execute( Level level ) => throw new InvalidOperationException( "a rule the command missed" );
	}

	[TestMethod]
	public void EachEconomySettingChangesWhatItNames()
	{
		var economy = EconomyTestData.Park();
		var level = CommandTestLevel.Create( economy );
		var mechanic = EconomyTestData.HireBest( economy, StaffType.Mechanic );
		var guard = EconomyTestData.HireBest( economy, StaffType.Guard );
		void Accept( ParkCommand command ) => Assert.IsTrue( level.Commands.Issue( command ).Accepted, command.ToString() );
		Accept( new SetEntranceFeeCommand( 35 ) );
		Assert.AreEqual( 35, economy.EntranceFee );
		Accept( new SetParkOpenCommand( true ) );
		Assert.IsTrue( economy.IsParkOpen );
		Accept( new SetParkOpenCommand( false ) );
		Assert.IsFalse( economy.IsParkOpen );
		Accept( new SetTrainingBudgetCommand( StaffType.Mechanic, 125 ) );
		Assert.AreEqual( 125L, economy.Staff.GetTrainingBudget( StaffType.Mechanic ) );
		Accept( new SetResearchWorkloadCommand( 40 ) );
		Assert.AreEqual( 40, economy.Research.Workload );
		Accept( new SetResearchEffortCommand( ResearchCategory.Shop, 20 ) );
		Assert.AreEqual( 20, economy.Research.Effort[(int)ResearchCategory.Shop] );
		// the loans: the dialog's list build stores the credit tests (a full map's land makes a net worth of 32,768: offer 2's 18,000 passes, ParkEconomyTests), a take
		// names the amount the confirmation showed, a repayment the balance
		economy.Litter.Cells = new ParkCellMap( 128, 128 );
		Assert.IsFalse( level.Commands.Issue( new TakeLoanCommand( 2, 18000 ) ).Accepted, "no offer is on the list before the dialog builds it" );
		Accept( new RefreshLoanOffersCommand() );
		Assert.IsFalse( level.Commands.Issue( new TakeLoanCommand( 2, 17999 ) ).Accepted, "another amount than the offer's" );
		var loan = level.Commands.Issue( new TakeLoanCommand( 2, 18000 ) );
		Assert.IsTrue( loan.Accepted );
		Assert.AreEqual( 2, loan.As<LoanAccount>()!.OfferIndex );
		Assert.IsFalse( level.Commands.Issue( new TakeLoanCommand( 2, 18000 ) ).Accepted, "an offer is taken once" );
		var balance = economy.Loans.Single().RemainingBalance;
		Assert.IsFalse( level.Commands.Issue( new RepayLoanCommand( 2, balance + 1 ) ).Accepted, "a stale balance" );
		Accept( new RepayLoanCommand( 2, balance ) );
		Assert.IsFalse( economy.Loans.Any() );
		Accept( new RenameStaffCommand( mechanic.Id, "Ann" ) );
		Assert.AreEqual( "Ann", mechanic.Name );
		Accept( new SetPatrolAreaCommand( guard.Id, 20, 4, 12, 9 ) );
		Assert.AreEqual( (12, 4, 20, 9), guard.PatrolArea, "the corners as the tool's clicks gave them" );
		Accept( new SetPatrolAreaCommand( guard.Id, 0, 0, 0, 0, Clear: true ) );
		Assert.AreEqual( StaffMember.WholeMap, guard.PatrolArea );
		Accept( new FireStaffCommand( guard.Id ) );
		Assert.IsFalse( economy.Staff.Members.Any( member => member.Id == guard.Id ) );
		Accept( new SetGameSpeedCommand( GameSpeed.Fast ) );
		Assert.AreEqual( GameSpeed.Fast, economy.Speed );
		Assert.IsFalse( level.Commands.Issue( new QueueCheatCommand( CheatKind.Money ) ).Accepted, "no cheat before the cheat mode" );
		Accept( new QueueCheatCommand( CheatKind.Mode ) );
		Accept( new QueueCheatCommand( CheatKind.Money ) );
		CollectionAssert.AreEqual( new[] { new CheatAction( CheatKind.Mode ), new CheatAction( CheatKind.Money ) }, economy.Cheats.Pending.ToArray(), "the cheats wait for the next turn" );
	}

	[TestMethod]
	public void AChallengeAnswerAnswersOnlyTheOfferItNames()
	{
		var economy = EconomyTestData.Park();
		economy.OpenPark();
		economy.AdvanceDays( 541 );
		var level = CommandTestLevel.Create( economy );
		var offer = economy.Objectives.Current!;
		Assert.IsFalse( offer.Accepted );
		Assert.IsFalse( level.Commands.Issue( new AnswerChallengeCommand( offer.DefinitionIndex, offer.OfferedDay + 1, true ) ).Accepted, "an earlier or later offer" );
		Assert.IsFalse( level.Commands.Issue( new AnswerChallengeCommand( offer.DefinitionIndex + 1, offer.OfferedDay, true ) ).Accepted );
		Assert.IsFalse( economy.Objectives.Current!.Accepted );
		Assert.IsTrue( level.Commands.Issue( new AnswerChallengeCommand( offer.DefinitionIndex, offer.OfferedDay, true ) ).Accepted );
		Assert.IsTrue( economy.Objectives.Current!.Accepted );
		Assert.IsFalse( level.Commands.Issue( new AnswerChallengeCommand( offer.DefinitionIndex, offer.OfferedDay, false ) ).Accepted, "an answered offer takes no second answer" );
	}

	[TestMethod]
	public void TheParksNameChangesThroughItsCommand()
	{
		var level = CommandTestLevel.Create( EconomyTestData.Park() );
		Assert.IsFalse( level.Commands.Issue( new RenameParkCommand( "New", "Name" ) ).Accepted, "a level without a name keeps none" );
		typeof( Level ).GetField( "<ParkName>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, new ParkNameLines( "Old", "Park" ) );
		Assert.IsTrue( level.Commands.Issue( new RenameParkCommand( "New", "Name" ) ).Accepted );
		Assert.AreEqual( new ParkNameLines( "New", "Name" ), level.ParkName );
		Assert.IsFalse( level.Commands.Issue( new RenameParkCommand( "Line\u0007", "Name" ) ).Accepted, "a line a save could not hold" );
		Assert.AreEqual( new ParkNameLines( "New", "Name" ), level.ParkName );
	}

	[TestMethod]
	public void ARefusedSettingChangesNothingAndIsLogged()
	{
		var economy = EconomyTestData.Park();
		var level = CommandTestLevel.Create( economy );
		var mechanic = EconomyTestData.HireBest( economy, StaffType.Mechanic );
		var hash = CommandTestLevel.Hash( level );
		var refused = new ParkCommand[]
		{
			new SetAttractionOpenCommand( 999, true ),
			new SetRideSettingsCommand( 999, 1, 1, 1 ),
			new SetPriceCommand( 999, 10 ),
			new SetGamePrizeCommand( 999, 10, 10, 10 ),
			new SetShopSettingsCommand( 999, 50, 50 ),
			new TakeLoanCommand( 9, 100 ),
			new RepayLoanCommand( 0, 100 ),
			new BuyUpgradeCommand( 999 ),
			new CancelUpgradeCommand( 999 ),
			new RequestMaintenanceCommand( 999, true ),
			new AnswerChallengeCommand( 0, 0, true ),
			new RenameStaffCommand( 999, "Ann" ),
			new RenameStaffCommand( mechanic.Id, "A\u0007B" ),
			new FireStaffCommand( 999 ),
			new SetPatrolAreaCommand( 999, 1, 1, 2, 2 ),
			new SetPatrolAreaCommand( mechanic.Id, 1, 1, 2, ParkLitter.MapSize ),
			new RenameParkCommand( "A", "B" ),
			new SetPrototypeRunningCommand( true ),
			new QueueCheatCommand( CheatKind.Research )
		};
		foreach ( var command in refused )
		{
			var result = level.Commands.Issue( command );
			Assert.IsFalse( result.Accepted, command.ToString() );
			Assert.IsFalse( string.IsNullOrEmpty( result.Reason ), command.ToString() );
			Assert.AreEqual( hash, CommandTestLevel.Hash( level ), command.ToString() );
		}
		Assert.AreEqual( refused.Length, level.Commands.Log.Count, "every refusal is logged: a replay refuses it the same way" );
	}

	[TestMethod]
	public void AParkWithoutStaffAndAParkCallThatThrowsRejectTheCommandInsteadOfThrowing()
	{
		var economy = new ParkEconomy( EconomyTestData.Settings(), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7, EconomyRules.ThemeParkIncMoney );
		var level = CommandTestLevel.Create( economy );
		var hash = CommandTestLevel.Hash( level );
		Assert.IsFalse( level.Commands.Issue( new SetTrainingBudgetCommand( StaffType.Mechanic, 100 ) ).Accepted, "Theme Park Inc's money slice has no staff" );
		var thrown = level.Commands.Issue( new ThrowingSettingCommand() );
		Assert.IsFalse( thrown.Accepted );
		StringAssert.Contains( thrown.Reason, "a rule the command missed" );
		Assert.AreEqual( 2, level.Commands.Log.Count );
		Assert.AreEqual( hash, CommandTestLevel.Hash( level ) );
	}

	[TestMethod]
	public void AMalformedSettingNeverReachesTheParkOrTheLog()
	{
		var economy = EconomyTestData.Park();
		var level = CommandTestLevel.Create( economy );
		var hash = CommandTestLevel.Hash( level );
		foreach ( var command in new ParkCommand[]
		{
			new SetEntranceFeeCommand( -1 ),
			new SetPriceCommand( 1, ParkHud.MaximumPrice + 1 ),
			new SetTrainingBudgetCommand( StaffType.Guard, ParkHud.TrainingBudgetMaximum + 1 ),
			new MoveResearchCommand( ResearchCategory.Ride, 5 ),
			// text that would not decode to itself: a lone surrogate, and more bytes than a command string holds
			new RenameStaffCommand( 1, "A\ud800" ),
			new RenameStaffCommand( 1, new string( 'é', ParkCommandCodec.MaximumString ) ),
			new RenameParkCommand( "ok", "\udfff" ),
			new SetGameSpeedCommand( (GameSpeed)3 )
		} )
		{
			Assert.IsFalse( command.IsWellFormed, command.ToString() );
			Assert.IsFalse( level.Commands.Issue( command ).Accepted, command.ToString() );
		}
		Assert.AreEqual( 0, level.Commands.Log.Count, "a malformed command has no byte form to log" );
		Assert.AreEqual( hash, CommandTestLevel.Hash( level ) );
		Assert.IsTrue( ParkSettingCommand.IsText( new string( 'a', ParkCommandCodec.MaximumString ) ), "the bound itself fits" );
	}

	[TestMethod]
	public void AVisitRefusesEverySetting()
	{
		var economy = EconomyTestData.Park();
		var level = CommandTestLevel.Create( economy, readOnly: true );
		var mechanic = EconomyTestData.HireBest( economy, StaffType.Mechanic );
		var hash = CommandTestLevel.Hash( level );
		foreach ( var (command, _) in ParkCommandCodecTests.Schema.Where( row => row.Command is ParkSettingCommand ) )
			Assert.IsFalse( level.Commands.Issue( command ).Accepted, command.ToString() );
		Assert.IsFalse( level.Commands.Issue( new RenameStaffCommand( mechanic.Id, "Ann" ) ).Accepted );
		Assert.AreEqual( hash, CommandTestLevel.Hash( level ) );
		Assert.AreNotEqual( "Ann", mechanic.Name );
	}

	[TestMethod]
	public void ThePatrolToolSetsItsAreaThroughTheParksCommands()
	{
		var park = EconomyTestData.Park();
		var guard = EconomyTestData.HireBest( park, StaffType.Guard );
		var sent = new List<ParkCommand>();
		var tool = new PatrolAreaTool( () => park, command => { sent.Add( command ); return ParkCommandResult.Ok(); } );
		Assert.IsTrue( tool.Begin( guard ) );
		Assert.IsTrue( tool.Click( 20, 4 ) );
		Assert.IsTrue( tool.Click( 12, 9 ) );
		CollectionAssert.AreEqual( new ParkCommand[] { new SetPatrolAreaCommand( guard.Id, 20, 4, 12, 9 ) }, sent );
		Assert.IsFalse( guard.HasPatrolArea, "the command, not the tool, sets the area" );
		Assert.IsFalse( tool.IsActive );
	}

	[TestMethod]
	public void AStatusBoundToItsLevelSetsTheSpeedThroughTheCommands()
	{
		var economy = EconomyTestData.Park();
		var level = CommandTestLevel.Create( economy );
		var status = EconomyParkStatus.ForLevel( level );
		status.Speed = GameSpeed.Paused;
		Assert.AreEqual( GameSpeed.Paused, economy.Speed );
		Assert.AreEqual( new ParkCommandLogEntry( 0, new SetGameSpeedCommand( GameSpeed.Paused ) ), level.Commands.Log.Single() );
		var unbound = new EconomyParkStatus( () => economy );
		Assert.ThrowsException<InvalidOperationException>( () => unbound.Speed = GameSpeed.Fastest, "a status bound to no level cannot set the speed" );
		Assert.AreEqual( GameSpeed.Paused, economy.Speed );
	}

	[TestMethod]
	public void ASettingsLogReplaysTickForTickOnATwinPark()
	{
		const long Ticks = 1200;
		var script = new Dictionary<long, ParkCommand[]>
		{
			[0] = new ParkCommand[] { new SetEntranceFeeCommand( 12 ), new SetParkOpenCommand( true ) },
			[90] = new ParkCommand[] { new SetResearchWorkloadCommand( 70 ), new SetTrainingBudgetCommand( StaffType.Handyman, 300 ) },
			// the repayment of an offer never taken is refused, and replays refused
			[400] = new ParkCommand[] { new RefreshLoanOffersCommand(), new TakeLoanCommand( 2, 18000 ), new RepayLoanCommand( 1, 100 ) },
			[600] = new ParkCommand[] { new SetGameSpeedCommand( GameSpeed.Fast ), new QueueCheatCommand( CheatKind.Mode ) },
			[900] = new ParkCommand[] { new SetEntranceFeeCommand( 40 ), new SetGameSpeedCommand( GameSpeed.Normal ), new QueueCheatCommand( CheatKind.Money ) }
		};
		// a full map's land gives the parks the net worth a loan's credit test needs (ParkEconomyTests)
		static ParkEconomy Park() { var park = EconomyTestData.Park(); park.Litter.Cells = new ParkCellMap( 128, 128 ); return park; }
		var recorded = CommandTestLevel.Create( Park() );
		var twin = CommandTestLevel.Create( Park() );
		Assert.AreEqual( CommandTestLevel.Hash( recorded ), CommandTestLevel.Hash( twin ) );
		var hashes = new List<ulong>();
		while ( recorded.Commands.NextTick < Ticks )
		{
			if ( script.TryGetValue( recorded.Commands.NextTick, out var commands ) )
				foreach ( var command in commands )
					recorded.Commands.Issue( command );
			recorded.AdvanceSimulation( FixedStepClock.TickDuration );
			hashes.Add( CommandTestLevel.Hash( recorded ) );
		}
		Assert.AreEqual( script.Values.Sum( commands => commands.Length ), recorded.Commands.Log.Count );
		Assert.IsTrue( recorded.Park!.Economy.Loans.Any(), "the loan was taken" );
		// the log survives its bytes
		var log = ParkCommandLogFile.Deserialize( new ParkCommandLogFile( "jungle", 0, 0, 0, recorded.Commands.Log, Array.Empty<ParkCommandHashCheckpoint>() ).Serialize() ).Entries;
		var replayed = new List<ulong>();
		ParkCommandReplay.Run( twin.Commands, log, () => twin.Commands.NextTick, () => twin.AdvanceSimulation( FixedStepClock.TickDuration ), Ticks, _ => replayed.Add( CommandTestLevel.Hash( twin ) ) );
		CollectionAssert.AreEqual( hashes, replayed, "the replay matches the recorded run after every tick" );
		CollectionAssert.AreEqual( recorded.Commands.Log.ToArray(), twin.Commands.Log.ToArray() );
		Assert.AreEqual( 40, twin.Park!.Economy.EntranceFee );
		Assert.IsTrue( twin.Park.Economy.Cheats.Mode, "the cheat mode came at its turn" );
		// and the commands mattered: the same park without them parts from the recorded one
		var plain = CommandTestLevel.Create( Park() );
		for ( var tick = 0; tick < Ticks; tick++ )
			plain.AdvanceSimulation( FixedStepClock.TickDuration );
		Assert.AreNotEqual( hashes[^1], CommandTestLevel.Hash( plain ) );
	}
}
