using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// <c>OPENTPW_TPWS_SAVES</c> with <c>OPENTPW_GAME_PATH</c>: every original save (autosaves, named parks, restart.INTS) of the four themes goes through the park import (<see cref="OriginalEconomyImport"/>)
/// and the evidence it reports is checked against what each import step now takes from the save (SAVE-004 to SAVE-010).
/// </summary>
[TestClass]
[DoNotParallelize]
public class OriginalSaveImportCorpusTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "fantasy", "space" };

	/// <summary>Runs <paramref name="check"/> on the import of every corpus save with the balance layer it was made with.</summary>
	internal static void ForEachSave( Action<string, OriginalParkImport, ParkEconomy, OriginalEconomyImport> check )
	{
		var folder = Environment.GetEnvironmentVariable( "OPENTPW_TPWS_SAVES" );
		if ( string.IsNullOrWhiteSpace( folder ) || !Directory.Exists( folder ) )
			Assert.Inconclusive( "Set OPENTPW_TPWS_SAVES to a folder with original .TPWS/.INTS saves in per-theme subfolders." );
		var files = Directory.EnumerateFiles( folder!, "*", SearchOption.AllDirectories )
			.Where( file => Path.GetExtension( file ).ToUpperInvariant() is ".TPWS" or ".INTS" && Themes.Contains( Path.GetFileName( Path.GetDirectoryName( file ) )!.ToLowerInvariant() ) )
			.OrderBy( file => file, StringComparer.Ordinal ).ToList();
		if ( files.Count == 0 )
			Assert.Inconclusive( "The OPENTPW_TPWS_SAVES folder has no .TPWS/.INTS files in a theme subfolder." );
		var previous = FileSystem;
		var previousLog = Log;
		try
		{
			Log = new();
			FileSystem = new BaseFileSystem( OriginalParkImportTests.OriginalDataPath() );
			FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			foreach ( var path in files )
			{
				var theme = Path.GetFileName( Path.GetDirectoryName( path ) )!.ToLowerInvariant();
				var name = $"{theme}/{Path.GetFileName( path )}";
				using var reader = new SaveReader( path );
				var map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( $"levels/{theme}/terrain.wad", "base.map" ) ) );
				var save = OriginalPark.ImportOriginalSave( theme, reader.ReadFile(), map );
				var economy = new ParkEconomy( BalanceSettings.Load( theme, easy: false ), EconomyObjectCatalog.Load( theme, easy: false ), ParkGameMode.FullSimulation, 1 );
				var import = OriginalEconomyImport.Apply( economy, save );
				check( name, save, economy, import );
			}
		}
		finally
		{
			FileSystem = previous;
			Log = previousLog;
		}
	}

	[TestMethod]
	public void EverySavesLabTakesItsHighestGroupsAndRaisesNoResearchApproximation()
	{
		ForEachSave( ( name, save, economy, import ) =>
		{
			var lab = save.ResearchLab!;
			CollectionAssert.AreEqual( Enumerable.Range( 0, SaveResearchLab.GroupRecords ).Select( index => (int)lab.Group( index ).HighestGroup ).ToArray(),
				Enum.GetValues<ResearchCategory>().Select( economy.Research.HighestOpenGroup ).ToArray(), name );
			Assert.IsTrue( import.Evidence.Any( line => line.StartsWith( "Research highest open groups imported as saved", StringComparison.Ordinal ) ), name );
			Assert.IsFalse( import.Evidence.Any( line => line.Contains( "[APPROX:SAVE-007]", StringComparison.Ordinal ) ), $"{name}: no project or current project is dropped" );
		} );
	}
}
