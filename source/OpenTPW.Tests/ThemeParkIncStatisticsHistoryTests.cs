using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Independent synthetic TPI analyser bytes, referenced import and fresh-clock host continuation; no original assets.</summary>
[TestClass, DoNotParallelize]
public class ThemeParkIncStatisticsHistoryTests
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

	private sealed record BodyFixture( byte[] Bytes, int[] Shorts, int[] Monthly, int RecentOffset, int[] ByteRings, int Tail );
	private static void Word( byte[] bytes, int offset, int value ) => BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( offset, 4 ), value );
	private static void Short( byte[] bytes, int offset, ushort value ) => BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( offset, 2 ), value );
	private static string State( ParkStatisticsHistory history ) => JsonSerializer.Serialize( history.Capture() );
	private static NativeStatisticsRing[] Rings( NativeParkStatistics data ) => new[] { data.People, data.Arrivals, data.Happiness, data.Stay, data.Rating, data.RecentStays };
	private static HistoryRing[] Rings( ParkStatisticsHistory data ) => new[] { data.People, data.Arrivals, data.Happiness, data.Stay, data.Rating, data.RecentStays };
	private static ThemeParkIncSaveWorld World( byte[] payload ) => ThemeParkIncSaveWorld.Parse( payload, SavePayloadLayout.ParseThemeParkInc( payload ), 128, 128 );
	private static NativeParkStatistics Read( BodyFixture fixture ) => SaveThemeParkIncStatistics.Read( World( Payload( fixture.Bytes ) ) )!;
	private static OriginalParkImport Import( byte[] payload ) => OriginalParkImport.ImportThemeParkInc( payload, OriginalParkImportTests.CreateMap( 128, 128, ( _, _ ) => 0 ) );

	/// <summary>Written independently of ThemeParkIncBodyLayout, including all 21 monthly records and the complete scalar tail.</summary>
	private static BodyFixture Body( int[]? shortCounts = null, int[]? monthlyCounts = null, int recentCount = Recent, int byteCount = 50 )
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
		EmptyRing( 4, 5 );
		var bytes = Enumerable.Range( 0, 22 ).Select( _ => EmptyRing( 1, byteCount ) ).ToArray();
		var tail = (int)stream.Position;
		writer.Write( new byte[92] );
		return new( stream.ToArray(), shorts, monthly, recent, bytes, tail );
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

	[TestMethod]
	public void EmptyPhysicalRingsUseSignedMinusOneAndKeepTheCompleteBody()
	{
		var body = Body();
		Assert.AreEqual( 76617, body.Bytes.Length );
		var native = Read( body );
		CollectionAssert.AreEqual( new[] { 144, 144, 144, 144, 144, 50 }, Rings( native ).Select( ring => ring.Values.Count ).ToArray() );
		foreach ( var ring in Rings( native ) )
		{
			Assert.AreEqual( -1, ring.Head ); Assert.IsFalse( ring.Wrapped ); Assert.AreEqual( 0, ring.Temp );
			Assert.IsTrue( ring.Values.All( value => value == 0 ) );
		}
		var history = new ParkStatisticsHistory(); history.RestoreOriginal( native );
		Assert.IsTrue( history.IsEmpty ); Assert.IsTrue( history.Capture().IsValid );
	}

	[TestMethod]
	public void AllTwentyOneRecordsStayDistinctAndBusArrivalsAreNotAdmissions()
	{
		var body = Distinct(); var payload = Payload( body.Bytes ); var before = payload.ToArray(); var world = World( payload );
		Assert.AreEqual( 21, world.ParkAnalyser!.DayRings.Count );
		Assert.AreEqual( 15000u, world.ParkAnalyser.DayRings[15].Entries[0] );
		Assert.AreEqual( 115u, world.ParkAnalyser.DayRings[15].Current );
		Assert.AreEqual( 987654u, world.ParkAnalyser.VisitorsThisMonth );
		var native = SaveThemeParkIncStatistics.Read( world )!;
		Assert.AreEqual( 17, native.ArrivalsThisMonth );
		CollectionAssert.AreEqual( new[] { 1, 0, 2, 143, 7, 1 }, Rings( native ).Select( ring => ring.Head ).ToArray() );
		CollectionAssert.AreEqual( new[] { false, true, false, false, true, false }, Rings( native ).Select( ring => ring.Wrapped ).ToArray() );
		CollectionAssert.AreEqual( new[] { -101, 17, unchecked( (int)0x7FC00001 ), -104, -105, -106 }, Rings( native ).Select( ring => ring.Temp ).ToArray() );
		Assert.AreEqual( 14002, native.People.RawValues[2] ); Assert.AreEqual( 0d, native.People.Values[2] );
		Assert.AreEqual( unchecked( (int)0x7FC00001 ), native.Happiness.RawValues[3] ); Assert.AreEqual( 0d, native.Happiness.Values[3] );
		var history = new ParkStatisticsHistory(); history.RestoreOriginal( native );
		CollectionAssert.AreEqual( new double[] { -11, 23 }, history.People.Oldest().ToArray() );
		CollectionAssert.AreEqual( Enumerable.Range( 17001, 143 ).Append( 17000 ).Select( value => (double)value ).ToArray(), history.Arrivals.Oldest().ToArray() );
		CollectionAssert.AreEqual( new double[] { 61.5, -2.25, 110.75 }, history.Happiness.Oldest().ToArray() );
		CollectionAssert.AreEqual( Enumerable.Range( -5, 144 ).Select( value => (double)value ).ToArray(), history.Stay.Oldest().ToArray() );
		CollectionAssert.AreEqual( Enumerable.Range( 20008, 136 ).Concat( Enumerable.Range( 20000, 8 ) ).Select( value => (double)value ).ToArray(), history.Rating.Oldest().ToArray() );
		CollectionAssert.AreEqual( new double[] { 2, 5 }, history.RecentStays.Oldest().ToArray() );
		CollectionAssert.AreEqual( before, payload, "Reading never normalizes native bytes in place." );
		var imported = Import( payload );
		Assert.IsNotNull( imported.Statistics ); Assert.AreEqual( 17, imported.Statistics!.ArrivalsThisMonth );
	}

	[TestMethod]
	public void ShortAndUnselectedCapacitiesMoveSelectedOffsetsWithoutChangingTheirValues()
	{
		var standard = Distinct();
		foreach ( var shorts in new[] { new[] { 0, 0, 0, 0 }, new[] { 1, 2, 3, 0 } } )
		{
			var counts = Enumerable.Range( 0, 21 ).Select( index => Selected.Contains( index ) ? Months : index % 4 ).ToArray();
			var moved = Distinct( shorts, counts );
			Assert.AreNotEqual( standard.Monthly[14], moved.Monthly[14] );
			Assert.AreNotEqual( standard.Monthly[17], moved.Monthly[17] );
			Assert.AreNotEqual( standard.RecentOffset, moved.RecentOffset );
			var expected = new ParkStatisticsHistory(); expected.RestoreOriginal( Read( standard ) );
			var actual = new ParkStatisticsHistory(); actual.RestoreOriginal( Read( moved ) );
			Assert.AreEqual( State( expected ), State( actual ) );
		}
	}

	[TestMethod]
	public void SmallerSelectedCapacitiesAreStructurallyValidButOutsideTheFixedHostProfile()
	{
		const string refusal = "structurally supported but unsupported by the fixed host history profile";
		foreach ( var ordinal in Selected )
			foreach ( var capacity in new[] { 0, 1, 143 } )
				foreach ( var (head, wrapped) in capacity == 0 ? new[] { (-1, false) } : new[] { (-1, false), (capacity - 1, false), (0, true) } )
				{
					var counts = Enumerable.Repeat( Months, 21 ).ToArray(); counts[ordinal] = capacity;
					var body = Body( monthlyCounts: counts );
					Ring( body, body.Monthly[ordinal], capacity, head, wrapped, 0, _ => ordinal == 18 ? BitConverter.SingleToInt32Bits( 12.5f ) : 12 );
					var world = World( Payload( body.Bytes ) );
					Assert.AreEqual( capacity, world.ParkAnalyser!.DayRings[ordinal].Entries.Count );
					var error = Assert.ThrowsException<InvalidDataException>( () => SaveThemeParkIncStatistics.Read( world ) );
					StringAssert.Contains( error.Message, refusal );
				}
		foreach ( var capacity in new[] { 0, 1, 49 } )
			foreach ( var (head, wrapped) in capacity == 0 ? new[] { (-1, false) } : new[] { (-1, false), (capacity - 1, false), (0, true) } )
			{
				var body = Body( recentCount: capacity ); Ring( body, body.RecentOffset, capacity, head, wrapped, 0, _ => 3 );
				var world = World( Payload( body.Bytes ) );
				Assert.AreEqual( capacity, world.ParkAnalyser!.Ring50.Entries.Count );
				var error = Assert.ThrowsException<InvalidDataException>( () => SaveThemeParkIncStatistics.Read( world ) );
				StringAssert.Contains( error.Message, refusal );
			}
	}

	[TestMethod]
	public void SelectedHeadsAndRawWrappedFlagsAreValidatedBeforeTheBundleIsReturned()
	{
		var body = Distinct(); var payload = Payload( body.Bytes ); var world = World( payload );
		var start = world.Find( 41 )!.Value.BodyOffset;
		foreach ( var (offset, capacity) in Selected.Select( ordinal => (body.Monthly[ordinal], Months) ).Append( (body.RecentOffset, Recent) ) )
			foreach ( var (head, flag) in new[] { (int.MinValue, 0), (-2, 0), (capacity, 0), (int.MaxValue, 0), (-1, 1), (0, 2), (0, 255) } )
			{
				body.Bytes.CopyTo( payload, start ); Word( payload, start + offset, head ); payload[start + offset + 8] = (byte)flag;
				Assert.ThrowsException<InvalidDataException>( () => SaveThemeParkIncStatistics.Read( world ), $"offset {offset}, head {head}, flag {flag}" );
			}
		body.Bytes.CopyTo( payload, start ); payload[start + body.Monthly[14] + 8] = 2;
		Assert.IsTrue( World( payload ).ParkAnalyser!.DayRings[14].WrappedAround, "The existing raw typed ring collapses byte 2 to true." );
		Assert.ThrowsException<InvalidDataException>( () => SaveThemeParkIncStatistics.Read( World( payload ) ) );
	}

	[TestMethod]
	public void InvalidStructuralCountsAndTruncatedBodiesCannotBorrowTheFollowingThing()
	{
		var body = Body();
		foreach ( var (offset, maximum) in body.Shorts.Select( offset => (offset, 3) )
			.Concat( body.Monthly.Select( offset => (offset, Months) ) ).Append( (body.RecentOffset, Recent) ).Append( (body.ByteRings[0], 50) ) )
			foreach ( var capacity in new[] { -1, int.MinValue, maximum + 1, int.MaxValue } )
			{
				var damaged = body.Bytes.ToArray(); Word( damaged, offset + 4, capacity );
				Assert.ThrowsException<InvalidDataException>( () => World( Payload( damaged ) ), $"offset {offset}, count {capacity}" );
			}
		foreach ( var length in new[] { 0, body.Monthly[14], body.RecentOffset, body.Bytes.Length - 1 } )
			Assert.ThrowsException<InvalidDataException>( () => Import( Payload( body.Bytes[..length] ) ), $"truncated length {length}; the following thing is not analyser data" );
		var payload = Payload( body.Bytes ); var world = World( payload );
		Assert.IsTrue( world.Find( 41 )!.Value.BodyOffset > 16 );
		Assert.AreEqual( body.Bytes.Length, world.BodyOf( world.Find( 41 )!.Value ).Length );
	}

	[TestMethod]
	public void OnlyActiveHappinessMustBeFiniteAndIntegerSamplesKeepTheirSignedBits()
	{
		foreach ( var bits in new[] { unchecked( (int)0x7FC00001 ), unchecked( (int)0x7F800000 ), unchecked( (int)0xFF800000 ) } )
		{
			var body = Body(); Ring( body, body.Monthly[18], Months, -1, false, bits, _ => bits );
			var empty = Read( body ); Assert.IsTrue( empty.Happiness.Values.All( value => value == 0 ) );
			Assert.IsTrue( empty.Happiness.RawValues.All( value => value == bits ) );
			Word( body.Bytes, body.Monthly[18], 0 ); Assert.ThrowsException<InvalidDataException>( () => Read( body ) );
			Word( body.Bytes, body.Monthly[18] + 13, BitConverter.SingleToInt32Bits( 61.5f ) );
			Assert.AreEqual( 61.5, Read( body ).Happiness.Values[0] );
			body.Bytes[body.Monthly[18] + 8] = 1; Assert.ThrowsException<InvalidDataException>( () => Read( body ) );
		}
		var finite = Body(); var floats = new[] { float.MinValue, float.MaxValue, float.Epsilon, -float.Epsilon, -0.0f };
		Ring( finite, finite.Monthly[18], Months, floats.Length - 1, false, int.MinValue, slot => BitConverter.SingleToInt32Bits( floats[slot % floats.Length] ) );
		var words = new[] { int.MinValue, -17, 0, int.MaxValue };
		foreach ( var ordinal in new[] { 14, 17, 19, 20 } )
			Ring( finite, finite.Monthly[ordinal], Months, words.Length - 1, false, ordinal == 17 ? int.MaxValue : int.MinValue, slot => words[slot % words.Length] );
		Ring( finite, finite.RecentOffset, Recent, words.Length - 1, false, int.MinValue, slot => words[slot % words.Length] );
		var native = Read( finite );
		foreach ( var ring in new[] { native.People, native.Arrivals, native.Stay, native.Rating, native.RecentStays } )
			CollectionAssert.AreEqual( words.Select( value => (double)value ).ToArray(), ring.Values.Take( words.Length ).ToArray() );
		for ( var index = 0; index < floats.Length; index++ )
			Assert.AreEqual( BitConverter.DoubleToInt64Bits( (double)floats[index] ), BitConverter.DoubleToInt64Bits( native.Happiness.Values[index] ) );
		Assert.AreEqual( int.MaxValue, native.ArrivalsThisMonth );
		foreach ( var temp in new[] { -1, int.MinValue } )
		{
			Word( finite.Bytes, finite.Monthly[17] + 9, temp );
			Assert.ThrowsException<InvalidDataException>( () => Read( finite ), "The nonnegative live counter is a host policy, unlike historical signed samples." );
		}
	}

	[TestMethod]
	public void EmptyPartialFullAndWrappedRingsPreservePhysicalAndRelativeOrder()
	{
		foreach ( var samples in new[] { 0, 1, 12, 36, 49, 50, 51, 143, 144, 145, 289 } )
		{
			var body = Body();
			var records = Selected.Select( ordinal => (Offset: body.Monthly[ordinal], Capacity: Months, Floating: ordinal == 18) )
				.Append( (Offset: body.RecentOffset, Capacity: Recent, Floating: false) ).ToArray();
			foreach ( var (offset, capacity, floating) in records )
			{
				var values = new int[capacity];
				for ( var sample = 1; sample <= samples; sample++ ) values[(sample - 1) % capacity] = floating ? BitConverter.SingleToInt32Bits( sample + 0.5f ) : sample;
				Ring( body, offset, capacity, samples == 0 ? -1 : (samples - 1) % capacity, samples > capacity, 0, slot => values[slot] );
			}
			var history = new ParkStatisticsHistory(); history.RestoreOriginal( Read( body ) );
			for ( var index = 0; index < records.Length; index++ )
			{
				var ring = Rings( history )[index]; var record = records[index];
				Assert.AreEqual( samples == 0 ? -1 : (samples - 1) % record.Capacity, ring.Head );
				Assert.AreEqual( samples > record.Capacity, ring.Wrapped );
				var expected = Enumerable.Range( Math.Max( 1, samples - record.Capacity + 1 ), Math.Min( samples, record.Capacity ) )
					.Select( sample => sample + (record.Floating ? 0.5 : 0) ).ToArray();
				CollectionAssert.AreEqual( expected, ring.Oldest().ToArray() );
				Assert.IsFalse( ring.TryGet( ring.Count, out _ ) );
				foreach ( var period in new[] { 12, 36, 144 } )
				{
					var newest = Enumerable.Range( 0, Math.Min( period, ring.Count ) ).Select( age =>
					{
						Assert.IsTrue( ring.TryGet( age, out var value ) ); return value;
					} ).ToArray();
					CollectionAssert.AreEqual( expected.Reverse().Take( period ).ToArray(), newest );
				}
			}
		}
	}

	[TestMethod]
	public void ZeroReferenceDoesNotAdoptADecoyAndWrongIdentityOrDuplicateAnalyserIsRejected()
	{
		Assert.IsNull( SaveThemeParkIncStatistics.Read( World( Payload( null, reference: 0 ) ) ) );
		var decoy = Distinct(); decoy.Bytes[decoy.Monthly[20] + 8] = 2;
		var world = World( Payload( decoy.Bytes, reference: 0 ) ); Assert.IsNotNull( world.ParkAnalyser );
		Assert.IsNull( SaveThemeParkIncStatistics.Read( world ) );
		Assert.IsNull( Import( Payload( decoy.Bytes, reference: 0 ) ).Statistics );
		foreach ( var reference in new ushort[] { 7, 99, 999 } )
			Assert.ThrowsException<InvalidDataException>( () => Import( Payload( Body().Bytes, reference ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Import( Payload( null ) ), "A nonzero reference cannot be missing." );
		Assert.ThrowsException<InvalidDataException>( () => Import( Payload( Body().Bytes, duplicate: true ) ), "The TPI world permits only one analyser, including unreferenced duplicates." );
	}

	[TestMethod]
	public void ImmutableBundleAndFailedImportDoNotMutateRunningHistoryFinanceOrSource()
	{
		var fixture = Distinct(); var payload = Payload( fixture.Bytes ); var world = World( payload );
		var native = SaveThemeParkIncStatistics.Read( world )!;
		var raw = Rings( native ).Select( ring => ring.RawValues.ToArray() ).ToArray();
		var values = Rings( native ).Select( ring => ring.Values.ToArray() ).ToArray();
		Array.Clear( payload, 0, payload.Length );
		for ( var index = 0; index < raw.Length; index++ )
		{
			var ring = Rings( native )[index];
			CollectionAssert.AreEqual( raw[index], ring.RawValues.ToArray() ); CollectionAssert.AreEqual( values[index], ring.Values.ToArray() );
			Assert.IsFalse( ring.RawValues is int[] ); Assert.IsFalse( ring.Values is double[] );
			if ( ring.RawValues is IList<int> mutableRaw ) Assert.ThrowsException<NotSupportedException>( () => mutableRaw[0] = 0 );
			if ( ring.Values is IList<double> mutableValues ) Assert.ThrowsException<NotSupportedException>( () => mutableValues[0] = 0 );
		}
		var live = RecentResearchTests.Park(); live.StatisticsHistory.RestoreOriginal( native ); live.Ledger.Post( LedgerCategory.GateTakings, 57 );
		var untouched = new ParkStatisticsHistory(); untouched.RestoreOriginal( native ); var untouchedState = State( untouched );
		var before = ParkSaveFile.Serialize( live ); var hash = WorldStateHash.Compute( new WorldStateSources { Economy = live } );
		foreach ( var damage in new Action<BodyFixture>[]
		{
			body => Word( body.Bytes, body.Monthly[20], Months ),
			body => Word( body.Bytes, body.Monthly[18] + 13, unchecked( (int)0x7FC00001 ) ),
			body => body.Bytes[body.RecentOffset + 8] = 2,
			body => Word( body.Bytes, body.Monthly[17] + 9, -1 ),
		} )
		{
			var damaged = Distinct(); damage( damaged );
			Assert.ThrowsException<InvalidDataException>( () => live.StatisticsHistory.RestoreOriginal( Import( Payload( damaged.Bytes ) ).Statistics! ) );
			Assert.AreEqual( before, ParkSaveFile.Serialize( live ) );
			Assert.AreEqual( hash, WorldStateHash.Compute( new WorldStateSources { Economy = live } ) );
		}
		live.StatisticsHistory.Capture().People!.Slots[0] = 999;
		Assert.AreEqual( -11d, live.StatisticsHistory.People.Oldest().First() );
		live.StatisticsHistory.CloseMonth( 55, 44, 33 ); live.StatisticsHistory.RecordStay( 18 );
		Assert.AreEqual( untouchedState, State( untouched ) );
		for ( var index = 0; index < raw.Length; index++ ) CollectionAssert.AreEqual( values[index], Rings( native )[index].Values.ToArray() );
	}

	private ParkEconomyRuntime Runtime( OriginalParkImport? imported )
	{
		if ( root == null )
		{
			root = Path.Combine( Path.GetTempPath(), "opentpw-tpi-history-" + Guid.NewGuid().ToString( "N" ) );
			Directory.CreateDirectory( Path.Combine( root, "levels", "science" ) );
			File.WriteAllText( Path.Combine( root, "levels", "Standard.sam" ),
				"BankAccountInfo.InitialCash 50000\nBankAccountInfo.InitialAdmissionFee 25\n"
				+ "LoanInfo[0].LoanAmount 10000\nLoanInfo[0].APRInPercent 20\nLoanInfo[0].RepaymentPeriodInMonths 36\nLoanInfo[0].Lendername 0\n"
				+ "Costs.QueueCell 25\nCosts.PathCell 10\nCosts.KartTrackCell 40\nCosts.WaterTrackCell 50\nCosts.MapCell 75\n"
				+ "PeepInfo.ExcitementToCostDivisor 6\nPeepInfo.MinimumEntryFee 20\nPeepInfo.CheapPriceMultiplier 0.75\nPeepInfo.AveragePriceMultiplier 1.25\nPeepInfo.ExpensivePriceMultiplier 2\n" );
			File.WriteAllText( Path.Combine( root, "levels", "science", "Standard.sam" ), "" );
			FileSystem = new BaseFileSystem( root ); GameEditions.Select( GameEdition.ThemeParkInc );
		}
		// The real factory consumes these two metadata properties; no terrain model or GPU resource is needed.
		var park = (OriginalPark)RuntimeHelpers.GetUninitializedObject( typeof( OriginalPark ) );
		typeof( OriginalPark ).GetField( "<LevelName>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( park, "science" );
		typeof( OriginalPark ).GetField( "<Save>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( park, imported );
		return ParkEconomyRuntime.ForThemeParkIncLevel( park, new WorldSeed( 123 ) );
	}

	[TestMethod]
	public void ActualTpiFactoryImportsHistoryButKeepsFreshClockMoneyAndFirstHostMonthBoundary()
	{
		foreach ( var rate in new[] { 15000, 30000 } )
		{
			var imported = Import( Payload( Distinct().Bytes, rate: rate ) );
			Assert.AreEqual( 76543u, imported.ThemeParkIncWorld!.Variables.GameTick );
			Assert.AreEqual( rate, imported.ThemeParkIncWorld.Calendar.FunnySecsPerRealSec );
			var runtime = Runtime( imported ); var economy = runtime.Economy; var control = Runtime( null ).Economy;
			Assert.IsNull( runtime.Import ); Assert.IsTrue( runtime.StartsOpen ); Assert.IsFalse( runtime.Start.ImportShippedSave );
			Assert.AreEqual( EconomyRules.ThemeParkIncMoney, economy.Rules );
			Assert.AreEqual( 0L, economy.Tick ); Assert.AreEqual( new DateTime( 2000, 1, 1 ), ParkCalendar.DateTimeAt( economy.Tick ) );
			Assert.AreEqual( 50000L, economy.Balance ); Assert.AreEqual( 25, economy.EntranceFee );
			Assert.AreEqual( 0, economy.Ledger.History.Count ); Assert.AreEqual( 0, economy.Staff.Members.Count + economy.Staff.Candidates.Count );
			var expectedHistory = new ParkStatisticsHistory(); expectedHistory.RestoreOriginal( imported.Statistics! );
			Assert.AreEqual( State( expectedHistory ), State( economy.StatisticsHistory ) );
			Assert.AreEqual( 17, economy.StatisticsHistory.ArrivalsThisMonth );
			CollectionAssert.AreEqual( new[] { 1, 0, 2, 143, 7, 1 }, Rings( economy.StatisticsHistory ).Select( ring => ring.Head ).ToArray() );
			Assert.AreEqual( JsonSerializer.Serialize( control.Ledger ), JsonSerializer.Serialize( economy.Ledger ) );
			Assert.AreEqual( WorldStateHash.Compute( new WorldStateSources { Economy = control } ), WorldStateHash.Compute( new WorldStateSources { Economy = economy } ) );
			economy.StatisticsHistory.RecordArrival(); var recentBefore = economy.StatisticsHistory.RecentStays.Oldest().ToArray();
			economy.AdvanceMonths( 1 ); control.AdvanceMonths( 1 );
			Assert.AreEqual( new DateTime( 2000, 2, 1 ), ParkCalendar.DateTimeAt( economy.Tick ).Date );
			CollectionAssert.AreEqual( new[] { 2, 1, 3, 0, 8, 1 }, Rings( economy.StatisticsHistory ).Select( ring => ring.Head ).ToArray() );
			CollectionAssert.AreEqual( new[] { false, true, false, true, true, false }, Rings( economy.StatisticsHistory ).Select( ring => ring.Wrapped ).ToArray() );
			Assert.AreEqual( 0d, economy.StatisticsHistory.People.Oldest().Last() ); Assert.AreEqual( 0d, economy.StatisticsHistory.Happiness.Oldest().Last() );
			Assert.AreEqual( 18d, economy.StatisticsHistory.Arrivals.Oldest().Last() ); Assert.AreEqual( 0, economy.StatisticsHistory.ArrivalsThisMonth );
			Assert.AreEqual( 3d, economy.StatisticsHistory.Stay.Oldest().Last() );
			Assert.AreEqual( (double)economy.Ledger.History.Single().ParkRating, economy.StatisticsHistory.Rating.Oldest().Last() );
			CollectionAssert.AreEqual( recentBefore, economy.StatisticsHistory.RecentStays.Oldest().ToArray() );
			Assert.AreEqual( JsonSerializer.Serialize( control.Ledger ), JsonSerializer.Serialize( economy.Ledger ) );
			economy.AdvanceMonths( 1 ); Assert.AreEqual( 0d, economy.StatisticsHistory.Arrivals.Oldest().Last() );
		}
	}

	[TestMethod]
	public void RecentStayWindowCanExistWithoutMonthlyStayAndContinuesAtItsOwnHead()
	{
		var body = Body(); Ring( body, body.RecentOffset, Recent, 7, true, 0, slot => slot == 8 ? 100 : 2 );
		var economy = Runtime( Import( Payload( body.Bytes ) ) ).Economy;
		Assert.AreEqual( 0, economy.StatisticsHistory.Stay.Count ); Assert.AreEqual( 50, economy.StatisticsHistory.RecentStays.Count );
		economy.StatisticsHistory.RecordStay( -5 ); economy.AdvanceMonths( 1 );
		Assert.AreEqual( (8, true), (economy.StatisticsHistory.RecentStays.Head, economy.StatisticsHistory.RecentStays.Wrapped) );
		Assert.AreEqual( 1d, economy.StatisticsHistory.Stay.Oldest().Last(), "The replaced oldest 100 is excluded; (49 * 2 - 5) / 50 truncates to 1." );
		Ring( body, body.RecentOffset, Recent, 1, false, 0, slot => slot == 0 ? -2 : slot == 1 ? -5 : 12345 );
		economy = Runtime( Import( Payload( body.Bytes ) ) ).Economy; economy.AdvanceMonths( 1 );
		Assert.AreEqual( -3d, economy.StatisticsHistory.Stay.Oldest().Last(), "Signed means truncate toward zero; inactive slots do not enter the mean." );
	}

	[TestMethod]
	public void AbsentAnalyserAndNoSaveBothLeaveTheActualTpiFactoryHistoryEmpty()
	{
		foreach ( var imported in new[] { null, Import( Payload( null, reference: 0 ) ), Import( Payload( Distinct().Bytes, reference: 0 ) ) } )
		{
			var economy = Runtime( imported ).Economy;
			Assert.IsTrue( economy.StatisticsHistory.IsEmpty ); Assert.AreEqual( 0, economy.Ledger.History.Count );
			economy.AdvanceMonths( 1 );
			Assert.IsTrue( Rings( economy.StatisticsHistory ).Take( 5 ).All( ring => ring.Count == 1 ) );
			Assert.AreEqual( 0d, economy.StatisticsHistory.Arrivals.Oldest().Single() );
			Assert.AreEqual( 0d, economy.StatisticsHistory.Stay.Oldest().Single() );
		}
	}

	[TestMethod]
	public void ImportedBundleRoundTripsTheExistingHostJsonSchemaWithoutClaimingTpiSaveResume()
	{
		// TPI park saving is outside this slice. The shared host history schema must nevertheless preserve its typed bundle.
		var park = RecentResearchTests.Park(); park.StatisticsHistory.RestoreOriginal( Read( Distinct() ) );
		var json = ParkSaveFile.Serialize( park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog );
		Assert.AreEqual( State( park.StatisticsHistory ), State( restored.StatisticsHistory ) );
		Assert.AreEqual( (7, true), (restored.StatisticsHistory.Rating.Head, restored.StatisticsHistory.Rating.Wrapped) );
		Assert.AreEqual( 17, restored.StatisticsHistory.ArrivalsThisMonth );
		park.StatisticsHistory.CloseMonth( 22.75f, 123, 64 ); restored.StatisticsHistory.CloseMonth( 22.75f, 123, 64 );
		Assert.AreEqual( State( park.StatisticsHistory ), State( restored.StatisticsHistory ) );
		Assert.AreEqual( 0, restored.Ledger.History.Count );
		Assert.IsTrue( restored.StatisticsHistory.Capture().Happiness!.Slots.Skip( 4 ).All( value => value == 0 ) );
		var old = JsonNode.Parse( json )!.AsObject();
		var key = old.Select( pair => pair.Key ).Single( name => name.Equals( "StatisticsHistory", StringComparison.OrdinalIgnoreCase ) ); old.Remove( key );
		var absent = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( old.ToJsonString() ) ), park.Settings, park.Catalog );
		Assert.IsTrue( absent.StatisticsHistory.IsEmpty ); Assert.AreEqual( 0, absent.Ledger.History.Count );
	}
}
