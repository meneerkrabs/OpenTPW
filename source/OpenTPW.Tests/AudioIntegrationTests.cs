using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Original-data checks of live audio callbacks through decoded PCM, without an SDL device.</summary>
[TestClass]
[DoNotParallelize]
public class AudioIntegrationTests
{
	private readonly Dictionary<FieldInfo, object?> saved = new();
	private BaseFileSystem? previousFileSystem;
	private string? previousBonus;
	private AudioMixer? mixer;
	private SoundEventSystem system = null!;

	[TestInitialize]
	public void Setup()
	{
		Log ??= new();
		var data = StringTableTests.OriginalDataDirectory();
		previousFileSystem = FileSystem;
		previousBonus = ObjectCatalog.BonusDataRoot;
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		ObjectCatalog.BonusDataRoot = null;
		mixer = new AudioMixer( null );
		system = new SoundEventSystem( mixer, seed: 7 );
		SetField( "events", system );
		SetField( "rain", null );
		SetField( "staffTurn", -1L );
		SetField( "staffRandom", new DeterministicRandom( 1 ) );
		SetField( "staffVoiceLogged", false );
		foreach ( var (property, category) in new[] { ("Ambient", "cat_ambient"), ("Kids", "cat_kids"), ("Staff", "cat_staff") } )
		{
			var loaded = SoundCategory.TryLoad( FileSystem, "/global/sound", category );
			Assert.IsNotNull( loaded, category );
			SetField( $"<{property}>k__BackingField", loaded );
		}
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
	}

	private void SetField( string name, object? value )
	{
		var field = typeof( GameAudio ).GetField( name, BindingFlags.NonPublic | BindingFlags.Static )!;
		saved.Add( field, field.GetValue( null ) );
		field.SetValue( null, value );
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( mixer != null )
		{
			GameAudio.SetRain( 0 );
			system.StopChains();
			mixer.Dispose();
		}
		foreach ( var (field, value) in saved )
			field.SetValue( null, value );
		if ( previousFileSystem != null )
		{
			FileSystem = previousFileSystem;
			ObjectCatalog.BonusDataRoot = previousBonus;
		}
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
	}

	private bool MixUntilAudible()
	{
		var timeout = Stopwatch.StartNew();
		while ( timeout.Elapsed < TimeSpan.FromSeconds( 30 ) )
		{
			system.Pump();
			if ( mixer!.Mix( 4096 ).Any( sample => sample != 0 ) )
				return true;
			Thread.Sleep( 2 );
		}
		return false;
	}

	[TestMethod]
	public void WeatherRainAndDueThunderReachTheDecodedMix()
	{
		var keys = new SamSettings( new[] { SamDocument.Parse(
			"WeatherEffects.QualityForSnowLow\t-1\nWeatherEffects.QualityForSnowHigh\t-1\n" +
			"WeatherEffects.QualityForRainLow\t0\nWeatherEffects.QualityForRainHigh\t40\n" +
			"WeatherEffects.QualityForLightningLow\t0\nWeatherEffects.QualityForLightningHigh\t15\n" +
			"WeatherEffects.MaxRaindrops\t600\nWeather.SpeedOfChange\t20\n" +
			"Weather.DaysOfWarning\t4\nWeather.DaysBetweenChanges\t7\n", "forced-weather-test" ) } );
		var weather = new ParkWeather( keys, 5, 10, 10 );
		weather.AdvanceTo( 0 );
		weather.ApplyQuality( 1 );
		for ( var turn = 0; turn < 40; turn++ )
			weather.Step( 0 );
		Assert.IsTrue( weather.RainLevel > 0 );
		GameAudio.SetRain( weather.RainLevel );
		Assert.IsTrue( MixUntilAudible(), "forced rainy weather reaches actual decoded output samples" );
		GameAudio.SetRain( 0 );
		mixer!.StopAll();
		mixer.Mix( 1 );
		var thunder = weather.TakeThunder();
		Assert.IsTrue( thunder.Count > 0, "the forced storm reaches its thunder countdown" );
		foreach ( var top in thunder )
		{
			// Hear the scheduled strike near its top; the range law remains a qualified fork lead.
			AudioListener.Update( top, new Vector3( 0, 0, 1 ) );
			Assert.IsNotNull( GameAudio.PlayThunder( top ) );
		}
		Assert.IsTrue( MixUntilAudible(), "due thunder reaches actual decoded output samples" );
	}

	[TestMethod]
	public void AnUnpositionedSingleScreamStaysFlatAwayFromTheWorldOrigin()
	{
		AudioListener.Update( new Vector3( 100000, 100000, 100000 ), new Vector3( 0, 0, 1 ) );
		var voice = GameAudio.SingleScream( 4, 100, null );
		Assert.IsNotNull( voice );
		Assert.IsNull( voice!.Position, "AUDIO-032 means flat audio, not a source at zero" );
		Assert.IsTrue( MixUntilAudible() );
	}

	[TestMethod]
	public void RemovingOneRideStopsItsRefusedAndHeldChainsButKeepsTheOtherRide()
	{
		var entry = ObjectCatalog.Load( "jungle" ).Entries.Single( item => item.ScriptPath?.EndsWith( "Totem.RSE", StringComparison.OrdinalIgnoreCase ) == true );
		var first = new OriginalObjectRuntime( entry, seed: 1 );
		var second = new OriginalObjectRuntime( entry, seed: 2 );
		try
		{
			void Effect( OriginalObjectRuntime ride, Opcode opcode ) => ride.Script!.Effect( opcode,
				new[] { 2, 50 }.Select( value => new Operand( ride.Script!, new RideScriptOperand( RideScriptOperandKind.Literal, (ushort)value ), -1 ) ).ToArray() );
			Effect( first, Opcode.STARTSCREAM );
			var orphan = (SoundChain)typeof( OriginalObjectEffects ).GetField( "screamChain", BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( first.Script!.Effects )!;
			Assert.IsNotNull( orphan );
			Effect( first, Opcode.STARTSCREAM );
			Assert.IsFalse( orphan.IsStopped, "the traced repeated-start refusal leaves the old chain running" );
			Effect( first, Opcode.STARTSCREAM );
			var held = (SoundChain)typeof( OriginalObjectEffects ).GetField( "screamChain", BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( first.Script!.Effects )!;
			Effect( second, Opcode.STARTSCREAM );
			var other = (SoundChain)typeof( OriginalObjectEffects ).GetField( "screamChain", BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( second.Script!.Effects )!;
			first.Stop();
			Assert.IsTrue( orphan.IsStopped, "runtime teardown also owns the refused handle" );
			Assert.IsTrue( held.IsStopped, "runtime teardown stops the current handle too" );
			Assert.IsFalse( other.IsStopped, "stopping one ride never stops another ride's chain" );
			system.Pump();
			Assert.IsTrue( MixUntilAudible(), "the other ride still produces sound after teardown" );
			second.Stop();
			Assert.IsTrue( other.IsStopped );
		}
		finally { first.Stop(); second.Stop(); }
	}

	[TestMethod]
	public void StaffTurnsVaryTheirGateWithoutAdvancingTheNativeSoundChooser()
	{
		// Find an audible shipped idle sample; many staff catalogue samples deliberately contain silence.
		var selected = false;
		for ( uint seed = 1; seed <= 256; seed++ )
		{
			system.Reseed( seed );
			var candidate = GameAudio.PlayStaffSound( StaffType.Handyman, false, Vector3.Zero );
			if ( candidate != null && mixer!.Mix( 4096 ).Any( sample => sample != 0 ) )
			{
				selected = true;
				break;
			}
			mixer!.StopAll();
			mixer.Mix( 1 );
		}
		Assert.IsTrue( selected, "the test must exercise a nonblank staff sample" );
		mixer!.StopAll();
		mixer.Mix( 1 );
		var seedBefore = system.Seed;
		var member = new[] { new StaffWorldSnapshot( 1, StaffType.Handyman, 0, 0, false, false ) };
		var triggered = 0;
		var audible = false;
		for ( var turn = 0; turn < 512; turn++ )
		{
			GameAudio.UpdateStaffVoices( member, turn );
			var voices = mixer.Voices.Count;
			GameAudio.UpdateStaffVoices( member, turn );
			Assert.AreEqual( voices, mixer.Voices.Count, "a repeated frame in the same park turn has no extra draw" );
			if ( voices > 0 )
				triggered++;
			audible |= mixer.Mix( 4096 ).Any( sample => sample != 0 );
			mixer.StopAll();
			mixer.Mix( 1 );
		}
		Assert.IsTrue( triggered is > 0 and < 128, $"independent advancing 1-in-16 gate: {triggered}/512" );
		Assert.IsTrue( audible, "a triggered staff voice reaches the decoded mix" );
		Assert.AreEqual( seedBefore, system.Seed, "the traced sound chooser seed remains unchanged" );
	}

	[TestMethod]
	public void ListenerMappingUndoesTheImportedTerrainOrigin()
	{
		var field = OriginalPark.Load( "jungle" ).Heightfield;
		var original = new System.Numerics.Vector3( 530, 7, 513 );
		var engine = OriginalParkPlacement.ToEngine( field, original );
		GameAudio.UpdateListener( engine, new System.Numerics.Vector3( 0, 1, 0 ), field );
		Assert.AreEqual( original.X, AudioListener.Position.X, 0.001f );
		Assert.AreEqual( original.Y, AudioListener.Position.Y, 0.001f );
		Assert.AreEqual( original.Z, AudioListener.Position.Z, 0.001f );
		Assert.AreEqual( 1f, AudioListener.Placement( new Vector3( 530, 7, 513 ), 97 ).Gain );
	}
}
