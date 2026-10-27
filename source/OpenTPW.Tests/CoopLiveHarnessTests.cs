using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-session] The live co-op acceptance's harness without a game (docs/COOP.md, "The live acceptance"): its steps run one at a time, each until its condition holds or
/// its deadline passes; the games and the driver meet through their status files; a failure anywhere ends the run with its evidence; and the run takes its credentials and
/// scenario from the environment only.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CoopLiveHarnessTests
{
	private string evidence = null!;
	private Logger previousLog = null!;
	private readonly List<string> completed = new();
	private readonly List<string> failed = new();

	[TestInitialize]
	public void Setup()
	{
		previousLog = Log;
		Log = new Logger();
		evidence = Path.Combine( Path.GetTempPath(), "opentpw-coop-live-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( evidence );
	}

	[TestCleanup]
	public void Cleanup()
	{
		Directory.Delete( evidence, true );
		Log = previousLog;
	}

	private static GameFlow Flow() => (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );

	private static Func<string, string?> Environment( params (string Name, string Value)[] extra )
	{
		var values = new Dictionary<string, string>
		{
			["OPENTPW_COOP_LIVE_SERVER"] = "http://127.0.0.1:5000",
			["OPENTPW_COOP_LIVE_NAME"] = "Hostabc1234",
			["OPENTPW_COOP_LIVE_PASSWORD"] = "secret-password-1",
			["OPENTPW_COOP_LIVE_CODE"] = "ABC123",
		};
		foreach ( var (name, value) in extra )
			values[name] = value;
		return name => values.TryGetValue( name, out var value ) ? value : null;
	}

	private CoopLiveTest Harness( bool hosting, Func<CoopLiveTest, IEnumerable<CoopLiveWait>> script, Func<string, string?>? environment = null, Func<double>? clock = null ) =>
		new( Flow(), evidence, hosting, environment ?? Environment(), completed.Add, failed.Add, script ) { Clock = clock };

	private JsonObject Json( string name ) => (JsonObject)JsonNode.Parse( File.ReadAllText( Path.Combine( evidence, name ) ) )!;

	[TestMethod]
	public void TheStepsRunInTurnEachUntilItsConditionHolds()
	{
		var (first, second) = (false, false);
		IEnumerable<CoopLiveWait> Script( CoopLiveTest test )
		{
			yield return CoopLiveTest.Wait( "the first", () => first, 30 );
			test.Publish( "said", "after the first" );
			yield return CoopLiveTest.Wait( "the second", () => second, 30 );
		}
		using var harness = Harness( hosting: true, Script );
		harness.Update();
		harness.Update();
		Assert.AreEqual( "the first", Json( "host-status.json" )["step"]!.GetValue<string>() );
		// the second's condition holding first changes nothing: the steps run in turn
		second = true;
		harness.Update();
		Assert.IsFalse( harness.Done );
		first = true;
		harness.Update();
		// the step after a held one starts in the same frame, and the script's own code between them runs then
		Assert.IsTrue( harness.Completed );
		Assert.AreEqual( 1, completed.Count );
		Assert.AreEqual( 0, failed.Count );
		var status = Json( "host-status.json" );
		Assert.AreEqual( "after the first", status["said"]!.GetValue<string>() );
		Assert.IsTrue( status["completed"]!.GetValue<bool>() );
		var written = Json( "host-evidence.json" );
		Assert.IsTrue( written["completed"]!.GetValue<bool>() );
		Assert.AreEqual( "host", written["role"]!.GetValue<string>() );
		Assert.AreEqual( "baseline", written["scenario"]!.GetValue<string>() );
		CollectionAssert.AreEqual( new[] { "the first", "the second" }, written["steps"]!.AsArray().Select( step => step!["What"]!.GetValue<string>() ).ToArray() );
		CollectionAssert.AreEqual( new[] { 4, 4 }, written["steps"]!.AsArray().Select( step => step!["Frame"]!.GetValue<int>() ).ToArray() );
		// nothing of the credentials reaches the evidence or the status
		foreach ( var file in Directory.GetFiles( evidence ) )
			Assert.IsFalse( File.ReadAllText( file ).Contains( "secret-password-1", StringComparison.Ordinal ), file );
		// a completed run does nothing more
		harness.Update();
		Assert.AreEqual( 1, completed.Count );
	}

	[TestMethod]
	public void AStepPastItsDeadlineFailsTheRunWithItsEvidence()
	{
		IEnumerable<CoopLiveWait> Script( CoopLiveTest test )
		{
			yield return CoopLiveTest.Wait( "the guest's join", () => false, 0.01 );
		}
		// the run's own clock: a slow first frame (a loaded machine writing the status) never passes the deadline before the test lets it
		var now = 0.0;
		using var harness = Harness( hosting: true, Script, clock: () => now );
		harness.Update();
		Assert.IsFalse( harness.Done );
		now = 0.05;
		harness.Update();
		Assert.IsTrue( harness.Done );
		Assert.IsFalse( harness.Completed );
		Assert.AreEqual( 0, completed.Count );
		Assert.AreEqual( 1, failed.Count );
		StringAssert.Contains( failed[0], "the guest's join did not happen within 0.01 s" );
		var written = Json( "host-evidence.json" );
		Assert.IsFalse( written["completed"]!.GetValue<bool>() );
		StringAssert.Contains( written["failure"]!.GetValue<string>(), "TimeoutException" );
		StringAssert.Contains( Json( "host-status.json" )["failed"]!.GetValue<string>(), "the guest's join" );
	}

	[TestMethod]
	public void TheOtherGamesFailureAndTheDriversStopEndTheRunAtOnce()
	{
		IEnumerable<CoopLiveWait> Script( CoopLiveTest test )
		{
			yield return CoopLiveTest.Wait( "the host's line", () => false, 600 );
		}
		File.WriteAllText( Path.Combine( evidence, "host-status.json" ), "{\"role\":\"host\",\"failed\":\"InvalidOperationException: no free line\"}" );
		using ( var guest = Harness( hosting: false, Script ) )
		{
			guest.Update();
			Assert.AreEqual( 1, failed.Count );
			StringAssert.Contains( failed[0], "the host failed: InvalidOperationException: no free line" );
		}
		// the guest's own failure is in its status now: a fresh pair of runs without either
		foreach ( var file in Directory.GetFiles( evidence ) )
			File.Delete( file );
		File.WriteAllText( Path.Combine( evidence, $"{CoopLiveTest.DriverRole}-status.json" ), "{\"abort\":\"the guest exited with 1\"}" );
		using var host = Harness( hosting: true, Script );
		host.Update();
		Assert.AreEqual( 2, failed.Count );
		StringAssert.Contains( failed[1], "the driver stopped the run: the guest exited with 1" );
	}

	[TestMethod]
	public void TheGamesReadEachOthersStatus()
	{
		static IEnumerable<CoopLiveWait> Idle( CoopLiveTest test )
		{
			yield return CoopLiveTest.Wait( "nothing", () => false, 600 );
		}
		var now = 0.0;
		using var host = Harness( hosting: true, Idle );
		using var guest = Harness( hosting: false, Idle, clock: () => now );
		Assert.IsNull( guest.StatusOf( "host", "code" ) );
		host.Publish( "code", "ABC123" );
		host.Publish( "hostAt", new JsonArray( 32.5, 17.5 ) );
		// a status is read again at most every quarter of a second
		Assert.IsNull( guest.StatusOf( "host", "code" ), "read again too soon" );
		now = 0.3;
		Assert.AreEqual( "ABC123", guest.StatusOf( "host", "code" )!.GetValue<string>() );
		Assert.AreEqual( 17.5, guest.StatusOf( "host", "hostAt" )![1]!.GetValue<double>() );
		Assert.IsNull( guest.StatusOf( CoopLiveTest.DriverRole, "restarted" ) );
	}

	[TestMethod]
	public void TheRunTakesItsCredentialsAndScenarioFromTheEnvironment()
	{
		static IEnumerable<CoopLiveWait> None( CoopLiveTest test ) => Array.Empty<CoopLiveWait>();
		var missingPassword = Environment( ("OPENTPW_COOP_LIVE_PASSWORD", "") );
		Assert.ThrowsException<ArgumentException>( () => Harness( hosting: true, None, missingPassword ) );
		var missingCode = Environment( ("OPENTPW_COOP_LIVE_CODE", "") );
		Harness( hosting: true, None, missingCode ).Dispose();
		Assert.ThrowsException<ArgumentException>( () => Harness( hosting: false, None, missingCode ) );
		Assert.ThrowsException<ArgumentException>( () => Harness( hosting: true, None, Environment( ("OPENTPW_COOP_LIVE_SCENARIO", "longer") ) ) );
		Assert.ThrowsException<ArgumentException>( () => Harness( hosting: true, None, Environment( ("OPENTPW_COOP_LIVE_SCENARIO", "endurance") ) ) );
		Assert.ThrowsException<ArgumentException>( () => Harness( hosting: true, None, Environment( ("OPENTPW_COOP_LIVE_SCENARIO", "endurance"), ("OPENTPW_COOP_LIVE_MINUTES", "-3") ) ) );
		using var endurance = Harness( hosting: true, None, Environment( ("OPENTPW_COOP_LIVE_SCENARIO", "endurance"), ("OPENTPW_COOP_LIVE_MINUTES", "2.5") ) );
		endurance.Update();
		Assert.IsTrue( endurance.Completed );
		var written = Json( "host-evidence.json" );
		Assert.AreEqual( "endurance", written["scenario"]!.GetValue<string>() );
		Assert.AreEqual( 2.5, written["enduranceMinutes"]!.GetValue<double>() );
		// [EXT:online-coop-commands] the management scenario (C4b-2) needs no minutes
		using var management = Harness( hosting: true, None, Environment( ("OPENTPW_COOP_LIVE_SCENARIO", "management") ) );
		management.Update();
		Assert.IsTrue( management.Completed );
		Assert.AreEqual( "management", Json( "host-evidence.json" )["scenario"]!.GetValue<string>() );
		Assert.ThrowsException<ArgumentException>( () => CoopLiveTest.Run( Flow(), "watch", evidence ) );
	}

	[TestMethod]
	public void APageRelaysOnlyItsOwnStatusAndEvidenceOutAndTheOthersIn()
	{
		Assert.IsTrue( CoopLiveTest.PageMayRead( "guest", "guest-status.json" ) );
		Assert.IsTrue( CoopLiveTest.PageMayRead( "guest", "guest-evidence.json" ) );
		foreach ( var name in new[] { "host-status.json", "host-evidence.json", "driver-status.json", "guest-host.png", "../guest-status.json", "guest-status.json.tmp" } )
			Assert.IsFalse( CoopLiveTest.PageMayRead( "guest", name ), name );
		Assert.IsFalse( CoopLiveTest.PageMayRead( CoopLiveTest.DriverRole, "driver-status.json" ) );
		Assert.IsTrue( CoopLiveTest.PageMayWrite( "guest", "host-status.json" ) );
		Assert.IsTrue( CoopLiveTest.PageMayWrite( "guest", "driver-status.json" ) );
		Assert.IsTrue( CoopLiveTest.PageMayWrite( "host", "guest-status.json" ) );
		foreach ( var name in new[] { "guest-status.json", "guest-evidence.json", "host-evidence.json", "/coop-live/host-status.json" } )
			Assert.IsFalse( CoopLiveTest.PageMayWrite( "guest", name ), name );
		Assert.IsFalse( CoopLiveTest.PageMayWrite( "host", "host-status.json" ) );
		Assert.IsFalse( CoopLiveTest.PageMayWrite( "", "host-status.json" ) );
	}

	[TestMethod]
	public void ThePageLiveModeIsForALoopbackPageWithNoOtherHarnessMode()
	{
		Assert.IsTrue( CoopLiveTest.IsPageAddress( "http://127.0.0.1:5000/index.html?coopLive=1&data=/game/&keep=off" ) );
		Assert.IsTrue( CoopLiveTest.IsPageAddress( "http://localhost:5000/?coopLive=1" ) );
		Assert.IsFalse( CoopLiveTest.IsPageAddress( "https://opentpw.example/?coopLive=1" ) );
		Assert.IsFalse( CoopLiveTest.IsPageAddress( "http://127.0.0.1:5000/?coopLive=0" ) );
		Assert.IsFalse( CoopLiveTest.IsPageAddress( null ) );
		foreach ( var other in new[] { "perf=1", "coopSmoke=1", "coopLinkSmoke=1" } )
			Assert.IsFalse( CoopLiveTest.IsPageAddress( $"http://127.0.0.1:5000/?coopLive=1&{other}" ), other );
	}

	[TestMethod]
	public void AThrowAfterTheRunCompletedInTheSameFrameIsItsFailure()
	{
		static IEnumerable<CoopLiveWait> None( CoopLiveTest test ) => Array.Empty<CoopLiveWait>();
		using var harness = Harness( hosting: false, None );
		harness.Update();
		Assert.IsTrue( harness.Completed );
		Assert.IsTrue( Json( "guest-status.json" )["completed"]!.GetValue<bool>() );
		// the page's frame threw after the script completed in it (the game's update or its drawing): the run did not pass
		harness.PageFailed( new InvalidOperationException( "the drawing failed" ) );
		Assert.IsFalse( harness.Completed );
		Assert.IsTrue( harness.Done );
		StringAssert.Contains( harness.Failure, "the drawing failed" );
		var status = Json( "guest-status.json" );
		Assert.IsNull( status["completed"] );
		StringAssert.Contains( status["failed"]!.GetValue<string>(), "the drawing failed" );
		Assert.IsFalse( Json( "guest-evidence.json" )["completed"]!.GetValue<bool>() );
		Assert.AreEqual( 1, failed.Count );
	}

	[TestMethod]
	public void ThePageRunTakesItsCredentialsFromItsOwnEnvironment()
	{
		var values = new Dictionary<string, string>
		{
			["OPENTPW_COOP_LIVE_SERVER"] = "http://127.0.0.1:5000",
			["OPENTPW_COOP_LIVE_NAME"] = "Guestabc1234",
			["OPENTPW_COOP_LIVE_PASSWORD"] = "secret-password-2",
			["OPENTPW_COOP_LIVE_CODE"] = "ABC123",
			["OPENTPW_COOP_LIVE_SCENARIO"] = "endurance",
			["OPENTPW_COOP_LIVE_MINUTES"] = "2",
		};
		Func<string, string?> page = name => values.TryGetValue( name, out var value ) ? value : null;
		using var harness = Harness( hosting: false, test => Array.Empty<CoopLiveWait>(), page );
		Assert.AreEqual( "guest", harness.Role );
		Assert.AreEqual( 60 + 2, harness.MaximumMinutes, 1e-9 );
		harness.Update();
		Assert.IsTrue( harness.Completed );
		foreach ( var file in Directory.GetFiles( evidence ) )
			Assert.IsFalse( File.ReadAllText( file ).Contains( "secret-password-2", StringComparison.Ordinal ), file );
	}
}
