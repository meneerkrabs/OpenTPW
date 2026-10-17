using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// <c>OPENTPW_TPWS_SAVES</c> with <c>OPENTPW_GAME_PATH</c>: the RSSE block of every original save (the autosaves, named parks and restart.INTS files) is imported into a headless park's objects, and the
/// imported scripts hold the ids and the pass counter the save holds (loader 0x5597A0, DET-013).
/// </summary>
[TestClass]
[DoNotParallelize]
public class OriginalSaveScriptCorpusTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "fantasy", "space" };

	[TestMethod]
	public void EveryCorpusSavesScriptsTakeTheirSavedIdsAndPassCounter()
	{
		var folder = Environment.GetEnvironmentVariable( "OPENTPW_TPWS_SAVES" );
		if ( string.IsNullOrWhiteSpace( folder ) || !Directory.Exists( folder ) )
			Assert.Inconclusive( "Set OPENTPW_TPWS_SAVES to a folder with original .TPWS/.INTS saves in per-theme subfolders." );
		var files = Directory.EnumerateFiles( folder!, "*", SearchOption.AllDirectories )
			.Where( file => Path.GetExtension( file ).ToUpperInvariant() is ".TPWS" or ".INTS" && Themes.Contains( Path.GetFileName( Path.GetDirectoryName( file ) )!.ToLowerInvariant() ) )
			.OrderBy( file => file, StringComparer.Ordinal ).ToList();
		if ( files.Count == 0 )
			Assert.Inconclusive( "The OPENTPW_TPWS_SAVES folder has no .TPWS/.INTS files in a theme subfolder." );
		var previousFiles = FileSystem;
		var previousEntities = Entity.All;
		var previousLog = Log;
		try
		{
			Log = new();
			FileSystem = new BaseFileSystem( OriginalParkImportTests.OriginalDataPath() );
			FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			var scripts = 0;
			foreach ( var path in files )
			{
				var theme = Path.GetFileName( Path.GetDirectoryName( path ) )!.ToLowerInvariant();
				var name = $"{theme}/{Path.GetFileName( path )}";
				Entity.All = new();
				using var reader = new SaveReader( path );
				var park = OriginalPark.Load( theme, readShippedSave: false, savePayload: reader.ReadFile() );
				var save = park.Save!;
				// the park's scripts as the level creates them: one runtime per imported object, in the save's creation order, each with the thing id of its attraction
				var catalog = ObjectCatalog.Load( theme );
				var world = new RideScriptWorld( 7 );
				var candidates = save.Attractions.Select( attraction =>
				{
					var entry = catalog.Find( attraction.InfoId )!;
					return new SavedScriptImport.Candidate( attraction.Id, entry, new OriginalObjectRuntime( entry, world, seed: null, open: true ).Script! );
				} ).ToList();
				var report = SavedScriptImport.Apply( world, save.Rsse!, candidates );
				Assert.IsTrue( report.Single().StartsWith( "RSSE scripts imported:", StringComparison.Ordinal ), $"{name}: {string.Join( "; ", report )}" );
				Assert.AreEqual( (long)save.Rsse!.TickCounter, world.PassCounter, name );
				foreach ( var record in save.Rsse.Records )
				{
					var owner = candidates.Single( candidate => candidate.ThingId == record.ThingId );
					Assert.AreEqual( (int)record.ScriptId, owner.Script.ScriptId, $"{name}: thing {record.ThingId}" );
					Assert.AreSame( owner.Script, world.Find( (int)record.ScriptId ), name );
					scripts++;
				}
				// the walk runs the saved scripts in reverse of the file's order
				CollectionAssert.AreEqual( save.Rsse.Records.Reverse().Select( record => (int)record.ScriptId ).ToArray(), world.ListOrder.Select( script => script.ScriptId ).ToArray(), name );
				Assert.IsTrue( world.Scripts.All( script => script.ScriptId < save.Rsse.NextScriptId ), name );
				foreach ( var entity in Entity.All.ToArray() )
					entity.Delete();
			}
			Assert.IsTrue( scripts >= 11 * 2, $"{scripts} scripts imported" );
		}
		finally
		{
			Entity.All = previousEntities;
			FileSystem = previousFiles;
			Log = previousLog;
		}
	}
}
