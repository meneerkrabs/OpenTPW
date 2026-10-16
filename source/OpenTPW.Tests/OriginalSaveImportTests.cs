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

	internal static BalanceSettings Settings() => EconomyTestData.Settings( apr: 0, extra: ExtraLoans );

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
	internal static OriginalParkImport Park( BalanceSettings settings, byte[]? bank = null, uint parkClosed = 0, IEnumerable<(uint, uint, byte[])>? extraThings = null,
		byte[]? researcher = null, byte[]? bellyBounce = null, byte[]? staffHq = null, bool unverifiedFootprint = false, byte[]? challenges = null, byte[]? staffPool = null )
	{
		var map = OriginalParkImportTests.CreateMap( 3, 3, ( _, _ ) => 0 );
		var things = SaveTestPayloads.ParkThings();
		if ( researcher != null )
			things[0] = (30, (uint)SaveThingModel.Researcher, researcher);
		if ( bellyBounce != null )
			things[1] = (12, (uint)SaveThingModel.Attraction, bellyBounce);
		things[2] = (10, (uint)SaveThingModel.ChallengeSystem, challenges ?? Challenges( settings ));
		things[4] = (8, (uint)SaveThingModel.Bank, bank ?? Bank( settings ));
		if ( staffHq != null )
			things[^1] = (1, (uint)SaveThingModel.StaffHq, staffHq);
		things.Insert( 2, (11, (uint)SaveThingModel.Attraction, SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields { PositionX = 128, PositionY = 128, Angle = 0, InfoId = 1601, Price = 0, Level = 0, Repair = 100, LifeGauge = 100 } )) );
		things.InsertRange( 0, extraThings ?? Array.Empty<(uint, uint, byte[])>() );
		var grid = SaveTestPayloads.Grid( map, ( x, y ) => OriginalParkImportTests.CreateCellRecord( 0, occupied: (x, y) == (1, 1) ) );
		var rideSystem = RideSystemRecord( 1601, 0, 0, SaveObject.FixedItemKind, 1 ).Concat( RideSystemRecord( 1100, 1, 1, SaveObject.PlacedObjectKind, 2, width: unverifiedFootprint ? 2 : 1, rotation: unverifiedFootprint ? 90 : 0 ) ).ToArray();
		var payload = SaveTestPayloads.Payload( SaveTestPayloads.World( SaveTestPayloads.ParkVariables( parkClosed: parkClosed ), grid, SaveTestPayloads.Things( things ), opaque: staffPool ), rideSystem );
		return OriginalParkImport.Import( payload, map );
	}

	/// <summary>A 670-byte staff pool (tp.exe 0x507850) with the given valid candidates in its first slots and a stray invalid slot after them.</summary>
	private static byte[] StaffPool( uint timeSig, params (uint Type, uint Name, byte Grade, byte SubType, uint TimeSig, uint Timeout)[] candidates )
	{
		var pool = new byte[SaveStaffPool.Bytes];
		for ( var slot = 0; slot <= candidates.Length; slot++ )
		{
			var record = pool.AsSpan( slot * SaveStaffCandidate.Bytes, SaveStaffCandidate.Bytes );
			var (type, name, grade, subType, sig, timeout) = slot < candidates.Length ? candidates[slot] : (9u, 3u, (byte)1, (byte)0, 1u, 2u);
			BinaryPrimitives.WriteUInt32LittleEndian( record, type );
			BinaryPrimitives.WriteUInt32LittleEndian( record[4..], name );
			record[8] = grade;
			record[9] = subType;
			record[10] = slot < candidates.Length ? (byte)1 : (byte)0;
			BinaryPrimitives.WriteUInt32LittleEndian( record[12..], sig );
			BinaryPrimitives.WriteUInt32LittleEndian( record[16..], timeout );
		}
		BinaryPrimitives.WriteUInt32LittleEndian( pool.AsSpan( SaveStaffPool.Slots * SaveStaffCandidate.Bytes ), 2 );
		BinaryPrimitives.WriteUInt32LittleEndian( pool.AsSpan( SaveStaffPool.Bytes - 5 ), timeSig );
		pool[^1] = 1;
		return Enumerable.Repeat( (byte)0xCD, 37 ).Concat( pool ).ToArray();
	}

	[TestMethod]
	public void ImportsTheSavedHiringPool()
	{
		var settings = Settings();
		var save = Park( settings, staffPool: StaffPool( 740, (3, 7, 3, 2, 700, 130), (4, 12, 1, 0, 750, 121) ) );
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var import = OriginalEconomyImport.Apply( economy, save );
		// A candidate leaves on the first turn past 4 × its timeout from its stamp; the pool refills past 4 × TimeBetweenStaffUpdates from its own (tp.exe 0x5084f0)
		CollectionAssert.AreEqual( new[] { (StaffType.Guard, 7, 3, ParkCalendar.TickOfTurn( 700 + 4 * 130 + 1 )), (StaffType.Researcher, 12, 1, ParkCalendar.TickOfTurn( 750 + 4 * 121 + 1 )) },
			economy.Staff.Candidates.Select( candidate => (candidate.Type, candidate.NameIndex, candidate.Grade, candidate.ExpiresTick) ).ToArray() );
		Assert.AreEqual( ParkCalendar.TickOfTurn( 740 + 4 * settings.TimeBetweenStaffUpdates + 1 ), economy.Staff.NextPoolUpdateTick );
		Assert.IsTrue( import.Evidence.Any( line => line.StartsWith( "Staff pool imported: 2 candidates", StringComparison.Ordinal ) ) );
		StringAssert.Contains( import.Evidence.Single( line => line.Contains( "[APPROX:SAVE-004]" ) ), "sub-types 2/0, on pointer 0, people in category 2/0/0/0/0, stop producing 0/0/0/0/0, opening pool generated 1." );
		// The imported candidates stay until their own turn and the pool refills on its own schedule.
		economy.Advance( ParkCalendar.TickOfTurn( 700 + 4 * 130 + 1 ) - economy.Tick );
		Assert.IsFalse( economy.Staff.Candidates.Any( candidate => candidate.Type == StaffType.Guard && candidate.NameIndex == 7 ) );
		Assert.IsTrue( economy.Staff.Candidates.Any( candidate => candidate.Type == StaffType.Researcher && candidate.NameIndex == 12 ) );
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
		Assert.AreEqual( firstOffer, economy.Objectives.NextOfferDay, "a system not yet switched on offers on park day DaysUntilFirstChallenge (tp.exe 0x4d1e90), not counted from the import" );
		Assert.AreEqual( poolUpdate + economy.Tick, economy.Staff.NextPoolUpdateTick );
		Assert.IsTrue( economy.Staff.Candidates.All( candidate => candidate.ExpiresTick > economy.Tick ) );

		Assert.AreEqual( (87987L, 25, -12013L), (economy.Balance, economy.EntranceFee, economy.Ledger.ProfitThisYear) );
		Assert.AreEqual( 87987, economy.Ledger.CurrentOpeningBalance );
		Assert.AreEqual( 0, economy.Loans.Count );

		Assert.AreEqual( (1, 1), (import.ImportedObjects, import.ImportedFixedItems) );
		var bouncy = economy.Objects.Single( item => item.InfoId == 1100 );
		Assert.AreEqual( (30, 12, 0, 80.0, 90.0), (bouncy.Price, bouncy.CostOfGoods, bouncy.Level, bouncy.Repair, bouncy.LifeGauge) );
		Assert.AreEqual( 80, bouncy.IngredientAmount, "mAmountOfSpecialIngredient is imported as saved (tp.exe 0x4dcdc1)" );
		Assert.AreEqual( (100, 70), (bouncy.QualityOfGoods, bouncy.ChanceOfLosingPercent), "mQualityOfGoods and the win byte mChanceOfWinning (0x4dcd29): losing = 100 - 30" );
		var attraction = save.Attractions.Single( item => item.InfoId == 1100 );
		var stamp = attraction.Timestamp;
		Assert.AreEqual( ParkCalendar.TickAtSeconds( (long)(new DateTime( stamp.Year, stamp.Month, stamp.Day, stamp.Hour, stamp.Minute, stamp.Second ) - ParkCalendar.Epoch).TotalSeconds ), bouncy.BuiltTick, "the saved time stamp is the build time" );
		Assert.AreEqual( attraction.CustomerCount, (uint)bouncy.TimesUsed, "the saved customer count is the use count: an imported used object never refunds the unused 100 %" );
		Assert.AreEqual( attraction.WalkAwayCount, (uint)bouncy.WalkAways, "mNumWalkAways is the walk-away count" );

		var researcher = economy.Staff.Members.Single();
		Assert.AreEqual( (StaffType.Researcher, 2, 0, 97, StaffState.Patrolling), (researcher.Type, researcher.Grade, researcher.TrainingPoints, researcher.Happiness, researcher.State) );
		Assert.AreEqual( ParkCalendar.TickOfTurn( 648 ), researcher.HiredTick, "hired 2,430,000 park seconds after FunnyTimeStart = turn 648" );
		Assert.AreEqual( 1, import.ImportedStaff );
		Assert.IsFalse( import.ParkClosed );
		foreach ( var id in new[] { "SAVE-004", "SAVE-005", "SAVE-006", "SAVE-007", "SAVE-008", "SAVE-010" } )
			Assert.IsTrue( import.Evidence.Any( line => line.Contains( $"[APPROX:{id}]" ) ), $"{id} is reported" );
		Assert.IsTrue( import.Evidence.Any( line => line.Contains( "0x3F416BBA" ) && line.Contains( "imported" ) ), "the generator state is named as imported" );
		Assert.AreEqual( 0x3F416BBAu, economy.World.State, "the save's mRandomSeed is the world generator's state (tp.exe 0x00516330, saved raw by 0x00517123)" );
		Assert.IsFalse( import.Evidence.Any( line => line.Contains( "[APPROX:SAVE-001]" ) ), "SAVE-001 is retired" );
		Assert.IsTrue( import.Evidence.Any( line => line.StartsWith( "0 saved guests", StringComparison.Ordinal ) && line.Contains( "imported by the guest simulation" ) ) );
		Assert.IsFalse( import.Evidence.Any( line => line.Contains( "adjusted on import" ) ), "in-range staff values are imported unchanged" );
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
		StringAssert.Contains( Line( "[APPROX:SAVE-008] Not imported:" ), "batch balance " );
		StringAssert.Contains( Line( "Bank words imported:" ), "withdrawals enabled, last balance " );
		StringAssert.Contains( Line( "Loan offers' stored credit tests imported:" ), "the Available tab lists " );
		var challenges = Line( "[APPROX:SAVE-005]" );
		foreach ( var value in new[] { "losses ", "ActualThing ", "follow-up things " } )
			StringAssert.Contains( challenges, value );
		StringAssert.Contains( Line( "Challenges imported:" ), "not started" );
		var attractions = import.Evidence.Where( line => line.StartsWith( "[APPROX:SAVE-006] Attraction thing ", StringComparison.Ordinal ) ).ToArray();
		Assert.AreNotEqual( 0, attractions.Length );
		foreach ( var line in attractions )
		{
			foreach ( var value in new[] { "operating capacity ", "duration ", "speed ", "open word ", "third gauge ", "name \"", "daily rings ",
				"claim by thing ", "queue size ", "first ", "back ", "called ", "mesh instance ", "flags 0x" } )
				StringAssert.Contains( line, value );
		}
		StringAssert.Contains( Line( "Attraction walk-away counts " ), "imported" );
		StringAssert.Contains( Line( "Staff jobs-done counts " ), "need no import" );
		var hq = Line( "[APPROX:SAVE-009]" );
		foreach ( var value in new[] { "force strike ", "picked up ", "trained " } )
			StringAssert.Contains( hq, value );
		var imported = Line( "Staff HQ imported:" );
		foreach ( var value in new[] { "training budgets ", "strike levels ", "last considered on turns " } )
			StringAssert.Contains( imported, value );
		StringAssert.Contains( Line( "[APPROX:SAVE-010] Saved advisor pending advice" ), "imported only when the automatic advisor attaches" );
	}

	[TestMethod]
	public void ReportsEveryStaffAndAttractionValueItAdjusts()
	{
		var settings = Settings();
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var save = Park( settings, researcher: SaveTestPayloads.Staff( SaveThingModel.Researcher, happiness: 150, state: 15, percentage: 100, hired: 125_935_884_000_000_000 + 5 * OriginalEconomyImport.TimeStampUnitsPerSecond ),
			bellyBounce: SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields { PositionX = 1 * 256 + 128, PositionY = 1 * 256 + 128, Angle = 0, Price = 30, Level = 0, Repair = 80, LifeGauge = 90, CostOfGoods = -4 } ) );
		var import = OriginalEconomyImport.Apply( economy, save );

		var researcher = economy.Staff.Members.Single();
		Assert.AreEqual( (100, 99, StaffState.Working), (researcher.Happiness, researcher.TrainingPoints, researcher.State), "clamped to the traced happiness range and OpenTPW's 0-99 points; the state word 15 (researching) shows as working (tp.exe 0x5064f0)" );
		var adjusted = import.Evidence.Where( line => line.StartsWith( "Staff value adjusted on import:", StringComparison.Ordinal ) ).ToArray();
		Assert.AreEqual( 3, adjusted.Length );
		Assert.IsTrue( adjusted.Any( line => line.Contains( "happiness 150 imported as 100" ) ) );
		Assert.IsTrue( adjusted.Any( line => line.Contains( "grade percentage 100 imported as 99" ) ) );
		CollectionAssert.AreEqual( new[] { StaffState.Idle, StaffState.Patrolling, StaffState.Resting, StaffState.Resting, StaffState.OnStrike, StaffState.OnStrike, StaffState.Working, StaffState.PickedUp, StaffState.Working },
			Enumerable.Range( 0, 9 ).Select( word => ParkStaff.DisplayState( (uint)word ) ).ToArray(), "0x5064f0: the saved word is the state word +0x19c, not the STAFFSTATES index" );
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
		StringAssert.Contains( import.Evidence.Single( line => line.StartsWith( "Staff HQ imported:", StringComparison.Ordinal ) ), "training budgets 0, 250, 0, 0, 0 (handyman, mechanic" );
		StringAssert.Contains( line, "force strike 1, picked up 0" );
		StringAssert.Contains( line, "trained 1." );
		Assert.AreEqual( 250, economy.Staff.GetTrainingBudget( StaffType.Mechanic ), "budget word 1 is the mechanics' (tp.exe 0x50c800)" );
		Assert.IsTrue( Enum.GetValues<StaffType>().Where( type => type != StaffType.Mechanic ).All( type => economy.Staff.GetTrainingBudget( type ) == 0 ) );

		var zero = OriginalEconomyImport.Apply( new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 ), Park( settings ) );
		StringAssert.Contains( zero.Evidence.Single( line => line.StartsWith( "Staff HQ imported:", StringComparison.Ordinal ) ), "training budgets 0, 0, 0, 0, 0" );
		var available = zero.Evidence.Single( line => line.StartsWith( "Loan offers' stored credit tests imported:", StringComparison.Ordinal ) );
		StringAssert.Contains( available, "no offer passed" );
		StringAssert.Contains( available, "the Available tab lists none until the dialog tests them again." );
		Assert.AreEqual( 0, zero.Evidence.Count( line => line.Contains( "Available flags not imported" ) ) );

		var count = zero.Evidence.Count;
		zero.Report( "advisor outcome" );
		Assert.AreEqual( (count + 1, "advisor outcome"), (zero.Evidence.Count, zero.Evidence[^1]) );
	}

	[TestMethod]
	public void ImportsARunningAnOfferedAndAnIdleChallengeSystem()
	{
		// tp.exe 0x4d2320: after the 20 slots, current 4, next event time 8, switched on, challenge on, waiting for reply, value 4, actual thing 2
		var settings = Settings();
		var level = settings.ChallengesInThisLevel;
		var start = Park( settings ).World.Calendar.FunnyTimeStart;
		const long Day = 864_000_000_000;
		ParkEconomy Import( int current, long nextDays, byte on, byte running, byte waiting, int value, Action<byte[]>? slots = null )
		{
			var body = Challenges( settings );
			var tail = 8 + SaveChallengeSystem.Slots * SaveChallengeSystem.SlotBytes;
			BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( tail ), current );
			BinaryPrimitives.WriteInt64LittleEndian( body.AsSpan( tail + 4 ), start + nextDays * Day + Day / 2 );
			(body[tail + 12], body[tail + 13], body[tail + 14]) = (on, running, waiting);
			BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( tail + 15 ), value );
			slots?.Invoke( body );
			var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
			OriginalEconomyImport.Apply( economy, Park( settings, challenges: body ) );
			return economy;
		}
		int Slot( int index, int field ) => 8 + index * SaveChallengeSystem.SlotBytes + field;

		var running = Import( 1, 100, 1, 1, 0, 7, body =>
		{
			body[Slot( 0, 41 )] = 1;
			BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( Slot( 2, 37 ) ), 1 );
		} );
		var active = running.Objectives.Current!;
		Assert.AreEqual( (level[1], true, 101L), (active.DefinitionIndex, active.Accepted, active.DeadlineDay), "the deadline passes at the first daily update after the stamp" );
		var definition = settings.Challenges[level[1]];
		if ( ParkObjectives.IsCountSinceAcceptance( definition.Type ) )
			Assert.AreEqual( 7, running.Objectives.Measure( definition, running.Counters, running.Staff, running.Objects, running.GuestStatistics ) - active.Baseline, "the saved value is reached now" );
		CollectionAssert.AreEqual( new[] { level[0] }, running.Objectives.Finished.ToArray(), "a won slot is finished" );
		Assert.AreEqual( 1, running.Objectives.Declines[level[2]] );
		Assert.AreEqual( 2, running.Objectives.NextListPosition, "the offer scan continues after the current slot" );

		var offered = Import( 0, 0, 1, 0, 1, 0 );
		Assert.AreEqual( (level[0], false), (offered.Objectives.Current!.DefinitionIndex, offered.Objectives.Current.Accepted) );

		var idle = Import( 3, 200, 1, 0, 0, 0 );
		Assert.IsNull( idle.Objectives.Current );
		Assert.AreEqual( 201, idle.Objectives.NextOfferDay, "an idle system offers at the first daily update after the stamp" );
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
		Assert.AreEqual( (true, -700L, 0L), (economy.WithdrawalsEnabled, economy.LastBalance, economy.TurnEnteredRed), "the bank words: the payload's last balance is the balance − 200" );
		Assert.IsTrue( import.Evidence.Any( line => line.StartsWith( "Bank words imported:", StringComparison.Ordinal ) && line.Contains( "months in the red" ) ) );

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
			new SaveTestPayloads.AdvisorRecordFields( 0, 0, 90, 1000, 1, -1, 0, 0 ),
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
		StringAssert.Contains( report, "dropped 1 pending advice of unbound messages (90)" );
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
	public void ImportsTheSavedCurrentRecordAsTheLastTutorialAdvice()
	{
		SaveAdvisorController Saved( SaveTestPayloads.AdvisorRecordFields current )
		{
			var things = SaveTestPayloads.ParkThings();
			things[9] = (3, (uint)SaveThingModel.AdvisorController, SaveTestPayloads.AdvisorController( Array.Empty<SaveTestPayloads.AdvisorRecordFields>(), new Dictionary<int, (uint, int, byte, uint)>(), current ));
			var map = OriginalParkImportTests.CreateMap( 3, 3, ( _, _ ) => 0 );
			var payload = SaveTestPayloads.Payload( SaveTestPayloads.World( SaveTestPayloads.ParkVariables(), SaveTestPayloads.Grid( map ), SaveTestPayloads.Things( things ) ) );
			return SaveAdvisorController.Read( payload, SaveWorld.Parse( payload, SavePayloadLayout.Parse( payload ), 3, 3 ).Single( SaveThingModel.AdvisorController ) );
		}
		AdvisorController Fresh() => new( AdvisorBalance.Parse( Encoding.ASCII.GetBytes( AdvisorSettings ), "test.sam" ) );

		var controller = Fresh();
		var report = controller.ImportOriginal( Saved( new SaveTestPayloads.AdvisorRecordFields( 0, 0, 188, 1000, 1, 0, 1, 1 ) ) );
		Assert.AreEqual( new AdvisorPendingAdvice( 188, 1000, 0, true ), controller.Queue.LastTutorial, "the record at +0x16D0 keeps its score, response number and override" );
		StringAssert.Contains( report, "last tutorial advice message 188 kept" );

		controller = Fresh();
		controller.Advise( 189, 0, 0, true );
		controller.ImportOriginal( Saved( new SaveTestPayloads.AdvisorRecordFields( 0, 0, 346, 1000, 0, -1, 0, 0 ) ) );
		Assert.IsNull( controller.Queue.LastTutorial, "a free record imports none and replaces the session's own" );

		controller = Fresh();
		report = controller.ImportOriginal( Saved( new SaveTestPayloads.AdvisorRecordFields( 0, 0, 128, 20, 1, -1, 0, 0 ) ) );
		Assert.IsNull( controller.Queue.LastTutorial );
		StringAssert.Contains( report, "last tutorial advice message 128 dropped (unbound or not a tutorial)" );
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
			// The saved hiring pool (tp.exe 0x507850): 16 candidates and refills on turns 361 and 722 (4 × 90 + 1 apart), the next one past 4 × 90 more turns
			var pool = save.World.StaffPool!;
			Assert.AreEqual( (16, 722u), (economy.Staff.Candidates.Count, pool.TimeSig) );
			Assert.AreEqual( ParkCalendar.TickOfTurn( 722 + 4 * 90 + 1 ), economy.Staff.NextPoolUpdateTick );
			CollectionAssert.AreEqual( pool.Candidates.Where( candidate => candidate.Valid ).Select( candidate => ((StaffType)candidate.Type, (int)candidate.Grade,
				ParkCalendar.TickOfTurn( candidate.TimeSig + 4 * candidate.Timeout + 1 )) ).ToList(),
				economy.Staff.Candidates.Select( candidate => (candidate.Type, candidate.Grade, candidate.ExpiresTick) ).ToList() );
			// The shipped lab (tp.exe 0x502dc0): 24 projects, rides 1150 at 30 of 600 points, features 1414 at 7.5, shops 1212 at 4.5, sideshows 1304 at 9; effort 100/15/30/25/0, work load 85
			var research = economy.Research;
			Assert.AreEqual( 85, research.Workload );
			Assert.AreEqual( (1150, 1414, 1212, 1304), (research.Current( ResearchCategory.Ride )?.InfoId ?? 0, research.Current( ResearchCategory.Feature )?.InfoId ?? 0,
				research.Current( ResearchCategory.Shop )?.InfoId ?? 0, research.Current( ResearchCategory.Sideshow )?.InfoId ?? 0), "the saved current projects, not OpenTPW's info-id order (1400 before 1414)" );
			Assert.AreEqual( 30L * ParkResearch.PointScale, research.GetProgress( research.Current( ResearchCategory.Ride )! ) );
			Assert.AreEqual( 7500L, research.GetProgress( research.Current( ResearchCategory.Feature )! ) );
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
