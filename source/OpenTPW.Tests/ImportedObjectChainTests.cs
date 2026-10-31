using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The object chain of an original save (ECON-022, SAVE-006): the world's first-object word and each attraction's <c>mNext</c> give the order the original created its objects in (newest first), which the
/// import keeps for the objects and the economy, and the names the chain's objects carry are what the loaded park draws on its signs.
/// </summary>
[TestClass, DoNotParallelize]
public class ImportedObjectChainTests
{
	private static byte[] Belly( ushort next, string line1 = "Belly", string line2 = "Bounce" ) =>
		SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields { PositionX = 1 * 256 + 128, PositionY = 1 * 256 + 128, Angle = 0, Price = 30, Level = 0, Repair = 80, LifeGauge = 90, Next = next, Line1 = line1, Line2 = line2 } );

	private static OriginalParkImport Park( ushort next, string line1 = "Belly", string line2 = "Bounce" ) =>
		OriginalSaveImportTests.Park( OriginalSaveImportTests.Settings(), bellyBounce: Belly( next, line1, line2 ) );

	[TestMethod]
	public void ARecordListFollowsTheChainBackwardsAndTheEconomyRegistersInThatOrder()
	{
		// the placed Belly Bounce (thing 12) heads the chain and the gate (thing 11) is older: oldest first, the gate comes before it, though the record order lists the placed object first
		var save = Park( next: 11 );
		Assert.IsNull( save.ChainOrderProblem );
		CollectionAssert.AreEqual( new[] { 1601, 1100 }, save.ObjectsInChainOrder.Select( record => record.InfoId ).ToArray() );
		var economy = new ParkEconomy( OriginalSaveImportTests.Settings(), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		OriginalEconomyImport.Apply( economy, save );
		CollectionAssert.AreEqual( new[] { 1601, 1100 }, economy.Objects.OrderBy( item => item.Id ).Select( item => item.InfoId ).ToArray(), "the older object takes the lower economy id: newest first by id is the original's list order" );
	}

	[DataTestMethod]
	[DataRow( 99, "99" )]
	[DataRow( 12, "met twice" )]
	[DataRow( 0, "no single attraction on the chain" )]
	public void AChainThatDoesNotNameEveryRecordOnceKeepsTheRecordOrderAndSaysWhy( int next, string reason )
	{
		var save = Park( (ushort)next );
		CollectionAssert.AreEqual( new[] { 1100, 1601 }, save.ObjectsInChainOrder.Select( record => record.InfoId ).ToArray(), "placed, then fixed items" );
		StringAssert.Contains( save.ChainOrderProblem, reason );
	}

	[TestMethod]
	public void ASavedNameThatIsNotTheTypesOwnIsTheLinesOfItsSign()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		var previous = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		try
		{
			var catalog = ObjectCatalog.Load( "jungle" );
			var own = ObjectSigns.Lines( catalog.Find( 1100 )! );
			// the type's own name: no override
			var plain = Park( 11, own.Line1, own.Line2 );
			Assert.IsFalse( ParkObjects.SavedSignLines( catalog, plain, plain.ObjectsInChainOrder ).ContainsKey( 1100 ) );
			// a renamed ride: its lines, with the dash of a trailing "--" dropped from the first as 0x4E2860 does
			var named = Park( 11, "Spin--", "Cycle" );
			Assert.AreEqual( ("Spin-", "Cycle"), ParkObjects.SavedSignLines( catalog, named, named.ObjectsInChainOrder )[1100] );
			// a line a sign cannot hold is ignored
			var damaged = Park( 11, "Spin\u0007", "Cycle" );
			Assert.IsFalse( ParkObjects.SavedSignLines( catalog, damaged, damaged.ObjectsInChainOrder ).ContainsKey( 1100 ) );
			// the object's own lines reach ObjectSigns.Get as the key of the composition: another name is another sign
			Assert.AreNotEqual( ObjectSigns.Lines( catalog.Find( 1100 )! ), ("Spin-", "Cycle") );
		}
		finally
		{
			FileSystem = previous;
		}
	}

	[TestMethod]
	public void EveryCorpusSavesObjectsKeepTheirTypesOwnSigns()
	{
		var folder = Environment.GetEnvironmentVariable( "OPENTPW_TPWS_SAVES" );
		if ( string.IsNullOrWhiteSpace( folder ) || !Directory.Exists( folder ) )
			Assert.Inconclusive( "Set OPENTPW_TPWS_SAVES to a folder with original .TPWS/.INTS saves in per-theme subfolders." );
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		var previous = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		try
		{
			var checkedSaves = 0;
			foreach ( var file in Directory.EnumerateFiles( folder!, "*", SearchOption.AllDirectories ).Where( path => Path.GetExtension( path ).ToUpperInvariant() is ".TPWS" or ".INTS" ).OrderBy( path => path, StringComparer.Ordinal ) )
			{
				var theme = Path.GetFileName( Path.GetDirectoryName( file ) )!.ToLowerInvariant();
				if ( theme is not ("jungle" or "hallow" or "fantasy" or "space") )
					continue;
				using var reader = new SaveReader( file );
				reader.Inspect();
				var map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( $"levels/{theme}/terrain.wad", "base.map" ) ) );
				var save = OriginalParkImport.Import( reader.ReadFile(), map );
				var name = $"{theme}/{Path.GetFileName( file )}";
				Assert.IsNull( save.ChainOrderProblem, name );
				// the chain lists the park's objects newest first: its reverse is what the import registers
				Assert.AreEqual( save.PlacedObjects.Count + save.FixedItems.Count + save.UnresolvedObjects.Count, save.ObjectsInChainOrder.Count, name );
				var catalog = ObjectCatalog.Load( theme );
				var overrides = ParkObjects.SavedSignLines( catalog, save, save.ObjectsInChainOrder );
				// no corpus object was renamed: the park gate carries the player's park name, which its own sign shows (the gate's sign is the park's name, not its type's)
				Assert.IsTrue( overrides.Keys.All( infoId => catalog.Find( infoId )!.IsFixedItem ), $"{name}: only the gate's lines differ from a type's own name" );
				checkedSaves++;
			}
			Assert.IsTrue( checkedSaves > 0 );
		}
		finally
		{
			FileSystem = previous;
		}
	}
}
