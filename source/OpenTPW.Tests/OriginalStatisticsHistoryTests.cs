using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic native TPW statistics, physical ring state and continuation, without original game assets.</summary>
[TestClass, DoNotParallelize]
public class OriginalStatisticsHistoryTests
{
	private Logger previousLog = null!;
	[TestInitialize]
	public void Setup() => previousLog = Log;
	[TestCleanup]
	public void Cleanup() => Log = previousLog;

	private const int MonthlyOffset = 62_590, MonthlyBytes = 589, MonthlyCapacity = 144;
	private const int RecentStaysOffset = 72_014, RecentStayCapacity = 50, BodyBytes = 73_536;
	private static readonly int[] SelectedOrdinals = { 9, 12, 13, 14, 15 };

	private static SaveThing Thing( int bytes = BodyBytes, int offset = 0 ) => new( 5, SaveThingModel.ParkAnalyser, 0, 0, offset, bytes );
	private static int Monthly( int ordinal ) => MonthlyOffset + ordinal * MonthlyBytes;
	private static void Word( byte[] bytes, int offset, int value ) => BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( offset, 4 ), value );
	private static NativeParkStatistics Read( byte[] body ) => SaveParkStatistics.Read( body, Thing( body.Length ) );
	private static string State( ParkStatisticsHistory history ) => JsonSerializer.Serialize( history.Capture() );
	private static ulong Hash( ParkEconomy economy ) => WorldStateHash.Compute( new WorldStateSources { Economy = economy } );

	private static void Ring( byte[] body, int offset, int capacity, int head, bool wrapped, int temp, Func<int, int> value )
	{
		Word( body, offset, head );
		Word( body, offset + 4, capacity );
		body[offset + 8] = wrapped ? (byte)1 : (byte)0;
		Word( body, offset + 9, temp );
		for ( var slot = 0; slot < capacity; slot++ )
			Word( body, offset + 13 + slot * 4, value( slot ) );
	}

	private static byte[] DistinctBody()
	{
		var body = SaveNewParkThings.ParkAnalyser( 0, 0 );
		Ring( body, Monthly( 9 ), MonthlyCapacity, 1, false, -101, slot => slot == 0 ? -11 : slot == 1 ? 23 : 9100 + slot );
		Ring( body, Monthly( 12 ), MonthlyCapacity, 0, true, 17, slot => 1200 + slot );
		Ring( body, Monthly( 13 ), MonthlyCapacity, 2, false, unchecked( (int)0x7FC00001 ), slot =>
			slot < 3 ? BitConverter.SingleToInt32Bits( new[] { 61.5f, -2.25f, 110.75f }[slot] ) : unchecked( (int)0x7FC00001 ) );
		Ring( body, Monthly( 14 ), MonthlyCapacity, 143, false, -104, slot => slot - 5 );
		Ring( body, Monthly( 15 ), MonthlyCapacity, 7, true, -105, slot => 1500 + slot );
		Ring( body, RecentStaysOffset, RecentStayCapacity, 1, false, -106, slot => slot == 0 ? 2 : slot == 1 ? 5 : 9900 + slot );
		return body;
	}

	private static NativeStatisticsRing[] NativeRings( NativeParkStatistics data ) =>
		new[] { data.People, data.Arrivals, data.Happiness, data.Stay, data.Rating, data.RecentStays };

	private static HistoryRing[] RuntimeRings( ParkStatisticsHistory history ) =>
		new[] { history.People, history.Arrivals, history.Happiness, history.Stay, history.Rating, history.RecentStays };

	[TestMethod]
	public void ConstructorAndCodecAgreeOnCompleteBodyAndEmptyPhysicalRings()
	{
		Assert.AreEqual( MonthlyOffset, SaveParkStatistics.MonthlyOffset );
		Assert.AreEqual( MonthlyBytes, SaveParkStatistics.MonthlyBytes );
		Assert.AreEqual( MonthlyCapacity, SaveParkStatistics.MonthlyCapacity );
		Assert.AreEqual( RecentStaysOffset, SaveParkStatistics.RecentStaysOffset );
		Assert.AreEqual( RecentStayCapacity, SaveParkStatistics.RecentStayCapacity );
		Assert.AreEqual( BodyBytes, SaveParkStatistics.BodyBytes );
		var body = SaveNewParkThings.ParkAnalyser( 4, 6 );
		Assert.AreEqual( BodyBytes, body.Length );
		var parsed = Read( body );
		Assert.AreEqual( 0, parsed.ArrivalsThisMonth );
		foreach ( var ring in NativeRings( parsed ) )
		{
			Assert.AreEqual( -1, ring.Head );
			Assert.IsFalse( ring.Wrapped );
			Assert.AreEqual( 0, ring.Temp );
			Assert.IsTrue( ring.RawValues.All( value => value == 0 ) );
			Assert.IsTrue( ring.Values.All( value => value == 0 ) );
		}
		CollectionAssert.AreEqual( new[] { 144, 144, 144, 144, 144, 50 }, NativeRings( parsed ).Select( ring => ring.Values.Count ).ToArray() );
		var history = new ParkStatisticsHistory();
		history.RestoreOriginal( parsed );
		Assert.IsTrue( history.IsEmpty );
		Assert.IsTrue( history.Capture().IsValid );
	}

	[TestMethod]
	public void SelectedOrdinalsKeepDistinctSamplesTempsAndIndependentPhysicalMetadata()
	{
		var body = DistinctBody(); var before = body.ToArray();
		var parsed = Read( body );
		CollectionAssert.AreEqual( before, body, "reading must not normalize the source bytes in place" );
		CollectionAssert.AreEqual( new[] { 1, 0, 2, 143, 7, 1 }, NativeRings( parsed ).Select( ring => ring.Head ).ToArray() );
		CollectionAssert.AreEqual( new[] { false, true, false, false, true, false }, NativeRings( parsed ).Select( ring => ring.Wrapped ).ToArray() );
		CollectionAssert.AreEqual( new[] { -101, 17, unchecked( (int)0x7FC00001 ), -104, -105, -106 }, NativeRings( parsed ).Select( ring => ring.Temp ).ToArray() );
		Assert.AreEqual( 17, parsed.ArrivalsThisMonth );
		Assert.AreEqual( 9102, parsed.People.RawValues[2], "inactive physical words remain available to a future native exporter" );
		Assert.AreEqual( 0d, parsed.People.Values[2], "inactive physical words are not observations" );
		Assert.AreEqual( unchecked( (int)0x7FC00001 ), parsed.Happiness.RawValues[3] );
		Assert.AreEqual( 0d, parsed.Happiness.Values[3] );
		var history = new ParkStatisticsHistory(); history.RestoreOriginal( parsed );
		CollectionAssert.AreEqual( new double[] { -11, 23 }, history.People.Oldest().ToArray() );
		CollectionAssert.AreEqual( Enumerable.Range( 1201, 143 ).Select( value => (double)value ).Append( 1200 ).ToArray(), history.Arrivals.Oldest().ToArray() );
		CollectionAssert.AreEqual( new double[] { 61.5, -2.25, 110.75 }, history.Happiness.Oldest().ToArray() );
		CollectionAssert.AreEqual( Enumerable.Range( -5, 144 ).Select( value => (double)value ).ToArray(), history.Stay.Oldest().ToArray() );
		CollectionAssert.AreEqual( Enumerable.Range( 1508, 136 ).Concat( Enumerable.Range( 1500, 8 ) ).Select( value => (double)value ).ToArray(), history.Rating.Oldest().ToArray() );
		CollectionAssert.AreEqual( new double[] { 2, 5 }, history.RecentStays.Oldest().ToArray() );
		CollectionAssert.AreEqual( NativeRings( parsed ).Select( ring => ring.Head ).ToArray(), RuntimeRings( history ).Select( ring => ring.Head ).ToArray() );
		CollectionAssert.AreEqual( NativeRings( parsed ).Select( ring => ring.Wrapped ).ToArray(), RuntimeRings( history ).Select( ring => ring.Wrapped ).ToArray() );
	}

	[TestMethod]
	public void BodyOffsetIsAppliedOnceAndNoReadCanBorrowBytesFromTheNextThing()
	{
		var body = DistinctBody();
		var payload = Enumerable.Repeat( (byte)0xA5, 37 ).Concat( body ).Concat( Enumerable.Repeat( (byte)0x5A, BodyBytes ) ).ToArray();
		var before = payload.ToArray();
		var parsed = SaveParkStatistics.Read( payload, Thing( body.Length, 37 ) );
		Assert.AreEqual( 61.5, parsed.Happiness.Values[0] );
		Assert.AreEqual( 17, parsed.ArrivalsThisMonth );
		CollectionAssert.AreEqual( before, payload );
		foreach ( var length in new[] { -1, 0, MonthlyOffset, RecentStaysOffset, RecentStaysOffset + 213, BodyBytes - 1 } )
			Assert.ThrowsException<InvalidDataException>( () => SaveParkStatistics.Read( payload, Thing( length, 37 ) ), $"body length {length}; the following payload is not part of this thing" );
		foreach ( var thing in new[]
		{
			Thing() with { Model = SaveThingModel.Bank }, Thing( BodyBytes, -1 ), Thing( -1 ),
			Thing( BodyBytes, int.MaxValue ), Thing( int.MaxValue ), Thing( int.MaxValue, int.MaxValue ), Thing( BodyBytes, body.Length ),
		} )
			Assert.ThrowsException<InvalidDataException>( () => SaveParkStatistics.Read( body, thing ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveParkStatistics.Read( body[..^1], Thing() ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveParkStatistics.Read( Array.Empty<byte>(), Thing() ) );
	}

	[TestMethod]
	public void AllPrecedingShortRingsMustKeepTheirFixedCapacitiesAndValidMetadata()
	{
		for ( var index = 0; index < SaveRecentResearch.RingCount; index++ )
		{
			var offset = SaveRecentResearch.BodyOffset + index * SaveRecentResearch.RingBytes;
			foreach ( var capacity in new[] { int.MinValue, -1, 0, 2, 4, int.MaxValue } )
			{
				var body = DistinctBody(); Word( body, offset + 4, capacity );
				Assert.ThrowsException<InvalidDataException>( () => Read( body ), $"short ring {index}, capacity {capacity}" );
			}
			foreach ( var (head, wrapped) in new[] { (-2, 0), (3, 0), (-1, 1), (0, 2), (0, 255) } )
			{
				var body = DistinctBody(); Word( body, offset, head ); body[offset + 8] = (byte)wrapped;
				Assert.ThrowsException<InvalidDataException>( () => Read( body ), $"short ring {index}, head {head}, wrapped {wrapped}" );
			}
		}
	}

	[TestMethod]
	public void EveryMonthlyCapacityIsValidatedEvenForUngraphedOrdinals()
	{
		for ( var ordinal = 0; ordinal < 16; ordinal++ )
			foreach ( var capacity in new[] { int.MinValue, -1, 0, 143, 145, int.MaxValue } )
			{
				var body = DistinctBody(); Word( body, Monthly( ordinal ) + 4, capacity );
				Assert.ThrowsException<InvalidDataException>( () => Read( body ), $"monthly ordinal {ordinal}, capacity {capacity}" );
			}
		foreach ( var capacity in new[] { int.MinValue, -1, 0, 49, 51, int.MaxValue } )
		{
			var body = DistinctBody(); Word( body, RecentStaysOffset + 4, capacity );
			Assert.ThrowsException<InvalidDataException>( () => Read( body ), $"recent-stay capacity {capacity}" );
		}
	}

	[TestMethod]
	public void AllSelectedRingsRejectOutOfRangeHeadsAndNonBooleanWrappedBytes()
	{
		var records = SelectedOrdinals.Select( ordinal => (Offset: Monthly( ordinal ), Capacity: MonthlyCapacity) )
			.Append( (Offset: RecentStaysOffset, Capacity: RecentStayCapacity) );
		foreach ( var (offset, capacity) in records )
			foreach ( var (head, wrapped) in new[] { (int.MinValue, 0), (-2, 0), (capacity, 0), (int.MaxValue, 0), (-1, 1), (0, 2), (0, 255) } )
			{
				var body = DistinctBody(); Word( body, offset, head ); body[offset + 8] = (byte)wrapped;
				Assert.ThrowsException<InvalidDataException>( () => Read( body ), $"offset {offset}, head {head}, wrapped {wrapped}" );
			}
	}

	[TestMethod]
	public void ActiveHappinessIsReinterpretedAsSinglePrecisionIncludingFiniteExtremes()
	{
		var expected = new[] { 61.5f, -2.25f, float.MinValue, float.MaxValue, float.Epsilon, -float.Epsilon, -0.0f };
		var body = SaveNewParkThings.ParkAnalyser( 0, 0 );
		Ring( body, Monthly( 13 ), MonthlyCapacity, expected.Length - 1, false, int.MinValue,
			slot => slot < expected.Length ? BitConverter.SingleToInt32Bits( expected[slot] ) : unchecked( (int)0x7FC00001 ) );
		var parsed = Read( body );
		for ( var slot = 0; slot < expected.Length; slot++ )
		{
			Assert.AreEqual( BitConverter.SingleToInt32Bits( expected[slot] ), parsed.Happiness.RawValues[slot] );
			Assert.AreEqual( BitConverter.DoubleToInt64Bits( (double)expected[slot] ), BitConverter.DoubleToInt64Bits( parsed.Happiness.Values[slot] ), $"float slot {slot}" );
		}
		Assert.IsTrue( parsed.Happiness.Values.Skip( expected.Length ).All( value => value == 0 ) );
		var history = new ParkStatisticsHistory(); history.RestoreOriginal( parsed );
		CollectionAssert.AreEqual( expected.Select( value => (double)value ).ToArray(), history.Happiness.Oldest().ToArray() );
		Assert.IsTrue( history.Capture().IsValid );
	}

	[TestMethod]
	public void NonfiniteHappinessIsRejectedOnlyWhenItsPhysicalSlotIsActive()
	{
		foreach ( var bits in new[] { unchecked( (int)0x7FC00001 ), unchecked( (int)0x7F800000 ), unchecked( (int)0xFF800000 ) } )
		{
			var body = SaveNewParkThings.ParkAnalyser( 0, 0 );
			Ring( body, Monthly( 13 ), MonthlyCapacity, -1, false, bits, _ => bits );
			var empty = Read( body );
			Assert.IsTrue( empty.Happiness.RawValues.All( value => value == bits ) );
			Assert.IsTrue( empty.Happiness.Values.All( value => value == 0 ) );
			Word( body, Monthly( 13 ), 0 );
			Assert.ThrowsException<InvalidDataException>( () => Read( body ), "slot zero became active" );
			Word( body, Monthly( 13 ) + 13, BitConverter.SingleToInt32Bits( 61.5f ) );
			Assert.AreEqual( 61.5, Read( body ).Happiness.Values[0] );
			body[Monthly( 13 ) + 8] = 1;
			Assert.ThrowsException<InvalidDataException>( () => Read( body ), "wrapped rings make every slot active, even slots after the head" );
		}
	}

	[TestMethod]
	public void EveryIntegerSeriesPreservesSignedExtremesWithoutPlausibilityClamps()
	{
		var body = SaveNewParkThings.ParkAnalyser( 0, 0 );
		var expected = new[] { int.MinValue, -17, 0, int.MaxValue };
		foreach ( var ordinal in new[] { 9, 12, 14, 15 } )
			Ring( body, Monthly( ordinal ), MonthlyCapacity, 3, false, ordinal == 12 ? int.MaxValue : int.MinValue, slot => expected[slot % expected.Length] );
		Ring( body, RecentStaysOffset, RecentStayCapacity, 3, false, int.MinValue, slot => expected[slot % expected.Length] );
		var parsed = Read( body );
		Assert.AreEqual( int.MaxValue, parsed.ArrivalsThisMonth );
		var history = new ParkStatisticsHistory(); history.RestoreOriginal( parsed );
		foreach ( var ring in new[] { history.People, history.Arrivals, history.Stay, history.Rating, history.RecentStays } )
			CollectionAssert.AreEqual( expected.Select( value => (double)value ).ToArray(), ring.Oldest().ToArray() );
		Assert.IsTrue( history.Capture().IsValid );
	}

	[TestMethod]
	public void NegativeLiveArrivalsAreRejectedAsHostPolicyWithoutRejectingNegativeClosedSamples()
	{
		var body = SaveNewParkThings.ParkAnalyser( 0, 0 );
		Ring( body, Monthly( 12 ), MonthlyCapacity, 0, false, 0, _ => -50 );
		Assert.AreEqual( -50d, Read( body ).Arrivals.Values[0] );
		foreach ( var temp in new[] { -1, int.MinValue } )
		{
			Word( body, Monthly( 12 ) + 9, temp );
			Assert.ThrowsException<InvalidDataException>( () => Read( body ), "host saves require a nonnegative running arrival count" );
		}
	}

	[TestMethod]
	public void EmptyPartialExactlyFullAndWrappedHistoriesKeepPhysicalAndLogicalOrder()
	{
		foreach ( var samples in new[] { 0, 1, 12, 36, 49, 50, 51, 143, 144, 145, 289 } )
		{
			var body = SaveNewParkThings.ParkAnalyser( 0, 0 );
			var records = SelectedOrdinals.Select( ordinal => (Offset: Monthly( ordinal ), Capacity: MonthlyCapacity, Floating: ordinal == 13) )
				.Append( (Offset: RecentStaysOffset, Capacity: RecentStayCapacity, Floating: false) ).ToArray();
			foreach ( var (offset, capacity, floating) in records )
			{
				var words = Enumerable.Repeat( floating ? unchecked( (int)0x7FC00001 ) : -999, capacity ).ToArray();
				for ( var sample = 1; sample <= samples; sample++ )
					words[(sample - 1) % capacity] = floating ? BitConverter.SingleToInt32Bits( sample + 0.5f ) : sample;
				Ring( body, offset, capacity, samples == 0 ? -1 : (samples - 1) % capacity, samples > capacity, 0, slot => words[slot] );
			}
			var history = new ParkStatisticsHistory(); history.RestoreOriginal( Read( body ) );
			var rings = RuntimeRings( history );
			for ( var index = 0; index < rings.Length; index++ )
			{
				var ring = rings[index]; var capacity = records[index].Capacity;
				Assert.AreEqual( samples == 0 ? -1 : (samples - 1) % capacity, ring.Head, $"samples {samples}, ring {index}" );
				Assert.AreEqual( samples > capacity, ring.Wrapped, $"samples {samples}, ring {index}" );
				Assert.AreEqual( Math.Min( samples, capacity ), ring.Count );
				var expected = Enumerable.Range( Math.Max( 1, samples - capacity + 1 ), Math.Min( samples, capacity ) )
					.Select( sample => sample + (records[index].Floating ? 0.5 : 0) ).ToArray();
				CollectionAssert.AreEqual( expected, ring.Oldest().ToArray(), $"samples {samples}, ring {index}" );
				Assert.IsFalse( ring.TryGet( ring.Count, out _ ) );
			}
		}
	}

	[TestMethod]
	public void NativeBundlesOwnTheirRawAndNormalizedDataAndRestoresDoNotAliasThem()
	{
		var body = DistinctBody(); var parsed = Read( body );
		var rawBefore = NativeRings( parsed ).Select( ring => ring.RawValues.ToArray() ).ToArray();
		var normalizedBefore = NativeRings( parsed ).Select( ring => ring.Values.ToArray() ).ToArray();
		Array.Clear( body, 0, body.Length );
		var native = NativeRings( parsed );
		for ( var index = 0; index < native.Length; index++ )
		{
			CollectionAssert.AreEqual( rawBefore[index], native[index].RawValues.ToArray() );
			CollectionAssert.AreEqual( normalizedBefore[index], native[index].Values.ToArray() );
			Assert.IsFalse( native[index].RawValues is int[], "a caller must not be able to cast and mutate the parser's backing array" );
			Assert.IsFalse( native[index].Values is double[] );
			if ( native[index].RawValues is IList<int> raw )
				Assert.ThrowsException<NotSupportedException>( () => raw[0] = 42 );
			if ( native[index].Values is IList<double> values )
				Assert.ThrowsException<NotSupportedException>( () => values[0] = 42 );
		}
		var first = new ParkStatisticsHistory(); first.RestoreOriginal( parsed );
		var second = new ParkStatisticsHistory(); second.RestoreOriginal( parsed );
		var before = State( second );
		first.Capture().People!.Slots[0] = 999;
		Assert.AreEqual( -11d, first.People.Oldest().First() );
		first.CloseMonth( 88, 456, 123 ); first.RecordStay( 20 ); first.RecordArrival();
		Assert.AreEqual( before, State( second ) );
		for ( var index = 0; index < native.Length; index++ )
		{
			CollectionAssert.AreEqual( rawBefore[index], native[index].RawValues.ToArray() );
			CollectionAssert.AreEqual( normalizedBefore[index], native[index].Values.ToArray() );
		}
	}

	[TestMethod]
	public void FirstCloseContinuesAllIndependentHeadsAndImportedLiveInputsExactlyOnce()
	{
		var history = new ParkStatisticsHistory(); history.RestoreOriginal( Read( DistinctBody() ) );
		var recentBefore = history.RecentStays.Oldest().ToArray();
		history.RecordArrival();
		history.CloseMonth( 55.25f, 40, 91 );
		Assert.AreEqual( 0, history.ArrivalsThisMonth );
		CollectionAssert.AreEqual( new[] { 2, 1, 3, 0, 8, 1 }, RuntimeRings( history ).Select( ring => ring.Head ).ToArray() );
		CollectionAssert.AreEqual( new[] { false, true, false, true, true, false }, RuntimeRings( history ).Select( ring => ring.Wrapped ).ToArray() );
		CollectionAssert.AreEqual( new double[] { 40, 18, 55.25, 3, 91 }, RuntimeRings( history ).Take( 5 ).Select( ring => ring.Oldest().Last() ).ToArray() );
		CollectionAssert.AreEqual( recentBefore, history.RecentStays.Oldest().ToArray(), "closing a month does not consume the departed-guests window" );
		history.CloseMonth( 12.5f, 41, 92 );
		Assert.AreEqual( 0d, history.Arrivals.Oldest().Last() );
		CollectionAssert.AreEqual( new double[] { 91, 92 }, history.Rating.Oldest().TakeLast( 2 ).ToArray() );
		Assert.AreEqual( 3d, history.Stay.Oldest().Last() );
	}

	[TestMethod]
	public void WrappedRecentStayWindowContinuesAtItsSavedHeadAndTruncatesSignedMean()
	{
		var body = SaveNewParkThings.ParkAnalyser( 0, 0 );
		Ring( body, RecentStaysOffset, RecentStayCapacity, 7, true, 123, slot => slot == 8 ? 100 : 2 );
		var history = new ParkStatisticsHistory(); history.RestoreOriginal( Read( body ) );
		history.RecordStay( -5 );
		Assert.AreEqual( (8, true), (history.RecentStays.Head, history.RecentStays.Wrapped) );
		history.CloseMonth( 0, 0, 0 );
		Assert.AreEqual( 1d, history.Stay.Oldest().Last(), "the overwritten oldest 100 is excluded: (49 * 2 - 5) / 50 truncates to 1" );
		Ring( body, RecentStaysOffset, RecentStayCapacity, 1, false, 0, slot => slot == 0 ? -2 : slot == 1 ? -5 : 12345 );
		history.RestoreOriginal( Read( body ) ); history.CloseMonth( 0, 0, 0 );
		Assert.AreEqual( -3d, history.Stay.Oldest().Last(), "signed means truncate toward zero and inactive words do not enter the average" );
	}

	private static OriginalSavePayload Template( BalanceSettings settings, byte[] body )
	{
		var template = OriginalSavePayloadFixtures.Blank( settings );
		template.RideSystem = SaveTestPayloads.EmptyRideSystem[4..];
		var things = template.World.Things;
		things[0].Next = 2;
		things.Add( new SaveThingEntry { Id = 2, Next = 3, Model = SaveThingModel.ResearchLab, Body = new byte[SaveResearchLab.Bytes] } );
		things.Add( new SaveThingEntry { Id = 3, Next = 4, Model = SaveThingModel.ParkAnalyser, Body = SaveNewParkThings.ParkAnalyser( 0, 0 ) } );
		things.Add( new SaveThingEntry { Id = 4, Next = 5, Model = SaveThingModel.ChallengeSystem, Body = OriginalSaveImportTests.Challenges( settings ) } );
		things.Add( new SaveThingEntry { Id = 5, Next = 0, Model = SaveThingModel.ParkAnalyser, Body = body } );
		template.World.Variables = template.World.Variables with { ResearchLab = 2, ParkAnalyser = 5 };
		return template;
	}

	private static OriginalParkImport Import( OriginalSavePayload template ) =>
		OriginalParkImport.Import( template.Write(), OriginalParkImportTests.CreateMap( 128, 128, ( _, _ ) => 0 ) );

	[TestMethod]
	public void FullImportUsesTheWorldReferencedAnalyserAndIgnoresAMalformedDecoyHistory()
	{
		var settings = OriginalSaveImportTests.Settings();
		var template = Template( settings, DistinctBody() );
		var decoy = template.World.Things.Single( thing => thing.Id == 3 ).Body;
		Word( decoy, Monthly( 15 ) + 4, int.MaxValue );
		var before = template.Write();
		var imported = Import( template );
		Assert.IsNotNull( imported.Statistics );
		Assert.AreEqual( 17, imported.Statistics!.ArrivalsThisMonth );
		Assert.AreEqual( 61.5, imported.Statistics.Happiness.Values[0] );
		Assert.AreEqual( (7, true), (imported.Statistics.Rating.Head, imported.Statistics.Rating.Wrapped) );
		var park = RecentResearchTests.Park( settings: settings );
		OriginalEconomyImport.Apply( park, imported );
		var expected = new ParkStatisticsHistory(); expected.RestoreOriginal( Read( DistinctBody() ) );
		Assert.AreEqual( State( expected ), State( park.StatisticsHistory ) );
		CollectionAssert.AreEqual( before, template.Write(), "parsing and applying do not rewrite the source payload" );
	}

	[TestMethod]
	public void ZeroAnalyserReferenceLeavesHistoryAbsentWithoutSearchingOtherThings()
	{
		var settings = OriginalSaveImportTests.Settings();
		var template = Template( settings, DistinctBody() );
		template.World.Variables = template.World.Variables with { ParkAnalyser = 0 };
		foreach ( var analyser in template.World.Things.Where( thing => thing.Model == SaveThingModel.ParkAnalyser ) )
			Word( analyser.Body, Monthly( 15 ) + 4, int.MaxValue );
		var imported = Import( template );
		Assert.IsNull( imported.Statistics );
		var park = RecentResearchTests.Park( settings: settings );
		OriginalEconomyImport.Apply( park, imported );
		Assert.IsTrue( park.StatisticsHistory.IsEmpty );
		Assert.AreEqual( 0, park.Ledger.History.Count );
		park.StatisticsHistory.RestoreOriginal( Read( DistinctBody() ) );
		OriginalEconomyImport.Apply( park, imported );
		Assert.IsTrue( park.StatisticsHistory.IsEmpty, "reusing an economy cannot keep another park's history when the new save names no analyser" );
	}

	[TestMethod]
	public void MissingWrongModelAndMalformedReferencedAnalysersRejectBeforeApplyingAnyParkState()
	{
		var settings = OriginalSaveImportTests.Settings();
		var park = RecentResearchTests.Park( settings: settings );
		park.StatisticsHistory.RestoreOriginal( Read( DistinctBody() ) );
		park.Ledger.Post( LedgerCategory.GateTakings, 57 );
		var before = ParkSaveFile.Serialize( park ); var hash = Hash( park ); var rng = park.World.State;
		foreach ( var id in new ushort[] { 1, 2, 99 } )
		{
			var template = Template( settings, DistinctBody() );
			template.World.Variables = template.World.Variables with { ParkAnalyser = id };
			Assert.ThrowsException<InvalidDataException>( () => OriginalEconomyImport.Apply( park, Import( template ) ), $"invalid analyser reference {id}" );
			Assert.AreEqual( before, ParkSaveFile.Serialize( park ) );
		}
		foreach ( var damage in new Action<byte[]>[]
		{
			body => Word( body, Monthly( 15 ), MonthlyCapacity ),
			body => Word( body, Monthly( 13 ) + 13, unchecked( (int)0x7FC00001 ) ),
			body => Word( body, RecentStaysOffset + 4, int.MaxValue ),
			body => Word( body, Monthly( 12 ) + 9, -1 ),
		} )
		{
			var body = DistinctBody(); damage( body );
			Assert.ThrowsException<InvalidDataException>( () => OriginalEconomyImport.Apply( park, Import( Template( settings, body ) ) ) );
			Assert.AreEqual( before, ParkSaveFile.Serialize( park ), "failure of even the final selected ring cannot partially apply bank or histories" );
		}
		Assert.ThrowsException<InvalidDataException>( () => OriginalEconomyImport.Apply( park, Import( Template( settings, DistinctBody()[..^1] ) ) ),
			"the referenced fixed-size analyser is truncated even when later payload blocks provide extra bytes" );
		Assert.AreEqual( before, ParkSaveFile.Serialize( park ) );
		Assert.AreEqual( hash, Hash( park ) );
		Assert.AreEqual( rng, park.World.State );
	}

	[TestMethod]
	public void RestoringNativeRatingLeavesExistingFinancialYearsCountersAndRngUntouched()
	{
		var park = RecentResearchTests.Park();
		park.Ledger.Post( LedgerCategory.GateTakings, 73 );
		park.Ledger.CloseMonth( 1, 44, 2000 );
		park.Ledger.Post( LedgerCategory.OtherCosts, 11 );
		park.Counters.Add( ParkCounters.Admissions, 7 );
		var ledger = JsonSerializer.Serialize( park.Ledger );
		var year = JsonSerializer.Serialize( park.Ledger.SummariseYear( 0 ) );
		var counters = JsonSerializer.Serialize( park.Counters.Values );
		var hash = Hash( park ); var rng = park.World.State;
		park.StatisticsHistory.RestoreOriginal( Read( DistinctBody() ) );
		Assert.AreEqual( 1507d, park.StatisticsHistory.Rating.Oldest().Last() );
		Assert.AreEqual( 44, park.Ledger.History.Single().ParkRating );
		Assert.AreEqual( ledger, JsonSerializer.Serialize( park.Ledger ) );
		Assert.AreEqual( year, JsonSerializer.Serialize( park.Ledger.SummariseYear( 0 ) ) );
		Assert.AreEqual( counters, JsonSerializer.Serialize( park.Counters.Values ) );
		Assert.AreEqual( hash, Hash( park ) );
		Assert.AreEqual( rng, park.World.State );
	}

	[TestMethod]
	public void OriginalHistoryImportDoesNotFabricateFinanceCountersOrConsumeWorldRandomness()
	{
		var settings = OriginalSaveImportTests.Settings();
		var withHistory = Template( settings, DistinctBody() );
		var withoutHistory = Template( settings, SaveNewParkThings.ParkAnalyser( 0, 0 ) );
		var park = RecentResearchTests.Park( settings: settings );
		var control = RecentResearchTests.Park( settings: settings );
		OriginalEconomyImport.Apply( park, Import( withHistory ) );
		OriginalEconomyImport.Apply( control, Import( withoutHistory ) );
		Assert.AreNotEqual( State( park.StatisticsHistory ), State( control.StatisticsHistory ) );
		Assert.AreEqual( 0, park.Ledger.History.Count );
		Assert.AreEqual( 0, park.Ledger.CurrentTotals.Count );
		Assert.IsNull( park.Ledger.SummariseYear( 0 ) );
		Assert.AreEqual( JsonSerializer.Serialize( control.Ledger ), JsonSerializer.Serialize( park.Ledger ) );
		Assert.AreEqual( JsonSerializer.Serialize( control.Counters ), JsonSerializer.Serialize( park.Counters ) );
		CollectionAssert.AreEqual( control.MonthlyAdmissions.ToArray(), park.MonthlyAdmissions.ToArray() );
		Assert.AreEqual( control.World.State, park.World.State );
		Assert.AreEqual( Hash( control ), Hash( park ), "display history must remain outside the simulation hash" );
		park.AdvanceMonths( 1 ); control.AdvanceMonths( 1 );
		Assert.AreEqual( 1, park.Ledger.History.Count );
		Assert.AreEqual( 8, park.StatisticsHistory.Rating.Head, "exactly one rating append follows the imported head 7" );
		Assert.AreEqual( (double)park.Ledger.History.Single().ParkRating, park.StatisticsHistory.Rating.Oldest().Last() );
		Assert.AreEqual( JsonSerializer.Serialize( control.Ledger ), JsonSerializer.Serialize( park.Ledger ) );
		Assert.AreEqual( control.World.State, park.World.State );
		Assert.AreEqual( Hash( control ), Hash( park ), "subsequent simulation also cannot read the imported display-only values" );
		var beforeControl = Hash( control );
		control.Ledger.Post( LedgerCategory.GateTakings, 1 );
		Assert.AreNotEqual( beforeControl, Hash( control ), "real finance remains a hashed control" );
		beforeControl = Hash( park ); park.World.Restore( park.World.State ^ 1u );
		Assert.AreNotEqual( beforeControl, Hash( park ), "the RNG remains a hashed control" );
	}

	[TestMethod]
	public void ImportedPhysicalStateRoundTripsThroughHostJsonAndContinuesIndependently()
	{
		var park = RecentResearchTests.Park();
		park.StatisticsHistory.RestoreOriginal( Read( DistinctBody() ) );
		var json = ParkSaveFile.Serialize( park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog );
		Assert.AreEqual( State( park.StatisticsHistory ), State( restored.StatisticsHistory ) );
		Assert.AreEqual( (7, true), (restored.StatisticsHistory.Rating.Head, restored.StatisticsHistory.Rating.Wrapped) );
		Assert.AreEqual( (1, false), (restored.StatisticsHistory.RecentStays.Head, restored.StatisticsHistory.RecentStays.Wrapped) );
		Assert.AreEqual( 17, restored.StatisticsHistory.ArrivalsThisMonth );
		park.StatisticsHistory.CloseMonth( 22.75f, 123, 64 );
		restored.StatisticsHistory.CloseMonth( 22.75f, 123, 64 );
		Assert.AreEqual( State( park.StatisticsHistory ), State( restored.StatisticsHistory ) );
		Assert.AreEqual( 0, restored.Ledger.History.Count, "a statistics-only close never creates a financial month" );
		Assert.IsTrue( restored.StatisticsHistory.Capture().Happiness!.Slots.Skip( 4 ).All( value => value == 0 ), "inactive native NaN words never enter host JSON" );
	}
}
