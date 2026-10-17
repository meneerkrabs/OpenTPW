using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The RSE text form: opcode-table agreement with the VM, authoring without game data, error positions and the
/// byte-exact corpus roundtrip (skipped without OPENTPW_GAME_PATH).
/// </summary>
[TestClass]
public class RseAssemblerTests
{
	private const string RideLoop = @"
; A ride loop: a visitor boards, the ride waits, the visitor leaves.
.stack 8
.timeslice 50
.limbo 2
.var VAR_VISITOR
.var VAR_TICKS

	NAME ""Pool Ride""
	COPY VAR_TICKS 0
loop:
	ADDHEAD VAR_VISITOR
	WAIT 500
	DELHEAD VAR_VISITOR
	WAIT 0x01F4
	BRANCH @loop
";

	private sealed class RecordingEffects : IRideScriptEffects
	{
		public readonly List<Opcode> Calls = new();

		public int Perform( RideEffectCall call )
		{
			Calls.Add( call.Opcode );
			return 0;
		}
	}

	private static RideScriptFile Read( byte[] data ) => new( new MemoryStream( data ) );

	private static RideScriptAssemblyException Fails( string text )
		=> Assert.ThrowsException<RideScriptAssemblyException>( () => RideScriptAssembler.Assemble( text ) );

	[TestMethod]
	public void OpcodeNamesAgreeWithTheVmTable()
	{
		foreach ( var opcode in Enum.GetValues<Opcode>() )
		{
			Assert.AreEqual( opcode.ToString(), RideScriptOpcodeNames.GetName( (ushort)opcode ) );
			Assert.IsTrue( RideScriptOpcodeNames.TryParse( opcode.ToString(), out var parsed ) );
			Assert.AreEqual( (ushort)opcode, parsed );
			Assert.AreEqual( RideScriptAnalysis.GetOpcodeName( (ushort)opcode ), RideScriptOpcodeNames.GetName( (ushort)opcode ) );
		}
		// every opcode the VM handles is reachable by name, and a name never maps to a different number
		foreach ( var definition in RideOpcodes.Definitions.Values )
		{
			Assert.IsTrue( RideScriptOpcodeNames.TryParse( definition.Opcode.ToString(), out var value ) );
			Assert.AreEqual( (ushort)definition.Opcode, value );
		}
		Assert.IsTrue( RideScriptOpcodeNames.TryParse( "OP_200", out var unnamed ) );
		Assert.AreEqual( (ushort)200, unnamed );
		Assert.AreEqual( "OP_200", RideScriptOpcodeNames.GetName( 200 ) );
		Assert.IsFalse( RideScriptOpcodeNames.TryParse( "NOPE", out _ ) );
	}

	[TestMethod]
	public void HandWrittenScriptAssemblesAndReadsBack()
	{
		var script = Read( RideScriptAssembler.Assemble( RideLoop ) );
		Assert.AreEqual( 8, script.StackSize );
		Assert.AreEqual( 50, script.TimeSlice );
		Assert.AreEqual( 2, script.LimboSize );
		CollectionAssert.AreEqual( new[] { "VAR_VISITOR", "VAR_TICKS" }, script.VariableNames.ToArray() );
		Assert.AreEqual( "Pool Ride", script.Strings[0] );
		CollectionAssert.AreEqual(
			new ushort[] { (ushort)Opcode.NAME, (ushort)Opcode.COPY, (ushort)Opcode.ADDHEAD, (ushort)Opcode.WAIT, (ushort)Opcode.DELHEAD, (ushort)Opcode.WAIT, (ushort)Opcode.BRANCH },
			script.Instructions.Select( x => x.Opcode ).ToArray() );
		// the label is the ADDHEAD at word 5 (NAME 2 words, COPY 3 words)
		Assert.AreEqual( new RideScriptOperand( RideScriptOperandKind.Branch, 5 ), script.Instructions[^1].Operands[0] );
		Assert.AreEqual( new RideScriptOperand( RideScriptOperandKind.Literal, 0x1F4 ), script.Instructions[5].Operands[0] );
	}

	[TestMethod]
	public void HandWrittenScriptRunsInTheVm()
	{
		var effects = new RecordingEffects();
		var executed = new List<Opcode>();
		var vm = new RideVM( Read( RideScriptAssembler.Assemble( RideLoop ) ), new RideVMOptions { Effects = effects, Seed = 1 } );
		vm.InstructionExecuting = ( _, instruction ) => executed.Add( (Opcode)instruction.Opcode );
		for ( var step = 0; step < 400; ++step )
		{
			vm.Advance( 1.0 / 60 );
			Assert.AreNotEqual( RideVMState.Faulted, vm.State, vm.FaultMessage );
		}
		// 400 steps are 6.7 s: at least two full board/wait/leave/wait cycles
		Assert.IsTrue( effects.Calls.Count >= 4 );
		for ( var index = 0; index < effects.Calls.Count; ++index )
			Assert.AreEqual( index % 2 == 0 ? Opcode.ADDHEAD : Opcode.DELHEAD, effects.Calls[index] );
		CollectionAssert.AreEqual( new[] { Opcode.NAME, Opcode.COPY, Opcode.ADDHEAD, Opcode.WAIT }, executed.Take( 4 ).ToArray() );
		Assert.IsTrue( executed.Count( x => x == Opcode.BRANCH ) >= 2 );
	}

	[TestMethod]
	public void DisassemblyOfAnAssembledScriptReassemblesToTheSameBytes()
	{
		var bytes = RideScriptAssembler.Assemble( RideLoop );
		var text = RideScriptDisassembler.Disassemble( bytes );
		CollectionAssert.AreEqual( bytes, RideScriptAssembler.Assemble( text ) );
		Assert.AreEqual( text, RideScriptDisassembler.Disassemble( RideScriptAssembler.Assemble( text ) ) );
		StringAssert.Contains( text, "L5:" );
		StringAssert.Contains( text, "BRANCH @L5" );
	}

	[TestMethod]
	public void ExplicitStringTablePreservesOrderDuplicatesAndUnreferencedEntries()
	{
		const string source = @"
.var A
.string ""unused""
.string ""b""
.string ""a""
.string ""b""
	NAME ""a""
	SPAWNCHILD ""b""
	SPAWNCHILD #3
";
		var bytes = RideScriptAssembler.Assemble( source );
		var script = Read( bytes );
		Assert.AreEqual( "unused", script.Strings[0] );
		Assert.AreEqual( 4, script.Strings.Count );
		// blob layout: "unused\0" (0..6), "b\0" (7), "a\0" (9), "b\0" (11); a text maps to its first entry, #3 reaches the second "b"
		CollectionAssert.AreEqual( new ushort[] { 9, 7, 11 }, script.Instructions.Select( x => x.Operands[0].Value ).ToArray() );
		var text = RideScriptDisassembler.Disassemble( bytes );
		StringAssert.Contains( text, "#3" );
		CollectionAssert.AreEqual( bytes, RideScriptAssembler.Assemble( text ) );
	}

	[TestMethod]
	public void NamesThatAreNotIdentifiersAndDuplicatesUseQuotesAndIndices()
	{
		const string source = ".var \"odd name\"\n.var dup\n.var dup\n\tTEST $0\n\tTEST $2\n";
		var bytes = RideScriptAssembler.Assemble( source );
		var text = RideScriptDisassembler.Disassemble( bytes );
		StringAssert.Contains( text, ".var \"odd name\"" );
		StringAssert.Contains( text, "TEST $2" );
		CollectionAssert.AreEqual( bytes, RideScriptAssembler.Assemble( text ) );
	}

	[TestMethod]
	[DataRow( (ushort)32767 )]
	[DataRow( (ushort)32768 )]
	[DataRow( (ushort)65535 )]
	public void HighOpcodeNumbersRoundtripFromBinaryFixtures( ushort opcode )
	{
		var original = RseScriptTests.CreateScript( new[] { 0x8000_0000u | opcode, 5u, 0x8000_0000u | opcode }, Array.Empty<string>(), Array.Empty<string>() );
		var text = RideScriptDisassembler.Disassemble( original );
		StringAssert.Contains( text, $"OP_{opcode}" );
		CollectionAssert.AreEqual( original, RideScriptAssembler.Assemble( text ) );
	}

	[TestMethod]
	public void EmptyVariableNamesRoundtrip()
	{
		var original = RseScriptTests.CreateScript( new[] { 0x8000_0000u | 38, 0x4000_0000u | 1 }, Array.Empty<string>(), new[] { "", "x", "" } );
		var text = RideScriptDisassembler.Disassemble( original );
		StringAssert.Contains( text, ".var \"\"" );
		CollectionAssert.AreEqual( original, RideScriptAssembler.Assemble( text ) );
		var empty = RideScriptAssembler.Assemble( ".var \"\"\n\tTEST $0\n" );
		Assert.AreEqual( "", Read( empty ).VariableNames[0] );
	}

	[TestMethod]
	public void NegativeAndHexLiteralsAssembleToSixteenBitWords()
	{
		var script = Read( RideScriptAssembler.Assemble( "\tCOPY 0 -1\n\tCOPY 0xFFFF 65535\n" ) );
		Assert.AreEqual( (ushort)0xFFFF, script.Instructions[0].Operands[1].Value );
		Assert.AreEqual( (ushort)0xFFFF, script.Instructions[1].Operands[0].Value );
		Assert.AreEqual( (ushort)0xFFFF, script.Instructions[1].Operands[1].Value );
		Assert.AreEqual( "0xFFFF", RideScriptDisassembler.Disassemble( script ).Split( '\n' ).First( x => x.Contains( "COPY" ) ).Split( ' ' )[2] );
	}

	[TestMethod]
	public void ErrorsCarryLineAndColumn()
	{
		void Check( string text, int line, int column, string fragment )
		{
			var exception = Fails( text );
			Assert.AreEqual( line, exception.Line, exception.Message );
			Assert.AreEqual( column, exception.Column, exception.Message );
			StringAssert.Contains( exception.Message, fragment );
			StringAssert.StartsWith( exception.Message, $"line {line}, column {column}:" );
		}
		Check( "\tNOP\n\tFROBNICATE 1\n", 2, 2, "unknown opcode 'FROBNICATE'" );
		Check( ".var A\n\tTEST B\n", 2, 7, "undeclared variable 'B'" );
		Check( "\tBRANCH @nowhere\n", 1, 9, "undefined label 'nowhere'" );
		Check( "a:\n\tNOP\na:\n", 3, 1, "already defined on line 1" );
		Check( "\tNAME \"open\n", 1, 7, "unterminated string" );
		Check( "\tNAME \"bad\\q\"\n", 1, 11, "unknown escape" );
		Check( "\tCOPY 0 70000\n", 1, 9, "does not fit 16 bits" );
		Check( "\tCOPY 0 12abc\n", 1, 9, "not a valid number" );
		Check( ".stack\n", 1, 1, "exactly one argument" );
		Check( ".stack 1\n.stack 2\n", 2, 1, "already set on line 1" );
		Check( ".bogus 1\n", 1, 1, "unknown directive" );
		Check( ".var 9bad\n", 1, 6, "not a valid variable name" );
		Check( "\tBRANCH @end\nend:\n", 1, 9, "must be followed by an instruction" );
		Check( "\tTEST $4\n", 1, 7, "not a declared variable index" );
		Check( "\"x\"\n", 1, 1, "expected an opcode" );
		Check( "\tNAME #0\n", 1, 7, ".string table" );
		Check( ".string \"a\"\n\tNAME \"b\"\n", 2, 7, "not in the .string table" );
		Check( "\tNAME \"\u20AC\"\n", 1, 7, "Latin-1" );
	}

	[TestMethod]
	public void BranchTargetsMustBeLabelsNotLiterals()
	{
		// a literal operand is just a literal; only @label produces a branch word
		var script = Read( RideScriptAssembler.Assemble( "\tBRANCH 0\n" ) );
		Assert.AreEqual( RideScriptOperandKind.Literal, script.Instructions[0].Operands[0].Kind );
	}

	[TestMethod]
	public void AuthoredFileGetsDocumentedDefaults()
	{
		var script = Read( RideScriptAssembler.Assemble( "\tNOP\n" ) );
		Assert.AreEqual( RideScriptAssembler.DefaultStackSize, script.StackSize );
		Assert.AreEqual( RideScriptAssembler.DefaultTimeSlice, script.TimeSlice );
		Assert.AreEqual( RideScriptAssembler.DefaultLimboSize, script.LimboSize );
		Assert.AreEqual( RideScriptAssembler.DefaultBounceSize, script.BounceSize );
		Assert.AreEqual( RideScriptAssembler.DefaultWalkSize, script.WalkSize );
		Assert.AreEqual( 50, script.TimeSlice );
	}

	[TestMethod]
	public void CorpusRoundtripsByteForByte()
	{
		var corpus = RseScriptTests.LoadCorpusBytes();
		Assert.AreEqual( 308, corpus.Count );
		var failures = new List<string>();
		foreach ( var (member, original) in corpus )
		{
			try
			{
				var text = RideScriptDisassembler.Disassemble( original );
				var rebuilt = RideScriptAssembler.Assemble( text );
				if ( !rebuilt.AsSpan().SequenceEqual( original ) )
					failures.Add( $"{member}: bytes differ" );
				else if ( RideScriptDisassembler.Disassemble( rebuilt ) != text )
					failures.Add( $"{member}: text is not stable" );
			}
			catch ( Exception exception )
			{
				failures.Add( $"{member}: {exception.Message}" );
			}
		}
		Assert.AreEqual( 0, failures.Count, string.Join( "\n", failures.Take( 10 ) ) );
	}
}
