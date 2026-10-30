using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The park analyser's six saved time stamps (<see cref="SaveAnalyserStamps"/>, tp.exe serializer <c>0x4C5D70</c>) and the advisor of a park from an original save, which takes the saved
/// <c>mLastObjectBuilt</c> and <c>mLastStaffHired</c> as its object and hire stamps (<see cref="OriginalEconomyImport.AnalyserStampTurns"/>) instead of deriving them from the standing
/// objects and the employed staff. The synthetic cases need no game data; the corpus case reads <c>OPENTPW_TPWS_SAVES</c>.
/// </summary>
[TestClass, DoNotParallelize]
public class AdvisorSavedStampsTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "fantasy", "space" };
	/// <summary>The calendar's start in every original save: 2000-01-01 as 100 ns since 1601-01-01 (tp.exe <c>0x4F7E80</c>).</summary>
	private const long Start = SaveCalendar.InitialFunnyTimeStart;
	/// <summary>One park turn of the calendar (15000 / 4 seconds) in 100 ns units.</summary>
	private const long UnitsPerTurn = 3750L * 10_000_000;
	private const uint SavedTurn = 2880;
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Setup() => Log ??= new();

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
	}

	private static long Stamp( long turn, long start = Start ) => start + turn * UnitsPerTurn;

	private static SaveThing Thing( int bytes, SaveThingModel model = SaveThingModel.ParkAnalyser ) => new( 5, model, 0, 0, 0, bytes );

	private static SaveAnalyserStamps Read( byte[] body ) => SaveAnalyserStamps.Read( body, Thing( body.Length ) );

	private static byte[] Analyser( params long[] stamps )
	{
		var body = SaveNewParkThings.ParkAnalyser( 0, 0 );
		for ( var slot = 0; slot < stamps.Length; slot++ )
			BinaryPrimitives.WriteInt64LittleEndian( body.AsSpan( SaveAnalyserStamps.BodyOffset + slot * 8, 8 ), stamps[slot] );
		return body;
	}

	/// <summary>A Theme Park World save on a 3 x 3 map whose world names the analyser thing 5 (or none), at park turn <see cref="SavedTurn"/>.</summary>
	private static OriginalParkImport Save( BalanceSettings settings, long objectStamp, long hireStamp, long calendarStart = Start, bool analyser = true, int rate = SaveCalendar.InitialRate, int[]? counters = null )
	{
		var map = OriginalParkImportTests.CreateMap( 3, 3, ( _, _ ) => 0 );
		var body = Analyser( Stamp( 0, calendarStart ), Stamp( 0, calendarStart ), Stamp( 0, calendarStart ), Stamp( 0, calendarStart ), objectStamp, hireStamp );
		for ( var word = 0; counters != null && word < counters.Length; word++ )
			BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( SaveAnalyserStamps.BodyOffset - 16 + word * 4, 4 ), counters[word] );
		var bank = SaveTestPayloads.Bank( loans: index =>
		{
			var offer = settings.Loans[index];
			return new[] { 0, (int)offer.Amount, offer.AprPercent, offer.Months, (int)LoanMath.MonthlyRepayment( offer.Amount, offer.AprPercent, offer.Months ), 0, 0, offer.LenderNameIndex };
		} );
		var things = SaveTestPayloads.Things( new[]
		{
			(8u, (uint)SaveThingModel.Bank, bank),
			(10u, (uint)SaveThingModel.ChallengeSystem, OriginalSaveImportTests.Challenges( settings )),
			(5u, (uint)SaveThingModel.ParkAnalyser, body),
		} );
		var variables = SaveTestPayloads.Variables( bank: 8, gameTick: SavedTurn );
		BinaryPrimitives.WriteUInt16LittleEndian( variables.AsSpan( 20, 2 ), analyser ? (ushort)5 : (ushort)0 );
		var world = SaveTestPayloads.World( variables, SaveTestPayloads.Grid( map ), things, calendar: SaveTestPayloads.CalendarAndArrivals( rate: rate, funnyTimeStart: calendarStart ) );
		return OriginalParkImport.Import( SaveTestPayloads.Payload( world ), map );
	}

	private static (ParkEconomy Economy, OriginalEconomyImport Import) Imported( OriginalParkImport save, BalanceSettings settings )
	{
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		return (economy, OriginalEconomyImport.Apply( economy, save ));
	}

	private static void Set( object target, string name, object? value ) =>
		target.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	/// <summary>A level of an original park that has only its save and its economy runtime, as the advisor attachment reads them.</summary>
	private static Level LevelWith( OriginalParkImport save, ParkEconomyRuntime runtime )
	{
		var source = (OriginalPark)RuntimeHelpers.GetUninitializedObject( typeof( OriginalPark ) );
		Set( source, "<LevelName>k__BackingField", "jungle" );
		Set( source, "<Save>k__BackingField", save );
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		Set( level, "<OriginalPark>k__BackingField", source );
		Set( level, "<Park>k__BackingField", runtime );
		return level;
	}

	/// <summary>The runtime a park started from an original save has: the economy and its import (as <see cref="ParkEconomyRuntime.ForOriginalLevel"/> makes it).</summary>
	private static ParkEconomyRuntime ImportedRuntime( ParkEconomy economy, OriginalEconomyImport import ) =>
		(ParkEconomyRuntime)Activator.CreateInstance( typeof( ParkEconomyRuntime ), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { economy, import }, null )!;

	private static AutomaticAdvisor Advisor() => new( new AdvisorController( AdvisorSnapshotTests.Balance() ), () => 0 );

	/// <summary>Whole days from a saved stamp to the park time of a turn, as tp.exe's <c>0x4C9070</c> counts them: (FunnyTimeStart + turn x 3750 x 10^7 - stamp) / 864,000,000,000, signed.</summary>
	private static long OriginalDays( long turn, long stamp, long calendarStart = Start ) => (calendarStart + turn * UnitsPerTurn - stamp) / 864_000_000_000L;

	[TestMethod]
	public void TheSixStampsLieBetweenTheCountersAndTheThreeExtremesThatEndTheBody()
	{
		// grid words, 1024 cells, four short rings, six trackers, sixteen monthly rings, recent stays, arrivals, twenty byte rings, four counters
		Assert.AreEqual( 8 + 5 * 4 + 1024 * 61 + 4 * 17 + 6 * 5 + 16 * 589 + 213 + 33 + 20 * 60 + 4 * 4, SaveAnalyserStamps.BodyOffset );
		Assert.AreEqual( SaveParkStatistics.BodyBytes, SaveAnalyserStamps.BodyOffset + SaveAnalyserStamps.Bytes + 3 * 4, "mMostPaidForTicket, mLargestBusLoad and mLongestStay end the body" );
		var fresh = SaveNewParkThings.ParkAnalyser( 0, 0 );
		Assert.AreEqual( SaveParkStatistics.BodyBytes, fresh.Length );
		Assert.AreEqual( Start, SaveNewParkThings.ZeroTimestamp, "a new park's stamps are the park time of turn 0, the calendar's start" );
		Assert.AreEqual( new SaveAnalyserStamps( Start, Start, Start, Start, Start, Start ), Read( fresh ) );
	}

	[TestMethod]
	public void EachStampIsReadFromItsSlotInTheSerializersOrder()
	{
		var body = Analyser( 11, -22, long.MinValue, long.MaxValue, Stamp( 1000 ), Stamp( 400 ) );
		// the counters before the stamps and the extremes after them are other words
		for ( var counter = 1; counter <= 4; counter++ )
			BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( SaveAnalyserStamps.BodyOffset - counter * 4, 4 ), -1 );
		for ( var extreme = 0; extreme < 3; extreme++ )
			BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( SaveAnalyserStamps.BodyOffset + SaveAnalyserStamps.Bytes + extreme * 4, 4 ), -1 );
		var stamps = Read( body );
		Assert.AreEqual( (11L, -22L, long.MinValue, long.MaxValue), (stamps.LastStrike, stamps.LastPuke, stamps.LastPrank, stamps.ParkLastOpened), "mLastStrike, mLastPuke, mLastPrank, mParkLastOpened" );
		Assert.AreEqual( (Stamp( 1000 ), Stamp( 400 )), (stamps.LastObjectBuilt, stamps.LastStaffHired), "mLastObjectBuilt is the fifth, mLastStaffHired the sixth" );
	}

	[TestMethod]
	public void AnAnalyserWithoutItsStampsOrAnotherThingIsRefused()
	{
		var body = SaveNewParkThings.ParkAnalyser( 0, 0 );
		var shortened = body.AsSpan( 0, SaveAnalyserStamps.BodyOffset + SaveAnalyserStamps.Bytes - 1 ).ToArray();
		Assert.ThrowsException<InvalidDataException>( () => Read( shortened ) );
		Assert.AreEqual( new SaveAnalyserStamps( Start, Start, Start, Start, Start, Start ), Read( body.AsSpan( 0, SaveAnalyserStamps.BodyOffset + SaveAnalyserStamps.Bytes ).ToArray() ), "the stamps need no more of the body" );
		Assert.ThrowsException<InvalidDataException>( () => SaveAnalyserStamps.Read( body, Thing( body.Length, SaveThingModel.Weather ) ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveAnalyserStamps.Read( body.AsSpan( 0, 100 ), Thing( body.Length ) ) );
		// a byte ring of another size would move the stamps (0x4CEA00 writes as many entries as its mNumEntries holds)
		for ( var ring = 0; ring < SaveAnalyserStamps.ByteRings; ring += 19 )
		{
			var resized = SaveNewParkThings.ParkAnalyser( 0, 0 );
			var capacity = SaveArrivalRing.BodyOffset + SaveArrivalRing.Bytes + ring * SaveAnalyserStamps.ByteRingBytes + 4;
			Assert.AreEqual( SaveAnalyserStamps.ByteRingCapacity, BinaryPrimitives.ReadInt32LittleEndian( resized.AsSpan( capacity, 4 ) ) );
			BinaryPrimitives.WriteInt32LittleEndian( resized.AsSpan( capacity, 4 ), SaveAnalyserStamps.ByteRingCapacity - 1 );
			Assert.ThrowsException<InvalidDataException>( () => Read( resized ), $"byte ring {ring}" );
		}
	}

	[TestMethod]
	public void TheGuardChaseCountersAreTheThreeWordsBeforeTheLifetimeVisitorsAndTheStamps()
	{
		var body = Analyser( Start, Start, Start, Start, Start, Start );
		// mMisbehavingKids, mMisbehavingKidsThatGotAwayWithIt, mMisbehavingKidsEver and then mLifetimeVisitors (another word, not read here)
		foreach ( var (word, value) in new[] { (0, 7), (1, -3), (2, 1_000_000), (3, 99) } )
			BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( SaveAnalyserStamps.BodyOffset - 16 + word * 4, 4 ), value );
		Assert.AreEqual( new SaveAnalyserCounters( 7, -3, 1_000_000 ), SaveAnalyserCounters.Read( body, Thing( body.Length ) ) );
		Assert.AreEqual( new SaveAnalyserCounters( 0, 0, 0 ), SaveAnalyserCounters.Read( SaveNewParkThings.ParkAnalyser( 0, 0 ), Thing( SaveNewParkThings.ParkAnalyser( 0, 0 ).Length ) ), "a new park's analyser counts nothing (the constructor 0x4C5BBE)" );
		Assert.ThrowsException<InvalidDataException>( () => SaveAnalyserCounters.Read( body, Thing( body.Length, SaveThingModel.Weather ) ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveAnalyserCounters.Read( body.AsSpan( 0, 100 ), Thing( body.Length ) ) );
	}

	[TestMethod]
	public void TheImportKeepsTheSavedGuardChaseCountersForTheAdvisorsPrankLines()
	{
		var settings = OriginalSaveImportTests.Settings();
		var save = Save( settings, Start, Start, counters: new[] { 4, 2, 9 } );
		Assert.AreEqual( new SaveAnalyserCounters( 4, 2, 9 ), save.AnalyserCounters );
		var (economy, import) = Imported( save, settings );
		var state = new EconomyParkState( economy );
		Assert.AreEqual( (4, 2), (state.MisbehavingKids( false ), state.MisbehavingKids( true )) );
		Assert.AreEqual( 9, economy.MisbehavingKidsEver );
		Assert.IsTrue( import.Evidence.Any( line => line.StartsWith( "Park analyser: the guards' chase counters imported (this month 4, got away 2, ever 9)", StringComparison.Ordinal ) ) );
		// a save that names no analyser has none
		var (plain, _) = Imported( Save( settings, Start, Start, analyser: false ), settings );
		Assert.AreEqual( (0, 0), (new EconomyParkState( plain ).MisbehavingKids( false ), new EconomyParkState( plain ).MisbehavingKids( true )) );
	}

	[TestMethod]
	public void TheImportTakesWholeTurnsFromTheSaveCalendarsStart()
	{
		var settings = OriginalSaveImportTests.Settings();
		foreach ( var start in new[] { Start, Start + 7 * UnitsPerTurn + 12_345 } )
		{
			var save = Save( settings, Stamp( 1000, start ), Stamp( 400, start ), start );
			Assert.AreEqual( (Stamp( 1000, start ), Stamp( 400, start )), (save.AnalyserStamps!.Value.LastObjectBuilt, save.AnalyserStamps.Value.LastStaffHired) );
			var (economy, import) = Imported( save, settings );
			Assert.AreEqual( (long)SavedTurn, economy.Turn );
			Assert.AreEqual( (1000L, 400L), import.AnalyserStampTurns, $"calendar start {start}" );
			Assert.IsTrue( import.Evidence.Any( line => line.StartsWith( "Park analyser: the stamps of the newest construction, mLastObjectBuilt (turn 1000), and of the newest hire, mLastStaffHired (turn 400)", StringComparison.Ordinal ) ) );
			Assert.IsFalse( import.Evidence.Any( line => line.StartsWith( "Park analyser value adjusted", StringComparison.Ordinal ) ), "whole turns are taken as they are" );
		}
		var (_, plain) = Imported( Save( settings, Start, Start ), settings );
		Assert.AreEqual( (0L, 0L), plain.AnalyserStampTurns, "a park built and staffed at its start stamps turn 0" );
	}

	[TestMethod]
	public void AStampInsideATurnIsTakenAtThatTurnsStartAndReported()
	{
		var settings = OriginalSaveImportTests.Settings();
		var (_, import) = Imported( Save( settings, Stamp( 1000 ) + 1800L * 10_000_000, Stamp( 400 ) + 1 ), settings );
		Assert.AreEqual( (1000L, 400L), import.AnalyserStampTurns );
		var adjusted = import.Evidence.Where( line => line.StartsWith( "Park analyser value adjusted on import: ", StringComparison.Ordinal ) ).ToArray();
		Assert.AreEqual( 2, adjusted.Length, string.Join( "\n", adjusted ) );
		StringAssert.Contains( adjusted[0], "mLastObjectBuilt time stamp " + (Stamp( 1000 ) + 1800L * 10_000_000) + " lies 1800 park seconds into turn 1000, imported as that turn's start" );
		StringAssert.Contains( adjusted[1], "mLastStaffHired time stamp " );
		StringAssert.Contains( adjusted[1], "into turn 400" );
		// counting from the turn's start can add the day the stamp's seconds cross: 3,700 seconds into a turn 24 turns (90,000 seconds) back
		var crossing = Stamp( SavedTurn - 24 ) + 3700L * 10_000_000;
		var (economy, late) = Imported( Save( settings, crossing, Stamp( 400 ) ), settings );
		Assert.AreEqual( (SavedTurn - 24L, 400L), late.AnalyserStampTurns );
		Assert.AreEqual( (1, 0L), (new EconomyParkState( economy, late.AnalyserStampTurns!.Value.ObjectTurn, 400 ).DaysSinceObjectStamp, OriginalDays( SavedTurn, crossing )) );
	}

	[TestMethod]
	public void AStampOutsideTheCalendarKeepsItsWholeTurnAsTheOriginalCountsFromIt()
	{
		var settings = OriginalSaveImportTests.Settings();
		// a stamp past the park's turn keeps its turn, and one before the calendar's start (here the year 1601) a negative one, rounded down
		var (economy, import) = Imported( Save( settings, Stamp( 5000 ), 0 ), settings );
		var hireTurn = (long)BigInteger.Divide( new BigInteger( 0 ) - Start - (UnitsPerTurn - 1), UnitsPerTurn );
		Assert.AreEqual( -3_357_643L, hireTurn );
		Assert.AreEqual( (5000L, hireTurn), import.AnalyserStampTurns );
		var park = new EconomyParkState( economy, import.AnalyserStampTurns!.Value.ObjectTurn, import.AnalyserStampTurns.Value.HireTurn );
		Assert.AreEqual( -92, park.DaysSinceObjectStamp, "a stamp still to come counts negative days, divided signed toward zero" );
		Assert.AreEqual( OriginalDays( SavedTurn, Stamp( 5000 ) ), park.DaysSinceObjectStamp );
		Assert.AreEqual( (int)((SavedTurn - hireTurn) * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerDay), park.DaysSinceHireStamp );
		Assert.AreEqual( OriginalDays( SavedTurn, 0 ), park.DaysSinceHireStamp, "the 2850 seconds the stamp lies into its turn, which the import reports, cross no day here" );
		// any word a modified save holds converts without overflow
		foreach ( var wild in new[] { long.MinValue, long.MaxValue, -1L } )
		{
			var (_, extreme) = Imported( Save( settings, wild, wild ), settings );
			var expected = (long)BigInteger.Divide( new BigInteger( wild ) - Start - (wild < Start ? UnitsPerTurn - 1 : 0), UnitsPerTurn );
			Assert.AreEqual( (expected, expected), extreme.AnalyserStampTurns, $"stamp {wild}" );
		}
	}

	[TestMethod]
	public void AStampIsTheTurnOfTheSaveCalendarsOwnRate()
	{
		var settings = OriginalSaveImportTests.Settings();
		// the park time of a turn is the start plus floor(turn x rate / 4) seconds (0x4F8690); only a modified save has a rate other than 15000
		long StampAt( long turn, int rate ) => Start + turn * rate / 4 * 10_000_000;
		foreach ( var rate in new[] { 12_000, 15_001, 1 } )
		{
			var (economy, import) = Imported( Save( settings, StampAt( 1000, rate ), StampAt( 401, rate ), rate: rate ), settings );
			// below a rate of 4 several turns share a park second (400 to 403 at rate 1), and the first of them is taken
			Assert.AreEqual( (1000L, rate == 1 ? 400L : 401L), import.AnalyserStampTurns, $"rate {rate}" );
			Assert.IsFalse( import.Evidence.Any( line => line.StartsWith( "Park analyser value adjusted", StringComparison.Ordinal ) ), $"rate {rate}" );
			// the days count on the park's own calendar of 3,750 seconds a turn, as its dates do, and the import reports that the save's calendar does not agree
			Assert.AreEqual( (int)((SavedTurn - 1000L) * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerDay), new EconomyParkState( economy, 1000, 401 ).DaysSinceObjectStamp, $"rate {rate}" );
			Assert.IsTrue( import.Evidence.Any( line => line.StartsWith( $"Park turn {SavedTurn} imported", StringComparison.Ordinal ) && line.Contains( $"(rate {rate}," ) && line.EndsWith( "does not agree with that date.", StringComparison.Ordinal ) ), $"rate {rate}" );
		}
		// a stamp between two turns' times takes the earlier turn at any rate
		var (_, between) = Imported( Save( settings, StampAt( 1000, 15_001 ) + 10_000_000, StampAt( 401, 15_001 ), rate: 15_001 ), settings );
		Assert.AreEqual( (1000L, 401L), between.AnalyserStampTurns );
		Assert.IsTrue( between.Evidence.Any( line => line.StartsWith( "Park analyser value adjusted on import: mLastObjectBuilt", StringComparison.Ordinal ) && line.Contains( "lies 1 park seconds into turn 1000" ) ) );
		// the World reader refuses a calendar without a positive rate before any stamp is converted
		Assert.ThrowsException<InvalidDataException>( () => Save( settings, Stamp( 1000 ), Stamp( 400 ), rate: 0 ) );
	}

	[TestMethod]
	public void ASaveThatNamesNoAnalyserHasNoStamps()
	{
		var settings = OriginalSaveImportTests.Settings();
		var save = Save( settings, Stamp( 1000 ), Stamp( 400 ), analyser: false );
		Assert.IsNull( save.AnalyserStamps );
		var (_, import) = Imported( save, settings );
		Assert.IsNull( import.AnalyserStampTurns );
		Assert.IsFalse( import.Evidence.Any( line => line.StartsWith( "Park analyser: the stamps", StringComparison.Ordinal ) ) );
	}

	[TestMethod]
	public void AnOriginalSaveParksAdvisorTakesTheSavedStampsNotTheNewestStandingObject()
	{
		var settings = OriginalSaveImportTests.Settings();
		var save = Save( settings, Stamp( 1000 ), Stamp( 400 ) );
		var (economy, import) = Imported( save, settings );
		// the newest construction was demolished: what still stands was built earlier, and nobody is employed
		economy.RegisterExisting( 1100, ParkCalendar.TickOfTurn( 300 ) );
		Assert.AreEqual( (300L, 0L), EconomyParkState.NewestStamps( economy ) );
		using var advisor = Advisor();
		advisor.AttachLevel( LevelWith( save, ImportedRuntime( economy, import ) ) );
		var snapshot = advisor.CaptureSnapshot();
		Assert.AreEqual( (1000L, 400L), (snapshot.ObjectStampTurn!.Value, snapshot.HireStampTurn!.Value), "the analyser's saved words, not the derivation" );
		// the days the scan's callbacks read are the original's count from the saved words
		var park = new EconomyParkState( economy, snapshot.ObjectStampTurn, snapshot.HireStampTurn );
		Assert.AreEqual( 81, park.DaysSinceObjectStamp );
		Assert.AreEqual( OriginalDays( SavedTurn, Stamp( 1000 ) ), park.DaysSinceObjectStamp );
		Assert.AreEqual( OriginalDays( SavedTurn, Stamp( 400 ) ), park.DaysSinceHireStamp );
		// a build in play moves the stamp on as before
		EconomyTestData.AdvanceToTurn( economy, 2900 );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1100, out _ ) );
		Assert.AreEqual( 2900L, advisor.CaptureSnapshot().ObjectStampTurn );
		Assert.AreEqual( 400L, advisor.CaptureSnapshot().HireStampTurn );
	}

	[TestMethod]
	public void AnOpenTpwSnapshotKeepsPriorityAndAResumedParkWithoutStampsDerivesThem()
	{
		var settings = OriginalSaveImportTests.Settings();
		var save = Save( settings, Stamp( 1000 ), Stamp( 400 ) );
		var (economy, import) = Imported( save, settings );
		economy.RegisterExisting( 1100, ParkCalendar.TickOfTurn( 300 ) );
		using var advisor = Advisor();
		var level = LevelWith( save, ImportedRuntime( economy, import ) );
		advisor.AttachLevel( level );
		var snapshot = advisor.CaptureSnapshot();
		advisor.RestoreSnapshot( snapshot with { ObjectStampTurn = 55, HireStampTurn = 66 } );
		Assert.AreEqual( (55L, 66L), (advisor.CaptureSnapshot().ObjectStampTurn!.Value, advisor.CaptureSnapshot().HireStampTurn!.Value), "the snapshot of an OpenTPW save comes after the attach" );
		advisor.AttachLevel( level );
		advisor.RestoreSnapshot( snapshot with { ObjectStampTurn = null, HireStampTurn = null } );
		Assert.AreEqual( (1000L, 400L), (advisor.CaptureSnapshot().ObjectStampTurn!.Value, advisor.CaptureSnapshot().HireStampTurn!.Value), "a snapshot without stamps leaves the attach's" );
		// a resumed OpenTPW save has no import: the park that came from the same original save derives its stamps when its snapshot holds none
		advisor.AttachLevel( LevelWith( save, ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation ) ) );
		advisor.RestoreSnapshot( snapshot with { ObjectStampTurn = null, HireStampTurn = null } );
		Assert.AreEqual( (300L, 0L), (advisor.CaptureSnapshot().ObjectStampTurn!.Value, advisor.CaptureSnapshot().HireStampTurn!.Value) );
	}

	[TestMethod]
	public void ASnapshotThatHoldsOnlyOneStampIsRefused()
	{
		using var advisor = Advisor();
		advisor.AttachEconomy( EconomyTestData.Park() );
		var snapshot = advisor.CaptureSnapshot();
		advisor.ValidateSnapshot( snapshot with { ObjectStampTurn = null, HireStampTurn = null } );
		foreach ( var half in new[] { snapshot with { HireStampTurn = null }, snapshot with { ObjectStampTurn = null } } )
		{
			Assert.ThrowsException<InvalidDataException>( () => advisor.ValidateSnapshot( half ) );
			Assert.ThrowsException<InvalidDataException>( () => advisor.RestoreSnapshot( half ) );
		}
	}

	[TestMethod]
	public void EveryOriginalSavesStampsAreWholeTurnsOfItsCalendarAtOrBeforeItsTurn()
	{
		var folder = Environment.GetEnvironmentVariable( "OPENTPW_TPWS_SAVES" );
		if ( string.IsNullOrWhiteSpace( folder ) || !Directory.Exists( folder ) )
			Assert.Inconclusive( "Set OPENTPW_TPWS_SAVES to a folder with original .TPWS/.INTS saves in per-theme subfolders." );
		var files = Directory.EnumerateFiles( folder!, "*", SearchOption.AllDirectories )
			.Where( file => Path.GetExtension( file ).ToUpperInvariant() is ".TPWS" or ".INTS" )
			.Select( file => (Path: file, Theme: Path.GetFileName( Path.GetDirectoryName( file ) )!.ToLowerInvariant()) )
			.Where( file => Themes.Contains( file.Theme ) ).OrderBy( file => file.Path, StringComparer.Ordinal ).ToList();
		if ( files.Count == 0 )
			Assert.Inconclusive( "The OPENTPW_TPWS_SAVES folder has no .TPWS/.INTS files in a theme subfolder." );
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		originalFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		initialized = true;
		var summary = new List<string>();
		var played = 0;
		foreach ( var (path, theme) in files )
		{
			var name = $"{theme}/{Path.GetFileName( path )}";
			using var reader = new SaveReader( path );
			var payload = reader.ReadFile();
			var map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( $"levels/{theme}/terrain.wad", "base.map" ) ) );
			var park = OriginalParkImport.Import( payload, map );
			Assert.IsNotNull( park.AnalyserStamps, name );
			var stamps = park.AnalyserStamps!.Value;
			var start = park.World.Calendar.FunnyTimeStart;
			var turn = (long)park.World.Variables.GameTick;
			var turns = new[] { stamps.LastStrike, stamps.LastPuke, stamps.LastPrank, stamps.ParkLastOpened, stamps.LastObjectBuilt, stamps.LastStaffHired }.Select( stamp =>
			{
				Assert.AreEqual( 0L, (stamp - start) % UnitsPerTurn, $"{name}: stamp {stamp} is a whole turn from the calendar's start {start}" );
				var stampTurn = (stamp - start) / UnitsPerTurn;
				Assert.IsTrue( stampTurn >= 0 && stampTurn <= turn, $"{name}: stamp turn {stampTurn} lies within the park's turns 0 to {turn}" );
				return stampTurn;
			} ).ToArray();
			if ( turns[4] > 0 || turns[5] > 0 )
				played++;
			// the economy import hands the advisor the object and hire turns; what standing objects and employed staff would give is printed beside them
			var economy = new ParkEconomy( BalanceSettings.Load( theme, easy: false ), EconomyObjectCatalog.Load( theme, easy: false ), ParkGameMode.FullSimulation, 1 );
			var import = OriginalEconomyImport.Apply( economy, park );
			Assert.AreEqual( (turns[4], turns[5]), import.AnalyserStampTurns, name );
			Assert.IsFalse( import.Evidence.Any( line => line.StartsWith( "Park analyser value adjusted", StringComparison.Ordinal ) ), name );
			summary.Add( $"{name}: turn {turn}, stamps strike/puke/prank/opened/object/hire at turns {string.Join( "/", turns )}; the newest standing object and employed member at turns {EconomyParkState.NewestStamps( economy )}" );
		}
		Console.WriteLine( string.Join( "\n", summary ) );
		Assert.IsTrue( played > 0, "a played original park has built or hired after its start:\n" + string.Join( "\n", summary ) );
	}
}
