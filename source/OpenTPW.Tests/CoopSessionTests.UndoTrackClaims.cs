using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-claims] [EXT:online-coop-undo] Other rides' tiles in a shared park (docs/COOP.md, "Claims"; C5-3): a track line and its take-back leave alone the protected track of every
/// kart or water ride another seat claims (<see cref="TrackProvenance"/>), an older save's owner-zero tiles included, which any ride's builder joins; an unclaimed ride's track is any granted
/// player's to join, as in single player. The test is of real effects on a copy of the park: a neighbour's refresh that writes the same values changes nothing, a refused line or take-back
/// leaves the park as it was.
/// </summary>
public partial class CoopSessionTests
{
	private const byte NegYArm = TrackHeading.NegY, PosXArm = TrackHeading.PosX, PosYArm = TrackHeading.PosY, NegXArm = TrackHeading.NegX;

	/// <summary>Ride B beside the kart <paramref name="a"/> of <see cref="PlaceKartRide"/>: placed at (20, 21), its station (20, 22) and (22, 22), its exit (24, 22); its kart.</summary>
	private OriginalObject PlaceRideB( Park park ) => PlaceKartRide( park, 20, 21 );

	/// <summary>A tile laid at <paramref name="at"/> for <paramref name="owner"/> (0: an older save's, which no owner word tags), its arms then set to <paramref name="mask"/>.</summary>
	private static void SaveTile( Park park, OriginalObject ride, (int X, int Y) at, byte mask, int owner = 0 )
	{
		var builder = park.Level.BuilderOf( ride )!;
		if ( !builder.IsTile( at.X, at.Y ) )
			Assert.AreEqual( CellBuildResult.Ok, builder.PlaceTile( at.X, at.Y, owner ), $"a tile at {at}" );
		builder.Tracks.Set( at.X, at.Y, builder.Tracks.Get( at.X, at.Y ) with { Mask = mask, Owner = owner } );
		builder.Refresh( at.X, at.Y );
	}

	/// <summary>B's owner word.</summary>
	private static int OwnerOf( Park park, OriginalObject ride ) => park.Level.BuilderOf( ride )!.OwnerOf( park.Level.TrackStations[ride] );

	/// <summary>
	/// B's track of an older save (owner word 0): from its exit up through a junction on (24, 20) to (24, 18), whose arm dangles toward (24, 16), and a side branch from the junction over
	/// (26, 20) to (26, 18), whose arm dangles toward (26, 16). A line of A's down column 24 or 26 joins one of them.
	/// </summary>
	private static void DanglingNeighbour( Park park, OriginalObject b )
	{
		SaveTile( park, b, (24, 22), NegXArm | NegYArm, OwnerOf( park, b ) );
		SaveTile( park, b, (24, 20), PosYArm | NegYArm | PosXArm );
		SaveTile( park, b, (24, 18), PosYArm | NegYArm );
		SaveTile( park, b, (26, 20), NegXArm | NegYArm );
		SaveTile( park, b, (26, 18), PosYArm | NegYArm );
	}

	/// <summary>The park's own rights, A's claim the guest seat's (1), B's the host's: what every peer asks of a guest's line of A's.</summary>
	private static CoopBuildRights ClaimedNeighbour( Park park, OriginalObject a, OriginalObject b )
	{
		var level = park.Level;
		level.SetBuildGrant( 1, true );
		Assert.IsTrue( level.ExecuteClaimRide( 1, a.Runtime.AttractionId ).Accepted );
		Assert.IsTrue( level.ExecuteClaimRide( ParkCommands.LocalPlayer, b.Runtime.AttractionId ).Accepted );
		return new CoopBuildRights( level );
	}

	/// <summary>A line of A's from <paramref name="from"/> to <paramref name="to"/>, as its tool sends it.</summary>
	private static LayTrackLineCommand LineOf( OriginalObject a, (int X, int Y) from, (int X, int Y) to ) => new( a.Runtime.AttractionId, from.X, from.Y, to.X, to.Y ) { Player = 1 };

	/// <summary>Whether the rights refuse <paramref name="line"/> in the claim's words, and the park, its rides and its ledger stay as they were whatever they answer.</summary>
	private static bool RefusedInTheClaimsWords( Park park, CoopBuildRights rights, LayTrackLineCommand line )
	{
		var tracks = TrackCellsOf( park );
		var balance = park.Economy.Balance;
		var valid = park.Level.TrackStations.Keys.Select( ride => ride.Runtime.Bump!.IsTrackValid ).ToArray();
		var refused = !rights.LeavesClaimsAlone( 1, line );
		Assert.AreEqual( refused, !rights.Allows( 1, line ) );
		Assert.AreEqual( refused ? Level.ClaimedReason : null, rights.RefusalReason( 1, line ) );
		Assert.AreEqual( tracks, TrackCellsOf( park ), "the prediction ran on a copy" );
		Assert.AreEqual( balance, park.Economy.Balance );
		CollectionAssert.AreEqual( valid, park.Level.TrackStations.Keys.Select( ride => ride.Runtime.Bump!.IsTrackValid ).ToArray() );
		return refused;
	}

	[TestMethod]
	public void ALineJoiningAClaimedRidesOwnerZeroTrackIsRefusedAndAnUnclaimedOnesIsAnyonesToJoin()
	{
		var park = NewPark();
		park.Enter();
		var a = PlaceKartRide( park );
		var b = PlaceRideB( park );
		DanglingNeighbour( park, b );
		var rights = ClaimedNeighbour( park, a, b );
		// a line down column 24 lays a tile at (24, 16), which takes the arm of B's (24, 18) that points at it; one down column 26 joins the side branch's dangling arm
		Assert.IsTrue( RefusedInTheClaimsWords( park, rights, LineOf( a, (24, 10), (24, 16) ) ), "joining B's dangling arm" );
		Assert.IsTrue( RefusedInTheClaimsWords( park, rights, LineOf( a, (26, 10), (26, 16) ) ), "joining the side branch beyond B's junction" );
		Assert.IsFalse( RefusedInTheClaimsWords( park, rights, LineOf( a, (24, 10), (24, 14) ) ), "a line short of them" );
		// the host's line of an unclaimed ride beside B is asked the same way: the host may not change another seat's claimed track either
		Assert.IsTrue( park.Level.ExecuteReleaseRide( ParkCommands.LocalPlayer, a.Runtime.AttractionId ).Accepted );
		Assert.IsTrue( park.Level.ExecuteReleaseRide( ParkCommands.LocalPlayer, b.Runtime.AttractionId ).Accepted );
		Assert.IsTrue( park.Level.ExecuteClaimRide( 1, b.Runtime.AttractionId ).Accepted );
		Assert.IsFalse( rights.LeavesClaimsAlone( ParkCommands.LocalPlayer, LineOf( a, (24, 10), (24, 16) ) with { Player = ParkCommands.LocalPlayer } ) );
		// unclaimed, B's track is anyone's to join, as in single player
		Assert.IsTrue( park.Level.ExecuteReleaseRide( ParkCommands.LocalPlayer, b.Runtime.AttractionId ).Accepted );
		Assert.IsTrue( park.Level.ExecuteClaimRide( 1, a.Runtime.AttractionId ).Accepted );
		Assert.IsFalse( RefusedInTheClaimsWords( park, rights, LineOf( a, (24, 10), (24, 16) ) ) );
	}

	[TestMethod]
	public void AClaimedRidesTrackHoldsWhatItsOneSidedArmsItsExitSeedAndItsWalkReachAndATaggedTileOfAnothersItReaches()
	{
		// a one-sided arm: B's exit points up at an owner-zero tile across it, whose own arms do not point back; A's line over it raises its counter
		{
			var park = NewPark();
			park.Enter();
			var a = PlaceKartRide( park );
			var b = PlaceRideB( park );
			SaveTile( park, b, (24, 22), NegXArm | NegYArm, OwnerOf( park, b ) );
			SaveTile( park, b, (24, 20), NegXArm | PosXArm );
			var rights = ClaimedNeighbour( park, a, b );
			Assert.IsTrue( RefusedInTheClaimsWords( park, rights, LineOf( a, (22, 20), (26, 20) ) ), "over the tile B's arm reaches" );
		}
		// the station's second tile has no arm toward its exit, an older save's tile: the laid exit seeds the flood, which goes on beyond it
		{
			var park = NewPark();
			park.Enter();
			var a = PlaceKartRide( park );
			var b = PlaceRideB( park );
			var station = park.Level.TrackStations[b];
			SaveTile( park, b, station.Second, NegXArm, OwnerOf( park, b ) );
			SaveTile( park, b, (24, 22), PosXArm );
			SaveTile( park, b, (26, 22), NegXArm | PosXArm );
			var rights = ClaimedNeighbour( park, a, b );
			Assert.IsTrue( RefusedInTheClaimsWords( park, rights, LineOf( a, (26, 18), (26, 24) ) ), "over the tile beyond the exit" );
		}
		// B's circuit walk goes straight on through a tile whose arms are across its heading, onto an owner-zero tile no arm points at
		{
			var park = NewPark();
			park.Enter();
			var a = PlaceKartRide( park );
			var b = PlaceRideB( park );
			SaveTile( park, b, (24, 22), NegXArm | PosXArm, OwnerOf( park, b ) );
			SaveTile( park, b, (26, 22), NegYArm | PosYArm );
			SaveTile( park, b, (28, 22), NegYArm | PosYArm );
			var rights = ClaimedNeighbour( park, a, b );
			Assert.IsTrue( RefusedInTheClaimsWords( park, rights, LineOf( a, (28, 18), (28, 24) ) ), "over the tile the walk reaches" );
		}
		// a tile tagged A that B's owner-zero track reaches: A's line rewriting it is refused while B is claimed, and taken when it is not
		{
			var park = NewPark();
			park.Enter();
			var a = PlaceKartRide( park );
			var b = PlaceRideB( park );
			SaveTile( park, b, (24, 22), NegXArm | NegYArm, OwnerOf( park, b ) );
			SaveTile( park, b, (24, 20), PosYArm | NegYArm );
			SaveTile( park, a, (24, 18), PosYArm | NegYArm, OwnerOf( park, a ) );
			var rights = ClaimedNeighbour( park, a, b );
			Assert.IsTrue( RefusedInTheClaimsWords( park, rights, LineOf( a, (24, 18), (24, 14) ) ), "A's own tile, which B's track reaches" );
			Assert.IsTrue( park.Level.ExecuteReleaseRide( ParkCommands.LocalPlayer, b.Runtime.AttractionId ).Accepted );
			Assert.IsFalse( RefusedInTheClaimsWords( park, rights, LineOf( a, (24, 18), (24, 14) ) ), "B unclaimed" );
		}
		// two tagged rides side by side with no connection: A's line beside B's exit refreshes it to the same values, which changes nothing
		{
			var park = NewPark();
			park.Enter();
			var a = PlaceKartRide( park );
			var b = PlaceRideB( park );
			var rights = ClaimedNeighbour( park, a, b );
			Assert.IsFalse( RefusedInTheClaimsWords( park, rights, LineOf( a, (24, 10), (24, 20) ) ), "beside B, not joined" );
		}
	}

	/// <summary>A shared park with A and B (<paramref name="prepare"/> runs on each before the session), the guest granted; the guest's A, and the host's A and B.</summary>
	private (Park Host, Park Guest, View View, OriginalObject A, OriginalObject HostA, OriginalObject HostB, int Seat) UndoKarts( Action<Park, OriginalObject, OriginalObject>? prepare = null )
	{
		var (host, guest, _, _) = Shared( ( park, _ ) =>
		{
			var a = PlaceKartRide( park );
			var b = PlaceRideB( park );
			prepare?.Invoke( park, a, b );
		} );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		OriginalObject First( Park park ) => park.Level.TrackStations.Keys.OrderBy( ride => ride.Runtime.AttractionId ).First();
		OriginalObject Second( Park park ) => park.Level.TrackStations.Keys.OrderBy( ride => ride.Runtime.AttractionId ).Last();
		return (host, guest, view, First( guest ), First( host ), Second( host ), guest.Level.Commands.Player);
	}

	/// <summary>The host's track tool on <paramref name="ride"/>, which claims it; or with <paramref name="leave"/> the tool left and its frame's release of the claim.</summary>
	private void HostOnRide( Park host, Park guest, View view, View hostView, OriginalObject ride, bool leave = false )
	{
		host.Enter();
		if ( leave )
		{
			host.Level.ExitTrackTool();
			hostView.Update();
			RoundsUntil( () => guest.Level.ClaimOf( ride.Runtime.AttractionId ) == null, host, guest, view, "the host's release reached the guest" );
			return;
		}
		Assert.IsTrue( host.Level.EnterTrackTool( ride ) );
		RoundsUntil( () => guest.Level.ClaimOf( ride.Runtime.AttractionId ) == ParkCommands.LocalPlayer, host, guest, view, "the host's claim reached the guest" );
	}

	[TestMethod]
	public void AGuestsLineJoiningTheHostsClaimedRideIsRefusedOnEveryPeerAndItsTakeBackOverItIsInTheWay()
	{
		var (host, guest, view, a, hostA, hostB, seat) = UndoKarts( ( park, _, b ) => DanglingNeighbour( park, b ) );
		var hostView = ViewOf( host );
		var (twin, twinA) = TwinKart( ( park, _ ) => DanglingNeighbour( park, PlaceRideB( park ) ) );
		// B claimed by the host (its button opens the edit tool: no edit has walked B's track word yet), so the guest's line that would join B's dangling arm is refused in the claim's
		// words, nothing laid
		HostOnRide( host, guest, view, hostView, hostB );
		Assert.AreEqual( CellToolMode.KartEdit, host.Level.CellTool.Mode );
		GuestTrackTool( host, guest, view, a );
		var tracks = TrackCellsOf( host );
		guest.Enter();
		Assert.IsTrue( guest.Level.ClickCellTool( KartAt( guest, a, 4, 6 ) ) is { Waiting: null, CommandRefused: true }, "the guest's own park refuses it before it goes" );
		Assert.IsFalse( guest.Level.CellTool.IsActive, "the refusal ended the tool" );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopClaimed ), "in the claim's words" );
		// past the guest's own rights, the host refuses it at its record, so every peer does
		var (from, to) = (KartAt( guest, a, 4, 0 ), KartAt( guest, a, 4, 6 ));
		var line = Asked( host, guest, new LayTrackLineCommand( a.Runtime.AttractionId, from.X, from.Y, to.X, to.Y ) );
		Assert.IsFalse( line.Accepted );
		Assert.AreEqual( Level.ClaimedReason, line.Result?.Reason );
		Assert.AreEqual( tracks, TrackCellsOf( host ), "nothing laid" );
		CatchUpAndCompare( host, guest );
		// B unclaimed: the line joins, as in single player
		HostOnRide( host, guest, view, hostView, hostB, leave: true );
		GuestTrackTool( host, guest, view, a );
		TwinTool( twin, twinA );
		GuestTrackLine( host, guest, view, KartAt( guest, a, 4, 6 ) );
		TwinClick( twin, KartAt( twin, twinA, 4, 6 ) );
		Assert.AreEqual( TrackCellsOf( twin ), TrackCellsOf( host ) );
		// B claimed again: the line's take-back would take B's (24, 18) arm off with the tile it clears: in the way, nothing changed
		HostOnRide( host, guest, view, hostView, hostB );
		tracks = TrackCellsOf( host );
		var balance = host.Economy.Balance;
		Assert.IsFalse( GuestTrackBackspace( host, guest, view ).Taken );
		Assert.AreEqual( TakeBackVerdict.InTheWay, TakeBacksOf( host, seat ).Last().Verdict );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopUndoInTheWay ) );
		Assert.AreEqual( tracks, TrackCellsOf( host ), "B's tiles and A's line intact" );
		Assert.AreEqual( balance, host.Economy.Balance );
		Assert.IsTrue( guest.Level.CellTool.IsActive, "the tool stays on" );
		CatchUpAndCompare( host, guest );
		// B unclaimed again: its take-back goes, as single player's Backspace
		HostOnRide( host, guest, view, hostView, hostB, leave: true );
		var (taken, refund) = GuestTrackBackspace( host, guest, view );
		Assert.IsTrue( taken );
		Assert.AreEqual( TwinBackspace( twin ), refund );
		Assert.AreEqual( TrackCellsOf( twin ), TrackCellsOf( host ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsLineBesideAClaimedRideIsTakenBackAndOneOverATileItsOneSidedArmReachesIsInTheWay()
	{
		// B's track of an older save from its exit along column 26 up to (26, 14), whose arm points up at an owner-zero tile on (26, 12) that points only away, along row 12
		var (host, guest, view, a, hostA, hostB, seat) = UndoKarts( ( park, _, b ) =>
		{
			SaveTile( park, b, (24, 22), NegXArm | PosXArm, OwnerOf( park, b ) );
			SaveTile( park, b, (26, 22), NegXArm | NegYArm );
			for ( var y = 20; y >= 14; y -= 2 )
				SaveTile( park, b, (26, y), PosYArm | NegYArm );
			SaveTile( park, b, (26, 12), PosXArm );
		} );
		var hostView = ViewOf( host );
		HostOnRide( host, guest, view, hostView, hostB );
		GuestTrackTool( host, guest, view, a );
		// beside B's track, not joined to it: (24, 12) lies next to (26, 12), whose arm points away; its refresh writes the same values, which changes nothing
		GuestTrackLine( host, guest, view, KartAt( guest, a, 4, 2 ) );
		Assert.IsTrue( GuestTrackBackspace( host, guest, view ).Taken, "laid and taken back with B claimed" );
		// over the tile B's arm reaches: refused while B is claimed (its counter would rise), laid while it is not
		GuestTrackLine( host, guest, view, KartAt( guest, a, 4, 2 ) );
		guest.Enter();
		Assert.IsTrue( guest.Level.ClickCellTool( KartAt( guest, a, 8, 2 ) ) is { Waiting: null, CommandRefused: true }, "refused in the claim's words" );
		HostOnRide( host, guest, view, hostView, hostB, leave: true );
		GuestTrackTool( host, guest, view, a );
		var crossed = (26, 12);
		var counter = host.Level.Guests!.Grid.Cells.Tracks.Get( crossed.Item1, crossed.Item2 ).Overlap;
		GuestTrackLine( host, guest, view, KartAt( guest, a, 8, 2 ) );
		Assert.AreEqual( counter + 1, host.Level.Guests.Grid.Cells.Tracks.Get( crossed.Item1, crossed.Item2 ).Overlap, "B unclaimed: the line crossed it" );
		// B claimed again: the line's take-back would count it down
		HostOnRide( host, guest, view, hostView, hostB );
		var tracks = TrackCellsOf( host );
		Assert.IsFalse( GuestTrackBackspace( host, guest, view ).Taken );
		Assert.AreEqual( TakeBackVerdict.InTheWay, TakeBacksOf( host, seat ).Last().Verdict );
		Assert.AreEqual( tracks, TrackCellsOf( host ), "nothing changed" );
		CatchUpAndCompare( host, guest );
	}
}
