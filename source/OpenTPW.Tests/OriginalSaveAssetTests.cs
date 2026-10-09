using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class OriginalSaveAssetTests
{
	[TestMethod]
	public void OriginalJungleEasyModeMatchesContainerAndDecodedIdentity()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for original read-only save-container evidence." );
		var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
		var path = Path.Combine( dataPath, "levels", "jungle", "Easymode.TPWI" );
		if ( !File.Exists( path ) )
			Assert.Inconclusive( "The selected original Jungle Easymode.TPWI fixture is missing." );
		using var reader = new SaveReader( path );
		Assert.AreEqual( "6D89303D098900364BF5E80B236B64BD85976FB947E9E4609D088547F430B39A", Convert.ToHexString( SHA256.HashData( reader.buffer ) ) );
		Assert.AreEqual( new SaveContainerInfo( 400, 0x19220100, 133, 1608309, 36930 ), reader.Inspect() );
		var payload = reader.ReadFile();
		Assert.AreEqual( 1608309, payload.Length );
		Assert.AreEqual( "A3C9A28252C37AD49A8EB78E4A0C5E1D5229D01548FA35801DB67015D2589173", Convert.ToHexString( SHA256.HashData( payload ) ) );
	}
}
