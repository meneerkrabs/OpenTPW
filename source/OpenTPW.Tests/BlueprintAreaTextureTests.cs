using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// COAST-028: the coaster tool's placement area is face type 0x11f, table slot 2 (orange, <c>0x535670</c>), whose texture is the level's own <c>orange.wct</c> in <c>Levels/&lt;level&gt;/dynamic.wad</c>
/// (inconclusive without OPENTPW_GAME_PATH).
/// </summary>
[TestClass]
public class BlueprintAreaTextureTests
{
	private BaseFileSystem previousFileSystem = null!;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Init()
	{
		previousFileSystem = FileSystem;
		previousLog = Log;
		Log = new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the level archives." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup()
	{
		ObjectAssets.LowDetailTexturesOverride = null;
		FileSystem = previousFileSystem;
		Log = previousLog;
	}

	[TestMethod]
	public void EveryLevelsDynamicArchiveHoldsTheOrangeAreaTextureInBothDetailSets()
	{
		foreach ( var level in new[] { "jungle", "fantasy", "hallow", "space" } )
		{
			ObjectAssets.LowDetailTexturesOverride = false;
			Assert.AreEqual( $"/levels/{level}/dynamic/textures/orange.wct", BlueprintMarkers.AreaTexturePath( level ), level );
			ObjectAssets.LowDetailTexturesOverride = true;
			Assert.AreEqual( $"/levels/{level}/dynamic/stexture/orange.wct", BlueprintMarkers.AreaTexturePath( level ), $"{level}: the low-detail set" );
		}
		ObjectAssets.LowDetailTexturesOverride = false;
		Assert.IsNull( BlueprintMarkers.AreaTexturePath( null ) );
		Assert.IsNull( BlueprintMarkers.AreaTexturePath( "nolevel" ) );
	}

	/// <summary>The texture is the translucent yellow-orange the video shows over the placement area, not the generic folder's blue.</summary>
	[TestMethod]
	public void TheOrangeTextureIsTranslucentYellowAndOrange()
	{
		ObjectAssets.LowDetailTexturesOverride = false;
		var data = new TextureFile( FileSystem.OpenRead( BlueprintMarkers.AreaTexturePath( "jungle" )! ) ).Data;
		Assert.AreEqual( (64, 64), (data.Width, data.Height) );
		long red = 0, green = 0, blue = 0, alpha = 0;
		var pixels = data.Width * data.Height;
		for ( var index = 0; index < pixels; index++ )
		{
			red += data.Data[index * 4];
			green += data.Data[index * 4 + 1];
			blue += data.Data[index * 4 + 2];
			alpha += data.Data[index * 4 + 3];
		}
		Assert.IsTrue( red / pixels > 200 && green / pixels is > 140 and < 180 && blue / pixels < 20, "orange: strong red, medium green, no blue" );
		Assert.IsTrue( alpha / pixels is > 100 and < 255, "translucent" );
	}
}
