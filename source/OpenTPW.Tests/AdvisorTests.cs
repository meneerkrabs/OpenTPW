using System.Collections.Generic;
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
	public void AdvisorShaderLightsEachVertexWithTheOriginalAmbientAndLightColour()
	{
		// 0x573FD0 default ambient 0.15 grey, 0x5741B0 per-vertex ambient + colour x max(0, -L.N) clamped per channel; the advisor's light colour is 1.0 (0x429BA0).
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !(File.Exists( Path.Combine( directory.FullName, "content", "shaders", "advisor.shader" ) ) && Directory.Exists( Path.Combine( directory.FullName, "source" ) )) )
			directory = directory.Parent;
		Assert.IsNotNull( directory, "The repository checkout must be available for this test." );
		var source = File.ReadAllText( Path.Combine( directory.FullName, "content", "shaders", "advisor.shader" ) );
		StringAssert.Contains( source, "vs_out.vLight = clamp(vec3(0.15) + max(dot(N, L), 0.0) * g_oUbo.g_vLightColor, 0.0, 1.0);" );
		Assert.IsFalse( source.Contains( "fog", StringComparison.OrdinalIgnoreCase ), "the fog of the advisor pass is not read, so none is drawn" );
		var advisor = File.ReadAllText( Path.Combine( directory.FullName, "source", "OpenTPW", "World", "Advisor.cs" ) );
		StringAssert.Contains( advisor, "g_vLightColor = System.Numerics.Vector3.One," );
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
		// 11 (a deliberate extension), 16 (the fixed rows) and 18 (the model hides at the end of the last clip) left in the MISC round of October 14;
		// 7 (no face test before the submit's transform) and 10 (the game's mouth runs on the advisor clock; the tools' audio clock is an extension) in the read-audio round;
		// 27 (the slap on cut-off and the F1 replay) in the advisor presentation round;
		// 20 (event 2 is the bank's monthly message, 0x4D0370) in the economy rules round;
		// 6 (a clip the archive lacks: the start 0x472F60 clears the animation of the model, which keeps its rest pose) and 22 (all 351 descriptors of table 0x76E300 are bound, so an original save's controller is imported whole) in the audio-fx sweep;
		// 17 (the queue is served once per park turn, 0x59A550) in the pause and loss round (October 26);
		// 26 (the node bits: every clip end clears all of them, 0x472310 with the advisor's model flag 8, so no state persists between clips or responses) in the lip round.
		var retired = new[] { 1, 2, 3, 4, 6, 7, 8, 10, 11, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 26, 27 };
		CollectionAssert.AreEqual( Enumerable.Range( 1, 28 ).Except( retired ).Select( index => $"ADVISOR-{index:000}" ).ToArray(), Advisor.Approximations.Select( entry => entry.Id ).ToArray() );
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

	/// <summary>
	/// Every record of the global and level advisor clips decodes (ANIM-plan §10: ADVISOR-006's "undecoded non-rigid
	/// tracks" premise is stale) and plays against its model; only one level clip carries path parameters.
	/// </summary>
	[TestMethod]
	public void OriginalAdvisorClipsDecodeAndPlayAgainstTheirModels()
	{
		using var assets = new OriginalAssets();
		Assert.AreEqual( "Advisorm3.MD2", Advisor.AnimationClipName( "", 3 ) );
		Assert.AreEqual( "Advisor_FANm16.MD2", Advisor.AnimationClipName( "_FAN", 16 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Advisor.AnimationClipName( "", 0 ) );
		int clips = 0, withPathParameters = 0, withUv = 0;
		foreach ( var (archive, suffix) in new[] { (Advisor.ArchivePath, ""), ("/levels/fantasy/advisor", "_FAN"), ("/levels/hallow/advisor", "_HAL"),
			("/levels/jungle/advisor", "_JUN"), ("/levels/space/advisor", "_SPA") } )
		{
			var model = new ModelFile( $"{archive}/Advisor{suffix}.MD2" );
			var members = FileSystem.GetFiles( archive ).Select( Path.GetFileName ).ToHashSet( StringComparer.OrdinalIgnoreCase );
			Assert.IsTrue( members.Contains( Advisor.AnimationClipName( suffix, 1 ) ), archive );
			for ( var index = 1; members.Contains( Advisor.AnimationClipName( suffix, index ) ); index++ )
			{
				var name = Advisor.AnimationClipName( suffix, index );
				var clip = new ModelFile( $"{archive}/{name}" ).Clip!;
				clips++;
				Assert.AreEqual( 0u, clip.Tracks.Aggregate( 0u, ( flags, track ) => flags | track.UnsupportedFlags ), name );
				withPathParameters += clip.Tracks.Any( track => track.PathParameters != null ) ? 1 : 0;
				withUv += clip.Tracks.Any( track => track.UvAnimation != null ) ? 1 : 0;
				Assert.IsTrue( clip.Tracks.All( track => track.VertexPose == null ), $"{name} has no 12-byte block" );
				var animator = new ObjectAnimator( model );
				animator.Play( 0, clip, name, loop: false );
				for ( var step = 1; step <= 4; step++ )
					animator.Advance( clip.Duration / 4.0 / animator.TicksPerSecond );
				Assert.AreEqual( 0, animator.VertexLimitations.Count, $"{name}: {string.Join( "; ", animator.VertexLimitations )}" );
				Assert.IsTrue( animator.NodeTransforms.All( matrix => float.IsFinite( matrix.M41 ) && float.IsFinite( matrix.M42 ) && float.IsFinite( matrix.M43 ) ), name );
			}
		}
		Assert.IsTrue( clips >= 15 );
		Assert.AreEqual( 1, withPathParameters, "Advisor_HALm16 is the one clip with path records" );
		Assert.IsTrue( withUv > 0, "the level advisors carry texture-coordinate tracks" );

		// The global clips' node lists set node bit 0x10 on the hats, spatula and bow tie (ADVISOR-002's hidden set).
		var global = new ModelFile( $"{Advisor.ArchivePath}/Advisor.MD2" );
		var listed = new ModelFile( $"{Advisor.ArchivePath}/Advisorm1.MD2" ).Clip!.NodeList.Select( node => global.Nodes[node].Name ).ToArray();
		Assert.IsTrue( listed.Contains( "Spatula" ) && listed.Contains( "Bowler_BTie" ) && listed.All( name => !Advisor.BodyMeshes.Contains( name ) ), string.Join( ", ", listed ) );
		// Advisorm13 is the only clip with tracks on the five mouth meshes: the likeliest source of the original mouth-shape choice.
		var mouths = new ModelFile( $"{Advisor.ArchivePath}/Advisorm13.MD2" ).Clip!.Tracks.Select( track => global.Nodes[track.NodeIndex].Name ).Where( name => name.StartsWith( "Mouth - " ) ).OrderBy( name => name ).ToArray();
		CollectionAssert.AreEqual( new[] { "Mouth - Aah", "Mouth - Eee", "Mouth - Normal", "Mouth - Ooh", "Mouth - Sss" }, mouths );
	}

	[TestMethod]
	public void TheClipsVertexAndTextureCoordinateTracksFitTheAdvisorMeshes()
	{
		using var assets = new OriginalAssets();
		var global = new ModelFile( $"{Advisor.ArchivePath}/Advisor.MD2" );
		Assert.AreEqual( 0u, global.HeaderFlags & ModelFile.RelativeAnimationFlag, "set mode: the vertex keys replace the positions" );
		var clip = new ModelFile( $"{Advisor.ArchivePath}/Advisorm3.MD2" ).Clip!;
		var tracks = AdvisorRig.FindMeshTracks( global, clip );
		var animated = Enumerable.Range( 0, tracks.Length ).Where( node => tracks[node]?.Vertices != null ).Select( node => global.Nodes[node].Name ).ToArray();
		CollectionAssert.AreEquivalent( new[] { "Right Antennae", "Left Antennae", "Right hand", "Left Hand" }, animated );
		var hand = global.Meshes.Single( mesh => mesh.Name == "Right hand" );
		var positions = new System.Numerics.Vector3[hand.Positions.Length];
		var moved = 0;
		for ( var tick = 0; tick < clip.Duration; tick += Math.Max( 1, clip.Duration / 8 ) )
		{
			tracks[hand.NodeIndex]!.Vertices!.ApplyPose( tick, positions );
			moved += positions.Where( ( position, index ) => System.Numerics.Vector3.Distance( position, hand.Positions[index].GetSystemVector3() ) > 0.01f ).Any() ? 1 : 0;
		}
		Assert.IsTrue( moved > 0, "the hand's vertex track moves it away from the stored mesh" );

		var fantasy = new ModelFile( "/levels/fantasy/advisor/Advisor_FAN.MD2" );
		var eyes = AdvisorRig.FindMeshTracks( fantasy, new ModelFile( "/levels/fantasy/advisor/Advisor_FANm16.MD2" ).Clip! );
		CollectionAssert.AreEquivalent( new[] { "Right Eye", "Left Eye" },
			Enumerable.Range( 0, eyes.Length ).Where( node => eyes[node]?.TexCoords != null ).Select( node => fantasy.Nodes[node].Name ).ToArray(),
			"the level advisors' texture-coordinate tracks" );
	}

	/// <summary>
	/// A clip start hides only its own node list (every other node is shown: the previous clip's end cleared them all, 0x472310 with the
	/// advisor's model flag 8), so the ticket advice shows the held ticket in clips 17 and 18, the clip's toggles do the rest, and the
	/// close-ups hide the body and the bug head.
	/// </summary>
	[TestMethod]
	public void EachClipStartsFromAnAllShownModelAndHidesOnlyItsOwnNodeList()
	{
		using var assets = new OriginalAssets();
		const string archive = "/levels/fantasy/advisor";
		var model = new ModelFile( $"{archive}/Advisor_FAN.MD2" );
		var map = AdvisorNodeMap.From( model );
		var durations = AdvisorClipDurations.LoadArchive( FileSystem, archive )!;
		var clips = new Dictionary<int, ModelAnimation?>();
		ModelAnimation? ClipOf( int number ) => clips.TryGetValue( number, out var clip ) ? clip
			: clips[number] = new ModelFile( $"{archive}/{Advisor.AnimationClipName( "_FAN", number )}" ).Clip;
		var sequence = AdvisorSequences.Build( 29, 1500, durations, () => 0 );
		CollectionAssert.AreEqual( new[] { 17, 17, 17, 17, 18, 19, 20 }, sequence.Clips.ToArray() );
		int Node( string name ) => model.Meshes.Single( mesh => mesh.Name == name ).NodeIndex;
		HashSet<int> At( int index ) => AdvisorAccessories.SequenceHiddenNodes( map, sequence, ClipOf, AdvisorCostume.None, AdvisorThemeCostume.None, index );

		var holding = At( 0 );
		Assert.IsFalse( holding.Contains( Node( "Ticket" ) ), "clip 17's list leaves the ticket the advisor holds" );
		Assert.IsTrue( holding.Contains( Node( "Ticket2" ) ) && holding.Contains( Node( "Fantasy Helm" ) ), "and hides the second ticket and the helmet" );
		Assert.IsFalse( holding.Contains( Node( "Body" ) ) || holding.Contains( Node( AdvisorAccessories.BugHead ) ), "the advisor itself shows in clip 17" );

		var ticketClip = At( 4 );
		Assert.IsFalse( ticketClip.Contains( Node( "Ticket2" ) ), "clip 18's list does not name the second ticket" );
		AdvisorAccessories.ApplyToggles( map, ticketClip, ClipOf( 18 ), 0 );
		Assert.IsTrue( ticketClip.Contains( Node( "Ticket2" ) ), "its toggle at tick 0 hides the second ticket" );
		AdvisorAccessories.ApplyToggles( map, ticketClip, ClipOf( 18 ), 40 );
		Assert.IsFalse( ticketClip.Contains( Node( "Ticket2" ) ), "and shows it from tick 38" );
		Assert.IsTrue( ticketClip.Contains( Node( "Ticket" ) ), "while the held ticket goes at tick 38" );

		var closeUp = At( 5 );
		Assert.IsFalse( closeUp.Contains( Node( "Ticket2" ) ), "clip 19's list leaves the second ticket" );
		foreach ( var name in new[] { "Body", "Right Eye", "Right hand", AdvisorAccessories.BugHead } )
			Assert.IsTrue( closeUp.Contains( Node( name ) ), $"the close-up hides {name}" );
		AdvisorAccessories.ApplyToggles( map, closeUp, ClipOf( 19 ), 40 );
		Assert.IsTrue( closeUp.Contains( Node( "Ticket2" ) ), "clip 19's toggle hides it at tick 39" );

		var last = At( 6 );
		Assert.IsFalse( last.Contains( Node( "Ticket2" ) ), "clip 20 starts with it shown again: nothing of clip 19 survives its end" );
		AdvisorAccessories.ApplyToggles( map, last, ClipOf( 20 ), 61 );
		Assert.IsTrue( last.Contains( Node( "Ticket2" ) ), "and its own toggle hides it at tick 60" );
	}

	/// <summary>The clip lengths of the original's advisor clips come from trailer word 2 (0x474070): the global advisor's and a level advisor's.</summary>
	[TestMethod]
	public void OriginalAdvisorClipLengthsComeFromTheClipTrailers()
	{
		using var assets = new OriginalAssets();
		var global = AdvisorClipDurations.Load( FileSystem, null )!;
		Assert.IsNotNull( global );
		Assert.AreEqual( AdvisorClipDurations.FromTrailer( (uint)new ModelFile( $"{Advisor.ArchivePath}/Advisorm13.MD2" ).Clip!.Duration ), global.Of( 13 ) );
		Assert.AreEqual( 0u, global.Of( AdvisorClipDurations.StartClip ), "the entry of clip 14 is forced to 0" );
		Assert.IsTrue( global.Of( AdvisorClipDurations.EndClip ) > 0 );
		var jungle = AdvisorClipDurations.Load( FileSystem, "jungle" );
		Assert.IsNotNull( jungle, "the level advisor's clips are used when loaded" );
		var sequence = AdvisorSequences.Generate( 5000 + AdvisorSequences.BudgetSlackMilliseconds, jungle!, new AdvisorRandom().Next );
		Assert.AreEqual( 14, sequence.Clips[0] );
		Assert.AreEqual( 15, sequence.Clips[^1] );
		Assert.IsTrue( AdvisorSequences.ResponseSpan( sequence, jungle! ) >= 1000 + 2 * jungle!.Of( 15 ) );
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
		// 93,312 decoded samples; the entry's playback length (SdtEntryHeader.PlaybackBytes) keeps 92,320 of them.
		Assert.AreEqual( 92320, audio.Samples.Length );
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

	[TestMethod]
	public void TheAdvisorMaterialCullsTheFacesTheOriginalsFaceTestDrops()
	{
		// tp.exe 0x581320 (in the primitive builder 0x580530, the only path from the mesh submit 0x581cd0) keeps a triangle only when it runs clockwise
		// on the screen; the material culls the back faces with the front faces clockwise, as for the park's objects
		Assert.IsFalse( AdvisorRig.MeshMaterialFlags.HasFlag( MaterialFlags.DoubleSided ) );
	}

	/// <summary>
	/// The face test against the model's own normals: in the advisor's screen space (<see cref="Advisor.ModelTransform"/>, x right, y up, depth with the
	/// viewer on the near side), the triangles that run clockwise on the screen are the ones whose stored normals face the viewer, so culling the others
	/// drops the advisor's hidden sides and not the ones in view.
	/// </summary>
	[TestMethod]
	public void TheOriginalsFaceTestKeepsTheAdvisorsFacesTowardsTheViewer()
	{
		using var assets = new OriginalAssets();
		var transform = Advisor.ModelTransform( new Point2( 640, 480 ) );
		int kept = 0, dropped = 0, agree = 0, total = 0;
		var inverted = new List<string>();
		foreach ( var archive in new[] { $"{Advisor.ArchivePath}/Advisor.MD2", "/levels/fantasy/advisor/Advisor_FAN.MD2", "/levels/hallow/advisor/Advisor_HAL.MD2", "/levels/jungle/advisor/Advisor_JUN.MD2", "/levels/space/advisor/Advisor_SPA.MD2" } )
		{
			var model = new ModelFile( archive );
			var rest = ModelAnimationPlayer.ComputeRestTransforms( model );
			foreach ( var mesh in model.Meshes )
			{
				if ( mesh.Normals.Length != mesh.Vertices.Length || mesh.NodeIndex < 0 || mesh.NodeIndex >= rest.Length )
					continue;
				var world = rest[mesh.NodeIndex] * transform;
				int meshAgree = 0, meshTotal = 0;
				for ( var index = 0; index + 2 < mesh.Indices.Length; index += 3 )
				{
					var corners = new[] { mesh.Indices[index], mesh.Indices[index + 1], mesh.Indices[index + 2] };
					var screen = corners.Select( corner => System.Numerics.Vector3.Transform( mesh.Vertices[corner].Position.GetSystemVector3(), world ) ).ToArray();
					// clip space has y up; on the screen y runs down
					var determinant = (screen[1].X - screen[0].X) * -(screen[2].Y - screen[0].Y) - (screen[2].X - screen[0].X) * -(screen[1].Y - screen[0].Y);
					if ( Math.Abs( determinant ) < 1e-9f )
						continue;
					var clockwise = determinant > 0;
					var normal = corners.Aggregate( System.Numerics.Vector3.Zero, ( sum, corner ) => sum + System.Numerics.Vector3.TransformNormal( mesh.Normals[corner].GetSystemVector3(), world ) );
					// the depth grows away from the viewer, so a normal towards the viewer has a negative depth part
					var towardsViewer = normal.Z < 0;
					total++;
					if ( clockwise )
						kept++;
					else
						dropped++;
					meshTotal++;
					if ( clockwise == towardsViewer )
					{
						agree++;
						meshAgree++;
					}
				}
				Console.WriteLine( $"  {archive} {mesh.Name}: {meshAgree}/{meshTotal}" );
				// a mesh whose kept faces mostly face away from the viewer would be drawn inside out
				if ( meshTotal >= 8 && meshAgree * 2 < meshTotal )
					inverted.Add( $"{archive} {mesh.Name} ({meshAgree}/{meshTotal})" );
			}
		}
		Console.WriteLine( $"{total} advisor triangles at rest: {kept} kept, {dropped} dropped by the face test; {agree} agree with their stored normals" );
		Assert.IsTrue( kept > 0 && dropped > 0 );
		CollectionAssert.AreEqual( Array.Empty<string>(), inverted.ToArray(), "no mesh is drawn inside out" );
		Assert.IsTrue( agree >= total * 0.85, $"{agree} of {total} agree (stored normals are smoothed across edges, so faces near the outline can disagree)" );
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
