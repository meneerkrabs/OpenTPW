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

	/// <summary>
	/// Default effects for scripts that have an object without any clips: every animation trigger returns the
	/// missing-member result (1000 ms) and leaves the channel on AnimID 12, as the original trigger does for an object
	/// without that member; every other effect stays unimplemented. Without any host a script has no object and its
	/// first WAITANIM waits INT32_MAX ms (TotemWithoutAnObjectWaitsOnItsFirstWaitAnim).
	/// </summary>
	private sealed class ClipLessObjectEffects : IRideScriptEffects, IRideAnimationQuery
	{
		public int Perform( RideEffectCall call )
		{
			if ( call.Opcode is Opcode.TRIGANIM or Opcode.WAITANIM or Opcode.LOOPANIM or Opcode.TRIGWAITANIM or Opcode.TRIGANIMSPEED
				or Opcode.TRIGANIM_CH or Opcode.WAITANIM_CH or Opcode.LOOPANIM_CH or Opcode.TRIGWAITANIM_CH )
				return ObjectAnimator.MissingClipMilliseconds;
			if ( call.Opcode is Opcode.GETANIM or Opcode.GETANIM_CH )
				return ObjectAnimator.NoAnimation;
			if ( call.Opcode is Opcode.FLUSHANIM or Opcode.FLUSHANIM_CH )
				return 0;
			return UnimplementedRideScriptEffects.Instance.Perform( call );
		}

		public int GetAnimationId( RideVM vm, int channel ) => ObjectAnimator.NoAnimation;
	}

	/// <summary>
	/// Random results for every effect and random host changes to the common variables (stress, not gameplay). Its
	/// channels show the AnimID last triggered on them, so TRIGWAITANIM(_CH) resumes.
	/// </summary>
	private sealed class ChaosEffects : IRideScriptEffects, IRideAnimationQuery
	{
		private readonly Random random;
		private readonly Dictionary<(RideVM, int), int> animationIds = new();

		public ChaosEffects( int seed ) => random = new Random( seed );

		public int GetAnimationId( RideVM vm, int channel ) => animationIds.TryGetValue( (vm, channel), out var id ) ? id : ObjectAnimator.NoAnimation;

		public int Perform( RideEffectCall call )
		{
			if ( call.Opcode is Opcode.TRIGWAITANIM or Opcode.TRIGWAITANIM_CH )
				animationIds[(call.VM, call.Opcode == Opcode.TRIGWAITANIM ? 0 : call.Argument( 3 ))] = call.Argument( 0 );
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
	public void OriginalCorpusUsesVariableCopyDestinationsAndLiteralRandBounds()
	{
		var corpus = RseScriptTests.LoadCorpusBytes();
		var copies = 0;
		var randoms = 0;
		foreach ( var (name, data) in corpus )
		{
			var script = new RideScriptFile( new MemoryStream( data ) );
			foreach ( var instruction in script.Instructions )
			{
				if ( instruction.Opcode == (ushort)Opcode.COPY )
				{
					++copies;
					Assert.AreEqual( RideScriptOperandKind.Variable, instruction.Operands[0].Kind, $"{name} COPY word{instruction.WordOffset}" );
				}
				if ( instruction.Opcode == (ushort)Opcode.RAND )
				{
					++randoms;
					Assert.AreEqual( RideScriptOperandKind.Literal, instruction.Operands[1].Kind, $"{name} RAND word{instruction.WordOffset}" );
					Assert.IsTrue( instruction.Operands[1].Value < 0x8000, "negative RAND bounds remain an unqualified native input domain" );
				}
			}
		}
		Assert.AreEqual( 1243, copies );
		Assert.AreEqual( 56, randoms );
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
			var vms = roots.Select( ( name, index ) => Create( corpus, name, chaos ? chaosEffects[index] : new ClipLessObjectEffects(), index + 1, world ) ).ToArray();
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
				// With no visitors and no clips every script reaches some hooked effect beyond the animation opcodes.
				Assert.IsTrue( hooks.Count > 20, report.ToString() );
				Assert.IsTrue( hooks.ContainsKey( Opcode.EVENT ) && executed.Contains( Opcode.WAITANIM ), report.ToString() );
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

	/// <summary>
	/// Totem.RSE with no animation host is a script without an object: its first WAITANIM gets ret 0 and, as in the
	/// original (0xafc74, unsigned wrap saturated by fctiwz), waits INT32_MAX ms, so nothing else happens in 30 s.
	/// </summary>
	[TestMethod]
	public void TotemWithoutAnObjectWaitsOnItsFirstWaitAnim()
	{
		var corpus = RseScriptTests.LoadCorpusBytes();
		var effects = new TraceEffects();
		var vm = Create( corpus, "jungle/rides/totem.wad/Totem.RSE", effects, 1234 );
		for ( var tick = 0; tick < 30 * FixedStepClock.TicksPerSecond; ++tick )
			vm.Advance( RideVMTests.Tick );
		Assert.AreEqual( "16 WAITANIM 0 0\n", effects.Trace.ToString() );
		Assert.AreEqual( RideVMState.Waiting, vm.State );
		Assert.AreEqual( 16d + int.MaxValue, vm.WakeTimeMilliseconds, 1 );
	}

	/// <summary>Hooked instructions a root script executes, as "ms OPCODE operand values" (values before the instruction runs).</summary>
	private static StringBuilder TraceHooked( RideVM vm )
	{
		var trace = new StringBuilder();
		vm.InstructionExecuting = ( script, instruction ) =>
		{
			if ( RideOpcodes.GetStatus( instruction.Opcode ) == RideOpcodeStatus.Hooked )
				trace.Append( $"{(int)script.TimeMilliseconds} {instruction.Opcode} {string.Join( " ", instruction.Operands.Select( x => x.Value ) )}".TrimEnd() + "\n" );
		};
		return trace;
	}

	// 30 s of the open Inca Totem placed as an original object (OriginalObjectRuntime: its catalog entry, model and
	// clips, OriginalObjectEffects as the animation host), capacity 6, no visitors, seed 1234. WAITANIM 0 0 plays
	// AnimID 0 and waits its length less 300 ms; then the 10 s passenger time-out, WAIT 700, a random TRIGANIM_CH set
	// via JSR at 14,749 ms, TRIGANIM 5 (the main clip) at 15,216 ms, the sound/object timeline to 26,216 ms, and
	// WAIT4ANIM, which waits for the deadline of the last trigger. Every wait is divided by the script speed (1.1). Hooked instructions are traced as
	// executed (WAIT4ANIM included), with operand values before the instruction runs.
	private const string HostedTotemTrace =
		"16 WAITANIM 0 0\n" +
		"14749 TRIGANIM_CH 5 2 0 1\n" +
		"14749 TRIGANIM_CH 5 5 0 2\n" +
		"14749 TRIGANIM_CH 5 8 0 3\n" +
		"15216 TRIGANIM 5 0 0\n" +
		"15216 ADDOBJ 3 -1 73 5\n" +
		"15216 ADDOBJ 3 -1 90 5\n" +
		"15216 ADDOBJ 3 -1 74 2\n" +
		"15216 ADDOBJ 3 -1 75 2\n" +
		"20050 EVENT 3 -1 76\n" +
		"20050 KILLOBJ 2\n" +
		"20966 SETREVERB 3\n" +
		"20966 KILLOBJ 5\n" +
		"20966 EVENT 3 -1 77\n" +
		"21200 SINGLESCREAM 0 100\n" +
		"21200 EVENT 3 -1 78\n" +
		"23033 ADDOBJ 3 -1 73 5\n" +
		"23033 ADDOBJ 3 -1 90 5\n" +
		"23033 ADDOBJ 3 -1 74 5\n" +
		"23033 ADDOBJ 3 -1 75 5\n" +
		"26216 SETREVERB 0\n" +
		"26216 KILLOBJ 5\n" +
		"26216 EVENT 3 -1 76\n" +
		"26216 WAIT4ANIM\n" +
		"27983 WAIT4ANIM\n";

	[TestMethod]
	public void TotemScriptTraceWithItsObjectIsPinned()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the object corpus." );
		var bonus = ObjectCatalog.BonusDataRoot;
		try
		{
			ObjectCatalog.BonusDataRoot = null;
			var entry = ObjectCatalog.Load( "jungle" ).Entries.Single( x => x.ScriptPath != null && x.ScriptPath.EndsWith( "Totem.RSE", StringComparison.OrdinalIgnoreCase ) );
			var runtime = new OriginalObjectRuntime( entry, seed: 1234, open: true );
			var vm = runtime.Script!;
			runtime.SetVariable( nameof( RideVariables.VAR_CAPACITY ), 6 );
			var trace = TraceHooked( vm );
			var running = new List<int>();
			for ( var tick = 0; tick < 30 * FixedStepClock.TicksPerSecond; ++tick )
			{
				runtime.Simulate( RideVMTests.Tick );
				if ( running.Count == 0 || running[^1] != vm[RideVariables.VAR_RUNNING] )
					running.Add( vm[RideVariables.VAR_RUNNING] );
			}
			Assert.AreEqual( "Inca Totem", vm.ScriptName );
			Assert.AreEqual( HostedTotemTrace, trace.ToString() );
			CollectionAssert.AreEqual( new[] { 0, 1, 0 }, running );
			Assert.AreEqual( 5, runtime.AnimationsPlayed, "WAITANIM 0, three TRIGANIM_CH and TRIGANIM 5 all found their clips" );
			Assert.AreEqual( 0, runtime.UnresolvedAnimations.Count );
			Assert.AreEqual( 0, vm.UnimplementedEffects.Count, "every Totem effect is served: the held sounds, the events, the screams and the particles" );

			// Closed, the same object plays its opening clip and then idles in its ENDSLICE loop: VAR_RUNNING stays 0.
			var closed = new OriginalObjectRuntime( entry, seed: 1234, open: true );
			closed.SetVariable( nameof( RideVariables.VAR_RIDECLOSED ), 1 );
			closed.SetVariable( nameof( RideVariables.VAR_CAPACITY ), 6 );
			var closedTrace = TraceHooked( closed.Script! );
			for ( var tick = 0; tick < 30 * FixedStepClock.TicksPerSecond; ++tick )
			{
				closed.Simulate( RideVMTests.Tick );
				Assert.AreEqual( 0, closed.Script![RideVariables.VAR_RUNNING], "a closed Totem never runs" );
			}
			Assert.AreEqual( "16 WAITANIM 0 0\n", closedTrace.ToString(), "a closed Totem idles in its ENDSLICE loop" );
			Assert.AreEqual( RideVMState.Running, closed.Script!.State, "past the opening clip's wait, idling" );
			Assert.IsFalse( closed.IsOpen );
		}
		finally
		{
			ObjectCatalog.BonusDataRoot = bonus;
			FileSystem = previous;
		}
	}
}
