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
		Assert.AreEqual( "1 2 4 5 10 14 15 16 18 54 75 76 78 79 80 81 82 83 90 91 92 93 94 95 96 97 98 103", classes[CoopCommandClass.GuestBuilding], "C3's building, claim-checked" );
		Assert.AreEqual( "6 104", classes[CoopCommandClass.SeatCleanup], "a Move's end and a claim's release" );
		Assert.AreEqual( "32 33 34 35 36 43 44 45", classes[CoopCommandClass.Attractions] );
		Assert.AreEqual( "37 47 48 49 60 61 62 63 64", classes[CoopCommandClass.Staff] );
		Assert.AreEqual( "38 39 40", classes[CoopCommandClass.Research] );
		Assert.AreEqual( "30 31 41 42 50 53", classes[CoopCommandClass.Finance] );
		Assert.AreEqual( "3 11 12 13 17 70 71 72 74 77", classes[CoopCommandClass.HostTools], "what only the host's tools issue" );
		Assert.AreEqual( "20 21 23 46 51 73 100 101 102 105", classes[CoopCommandClass.HostOnly] );
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
		// a management right no part offers yet is refused even when the seat's rights hold it (set here directly: no command can give one)
		level.SetSeatRights( seat, (CoopRights)0x1F );
		CollectionAssert.AreEquivalent( building, Allowed( seat ).ToArray(), "every right held, building's alone answers" );
		// a session's own kinds and the speed stay the host's
		foreach ( var command in new ParkCommand[] { new SetSeatRightsCommand( seat, CoopRights.Build ), new SetBuildGrantCommand( seat, true ), new ReleaseSeatCommand( seat ),
			new SetGameSpeedCommand( GameSpeed.Fast ), new SetSessionClockCommand( 63928191600 ) } )
			Assert.IsFalse( rights.Allows( seat, command ), $"{command.Kind}" );
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
		Assert.AreEqual( CoopRights.Build, CoopRightsOffer.Offered, "C4a offers building alone" );
		foreach ( var seat in new[] { 1, 3, CoopJoinState.MaximumPlayerEntries - 1 } )
			foreach ( var rights in new[] { CoopRights.None, CoopRights.Build } )
				Assert.IsTrue( new SetSeatRightsCommand( seat, rights ).IsWellFormed, $"seat {seat}, {rights}" );
		foreach ( var seat in new[] { int.MinValue, -1, 0, CoopJoinState.MaximumPlayerEntries } )
			Assert.IsFalse( new SetSeatRightsCommand( seat, CoopRights.Build ).IsWellFormed, $"seat {seat}: never the host's, never past the per-player bound" );
		foreach ( var rights in new[] { CoopRights.Attractions, CoopRights.Staff, CoopRights.Research, CoopRights.Finance, CoopRights.Build | CoopRights.Staff, (CoopRights)0x20, (CoopRights)0xFF } )
			Assert.IsFalse( new SetSeatRightsCommand( 2, rights ).IsWellFormed, $"{rights}: a right whose actions do not wait for the host yet cannot be given" );
		// a hand-made record that gives the finance right does not decode, so no stream carries it; one made in memory is refused before the park and its log
		var bytes = ParkCommandCodec.Encode( new SetSeatRightsCommand( 2, CoopRights.Build ) );
		bytes[^1] = 0x20;
		Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( bytes ) );
		var (level, _) = NewPark();
		var log = level.Commands.Log.Count;
		Assert.IsFalse( level.Commands.Apply( new SetSeatRightsCommand( 2, CoopRights.Finance ) ).Accepted );
		Assert.AreEqual( (log, CoopRights.None), (level.Commands.Log.Count, level.RightsOf( 2 )) );
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
}
