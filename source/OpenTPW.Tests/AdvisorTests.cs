using System;
using System.IO;
using System.Linq;
using NumericVector = System.Numerics.Vector3;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class AdvisorTests
{
	[DataTestMethod]
	[DataRow( 1024, 768, 16, 496, 256 )]
	[DataRow( 512, 384, 8, 248, 128 )]
	[DataRow( 2048, 1536, 32, 992, 512 )]
	public void AdvisorViewportMapsLogicalPlacementIntoTheWorldTarget( int width, int height, int x, int y, int size )
	{
		var viewport = Advisor.ViewportRectangle( new Point2( 1024, 768 ), new Point2( width, height ) );
		Assert.AreEqual( (x, y, size), viewport );
		Assert.IsTrue( viewport.X + viewport.Size <= width && viewport.Y + viewport.Size <= height );
	}

	[TestMethod]
	public void MouthFollowsTimelineAndDefaultsClosed()
	{
		var timeline = new LipSyncTimeline( new uint[] { 2226893, 2812380, 4058820 } );
		Assert.AreEqual( Advisor.TalkingMouth, Advisor.MouthFor( timeline, TimeSpan.Zero ) );
		Assert.AreEqual( Advisor.ClosedMouth, Advisor.MouthFor( timeline, TimeSpan.FromSeconds( 2.5 ) ) );
		Assert.AreEqual( Advisor.TalkingMouth, Advisor.MouthFor( timeline, TimeSpan.FromSeconds( 3 ) ) );
		Assert.AreEqual( Advisor.ClosedMouth, Advisor.MouthFor( timeline, TimeSpan.FromSeconds( 4.1 ) ) );
		Assert.AreEqual( Advisor.ClosedMouth, Advisor.MouthFor( null, TimeSpan.Zero ) );
	}

	[TestMethod]
	public void MouthTimelineSampledPerFrameMatchesTalkingIntervals()
	{
		// 60 Hz sampling of sp_001: open 0–2.227 s, closed to 2.812 s, open to 4.059 s.
		var timeline = new LipSyncTimeline( new uint[] { 2226893, 2812380, 4058820 } );
		var frames = Enumerable.Range( 0, 270 ).Select( frame => Advisor.MouthFor( timeline, TimeSpan.FromSeconds( frame / 60.0 ) ) ).ToArray();
		var changes = Enumerable.Range( 1, frames.Length - 1 ).Where( frame => frames[frame] != frames[frame - 1] ).ToArray();
		CollectionAssert.AreEqual( new[] { 134, 169, 244 }, changes );
	}

	[TestMethod]
	public void ApproximationRegisterIsSequentialAndUnique()
	{
		CollectionAssert.AreEqual( Enumerable.Range( 1, 14 ).Select( index => $"ADVISOR-{index:000}" ).ToArray(), Advisor.Approximations.Select( entry => entry.Id ).ToArray() );
	}

	[DataTestMethod]
	[DataRow( 1, "sp_001" )]
	[DataRow( 42, "sp_042" )]
	[DataRow( 637, "sp_637" )]
	public void ClipNamesUseThreeDigits( int number, string name )
	{
		Assert.AreEqual( name, Advisor.ClipName( number ) );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 638 )]
	public void RejectsClipNumbersOutsideTheBank( int number )
	{
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Advisor.ClipName( number ) );
	}

	[TestMethod]
	public void ReversesTriangleWindingWithoutTouchingInput()
	{
		var indices = new uint[] { 0, 1, 2, 3, 4, 5 };
		CollectionAssert.AreEqual( new uint[] { 0, 2, 1, 3, 5, 4 }, Advisor.ReverseWinding( indices ) );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2, 3, 4, 5 }, indices );
	}

	[TestMethod]
	public void SpeechPositionFollowsFramesTakenByTheSharedAudioOutput()
	{
		var audio = new Mp2Audio( 22050, 1, new short[22050], 1, 0 );
		var output = new SimulatedMovieAudioOutput( 22050 );
		using var player = new SpeechAudioPlayer( audio, output );
		Assert.AreEqual( 22050, output.QueuedFrames, "mono is queued as one stereo frame per sample" );
		Assert.IsFalse( player.IsStarted );
		output.Advance( 0.5 );
		Assert.AreEqual( TimeSpan.Zero, player.Position, "the device stays paused until Start" );
		player.Start();
		output.Advance( 0.5 );
		Assert.AreEqual( 0.5, player.Position.TotalSeconds, 1e-4 );
		Assert.IsFalse( player.IsFinished );
		output.Advance( 0.6 );
		Assert.IsTrue( player.IsFinished );
		CollectionAssert.AreEqual( new short[] { 7, 7, -3, -3 }, SpeechAudioPlayer.ToStereo( new Mp2Audio( 22050, 1, new short[] { 7, -3 }, 1, 0 ) ) );
	}

	[TestMethod]
	public void OriginalAdvisorClipsAreNotRigidOnly()
	{
		using var assets = new OriginalAssets();
		var model = new ModelFile( $"{Advisor.ArchivePath}/Advisor.MD2" );
		for ( var index = 1; index <= 15; index++ )
		{
			var clip = new ModelFile( $"{Advisor.ArchivePath}/Advisorm{index}.MD2" ).Clip;
			Assert.IsNotNull( clip, $"Advisorm{index}" );
			Assert.IsTrue( clip!.Tracks.Any( track => track.HasUndecodedPayload ), $"Advisorm{index} would be playable as rigid tracks" );
		}
		// Advisorm13 is the only clip with tracks on the five mouth meshes (payload undecoded):
		// the likeliest source of the original mouth-shape choice.
		var mouths = new ModelFile( $"{Advisor.ArchivePath}/Advisorm13.MD2" ).Clip!.Tracks.Select( track => model.Nodes[track.NodeIndex].Name ).Where( name => name.StartsWith( "Mouth - " ) ).OrderBy( name => name ).ToArray();
		CollectionAssert.AreEqual( new[] { "Mouth - Aah", "Mouth - Eee", "Mouth - Normal", "Mouth - Ooh", "Mouth - Sss" }, mouths );
	}

	[TestMethod]
	public void OriginalAdvisorStandsUprightFacingTheCameraWithCoLocatedMouths()
	{
		using var assets = new OriginalAssets();
		var model = new ModelFile( $"{Advisor.ArchivePath}/Advisor.MD2" );
		(NumericVector Min, NumericVector Max) Bounds( string name )
		{
			var mesh = model.Meshes.Single( candidate => candidate.Name == name );
			var points = Advisor.ConvertMesh( mesh, Advisor.NodeWorld( model, mesh.NodeIndex ) ).Select( vertex => vertex.Position.GetSystemVector3() ).ToArray();
			return (points.Aggregate( NumericVector.Min ), points.Aggregate( NumericVector.Max ));
		}
		var head = Bounds( "Bug Head" );
		var antenna = Bounds( "Left Antennae" );
		var body = Bounds( "Body" );
		var eye = Bounds( "Left Eye" );
		var mouth = Bounds( Advisor.ClosedMouth );
		Assert.IsTrue( antenna.Min.Y > head.Min.Y && body.Max.Y < head.Min.Y + 1, "Y is up after composing the node hierarchy" );
		Assert.IsTrue( eye.Max.Z < head.Min.Z && mouth.Min.Z < head.Min.Z, "the face is on the −Z side, towards the overlay camera" );
		foreach ( var name in new[] { Advisor.TalkingMouth, "Mouth - Eee", "Mouth - Ooh", "Mouth - Sss" } )
		{
			var other = Bounds( name );
			Assert.IsTrue( NumericVector.Distance( mouth.Min, other.Min ) < 1e-3f && NumericVector.Distance( mouth.Max, other.Max ) < 1e-3f, name );
		}
		foreach ( var name in Advisor.BodyMeshes )
			Assert.IsTrue( model.Meshes.Any( mesh => mesh.Name == name ), name );
	}

	[TestMethod]
	public void OriginalClipLoadsThroughTheGameFileSystem()
	{
		using var assets = new OriginalAssets();
		var (audio, timeline, _) = Advisor.LoadClip( FileSystem, 1 );
		Assert.AreEqual( 22050, audio.SampleRate );
		Assert.AreEqual( 93312, audio.Samples.Length );
		CollectionAssert.AreEqual( new uint[] { 2226893, 2812380, 4058820 }, timeline.Marks.ToArray() );
		Assert.IsTrue( timeline.EndMicroseconds < audio.DurationSeconds * 1e6 );
	}

	[TestMethod]
	public void OriginalClipFollowsTheSelectedLanguage()
	{
		using var assets = new OriginalAssets();
		var overlay = Environment.GetEnvironmentVariable( "OPENTPW_LANGUAGE_DATA" );
		if ( string.IsNullOrWhiteSpace( overlay ) || !Directory.Exists( overlay ) )
			Assert.Inconclusive( "Set OPENTPW_LANGUAGE_DATA to the CD language data for the German speech bank." );
		var german = GameLanguage.Resolve( FileSystem.GetAbsolutePath( "/" ), "German", overlay );
		var (audio, timeline, source) = Advisor.LoadClip( FileSystem, 1, german );
		StringAssert.Contains( source, "German" );
		CollectionAssert.AreEqual( new uint[] { 3272743, 3767619, 6915192 }, timeline.Marks.ToArray() );
		Assert.IsTrue( timeline.EndMicroseconds < audio.DurationSeconds * 1e6 );
		var english = GameLanguage.Resolve( FileSystem.GetAbsolutePath( "/" ), "English", null );
		CollectionAssert.AreEqual( new uint[] { 2226893, 2812380, 4058820 }, Advisor.LoadClip( FileSystem, 1, english ).Timeline.Marks.ToArray() );
	}

	private sealed class OriginalAssets : IDisposable
	{
		private readonly BaseFileSystem? original;

		public OriginalAssets()
		{
			var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
			if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
				Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original advisor assets." );
			var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
			if ( !File.Exists( Path.Combine( dataPath, "global", "advisor.wad" ) ) || !File.Exists( Path.Combine( dataPath, "global", "Speech", "speechHD.SDT" ) ) )
				Assert.Inconclusive( "The original advisor WAD or speech bank is missing." );
			original = FileSystem;
			FileSystem = new BaseFileSystem( dataPath );
			FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			FileSystem.RegisterArchiveHandler<SdtArchive>( ".sdt" );
		}

		public void Dispose() => FileSystem = original!;
	}
}
