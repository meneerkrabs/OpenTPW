using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// The sound side of DIPMUSIC and SETREVERB (tp.exe 0x51BE5A music duck, 0x55A470 reverb reader, 0x51C8D0 preset, SndReverb.map loader 0x6C5D20).
/// Presentation only: none of it touches the world generator or the state digests.
/// </summary>
[TestClass]
[DoNotParallelize]
public class RseFollowupAudioTests
{
	private static Mp2Audio Constant( short value, int frames ) => new( AudioMixer.OutputRate, 1, Enumerable.Repeat( value, frames ).ToArray(), 1, 0 );

	[TestMethod]
	public void ANonZeroDuckRequestSilencesTheMusicChannelAndNothingElse()
	{
		var mixer = new AudioMixer( null );
		mixer.Play( Constant( 1000, 100 ), AudioChannel.Music );
		mixer.Play( Constant( 200, 100 ), AudioChannel.Effects );
		mixer.Play( Constant( 30, 100 ), AudioChannel.Speech );
		Assert.AreEqual( (short)1230, mixer.Mix( 10 )[0] );
		mixer.MusicDuckRequest = 1;
		var ducked = mixer.Mix( 10 );
		Assert.AreEqual( (short)230, ducked[0], "the music channel is 0, the effects and the speech keep their volume" );
		mixer.MusicDuckRequest = 0;
		Assert.AreEqual( (short)1230, mixer.Mix( 10 )[0], "the request 0 gives the music its option volume back; the voice kept playing while silent" );
	}

	[TestMethod]
	public void TheDuckIsTheRequestWordNotAFlag()
	{
		var mixer = new AudioMixer( null );
		mixer.Play( Constant( 500, 100 ), AudioChannel.Music );
		mixer.MusicDuckRequest = 0x100;
		Assert.AreEqual( (short)0, mixer.Mix( 4 )[0], "any non-zero word (tp.exe compares the 16-bit 0x803AD0 with 0)" );
	}

	[TestMethod]
	public void TheGameAudioRequestReachesTheCurrentMixerAndEndsWithThePark()
	{
		var previous = AudioMixer.Current;
		var mixer = new AudioMixer( null );
		AudioMixer.Current = mixer;
		try
		{
			GameAudio.SetMusicDuck( 1 );
			Assert.AreEqual( 1, mixer.MusicDuckRequest );
			Assert.AreEqual( 1, GameAudio.MusicDuckRequest );
			GameAudio.LeavePark();
			Assert.AreEqual( 0, mixer.MusicDuckRequest, "the park's scripts are destroyed with it, each writing 0" );
			Assert.AreEqual( 0, GameAudio.MusicDuckRequest );
		}
		finally
		{
			GameAudio.SetMusicDuck( 0 );
			AudioMixer.Current = previous;
		}
	}

	[TestMethod]
	public void ADipMusicScriptDucksTheMixerThroughTheObjectEffectsAndItsDestructionReleasesIt()
	{
		var previous = AudioMixer.Current;
		var mixer = new AudioMixer( null );
		AudioMixer.Current = mixer;
		try
		{
			var effects = new DuckingEffects();
			var vm = new RideVM( new RideScriptFile( new MemoryStream( new Asm().I( Opcode.DIPMUSIC, 1 ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( new[] { "V0" } ) ) ),
				new RideVMOptions { Seed = 1, Effects = effects } );
			vm.Advance( 1d / 60 );
			Assert.AreEqual( 1, mixer.MusicDuckRequest );
			vm.Stop();
			Assert.AreEqual( 0, mixer.MusicDuckRequest );
		}
		finally
		{
			GameAudio.SetMusicDuck( 0 );
			AudioMixer.Current = previous;
		}
	}

	/// <summary>The part of the object effects DIPMUSIC takes: the sound service call (the full effects need a placed object).</summary>
	private sealed class DuckingEffects : IRideScriptEffects
	{
		public int Perform( RideEffectCall call )
		{
			if ( call.Opcode == Opcode.DIPMUSIC )
				GameAudio.SetMusicDuck( unchecked((ushort)call.Argument( 0 )) );
			return 0;
		}
	}

	private static RideVM Script( int level, bool child = false )
	{
		var asm = new Asm().I( Opcode.SETREVERB, level ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" );
		var parent = new RideVM( new RideScriptFile( new MemoryStream( new Asm().I( Opcode.NOP ).Build( new[] { "V0" } ) ) ), new RideVMOptions { Seed = 1 } );
		var vm = new RideVM( new RideScriptFile( new MemoryStream( asm.Build( new[] { "V0" } ) ) ), new RideVMOptions { Seed = 1 } );
		vm.Advance( 1d / 60 );
		if ( !child )
			return vm;
		return new RideVM( new RideScriptFile( new MemoryStream( asm.Build( new[] { "V0" } ) ) ), new RideVMOptions { Seed = 1, World = parent.World }, parent );
	}

	[TestMethod]
	public void TheReverbReaderTakesTheRiddenRidesRootScriptLevelAndOtherwiseTheDefault()
	{
		Assert.AreEqual( 0, ReverbSelector.PresetFor( (RideVM?)null ), "no ridden ride: the default (0x78578C)" );
		Assert.AreEqual( 8, ReverbSelector.PresetFor( Script( 8 ) ) );
		Assert.AreEqual( 0, ReverbSelector.PresetFor( Script( 0 ) ), "a level of 0 counts as not set" );
		Assert.IsNull( ReverbSelector.PresetFor( Script( -3 ) ), "a negative level leaves the held preset (0x55A4B9)" );
		var child = Script( 5, child: true );
		child.Advance( 1d / 60 );
		Assert.AreEqual( 5, child.ReverbLevel );
		Assert.AreEqual( 0, ReverbSelector.PresetFor( child ), "only a script with no parent is looked at (0x55A48B)" );
	}

	[TestMethod]
	public void TheSoundServiceSendsAPresetOnlyWhenItChanges()
	{
		GameAudio.ResetReverb();
		try
		{
			Assert.AreEqual( -1, GameAudio.ReverbPreset );
			ReverbSelector.Update( (OriginalObject?)null );
			Assert.AreEqual( 0, GameAudio.ReverbPreset );
			Assert.AreEqual( 1, GameAudio.ReverbChanges );
			ReverbSelector.Update( (OriginalObject?)null );
			Assert.AreEqual( 1, GameAudio.ReverbChanges, "0x51C8D0 sends nothing when the preset is the held one" );
			GameAudio.SelectReverb( 9 );
			Assert.AreEqual( 9, GameAudio.ReverbPreset );
			Assert.AreEqual( 2, GameAudio.ReverbChanges );
		}
		finally
		{
			GameAudio.ResetReverb();
			GameAudio.ReverbMap = null;
		}
	}

	private static byte[] MapBytes( params (bool Present, uint First)[] entries )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		writer.Write( 2000u );
		writer.Write( (uint)entries.Length );
		foreach ( var (present, first) in entries )
		{
			writer.Write( (byte)(present ? 1 : 0) );
			if ( !present )
				continue;
			writer.Write( first );
			for ( var word = 1; word < SndReverbMap.RecordWords; word++ )
				writer.Write( (uint)word );
		}
		return stream.ToArray();
	}

	[TestMethod]
	public void TheReverbMapReadsPresentAndAbsentEntriesAndRefusesDamage()
	{
		var map = SndReverbMap.Read( MapBytes( (true, 7), (false, 0), (true, 0x40F00000) ) );
		Assert.AreEqual( 3, map.Count );
		Assert.IsTrue( map.Contains( 0 ) );
		Assert.IsFalse( map.Contains( 1 ), "an absent entry cannot be selected" );
		Assert.IsFalse( map.Contains( 3 ) );
		Assert.IsFalse( map.Contains( -1 ) );
		Assert.AreEqual( 13, map.Record( 2 )!.Count );
		Assert.AreEqual( 7.5f, map.Single( 2, 0 ) );
		Assert.AreEqual( 12u, map.Record( 0 )![12] );
		Assert.IsNull( map.Record( 1 ) );

		var bytes = MapBytes( (true, 1) );
		Assert.ThrowsException<InvalidDataException>( () => SndReverbMap.Read( bytes.AsSpan( 0, bytes.Length - 1 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => SndReverbMap.Read( bytes.Concat( new byte[] { 0 } ).ToArray() ) );
		var wrong = (byte[])bytes.Clone();
		wrong[0] = 0xD1;
		Assert.ThrowsException<InvalidDataException>( () => SndReverbMap.Read( wrong ) );
		Assert.ThrowsException<InvalidDataException>( () => SndReverbMap.Read( new byte[4] ) );
	}

	[TestMethod]
	public void APresentFirstWordMinusOneIsPhysicallyPresentButNotSelectable()
	{
		var map = SndReverbMap.Read( MapBytes( (true, 0xFFFFFFFFu), (false, 0u), (true, 7u) ) );

		Assert.IsTrue( map.Contains( 0 ), "the presence byte still makes the sentinel entry physically present" );
		Assert.AreEqual( 0xFFFFFFFFu, BitConverter.SingleToUInt32Bits( map.Single( 0, 0 ) ), "Single keeps raw word access" );
		Assert.IsNull( map.Record( 0 ), "Record returns null for the first-word uint.MaxValue sentinel" );

		Assert.IsFalse( map.Contains( 1 ) );
		Assert.IsNull( map.Record( 1 ), "an absent entry has no record" );

		Assert.IsTrue( map.Contains( 2 ) );
		Assert.AreEqual( 7u, map.Record( 2 )![0] );
		Assert.IsNull( map.Record( 3 ), "an out-of-range level has no record" );
	}

	[TestMethod]
	public void AFirstWordMinusOnePresetDisablesTheHostReverbLikeAnAbsentPreset()
	{
		var before = AudioMixer.Current;
		GameAudio.ResetReverb();
		try
		{
			var mixer = new AudioMixer( null );
			AudioMixer.Current = mixer;
			var map = SndReverbMap.Read( MapBytes( (false, 0u), (true, 1u), (true, 0xFFFFFFFFu), (true, 3u), (false, 0u) ) );
			for ( var word = 1; word < SndReverbMap.RecordWords; word++ )
				Assert.AreEqual( map.Single( 1, word ), map.Single( 2, word ), $"the valid and sentinel presets differ at word {word}" );
			GameAudio.ReverbMap = map;

			GameAudio.SelectReverb( 1 );
			Assert.AreEqual( 1, GameAudio.ReverbPreset );
			Assert.AreEqual( 1, GameAudio.ReverbChanges );
			Assert.AreEqual( 1u, GameAudio.ReverbRecord![0] );
			Assert.IsTrue( mixer.Reverb.Active );

			GameAudio.SelectReverb( 2 );
			Assert.AreEqual( 2, GameAudio.ReverbPreset );
			Assert.AreEqual( 2, GameAudio.ReverbChanges );
			Assert.IsNull( GameAudio.ReverbRecord );
			Assert.IsFalse( mixer.Reverb.Active, "the sentinel disables the current host effect just as an absent preset does" );

			GameAudio.SelectReverb( 2 );
			Assert.AreEqual( 2, GameAudio.ReverbPreset );
			Assert.AreEqual( 2, GameAudio.ReverbChanges, "the same held preset does not send again" );
			Assert.IsNull( GameAudio.ReverbRecord );
			Assert.IsFalse( mixer.Reverb.Active );

			GameAudio.SelectReverb( 3 );
			Assert.AreEqual( 3, GameAudio.ReverbPreset );
			Assert.AreEqual( 3, GameAudio.ReverbChanges );
			Assert.AreEqual( 3u, GameAudio.ReverbRecord![0] );
			Assert.IsTrue( mixer.Reverb.Active );

			GameAudio.SelectReverb( 4 );
			Assert.AreEqual( 4, GameAudio.ReverbPreset );
			Assert.AreEqual( 4, GameAudio.ReverbChanges );
			Assert.IsNull( GameAudio.ReverbRecord );
			Assert.IsFalse( mixer.Reverb.Active, "an absent preset also disables the host effect" );

			GameAudio.SelectReverb( 4 );
			Assert.AreEqual( 4, GameAudio.ReverbPreset );
			Assert.AreEqual( 4, GameAudio.ReverbChanges, "the same held absent preset does not send again" );
			Assert.IsNull( GameAudio.ReverbRecord );
			Assert.IsFalse( mixer.Reverb.Active );
		}
		finally
		{
			GameAudio.ResetReverb();
			GameAudio.ReverbMap = null;
			AudioMixer.Current = before;
		}
	}

	[TestMethod]
	public void TheHeldPresetsRecordComesFromTheMap()
	{
		GameAudio.ResetReverb();
		try
		{
			GameAudio.ReverbMap = SndReverbMap.Read( MapBytes( (true, 1), (false, 0), (true, 3) ) );
			GameAudio.SelectReverb( 2 );
			Assert.AreEqual( 3u, GameAudio.ReverbRecord![0] );
			GameAudio.SelectReverb( 1 );
			Assert.IsNull( GameAudio.ReverbRecord, "the sound service refuses a preset the map lacks" );
		}
		finally
		{
			GameAudio.ResetReverb();
			GameAudio.ReverbMap = null;
		}
	}

	[TestMethod]
	public void TheDefaultPresetStaysDryAndARidePresetIsMixed()
	{
		var before = AudioMixer.Current;
		GameAudio.ResetReverb();
		try
		{
			var mixer = new AudioMixer( null );
			AudioMixer.Current = mixer;
			GameAudio.ReverbMap = SndReverbMap.Read( MapBytes( (true, 1), (true, 1) ) );
			GameAudio.SelectReverb( 0 );
			Assert.IsFalse( mixer.Reverb.Active, "[EXT:audio-reverb] the default preset 0 is not mixed" );
			GameAudio.SelectReverb( 1 );
			Assert.IsTrue( mixer.Reverb.Active );
			GameAudio.SelectReverb( 0 );
			Assert.IsFalse( mixer.Reverb.Active, "leaving the ride switches the reverb off" );
		}
		finally
		{
			GameAudio.ResetReverb();
			GameAudio.ReverbMap = null;
			AudioMixer.Current = before;
		}
	}

	/// <summary>The original file (the game folder's root, outside Data): 32 entries, 15 of them present, 820 bytes (inconclusive without OPENTPW_GAME_PATH).</summary>
	[TestMethod]
	public void TheOriginalReverbMapParses()
	{
		var root = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		var path = string.IsNullOrWhiteSpace( root ) ? null : Path.Combine( root, "SndReverb.map" );
		if ( path == null || !File.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to a game folder with SndReverb.map." );
		var map = SndReverbMap.Read( File.ReadAllBytes( path ) );
		Assert.AreEqual( 32, map.Count );
		Assert.AreEqual( 15, Enumerable.Range( 0, map.Count ).Count( map.Contains ) );
		foreach ( var level in new[] { 0, 1, 3, 8, 9 } )
			Assert.IsTrue( map.Contains( level ), $"the shipped scripts' SETREVERB level {level} has a preset" );
	}
}
