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
	[DataRow( 640, 480, 384, 288, 256, 192 )]
	[DataRow( 2560, 1440, 1536, 864, 1024, 576 )]
	public void AdvisorBoxIsTheBottomRightOfTheScreen( int width, int height, int x, int y, int boxWidth, int boxHeight )
	{
		var box = Advisor.BoxRectangle( new Point2( width, height ) );
		Assert.AreEqual( (x, y, boxWidth, boxHeight), box );
		Assert.AreEqual( (width, height), (box.X + box.Width, box.Y + box.Height) );
	}

	[TestMethod]
	public void AdvisorTransformMatchesTheOriginalAt4By3()
	{
		// Composed model point → clip space of the whole 640×480 screen (box clip × box half side + centre).
		var transform = Advisor.ModelTransform( new Point2( 640, 480 ) );
		System.Numerics.Vector3 Screen( System.Numerics.Vector3 point )
		{
			var box = System.Numerics.Vector3.Transform( point, transform );
			return new( box.X * Advisor.BoxHalfSide + Advisor.Centre.X, box.Y * Advisor.BoxHalfSide + Advisor.Centre.Y, box.Z );
		}
		var origin = Screen( System.Numerics.Vector3.Zero );
		Assert.AreEqual( 0.6f, origin.X, 1e-6f );
		Assert.AreEqual( -0.6f, origin.Y, 1e-6f );
		Assert.AreEqual( 0.2f, origin.Z, 1e-6f );
		var unit = Screen( System.Numerics.Vector3.One ) - origin;
		Assert.AreEqual( 0.015f * 0.75f, unit.X, 1e-6f );
		Assert.AreEqual( 0.015f, unit.Y, 1e-6f );
		Assert.AreEqual( 0.001f, unit.Z, 1e-6f );
	}

	[TestMethod]
	public void MouthFollowsTimelineAndDefaultsClosed()
	{
		var timeline = new LipSyncTimeline( new uint[] { 2226893, 2812380, 4058820 } );
		Assert.IsTrue( Advisor.IsTalking( timeline, TimeSpan.Zero ) );
		Assert.IsFalse( Advisor.IsTalking( timeline, TimeSpan.FromSeconds( 2.5 ) ) );
		Assert.IsTrue( Advisor.IsTalking( timeline, TimeSpan.FromSeconds( 3 ) ) );
		Assert.IsFalse( Advisor.IsTalking( timeline, TimeSpan.FromSeconds( 4.1 ) ) );
		Assert.IsFalse( Advisor.IsTalking( null, TimeSpan.Zero ) );
	}

	[TestMethod]
	public void TalkingPicksARandomMouthEvery100Milliseconds()
	{
		var mouth = new AdvisorMouth( new Random( 7 ) );
		Assert.AreEqual( Advisor.ClosedMouth, mouth.Update( false, 0 ) );
		var first = mouth.Update( true, 1 );
		CollectionAssert.Contains( Advisor.Mouths, first );
		Assert.AreEqual( first, mouth.Update( true, 50 ), "no new pick within 100 ms" );
		Assert.AreEqual( first, mouth.Update( true, 101 ), "the next pick needs more than 100 ms" );
		var picks = new System.Collections.Generic.HashSet<string>();
		for ( var time = 102; time < 20000; time += 16 )
			picks.Add( mouth.Update( true, time ) );
		CollectionAssert.AreEquivalent( Advisor.Mouths, picks.ToArray(), "all five mouths are used while talking" );
		Assert.AreEqual( Advisor.ClosedMouth, mouth.Update( false, 20000 ) );
	}

	[TestMethod]
	public void MouthTimelineSampledPerFrameMatchesTalkingIntervals()
	{
		// 60 Hz sampling of sp_001: open 0–2.227 s, closed to 2.812 s, open to 4.059 s.
		var timeline = new LipSyncTimeline( new uint[] { 2226893, 2812380, 4058820 } );
		var frames = Enumerable.Range( 0, 270 ).Select( frame => Advisor.IsTalking( timeline, TimeSpan.FromSeconds( frame / 60.0 ) ) ).ToArray();
		var changes = Enumerable.Range( 1, frames.Length - 1 ).Where( frame => frames[frame] != frames[frame - 1] ).ToArray();
		CollectionAssert.AreEqual( new[] { 134, 169, 244 }, changes );
	}

	[TestMethod]
	public void ApproximationRegisterIsSequentialAndUnique()
	{
		// Traced rules leave the register; their numbers are not reused.
		var retired = new[] { 1, 3, 4, 15, 21 };
		CollectionAssert.AreEqual( Enumerable.Range( 1, 24 ).Except( retired ).Select( index => $"ADVISOR-{index:000}" ).ToArray(), Advisor.Approximations.Select( entry => entry.Id ).ToArray() );
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
	public void MutedSpeechKeepsTheWallClockWithoutOpeningADevice()
	{
		if ( GameAudio.Events != null )
			Assert.Inconclusive( "Game audio was already started in this process." );
		var enabled = GameAudio.Enabled;
		GameAudio.Enabled = false;
		try
		{
			using var player = Advisor.CreatePlayer( new Mp2Audio( 22050, 1, new short[2205], 1, 0 ) );
			Assert.AreEqual( "wall clock (no audio device)", player.ClockSource );
			Assert.IsNull( player.DeviceError, "no SDL device was attempted" );
			Assert.IsNull( AudioMixer.Current );
			player.Start();
			Assert.IsTrue( player.Position >= TimeSpan.Zero );
		}
		finally
		{
			GameAudio.Enabled = enabled;
		}
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
		Assert.IsTrue( eye.Max.Z < head.Min.Z && mouth.Min.Z < head.Min.Z, "the face is on the −Z side, towards the viewer (nearer depth)" );
		foreach ( var name in Advisor.Mouths.Skip( 1 ) )
		{
			var other = Bounds( name );
			Assert.IsTrue( NumericVector.Distance( mouth.Min, other.Min ) < 1e-3f && NumericVector.Distance( mouth.Max, other.Max ) < 1e-3f, name );
		}
		foreach ( var name in Advisor.BodyMeshes )
			Assert.IsTrue( model.Meshes.Any( mesh => mesh.Name == name ), name );
		// The whole model fits the box it is drawn in, on 4:3 and on wider screens.
		foreach ( var output in new[] { new Point2( 640, 480 ), new Point2( 2560, 1080 ) } )
		{
			var transform = Advisor.ModelTransform( output );
			foreach ( var mesh in model.Meshes )
				foreach ( var vertex in Advisor.ConvertMesh( mesh, Advisor.NodeWorld( model, mesh.NodeIndex ) ) )
				{
					var box = NumericVector.Transform( vertex.Position.GetSystemVector3(), transform );
					Assert.IsTrue( MathF.Abs( box.X ) < 1 && MathF.Abs( box.Y ) < 1 && box.Z > 0 && box.Z < 1, $"{mesh.Name} at {output.X}x{output.Y}: {box}" );
				}
		}
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

	[TestMethod]
	public void AdvisorClockStandsStillWhilePausedAndContinuesFromTheSameValue()
	{
		long now = 1000;
		var clock = new PausableClock( () => now );
		Assert.AreEqual( 1000u, clock.Milliseconds );
		clock.SetPaused( true );
		now = 5000;
		Assert.AreEqual( 1000u, clock.Milliseconds, "frozen at the pause snapshot" );
		clock.SetPaused( true );
		now = 6000;
		clock.SetPaused( false );
		Assert.AreEqual( 1000u, clock.Milliseconds, "the paused 5 s are compensated" );
		now = 6250;
		Assert.AreEqual( 1250u, clock.Milliseconds );
	}
}
