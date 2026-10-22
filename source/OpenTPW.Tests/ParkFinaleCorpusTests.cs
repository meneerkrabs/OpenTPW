using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The finale's original assets: End's camera seat the winning camera rides (tp.exe 0x4E15B0 → 0x42A560, seat 1) and the language folder's congratulations banner
/// the winning screen draws (table 0x74FAF0, model s_congrats). Inconclusive without OPENTPW_GAME_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParkFinaleCorpusTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "fantasy", "space" };
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the finale corpus." );
		originalFileSystem = previous;
		initialized = true;
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
	}

	/// <summary>Every theme's End model carries the camera seat (type 0x1031, value 1) at node 38, so the winning screen rides it.</summary>
	[TestMethod]
	public void EveryThemesEndCarriesTheWinningCameraSeat()
	{
		foreach ( var theme in Themes )
		{
			var end = ObjectCatalog.Load( theme ).Entries.Single( entry => entry.IsFixedItem && entry.SettingsName == "End" );
			var model = ObjectAssets.LoadModel( end.FileSystem, end.ModelPath! );
			Assert.AreEqual( 38, RideSeat.FindNode( model ), theme );
			Assert.AreEqual( 0x1031u, model.DummyAttributes[0].Type, theme );
		}
	}

	/// <summary>The default UI registry takes the language folder's banner meshes beside ui.wad's models, as the start-up 0x4893C0 does: the winning banner resolves, at the table's rectangle.</summary>
	[TestMethod]
	public void TheLanguageBannerMeshesJoinTheUiRegistry()
	{
		var data = OriginalParkImportTests.OriginalDataPath();
		var previous = GameLanguage.IsSelected ? GameLanguage.Current : null;
		try
		{
			GameLanguage.Current = GameLanguage.Resolve( data, "English", null );
			var models = new UiModels();
			var banner = models.Get( "s_congrats" );
			Assert.IsNotNull( banner, "congrats.MD2 of the language folder" );
			Assert.AreEqual( UiModels.RootNameKey( "s_congrats" ), banner!.DrawingKey );
			Assert.AreEqual( "cong_grad", Path.GetFileNameWithoutExtension( banner.GetFrame( 0 ).Parts.Single().TextureName ) );
			var frame = banner.GetFrame( 0 );
			var rect = OriginalLayoutTables.WinningScreen.Root.Rect;
			Assert.AreEqual( rect.X, frame.MinX, 1 ); Assert.AreEqual( rect.Y, frame.MinY, 1 ); Assert.AreEqual( rect.Right, frame.MaxX, 1 );
			Assert.IsNotNull( models.Get( "s_bkrupt" ) ); Assert.IsNotNull( models.Get( "s_paused" ) );
			Assert.IsNotNull( models.Get( "wdialog" ), "the message box's window is a ui.wad model" );
		}
		finally { GameLanguage.Current = previous ?? GameLanguage.Resolve( data, null, null ); }
	}
}
