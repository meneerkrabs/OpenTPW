using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SaveWorldTests
{
	private static readonly MapFile Map = OriginalParkImportTests.CreateMap( 3, 2, ( _, _ ) => 0 );

	private static byte[] Payload( List<(uint Id, uint Model, byte[] Body)>? things = null, byte[]? variables = null, byte[]? calendar = null, Func<byte[], byte[]>? editThings = null )
	{
		var list = SaveTestPayloads.Things( things ?? SaveTestPayloads.ParkThings() );
		return SaveTestPayloads.Payload( SaveTestPayloads.World( variables ?? SaveTestPayloads.ParkVariables(), SaveTestPayloads.Grid( Map ), editThings?.Invoke( list ) ?? list, calendar: calendar ) );
	}

	private static SaveWorld Parse( byte[] payload ) => SaveWorld.Parse( payload, SavePayloadLayout.Parse( payload ), Map.CellCountX, Map.CellCountY );

	[TestMethod]
	public void WalksTheThingChainToTheWorldDelimiter()
	{
		var payload = Payload();
		var world = Parse( payload );
		var expected = SaveTestPayloads.ParkThings();
		Assert.AreEqual( 30u, world.UsedThingHead );
		CollectionAssert.AreEqual( expected.Select( thing => (int)thing.Id ).ToArray(), world.Things.Select( thing => thing.Id ).ToArray() );
		CollectionAssert.AreEqual( expected.Select( thing => (SaveThingModel)thing.Model ).ToArray(), world.Things.Select( thing => thing.Model ).ToArray() );
		CollectionAssert.AreEqual( expected.Select( thing => thing.Body.Length ).ToArray(), world.Things.Select( thing => thing.BodyLength ).ToArray() );
		Assert.AreEqual( world.Block.End, world.Things[^1].End, "the chain closes exactly at WRLD" );
		Assert.AreEqual( 0, world.Things[^1].NextId );
		Assert.AreEqual( world.Cells.EndOffset + 4, world.Things[0].Offset );
		for ( var index = 1; index < world.Things.Count; index++ )
			Assert.AreEqual( world.Things[index - 1].End, world.Things[index].Offset );
		Assert.AreEqual( (2u, 755u, 0x3F416BBAu, (ushort)8), (world.Variables.Version, world.Variables.GameTick, world.Variables.RandomSeed, world.Variables.BankAccount) );
		Assert.AreEqual( new SaveCalendar( world.Calendar.Offset, 125_911_584_000_000_000, 125_850_128_932_900_000, 1, 2, 15_000 ), world.Calendar );
		Assert.AreEqual( world.Calendar.Offset + SaveCalendar.Bytes, world.ArrivalOffset );
		Assert.AreEqual( world.ArrivalOffset + SaveWorld.ArrivalBytes, world.Cells.StartOffset );
		Assert.AreEqual( 37, world.OpaqueMacroAiLength );
		Assert.AreEqual( SaveThingModel.Bank, world.Single( SaveThingModel.Bank ).Model );
		Assert.ThrowsException<InvalidDataException>( () => world.Single( SaveThingModel.Guest ) );
	}

	[TestMethod]
	public void RejectsAChainThatDoesNotCloseAtTheDelimiter()
	{
		// One byte more or less before WRLD.
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Parse( Payload( editThings: list => list.Append( (byte)0 ).ToArray() ) ) ).Message, "does not close" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( Payload( editThings: list => list[..^1] ) ) );
		// A body one byte longer than its model's width shifts every later header.
		var things = SaveTestPayloads.ParkThings();
		things[4] = (things[4].Id, things[4].Model, things[4].Body.Append( (byte)0 ).ToArray());
		Assert.ThrowsException<InvalidDataException>( () => Parse( Payload( things ) ) );
	}

	[TestMethod]
	public void RejectsUnknownModelsRepeatedIdsAndTruncatedBodies()
	{
		foreach ( var model in new uint[] { 0, 2, 18, 20, uint.MaxValue } )
		{
			var things = SaveTestPayloads.ParkThings();
			things[3] = (things[3].Id, model, things[3].Body);
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Parse( Payload( things ) ) ).Message, "unsupported model", $"model {model}" );
		}
		var repeated = SaveTestPayloads.ParkThings();
		repeated[5] = (repeated[2].Id, repeated[5].Model, repeated[5].Body);
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Parse( Payload( repeated ) ) ).Message, "repeated" );
		var huge = SaveTestPayloads.ParkThings();
		huge[1] = (0x10000, huge[1].Model, huge[1].Body);
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Parse( Payload( huge ) ) ).Message, "out-of-range" );
		// The last thing's body runs into the delimiter.
		var truncated = SaveTestPayloads.ParkThings();
		truncated[^1] = (truncated[^1].Id, truncated[^1].Model, truncated[^1].Body[..^3]);
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Parse( Payload( truncated ) ) ).Message, "crosses the World delimiter" );
		var attraction = SaveTestPayloads.ParkThings();
		attraction[1] = (attraction[1].Id, attraction[1].Model, attraction[1].Body[..40]);
		Assert.ThrowsException<InvalidDataException>( () => Parse( Payload( attraction.Take( 2 ).ToList(), SaveTestPayloads.Variables() ) ) );
	}

	[TestMethod]
	public void ChecksWorldVariablesVersionAndCalendar()
	{
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Parse( Payload( variables: SaveTestPayloads.Variables( version: 3 ) ) ) ).Message, "version 3" );
		var wrongModel = SaveTestPayloads.Variables( bank: 10 );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Parse( Payload( variables: wrongModel ) ) ).Message, "mBankAccount".Substring( 1 ) );
		var absent = SaveTestPayloads.Variables( researchLab: 77 );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Parse( Payload( variables: absent ) ) ).Message, "not in the thing list" );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Parse( Payload( calendar: SaveTestPayloads.CalendarAndArrivals( rate: 0 ) ) ) ).Message, "calendar" );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Parse( Payload( calendar: SaveTestPayloads.CalendarAndArrivals( month: 12 ) ) ) ).Message, "calendar" );
	}

	[TestMethod]
	public void AcceptsTheCalendarOfAnUnplayedParkAndNothingLooser()
	{
		// tp.exe 0x4F7E80: a new calendar starts at 2000-01-01 with month cache 0, day cache -1 and rate 15000
		var world = Parse( Payload( calendar: SaveTestPayloads.CalendarAndArrivals( month: 0, day: -1 ) ) );
		Assert.IsTrue( world.Calendar.IsInitial );
		Assert.IsFalse( world.Calendar.IsUpdated );
		Assert.AreEqual( (0, -1, 15_000), (world.Calendar.MonthAtLastUpdate, world.Calendar.DayAtLastUpdate, world.Calendar.FunnySecsPerRealSec) );
		Assert.IsFalse( Parse( Payload() ).Calendar.IsInitial );
		foreach ( var calendar in new[]
		{
			SaveTestPayloads.CalendarAndArrivals( month: 0, day: -1, rate: 7_500 ),
			SaveTestPayloads.CalendarAndArrivals( month: 0, day: 0 ),
			SaveTestPayloads.CalendarAndArrivals( month: 1, day: -1 ),
			SaveTestPayloads.CalendarAndArrivals( month: 0, day: -2 ),
			SaveTestPayloads.CalendarAndArrivals( month: 0, day: -1, funnyTimeStart: 125_911_584_000_000_001L ),
		} )
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Parse( Payload( calendar: calendar ) ) ).Message, "calendar" );
	}

	[TestMethod]
	public void ReadsTheEmptyHistoryRingOfANewAttractionAndNothingLooser()
	{
		// tp.exe 0x51ADE0: a new ring has current entry -1, 30 entries and no wrap
		var empty = SaveAttraction.Read( SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields { RingHead = -1, RingFlag = 0 } ), 0, 1 );
		Assert.IsTrue( empty.Histories.All( ring => ring.CurrentEntry == SaveAttraction.EmptyRingHead && !ring.WrappedAround && ring.Entries.Count == 30 ) );
		foreach ( var fields in new[]
		{
			new SaveTestPayloads.AttractionFields { RingHead = -1, RingFlag = 1 },
			new SaveTestPayloads.AttractionFields { RingHead = -1, RingFlag = 0, RingCount = 29 },
			new SaveTestPayloads.AttractionFields { RingHead = -2, RingFlag = 0 },
			new SaveTestPayloads.AttractionFields { RingHead = int.MinValue, RingFlag = 0 },
		} )
			Assert.ThrowsException<InvalidDataException>( () => SaveAttraction.Read( SaveTestPayloads.Attraction( fields ), 0, 1 ), fields.ToString() );
	}

	[TestMethod]
	public void ReadsAttractionsWithinTheirBounds()
	{
		var body = SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields() );
		var attraction = SaveAttraction.Read( body, 0, 12 );
		Assert.AreEqual( (12, 1100, 51, 23, 90), (attraction.Id, attraction.InfoId, attraction.CellX, attraction.CellY, attraction.Angle) );
		Assert.AreEqual( ("Belly", "Bounce"), (attraction.NameLine1, attraction.NameLine2) );
		Assert.AreEqual( (5, 30, 60u, 12, 50, 100, 20, 4), (attraction.OperatingCapacity, attraction.OperatingDuration, attraction.OperatingSpeed,
			attraction.CostOfGoods, attraction.QualityOfGoods, attraction.ChanceOfWinning, attraction.PricePerUse, attraction.QueueSizeInCells) );
		Assert.AreEqual( (7u, 2u, 150, 900, 100f, 97f, 64f, 1), (attraction.CustomerCount, attraction.WalkAwayCount, attraction.TotalCosts, attraction.TotalTakings,
			attraction.Gauge48, attraction.LifeGauge, attraction.StateOfRepair, attraction.UpgradeLevel) );
		Assert.AreEqual( 50, attraction.AmountOfSpecialIngredient );
		Assert.AreEqual( -7, SaveAttraction.Read( SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields { IngredientAmount = -7 } ), 0, 1 ).AmountOfSpecialIngredient, "the loader takes the word unclamped (0x4dcdc1)" );
		Assert.AreEqual( body.Length, attraction.Length );
		Assert.AreEqual( body.Length - 6 * 30 * 4, SaveAttraction.Read( SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields { RingCount = 0 } ), 0, 1 ).Length );
		var invalid = new[]
		{
			new SaveTestPayloads.AttractionFields { Repair = float.NaN },
			new SaveTestPayloads.AttractionFields { LifeGauge = -1f },
			new SaveTestPayloads.AttractionFields { Gauge48 = 256f },
			new SaveTestPayloads.AttractionFields { Stamp = new uint[] { 2000, 13, 1, 6, 15, 37, 30, 0 } },
			new SaveTestPayloads.AttractionFields { Stamp = new uint[] { 2000, 1, 0, 6, 15, 37, 30, 0 } },
			new SaveTestPayloads.AttractionFields { Stamp = new uint[] { 2000, 1, 1, 6, 24, 37, 30, 0 } },
			new SaveTestPayloads.AttractionFields { State = ushort.MaxValue + 1u },
			new SaveTestPayloads.AttractionFields { QueueCells = ushort.MaxValue + 1u },
			new SaveTestPayloads.AttractionFields { RingFlag = 2 },
			new SaveTestPayloads.AttractionFields { Angle = 45 },
			new SaveTestPayloads.AttractionFields { InfoId = 0 },
			new SaveTestPayloads.AttractionFields { Price = SaveAttraction.MaximumPricePerUse + 1 },
			new SaveTestPayloads.AttractionFields { Level = SaveAttraction.MaximumUpgradeLevel + 1 },
			new SaveTestPayloads.AttractionFields { RingCount = SaveAttraction.MaximumRingEntries + 1 },
		};
		foreach ( var fields in invalid )
			Assert.ThrowsException<InvalidDataException>( () => SaveAttraction.Read( SaveTestPayloads.Attraction( fields ), 0, 1 ), fields.ToString() );
		for ( var length = 0; length < body.Length; length += 97 )
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SaveAttraction.Read( body.AsSpan( 0, length ), 0, 1 ) ).Message, "truncated" );
	}

	[TestMethod]
	public void ReadsTheBankAndRejectsCorruptLoanOffers()
	{
		var world = Parse( Payload() );
		var payload = Payload();
		var bank = SaveBank.Read( payload, world.Single( SaveThingModel.Bank ) );
		Assert.AreEqual( (8, 25, 87987, 0, true, 87787, 0u, -12013), (bank.Id, bank.Account.AdmissionFee, bank.Account.Balance, bank.Account.BatchBalance,
			bank.Account.WithdrawalsEnabled, bank.Account.LastBalance, bank.Account.TurnEnteredRed, bank.Account.ProfitThisYear) );
		Assert.AreEqual( new SaveThingBase( 128, 128, 7, 9 ), bank.Base );
		Assert.AreEqual( 8, bank.Loans.Count );
		Assert.AreEqual( new SaveLoanRecord( bank.Loans[3].Offset, 3, true, 40_000, 0, 36, 1111, false, 0, 3 ), bank.Loans[3] );
		Assert.AreEqual( bank.Account.Offset + SaveBank.AccountBytes + 3 * SaveBank.LoanBytes, bank.Loans[3].Offset );
		Assert.ThrowsException<ArgumentException>( () => SaveBank.Read( payload, world.Single( SaveThingModel.ChallengeSystem ) ) );
		foreach ( var corrupt in new Func<int, int[]>[]
		{
			index => new[] { 2, 1000, 0, 10, 100, 0, 0, index },
			index => new[] { 0, 1000, 0, 10, 100, 2, 0, index },
			index => new[] { 0, 1000, 0, 0, 100, 0, 0, index },
			index => new[] { 0, 1000, 0, 10, 100, 1, 11, index },
			index => new[] { 0, -1, 0, 10, 100, 0, 0, index },
		} )
		{
			var things = SaveTestPayloads.ParkThings();
			things[4] = (8, (uint)SaveThingModel.Bank, SaveTestPayloads.Bank( loans: corrupt ));
			var changed = Payload( things );
			Assert.ThrowsException<InvalidDataException>( () => SaveBank.Read( changed, Parse( changed ).Single( SaveThingModel.Bank ) ) );
		}
		var flag = SaveTestPayloads.ParkThings();
		flag[4] = (8, (uint)SaveThingModel.Bank, SaveTestPayloads.Bank( withdrawals: 2 ));
		var flagPayload = Payload( flag );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SaveBank.Read( flagPayload, Parse( flagPayload ).Single( SaveThingModel.Bank ) ) ).Message, "withdrawals" );
	}

	[TestMethod]
	public void ReadsLeadingChallengeSlotsOnly()
	{
		var payload = Payload();
		var system = SaveChallengeSystem.Read( payload, Parse( payload ).Single( SaveThingModel.ChallengeSystem ) );
		Assert.AreEqual( 2, system.Challenges.Count );
		Assert.AreEqual( (3, 60, 30, 5000, false, true), (system.Challenges[0].Type, system.Challenges[0].TargetTime, system.Challenges[0].TargetValue, system.Challenges[0].Prize,
			system.Challenges[0].CheckAtEndOnly, system.Challenges[0].Independent) );
		Assert.AreEqual( (-1, 0L), (system.CurrentChallenge, system.NextEventTime) );
		var gap = SaveTestPayloads.ParkThings();
		gap[2] = (10, (uint)SaveThingModel.ChallengeSystem, SaveTestPayloads.ChallengeSystem( new[] { (3, 60, 30, 5000) }, gap: true ));
		var gapPayload = Payload( gap );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SaveChallengeSystem.Read( gapPayload, Parse( gapPayload ).Single( SaveThingModel.ChallengeSystem ) ) ).Message, "after an unused slot" );
		var economy = SaveEconomyRecords.Parse( payload, Parse( payload ) );
		Assert.AreEqual( (8, 2), (economy.BankAccount.Id, economy.Challenges.Count) );
	}

	[TestMethod]
	public void ReadsTheAdvisorControllerRecordsAndHistory()
	{
		var payload = Payload();
		var advisor = SaveAdvisorController.Read( payload, Parse( payload ).Single( SaveThingModel.AdvisorController ) );
		Assert.AreEqual( SaveAdvisorController.Bytes, SaveThingChain.FixedBodyBytes[SaveThingModel.AdvisorController] );
		Assert.AreEqual( 5838, SaveAdvisorController.Bytes );
		Assert.AreEqual( 8, advisor.Pending.Count );
		Assert.AreEqual( new SaveAdvisorRecord( 0, 0, 128, 20, true, -1, 0, 0 ), advisor.Pending[0] );
		Assert.IsTrue( advisor.Pending.Skip( 1 ).All( record => !record.Valid ), "0xCD records with a clear valid byte are free slots" );
		Assert.AreEqual( 351, advisor.History.Count );
		Assert.AreEqual( new SaveAdvisorHistory( 1, 0, true, 0xCDCDCD, 0 ), advisor.History[0] );
		Assert.IsTrue( advisor.History.Skip( 1 ).All( entry => entry.IsInitial ) );
		Assert.AreEqual( (346u, 282u), (advisor.Current.MessageId, advisor.MessagePlaying) );
		Assert.AreEqual( 3, advisor.RawWords.Count );

		var played = SaveTestPayloads.ParkThings();
		played[9] = (3, (uint)SaveThingModel.AdvisorController, SaveTestPayloads.AdvisorController( Array.Empty<SaveTestPayloads.AdvisorRecordFields>(), new Dictionary<int, (uint, int, byte, uint)> { [5] = (1, 0, 2, 0) } ));
		var playedPayload = Payload( played );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SaveAdvisorController.Read( playedPayload, Parse( playedPayload ).Single( SaveThingModel.AdvisorController ) ) ).Message, "played byte" );
		var valid = SaveTestPayloads.ParkThings();
		valid[9] = (3, (uint)SaveThingModel.AdvisorController, SaveTestPayloads.AdvisorController( new[] { new SaveTestPayloads.AdvisorRecordFields( 0, 0, 1, 1, 5, -1, 0, 0 ) }, new Dictionary<int, (uint, int, byte, uint)>() ));
		var validPayload = Payload( valid );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SaveAdvisorController.Read( validPayload, Parse( validPayload ).Single( SaveThingModel.AdvisorController ) ) ).Message, "mValid" );
	}

	[TestMethod]
	public void ReadsStaffStaffHqAndResearchLab()
	{
		var payload = Payload();
		var world = Parse( payload );
		var researcher = SaveStaff.Read( payload, world.Single( SaveThingModel.Researcher ) );
		Assert.AreEqual( (30, SaveThingModel.Researcher, 2, 97f, "Nicholas Ricks", 1u, 125_935_884_000_000_000L, 93f, (ushort)0),
			(researcher.Id, researcher.Model, researcher.Staff.Grade, researcher.Staff.Happiness, researcher.Staff.Name, researcher.Staff.State,
				researcher.Staff.HiredTimestamp, researcher.Staff.Energy, researcher.RoleNextId) );
		Assert.AreEqual( SaveThingChain.PersonBytes - SaveThingBase.Bytes, researcher.PersonBytes.Length );
		Assert.AreEqual( 4, researcher.RoleBytes.Length );
		foreach ( var model in new[] { SaveThingModel.Mechanic, SaveThingModel.Handyman, SaveThingModel.Entertainer, SaveThingModel.Guard } )
		{
			var things = new List<(uint, uint, byte[])> { (20, (uint)model, SaveTestPayloads.Staff( model, grade: 3, roleNext: 19 )) };
			var staffPayload = Payload( things, SaveTestPayloads.Variables() );
			var staff = SaveStaff.Read( staffPayload, Parse( staffPayload ).Things[0] );
			Assert.AreEqual( (model, 3, (ushort)19), (staff.Model, staff.Staff.Grade, staff.RoleNextId) );
		}
		var badGrade = new List<(uint, uint, byte[])> { (20, (uint)SaveThingModel.Guard, SaveTestPayloads.Staff( SaveThingModel.Guard, grade: 5 )) };
		var badPayload = Payload( badGrade, SaveTestPayloads.Variables() );
		Assert.ThrowsException<InvalidDataException>( () => SaveStaff.Read( badPayload, Parse( badPayload ).Things[0] ) );
		Assert.ThrowsException<ArgumentException>( () => SaveStaff.Read( payload, world.Single( SaveThingModel.Bank ) ) );

		var hq = SaveStaffHq.Read( payload, world.Single( SaveThingModel.StaffHq ) );
		Assert.AreEqual( (5, 60), (hq.TrainingBudgets.Count, hq.StrikeLevels.Length) );
		var lab = SaveResearchLab.Read( payload, world.Single( SaveThingModel.ResearchLab ) );
		Assert.AreEqual( (256, 5, 13), (lab.Projects.Count, lab.Groups.Count, lab.Tail.Length) );
		Assert.AreEqual( 4182, SaveResearchLab.Bytes );
	}

	[TestMethod]
	public void OriginalJungleEasymodeWorldClosesAndEveryThingParses()
	{
		var payload = OriginalParkImportTests.ReadEasymodePayload();
		var map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( "levels/jungle/terrain.wad", "base.map" ) ) );
		var park = OriginalParkImport.Import( payload, map );
		var world = park.World;
		Assert.AreEqual( world.Block.End, world.Things[^1].End, "the thing chain closes exactly at the WRLD delimiter" );
		Assert.AreEqual( 0, world.Things[^1].NextId );
		Assert.AreEqual( world.Cells.EndOffset + 4, world.Things[0].Offset, "Used Thing Head follows the map" );
		Assert.AreEqual( (int)world.UsedThingHead, world.Things[0].Id );
		Assert.AreEqual( world.Things.Count, world.Things.Select( thing => thing.Id ).Distinct().Count() );
		// Every model with a serializer occurs; singletons once.
		foreach ( var model in Enum.GetValues<SaveThingModel>() )
			Assert.IsTrue( world.OfModel( model ).Any(), model.ToString() );
		foreach ( var model in new[] { SaveThingModel.StaffHq, SaveThingModel.MechanicHq, SaveThingModel.AdvisorController, SaveThingModel.TagSystem, SaveThingModel.ParkAnalyser,
			SaveThingModel.ResearchLab, SaveThingModel.Weather, SaveThingModel.Bank, SaveThingModel.UIMsgReceiver, SaveThingModel.ChallengeSystem } )
			Assert.AreEqual( 1, world.OfModel( model ).Count(), model.ToString() );
		// Counts agree across readers: one attraction thing per RideSystem record, every staff thing read, one per role list head.
		Assert.AreEqual( park.PlacedObjects.Count + park.FixedItems.Count, park.Attractions.Count );
		Assert.IsTrue( park.PlacedObjects.All( item => park.FindAttraction( item.Record ) != null ) && park.FixedItems.All( item => park.FindAttraction( item ) != null ) );
		Assert.AreEqual( world.Things.Count( thing => thing.Model is >= SaveThingModel.Mechanic and <= SaveThingModel.Researcher ), park.Staff.Count );
		Assert.AreEqual( world.Variables.FirstResearcher, park.Staff.Single( staff => staff.Model == SaveThingModel.Researcher ).Id );
		Assert.IsTrue( park.Staff.All( staff => staff.RoleNextId == 0 && staff.Staff.Name.Length > 0 ) );
		Assert.AreEqual( world.Things.Count, park.Attractions.Count + park.Staff.Count + park.GuestCount + 10 );
		// The calendar suffix before the map converts the saved turn to its own month/day caches.
		Assert.AreEqual( 15_000, world.Calendar.FunnySecsPerRealSec );
		var date = ParkCalendar.DateTimeAt( ParkCalendar.TickOfTurn( world.Variables.GameTick ) );
		Assert.AreEqual( (date.Month - 1, date.Day), (world.Calendar.MonthAtLastUpdate, world.Calendar.DayAtLastUpdate) );
		Assert.AreEqual( SaveWorldVariables.SupportedVersion, world.Variables.Version );
		// Advisor controller: three initialized pending records of which only one is valid, the rest uninitialized and invalid.
		var advisor = park.Advisor!;
		Assert.AreEqual( 1, advisor.Pending.Count( record => record.Valid ) );
		Assert.IsTrue( advisor.Pending.Where( record => !record.Valid ).Any( record => record.MessageId == 0xCDCDCDCD ) );
		Assert.IsTrue( advisor.History.Any( entry => !entry.IsInitial ) );
		Assert.AreEqual( park.Economy.ChallengeSystem.Challenges.Count, park.Economy.Challenges.Count );
		Assert.IsTrue( park.Economy.Challenges.Count is > 0 and <= SaveChallengeSystem.Slots );
	}

	/// <summary>The control manager's table (tp.exe 0x4D3AA0): 150 records of 32 bytes, the used count and the previous search key.</summary>
	private static byte[] ObjectControlTable( params (uint Id, uint Cost, bool Researched, uint Level)[] controls )
	{
		var table = new byte[SaveObjectControls.Bytes];
		for ( var index = 0; index < controls.Length; index++ )
		{
			var record = table.AsSpan( index * SaveObjectControl.Bytes, SaveObjectControl.Bytes );
			BitConverter.GetBytes( controls[index].Id ).CopyTo( record );
			BitConverter.GetBytes( controls[index].Cost ).CopyTo( record[4..] );
			record[0x10] = (byte)(controls[index].Researched ? 1 : 0);
			BitConverter.GetBytes( controls[index].Level ).CopyTo( record[0x14..] );
			BitConverter.GetBytes( 15u ).CopyTo( record[0x1c..] );
		}
		BitConverter.GetBytes( (uint)controls.Length ).CopyTo( table.AsSpan( SaveObjectControls.Slots * SaveObjectControl.Bytes ) );
		BitConverter.GetBytes( (ushort)1100 ).CopyTo( table.AsSpan( SaveObjectControls.Slots * SaveObjectControl.Bytes + 4 ) );
		return table;
	}

	[TestMethod]
	public void ReadsTheObjectControlTableRightAfterTheVariables()
	{
		var table = ObjectControlTable( (1100, 500, true, 2), (1140, 2000, false, 0), (1600, 0, true, 0) );
		var things = SaveTestPayloads.Things( SaveTestPayloads.ParkThings() );
		var payload = SaveTestPayloads.Payload( SaveTestPayloads.World( SaveTestPayloads.ParkVariables(), SaveTestPayloads.Grid( Map ), things, opaque: table ) );
		var world = Parse( payload );
		var controls = world.ObjectControls!;
		Assert.AreEqual( world.VariablesOffset + SaveWorldVariables.Bytes, controls.Offset );
		Assert.AreEqual( 3, controls.Controls.Count );
		Assert.AreEqual( 1100, controls.PreviousSearchKey );
		Assert.AreEqual( new SaveObjectControl( 0, 1100, 500, 0, 0, true, 2, 0, 15 ), controls.Controls[0] );
		Assert.IsFalse( controls.Find( 1140 )!.Value.Researched );
		Assert.IsTrue( controls.Find( 1600 )!.Value.Researched );
		Assert.IsNull( controls.Find( 1234 ) );
		// A table that is cut short, counts more than the 150 slots or holds a researched byte beyond 0 and 1 is not one.
		var short_ = SaveTestPayloads.Payload( SaveTestPayloads.World( SaveTestPayloads.ParkVariables(), SaveTestPayloads.Grid( Map ), things, opaque: table[..^1] ) );
		Assert.IsNull( Parse( short_ ).ObjectControls );
		var many = table.ToArray();
		BitConverter.GetBytes( 151u ).CopyTo( many, SaveObjectControls.Slots * SaveObjectControl.Bytes );
		Assert.IsNull( Parse( SaveTestPayloads.Payload( SaveTestPayloads.World( SaveTestPayloads.ParkVariables(), SaveTestPayloads.Grid( Map ), things, opaque: many ) ) ).ObjectControls );
		var flag = table.ToArray();
		flag[0x10] = 7;
		Assert.IsNull( Parse( SaveTestPayloads.Payload( SaveTestPayloads.World( SaveTestPayloads.ParkVariables(), SaveTestPayloads.Grid( Map ), things, opaque: flag ) ) ).ObjectControls );
		// Without the table (the default synthetic opaque bytes are 37 long) the world has none.
		Assert.IsNull( Parse( Payload() ).ObjectControls );
	}
}
