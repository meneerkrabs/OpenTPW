using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The saved types' first use read ahead in steps of their own before a co-op build restores their objects (<see cref="SavedTypePreparation"/>): what the steps read for each type and
/// in which order, which signs they compose ahead, what a failed read leaves, and the texture lookup ahead of use. The steps take the layout's records and the level's catalogue, gate
/// and terrain only, so they cannot touch a park's objects, seeds or allocators. With the game data's Jungle where noted.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SavedTypePreparationTests
{
	private BaseFileSystem? previousFileSystem;
	private Logger? previousLog;
	private bool withData;

	[TestInitialize]
	public void Initialize()
	{
		previousFileSystem = FileSystem;
		previousLog = Log;
		Log = new Logger();
		ObjectSigns.ForgetForTests();
		ObjectSigns.ComposeAheadEnabled = false;
	}

	[TestCleanup]
	public void Cleanup()
	{
		ObjectSigns.ForgetForTests();
		ObjectSigns.ComposeAheadEnabled = !OperatingSystem.IsBrowser();
		FileSystem = previousFileSystem!;
		Log = previousLog!;
		if ( withData )
			ObjectCatalog.BonusDataRoot = null;
	}

	private void UseGameData()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the saved types' preparation." );
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !Directory.Exists( Path.Combine( dataPath, "levels", "jungle", "rides" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH has no level data." );
		FileSystem = GameDataFileSystem.Create( dataPath );
		ObjectCatalog.BonusDataRoot = null;
		withData = true;
	}

	private static ParkLayoutSnapshot.PlacedObjectData Record( int infoId ) => new()
	{
		InfoId = infoId, Archive = "", X = 0, Y = 0, Rotation = 0, Open = true, CreationSeed = 1, AttractionId = 1, BaseHeight = 0, IsFixedItem = false
	};

	private static ParkLayoutSnapshot.Data Layout( params int[] infoIds ) => new()
	{
		Cells = new ParkLayoutSnapshot.CellMapData { Width = 1, Height = 1, Version = 0, Cells = new List<ParkLayoutSnapshot.CellData>() },
		Objects = infoIds.Select( Record ).ToList(),
		NextSeed = 1,
		NextAttractionId = 1,
		ObjectScriptKey = WorldSeed.Default.ObjectScriptKey
	};

	/// <summary>The texture names a type's first object loads, as the steps should read them: its parts' names but its sign slots', then its animated slots' frames, each once.</summary>
	private static IEnumerable<string> FirstUseTextures( ObjectCatalogEntry entry )
	{
		var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
		return ObjectRenderParts.TextureNames( model ).Where( name => !ObjectSigns.IsSignSlot( name ) )
			.Concat( OriginalObject.FrameAnimatedSlots( entry, model ).SelectMany( slot => slot.Frames ) ).Distinct( StringComparer.OrdinalIgnoreCase );
	}

	private static bool ShowsASign( ObjectCatalogEntry entry ) =>
		ObjectRenderParts.TextureNames( ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath ) ).Any( ObjectSigns.IsSignSlot );

	[TestMethod]
	public void TheStepsReadEveryTypesModelAndSignThenItsTexturesAndHoardingInFirstUseOrder()
	{
		UseGameData();
		var catalog = ObjectCatalog.Load( "jungle" );
		var park = OriginalPark.Load( "jungle" );
		const int gate = 1601;
		// a ride, a shop, the ride again, the gate, a coaster and a record the catalogue does not know (the restore's to refuse)
		var layout = Layout( 1100, 1203, 1100, gate, 1185, 99999 );
		var types = new[] { 1100, 1203, gate, 1185 }.Select( id => catalog.Find( id )! ).ToArray();
		var textures = new List<(int, string)>();
		var hoardingTextures = new List<string>();
		var cache = new ObjectHoardingCache();
		var reads = new SavedTypePreparation.Reads( catalog, gate, park.Heightfield, park.LevelName, cache, ( entry, name ) => textures.Add( (entry.InfoId, name) ),
			( _, name ) => hoardingTextures.Add( name ) );

		var shares = SavedTypePreparation.Steps( layout, reads ).ToList();

		CollectionAssert.AreEqual( types.SelectMany( entry => FirstUseTextures( entry ).Select( name => (entry.InfoId, name) ) ).ToArray(), textures.ToArray(),
			"each type's textures, a type once, in the order the layout first uses them" );
		Assert.IsTrue( textures.Count > 10 );
		foreach ( var entry in types )
		{
			Assert.AreEqual( entry.InfoId != gate && ShowsASign( entry ), ObjectSigns.ComposedAhead( entry ), $"{entry}: composed ahead when its parts show a sign, never the gate's" );
			Assert.IsTrue( cache.Holds( entry ), $"{entry}: its hoarding boards over the terrain" );
		}
		Assert.IsTrue( types.Where( entry => entry.InfoId != gate ).Any( ShowsASign ) );
		var boards = types.Any( entry => new ObjectHoardingCache().Geometry( entry, ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath ), park.Heightfield ) != null );
		CollectionAssert.AreEqual( boards ? ObjectHoardingGeometry.TextureNames.ToArray() : Array.Empty<string>(), hoardingTextures.ToArray(), "the level's hoarding textures once" );
		Assert.AreEqual( 0f, shares[0] );
		Assert.IsTrue( shares.Zip( shares.Skip( 1 ) ).All( pair => pair.Second >= pair.First ) && shares[^1] < 1, "the share rises, and the restore ends it" );
	}

	[TestMethod]
	public void AFailedReadIsDroppedWithWhatItLoggedAndOneThatSucceedsLogsAtOnce()
	{
		UseGameData();
		var catalog = ObjectCatalog.Load( "jungle" );
		var loads = 0;
		var logged = new List<string>();
		void Record( Logger.Level severity, string text ) => logged.Add( text );
		var reads = new SavedTypePreparation.Reads( catalog, null, null, null, new ObjectHoardingCache(), ( entry, name ) =>
		{
			if ( ++loads == 1 )
			{
				Log.Warning( "the first read failed" );
				throw new IOException( "busy" );
			}
			if ( loads == 2 )
				Log.Warning( "the second read succeeded" );
		}, ( _, _ ) => Assert.Fail( "no terrain, no hoarding" ) );
		Logger.OnLog += Record;
		try
		{
			var steps = SavedTypePreparation.Steps( Layout( 1203 ), reads ).Count();
			Assert.IsTrue( steps > 2 && loads > 2, "the steps go on after a failed read" );
		}
		finally { Logger.OnLog -= Record; }
		CollectionAssert.AreEqual( new[] { "the second read succeeded" }, logged.ToArray(), "a failed read's log is dropped (its object meets the failure itself), a successful one's written" );
	}

	private static Exception? Record( Action action )
	{
		try
		{
			action();
			return null;
		}
		catch ( Exception exception )
		{
			return exception;
		}
	}

	[TestMethod]
	public void ATextureLookupAheadOfUseLeavesADirectoryReadThatFailedToTheObjectsOwnLookup()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-texture-ahead-" ).FullName;
		try
		{
			FileSystem = new BaseFileSystem( root );
			var settings = new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1\nInfo.Name Synthetic\n", "synthetic.sam" ) } );
			ObjectCatalogEntry Entry( string archive ) => new( "synthetic", ObjectCategory.Ride, archive, settings, new ObjectShape( new[] { "*" } ), $"{archive}/model.MD2", null, null,
				Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( root ), null );
			// the archive's textures folder cannot be read for now (a link that loops, an I/O error that is no missing folder)
			void Unreadable( string archive )
			{
				Directory.CreateDirectory( Path.Combine( root, archive ) );
				File.CreateSymbolicLink( Path.Combine( root, archive, "textures" ), "textures" );
				Assert.ThrowsException<IOException>( () => Directory.GetFiles( Path.Combine( root, archive, "textures" ) ) );
				Assert.IsNotInstanceOfType( Record( () => Directory.GetFiles( Path.Combine( root, archive, "textures" ) ) ), typeof( DirectoryNotFoundException ) );
			}
			void Readable( string archive )
			{
				File.Delete( Path.Combine( root, archive, "textures" ) );
				Directory.CreateDirectory( Path.Combine( root, archive, "textures" ) );
				File.WriteAllBytes( Path.Combine( root, archive, "textures", "wall.wct" ), new byte[] { 1 } );
			}
			Unreadable( "ahead" );
			var ahead = Entry( "/ahead" );
			Assert.IsNull( ObjectAssets.ResolveTexture( ahead, "wall", aheadOfUse: true ) );
			Readable( "ahead" );
			StringAssert.EndsWith( ObjectAssets.ResolveTexture( ahead, "wall" )?.Path, "wall.wct", "the object's own lookup reads the folder again and finds the texture" );
			// a lookup on use remembers the failed read, as it always has: the steps leave nothing the object would not have kept itself
			Unreadable( "on-use" );
			var onUse = Entry( "/on-use" );
			Assert.IsNull( ObjectAssets.ResolveTexture( onUse, "wall" ) );
			Readable( "on-use" );
			Assert.IsNull( ObjectAssets.ResolveTexture( onUse, "wall" ) );
		}
		finally { Directory.Delete( root, true ); }
	}
}
