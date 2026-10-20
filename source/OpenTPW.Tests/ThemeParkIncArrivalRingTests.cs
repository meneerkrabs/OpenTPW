using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Independent complete TPI payloads and bounded arrival-history readiness; no original assets or full TPI resume.</summary>
[TestClass, DoNotParallelize]
public class ThemeParkIncArrivalRingTests
{
	private const int Months = 144, Recent = 50;
	private static readonly int[] Selected = { 14, 17, 18, 19, 20 };
	private BaseFileSystem previousFiles = null!;
	private Logger previousLog = null!;
	private GameEdition previousEdition;
	private IReadOnlyList<CatalogContentRoot> previousRoots = null!;
	private string? root;

	[TestInitialize]
	public void Initialize()
	{
		previousFiles = FileSystem; previousLog = Log; previousEdition = GameEditions.Current;
		previousRoots = ObjectCatalog.ContentRoots;
		ObjectCatalog.SetContentRoots( Array.Empty<CatalogContentRoot>() );
		Log = new();
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previousFiles; Log = previousLog; GameEditions.Select( previousEdition );
		ObjectCatalog.SetContentRoots( previousRoots );
		if ( root != null ) Directory.Delete( root, true );
	}

	private sealed record BodyFixture( byte[] Bytes, int[] Shorts, int[] Monthly, int RecentOffset, int ArrivalOffset, int[] ByteRings, int Tail );
	private static void Word( byte[] bytes, int offset, int value ) => BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( offset, 4 ), value );
	private static void Short( byte[] bytes, int offset, ushort value ) => BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( offset, 2 ), value );
	private static string State( ParkStatisticsHistory history ) => JsonSerializer.Serialize( history.Capture() );
	private static ThemeParkIncSaveWorld World( byte[] payload ) => ThemeParkIncSaveWorld.Parse( payload, SavePayloadLayout.ParseThemeParkInc( payload ), 128, 128 );
	private static IReadOnlyList<int> Read( BodyFixture fixture ) => SaveThemeParkIncArrivalRing.Read( World( Payload( fixture.Bytes ) ) )!;
	private static OriginalParkImport Import( byte[] payload ) => OriginalParkImport.ImportThemeParkInc( payload, OriginalParkImportTests.CreateMap( 128, 128, ( _, _ ) => 0 ) );

	/// <summary>Written independently of ThemeParkIncBodyLayout, including all 21 monthly records and the complete scalar tail.</summary>
	private static BodyFixture Body( int[]? shortCounts = null, int[]? monthlyCounts = null, int recentCount = Recent, int arrivalCount = 5, int byteCount = 50 )
	{
		shortCounts ??= new[] { 3, 3, 3, 3 };
		monthlyCounts ??= Enumerable.Repeat( Months, 21 ).ToArray();
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		writer.Write( new byte[8 + 5 * 4 + 1024 * 61] );
		int EmptyRing( int width, int count )
		{
			var at = (int)stream.Position;
			writer.Write( -1 ); writer.Write( count ); writer.Write( (byte)0 );
			writer.Write( new byte[width * (count + 1)] );
			return at;
		}
		var shorts = shortCounts.Select( count => EmptyRing( 2, count ) ).ToArray();
		writer.Write( new byte[6 * 5] );
		var monthly = monthlyCounts.Select( count => EmptyRing( 4, count ) ).ToArray();
		var recent = EmptyRing( 4, recentCount );
		var arrival = EmptyRing( 4, arrivalCount );
		var bytes = Enumerable.Range( 0, 22 ).Select( _ => EmptyRing( 1, byteCount ) ).ToArray();
		var tail = (int)stream.Position;
		writer.Write( new byte[92] );
		return new( stream.ToArray(), shorts, monthly, recent, arrival, bytes, tail );
	}

	private static void Ring( BodyFixture body, int offset, int capacity, int head, bool wrapped, int temp, Func<int, int> value )
	{
		Word( body.Bytes, offset, head ); Word( body.Bytes, offset + 4, capacity );
		body.Bytes[offset + 8] = wrapped ? (byte)1 : (byte)0; Word( body.Bytes, offset + 9, temp );
		for ( var slot = 0; slot < capacity; slot++ ) Word( body.Bytes, offset + 13 + 4 * slot, value( slot ) );
	}

	private static BodyFixture Distinct( int[]? shortCounts = null, int[]? monthlyCounts = null )
	{
		var body = Body( shortCounts, monthlyCounts );
		for ( var ordinal = 0; ordinal < 21; ordinal++ )
		{
			var capacity = monthlyCounts?[ordinal] ?? Months;
			Ring( body, body.Monthly[ordinal], capacity, capacity == 0 ? -1 : 0, false, 100 + ordinal, slot => 1000 * ordinal + slot );
		}
		Ring( body, body.Monthly[14], Months, 1, false, -101, slot => slot == 0 ? -11 : slot == 1 ? 23 : 14000 + slot );
		Ring( body, body.Monthly[17], Months, 0, true, 17, slot => 17000 + slot );
		Ring( body, body.Monthly[18], Months, 2, false, unchecked( (int)0x7FC00001 ), slot => slot < 3
			? BitConverter.SingleToInt32Bits( new[] { 61.5f, -2.25f, 110.75f }[slot] ) : unchecked( (int)0x7FC00001 ) );
		Ring( body, body.Monthly[19], Months, 143, false, -104, slot => slot - 5 );
		Ring( body, body.Monthly[20], Months, 7, true, -105, slot => 20000 + slot );
		Ring( body, body.RecentOffset, Recent, 1, false, -106, slot => slot == 0 ? 2 : slot == 1 ? 5 : 9900 + slot );
		Word( body.Bytes, body.Tail + 16, 987654 ); // The separate admissions scalar must never replace bus-arrivals temp.
		return body;
	}

	/// <summary>A complete TPI payload with a bank, optional analyser and following UI receiver, each in its actual chain framing.</summary>
	private static byte[] Payload( byte[]? body, ushort reference = 41, bool duplicate = false, int rate = 15000 )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		var variables = new byte[70]; Word( variables, 0, 3 ); Short( variables, 10, 7 );
		Word( variables, 14, 76543 ); Short( variables, 20, reference ); Word( variables, 38, 0x12345678 );
		writer.Write( variables ); writer.Write( new byte[150 * 36 + 4 + 2] ); writer.Write( new byte[32 * 31 + 6 * 5 + 4 + 1] );
		writer.Write( SaveCalendar.InitialFunnyTimeStart ); writer.Write( 1234L );
		writer.Write( 2007 ); writer.Write( 8 ); writer.Write( 17 ); writer.Write( rate );
		writer.Write( new byte[18] ); writer.Write( new byte[128 * 128] ); // Each status-zero cell has no optional records.
		var things = new List<(int Id, ThemeParkIncThingModel Model, byte[] Body)> { (7, ThemeParkIncThingModel.Bank, SaveTestPayloads.Bank( balance: 123456, fee: 91 )) };
		if ( body != null ) things.Add( (41, ThemeParkIncThingModel.ParkAnalyser, body) );
		if ( duplicate ) things.Add( (42, ThemeParkIncThingModel.ParkAnalyser, Body().Bytes) );
		things.Add( (99, ThemeParkIncThingModel.UIMsgReceiver, new byte[8]) );
		writer.Write( things[0].Id ); writer.Write( "EATC"u8 );
		for ( var index = 0; index < things.Count; index++ )
		{
			writer.Write( index + 1 < things.Count ? things[index + 1].Id : 0 ); writer.Write( (int)things[index].Model );
			writer.Write( "LDMT"u8 ); writer.Write( Encoding.ASCII.GetBytes( ThemeParkIncSaveWorld.Tags[things[index].Model] ) ); writer.Write( things[index].Body );
		}
		writer.Write( "ENOD"u8 ); writer.Write( 0 );
		return SaveTestPayloads.Payload( stream.ToArray(), blocks: new Dictionary<SaveBlock, byte[]>
		{
			[SaveBlock.MessageCentre] = new byte[4 + 45 * 4], [SaveBlock.Camera] = new byte[48],
		} )[..^4]; // TPI has no trailing UI block.
	}

	private static void Arrival( BodyFixture body, int head, bool wrapped, params int[] slots ) =>
		Ring( body, body.ArrivalOffset, slots.Length, head, wrapped, 87654321, slot => slots[slot] );

	[TestMethod]
	public void EveryHeadAndWrapReturnsOnlyActiveSignedGroupsOldestFirst()
	{
		var words = new[] { int.MinValue, -17, 0, int.MaxValue, -1 };
		foreach ( var wrapped in new[] { false, true } )
			for ( var head = wrapped ? 0 : -1; head < 5; head++ )
			{
				var body = Body(); Arrival( body, head, wrapped, words );
				var payload = Payload( body.Bytes ); var before = payload.ToArray();
				var expected = wrapped ? words.Skip( head + 1 ).Concat( words.Take( head + 1 ) ).ToArray() : words.Take( head + 1 ).ToArray();
				var actual = SaveThemeParkIncArrivalRing.Read( World( payload ) )!;
				CollectionAssert.AreEqual( expected, actual.ToArray(), $"head {head}, wrapped {wrapped}" );
				CollectionAssert.AreEqual( expected, Import( payload ).ArrivalGroups!.ToArray() );
				CollectionAssert.AreEqual( before, payload, "No metadata, scratch or inactive bytes are normalized in place." );
				Assert.IsFalse( actual.Contains( 87654321 ), "mTemp is not an extra group or a held-count field." );
				if ( head == -1 )
					Assert.AreEqual( uint.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian( body.Bytes.AsSpan( body.ArrivalOffset ) ) );
			}
		var empty = Body(); Arrival( empty, -1, false, -1, int.MinValue, int.MaxValue, 99, -99 );
		Assert.AreEqual( 0, Read( empty ).Count, "Every inactive slot may contain arbitrary bits." );
		Assert.AreEqual( SaveThemeParkIncArrivalRing.Capacity, AdvisorArrivalRing.Capacity );
		Assert.ThrowsException<ArgumentNullException>( () => SaveThemeParkIncArrivalRing.Read( null! ) );
	}

	[TestMethod]
	public void EarlierVariableCountsMoveTheNamedRingAndNeverSelectAMonthlyOrScratchValue()
	{
		var monthly = Enumerable.Repeat( Months, 21 ).ToArray();
		foreach ( var ordinal in Enumerable.Range( 0, 21 ).Except( Selected ) ) monthly[ordinal] = ordinal % 3;
		var standard = Distinct(); Arrival( standard, 1, true, 11, -22, 33, -44, 55 );
		foreach ( var shortCounts in new[] { new[] { 0, 1, 2, 3 }, new[] { 3, 0, 1, 2 }, new[] { 1, 2, 3, 0 } } )
		{
			var shifted = Distinct( shortCounts, monthly ); Arrival( shifted, 1, true, 11, -22, 33, -44, 55 );
			Assert.AreNotEqual( standard.ArrivalOffset, shifted.ArrivalOffset );
			var imported = Import( Payload( shifted.Bytes ) );
			CollectionAssert.AreEqual( Read( standard ).ToArray(), imported.ArrivalGroups!.ToArray() );
			Assert.AreEqual( 17, imported.Statistics!.ArrivalsThisMonth, "Monthly bus-arrivals temp is distinct from arrival groups." );
			Assert.AreEqual( 987654u, imported.ThemeParkIncWorld!.ParkAnalyser!.VisitorsThisMonth );
		}
		foreach ( var recent in new[] { 0, 1, 17, 49 } )
		{
			var shifted = Body( recentCount: recent ); Arrival( shifted, 1, true, 11, -22, 33, -44, 55 );
			CollectionAssert.AreEqual( Read( standard ).ToArray(), Read( shifted ).ToArray(), "The codec follows a changed recent-stays count." );
			var error = Assert.ThrowsException<InvalidDataException>( () => Import( Payload( shifted.Bytes ) ) );
			StringAssert.Contains( error.Message, "recent stays capacity" );
			StringAssert.Contains( error.Message, "unsupported by the fixed host history profile" );
		}
		foreach ( var selected in Selected )
		{
			var counts = Enumerable.Repeat( Months, 21 ).ToArray(); counts[selected] = 2;
			var shifted = Body( monthlyCounts: counts ); Arrival( shifted, 1, false, -1, 7, 99, 88, 77 );
			CollectionAssert.AreEqual( new[] { -1, 7 }, Read( shifted ).ToArray() );
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Import( Payload( shifted.Bytes ) ) ).Message,
				"unsupported by the fixed host history profile", "The statistics import keeps its selected-capacity policy." );
		}
	}

	[TestMethod]
	public void SmallerNativeCapacitiesAreStructurallySupportedButNotTheFixedHostProfile()
	{
		for ( var capacity = 0; capacity < 5; capacity++ )
			foreach ( var (head, wrapped) in capacity == 0 ? new[] { (-1, false) } : new[] { (-1, false), (capacity - 1, false), (0, true) } )
			{
				var body = Body( arrivalCount: capacity );
				Arrival( body, head, wrapped, Enumerable.Range( 10, capacity ).ToArray() );
				var world = World( Payload( body.Bytes ) );
				Assert.AreEqual( capacity, world.ParkAnalyser!.Ring5.Entries.Count );
				Assert.AreEqual( body.Bytes.Length, world.BodyOf( world.Find( 41 )!.Value ).Length );
				var error = Assert.ThrowsException<InvalidDataException>( () => SaveThemeParkIncArrivalRing.Read( world ) );
				StringAssert.Contains( error.Message, "structurally supported but unsupported by the fixed host history profile (requires 5)" );
				StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Import( Payload( body.Bytes ) ) ).Message, "arrival capacity" );
			}
	}

	[TestMethod]
	public void MalformedHeadRawWrapAndCountsAreRejectedEvenWhenTheTypedFlagLooksTrue()
	{
		foreach ( var (head, flag) in new[] { (int.MinValue, 0), (-2, 0), (5, 0), (int.MaxValue, 0), (-1, 1), (0, 2), (0, 255) } )
		{
			var body = Body(); Word( body.Bytes, body.ArrivalOffset, head ); body.Bytes[body.ArrivalOffset + 8] = (byte)flag;
			var world = World( Payload( body.Bytes ) );
			if ( flag > 1 ) Assert.IsTrue( world.ParkAnalyser!.Ring5.WrappedAround, "The typed flag alone would accept this malformed byte." );
			Assert.ThrowsException<InvalidDataException>( () => SaveThemeParkIncArrivalRing.Read( world ), $"head {head}, flag {flag}" );
			Assert.ThrowsException<InvalidDataException>( () => Import( Payload( body.Bytes ) ) );
		}
		foreach ( var count in new[] { 6, int.MaxValue, int.MinValue, -1 } )
		{
			var body = Body(); Word( body.Bytes, body.ArrivalOffset + 4, count );
			Assert.ThrowsException<InvalidDataException>( () => World( Payload( body.Bytes ) ), $"invalid raw count {unchecked((uint)count)}" );
		}
		var mutable = Body(); var payload = Payload( mutable.Bytes ); var parsed = World( payload );
		Word( payload, parsed.Find( 41 )!.Value.BodyOffset + mutable.ArrivalOffset + 4, 6 );
		Assert.ThrowsException<InvalidDataException>( () => SaveThemeParkIncArrivalRing.Read( parsed ), "The reader rechecks the exact body instead of trusting a cached typed ring." );
	}

	[TestMethod]
	public void CompleteBodyBoundsNeverConsumeOrSearchPastTheFollowingThingHeader()
	{
		var body = Body(); Arrival( body, 4, true, 4, 3, 2, 1, 0 );
		foreach ( var length in new[] { 0, body.ArrivalOffset, body.ArrivalOffset + 4, body.ArrivalOffset + 8,
			body.ArrivalOffset + 12, body.ArrivalOffset + 13 + 3 * 4 + 2, body.ArrivalOffset + 33, body.Bytes.Length - 1 } )
		{
			Assert.ThrowsException<InvalidDataException>( () => new ThemeParkIncBody( ThemeParkIncThingModel.ParkAnalyser, body.Bytes[..length] ) );
			Assert.ThrowsException<InvalidDataException>( () => Import( Payload( body.Bytes[..length] ) ), $"truncated at {length}, immediately followed by a real header" );
		}
		var payload = Payload( body.Bytes ); var before = payload.ToArray(); var world = World( payload );
		var thing = world.Find( 41 )!.Value; var next = world.Find( 99 )!.Value;
		Assert.AreEqual( body.Bytes.Length, world.BodyOf( thing ).Length );
		Assert.AreEqual( thing.BodyOffset + body.Bytes.Length + 16, next.BodyOffset, "Exactly one next/model/LDMT/tag header separates the bodies." );
		CollectionAssert.AreEqual( "LDMT"u8.ToArray(), payload.AsSpan( next.BodyOffset - 8, 4 ).ToArray() );
		Assert.ThrowsException<InvalidDataException>( () => new ThemeParkIncBody( thing.Model, payload.AsMemory( thing.BodyOffset, thing.BodyLength + 16 ) ),
			"A following header must not enlarge the analyser's exact body." );
		CollectionAssert.AreEqual( new[] { 4, 3, 2, 1, 0 }, SaveThemeParkIncArrivalRing.Read( world )!.ToArray() );
		CollectionAssert.AreEqual( before, payload );
		foreach ( var offset in new[] { body.Shorts[0], body.Monthly[0], body.RecentOffset, body.ByteRings[0] } )
		{
			var damaged = body.Bytes.ToArray(); Word( damaged, offset + 4, int.MinValue );
			Assert.ThrowsException<InvalidDataException>( () => Import( Payload( damaged ) ), "A malformed earlier count or later tail cannot be ignored." );
		}
		// A convenient valid body/header later in the payload is never used to repair an invalid chain.
		var hidden = body.Bytes[..body.ArrivalOffset].Concat( body.Bytes ).ToArray();
		Assert.ThrowsException<InvalidDataException>( () => Import( Payload( hidden ) ) );
	}

	[TestMethod]
	public void OnlyTheReferencedTypedAnalyserCanSupplyHistoryAndResultsOwnTheirValues()
	{
		Assert.IsNull( SaveThemeParkIncArrivalRing.Read( World( Payload( null, reference: 0 ) ) ) );
		var decoy = Body(); decoy.Bytes[decoy.ArrivalOffset + 8] = 255;
		var world = World( Payload( decoy.Bytes, reference: 0 ) ); Assert.IsNotNull( world.ParkAnalyser );
		Assert.IsNull( SaveThemeParkIncArrivalRing.Read( world ), "Unreferenced semantic metadata is not validated or adopted." );
		var absent = Import( Payload( decoy.Bytes, reference: 0 ) ); Assert.IsNull( absent.ArrivalGroups ); Assert.IsNull( absent.Statistics );
		foreach ( var reference in new ushort[] { 7, 99, 999 } )
			Assert.ThrowsException<InvalidDataException>( () => Import( Payload( Body().Bytes, reference ) ), "Wrong-model or absent reference." );
		Assert.ThrowsException<InvalidDataException>( () => Import( Payload( null ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Import( Payload( Body().Bytes, duplicate: true ) ) );
		var body = Body(); Arrival( body, 1, false, -9, int.MaxValue, 88, 77, 66 );
		var payload = Payload( body.Bytes ); var imported = Import( payload ); var result = imported.ArrivalGroups!;
		Array.Clear( payload, 0, payload.Length ); Array.Clear( body.Bytes, 0, body.Bytes.Length );
		CollectionAssert.AreEqual( new[] { -9, int.MaxValue }, result.ToArray(), "Neither source buffer aliases the returned history." );
		Assert.IsFalse( result is int[] );
		if ( result is IList<int> mutable ) Assert.ThrowsException<NotSupportedException>( () => mutable[0] = 123 );
	}

	private static void Set( object target, string name, object? value ) =>
		target.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	private static OriginalPark Source( OriginalParkImport? imported )
	{
		var source = (OriginalPark)RuntimeHelpers.GetUninitializedObject( typeof( OriginalPark ) );
		Set( source, "<LevelName>k__BackingField", "science" ); Set( source, "<Save>k__BackingField", imported );
		return source;
	}

	private ParkEconomyRuntime Runtime( OriginalPark source )
	{
		if ( root == null )
		{
			root = Path.Combine( Path.GetTempPath(), "opentpw-tpi-arrival-" + Guid.NewGuid().ToString( "N" ) );
			Directory.CreateDirectory( Path.Combine( root, "levels", "science" ) );
			File.WriteAllText( Path.Combine( root, "levels", "Standard.sam" ),
				"BankAccountInfo.InitialCash 50000\nBankAccountInfo.InitialAdmissionFee 25\n"
				+ "LoanInfo[0].LoanAmount 10000\nLoanInfo[0].APRInPercent 20\nLoanInfo[0].RepaymentPeriodInMonths 36\nLoanInfo[0].Lendername 0\n"
				+ "Costs.QueueCell 25\nCosts.PathCell 10\nCosts.KartTrackCell 40\nCosts.WaterTrackCell 50\nCosts.MapCell 75\n"
				+ "PeepInfo.ExcitementToCostDivisor 6\nPeepInfo.MinimumEntryFee 20\nPeepInfo.CheapPriceMultiplier 0.75\nPeepInfo.AveragePriceMultiplier 1.25\nPeepInfo.ExpensivePriceMultiplier 2\n" );
			File.WriteAllText( Path.Combine( root, "levels", "science", "Standard.sam" ), "" );
			FileSystem = new BaseFileSystem( root ); GameEditions.Select( GameEdition.ThemeParkInc );
		}
		return ParkEconomyRuntime.ForThemeParkIncLevel( source, new WorldSeed( 123 ) );
	}

	private static Level LevelWith( OriginalPark? source, ParkEconomyRuntime? runtime, GuestSimulation? guests = null )
	{
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		Set( level, "<OriginalPark>k__BackingField", source ); Set( level, "<Park>k__BackingField", runtime );
		Set( level, "<Guests>k__BackingField", guests );
		return level;
	}

	private static AutomaticAdvisor Advisor() => new( new AdvisorController( AdvisorSnapshotTests.Balance() ), () => 0 );

	private static OriginalParkImport TpwImport( BalanceSettings settings, int[]? groups = null )
	{
		var map = OriginalParkImportTests.CreateMap( 3, 3, ( _, _ ) => 0 );
		var analyser = SaveNewParkThings.ParkAnalyser( 0, 0 );
		if ( groups != null )
		{
			Word( analyser, SaveArrivalRing.BodyOffset, groups.Length - 1 );
			for ( var slot = 0; slot < groups.Length; slot++ ) Word( analyser, SaveArrivalRing.BodyOffset + 13 + slot * 4, groups[slot] );
		}
		var bank = SaveTestPayloads.Bank( loans: index =>
		{
			var offer = settings.Loans[index];
			return new[] { 0, (int)offer.Amount, offer.AprPercent, offer.Months,
				(int)LoanMath.MonthlyRepayment( offer.Amount, offer.AprPercent, offer.Months ), 0, 0, offer.LenderNameIndex };
		} );
		var things = SaveTestPayloads.Things( new[]
		{
			(8u, (uint)SaveThingModel.Bank, bank),
			(10u, (uint)SaveThingModel.ChallengeSystem, OriginalSaveImportTests.Challenges( settings )),
			(5u, (uint)SaveThingModel.ParkAnalyser, analyser),
		} );
		var variables = SaveTestPayloads.Variables( bank: 8 ); Short( variables, 20, 5 );
		var clock = new byte[8]; Word( clock, 4, 9876 );
		return OriginalParkImport.Import( SaveTestPayloads.Payload( SaveTestPayloads.World( variables, SaveTestPayloads.Grid( map ), things ),
			blocks: new Dictionary<SaveBlock, byte[]> { [SaveBlock.Clock] = clock } ), map );
	}

	[TestMethod]
	public void ActualFactoryAndManualAttachmentRetainBothHistoriesWithoutImportingNativeEconomyOrClock()
	{
		foreach ( var nativeRate in new[] { 15000, 30000 } )
		{
			var body = Distinct(); Arrival( body, 1, true, -10, 20, int.MinValue, 0, int.MaxValue );
			var imported = Import( Payload( body.Bytes, rate: nativeRate ) ); var source = Source( imported ); var runtime = Runtime( source );
			var economy = runtime.Economy; var control = Runtime( Source( null ) ).Economy;
			Assert.IsNull( runtime.Import ); Assert.IsFalse( runtime.Start.ImportShippedSave ); Assert.IsTrue( runtime.StartsOpen );
			Assert.AreEqual( EconomyRules.ThemeParkIncMoney, economy.Rules );
			Assert.AreEqual( 76543u, imported.ThemeParkIncWorld!.Variables.GameTick );
			Assert.AreEqual( nativeRate, imported.ThemeParkIncWorld.Calendar.FunnySecsPerRealSec );
			Assert.AreEqual( 0L, economy.Tick ); Assert.AreEqual( new DateTime( 2000, 1, 1 ), ParkCalendar.DateTimeAt( economy.Tick ) );
			Assert.AreEqual( 3750L, ParkCalendar.SecondsPerTurn ); Assert.AreEqual( control.Date, economy.Date );
			Assert.AreEqual( 50000L, economy.Balance ); Assert.AreEqual( 25, economy.EntranceFee );
			Assert.IsNull( imported.Advisor ); Assert.IsNull( imported.SavedAdvisorClockMilliseconds );
			Assert.AreEqual( 0, economy.Ledger.History.Count );
			Assert.AreEqual( 0, economy.Staff.Members.Count + economy.Staff.Candidates.Count + economy.Research.Items.Count + economy.Settings.Challenges.Count );
			var history = new ParkStatisticsHistory(); history.RestoreOriginal( imported.Statistics! );
			Assert.AreEqual( State( history ), State( economy.StatisticsHistory ) ); Assert.AreEqual( 17, economy.StatisticsHistory.ArrivalsThisMonth );
			Assert.AreEqual( JsonSerializer.Serialize( control.Ledger ), JsonSerializer.Serialize( economy.Ledger ) );
			Assert.AreEqual( WorldStateHash.Compute( new WorldStateSources { Economy = control } ), WorldStateHash.Compute( new WorldStateSources { Economy = economy } ) );
			var before = ParkSaveFile.Serialize( economy ); var hash = WorldStateHash.Compute( new WorldStateSources { Economy = economy } );
			using var advisor = Advisor(); advisor.AttachLevel( LevelWith( source, runtime ) );
			CollectionAssert.AreEqual( new[] { int.MinValue, 0, int.MaxValue, -10, 20 }, advisor.ArrivalWaves.ToArray() );
			Assert.AreEqual( 0u, advisor.CaptureSnapshot().AdvisorClockMilliseconds );
			Assert.AreEqual( before, ParkSaveFile.Serialize( economy ) ); Assert.AreEqual( hash, WorldStateHash.Compute( new WorldStateSources { Economy = economy } ) );
			Assert.IsNull( runtime.Import, "Readiness must not fabricate OriginalEconomyImport to pass a TPW guard." );
		}
		var capabilities = EditionCapabilities.ForThemeParkIncSlice();
		Assert.IsFalse( capabilities.Advisor || capabilities.ParkSaves || capabilities.Staff || capabilities.Research || capabilities.Challenges );
		Assert.IsTrue( capabilities.Money && capabilities.ExperimentalHud );
	}

	[TestMethod]
	public void KnownEmptyNativeRingOverridesAdmissionsAtAttachButLaterSnapshotPolicyIsUnchanged()
	{
		var source = Source( Import( Payload( Body().Bytes ) ) ); var runtime = Runtime( source );
		runtime.Economy.Counters.Add( ParkCounters.Admissions, 42 );
		using var advisor = Advisor(); var level = LevelWith( source, runtime ); advisor.AttachLevel( level );
		Assert.AreEqual( 0, advisor.ArrivalWaves.Count, "Native head -1 is authoritative after the attachment's legacy fallback." );
		var snapshot = advisor.CaptureSnapshot(); Assert.IsNotNull( snapshot.ArrivalWaves ); Assert.AreEqual( 0, snapshot.ArrivalWaves!.Count );
		foreach ( var waves in new IReadOnlyList<int>?[] { null, Array.Empty<int>() } )
		{
			advisor.RestoreSnapshot( snapshot with { ArrivalWaves = waves } );
			CollectionAssert.AreEqual( new[] { 0, 0, 0, 0, 0 }, advisor.ArrivalWaves.ToArray(), "The separate existing snapshot fallback remains for null AND empty with admissions." );
			advisor.AttachLevel( level ); Assert.AreEqual( 0, advisor.ArrivalWaves.Count );
		}
		advisor.RestoreSnapshot( snapshot with { ArrivalWaves = new[] { -7, 11 } } );
		CollectionAssert.AreEqual( new[] { -7, 11 }, advisor.ArrivalWaves.ToArray(), "A nonempty host snapshot remains authoritative at its later restore stage." );
		var fresh = Runtime( source ); advisor.AttachLevel( LevelWith( source, fresh ) ); advisor.RestoreSnapshot( snapshot );
		Assert.AreEqual( 0, advisor.ArrivalWaves.Count, "Without admissions the existing empty snapshot policy retains empty." );
	}

	[TestMethod]
	public void NoReferenceAbsentSourceWrongRulesAndReadOnlyLevelsDoNotAdoptTpiGroups()
	{
		var body = Body(); Arrival( body, 1, false, 19, -23, 999, 888, 777 );
		var real = Source( Import( Payload( body.Bytes ) ) );
		foreach ( var source in new OriginalPark?[] { null, Source( null ), Source( Import( Payload( null, reference: 0 ) ) ), Source( Import( Payload( body.Bytes, reference: 0 ) ) ) } )
		{
			var runtime = Runtime( source ?? Source( null ) ); runtime.Economy.Counters.Add( ParkCounters.Admissions, 1 );
			using var advisor = Advisor(); advisor.AttachLevel( LevelWith( source, runtime ) );
			CollectionAssert.AreEqual( new[] { 0, 0, 0, 0, 0 }, advisor.ArrivalWaves.ToArray(), "No recorded source retains the existing admissions heuristic." );
		}
		using var readOnly = Advisor(); readOnly.AttachLevel( LevelWith( real, null ) ); Assert.AreEqual( 0, readOnly.ArrivalWaves.Count );
		var tpw = ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation );
		using var wrongRules = Advisor(); wrongRules.AttachLevel( LevelWith( real, tpw ) ); Assert.AreEqual( 0, wrongRules.ArrivalWaves.Count );
		var otherRules = RecentResearchTests.Park( EconomyRules.ThemeParkIncMoney with { Staff = true } );
		using var nearMatch = Advisor(); nearMatch.AttachLevel( LevelWith( real, ParkEconomyRuntime.ForResume( otherRules, ParkStartKind.FullSimulation ) ) );
		Assert.AreEqual( 0, nearMatch.ArrivalWaves.Count, "A single ThemeParkInc flag is insufficient: the complete money-only rules must match." );
		var tpiWithWrongSource = Runtime( Source( null ) ); tpiWithWrongSource.Economy.Counters.Add( ParkCounters.Admissions, 1 );
		using var wrongSource = Advisor(); wrongSource.AttachLevel( LevelWith( Source( TpwImport( OriginalSaveImportTests.Settings() ) ), tpiWithWrongSource ) );
		CollectionAssert.AreEqual( new[] { 0, 0, 0, 0, 0 }, wrongSource.ArrivalWaves.ToArray(), "TPI money rules cannot authorize a TPW source's known-empty ring or clock." );
		Assert.AreEqual( 0u, wrongSource.CaptureSnapshot().AdvisorClockMilliseconds );
	}

	[TestMethod]
	public void FailedArrivalDecodePrecedesAnyLiveAdvisorStatisticsFinanceOrTickApplication()
	{
		var valid = Distinct(); Arrival( valid, 1, true, -5, 6, 7, 8, 9 );
		var source = Source( Import( Payload( valid.Bytes ) ) ); var runtime = Runtime( source ); var economy = runtime.Economy;
		economy.Ledger.Post( LedgerCategory.GateTakings, 57 ); economy.AdvanceFixedTick();
		using var advisor = Advisor(); advisor.AttachLevel( LevelWith( source, runtime ) );
		var before = ParkSaveFile.Serialize( economy ); var advisorBefore = JsonSerializer.Serialize( advisor.CaptureSnapshot() );
		var historyBefore = State( economy.StatisticsHistory ); var hash = WorldStateHash.Compute( new WorldStateSources { Economy = economy } );
		foreach ( var damage in new Action<BodyFixture>[]
		{
			body => Word( body.Bytes, body.ArrivalOffset, 5 ),
			body => body.Bytes[body.ArrivalOffset + 8] = 2,
			body => { Word( body.Bytes, body.ArrivalOffset, -1 ); body.Bytes[body.ArrivalOffset + 8] = 1; },
		} )
		{
			var damaged = Distinct(); damage( damaged ); var payload = Payload( damaged.Bytes ); var unchanged = payload.ToArray();
			Assert.IsNotNull( SaveThemeParkIncStatistics.Read( World( payload ) ), "The earlier statistics decoder succeeds before arrival metadata is refused." );
			OriginalParkImport? candidate = null;
			Assert.ThrowsException<InvalidDataException>( () =>
			{
				candidate = Import( payload );
				economy.StatisticsHistory.RestoreOriginal( candidate.Statistics! );
				advisor.AttachLevel( LevelWith( Source( candidate ), runtime ) );
			} );
			Assert.IsNull( candidate ); CollectionAssert.AreEqual( unchanged, payload );
			Assert.AreEqual( before, ParkSaveFile.Serialize( economy ) ); Assert.AreEqual( historyBefore, State( economy.StatisticsHistory ) );
			Assert.AreEqual( advisorBefore, JsonSerializer.Serialize( advisor.CaptureSnapshot() ) );
			Assert.AreEqual( hash, WorldStateHash.Compute( new WorldStateSources { Economy = economy } ) );
		}
		var wrongMap = OriginalParkImportTests.CreateMap( 128, 128, ( x, y ) => (byte)(x == 0 && y == 0 ? 1 : 0) );
		Assert.ThrowsException<InvalidDataException>( () => OriginalParkImport.ImportThemeParkInc( Payload( valid.Bytes ), wrongMap ), "Later normal import validation still runs after both decoders." );
		Assert.AreEqual( before, ParkSaveFile.Serialize( economy ) ); Assert.AreEqual( advisorBefore, JsonSerializer.Serialize( advisor.CaptureSnapshot() ) );
	}

	[TestMethod]
	public void NonemptySignedImportSurvivesAdvisorJsonAndASubsequentRealArrivalReplacesOnlyTheOldest()
	{
		// This tests a shared advisor-state seam by manual attachment, not a supported full TPI save/resume route.
		var body = Body(); var words = new[] { int.MinValue, -1, 0, int.MaxValue, -700 };
		Arrival( body, 4, false, words ); var source = Source( Import( Payload( body.Bytes ) ) );
		var runtime = Runtime( source );
		var guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 ) { ArrivalsEnabled = true };
		using var advisor = Advisor(); advisor.AttachLevel( LevelWith( source, runtime, guests ) );
		var captured = advisor.CaptureSnapshot(); var json = JsonSerializer.Serialize( captured );
		var saved = JsonSerializer.Deserialize<AutomaticAdvisor.AutomaticAdvisorSnapshot>( json )!;
		CollectionAssert.AreEqual( words, saved.ArrivalWaves!.ToArray() );
		using var restored = Advisor(); restored.AttachLevel( LevelWith( source, Runtime( source ), guests ) ); restored.RestoreSnapshot( saved );
		CollectionAssert.AreEqual( words, restored.ArrivalWaves.ToArray() );
		var observed = new List<int>(); guests.ArrivalGroupStarted += observed.Add;
		for ( var tick = 0; tick < 2000 && observed.Count == 0; tick++ ) guests.Tick( 1f / FixedStepClock.TicksPerSecond );
		Assert.AreEqual( 1, observed.Count, "A genuine simulation event, not a direct advisor-method call, appends the next group." );
		var expected = words.Skip( 1 ).Append( observed[0] ).ToArray();
		CollectionAssert.AreEqual( expected, advisor.ArrivalWaves.ToArray() ); CollectionAssert.AreEqual( expected, restored.ArrivalWaves.ToArray() );
		CollectionAssert.AreEqual( words, captured.ArrivalWaves!.ToArray(), "Capture owns its list; continuing either advisor cannot rewrite it." );
		CollectionAssert.AreEqual( words, source.Save!.ArrivalGroups!.ToArray(), "Continuing advisors cannot rewrite the original import." );
	}

	[TestMethod]
	public void TpwImportStillOverridesEmptyWhileFreshAndRestoredTpwKeepTheirExistingBoundary()
	{
		var settings = OriginalSaveImportTests.Settings(); var imported = TpwImport( settings );
		Assert.IsNull( imported.ThemeParkIncWorld ); Assert.IsNotNull( imported.ArrivalGroups ); Assert.AreEqual( 0, imported.ArrivalGroups!.Count );
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var originalImport = OriginalEconomyImport.Apply( economy, imported );
		// Exercise the existing TPW attachment gate with a genuine parsed/applied original import. TPI tests above always use its public factory.
		var runtime = (ParkEconomyRuntime)Activator.CreateInstance( typeof( ParkEconomyRuntime ), BindingFlags.Instance | BindingFlags.NonPublic,
			null, new object[] { economy, originalImport }, null )!;
		economy.Counters.Add( ParkCounters.Admissions, 42 );
		using var advisor = Advisor(); advisor.AttachLevel( LevelWith( Source( imported ), runtime ) );
		Assert.AreEqual( 0, advisor.ArrivalWaves.Count, "TPW imported native empty still overrides the admissions fallback." );
		Assert.AreEqual( imported.SavedAdvisorClockMilliseconds!.Value, advisor.CaptureSnapshot().AdvisorClockMilliseconds );
		foreach ( var source in new[] { Source( imported ), Source( null ) } )
		{
			advisor.AttachLevel( LevelWith( source, ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation ) ) );
			CollectionAssert.AreEqual( new[] { 0, 0, 0, 0, 0 }, advisor.ArrivalWaves.ToArray(), "No TPW import object means source history is not applied." );
			advisor.RestoreSnapshot( advisor.CaptureSnapshot() with { ArrivalWaves = new[] { -9, 11 }, AdvisorClockMilliseconds = 1234 } );
			CollectionAssert.AreEqual( new[] { -9, 11 }, advisor.ArrivalWaves.ToArray() ); Assert.AreEqual( 1234u, advisor.CaptureSnapshot().AdvisorClockMilliseconds );
		}
		// A genuinely fresh economy (no prior import or admissions) also rejects an unrelated nonempty original source.
		var fresh = ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation );
		using var freshAdvisor = Advisor();
		freshAdvisor.AttachLevel( LevelWith( Source( TpwImport( settings, new[] { 77, -8 } ) ), fresh ) );
		Assert.AreEqual( 0L, fresh.Economy.Tick ); Assert.AreEqual( 0, freshAdvisor.ArrivalWaves.Count );
		Assert.AreEqual( 0u, freshAdvisor.CaptureSnapshot().AdvisorClockMilliseconds, "Neither the source ring nor source clock is adopted without a TPW import." );
	}

	[TestMethod]
	public void LicensedTpiPrebuiltRingMatchesItsDynamicTypedFieldsWhenAvailable()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH for the optional licensed TPI prebuilt fixture; this test executes no original software." );
		var data = Directory.Exists( Path.Combine( path!, "Data" ) ) ? Path.Combine( path!, "Data" ) : path!;
		using var archive = new WadArchive( Path.Combine( data, "levels/science/terrain.wad" ) );
		var map = new MapFile( new MemoryStream( archive.GetFile( "base.map" )!.GetData() ) );
		using var reader = new SaveReader( Path.Combine( data, "PreBuilt", "prebuilt.TPWS" ) );
		var imported = OriginalParkImport.ImportThemeParkInc( reader.ReadFile(), map ); var world = imported.ThemeParkIncWorld!;
		Assert.IsTrue( world.Variables.ParkAnalyser != 0 ); var ring = world.ParkAnalyser!.Ring5;
		Assert.AreEqual( 5, ring.Entries.Count );
		var head = unchecked( (int)ring.CurrentEntry );
		var expected = ring.WrappedAround ? ring.Entries.Skip( head + 1 ).Concat( ring.Entries.Take( head + 1 ) ) : ring.Entries.Take( head + 1 );
		CollectionAssert.AreEqual( expected.Select( word => unchecked( (int)word ) ).ToArray(), imported.ArrivalGroups!.ToArray() );
		// Do not require populated/wrapped history: a shipped prebuilt may be entirely unplayed.
	}
}
