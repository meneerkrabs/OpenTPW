using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The four global golden tickets are held by one park at a time and move to a park with a better record (tp.exe 0x5AF940).</summary>
[TestClass]
[DoNotParallelize]
public class GlobalTicketHolderTests
{
	private Logger previousLog = null!;
	[TestInitialize]
	public void Initialize() { previousLog = Log; Log = new(); }
	[TestCleanup]
	public void Cleanup() { Log = previousLog; }

	[TestMethod]
	public void LedgerAwardsOnceThenMovesOnlyForAHigherRecordOfAnotherPark()
	{
		var ledger = new PlayerTicketLedger();
		Assert.AreEqual( GlobalTicketOutcome.Awarded, ledger.RecordGlobal( "jungle", GoldenTicketKind.CoasterHeight, 110 ) );
		Assert.AreEqual( new GlobalTicketHolder( GoldenTicketKind.CoasterHeight, "jungle", 110 ), ledger.HolderOf( GoldenTicketKind.CoasterHeight ) );
		Assert.AreEqual( GlobalTicketOutcome.None, ledger.RecordGlobal( "jungle", GoldenTicketKind.CoasterHeight, 500 ), "the holder keeps it and its record" );
		Assert.AreEqual( 110, ledger.HolderOf( GoldenTicketKind.CoasterHeight )!.Record );
		Assert.AreEqual( GlobalTicketOutcome.None, ledger.RecordGlobal( "hallow", GoldenTicketKind.CoasterHeight, 110 ), "an equal record does not move it" );
		Assert.AreEqual( GlobalTicketOutcome.None, ledger.RecordGlobal( "hallow", GoldenTicketKind.CoasterHeight, 90 ) );
		Assert.AreEqual( GlobalTicketOutcome.Moved, ledger.RecordGlobal( "hallow", GoldenTicketKind.CoasterHeight, 111 ) );
		Assert.AreEqual( "hallow", ledger.HolderOf( GoldenTicketKind.CoasterHeight )!.Theme );
		Assert.AreEqual( 1, ledger.EarnedTickets, "a move changes no count" );
		Assert.AreEqual( GlobalTicketOutcome.Moved, ledger.RecordGlobal( "jungle", GoldenTicketKind.CoasterHeight, 200 ), "the old park can take it back" );
		Assert.AreEqual( 1, ledger.EarnedTickets );
		Assert.ThrowsException<InvalidDataException>( () => ledger.RecordGlobal( "jungle", GoldenTicketKind.Visitors, 1 ) );
		Assert.ThrowsException<InvalidDataException>( () => ledger.RecordGlobal( "jungle", GoldenTicketKind.WaterLength, -1 ) );
	}

	[TestMethod]
	public void ReconcileAdoptsAnUnknownHolderOnce()
	{
		var legacy = PlayerTicketLedger.Restore( new( ParkGameMode.FullSimulation, 0, 0, new[] { new EarnedTicketIdentity( null, GoldenTicketKind.WaterLength ) } ) );
		Assert.IsNull( legacy.HolderOf( GoldenTicketKind.WaterLength ) );
		Assert.AreEqual( 1, legacy.Reconcile( "space", new[] { GoldenTicketKind.WaterLength } ) );
		Assert.AreEqual( "space", legacy.HolderOf( GoldenTicketKind.WaterLength )!.Theme );
		Assert.AreEqual( 0, legacy.Reconcile( "jungle", new[] { GoldenTicketKind.WaterLength } ) );
		Assert.AreEqual( "space", legacy.HolderOf( GoldenTicketKind.WaterLength )!.Theme );
	}

	[TestMethod]
	public void AnAdoptedHolderLearnsItsRecordOnceSoABetterParkStillNeedsToBeatIt()
	{
		var legacy = PlayerTicketLedger.Restore( new( ParkGameMode.FullSimulation, 0, 0, new[] { new EarnedTicketIdentity( null, GoldenTicketKind.WaterLength ) } ) );
		legacy.Reconcile( "space", new[] { GoldenTicketKind.WaterLength } );
		Assert.AreEqual( 0, legacy.HolderOf( GoldenTicketKind.WaterLength )!.Record );
		Assert.AreEqual( GlobalTicketOutcome.None, legacy.RecordGlobal( "space", GoldenTicketKind.WaterLength, 300 ) );
		Assert.AreEqual( 300, legacy.HolderOf( GoldenTicketKind.WaterLength )!.Record );
		Assert.AreEqual( GlobalTicketOutcome.None, legacy.RecordGlobal( "space", GoldenTicketKind.WaterLength, 900 ), "a known record is not raised" );
		Assert.AreEqual( 300, legacy.HolderOf( GoldenTicketKind.WaterLength )!.Record );
		Assert.AreEqual( GlobalTicketOutcome.None, legacy.RecordGlobal( "jungle", GoldenTicketKind.WaterLength, 250 ) );
		Assert.AreEqual( GlobalTicketOutcome.Moved, legacy.RecordGlobal( "jungle", GoldenTicketKind.WaterLength, 301 ) );
	}

	[TestMethod]
	public void HoldersRoundTripAndOlderFilesStillLoad()
	{
		var ledger = new PlayerTicketLedger();
		ledger.RecordGlobal( "jungle", GoldenTicketKind.BigPark, 4000 );
		ledger.RecordGlobal( "fantasy", GoldenTicketKind.GokartExcitement, 70 );
		ledger.RecordGlobal( "space", GoldenTicketKind.BigPark, 5000 );
		var bytes = PlayerTicketFile.Serialize( ledger.Capture() );
		var loaded = PlayerTicketFile.Deserialize( bytes );
		Assert.AreEqual( new GlobalTicketHolder( GoldenTicketKind.BigPark, "space", 5000 ), loaded.HolderOf( GoldenTicketKind.BigPark ) );
		Assert.AreEqual( new GlobalTicketHolder( GoldenTicketKind.GokartExcitement, "fantasy", 70 ), loaded.HolderOf( GoldenTicketKind.GokartExcitement ) );
		Assert.IsNull( loaded.HolderOf( GoldenTicketKind.WaterLength ) );
		CollectionAssert.AreEqual( bytes, PlayerTicketFile.Serialize( loaded.Capture() ) );

		// A ledger without a global has no field at all, so its bytes are those of the earlier format, and a file of that format loads with unknown holders.
		var local = new PlayerTicketLedger();
		local.RecordEarned( "jungle", GoldenTicketKind.Visitors );
		var localBytes = PlayerTicketFile.Serialize( local.Capture() );
		Assert.IsFalse( Encoding.UTF8.GetString( localBytes ).Contains( "GlobalHolders", StringComparison.OrdinalIgnoreCase ) );
		var older = Encoding.UTF8.GetBytes( Encoding.UTF8.GetString( bytes ).Split( ",\"GlobalHolders\"", StringSplitOptions.None )[0] + "}" );
		var olderLedger = PlayerTicketFile.Deserialize( older );
		Assert.AreEqual( 2, olderLedger.EarnedTickets );
		Assert.IsNull( olderLedger.HolderOf( GoldenTicketKind.BigPark ) );
	}

	[TestMethod]
	public void InvalidHoldersAreRefused()
	{
		var big = new EarnedTicketIdentity( null, GoldenTicketKind.BigPark );
		PlayerTicketSnapshot Snapshot( params GlobalTicketHolder[] holders ) => new( ParkGameMode.FullSimulation, 0, 0, new[] { big, new EarnedTicketIdentity( "jungle", GoldenTicketKind.Visitors ) }, holders );
		PlayerTicketLedger.Restore( Snapshot( new GlobalTicketHolder( GoldenTicketKind.BigPark, "jungle", 0 ) ) );
		foreach ( var bad in new[]
		{
			new GlobalTicketHolder( GoldenTicketKind.Visitors, "jungle", 1 ),
			new GlobalTicketHolder( GoldenTicketKind.CoasterHeight, "jungle", 1 ),
			new GlobalTicketHolder( GoldenTicketKind.BigPark, "JUNGLE", 1 ),
			new GlobalTicketHolder( GoldenTicketKind.BigPark, "moon", 1 ),
			new GlobalTicketHolder( GoldenTicketKind.BigPark, "jungle", -1 ),
			new GlobalTicketHolder( (GoldenTicketKind)99, "jungle", 1 ),
		} )
			Assert.ThrowsException<InvalidDataException>( () => PlayerTicketLedger.Restore( Snapshot( bad ) ), bad.ToString() );
		Assert.ThrowsException<InvalidDataException>( () => PlayerTicketLedger.Restore( Snapshot( new GlobalTicketHolder( GoldenTicketKind.BigPark, "jungle", 1 ), new GlobalTicketHolder( GoldenTicketKind.BigPark, "hallow", 2 ) ) ) );
		var json = "{\"Format\":\"opentpw-player-progress\",\"Version\":1,\"Mode\":\"FullSimulation\",\"ExtraKeys\":0,\"SpentTickets\":0,\"Earned\":[],\"GlobalHolders\":[{\"Kind\":\"BigPark\",\"Theme\":\"jungle\",\"Record\":1}]}";
		Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Deserialize( Encoding.UTF8.GetBytes( json ) ) );
	}

	[TestMethod]
	public void AParkWithABetterRecordTakesTheTicketAndRaisesTheMovedEvent()
	{
		var ledger = new PlayerTicketLedger();
		var first = Park( ledger, "jungle", 110, out var firstEvents );
		CollectionAssert.AreEqual( new[] { GoldenTicketKind.CoasterHeight }, first.Objectives.GoldenTickets.ToArray() );
		Assert.AreEqual( 1, firstEvents.Count( item => item.Kind == ParkEventKind.GoldenTicketWon ) );
		Assert.AreEqual( 0, firstEvents.Count( item => item.Kind == ParkEventKind.GlobalTicketMoved ) );

		var worse = Park( ledger, "hallow", 105, out var worseEvents );
		Assert.AreEqual( 0, worse.Objectives.GoldenTickets.Count, "the ticket is not earned twice and a lower record does not take it" );
		Assert.AreEqual( 0, worseEvents.Count( item => item.Kind is ParkEventKind.GoldenTicketWon or ParkEventKind.GlobalTicketMoved ) );

		var better = Park( ledger, "fantasy", 130, out var betterEvents );
		CollectionAssert.AreEqual( new[] { GoldenTicketKind.CoasterHeight }, better.Objectives.GoldenTickets.ToArray() );
		Assert.AreEqual( 0, betterEvents.Count( item => item.Kind == ParkEventKind.GoldenTicketWon ), "a move is not a new award" );
		var moved = betterEvents.Single( item => item.Kind == ParkEventKind.GlobalTicketMoved );
		Assert.AreEqual( (int)GoldenTicketKind.CoasterHeight, moved.Amount );
		Assert.AreEqual( 1, ledger.EarnedTickets, "the player-wide count is unchanged by the move" );
		Assert.AreEqual( "fantasy", ledger.HolderOf( GoldenTicketKind.CoasterHeight )!.Theme );
		Assert.AreEqual( 1, first.Objectives.GoldenTickets.Count, "the old park's own saved set is left alone" );

		better.AdvanceMonths( 1 );
		Assert.AreEqual( 1, betterEvents.Count( item => item.Kind == ParkEventKind.GlobalTicketMoved ), "the holder is not moved again" );
	}

	[TestMethod]
	public void WithoutAnAuthorityTheParkKeepsItsOwnSet()
	{
		var park = EconomyTestData.Park();
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		park.OpenPark();
		park.ReportRecord( ParkRecordKind.CoasterHeight, 0, 110 );
		park.AdvanceMonths( 1 );
		CollectionAssert.AreEqual( new[] { GoldenTicketKind.CoasterHeight }, park.Objectives.GoldenTickets.ToArray() );
		Assert.AreEqual( 0, events.Count( item => item.Kind == ParkEventKind.GlobalTicketMoved ) );
	}

	[TestMethod]
	public void TheMovedAdviceIsMessage234PlusTheType()
	{
		Assert.AreEqual( 234, AutomaticAdvisor.GlobalTicketMovedMessage( GoldenTicketKind.CoasterHeight ) );
		Assert.AreEqual( 235, AutomaticAdvisor.GlobalTicketMovedMessage( GoldenTicketKind.GokartExcitement ) );
		Assert.AreEqual( 236, AutomaticAdvisor.GlobalTicketMovedMessage( GoldenTicketKind.WaterLength ) );
		Assert.AreEqual( 237, AutomaticAdvisor.GlobalTicketMovedMessage( GoldenTicketKind.BigPark ) );
		Assert.IsNull( AutomaticAdvisor.GlobalTicketMovedMessage( GoldenTicketKind.Visitors ) );
		Assert.IsNull( AutomaticAdvisor.GlobalTicketMovedMessage( GoldenTicketKind.CamerasEverywhere ) );
	}

	private static ParkEconomy Park( PlayerTicketLedger ledger, string theme, long record, out List<ParkEvent> events )
	{
		var park = EconomyTestData.Park();
		var list = new List<ParkEvent>();
		park.EventRaised += list.Add;
		events = list;
		park.Objectives.GlobalTicketAuthority = ( kind, value ) => ledger.RecordGlobal( theme, kind, value );
		park.OpenPark();
		park.ReportRecord( ParkRecordKind.CoasterHeight, 0, record );
		park.AdvanceMonths( 1 );
		return park;
	}

	[TestMethod]
	public void TheProgressOwnerAppliesTheRuleAndSavesTheHolder()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-global-holder-" ).FullName;
		try
		{
			var path = Path.Combine( root, PlayerTicketFile.FileName );
			PlayerTicketFile.Save( path, new PlayerTicketLedger().Capture() );
			ParkEconomyRuntime Runtime() => ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation );
			void Play( string theme, long record, List<ParkEvent> events )
			{
				var runtime = Runtime();
				runtime.EventRaised += events.Add;
				using var owner = new PlayerProgressRuntimeOwner( path, theme, PlayerTicketFile.ReadIfPresent( path )!, runtime, () => true, _ => Assert.Fail() );
				runtime.Economy.OpenPark();
				runtime.Economy.ReportRecord( ParkRecordKind.CoasterHeight, 0, record );
				runtime.Economy.AdvanceMonths( 1 );
				Assert.IsFalse( owner.Dirty );
			}
			var first = new List<ParkEvent>(); Play( "jungle", 110, first );
			Assert.AreEqual( new GlobalTicketHolder( GoldenTicketKind.CoasterHeight, "jungle", 110 ), PlayerTicketFile.LoadIfPresent( path )!.HolderOf( GoldenTicketKind.CoasterHeight ) );
			var worse = new List<ParkEvent>(); Play( "hallow", 100 + 5, worse );
			Assert.AreEqual( 0, worse.Count( item => item.Kind is ParkEventKind.GoldenTicketWon or ParkEventKind.GlobalTicketMoved ) );
			var better = new List<ParkEvent>(); Play( "space", 140, better );
			Assert.AreEqual( 1, better.Count( item => item.Kind == ParkEventKind.GlobalTicketMoved ) );
			var saved = PlayerTicketFile.LoadIfPresent( path )!;
			Assert.AreEqual( new GlobalTicketHolder( GoldenTicketKind.CoasterHeight, "space", 140 ), saved.HolderOf( GoldenTicketKind.CoasterHeight ) );
			Assert.AreEqual( 1, saved.EarnedTickets );
		}
		finally { Directory.Delete( root, true ); }
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] A park in a co-op session decides its global tickets by its own records, as a peer's park without a ledger does, and leaves the player's holder alone;
	/// when the session ends, the hand-back takes a ticket the rule keeps with another park and records one the park's record takes over.
	/// </summary>
	[TestMethod]
	public void AParkInASessionDecidesItsGlobalTicketsAloneAndTheHandBackAppliesTheRule()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-global-session-" ).FullName;
		try
		{
			var path = Path.Combine( root, PlayerTicketFile.FileName );
			PlayerTicketFile.Save( path, new PlayerTicketLedger().Capture() );
			var held = new GlobalTicketHolder( GoldenTicketKind.CoasterHeight, "jungle", 110 );
			{
				var runtime = ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation );
				using var owner = new PlayerProgressRuntimeOwner( path, "jungle", PlayerTicketFile.ReadIfPresent( path )!, runtime, () => true, _ => Assert.Fail() );
				runtime.Economy.OpenPark();
				runtime.Economy.ReportRecord( ParkRecordKind.CoasterHeight, 0, 110 );
				runtime.Economy.AdvanceMonths( 1 );
			}
			Assert.AreEqual( held, PlayerTicketFile.LoadIfPresent( path )!.HolderOf( GoldenTicketKind.CoasterHeight ) );

			foreach ( var (record, keeps) in new[] { (108L, false), (140L, true) } )
			{
				var shared = true;
				var runtime = ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation );
				using var owner = new PlayerProgressRuntimeOwner( path, "space", PlayerTicketFile.ReadIfPresent( path )!, runtime, () => true, _ => Assert.Fail(), () => shared );
				// the session started after the owner's last pump: the park is outside its player's progress from its first tick
				Assert.AreEqual( GlobalTicketOutcome.Unmanaged, runtime.Economy.Objectives.GlobalTicketAuthority!( GoldenTicketKind.CoasterHeight, 500 ), "no player-wide rule in a session" );
				Assert.AreEqual( held, owner.Ledger.HolderOf( GoldenTicketKind.CoasterHeight ) );
				var peer = EconomyTestData.Park();
				foreach ( var economy in new[] { runtime.Economy, peer } )
				{
					economy.OpenPark();
					economy.ReportRecord( ParkRecordKind.CoasterHeight, 0, record );
					economy.AdvanceMonths( 1 );
				}
				CollectionAssert.AreEquivalent( peer.Objectives.GoldenTickets.ToArray(), runtime.Economy.Objectives.GoldenTickets.ToArray(), "decided alike with and without a ledger" );
				Assert.IsTrue( runtime.Economy.Objectives.GoldenTickets.Contains( GoldenTicketKind.CoasterHeight ), $"a record of {record} wins the ticket in the session" );
				Assert.AreEqual( held, owner.Ledger.HolderOf( GoldenTicketKind.CoasterHeight ), "the player's holder is untouched in the session" );
				Assert.IsFalse( owner.Dirty );

				shared = false;
				owner.Pump();
				Assert.AreEqual( keeps, runtime.Economy.Objectives.GoldenTickets.Contains( GoldenTicketKind.CoasterHeight ), $"the hand-back with a record of {record}" );
				var holder = PlayerTicketFile.LoadIfPresent( path )!.HolderOf( GoldenTicketKind.CoasterHeight );
				Assert.AreEqual( keeps ? new GlobalTicketHolder( GoldenTicketKind.CoasterHeight, "space", 140 ) : held, holder );
				held = holder!;
			}

			// a global ticket the park held before the session is no session win: the hand-back leaves it alone
			{
				var shared = true;
				var economy = EconomyTestData.Park();
				var objectives = economy.Objectives;
				typeof( ParkObjectives ).GetField( "tickets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.GetValue( objectives )!
					.GetType().GetMethod( "Add" )!.Invoke( typeof( ParkObjectives ).GetField( "tickets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.GetValue( objectives ),
						new object[] { GoldenTicketKind.CoasterHeight } );
				var runtime = ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation );
				using var owner = new PlayerProgressRuntimeOwner( path, "hallow", PlayerTicketFile.ReadIfPresent( path )!, runtime, () => true, _ => Assert.Fail(), () => shared );
				owner.Pump();
				shared = false;
				owner.Pump();
				Assert.IsTrue( economy.Objectives.GoldenTickets.Contains( GoldenTicketKind.CoasterHeight ), "a ticket from before the session stays" );
			}
		}
		finally { Directory.Delete( root, true ); }
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] The game's way out of a shared park (<c>GameFlow.LeaveCoopSession</c>, first on every way out) runs the hand-back also when the session ended earlier in
	/// the frame (the server or the host's stream ended it, so the game holds no session any more), before anything saves or detaches progress.
	/// </summary>
	[TestMethod]
	public void TheWayOutOfASharedParkRunsTheHandBackAlsoWhenItsSessionEndedEarlier()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-global-leave-" ).FullName;
		try
		{
			var path = Path.Combine( root, PlayerTicketFile.FileName );
			PlayerTicketFile.Save( path, new PlayerTicketLedger().Capture() );
			var shared = true;
			var runtime = ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation );
			using var owner = new PlayerProgressRuntimeOwner( path, "space", PlayerTicketFile.ReadIfPresent( path )!, runtime, () => true, _ => Assert.Fail(), () => shared );
			runtime.Economy.OpenPark();
			runtime.Economy.ReportRecord( ParkRecordKind.CoasterHeight, 0, 140 );
			runtime.Economy.AdvanceMonths( 1 );
			Assert.IsTrue( runtime.Economy.Objectives.GoldenTickets.Contains( GoldenTicketKind.CoasterHeight ), "won in the session" );
			Assert.IsNull( PlayerTicketFile.LoadIfPresent( path )!.HolderOf( GoldenTicketKind.CoasterHeight ), "nothing of it recorded while shared" );
			shared = false;
			var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
			typeof( GameFlow ).GetField( "progressOwner", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( flow, owner );
			typeof( GameFlow ).GetMethod( "LeaveCoopSession", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( flow, new object[] { "The park was closed." } );
			Assert.AreEqual( new GlobalTicketHolder( GoldenTicketKind.CoasterHeight, "space", 140 ), PlayerTicketFile.LoadIfPresent( path )!.HolderOf( GoldenTicketKind.CoasterHeight ),
				"the hand-back recorded the session's win" );
		}
		finally { Directory.Delete( root, true ); }
	}

	private static void Set( object target, string name, object? value ) => target.GetType().GetField( name, BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( target, value );
	private static object? Get( object target, string name ) => target.GetType().GetField( name, BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( target );

	/// <summary>
	/// [EXT:online-coop-snapshot-join] A park that joins a session as a guest's at the session's identical start is the host's park from then on: the progress attached to it while it was
	/// the player's own never takes the park's tickets for its player, during the session or after it (<c>GameFlow.AttachProgress</c>'s owner is active only for a park that is no guest's).
	/// </summary>
	[TestMethod]
	public void AGuestsParkNeverHandsTheHostsTicketsToItsPlayer()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-global-guest-" ).FullName;
		var (saves, edition) = (SaveFileSystem, GameEditions.Current);
		try
		{
			SaveFileSystem = new( root );
			GameEditions.Select( GameEdition.ThemeParkWorld );
			var path = Path.Combine( root, PlayerTicketFile.FileName );
			PlayerTicketFile.Save( path, new PlayerTicketLedger().Capture() );
			var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
			var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
			var runtime = ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation );
			Set( level, "<LevelName>k__BackingField", "space" );
			Set( level, "<Park>k__BackingField", runtime );
			Set( flow, "<Level>k__BackingField", level );
			flow.AttachProgress( level );
			var owner = (PlayerProgressRuntimeOwner)Get( flow, "progressOwner" )!;
			Assert.IsNotNull( owner, "the player's own park takes part in its progress" );
			// the park joins a session as a guest's: the host's park from then on, which wins a global ticket in the session
			level.IsCoopGuestPark = true;
			level.CoopSession = (CoopSession)RuntimeHelpers.GetUninitializedObject( typeof( CoopSession ) );
			owner.Pump();
			runtime.Economy.OpenPark();
			runtime.Economy.ReportRecord( ParkRecordKind.CoasterHeight, 0, 140 );
			runtime.Economy.AdvanceMonths( 1 );
			Assert.IsTrue( runtime.Economy.Objectives.GoldenTickets.Contains( GoldenTicketKind.CoasterHeight ), "won by the park's own record" );
			// the session ended; the way out finds no session and pumps the progress: the guest's player gets nothing of the host's park
			level.CoopSession = null;
			typeof( GameFlow ).GetMethod( "LeaveCoopSession", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( flow, new object[] { "The park was closed." } );
			Assert.IsNull( PlayerTicketFile.LoadIfPresent( path )!.HolderOf( GoldenTicketKind.CoasterHeight ) );
			Assert.IsFalse( owner.Dirty );
			typeof( GameFlow ).GetMethod( "DetachProgress", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( flow, null );
		}
		finally
		{
			SaveFileSystem = saves;
			GameEditions.Select( edition );
			Directory.Delete( root, true );
		}
	}
}
