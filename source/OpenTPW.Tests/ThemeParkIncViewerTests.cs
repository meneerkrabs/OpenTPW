using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// The experimental Theme Park Inc level viewer (docs/THEME-PARK-INC.md). Edition detection runs on synthetic folders;
/// the corpus tests are inconclusive without <c>OPENTPW_TPI_PATH</c> (a Theme Park Inc installation or its <c>Data</c> folder).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncViewerTests
{
	private static readonly string[] Worlds = { "arabian", "science", "water" };

	/// <summary>
	/// Texture names that no archive of the supplied retail data holds: the build-tool markers PathTex 16-18 of every world (the viewer's path
	/// layer does not use them) and the lake bed set of the science lake (drawn plain white, see docs/THEME-PARK-INC.md).
	/// </summary>
	private static readonly string[] MissingTerrainTextures =
	{
		"arabian: pathtex/baseblue", "arabian: pathtex/grnarrow", "arabian: pathtex/redarrow",
		"science: pathtex/baseblue", "science: pathtex/grnarrow", "science: pathtex/redarrow",
		"science: textures/bed04", "science: textures/bed06", "science: textures/bed07", "science: textures/bed09", "science: textures/bed10", "science: textures/bed12",
		"water: pathtex/baseblue", "water: pathtex/grnarrow", "water: pathtex/redarrow",
	};

	private BaseFileSystem? previous;

	[TestInitialize]
	public void Initialize() => previous = FileSystem;

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previous!;
		GameEditions.Select( GameEdition.ThemeParkWorld );
	}

	private string UseTpiData()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		GameEditions.Select( GameEdition.ThemeParkInc );
		ObjectCatalog.BonusDataRoot = null;
		return data;
	}

	private static string Folder( params string[] directories )
	{
		var root = Path.Combine( Path.GetTempPath(), "opentpw-edition-" + Guid.NewGuid().ToString( "N" ) );
		foreach ( var directory in directories )
			Directory.CreateDirectory( Path.Combine( root, directory ) );
		return root;
	}

	[TestMethod]
	public void StructureAloneIdentifiesThemeParkIncAndTheWorldThemesDoNot()
	{
		var inc = Folder( "levels/arabian", "levels/science" );
		var world = Folder( "levels/jungle", "levels/fantasy" );
		var mixed = Folder( "levels/jungle", "levels/arabian" );
		try
		{
			File.WriteAllText( Path.Combine( inc, "Ver.txt" ), "Version: Beta_21" );
			File.WriteAllText( Path.Combine( world, "Ver.txt" ), "Version: 1" );
			File.WriteAllText( Path.Combine( mixed, "Ver.txt" ), "Version: 1" );
			Assert.AreEqual( new GameEditionReport( GameEdition.ThemeParkInc, GameEditionEvidence.Structure ), GameEditions.Detect( inc ) );
			Assert.AreEqual( GameEdition.ThemeParkWorld, GameEditions.Detect( world ).Edition );
			Assert.AreEqual( GameEdition.ThemeParkWorld, GameEditions.Detect( mixed ).Edition, "a Theme Park World theme folder rules Theme Park Inc out" );
			Directory.Delete( Path.Combine( inc, "levels", "arabian" ) );
			Directory.Delete( Path.Combine( inc, "levels", "science" ) );
			Assert.AreEqual( GameEdition.ThemeParkWorld, GameEditions.Detect( inc ).Edition, "no theme folder, no edition" );
			Assert.AreEqual( GameEdition.ThemeParkWorld, GameEditions.Detect( Path.Combine( inc, "nowhere" ) ).Edition );
		}
		finally
		{
			foreach ( var folder in new[] { inc, world, mixed } )
				Directory.Delete( folder, true );
		}
	}

	[TestMethod]
	public void TheViewerOnlyOpensAThemeParkIncWorldWithoutPlayOptions()
	{
		Assert.IsNull( GameEditions.ViewerRefusal( new[] { "--load-original-level", "arabian" } ) );
		Assert.IsNull( GameEditions.ViewerRefusal( new[] { "--game-path", "x", "--load-original-level", "Science", "--capture-world", "a.png" } ) );
		StringAssert.Contains( GameEditions.ViewerRefusal( Array.Empty<string>() )!, "--load-original-level" );
		// read-only inspection of Theme Park Inc assets stays possible
		Assert.IsNull( GameEditions.ViewerRefusal( new[] { "--inspect-model", "/levels/arabian/terrain/base.MD2" } ) );
		Assert.IsNull( GameEditions.ViewerRefusal( new[] { "--play-movie", "intro", "--headless" } ) );
		Assert.IsNotNull( GameEditions.ViewerRefusal( new[] { "--play-movie", "intro" } ) );
		StringAssert.Contains( GameEditions.ViewerRefusal( new[] { "--load-original-level", "jungle" } )!, "arabian, science or water" );
		StringAssert.Contains( GameEditions.ViewerRefusal( new[] { "--load-original-level", "--smoke-test" } )!, "--load-original-level" );
		StringAssert.Contains( GameEditions.ViewerRefusal( new[] { "--load-original-level", "arabian", "--sandbox" } )!, "drop --sandbox" );
		StringAssert.Contains( GameEditions.ViewerRefusal( new[] { "--load-original-level", "arabian", "--smoke-test" } )!, "drop --smoke-test" );
	}

	[TestMethod]
	public void TheRoadSignSpanSymbolIsAnOccupiedCell()
	{
		var sign = new ObjectShape( new[] { "*@@*" } );
		Assert.AreEqual( 4, sign.OccupiedCells.Count() );
		Assert.IsTrue( sign.Cells.Where( cell => cell.Symbol == '@' ).All( cell => cell.Kind == ObjectCellKind.Occupied ) );
	}

	[TestMethod]
	public void TheSuppliedRetailFilesAreThemeParkIncAndTheWorldFilesAreNot()
	{
		var data = UseTpiData();
		Assert.AreEqual( new GameEditionReport( GameEdition.ThemeParkInc, GameEditionEvidence.StandardSamHash ), GameEditions.Detect( data ) );
		var world = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( !string.IsNullOrWhiteSpace( world ) && Directory.Exists( world ) )
			Assert.AreEqual( GameEdition.ThemeParkWorld, GameEditions.Detect( OriginalParkImportTests.OriginalDataPath() ).Edition );
	}

	[TestMethod]
	public void EveryTerrainTextureOfBothWorldsResolvesAndDecodesExceptTheMissingLakeBeds()
	{
		UseTpiData();
		var unresolved = new SortedSet<string>( StringComparer.Ordinal );
		var decoded = 0;
		foreach ( var world in Worlds )
		{
			var park = OriginalPark.Load( world, readShippedSave: false );
			var terrainDirectory = park.TerrainDirectory;
			string[] List( string directory )
			{
				try { return FileSystem.GetFiles( directory ); }
				catch ( DirectoryNotFoundException ) { return Array.Empty<string>(); }
			}
			var requests = new List<(string Directory, string Name)>();
			var field = park.Heightfield;
			for ( var z = 0; z < field.CellCountZ; z++ )
				for ( var x = 0; x < field.CellCountX; x++ )
					if ( field.GetCellTextureSlot( x, z ) is >= 0 and var slot )
						requests.Add( ("textures", park.TerrainModel.Textures[slot].FrameNames[0]) );
			foreach ( var model in new[] { park.TerrainModel, park.LakeModel }.OfType<ModelFile>() )
				foreach ( var material in model.Meshes.SelectMany( mesh => mesh.Materials ) )
					requests.Add( ("textures", material.Name) );
			var table = List( terrainDirectory ).First( file => file.EndsWith( ".tct", StringComparison.OrdinalIgnoreCase ) );
			foreach ( var name in OriginalTerrain.ReadPathTextureNames( FileSystem.ReadAllText( table ) ).Values )
				requests.Add( ("pathtex", name) );
			foreach ( var (directory, name) in requests.Distinct() )
			{
				var path = OriginalTerrain.FindTexture( terrainDirectory, world, name, directory, List );
				if ( path == null )
				{
					unresolved.Add( $"{world}: {directory}/{Path.GetFileNameWithoutExtension( name )}" );
					continue;
				}
				Assert.IsFalse( path.HasExtension( ".wct" ), path );
				var (data, width, height) = Texture.DecodeImage( path );
				Assert.IsTrue( width > 0 && height > 0 && data.Length == width * height * 4, path );
				decoded++;
			}
		}
		Assert.IsTrue( decoded > 100, $"{decoded} textures decoded" );
		Assert.AreEqual( string.Join( "\n", MissingTerrainTextures ), string.Join( "\n", unresolved ) );
	}

	[TestMethod]
	public void EveryObjectOfBothWorldsClassifiesItsShapeAndResolvesItsTextures()
	{
		UseTpiData();
		var missing = new SortedSet<string>( StringComparer.OrdinalIgnoreCase );
		var unbuildable = new SortedSet<string>( StringComparer.Ordinal );
		var entries = 0;
		var spans = 0;
		foreach ( var world in Worlds )
		{
			var catalog = ObjectCatalog.Load( world );
			Assert.IsTrue( catalog.Entries.Count > 50, world );
			foreach ( var entry in catalog.Entries )
			{
				entries++;
				spans += entry.Shape.Cells.Count( cell => cell.Symbol == '@' );
				var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
				try { Assert.IsTrue( ObjectRenderParts.Build( entry, model ).Count > 0, entry.ToString() ); }
				catch ( InvalidDataException exception ) { unbuildable.Add( $"{entry.ArchivePath}: {exception.Message}" ); }
				foreach ( var material in model.Meshes.SelectMany( mesh => mesh.Materials ) )
				{
					var path = ObjectAssets.ResolveTexturePath( entry, material.Name );
					if ( path == null )
					{
						missing.Add( $"{entry.ArchivePath}:{material.Name}" );
						continue;
					}
					var (data, width, height) = Texture.DecodeImage( path );
					Assert.IsTrue( width > 0 && height > 0 && data.Length == width * height * 4, path );
				}
			}
		}
		Assert.AreEqual( "", string.Join( "\n", unbuildable ) );
		Assert.IsTrue( spans > 0, "the road signs use '@'" );
		Assert.IsTrue( entries > 100 );
		// Whatever the data does not ship is listed so a loader regression (a whole directory of misses) stands out.
		Assert.IsTrue( missing.Count < entries / 4, $"{missing.Count} unresolved of {entries} objects: {string.Join( ", ", missing.Take( 20 ) )}" );
	}

	[TestMethod]
	public void ScienceViewerImportsTheShippedPrebuiltPark()
	{
		UseTpiData();
		var science = OriginalPark.Load( "science" );
		Assert.IsNotNull( science.Save );
		Assert.AreEqual( 76, science.Save.PathCells.Count );
		Assert.AreEqual( 8, science.Save.PlacedObjects.Count );
		Assert.AreEqual( 1, science.Save.UnresolvedObjects.Count );
		var catalog = ObjectCatalog.Load( "science" );
		var records = science.Save.PlacedObjects.Select( item => item.Record )
			.Concat( science.Save.UnresolvedObjects ).Concat( science.Save.FixedItems ).ToArray();
		Assert.AreEqual( 18, records.Count( item => catalog.Find( item.InfoId ) != null ) );
		Assert.AreEqual( 17, records.Count( item => catalog.Find( item.InfoId ) is { IsTool: false } ) );
		Assert.IsTrue( records.Any( item => catalog.Find( item.InfoId )?.SettingsName == "Gates" ) );
		Assert.IsTrue( records.Any( item => item.Rotation == 90 ) );
		Assert.IsNull( OriginalPark.Load( "arabian" ).Save );
		Assert.IsNull( OriginalPark.Load( "water" ).Save );
	}

	[TestMethod]
	public void TheCpuPartsOfBothWorldsBuildWithoutAnEconomy()
	{
		UseTpiData();
		foreach ( var world in Worlds )
		{
			var park = OriginalPark.Load( world, readShippedSave: false );
			Assert.IsNull( park.Save, "the Theme Park Inc park saves are not read" );
			Assert.IsTrue( park.Heightfield.CellCountX > 0 );
			var catalog = ObjectCatalog.Load( world );
			var objects = new ParkObjects( catalog, new OriginalParkGrid( park ), WorldSeed.Default );
			Assert.IsTrue( catalog.Entries.Any( entry => entry.IsFixedItem ), world );
			Assert.IsNotNull( catalog.Entries.Single( entry => entry.SettingsName == "Gates" ), world );
			Assert.AreEqual( 0, objects.Objects.Count() );
		}
	}
}
