using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// <c>OPENTPW_GAME_PATH</c>: the shipped Easymode saves' RSSE records hold what the loader 0x5597A0 reads beyond the run state, and the import gives it to the objects: each script's NAME offset (0x0055429A) is its name
/// again, and the object list's kept particle effects (ADDOBJ, 0x00551F5F) are the objects' records again (the Jungle coconut shop holds the effect with the emitter handle 0x7A0014 in its slot 1).
/// </summary>
[TestClass]
[DoNotParallelize]
public class SavedScriptObjectsShippedTests
{
	[TestMethod]
	public void TheShippedJungleSavesScriptsKeepTheirNamesAndTheCoconutShopItsKeptEffect()
	{
		var previousFiles = FileSystem;
		var previousEntities = Entity.All;
		var previousLog = Log;
		try
		{
			Log = new();
			FileSystem = new BaseFileSystem( OriginalParkImportTests.OriginalDataPath() );
			FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			Entity.All = new();
			var park = OriginalPark.Load( "jungle" );
			var save = park.Save!;
			var clock = save.RuntimeBlocks!.ClockWords[0];
			var catalog = ObjectCatalog.Load( "jungle" );
			var world = new RideScriptWorld( 7 );
			var candidates = save.Attractions.Select( attraction =>
			{
				var entry = catalog.Find( attraction.InfoId )!;
				return new SavedScriptImport.Candidate( attraction.Id, entry, null!, new OriginalObjectRuntime( entry, world, seed: null, open: true ) );
			} ).Select( candidate => candidate with { Script = candidate.Runtime!.Script! } ).ToList();
			var report = SavedScriptImport.Apply( world, save.Rsse!, candidates, clock );
			Assert.IsFalse( report.Any( line => line.Contains( "kept fresh", StringComparison.Ordinal ) ), string.Join( "; ", report ) );
			var named = 0;
			foreach ( var record in save.Rsse!.Records )
			{
				var owner = candidates.Single( candidate => candidate.ThingId == record.ThingId );
				if ( record.Name == uint.MaxValue )
					continue;
				var start = (int)record.Name;
				var end = Array.IndexOf( record.Strings, (byte)0, start );
				Assert.AreEqual( Encoding.Latin1.GetString( record.Strings, start, end - start ), owner.Script.ScriptName, $"thing {record.ThingId}" );
				named++;
			}
			Assert.IsTrue( named > 0, "some of the shipped scripts have run NAME" );
			// the coconut shop's record holds one type-1 object: the kept effect under slot 1 with key 1
			var coconut = save.Rsse.Records.Single( record => record.Folder.Contains( "coconut", StringComparison.OrdinalIgnoreCase ) );
			Assert.AreEqual( (1u, 0x7A0014u, 1u), (coconut.Objects.Single().Type, coconut.Objects.Single().RuntimeHandle, coconut.Objects.Single().Key) );
			var effects = candidates.Single( candidate => candidate.ThingId == coconut.ThingId ).Runtime!.CaptureSnapshot().ParticleObjects!;
			Assert.AreEqual( (1, 0x7A0014, 1, 1), (effects.Single().Slot, effects.Single().Handle, effects.Single().Type, effects.Single().Key) );
			// the fountain and the pelican bin hold kept sounds (types 3 and 5): not restored, and said so
			StringAssert.Contains( string.Join( "; ", report ), "kept sounds" );
		}
		finally
		{
			foreach ( var entity in Entity.All.ToArray() )
				entity.Delete();
			Entity.All = previousEntities;
			FileSystem = previousFiles;
			Log = previousLog;
		}
	}
}
