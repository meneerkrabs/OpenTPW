using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class PlayerProgressIngressIndependentTests
{
	private PlayerProgressIngressTests fixture = null!;
	private GameFlow Flow => Field<GameFlow>( fixture, "flow" );
	private FrontEndMenu Menu => Field<FrontEndMenu>( fixture, "menu" );
	private string Profile => Path.Combine( Field<string>( fixture, "root" ), PlayerTicketFile.FileName );
	private static T Field<T>( object target, string name ) => (T)target.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( target )!;
	private static void Set( object target, string name, object? value ) => target.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );
	private void Call( string method, params object[] args ) => fixture.GetType().GetMethod( method, BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture, args );
	private static void FlowCall( GameFlow flow, string method, params object[] args ) => typeof( GameFlow ).GetMethod( method, BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( flow, args );
	[TestInitialize] public void Setup() { fixture = new(); fixture.Initialize(); }
	[TestCleanup] public void Cleanup() => fixture.Cleanup();

	[TestMethod]
	public void RequiredDeclaredZeroCostAllowsKnownZeroWithoutBootstrapOrFileMutation()
	{
		Call( "Known", 0, ParkGameMode.FullSimulation ); Call( "WriteCost", "jungle", "0" ); var before = File.ReadAllBytes( Profile );
		Call( "Choose", "fullSimulation", "jungle" ); Call( "Pending" ); Assert.AreEqual( 1, Field<int>( fixture, "built" ) );
		CollectionAssert.AreEqual( before, File.ReadAllBytes( Profile ) ); Assert.AreEqual( 0, PlayerTicketFile.LoadIfPresent( Profile )!.ExtraKeys );
	}
	[TestMethod]
	public void DeclaredZeroCostNeverMigratesKnownInstantActionHistory()
	{
		Call( "Known", 0, ParkGameMode.InstantAction ); Call( "WriteCost", "jungle", "0" ); var before = File.ReadAllBytes( Profile );
		Call( "Choose", "fullSimulation", "jungle" ); Call( "Pending" ); Assert.AreEqual( 0, Field<int>( fixture, "built" ) ); CollectionAssert.AreEqual( before, File.ReadAllBytes( Profile ) );
	}
	[TestMethod]
	public void SameLogicalProgressWithDifferentRawBytesInvalidatesActualQueuedMenuAdmission()
	{
		Call( "Known", 1, ParkGameMode.FullSimulation ); var bytes = File.ReadAllBytes( Profile ); var state = PlayerTicketFile.ReadIfPresent( Profile )!;
		Assert.AreEqual( Convert.ToHexString( SHA256.HashData( bytes ) ), state.Digest );
		Call( "Choose", "fullSimulation", "jungle" ); var changed = bytes.Concat( new byte[] { 32, 9, 10 } ).ToArray(); File.WriteAllBytes( Profile, changed );
		Assert.AreEqual( state.Ledger.Keys, PlayerTicketFile.ReadIfPresent( Profile )!.Ledger.Keys );
		Call( "Pending" ); Assert.AreEqual( 0, Field<int>( fixture, "built" ) ); CollectionAssert.AreEqual( changed, File.ReadAllBytes( Profile ) );
	}
	[TestMethod]
	public void SelectedThemeMutationRefusesOldQueuedLeaseWithoutRetiringMountedScreen()
	{
		Call( "Known", 5, ParkGameMode.FullSimulation ); Call( "Choose", "fullSimulation", "jungle" ); var screen = Menu.Stack.Top; var before = File.ReadAllBytes( Profile );
		Menu.SelectLevel( "fantasy" ); Call( "Pending" ); Assert.AreEqual( 0, Field<int>( fixture, "built" ) ); Assert.AreSame( screen, Menu.Stack.Top ); CollectionAssert.AreEqual( before, File.ReadAllBytes( Profile ) );
	}
	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ActualCandidateHookRereadsCostAndProfileBeforePublication( bool changeProfile )
	{
		Action? commit = null; Flow.ProgressBuildOverride = ( _, _, callback ) => commit = callback;
		Call( "Choose", "fullSimulation", "jungle" ); Call( "Click", "choice0" ); Call( "Pending" ); Assert.IsNotNull( commit );
		if ( changeProfile ) { Call( "Known", 7, ParkGameMode.FullSimulation ); var before = File.ReadAllBytes( Profile ); Assert.ThrowsException<InvalidDataException>( () => commit!() ); CollectionAssert.AreEqual( before, File.ReadAllBytes( Profile ) ); }
		else { Call( "WriteCost", "jungle", "0" ); Assert.ThrowsException<InvalidDataException>( () => commit!() ); Assert.IsFalse( File.Exists( Profile ) ); }
	}
	[TestMethod]
	public void NewDigestReaderStillRefusesOversizeBeforePayloadAllocation()
	{
		Call( "Known", 1, ParkGameMode.FullSimulation ); PlayerTicketFile.ReadIfPresent( Profile );
		using ( var stream = File.OpenWrite( Profile ) ) stream.SetLength( 1024 * 1024 );
		var before = GC.GetAllocatedBytesForCurrentThread(); Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.ReadIfPresent( Profile ) ); var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.IsTrue( allocated < 128 * 1024, $"Oversize read allocated {allocated} bytes before refusing." );
	}
	[TestMethod]
	public void ExistingRuntimeMembershipCannotTurnHighLongAmountOrDetailIntoAnotherTicket()
	{
		Call( "Known", 1, ParkGameMode.FullSimulation ); var runtime = ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation ); var active = true;
		using var owner = new PlayerProgressRuntimeOwner( Profile, "jungle", PlayerTicketFile.ReadIfPresent( Profile )!, runtime, () => active, _ => Assert.Fail() );
		runtime.Economy.Objectives.Restore( 100, 0, null, new Dictionary<int, int>(), Array.Empty<int>(), new[] { GoldenTicketKind.Visitors } );
		var forward = typeof( ParkEconomyRuntime ).GetMethod( "ForwardEvent", BindingFlags.Instance | BindingFlags.NonPublic )!; var before = File.ReadAllBytes( Profile );
		foreach ( var amount in new[] { 1L << 32, long.MinValue } ) forward.Invoke( runtime, new object[] { new ParkEvent( 0, ParkEventKind.GoldenTicketWon, 0, 0, amount, "Visitors" ) } );
		Assert.AreEqual( 0, owner.Ledger.EarnedTickets ); CollectionAssert.AreEqual( before, File.ReadAllBytes( Profile ) );
		forward.Invoke( runtime, new object[] { new ParkEvent( 0, ParkEventKind.GoldenTicketWon, 99, 99, 0, "Happiness" ) } ); Assert.AreEqual( GoldenTicketKind.Visitors, PlayerTicketFile.LoadIfPresent( Profile )!.Capture().Earned.Single().Kind );
		var handlers = typeof( ParkEconomyRuntime ).GetField( "EventRaised", BindingFlags.Instance | BindingFlags.NonPublic )!; Assert.AreEqual( 1, ((Delegate)handlers.GetValue( runtime )!).GetInvocationList().Length );
		owner.Dispose(); Assert.IsNull( handlers.GetValue( runtime ) );
	}
	[TestMethod]
	public void ActualLevelQueueFlushRefusalPreservesPendingLevelAndPhysicsThenRetries()
	{
		Call( "Known", 1, ParkGameMode.FullSimulation ); var source = File.ReadAllBytes( Profile ); var runtime = ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation );
		var live = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ); Set( live, "<LevelName>k__BackingField", "jungle" ); Set( live, "<Park>k__BackingField", runtime ); Set( Flow, "<Level>k__BackingField", live ); Flow.AttachProgress( live );
		try
		{
			File.WriteAllText( Profile, "{}" ); runtime.Economy.Counters.Add( ParkCounters.Admissions, 101 ); runtime.Economy.Advance( ParkCalendar.TickOfTurn( 100 ) - runtime.Economy.Tick );
			var hash = WorldStateHash.Compute( new WorldStateSources { Economy = runtime.Economy } ); Action old = () => { }; Set( Flow, "pending", old ); Set( Flow, "pendingLevel", "jungle" ); var retired = 0;
			FlowCall( Flow, "QueueProgressLevel", "fantasy", (Action)(() => retired++) ); Assert.AreSame( old, Field<Action>( Flow, "pending" ) ); Assert.AreEqual( "jungle", Field<string>( Flow, "pendingLevel" ) ); Assert.AreSame( live, Flow.Level ); Assert.AreEqual( hash, WorldStateHash.Compute( new WorldStateSources { Economy = runtime.Economy } ) ); Assert.AreEqual( 0, retired );
			File.WriteAllBytes( Profile, source ); FlowCall( Flow, "QueueProgressLevel", "fantasy", (Action)(() => retired++) ); Call( "Pending" ); Assert.AreEqual( 1, retired ); Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( Profile )!.EarnedTickets );
		}
		finally { FlowCall( Flow, "DetachProgress" ); }
	}
}
