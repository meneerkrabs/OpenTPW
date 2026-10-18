using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The shipped track add-ons (docs/TRACKS.md "Add-ons"): every one fits its ride's tiles from the build tool's hover, has its two open ends and a surface for the vehicles.</summary>
[TestClass]
public class TrackAddOnDataTests
{
	[TestInitialize]
	public void Init()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original add-on archives." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	private static IReadOnlyList<ObjectCatalogEntry> TrackAddOnEntries() =>
		ObjectCatalog.Themes.SelectMany( theme => ObjectCatalog.Load( theme ).Entries ).Where( entry => !entry.IsBonus && TrackAddOns.IsTrackAddOn( entry ) ).ToArray();

	[TestMethod]
	public void TheTenTrackAddOnsAreOfferedToTheirRidesAndOnlyTheExtraTubeIsAnOrdinaryObject()
	{
		var entries = TrackAddOnEntries();
		CollectionAssert.AreEquivalent( new[] { 1500, 1501, 1502, 2500, 2501, 2502, 3500, 3501, 4500, 4501 }, entries.Select( entry => entry.InfoId ).ToArray() );
		var others = ObjectCatalog.Themes.SelectMany( theme => ObjectCatalog.Load( theme ).Entries ).Where( entry => !entry.IsBonus && TrackAddOns.IsAddOn( entry ) && !TrackAddOns.IsTrackAddOn( entry ) );
		CollectionAssert.AreEqual( new[] { 3502 }, others.Select( entry => entry.InfoId ).ToArray(), "the Tubes Of Zob Extra Tube, AddOn.UpgradeType 2" );
		foreach ( var entry in entries )
		{
			var ride = ObjectCatalog.Load( entry.Theme ).Find( entry.Settings.GetInt( "AddOn.UpgradesId" ) );
			Assert.IsNotNull( ride, $"{entry.DisplayName}: its ride" );
			Assert.IsTrue( ride!.Settings.GetInt( "Bumper.WhichTrackType" ) is 1 or 2, $"{entry.DisplayName}: a kart or water ride ({ride.DisplayName})" );
		}
	}

	[TestMethod]
	public void EveryTrackAddOnCoversWholeTilesAlongTheFlowFromTheHover()
	{
		foreach ( var entry in TrackAddOnEntries() )
			TrackAddOnTests.AssertHoverCoversWholeTiles( entry.Settings, entry.Shape, entry.DisplayName );
	}

	[TestMethod]
	public void EveryTrackAddOnHasItsTwoOpenEndsAndASurface()
	{
		foreach ( var entry in TrackAddOnEntries() )
		{
			var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
			var behind = TrackAddOns.EndNode( model, TrackAddOns.EndBehind );
			var ahead = TrackAddOns.EndNode( model, TrackAddOns.EndAhead );
			Assert.IsTrue( behind >= 0 && ahead >= 0 && behind != ahead, $"{entry.DisplayName}: dummies 1 and 0x10 ({behind}, {ahead})" );
			var animated = ObjectRenderParts.FindAnimatedNodes( entry, model );
			Assert.IsTrue( animated[behind] && animated[ahead], $"{entry.DisplayName}: the ends stay separate parts, so the walk can hide them" );
			var surface = TrackSurface.FromObject( model );
			var covered = entry.Shape.Cells.Where( cell => cell.CellType is ObjectShapeSymbols.TypeUpgrade or ObjectShapeSymbols.TypeHash )
				.Count( cell => surface.HeightAt( cell.U * 10 + 5, cell.V * 10 + 5 ) != null );
			Console.WriteLine( $"{entry.InfoId} {entry.DisplayName}: sampler {surface.Mode}, {covered} track cells covered" );
			Assert.IsTrue( covered > 0, $"{entry.DisplayName}: its surface covers its track cells ({surface.Mode})" );
		}
	}
}
