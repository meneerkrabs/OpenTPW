using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Asset-free coverage of OpenTPW's missing-model fallback, not a native-game parity claim.</summary>
[TestClass]
public class CoasterMissingPylonTests
{
	private string directory = null!;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		directory = Directory.CreateTempSubdirectory( "opentpw-missing-pylon-" ).FullName;
		Directory.CreateDirectory( Path.Combine( directory, "synthetic" ) );
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete( directory, recursive: true );

	private ObjectCatalogEntry Entry( bool malformed )
	{
		// Distinct ranges catch mixing up the standard and loop pylon controls.
		var sam = "Info.Id 1000\nBumper.WhichTrackType 3\n"
			+ "asPylonControls[0].pcMeshFilename StdPylon\n"
			+ "asPylonControls[0].fMinHeight 12\nasPylonControls[0].fMaxHeight 88\n"
			+ "asPylonControls[1].pcMeshFilename LoopPylon\n"
			+ "asPylonControls[1].fMinHeight 17\nasPylonControls[1].fMaxHeight 97\n";
		File.WriteAllText( Path.Combine( directory, "synthetic", "coaster.sam" ), sam );
		if ( malformed )
		{
			// A short header is invalid data, not a missing file or a platform-dependent permission error.
			foreach ( var stem in new[] { "StdPylon", "LoopPylon" } )
				File.WriteAllBytes( Path.Combine( directory, "synthetic", stem + ".MD2" ), new byte[] { 0 } );
		}
		var fileSystem = new BaseFileSystem( directory );
		var settings = new ObjectSettings( new[] { ObjectSettingsFile.Load( fileSystem, "/synthetic/coaster.sam" ) } );
		return new ObjectCatalogEntry( "test", ObjectCategory.Ride, "/synthetic", settings, ObjectShape.Single,
			"/synthetic/station.MD2", null, null, Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(),
			Array.Empty<string>(), Array.Empty<string>(), fileSystem, null );
	}

	private static void AssertFallback( CoasterPylonControl pylon, float low, float high )
	{
		Assert.AreEqual( low, pylon.MinHeight );
		Assert.AreEqual( high, pylon.MaxHeight );
		Assert.AreEqual( low, pylon.MeshHeightLow );
		Assert.AreEqual( high, pylon.MeshHeightHigh );
		Assert.IsFalse( pylon.HasModelHeights );
		Assert.IsNull( pylon.TagModel, "no model means no invented tag table" );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void UnavailablePylonsKeepCatalogHeightRanges( bool malformed )
	{
		var entry = Entry( malformed );
		Assert.IsTrue( CoasterDefinition.IsCoaster( entry.Settings ) );
		if ( malformed )
		{
			// Prove that the listed MD2 members really reach the reader and fail as invalid data.
			foreach ( var stem in new[] { "StdPylon", "LoopPylon" } )
				Assert.ThrowsException<InvalidDataException>( () => ObjectAssets.LoadModel( entry.FileSystem, "/synthetic/" + stem + ".MD2" ) );
		}
		else
			Assert.IsFalse( entry.FileSystem.GetFiles( entry.ArchivePath ).Any( path => path.EndsWith( ".MD2", StringComparison.OrdinalIgnoreCase ) ) );

		using var assets = new CoasterAssets( entry, CoasterDefinition.FromEntry( entry ) );
		foreach ( var loop in new[] { false, true } )
		{
			Assert.IsNull( assets.GetPylon( loop ) );
			Assert.IsNull( assets.GetPylon( loop ), "a repeated request still has no rig" );
		}
		for ( var attempt = 0; attempt < 2; attempt++ )
		{
			var definition = CoasterDefinition.FromCatalogEntry( entry );
			Assert.IsTrue( definition.HasLoopPylon );
			AssertFallback( definition.StandardPylon, 12, 88 );
			AssertFallback( definition.LoopPylon, 17, 97 );
		}
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CatalogWithoutPylonModelsKeepsOneKnotPerNodeOnRepeatedPasses( bool malformed )
	{
		var definition = CoasterDefinition.FromCatalogEntry( Entry( malformed ) );
		var ride = new CoasterRide( definition, new CoasterWorld( 1 ), "Synthetic" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, (20, 20) );
		var track = builder.Track;
		var first = track.BuildKnotPass( builder.Site, writeBanks: false );
		var repeated = track.BuildKnotPass( builder.Site, writeBanks: false );
		Assert.IsTrue( track.Nodes.Count > 0 );
		Assert.AreEqual( track.Nodes.Count, first.Knots.Length );
		Assert.AreEqual( first.Knots.Length, repeated.Knots.Length );
		for ( var index = 0; index < track.Nodes.Count; index++ )
		{
			Assert.AreEqual( 1, first.KnotCount[index] );
			Assert.AreEqual( 1, repeated.KnotCount[index] );
			Assert.AreEqual( first.Positions[index], first.Knots[first.FirstKnot[index]].Position );
			Assert.AreEqual( first.Knots[index].Position, repeated.Knots[index].Position );
		}
	}
}
