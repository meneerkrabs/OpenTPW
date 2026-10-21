using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The sound system across park visits, with the original data (inconclusive without OPENTPW_GAME_PATH) and a mixer without an output
/// device: every visit (the lobby, a Jungle park with its music, placed ambients, crowd, a scream and a few effects, then the lobby again)
/// must leave the same holdings behind (docs/BROWSER-PERFORMANCE.md, "The AudioMixer root across visits").
/// </summary>
[TestClass]
[DoNotParallelize]
public class AudioParkVisitTests
{
	private readonly Dictionary<FieldInfo, object?> saved = new();
	private BaseFileSystem previousFileSystem = null!;
	private AudioMixer? previousMixer;
	private bool previousSuppressed;
	private Vector3 previousListener;

	[TestInitialize]
	public void Setup()
	{
		Log ??= new();
		previousFileSystem = FileSystem;
		previousMixer = AudioMixer.Current;
		previousSuppressed = SdlMovieAudioOutput.Suppressed;
		previousListener = AudioListener.Position;
		foreach ( var field in typeof( GameAudio ).GetFields( BindingFlags.NonPublic | BindingFlags.Static ).Where( field => !field.IsLiteral && !field.IsInitOnly ) )
			saved.Add( field, field.GetValue( null ) );
		foreach ( var property in typeof( GameAudio ).GetProperties( BindingFlags.Public | BindingFlags.Static ).Where( property => property.CanWrite && property.SetMethod!.IsPrivate ) )
			if ( typeof( GameAudio ).GetField( $"<{property.Name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static ) is { } backing && !saved.ContainsKey( backing ) )
				saved.Add( backing, backing.GetValue( null ) );
	}

	[TestCleanup]
	public void Cleanup()
	{
		GameAudio.Shutdown();
		foreach ( var (field, value) in saved )
			field.SetValue( null, value );
		AudioMixer.Current = previousMixer;
		SdlMovieAudioOutput.Suppressed = previousSuppressed;
		AudioListener.Update( previousListener, new Vector3( 0, 0, 1 ) );
		FileSystem = previousFileSystem;
	}

	[TestMethod]
	public void EveryParkVisitLeavesTheSoundSystemWhereTheFirstLeftIt()
	{
		var data = StringTableTests.OriginalDataDirectory();
		FileSystem = new BaseFileSystem( data );
		// no output device: the mixer runs on its own clock, and this test drives it by Mix
		SdlMovieAudioOutput.Suppressed = true;
		typeof( GameAudio ).GetField( "events", BindingFlags.NonPublic | BindingFlags.Static )!.SetValue( null, null );
		GameAudio.Enabled = true;
		Assert.IsTrue( GameAudio.EnsureStarted() );
		Assert.IsFalse( AudioMixer.Current!.HasDevice );
		var mixer = AudioMixer.Current;
		var scape = new PlacedObjectsFile( FileSystem.ReadAllBytes( "/levels/jungle/scape.omp" ) );
		var ambient = scape.Records.First( record => record.Type == PlacedObjectsFile.RangedSoundType ).SoundPlace;
		var inPark = new Vector3( ambient.Item1, ambient.Item2, ambient.Item3 );

		var after = new List<SoundSystemCounts>();
		for ( var visit = 0; visit < 5; visit++ )
		{
			GameAudio.EnterLobby( "jungle" );
			Run( mixer, 40 );
			GameAudio.EnterPark( "jungle", seed: 11 );
			// the listener among the land's own sounds, so the near ones play, then far away, so the looping ones release their outputs
			AudioListener.Update( inPark, new Vector3( 0, 0, 1 ) );
			GameAudio.SetCrowd( 40 );
			GameAudio.StartScream( 2, 60, inPark );
			GameAudio.PlayUi( 0x1F );
			Run( mixer, 60, guests: 80 );
			var inside = GameAudio.Counts!.Value;
			// the park's music, crowd and placed sentence run, with its placed ambients and the scream's chain tracked
			Assert.IsTrue( inside.Sentences >= 3 && inside.Chains + inside.OneShots + inside.LoopSamples > 0 && inside.Voices > 0, $"visit {visit + 1} in the park: {inside}" );
			AudioListener.Update( inPark + new Vector3( 100000, 0, 100000 ), new Vector3( 0, 0, 1 ) );
			Run( mixer, 20, guests: 80 );
			GameAudio.LeavePark();
			GameAudio.EnterLobby( "jungle" );
			Run( mixer, 40 );
			after.Add( Settle( mixer ) );
			Console.WriteLine( $"visit {visit + 1}: {after[^1]}" );
		}
		for ( var visit = 1; visit < after.Count; visit++ )
		{
			var (first, now) = (after[0], after[visit]);
			Assert.AreEqual( 0, now.DecodesInFlight, $"visit {visit + 1}: settled" );
			// EnterLobby takes no seed: the lobby's sentences draw their segments from the shared chooser, so the voices of a sentence between two
			// segments (the ending one and its successor) and the PCM held may differ from the first visit, but by the segment playing, not by visit
			Assert.AreEqual( first with { Voices = 0, VoiceSampleBytes = 0, DecodedBytes = 0, DecodedSamples = 0 }, now with { Voices = 0, VoiceSampleBytes = 0, DecodedBytes = 0, DecodedSamples = 0 },
				$"visit {visit + 1} holds what the first held" );
			Assert.IsTrue( now.Voices <= 2 * now.Sentences, $"visit {visit + 1}: at most an ending and a starting voice per running sentence: {now}" );
			Assert.IsTrue( now.VoiceSampleBytes + now.DecodedBytes <= Math.Max( first.VoiceSampleBytes + first.DecodedBytes, 1 ) * 3,
				$"visit {visit + 1}: {now.VoiceSampleBytes + now.DecodedBytes} PCM bytes against {first.VoiceSampleBytes + first.DecodedBytes} after the first" );
		}
		Assert.IsTrue( after[^1].Sentences <= 2, $"only the lobby's music and ambience run: {after[^1]}" );
		Assert.AreEqual( (0, 0, 0, 0, 0), (after[^1].Chains, after[^1].Placements, after[^1].OneShots, after[^1].LoopSamples, after[^1].HeldSounds),
			$"nothing of the park is still tracked: {after[^1]}" );
	}

	/// <summary><paramref name="frames"/> game frames of a tenth of a second each: the per-frame update, then that much output mixed.</summary>
	private static void Run( AudioMixer mixer, int frames, int? guests = null )
	{
		for ( var frame = 0; frame < frames; frame++ )
		{
			GameAudio.Update( guests );
			mixer.Mix( AudioMixer.OutputRate / 10 );
			Thread.Sleep( 1 );
		}
	}

	/// <summary>
	/// Runs frames until no sample decode is in flight (the decodes run on worker threads here) on three checks in a row, and returns the counts of
	/// the last check, so no frame runs between the check and the counts read.
	/// </summary>
	private static SoundSystemCounts Settle( AudioMixer mixer )
	{
		var clock = Stopwatch.StartNew();
		var quiet = 0;
		while ( clock.Elapsed < TimeSpan.FromSeconds( 60 ) )
		{
			Run( mixer, 1 );
			var counts = GameAudio.Counts!.Value;
			quiet = counts.DecodesInFlight == 0 ? quiet + 1 : 0;
			if ( quiet >= 3 )
				return counts;
		}
		Assert.Fail( $"a sample decode was still in flight after 60 s: {GameAudio.Counts}" );
		return default;
	}
}
