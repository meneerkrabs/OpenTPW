using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Optional local-data coverage of both track families and each shipped ride's own scrap row; no original data is included.</summary>
[TestClass, DoNotParallelize]
public class TrackEditRecoveryDataTests
{
	private BaseFileSystem previousFiles = null!;

	[TestInitialize]
	public void Initialize()
	{
		previousFiles = FileSystem;
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original track definitions and balance settings." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup() => FileSystem = previousFiles;

	[TestMethod]
	public void EveryShippedKartAndWaterRideRefundsItsOwnCostAndUsedRideScrapRowOnBackspace()
	{
		var families = new System.Collections.Generic.HashSet<TrackFamily>();
		foreach ( var theme in ObjectCatalog.Themes )
		{
			var settings = BalanceSettings.Load( theme, easy: false );
			var catalog = EconomyObjectCatalog.Load( theme, easy: false );
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( entry => !entry.IsBonus && entry.Category == ObjectCategory.Ride
				&& entry.Settings.GetInt( "Bumper.WhichTrackType" ) is 1 or 2 ) )
			{
				var family = entry.Settings.GetInt( "Bumper.WhichTrackType" ) == 1 ? TrackFamily.Kart : TrackFamily.Water;
				families.Add( family );
				var economy = new ParkEconomy( settings, catalog, ParkGameMode.FullSimulation, 1 );
				var ride = economy.RegisterExisting( entry.InfoId );
				ride.TimesUsed = 1; // use the shipped first-year row rather than the unused-ride full-refund exception
				var cells = new ParkCellMap( 40, 40 );
				var station = TrackStation.FromPlacement( 10, 9, 0 );
				var builder = new TrackBuilder( cells, family, () => economy, new SandboxParkGrid() );
				builder.PlaceStation( station );
				Assert.IsTrue( new TrackLineWriter( builder, owner: builder.OwnerOf( station ) ).Commit( (14, 10), (18, 10) ).Completed, entry.DisplayName );
				var before = economy.Balance;
				var count = 0;
				var editor = new TrackEditor( builder, station, new TrackEditHooks
				{
					TileCleared = _ => { count++; economy.RefundTrackTile( builder.Purchase, ride.Id ); }
				} );
				var tool = new CellBuildTool();
				editor.EnterLineTool( tool );
				Assert.IsTrue( tool.Undo(), entry.DisplayName );
				Assert.IsFalse( tool.Undo(), entry.DisplayName );
				// the line's first tile is the station exit, which the clear spares (0x7639E0/E4, 0x53B47C, 0x53B54A): the undo refunds the other two
				Assert.AreEqual( 2, count, entry.DisplayName );
				Assert.IsTrue( builder.Tracks.Get( station.Exit.X, station.Exit.Y ).IsOrigin, entry.DisplayName );
				Assert.AreEqual( before + 2 * (builder.TileCost * economy.ScrapPercent( ride ) / 100), economy.Balance, entry.DisplayName );
			}
		}
		CollectionAssert.AreEquivalent( new[] { TrackFamily.Kart, TrackFamily.Water }, families.ToArray() );
	}
}
