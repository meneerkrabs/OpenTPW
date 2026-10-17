using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Keeps <see cref="RideOpcodeTestMap"/> honest (docs/M4-GATE.md, row vm.opcodes): every entry's unit test is run
/// under a <see cref="RideOpcodeRecorder"/> and must execute its opcode (through OpenTPW's game effects when the entry
/// says so), and every opcode the asset-free script tests execute must have an entry. Set
/// OPENTPW_OPCODE_TEST_MAP_OUT=&lt;file&gt; to write the regenerated table.
/// </summary>
[TestClass]
public class RideOpcodeTestMapTests
{
	/// <summary>The asset-free test classes that run RSE scripts; the map may only name tests of these classes.</summary>
	internal static readonly Type[] ScriptTestClasses =
	{
		typeof( DeterminismTests ), typeof( GuestTests ), typeof( PathBuilderTests ), typeof( QueueTests ),
		typeof( RideMotionTests ), typeof( RideOpcodeEffectsTests ), typeof( RideVMInternalsTests ), typeof( RideVMTests ), typeof( RseScriptTests )
	};

	internal sealed record TestRecording( string Test, IReadOnlySet<Opcode> Executed, IReadOnlySet<Opcode> PerformedByRuntime, string? Outcome );

	/// <summary>Runs one test method (every data row) under a recorder; inconclusive and failing tests record their outcome instead.</summary>
	internal static TestRecording Record( MethodInfo method )
	{
		var executed = new HashSet<Opcode>();
		var performed = new HashSet<Opcode>();
		string? outcome = null;
		var rows = method.GetCustomAttributes<DataRowAttribute>().Select( row => row.Data ).ToList();
		if ( rows.Count == 0 )
			rows.Add( Array.Empty<object>() );
		foreach ( var arguments in rows )
		{
			var instance = Activator.CreateInstance( method.DeclaringType! );
			using var recorder = RideOpcodeRecorder.Start();
			try
			{
				method.Invoke( instance, arguments );
			}
			catch ( TargetInvocationException exception ) when ( exception.InnerException is AssertInconclusiveException )
			{
				outcome = "inconclusive";
			}
			catch ( TargetInvocationException exception )
			{
				outcome = $"failed: {exception.InnerException?.GetType().Name}";
			}
			executed.UnionWith( recorder.Executed.Keys );
			performed.UnionWith( recorder.EffectsPerformedByRuntime() );
		}
		return new TestRecording( $"{method.DeclaringType!.Name}.{method.Name}", executed, performed, outcome );
	}

	internal static IEnumerable<MethodInfo> TestMethods( Type type ) => type.GetMethods( BindingFlags.Public | BindingFlags.Instance )
		.Where( method => method.GetCustomAttribute<TestMethodAttribute>() != null ).OrderBy( method => method.Name, StringComparer.Ordinal );

	private static MethodInfo Resolve( string test )
	{
		var parts = test.Split( '.' );
		var type = ScriptTestClasses.SingleOrDefault( type => type.Name == parts[0] ) ?? throw new AssertFailedException( $"{test}: not a script test class" );
		return type.GetMethod( parts[1] ) ?? throw new AssertFailedException( $"{test}: no such test method" );
	}

	/// <summary>Every map entry's test executes its opcode; a runtime entry's opcode is performed by OpenTPW's game effects, never recorded as unimplemented.</summary>
	[TestMethod]
	public void EveryMappedTestExercisesItsOpcode()
	{
		var problems = new List<string>();
		foreach ( var group in RideOpcodeTestMap.Entries.GroupBy( entry => entry.Test ) )
		{
			var recording = Record( Resolve( group.Key ) );
			if ( recording.Outcome != null )
				problems.Add( $"{group.Key}: {recording.Outcome}" );
			foreach ( var entry in group )
			{
				var exercised = entry.ThroughRuntimeEffects ? recording.PerformedByRuntime.Contains( entry.Opcode ) : recording.Executed.Contains( entry.Opcode );
				if ( !exercised )
					problems.Add( $"{entry.Test} does not {(entry.ThroughRuntimeEffects ? "perform" : "execute")} {entry.Opcode}" );
			}
		}
		Assert.AreEqual( 0, problems.Count, string.Join( "\n", problems ) );
	}

	/// <summary>Every opcode the asset-free script tests execute (and every hooked one OpenTPW's effects perform there) has a map entry.</summary>
	[TestMethod]
	public void TheMapCoversEveryOpcodeTheScriptTestsExercise()
	{
		var all = ScriptTestClasses.SelectMany( TestMethods ).Select( Record ).ToList();
		var recordings = all.Where( recording => recording.Outcome == null ).ToList();
		var table = RideOpcodeTestMap.Format( Discover( recordings ) ) + string.Concat( all.Where( recording => recording.Outcome != null ).Select( recording => $"\t\t// not run: {recording.Test} ({recording.Outcome})\n" ) );
		var output = Environment.GetEnvironmentVariable( "OPENTPW_OPCODE_TEST_MAP_OUT" );
		if ( !string.IsNullOrWhiteSpace( output ) )
			File.WriteAllText( output, table );
		var mapped = RideOpcodeTestMap.Entries.Where( entry => !entry.ThroughRuntimeEffects ).Select( entry => entry.Opcode ).ToHashSet();
		var mappedRuntime = RideOpcodeTestMap.Entries.Where( entry => entry.ThroughRuntimeEffects ).Select( entry => entry.Opcode ).ToHashSet();
		var missing = recordings.SelectMany( recording => recording.Executed ).Distinct().Where( opcode => !mapped.Contains( opcode ) )
			.Concat( recordings.SelectMany( recording => recording.PerformedByRuntime ).Distinct().Where( opcode => !mappedRuntime.Contains( opcode ) ) )
			.OrderBy( opcode => opcode ).ToList();
		Assert.AreEqual( 0, missing.Count, $"Opcodes exercised by the script tests but missing from RideOpcodeTestMap: {string.Join( ", ", missing )}\n{table}" );
	}

	/// <summary>The first test (class, then method, ordinal) per opcode and per runtime-performed hooked opcode.</summary>
	internal static IReadOnlyList<(Opcode Opcode, string Test, bool ThroughRuntimeEffects)> Discover( IEnumerable<TestRecording> recordings )
	{
		var entries = new List<(Opcode Opcode, string Test, bool ThroughRuntimeEffects)>();
		var ordered = recordings.OrderBy( recording => recording.Test, StringComparer.Ordinal ).ToList();
		foreach ( var opcode in ordered.SelectMany( recording => recording.Executed ).Distinct().OrderBy( opcode => opcode ) )
			entries.Add( (opcode, ordered.First( recording => recording.Executed.Contains( opcode ) ).Test, false) );
		foreach ( var opcode in ordered.SelectMany( recording => recording.PerformedByRuntime ).Distinct().OrderBy( opcode => opcode ) )
			entries.Add( (opcode, ordered.First( recording => recording.PerformedByRuntime.Contains( opcode ) ).Test, true) );
		return entries;
	}

	/// <summary>The recorder sees the scripts of its own flow only, nested recorders take over and give back, and nothing is recorded once disposed.</summary>
	[TestMethod]
	public void RecorderCountsExecutedOpcodesOnItsOwnFlow()
	{
		var outer = RideOpcodeRecorder.Start();
		var vm = new RideVM( new RideScriptFile( new MemoryStream( new RideVMTests.Asm().Label( "loop" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" )
			.Build( new[] { "V0" } ) ) ), new RideVMOptions { Seed = 1 } );
		vm.Advance( 1 / 60.0 );
		using ( var inner = RideOpcodeRecorder.Start() )
		{
			vm.Advance( 1 / 60.0 );
			Assert.IsTrue( inner.Executed.ContainsKey( Opcode.ENDSLICE ), string.Join( ",", inner.Executed.Keys ) );
		}
		var before = outer.Executed.Values.Sum();
		vm.Advance( 1 / 60.0 );
		Assert.IsTrue( outer.Executed.Values.Sum() > before );
		outer.Dispose();
		var after = outer.Executed.Values.Sum();
		vm.Advance( 1 / 60.0 );
		Assert.AreEqual( after, outer.Executed.Values.Sum() );
		Assert.AreEqual( 1, outer.Machines.Count );
	}
}
