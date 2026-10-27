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
			Assert.IsFalse( import.Evidence.Any( line => line.StartsWith( "Research not imported", StringComparison.Ordinal ) ), $"{name}: no project or current project is dropped" );
		} );
	}

	/// <summary>
	/// The 11 corpus labs, pinned: the projects, the started and researched counts, the effort split, the work load, the current projects and the object controls' agreement, as the import reports them
	/// (the lab loader 0x5031A0 and the research step 0x504630 write what the save holds; none of the 11 holds a project of an object the archives lack or a current project the picker would not offer).
	/// </summary>
	[TestMethod]
	public void TheElevenCorpusLabsArePinned()
	{
		var expected = new Dictionary<string, (string Groups, string Imported, string Controls)>
		{
			["fantasy/Wonder Land.TPWS"] = ("7/7/7/7/7", "68 projects, 0 started, 66 researched by the lab, effort 0/0/0/100/100, work load 70, current none.", "66 records, 66 researched, 58 research items checked"),
			["fantasy/autosave.TPWS"] = ("7/7/7/7/7", "68 projects, 0 started, 66 researched by the lab, effort 0/0/0/100/100, work load 70, current none.", "66 records, 66 researched, 58 research items checked"),
			["fantasy/restart.INTS"] = ("1/1/1/1/1", "44 projects, 0 started, 0 researched by the lab, effort 100/15/30/15/10, work load 85, current Ride 4104, Shop 4202, Sideshow 4300, Feature 4404, Upgrade 4140.", "66 records, 28 researched, 58 research items checked"),
			["hallow/Halloween World.TPWS"] = ("7/7/7/7/7", "78 projects, 0 started, 76 researched by the lab, effort 0/0/0/100/0, work load 91, current none.", "70 records, 70 researched, 62 research items checked"),
			["hallow/autosave.TPWS"] = ("7/7/7/7/7", "78 projects, 0 started, 76 researched by the lab, effort 0/0/0/100/0, work load 91, current none.", "70 records, 70 researched, 62 research items checked"),
			["hallow/restart.INTS"] = ("1/1/1/1/1", "48 projects, 0 started, 0 researched by the lab, effort 100/15/30/15/10, work load 85, current Ride 2110, Shop 2204, Sideshow 2303, Feature 2421, Upgrade 2116.", "70 records, 28 researched, 62 research items checked"),
			["jungle/Lost Kingdom.TPWS"] = ("7/7/7/7/7", "74 projects, 0 started, 72 researched by the lab, effort 0/0/0/100/0, work load 96, current none.", "70 records, 70 researched, 62 research items checked"),
			["jungle/autosave.TPWS"] = ("7/7/7/7/7", "74 projects, 0 started, 72 researched by the lab, effort 0/0/0/100/0, work load 96, current none.", "70 records, 70 researched, 62 research items checked"),
			["jungle/restart.INTS"] = ("1/1/1/1/1", "48 projects, 0 started, 0 researched by the lab, effort 100/15/30/15/10, work load 85, current Ride 1150, Shop 1212, Sideshow 1304, Feature 1414, Upgrade 1100.", "70 records, 28 researched, 62 research items checked"),
			["space/Space Zone.TPWS"] = ("7/7/7/7/7", "76 projects, 0 started, 73 researched by the lab, effort 0/0/0/100/0, work load 94, current none.", "68 records, 68 researched, 60 research items checked"),
			["space/restart.INTS"] = ("1/1/1/1/1", "46 projects, 0 started, 0 researched by the lab, effort 100/15/30/15/10, work load 85, current Ride 3101, Shop 3202, Sideshow 3303, Feature 3400, Upgrade 3108.", "68 records, 29 researched, 60 research items checked"),
		};
		var seen = new HashSet<string>();
		ForEachSave( ( name, save, economy, import ) =>
		{
			// the corpus folder may hold more saves than the 11 pinned ones: only the pinned names are compared
			if ( !expected.TryGetValue( name, out var pin ) )
				return;
			seen.Add( name );
			Assert.IsTrue( import.Evidence.Contains( $"Research highest open groups imported as saved: {pin.Groups}, the groups the researched projects give." ), $"{name}: groups" );
			Assert.IsTrue( import.Evidence.Contains( $"Research imported: {pin.Imported}" ), $"{name}: lab" );
			Assert.IsTrue( import.Evidence.Any( line => line.StartsWith( $"Research object controls: {pin.Controls}", StringComparison.Ordinal ) && line.EndsWith( "none disagree.", StringComparison.Ordinal ) ), $"{name}: controls" );
			Assert.IsFalse( import.Evidence.Any( line => line.StartsWith( "Research not imported", StringComparison.Ordinal ) ), $"{name}: nothing dropped" );
		} );
		CollectionAssert.AreEquivalent( expected.Keys.ToArray(), seen.ToArray(), "every pinned save was in the corpus" );
	}

	[TestMethod]
	public void EverySavesChallengeSlotsAreTheLevelsAndAdoptNothing()
	{
		ForEachSave( ( name, save, economy, import ) =>
		{
			Assert.IsNull( economy.Settings.AdoptedChallenges, name );
			Assert.IsFalse( import.Evidence.Any( line => line.Contains( "[APPROX:SAVE-005]", StringComparison.Ordinal ) ), name );
			Assert.IsTrue( import.Evidence.Any( line => line.Contains( "challenge records equal Challenges[", StringComparison.Ordinal ) ), name );
		} );
	}
}
