using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// The sounds a ride script keeps with ADDOBJ types 3-9 (type 10: ObjectTypeSoundsTests) and the opcodes that reach them (KILLOBJ, FADEOBJ, SETOBJPARAM): tp.exe 0x551F5F, 0x557970, 0x5573D0, the object pass
/// 0x5516B0 (re-position every eighth pass, 0x551A1C -> 0x51C270), 0x5522C5 (0x51C2C0), 0x5523D0 (0x51C300 -> 0x6B8930), 0x5524D9 (0x51BC40 -> 0x6BBB80) and the script destructor 0x558500.
/// </summary>
[TestClass]
[DoNotParallelize]
public class HeldSoundTests
{
	private string root = null!;
	private List<Entity> previousEntities = null!;
	private ObjectCatalogEntry entry = null!;
	private readonly Dictionary<FieldInfo, object?> savedAudio = new();
	private readonly List<RideVM> scripts = new();
	private AudioMixer mixer = null!;
	private SoundEventSystem system = null!;

	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void Set( object target, string property, object value ) =>
		target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( target, value );

	/// <summary>A sound whose selector 20 is bound to the pitch (the bus engine's parameter code): pitch step -24 + value * 60 / 100.</summary>
	private static SoundEntry BoundSound()
	{
		var raw = new byte[42];
		raw[12] = raw[13] = 68;
		raw[14] = unchecked((byte)-24);
		raw[15] = 36;
		raw[20] = raw[21] = 0x32;
		raw[22] = 20;
		BitConverter.GetBytes( (ushort)2 ).CopyTo( raw, 24 );
		raw[26] = 20;
		return new SoundEntry( 68, 68, -24, 36, 0, 0, 1, 100, new[] { new SoundSample( 1, 65535, 0, 1, 0 ) }, Array.Empty<SoundLink>(), raw );
	}

	[TestInitialize]
	public void Setup()
	{
		Log ??= new();
		previousEntities = Entity.All;
		Entity.All = new();
		root = Directory.CreateTempSubdirectory( "opentpw-held-sound-" ).FullName;
		var original = Md2ModelFileTests.CreateGeometry();
		var bytes = new byte[original.Length + 176];
		original.CopyTo( bytes, 0 );
		var first = original.Length;
		var second = first + 88;
		original.AsSpan( 0x1c4, 88 ).CopyTo( bytes.AsSpan( first ) );
		original.AsSpan( 0x264, 88 ).CopyTo( bytes.AsSpan( second ) );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( first ), 0x200 );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( first + 12 ), (uint)second );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( second + 4 ), (uint)first );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x74 ), (uint)first );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x78 ), (uint)first );
		foreach ( var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x44, 0x4a } )
			BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( offset ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x1b0 ), 0x330 );
		File.WriteAllBytes( Path.Combine( root, "model.MD2" ), bytes );
		File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
		entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/synthetic", new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1100\n", "synthetic.sam" ) } ),
			new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( root ), null );
		mixer = new AudioMixer( null );
		system = new SoundEventSystem( mixer, 7 );
		var category = Empty<SoundCategory>();
		var catalog = Empty<SoundCatalog>();
		// 43: a one-shot, 44: a looping one-shot (flag 0x8), 45: a repeating sentence (flags 0x6), all with the pitch-bound sound
		Set( catalog, "Events", new[]
		{
			new SoundEvent( 43, 0, 0, 0, new[] { BoundSound() } ),
			new SoundEvent( 44, 0, 8, 0, new[] { BoundSound() } ),
			new SoundEvent( 45, 0, 6, 0, new[] { BoundSound() } ),
		} );
		Set( category, "Catalog", catalog );
		var decoded = (Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof( SoundEventSystem ).GetField( "decoded", BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( system )!;
		decoded[(category, 1, 1)] = new Mp2Audio( AudioMixer.OutputRate, 1, Enumerable.Repeat( (short)1000, 22050 ).ToArray(), 1, 0 );
		Audio( "events", system );
		foreach ( var property in new[] { "LevelRides", "LevelAmbient", "Rides", "Kids", "Staff", "Ambient", "Ui" } )
			Audio( "<" + property + ">k__BackingField", category );
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
	}

	private void Audio( string name, object value )
	{
		var field = typeof( GameAudio ).GetField( name, BindingFlags.NonPublic | BindingFlags.Static )!;
		savedAudio.Add( field, field.GetValue( null ) );
		field.SetValue( null, value );
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var script in scripts )
			script.Stop();
		scripts.Clear();
		foreach ( var entity in Entity.All.ToArray() )
			entity.Delete();
		Entity.All = previousEntities;
		mixer.Dispose();
		foreach ( var pair in savedAudio )
			pair.Key.SetValue( null, pair.Value );
		savedAudio.Clear();
		Directory.Delete( root, true );
	}

	private OriginalObject Placed() => new( entry, new ObjectPlacement( 3, 4, 0, new N.Vector2( -7, 11 ), 9 ) );

	/// <summary>Runs <paramref name="body"/> as a script of the object for one slice and leaves it alive (a stopped script ends its records), so its records can be reached later.</summary>
	private RideVM Script( OriginalObjectRuntime runtime, Func<RideVMTests.Asm, RideVMTests.Asm> body )
	{
		var stream = new MemoryStream( body( new RideVMTests.Asm() ).I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
		var vm = new RideVM( stream, new RideVMOptions { Effects = runtime.Script!.Effects, World = runtime.Script.World } );
		vm.Advance( FixedStepClock.TickDuration );
		Assert.AreNotEqual( RideVMState.Faulted, vm.State, vm.FaultMessage );
		scripts.Add( vm );
		return vm;
	}

	[DataTestMethod]
	[DataRow( 3 )][DataRow( 4 )][DataRow( 5 )][DataRow( 6 )][DataRow( 7 )][DataRow( 8 )][DataRow( 9 )]
	public void AddObjOfEverySoundTypeKeepsTheSoundUnderItsSlot( int type )
	{
		// [BIN:TPW-EXE:0x551F5F ADDOBJ] [BIN:TPW-EXE:0x557970 record] types 3-9 submit the event like EVENT and keep the handle under the slot operand
		var placed = Placed();
		var vm = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, type, -1, 43, 12 ) );
		var record = placed.Runtime.HeldSounds.Single();
		Assert.AreEqual( (vm.ScriptId, 12, type, -1), (record.ScriptId, record.Slot, record.Type, record.Key) );
		var voice = mixer.Voices.Single();
		Assert.IsFalse( voice.IsFinished );
		Assert.AreEqual( OriginalObjectRuntime.WholeUnits( placed.Runtime.ReadFootprintMiddle() ), voice.Position, "key -1: the footprint middle, truncated to whole units" );
		Assert.AreEqual( 0, vm.UnimplementedEffects.Count, "no longer an unimplemented effect" );
	}

	[TestMethod]
	public void AddObjExtTakesItsSlotFromTheFifthOperandAndIgnoresTheControl()
	{
		// [BIN:TPW-EXE:0x5520E8 ADDOBJ_EXT] type key event control slot: the sound types ignore the control
		var placed = Placed();
		var vm = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ_EXT, 3, -1, 43, 777, 9 ) );
		Assert.AreEqual( 9, placed.Runtime.HeldSounds.Single().Slot );
		Assert.AreEqual( 1, mixer.Voices.Count );
		Assert.AreEqual( 0, vm.UnimplementedEffects.Count );
	}

	[TestMethod]
	public void AnAddObjThatSubmitsNothingKeepsNoRecord()
	{
		var placed = Placed();
		// an event the category lacks, an event id 0, a key naming no node: the submit has no handle (0x557A0A frees the record)
		var vm = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 3, -1, 99, 1 ).I( Opcode.ADDOBJ, 3, -1, 0, 1 ).I( Opcode.ADDOBJ, 4, 99, 43, 1 ) );
		Assert.AreEqual( 0, placed.Runtime.HeldSounds.Count );
		Assert.AreEqual( 0, mixer.Voices.Count );
		Assert.AreEqual( 0, vm.UnimplementedEffects.Count, "a refused submit is not an unimplemented effect" );
		// types 0 and above 10 are not served by the dispatcher (0x557931 logs); the particle types keep no sound
		Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 0, -1, 43, 1 ).I( Opcode.ADDOBJ, 11, -1, 43, 1 ).I( Opcode.ADDOBJ, 1, -1, 43, 1 ).I( Opcode.ADDOBJ, 2, 7, 43, 1 ) );
		Assert.AreEqual( 0, placed.Runtime.HeldSounds.Count );
		Assert.AreEqual( 0, mixer.Voices.Count );
		// type 10 plays from the object type's own sound list, which this type does not have (0x557686 logs and returns handle 0; ObjectTypeSoundsTests)
		var ten = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 10, -1, 43, 1 ) );
		Assert.AreEqual( 0, ten.UnimplementedEffects.Count );
		Assert.AreEqual( 0, placed.Runtime.HeldSounds.Count );
	}

	[TestMethod]
	public void ARepeatingEventIsHeldAsASentenceAndAOneShotAsAPlacedSound()
	{
		var placed = Placed();
		Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 3, -1, 45, 1 ).I( Opcode.ADDOBJ, 3, -1, 43, 2 ) );
		var records = placed.Runtime.HeldSounds;
		Assert.IsNotNull( records.Single( r => r.Slot == 1 ).Sound.Sentence );
		Assert.IsNotNull( records.Single( r => r.Slot == 2 ).Sound.Placed );
	}

	[TestMethod]
	public void AKeyedSoundStartsAtItsNodeAndAHeadlessObjectKeepsAFlatSound()
	{
		// [BIN:TPW-EXE:0x556b90 EVENT anchor] key >= 0: the node's place (selector 0x200)
		var placed = Placed();
		placed.Runtime.SetEventAnchors( () => new Vector3( 1, 2, 3 ), _ => new Vector3( 100.9f, 0, -3.5f ) );
		Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 3, 7, 43, 5 ) );
		Assert.AreEqual( new Vector3( 100, 0, -3 ), mixer.Voices.Single().Position, "truncated toward zero (ftol)" );
		var headless = new OriginalObjectRuntime( entry );
		Script( headless, a => a.I( Opcode.ADDOBJ, 3, -1, 43, 5 ) );
		Assert.AreEqual( 1, headless.HeldSounds.Count );
		Assert.IsNull( mixer.Voices.Last().Position, "no placement: a flat voice, as an EVENT" );
		headless.Stop();
	}

	[TestMethod]
	public void KeyedSoundsFollowTheirNodeInOneOfEightPassesAndTheScriptsIdSelectsWhich()
	{
		// [BIN:TPW-EXE:0x551A1C sound follow] ((script id) xor (pass counter)) & 7 == 0 moves the sound to its node (0x51C270); a negative key never moves
		var placed = Placed();
		var shift = 100f;
		placed.Runtime.SetEventAnchors( () => new Vector3( 1, 2, 3 ), _ => new Vector3( shift, 0, 0 ) );
		var vm = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 3, 7, 43, 5 ).I( Opcode.ADDOBJ, 3, -1, 43, 6 ) );
		var keyed = mixer.Voices[0];
		var fixedAtStart = mixer.Voices[1];
		var footprint = fixedAtStart.Position;
		var moved = new List<int>();
		for ( var pass = 1; pass <= 24; pass++ )
		{
			shift = 1000 + pass * 10;
			var before = keyed.Position;
			placed.Runtime.Simulate( FixedStepClock.TickDuration );
			var gated = ((vm.ScriptId ^ pass) & 7) == 0;
			Assert.AreEqual( gated, keyed.Position != before, $"pass {pass}, script {vm.ScriptId}" );
			if ( gated )
			{
				moved.Add( pass );
				Assert.AreEqual( new Vector3( shift, 0, 0 ), keyed.Position );
			}
			Assert.AreEqual( footprint, fixedAtStart.Position, "a record with a negative key stays where it started" );
		}
		Assert.AreEqual( 3, moved.Count, "one pass in eight" );
	}

	[TestMethod]
	public void TheSchedulersPassCounterSelectsTheFollowPassWhenTheParkRunsTheObject()
	{
		var placed = Placed();
		var shift = 100f;
		placed.Runtime.SetEventAnchors( () => new Vector3( 1, 2, 3 ), _ => new Vector3( shift, 0, 0 ) );
		var vm = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 4, 7, 43, 5 ) );
		var voice = mixer.Voices.Single();
		var world = placed.Script!.World;
		var substep = new SimulationSubstep( true, true, 0, 0 );
		var moves = 0;
		for ( var step = 0; step < 16; step++ )
		{
			world.RunPass( SimulationSubstep.Milliseconds );
			shift = 1000 + step * 10;
			var before = voice.Position;
			placed.Runtime.Simulate( FixedStepClock.TickDuration, substep );
			placed.Runtime.Simulate( FixedStepClock.TickDuration, substep );
			var gated = ((vm.ScriptId ^ (int)world.PassCounter) & 7) == 0;
			Assert.AreEqual( gated, voice.Position != before, $"pass {world.PassCounter}" );
			if ( gated )
				moves++;
		}
		Assert.AreEqual( 2, moves );
	}

	[TestMethod]
	public void KillObjStopsEverySoundOfTheSlotAndFreesItsRecords()
	{
		// [BIN:TPW-EXE:0x5522C5 KILLOBJ] every record with the slot: the sound stops at once (0x51C2C0) and the record is freed
		var placed = Placed();
		var vm = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 3, -1, 43, 10 ).I( Opcode.ADDOBJ, 8, -1, 44, 10 ).I( Opcode.ADDOBJ, 3, -1, 44, 11 ).I( Opcode.ENDSLICE )
			.I( Opcode.KILLOBJ, 10 ).I( Opcode.ENDSLICE ).I( Opcode.KILLOBJ, 10 ).I( Opcode.KILLOBJ, 77 ).I( Opcode.ENDSLICE ) );
		Assert.AreEqual( 3, placed.Runtime.HeldSounds.Count );
		var voices = mixer.Voices.ToArray();
		vm.Advance( FixedStepClock.TickDuration );
		Assert.IsTrue( voices[0].IsFinished && voices[1].IsFinished, "both records of slot 10 ended" );
		Assert.IsFalse( voices[2].IsFinished, "slot 11 plays on" );
		CollectionAssert.AreEqual( new[] { 11 }, placed.Runtime.HeldSounds.Select( r => r.Slot ).ToArray(), "the records of the slot are freed" );
		vm.Advance( FixedStepClock.TickDuration );
		Assert.IsFalse( voices[2].IsFinished, "a slot with no record (or already freed) does nothing" );
		Assert.AreEqual( 1, placed.Runtime.HeldSounds.Count );
		Assert.AreEqual( 0, vm.UnimplementedEffects.Count );
	}

	[TestMethod]
	public void KillObjEndsOnlyItsOwnScriptsRecordsOfTheSlot()
	{
		var placed = Placed();
		var a = Script( placed.Runtime, s => s.I( Opcode.ADDOBJ, 3, -1, 43, 1 ).I( Opcode.ADDOBJ, 3, -1, 44, 2 ) );
		var voiceA = mixer.Voices[0];
		var b = Script( placed.Runtime, s => s.I( Opcode.ADDOBJ, 3, -1, 43, 1 ).I( Opcode.KILLOBJ, 1 ) );
		Assert.IsFalse( voiceA.IsFinished, "the other script's KILLOBJ 1 does not reach script A's slot 1 record (the lists are per script)" );
		Assert.AreEqual( 2, placed.Runtime.HeldSounds.Count( r => r.ScriptId == a.ScriptId ) );
		Assert.AreEqual( 0, placed.Runtime.HeldSounds.Count( r => r.ScriptId == b.ScriptId ) );
		Assert.IsTrue( mixer.Voices[2].IsFinished, "B's own sound ended" );
	}

	[TestMethod]
	public void LowQualityFadeStopsTheSoundAndEndsTheParticleRecordsToo()
	{
		system.HeldSounds.Configure( new(), 0 );
		// [BIN:TPW-EXE:0x5523D0 FADEOBJ] types 3-10: 0x51C300 -> 0x6B8930, which takes the stop call 0x6B89B0 while the library's 0x400 bit is clear (SoundQuality 0); types 1-2: 0x51FF70(handle, -2)
		var killed = new List<int>();
		var previousStarter = OriginalObjectRuntime.ParticleStarter;
		var previousKiller = OriginalObjectRuntime.ParticleKiller;
		var previousAlive = OriginalObjectRuntime.ParticleAlive;
		OriginalObjectRuntime.ParticleStarter = _ => 0x10001;
		OriginalObjectRuntime.ParticleKiller = killed.Add;
		OriginalObjectRuntime.ParticleAlive = _ => true;
		try
		{
			var placed = Placed();
			var vm = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 1, -1, 43, 11 ).I( Opcode.ADDOBJ, 3, -1, 44, 11 ).I( Opcode.ADDOBJ, 3, -1, 44, 12 ) );
			var voice = mixer.Voices[0];
			var untouched = mixer.Voices[1];
			Assert.AreEqual( 1, placed.Runtime.CaptureSnapshot().ParticleObjects!.Count );
			Assert.IsTrue( voice.Loops && !voice.IsFinished );
			var fade = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 3, -1, 44, 11 ) );
			var own = Script( placed.Runtime, a => a.I( Opcode.FADEOBJ, 11 ) );
			Assert.AreEqual( 0, own.UnimplementedEffects.Count, "FADEOBJ is served" );
			CollectionAssert.AreEqual( new[] { 0x10001 }, killed, "the particle record of the slot ends (0x51FF70(handle, -2)) whichever script asks" );
			Assert.IsFalse( voice.IsFinished, "the sound belongs to the first script" );
			Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 3, -1, 44, 11 ).I( Opcode.FADEOBJ, 11 ).I( Opcode.FADEOBJ, 11 ) );
			Assert.IsTrue( mixer.Voices.Last().IsFinished, "FADEOBJ stops a sound at once, with no tail" );
			vm.Stop();
			Assert.IsTrue( voice.IsFinished && untouched.IsFinished );
			Assert.IsNotNull( fade );
		}
		finally { OriginalObjectRuntime.ParticleStarter = previousStarter; OriginalObjectRuntime.ParticleKiller = previousKiller; OriginalObjectRuntime.ParticleAlive = previousAlive; }
	}

	[TestMethod]
	public void LowQualityFadeEndsTheSoundsOfTheSameScript()
	{
		system.HeldSounds.Configure( new(), 0 );
		var placed = Placed();
		var vm = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 5, -1, 44, 500 ).I( Opcode.ADDOBJ, 5, -1, 43, 500 ).I( Opcode.ADDOBJ, 5, -1, 44, 501 ).I( Opcode.ENDSLICE ).I( Opcode.FADEOBJ, 500 ) );
		var voices = mixer.Voices.ToArray();
		Script( placed.Runtime, a => a.I( Opcode.FADEOBJ, 500 ) );
		Assert.IsFalse( voices[0].IsFinished, "another script's FADEOBJ reaches nothing of script A" );
		vm.Advance( FixedStepClock.TickDuration );
		Assert.IsTrue( voices[0].IsFinished && voices[1].IsFinished );
		Assert.IsFalse( voices[2].IsFinished, "slot 501 plays on" );
		Assert.AreEqual( 0, vm.UnimplementedEffects.Count );
	}

	[TestMethod]
	public void HighQualityFadeReleasesAllMatchingRecordsButServiceKeepsTheirTails()
	{
		system.HeldSounds.Configure( new(), 50 );
		var placed = Placed();
		var other = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 5, -1, 44, 500 ) );
		var vm = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 5, -1, 44, 500 ).I( Opcode.ADDOBJ, 5, -1, 43, 500 ).I( Opcode.ADDOBJ, 5, -1, 44, 501 )
			.I( Opcode.ENDSLICE ).I( Opcode.FADEOBJ, 500 ).I( Opcode.SETOBJPARAM, 500, 20, 100 ).I( Opcode.KILLOBJ, 500 ) );
		var tails = placed.Runtime.HeldSounds.Where( record => record.ScriptId == vm.ScriptId && record.Slot == 500 ).Select( record => record.Sound ).ToArray();
		var voices = tails.Select( tail => tail.Placed!.Voice! ).ToArray();
		vm.Advance( FixedStepClock.TickDuration );
		Assert.AreEqual( 0, placed.Runtime.HeldSounds.Count( record => record.ScriptId == vm.ScriptId && record.Slot == 500 ) );
		Assert.AreEqual( 2, placed.Runtime.HeldSounds.Count );
		Assert.IsTrue( tails.All( tail => tail.Lifetime.Fading && !tail.IsFinished ) );
		Assert.IsTrue( voices.All( voice => voice.PitchStep == -24 ), "Released records cannot receive later parameter writes" );
		vm.Stop();
		Assert.IsTrue( tails.All( tail => !tail.IsFinished ), "Script destruction cannot lose service-owned tails" );
		Assert.IsTrue( system.HeldSounds.Sounds.Contains( tails[0] ) );
		mixer.StopAll();
		Assert.IsTrue( tails.All( tail => tail.IsFinished ) );
		Assert.IsNotNull( other );
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void LeaveParkAndSoundSwitchStopReleasedTails( bool leave )
	{
		// Each teardown gets its own initialized category: LeavePark forgets its decoded samples.
		var placed = Placed();
		var vm = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 5, -1, 44, 500 ).I( Opcode.ENDSLICE ).I( Opcode.FADEOBJ, 500 ) );
		var tail = placed.Runtime.HeldSounds.Single().Sound;
		vm.Advance( FixedStepClock.TickDuration ); Assert.IsFalse( tail.IsFinished );
		if ( leave ) GameAudio.LeavePark();
		else
		{
			var wasEnabled = GameAudio.Enabled;
			try { GameAudio.Enabled = false; } finally { GameAudio.Enabled = wasEnabled; }
		}
		Assert.IsTrue( tail.IsFinished );
		Assert.AreEqual( 0, system.HeldSounds.Sounds.Count );
	}

	private void Pump( SoundSentence sentence )
	{
		for ( var attempt = 0; attempt < 400 && !sentence.IsPlaying; attempt++ )
		{
			system.Pump();
			System.Threading.Thread.Sleep( 5 );
		}
		Assert.IsTrue( sentence.IsPlaying );
	}

	[TestMethod]
	public void SetObjParamGivesTheParameterToTheSoundsOfTheSlot()
	{
		// [BIN:TPW-EXE:0x5524D9 SETOBJPARAM] types 3-10: 0x51BC40(handle, code, value) -> the voice's SetParameter (0x6BBB80); the bus engine's code 20 is bound to the pitch here
		var placed = Placed();
		var vm = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 8, -1, 43, 1 ).I( Opcode.ADDOBJ, 8, -1, 43, 2 ).I( Opcode.ADDOBJ, 8, -1, 45, 1 ).I( Opcode.ENDSLICE ).I( Opcode.SETOBJPARAM, 1, 20, 75 ) );
		var first = mixer.Voices[0];
		var second = mixer.Voices[1];
		var sentence = placed.Runtime.HeldSounds.Single( r => r.Sound.Sentence != null ).Sound.Sentence!;
		Pump( sentence );
		var sentenceVoice = sentence.Voice!;
		Assert.AreEqual( -24, first.PitchStep, "before any call the bound value is 0: the bottom of the pitch range" );
		Assert.AreEqual( -24, sentenceVoice.PitchStep );
		vm.Advance( FixedStepClock.TickDuration );
		Assert.AreEqual( -24 + 75 * 60 / 100, first.PitchStep, "(value x (max - min)) / 100 + min, on the placed one-shot of the slot" );
		Assert.AreEqual( -24 + 75 * 60 / 100, sentenceVoice.PitchStep, "and on the sentence of the slot" );
		Assert.AreEqual( -24, second.PitchStep, "the other slot is untouched" );
		Assert.AreEqual( 0, vm.UnimplementedEffects.Count );
	}

	[TestMethod]
	public void ASetObjParamBeforeTheFirstSegmentIsKeptAndTheValueIsAByte()
	{
		var placed = Placed();
		Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 8, -1, 45, 1 ).I( Opcode.SETOBJPARAM, 1, 20, 0x10A ) );
		var sentence = placed.Runtime.HeldSounds.Single().Sound.Sentence!;
		Pump( sentence );
		Assert.AreEqual( -24 + 10 * 60 / 100, sentence.Voice!.PitchStep, "the table keeps the byte 0x0A that was set before the first segment played" );
	}

	[TestMethod]
	public void SetObjParamSkipsParticleRecordsAndDeadSounds()
	{
		var previousStarter = OriginalObjectRuntime.ParticleStarter;
		var previousAlive = OriginalObjectRuntime.ParticleAlive;
		OriginalObjectRuntime.ParticleStarter = _ => 0x10001;
		OriginalObjectRuntime.ParticleAlive = _ => true;
		try
		{
			var placed = Placed();
			var vm = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 1, -1, 43, 4 ).I( Opcode.ADDOBJ, 3, -1, 43, 4 ).I( Opcode.KILLOBJ, 99 ).I( Opcode.SETOBJPARAM, 4, 20, 75 ) );
			Assert.AreEqual( 1, placed.Runtime.CaptureSnapshot().ParticleObjects!.Count, "the particle record is there and takes no call" );
			Assert.AreEqual( 0, vm.UnimplementedEffects.Count );
			var sound = placed.Runtime.HeldSounds.Single().Sound;
			Assert.AreEqual( -24 + 75 * 60 / 100, mixer.Voices.Single().PitchStep );
			sound.Stop();
			// a sound that has ended takes no more calls (the library no longer finds its handle)
			sound.SetParameter( 20, 0 );
			Assert.AreEqual( -24 + 75 * 60 / 100, mixer.Voices.Single().PitchStep );
		}
		finally { OriginalObjectRuntime.ParticleStarter = previousStarter; OriginalObjectRuntime.ParticleAlive = previousAlive; }
	}

	[TestMethod]
	public void AScriptsEndAndStopEndTheSoundsItStillHolds()
	{
		// [BIN:TPW-EXE:0x558703 script destructor] 0x558500 ends every record the script still holds (types 3-10 through 0x51C2C0)
		var placed = Placed();
		var ending = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 3, -1, 44, 1 ).I( Opcode.ADDOBJ, 4, -1, 44, 2 ) );
		var staying = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 3, -1, 44, 1 ) );
		var voices = mixer.Voices.ToArray();
		Assert.AreEqual( 3, placed.Runtime.HeldSounds.Count );
		// END: the program counter goes negative and the pass destroys the script
		var endStream = new MemoryStream( new RideVMTests.Asm().I( Opcode.ADDOBJ, 3, -1, 44, 1 ).I( Opcode.END ).Build( Enum.GetNames<RideVariables>() ) );
		var finished = new RideVM( endStream, new RideVMOptions { Effects = placed.Script!.Effects, World = placed.Script.World } );
		scripts.Add( finished );
		finished.Advance( FixedStepClock.TickDuration );
		Assert.AreEqual( RideVMState.Halted, finished.State );
		Assert.IsTrue( mixer.Voices[^1].IsFinished, "an END ends the script's sound" );
		Assert.AreEqual( 3, placed.Runtime.HeldSounds.Count, "and frees its record" );
		ending.Stop();
		Assert.IsTrue( voices[0].IsFinished && voices[1].IsFinished );
		Assert.IsFalse( voices[2].IsFinished, "the other script's sound plays on" );
		Assert.AreEqual( 1, placed.Runtime.HeldSounds.Count );
		// removing the object (the park's end, a delete) ends everything the scripts still hold
		placed.Delete();
		Assert.IsTrue( voices[2].IsFinished );
		Assert.AreEqual( 0, placed.Runtime.HeldSounds.Count );
		Assert.AreNotEqual( RideVMState.Faulted, staying.State );
	}

	[TestMethod]
	public void AnObjectsRemovalEndsItsHeldSoundsAndLeavesAnotherObjectsAlone()
	{
		var first = new OriginalObject( entry, new ObjectPlacement( 1, 2, 0, N.Vector2.Zero, 0 ) );
		var second = new OriginalObject( entry, new ObjectPlacement( 3, 4, 180, N.Vector2.Zero, 1 ) );
		Script( first.Runtime, a => a.I( Opcode.ADDOBJ, 3, -1, 44, 1 ) );
		var firstVoice = mixer.Voices.Last();
		Script( second.Runtime, a => a.I( Opcode.ADDOBJ, 3, -1, 44, 1 ) );
		var secondVoice = mixer.Voices.Last();
		first.Delete();
		Assert.IsTrue( firstVoice.IsFinished );
		Assert.IsFalse( secondVoice.IsFinished );
		foreach ( var entity in Entity.All.ToArray() )
			entity.Delete();
		Assert.IsTrue( secondVoice.IsFinished );
		Assert.IsTrue( mixer.Mix( 1 ).All( value => value == 0 ) );
	}

	[TestMethod]
	public void HeldSoundsAreNotSavedAndAResumedParkStartsNone()
	{
		// [BIN:TPW-EXE:0x559710 RSSE writer] [BIN:TPW-EXE:0x559f44 RSSE loader] the original's save holds each script's records raw (type, handle, key, node, slot) and its loader links them back without
		// starting a sound, so after a load the sound library has no voice for those handles; OpenTPW's save holds no sound, so a looping held ambient does not block the save (unlike a looping EVENT)
		var placed = Placed();
		var before = System.Text.Json.JsonSerializer.Serialize( placed.Runtime.CaptureSnapshot() );
		Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 8, -1, 44, 1 ).I( Opcode.ADDOBJ, 3, -1, 45, 2 ) );
		var voice = mixer.Voices.Single( v => v.Loops );
		var saved = placed.Runtime.CaptureSnapshot();
		Assert.AreEqual( before, System.Text.Json.JsonSerializer.Serialize( saved ), "held sounds are in no saved state" );
		var restored = OriginalObjectRuntime.CreateForRestore( entry, new RideScriptWorld(), seed: 2, open: true, attractionId: saved.AttractionId );
		var voicesBefore = mixer.Voices.Count;
		Assert.AreEqual( 0, restored.RestoreSnapshot( saved ).Count );
		Assert.AreEqual( 0, restored.HeldSounds.Count );
		Assert.AreEqual( voicesBefore, mixer.Voices.Count, "a resumed park starts no held sound" );
		Assert.IsFalse( voice.IsFinished, "capturing leaves the live sound alone" );
		// a KILLOBJ after the resume finds nothing and does nothing
		Script( restored, a => a.I( Opcode.KILLOBJ, 1 ).I( Opcode.FADEOBJ, 2 ).I( Opcode.SETOBJPARAM, 1, 20, 3 ) );
		restored.Stop();
	}

	[TestMethod]
	public void OnlyTheGenuinelyUnimplementedEffectsStillCount()
	{
		var placed = Placed();
		var vm = Script( placed.Runtime, a => a
			.I( Opcode.STARTSCREAM, 1, 50 ).I( Opcode.SCREAMLEVEL, 60 ).I( Opcode.STOPSCREAM ).I( Opcode.SINGLESCREAM, 1, -1 )
			.I( Opcode.EVENT, 3, -1, 43 ).I( Opcode.EVENT, 1, -1, 43 ).I( Opcode.EVENT_EXT, 5, -1, 43, 500 ).I( Opcode.REPAIREFFECT, 1 ).I( Opcode.REPAIREFFECT, 0 )
			.I( Opcode.ADDOBJ, 1, -1, 43, 1 ).I( Opcode.ADDOBJ, 3, -1, 43, 2 ).I( Opcode.ADDOBJ_EXT, 9, -1, 43, 500, 3 )
			.I( Opcode.SETOBJPARAM, 2, 20, 5 ).I( Opcode.FADEOBJ, 2 ).I( Opcode.KILLOBJ, 3 ).I( Opcode.KILLOBJ, 1 ) );
		Assert.AreEqual( 0, vm.UnimplementedEffects.Count, string.Join( ", ", vm.UnimplementedEffects ) );
		// type 10 is served too: it plays from the object type's own sound list, and a type without one submits nothing (ObjectTypeSoundsTests)
		var rest = Script( placed.Runtime, a => a
			.I( Opcode.ADDOBJ, 10, -1, 43, 1 ).I( Opcode.EVENT, 10, -1, 43 ) );
		Assert.AreEqual( 0, rest.UnimplementedEffects.Count, string.Join( ", ", rest.UnimplementedEffects ) );
		// the light opcodes are served ([EXT:object-lights]): on an object whose model has no light dummy they change nothing and are not counted as unimplemented
		var lights = Script( placed.Runtime, a => a
			.I( Opcode.ENABLELIGHT, 1 ).I( Opcode.DISABLELIGHT, 1 ).I( Opcode.SETLIGHT, 1, 50 ).I( Opcode.COLOURLIGHT, 1, 10, 20, 30 ) );
		Assert.AreEqual( 0, lights.UnimplementedEffects.Count, string.Join( ", ", lights.UnimplementedEffects ) );
		Assert.IsNull( placed.Runtime.Lights );
	}
}
