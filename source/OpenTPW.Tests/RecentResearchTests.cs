using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic TPW paid-completion events, physical history persistence and presentation-only hash scope.</summary>
[TestClass]
public class RecentResearchTests
{
	internal const int A = 2001, B = 2002, C = 2003, D = 2004, Shop = 3001, OtherShop = 3002, Sideshow = 4001, Feature = 5001, AddOn = 6001, Free = 7001, FreeLevels = 7002;

	private static EconomyObjectInfo Info( int id, ParkObjectKind kind, ResearchCategory category, int ui, int[] costs, int target = 0 ) =>
		new( id, $"Item {id}", kind, category, 0, costs.Select( (cost, level) => new UpgradeLevelInfo( level, 100, cost, 0, 0, new[] { 50, 30, 20, 10 } ) ).ToArray(),
			null, null, null, 0, 0, 0, target, 0, 10, $"synthetic/{id}.wad", WhichUIType: ui );

	internal static ParkEconomy Park( EconomyRules? rules = null, BalanceSettings? settings = null )
	{
		Log ??= new();
		var catalog = new EconomyObjectCatalog( new[]
		{
			Info( A, ParkObjectKind.Ride, ResearchCategory.Ride, 0, new[] { 10, 10, 10 } ),
			Info( B, ParkObjectKind.Ride, ResearchCategory.Ride, 0, new[] { 10 } ),
			Info( C, ParkObjectKind.Ride, ResearchCategory.Ride, 0, new[] { 10 } ),
			Info( D, ParkObjectKind.Ride, ResearchCategory.Ride, 0, new[] { 10 } ),
			Info( Shop, ParkObjectKind.Shop, ResearchCategory.Shop, 1, new[] { 10, 10, 10 } ),
			Info( OtherShop, ParkObjectKind.Shop, ResearchCategory.Shop, 1, new[] { 10 } ),
			Info( Sideshow, ParkObjectKind.Sideshow, ResearchCategory.Sideshow, 2, new[] { 10 } ),
			Info( Feature, ParkObjectKind.Feature, ResearchCategory.Feature, 3, new[] { 10 } ),
			Info( AddOn, ParkObjectKind.Upgrade, ResearchCategory.Upgrade, 0, new[] { 10 }, A ),
			Info( Free, ParkObjectKind.Ride, ResearchCategory.Ride, 0, new[] { 0, 0, 0 } ),
			Info( FreeLevels, ParkObjectKind.Ride, ResearchCategory.Ride, 0, new[] { 10, 0, 0 } ),
			Info( 8000, ParkObjectKind.Ride, ResearchCategory.Ride, -1, new[] { 10 } ),
			Info( 8001, ParkObjectKind.Ride, ResearchCategory.Ride, 4, new[] { 10 } ),
		} );
		return new ParkEconomy( settings ?? EconomyTestData.Settings(), catalog, ParkGameMode.FullSimulation, 7, rules );
	}

	internal static IReadOnlyList<ResearchItem> Complete( ParkResearch research, int id, int level = 0 )
	{
		var project = research.Items.Single( item => item.InfoId == id && item.Level == level );
		foreach ( var category in Enum.GetValues<ResearchCategory>() )
			research.SetEffort( category, category == project.Category ? 100 : 0 );
		research.SetWorkload( 100 );
		Assert.IsTrue( research.TrySelect( project.Category, id, level ), $"{id}/{level} selectable" );
		var done = research.AddResearcherPoints( 10 );
		Assert.IsTrue( research.Completed.Contains( (id, level) ) );
		return done;
	}

	internal static string State( ParkResearch research ) => JsonSerializer.Serialize( research.RecentItems );

	internal static RecentResearchRing[] RawRings() => new[]
	{
		new RecentResearchRing( 1, 3, 1, 1234, new[] { A, A, C } ),
		new RecentResearchRing( 0, 3, 0, 7, new[] { Shop, 65535, 42 } ),
		new RecentResearchRing( -1, 3, 0, 0xBEEF, new[] { Sideshow, 12, 65535 } ),
		new RecentResearchRing( 2, 3, 0, 65535, new[] { 1, 2, Feature } ),
	};

	[TestMethod]
	public void PaidCompletionsWrapInEventOrderAndDuplicateLevelsConsumeSlots()
	{
		var research = Park().Research;
		Assert.IsTrue( research.RecentItems.All( ring => ring.Head == -1 ) );
		Complete( research, A ); Complete( research, B ); Complete( research, C );
		Assert.AreEqual( (2, 0), (research.RecentItems[0].Head, research.RecentItems[0].Wrapped) );
		CollectionAssert.AreEqual( new[] { A, B, C }, research.RecentItems[0].Values.ToArray() );
		Complete( research, A, 1 ); Complete( research, A, 2 );
		CollectionAssert.AreEqual( new[] { A, A, C }, research.RecentItems[0].Values.ToArray() );
		Assert.AreEqual( (1, 1), (research.RecentItems[0].Head, research.RecentItems[0].Wrapped) );
		Assert.IsTrue( research.IsRecentlyResearched( 0, A ) );
		Assert.IsTrue( research.IsRecentlyResearched( 0, C ) );
		Assert.IsFalse( research.IsRecentlyResearched( 0, B ), "deduplication would incorrectly keep B" );
		Complete( research, D );
		CollectionAssert.AreEqual( new[] { A, A, D }, research.RecentItems[0].Values.ToArray() );
	}

	[TestMethod]
	public void CategoryRoutingUsesWhichUITypeIncludingPaidAddOnsAndShopLevels()
	{
		var research = Park().Research;
		Complete( research, A ); Complete( research, B ); Complete( research, C );
		Complete( research, Shop ); Complete( research, Shop, 1 );
		Complete( research, Sideshow ); Complete( research, Feature );
		CollectionAssert.AreEqual( new[] { A, B, C }, research.RecentItems[0].Values.ToArray() );
		CollectionAssert.AreEqual( new[] { Shop, Shop, 0 }, research.RecentItems[1].Values.ToArray() );
		Complete( research, Shop, 2 ); Complete( research, OtherShop );
		CollectionAssert.AreEqual( new[] { OtherShop, Shop, Shop }, research.RecentItems[1].Values.ToArray() );
		CollectionAssert.AreEqual( new[] { A, B, C }, research.RecentItems[0].Values.ToArray(), "a fourth shop event cannot evict a ride" );
		Assert.IsTrue( research.IsRecentlyResearched( 2, Sideshow ) );
		Assert.IsTrue( research.IsRecentlyResearched( 3, Feature ) );
		Complete( research, AddOn );
		CollectionAssert.AreEqual( new[] { AddOn, B, C }, research.RecentItems[0].Values.ToArray() );
		Assert.IsFalse( research.IsRecentlyResearched( 0, A ), "an add-on takes a ride slot even though the ordinary buy list filters it" );
		var before = State( research );
		Complete( research, 8000 ); Complete( research, 8001 );
		Assert.AreEqual( before, State( research ), "no directory fallback for a missing or unsupported WhichUIType" );
	}

	[TestMethod]
	public void InitialAndPropagatedFreeLevelsDoNotRecordEventsAndTpiDoesNotAdoptThePolicy()
	{
		var research = Park().Research;
		Assert.IsTrue( research.Completed.Contains( (Free, 2) ) );
		Assert.IsFalse( research.IsRecentlyResearched( 0, Free ) );
		var done = Complete( research, FreeLevels );
		Assert.AreEqual( 3, done.Count, "base completion propagates two free levels" );
		Assert.AreEqual( 1, research.LastResearched.Count );
		Assert.AreEqual( 0, research.RecentItems[0].Head, "only the paid base consumes a slot" );
		Assert.IsFalse( research.IsRecentlyResearched( -1, FreeLevels ) );
		Assert.IsFalse( research.IsRecentlyResearched( 4, FreeLevels ) );
		var tpi = Park( EconomyRules.ThemeParkIncMoney ).Research;
		Complete( tpi, A );
		Assert.IsTrue( tpi.RecentItems.All( ring => ring.Head == -1 ) );
		tpi.RestoreRecentItems( RawRings() );
		Assert.IsFalse( tpi.IsRecentlyResearched( 0, A ), "an imported host ring does not opt TPI into an untraced policy" );
	}

	[TestMethod]
	public void PhysicalStateSurvivesJsonAndContinuationWithoutInferringChronology()
	{
		var park = Park(); Complete( park.Research, A );
		park.Research.RestoreRecentItems( RawRings() );
		var json = ParkSaveFile.Serialize( park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog );
		Assert.AreEqual( State( park.Research ), State( restored.Research ) );
		Assert.IsFalse( restored.Research.IsRecentlyResearched( 1, 65535 ), "inactive values are opaque, not recent" );
		Assert.IsFalse( restored.Research.IsRecentlyResearched( 2, Sideshow ), "head -1 has no active slots" );
		Complete( park.Research, D ); Complete( restored.Research, D );
		Assert.AreEqual( State( park.Research ), State( restored.Research ) );
		Assert.AreEqual( 1234, restored.Research.RecentItems[0].Temp, "append preserves the serialized scratch word" );
		var node = JsonNode.Parse( json )!;
		node["Research"]!.AsObject().Remove( "RecentItems" );
		var old = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ), park.Settings, park.Catalog );
		Assert.IsTrue( old.Research.Completed.Contains( (A, 0) ) );
		Assert.IsTrue( old.Research.RecentItems.All( ring => ring.Head == -1 ) );
		Assert.IsFalse( old.Research.IsRecentlyResearched( 0, A ), "sorted completed keys cannot recover history" );
	}

	[TestMethod]
	public void CaptureAndRestoreDoNotShareMutableRingArrays()
	{
		var research = Park().Research;
		var input = RawRings(); research.RestoreRecentItems( input );
		((int[])input[0].Values)[0] = 42;
		Assert.AreEqual( A, research.RecentItems[0].Values[0] );
		var captured = research.RecentItems;
		((int[])captured[0].Values)[0] = 43;
		Assert.AreEqual( A, research.RecentItems[0].Values[0] );
	}

	[TestMethod]
	public void OnlyThePresentationPropertyIsExcludedFromTheWorldHash()
	{
		var park = Park();
		ulong Hash() => WorldStateHash.Compute( new WorldStateSources { Economy = park } );
		var before = Hash(); var json = ParkSaveFile.Serialize( park );
		park.Research.RestoreRecentItems( RawRings() );
		Assert.AreEqual( before, Hash() );
		Assert.AreNotEqual( json, ParkSaveFile.Serialize( park ), "all physical history remains in host saves" );
		park.Research.Restore( park.Research.Completed.Append( (A, 0) ).ToArray(), Array.Empty<((int, int), long)>(), park.Research.Effort );
		Assert.AreNotEqual( before, Hash(), "completed-project controls remain hashed" );
		park = Park(); before = Hash();
		park.Research.Restore( park.Research.Completed.ToArray(), new[] { ((A, 0), 1L) }, park.Research.Effort );
		Assert.AreNotEqual( before, Hash(), "progress-only control remains hashed" );
		park = Park(); before = Hash(); park.Research.SetEffort( ResearchCategory.Ride, 99 );
		Assert.AreNotEqual( before, Hash(), "effort-only control remains hashed" );
	}

	[TestMethod]
	public void MalformedPresentJsonHistoryIsRejected()
	{
		var json = ParkSaveFile.Serialize( Park() );
		foreach ( var bad in new[] { "[]", "[null,null,null,null]", "{}" } )
		{
			var root = JsonNode.Parse( json )!; root["Research"]!["RecentItems"] = JsonNode.Parse( bad );
			Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( root.ToJsonString() ) ) );
		}
		foreach ( var field in new[] { "Head", "Capacity", "Wrapped", "Temp", "Values" } )
		{
			var root = JsonNode.Parse( json )!; root["Research"]!["RecentItems"]![0]!.AsObject().Remove( field );
			Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( root.ToJsonString() ) ) );
		}
		var invalid = JsonNode.Parse( json )!; invalid["Research"]!["RecentItems"]![3]!["Values"] = new JsonArray( -1, 2, 3 );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( invalid.ToJsonString() ) ) );
	}
}
