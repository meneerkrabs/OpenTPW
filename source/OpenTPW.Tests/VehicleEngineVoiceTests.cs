using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The ride vehicles' engine voices (docs/TRACKS.md, "Effects"): the placeholder's parameter table (tp.exe 0x6B7150, 0x6BBF00, 0x6BBB80, 0x6BC040), the parameter-bound
/// volume and pitch (0x6BC090, 0x6BC170), the voice place of a vehicle (0x700F20) and the per-tick move and parameter of every vehicle family (0x547F50).
/// Synthetic sounds, plus the shipped EventMaps and sound maps when OPENTPW_GAME_PATH is set.
/// </summary>
[TestClass]
[DoNotParallelize]
public class VehicleEngineVoiceTests
{
	private AudioMixer mixer = null!;
	private SoundEventSystem system = null!;
	private BaseFileSystem? previousFileSystem;

	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void Set( object target, string property, object value ) =>
		target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	[TestInitialize]
	public void Setup()
	{
		previousFileSystem = FileSystem;
		Log ??= new();
		mixer = new AudioMixer( null );
		system = new SoundEventSystem( mixer, 7 );
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
	}

	[TestCleanup]
	public void Cleanup()
	{
		mixer.Dispose();
		FileSystem = previousFileSystem!;
	}

	/// <summary>The shipped jungle kart engine's sound record (cat_ridesSFX.map event 194): volume 68, pitch −24..36, selector 0x10 bound to pitch (mask 2).</summary>
	private static SoundEntry EngineSound( byte selector = 0x10, ushort firstMask = 2, ushort secondMask = 0, byte minimumVolume = 68, byte maximumVolume = 68 )
	{
		var raw = new byte[42];
		raw[12] = minimumVolume;
		raw[13] = maximumVolume;
		raw[14] = unchecked((byte)-24);
		raw[15] = 36;
		raw[20] = raw[21] = 0x32;
		raw[22] = selector;
		BitConverter.GetBytes( firstMask ).CopyTo( raw, 24 );
		raw[26] = selector;
		BitConverter.GetBytes( secondMask ).CopyTo( raw, 28 );
		return new SoundEntry( minimumVolume, maximumVolume, -24, 36, 0, 0, 1, 100, new[] { new SoundSample( 1, 65535, 0, 1, 0 ) }, Array.Empty<SoundLink>(), raw );
	}

	private SoundCategory Category( ushort flags, ushort extra, params SoundEntry[] sounds )
	{
		var category = Empty<SoundCategory>();
		var catalog = Empty<SoundCatalog>();
		var decoded = (Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof( SoundEventSystem ).GetField( "decoded", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( system )!;
		decoded[(category, 1, 1)] = new Mp2Audio( AudioMixer.OutputRate, 1, Enumerable.Repeat( (short)1000, 22050 ).ToArray(), 1, 0 );
		Set( catalog, "Events", new[] { new SoundEvent( 5, 0, flags, extra, sounds ) } );
		Set( category, "Catalog", catalog );
		return category;
	}

	private SoundSentence Playing( SoundSentence sentence )
	{
		for ( var attempt = 0; attempt < 400 && !sentence.IsPlaying; attempt++ )
		{
			system.Pump();
			System.Threading.Thread.Sleep( 5 );
		}
		Assert.IsTrue( sentence.IsPlaying );
		return sentence;
	}

	// ---- the placeholder's parameter table ------------------------------------------------------------

	[TestMethod]
	public void TheTableTakesTheEventCodeAndTheChosenSoundsSelectors()
	{
		Assert.IsNull( SoundParameterTable.ForEvent( new SoundEvent( 1, 0, 6, 0, new[] { EngineSound() } ) ), "an event short +0x12 of 0 creates no table" );
		var table = SoundParameterTable.ForEvent( new SoundEvent( 1, 0, 6, 0x0107, new[] { EngineSound() } ) )!;
		Assert.AreEqual( 7, table.Code( 0 ), "the low byte of +0x12" );
		SoundParameterTable.SelectSound( ref table, EngineSound( selector: 0x10 ) );
		Assert.AreEqual( (0x10, 0x10, 0), (table.Code( 1 ), table.Code( 2 ), table.Code( 3 )) );
		SoundParameterTable.SelectSound( ref table, EngineSound( selector: 0 ) );
		Assert.AreEqual( (0, 0, 0), (table.Code( 1 ), table.Code( 2 ), table.Code( 3 )), "a sound without selectors clears codes 1–3" );
		Assert.AreEqual( 7, table.Code( 0 ) );
		SoundParameterTable? none = null;
		SoundParameterTable.SelectSound( ref none, EngineSound( selector: 0 ) );
		Assert.IsNull( none, "no selectors and no table: still none" );
		SoundParameterTable.SelectSound( ref none, EngineSound( selector: 0x11 ) );
		Assert.AreEqual( 0x11, none!.Code( 1 ), "a selector creates the table" );
	}

	[TestMethod]
	public void ASetReEvaluatesOnlyThroughSlotsOneToThree()
	{
		var table = SoundParameterTable.ForEvent( new SoundEvent( 1, 0, 6, 9, new[] { EngineSound() } ) )!;
		Assert.IsFalse( table.Set( 9, 50 ), "slot 0 stores without re-evaluating" );
		SoundParameterTable.SelectSound( ref table, EngineSound( selector: 0x10 ) );
		Assert.IsTrue( table.Set( 0x10, 30 ) );
		Assert.IsFalse( table.Set( 0x22, 30 ), "no slot has the code" );
		Assert.IsTrue( table.TryValue( EngineSound(), SoundParameterTable.KindPitch, out var pitch ) );
		Assert.AreEqual( 30, pitch );
		Assert.IsFalse( table.TryValue( EngineSound(), SoundParameterTable.KindVolume, out _ ), "the engine binds pitch only" );
		Assert.IsTrue( table.TryValue( EngineSound( firstMask: 0, secondMask: 1 ), SoundParameterTable.KindVolume, out var volume ), "the second mask reads slot 2" );
		Assert.AreEqual( 30, volume );
	}

	[TestMethod]
	public void ABoundPitchIsMinPlusValueTimesRangeOverAHundredWithoutADraw()
	{
		var table = SoundParameterTable.ForEvent( new SoundEvent( 1, 0, 6, 0, new[] { EngineSound() } ) );
		SoundParameterTable.SelectSound( ref table, EngineSound() );
		table!.Set( 0x10, 30 );
		var seed = system.Seed;
		Assert.AreEqual( -24 + 30 * 60 / 100, system.Pitch( EngineSound(), table ) );
		Assert.AreEqual( 68, system.Volume( EngineSound(), table ), "an unbound empty volume range draws nothing" );
		Assert.AreEqual( seed, system.Seed, "the bound pitch takes no chooser draw" );
		table.Set( 0x10, 200 );
		Assert.AreEqual( -24 + 200 * 60 / 100, system.Pitch( EngineSound(), table ), "no clamp at the range's top" );
		// a volume binding: 40 + v × 60 / 100
		var loud = EngineSound( firstMask: 1, minimumVolume: 40, maximumVolume: 100 );
		table.Set( 0x10, 50 );
		Assert.AreEqual( 70, system.Volume( loud, table ) );
		Assert.AreNotEqual( seed, system.Seed + 1, "sanity" );
	}

	// ---- the sentence's parameter table ---------------------------------------------------------------

	[TestMethod]
	public void AParameterChangesThePlayingSegmentsPitchAndAMoveItsPlace()
	{
		var category = Category( 0x6, 0, EngineSound() );
		var sentence = Playing( system.StartSentence( category, 5, AudioChannel.Effects, position: new Vector3( 1, 0, 1 ) )! );
		var voice = mixer.Voices.Last();
		Assert.AreEqual( -24, voice.PitchStep, "before any parameter the bound value is 0: the bottom of the range" );
		sentence.SetParameter( 0x10, 30 );
		Assert.AreEqual( -6, voice.PitchStep );
		Assert.AreEqual( 30, sentence.ParameterValue( SoundParameterTable.KindPitch ) );
		sentence.SetParameter( 0x10, 0x10A );
		Assert.AreEqual( -24 + 10 * 60 / 100, voice.PitchStep, "the value is a byte" );
		sentence.MoveTo( new Vector3( 5, 0, 7 ) );
		Assert.AreEqual( new Vector3( 5, 0, 7 ), voice.Position );
		Assert.AreEqual( new Vector3( 5, 0, 7 ), sentence.Position );
		sentence.Stop();
	}

	[TestMethod]
	public void AParameterSetBeforeTheFirstSegmentIsKept()
	{
		var category = Category( 0x6, 0, EngineSound() );
		var sentence = system.StartSentence( category, 5, AudioChannel.Effects, position: new Vector3( 1, 0, 1 ) )!;
		sentence.SetParameter( 0x10, 30 );
		Playing( sentence );
		Assert.AreEqual( -6, mixer.Voices.Last().PitchStep, "the selectors are in the table from the creation (0x6BBF60 -> 0x6BBF00)" );
		sentence.Stop();
	}

	[TestMethod]
	public void EverySentenceTakesParametersButOnlyBoundCodesReEvaluate()
	{
		// a sentence whose event and sounds bind nothing: the call creates the table (0x6BBB80 -> 0x6B7150), matches no slot 1-3 and leaves the voice alone
		var category = Category( 0x6, 0, EngineSound( selector: 0, firstMask: 0 ) );
		var sentence = Playing( system.StartSentence( category, 5, AudioChannel.Effects, position: new Vector3( 1, 0, 1 ) )! );
		var step = mixer.Voices.Last().PitchStep;
		var seed = system.Seed;
		sentence.SetParameter( 0x10, 30 );
		Assert.AreEqual( step, mixer.Voices.Last().PitchStep );
		Assert.AreEqual( seed, system.Seed, "nothing is evaluated again" );
		Assert.IsNull( sentence.ParameterValue( SoundParameterTable.KindPitch ) );
		Assert.AreEqual( 0, sentence.Parameter, "only a call with the event's code (slot 0) sets the branching value (0x6BE5E0 reads table +4)" );
		sentence.SetParameter( 0x100, 0x1C8 );
		Assert.AreEqual( 0xC8, sentence.Parameter, "code 0x100 is the event's code 0 as a byte, and the value is stored as a byte" );
		sentence.Stop();
	}

	[TestMethod]
	public void AOneShotWithASelectorReadsItsBoundPitchFromItsTableWithoutADraw()
	{
		// every placeholder class sets up its table (0x6BBF60) and queries it (0x6BC040): a one-shot of a pitch-bound sound with no parameter call reads 0,
		// so the kart and bumper stop sounds (EventMap slot 1, e.g. hallow 0xA6 pitch 0..100, fantasy 0x87 -24..36) play at the bottom of their range
		var category = Category( 0, 0, EngineSound() );
		var seed = system.Seed;
		Assert.AreEqual( -24, system.Play( category, 5, AudioChannel.Effects )!.PitchStep );
		Assert.AreEqual( -24, system.PlayAt( category, 5, AudioChannel.Effects, new Vector3( 1, 0, 1 ) )!.PitchStep );
		Assert.AreEqual( -24, system.PlayAtWhenDecoded( category, 5, AudioChannel.Effects, new Vector3( 1, 0, 1 ) )!.Pitch );
		Assert.AreEqual( seed, system.Seed, "one sound, one sample, a fixed volume and a bound pitch: nothing is drawn" );
		// the same sound without a selector draws its pitch
		var unbound = Category( 0, 0, EngineSound( selector: 0, firstMask: 0 ) );
		system.Play( unbound, 5, AudioChannel.Effects );
		Assert.AreNotEqual( seed, system.Seed, "an unbound pitch draws" );
	}

	// ---- the vehicle side -----------------------------------------------------------------------------

	[TestMethod]
	public void TheVoicePlaceIsTheSectionPointScaledAndTruncated()
	{
		Assert.AreEqual( 0.0033036007f, BitConverter.Int32BitsToSingle( 0x3B588139 ), "the float at 0x700F20" );
		Assert.AreEqual( (101, -10), BumpEffects.VoicePlace( 30720, -3072 ), "30720 × 0.0033036 = 101.49, −3072 × 0.0033036 = −10.15: truncated toward zero" );
		Assert.AreEqual( (0, 0), BumpEffects.VoicePlace( 302, -302 ) );
	}

	private sealed class FlatGrid : IParkGrid
	{
		public int Width => 40;
		public int Height => 40;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	private static (BumpController Controller, List<BumpEffect> Effects) Rig( int type )
	{
		uint rng = 77;
		var template = BumperTemplates.Get( type );
		var controller = new BumpController( template, () => { rng = unchecked( rng * 1664525u + 1013904223u ); return rng >> 8; }, new BumpWorld() );
		var effects = new List<BumpEffect>();
		controller.Effect += effects.Add;
		if ( template.IsBumper )
			return (controller, effects);
		var builder = new TrackBuilder( new ParkCellMap( 40, 40 ), template.IsWater ? TrackFamily.Water : TrackFamily.Kart, () => null, new FlatGrid() );
		var station = TrackStation.FromPlacement( 10, 9, 0 );
		builder.PlaceStation( station );
		var writer = new TrackLineWriter( builder );
		writer.Commit( (14, 10), (14, 12) );
		writer.Commit( (14, 12), (8, 12) );
		writer.Commit( (8, 12), (8, 10) );
		var circuit = TrackCircuitWalker.WalkAndRefresh( builder, station );
		var centre = station.Reference;
		controller.SetStation( centre.X, centre.Y, station.Heading );
		controller.Rebuild( circuit );
		return (controller, effects);
	}

	[TestMethod]
	public void EveryVehicleFamilyMovesItsVoiceAndSetsSpeedOverThreeEveryTick()
	{
		foreach ( var type in new[] { -5, -4 } )
		{
			var (controller, effects) = Rig( type );
			controller.Command( 7, 0 );
			controller.Command( 1, 1 );
			Assert.AreEqual( 1, controller.Command( 4, 0 ) );
			if ( !controller.IsWater )
				controller.Command( 3, 0 );
			effects.Clear();
			for ( var tick = 0; tick < 50; tick++ )
				controller.Tick();
			var parameters = effects.Where( effect => effect.Kind == BumpEffectKind.EngineParameter ).ToList();
			Assert.AreEqual( 50, parameters.Count, $"type {type}: one move and parameter per tick (the water branch 0x548104 included)" );
			var car = controller.Vehicles.Single();
			Assert.AreEqual( car.Speed / 3, parameters[^1].Value, $"type {type}" );
			Assert.AreEqual( (car.X, car.Y), (parameters[^1].X, parameters[^1].Y), $"type {type}: the voice goes where the vehicle is" );
		}
	}

	[TestMethod]
	public void TheSmokeEffectsCarryTheDrawnSparksNode()
	{
		var (controller, effects) = Rig( -4 );
		controller.Command( 7, 0 );
		controller.Command( 1, 1 );
		controller.Command( 4, 0 );
		var car = controller.Vehicles.Single();
		car.SparksNode = new System.Numerics.Vector3( 3, 4, 5 );
		controller.Command( 8, 1 );
		controller.Tick();
		Assert.AreEqual( car.SparksNode, effects.Single( effect => effect.Kind == BumpEffectKind.EmitterStart ).Node, "BUMP 8 creates the emitter at the node (0x544D4D)" );
		Assert.AreEqual( car.SparksNode, effects.Last( effect => effect.Kind == BumpEffectKind.EmitterMove ).Node, "the integration moves it there (0x548593)" );
		car.SparksNode = null;
		controller.Tick();
		Assert.IsNull( effects.Last( effect => effect.Kind == BumpEffectKind.EmitterMove ).Node, "headless: no node, the runtime uses the car's point" );
	}

	// ---- the shipped data -------------------------------------------------------------------------------

	private static string? DataDirectory()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			return null;
		return Directory.EnumerateDirectories( path ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path;
	}

	/// <summary>The shipped stop sounds are pitch-bound one-shots: with no parameter call their table's slot reads 0, so they play at the bottom of their range.</summary>
	[TestMethod]
	public void TheShippedStopSoundsPlayAtTheBottomOfTheirPitchRange()
	{
		var data = DataDirectory();
		if ( data == null || !File.Exists( Path.Combine( data, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH is not set." );
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		foreach ( var (theme, id, step) in new[] { ("hallow", 0xA6u, 0), ("space", 0xA8u, 0), ("fantasy", 0x87u, -24) } )
		{
			var rides = SoundCategory.TryLoad( FileSystem, $"/levels/{theme}/Sound", "cat_rides" )!;
			var soundEvent = rides.Catalog.Find( id )!;
			Assert.AreEqual( SoundEventPlayer.OneShot, soundEvent.Player, $"{theme} 0x{id:X}" );
			var sound = soundEvent.Sounds[0];
			var seed = system.Seed;
			Assert.AreEqual( step, system.Pitch( sound, SoundParameterTable.ForPlayback( soundEvent, sound ) ), $"{theme} 0x{id:X}" );
			Assert.AreEqual( seed, system.Seed, "the bound pitch takes no draw" );
		}
	}

	/// <summary>
	/// Every kart, water and bumper ride: the EventMap's variable 10 is the parameter code its engine sound binds to pitch, so the speed reaches the pitch; the
	/// kart meshes carry the sparks node class 0x100 number 1 and the bumper meshes number 2.
	/// </summary>
	[TestMethod]
	public void TheShippedEngineSoundsBindTheirPitchToEventMapVariableTen()
	{
		var data = DataDirectory();
		if ( data == null || !File.Exists( Path.Combine( data, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH is not set." );
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		var checkedRides = 0;
		foreach ( var theme in ObjectCatalog.Themes )
		{
			var rides = SoundCategory.TryLoad( FileSystem, $"/levels/{theme}/Sound", "cat_rides" );
			var global = SoundCategory.TryLoad( FileSystem, "/global/sound", "cat_rides" );
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( entry => !entry.IsBonus && entry.Category == ObjectCategory.Ride && entry.Settings["Bumper.WhichTrackType"] != null ) )
			{
				var runtime = new OriginalObjectRuntime( entry, new RideScriptWorld( 3 ), seed: 1, open: false );
				for ( var tick = 0; tick < 10; tick++ )
					runtime.Simulate( 1f / FixedStepClock.TicksPerSecond );
				if ( runtime.Bump == null || runtime.EventMapEvent( BumpEffects.SlotEngine ) is not { } engine )
				{
					runtime.Stop();
					continue;
				}
				var soundEvent = rides?.Catalog.Find( (uint)engine ) ?? global?.Catalog.Find( (uint)engine );
				Assert.IsNotNull( soundEvent, $"{theme} {entry.DisplayName}: engine event {engine}" );
				var code = runtime.EventMapVariable( BumpEffects.EngineParameterVariable );
				Assert.IsNotNull( code, $"{theme} {entry.DisplayName}: variable 10" );
				foreach ( var sound in soundEvent.Sounds.Where( sound => (BitConverter.ToUInt16( sound.Raw, 24 ) & SoundParameterTable.KindPitch) != 0 ) )
					Assert.AreEqual( sound.Raw[22], (byte)code.Value, $"{theme} {entry.DisplayName}: the engine's pitch selector is variable 10" );
				if ( runtime.Bump.Template.IsKart )
					Assert.IsTrue( soundEvent.Sounds.Any( sound => (BitConverter.ToUInt16( sound.Raw, 24 ) & SoundParameterTable.KindPitch) != 0 ), $"{theme} {entry.DisplayName}: the kart engine has a pitch binding" );
				runtime.Stop();
				checkedRides++;
			}
		}
		Assert.IsTrue( checkedRides >= 8, $"the kart and water rides at least ({checkedRides})" );
	}
}
