using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic scripts per opcode family. Original-corpus runs live in <see cref="RideVMCorpusTests"/>.</summary>
[TestClass]
public class RideVMTests
{
	internal const double Tick = 1d / 60;

	/// <summary>Minimal assembler: ints are literals, <see cref="V"/>/<see cref="S"/> are tagged words, strings are branch labels.</summary>
	internal sealed class Asm
	{
		private readonly List<uint> words = new();
		private readonly Dictionary<string, int> labels = new();
		private readonly List<(int Word, string Label)> fixups = new();

		public Asm Label( string name )
		{
			labels.Add( name, words.Count );
			return this;
		}

		public Asm I( Opcode opcode, params object[] operands )
		{
			words.Add( 0x8000_0000 | (uint)opcode );
			foreach ( var operand in operands )
			{
				switch ( operand )
				{
					case int literal:
						words.Add( (ushort)(short)literal );
						break;
					case uint word:
						words.Add( word );
						break;
					case string label:
						fixups.Add( (words.Count, label) );
						words.Add( 0 );
						break;
					default:
						throw new ArgumentException( operand?.ToString() );
				}
			}
			return this;
		}

		public byte[] Build( string[] variables, params string[] strings )
		{
			foreach ( var (word, label) in fixups )
				words[word] = 0x2000_0000 | (uint)labels[label];
			return RseScriptTests.CreateScript( words.ToArray(), strings, variables );
		}
	}

	internal static uint V( int index ) => 0x4000_0000 | (uint)index;
	internal static uint S( int offset ) => 0x1000_0000 | (uint)offset;

	private static readonly string[] Vars = { "V0", "V1", "V2", "V3" };

	private static RideVM Load( Asm asm, RideVMOptions? options = null, string[]? variables = null, params string[] strings )
		=> new( new RideScriptFile( new MemoryStream( asm.Build( variables ?? Vars, strings ) ) ), options ?? new RideVMOptions { Seed = 1 } );

	private static void Run( RideVM vm, int ticks )
	{
		for ( var tick = 0; tick < ticks; ++tick )
			vm.Advance( Tick );
	}

	private sealed class StubEffects : IRideScriptEffects
	{
		public readonly List<string> Calls = new();
		public Func<RideEffectCall, int> Result = _ => 0;

		public int Perform( RideEffectCall call )
		{
			Calls.Add( call.ToString() );
			return Result( call );
		}
	}

	[TestMethod]
	public void LoadsVariablesStringsAndCodeWordBranchTargets()
	{
		var vm = Load( new Asm()
			.I( Opcode.NAME, S( 0 ) )
			.I( Opcode.COPY, V( 3 ), 5 )
			.Label( "loop" )
			.I( Opcode.ENDSLICE )
			.I( Opcode.BRANCH, "loop" ), null, null, "Test Ride" );
		Assert.AreEqual( 4, vm.Variables.Length );
		Assert.AreEqual( 3, vm.GetVariableIndex( "V3" ) );
		// NAME = words 0-1, COPY = 2-4, ENDSLICE = 5, BRANCH = 6-7: the label is code word 5, instruction 2.
		Assert.AreEqual( 5, vm.Instructions[3].Operands[0].Raw );
		Assert.AreEqual( 2, vm.Instructions[3].Operands[0].BranchTarget );
		Assert.AreEqual( "BRANCH label_5", vm.Instructions[3].ToString() );
		Run( vm, 3 );
		Assert.AreEqual( "Test Ride", vm.ScriptName );
		Assert.AreEqual( 5, vm.Variables[3] );
		Assert.AreEqual( RideVMState.Running, vm.State );
		Assert.AreEqual( 3, vm.ProgramCounter, "parked after ENDSLICE, before BRANCH" );
	}

	[TestMethod]
	public void ArithmeticWritesDestinationFirstAndSignExtendsLiterals()
	{
		var vm = Load( new Asm()
			.I( Opcode.COPY, V( 0 ), 10 )
			.I( Opcode.SUB, V( 1 ), V( 0 ), 3 )
			.I( Opcode.ADD, V( 0 ), 0xFFFF )
			.I( Opcode.DIV, V( 2 ), V( 0 ), 2 )
			.I( Opcode.MOD, V( 3 ), V( 0 ), 4 )
			.I( Opcode.MOD, 0, V( 0 ), 3 )
			.Label( "idle" )
			.I( Opcode.ENDSLICE )
			.I( Opcode.BRANCH, "idle" ) );
		Run( vm, 1 );
		CollectionAssert.AreEqual( new[] { 9, 7, 4, 1 }, vm.Variables );
		Assert.AreEqual( RideVM.VMFlags.Zero, vm.Flags, "MOD 0 V0 3 sets flags for 9 % 3 and discards the result" );
		Assert.AreEqual( 0, vm.Instructions[5].Operands[0].Value, "writes to a literal destination are discarded" );
		Assert.AreEqual( -1, vm.Instructions[2].Operands[1].Value );
	}

	[DataTestMethod]
	[DataRow( -5, 3 )]
	[DataRow( 0, 1 )]
	[DataRow( 7, 2 )]
	public void ConditionalBranchesUseZeroAndStrictSignFlags( int value, int expectedPath )
	{
		var vm = Load( new Asm()
			.I( Opcode.TEST, V( 0 ) )
			.I( Opcode.BRANCH_Z, "zero" )
			.I( Opcode.BRANCH_PV, "positive" )
			.I( Opcode.BRANCH_NV, "negative" )
			.I( Opcode.COPY, V( 1 ), 99 )
			.I( Opcode.BRANCH, "idle" )
			.Label( "zero" ).I( Opcode.COPY, V( 1 ), 1 ).I( Opcode.BRANCH, "idle" )
			.Label( "positive" ).I( Opcode.COPY, V( 1 ), 2 ).I( Opcode.BRANCH, "idle" )
			.Label( "negative" ).I( Opcode.COPY, V( 1 ), 3 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ) );
		vm.Variables[0] = value;
		Run( vm, 1 );
		Assert.AreEqual( expectedPath, vm.Variables[1] );
	}

	[TestMethod]
	public void CmpSubtractsAndBranchNzIsTheInverseOfZero()
	{
		var results = new List<int>();
		foreach ( var value in new[] { 4, 5, 3, 0 } )
		{
			var copy = Load( new Asm()
				.I( Opcode.CMP, V( 0 ), 4 )
				.I( Opcode.BRANCH_PV, "greater" )
				.I( Opcode.BRANCH_NZ, "less" )
				.I( Opcode.COPY, V( 1 ), 1 ).I( Opcode.BRANCH, "idle" )
				.Label( "greater" ).I( Opcode.COPY, V( 1 ), 2 ).I( Opcode.BRANCH, "idle" )
				.Label( "less" ).I( Opcode.COPY, V( 1 ), 3 )
				.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ) );
			copy.Variables[0] = value;
			Run( copy, 1 );
			results.Add( copy.Variables[1] );
		}
		CollectionAssert.AreEqual( new[] { 1, 2, 3, 3 }, results, "4 & 4 would be nonzero under the upstream AND reading" );
	}

	[TestMethod]
	public void RandIsInclusiveSeededAndSetsFlags()
	{
		RideVM Make() => Load( new Asm()
			.Label( "loop" )
			.I( Opcode.RAND, V( 0 ), 2 )
			.I( Opcode.ENDSLICE )
			.I( Opcode.BRANCH, "loop" ), new RideVMOptions { Seed = 42 } );
		var first = Make();
		var second = Make();
		var seen = new List<int>();
		for ( var tick = 0; tick < 200; ++tick )
		{
			first.Advance( Tick );
			second.Advance( Tick );
			Assert.AreEqual( first.Variables[0], second.Variables[0] );
			Assert.AreEqual( first.Variables[0] == 0, first.Flags.HasFlag( RideVM.VMFlags.Zero ) );
			seen.Add( first.Variables[0] );
		}
		CollectionAssert.AreEquivalent( new[] { 0, 1, 2 }, seen.Distinct().ToArray() );
	}

	[TestMethod]
	public void JsrAndReturnAreLifoAndBoundedByTheDeclaredStack()
	{
		var vm = Load( new Asm()
			.I( Opcode.JSR, "outer" )
			.I( Opcode.COPY, V( 0 ), V( 1 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Label( "outer" ).I( Opcode.ADD, V( 1 ), 1 ).I( Opcode.JSR, "inner" ).I( Opcode.ADD, V( 1 ), 10 ).I( Opcode.RETURN )
			.Label( "inner" ).I( Opcode.ADD, V( 1 ), 100 ).I( Opcode.RETURN ) );
		Run( vm, 1 );
		Assert.AreEqual( 111, vm.Variables[0] );
		Assert.AreEqual( 0, vm.CallDepth );

		var recursive = Load( new Asm().Label( "self" ).I( Opcode.JSR, "self" ) );
		Run( recursive, 1 );
		Assert.AreEqual( RideVMState.Faulted, recursive.State );
		StringAssert.Contains( recursive.FaultMessage, "stack size 4" );

		var stray = Load( new Asm().I( Opcode.RETURN ) );
		Run( stray, 1 );
		Assert.AreEqual( RideVMState.Faulted, stray.State );
	}

	[TestMethod]
	public void SliceBudgetPreemptsBusyLoopsButNotCriticalSections()
	{
		var busy = Load( new Asm().Label( "loop" ).I( Opcode.ADD, V( 0 ), 1 ).I( Opcode.BRANCH, "loop" ) );
		Run( busy, 1 );
		Assert.AreEqual( 25, busy.Variables[0], "header time slice 50 = 50 instructions per tick" );
		Run( busy, 1 );
		Assert.AreEqual( 50, busy.Variables[0] );

		var locked = Load( new Asm()
			.I( Opcode.CRIT_LOCK )
			.Label( "loop" ).I( Opcode.ADD, V( 0 ), 1 ).I( Opcode.CMP, V( 0 ), 100 ).I( Opcode.BRANCH_NZ, "loop" )
			.I( Opcode.CRIT_UNLOCK )
			.Label( "spin" ).I( Opcode.ADD, V( 1 ), 1 ).I( Opcode.BRANCH, "spin" ) );
		Run( locked, 1 );
		Assert.AreEqual( 100, locked.Variables[0] );
		Assert.IsFalse( locked.InCriticalSection );
		Assert.AreEqual( 0, locked.Variables[1], "the overdue budget preempts right after CRIT_UNLOCK" );

		var runaway = Load( new Asm().I( Opcode.CRIT_LOCK ).Label( "loop" ).I( Opcode.BRANCH, "loop" ) );
		Run( runaway, 1 );
		Assert.AreEqual( RideVMState.Faulted, runaway.State );
	}

	[TestMethod]
	public void EndSliceYieldsOncePerTick()
	{
		var vm = Load( new Asm().Label( "loop" ).I( Opcode.ADD, V( 0 ), 1 ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" ) );
		Run( vm, 7 );
		Assert.AreEqual( 7, vm.Variables[0] );
		Assert.AreEqual( 7, vm.SliceCount );
	}

	[TestMethod]
	public void WaitCountsMillisecondsOnTheFixedTickAndKeepsFlags()
	{
		var vm = Load( new Asm()
			.I( Opcode.TEST, V( 1 ) )
			.I( Opcode.WAIT, 500 )
			.I( Opcode.BRANCH_Z, "zero" )
			.I( Opcode.COPY, V( 0 ), 2 ).I( Opcode.BRANCH, "idle" )
			.Label( "zero" ).I( Opcode.COPY, V( 0 ), 1 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ) );
		Run( vm, 1 );
		Assert.AreEqual( RideVMState.Waiting, vm.State );
		Assert.AreEqual( 1000d / 60 + 500, vm.WakeTimeMilliseconds, 1e-9 );
		Run( vm, 29 );
		Assert.AreEqual( 0, vm.Variables[0], "500 ms = 30 ticks have not passed" );
		Run( vm, 2 );
		Assert.AreEqual( 1, vm.Variables[0] );
	}

	[TestMethod]
	public void GetTimeAndTimerCountInMilliseconds()
	{
		var vm = Load( new Asm()
			.I( Opcode.GETTIME, V( 0 ) )
			.I( Opcode.SETTIMER, 100 )
			.Label( "loop" )
			.I( Opcode.GETTIMER, V( 1 ) )
			.I( Opcode.BRANCH_Z, "done" )
			.I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" )
			.Label( "done" ).I( Opcode.GETTIME, V( 2 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ) );
		Run( vm, 1 );
		Assert.AreEqual( 16, vm.Variables[0] );
		Assert.AreEqual( 100, vm.Variables[1] );
		Run( vm, 5 );
		Assert.AreEqual( 0, vm.Variables[2] );
		Assert.IsTrue( vm.Variables[1] > 0 );
		Run( vm, 2 );
		Assert.IsTrue( vm.Variables[2] >= 116 && vm.Variables[2] <= 134, vm.Variables[2].ToString() );
		Assert.AreEqual( 0, vm.Variables[1] );
	}

	[TestMethod]
	public void AnimationDurationsFromEffectsDriveOutputsAndWaits()
	{
		var effects = new StubEffects { Result = call => call.Opcode switch { Opcode.TRIGANIM => 1000, Opcode.WAITANIM => 250, _ => 0 } };
		var vm = Load( new Asm()
			.I( Opcode.TRIGANIM, 5, 0, V( 0 ) )
			.I( Opcode.WAIT4ANIM )
			.I( Opcode.COPY, V( 1 ), 1 )
			.I( Opcode.WAITANIM, 0, 0 )
			.I( Opcode.COPY, V( 1 ), 2 )
			.I( Opcode.TRIGANIM, 5, 1, 0 )
			.I( Opcode.FLUSHANIM )
			.I( Opcode.WAIT4ANIM )
			.I( Opcode.COPY, V( 1 ), 3 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), new RideVMOptions { Effects = effects } );
		Run( vm, 1 );
		Assert.AreEqual( 1000, vm.Variables[0] );
		Run( vm, 58 );
		Assert.AreEqual( 0, vm.Variables[1] );
		Run( vm, 3 );
		Assert.AreEqual( 1, vm.Variables[1] );
		Run( vm, 13 );
		Assert.AreEqual( 1, vm.Variables[1], "WAITANIM 250 ms" );
		var ticks = 0;
		while ( vm.Variables[1] == 1 && ticks++ < 10 )
			Run( vm, 1 );
		Assert.IsTrue( ticks is >= 1 and <= 3, ticks.ToString() );
		Assert.AreEqual( 2, vm.Variables[1], "WAITANIM done; WAIT4ANIM after FLUSHANIM only yields one slice" );
		Run( vm, 1 );
		Assert.AreEqual( 3, vm.Variables[1], "FLUSHANIM forgets pending animation time" );
		CollectionAssert.AreEqual( new[] { "TRIGANIM 5 0 0", "WAITANIM 0 0", "TRIGANIM 5 1 0", "FLUSHANIM" }, effects.Calls );
	}

	[TestMethod]
	public void DefaultEffectsAreRecordedAsUnimplementedAndReturnZero()
	{
		var vm = Load( new Asm()
			.I( Opcode.ADDOBJ, 3, 0xFFFF, 73, 5 )
			.I( Opcode.EVENT, 3, 0xFFFF, 76 )
			.I( Opcode.EVENT, 3, 0xFFFF, 77 )
			.I( Opcode.COPY, V( 0 ), 9 )
			.I( Opcode.WALKGET, V( 0 ) )
			.I( Opcode.BRANCH_Z, "none" )
			.I( Opcode.COPY, V( 1 ), 2 ).I( Opcode.BRANCH, "idle" )
			.Label( "none" ).I( Opcode.COPY, V( 1 ), 1 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ) );
		Run( vm, 1 );
		Assert.AreEqual( 0, vm.Variables[0] );
		Assert.AreEqual( 1, vm.Variables[1] );
		CollectionAssert.AreEquivalent( new[] { "ADDOBJ=1", "EVENT=2", "WALKGET=1" }, vm.UnimplementedEffects.Select( x => $"{x.Key}={x.Value}" ).ToArray() );
	}

	[TestMethod]
	public void CommandOpcodesLetEffectsWriteTheirParameterAndSetFlags()
	{
		var effects = new StubEffects
		{
			Result = call =>
			{
				if ( call.Opcode == Opcode.BUMP && call.Argument( 0 ) == 11 && call.IsVariable( 1 ) )
					call.SetOutput( 1, 3 );
				return call.Opcode == Opcode.BUMP ? 3 : 0;
			}
		};
		var vm = Load( new Asm()
			.I( Opcode.BUMP, 11, V( 0 ) )
			.I( Opcode.BRANCH_PV, "cars" )
			.I( Opcode.COPY, V( 1 ), 1 ).I( Opcode.BRANCH, "idle" )
			.Label( "cars" ).I( Opcode.COPY, V( 1 ), 2 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), new RideVMOptions { Effects = effects } );
		Run( vm, 1 );
		Assert.AreEqual( 3, vm.Variables[0] );
		Assert.AreEqual( 2, vm.Variables[1] );
		Assert.AreEqual( 0, vm.UnimplementedEffects.Count );
	}

	[TestMethod]
	public void ChildScriptsExchangeVariablesWithTheirParent()
	{
		var child = new Asm()
			.Label( "loop" ).I( Opcode.GETVARINPARENT, V( 0 ), 1 ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" )
			.Build( new[] { "C0", "C1" } );
		var sound = new Asm().I( Opcode.COPY, V( 0 ), 165 ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( new[] { "VAR_EVT0" } );
		var resolved = new List<string>();
		var options = new RideVMOptions
		{
			Seed = 1,
			ResolveScript = name =>
			{
				resolved.Add( name );
				return name switch
				{
					"child.rse" => new RideScriptFile( new MemoryStream( child ) ),
					"EventMap.rse" => new RideScriptFile( new MemoryStream( sound ) ),
					_ => null
				};
			}
		};
		// Strings: "child.rse" at 0, "EventMap.rse" at 10.
		var vm = Load( new Asm()
			.I( Opcode.SPAWNSOUND, S( 10 ) )
			.I( Opcode.SPAWNCHILD, S( 0 ) )
			.I( Opcode.SETVARINCHILD, 1, 7 )
			.Label( "loop" ).I( Opcode.GETVARINCHILD, V( 0 ), 0 ).I( Opcode.ENDSLICE ).I( Opcode.CMP, V( 2 ), 1 ).I( Opcode.BRANCH_NZ, "loop" )
			.I( Opcode.REMOVECHILD )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), options, null, "child.rse", "EventMap.rse" );
		vm.Variables[1] = 42;
		Run( vm, 2 );
		Assert.IsNotNull( vm.Child );
		Assert.AreSame( vm, vm.Child!.Parent );
		Assert.AreEqual( 7, vm.Child.Variables[1] );
		Assert.AreEqual( 42, vm.Child.Variables[0] );
		Assert.AreEqual( 42, vm.Variables[0] );
		Assert.AreEqual( 165, vm.SoundChild!.Variables[0] );
		Assert.AreEqual( 3, vm.World.Scripts.Count );
		vm.Variables[2] = 1;
		Run( vm, 1 );
		Assert.IsNull( vm.Child );
		Assert.IsNotNull( vm.SoundChild, "REMOVECHILD leaves the SPAWNSOUND slot alone" );
		Assert.AreEqual( 2, vm.World.Scripts.Count );
		CollectionAssert.AreEqual( new[] { "EventMap.rse", "child.rse" }, resolved );

		var missing = Load( new Asm().I( Opcode.SPAWNCHILD, S( 0 ) ), new RideVMOptions(), null, "child.rse" );
		Run( missing, 1 );
		Assert.AreEqual( RideVMState.Faulted, missing.State );
		var orphan = Load( new Asm().I( Opcode.GETVARINPARENT, V( 0 ), 1 ) );
		Run( orphan, 1 );
		Assert.AreEqual( RideVMState.Faulted, orphan.State );
	}

	[TestMethod]
	public void NamedScriptsShareVariablesThroughTheWorld()
	{
		var world = new RideScriptWorld();
		var lights = Load( new Asm().I( Opcode.NAME, S( 0 ) ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ),
			new RideVMOptions { World = world }, null, "Traffic Lights" );
		// Strings: "Traffic Lights" at 0, "Nothing" at 15.
		var bus = Load( new Asm()
			.I( Opcode.FINDSCRIPTRAND, S( 15 ), V( 2 ) )
			.I( Opcode.BRANCH_NZ, "idle" )
			.I( Opcode.FINDSCRIPTRAND, S( 0 ), V( 0 ) )
			.I( Opcode.BRANCH_Z, "idle" )
			.I( Opcode.SETREMOTEVAR, V( 0 ), 0, 5 )
			.I( Opcode.GETREMOTEVAR, V( 1 ), V( 0 ), 0 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), new RideVMOptions { World = world, Seed = 3 }, null, "Traffic Lights", "Nothing" );
		Run( lights, 1 );
		Run( bus, 1 );
		Assert.AreEqual( lights.ScriptId, bus.Variables[0] );
		Assert.AreEqual( 0, bus.Variables[2] );
		Assert.AreEqual( 5, lights.Variables[0] );
		Assert.AreEqual( 5, bus.Variables[1] );
		lights.Stop();
		Assert.IsNull( world.Find( lights.ScriptId ) );
	}

	[TestMethod]
	public void UnknownOpcodesDivisionByZeroAndRunningOffTheEndStopTheScript()
	{
		var unknown = Load( new Asm().I( Opcode.PUSH, 1 ) );
		Run( unknown, 1 );
		Assert.AreEqual( RideVMState.Faulted, unknown.State );
		StringAssert.Contains( unknown.FaultMessage, "PUSH" );

		var divide = Load( new Asm().I( Opcode.DIV, V( 0 ), 1, V( 1 ) ) );
		Run( divide, 1 );
		Assert.AreEqual( RideVMState.Faulted, divide.State );

		var end = Load( new Asm().I( Opcode.NOP ) );
		Run( end, 2 );
		Assert.AreEqual( RideVMState.Halted, end.State );

		Assert.ThrowsException<InvalidDataException>( () => Load( new Asm().I( Opcode.ADD, V( 0 ) ) ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => end.Advance( -1 ) );
	}

	[TestMethod]
	public void CoverageDocumentMatchesTheHandlerTable()
	{
		var histogram = RseScriptTests.CorpusHistogram.Split( ',' ).Select( x => x.Split( ':' ) ).ToDictionary( x => ushort.Parse( x[0] ), x => int.Parse( x[1] ) );
		Assert.IsTrue( histogram.Keys.All( x => RideOpcodes.GetStatus( (Opcode)x ) != RideOpcodeStatus.Unknown ), "every corpus opcode has a handler" );
		var corpusStatuses = histogram.Keys.GroupBy( x => RideOpcodes.GetStatus( (Opcode)x ) ).ToDictionary( x => x.Key, x => (Opcodes: x.Count(), Instructions: x.Sum( y => histogram[y] )) );
		Assert.AreEqual( (33, 7850), corpusStatuses[RideOpcodeStatus.Implemented] );
		Assert.AreEqual( (51, 4065), corpusStatuses[RideOpcodeStatus.Hooked] );

		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !File.Exists( Path.Combine( directory.FullName, "docs", "RSE-VM.md" ) ) )
			directory = directory.Parent;
		Assert.IsNotNull( directory, "docs/RSE-VM.md not found above the test directory" );
		var document = File.ReadAllText( Path.Combine( directory!.FullName, "docs", "RSE-VM.md" ) ).Replace( "\r\n", "\n" );
		StringAssert.Contains( document, RideOpcodes.FormatCoverageTable( histogram ) );
	}
}
