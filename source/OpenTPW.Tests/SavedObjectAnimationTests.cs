using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The animation time controls and node states of an original save's scene-mesh records (tp.exe loader 0x4647A0): a synthetic record proves the decoding
/// of the eleven saved words and the channel restore of <see cref="ObjectAnimator"/>; the Jungle Easymode save proves that the imported gate stands open
/// (animation 5, sub-animation 1, ended on frame 50 of 50) where it used to show its closed rest pose, with the Bus ended and the Traffic Lights looping.
/// </summary>
[TestClass]
public class SavedObjectAnimationTests
{
	private const uint One = 0x3F800000; // 1.0f

	private static ModelFile Geometry() => new( new MemoryStream( Md2ModelFileTests.CreateGeometry() ) );
	private static ModelAnimation Vertex( ushort duration ) => new ModelFile( new MemoryStream( Md2VertexAnimationTests.CreateAnimation( 0, duration ) ) ).Clip!;

	/// <summary>A synthetic RideSystem record with four controls: ended (the Jungle gate's), looping, deferring, and AnimID 12.</summary>
	private static SaveRideSystemRecord SyntheticRecord()
	{
		var world = SaveTestPayloads.World( SaveTestPayloads.ParkVariables(), SaveTestPayloads.Grid( OriginalParkImportTests.CreateMap( 3, 3, ( _, _ ) => 0 ) ),
			SaveTestPayloads.Things( SaveTestPayloads.ParkThings() ) );
		var payload = SaveTestPayloads.Payload( world, SaveTestPayloads.RideSystem( new[]
		{
			SaveTestPayloads.RideSystemRecord( 1601, 0, 0, 1, 1, SaveObject.FixedItemKind, 1, nodeStates: new[] { 0x1u, 0x401u, 0x0u },
				controls: new[]
				{
					// flags, AnimID, SubAnim, StartAnimTime, AnimTime, NoPauseAnimTime, speed, deferred AnimID, SubAnim, flags, speed
					SaveTestPayloads.RideSystemControl( 0x4, 5, 1, 1_000, 2_666, 3_000, One, 12, 0, 0, One ),
					SaveTestPayloads.RideSystemControl( 0x1, 5, 0, 2_780, 3_000, 3_000, 0x3F8CCCCD, 12, 0, 0x1, 0x3F8CCCCD ),
					SaveTestPayloads.RideSystemControl( 0x0, 2, 0, 2_900, 3_000, 3_000, One, 4, 1, 0x1, 0x40000000 ),
					SaveTestPayloads.RideSystemControl( 0x4, 12, 0, 0, 0, 3_000, One, 12, 0, 0, One ),
				} ),
		} ) );
		return SaveRideSystemBlock.Parse( payload, SavePayloadLayout.Parse( payload ) ).Records.Single();
	}

	[TestMethod]
	public void SavedControlsDecodeInTheLoadersWordOrder()
	{
		var record = SyntheticRecord();
		Assert.AreEqual( 4, record.Controls.Length );
		var gate = SavedChannelState.Decode( record.Controls[0], 3_000, out var gap );
		Assert.IsNull( gap );
		Assert.AreEqual( new SavedChannelState( 5, 1, Loop: false, Ended: true, 1f, 2_000, ObjectAnimator.NoAnimation, 0, false, 0 ), gate,
			"flag 0x4 ended; the elapsed time is the saved game clock minus StartAnimTime" );
		var lights = SavedChannelState.Decode( record.Controls[1], 3_000, out gap );
		Assert.IsNull( gap );
		Assert.AreEqual( (5, 0, true, false, 1.1f, 220L, false), (lights!.Value.AnimationId, lights.Value.Variant, lights.Value.Loop, lights.Value.Ended,
			lights.Value.Speed, lights.Value.ElapsedMilliseconds, lights.Value.HasDeferred), "flag 0x1 loops; the speed is the seventh saved word" );
		var deferring = SavedChannelState.Decode( record.Controls[2], 3_000, out gap );
		Assert.IsNull( gap );
		Assert.AreEqual( (4, 1, true, 2f), (deferring!.Value.DeferredAnimationId, deferring.Value.DeferredVariant, deferring.Value.DeferredLoop, deferring.Value.DeferredSpeed) );
		Assert.IsNull( SavedChannelState.Decode( record.Controls[3], 3_000, out gap ), "AnimID 12 binds nothing" );
		Assert.IsNull( gap );

		// Without a Clock block the control's own AnimTime is the clock; the difference is the loader's unsigned one.
		Assert.AreEqual( 1_666L, SavedChannelState.Decode( record.Controls[0], null, out _ )!.Value.ElapsedMilliseconds );
		var wrapped = new SaveRideSystemControl( 0x1, 5, new uint[] { 0x1, 5, 0, uint.MaxValue - 9, 0, 0, One, 12, 0, 0, One } );
		Assert.AreEqual( 20L, SavedChannelState.Decode( wrapped, 10, out _ )!.Value.ElapsedMilliseconds );

		// States the object animator does not model are reported, not guessed.
		foreach ( var flags in new[] { SavedChannelState.FirstFrameFlag, SavedChannelState.FirstFrameFlag | SavedChannelState.EndedFlag, SavedChannelState.UnscaledClockFlag } )
		{
			var control = new SaveRideSystemControl( flags, 5, new uint[] { flags, 5, 0, 0, 0, 0, One, 12, 0, 0, One } );
			Assert.IsNull( SavedChannelState.Decode( control, 10, out gap ) );
			Assert.IsNotNull( gap, $"flags 0x{flags:X}" );
		}
	}

	[TestMethod]
	public void RestoredChannelsContinueTheSavedFrame()
	{
		var model = Geometry();
		var clip = Vertex( 50 );
		var animator = new ObjectAnimator( model );

		// The gate's control: ended on its last frame, GETANIM -1, frozen however long the park runs.
		animator.RestoreSaved( 0, clip, "gatesm2", 5, 1, loop: false, speed: 1f, elapsedMilliseconds: 2_000, ended: true );
		Assert.IsFalse( animator.IsChannelPlaying( 0 ) );
		Assert.AreEqual( -1, animator.GetAnimation( 0 ) );
		Assert.AreEqual( 50f, animator.GetTick( 0 ) );
		var last = new System.Numerics.Vector3[4];
		clip.Tracks[0].VertexAnimation!.ApplyPose( 50, last );
		CollectionAssert.AreEqual( last, animator.GetVertexPositions( 0 )!.ToArray(), "the restored pose is the clip's last frame" );
		animator.Advance( 10 );
		Assert.AreEqual( 50f, animator.GetTick( 0 ) );
		var channel = animator.CaptureSnapshot().Channels.Single();
		Assert.AreEqual( (5, 1, "gatesm2", true), (channel.AnimationId, channel.Variant, channel.ClipName, channel.Ended) );

		// A running control continues from its saved frame: 30 × elapsed / 1000 × speed.
		var running = new ObjectAnimator( model );
		running.RestoreSaved( 0, clip, "m1", 5, 0, loop: true, speed: 1f, elapsedMilliseconds: 1_000, ended: false );
		Assert.AreEqual( 30f, running.GetTick( 0 ) );
		Assert.AreEqual( 5, running.GetAnimation( 0 ) );
		running.Advance( 0.5 );
		Assert.AreEqual( 45f, running.GetTick( 0 ), 1e-4f );
		running.Advance( 0.6 );
		Assert.IsTrue( running.IsChannelPlaying( 0 ), "the looping clip replays past its end" );
		Assert.IsTrue( running.GetTick( 0 ) < 50f );

		// A saved deferred clip follows the running one at its end.
		var deferring = new ObjectAnimator( model );
		var next = Vertex( 20 );
		deferring.RestoreSaved( 1, clip, "m1", 2, 0, loop: false, speed: 1f, elapsedMilliseconds: 1_600, ended: false, deferred: (next, "m2", 4, 1, true, 1f) );
		Assert.AreEqual( 4, deferring.GetDeferredAnimationId( 1 ) );
		deferring.Advance( 0.1 );
		Assert.AreEqual( "m2", deferring.GetClip( 1 ) );
		Assert.AreEqual( ObjectAnimator.NoAnimation, deferring.GetDeferredAnimationId( 1 ) );
	}

	[TestMethod]
	public void SavedNodeStatesRestoreTheToggledAndPinnedBits()
	{
		var model = Geometry();
		var animator = new ObjectAnimator( model );
		var toggled = Enumerable.Range( 0, model.Nodes.Count ).Select( node => node == 0 ).ToArray();
		var pinned = Enumerable.Range( 0, model.Nodes.Count ).Select( node => node == model.Nodes.Count - 1 ).ToArray();
		animator.RestoreSavedNodeStates( toggled, pinned );
		CollectionAssert.AreEqual( toggled, Enumerable.Range( 0, model.Nodes.Count ).Select( animator.GetNodeStateBit ).ToArray() );
		Assert.ThrowsException<ArgumentException>( () => animator.RestoreSavedNodeStates( new bool[model.Nodes.Count + 1], new bool[model.Nodes.Count + 1] ) );
	}

	[TestMethod]
	public void ImportedJungleGateStandsOpenAtLoad()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original Jungle Easymode save." );
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
			var catalog = ObjectCatalog.Load( "jungle" );
			var clock = save.RuntimeBlocks!.ClockWords[0];
			// The objects as ParkObjects.ImportOriginal restores them: each fixed item's runtime takes its RideSystem record on the saved game clock.
			OriginalObjectRuntime Restore( string name )
			{
				var entry = catalog.Entries.Single( candidate => candidate.SettingsName == name );
				var item = save.FixedItems.Single( fixedItem => fixedItem.InfoId == entry.InfoId );
				var runtime = new OriginalObjectRuntime( entry );
				var gaps = runtime.RestoreSavedScene( save.RideSystem!.Records.Single( scene => scene.Offset == item.Offset ), clock );
				Assert.AreEqual( 0, gaps.Count, $"{name}: {string.Join( "; ", gaps )}" );
				return runtime;
			}
			static AnimatorChannelSnapshot Channel( OriginalObjectRuntime runtime ) => runtime.Animator.CaptureSnapshot().Channels.Single( state => state.ChannelIndex == 0 );

			// The gate saved the open command's clip (5/1) ended on its last frame: it stands open, not in its closed rest pose.
			var gates = Restore( "Gates" );
			var gate = Channel( gates );
			Assert.AreEqual( (5, 1, true, 50f), (gate.AnimationId, gate.Variant, gate.Ended, gate.Tick) );
			Assert.AreEqual( gates.Entry.ResolveAnimation( 5, 1 )!.Name, gate.ClipName );
			Assert.AreEqual( -1, gates.Animator.GetAnimation( 0 ) );
			// The Bus saved its leaving clip (5/2) ended; the Traffic Lights loop 5/0, 220 ms (6.6 frames) past their start on the saved clock.
			var busRuntime = Restore( "Bus" );
			var bus = Channel( busRuntime );
			Assert.AreEqual( (5, 2, true, 220f), (bus.AnimationId, bus.Variant, bus.Ended, bus.Tick) );
			var lights = Channel( Restore( "Lights" ) );
			Assert.AreEqual( (5, 0, true, false), (lights.AnimationId, lights.Variant, lights.Loop, lights.Ended) );
			Assert.AreEqual( 6.6f, lights.Tick, 1e-4f );
			// The Bus's saved node states hide its three meshes (it has left).
			Assert.IsTrue( Enumerable.Range( 0, 3 ).All( busRuntime.Animator.IsNodeHidden ) );
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

	/// <summary>
	/// Every object record ParkObjects.ImportOriginal restores, in the shipped Jungle Easymode save and the OPENTPW_TPWS_SAVES corpus when it is set: the node states and every
	/// control below AnimID 12 restore without a gap, one channel per control, with the saved clip, sub-animation, loop and ended state.
	/// </summary>
	[TestMethod]
	public void EverySavedObjectControlRestores()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original saves." );
		var themes = new[] { "jungle", "hallow", "fantasy", "space" };
		var saves = new[] { (Theme: "jungle", Path: (string?)null) }.ToList();
		var folder = Environment.GetEnvironmentVariable( "OPENTPW_TPWS_SAVES" );
		if ( !string.IsNullOrWhiteSpace( folder ) && Directory.Exists( folder ) )
			saves.AddRange( Directory.EnumerateFiles( folder!, "*", SearchOption.AllDirectories )
				.Where( file => Path.GetExtension( file ).ToUpperInvariant() is ".TPWS" or ".INTS" )
				.Select( file => (Theme: Path.GetFileName( Path.GetDirectoryName( file ) )!.ToLowerInvariant(), Path: (string?)file) )
				.Where( save => themes.Contains( save.Theme ) ).OrderBy( save => save.Path, StringComparer.Ordinal ) );
		var previousFiles = FileSystem;
		var previousEntities = Entity.All;
		var previousLog = Log;
		try
		{
			Log = new();
			FileSystem = new BaseFileSystem( OriginalParkImportTests.OriginalDataPath() );
			FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			var restored = 0;
			foreach ( var (theme, path) in saves )
			{
				Entity.All = new();
				OriginalPark park;
				if ( path == null )
					park = OriginalPark.Load( theme );
				else
				{
					using var reader = new SaveReader( path );
					park = OriginalPark.Load( theme, readShippedSave: false, savePayload: reader.ReadFile() );
				}
				if ( park.Save is not { } save )
					continue; // only Jungle ships an Easymode save
				var catalog = ObjectCatalog.Load( theme );
				var name = path == null ? $"{theme}/Easymode" : $"{theme}/{Path.GetFileName( path )}";
				foreach ( var record in save.PlacedObjects.Select( item => item.Record ).Concat( save.UnresolvedObjects ).Concat( save.FixedItems ) )
				{
					if ( catalog.Find( record.InfoId ) is not { IsTool: false } entry || save.RideSystem?.Records.FirstOrDefault( scene => scene.Offset == record.Offset ) is not { } scene )
						continue;
					var runtime = new OriginalObjectRuntime( entry );
					var gaps = runtime.RestoreSavedScene( scene, save.RuntimeBlocks?.ClockWords[0] );
					Assert.AreEqual( 0, gaps.Count, $"{name} {entry.SettingsName}: {string.Join( "; ", gaps )}" );
					var channels = runtime.Animator.CaptureSnapshot().Channels.ToDictionary( channel => channel.ChannelIndex );
					for ( var index = 0; index < scene.Controls.Length; index++ )
					{
						var words = scene.Controls[index].Words;
						if ( words[1] >= ObjectAnimator.NoAnimation )
						{
							Assert.IsFalse( channels.ContainsKey( index ), $"{name} {entry.SettingsName} channel {index}" );
							continue;
						}
						var channel = channels[index];
						Assert.AreEqual( ((int)words[1], (int)words[2], (words[0] & 1) != 0, (words[0] & 4) != 0), (channel.AnimationId, channel.Variant, channel.Loop, channel.Ended),
							$"{name} {entry.SettingsName} channel {index}" );
						restored++;
					}
				}
				foreach ( var entity in Entity.All.ToArray() )
					entity.Delete();
			}
			// the shipped Jungle Easymode save alone holds 15 controls below AnimID 12; the private corpus adds more
			Assert.IsTrue( restored >= (string.IsNullOrEmpty( folder ) ? 15 : 18), $"{restored} controls restored" );
		}
		finally
		{
			Entity.All = previousEntities;
			FileSystem = previousFiles;
			Log = previousLog;
		}
	}
}
