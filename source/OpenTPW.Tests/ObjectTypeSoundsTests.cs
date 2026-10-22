using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// The object type's own sound list and the ride-script sound type 10 that plays from it: tp.exe's type loader registers the archive's <c>cat_ride</c>
/// category when <c>cat_rideBANK.map</c>, <c>cat_rideSFX.map</c> and <c>BankHD.sdt</c> are all there (0x41F420, 0x41408D, kept at type +0x4F0), a scoped
/// particle template numbered 5 takes that slot afterwards (0x414420), and the object dispatcher's case 10 (0x557686) submits from it like types 3-9, or logs
/// and submits nothing when the type has none. No shipped script uses type 10, so the scripts here are synthetic.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ObjectTypeSoundsTests
{
	private string root = null!;
	private List<Entity> previousEntities = null!;
	private readonly Dictionary<FieldInfo, object?> savedAudio = new();
	private readonly List<RideVM> scripts = new();
	private AudioMixer mixer = null!;
	private SoundEventSystem system = null!;

	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );

	[TestInitialize]
	public void Setup()
	{
		Log ??= new();
		previousEntities = Entity.All;
		Entity.All = new();
		root = Directory.CreateTempSubdirectory( "opentpw-type-sounds-" ).FullName;
		// the placeable model of HeldSoundTests (a node and no skin, so a placed object draws without textures)
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
		mixer = new AudioMixer( null );
		system = new SoundEventSystem( mixer, 7 );
		Audio( "events", system );
		// the running game's categories know none of the own list's events: a type-10 sound can only come from the type
		var empty = Empty<SoundCategory>();
		var catalog = Empty<SoundCatalog>();
		typeof( SoundCatalog ).GetField( "<Events>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( catalog, Array.Empty<SoundEvent>() );
		typeof( SoundCategory ).GetField( "<Catalog>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( empty, catalog );
		foreach ( var property in new[] { "LevelRides", "LevelAmbient", "Rides", "Kids", "Staff", "Ambient", "Ui" } )
			Audio( "<" + property + ">k__BackingField", empty );
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
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
	}

	private static byte[] BankMap( params string[] banks )
	{
		var bytes = new List<byte>( new byte[24] );
		bytes.AddRange( BitConverter.GetBytes( (uint)banks.Length ) );
		foreach ( var _ in banks )
			bytes.AddRange( new byte[11] );
		foreach ( var name in banks )
		{
			var text = Encoding.Latin1.GetBytes( name );
			bytes.AddRange( BitConverter.GetBytes( (uint)text.Length ) );
			bytes.AddRange( text );
		}
		return bytes.ToArray();
	}

	/// <summary>An archive folder <c>/name</c> with the given members of the own sound list (the catalogue holds event 31, a one-shot of samples 10 and 11 in bank 1, and event 2).</summary>
	private ObjectCatalogEntry Archive( string name, IEnumerable<string> members, params string[] extra )
	{
		var folder = Path.Combine( root, name );
		Directory.CreateDirectory( folder );
		foreach ( var member in members )
		{
			File.WriteAllBytes( Path.Combine( folder, member ), member switch
			{
				"cat_rideSFX.map" => AudioTests.CreateCatalog(),
				"cat_rideBANK.map" => BankMap( "Speaker\\Bank" ),
				_ => new byte[] { 1, 2, 3, 4 },
			} );
		}
		foreach ( var member in extra )
			File.WriteAllBytes( Path.Combine( folder, member ), new byte[] { 0 } );
		return new ObjectCatalogEntry( "jungle", ObjectCategory.Feature, "/" + name, new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1417\n", "synthetic.sam" ) } ),
			new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( root ), null );
	}

	private ObjectCatalogEntry WithOwnSounds( string name = "speaker" )
	{
		var entry = Archive( name, ObjectTypeSounds.RequiredFiles );
		var own = entry.OwnSounds!;
		var decoded = (Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof( SoundEventSystem ).GetField( "decoded", BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( system )!;
		foreach ( var sample in new uint[] { 1, 10, 11 } )
			decoded[(own, 1, sample)] = new Mp2Audio( AudioMixer.OutputRate, 1, Enumerable.Repeat( (short)1000, 22050 ).ToArray(), 1, 0 );
		return entry;
	}

	private RideVM Script( OriginalObjectRuntime runtime, Func<RideVMTests.Asm, RideVMTests.Asm> body )
	{
		var stream = new MemoryStream( body( new RideVMTests.Asm() ).I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
		var vm = new RideVM( stream, new RideVMOptions { Effects = runtime.Script!.Effects, World = runtime.Script.World } );
		vm.Advance( FixedStepClock.TickDuration );
		Assert.AreNotEqual( RideVMState.Faulted, vm.State, vm.FaultMessage );
		scripts.Add( vm );
		return vm;
	}

	[TestMethod]
	public void TheTypeLoaderRegistersTheOwnListOnlyWhenAllThreeMembersAreThere()
	{
		// [BIN:TPW-EXE:0x41F420] cat_rideBANK.map, cat_rideSFX.map and BankHD.sdt must all be found; [BIN:TPW-EXE:0x41408D] then "cat_ride" is registered from the archive's folder
		var own = Archive( "complete", ObjectTypeSounds.RequiredFiles ).OwnSounds;
		Assert.IsNotNull( own );
		Assert.AreEqual( "cat_ride", own.Name );
		CollectionAssert.AreEquivalent( new uint[] { 31, 2 }, own.Catalog.Events.Select( item => item.Id ).ToArray() );
		foreach ( var missing in ObjectTypeSounds.RequiredFiles )
			Assert.IsNull( Archive( "without-" + missing.Replace( '.', '-' ), ObjectTypeSounds.RequiredFiles.Where( name => name != missing ) ).OwnSounds, $"no own list without {missing}" );
		Assert.IsNull( Archive( "nothing", Array.Empty<string>() ).OwnSounds );
		var entry = Archive( "once", ObjectTypeSounds.RequiredFiles );
		Assert.AreSame( entry.OwnSounds, entry.OwnSounds, "the type loads its list once" );
	}

	[TestMethod]
	public void AScopedParticleTemplateNumberedFiveTakesTheSlot()
	{
		// [BIN:TPW-EXE:0x414420] every *.emt member is stored at +0x4DC + 4 x atoi(the last character of its stem), after the own list (0x4140BC): a stem ending in 5 lands on +0x4F0
		Assert.IsNull( Archive( "five", ObjectTypeSounds.RequiredFiles, "Ptcl5.emt" ).OwnSounds );
		Assert.IsNull( Archive( "fifteen", ObjectTypeSounds.RequiredFiles, "ptcl15.EMT" ).OwnSounds, "only the last character counts, and the extension is compared without case" );
		Assert.IsNotNull( Archive( "others", ObjectTypeSounds.RequiredFiles, "Ptcl1.emt", "Ptcl4.emt", "Ptcl6.emt", "Ptcl5.txt" ).OwnSounds );
	}

	[TestMethod]
	public void EventTypeTenPlaysFromTheTypesOwnListAtTheKeysPlace()
	{
		// [BIN:TPW-EXE:0x557686 EVENT type 10] the type's own category, the key resolved as for types 3-9, 0x51BFC0 at the place truncated to whole units
		var placed = new OriginalObject( WithOwnSounds(), new ObjectPlacement( 3, 4, 0, new N.Vector2( -7, 11 ), 9 ) );
		var vm = Script( placed.Runtime, a => a.I( Opcode.EVENT, 10, -1, 31 ) );
		var voice = mixer.Voices.Single();
		Assert.AreEqual( OriginalObjectRuntime.WholeUnits( placed.Runtime.ReadFootprintMiddle() ), voice.Position, "key -1: the footprint middle" );
		Assert.AreEqual( 0, vm.UnimplementedEffects.Count, "type 10 is no longer an unimplemented effect" );
		// the same id through a category type finds nothing: the running game's categories do not hold the type's events, and the type's list is not a fallback for them
		Script( placed.Runtime, a => a.I( Opcode.EVENT, 3, -1, 31 ).I( Opcode.EVENT, 5, -1, 31 ) );
		Assert.AreEqual( 1, mixer.Voices.Count );
		// an id the own list lacks plays nothing
		Script( placed.Runtime, a => a.I( Opcode.EVENT, 10, -1, 99 ) );
		Assert.AreEqual( 1, mixer.Voices.Count );
		Assert.AreSame( placed.Entry.OwnSounds, GameAudio.FindRseEvent( 10, 31, null, placed.Entry.OwnSounds )!.Value.Category );
		Assert.IsNull( GameAudio.FindRseEvent( 10, 31 ), "type 10 has no category of the game's own" );
	}

	[TestMethod]
	public void AddObjTypeTenKeepsItsSoundUnderTheSlotUntilKillObj()
	{
		// [BIN:TPW-EXE:0x557970 ADDOBJ record] case 10's handle is kept like those of types 3-9; [BIN:TPW-EXE:0x5522C5 KILLOBJ] types 3-10 end it
		var placed = new OriginalObject( WithOwnSounds(), new ObjectPlacement( 3, 4, 0, new N.Vector2( -7, 11 ), 9 ) );
		// event 2 is a sentence the record keeps; event 31 carries the queued flag 1 (the submit returns the prior handle, 0 here, as for types 3-9) and 99 is not in the list: neither keeps a record
		var vm = Script( placed.Runtime, a => a.I( Opcode.ADDOBJ, 10, -1, 2, 4 ).I( Opcode.ADDOBJ, 10, -1, 31, 5 ).I( Opcode.ADDOBJ, 10, -1, 99, 6 ).I( Opcode.ENDSLICE ).I( Opcode.KILLOBJ, 4 ) );
		var record = placed.Runtime.HeldSounds.Single();
		Assert.AreEqual( (vm.ScriptId, 4, 10, -1), (record.ScriptId, record.Slot, record.Type, record.Key) );
		Assert.IsNotNull( record.Sound.Sentence );
		Assert.AreEqual( 0, vm.UnimplementedEffects.Count );
		vm.Advance( FixedStepClock.TickDuration );
		Assert.AreEqual( 0, placed.Runtime.HeldSounds.Count );
		Assert.IsTrue( record.Sound.IsFinished );
	}

	[TestMethod]
	public void WithoutAnOwnListTypeTenSubmitsNothingBeforeTheKeyIsLookedAt()
	{
		// [BIN:TPW-EXE:0x557686] a type without the list logs 0x765E18 and returns 0 before 0x556B90 resolves the key
		var runtime = new OriginalObjectRuntime( Archive( "plain", Array.Empty<string>() ) );
		var footprints = 0;
		runtime.SetEventAnchors( () => { footprints++; return new Vector3( 1, 2, 3 ); }, _ => Vector3.Zero );
		var vm = Script( runtime, a => a.I( Opcode.EVENT, 10, -1, 31 ).I( Opcode.ADDOBJ, 10, -1, 31, 1 ).I( Opcode.ADDOBJ_EXT, 10, -1, 31, 1000, 2 ) );
		Assert.AreEqual( 0, footprints, "the key is not resolved" );
		Assert.AreEqual( 0, mixer.Voices.Count );
		Assert.AreEqual( 0, runtime.HeldSounds.Count );
		Assert.AreEqual( 0, vm.UnimplementedEffects.Count, "a refused submit is not an unimplemented effect" );
		// a type with the list resolves the key for the same instruction
		var withList = new OriginalObjectRuntime( WithOwnSounds( "listed" ) );
		withList.SetEventAnchors( () => { footprints++; return new Vector3( 1, 2, 3 ); }, _ => Vector3.Zero );
		Script( withList, a => a.I( Opcode.EVENT, 10, -1, 31 ) );
		Assert.AreEqual( 1, footprints );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), mixer.Voices.Single().Position );
		runtime.Stop();
		withList.Stop();
	}

	/// <summary>
	/// With the original data: Jungle's loudspeaker (<c>features/speaker1</c>, Info.Id 1417) is the one shipped type whose archive carries its own sound list; its
	/// <c>cat_ride</c> holds events 2-5 and names its bank <c>Speaker\Bank</c>. Its script plays sounds of type 4 only. Skips without <c>OPENTPW_GAME_PATH</c>.
	/// </summary>
	[TestMethod]
	public void JungleLoudspeakerIsTheOnlyShippedTypeWithItsOwnSoundList()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game." );
		try
		{
			var bonus = ObjectCatalog.BonusDataRoot;
			ObjectCatalog.BonusDataRoot = null;
			try
			{
				var listed = ObjectCatalog.Themes.SelectMany( theme => ObjectCatalog.Load( theme ).Entries ).Where( entry => entry.OwnSounds != null ).ToList();
				Assert.AreEqual( "/levels/jungle/features/speaker1", listed.Single().ArchivePath );
				var speaker = listed.Single();
				Assert.AreEqual( 1417, speaker.InfoId );
				var own = speaker.OwnSounds!;
				Assert.AreEqual( "cat_ride", own.Name );
				CollectionAssert.AreEqual( new uint[] { 2, 3, 4, 5 }, own.Catalog.Events.Select( item => item.Id ).OrderBy( id => id ).ToArray() );
				CollectionAssert.AreEqual( new[] { "Speaker\\Bank" }, own.BankNames.ToArray() );
				Assert.AreSame( own, GameAudio.FindRseEvent( 10, 3, null, own )!.Value.Category, "a type-10 sound of the loudspeaker's script would find event 3 in its own list" );
				// its script uses type 4 (the level's ambient) for its sound, never type 10
				RideScriptFile script;
				using ( var stream = speaker.FileSystem.OpenRead( speaker.ScriptPath! ) )
					script = new RideScriptFile( stream );
				var effects = script.Instructions.Where( item => item.Opcode is (ushort)Opcode.EVENT or (ushort)Opcode.ADDOBJ or (ushort)Opcode.EVENT_EXT or (ushort)Opcode.ADDOBJ_EXT ).ToList();
				CollectionAssert.AreEqual( new[] { 4 }, effects.Where( item => item.Opcode == (ushort)Opcode.ADDOBJ ).Select( item => (int)item.Operands[0].Value ).ToArray() );
				Assert.IsFalse( effects.Any( item => item.Operands[0].Value == 10 ), "no type-10 sound in the loudspeaker's script" );
			}
			finally
			{
				ObjectCatalog.BonusDataRoot = bonus;
			}
		}
		finally
		{
			FileSystem = previous;
		}
	}
}
