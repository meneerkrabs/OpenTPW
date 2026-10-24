using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
		previousFileSystem = FileSystem;
		previousBonus = ObjectCatalog.BonusDataRoot;
		Log ??= new();
		var data = StringTableTests.OriginalDataDirectory();
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
		FileSystem = previousFileSystem!;
		// only when the setup changed it: the setter also stops the catalog following OPENTPW_BONUS_DATA
		if ( ObjectCatalog.BonusDataRoot != previousBonus )
			ObjectCatalog.BonusDataRoot = previousBonus;
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
	public void InvalidChainPlacementNeverPoisonsTheFollowingPump()
	{
		foreach ( var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity } )
		{
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => system.StartChain( GameAudio.Kids, 71, AudioChannel.Effects, 50, new Vector3( invalid, 0, 0 ) ) );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => system.StartChain( GameAudio.Kids, 71, AudioChannel.Effects, 50, null, invalid ) );
		}
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => system.StartChain( GameAudio.Kids, 71, AudioChannel.Effects, 50, null, -1 ) );
		var sounds = (IList<SoundEntry>)GameAudio.Kids!.Catalog.Find( 71 )!.Sounds;
		var original = sounds[0];
		try
		{
			sounds[0] = original with { Rolloff = float.NaN };
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => system.StartChain( GameAudio.Kids, 71, AudioChannel.Effects, 50, null ) );
		}
		finally { sounds[0] = original; }
		system.Pump();
		Assert.AreEqual( 0, mixer!.Voices.Count, "validation happens before the first child is registered" );
		Assert.IsNotNull( system.StartChain( GameAudio.Kids, 71, AudioChannel.Effects, 50, null ) );
		Assert.IsTrue( MixUntilAudible(), "valid audio still reaches PCM after rejected starts" );
	}

	private static int Children( SoundChain chain ) =>
		((System.Collections.ICollection)typeof( SoundChain ).GetField( "children", BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( chain )!).Count;

	private static void MakeDue( SoundChain chain ) =>
		typeof( SoundChain ).GetField( "nextChildAt", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( chain, 0L );

	[TestMethod]
	public void TheVoicePoolKeyFallsWithTheGroundDistance()
	{
		AudioListener.Update( new Vector3( 10, 500, 20 ), new Vector3( 0, 0, 1 ) );
		Assert.AreEqual( 0xFFFF, SoundEventSystem.PoolKey( null ), "flat" );
		Assert.AreEqual( 0xFFFF, SoundEventSystem.PoolKey( new Vector3( 10, 0, 20 ) ), "at the listener: height does not count" );
		Assert.AreEqual( 0xFFFF - 25, SoundEventSystem.PoolKey( new Vector3( 40, 0, 60 ) ), "d² 2500 / 100" );
		Assert.AreEqual( 0, SoundEventSystem.PoolKey( new Vector3( 10_000, 0, 0 ) ), "past the scale" );
		Assert.IsTrue( SoundEventSystem.Audible( new Vector3( 10, 400, 20 ), 95 ), "the range test is over the ground (0x6bca10): height does not count" );
		Assert.IsFalse( SoundEventSystem.Audible( new Vector3( 110, 500, 20 ), 95 ) );
		Assert.AreEqual( (2700u << 16) | (0xFFFF - 25), SoundEventSystem.PoolRank( 2700, new Vector3( 40, 0, 60 ) ) );
		Assert.IsTrue( SoundEventSystem.PoolRank( 5300, new Vector3( 1000, 0, 0 ) ) > SoundEventSystem.PoolRank( 2700, new Vector3( 10, 0, 20 ) ), "priority decides, distance breaks ties" );
	}

	[TestMethod]
	public void AChainOutOfRangeOrBeyondTheTwelveNearestStartsNoChild()
	{
		// tp.exe 0x6b62b0 ticks the 12 voices of its pool, admitted while audible (0x6bca10) and ranked by 0x6bb9f0; a scream's range is 95.
		var near = Enumerable.Range( 0, SoundEventSystem.VoicePool + 1 ).Select( index => system.StartChain( GameAudio.Kids, 74, AudioChannel.Effects, 50, new Vector3( 5 * index, 0, 0 ) )! ).ToList();
		var far = system.StartChain( GameAudio.Kids, 74, AudioChannel.Effects, 50, new Vector3( 500, 0, 0 ) )!;
		var before = near.Select( Children ).ToList();
		var farBefore = Children( far );
		foreach ( var chain in near.Append( far ) )
			MakeDue( chain );
		system.Pump();
		for ( var index = 0; index < SoundEventSystem.VoicePool; index++ )
			Assert.AreEqual( before[index] + 1, Children( near[index] ), $"chain {index} is among the twelve nearest" );
		Assert.AreEqual( before[^1], Children( near[^1] ), "the thirteenth nearest waits" );
		Assert.AreEqual( farBefore, Children( far ), "out of range: no new child" );

		AudioListener.Update( new Vector3( 500, 0, 0 ), new Vector3( 0, 0, 1 ) );
		MakeDue( far );
		system.Pump();
		Assert.AreEqual( farBefore + 1, Children( far ), "in range again: the next child starts" );
	}

	[TestMethod]
	public void AFailedChainStartStopsItsAlreadyCreatedChild()
	{
		var sounds = (IList<SoundEntry>)GameAudio.Kids!.Catalog.Find( 71 )!.Sounds;
		var original = sounds[0];
		try
		{
			// The first child starts in the pooled tick (tp.exe 0x6bdb50); the link walk fails when the next child is made (0x6bdbb0), with the first child alive.
			var failing = system.StartChain( GameAudio.Kids, 71, AudioChannel.Effects, 50, null )!;
			system.Pump();
			Assert.AreEqual( 1, Children( failing ), "the first child is made before any link is read" );
			sounds[0] = original with { Links = null! };
			MakeDue( failing );
			Assert.ThrowsException<ArgumentNullException>( () => system.Pump() );
			Assert.IsTrue( failing.IsStopped );
			Assert.IsTrue( mixer!.Voices.Count > 0 );
			Assert.IsTrue( mixer.Voices.All( voice => voice.IsFinished ), "startup failure stops its partially created children" );
			system.Pump();
		}
		finally { sounds[0] = original; }
		Assert.IsNotNull( system.StartChain( GameAudio.Kids, 71, AudioChannel.Effects, 50, null ) );
		Assert.IsTrue( MixUntilAudible() );
	}

	[TestMethod]
	public void AFailedDeferredChildStopsOnlyItsChainAndIsNotRetriedEveryFrame()
	{
		var failing = system.StartChain( GameAudio.Kids, 71, AudioChannel.Effects, 50, null )!;
		var survivor = system.StartChain( GameAudio.Kids, 74, AudioChannel.Effects, 50, null )!;
		var sounds = (IList<SoundEntry>)GameAudio.Kids!.Catalog.Find( 71 )!.Sounds;
		var original = sounds[0];
		try
		{
			// Force a later child due now, then fail its link walk.
			typeof( SoundChain ).GetField( "nextChildAt", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( failing, 0L );
			typeof( SoundChain ).GetField( "soundIndex", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( failing, 0 );
			typeof( SoundChain ).GetField( "started", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( failing, true );
			sounds[0] = original with { Links = null! };
			Assert.ThrowsException<ArgumentNullException>( () => system.Pump() );
			Assert.IsTrue( failing.IsStopped );
			Assert.IsFalse( survivor.IsStopped );
			system.Pump();
			Assert.IsTrue( MixUntilAudible(), "unrelated chain audio survives and the failed chain is not retried" );
		}
		finally { sounds[0] = original; }
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
	public void ScreamLevelSetsTheHeldChainsParameterAndNothingWithoutOne()
	{
		// [BIN:TPW-EXE:0x555FDA SCREAMLEVEL] 0x551290(handle, level, speed word) sets the held voice's parameter to clamp((level + speed) / 2, 0, 100); without a handle nothing happens
		var entry = ObjectCatalog.Load( "jungle" ).Entries.Single( item => item.ScriptPath?.EndsWith( "Totem.RSE", StringComparison.OrdinalIgnoreCase ) == true );
		var ride = new OriginalObjectRuntime( entry, seed: 1 );
		try
		{
			void Effect( Opcode opcode, params int[] values ) => ride.Script!.Effect( opcode,
				values.Select( value => new Operand( ride.Script!, new RideScriptOperand( RideScriptOperandKind.Literal, (ushort)value ), -1 ) ).ToArray() );
			SoundChain? Held() => (SoundChain?)typeof( OriginalObjectEffects ).GetField( "screamChain", BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( ride.Script!.Effects );
			Effect( Opcode.SCREAMLEVEL, 100 );
			Assert.IsNull( Held(), "no handle: nothing to set" );
			Effect( Opcode.STARTSCREAM, 2, 50 );
			var chain = Held()!;
			var speed = ride.Script!.SpeedBias;
			Assert.AreEqual( Math.Clamp( (50 + speed) / 2, 0, 100 ), chain.Parameter, "STARTSCREAM's parameter" );
			Effect( Opcode.SCREAMLEVEL, 100 );
			Assert.AreEqual( Math.Clamp( (100 + speed) / 2, 0, 100 ), chain.Parameter );
			Effect( Opcode.SCREAMLEVEL, 10 );
			Assert.AreEqual( Math.Clamp( (10 + speed) / 2, 0, 100 ), chain.Parameter );
			Assert.AreEqual( (10, speed), (((OriginalObjectEffects)ride.Script!.Effects).HeldScreamLevel, ((OriginalObjectEffects)ride.Script!.Effects).HeldScreamSpeed), "the held scream keeps its level for a park resume" );
			Effect( Opcode.STOPSCREAM );
			Effect( Opcode.SCREAMLEVEL, 100 );
			Assert.IsTrue( chain.IsStopped );
			Assert.AreEqual( Math.Clamp( (10 + speed) / 2, 0, 100 ), chain.Parameter, "a stopped chain keeps its last parameter" );
		}
		finally { ride.Stop(); }
	}

	[DataTestMethod]
	[DataRow( 0, true )]
	[DataRow( 1, true )]
	[DataRow( 2, true )]
	[DataRow( 10, true )]
	[DataRow( 0, false )]
	[DataRow( 1, false )]
	[DataRow( 2, false )]
	[DataRow( 10, false )]
	public void UnsupportedEventTypesCannotFallBackToGlobalAudioWhenLevelBanksAreAbsent( int type, bool globalRidesLoaded )
	{
		var rides = SoundCategory.TryLoad( FileSystem, "/global/sound", "cat_rides" );
		Assert.IsNotNull( rides );
		SetField( "<Rides>k__BackingField", globalRidesLoaded ? rides : null );
		SetField( "<LevelRides>k__BackingField", null );
		SetField( "<LevelAmbient>k__BackingField", null );
		// [BIN:TPW-EXE:0x6b87d0 event lookup] a level event is looked up in the level category only, so without a level bank nothing plays
		Assert.IsNull( GameAudio.FindRseEvent( 3, 43 ), "a level-ride event never falls back to the global bank" );
		Assert.IsNull( GameAudio.FindRseEvent( 4, 0x21 ), "a level-ambient event never falls back to the global bank" );
		var eventId = globalRidesLoaded ? 43u : 0x21u;
		Assert.IsNull( GameAudio.FindRseEvent( type, eventId ), "a null level bank cannot turn an unsupported type into audio" );
		Assert.IsNull( GameAudio.PlayRseEvent( type, eventId ) );
		Assert.AreEqual( 0, mixer!.Voices.Count );
	}

	[TestMethod]
	public void OriginalParticleEventInstructionsAreServedWithoutCreatingAudio()
	{
		// [BIN:STP-PPC:0x100AE97C particle EVENT] types 1/2 resolve model keys and allocate particle pool handles, never sound voices
		// Inject shipped EVENT operands through the VM handler and real object-effects hook; do not run unrelated ride triggers.
		var entry = ObjectCatalog.Load( "jungle" ).Entries.Single( item => item.ScriptPath?.EndsWith( "Totem.RSE", StringComparison.OrdinalIgnoreCase ) == true );
		var owner = new OriginalObjectRuntime( entry, seed: 1 );
		var counts = new int[3];
		var negativeKeys = 0;
		try
		{
			// Load real categories so refusing particles cannot pass merely because audio is unavailable.
			foreach ( var (property, category) in new[] { ("Rides", "cat_rides"), ("LevelRides", "cat_rides"), ("LevelAmbient", "cat_ambient") } )
			{
				var directory = property.StartsWith( "Level", StringComparison.Ordinal ) ? "/levels/jungle/Sound" : "/global/sound";
				var loaded = SoundCategory.TryLoad( FileSystem, directory, category );
				Assert.IsNotNull( loaded );
				SetField( $"<{property}>k__BackingField", loaded );
			}
			Assert.IsNotNull( GameAudio.FindRseEvent( 5, 43 ), "a control sound ID really resolves in these loaded catalogs (the global cat_rides; the level ones lack 43)" );
			foreach ( var level in new[] { "jungle", "space", "fantasy", "hallow" } )
				foreach ( var item in ObjectCatalog.Load( level ).Entries.Where( item => item.ScriptPath != null ) )
				{
					using var original = FileSystem.OpenRead( item.ScriptPath! );
					RideScriptFile script;
					try { script = new RideScriptFile( original ); }
					catch ( InvalidDataException ) { continue; }
					foreach ( var instruction in script.Instructions.Where( instruction => instruction.Opcode == (ushort)Opcode.EVENT ) )
					{
						var type = instruction.Operands[0];
						if ( type.Kind != RideScriptOperandKind.Literal || type.Value is not (1 or 2) )
							continue;
						counts[type.Value]++;
						if ( instruction.Operands[1] == new RideScriptOperand( RideScriptOperandKind.Literal, 0xffff ) )
							negativeKeys++;
						var words = new[] { 0x8000_0000u | (uint)Opcode.EVENT }
							.Concat( instruction.Operands.Select( operand => ((uint)operand.Kind << 16) | operand.Value ) )
							.Append( 0x8000_0000u | (uint)Opcode.ENDSLICE ).ToArray();
						using var injected = new MemoryStream( RseScriptTests.CreateScript( words, Array.Empty<string>(), script.VariableNames.ToArray() ) );
						var vm = new RideVM( injected, new RideVMOptions { Effects = owner.Script!.Effects, Seed = 1 } );
						// Variable operands remain zero-initialized; this check qualifies dispatch/refusal, not particle identity or transform parity.
						vm.Advance( FixedStepClock.TickDuration );
						Assert.AreEqual( 0, vm.UnimplementedEffects.Count, item.ScriptPath );
						Assert.AreNotEqual( RideVMState.Faulted, vm.State, item.ScriptPath );
						Assert.IsNull( GameAudio.RseEventCategory( type.Value ) );
						Assert.IsNull( GameAudio.FindRseEvent( type.Value, 43 ), "matching numeric catalog IDs cannot turn particles into sounds" );
						vm.Stop();
					}
				}
			system.Pump();
			Assert.AreEqual( 0, mixer!.Voices.Count, "particle requests never allocate audio voices" );
			Assert.IsTrue( mixer.Mix( 4096 ).All( sample => sample == 0 ), "particle requests produce no PCM" );
			Assert.IsTrue( counts[1] > 0 && counts[2] > 50, "both original particle dispatch types must be exercised" );
			Assert.IsTrue( negativeKeys > 0, "the shipped -1 key sentinel reaches the existing hook" );
			Console.WriteLine( $"Particle EVENT instructions checked: type 1={counts[1]}, type 2={counts[2]}, key -1={negativeKeys}." );
			// Positive control uses the same VM, owning effects hook, banks and mixer: silence above
			// must not pass because a decoder, audio setting or the EVENT hook is disconnected.
			using var control = new MemoryStream( RseScriptTests.CreateScript(
				new uint[] { 0x8000_0000u | (uint)Opcode.EVENT, 5, 0xffff, 43, 0x8000_0000u | (uint)Opcode.ENDSLICE },
				Array.Empty<string>(), Array.Empty<string>() ) );
			var controlVm = new RideVM( control, new RideVMOptions { Effects = owner.Script!.Effects, Seed = 1 } );
			try
			{
				controlVm.Advance( FixedStepClock.TickDuration );
				Assert.AreNotEqual( RideVMState.Faulted, controlVm.State );
				Assert.IsTrue( mixer.Voices.Count > 0, "supported EVENT type 5 reaches the same live audio hook" );
				Assert.IsTrue( MixUntilAudible(), "supported EVENT type 5 produces nonzero decoded PCM in the same mixer" );
			}
			finally { controlVm.Stop(); }
		}
		finally { owner.Stop(); }
	}

	[TestMethod]
	public void StaffTurnsVaryTheirGateAndTheirVoicesDrawFromTheSharedSoundChooser()
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
		Assert.AreNotEqual( seedBefore, system.Seed, "[BIN:TPW-EXE:0x6bc6ce] the triggered voices' sound and sample draws advance the shared chooser (the 1-in-16 gate is the separate staff stream, AUDIO-024)" );
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
