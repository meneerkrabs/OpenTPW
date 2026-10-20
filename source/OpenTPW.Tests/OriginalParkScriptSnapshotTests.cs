using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// With <c>OPENTPW_GAME_PATH</c>: the scripts a jungle park takes from its shipped original save can be captured. The restart snapshot and the
/// periodic autosave capture the park through the same script-world snapshot, which failed validation ("WAIT4ANIM deadline -1 is not finite
/// and non-negative") because a saved deadline behind the saved clock imported as -1.
/// </summary>
[TestClass, DoNotParallelize]
public class OriginalParkScriptSnapshotTests
{
	[TestMethod]
	public void AJungleParkStartedFromItsShippedSaveCapturesItsScriptWorld()
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
				return new SavedScriptImport.Candidate( attraction.Id, entry, new OriginalObjectRuntime( entry, world, seed: null, open: true ).Script! );
			} ).ToList();
			SavedScriptImport.Apply( world, save.Rsse!, candidates, clock );
			Assert.IsTrue( save.Rsse!.Records.Any( record => record.AnimationWait0 != 0 && unchecked( (int)(record.AnimationWait0 - clock) ) <= 0 ),
				"the shipped save holds a script whose WAIT4ANIM deadline is behind the saved clock" );
			var snapshot = world.CaptureSnapshot();
			snapshot.ValidateSnapshot();
			Assert.IsTrue( snapshot.Scripts.All( script => script.Wait4AnimDeadline >= 0 ) );
			foreach ( var entity in Entity.All.ToArray() )
				entity.Delete();
		}
		finally
		{
			Entity.All = previousEntities;
			FileSystem = previousFiles;
			Log = previousLog;
		}
	}
}
