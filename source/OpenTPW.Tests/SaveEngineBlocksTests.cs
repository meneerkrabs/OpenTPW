using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The Scripts (tp.exe loader 0x475730) and Particles (loader 0x51F7A0) block readers: synthetic blocks prove each field, list and refusal; the original-save corpus
/// (OPENTPW_TPWS_SAVES) pins that both blocks are consumed exactly in every save and that the pool state restores into the runtime particle system.
/// </summary>
[TestClass]
public class SaveEngineBlocksTests
{
	private static byte[] Words( params uint[] words ) => words.SelectMany( BitConverter.GetBytes ).ToArray();

	private static byte[] Join( params byte[][] parts ) => parts.SelectMany( part => part ).ToArray();

	// ---- Scripts ----

	private static byte[] ScriptRecord( int slot, uint counter = 0x61, Action<byte[]>? edit = null )
	{
		var body = new byte[SaveScriptRecord.Bytes];
		BitConverter.TryWriteBytes( body.AsSpan( 4 ), (uint)slot );
		BitConverter.TryWriteBytes( body.AsSpan( 8 ), counter );
		BitConverter.TryWriteBytes( body.AsSpan( 0x0C ), 0x5Au );
		BitConverter.TryWriteBytes( body.AsSpan( 0x10 ), 0x5Fu );
		BitConverter.TryWriteBytes( body.AsSpan( 0x14 ), SaveScriptRecord.ProgramTable );
		BitConverter.TryWriteBytes( body.AsSpan( 0x18 ), 2u );
		BitConverter.TryWriteBytes( body.AsSpan( 0x1C ), 0x14u );
		BitConverter.TryWriteBytes( body.AsSpan( 0x7C ), 0xAD6071Cu );
		BitConverter.TryWriteBytes( body.AsSpan( 0x80 ), 0x8Cu );
		BitConverter.TryWriteBytes( body.AsSpan( 0x88 ), 470.5f );
		BitConverter.TryWriteBytes( body.AsSpan( 0x90 ), 130.25f );
		BitConverter.TryWriteBytes( body.AsSpan( 0xC0 ), 0x1200u );
		edit?.Invoke( body );
		return body;
	}

	internal static byte[] ScriptsBlock( uint[] slots, params byte[][] records ) =>
		Join( Words( SaveBlockWidths.ScriptsMagic, SaveBlockWidths.ScriptRecordBytes, (uint)slots.Length ), Words( slots ), Join( records ) );

	[TestMethod]
	public void ScriptsBlockReadsTheSlotTableAndTheRecordsOfNonzeroSlots()
	{
		var slots = new uint[6];
		slots[0] = 0x1234;
		slots[2] = 0x12787DA0;
		slots[5] = 0x127879C8;
		var bytes = Join( new byte[7], ScriptsBlock( slots, ScriptRecord( 2 ), ScriptRecord( 5, 0x70 ) ) );
		var block = SaveScriptsBlock.Parse( bytes, 7 );
		Assert.AreEqual( 12 + 24 + 2 * 280, block.Length );
		Assert.AreEqual( 6, block.SlotCount );
		CollectionAssert.AreEqual( new[] { 2, 5 }, block.Records.Select( record => record.Slot ).ToArray(), "slot 0 is never read, zero slots have no record" );
		var first = block.Records[0];
		Assert.AreEqual( 7 + 12 + 24, first.Offset );
		Assert.AreEqual( 2u, first.StoredSlot );
		Assert.AreEqual( 0x61u, first.ProgramCounter );
		Assert.AreEqual( 0x5Au, first.EntryPoint );
		Assert.AreEqual( 0x5Fu, first.LastCounter );
		Assert.AreEqual( SaveScriptRecord.ProgramTable, first.Program );
		Assert.AreEqual( 2u, first.State );
		Assert.AreEqual( 0x14u, first.StackCapacity );
		Assert.AreEqual( 0xAD6071Cu, first.WakeTick );
		Assert.AreEqual( 0x8Cu, first.Period );
		Assert.AreEqual( (470.5f, 0f, 130.25f), first.Position );
		Assert.AreEqual( 0x1200u, first.PositionFlags );
		Assert.AreEqual( SaveScriptRecord.StackWords, first.Stack.Length );
		Assert.AreEqual( SaveScriptRecord.ParameterWords, first.Parameters.Length );
		Assert.AreEqual( 0x70u, block.Records[1].ProgramCounter );
	}

	[TestMethod]
	public void ScriptsBlockWithoutScriptsIsItsHeaderAndSlotTable()
	{
		var block = SaveScriptsBlock.Parse( ScriptsBlock( new uint[100] ), 0 );
		Assert.AreEqual( 12 + 400, block.Length );
		Assert.AreEqual( 0, block.Records.Count );
	}

	[TestMethod]
	public void ScriptsBlockRefusesWhatTheLoaderFails()
	{
		var good = ScriptsBlock( new uint[] { 0, 1, 0 }, ScriptRecord( 1 ) );
		var badMagic = (byte[])good.Clone();
		badMagic[0] ^= 1;
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SaveScriptsBlock.Parse( badMagic, 0 ) ).Message, "Restore scripts FAILED" );
		var badSize = (byte[])good.Clone();
		badSize[4] = 0x17;
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SaveScriptsBlock.Parse( badSize, 0 ) ).Message, "Restore scripts FAILED" );
		for ( var cut = 0; cut < good.Length; cut++ )
			Assert.ThrowsException<InvalidDataException>( () => SaveScriptsBlock.Parse( good.AsSpan( 0, cut ), 0 ), $"truncated at {cut}" );
		var hugeCount = Join( Words( SaveBlockWidths.ScriptsMagic, SaveBlockWidths.ScriptRecordBytes, 0x7FFFFFFF ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveScriptsBlock.Parse( hugeCount, 0 ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveScriptsBlock.Parse( good, good.Length + 1 ) );
	}

	[TestMethod]
	public void ScriptsBlockWidthMatchesTheBlockWidthRule()
	{
		var block = ScriptsBlock( new uint[] { 0, 7, 0, 9 }, ScriptRecord( 1 ), ScriptRecord( 3 ) );
		Assert.IsTrue( SaveBlockWidths.TryMeasure( block, SaveBlock.Scripts, 0, out var width ) );
		Assert.AreEqual( SaveScriptsBlock.Parse( block, 0 ).Length, width );
	}

	// ---- Particles ----

	/// <summary>A pool table: slot lists the way a reset builds them, emitters in <paramref name="active"/> in use (newest first).</summary>
	internal static byte[] PoolTable( int[] active, Action<byte[]>? edit = null, int[]? activeEffectors = null )
	{
		var table = new byte[SaveBlockWidths.ParticleHeaderBytes];
		void U32( int at, uint value ) => BitConverter.TryWriteBytes( table.AsSpan( at ), value );
		void I16( int at, int value ) => BitConverter.TryWriteBytes( table.AsSpan( at ), (short)value );
		U32( 0, 0 );
		U32( 4, 97006 );
		U32( 0xC, 0x800 );
		U32( 0x10, 3957965337 );
		U32( 0x14, 1 );
		I16( 0x18, 1483 );
		I16( 0x1A, 3 );
		U32( 0x1C, 8403816 );
		U32( 0x20, 1500 );
		U32( 0x24, 6119616 );
		var inactive = Enumerable.Range( 0, SaveParticlesBlock.EmitterSlots ).Except( active ).ToArray();
		void Chain( int baseOffset, int stride, int[] order )
		{
			for ( var index = 0; index < order.Length; index++ )
			{
				I16( baseOffset + order[index] * stride + (stride == 320 ? 0xD0 : 0x60), index + 1 < order.Length ? order[index + 1] : -1 );
				I16( baseOffset + order[index] * stride + (stride == 320 ? 0xD2 : 0x62), index > 0 ? order[index - 1] : -1 );
			}
		}
		Chain( SaveParticlesBlock.EmittersOffset, 320, active );
		Chain( SaveParticlesBlock.EmittersOffset, 320, inactive );
		foreach (var slot in active)
			BitConverter.TryWriteBytes( table.AsSpan( SaveParticlesBlock.EmittersOffset + slot * 320 ), (ushort)1 );
		var effectorsActive = activeEffectors ?? Array.Empty<int>();
		var effectorsFree = Enumerable.Range( 0, SaveParticlesBlock.EffectorSlots ).Except( effectorsActive ).ToArray();
		Chain( SaveParticlesBlock.EffectorsOffset, 104, effectorsActive );
		Chain( SaveParticlesBlock.EffectorsOffset, 104, effectorsFree );
		I16( SaveParticleGlobals.TailOffset, active.Length > 0 ? active[0] : -1 );
		I16( SaveParticleGlobals.TailOffset + 2, effectorsActive.Length > 0 ? effectorsActive[0] : -1 );
		I16( SaveParticleGlobals.TailOffset + 4, inactive[0] );
		I16( SaveParticleGlobals.TailOffset + 6, effectorsFree[0] );
		edit?.Invoke( table );
		return table;
	}

	internal static byte[] TemplateTable( Action<byte[]>? edit = null )
	{
		var table = new byte[SaveBlockWidths.ParticleFieldBytes];
		for ( var index = 0; index < SaveParticlesBlock.EmitterTemplates; index++ )
		{
			table[index * 320 + 0x118] = (byte)'T';
			table[index * 320 + 0x119] = (byte)('A' + index % 26);
		}
		for ( var index = 0; index < SaveParticlesBlock.EffectorTemplates; index++ )
			BitConverter.TryWriteBytes( table.AsSpan( 105 * 320 + index * 104 + 0x40 ), (uint)(index + 1) );
		edit?.Invoke( table );
		return table;
	}

	internal static byte[] ParticlesBlock( byte[] pool, byte[] templates, byte[]? particles = null, uint on = 1 ) =>
		Join( Words( SaveBlockWidths.ParticlesMagic, on ), on == 0 ? Array.Empty<byte>() : Join( Words( SaveBlockWidths.ParticleHeaderBytes ), pool, Words( SaveBlockWidths.ParticleFieldBytes ), templates, Words( (uint)(particles?.Length ?? 0) ), particles ?? Array.Empty<byte>() ) );

	private static void Emitter( byte[] table, int slot, int effect, int generation, int life, int x, string name, Action<int>? more = null )
	{
		var at = SaveParticlesBlock.EmittersOffset + slot * 320;
		BitConverter.TryWriteBytes( table.AsSpan( at + 0x06 ), (short)-1 );
		BitConverter.TryWriteBytes( table.AsSpan( at + 0x0A ), (ushort)generation );
		BitConverter.TryWriteBytes( table.AsSpan( at + 0x0C ), (ushort)effect );
		BitConverter.TryWriteBytes( table.AsSpan( at + 0x14 ), x );
		BitConverter.TryWriteBytes( table.AsSpan( at + 0x20 ), life );
		BitConverter.TryWriteBytes( table.AsSpan( at + 0xD4 ), life );
		BitConverter.TryWriteBytes( table.AsSpan( at + 0xB4 ), -1 );
		System.Text.Encoding.ASCII.GetBytes( name ).CopyTo( table, at + 0x118 );
		for ( var rate = 0; rate < 4; rate++ )
			table[at + 0x68 + rate] = (byte)(rate == 0 ? 0xFE : rate);
		more?.Invoke( at );
	}

	[TestMethod]
	public void ParticlesBlockReadsGlobalsEmittersListsAndTemplates()
	{
		var pool = PoolTable( new[] { 14, 0, 12 }, table =>
		{
			Emitter( table, 14, 35, 1484, 30, 5191, "Button", at => BitConverter.TryWriteBytes( table.AsSpan( at + 0x48 ), 77 ) );
			Emitter( table, 0, 20, 1, 0, 34068, "WaterFall" );
			Emitter( table, 12, 35, 1483, 30, 5192, "Button" );
		} );
		var bytes = Join( new byte[5], ParticlesBlock( pool, TemplateTable() ) );
		var block = SaveParticlesBlock.Parse( bytes, 5 );
		Assert.AreEqual( 8 + 4 + 0x9E68 + 4 + 0x8B60 + 4, block.Length );
		Assert.IsTrue( block.On );
		Assert.AreEqual( 0x800u, block.Globals.PoolCapacity );
		Assert.AreEqual( 97006u, block.Globals.TickCounter );
		Assert.AreEqual( 3957965337u, block.Globals.Random );
		Assert.AreEqual( (ushort)1483, block.Globals.EmitterGeneration );
		Assert.AreEqual( (ushort)3, block.Globals.EffectorGeneration );
		Assert.AreEqual( 1500u, block.Globals.Density );
		CollectionAssert.AreEqual( new[] { 14, 0, 12 }, block.ActiveEmitterOrder.ToArray() );
		Assert.AreEqual( 117, block.FreeEmitterOrder.Count );
		Assert.AreEqual( 20, block.FreeEffectorOrder.Count );
		var emitter = block.Emitters[14];
		Assert.IsTrue( emitter.InUse );
		Assert.AreEqual( "Button", emitter.Name );
		Assert.AreEqual( 35, emitter.EffectId );
		Assert.AreEqual( 1484, emitter.Generation );
		Assert.AreEqual( 30, emitter.Life );
		Assert.AreEqual( 5191, emitter.Position.X );
		Assert.AreEqual( 77, emitter.VolumeExtent.Y );
		Assert.AreEqual( -1, emitter.FirstParticle );
		Assert.AreEqual( -1, emitter.Child );
		CollectionAssert.AreEqual( new[] { -2, 1, 2, 3 }, emitter.Rates.ToArray() );
		Assert.AreEqual( 0, emitter.PreviousSlot + 1, "the newest emitter has no previous slot" );
		Assert.AreEqual( 105, block.EmitterTemplateBytes.Count );
		Assert.AreEqual( 20, block.EffectorTemplateBytes.Count );
		Assert.AreEqual( 0, block.ParticlePool.Length );
		Assert.IsFalse( block.Emitters[5].InUse );
	}

	[TestMethod]
	public void ParticlesBlockOffIsTheTwoWordHeader()
	{
		var block = SaveParticlesBlock.Parse( ParticlesBlock( Array.Empty<byte>(), Array.Empty<byte>(), on: 0 ), 0 );
		Assert.IsFalse( block.On );
		Assert.AreEqual( 8, block.Length );
		Assert.IsNull( block.ToSystemState( Array.Empty<ParticleTemplate>() ) );
	}

	[TestMethod]
	public void ParticlesBlockWithAParticlePoolIsReadToItsLength()
	{
		var pool = PoolTable( Array.Empty<int>() );
		var bytes = ParticlesBlock( pool, TemplateTable(), new byte[3 * 52] );
		var block = SaveParticlesBlock.Parse( bytes, 0 );
		Assert.AreEqual( 156, block.ParticlePool.Length );
		Assert.AreEqual( bytes.Length, block.Length );
	}

	[TestMethod]
	public void ParticlesBlockRefusesWhatTheLoaderFailsOrOverruns()
	{
		var pool = PoolTable( new[] { 3 }, table => Emitter( table, 3, 1, 1, 5, 0, "Sparks" ) );
		var good = ParticlesBlock( pool, TemplateTable() );
		SaveParticlesBlock.Parse( good, 0 );
		void Rejects( byte[] bytes, string text ) => StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SaveParticlesBlock.Parse( bytes, 0 ) ).Message, text );
		var badMagic = (byte[])good.Clone();
		badMagic[0] ^= 1;
		Rejects( badMagic, "'PCLT'" );
		var badTable = (byte[])good.Clone();
		badTable[8] = 0x67;
		Rejects( badTable, "pool table" );
		var badTemplates = (byte[])good.Clone();
		badTemplates[12 + 0x9E68] = 0x5F;
		Rejects( badTemplates, "template table" );
		Rejects( ParticlesBlock( pool, TemplateTable(), new byte[0x800 * 52 + 1] ), "overruns its heap" );
		Rejects( ParticlesBlock( PoolTable( new[] { 3 }, table => { Emitter( table, 3, 1, 1, 5, 0, "Sparks" ); BitConverter.TryWriteBytes( table.AsSpan( 0xC ), 0u ); } ), TemplateTable(), new byte[1] ), "overruns its heap" );
		for ( var cut = 0; cut < good.Length; cut += 997 )
			Assert.ThrowsException<InvalidDataException>( () => SaveParticlesBlock.Parse( good.AsSpan( 0, cut ), 0 ), $"truncated at {cut}" );
		Assert.ThrowsException<InvalidDataException>( () => SaveParticlesBlock.Parse( good.AsSpan( 0, good.Length - 1 ), 0 ) );
	}

	[TestMethod]
	public void ParticlesBlockRefusesBrokenSlotLists()
	{
		var loop = PoolTable( new[] { 3, 4 }, table => BitConverter.TryWriteBytes( table.AsSpan( SaveParticlesBlock.EmittersOffset + 4 * 320 + 0xD0 ), (short)3 ) );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SaveParticlesBlock.Parse( ParticlesBlock( loop, TemplateTable() ), 0 ) ).Message, "twice" );
		var lost = PoolTable( new[] { 3 }, table => BitConverter.TryWriteBytes( table.AsSpan( SaveParticleGlobals.TailOffset + 4 ), (short)-1 ) );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SaveParticlesBlock.Parse( ParticlesBlock( lost, TemplateTable() ), 0 ) ).Message, "partition" );
		var flag = PoolTable( new[] { 3 }, table => BitConverter.TryWriteBytes( table.AsSpan( SaveParticlesBlock.EmittersOffset + 3 * 320 ), (ushort)0 ) );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => SaveParticlesBlock.Parse( ParticlesBlock( flag, TemplateTable() ), 0 ) ).Message, "not in use" );
	}

	[TestMethod]
	public void ParticlesBlockWidthMatchesTheBlockWidthRule()
	{
		var bytes = ParticlesBlock( PoolTable( Array.Empty<int>() ), TemplateTable(), new byte[104] );
		Assert.IsTrue( SaveBlockWidths.TryMeasure( bytes, SaveBlock.Particles, 0, out var width ) );
		Assert.AreEqual( SaveParticlesBlock.Parse( bytes, 0 ).Length, width );
	}

	private static List<ParticleTemplate> BlankTemplates() => Enumerable.Range( 0, 105 ).Select( _ => new ParticleTemplate( new byte[320] ) ).ToList();

	private static List<ParticleEffectorTemplate> BlankEffectors() => Enumerable.Range( 0, 20 ).Select( _ => new ParticleEffectorTemplate( new byte[104] ) ).ToList();

	[TestMethod]
	public void ParticlesBlockRestoresIntoTheRuntimePoolAndCapturesBack()
	{
		var pool = PoolTable( new[] { 14, 0 }, table =>
		{
			Emitter( table, 14, 35, 1484, 30, 5191, "Button" );
			Emitter( table, 0, 20, 1, 12, 34068, "WaterFall", at => BitConverter.TryWriteBytes( table.AsSpan( at + 0x44 ), 640 ) );
		}, new[] { 2 } );
		// effector 2 is a copy of effector template 7 (the id is not stored in the slot)
		var templates = TemplateTable();
		var effectorAt = SaveParticlesBlock.EffectorsOffset + 2 * 104;
		Array.Copy( templates, 105 * 320 + 7 * 104, pool, effectorAt, 104 );
		BitConverter.TryWriteBytes( pool.AsSpan( effectorAt ), 1u );
		BitConverter.TryWriteBytes( pool.AsSpan( effectorAt + 0x60 ), (short)-1 );
		BitConverter.TryWriteBytes( pool.AsSpan( effectorAt + 0x62 ), (short)-1 );
		BitConverter.TryWriteBytes( pool.AsSpan( effectorAt + 8 ), (ushort)9 );
		BitConverter.TryWriteBytes( pool.AsSpan( effectorAt + 0x4C ), 40 );
		var block = SaveParticlesBlock.Parse( ParticlesBlock( pool, templates ), 0 );
		var state = block.ToSystemState( BlankTemplates() )!;
		Assert.IsNull( ParticleSystem.ValidateState( state ) );
		CollectionAssert.AreEqual( new[] { 14, 0 }, state.Emitters.Select( emitter => emitter.Slot ).ToArray() );
		Assert.AreEqual( 7, state.Effectors.Single().EffectorId );
		Assert.AreEqual( 40, state.Effectors.Single().Life );
		Assert.IsNull( state.Emitters[0].VolumeExtent, "a blank template has a zero extent, as the slot does" );
		CollectionAssert.AreEqual( new[] { 640, 0, 0 }, state.Emitters[1].VolumeExtent!.ToArray(), "a changed extent is kept" );
		var system = new ParticleSystem( BlankTemplates(), effectors: BlankEffectors() );
		system.Restore( state );
		var captured = system.Capture()!;
		Assert.AreEqual( JsonSerializer.Serialize( state ), JsonSerializer.Serialize( captured ) );
	}

	[TestMethod]
	public void ParticlesBlockWithAnEffectorOfNoSavedTemplateCannotBeConverted()
	{
		var pool = PoolTable( Array.Empty<int>(), table => BitConverter.TryWriteBytes( table.AsSpan( SaveParticlesBlock.EffectorsOffset + 104 * 4 ), 1u ), new[] { 4 } );
		var block = SaveParticlesBlock.Parse( ParticlesBlock( pool, TemplateTable( table => { for ( var id = 0; id < 20; id++ ) BitConverter.TryWriteBytes( table.AsSpan( 105 * 320 + id * 104 + 0x40 ), 5u ); } ) ), 0 );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => block.ToSystemState( BlankTemplates() ) ).Message, "matches 0" );
	}
}
