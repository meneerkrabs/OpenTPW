using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The lost park's original assets: the gate's camera seat the loss window rides (tp.exe 0x48AC40 -> 0x4E15B0 -> 0x42A560, seat 1) and the language folder's bankrupt banner the window draws
/// (table 0x74FB20, model s_bkrupt). Inconclusive without OPENTPW_GAME_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParkLossCorpusTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "fantasy", "space" };
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the loss corpus." );
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

	/// <summary>Every theme's gate model carries the camera seat (type 0x1031, value 1), so the lost park's camera rides it.</summary>
	[TestMethod]
	public void EveryThemesGateCarriesTheLossCameraSeat()
	{
		foreach ( var theme in Themes )
		{
			var gate = ObjectCatalog.Load( theme ).Entries.Single( entry => entry.IsFixedItem && entry.SettingsName == "Gates" );
			var model = ObjectAssets.LoadModel( gate.FileSystem, gate.ModelPath! );
			var node = RideSeat.FindNode( model );
			Assert.IsTrue( node >= 0, theme );
			Assert.IsTrue( model.DummyAttributes.Any( attribute => attribute.Type == 0x1031u && attribute.Value == 1 ), theme );
		}
	}

	/// <summary>The language folder's bankrupt banner joins the UI registry beside ui.wad's models and resolves at the table's rectangle.</summary>
	[TestMethod]
	public void TheBankruptBannerResolvesAtTheTablesRectangle()
	{
		var data = OriginalParkImportTests.OriginalDataPath();
		var previous = GameLanguage.IsSelected ? GameLanguage.Current : null;
		try
		{
			GameLanguage.Current = GameLanguage.Resolve( data, "English", null );
			var models = new UiModels();
			var banner = models.Get( OriginalLayoutTables.LossScreen.Root.Model! );
			Assert.IsNotNull( banner, "bankrupt.MD2 of the language folder" );
			Assert.AreEqual( UiModels.RootNameKey( "s_bkrupt" ), banner!.DrawingKey );
			var frame = banner.GetFrame( 0 );
			Assert.AreEqual( 1, frame.Parts.Count );
			Assert.IsTrue( frame.TryGetFitBounds( out var fit ), "the banner has a drawn mesh that the window's rectangle stretches (UiContext.DrawFrame)" );
			Assert.IsTrue( fit.Width > 0 && fit.Height > 0 );
			Assert.AreEqual( "text_grad", Path.GetFileNameWithoutExtension( frame.Parts.Single().TextureName ), "the banner's own texture" );
		}
		finally { GameLanguage.Current = previous ?? GameLanguage.Resolve( data, null, null ); }
	}
}
