using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A ride script's scream: the band operand picks the held event (STARTSCREAM) or the one-shot grid (SINGLESCREAM),
/// the level and the script's speed its parameter. The maps are the traced Mac ones (<c>0x100ACA38</c>,
/// <c>0x100ACB7C</c>, <c>0x100ACC24</c>, <c>0x100ACE68</c>); the original-data test skips without <c>OPENTPW_GAME_PATH</c>.
/// </summary>
[TestClass]
public class GuestScreamTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	[TestMethod]
	public void TheBandPicksTheHeldEventAndNegativeBandsTakeTheSecondRow()
	{
		Assert.IsNull( GameAudio.ScreamBandEvent( 0 ), "band 0 plays nothing" );
		Assert.AreEqual( 71u, GameAudio.ScreamBandEvent( 1 ) );
		Assert.AreEqual( 72u, GameAudio.ScreamBandEvent( 2 ) );
		Assert.AreEqual( 72u, GameAudio.ScreamBandEvent( 3 ) );
		Assert.AreEqual( 73u, GameAudio.ScreamBandEvent( 4 ) );
		Assert.AreEqual( 73u, GameAudio.ScreamBandEvent( 7 ) );
		Assert.AreEqual( 74u, GameAudio.ScreamBandEvent( 8 ) );
		Assert.AreEqual( 74u, GameAudio.ScreamBandEvent( 100 ) );
		Assert.AreEqual( 72u, GameAudio.ScreamBandEvent( -3 ), "a negative band falls into the 2-3 branch" );
	}

	[TestMethod]
	public void TheParameterIsTheMeanOfLevelAndSpeedClampedToTheRange()
	{
		Assert.AreEqual( 25, GameAudio.ScreamParameter( 0 ), "(0 + 50) / 2" );
		Assert.AreEqual( 75, GameAudio.ScreamParameter( 100 ) );
		Assert.AreEqual( 100, GameAudio.ScreamParameter( 200 ), "clamped" );
		Assert.AreEqual( 0, GameAudio.ScreamParameter( -200 ), "clamped below" );
	}

	[TestMethod]
	public void TheSingleScreamGridAndNegativeLevelIdsAreTheTracedRows()
	{
		Assert.AreEqual( 75, GameAudio.ScreamGridRow( 71 ) );
		Assert.AreEqual( 79, GameAudio.ScreamGridRow( 72 ) );
		Assert.AreEqual( 83, GameAudio.ScreamGridRow( 73 ) );
		Assert.AreEqual( 87, GameAudio.ScreamGridRow( 74 ) );
		Assert.AreEqual( 105u, GameAudio.ScreamSingleEvent( 71 ) );
		Assert.AreEqual( 106u, GameAudio.ScreamSingleEvent( 72 ) );
		Assert.AreEqual( 108u, GameAudio.ScreamSingleEvent( 73 ) );
		Assert.AreEqual( 109u, GameAudio.ScreamSingleEvent( 74 ) );
	}

	[TestMethod]
	public void WithoutTheSoundServiceNoScreamStartsAndTheHandleIsRefused()
	{
		Assert.IsNull( GameAudio.StartScream( () => null, 2, 50, null ) );
		Assert.IsNull( GameAudio.SingleScream( 2, 50, null ) );
	}

	// ---- original data (OPENTPW_GAME_PATH) ----

	private static string? DataDirectory()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			return null;
		return Directory.EnumerateDirectories( path ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path;
	}

	[TestMethod]
	public void EveryScreamEventIsInTheGlobalKidsCategory()
	{
		var data = DataDirectory();
		if ( data == null )
			Assert.Inconclusive( "OPENTPW_GAME_PATH is not set." );
		var kids = SoundCategory.TryLoad( new BaseFileSystem( data ), "/global/sound", "cat_kids" );
		Assert.IsNotNull( kids );
		foreach ( var id in new uint[] { 71, 72, 73, 74, 75, 79, 83, 87, 105, 106, 108, 109 } )
			Assert.IsNotNull( kids.Catalog.Find( id ), $"cat_kids event {id}" );
	}
}
