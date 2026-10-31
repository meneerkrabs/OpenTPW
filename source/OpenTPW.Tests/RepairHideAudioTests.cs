using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class RepairHideAudioTests
{
	private string root = null!; private ObjectCatalogEntry entry = null!; private AudioMixer mixer = null!;
	private List<Entity> oldEntities = null!; private Level? oldLevel; private Logger oldLog = null!;
	private Vector3 oldPosition, oldForward; private SoundEventSystem system = null!;
	private readonly Dictionary<FieldInfo, object?> oldAudio = new();
	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void Set( object target, string property, object value ) => target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );
	private void AudioField( string name, object value ) { var field = typeof( GameAudio ).GetField( name, BindingFlags.Static | BindingFlags.NonPublic )!; oldAudio.Add( field, field.GetValue( null ) ); field.SetValue( null, value ); }
	[TestInitialize]
	public void Setup()
	{
		oldLog = Log; Log = new(); oldEntities = Entity.All; Entity.All = new(); oldLevel = Level.Current; Level.Current = null;
		oldPosition = AudioListener.Position; oldForward = AudioListener.Forward; AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
		root = Directory.CreateTempSubdirectory( "opentpw-repair-hide-" ).FullName;
		var model = Md2ModelFileTests.CreateGeometry();
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x42 ), 1 ); BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x44 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x268 ), 0 ); BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x78 ), 0x264 );
		foreach ( var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( offset ), 0 );
		File.WriteAllBytes( Path.Combine( root, "model.MD2" ), model );
		File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.CMP, 1, 2 ).I( Opcode.REPAIREFFECT, 1 ).I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
		entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/synthetic", new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1100\n", "synthetic.sam" ) } ),
			new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( root ), null );
		mixer = new AudioMixer( null ); system = new SoundEventSystem( mixer, 7 ); var category = Empty<SoundCategory>(); var catalog = Empty<SoundCatalog>();
		SoundEntry Sound( int bank ) => new( 100, 100, 0, 0, 0, 0, 1, 100, new[] { new SoundSample( 1, 65535, 0, (ushort)bank, 0 ) }, Array.Empty<SoundLink>(), Array.Empty<byte>() );
		Set( catalog, "Events", new[] { new SoundEvent( 111, 0, 8, 0, new[] { Sound( 1 ) } ), new SoundEvent( 211, 0, 8, 0, new[] { Sound( 2 ) } ), new SoundEvent( 75, 0, 0, 0, new[] { Sound( 3 ) } ),
			new SoundEvent( 79, 0, 0, 0, new[] { Sound( 4 ) } ), new SoundEvent( 83, 0, 0, 0, new[] { Sound( 5 ) } ), new SoundEvent( 87, 0, 0, 0, new[] { Sound( 6 ) } ) } ); Set( category, "Catalog", catalog );
		var decoded = (Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof( SoundEventSystem ).GetField( "decoded", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( system )!;
		for ( var bank = 1; bank <= 6; bank++ ) decoded[(category, bank, 1)] = Pcm( (short)(bank * 1000) );
		AudioField( "events", system ); AudioField( "<Rides>k__BackingField", category ); AudioField( "<Kids>k__BackingField", category );
	}
	private static Mp2Audio Pcm( short sample ) => new( AudioMixer.OutputRate, 1, new[] { sample, sample, sample, sample }, 1, 0 );
	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var entity in Entity.All.ToArray() ) entity.Delete(); Entity.All = oldEntities; Level.Current = oldLevel; Log = oldLog;
		mixer.Dispose(); foreach ( var pair in oldAudio ) pair.Key.SetValue( null, pair.Value ); oldAudio.Clear(); Directory.Delete( root, true );
		typeof( AudioListener ).GetMethod( "RestoreCaptured", BindingFlags.NonPublic | BindingFlags.Static )!.Invoke( null, new object[] { oldPosition, oldForward } );
	}
	private void Repair( OriginalObjectRuntime owner, int show = 1, int band = 1 )
	{
		using var data = new MemoryStream( new RideVMTests.Asm().I( Opcode.CMP, 1, 2 ).I( Opcode.REPAIREFFECT, show ).I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
		var vm = new RideVM( data, new RideVMOptions { Effects = owner.Script!.Effects } ); vm.Variables[5] = band; vm.Advance( FixedStepClock.TickDuration );
		Assert.AreNotEqual( RideVMState.Faulted, vm.State ); Assert.AreEqual( RideVM.VMFlags.Sign, vm.Flags, "REPAIREFFECT must preserve incoming flags." ); Assert.AreEqual( 0, vm.UnimplementedEffects.Count, "REPAIREFFECT is fully served: its particle and its sounds" ); vm.Stop();
	}
	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ActualPlacedRootHidePairSharesListenerSoundSpaceAndDistanceLaw( bool imported )
	{
		ModelHeightfield? field = null; var origin = new System.Numerics.Vector2( 1, -2 );
		if ( imported ) { field = new ModelHeightfield( 4, 4, 10, 10, 0, 0, Array.Empty<uint>(), new float[25], new uint[16] ); var park = Empty<OriginalPark>(); Set( park, "Heightfield", field ); var level = Empty<Level>(); Set( level, "OriginalPark", park ); Level.Current = level; origin = -OriginalParkPlacement.GetOrigin( field ); }
		var placed = new OriginalObject( entry, new ObjectPlacement( 7, 9, 90, origin, 3 ) );
		GameAudio.UpdateListener( placed.Position.GetSystemVector3(), new System.Numerics.Vector3( 0, 1, 0 ), field );
		placed.Runtime.Simulate( FixedStepClock.TickDuration ); Assert.AreEqual( 0, placed.Script!.UnimplementedEffects.Count ); Assert.AreEqual( RideVM.VMFlags.Sign, placed.Script.Flags );
		Assert.AreEqual( 2, mixer.Voices.Count ); foreach ( var voice in mixer.Voices ) { Assert.AreEqual( AudioListener.Position, voice.Position ); Assert.AreEqual( 100f, voice.Range ); Assert.IsTrue( voice.Loops ); }
		var source = AudioListener.Position; GameAudio.UpdateListener( placed.Position.GetSystemVector3() + new System.Numerics.Vector3( 6, 0, 0 ), new System.Numerics.Vector3( 0, 1, 0 ), field );
		using var reference = new AudioMixer( null ); reference.Play( Pcm( 1000 ), AudioChannel.Effects, 1, position: source, range: 100 ).AsEvent(); reference.Play( Pcm( 2000 ), AudioChannel.Effects, 1, position: source, range: 100 ).AsEvent();
		CollectionAssert.AreEqual( reference.Mix( 1 ), mixer.Mix( 1 ), "Both IDs and the existing pan/distance law must match sound-space submission." );
	}
	private static void Bind( OriginalObjectRuntime owner, Func<Vector3?> origin ) => typeof( OriginalObjectRuntime ).GetMethod( "SetEventOrigin", BindingFlags.NonPublic | BindingFlags.Instance )!.Invoke( owner, new object[] { origin } );
	[TestMethod]
	public void HideSamplesOncePerPairAndInvalidOrStoppedOriginsRetainFlatFallback()
	{
		// the fixture's events 111 and 211 carry flag 0x8, so even a flat pair loops ([BIN:TPW-EXE:0x6bc621]) and an owner with live loops refuses capture until they are stopped
		var owner = new OriginalObjectRuntime( entry ); Repair( owner ); foreach ( var voice in mixer.Voices ) { Assert.IsNull( voice.Position ); Assert.IsTrue( voice.Loops ); }
		Assert.ThrowsException<NotSupportedException>( () => owner.CaptureSnapshot() ); var calls = 0; Vector3? value = null; Bind( owner, () => { calls++; return value; } );
		foreach ( var invalid in new Vector3?[] { null, new Vector3( float.NaN, 0, 0 ), new Vector3( 0, float.PositiveInfinity, 0 ), new Vector3( 0, 0, float.NegativeInfinity ) } )
		{ value = invalid; var before = mixer.Voices.Count; Repair( owner ); foreach ( var voice in mixer.Voices.Skip( before ) ) { Assert.IsNull( voice.Position ); Assert.IsTrue( voice.Loops ); } }
		Assert.AreEqual( 4, calls ); owner.Stop(); Assert.IsNotNull( owner.CaptureSnapshot() ); value = new Vector3( 2, 3, 4 ); Repair( owner ); Assert.AreEqual( 4, calls ); Assert.IsNull( mixer.Voices.Last().Position );
	}
	[TestMethod]
	public void HidePairIsSubmittedAtWholeUnitsTruncatedTowardZero()
	{
		// [BIN:TPW-EXE:0x5562f6] the hide sounds' coordinates go through __ftol (truncation) before 0x51bfc0
		var owner = new OriginalObjectRuntime( entry ); Bind( owner, () => new Vector3( -10.75f, 2.5f, 39.99f ) );
		Repair( owner );
		foreach ( var voice in mixer.Voices ) Assert.AreEqual( new Vector3( -10, 2, 39 ), voice.Position );
		owner.Stop();
	}
	[TestMethod]
	public void ChangingProviderIsSampledPerHidePairAndOldVoicesKeepTheirSnapshot()
	{
		var owner = new OriginalObjectRuntime( entry ); var origin = new Vector3( 10, 0, 20 ); var calls = 0; Bind( owner, () => { calls++; return origin; } );
		Repair( owner ); var first = mixer.Voices.ToArray(); origin = new Vector3( -10, 2, 40 ); Repair( owner ); var second = mixer.Voices.Skip( 2 ).ToArray();
		Assert.AreEqual( 2, calls ); foreach ( var voice in first ) Assert.AreEqual( new Vector3( 10, 0, 20 ), voice.Position ); foreach ( var voice in second ) Assert.AreEqual( origin, voice.Position );
		AudioListener.Update( origin, new Vector3( 0, 0, 1 ) ); Assert.IsTrue( mixer.Mix( 12 ).Any( sample => sample != 0 ) ); Assert.IsTrue( mixer.Voices.All( voice => voice.Loops && !voice.IsFinished ) ); owner.Stop(); Assert.IsTrue( first.Concat( second ).All( voice => voice.IsFinished ) );
	}
	[DataTestMethod]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( -1 )]
	[DataRow( int.MaxValue )]
	public void EveryNonzeroValuePlaysThePairAtTheObjectAndOwnsItsLoops( int show )
	{
		// [BIN:TPW-EXE:0x5562C5] only the value 0 jumps away (je 0x55635F): every other value takes the sounds 0x6F and 0xD3 at the object's position (0x556A80)
		var owner = new OriginalObjectRuntime( entry ); var calls = 0; var place = new Vector3( 4, 5, 6 ); Bind( owner, () => { calls++; return place; } );
		Repair( owner, show ); Assert.AreEqual( 2, mixer.Voices.Count ); Assert.IsTrue( mixer.Voices.All( voice => voice.Position == place && voice.Loops ) ); Assert.AreEqual( 1, calls );
		Assert.ThrowsException<NotSupportedException>( () => owner.CaptureSnapshot() ); owner.Stop(); Assert.IsTrue( mixer.Voices.All( voice => voice.IsFinished ) );
	}
	[TestMethod]
	public void TheValueZeroPlaysTheBandFamilyAtTheObjectWithoutLoopOwnership()
	{
		// [BIN:TPW-EXE:0x55635F] the value 0 submits the cat_kids one-shot of its band at the object's position (0x556A80), as the pair does for the other values
		var owner = new OriginalObjectRuntime( entry ); var calls = 0; var place = new Vector3( 4, 5, 6 ); Bind( owner, () => { calls++; return place; } );
		Repair( owner, 0 ); var voice = mixer.Voices.Single(); Assert.AreEqual( place, voice.Position ); Assert.IsFalse( voice.Loops ); Assert.AreEqual( 1, calls ); Assert.IsNotNull( owner.CaptureSnapshot() );
		using var reference = new AudioMixer( null ); reference.Play( Pcm( 3000 ), AudioChannel.Effects, 1, position: place, range: 100 ).AsEvent(); CollectionAssert.AreEqual( reference.Mix( 1 ), mixer.Mix( 1 ) );
		owner.Stop(); Assert.IsFalse( voice.IsFinished ); mixer.Mix( 4 ); Assert.IsTrue( voice.IsFinished );
	}
	[DataTestMethod]
	[DataRow( 0, 0 )]
	[DataRow( 1, 3 )]
	[DataRow( 2, 4 )]
	[DataRow( 3, 4 )]
	[DataRow( -5, 4 )]
	[DataRow( 4, 5 )]
	[DataRow( 7, 5 )]
	[DataRow( 8, 6 )]
	[DataRow( 100, 6 )]
	public void TheBandFamilyFollowsTheScriptsVariableSlot5( int band, int expectedBank )
	{
		// [BIN:TPW-EXE:0x551320] band 0 is silent, 1 picks event 75, 2-3 (and negative) 79, 4-7 83 and 8 up 87; the band is [[vm+0x1c]+0x14], variable slot 5 (0x55637a)
		var owner = new OriginalObjectRuntime( entry ); var place = new Vector3( 4, 5, 6 ); Bind( owner, () => place );
		Repair( owner, 0, band );
		if ( expectedBank == 0 ) { Assert.AreEqual( 0, mixer.Voices.Count ); return; }
		Assert.AreEqual( 1, mixer.Voices.Count );
		using var reference = new AudioMixer( null ); reference.Play( Pcm( (short)(expectedBank * 1000) ), AudioChannel.Effects, 1, position: place, range: 100 ).AsEvent();
		CollectionAssert.AreEqual( reference.Mix( 1 ), mixer.Mix( 1 ) );
	}
	[TestMethod]
	public void HideLoopsRefuseCaptureUntilFinishedAndRootFaultStopsOnlyTheirOwner()
	{
		var first = new OriginalObject( entry, new ObjectPlacement( 1, 2, 0, System.Numerics.Vector2.Zero, 0 ) ); var other = new OriginalObject( entry, new ObjectPlacement( 3, 4, 0, System.Numerics.Vector2.Zero, 0 ) );
		first.Runtime.Simulate( FixedStepClock.TickDuration ); var owned = mixer.Voices.ToArray(); other.Runtime.Simulate( FixedStepClock.TickDuration ); var others = mixer.Voices.Skip( 2 ).ToArray();
		var state = first.Runtime.State; var output = Path.Combine( root, "previous.json" ); File.WriteAllText( output, "previous" );
		StringAssert.Contains( Assert.ThrowsException<NotSupportedException>( () => { var snapshot = first.Runtime.CaptureSnapshot(); File.WriteAllText( output, snapshot.EntryArchivePath ); } ).Message, "repair-hide" ); Assert.AreEqual( state, first.Runtime.State ); Assert.AreEqual( "previous", File.ReadAllText( output ) );
		first.Script!.Abort( "Synthetic root fault" ); first.Runtime.Simulate( FixedStepClock.TickDuration ); Assert.IsTrue( owned.All( voice => voice.IsFinished ) ); Assert.IsTrue( others.All( voice => !voice.IsFinished ) ); Assert.IsNotNull( first.Runtime.CaptureSnapshot() );
		Assert.ThrowsException<NotSupportedException>( () => other.Runtime.CaptureSnapshot() ); AudioListener.Update( others[0].Position!.Value, new Vector3( 0, 0, 1 ) ); Assert.IsTrue( mixer.Mix( 1 ).Any( sample => sample != 0 ) );
		foreach ( var voice in others ) voice.Stop(); Assert.IsNotNull( other.Runtime.CaptureSnapshot() ); Repair( other.Runtime ); other.Delete(); Assert.IsTrue( mixer.Voices.All( voice => voice.IsFinished ) );
	}
	[TestMethod]
	public void OneShotsAndMissingAudioRemainCaptureEligibleAndPublicHideApiStaysFlat()
	{
		// events 111 and 211 carry flag 0x8: a flat one-shot loops as a positioned one does ([BIN:TPW-EXE:0x6bc621])
		GameAudio.PlayRepairEffect( 1 ); Assert.AreEqual( 2, mixer.Voices.Count ); Assert.IsTrue( mixer.Voices.All( voice => voice.Position == null && voice.Loops ) );
		foreach ( var voice in mixer.Voices.ToArray() ) voice.Stop();
		mixer.Mix( 4 ); typeof( GameAudio ).GetField( "events", BindingFlags.Static | BindingFlags.NonPublic )!.SetValue( null, null );
		var placed = new OriginalObject( entry, new ObjectPlacement( 1, 2, 0, System.Numerics.Vector2.Zero, 0 ) ); placed.Runtime.Simulate( FixedStepClock.TickDuration ); Assert.AreEqual( 0, mixer.Voices.Count ); Assert.IsNotNull( placed.Runtime.CaptureSnapshot() );
	}

	[TestMethod]
	public void PositionedHideOneShotsKeepTheirLifetimeAndDoNotBlockCapture()
	{
		var catalog = GameAudio.Rides!.Catalog; Set( catalog, "Events", catalog.Events.Select( sound => sound with { Flags = 0 } ).ToArray() );
		var placed = new OriginalObject( entry, new ObjectPlacement( 1, 2, 0, System.Numerics.Vector2.Zero, 0 ) ); placed.Runtime.Simulate( FixedStepClock.TickDuration );
		var voices = mixer.Voices.ToArray(); Assert.AreEqual( 2, voices.Length ); Assert.IsTrue( voices.All( voice => voice.Position != null && !voice.Loops ) ); Assert.IsNotNull( placed.Runtime.CaptureSnapshot() );
		placed.Delete(); Assert.IsTrue( voices.All( voice => !voice.IsFinished ) ); mixer.Mix( 4 ); Assert.IsTrue( voices.All( voice => voice.IsFinished ) );
	}
	[DataTestMethod]
	[DataRow( 111 )]
	[DataRow( 211 )]
	public void MissingOneHideEventDoesNotPreventOtherVoiceOwnership( int missing )
	{
		var catalog = GameAudio.Rides!.Catalog; Set( catalog, "Events", catalog.Events.Where( sound => sound.Id != (uint)missing ).ToArray() );
		var placed = new OriginalObject( entry, new ObjectPlacement( 1, 2, 0, System.Numerics.Vector2.Zero, 0 ) ); placed.Runtime.Simulate( FixedStepClock.TickDuration );
		var voice = mixer.Voices.Single(); Assert.IsTrue( voice.Loops ); Assert.IsNotNull( voice.Position ); Assert.ThrowsException<NotSupportedException>( () => placed.Runtime.CaptureSnapshot() );
		placed.Delete(); Assert.IsTrue( voice.IsFinished ); Assert.IsNotNull( placed.Runtime.CaptureSnapshot() );
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void SecondSubmissionFailureCannotOrphanFirstHideLoopOrStopOtherOwner( bool looped )
	{
		var other = new OriginalObject( entry, new ObjectPlacement( 3, 4, 0, System.Numerics.Vector2.Zero, 0 ) ); other.Runtime.Simulate( FixedStepClock.TickDuration ); var otherVoices = mixer.Voices.ToArray();
		var catalog = GameAudio.Rides!.Catalog; if ( !looped ) Set( catalog, "Events", catalog.Events.Select( sound => sound.Id == 111 ? sound with { Flags = 0 } : sound ).ToArray() ); Set( catalog, "Events", catalog.Events.Select( sound => sound.Id == 211 ? sound with { Sounds = sound.Sounds.Select( sample => sample with { Rolloff = float.NaN } ).ToArray() } : sound ).ToArray() );
		var failed = new OriginalObject( entry, new ObjectPlacement( 1, 2, 0, System.Numerics.Vector2.Zero, 0 ) );
		var error = Assert.ThrowsException<ArgumentOutOfRangeException>( () => failed.Runtime.Simulate( FixedStepClock.TickDuration ) ); Assert.AreEqual( "range", error.ParamName ); var partial = mixer.Voices.Last(); Assert.AreEqual( looped, partial.Loops ); Assert.AreEqual( looped, partial.IsFinished ); failed.Script!.Abort( "Synthetic root containment after submission failure" ); failed.Runtime.Simulate( FixedStepClock.TickDuration );
		Assert.AreEqual( looped, partial.IsFinished, "Only a looping first voice belongs to this pair cleanup." ); Assert.IsTrue( otherVoices.All( voice => !voice.IsFinished ) ); Assert.IsNotNull( failed.Runtime.CaptureSnapshot() ); Assert.ThrowsException<NotSupportedException>( () => other.Runtime.CaptureSnapshot() ); AudioListener.Update( otherVoices[0].Position!.Value, new Vector3( 0, 0, 1 ) ); Assert.IsTrue( mixer.Mix( 1 ).Any( sample => sample != 0 ) );
	}
	[TestMethod]
	public void CurrentPlacedOriginIsReadAgainWithoutMovingAlreadySubmittedLoops()
	{
		var placed = new OriginalObject( entry, new ObjectPlacement( 1, 2, 0, System.Numerics.Vector2.Zero, 0 ) );
		Repair( placed.Runtime ); var before = mixer.Voices.ToArray(); var original = before[0].Position;
		var changed = new ObjectPlacement( 5, 6, 90, new System.Numerics.Vector2( 2, -3 ), 4 );
		// A CPU seam changes the current placement record; this is not a claim about a UI move tool.
		Set( placed, "Placement", changed ); Repair( placed.Runtime );
		var expected = GameAudio.SoundPosition( System.Numerics.Vector3.Transform( System.Numerics.Vector3.Zero, changed.ModelToEngine ) );
		Assert.AreNotEqual( original, expected );
		foreach ( var voice in before ) Assert.AreEqual( original, voice.Position );
		foreach ( var voice in mixer.Voices.Skip( 2 ) ) Assert.AreEqual( expected, voice.Position );
		placed.Delete(); Assert.IsTrue( mixer.Voices.All( voice => voice.IsFinished ) );
	}
	[TestMethod]
	public void OutsideRangeIsSilentButItsOwnedLoopsStillRefuseCapture()
	{
		var owner = new OriginalObjectRuntime( entry ); Bind( owner, () => new Vector3( 1000, 0, 0 ) ); Repair( owner );
		var outside = mixer.Voices.ToArray(); Assert.AreEqual( 2, outside.Length );
		Assert.IsTrue( outside.All( voice => voice.Loops && !voice.IsFinished ) );
		Assert.IsTrue( mixer.Mix( 8 ).All( sample => sample == 0 ) );
		Assert.ThrowsException<NotSupportedException>( () => owner.CaptureSnapshot() );
		var other = new OriginalObjectRuntime( entry ); Bind( other, () => Vector3.Zero ); Repair( other ); var audible = mixer.Voices.Skip( 2 ).ToArray();
		owner.Stop(); Assert.IsTrue( outside.All( voice => voice.IsFinished ) ); Assert.IsTrue( audible.All( voice => !voice.IsFinished ) );
		Assert.IsTrue( mixer.Mix( 1 ).Any( sample => sample != 0 ) ); other.Stop();
	}
	[TestMethod]
	public void HideUsesGlobalRideCatalogueDespiteMatchingLevelEventIds()
	{
		var decoy = Empty<SoundCategory>(); var catalog = Empty<SoundCatalog>();
		Set( catalog, "Events", GameAudio.Rides!.Catalog.Events ); Set( decoy, "Catalog", catalog ); AudioField( "<LevelRides>k__BackingField", decoy );
		var decoded = (Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof( SoundEventSystem ).GetField( "decoded", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( system )!;
		decoded[(decoy, 1, 1)] = Pcm( 9000 ); decoded[(decoy, 2, 1)] = Pcm( 9000 );
		var owner = new OriginalObjectRuntime( entry ); Bind( owner, () => Vector3.Zero ); Repair( owner );
		using var reference = new AudioMixer( null ); reference.Play( Pcm( 1000 ), AudioChannel.Effects, 1, Vector3.Zero, 100 ).AsEvent(); reference.Play( Pcm( 2000 ), AudioChannel.Effects, 1, Vector3.Zero, 100 ).AsEvent();
		CollectionAssert.AreEqual( reference.Mix( 1 ), mixer.Mix( 1 ) ); owner.Stop();
	}
	[TestMethod]
	public void ActualChildHideInheritsPlacedOriginAndObjectLoopOwnership()
	{
		File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
		File.WriteAllBytes( Path.Combine( root, "child.RSE" ), new RideVMTests.Asm().I( Opcode.REPAIREFFECT, 1 ).I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
		var childEntry = new ObjectCatalogEntry( entry.Theme, entry.Category, entry.ArchivePath, entry.Settings, entry.Shape, entry.ModelPath, null, entry.ScriptPath, new[] { "/child.RSE" }, entry.Animations, entry.AuxiliaryModels, entry.Models, entry.FileSystem, null );
		var placed = new OriginalObject( childEntry, new ObjectPlacement( 1, 2, 0, System.Numerics.Vector2.Zero, 0 ) );
		placed.Script!.SpawnChild( "child.RSE", sound: false ); placed.Runtime.Simulate( FixedStepClock.TickDuration );
		Assert.AreSame( placed.Script.Effects, placed.Script.Child!.Effects ); Assert.AreEqual( 0, placed.Script.Child.UnimplementedEffects.Count );
		Assert.AreEqual( 2, mixer.Voices.Count ); Assert.IsTrue( mixer.Voices.All( voice => voice.Position == new Vector3( 10, 0, 20 ) && voice.Loops ) );
		Assert.ThrowsException<NotSupportedException>( () => placed.Runtime.CaptureSnapshot() ); placed.Delete(); Assert.IsTrue( mixer.Voices.All( voice => voice.IsFinished ) );
	}

}
