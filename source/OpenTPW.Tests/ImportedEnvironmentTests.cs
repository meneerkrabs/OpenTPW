using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The environment grid of an original save (GUEST-006): tp.exe's cell serializer 0x4D7EA0 saves the region effect records with the cells (status byte bit 4, five two-byte words each), so a loaded
/// park starts from the saved grid. OpenTPW spreads the sources again (litter, staff, objects) and keeps what the saved grid holds beyond them as a residue that reads with the words, survives the
/// sources' later adds and removes, and is written to OpenTPW's own saves.
/// </summary>
[TestClass, DoNotParallelize]
public class ImportedEnvironmentTests
{
	private static ParkEnvironment Environment()
	{
		var environment = new ParkEnvironment { Effects = new RegionEffect[RegionEffect.Count] };
		environment.Effects[RegionEffect.Guard] = new RegionEffect( 2, 0, 0, 0, 12, 0 );
		return environment;
	}

	[TestMethod]
	public void TheSavedGridTakesOverAndASourcesLaterRemovalLeavesTheResidue()
	{
		var environment = Environment();
		environment.Apply( RegionEffect.Guard, 10, 10, add: true );
		// the save holds the guard's row and, one cell further, a security word nothing here spreads and a hunger word at another cell
		var differing = environment.ImportSaved( ( x, y ) =>
		{
			var own = environment.At( x, y );
			var words = new[] { own.Happiness, own.Illness, own.Hunger, own.Security, own.Attraction };
			if ( (x, y) == (11, 10) )
				words[3] += 5;
			if ( (x, y) == (60, 60) )
				words[2] = -7;
			return words;
		} );
		Assert.AreEqual( 2, differing );
		Assert.AreEqual( (short)(6 + 5), environment.At( 11, 10 ).Security, "the guard's 12 / 2 plus the saved residue of 5" );
		Assert.AreEqual( (short)-7, environment.At( 60, 60 ).Hunger );
		// the guard walks on: his row comes off, the residue stays
		environment.Apply( RegionEffect.Guard, 10, 10, add: false );
		Assert.AreEqual( (short)5, environment.At( 11, 10 ).Security );
		Assert.AreEqual( (short)0, environment.At( 10, 10 ).Security );
		environment.Apply( RegionEffect.Guard, 20, 20, add: true );
		Assert.AreEqual( (short)12, environment.At( 20, 20 ).Security );
		Assert.AreEqual( 2, environment.ResidueCells().Count() );
	}

	[TestMethod]
	public void ASaveWithNoRecordsAndNoSourcesLeavesNoResidueAndAClearForgetsOne()
	{
		var environment = Environment();
		Assert.AreEqual( 0, environment.ImportSaved( ( _, _ ) => null ) );
		Assert.AreEqual( 0, environment.ResidueCells().Count() );
		Assert.AreEqual( 1, environment.ImportSaved( ( x, y ) => (x, y) == (3, 4) ? new short[] { 1, 2, 3, 4, 5 } : null ) );
		Assert.AreEqual( (short)4, environment.At( 3, 4 ).Security );
		environment.Clear();
		Assert.AreEqual( default, environment.At( 3, 4 ) );
		Assert.AreEqual( 0, environment.ResidueCells().Count() );
	}

	[TestMethod]
	public void TheResidueWrapsLikeTheOriginalsHalfwordsAndRoundTrips()
	{
		var environment = Environment();
		environment.Apply( RegionEffect.Guard, 5, 5, add: true );
		environment.ImportSaved( ( x, y ) => (x, y) == (5, 5) ? new short[] { 0, 0, 0, short.MinValue, 0 } : null );
		var copy = Environment();
		copy.Apply( RegionEffect.Guard, 5, 5, add: true );
		copy.RestoreResidue( environment.ResidueCells() );
		Assert.AreEqual( environment.At( 5, 5 ), copy.At( 5, 5 ) );
		Assert.AreEqual( short.MinValue, copy.At( 5, 5 ).Security, "the saved word is read back exactly, wrapping through the 16-bit difference" );
		Assert.ThrowsException<InvalidDataException>( () => copy.RestoreResidue( new[] { new[] { 200, 0, 0, 0, 0, 0, 0 } } ) );
		Assert.ThrowsException<InvalidDataException>( () => copy.RestoreResidue( new[] { new[] { 1, 1, 0, 0, 0 } } ) );
		Assert.ThrowsException<InvalidDataException>( () => copy.RestoreResidue( new[] { new[] { 1, 1, 0, 0, 0, 0, 70000 } } ) );
	}

	[TestMethod]
	public void AnOpenTpwSaveKeepsTheResidueAndAParkWithoutOneWritesNothingNew()
	{
		var park = EconomyTestData.Park();
		Assert.IsFalse( ParkSaveFile.Serialize( park ).Contains( "EnvironmentResidue", StringComparison.Ordinal ), "a park whose grid is its sources' writes no field" );
		park.Litter.Environment.ImportSaved( ( x, y ) => (x, y) == (12, 34) ? new short[] { 0, 3, 0, 0, -2 } : null );
		var json = ParkSaveFile.Serialize( park );
		Assert.IsTrue( json.Contains( "EnvironmentResidue", StringComparison.Ordinal ) );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), EconomyTestData.Settings( 500000 ), EconomyTestData.Catalog() );
		Assert.AreEqual( (short)3, restored.Litter.Environment.At( 12, 34 ).Illness );
		Assert.AreEqual( (short)-2, restored.Litter.Environment.At( 12, 34 ).Attraction );
		Assert.AreEqual( 1, restored.Litter.Environment.ResidueCells().Count() );
		foreach ( var cells in new[] { "[[200,34,0,0,0,0,1]]", "[[1,2,3]]", "[[1,2,0,0,0,0,70000]]", "[[1,2,0,0,0,0,1],[1,2,0,0,0,1,0]]" } )
		{
			var node = System.Text.Json.Nodes.JsonNode.Parse( json )!;
			node["EnvironmentResidue"] = System.Text.Json.Nodes.JsonNode.Parse( cells );
			var damaged = Encoding.UTF8.GetBytes( node.ToJsonString() );
			Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( damaged ), cells );
		}
	}

	[TestMethod]
	public void EasymodesSavedGridIsExactlyTheSourcesItsStaffAndObjectsSpreadAgain()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "jungle", "Easymode.TPWI" ) ) )
			Assert.Inconclusive( "The original Jungle Easymode.TPWI fixture is missing." );
		var previous = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		try
		{
			var map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( "levels/jungle/terrain.wad", "base.map" ) ) );
			var park = OriginalParkImport.Import( OriginalParkImportTests.ReadEasymodePayload(), map );
			var economy = new ParkEconomy( BalanceSettings.Load( "jungle", easy: true ), EconomyObjectCatalog.Load( "jungle", easy: true ), ParkGameMode.FullSimulation, 1 );
			var import = OriginalEconomyImport.Apply( economy, park, shippedSeed: true );
			// the level links each object to its economy record and puts its row on the thing's cell (Level.LinkEconomy)
			foreach ( var record in park.PlacedObjects.Select( item => item.Record ).Concat( park.FixedItems ) )
				if ( park.FindAttraction( record ) is { } attraction && import.AttractionInstances.TryGetValue( attraction.Id, out var instance ) )
					economy.PlaceObjectEffects( instance, (record.X, record.Y) );
			// the entertainer and the guard stand on the cells their saved +0xCC words name
			Assert.AreEqual( (47, 25), economy.Staff.Members.Single( member => member.Type == StaffType.Entertainer ).EffectCell );
			Assert.AreEqual( (39, 28), economy.Staff.Members.Single( member => member.Type == StaffType.Guard ).EffectCell );
			var (records, different) = economy.ImportSavedEnvironment( park.World );
			Assert.AreEqual( 250, records, "the 250 cells with the status bit 4" );
			Assert.AreEqual( 0, different, "the rebuilt grid is the saved grid in every cell: the word order, the sources and the staff's effect cells agree with tp.exe" );
			Assert.AreEqual( (short)3, economy.Litter.Environment.At( 36, 25 ).Security, "a cell the camera and the guard both cover: the saved words (security 3 at (36, 25))" );
		}
		finally
		{
			FileSystem = previous;
		}
	}
}
