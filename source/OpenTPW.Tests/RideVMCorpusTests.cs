using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Runs the original RSE corpus in the VM (inconclusive without OPENTPW_GAME_PATH).
/// Set OPENTPW_RSE_VM_REPORT_OUT=&lt;file&gt; to write the run report.
/// </summary>
[TestClass]
public class RideVMCorpusTests
{
	private const int Ticks = 60 * FixedStepClock.TicksPerSecond;

	/// <summary>
	/// Scripts started by SPAWNCHILD/SPAWNSOUND in their own archive run as children. Unreferenced scripts that read
	/// their parent (GETVARINPARENT) are orphans: nothing in the corpus starts them, so they are not run as roots.
	/// </summary>
	private static (Dictionary<string, byte[]> Corpus, string[] Roots, HashSet<string> Children, string[] Orphans) LoadRoots()
	{
		var corpus = RseScriptTests.LoadCorpusBytes();
		var children = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var needsParent = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (name, data) in corpus )
		{
			var script = new RideScriptFile( new MemoryStream( data ) );
			foreach ( var instruction in script.Instructions.Where( x => x.Opcode is (ushort)Opcode.SPAWNCHILD or (ushort)Opcode.SPAWNSOUND ) )
				children.Add( Archive( name ) + script.Strings[instruction.Operands[0].Value] );
			if ( script.Instructions.Any( x => x.Opcode is (ushort)Opcode.GETVARINPARENT or (ushort)Opcode.SETVARINPARENT ) )
				needsParent.Add( name );
		}
		var unreferenced = corpus.Keys.Where( x => !children.Contains( x ) ).OrderBy( x => x, StringComparer.Ordinal ).ToArray();
		return (corpus, unreferenced.Where( x => !needsParent.Contains( x ) ).ToArray(), children, unreferenced.Where( needsParent.Contains ).ToArray());
	}

	private static string Archive( string member ) => member[..(member.LastIndexOf( '/' ) + 1)];

	private static RideVM Create( Dictionary<string, byte[]> corpus, string name, IRideScriptEffects? effects, int seed, RideScriptWorld? world = null )
	{
		var archive = Archive( name );
		var vm = new RideVM( new RideScriptFile( new MemoryStream( corpus[name] ) ), new RideVMOptions
		{
			Effects = effects,
			Seed = seed,
			World = world,
			SourceName = name,
			ResolveScript = file => corpus.TryGetValue( archive + file, out var data ) ? new RideScriptFile( new MemoryStream( data ) ) : null
		} );
		var capacity = vm.GetVariableIndex( "VAR_CAPACITY" );
		if ( capacity >= 0 )
			vm.Variables[capacity] = 6;
		return vm;
	}

	private static IEnumerable<RideVM> Tree( RideVM vm )
	{
		yield return vm;
		foreach ( var child in new[] { vm.Child, vm.SoundChild }.Where( x => x != null ).SelectMany( x => Tree( x! ) ) )
			yield return child;
	}

	/// <summary>Random results for every effect and random host changes to the common variables (stress, not gameplay).</summary>
	private sealed class ChaosEffects : IRideScriptEffects
	{
		private readonly Random random;

		public ChaosEffects( int seed ) => random = new Random( seed );

		public int Perform( RideEffectCall call )
		{
			if ( call.Opcode is Opcode.TRIGANIM or Opcode.WAITANIM or Opcode.TRIGWAITANIM or Opcode.TRIGANIMSPEED )
				return random.Next( 0, 3000 );
			if ( call.Opcode is Opcode.BUMP or Opcode.COAST or Opcode.TOUR && call.IsVariable( 1 ) && random.Next( 2 ) == 0 )
				call.SetOutput( 1, random.Next( 0, 4 ) );
			return random.Next( -1, 4 );
		}

		public void Poke( RideVM vm )
		{
			if ( vm.InCriticalSection )
				return;
			foreach ( var (name, maximum, oneIn) in new[] { ("VAR_RIDECLOSED", 1, 16), ("VAR_BREAKSTAT", 1, 16), ("VAR_WORN", 1, 8), ("VAR_LETMEON", 5, 2), ("VAR_LETMEOFF", 5, 4), ("VAR_TRIGGER", 3, 8), ("VAR_COMMAND", 4, 8) } )
			{
				var index = vm.GetVariableIndex( name );
				if ( index >= 0 && random.Next( oneIn ) == 0 )
					vm.Variables[index] = random.Next( 0, maximum + 1 );
			}
		}
	}

	[TestMethod]
	public void AllOriginalScriptsRunInTheVmWithoutFaults()
	{
		var (corpus, roots, children, orphans) = LoadRoots();
		Assert.AreEqual( 308, corpus.Count );
		Assert.AreEqual( 44, children.Count, "28 EventMap.rse + 16 SPAWNCHILD targets" );
		CollectionAssert.AreEqual( new[] { "space/rides/creature.wad/CreatureFX.RSE" }, orphans, "a copy of hoverbot's \"Driod Anim Child\" that Creature.RSE never spawns" );
		Assert.AreEqual( 263, roots.Length );

		var report = new StringBuilder();
		var executed = new SortedSet<Opcode>();
		var spawned = new SortedSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var chaos in new[] { false, true } )
		{
			var hooks = new SortedDictionary<Opcode, (int Scripts, long Calls)>();
			var states = new SortedDictionary<RideVMState, int>();
			var instructions = 0L;
			// Every root runs in one shared world, in lockstep, like scripts in one park (bus.RSE finds the traffic lights).
			var world = new RideScriptWorld();
			var chaosEffects = roots.Select( ( _, index ) => chaos ? new ChaosEffects( index + 1 ) : null ).ToArray();
			var vms = roots.Select( ( name, index ) => Create( corpus, name, chaosEffects[index], index + 1, world ) ).ToArray();
			Action<RideVM, Instruction> trace = ( _, instruction ) => executed.Add( instruction.Opcode );
			foreach ( var vm in vms )
				vm.InstructionExecuting = trace;
			for ( var tick = 0; tick < Ticks; ++tick )
			{
				for ( var index = 0; index < vms.Length; ++index )
				{
					if ( chaos && tick % 30 == 0 )
						chaosEffects[index]!.Poke( vms[index] );
					vms[index].Advance( RideVMTests.Tick );
					foreach ( var script in Tree( vms[index] ).Where( x => x.InstructionExecuting == null ) )
					{
						script.InstructionExecuting = trace;
						spawned.Add( script.SourceName );
					}
				}
			}
			foreach ( var vm in vms )
			{
				foreach ( var script in Tree( vm ) )
				{
					Assert.AreNotEqual( RideVMState.Faulted, script.State, $"{script.SourceName}: {script.FaultMessage}" );
					Assert.AreNotEqual( RideVMState.Halted, script.State, script.SourceName );
					instructions += script.ExecutedInstructions;
					foreach ( var (opcode, count) in script.UnimplementedEffects )
						hooks[opcode] = hooks.TryGetValue( opcode, out var total ) ? (total.Scripts + 1, total.Calls + count) : (1, count);
				}
				states[vm.State] = states.TryGetValue( vm.State, out var stateCount ) ? stateCount + 1 : 1;
			}
			foreach ( var vm in vms )
				vm.Stop();
			Assert.AreEqual( 0, world.Scripts.Count );
			report.Append( $"{(chaos ? "chaos effects" : "default effects")}: {roots.Length} root scripts in one world × {Ticks} ticks, {instructions} instructions, states {string.Join( " ", states.Select( x => $"{x.Key}={x.Value}" ) )}\n" );
			report.Append( "  unimplemented effects (scripts/calls): " + string.Join( ", ", hooks.Select( x => $"{x.Key} {x.Value.Scripts}/{x.Value.Calls}" ) ) + "\n" );
			if ( !chaos )
			{
				// With no visitors and no effect results every script reaches some hooked effect.
				Assert.IsTrue( hooks.Count > 20 );
				Assert.IsTrue( hooks.ContainsKey( Opcode.EVENT ) && hooks.ContainsKey( Opcode.WAITANIM ) );
			}
		}
		var histogram = RseScriptTests.CorpusHistogram.Split( ',' ).Select( x => (Opcode)int.Parse( x.Split( ':' )[0] ) ).ToArray();
		var neverExecuted = histogram.Where( x => !executed.Contains( x ) ).ToArray();
		report.Append( $"opcodes executed: {executed.Count} of {histogram.Length}; never reached: {string.Join( " ", neverExecuted )}\n" );
		report.Append( $"child scripts started: {spawned.Count} of {children.Count}: {string.Join( " ", spawned )}\n" );
		report.Append( $"orphan child scripts not run: {string.Join( " ", orphans )}\n" );
		var output = Environment.GetEnvironmentVariable( "OPENTPW_RSE_VM_REPORT_OUT" );
		if ( !string.IsNullOrEmpty( output ) )
			File.WriteAllText( output, report.ToString() );
		Console.Write( report.ToString() );
		Assert.IsTrue( executed.Count >= 83, report.ToString() );
	}

	private sealed class TraceEffects : IRideScriptEffects
	{
		public readonly StringBuilder Trace = new();

		public int Perform( RideEffectCall call )
		{
			Trace.Append( $"{(int)call.VM.TimeMilliseconds} {call}\n" );
			return UnimplementedRideScriptEffects.Instance.Perform( call );
		}
	}

	// 30 s of the open Inca Totem with no visitors, capacity 6, seed 1234 and every effect unimplemented:
	// WAITANIM 0 at start, a 10 s passenger time-out, WAIT 700, one random TRIGANIM_CH set via JSR,
	// WAIT 500, the main-animation cycle, then back to the passenger loop because VAR_ONRIDE is 0.
	private const string TotemTrace =
		"16 WAITANIM 0 0\n" +
		"10766 TRIGANIM_CH 5 2 0 1\n" +
		"10766 TRIGANIM_CH 5 5 0 2\n" +
		"10766 TRIGANIM_CH 5 8 0 3\n" +
		"11283 TRIGANIM 5 0 0\n" +
		"11283 ADDOBJ 3 -1 73 5\n" +
		"11283 ADDOBJ 3 -1 90 5\n" +
		"11283 ADDOBJ 3 -1 74 2\n" +
		"11283 ADDOBJ 3 -1 75 2\n" +
		"16599 EVENT 3 -1 76\n" +
		"16599 KILLOBJ 2\n" +
		"17599 SETREVERB 3\n" +
		"17599 KILLOBJ 5\n" +
		"17599 EVENT 3 -1 77\n" +
		"17849 SINGLESCREAM 0 100\n" +
		"17849 EVENT 3 -1 78\n" +
		"19850 ADDOBJ 3 -1 73 5\n" +
		"19850 ADDOBJ 3 -1 90 5\n" +
		"19850 ADDOBJ 3 -1 74 5\n" +
		"19850 ADDOBJ 3 -1 75 5\n" +
		"23350 SETREVERB 0\n" +
		"23350 KILLOBJ 5\n" +
		"23350 EVENT 3 -1 76\n";

	[TestMethod]
	public void TotemScriptTraceIsPinned()
	{
		var corpus = RseScriptTests.LoadCorpusBytes();
		var effects = new TraceEffects();
		var vm = Create( corpus, "jungle/rides/totem.wad/Totem.RSE", effects, 1234 );
		var running = new List<int>();
		for ( var tick = 0; tick < 30 * FixedStepClock.TicksPerSecond; ++tick )
		{
			vm.Advance( RideVMTests.Tick );
			if ( running.Count == 0 || running[^1] != vm[RideVariables.VAR_RUNNING] )
				running.Add( vm[RideVariables.VAR_RUNNING] );
		}
		Assert.AreEqual( "Inca Totem", vm.ScriptName );
		Assert.AreEqual( RideVMState.Running, vm.State );
		Assert.AreEqual( TotemTrace, effects.Trace.ToString() );
		CollectionAssert.AreEqual( new[] { 0, 1, 0 }, running );
		CollectionAssert.AreEquivalent( new[] { Opcode.WAITANIM, Opcode.TRIGANIM_CH, Opcode.TRIGANIM, Opcode.ADDOBJ, Opcode.EVENT, Opcode.KILLOBJ, Opcode.SETREVERB, Opcode.SINGLESCREAM }, vm.UnimplementedEffects.Keys.ToArray() );

		var closed = Create( corpus, "jungle/rides/totem.wad/Totem.RSE", effects = new TraceEffects(), 1234 );
		closed[RideVariables.VAR_RIDECLOSED] = 1;
		for ( var tick = 0; tick < 30 * FixedStepClock.TicksPerSecond; ++tick )
			closed.Advance( RideVMTests.Tick );
		Assert.AreEqual( "16 WAITANIM 0 0\n", effects.Trace.ToString(), "a closed Totem idles in its ENDSLICE loop" );
		Assert.AreEqual( 0, closed[RideVariables.VAR_RUNNING] );
	}
}
