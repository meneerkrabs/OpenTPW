using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class RideCivilTimeRuntimeTests
{
	private static readonly string[] Variables = { "YEAR", "MONTH", "DAY", "HOUR", "RESULT" };
	private GameEdition previousEdition;

	[TestInitialize]
	public void Initialize()
	{
		previousEdition = GameEditions.Current;
		GameEditions.Select( GameEdition.ThemeParkWorld );
		Log ??= new();
	}

	[TestCleanup]
	public void Cleanup() => GameEditions.Select( previousEdition );

	[TestMethod]
	public void CurrentHostCivilFieldsReachTheRealObjectVmInsteadOfUnimplementedZeros()
	{
		using var fixture = new RuntimeFixture( ClockScript() );
		var runtime = fixture.Create( new RideScriptWorld() );
		try
		{
			var before = DateTime.Now;
			runtime.Simulate( FixedStepClock.TickDuration );
			var after = DateTime.Now;
			var values = runtime.Script!.Variables;
			Assert.IsTrue( values[0] == before.Year - 1900 || values[0] == after.Year - 1900, "YEAR reports tm_year, not the park display year" );
			Assert.IsTrue( values[1] == before.Month || values[1] == after.Month );
			Assert.IsTrue( values[2] == before.Day || values[2] == after.Day );
			Assert.IsTrue( values[3] == before.Hour || values[3] == after.Hour );
			Assert.AreEqual( 0, runtime.Script.UnimplementedEffects.Count );
			Assert.AreNotEqual( RideVMState.Faulted, runtime.Script.State );
		}
		finally { runtime.Stop(); }
	}

	private static Asm ClockScript() => new Asm().Label( "clock" )
		.I( Opcode.YEAR, V( 0 ) ).I( Opcode.MONTH, V( 1 ) ).I( Opcode.DAY, V( 2 ) ).I( Opcode.HOUR, V( 3 ) )
		.I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "clock" );

	[DataTestMethod]
	[DataRow( 0, 1 )]
	[DataRow( 23, 2 )]
	public void InjectedCivilFieldsPreserveLiteralDestinationAndZeroPositiveFlags( int hour, int marker )
	{
		var date = new DateTime( 2024, 2, 29, hour, 37, 58 );
		var reads = 0;
		var script = new Asm().I( Opcode.YEAR, V( 0 ) ).I( Opcode.MONTH, V( 1 ) ).I( Opcode.DAY, V( 2 ) )
			.I( Opcode.HOUR, 0 ).I( Opcode.BRANCH_Z, "zero" )
			.I( Opcode.COPY, V( 4 ), 2 ).I( Opcode.BRANCH, "end" )
			.Label( "zero" ).I( Opcode.COPY, V( 4 ), 1 ).Label( "end" ).I( Opcode.ENDSLICE );
		using var fixture = new RuntimeFixture( script );
		var runtime = fixture.Create( new RideScriptWorld( 0, () => { reads++; return date; } ) );
		try
		{
			runtime.Script!.Variables[3] = 99;
			runtime.Simulate( Tick );
			CollectionAssert.AreEqual( new[] { 124, 2, 29, 99, marker }, runtime.Script.Variables );
			Assert.AreEqual( 4, reads, "One sample per executed host query; literal HOUR still samples and sets flags." );
			Assert.AreEqual( 0, runtime.Script.UnimplementedEffects.Count );
		}
		finally { runtime.Stop(); }
	}

	[TestMethod]
	public void EachExecutedInstructionSamplesItsOwnHostInput()
	{
		var samples = new Queue<DateTime>( new[] { new DateTime( 2025, 12, 31, 23, 59, 59 ),
			new DateTime( 2026, 1, 1 ), new DateTime( 2026, 1, 2 ), new DateTime( 2026, 1, 2, 9, 0, 0 ) } );
		using var fixture = new RuntimeFixture( ClockScript() );
		var runtime = fixture.Create( new RideScriptWorld( 0, () => samples.Dequeue() ) );
		try
		{
			runtime.Simulate( Tick );
			CollectionAssert.AreEqual( new[] { 125, 1, 2, 9 }, runtime.Script!.Variables.Take( 4 ).ToArray() );
			Assert.AreEqual( 0, samples.Count );
		}
		finally { runtime.Stop(); }
	}

	[DataTestMethod]
	[DataRow( GameSpeed.Paused )]
	[DataRow( GameSpeed.Normal )]
	[DataRow( GameSpeed.Fast )]
	[DataRow( GameSpeed.Fastest )]
	public void SimulationSpeedAndSchedulerTimeDoNotReplaceHostCivilTime( GameSpeed speed )
	{
		var host = new DateTime( 2026, 10, 13, 3, 0, 0 );
		var reads = 0;
		var world = new RideScriptWorld( 0, () => { reads++; return host; } );
		using var fixture = new RuntimeFixture( ClockScript() );
		var runtime = fixture.Create( world );
		try
		{
			var fixedClock = new FixedStepClock();
			fixedClock.Advance( Tick, delta => runtime.Simulate( delta ) );
			CollectionAssert.AreEqual( new[] { 126, 10, 13, 3 }, runtime.Script!.Variables.Take( 4 ).ToArray() );
			host = new DateTime( 2034, 7, 2, 17, 0, 0 );
			world.ObserveClock( 100_000_000 );
			var slices = runtime.Script.SliceCount;
			var steps = fixedClock.Advance( Tick * GameSpeedScale.Of( speed ), delta => runtime.Simulate( delta ) );
			if ( speed == GameSpeed.Paused )
			{
				Assert.AreEqual( 0, steps );
				Assert.AreEqual( slices, runtime.Script.SliceCount );
				Assert.AreEqual( 4, reads, "A paused script does not query the advancing host clock." );
				Assert.AreEqual( 3, runtime.Script.Variables[3], "The previous result remains saved VM state until execution resumes." );
				fixedClock.Advance( Tick, delta => runtime.Simulate( delta ) );
			}
			else
				Assert.AreEqual( (int)speed, steps );
			CollectionAssert.AreEqual( new[] { 134, 7, 2, 17 }, runtime.Script.Variables.Take( 4 ).ToArray(),
				"The host year/hour are not the virtual park date or a scaled elapsed timestamp." );
		}
		finally { runtime.Stop(); }
	}

	[DataTestMethod]
	[DataRow( (int)GameEdition.ThemeParkInc )]
	[DataRow( 777 )]
	public void NonTpwWorldCannotEnableHostQueriesWithAnInjectedProviderOrLaterEditionChange( int edition )
	{
		GameEditions.Select( (GameEdition)edition );
		var reads = 0;
		var world = new RideScriptWorld( 0, () => { reads++; return new DateTime( 2026, 10, 13 ); } );
		GameEditions.Select( GameEdition.ThemeParkWorld );
		using var fixture = new RuntimeFixture( ClockScript() );
		var runtime = fixture.Create( world );
		try
		{
			runtime.Simulate( Tick );
			Assert.AreEqual( 0, reads );
			CollectionAssert.AreEqual( new[] { 0, 0, 0, 0 }, runtime.Script!.Variables.Take( 4 ).ToArray() );
			foreach ( var opcode in new[] { Opcode.YEAR, Opcode.MONTH, Opcode.DAY, Opcode.HOUR } )
				Assert.AreEqual( 1, runtime.Script.UnimplementedEffects[opcode], opcode.ToString() );
		}
		finally { runtime.Stop(); }
	}

	[TestMethod]
	public void MinuteAndSecondAreHostLocalCivilTimeLikeYearToHour()
	{
		// [BIN:TPW-EXE:0x005567D8 civil time opcodes] opcodes 97-102 all read localtime: MIN is tm_min, SEC is tm_sec.
		var reads = 0;
		using var fixture = new RuntimeFixture( new Asm().I( Opcode.MIN, V( 0 ) ).I( Opcode.SEC, V( 1 ) ).I( Opcode.ENDSLICE ) );
		var runtime = fixture.Create( new RideScriptWorld( 0, () => { reads++; return new DateTime( 2026, 10, 13, 8, 41, 17 ); } ) );
		try
		{
			runtime.Simulate( Tick );
			Assert.AreEqual( 2, reads );
			Assert.AreEqual( 41, runtime.Script!.Variables[0] );
			Assert.AreEqual( 17, runtime.Script.Variables[1] );
			Assert.AreEqual( 0, runtime.Script.UnimplementedEffects.Count );
		}
		finally { runtime.Stop(); }
	}

	[DataTestMethod]
	[DataRow( 0, 1 )]
	[DataRow( 5, 2 )]
	public void ALiteralMinuteOrSecondDestinationOnlySetsTheFlags( int second, int marker )
	{
		var script = new Asm().I( Opcode.SEC, 0 ).I( Opcode.BRANCH_Z, "zero" )
			.I( Opcode.COPY, V( 0 ), 2 ).I( Opcode.BRANCH, "end" )
			.Label( "zero" ).I( Opcode.COPY, V( 0 ), 1 ).Label( "end" ).I( Opcode.ENDSLICE );
		using var fixture = new RuntimeFixture( script );
		var runtime = fixture.Create( new RideScriptWorld( 0, () => new DateTime( 2026, 10, 13, 8, 41, second ) ) );
		try
		{
			runtime.Simulate( Tick );
			Assert.AreEqual( marker, runtime.Script!.Variables[0] );
		}
		finally { runtime.Stop(); }
	}

	[TestMethod]
	public void RestoreKeepsPriorResultsButFutureQueriesUseExternalInputAndRemainHashed()
	{
		var savedDate = new DateTime( 2026, 10, 13, 3, 0, 0 );
		var otherDate = new DateTime( 2027, 2, 5, 19, 0, 0 );
		using var fixture = new RuntimeFixture( ClockScript() );
		var originalWorld = new RideScriptWorld( 0, () => savedDate );
		var original = fixture.Create( originalWorld );
		OriginalObjectRuntime? same = null, changed = null;
		try
		{
			original.Simulate( Tick );
			var runtimeSnapshot = original.CaptureSnapshot();
			var worldSnapshot = JsonSerializer.Deserialize<RideScriptWorldSnapshot>( JsonSerializer.Serialize( originalWorld.CaptureSnapshot() ) )!;
			var sameReads = 0;
			var changedReads = 0;
			var sameWorld = new RideScriptWorld( 0, () => { sameReads++; return savedDate; } );
			var changedWorld = new RideScriptWorld( 0, () => { changedReads++; return otherDate; } );
			same = OriginalObjectRuntime.CreateForRestore( fixture.Entry, sameWorld, seed: 1, open: true, runtimeSnapshot.AttractionId );
			changed = OriginalObjectRuntime.CreateForRestore( fixture.Entry, changedWorld, seed: 1, open: true, runtimeSnapshot.AttractionId );
			same.RestoreSnapshot( runtimeSnapshot );
			changed.RestoreSnapshot( runtimeSnapshot );
			sameWorld.RestoreSnapshot( worldSnapshot, new Dictionary<int, RideVM> { [original.Script!.ScriptId] = same.Script! } );
			changedWorld.RestoreSnapshot( worldSnapshot, new Dictionary<int, RideVM> { [original.Script.ScriptId] = changed.Script! } );
			Assert.AreEqual( 0, sameReads + changedReads, "Capture/restore does not sample or reconstruct host time." );
			Assert.AreEqual( ScriptHash( same.Script! ), ScriptHash( changed.Script! ), "The provider itself is not canonical state." );
			CollectionAssert.AreEqual( new[] { 126, 10, 13, 3 }, changed.Script!.Variables.Take( 4 ).ToArray() );
			same.Simulate( Tick );
			changed.Simulate( Tick );
			CollectionAssert.AreEqual( new[] { 126, 10, 13, 3 }, same.Script!.Variables.Take( 4 ).ToArray() );
			CollectionAssert.AreEqual( new[] { 127, 2, 5, 19 }, changed.Script.Variables.Take( 4 ).ToArray() );
			Assert.AreEqual( 4, sameReads );
			Assert.AreEqual( 4, changedReads );
			Assert.AreNotEqual( ScriptHash( same.Script ), ScriptHash( changed.Script ), "Clock-derived variables remain hashed; no exclusion hides divergence." );
		}
		finally { original.Stop(); same?.Stop(); changed?.Stop(); }
	}

	private static ulong ScriptHash( RideVM vm )
	{
		var hash = new StateHasher();
		vm.AddCanonicalState( hash );
		return hash.Value;
	}

	private sealed class RuntimeFixture : IDisposable
	{
		private readonly string directory = Path.Combine( Path.GetTempPath(), "opentpw-civil-time-" + Guid.NewGuid().ToString( "N" ) );
		public ObjectCatalogEntry Entry { get; }

		public RuntimeFixture( Asm script )
		{
			Directory.CreateDirectory( directory );
			File.WriteAllBytes( Path.Combine( directory, "clock.md2" ), Md2ModelFileTests.CreateGeometry() );
			File.WriteAllBytes( Path.Combine( directory, "clock.rse" ), script.Build( Variables ) );
			var fileSystem = new BaseFileSystem( directory );
			var settings = new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1000\nInfo.Name Clock\nInfo.FixedItem 1" ) } );
			Entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Feature, "/clock.wad", settings, new ObjectShape( new[] { "*" } ),
				"/clock.md2", null, "/clock.rse", new[] { "/clock.rse" }, Array.Empty<ObjectAnimationFile>(),
				Array.Empty<string>(), new[] { "/clock.md2" }, fileSystem, null );
		}

		public OriginalObjectRuntime Create( RideScriptWorld world ) => new( Entry, world, seed: 1 );
		public void Dispose() => Directory.Delete( directory, recursive: true );
	}
}
