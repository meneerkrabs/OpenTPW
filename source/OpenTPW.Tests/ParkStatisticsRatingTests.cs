using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>Independent rating history, legacy host-save migration and the actual graph consumer, without original assets.</summary>
[TestClass, DoNotParallelize]
public class ParkStatisticsRatingTests
{
	private Logger previousLog = null!;
	[TestInitialize]
	public void Setup() => previousLog = Log;
	[TestCleanup]
	public void Cleanup() => Log = previousLog;

	private static string State( ParkStatisticsHistory history ) => JsonSerializer.Serialize( history.Capture() );
	private static ulong Hash( ParkEconomy park ) => WorldStateHash.Compute( new WorldStateSources { Economy = park } );
	private static ParkEconomy Restore( JsonNode root, ParkEconomy template ) =>
		ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( root.ToJsonString() ) ), template.Settings, template.Catalog );
	private static ParkEconomy RoundTrip( ParkEconomy park ) => Restore( JsonNode.Parse( ParkSaveFile.Serialize( park ) )!, park );

	private static JsonObject SaveWithLedger( ParkEconomy park, params int[] ratings )
	{
		park.AdvanceMonths( ratings.Length );
		var root = JsonNode.Parse( ParkSaveFile.Serialize( park ) )!.AsObject();
		for ( var index = 0; index < ratings.Length; index++ )
			root["Ledger"]!["History"]![index]!["ParkRating"] = ratings[index];
		return root;
	}

	private static ParkStatisticsData WithRating( ParkStatisticsData data, RingData? rating, int? arrivalsThisMonth = null ) => new()
	{
		ArrivalsThisMonth = arrivalsThisMonth ?? data.ArrivalsThisMonth,
		Arrivals = data.Arrivals, Happiness = data.Happiness, Stay = data.Stay, People = data.People,
		RecentStays = data.RecentStays, Rating = rating
	};

	private static RingData Ring( int head = 0, bool wrapped = false, int size = ParkStatisticsHistory.MonthsKept ) =>
		new() { Slots = Enumerable.Range( 0, size ).Select( index => (double)(index + 10) ).ToList(), Head = head, Wrapped = wrapped };

	[TestMethod]
	public void WrappedRatingKeepsItsPhysicalSlotsAndHeadThroughJsonAndContinuation()
	{
		var park = RecentResearchTests.Park();
		park.StatisticsHistory.Restore( WithRating( park.StatisticsHistory.Capture(), Ring( 37, true ) ) );
		var before = State( park.StatisticsHistory );
		var restored = RoundTrip( park );
		Assert.AreEqual( before, State( restored.StatisticsHistory ) );
		Assert.AreEqual( (144, 37, true), (restored.StatisticsHistory.Rating.Count, restored.StatisticsHistory.Rating.Head, restored.StatisticsHistory.Rating.Wrapped) );
		CollectionAssert.AreEqual( Enumerable.Range( 48, 106 ).Concat( Enumerable.Range( 10, 38 ) ).Select( rating => (double)rating ).ToArray(),
			restored.StatisticsHistory.Rating.Oldest().ToArray(), "physical slots are not replayed into a newly aligned ring" );
		Assert.AreEqual( 0, restored.Ledger.History.Count, "a display-only rating history does not invent financial months" );
		park.StatisticsHistory.CloseMonth( 61.5f, 40, 83 );
		restored.StatisticsHistory.CloseMonth( 61.5f, 40, 83 );
		Assert.AreEqual( State( park.StatisticsHistory ), State( restored.StatisticsHistory ) );
		Assert.AreEqual( (38, true), (restored.StatisticsHistory.Rating.Head, restored.StatisticsHistory.Rating.Wrapped) );
		Assert.IsTrue( restored.StatisticsHistory.Rating.TryGet( 0, out var newest ) && newest == 83 );
	}

	[TestMethod]
	public void AStatisticsBlockWithoutRatingMigratesLedgerValuesOnceInOldestFirstOrder()
	{
		var park = RecentResearchTests.Park();
		var root = SaveWithLedger( park, 13, 27, 41 );
		root["StatisticsHistory"]!.AsObject().Remove( "Rating" );
		var restored = Restore( root, park );
		CollectionAssert.AreEqual( new double[] { 13, 27, 41 }, restored.StatisticsHistory.Rating.Oldest().ToArray() );
		Assert.AreEqual( 3, restored.StatisticsHistory.People.Count, "existing non-rating histories survive migration" );
		Assert.AreEqual( root["Ledger"]!.ToJsonString(), JsonNode.Parse( ParkSaveFile.Serialize( restored ) )!["Ledger"]!.ToJsonString() );
		var migrated = State( restored.StatisticsHistory );
		for ( var save = 0; save < 3; save++ )
		{
			restored = RoundTrip( restored );
			Assert.AreEqual( migrated, State( restored.StatisticsHistory ), "a new-format save must not replay the fallback again" );
		}
	}

	[TestMethod]
	public void AnAbsentStatisticsBlockMigratesOnlyRatingAndAnEmptyOldParkStaysEmpty()
	{
		var park = RecentResearchTests.Park();
		var root = SaveWithLedger( park, 19, 31 );
		root.Remove( "StatisticsHistory" );
		var restored = Restore( root, park );
		CollectionAssert.AreEqual( new double[] { 19, 31 }, restored.StatisticsHistory.Rating.Oldest().ToArray() );
		Assert.IsFalse( restored.StatisticsHistory.IsEmpty );
		Assert.AreEqual( (0, 0, 0, 0, 0, 0), (restored.StatisticsHistory.Arrivals.Count, restored.StatisticsHistory.People.Count,
			restored.StatisticsHistory.Happiness.Count, restored.StatisticsHistory.Stay.Count, restored.StatisticsHistory.RecentStays.Count, restored.StatisticsHistory.ArrivalsThisMonth) );
		Assert.AreEqual( State( restored.StatisticsHistory ), State( RoundTrip( restored ).StatisticsHistory ) );
		var fresh = RecentResearchTests.Park();
		var empty = JsonNode.Parse( ParkSaveFile.Serialize( fresh ) )!.AsObject();
		empty.Remove( "StatisticsHistory" );
		Assert.IsTrue( Restore( empty, fresh ).StatisticsHistory.IsEmpty );
	}

	[TestMethod]
	public void LegacyFallbackTakesOnlyTheNewest144ValuesAndDoesNotReplaceAnExplicitRating()
	{
		var history = new ParkStatisticsHistory();
		var legacy = Enumerable.Range( 1, 173 ).ToArray();
		history.Restore( WithRating( history.Capture(), null ), legacy );
		CollectionAssert.AreEqual( Enumerable.Range( 30, 144 ).Select( rating => (double)rating ).ToArray(), history.Rating.Oldest().ToArray() );
		Assert.AreEqual( 144, history.Rating.Count );
		var captured = history.Capture();
		history.Restore( captured, new[] { 999 } );
		CollectionAssert.AreEqual( Enumerable.Range( 30, 144 ).Select( rating => (double)rating ).ToArray(), history.Rating.Oldest().ToArray() );
		Assert.AreEqual( 0, history.People.Count, "fallback does not fabricate any other series" );
	}

	[TestMethod]
	public void ExplicitlyEmptyRatingWithPopulatedLedgerStaysEmptyAcrossMultipleSaves()
	{
		var park = RecentResearchTests.Park();
		var root = SaveWithLedger( park, 17, 29, 43 );
		root["StatisticsHistory"] = JsonSerializer.SerializeToNode( new ParkStatisticsHistory().Capture() );
		var restored = Restore( root, park );
		for ( var save = 0; save < 4; save++ )
		{
			Assert.IsTrue( restored.StatisticsHistory.IsEmpty );
			Assert.AreEqual( 0, restored.StatisticsHistory.Rating.Count );
			Assert.AreEqual( 3, restored.Ledger.History.Count );
			var captured = ParkSaveFile.Capture( restored );
			Assert.IsNotNull( captured.StatisticsHistory, "omitting an explicitly empty block would reactivate the legacy fallback" );
			Assert.IsNotNull( captured.StatisticsHistory!.Rating );
			Assert.AreEqual( -1, captured.StatisticsHistory.Rating!.Head );
			restored = RoundTrip( restored );
		}
		restored.AdvanceMonths( 1 );
		Assert.AreEqual( 1, restored.StatisticsHistory.Rating.Count, "a close adds the current rating once without backfilling the ledger" );
		Assert.AreEqual( 4, restored.Ledger.History.Count );
		Assert.IsTrue( restored.StatisticsHistory.Rating.TryGet( 0, out var rating ) );
		Assert.AreEqual( (double)restored.Ledger.History[^1].ParkRating, rating );
	}

	[TestMethod]
	public void RatingOnlyHistoryIsNotEmptyAndCaptureAndRestoreOwnTheirSlots()
	{
		var history = new ParkStatisticsHistory();
		Assert.IsTrue( history.IsEmpty );
		var input = WithRating( history.Capture(), Ring( 0 ) );
		history.Restore( input );
		Assert.IsFalse( history.IsEmpty );
		input.Rating!.Slots[0] = 400;
		Assert.IsTrue( history.Rating.TryGet( 0, out var rating ) && rating == 10, "restore must copy mutable input" );
		var captured = history.Capture();
		captured.Rating!.Slots[0] = 500;
		Assert.IsTrue( history.Rating.TryGet( 0, out rating ) && rating == 10, "capture must not expose the live array" );
		Assert.AreEqual( 0, history.Arrivals.Count );
		var park = RecentResearchTests.Park();
		park.StatisticsHistory.Restore( history.Capture() );
		Assert.IsNotNull( ParkSaveFile.Capture( park ).StatisticsHistory );
		Assert.IsTrue( RoundTrip( park ).StatisticsHistory.Rating.TryGet( 0, out rating ) && rating == 10 );
	}

	[TestMethod]
	public void MalformedRatingIsRejectedBeforeAnyLiveHistoryChanges()
	{
		var history = new ParkStatisticsHistory();
		history.RecordArrival(); history.RecordStay( 4 ); history.CloseMonth( 55.25f, 12, 67 ); history.RecordArrival();
		var before = State( history );
		var invalid = new List<RingData> { Ring( size: 143 ), Ring( size: 145 ), Ring( -2 ), Ring( 144 ), Ring( -1, true ),
			new() { Slots = null!, Head = 0, Wrapped = false } };
		foreach ( var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity } )
		{
			var ring = Ring(); ring.Slots[^1] = value; invalid.Add( ring );
		}
		foreach ( var ring in invalid )
		{
			var data = WithRating( new ParkStatisticsHistory().Capture(), ring, arrivalsThisMonth: 99 );
			Assert.IsFalse( data.IsValid );
			Assert.ThrowsException<InvalidDataException>( () => history.Restore( data, new[] { 90, 91 } ) );
			Assert.AreEqual( before, State( history ), "validation must finish before replacing the accumulator or any ring" );
		}
	}

	private static IEnumerable<int> InterruptedLegacyRatings()
	{
		yield return 13;
		throw new InvalidOperationException( "Synthetic legacy source failure." );
	}

	[TestMethod]
	public void InterruptedLegacyEnumerationLeavesEveryExistingHistoryUntouched()
	{
		var history = new ParkStatisticsHistory();
		history.RecordStay( 6 ); history.RecordArrival(); history.CloseMonth( 42.5f, 9, 73 ); history.RecordArrival();
		var before = State( history );
		var missingRating = WithRating( new ParkStatisticsHistory().Capture(), null, arrivalsThisMonth: 99 );
		Assert.ThrowsException<InvalidOperationException>( () => history.Restore( missingRating, InterruptedLegacyRatings() ) );
		Assert.AreEqual( before, State( history ), "fallback must finish enumerating before publishing any restored state" );
		history.Restore( history.Capture(), InterruptedLegacyRatings() );
		Assert.AreEqual( before, State( history ), "an explicit rating ring must not enumerate the legacy source" );
	}

	[TestMethod]
	public void MalformedPresentRatingJsonIsRejectedInsteadOfUsingLegacyFallback()
	{
		var park = RecentResearchTests.Park();
		var valid = SaveWithLedger( park, 17 ).ToJsonString();
		var mutations = new Action<JsonObject>[]
		{
			root => root["StatisticsHistory"]!["Rating"] = new JsonArray(),
			root => root["StatisticsHistory"]!["Rating"] = new JsonObject(),
			root => root["StatisticsHistory"]!["Rating"]!["Slots"] = new JsonArray( 1, 2 ),
			root => root["StatisticsHistory"]!["Rating"]!["Slots"] = null,
			root => root["StatisticsHistory"]!["Rating"]!["Head"] = 144,
			root => { root["StatisticsHistory"]!["Rating"]!["Head"] = -1; root["StatisticsHistory"]!["Rating"]!["Wrapped"] = true; },
			root => root["StatisticsHistory"]!["Rating"]!["Slots"]![0] = "NaN",
			root => root["StatisticsHistory"]!["Rating"]!["Slots"]![143] = JsonNode.Parse( "1e400" ),
		};
		foreach ( var mutate in mutations )
		{
			var root = JsonNode.Parse( valid )!.AsObject(); mutate( root );
			Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( root.ToJsonString() ) ) );
		}
		foreach ( var member in new[] { "Slots", "Head", "Wrapped" } )
		{
			var root = JsonNode.Parse( valid )!;
			root["StatisticsHistory"]!["Rating"]!.AsObject().Remove( member );
			Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( root.ToJsonString() ) ), member );
		}
	}

	[TestMethod]
	public void GraphRingAndPointsUseDedicatedRatingInsteadOfFinancialHistory()
	{
		var park = RecentResearchTests.Park();
		var root = SaveWithLedger( park, 7, 9, 11 );
		var restored = Restore( root, park );
		restored.StatisticsHistory.Restore( new ParkStatisticsHistory().Capture() );
		restored.StatisticsHistory.Rating.Add( 20 ); restored.StatisticsHistory.Rating.Add( 80 );
		Assert.AreSame( restored.StatisticsHistory.Rating, ParkInfoGraph.Ring( ParkInfoSeries.Rating, restored.StatisticsHistory ) );
		CollectionAssert.AreEqual( new[] { 0, 20, 80 }, ParkStatisticsHistory.SeriesPoints( ParkInfoGraph.Ring( ParkInfoSeries.Rating, restored.StatisticsHistory ), 0, false ) );
		Assert.AreEqual( 80, ParkInfoGraph.ScaleMaximum( ParkInfoSeries.Rating, 0, series => ParkInfoGraph.Ring( series, restored.StatisticsHistory ) ) );
		CollectionAssert.AreEqual( new[] { 7, 9, 11 }, restored.Ledger.History.Select( month => month.ParkRating ).ToArray() );
	}

	private static NativeParkStatistics NativeRating()
	{
		var body = SaveNewParkThings.ParkAnalyser( 0, 0 );
		var offset = SaveParkStatistics.MonthlyOffset + 15 * SaveParkStatistics.MonthlyBytes;
		BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( offset, 4 ), 1 );
		BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( offset + 13, 4 ), 20 );
		BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( offset + 17, 4 ), 80 );
		return SaveParkStatistics.Read( body, new SaveThing( 5, SaveThingModel.ParkAnalyser, 0, 0, 0, body.Length ) );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RepeatedGraphDrawsAndReopensUseDedicatedRatingWithoutChangingThePark( bool populatedLedger )
	{
		var park = RecentResearchTests.Park();
		if ( populatedLedger )
			park = Restore( SaveWithLedger( park, 7, 9, 11 ), park );
		park.StatisticsHistory.RestoreOriginal( NativeRating() );
		var hud = StaffWindowTests.Hud( park );
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, "0123456789".Select( digit => new FontAtlasTests.SyntheticGlyph( digit, 2, 2, 0, 0, 3 ) ).ToArray() ) );
		var context = new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) )
			{ Canvas = new UiCanvas( 1024, 768 ) };
		var snapshot = ParkSaveFile.Serialize( park );
		var random = park.World.State;
		var hash = Hash( park );
		UiVertex[]? firstFrame = null;
		for ( var opening = 0; opening < 3; opening++ )
		{
			hud.OpenParkInfoWindow();
			hud.SetParkInfoSeries( ParkInfoSeries.People, false );
			hud.SetParkInfoSeries( ParkInfoSeries.Rating, true );
			for ( var frame = 0; frame < 2; frame++ )
			{
				context.Batch.Clear();
				hud.Stack.Top!.Find( "graph" )!.Draw( context, false, false );
				Assert.AreEqual( "80", context.Batch.Texts.Last().Text, "the live graph label must come from the dedicated rating ring" );
				var orange = context.Batch.Draws.Where( draw => draw.Texture == UiTexture.Solid ).SelectMany( draw => draw.Vertices )
					.Where( vertex => vertex.Color.Equals( ParkInfoGraph.Colour( ParkInfoSeries.Rating ) ) ).ToArray();
				Assert.AreEqual( 60, orange.Length, "two rating segments and their four-sided sample marks must be drawn even with an empty ledger" );
				var vertices = context.Batch.Draws.SelectMany( draw => draw.Vertices ).ToArray();
				if ( firstFrame == null ) firstFrame = vertices;
				else CollectionAssert.AreEqual( firstFrame, vertices, "drawing and reopening must not migrate or append samples" );
			}
			hud.CloseParkInfoWindow();
		}
		Assert.AreEqual( snapshot, ParkSaveFile.Serialize( park ) );
		Assert.AreEqual( random, park.World.State );
		Assert.AreEqual( hash, Hash( park ) );
		Assert.AreEqual( populatedLedger ? 3 : 0, park.Ledger.History.Count );
	}

	private sealed class ChangingCensus : IParkGuestStatistics
	{
		private int reads;
		public int PeopleInPark => 100;
		public int GuestCensus => 100 + 100 * (reads++ % 5);
		public float AverageHappiness => 50;
		public int CountHappierThan( int happiness ) => 0;
		public int KidsWithBalloonsPercent => 0;
		public int KidsWithCostumesPercent => 0;
	}

	[TestMethod]
	public void FirstPostResumeCloseAppendsExactlyOnceAndSharesTheLedgersRatingSample()
	{
		var park = RecentResearchTests.Park();
		var root = SaveWithLedger( park, 13, 27, 41 );
		root["StatisticsHistory"]!.AsObject().Remove( "Rating" );
		var restored = Restore( root, park );
		restored.GuestStatistics = new ChangingCensus();
		for ( var close = 0; close < 2; close++ )
		{
			var oldRatings = restored.StatisticsHistory.Rating.Oldest().ToArray();
			var oldCount = restored.Ledger.History.Count;
			restored.AdvanceMonths( 1 );
			Assert.AreEqual( oldCount + 1, restored.Ledger.History.Count );
			Assert.AreEqual( oldRatings.Length + 1, restored.StatisticsHistory.Rating.Count );
			CollectionAssert.AreEqual( oldRatings, restored.StatisticsHistory.Rating.Oldest().Take( oldRatings.Length ).ToArray() );
			Assert.IsTrue( restored.StatisticsHistory.Rating.TryGet( 0, out var newest ) );
			Assert.AreEqual( (double)restored.Ledger.History[^1].ParkRating, newest,
				"a changing census exposes separately evaluated rating getters at the same month close" );
			var saved = State( restored.StatisticsHistory );
			restored = RoundTrip( restored );
			Assert.AreEqual( saved, State( restored.StatisticsHistory ) );
			restored.GuestStatistics = new ChangingCensus();
		}
	}

	[TestMethod]
	public void DisplayHistoryMigrationLeavesRandomStateWorldHashAndLedgerUntouched()
	{
		var park = RecentResearchTests.Park();
		var root = SaveWithLedger( park, 13, 27, 41 );
		var reference = Restore( root, park );
		root["StatisticsHistory"]!.AsObject().Remove( "Rating" );
		var migrated = Restore( root, park );
		Assert.AreNotEqual( State( reference.StatisticsHistory ), State( migrated.StatisticsHistory ), "the fixture must actually exercise migration" );
		Assert.AreEqual( reference.World.State, migrated.World.State );
		Assert.AreEqual( Hash( reference ), Hash( migrated ) );
		Assert.AreEqual( JsonSerializer.Serialize( ParkSaveFile.Capture( reference ).Ledger ), JsonSerializer.Serialize( ParkSaveFile.Capture( migrated ).Ledger ) );
		var before = Hash( migrated );
		var random = migrated.World.State;
		migrated.StatisticsHistory.Restore( WithRating( migrated.StatisticsHistory.Capture(), Ring( 21, true ) ) );
		Assert.AreEqual( before, Hash( migrated ), "changing only imported display history stays outside the simulation hash" );
		Assert.AreEqual( random, migrated.World.State );
		migrated.Ledger.Post( LedgerCategory.GateTakings, 1 );
		Assert.AreNotEqual( before, Hash( migrated ), "financial state remains covered by the hash" );
	}
}
