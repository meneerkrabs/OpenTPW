using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>Synthetic catalog-backed ordinary RSSE graphs. These are not original saved-child corpus evidence.</summary>
[TestClass]
[DoNotParallelize]
public class SavedScriptGraphImportTests
{
	private GameEdition previousEdition;
	[TestInitialize]
	public void Initialize() { previousEdition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkWorld ); }
	[TestCleanup]
	public void Cleanup() => GameEditions.Select( previousEdition );

	private const uint Clock = 185660000;
	private const string Folder = "/data/levels/jungle/Features/test/";
	private static void Put( byte[] bytes, int offset, uint value ) => BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( offset ), value );
	private static byte[] Words( params int[] values ) => values.SelectMany( BitConverter.GetBytes ).ToArray();

	private sealed class Fixture : IDisposable
	{
		private readonly string directory = Path.Combine( Path.GetTempPath(), "opentpw-rsse-graph-" + Guid.NewGuid().ToString( "N" ) );
		public RideScriptWorld World { get; } = new( 7 );
		public OriginalObjectRuntime Runtime { get; }
		public ObjectCatalogEntry Entry { get; }
		public RideVM Root => Runtime.Script!;
		public List<string> Members { get; } = new();
		public byte[][] Programs { get; }
		public SaveRsseRecord[] Records { get; set; }
		public SavedScriptImport.Candidate[] Candidates => new[] { new SavedScriptImport.Candidate( 11, Entry, Root, Runtime ) };
		public SaveRsseBlock Block => new( 1, 97036, 30, (uint)Records.Length, 0, Records, 0 );

		public Fixture( bool waits = false, int? seed = null, bool randomProgram = false )
		{
			Directory.CreateDirectory( directory );
			var geometry = Md2ModelFileTests.CreateGeometry();
			BinaryPrimitives.WriteUInt16LittleEndian( geometry.AsSpan( 0x42 ), 1 );
			BinaryPrimitives.WriteUInt16LittleEndian( geometry.AsSpan( 0x44 ), 0 );
			Put( geometry, 0x268, 0 ); Put( geometry, 0x78, 0x264 );
			foreach ( var at in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) BinaryPrimitives.WriteUInt16LittleEndian( geometry.AsSpan( at ), 0 );
			Write( "/model.md2", geometry );
			Programs = Enumerable.Range( 1, 3 ).Select( increment =>
			{
				var asm = new Asm();
				if ( waits ) asm.I( Opcode.WAIT, 500 );
				asm.Label( "loop" );
				if ( randomProgram ) asm.I( Opcode.RAND, V( 1 ), 32767 );
				return asm.I( Opcode.ADD, V( 0 ), increment ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" )
					.Build( new[] { "V0", "V1", "V2", "V3" }, "", "identity" + increment, "" );
			} ).ToArray();
			for ( var index = 0; index < Programs.Length; index++ ) AddMember( new[] { "root.rse", "child.rse", "grand.rse" }[index], Programs[index] );
			var settings = new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1100\nInfo.Name Graph\nInfo.IsChoosable 1" ) } );
			Entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Feature, "/test.wad", settings, new ObjectShape( new[] { "*" } ),
				"/model.md2", null, Members[0], Members, Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( directory ), null );
			Runtime = new OriginalObjectRuntime( Entry, World, seed: seed, open: true );
			World.RestoreRandomState( 0x12345678 );
			// File order and IDs are deliberately neither topology nor ID order: grandchild, root, child.
			Records = new[] { Record( Programs[2], 19, 3, 0 ), Record( Programs[0], 12, 0, 3 ), Record( Programs[1], 3, 12, 19 ) };
		}

		public void Write( string path, byte[] bytes )
		{
			var destination = Path.Combine( directory, path.TrimStart( '/' ) );
			Directory.CreateDirectory( Path.GetDirectoryName( destination )! );
			File.WriteAllBytes( destination, bytes );
		}
		public void AddMember( string name, byte[] bytes ) { var path = Folder + name; Write( path, bytes ); Members.Add( path ); }
		public IReadOnlyList<string> Apply( SaveRsseBlock? block = null, uint? clock = Clock ) => SavedScriptImport.Apply( World, block ?? Block, Candidates, clock );
		public void Dispose() { Runtime.Stop(); Directory.Delete( directory, true ); }
	}

	private static SaveRsseRecord Record( byte[] rse, uint id, uint parent, uint child, uint sound = 0, uint thing = 11, uint mesh = 110 )
	{
		var script = new RideScriptFile( new MemoryStream( rse ) );
		var raw = new byte[SaveRsseRecord.Bytes];
		Put( raw, 0x08, id ); Put( raw, 0x0C, child ); Put( raw, 0x10, parent ); Put( raw, 0x14, sound );
		Put( raw, 0x40, (uint)script.StackSize - 1 ); Put( raw, 0x50, (uint)script.CodeWordCount );
		Put( raw, 0x54, (uint)script.StackSize ); Put( raw, 0x58, (uint)script.LimboSize );
		Put( raw, 0x64, (uint)script.BounceSize ); Put( raw, 0x7C, (uint)script.WalkSize );
		Put( raw, 0x8C, (uint)script.VariableCount ); Put( raw, 0x90, (uint)script.StringBlobLength );
		Put( raw, 0x74, uint.MaxValue ); Put( raw, 0x94, (uint)script.TimeSlice ); Put( raw, 0x98, uint.MaxValue ); Put( raw, 0xA8, 0xFFFF );
		Put( raw, 0xAC, thing ); Put( raw, 0xC0, 50 ); Put( raw, 0xC8, mesh ); raw[0xE6] = raw[0xE7] = 0xFF;
		return new SaveRsseRecord
		{
			Raw = raw, Code = rse.AsSpan( 52, script.CodeWordCount * 4 ).ToArray(),
			Strings = rse.AsSpan( 56 + script.CodeWordCount * 4, script.StringBlobLength ).ToArray(),
			Stack = new byte[script.StackSize * 4], Variables = new byte[script.VariableCount * 4], Limbo = new byte[script.LimboSize * SaveRsseRecord.LimboEntryBytes],
			Bounce = new byte[script.BounceSize * SaveRsseRecord.BounceEntryBytes], Walk = new byte[script.WalkSize * SaveRsseRecord.WalkEntryBytes], Nodes = Array.Empty<byte>(),
			Folder = Folder.TrimStart( '/' ).Replace( '/', '\\' ), Objects = Array.Empty<SaveRsseObject>()
		};
	}

	private static void AssertRefusedUnchanged( Fixture fixture, SaveRsseBlock? block = null, uint? clock = Clock )
	{
		var world = ParkResumeJson.Serialize( fixture.World.CaptureSnapshot() );
		var root = ParkResumeJson.Serialize( fixture.Root.CaptureSnapshot() );
		var runtime = ParkResumeJson.Serialize( fixture.Runtime.CaptureSnapshot() );
		var list = fixture.World.ListOrder.ToArray();
		var report = fixture.Apply( block, clock );
		Assert.IsTrue( report.Count > 0, "a refused graph must explain why" );
		CollectionAssert.AreEqual( world, ParkResumeJson.Serialize( fixture.World.CaptureSnapshot() ), string.Join( "; ", report ) );
		CollectionAssert.AreEqual( root, ParkResumeJson.Serialize( fixture.Root.CaptureSnapshot() ) );
		CollectionAssert.AreEqual( runtime, ParkResumeJson.Serialize( fixture.Runtime.CaptureSnapshot() ) );
		CollectionAssert.AreEqual( list, fixture.World.ListOrder.ToArray() );
	}

	// ---- Sound children (SPAWNSOUND 0x005551AE) ----

	[TestMethod]
	public void ASoundChildIsRestoredBesideTheOrdinaryChain()
	{
		using var f = new Fixture();
		// root 12 spawned the ordinary child 3 and the sound child 19; the sound script keeps the thing id 0 and mesh instance 0 it was created with (0x00558D2B..0x00558D68)
		f.Records = new[] { Record( f.Programs[2], 19, 12, 0, thing: 0, mesh: 0 ), Record( f.Programs[1], 3, 12, 0 ), Record( f.Programs[0], 12, 0, 3, sound: 19 ) };
		var report = f.Apply();
		Assert.AreEqual( 3, f.World.Scripts.Count, string.Join( "; ", report ) );
		StringAssert.StartsWith( report[0], "RSSE child-script graph imported: 3 scripts" );
		Assert.AreEqual( 3, f.Root.Child!.ScriptId );
		Assert.AreEqual( 19, f.Root.SoundChild!.ScriptId );
		Assert.AreSame( f.Root, f.Root.SoundChild.Parent );
		Assert.IsNull( f.Root.Child.Child );
		Assert.AreEqual( 12, f.Root.ScriptId );
	}

	[TestMethod]
	public void ASoundChildThatTakesTheParentsThingOrAMeshIsRefusedUnchanged()
	{
		using var f = new Fixture();
		f.Records = new[] { Record( f.Programs[2], 19, 12, 0 ), Record( f.Programs[1], 3, 12, 0 ), Record( f.Programs[0], 12, 0, 3, sound: 19 ) };
		AssertRefusedUnchanged( f );
		using var g = new Fixture();
		g.Records = new[] { Record( g.Programs[2], 19, 12, 0, thing: 0, mesh: 110 ), Record( g.Programs[1], 3, 12, 0 ), Record( g.Programs[0], 12, 0, 3, sound: 19 ) };
		AssertRefusedUnchanged( g );
	}

	[TestMethod]
	public void ASoundLinkTheSoundChildDoesNotAnswerIsRefusedUnchanged()
	{
		using var f = new Fixture();
		f.Records = new[] { Record( f.Programs[2], 19, 4, 0, thing: 0, mesh: 0 ), Record( f.Programs[0], 12, 0, 0, sound: 19 ) };
		AssertRefusedUnchanged( f );
		using var same = new Fixture();
		// one record cannot be both the ordinary and the sound child
		same.Records = new[] { Record( same.Programs[1], 3, 12, 0 ), Record( same.Programs[0], 12, 0, 3, sound: 3 ) };
		AssertRefusedUnchanged( same );
	}

	// ---- What the records hold beyond the run state ----

	[TestMethod]
	public void TheRootsRiderTablesKeptEffectsAndRepairEffectComeWithTheGraph()
	{
		using var f = new Fixture();
		var sound = Record( f.Programs[2], 19, 12, 0, thing: 0, mesh: 0 );
		var root = Record( f.Programs[0], 12, 0, 0, sound: 19 );
		// walk entry 1: rider 41 on board (state 2) between the nodes 2 and 3, started 4 s before the saved clock
		BitConverter.GetBytes( (ushort)2 ).CopyTo( root.Walk, 32 );
		BitConverter.GetBytes( (ushort)3 ).CopyTo( root.Walk, 34 );
		BitConverter.GetBytes( Clock - 4000 ).CopyTo( root.Walk, 32 + 8 );
		BitConverter.GetBytes( Clock - 3000 ).CopyTo( root.Walk, 32 + 12 );
		BitConverter.GetBytes( 41u ).CopyTo( root.Walk, 32 + 0x10 );
		BitConverter.GetBytes( (ushort)1 ).CopyTo( root.Walk, 32 + 0x16 );
		BitConverter.GetBytes( (ushort)2 ).CopyTo( root.Walk, 32 + 0x18 );
		// the object list: a kept effect (type 1, key 1, slot 1, emitter handle 0x7A0014 as the shipped Jungle coconut shop holds it) and a kept sound (type 8); the repair effect handle at +0xCC
		root = root with { Objects = new[] { new SaveRsseObject( 0, 0, 3, 0x1320063, 0xFFFFFFFF, 0xFFFFFFFF, 1 ), new SaveRsseObject( 0, 0, 1, 0x7A0014, 1, 0, 1 ) } };
		Put( root.Raw, 0xCC, 0x330005 );
		f.Records = new[] { sound, root };
		var report = f.Apply();
		Assert.AreEqual( 2, f.World.Scripts.Count, string.Join( "; ", report ) );
		Assert.IsTrue( f.Runtime.Visitors.SavedRunStateImported, "the root's bridge holds the saved state" );
		var effects = f.Runtime.CaptureSnapshot().ParticleObjects!;
		Assert.AreEqual( (1, 0x7A0014, 1, 1), (effects.Single().Slot, effects.Single().Handle, effects.Single().Type, effects.Single().Key), "the kept effect is the saved node, the sound is a gap" );
		Assert.AreEqual( 0x330005, f.Runtime.RepairParticleHandle );
		StringAssert.Contains( string.Join( "; ", report ), "1 kept sounds" );
		// the rider tables are the root's: the rider joins once the guest import knows it
		Assert.AreEqual( 0, f.Runtime.Visitors.ImportSavedRiders( new[] { 41 } ).Count );
		var walkers = new List<RideVisitorBridge.WalkerView>();
		f.Runtime.Visitors.CollectWalkers( walkers );
		Assert.AreEqual( (41, 2, -4000d, -3000d), (walkers.Single().Guest, walkers.Single().State, walkers.Single().Began, walkers.Single().Ends) );
	}

	[TestMethod]
	public void AChildScriptsOwnRiderTablesAreNotKept()
	{
		using var f = new Fixture();
		var sound = Record( f.Programs[2], 19, 12, 0, thing: 0, mesh: 0 );
		BitConverter.GetBytes( 55u ).CopyTo( sound.Walk, 0x10 );
		BitConverter.GetBytes( (ushort)1 ).CopyTo( sound.Walk, 0x18 );
		f.Records = new[] { sound, Record( f.Programs[0], 12, 0, 0, sound: 19 ) };
		var report = f.Apply();
		StringAssert.Contains( string.Join( "; ", report ), "script 19 is a child script and holds rider tables" );
		var walkers = new List<RideVisitorBridge.WalkerView>();
		f.Runtime.Visitors.ImportSavedRiders( Array.Empty<int>() );
		f.Runtime.Visitors.CollectWalkers( walkers );
		Assert.AreEqual( 0, walkers.Count );
	}

	private static void WithIsolatedMusicDuck( Action<AudioMixer> test )
	{
		var previousMixer = AudioMixer.Current;
		var previousRequest = GameAudio.MusicDuckRequest;
		using var mixer = new AudioMixer( null );
		AudioMixer.Current = mixer;
		try { GameAudio.SetMusicDuck( 17 ); test( mixer ); }
		finally { AudioMixer.Current = previousMixer; GameAudio.SetMusicDuck( previousRequest ); }
	}

	[TestMethod]
	public void LateGraphFailureDoesNotSendPreparedMusicDuckRequests()
	{
		WithIsolatedMusicDuck( mixer =>
		{
			using var f = new Fixture();
			f.Records[1].Raw[0xB9] = 2;
			Put( f.Records[0].Raw, 0x3C, 1 ); // Invalid late grandchild PC, after the root was prepared.
			var calls = 0;
			void Changed() => calls++;
			mixer.GroupsChanged += Changed;
			try
			{
				AssertRefusedUnchanged( f );
				Assert.AreEqual( 0, calls );
				Assert.AreEqual( (ushort)17, GameAudio.MusicDuckRequest );
				Assert.AreEqual( (ushort)17, mixer.MusicDuckRequest );
			}
			finally { mixer.GroupsChanged -= Changed; }
		} );
	}

	[TestMethod]
	public void MusicDuckCallbacksSeeOnlyTheFullyCommittedGraph()
	{
		WithIsolatedMusicDuck( mixer =>
		{
			using var f = new Fixture();
			foreach ( var record in f.Records ) record.Raw[0xB9] = 2;
			var calls = 0;
			void Changed()
			{
				calls++;
				CollectionAssert.AreEqual( new[] { 3, 12, 19 }, f.World.ListOrder.Select( vm => vm.ScriptId ).ToArray() );
				Assert.AreEqual( 12, f.Root.ScriptId );
				Assert.AreEqual( 3, f.Root.Child!.ScriptId );
				Assert.AreEqual( 19, f.Root.Child.Child!.ScriptId );
				Assert.AreSame( f.Root, f.Root.Child.Parent );
				Assert.AreSame( f.Root.Child, f.Root.Child.Child.Parent );
				Assert.AreEqual( (double)Clock, f.World.ClockMilliseconds );
				Assert.AreEqual( 97036L, f.World.PassCounter );
				Assert.AreEqual( 30, f.World.CaptureSnapshot().NextScriptId );
				Assert.IsTrue( f.World.Scripts.All( vm => vm.MusicDuckByte == 2 ) );
			}
			mixer.GroupsChanged += Changed;
			try
			{
				var report = f.Apply();
				Assert.AreEqual( 3, f.World.Scripts.Count, string.Join( "; ", report ) );
				Assert.AreEqual( 3, calls );
				Assert.AreEqual( (ushort)2, GameAudio.MusicDuckRequest );
				Assert.AreEqual( (ushort)2, mixer.MusicDuckRequest );
			}
			finally { mixer.GroupsChanged -= Changed; }
		} );
	}

	[TestMethod]
	public void SavedZeroMusicDuckOverwritesFreshCandidateStateWithoutCallback()
	{
		WithIsolatedMusicDuck( mixer =>
		{
			using var f = new Fixture();
			f.Root.MusicDuckByte = 99;
			var calls = 0;
			void Changed() => calls++;
			mixer.GroupsChanged += Changed;
			try
			{
				var report = f.Apply();
				Assert.AreEqual( 3, f.World.Scripts.Count, string.Join( "; ", report ) );
				Assert.AreEqual( 0, f.Root.MusicDuckByte );
				Assert.AreEqual( 0, calls );
				Assert.AreEqual( (ushort)17, GameAudio.MusicDuckRequest );
				Assert.AreEqual( (ushort)17, mixer.MusicDuckRequest );
			}
			finally { mixer.GroupsChanged -= Changed; }
		} );
	}

	[TestMethod]
	public void NestedGraphUsesSavedOrderIdsAndPassCadenceWithoutDrawingRandom()
	{
		using var f = new Fixture();
		var random = f.World.RandomState;
		var report = f.Apply();
		Assert.AreEqual( 3, f.World.Scripts.Count, string.Join( "; ", report ) );
		Assert.AreEqual( 12, f.Root.ScriptId );
		Assert.AreSame( f.Root, f.Root.Child!.Parent );
		Assert.AreSame( f.Root.Child, f.Root.Child.Child!.Parent );
		CollectionAssert.AreEqual( new[] { 3, 12, 19 }, f.World.ListOrder.Select( vm => vm.ScriptId ).ToArray() );
		Assert.AreEqual( 30, f.World.CaptureSnapshot().NextScriptId );
		Assert.AreEqual( (double)Clock, f.World.ClockMilliseconds );
		Assert.AreEqual( random, f.World.RandomState );
		foreach ( var vm in f.World.Scripts )
		{
			Assert.IsTrue( vm.CaptureSnapshot().UsesWorldRandom );
			Assert.AreEqual( 0L, vm.CaptureSnapshot().RandomDraws );
			Assert.AreEqual( 0L, vm.ExecutedInstructions );
		}
		for ( var pass = 1; pass <= 16; pass++ )
		{
			var before = f.World.ListOrder.ToDictionary( vm => vm.ScriptId, vm => vm.Variables[0] );
			f.World.RunPass( SimulationSubstep.Milliseconds );
			Assert.AreEqual( 97036L + pass, f.World.PassCounter );
			foreach ( var vm in f.World.ListOrder )
				Assert.AreEqual( ((vm.ScriptId ^ (97036 + pass)) & 7) == 0, vm.Variables[0] != before[vm.ScriptId], $"ID {vm.ScriptId}, pass {pass}" );
		}
		Assert.AreEqual( random, f.World.RandomState );
	}

	[TestMethod]
	public void NestedWaitsStackHeapLimboAndTimersResumeWithoutReplayingSpawn()
	{
		using var f = new Fixture( waits: true );
		for ( var i = 0; i < f.Records.Length; i++ )
		{
			var record = f.Records[i];
			Put( record.Raw, 0x40, 2 ); Put( record.Raw, 0x44, 1 ); Put( record.Raw, 0x60, 1 );
			Put( record.Raw, 0xA0, Clock + 500 ); Put( record.Raw, 0xA4, Clock + 900 ); Put( record.Raw, 0xC4, Clock + 4000 );
			f.Records[i] = record with { Stack = Words( 42, 0, 0, 0x20000002 ), Variables = Words( 5, -6, 7, 8 ), Limbo = Words( 31, (int)Clock + 2000, 0, 0 ) };
		}
		var report = f.Apply();
		Assert.AreEqual( 3, f.World.Scripts.Count, string.Join( "; ", report ) );
		foreach ( var vm in f.World.Scripts )
		{
			Assert.AreEqual( RideVMState.Waiting, vm.State ); Assert.AreEqual( 1, vm.ProgramCounter );
			Assert.AreEqual( 500d, vm.WakeTimeMilliseconds ); Assert.AreEqual( 900d, vm.Wait4AnimDeadline ); Assert.AreEqual( 4000, vm.TimerRemaining );
			CollectionAssert.AreEqual( new[] { 5, -6, 7, 8 }, vm.Variables );
			Assert.AreEqual( 1, vm.HeapCount ); Assert.AreEqual( 42, vm.GetStackSlot( 0 ) ); Assert.AreEqual( 1, vm.CallDepth );
			Assert.AreEqual( (31, (int)Clock + 2000), vm.GetLimboSlot( 0 ) ); Assert.AreEqual( 1, vm.LimboCount );
			Assert.AreEqual( 0L, vm.ExecutedInstructions );
		}
	}


	[TestMethod]
	public void NonzeroSavedInstructionWordIsUsedRatherThanRestartingTheChild()
	{
		using var f = new Fixture();
		Put( f.Records[0].Raw, 0x3C, 3 ); // ENDSLICE follows the three-word ADD.
		Put( f.Records[0].Variables, 0, 123 );
		var report = f.Apply();
		Assert.AreEqual( 3, f.World.Scripts.Count, string.Join( "; ", report ) );
		Assert.AreEqual( 1, f.Root.Child!.Child!.ProgramCounter );
		Assert.AreEqual( 123, f.Root.Child.Child.Variables[0] );
		Assert.AreEqual( 0L, f.Root.Child.Child.ExecutedInstructions );
	}

	[TestMethod]
	public void ImportedGraphCanRoundTripThroughOwnFormatAndContinueIdentically()
	{
		using var original = new Fixture();
		foreach ( var record in original.Records ) record.Raw[0xB8] = 1; // All nodes execute on the same pass, exposing list-order differences.
		original.Apply();
		Assert.AreEqual( 3, original.World.Scripts.Count );
		Assert.AreEqual( "child.rse", original.Root.Child!.CaptureSnapshot().ScriptFileName );
		Assert.AreEqual( "grand.rse", original.Root.Child.Child!.CaptureSnapshot().ScriptFileName );
		var saved = ParkResumeJson.Deserialize<RideScriptWorldSnapshot>( ParkResumeJson.Serialize( original.World.CaptureSnapshot() ) );
		CollectionAssert.AreEqual( new[] { 3, 12, 19 }, saved.ListOrder! );
		using var restored = new Fixture();
		restored.World.RestoreSnapshot( saved, new Dictionary<int, RideVM> { [12] = restored.Root } );
		CollectionAssert.AreEqual( ParkResumeJson.Serialize( saved ), ParkResumeJson.Serialize( restored.World.CaptureSnapshot() ) );
		CollectionAssert.AreEqual( new[] { 3, 12, 19 }, restored.World.ListOrder.Select( vm => vm.ScriptId ).ToArray() );
		var originalTrace = new List<int>(); var restoredTrace = new List<int>();
		foreach ( var vm in original.World.Scripts ) vm.InstructionExecuting = ( script, instruction ) => { if ( instruction.Opcode == Opcode.ADD ) originalTrace.Add( script.ScriptId ); };
		foreach ( var vm in restored.World.Scripts ) vm.InstructionExecuting = ( script, instruction ) => { if ( instruction.Opcode == Opcode.ADD ) restoredTrace.Add( script.ScriptId ); };
		for ( var pass = 0; pass < 16; pass++ )
		{
			originalTrace.Clear(); restoredTrace.Clear();
			original.World.RunPass( SimulationSubstep.Milliseconds ); restored.World.RunPass( SimulationSubstep.Milliseconds );
			CollectionAssert.AreEqual( new[] { 3, 12, 19 }, originalTrace.ToArray(), $"original execution order on pass {pass}" );
			CollectionAssert.AreEqual( originalTrace, restoredTrace, $"restored execution order on pass {pass}" );
			CollectionAssert.AreEqual( ParkResumeJson.Serialize( original.World.CaptureSnapshot() ), ParkResumeJson.Serialize( restored.World.CaptureSnapshot() ) );
		}
	}

	[TestMethod]
	public void OwnFormatListOrderPreservesPerScriptSharedRandomDraws()
	{
		using var original = new Fixture( randomProgram: true );
		foreach ( var record in original.Records ) record.Raw[0xB8] = 1;
		var report = original.Apply();
		Assert.AreEqual( 3, original.World.Scripts.Count, string.Join( "; ", report ) );
		var saved = ParkResumeJson.Deserialize<RideScriptWorldSnapshot>( ParkResumeJson.Serialize( original.World.CaptureSnapshot() ) );
		using var restored = new Fixture( randomProgram: true );
		using var legacy = new Fixture( randomProgram: true );
		restored.World.RestoreSnapshot( saved, new Dictionary<int, RideVM> { [12] = restored.Root } );
		legacy.World.RestoreSnapshot( saved with { ListOrder = null }, new Dictionary<int, RideVM> { [12] = legacy.Root } );
		var originalTrace = new List<(int Id, ulong RandomState)>(); var restoredTrace = new List<(int Id, ulong RandomState)>();
		foreach ( var vm in original.World.Scripts ) vm.InstructionExecuting = ( script, instruction ) => { if ( instruction.Opcode == Opcode.RAND ) originalTrace.Add( (script.ScriptId, original.World.RandomState) ); };
		foreach ( var vm in restored.World.Scripts ) vm.InstructionExecuting = ( script, instruction ) => { if ( instruction.Opcode == Opcode.RAND ) restoredTrace.Add( (script.ScriptId, restored.World.RandomState) ); };
		for ( var pass = 0; pass < 16; pass++ )
		{
			originalTrace.Clear(); restoredTrace.Clear();
			original.World.RunPass( SimulationSubstep.Milliseconds ); restored.World.RunPass( SimulationSubstep.Milliseconds );
			CollectionAssert.AreEqual( new[] { 3, 12, 19 }, originalTrace.Select( draw => draw.Id ).ToArray() );
			CollectionAssert.AreEqual( originalTrace, restoredTrace, $"shared RNG assignment order on pass {pass}" );
			CollectionAssert.AreEqual( ParkResumeJson.Serialize( original.World.CaptureSnapshot() ), ParkResumeJson.Serialize( restored.World.CaptureSnapshot() ) );
			if ( pass == 0 )
			{
				legacy.World.RunPass( SimulationSubstep.Milliseconds );
				Assert.AreEqual( original.World.RandomState, legacy.World.RandomState, "the same total draws hide ordering errors in world RNG state alone" );
				Assert.IsFalse( original.World.Scripts.OrderBy( vm => vm.ScriptId ).Select( vm => vm.Variables[1] ).SequenceEqual( legacy.World.Scripts.OrderBy( vm => vm.ScriptId ).Select( vm => vm.Variables[1] ) ),
					"descending-ID legacy order must assign different draws to the imported scripts" );
			}
		}
	}


	[DataTestMethod]
	[DataRow( "duplicate" )]
	[DataRow( "missing" )]
	[DataRow( "unknown" )]
	public void InvalidOwnFormatListOrderIsRejectedBeforeWorldOrRootsChange( string failure )
	{
		using var original = new Fixture(); original.Apply();
		var order = failure switch
		{
			"duplicate" => new[] { 3, 12, 12 },
			"missing" => new[] { 3, 12 },
			_ => new[] { 3, 12, 99 }
		};
		var invalid = original.World.CaptureSnapshot() with { ListOrder = order };
		using var target = new Fixture();
		var before = ParkResumeJson.Serialize( target.World.CaptureSnapshot() );
		var root = ParkResumeJson.Serialize( target.Root.CaptureSnapshot() );
		var list = target.World.ListOrder.ToArray();
		Assert.ThrowsException<InvalidDataException>( () => target.World.RestoreSnapshot( invalid, new Dictionary<int, RideVM> { [12] = target.Root } ) );
		CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( target.World.CaptureSnapshot() ) );
		CollectionAssert.AreEqual( root, ParkResumeJson.Serialize( target.Root.CaptureSnapshot() ) );
		CollectionAssert.AreEqual( list, target.World.ListOrder.ToArray() );
	}

	[TestMethod]
	public void LegacyOwnFormatWithoutListOrderStillRestoresDescendingIds()
	{
		using var original = new Fixture(); original.Apply();
		var legacy = original.World.CaptureSnapshot() with { ListOrder = null };
		var bytes = ParkResumeJson.Serialize( legacy );
		Assert.IsFalse( System.Text.Encoding.UTF8.GetString( bytes ).Contains( "listOrder", StringComparison.OrdinalIgnoreCase ), "legacy descending order has no serialized optional field" );
		using var target = new Fixture();
		target.World.RestoreSnapshot( ParkResumeJson.Deserialize<RideScriptWorldSnapshot>( bytes ), new Dictionary<int, RideVM> { [12] = target.Root } );
		CollectionAssert.AreEqual( new[] { 19, 12, 3 }, target.World.ListOrder.Select( vm => vm.ScriptId ).ToArray() );
		Assert.IsNull( target.World.CaptureSnapshot().ListOrder );
	}

	[TestMethod]
	public void ListOrderDeepCopyDoesNotAliasTheSnapshotArray()
	{
		using var f = new Fixture(); f.Apply();
		var saved = f.World.CaptureSnapshot(); var copied = saved.DeepCopy();
		Assert.AreNotSame( saved.ListOrder, copied.ListOrder );
		copied.ListOrder![0] = 99;
		CollectionAssert.AreEqual( new[] { 3, 12, 19 }, saved.ListOrder! );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 3 )]
	public void CatalogReadReservesRemainingAggregateBudgetBeforeReading( int remaining )
	{
		using var stream = new MemoryStream( new byte[8192] );
		long inputBytes = 64L * 1024 * 1024 - remaining;
		Assert.ThrowsException<InvalidDataException>( () => SavedScriptGraphImport.ReadCatalogScript( stream, ref inputBytes ) );
		Assert.AreEqual( (long)remaining, stream.Position, "no byte may be read beyond the remaining aggregate budget, even to probe EOF" );
		Assert.AreEqual( 64L * 1024 * 1024, inputBytes );
	}

	[TestMethod]
	public void CatalogReadDoesNotReadPastThePerFileBudget()
	{
		using var stream = new MemoryStream( new byte[RideScriptFile.MaximumFileBytes + 1] );
		long inputBytes = 0;
		Assert.ThrowsException<InvalidDataException>( () => SavedScriptGraphImport.ReadCatalogScript( stream, ref inputBytes ) );
		Assert.AreEqual( (long)RideScriptFile.MaximumFileBytes, stream.Position );
		Assert.AreEqual( (long)RideScriptFile.MaximumFileBytes, inputBytes );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( SaveRsseRecord.Bytes - 1 )]
	[DataRow( SaveRsseRecord.Bytes + 1 )]
	public void MalformedLoneRootCannotFallThroughToThePartialLegacyImporter( int size )
	{
		using var f = new Fixture();
		var raw = new byte[size];
		Array.Copy( f.Records[1].Raw, raw, Math.Min( raw.Length, f.Records[1].Raw.Length ) );
		var malformed = f.Records[1] with { Raw = raw };
		AssertRefusedUnchanged( f, f.Block with { Records = new[] { malformed }, HeaderRecordCount = 1 } );
	}

	[TestMethod]
	public void PassOnlyOrClockOriginOnlyHistoryIsNotFreshForOriginalGraphImport()
	{
		using ( var f = new Fixture() )
		{
			f.World.ImportSaved( new[] { (f.Root, 1) }, new[] { 1 }, 2, 1 );
			Assert.AreEqual( 0d, f.World.ClockMilliseconds ); Assert.AreEqual( 0L, f.Root.ExecutedInstructions );
			AssertRefusedUnchanged( f );
		}
		using ( var f = new Fixture() )
		{
			f.Root.ClockOriginMilliseconds = 1;
			Assert.AreEqual( 0d, f.World.ClockMilliseconds ); Assert.AreEqual( 0L, f.Root.ExecutedInstructions );
			AssertRefusedUnchanged( f );
		}
	}

	[DataTestMethod]
	[DataRow( "sound" )] [DataRow( "missing-child" )] [DataRow( "missing-root" )] [DataRow( "duplicate-id" )]
	[DataRow( "duplicate-incoming" )] [DataRow( "orphan" )] [DataRow( "cycle" )] [DataRow( "reciprocal" )]
	[DataRow( "thing" )] [DataRow( "mesh" )] [DataRow( "pc" )] [DataRow( "stack" )] [DataRow( "limbo" )]
	[DataRow( "opcode" )] [DataRow( "operand" )] [DataRow( "string" )] [DataRow( "empty-string" )]
	[DataRow( "root-opcode" )] [DataRow( "root-operand" )]
	[DataRow( "theme" )] [DataRow( "category" )] [DataRow( "timeslice" )] [DataRow( "raw-count" )]
	public void MalformedGraphOrUnresolvedExactIdentityIsAtomic( string failure )
	{
		using var f = new Fixture();
		var leaf = f.Records[0]; var root = f.Records[1]; var child = f.Records[2];
		switch ( failure )
		{
			case "sound": Put( leaf.Raw, 0x14, 29 ); break;
			case "missing-child": Put( child.Raw, 0x0C, 29 ); break;
			case "missing-root": Put( root.Raw, 0xAC, 99 ); break;
			case "duplicate-id": Put( leaf.Raw, 0x08, 3 ); break;
			case "duplicate-incoming": Put( root.Raw, 0x0C, 19 ); break;
			case "orphan": Put( child.Raw, 0x0C, 0 ); break;
			case "cycle": Put( leaf.Raw, 0x0C, 12 ); Put( root.Raw, 0x10, 19 ); break;
			case "reciprocal": Put( leaf.Raw, 0x10, 12 ); break;
			case "thing": Put( leaf.Raw, 0xAC, 12 ); break;
			case "mesh": Put( leaf.Raw, 0xC8, 111 ); break;
			case "pc": Put( leaf.Raw, 0x3C, 1 ); break;
			case "stack": Put( leaf.Raw, 0x40, 99 ); break;
			case "limbo": Put( leaf.Raw, 0x60, 99 ); break;
			case "root-opcode": Put( root.Code, 0, 0x80000000 | (uint)Opcode.COPY ); break;
			case "root-operand": Put( root.Code, 8, 99 ); break;
			case "opcode": Put( leaf.Code, 0, 0x80000000 | (uint)Opcode.COPY ); break;
			case "operand": Put( leaf.Code, 8, 99 ); break;
			case "string": leaf.Strings[2] ^= 1; break;
			case "empty-string": leaf.Strings[0] = (byte)'x'; break;
			case "theme": f.Records[0] = leaf with { Folder = leaf.Folder.Replace( "jungle", "halloween" ) }; break;
			case "category": f.Records[0] = leaf with { Folder = leaf.Folder.Replace( "Features", "Rides" ) }; break;
			case "timeslice": Put( leaf.Raw, 0x94, 51 ); break;
			case "raw-count": Put( leaf.Raw, 0x90, (uint)leaf.Strings.Length + 1 ); break;
		}
		AssertRefusedUnchanged( f );
	}

	[TestMethod]
	public void MissingClockSeededRootsExtrasAndAlreadyRunWorldAreRefused()
	{
		using ( var f = new Fixture() ) AssertRefusedUnchanged( f, clock: null );
		using ( var f = new Fixture( seed: 1 ) ) AssertRefusedUnchanged( f );
		using ( var f = new Fixture() ) { _ = new RideVM( new RideScriptFile( new MemoryStream( f.Programs[0] ) ), new RideVMOptions { World = f.World } ); AssertRefusedUnchanged( f ); }
		using ( var f = new Fixture() ) { f.World.RunPass( SimulationSubstep.Milliseconds ); AssertRefusedUnchanged( f ); }
	}

	[DataTestMethod]
	[DataRow( 0u )] [DataRow( 19u )] [DataRow( 2147483647u )] [DataRow( 4294967295u )]
	public void InvalidAllocationFrontierIsNotSilentlyRepaired( uint next )
	{
		using var f = new Fixture(); AssertRefusedUnchanged( f, f.Block with { NextScriptId = next } );
	}

	[TestMethod]
	public void AmbiguousMatchingFilesAndBasenamesAreRefused()
	{
		using ( var f = new Fixture() ) { f.AddMember( "other.rse", f.Programs[2] ); AssertRefusedUnchanged( f ); }
		using ( var f = new Fixture() )
		{
			const string other = "/data/levels/halloween/Features/test/grand.rse";
			f.Write( other, f.Programs[0] ); f.Members.Insert( 0, other ); AssertRefusedUnchanged( f );
		}
	}


	[TestMethod]
	public void LateChildConstructorArityFailureDoesNotInstallPreparedAncestors()
	{
		using var f = new Fixture();
		var invalid = new Asm().I( Opcode.ADD, V( 0 ) ).I( Opcode.ENDSLICE ).Build( new[] { "V0", "V1", "V2", "V3" }, "invalid" );
		f.Write( Folder + "grand.rse", invalid );
		f.Records[0] = Record( invalid, 19, 3, 0 );
		AssertRefusedUnchanged( f );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void WaitAnimResumesAndTriggerWaitAnimKeepsItsSavedTokenForTheRestoredChannel( bool trigger )
	{
		using var f = new Fixture();
		var asm = new Asm();
		if ( trigger ) asm.I( Opcode.TRIGWAITANIM, 1, 0, V( 0 ) );
		else asm.I( Opcode.WAITANIM, 1, 0 );
		var bytes = asm.I( Opcode.ENDSLICE ).Build( new[] { "V0", "V1", "V2", "V3" }, "animation" );
		f.Write( Folder + "grand.rse", bytes );
		var leaf = Record( bytes, 19, 3, 0 );
		Put( leaf.Raw, 0xA0, Clock + 500 ); Put( leaf.Raw, 0xBC, 123 );
		f.Records[0] = leaf;
		var report = f.Apply();
		Assert.AreEqual( 3, f.World.Scripts.Count, string.Join( "; ", report ) );
		var vm = f.Root.Child!.Child!;
		Assert.AreEqual( trigger ? RideVMState.Running : RideVMState.Waiting, vm.State );
		Assert.AreEqual( trigger ? 0 : 1, vm.ProgramCounter );
		Assert.AreEqual( trigger ? 0d : 500d, vm.WakeTimeMilliseconds );
		// the token is the saved word (0x00552CD3): the object's channels come back with the RideSystem record, so the instruction compares them with it again instead of triggering a second time
		if ( trigger ) Assert.AreEqual( 123, vm.CaptureSnapshot().TriggerWaitToken );
		Assert.AreEqual( 0L, vm.ExecutedInstructions );
	}

	[TestMethod]
	public void UntrustedResolverIsNeverInvokedEvenWhenItCouldReturnMatchingCode()
	{
		using var f = new Fixture();
		f.Root.Stop();
		var calls = 0;
		var root = new RideVM( new RideScriptFile( new MemoryStream( f.Programs[0] ) ), new RideVMOptions
		{
			World = f.World, SourceName = Folder + "root.rse",
			ResolveScript = _ => { calls++; return new RideScriptFile( new MemoryStream( f.Programs[1] ) ); }
		} );
		var before = ParkResumeJson.Serialize( f.World.CaptureSnapshot() );
		var list = f.World.ListOrder.ToArray();
		var report = SavedScriptImport.Apply( f.World, f.Block, new[] { new SavedScriptImport.Candidate( 11, f.Entry, root ) }, Clock );
		Assert.AreEqual( 0, calls );
		CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( f.World.CaptureSnapshot() ), string.Join( "; ", report ) );
		CollectionAssert.AreEqual( list, f.World.ListOrder.ToArray() );
	}


	[TestMethod]
	public void ThemeParkIncGraphIsRefusedWithoutApplyingTpwEvidence()
	{
		using var f = new Fixture();
		GameEditions.Select( GameEdition.ThemeParkInc );
		AssertRefusedUnchanged( f );
	}

	[TestMethod]
	public void RootOnlyLegacyImportRetainsItsUnmatchedFreshScriptBehavior()
	{
		using var f = new Fixture();
		var root = f.Records[1]; Put( root.Raw, 0x0C, 0 );
		var extra = new RideVM( new RideScriptFile( new MemoryStream( f.Programs[0] ) ), new RideVMOptions { World = f.World, Seed = 1 } );
		f.Apply( f.Block with { Records = new[] { root }, HeaderRecordCount = 1 } );
		Assert.AreEqual( 12, f.Root.ScriptId ); Assert.AreEqual( 30, extra.ScriptId );
		Assert.AreEqual( 31, f.World.CaptureSnapshot().NextScriptId ); Assert.AreSame( extra, f.World.ListOrder[0] );
	}
}
