using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// The pools extension's objects in the economy (docs/POOLS.md, docs/MODS.md "research": false): the changing rooms are never research items, so a park without a pool or a room has the same research
/// tables, completed set, group statistics and saved bytes with the extension on or off, and the room is on offer from the start. The catalogue is the real one: a stand-in game folder plus the
/// extension's shipped <c>content/pools</c>, loaded through the mod roots before the economy is built.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PoolsResearchTests
{
	private string root = null!;
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		root = Directory.CreateTempSubdirectory( "opentpw-pools-research-" ).FullName;
		previousFileSystem = FileSystem;
		PoolContent.DirectoryOverride = RepositoryFolder();
		// a ride that is a research project (category 0, group 1, 300 points), a ride that starts researched, a feature that does
		WriteObject( "rides", "totem", "Info.Id\t1110\nInfo.Name\t\"Inca Totem\"\nInfo.WhichUIType\t0\nResearch.Category\t0\nResearch.Group\t1\nUpgrades[0].CostOfUpgrade\t100\nUpgrades[0].CostOfResearch\t300\n" );
		WriteObject( "rides", "bouncy", "Info.Id\t1100\nInfo.Name\t\"Belly Bounce\"\nInfo.WhichUIType\t0\nResearch.Category\t0\nResearch.Group\t0\nUpgrades[0].CostOfUpgrade\t100\nUpgrades[0].CostOfResearch\t0\n" );
		WriteObject( "features", "loo", "Info.Id\t1402\nInfo.Name\t\"Small Toilet\"\nInfo.WhichUIType\t3\nResearch.Category\t1\nResearch.Group\t0\nUpgrades[0].CostOfUpgrade\t100\nUpgrades[0].CostOfResearch\t0\n" );
		FileSystem = new BaseFileSystem( Path.Combine( root, "game" ) );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup()
	{
		PoolContent.DirectoryOverride = null;
		FileSystem = previousFileSystem!;
		Directory.Delete( root, true );
	}

	private void WriteObject( string category, string name, string sam )
	{
		var folder = Path.Combine( root, "game", "levels", "jungle", category, name );
		Directory.CreateDirectory( folder );
		File.WriteAllText( Path.Combine( folder, name + ".sam" ), sam );
		File.WriteAllBytes( Path.Combine( folder, name + ".MD2" ), Array.Empty<byte>() );
	}

	private static string RepositoryFolder()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var candidate = Path.Combine( directory.FullName, "content", "pools" );
			if ( File.Exists( Path.Combine( candidate, "objects.json" ) ) && Directory.Exists( Path.Combine( directory.FullName, "tools", "pools" ) ) )
				return candidate;
		}
		throw new FileNotFoundException( "content/pools not found above the test binaries." );
	}

	/// <summary>The economy of a jungle park whose catalogue was loaded with the extension's content roots in place (or not), built after them.</summary>
	private ParkEconomy Park( bool pools )
	{
		using var scope = ModContentRoots.Use( PoolsTests.PoolsSet( pools ), FileSystem );
		return new ParkEconomy( EconomyTestData.Settings(), EconomyObjectCatalog.Load( "jungle", false ), ParkGameMode.FullSimulation, 7 );
	}

	private static string Tables( ParkResearch research ) => string.Join( ";", research.Items.Select( item => $"{item.InfoId}/{item.Level}/{(int)item.Category}/{item.Group}/{item.Cost}/{item.RequiresInfoId}" ) )
		+ "|" + string.Join( ",", research.Completed.OrderBy( key => key.InfoId ).ThenBy( key => key.Level ) )
		+ "|" + string.Join( ",", research.UpgradeTable )
		+ "|" + string.Join( ",", Enum.GetValues<ResearchCategory>().SelectMany( category => Enumerable.Range( 0, ParkResearch.MaximumOpenGroup + 1 ).Select( group => research.PercentResearched( category, group ) ).Append( research.HighestOpenGroup( category ) ) ) )
		+ "|" + research.IsAllResearched;

	[TestMethod]
	public void TheChangingRoomIsListedButIsNoResearchItemWhateverTheExtensionDoes()
	{
		var off = Park( false );
		var on = Park( true );
		Assert.IsFalse( off.Catalog.TryGet( 65500, out _ ), "the room is not in the catalogue with the extension off" );
		Assert.IsTrue( on.Catalog.TryGet( 65500, out var room ), "and is with it on" );
		Assert.IsTrue( room.NotResearched );
		Assert.IsTrue( on.Catalog.Objects.Where( info => info.InfoId is >= 65500 and <= 65503 ).All( info => info.NotResearched ) );
		// the research tables, the completed set, the group statistics and the highest open groups are identical
		Assert.AreEqual( Tables( off.Research ), Tables( on.Research ) );
		Assert.IsFalse( on.Research.Items.Any( item => item.InfoId == 65500 ) );
		Assert.IsFalse( on.Research.Completed.Any( key => key.InfoId == 65500 ) );
		Assert.IsTrue( on.Research.Items.Any( item => item.InfoId == 1110 ), "the fixture holds a real research project" );
	}

	[TestMethod]
	public void TheChangingRoomIsOnOfferFromTheStartAndBuyable()
	{
		var on = Park( true );
		Assert.IsTrue( on.Research.IsAvailable( 65500 ) );
		Assert.IsFalse( on.Research.IsAvailable( 1110 ), "an ordinary project is not" );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, on.CheckBuild( 65500 ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, on.TryBuild( 65500, out var built ) );
		Assert.IsNotNull( built );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotResearched, on.CheckBuild( 1110 ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.UnknownObject, Park( false ).CheckBuild( 65500 ) );
	}

	[TestMethod]
	public void AParkWithoutAPoolSavesTheSameResearchBytesWithTheExtensionOnAndOff()
	{
		var off = ParkSaveFile.Serialize( Park( false ) );
		var on = ParkSaveFile.Serialize( Park( true ) );
		Assert.AreEqual( off, on, "the economy part of a park without pools is the same bytes" );
		Assert.IsFalse( on.Contains( "65500", StringComparison.Ordinal ), "no pools id in the saved research" );
	}

	[TestMethod]
	public void AParkSavedWithTheExtensionOnAndNoPoolLoadsWithItOffAndTheOtherWayRound()
	{
		var bytes = Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( Park( true ) ) );
		using ( ModContentRoots.Use( PoolsTests.PoolsSet( false ), FileSystem ) )
		{
			var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( bytes ), EconomyTestData.Settings(), EconomyObjectCatalog.Load( "jungle", false ) );
			Assert.IsFalse( restored.Catalog.TryGet( 65500, out _ ) );
		}
		var offBytes = Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( Park( false ) ) );
		using ( ModContentRoots.Use( PoolsTests.PoolsSet( true ), FileSystem ) )
		{
			var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( offBytes ), EconomyTestData.Settings(), EconomyObjectCatalog.Load( "jungle", false ) );
			Assert.IsTrue( restored.Research.IsAvailable( 65500 ), "an extension-off save loaded with it on still has the room on offer" );
			Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, restored.CheckBuild( 65500 ) );
		}
	}

	[TestMethod]
	public void TheGoldenTicketForEverythingBuiltDoesNotAskForTheChangingRoom()
	{
		var on = Park( true );
		on.Research.Restore( on.Research.Items.Select( item => (item.InfoId, item.Level) ), Array.Empty<((int InfoId, int Level), long)>(), on.Research.Effort );
		Assert.IsTrue( on.Research.IsAllResearched );
		foreach ( var info in on.Catalog.Objects.Where( info => info.IsBuyable && info.InfoId < 65000 ) )
			on.Counters.Add( ParkCounters.Built( info.InfoId ), 1 );
		var won = on.Objectives.CheckGoldenTickets( on.Counters, on.GuestStatistics, on.Research, 0, Array.Empty<long>() );
		CollectionAssert.Contains( won.ToList(), GoldenTicketKind.AllResearchedAndBuilt, "every ordinary object is built; the room that nobody has built does not hold the ticket back" );
	}

	[TestMethod]
	public void TheResearchFlagOfAnObjectsFileDefaultsToTrueAndIsStrict()
	{
		string Json( string extra ) => "{ \"schema\": 1, " + extra + "\"idRange\": { \"first\": 20000, \"last\": 20099 }, \"objects\": [ { \"archive\": \"objects/zoom\", \"theme\": \"jungle\", \"category\": \"rides\", \"infoId\": 20001 } ] }";
		ModObjectsFile Parse( string text ) => ModObjectsFile.Parse( Encoding.UTF8.GetBytes( text ), Array.Empty<string>() );
		Assert.IsTrue( Parse( Json( "" ) ).Objects.Single().Research, "a community mod is researched like the game's objects" );
		Assert.IsTrue( Parse( Json( "\"research\": true, " ) ).Objects.Single().Research );
		Assert.IsFalse( Parse( Json( "\"research\": false, " ) ).Objects.Single().Research );
		Assert.ThrowsException<InvalidDataException>( () => Parse( Json( "\"research\": \"no\", " ) ), "only a boolean" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( Json( "\"research\": false, \"research\": true, " ) ), "not twice" );
	}
}
