using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-permissions] A shared park's rights per seat (docs/COOP.md, "Money and rights (C4)"; C4a), on the stream tests' asset-free park: every command kind is of one class, each
/// class answers the host and a guest as listed, one reducer sets a seat's rights for both commands that write them, the rights command takes only a guest's seat and the rights this build
/// offers, and a seat's release takes its rights back in its one record.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CoopRightsTests
{
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;
	private List<Entity>? entities;
	private Level? current;
	private readonly List<List<Entity>> built = new();

	[TestInitialize]
	public void Initialize()
	{
		entities = Entity.All;
		current = Level.Current;
		renderer = new AutomaticCheckpointCpuRendererScope();
		fixture = new UpgradeCatalogueDefaultsTests();
		fixture.Setup();
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var list in built )
		{
			Entity.All = list;
			foreach ( var entity in list.ToArray() )
				entity.Delete();
		}
		Entity.All = new List<Entity>();
		fixture?.Cleanup();
		renderer?.Dispose();
		if ( entities != null )
			Entity.All = entities;
		Level.Current = current!;
	}

	/// <summary>The stream tests' park, entered.</summary>
	private (Level Level, OriginalObject One) NewPark()
	{
		var park = CoopStreamTests.BuildPark( fixture! );
		built.Add( park.Entities );
		Entity.All = park.Entities;
		Level.Current = park.Level;
		return (park.Level, park.One);
	}

	/// <summary>The kinds of each class, by number.</summary>
	private static Dictionary<CoopCommandClass, string> KindsByClass() => Enum.GetValues<ParkCommandKind>().GroupBy( CoopBuildRights.ClassOf )
		.ToDictionary( group => group.Key, group => string.Join( " ", group.Select( kind => (int)kind ).Order() ) );

	[TestMethod]
	public void EveryCommandKindIsOfOneClassOfTheSharedParksRights()
	{
		// a kind in no class throws, and a kind is in one class by the table's form: the pins hold each class to its kinds
		var classes = KindsByClass();
		CollectionAssert.AreEquivalent( Enum.GetValues<CoopCommandClass>(), classes.Keys.ToArray(), "every class holds a kind" );
		Assert.AreEqual( "1 2 4 5 10 14 15 16 18 54 76 78 79 80 82 83 90 91 92 93 94 95 96 97 98 103 106", classes[CoopCommandClass.GuestBuilding],
			"C3's building, claim-checked; the take-back (C5); a track line's undo is the host's since C5-3" );
		Assert.AreEqual( "6 104", classes[CoopCommandClass.SeatCleanup], "a Move's end and a claim's release" );
		Assert.AreEqual( "32 33 34 35 36 43 44 45 73", classes[CoopCommandClass.Attractions], "the attractions' management; a wave machine's settings (C5-2)" );
		Assert.AreEqual( "37 47 48 49 60 61 62 63 64", classes[CoopCommandClass.Staff] );
		Assert.AreEqual( "38 39 40", classes[CoopCommandClass.Research] );
		Assert.AreEqual( "30 31 41 42 50 53", classes[CoopCommandClass.Finance] );
		Assert.AreEqual( "3 11 12 13 17 70 71 72 74 75 77 81", classes[CoopCommandClass.HostTools], "what only the host's tools issue: every undo a take-back names (C5, a track line's since C5-3)" );
		Assert.AreEqual( "20 21 23 46 51 100 101 102 105", classes[CoopCommandClass.HostOnly] );
		Assert.AreEqual( "22 25 52", classes[CoopCommandClass.RefusedInSession], "the cheats and the developer kinds a session refuses" );
		// the management classes are the four rights'; the others are no right's
		Assert.AreEqual( CoopRights.Attractions, CoopBuildRights.RightOf( CoopCommandClass.Attractions ) );
		Assert.AreEqual( CoopRights.Staff, CoopBuildRights.RightOf( CoopCommandClass.Staff ) );
		Assert.AreEqual( CoopRights.Research, CoopBuildRights.RightOf( CoopCommandClass.Research ) );
		Assert.AreEqual( CoopRights.Finance, CoopBuildRights.RightOf( CoopCommandClass.Finance ) );
		foreach ( var other in new[] { CoopCommandClass.GuestBuilding, CoopCommandClass.SeatCleanup, CoopCommandClass.HostTools, CoopCommandClass.HostOnly, CoopCommandClass.RefusedInSession } )
			Assert.IsNull( CoopBuildRights.RightOf( other ), $"{other}" );
		// what the C3 allowlist calls building is of the building class, so a C3 session's log replays as it did
		var (level, _) = NewPark();
		var rights = new CoopBuildRights( level );
		foreach ( var (command, _) in ParkCommandCodecTests.Schema )
			if ( rights.Builds( command ) )
				Assert.AreEqual( CoopCommandClass.GuestBuilding, CoopBuildRights.ClassOf( command.Kind ), $"{command.Kind}" );
	}

	[TestMethod]
	public void EachClassAnswersTheHostAndAGuestAsListed()
	{
		var (level, _) = NewPark();
		var rights = new CoopBuildRights( level );
		const int seat = 2;
		IEnumerable<ParkCommandKind> Allowed( int player ) => ParkCommandCodecTests.Schema.Where( row => rights.Allows( player, row.Command ) ).Select( row => row.Command.Kind );
		// the host issues every kind while nothing is claimed (the session's rules refuse the cheats and kinds 22 and 25 besides, which are no rights' to decide)
		CollectionAssert.AreEquivalent( Enum.GetValues<ParkCommandKind>(), Allowed( ParkCommands.LocalPlayer ).ToArray() );
		// a guest without a right: its own cleanup alone, and nothing for a player number no session gives
		CollectionAssert.AreEquivalent( new[] { ParkCommandKind.EndMove, ParkCommandKind.ReleaseRide }, Allowed( seat ).ToArray() );
		Assert.AreEqual( 0, Allowed( -1 ).Count() );
		Assert.IsFalse( rights.Allows( seat, ParkCommandKind.LayLine ), "a kind alone is the host's" );
		// building: the C3 commands its fields allow, and still no management, the host's tools or the host's kinds
		level.SetSeatRights( seat, CoopRights.Build );
		var building = Allowed( seat ).ToArray();
		CollectionAssert.Contains( building, ParkCommandKind.LayLine );
		foreach ( var kind in building )
			Assert.IsTrue( CoopBuildRights.ClassOf( kind ) is CoopCommandClass.GuestBuilding or CoopCommandClass.SeatCleanup, $"{kind}" );
		// every right held: building's, the attractions', staff's, research's and finance's kinds answer (C4b-1, C4b-2, C4c-2)
		level.SetSeatRights( seat, (CoopRights)0x1F );
		var held = Allowed( seat ).ToArray();
		CollectionAssert.IsSubsetOf( building, held );
		foreach ( var kind in held )
			Assert.IsTrue( CoopBuildRights.ClassOf( kind ) is CoopCommandClass.GuestBuilding or CoopCommandClass.SeatCleanup or CoopCommandClass.Attractions or CoopCommandClass.Staff
				or CoopCommandClass.Research or CoopCommandClass.Finance, $"{kind}" );
		foreach ( var kind in Enum.GetValues<ParkCommandKind>().Where( kind => CoopBuildRights.ClassOf( kind ) is CoopCommandClass.Attractions or CoopCommandClass.Staff or CoopCommandClass.Research
			or CoopCommandClass.Finance ) )
			CollectionAssert.Contains( held, kind, $"{kind} with its right" );
		// each offered management right answers its own class alone
		level.SetSeatRights( seat, CoopRights.Research );
		CollectionAssert.AreEquivalent( Enum.GetValues<ParkCommandKind>().Where( kind => CoopBuildRights.ClassOf( kind ) is CoopCommandClass.Research or CoopCommandClass.SeatCleanup ).ToArray(),
			Allowed( seat ).ToArray(), "research alone" );
		level.SetSeatRights( seat, CoopRights.Finance );
		CollectionAssert.AreEquivalent( Enum.GetValues<ParkCommandKind>().Where( kind => CoopBuildRights.ClassOf( kind ) is CoopCommandClass.Finance or CoopCommandClass.SeatCleanup ).ToArray(),
			Allowed( seat ).ToArray(), "finance alone" );
		level.SetSeatRights( seat, CoopRights.Attractions );
		CollectionAssert.AreEquivalent( Enum.GetValues<ParkCommandKind>().Where( kind => CoopBuildRights.ClassOf( kind ) is CoopCommandClass.Attractions or CoopCommandClass.SeatCleanup ).ToArray(),
			Allowed( seat ).ToArray(), "the attractions alone" );
		level.SetSeatRights( seat, CoopRights.Staff );
		CollectionAssert.AreEquivalent( Enum.GetValues<ParkCommandKind>().Where( kind => CoopBuildRights.ClassOf( kind ) is CoopCommandClass.Staff or CoopCommandClass.SeatCleanup ).ToArray(),
			Allowed( seat ).ToArray(), "staff alone (C4c-2)" );
		level.SetSeatRights( seat, CoopRights.Build );
		// a session's own kinds and the speed stay the host's
		foreach ( var command in new ParkCommand[] { new SetSeatRightsCommand( seat, CoopRights.Build ), new SetBuildGrantCommand( seat, true ), new ReleaseSeatCommand( seat ),
			new SetGameSpeedCommand( GameSpeed.Fast ), new SetSessionClockCommand( 63928191600 ) } )
			Assert.IsFalse( rights.Allows( seat, command ), $"{command.Kind}" );
		// what a guest's refusal says: building without the grant, management without its right (C4b), and nothing of its own for the host's kinds
		level.SetSeatRights( seat, CoopRights.None );
		Assert.AreEqual( CoopBuildRights.NotGrantedReason, rights.RefusalReason( seat, new LayLineCommand( CellToolMode.Path, -1, 5, 20, 12, 20 ) ) );
		foreach ( var management in new ParkCommand[] { new SetEntranceFeeCommand( 20 ), new SetResearchWorkloadCommand( 50 ), new SetAttractionOpenCommand( 17, false ), new FireStaffCommand( 3 ) } )
			Assert.AreEqual( CoopBuildRights.NotPermittedReason, rights.RefusalReason( seat, management ), $"{management.Kind}" );
		Assert.AreEqual( CoopRefusal.NotPermitted, CoopBuildRights.RefusalOf( CoopBuildRights.NotPermittedReason ) );
		Assert.IsNull( rights.RefusalReason( seat, new SetGameSpeedCommand( GameSpeed.Fast ) ) );
	}

	/// <summary>
	/// The Ctrl copy of an object (<c>ParkHud.CopyObject</c>, tp.exe 0x4879D0) arms the tool of its type and its placement is the same <see cref="PlaceObjectCommand"/> any placement issues, so a guest the
	/// host has not let build gets the grant's refusal there, and a ride claimed against the guest does not stop a copy of its type from being placed on free ground.
	/// </summary>
	[TestMethod]
	public void ACopyOfAnObjectIsPlacedUnderTheSameRightsAsAnyPlacement()
	{
		var (level, one) = NewPark();
		var rights = new CoopBuildRights( level );
		const int seat = 2;
		var copy = level.BuildCommand( one.Entry, 40, 40, one.Placement.Rotation );
		Assert.IsFalse( rights.Allows( seat, copy ), "no grant, no placement" );
		Assert.AreEqual( CoopBuildRights.NotGrantedReason, rights.RefusalReason( seat, copy ) );
		level.SetSeatRights( seat, CoopRights.Build );
		var claims = (SortedDictionary<int, int>?)typeof( Level ).GetField( "rideClaims", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( level ) ?? new SortedDictionary<int, int>();
		claims[one.Runtime.AttractionId] = ParkCommands.LocalPlayer;
		typeof( Level ).GetField( "rideClaims", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, claims );
		Assert.IsTrue( level.ClaimedAgainst( seat, one.Runtime.AttractionId ), "the object is claimed against the guest" );
		Assert.AreEqual( rights.Allows( seat, level.BuildCommand( one.Entry, 44, 44, 0 ) ), rights.Allows( seat, copy ), "a copy's placement is a placement like the first one's" );
		Assert.AreNotEqual( Level.ClaimedReason, rights.RefusalReason( seat, copy ), "the claim guards the ride, not the type" );
	}

	[TestMethod]
	public void OneReducerSetsASeatsRightsForBothCommandsThatWriteThem()
	{
		var (level, one) = NewPark();
		var commands = level.Commands;
		Assert.AreEqual( 0, level.SeatRights.Count );
		Assert.IsTrue( commands.Issue( new SetSeatRightsCommand( 2, CoopRights.Build ) ).Accepted );
		Assert.AreEqual( CoopRights.Build, level.RightsOf( 2 ) );
		Assert.IsTrue( level.HasBuildGrant( 2 ) );
		CollectionAssert.AreEqual( new[] { 2 }, level.BuildGrants.ToArray() );
		// the grant's command flips the building bit alone: a right beside it stays (set here directly, as no part offers one yet)
		level.SetSeatRights( 2, CoopRights.Build | CoopRights.Finance );
		Assert.IsTrue( commands.Issue( new SetBuildGrantCommand( 2, false ) ).Accepted );
		Assert.AreEqual( CoopRights.Finance, level.RightsOf( 2 ) );
		Assert.IsFalse( level.HasBuildGrant( 2 ) );
		Assert.IsTrue( commands.Issue( new SetBuildGrantCommand( 2, true ) ).Accepted );
		Assert.AreEqual( CoopRights.Build | CoopRights.Finance, level.RightsOf( 2 ) );
		// a seat without a right has no row; the rows go in seat order
		Assert.IsTrue( commands.Issue( new SetSeatRightsCommand( 2, CoopRights.None ) ).Accepted );
		Assert.AreEqual( 0, level.SeatRights.Count );
		Assert.IsTrue( commands.Issue( new SetSeatRightsCommand( 5, CoopRights.Build ) ).Accepted );
		Assert.IsTrue( commands.Issue( new SetBuildGrantCommand( 1, true ) ).Accepted );
		CollectionAssert.AreEqual( new[] { 1, 5 }, level.SeatRights.Keys.ToArray() );
		// rights are session state: the session digest covers them, the world hash does not
		var (hash, digest) = (level.ComputeStateHash(), CoopDigest.Compute( level ));
		Assert.IsTrue( commands.Issue( new SetSeatRightsCommand( 3, CoopRights.Build ) ).Accepted );
		Assert.AreEqual( hash, level.ComputeStateHash() );
		Assert.AreNotEqual( digest, CoopDigest.Compute( level ) );
		// [EXT:online-coop-claims] building taken back by either command lets go of the seat's claim in the same record
		var stations = (Dictionary<OriginalObject, TrackStation>)typeof( Level ).GetField( "trackStations", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( level )!;
		stations[one] = new TrackStation( 0, 0, (one.Placement.X, one.Placement.Y), (one.Placement.X + 1, one.Placement.Y) );
		var ride = one.Runtime.AttractionId;
		foreach ( var revoke in new ParkCommand[] { new SetSeatRightsCommand( 3, CoopRights.None ), new SetBuildGrantCommand( 3, false ) } )
		{
			Assert.IsTrue( commands.Issue( new SetSeatRightsCommand( 3, CoopRights.Build ) ).Accepted );
			Assert.IsTrue( level.ExecuteClaimRide( 3, ride ).Accepted );
			var log = commands.Log.Count;
			Assert.IsTrue( commands.Issue( revoke ).Accepted );
			Assert.IsNull( level.ClaimOf( ride ), $"{revoke.Kind}: the claim went with the building" );
			Assert.AreEqual( log + 1, commands.Log.Count, "in the one record" );
		}
		// the session's start and end take every right back
		level.ClearSeatRights();
		Assert.AreEqual( 0, level.SeatRights.Count );
	}

	[TestMethod]
	public void TheRightsCommandTakesOnlyAGuestsSeatAndTheRightsThisBuildOffers()
	{
		Assert.AreEqual( CoopRights.Build | CoopRights.Attractions | CoopRights.Staff | CoopRights.Research | CoopRights.Finance, CoopRightsOffer.Offered,
			"C4c-2 offers all five: building, the attractions, staff, research and finance" );
		CollectionAssert.AreEqual( new[] { CoopRights.Build, CoopRights.Attractions, CoopRights.Staff, CoopRights.Research, CoopRights.Finance }, CoopRightsOffer.Each.ToArray(),
			"the dialog's order" );
		foreach ( var seat in new[] { 1, 3, CoopJoinState.MaximumPlayerEntries - 1 } )
			foreach ( var rights in new[] { CoopRights.None, CoopRights.Build, CoopRights.Attractions, CoopRights.Staff, CoopRights.Research, CoopRights.Finance,
				CoopRights.Research | CoopRights.Staff, (CoopRights)0x1F } )
				Assert.IsTrue( new SetSeatRightsCommand( seat, rights ).IsWellFormed, $"seat {seat}, {rights}" );
		foreach ( var seat in new[] { int.MinValue, -1, 0, CoopJoinState.MaximumPlayerEntries } )
			Assert.IsFalse( new SetSeatRightsCommand( seat, CoopRights.Build ).IsWellFormed, $"seat {seat}: never the host's, never past the per-player bound" );
		foreach ( var rights in new[] { (CoopRights)0x20, CoopRights.Staff | (CoopRights)0x20, (CoopRights)0x80, (CoopRights)0xFF } )
			Assert.IsFalse( new SetSeatRightsCommand( 2, rights ).IsWellFormed, $"{rights}: a right past the five cannot be given" );
		// a hand-made record that gives a right past the five does not decode, so no stream carries it; one made in memory is refused before the park and its log
		var bytes = ParkCommandCodec.Encode( new SetSeatRightsCommand( 2, CoopRights.Build ) );
		// the rights are the record's last field, a zigzag varint: the bit past the five is 32, written 0x40
		bytes[^1] = 0x40;
		Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( bytes ) );
		var (level, _) = NewPark();
		var log = level.Commands.Log.Count;
		Assert.IsFalse( level.Commands.Apply( new SetSeatRightsCommand( 2, (CoopRights)0x20 ) ).Accepted );
		Assert.AreEqual( (log, CoopRights.None), (level.Commands.Log.Count, level.RightsOf( 2 )) );
		// the staff right is given as any other (C4c-2)
		Assert.IsTrue( level.Commands.Apply( new SetSeatRightsCommand( 2, CoopRights.Staff ) ).Accepted );
		Assert.AreEqual( CoopRights.Staff, level.RightsOf( 2 ) );
	}

	[TestMethod]
	public void ASeatsReleaseTakesEverythingItHoldsInItsOneRecord()
	{
		var (level, one) = NewPark();
		var commands = level.Commands;
		// seat 2 holds a right alone, seat 3 a carried type alone (its Move tool's pick-up)
		Assert.IsTrue( commands.Issue( new SetSeatRightsCommand( 2, CoopRights.Build ) ).Accepted );
		Assert.IsTrue( commands.Apply( new PickUpObjectCommand( one.Runtime.AttractionId ) { Player = 3 } ).Accepted );
		CollectionAssert.AreEqual( new[] { 2, 3 }, level.HoldingGuestSeats().ToArray() );
		Assert.IsTrue( level.SeatHoldsAnything( 2 ) && level.SeatHoldsAnything( 3 ) );
		Assert.IsFalse( level.SeatHoldsAnything( 4 ) || level.SeatHoldsAnything( ParkCommands.LocalPlayer ), "an empty seat and the host's own hold nothing to release" );
		foreach ( var seat in new[] { 2, 3 } )
		{
			var released = commands.Issue( new ReleaseSeatCommand( seat ) );
			Assert.IsTrue( released.Accepted && released.Value is true, $"seat {seat} held something" );
			Assert.IsFalse( level.SeatHoldsAnything( seat ) );
		}
		Assert.AreEqual( (CoopRights.None, false), (level.RightsOf( 2 ), level.CarriesEntry( 3 )) );
		Assert.AreEqual( 0, level.HoldingGuestSeats().Count() );
		var none = commands.Issue( new ReleaseSeatCommand( 4 ) );
		Assert.IsTrue( none.Accepted && none.Value is false, "a seat that held nothing releases nothing" );
	}

	/// <summary>
	/// [EXT:online-coop-commands] A stale-checked command whose expectation no longer holds is refused as stale before the park's own rules, and one whose expectation holds runs as the host's
	/// does (C4b): a level upgrade naming another level, a cancellation naming another pending level, a research move naming another project.
	/// </summary>
	[TestMethod]
	public void AStaleCheckedCommandWhoseExpectationNoLongerHoldsIsRefusedAsStale()
	{
		var (level, one) = NewPark();
		var commands = level.Commands;
		var ride = one.Runtime.AttractionId;
		var state = level.Park!.Economy.TryGetObject( level.GetEconomyInstance( one )!.Value, out var found ) ? found : null;
		Assert.IsNotNull( state );
		foreach ( var stale in new ParkCommand[] { new BuyUpgradeCommand( ride, state.Level + 2 ), new CancelUpgradeCommand( ride, state.PendingLevel + 1 ) } )
		{
			var result = commands.Issue( stale );
			Assert.AreEqual( (false, ParkSettingCommand.StaleReason), (result.Accepted, result.Reason), stale.ToString() );
		}
		// the expected level passes the check, and the park's own rules decide as for the host's command
		Assert.AreNotEqual( ParkSettingCommand.StaleReason, commands.Issue( new BuyUpgradeCommand( ride, state.Level + 1 ) ).Reason );
		Assert.AreEqual( commands.Issue( new BuyUpgradeCommand( ride ) ).Reason, commands.Issue( new BuyUpgradeCommand( ride, state.Level + 1 ) ).Reason );
		// a lab with rides to choose from (ManagementUiTests' research cursor park)
		var economy = new ParkEconomy( EconomyTestData.Settings( extra: "ResearchTech[1].PercentageForThisTech 0\nResearchTech[2].PercentageForThisTech 0\n" ), EconomyTestData.Catalog(),
			ParkGameMode.FullSimulation, 7 );
		var lab = CommandTestLevel.Create( economy );
		var research = economy.Research;
		Assert.IsTrue( research.SelectableItems( ResearchCategory.Ride ).Count > 1 );
		var current = research.Current( ResearchCategory.Ride )!;
		var moved = lab.Commands.Issue( new MoveResearchCommand( ResearchCategory.Ride, 1, current.InfoId, current.Level + 1 ) );
		Assert.AreEqual( (false, ParkSettingCommand.StaleReason), (moved.Accepted, moved.Reason), "another project than the current one" );
		Assert.AreEqual( current, research.Current( ResearchCategory.Ride ), "nothing moved" );
		Assert.IsTrue( lab.Commands.Issue( new MoveResearchCommand( ResearchCategory.Ride, 1, current.InfoId, current.Level ) ).Accepted, "the current project moves" );
		Assert.AreNotEqual( current, research.Current( ResearchCategory.Ride ) );
	}

	/// <summary>
	/// [EXT:online-coop-claims] The claim guard of a guest's attractions commands (C4b-2): exactly the five kinds that name a claimable ride (the open switch, the settings, a level bought or
	/// cancelled, the call) are asked about its claim; a price, a game's prize and a shop's settings are not. The answer goes in order: without the right the rights' refusal whatever the
	/// claims, with it on another seat's claimed ride the claim's, the claiming seat's own allowed, and the host's allowed.
	/// </summary>
	[TestMethod]
	public void TheClaimGuardNamesFiveKindsOfAClaimableRideAndAnswersAfterTheRightInOrder()
	{
		var (level, one) = NewPark();
		var rights = new CoopBuildRights( level );
		var ride = one.Runtime.AttractionId;
		var guarded = new ParkCommand[] { new SetAttractionOpenCommand( ride, false ), new SetRideSettingsCommand( ride, 50, 2, 30 ), new BuyUpgradeCommand( ride ), new BuyUpgradeCommand( ride, 1 ),
			new CancelUpgradeCommand( ride ), new CancelUpgradeCommand( ride, 1, 1 ), new RequestMaintenanceCommand( ride, true ) };
		var unguarded = new ParkCommand[] { new SetPriceCommand( ride, 20 ), new SetGamePrizeCommand( ride, 20, 5, 50 ), new SetShopSettingsCommand( ride, 50, 10 ) };
		foreach ( var command in guarded )
			Assert.AreEqual( ride, CoopBuildRights.AttractionRide( command ), $"{command.Kind} names its ride" );
		foreach ( var command in unguarded )
			Assert.IsNull( CoopBuildRights.AttractionRide( command ), $"{command.Kind} names no claimable ride" );
		CollectionAssert.AreEquivalent( new[] { ParkCommandKind.SetAttractionOpen, ParkCommandKind.SetRideSettings, ParkCommandKind.BuyUpgrade, ParkCommandKind.CancelUpgrade, ParkCommandKind.RequestMaintenance },
			ParkCommandCodecTests.Schema.Where( row => CoopBuildRights.AttractionRide( row.Command ) != null ).Select( row => row.Command.Kind ).Distinct().ToArray(), "exactly five kinds of the codec's table" );
		// a seat holds the ride's claim (a claimable ride is a track ride)
		var stations = (Dictionary<OriginalObject, TrackStation>)typeof( Level ).GetField( "trackStations", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( level )!;
		stations[one] = new TrackStation( 0, 0, (one.Placement.X, one.Placement.Y), (one.Placement.X + 1, one.Placement.Y) );
		const int holder = 3, other = 2, without = 4;
		level.SetSeatRights( holder, CoopRights.Build | CoopRights.Attractions );
		Assert.IsTrue( level.ExecuteClaimRide( holder, ride ).Accepted );
		level.SetSeatRights( other, CoopRights.Attractions );
		level.SetSeatRights( without, CoopRights.Build );
		foreach ( var command in guarded )
		{
			// without the attractions right: the rights' refusal, whatever the claim
			Assert.IsFalse( rights.Allows( without, command ), $"{command.Kind}: no right" );
			Assert.AreEqual( CoopBuildRights.NotPermittedReason, rights.RefusalReason( without, command ), $"{command.Kind}: the rights' refusal, not the claim's" );
			// with it on another seat's claimed ride: the claim's
			Assert.IsFalse( rights.Allows( other, command ), $"{command.Kind}: another seat's claim" );
			Assert.AreEqual( Level.ClaimedReason, rights.RefusalReason( other, command ), $"{command.Kind}: the claim's refusal" );
			// the claiming seat's own, and the host's
			Assert.IsTrue( rights.Allows( holder, command ), $"{command.Kind}: the claiming seat's own" );
			Assert.IsNull( rights.RefusalReason( holder, command ), $"{command.Kind}: the claiming seat's own" );
			Assert.IsTrue( rights.Allows( ParkCommands.LocalPlayer, command ), $"{command.Kind}: the host's management of a claimed ride stays allowed" );
			Assert.IsNull( rights.RefusalReason( ParkCommands.LocalPlayer, command ), $"{command.Kind}: the host's" );
		}
		foreach ( var command in unguarded )
		{
			Assert.IsTrue( rights.Allows( other, command ), $"{command.Kind}: no claim guards it" );
			Assert.IsNull( rights.RefusalReason( other, command ), $"{command.Kind}" );
			Assert.AreEqual( CoopBuildRights.NotPermittedReason, rights.RefusalReason( without, command ), $"{command.Kind}: without the right" );
		}
		// with no claim on the ride every seat with the right asks nothing of it
		Assert.IsTrue( level.ExecuteReleaseRide( holder, ride ).Accepted );
		Assert.IsNull( level.ClaimOf( ride ) );
		foreach ( var command in guarded )
			Assert.IsTrue( rights.Allows( other, command ), $"{command.Kind}: a ride nobody claims" );
	}
}
