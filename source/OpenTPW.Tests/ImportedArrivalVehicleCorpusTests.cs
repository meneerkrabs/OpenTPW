using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// ARRIVAL-001 on the real saves: the cached bus, seaplane and Ferry handles (world <c>+0x1DA72C/E/30</c>) are the thing ids of the vehicle fixed items the saves hold, and the vehicle at work
/// (<c>+0x1DA72A</c>, the Fantasy saves' seaplane) is one of them: the data the import's validation (<see cref="GuestSimulation.ImportOriginalArrivals"/>) relies on. The level's objects need the GPU
/// for their textures, so the import itself is tested on a synthetic host (ArrivalVehicleTests). Needs <c>OPENTPW_GAME_PATH</c> and <c>OPENTPW_TPWS_SAVES</c>.
/// </summary>
[TestClass, DoNotParallelize]
public class ImportedArrivalVehicleCorpusTests
{
	private BaseFileSystem? originalFileSystem;
	private bool initialized;
	private GameEdition edition;

	[TestInitialize]
	public void Setup()
	{
		edition = GameEditions.Current;
		GameEditions.Select( GameEdition.ThemeParkWorld );
		Log ??= new();
	}

	[TestCleanup]
	public void Cleanup()
	{
		GameEditions.Select( edition );
		if ( initialized )
			FileSystem = originalFileSystem!;
	}

	[TestMethod]
	public void EveryCorpusSavesVehicleHandlesAreItsVehicleThings()
	{
		var folder = Environment.GetEnvironmentVariable( "OPENTPW_TPWS_SAVES" );
		if ( string.IsNullOrWhiteSpace( folder ) || !Directory.Exists( folder ) )
			Assert.Inconclusive( "Set OPENTPW_TPWS_SAVES to a folder with original .TPWS/.INTS saves in per-theme subfolders." );
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		originalFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		initialized = true;
		var checkedSaves = 0;
		var atWork = 0;
		foreach ( var file in Directory.EnumerateFiles( folder!, "*", SearchOption.AllDirectories ).Where( path => Path.GetExtension( path ).ToUpperInvariant() is ".TPWS" or ".INTS" ).OrderBy( path => path, StringComparer.Ordinal ) )
		{
			var theme = Path.GetFileName( Path.GetDirectoryName( file ) )!.ToLowerInvariant();
			if ( theme is not ("jungle" or "hallow" or "fantasy" or "space") )
				continue;
			using var reader = new SaveReader( file );
			reader.Inspect();
			var payload = reader.ReadFile();
			var map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( $"levels/{theme}/terrain.wad", "base.map" ) ) );
			var save = OriginalParkImport.Import( payload, map );
			var variables = save.World.Variables;
			var catalog = ObjectCatalog.Load( theme );
			var name = $"{theme}/{Path.GetFileName( file )}";
			var cached = new[] { (int)variables.ArrivalVehicleSize1, variables.ArrivalVehicleSize2, variables.ArrivalVehicleSize3 };
			// a handle is the thing id of the vehicle object: the saved attraction with that thing id is the vehicle's fixed item (bus 4600, seaplane 4602, Ferry 4604 in the shipped definitions)
			for ( var kind = 0; kind < cached.Length; kind++ )
			{
				if ( cached[kind] == 0 )
					continue;
				var thing = save.World.Find( cached[kind] );
				Assert.IsNotNull( thing, $"{name}: the {ArrivalVehicleKind.Names[kind]} handle {cached[kind]} names a thing" );
				Assert.AreEqual( SaveThingModel.Attraction, thing.Value.Model, name );
				var attraction = save.Attractions.Single( item => item.Id == cached[kind] );
				Assert.IsTrue( string.Equals( ArrivalVehicleKind.Names[kind], ObjectCatalog.ArchiveKey( catalog.Find( attraction.InfoId )!.ArchiveName ), StringComparison.OrdinalIgnoreCase ), $"{name}: the thing is the {ArrivalVehicleKind.Names[kind]}" );
			}
			if ( variables.CurrentArrivalVehicle != 0 )
			{
				CollectionAssert.Contains( cached, (int)variables.CurrentArrivalVehicle, $"{name}: the vehicle at work is one of the cached ones" );
				atWork++;
			}
			checkedSaves++;
		}
		Assert.IsTrue( checkedSaves > 0 );
		Assert.IsTrue( atWork > 0, "a corpus save holds a vehicle at work (the Fantasy seaplane)" );
	}
}
