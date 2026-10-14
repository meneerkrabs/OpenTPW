using System;
using System.IO;
using System.Linq;
using System.Reflection;
using OpenTPW.FrontEnd;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class PlayerProgressOwnerTests
{
	private string root = null!;
	private Logger previousLog = null!;
	[TestInitialize]
	public void Initialize() { root = Directory.CreateTempSubdirectory( "opentpw-progress-owner-" ).FullName; previousLog = Log; Log = new(); }
	[TestCleanup]
	public void Cleanup() { Log = previousLog; Directory.Delete( root, true ); }
	private string PathName => Path.Combine( root, PlayerTicketFile.FileName );
	private PlayerTicketFile.ReadState State( string? path = null ) { path ??= PathName; PlayerTicketFile.Save( path, new PlayerTicketLedger().Capture() with { ExtraKeys = 1 } ); return PlayerTicketFile.ReadIfPresent( path )!; }
	private static ParkEconomyRuntime Runtime( ParkEconomy? park = null ) => ParkEconomyRuntime.ForResume( park ?? EconomyTestData.Park(), ParkStartKind.FullSimulation );
	private static void AdvanceTo( ParkEconomy park, long turn ) => park.Advance( ParkCalendar.TickOfTurn( turn ) - park.Tick );
	private sealed class Guests( int count ) : IParkGuestStatistics
	{
		public int PeopleInPark => count;
		public float AverageHappiness => 0;
		public int CountHappierThan( int value ) => 0;
		public int KidsWithBalloonsPercent => 0;
		public int KidsWithCostumesPercent => 0;
	}
	[TestMethod]
	public void ActualDistinctAwardAmountsRecordVisitorsThenPeopleWithoutChangingEconomy()
	{
		var runtime = Runtime(); var active = true; var errors = 0;
		using var owner = new PlayerProgressRuntimeOwner( PathName, "jungle", State(), runtime, () => active, _ => errors++ );
		runtime.Economy.Counters.Add( ParkCounters.Admissions, 101 ); AdvanceTo( runtime.Economy, 100 );
		CollectionAssert.AreEquivalent( new[] { GoldenTicketKind.Visitors }, owner.Ledger.Capture().Earned.Select( item => item.Kind ).ToArray() );
		runtime.Economy.GuestStatistics = new Guests( 201 ); AdvanceTo( runtime.Economy, 200 );
		CollectionAssert.AreEquivalent( new[] { GoldenTicketKind.Visitors, GoldenTicketKind.PeopleInPark }, PlayerTicketFile.LoadIfPresent( PathName )!.Capture().Earned.Select( item => item.Kind ).ToArray() );
		Assert.AreEqual( 2, runtime.Economy.Objectives.GoldenTickets.Count ); Assert.AreEqual( 0, errors ); Assert.IsFalse( owner.Dirty );
		var before = File.ReadAllBytes( PathName ); active = false; runtime.Economy.Counters.Max( ParkCounters.Record( ParkRecordKind.CoasterHeight, 1100 ), 106 ); AdvanceTo( runtime.Economy, 300 );
		CollectionAssert.AreEqual( before, File.ReadAllBytes( PathName ) );
	}
	[TestMethod]
	public void RepeatedKnownResumeCrossThemeReconciliationDeduplicatesOnlyGlobalsAndSecrets()
	{
		var park = EconomyTestData.Park(); var tickets = new[] { GoldenTicketKind.Visitors, GoldenTicketKind.CoasterHeight, GoldenTicketKind.CamerasEverywhere, GoldenTicketKind.AllLandOwned };
		park.Objectives.Restore( 100, 0, null, new System.Collections.Generic.Dictionary<int, int>(), Array.Empty<int>(), tickets );
		State();
		foreach ( var theme in new[] { "jungle", "hallow", "jungle" } )
		{
			using var owner = new PlayerProgressRuntimeOwner( PathName, theme, PlayerTicketFile.ReadIfPresent( PathName )!, Runtime( park ), () => true, _ => Assert.Fail() );
			Assert.IsTrue( owner.TryFlush() );
		}
		var restored = PlayerTicketFile.LoadIfPresent( PathName )!; Assert.AreEqual( 5, restored.EarnedTickets ); Assert.AreEqual( 1, restored.ExtraKeys ); Assert.AreEqual( 2, restored.Keys );
	}
	[TestMethod]
	public void RuntimeEconomyReplacementReconcilesActualLoadedIdentitiesAndKeepsForwardedAwards()
	{
		var runtime = Runtime(); using var owner = new PlayerProgressRuntimeOwner( PathName, "jungle", State(), runtime, () => true, _ => Assert.Fail() );
		var loaded = EconomyTestData.Park(); loaded.Objectives.Restore( 100, 0, null, new System.Collections.Generic.Dictionary<int, int>(), Array.Empty<int>(), new[] { GoldenTicketKind.WaterLength } );
		var file = Path.Combine( root, "park.json" ); ParkSaveFile.Save( file, loaded ); runtime.Load( file ); owner.Pump();
		Assert.AreEqual( GoldenTicketKind.WaterLength, PlayerTicketFile.LoadIfPresent( PathName )!.Capture().Earned.Single().Kind );
		runtime.Economy.Counters.Add( ParkCounters.Admissions, 101 ); AdvanceTo( runtime.Economy, 100 ); Assert.AreEqual( 2, owner.Ledger.EarnedTickets );
	}
	[TestMethod]
	public void DisposedOwnerDoesNotWriteOnRealFutureAwards()
	{
		var runtime = Runtime(); var owner = new PlayerProgressRuntimeOwner( PathName, "jungle", State(), runtime, () => true, _ => Assert.Fail() );
		var before = File.ReadAllBytes( PathName ); owner.Dispose(); owner.Dispose();
		runtime.Economy.Counters.Add( ParkCounters.Admissions, 101 ); AdvanceTo( runtime.Economy, 100 );
		CollectionAssert.AreEqual( before, File.ReadAllBytes( PathName ) ); Assert.AreEqual( 0, owner.Ledger.EarnedTickets );
	}
	[TestMethod]
	public void ChangedFileKeepsDirtyEarnedMemoryAndRefusesRetirementUntilOriginalSourceReturns()
	{
		var runtime = Runtime(); var state = State(); var previous = File.ReadAllBytes( PathName ); var errors = 0;
		using var owner = new PlayerProgressRuntimeOwner( PathName, "jungle", state, runtime, () => true, _ => errors++ );
		File.WriteAllText( PathName, "{}" ); runtime.Economy.Counters.Add( ParkCounters.Admissions, 101 ); AdvanceTo( runtime.Economy, 100 );
		Assert.AreEqual( 1, owner.Ledger.EarnedTickets ); Assert.IsTrue( owner.Dirty ); Assert.IsFalse( owner.TryFlush() ); Assert.AreEqual( "{}", File.ReadAllText( PathName ) ); Assert.IsTrue( errors > 0 );
		File.WriteAllBytes( PathName, previous ); Assert.IsTrue( owner.TryFlush() ); Assert.IsFalse( owner.Dirty ); Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( PathName )!.EarnedTickets );
	}
	[TestMethod]
	public void ForgedAmountMembershipAndWrongStartCannotWrite()
	{
		var runtime = Runtime(); State(); var before = File.ReadAllBytes( PathName );
		using var owner = new PlayerProgressRuntimeOwner( PathName, "jungle", PlayerTicketFile.ReadIfPresent( PathName )!, runtime, () => true, _ => Assert.Fail() );
		var forward = typeof( ParkEconomyRuntime ).GetMethod( "ForwardEvent", BindingFlags.NonPublic | BindingFlags.Instance )!;
		foreach ( var amount in new long[] { 0, -1, 99, long.MaxValue } ) forward.Invoke( runtime, new object[] { new ParkEvent( 0, ParkEventKind.GoldenTicketWon, 0, 0, amount, "Visitors" ) } );
		CollectionAssert.AreEqual( before, File.ReadAllBytes( PathName ) );
		var reference = ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.OriginalSaveReference );
		using var unsupported = new PlayerProgressRuntimeOwner( PathName, "jungle", PlayerTicketFile.ReadIfPresent( PathName )!, reference, () => true, _ => Assert.Fail() );
		reference.Economy.Counters.Add( ParkCounters.Admissions, 101 ); AdvanceTo( reference.Economy, 100 ); CollectionAssert.AreEqual( before, File.ReadAllBytes( PathName ) );
	}
	private static void Set( object target, string name, object? value ) => target.GetType().GetField( name, BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( target, value );
	private static void Call( GameFlow flow, string name, params object[] args ) => typeof( GameFlow ).GetMethod( name, BindingFlags.NonPublic | BindingFlags.Instance )!.Invoke( flow, args );

	[TestMethod]
	public void ActualGameFlowAttachmentAndRetirementGuardKeepLiveWorldAndPendingTransitionOnFailedFlush()
	{
		var previousSaves = SaveFileSystem; var edition = GameEditions.Current;
		try
		{
			SaveFileSystem = new( root ); GameEditions.Select( GameEdition.ThemeParkWorld ); State();
			var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
			Set( flow, "<Strings>k__BackingField", OriginalUiTests.FakeStrings() );
			var menu = new OpenTPW.FrontEnd.FrontEndMenu( OriginalUiTests.FakeStrings(), new[] {
				new LobbyIslandInfo( 0, "jungle", "", "", "", "jungle", 0, 0, (0, 0, 0), Array.Empty<string>(), false, 0 ) }, new OpenTPW.FrontEnd.FrontEndActions() );
			Set( flow, "<Menu>k__BackingField", menu );
			var live = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ); var runtime = Runtime();
			Set( live, "<LevelName>k__BackingField", "jungle" ); Set( live, "<Park>k__BackingField", runtime ); Set( flow, "<Level>k__BackingField", live );
			flow.AttachProgress( live );
			var source = File.ReadAllBytes( PathName ); File.WriteAllText( PathName, "{}" );
			runtime.Economy.Counters.Add( ParkCounters.Admissions, 101 ); AdvanceTo( runtime.Economy, 100 );
			var before = ParkSaveFile.Serialize( runtime.Economy ); var retired = 0;
			Action existing = () => { }; Set( flow, "pending", existing );
			Call( flow, "QueueProgressTransition", (Action)(() => retired++) );
			Assert.AreSame( existing, typeof( GameFlow ).GetField( "pending", BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( flow ) );
			Assert.AreSame( live, flow.Level ); Assert.AreEqual( before, ParkSaveFile.Serialize( runtime.Economy ) ); Assert.AreEqual( 0, retired );
			Call( flow, "PumpProgress" ); Call( flow, "PumpProgress" );
			Assert.AreEqual( 2, menu.Stack.Screens.Count, "Immediate refusal consumes its queued notice instead of duplicating the dialog." );
			File.WriteAllBytes( PathName, source ); Call( flow, "QueueProgressTransition", (Action)(() => retired++) );
			((Action)typeof( GameFlow ).GetField( "pending", BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( flow )!)(); Assert.AreEqual( 1, retired );
			Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( PathName )!.EarnedTickets );
			Call( flow, "DetachProgress" ); var persisted = File.ReadAllBytes( PathName );
			runtime.Economy.GuestStatistics = new Guests( 201 ); AdvanceTo( runtime.Economy, 200 ); CollectionAssert.AreEqual( persisted, File.ReadAllBytes( PathName ) );
		}
		finally { SaveFileSystem = previousSaves; GameEditions.Select( edition ); }
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ActualFullResumeWithoutCompatibleKnownHistoryShowsNoticeAndNeverCreatesProfile( bool instantProfile )
	{
		var saves = SaveFileSystem; var edition = GameEditions.Current;
		try
		{
			SaveFileSystem = new( root ); GameEditions.Select( GameEdition.ThemeParkWorld );
			if ( instantProfile ) PlayerTicketFile.Save( PathName, new PlayerTicketLedger( ParkGameMode.InstantAction ).Capture() );
			var before = instantProfile ? File.ReadAllBytes( PathName ) : null;
			var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) ); Set( flow, "<Strings>k__BackingField", OriginalUiTests.FakeStrings() );
			var menu = new FrontEndMenu( OriginalUiTests.FakeStrings(), new[] { new LobbyIslandInfo( 0, "jungle", "", "", "", "jungle", 0, 0, (0, 0, 0), Array.Empty<string>(), false, 0 ) }, new FrontEndActions() );
			Set( flow, "<Menu>k__BackingField", menu );
			var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ); var runtime = Runtime();
			Set( level, "<LevelName>k__BackingField", "jungle" ); Set( level, "<Park>k__BackingField", runtime ); Set( flow, "<Level>k__BackingField", level );
			flow.AttachProgress( level ); Call( flow, "PumpProgress" );
			Assert.AreEqual( "progressFailed", menu.Stack.Top!.Name ); Assert.AreEqual( 2, menu.Stack.Screens.Count );
			runtime.Economy.Counters.Add( ParkCounters.Admissions, 101 ); AdvanceTo( runtime.Economy, 100 );
			Assert.AreEqual( 1, runtime.Economy.Objectives.GoldenTickets.Count, "Manual resumed park still earns its ordinary park identities." );
			if ( before == null ) Assert.IsFalse( File.Exists( PathName ) ); else CollectionAssert.AreEqual( before, File.ReadAllBytes( PathName ) );
			Call( flow, "DetachProgress" ); menu.Stack.Pop(); Call( flow, "PumpProgress" ); Assert.AreEqual( 1, menu.Stack.Screens.Count );
		}
		finally { SaveFileSystem = saves; GameEditions.Select( edition ); }
	}

}
