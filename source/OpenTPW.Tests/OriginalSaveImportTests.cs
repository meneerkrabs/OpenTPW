using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Import of a decoded original save into OpenTPW runtime state (economy, staff, advisor controller).</summary>
[TestClass]
public class OriginalSaveImportTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static readonly string ExtraLoans = string.Concat( Enumerable.Range( 3, 5 ).Select( index =>
		$"LoanInfo[{index}].LoanAmount {10000 * index}\nLoanInfo[{index}].APRInPercent 0\nLoanInfo[{index}].RepaymentPeriodInMonths 36\nLoanInfo[{index}].Lendername {index}\n" ) );

	private static BalanceSettings Settings() => EconomyTestData.Settings( apr: 0, extra: ExtraLoans );

	/// <summary>Bank loans equal to the settings' offers; <paramref name="bought"/> marks one offer bought with months repaid.</summary>
	private static byte[] Bank( BalanceSettings settings, int balance = 87_987, (int Index, int Repaid)? bought = null ) => SaveTestPayloads.Bank( balance: balance, loans: index =>
	{
		var offer = settings.Loans[index];
		var isBought = bought?.Index == index;
		return new[] { 0, (int)offer.Amount, offer.AprPercent, offer.Months, (int)LoanMath.MonthlyRepayment( offer.Amount, offer.AprPercent, offer.Months ), isBought ? 1 : 0, isBought ? bought!.Value.Repaid : 0, offer.LenderNameIndex };
	} );

	/// <summary>The settings' level challenges in save slot layout.</summary>
	private static byte[] Challenges( BalanceSettings settings )
	{
		var body = SaveTestPayloads.ChallengeSystem( Array.Empty<(int, int, int, int)>() );
		for ( var index = 0; index < settings.ChallengesInThisLevel.Count; index++ )
		{
			var definition = settings.Challenges[settings.ChallengesInThisLevel[index]];
			var offset = 8 + index * SaveChallengeSystem.SlotBytes;
			Array.Clear( body, offset, SaveChallengeSystem.SlotBytes );
			var words = new[] { definition.Type, definition.TargetTime, definition.TargetValue, definition.TargetObject | definition.TargetObject2 << 16,
				definition.TargetStaffType, checked((int)definition.Prize), definition.FollowupType };
			for ( var word = 0; word < words.Length; word++ )
				BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( offset + word * 4 ), words[word] );
			body[offset + 28] = (byte)(definition.CheckAtEndOnly ? 1 : 0);
			body[offset + 42] = (byte)(definition.Independent ? 1 : 0);
		}
		return body;
	}

	private static byte[] RideSystemRecord( int infoId, int x, int y, int kind, int index, int width = 1, int height = 1, int rotation = 0 )
	{
		var record = new byte[60];
		record[0] = 1;
		var values = new[] { infoId, x, y, width, height, kind, index };
		for ( var field = 0; field < values.Length; field++ )
			BinaryPrimitives.WriteInt32LittleEndian( record.AsSpan( 1 + field * 4 ), values[field] );
		BinaryPrimitives.WriteUInt16LittleEndian( record.AsSpan( 1 + 38 ), (ushort)rotation );
		return record;
	}

	/// <summary>
	/// A 3×3 park: Belly Bounce at (1, 1), the gates as a fixed item, a researcher, bank and challenges from <paramref name="settings"/>.
	/// <paramref name="unverifiedFootprint"/> records Belly Bounce as 2×1 turned 90°, a footprint without a verified rule.
	/// </summary>
	private static OriginalParkImport Park( BalanceSettings settings, byte[]? bank = null, uint parkClosed = 0, IEnumerable<(uint, uint, byte[])>? extraThings = null,
		byte[]? researcher = null, byte[]? bellyBounce = null, byte[]? staffHq = null, bool unverifiedFootprint = false )
	{
		var map = OriginalParkImportTests.CreateMap( 3, 3, ( _, _ ) => 0 );
		var things = SaveTestPayloads.ParkThings();
		if ( researcher != null )
			things[0] = (30, (uint)SaveThingModel.Researcher, researcher);
		if ( bellyBounce != null )
			things[1] = (12, (uint)SaveThingModel.Attraction, bellyBounce);
		things[2] = (10, (uint)SaveThingModel.ChallengeSystem, Challenges( settings ));
		things[4] = (8, (uint)SaveThingModel.Bank, bank ?? Bank( settings ));
		if ( staffHq != null )
			things[^1] = (1, (uint)SaveThingModel.StaffHq, staffHq);
		things.Insert( 2, (11, (uint)SaveThingModel.Attraction, SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields { PositionX = 128, PositionY = 128, Angle = 0, InfoId = 1601, Price = 0, Level = 0, Repair = 100, LifeGauge = 100 } )) );
		things.InsertRange( 0, extraThings ?? Array.Empty<(uint, uint, byte[])>() );
		var grid = SaveTestPayloads.Grid( map, ( x, y ) => OriginalParkImportTests.CreateCellRecord( 0, occupied: (x, y) == (1, 1) ) );
		var rideSystem = RideSystemRecord( 1601, 0, 0, SaveObject.FixedItemKind, 1 ).Concat( RideSystemRecord( 1100, 1, 1, SaveObject.PlacedObjectKind, 2, width: unverifiedFootprint ? 2 : 1, rotation: unverifiedFootprint ? 90 : 0 ) ).ToArray();
		var payload = SaveTestPayloads.Payload( SaveTestPayloads.World( SaveTestPayloads.ParkVariables( parkClosed: parkClosed ), grid, SaveTestPayloads.Things( things ) ), rideSystem );
		return OriginalParkImport.Import( payload, map );
	}

	[TestMethod]
	public void StaffNativeSubCellPositionsSurviveImportAndOwnedSave()
	{
		var settings = Settings();
		var researcher = SaveTestPayloads.Staff( SaveThingModel.Researcher );
		BinaryPrimitives.WriteUInt16LittleEndian( researcher.AsSpan( 0, 2 ), 321 );
		BinaryPrimitives.WriteUInt16LittleEndian( researcher.AsSpan( 2, 2 ), 777 );
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		OriginalEconomyImport.Apply( economy, Park( settings, researcher: researcher ) );
		var member = economy.Staff.Members.Single();
		Assert.AreEqual( (321 / 256f, 777 / 256f), (member.CellX!.Value, member.CellY!.Value) );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( economy ) ) ), settings, economy.Catalog );
		var loaded = restored.Staff.Members.Single();
		Assert.AreEqual( (member.CellX, member.CellY), (loaded.CellX, loaded.CellY) );
	}

	[TestMethod]
	public void ImportsTurnBankObjectsAndStaff()
	{
		var settings = Settings();
		var save = Park( settings );
		Assert.AreEqual( 1, save.PlacedObjects.Count );
		Assert.AreEqual( 1, save.FixedItems.Count );
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var firstOffer = economy.Objectives.NextOfferDay;
		var poolUpdate = economy.Staff.NextPoolUpdateTick;
		var import = OriginalEconomyImport.Apply( economy, save );

		Assert.AreEqual( 755, economy.Turn );
		Assert.AreEqual( new ParkDate( 1, 2, 2, 18 ), economy.Date );
		Assert.AreEqual( 1, economy.Ledger.CurrentMonthIndex, "the ledger's open month is February" );
		Assert.AreEqual( firstOffer + ParkCalendar.DayIndex( economy.Tick ), economy.Objectives.NextOfferDay );
		Assert.AreEqual( poolUpdate + economy.Tick, economy.Staff.NextPoolUpdateTick );
		Assert.IsTrue( economy.Staff.Candidates.All( candidate => candidate.ExpiresTick > economy.Tick ) );

		Assert.AreEqual( (87987L, 25, -12013L), (economy.Balance, economy.EntranceFee, economy.Ledger.ProfitThisYear) );
		Assert.AreEqual( 87987, economy.Ledger.CurrentOpeningBalance );
		Assert.AreEqual( 0, economy.Loans.Count );

		Assert.AreEqual( (1, 1), (import.ImportedObjects, import.ImportedFixedItems) );
		var bouncy = economy.Objects.Single( item => item.InfoId == 1100 );
		Assert.AreEqual( (30, 12, 0, 80.0, 90.0), (bouncy.Price, bouncy.CostOfGoods, bouncy.Level, bouncy.Repair, bouncy.LifeGauge) );
		Assert.AreEqual( economy.Tick, bouncy.BuiltTick );

		var researcher = economy.Staff.Members.Single();
		Assert.AreEqual( (StaffType.Researcher, 2, 0, 97, StaffState.Patrolling), (researcher.Type, researcher.Grade, researcher.TrainingPoints, researcher.Happiness, researcher.State) );
		Assert.AreEqual( ParkCalendar.TickOfTurn( 648 ), researcher.HiredTick, "hired 2,430,000 park seconds after FunnyTimeStart = turn 648" );
		Assert.AreEqual( 1, import.ImportedStaff );
		Assert.IsFalse( import.ParkClosed );
		foreach ( var id in new[] { "SAVE-001", "SAVE-002", "SAVE-003", "SAVE-004", "SAVE-005", "SAVE-006", "SAVE-007", "SAVE-008", "SAVE-010" } )
			Assert.IsTrue( import.Evidence.Any( line => line.Contains( $"[APPROX:{id}]" ) ), $"{id} is reported" );
		Assert.IsTrue( import.Evidence.Any( line => line.Contains( "0x3F416BBA" ) ), "the dropped generator state is named" );
		Assert.IsFalse( import.Evidence.Any( line => line.Contains( "Adjusted on import" ) ), "in-range staff values are imported unchanged" );
		AssertReportsUnimportedValues( import );

		// The imported park keeps running and its save round-trips.
		economy.AdvanceDays( 40 );
		Assert.AreEqual( 3, economy.Date.Month );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( economy ) ) ), settings, economy.Catalog );
		Assert.AreEqual( (economy.Tick, economy.Balance, economy.Staff.Members.Count), (restored.Tick, restored.Balance, restored.Staff.Members.Count) );
	}

	/// <summary>Every World, thing and advisor-controller value the importer reads but does not import is named under SAVE-010 or SAVE-008.</summary>
	private static void AssertReportsUnimportedValues( OriginalEconomyImport import )
	{
		string Line( string start ) => import.Evidence.Single( line => line.StartsWith( start, StringComparison.Ordinal ) );
		var blocks = Line( "[APPROX:SAVE-010] Not imported:" );
		foreach ( var model in new[] { "Weather", "ParkAnalyser", "TagSystem", "MechanicHq", "UIMsgReceiver" } )
			StringAssert.Contains( blocks, $"{model} things (" );
		var world = Line( "[APPROX:SAVE-010] World values not imported:" );
		foreach ( var value in new[] { "world state ", "arrival vehicle sizes ", "current arrival vehicle thing ", "arrival bytes ", "calendar session start " } )
			StringAssert.Contains( world, value );
		var advisor = Line( "[APPROX:SAVE-010] Advisor controller values not imported:" );
		foreach ( var value in new[] { "time signature ", "sound ", "raw words 0x", "current record (", "mRequestedLastTutorial 0x", "mMessagePlaying " } )
			StringAssert.Contains( advisor, value );
		StringAssert.Contains( Line( "[APPROX:SAVE-008] Not imported:" ), "last balance (mLastBalance" );
		StringAssert.Contains( Line( "[APPROX:SAVE-008] Loan offers' Available flags not imported:" ), "OpenTPW offers " );
		var challenges = Line( "[APPROX:SAVE-005]" );
		foreach ( var value in new[] { "current challenge ", "next event time ", "switch bytes ", "Value ", "ActualThing " } )
			StringAssert.Contains( challenges, value );
		var attractions = import.Evidence.Where( line => line.StartsWith( "[APPROX:SAVE-006] Attraction thing ", StringComparison.Ordinal ) ).ToArray();
		Assert.AreNotEqual( 0, attractions.Length );
		foreach ( var line in attractions )
		{
			foreach ( var value in new[] { "state ", "chance of winning ", "third gauge ", "total costs ", "total takings ", "name \"", "operating capacity ", "duration ", "speed ",
				"quality of goods ", "queue size ", "customers ", "walk-aways ", "time stamp ", "mesh instance " } )
				StringAssert.Contains( line, value );
		}
		foreach ( var line in import.Evidence.Where( line => line.StartsWith( "[APPROX:SAVE-003] ", StringComparison.Ordinal ) && line.Contains( " values not imported:" ) ) )
		{
			foreach ( var value in new[] { "idle tick ", "role bytes ", "role next ID " } )
				StringAssert.Contains( line, value );
		}
		var hq = Line( "[APPROX:SAVE-009]" );
		foreach ( var value in new[] { "training budgets ", "force strike ", "picked up ", "strike levels ", "trained " } )
			StringAssert.Contains( hq, value );
		StringAssert.Contains( Line( "[APPROX:SAVE-010] Saved advisor pending advice" ), "imported only when the automatic advisor attaches" );
	}

	[TestMethod]
	public void ReportsEveryStaffAndAttractionValueItAdjusts()
	{
		var settings = Settings();
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var save = Park( settings, researcher: SaveTestPayloads.Staff( SaveThingModel.Researcher, happiness: 150, state: 9, percentage: 100, hired: 125_935_884_000_000_000 + 5 * OriginalEconomyImport.TimeStampUnitsPerSecond ),
			bellyBounce: SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields { PositionX = 1 * 256 + 128, PositionY = 1 * 256 + 128, Angle = 0, Price = 30, Level = 0, Repair = 80, LifeGauge = 90, CostOfGoods = -4 } ) );
		var import = OriginalEconomyImport.Apply( economy, save );

		var researcher = economy.Staff.Members.Single();
		Assert.AreEqual( (100, 99, StaffState.Idle), (researcher.Happiness, researcher.TrainingPoints, researcher.State), "clamped to the traced happiness range and OpenTPW's 0-99 points" );
		var adjusted = import.Evidence.Where( line => line.StartsWith( "[APPROX:SAVE-003] Adjusted on import:", StringComparison.Ordinal ) ).ToArray();
		Assert.AreEqual( 4, adjusted.Length );
		Assert.IsTrue( adjusted.Any( line => line.Contains( "happiness 150 imported as 100" ) ) );
		Assert.IsTrue( adjusted.Any( line => line.Contains( "grade percentage 100 imported as 99" ) ) );
		Assert.IsTrue( adjusted.Any( line => line.Contains( "state 9 has no STAFFSTATES entry" ) ) );
		Assert.IsTrue( adjusted.Any( line => line.Contains( "lies 5 park seconds into turn 648" ) ) );
		Assert.AreEqual( ParkCalendar.TickOfTurn( 648 ), researcher.HiredTick, "the hire time is rounded down to its turn" );

		var bouncy = economy.Objects.Single( item => item.InfoId == 1100 );
		Assert.AreEqual( 100.0, bouncy.Repair, "an attraction with a negative cost of goods keeps OpenTPW's defaults (saved repair 80)" );
		Assert.IsTrue( import.Evidence.Any( line => line.Contains( "kept at their defaults" ) && line.Contains( "cost of goods -4" ) ) );
	}

	[TestMethod]
	public void ImportsTheEconomyValuesOfObjectsWithAnUnverifiedFootprint()
	{
		var settings = Settings();
		var save = Park( settings, unverifiedFootprint: true );
		Assert.AreEqual( (0, 1), (save.PlacedObjects.Count, save.UnresolvedObjects.Count) );
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var import = OriginalEconomyImport.Apply( economy, save );
		Assert.AreEqual( (1, 1), (import.ImportedObjects, import.ImportedFixedItems) );
		var bouncy = economy.Objects.Single( item => item.InfoId == 1100 );
		Assert.AreEqual( (30, 12, 0, 80.0, 90.0), (bouncy.Price, bouncy.CostOfGoods, bouncy.Level, bouncy.Repair, bouncy.LifeGauge) );
		Assert.IsTrue( bouncy.Imported );
		StringAssert.Contains( import.Evidence.Single( line => line.StartsWith( "Registered ", StringComparison.Ordinal ) ), "1 with an unverified footprint: 1100 at (1, 1) 2x1 turned 90" );
	}

	[TestMethod]
	public void ReportsStaffHqValuesAndLaterImportSteps()
	{
		var settings = Settings();
		var hq = SaveTestPayloads.Fixed( SaveThingModel.StaffHq );
		BinaryPrimitives.WriteUInt32LittleEndian( hq.AsSpan( SaveThingBase.Bytes ), 1 );
		BinaryPrimitives.WriteInt32LittleEndian( hq.AsSpan( SaveThingBase.Bytes + 4 + 2 + SaveStaffHq.Roles * 12 + 4 ), 250 );
		hq[^1] = 1;
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var import = OriginalEconomyImport.Apply( economy, Park( settings, staffHq: hq ) );
		var line = import.Evidence.Single( line => line.StartsWith( "[APPROX:SAVE-009]", StringComparison.Ordinal ) );
		StringAssert.Contains( line, "training budgets 0, 250, 0, 0, 0 (different from OpenTPW's starting budgets of 0, which are kept" );
		StringAssert.Contains( line, "force strike 1, picked up 0" );
		StringAssert.Contains( line, "trained 1." );
		Assert.IsTrue( Enum.GetValues<StaffType>().All( type => economy.Staff.GetTrainingBudget( type ) == 0 ) );

		var zero = OriginalEconomyImport.Apply( new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 ), Park( settings ) );
		StringAssert.Contains( zero.Evidence.Single( line => line.StartsWith( "[APPROX:SAVE-009]", StringComparison.Ordinal ) ), "all 0, which equals OpenTPW's starting budgets" );
		var available = zero.Evidence.Single( line => line.StartsWith( "[APPROX:SAVE-008] Loan offers' Available flags", StringComparison.Ordinal ) );
		StringAssert.Contains( available, "the save marks no offer available (0:0, 1:0" );
		StringAssert.Contains( available, "OpenTPW offers 0, 1, 2, 3, 4, 5, 6, 7." );

		var count = zero.Evidence.Count;
		zero.Report( "advisor outcome" );
		Assert.AreEqual( (count + 1, "advisor outcome"), (zero.Evidence.Count, zero.Evidence[^1]) );
	}

	[TestMethod]
	public void ImportsBoughtLoansAndTheClosedFlag()
	{
		var settings = Settings();
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var import = OriginalEconomyImport.Apply( economy, Park( settings, Bank( settings, balance: -500, bought: (1, 10) ), parkClosed: 1 ) );
		var offer = settings.Loans[1];
		var monthly = offer.Amount / offer.Months;
		Assert.AreEqual( new LoanAccount( 1, offer.LenderNameIndex, offer.Amount, 0, offer.Months, monthly, 10 ), economy.Loans.Single() );
		Assert.AreEqual( monthly * (offer.Months - 10), economy.Loans.Single().RemainingBalance, "early repayment is M x the months left" );
		Assert.IsFalse( economy.AvailableLoans.Any( item => item.Index == 1 ), "a bought offer is taken" );
		Assert.AreEqual( -500, economy.Balance );
		Assert.IsTrue( import.ParkClosed );
		Assert.IsTrue( import.Evidence.Any( line => line.Contains( "[APPROX:SAVE-008]" ) && line.Contains( "in the red" ) ) );

		var repaid = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => OriginalEconomyImport.Apply( repaid, Park( settings, Bank( settings, bought: (2, settings.Loans[2].Months) ) ) ) ).Message, "fully repaid" );
	}

	[TestMethod]
	public void VerifiesLoanOffersAndChallengesAgainstTheSettings()
	{
		var settings = Settings();
		var save = Park( settings );
		var evidence = new List<string>();
		OriginalEconomyImport.Verify( settings, save.Economy, evidence );
		Assert.IsTrue( evidence.Any( line => line.Contains( "8 monthly repayments equal" ) ) );
		foreach ( var (word, value) in new[] { (1, 99_999), (2, 5), (3, 37), (7, 9) } )
		{
			var changed = Park( settings, Mutate( Bank( settings ), SaveThingBase.Bytes + SaveBank.AccountBytes + word * 4, value ) );
			var error = Assert.ThrowsException<InvalidDataException>( () => OriginalEconomyImport.Verify( settings, changed.Economy, new List<string>() ) );
			StringAssert.Contains( error.Message, "Save loan offer 0" );
		}
		var fewer = EconomyTestData.Settings( apr: 0 );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => OriginalEconomyImport.Verify( fewer, save.Economy, new List<string>() ) ).Message, "8 loan offers" );
		var positive = EconomyTestData.Settings( apr: 10, extra: ExtraLoans.Replace( "APRInPercent 0", "APRInPercent 10" ) );
		var positiveSave = Park( positive );
		var reported = new List<string>();
		OriginalEconomyImport.Verify( positive, positiveSave.Economy, reported );
		Assert.IsTrue( reported.Any( line => line.Contains( "8 monthly repayments equal trunc(P x pow(1 + APR/100, N/24) / N)" ) ), "the stored instalment is accepted at a positive APR" );
		Assert.IsFalse( reported.Any( line => line.Contains( "ECON-006" ) ) );
		// a stored M that differs from this host's pow (the last bit of PC and Mac pow is unproven) imports and is reported
		var offByOne = Park( positive, SaveTestPayloads.Bank( loans: index =>
		{
			var offer = positive.Loans[index];
			return new[] { 0, (int)offer.Amount, offer.AprPercent, offer.Months, (int)LoanMath.MonthlyRepayment( offer.Amount, offer.AprPercent, offer.Months ) + (index == 2 ? 1 : 0), index == 2 ? 1 : 0, index == 2 ? 3 : 0, offer.LenderNameIndex };
		} ) );
		var softEvidence = new List<string>();
		OriginalEconomyImport.Verify( positive, offByOne.Economy, softEvidence );
		Assert.IsTrue( softEvidence.Any( line => line.StartsWith( "Save loan offer 2 monthly repayment", StringComparison.Ordinal ) && line.Contains( "stored value is kept" ) ) );
		var imported = new ParkEconomy( positive, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		OriginalEconomyImport.Apply( imported, offByOne );
		Assert.AreEqual( LoanMath.MonthlyRepayment( positive.Loans[2].Amount, 10, positive.Loans[2].Months ) + 1, imported.Loans.Single().MonthlyRepayment, "the stored M is kept" );
		var otherChallenges = EconomyTestData.Settings( apr: 0, extra: ExtraLoans + "ChallengesInThisLevel[1].ChallengeType 8\n" );
		Assert.ThrowsException<InvalidDataException>( () => OriginalEconomyImport.Verify( otherChallenges, save.Economy, new List<string>() ) );
	}

	private static byte[] Mutate( byte[] body, int offset, int value )
	{
		BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( offset ), value );
		return body;
	}

	private const string AdvisorSettings = """
		GeneralAdvisor.MinTimeAnyMessage	7
		GeneralAdvisor.MinTimeSameMessage	99
		GeneralAdvisor.MinScoreForConsideration	10
		MessageGroups[0].MinTimeSameMessage		10
		MessageGroups[0].SayOnlyOnce			0
		MessageGroups[0].DiscardAfterSlaps		0
		MessageGroups[1].MinTimeSameMessage		0
		MessageGroups[1].SayOnlyOnce			1
		MessageGroups[1].DiscardAfterSlaps		2
		Welcome.Score 9000
		Bankrupted.Score	8000
		ParkNowOpen.Score	50
		ParkNowClosed.Score	51
		PrebuiltPark.Score	700
		""";

	[TestMethod]
	public void ImportsTheAdvisorControllerForBoundMessages()
	{
		var pending = new[]
		{
			new SaveTestPayloads.AdvisorRecordFields( 0, 0, 188, 1000, 0, -1, 0, 0 ),
			new SaveTestPayloads.AdvisorRecordFields( 0, 0, 200, 1000, 1, -1, 0, 0 ),
			new SaveTestPayloads.AdvisorRecordFields( 0, 0, 128, 20, 1, 1, 1, 0 ),
		};
		var history = new Dictionary<int, (uint, int, byte, uint)> { [0] = (1, 0, 1, 0), [129] = (400, 1, 1, 2), [77] = (9, 0, 1, 0) };
		var things = SaveTestPayloads.ParkThings();
		things[9] = (3, (uint)SaveThingModel.AdvisorController, SaveTestPayloads.AdvisorController( pending, history ));
		var map = OriginalParkImportTests.CreateMap( 3, 3, ( _, _ ) => 0 );
		var payload = SaveTestPayloads.Payload( SaveTestPayloads.World( SaveTestPayloads.ParkVariables(), SaveTestPayloads.Grid( map ), SaveTestPayloads.Things( things ) ) );
		var saved = SaveAdvisorController.Read( payload, SaveWorld.Parse( payload, SavePayloadLayout.Parse( payload ), 3, 3 ).Single( SaveThingModel.AdvisorController ) );

		var controller = new AdvisorController( AdvisorBalance.Parse( Encoding.ASCII.GetBytes( AdvisorSettings ), "test.sam" ) );
		controller.HandleGameEvent( AdvisorGameEvent.Bankrupted, 0, 0, true );
		var report = controller.ImportOriginal( saved );
		Assert.AreEqual( 1, controller.Queue.Count, "only the valid record of a bound message is pending; the session's own advice is replaced" );
		Assert.AreEqual( new AdvisorPendingAdvice( 128, 20, 1, true ), controller.Queue.GetSlot( 2 ), "the record keeps its slot, score, response number and override" );
		Assert.AreEqual( new AdvisorMessageHistory( 1, 0, true, 0 ), controller.Queue.GetHistory( 0 ) );
		Assert.AreEqual( new AdvisorMessageHistory( 400, 1, true, 2 ), controller.Queue.GetHistory( 129 ) );
		Assert.AreEqual( AdvisorMessageHistory.Empty, controller.Queue.GetHistory( 106 ) );
		StringAssert.Contains( report, "dropped 1 pending advice of unbound messages (200)" );
		StringAssert.Contains( report, "1 unbound history entries" );
		StringAssert.Contains( report, "[APPROX:ADVISOR-022]" );
		StringAssert.Contains( report, "message 128 thing 0 secondary 0 score overridden 0" );

		// The level start that follows (events 10 then 0) keeps the saved ticks and adds Welcome next to the saved advice.
		controller.HandleGameEvent( AdvisorGameEvent.ResetForEasyMode, 755, 0, true );
		Assert.AreEqual( 1u, controller.Queue.GetHistory( 0 ).SavedGameTick );
		Assert.IsFalse( controller.Queue.GetHistory( 0 ).HasBeenPlayed );
		controller.HandleGameEvent( AdvisorGameEvent.LevelStarted, 755, 0, true );
		Assert.AreEqual( 2, controller.Queue.Count );
		var selection = controller.Update( () => 0, () => 755, _ => 100 );
		Assert.AreEqual( 0, selection!.Value.Advice.MessageId, "Welcome outscores the saved ParkNowOpen advice" );
	}

	[TestMethod]
	public void OriginalJungleEasymodeImportKeepsTheInstantActionStartRunning()
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
			var economy = runtime.Economy;
			var save = level.Save!;
			Assert.AreEqual( (long)save.World.Variables.GameTick, economy.Turn );
			Assert.AreEqual( save.Economy.Bank.Balance, economy.Balance );
			Assert.AreEqual( save.Staff.Count, economy.Staff.Members.Count );
			Assert.IsFalse( runtime.Import!.ParkClosed );
			// Every pending advice of the saved controller is either imported or reported.
			var controller = new AdvisorController( AdvisorBalance.Load() );
			var report = controller.ImportOriginal( save.Advisor! );
			var valid = save.Advisor!.Pending.Count( record => record.Valid );
			Assert.AreEqual( valid, controller.Queue.Count + int.Parse( report.Split( "dropped " )[1].Split( ' ' )[0] ) );
			Assert.IsTrue( controller.Queue.GetHistory( 0 ).HasBeenPlayed, "the saved Welcome history is imported" );
			// Values read but not imported are reported (SAVE-plan §8.2, "never silently defaulted").
			AssertReportsUnimportedValues( runtime.Import! );
			var advisor = runtime.Import!.Evidence.Single( line => line.StartsWith( "[APPROX:SAVE-010] Advisor controller", StringComparison.Ordinal ) );
			StringAssert.Contains( advisor, $"message {save.Advisor.Current.MessageId}," );
			StringAssert.Contains( advisor, $"mRequestedLastTutorial 0x{save.Advisor.RequestedLastTutorial:X2}" );
			StringAssert.Contains( advisor, $"mMessagePlaying {save.Advisor.MessagePlaying}." );
			foreach ( var record in save.Advisor.Pending.Where( record => record.Valid && controller.Queue.HasDescriptor( (int)record.MessageId ) ) )
				StringAssert.Contains( report, $"message {record.MessageId} thing {record.ThingId} secondary {record.Secondary}" );
			// The researcher researches: the seed park needs no stand-in.
			var progress = economy.Research.ProgressEntries.ToArray();
			var completed = economy.Research.Completed.Count;
			economy.AdvanceDays( 60 );
			Assert.IsTrue( economy.Research.Completed.Count > completed || !economy.Research.ProgressEntries.SequenceEqual( progress ) );
		}
		finally
		{
			FileSystem = original;
		}
	}
}
