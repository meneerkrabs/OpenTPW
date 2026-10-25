using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Coop;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] Building by every player (docs/COOP.md, "Building by every player"; C3a) over the in-memory server: a guest's requests through the host's stream, the host's
/// grants ([EXT:online-coop-permissions]) and the allowlist, refusals with and without a record, a request sent twice, a gap, the host's limits, a paused host, a grant that leaves with its
/// member, and four players building the same cells.
/// </summary>
public partial class CoopStreamTests
{
	private static readonly LayLineCommand GuestLine = new( CellToolMode.Path, -1, 5, 20, 12, 20 );

	/// <summary>A guest's requests to the host, following its stream's records.</summary>
	private static CoopGuestCommands Requests( Peer guest )
	{
		var requests = new CoopGuestCommands( guest.Link );
		guest.Guest!.RecordApplied += requests.RecordApplied;
		return requests;
	}

	/// <summary>Rounds of a frame until <paramref name="done"/> holds (at most <paramref name="limit"/>).</summary>
	private void Until( Func<bool> done, Random random, string what, int limit = 600 )
	{
		for ( var round = 0; round < limit && !done(); round++ )
			Round( 1 / 60.0, random );
		Assert.IsTrue( done(), what );
	}

	/// <summary>Every record the server took from the host, with its number and its command.</summary>
	private static (long Number, CoopStreamRecord Record, ParkCommand Command)[] Records( CoopTestNetwork network ) => network.Taken
		.Select( payload => CoopStreamFrame.Decode( payload ) )
		.SelectMany( frame => frame.Records.Select( ( record, index ) => (frame.First + index, record, ParkCommandCodec.Decode( record.Bytes.Span )) ) )
		.ToArray();

	/// <summary>The host's answers to one member's requests.</summary>
	private static CoopGuestCommandAnswer[] Answers( Peer host, uint member ) =>
		host.Link.Sent.Where( sent => sent.To == member ).Select( sent => sent.Message ).OfType<CoopGuestCommandAnswer>().ToArray();

	/// <summary>The host grants (or takes back) <paramref name="guest"/>'s building, and the grant reaches every peer.</summary>
	private void Grant( Peer host, Peer guest, Random random, bool granted = true )
	{
		Assert.IsTrue( host.Issue( new SetBuildGrantCommand( guest.Link.Slot, granted ) ).Accepted );
		Until( () => peers.Where( peer => !peer.Frozen ).All( peer => peer.Level.HasBuildGrant( guest.Link.Slot ) == granted ), random, "the grant reached every peer" );
	}

	[TestMethod]
	public void AGrantedGuestBuildsThroughTheHostsStreamOnEveryPeer()
	{
		var random = new Random( 301 );
		var (host, guests, network) = Session( 2 );
		var builder = guests[0];
		var requests = Requests( builder );
		Rounds( 20, random );
		Grant( host, builder, random );
		builder.Enter();
		var line = requests.Submit( GuestLine );
		var place = requests.Submit( builder.Level.BuildCommand( builder.One.Entry, 20, 12, 90 ) );
		Until( () => !line.IsOpen && !place.IsOpen, random, "both requests resolved" );
		Assert.AreEqual( (CoopGuestRequestState.Applied, CoopGuestRequestState.Applied), (line.State, place.State) );
		Assert.IsTrue( line.Result!.Accepted, line.Result.Reason );
		Assert.IsTrue( place.Result!.Accepted, place.Result.Reason );
		Assert.IsTrue( host.Level.Guests!.Grid.IsWalkable( 8, 20 ), "the guest's line is in the host's park" );
		Assert.IsNotNull( host.Level.Objects!.FindAt( 20, 12 ), "and its placement" );
		var records = Records( network ).Where( item => item.Record.Origin != null ).ToArray();
		CollectionAssert.AreEqual( new[] { new CoopRecordOrigin( builder.Link.Id, 1 ), new CoopRecordOrigin( builder.Link.Id, 2 ) },
			records.Select( item => item.Record.Origin!.Value ).ToArray(), "each record carries its request's origin" );
		Assert.IsTrue( records.All( item => item.Command.Player == builder.Link.Slot ), "the guest's commands run as its seat" );
		Assert.AreEqual( (records[0].Number, records[1].Number), (line.Record, place.Record) );
		Assert.AreEqual( 0, Answers( host, builder.Link.Id ).Length, "the stream answers a recorded request by itself" );
		CatchUpAndCompare( host, guests, random );
	}

	[TestMethod]
	public void AnUngrantedGuestsRequestIsARefusedRecordOnEveryPeerWhateverPlayerItClaims()
	{
		var random = new Random( 302 );
		var (host, guests, network) = Session( 1 );
		var guest = guests[0];
		var requests = Requests( guest );
		Rounds( 20, random );
		// a forged player changes nothing: the host takes the seat from the server's word of the sender
		var line = requests.Submit( GuestLine with { Player = ParkCommands.LocalPlayer } );
		var grant = requests.Submit( new SetBuildGrantCommand( guest.Link.Slot, true ) { Player = ParkCommands.LocalPlayer } );
		Until( () => !line.IsOpen && !grant.IsOpen, random, "both requests resolved" );
		Assert.AreEqual( CoopGuestRequestState.Applied, line.State, "a refusal by the rights is a record, refused on every peer alike" );
		Assert.IsFalse( line.Result!.Accepted );
		Assert.IsFalse( grant.Result!.Accepted, "a guest grants itself nothing" );
		Assert.IsFalse( host.Level.Guests!.Grid.IsWalkable( 8, 20 ) );
		Assert.IsFalse( host.Level.HasBuildGrant( guest.Link.Slot ) );
		var records = Records( network ).Where( item => item.Record.Origin != null ).ToArray();
		Assert.AreEqual( 2, records.Length );
		Assert.IsTrue( records.All( item => item.Record.Flags == CoopRecordFlags.None && item.Command.Player == guest.Link.Slot ) );
		CatchUpAndCompare( host, guests, random );
	}

	[TestMethod]
	public void AGuestsOwnClickNeverRunsOnItsParkEvenWhenGranted()
	{
		var random = new Random( 303 );
		var (host, guests, _) = Session( 1 );
		var guest = guests[0];
		Rounds( 20, random );
		Grant( host, guest, random );
		var own = guest.Issue( GuestLine );
		Assert.IsFalse( own.Accepted, "a guest's park takes the host's stream only" );
		Assert.IsFalse( guest.Level.Guests!.Grid.IsWalkable( 8, 20 ) );
		Rounds( 30, random );
		Assert.IsNull( guest.Guest!.Desync );
		CatchUpAndCompare( host, guests, random );
	}

	[TestMethod]
	public void ARequestSentAgainAfterALostSocketAppliesOnceAndTheHostNamesItsRecord()
	{
		var random = new Random( 304 );
		var (host, guests, network) = Session( 1 );
		var guest = guests[0];
		var requests = Requests( guest );
		Rounds( 20, random );
		Grant( host, guest, random );
		var line = requests.Submit( GuestLine );
		// the host takes the request and streams its record; the guest's socket drops before the frame reaches it
		host.Frame( 1 / 60.0 );
		host.Host!.PublishNow();
		Assert.AreEqual( 1, Records( network ).Count( item => item.Record.Origin != null ), "the host applied the request" );
		guest.Link.Drop();
		guest.Link.Resume();
		Until( () => !line.IsOpen, random, "the request resolved after the resume" );
		Assert.AreEqual( CoopGuestRequestState.Applied, line.State );
		Assert.IsTrue( line.Result!.Accepted );
		var answer = Answers( host, guest.Link.Id ).Single();
		Assert.AreEqual( (1u, line.Record, (CoopGuestRefusal?)null, 2u), (answer.Sequence, answer.Record, answer.Refusal, answer.Next), "the resend got its record's number" );
		Assert.AreEqual( 1, Records( network ).Count( item => item.Record.Origin != null ), "and applied once" );
		CatchUpAndCompare( host, guests, random );
	}

	[TestMethod]
	public void AGapIsAnsweredAndTheGuestSendsAgainFromTheNumberItIsTold()
	{
		var random = new Random( 305 );
		var (host, guests, network) = Session( 1 );
		var guest = guests[0];
		var requests = Requests( guest );
		Rounds( 20, random );
		Grant( host, guest, random );
		// the first request is lost on its way to the host (a socket that dropped after the link took it)
		var lost = false;
		host.Link.DropMessage = ( _, bytes ) => !lost && CoopStreamMessage.Decode( bytes ) is CoopGuestCommand { Sequence: 1 } && (lost = true);
		var first = requests.Submit( new LayLineCommand( CellToolMode.Path, -1, 5, 20, 8, 20 ) );
		var second = requests.Submit( new LayLineCommand( CellToolMode.Path, -1, 8, 20, 12, 20 ) );
		Until( () => !first.IsOpen && !second.IsOpen, random, "both requests resolved" );
		Assert.IsTrue( lost );
		Assert.IsTrue( first.Result!.Accepted && second.Result!.Accepted );
		Assert.IsTrue( first.Record < second.Record, "the host applied them in the guest's order" );
		Assert.IsTrue( Answers( host, guest.Link.Id ).Any( answer => answer is { Sequence: 2, Refusal: CoopGuestRefusal.Gap, Next: 1 } ) );
		Assert.AreEqual( 2, Records( network ).Count( item => item.Record.Origin != null ), "each applied once" );
		CatchUpAndCompare( host, guests, random );
	}

	[TestMethod]
	public void TheHostRefusesWhatItCannotTakeWithoutARecordAndKeepsItsAnswers()
	{
		var random = new Random( 306 );
		var (host, guests, network) = Session( 1 );
		var guest = guests[0];
		Rounds( 20, random );
		// the guest's requests as raw messages, numbered by the test
		void Raw( uint sequence, byte[] bytes ) => guest.Link.Send( host.Link.Id, new CoopGuestCommand( sequence, bytes ).Encode() );
		byte[] Fee() => ParkCommandCodec.Encode( new SetEntranceFeeCommand( 1 ) );
		Raw( 1, new byte[CoopHostStream.MaximumGuestCommandBytes + 1] );
		Raw( 2, new byte[] { 0xFF, 0xEE } );
		Raw( 3, ParkCommandCodec.Encode( new LayLineCommand( CellToolMode.Path, -1, -1, 20, 12, 20 ) ) );
		for ( uint sequence = 4; sequence <= 48; sequence++ )
			Raw( sequence, Fee() );
		host.Frame( 1 / 60.0 );
		host.Host!.PublishNow();
		var answers = Answers( host, guest.Link.Id );
		Assert.AreEqual( CoopGuestRefusal.TooLarge, answers.Single( answer => answer.Sequence == 1 ).Refusal );
		Assert.AreEqual( CoopGuestRefusal.Malformed, answers.Single( answer => answer.Sequence == 2 ).Refusal, "bytes that do not decode" );
		Assert.AreEqual( CoopGuestRefusal.Malformed, answers.Single( answer => answer.Sequence == 3 ).Refusal, "a command out of range" );
		// the burst of forty: two spent on the malformed ones, thirty-eight on records, the rest too fast
		var records = Records( network ).Where( item => item.Record.Origin is { Member: var member } && member == guest.Link.Id ).ToArray();
		CollectionAssert.AreEqual( Enumerable.Range( 4, 38 ).Select( sequence => (uint)sequence ).ToArray(), records.Select( item => item.Record.Origin!.Value.Sequence ).ToArray() );
		Assert.IsTrue( records.All( item => item.Record.Flags == CoopRecordFlags.None ), "a guest may not set the fee: refused by the rights, on every peer" );
		CollectionAssert.AreEqual( Enumerable.Range( 42, 7 ).Select( sequence => (uint)sequence ).ToArray(),
			answers.Where( answer => answer.Refusal == CoopGuestRefusal.TooFast ).Select( answer => answer.Sequence ).ToArray() );

		// a request answered already gets its answer again, never a second record
		Raw( 2, new byte[] { 0xFF, 0xEE } );
		Raw( 10, Fee() );
		host.Frame( 1 / 60.0 );
		host.Host.PublishNow();
		answers = Answers( host, guest.Link.Id );
		Assert.AreEqual( 2, answers.Count( answer => answer is { Sequence: 2, Refusal: CoopGuestRefusal.Malformed } ) );
		Assert.AreEqual( records[6].Number, answers.Single( answer => answer.Sequence == 10 ).Record );
		Assert.AreEqual( 38, Records( network ).Count( item => item.Record.Origin != null ) );

		// a flood past the queue between two frames: the excess is dropped unanswered, and the next request shows the gap
		for ( uint sequence = 49; sequence <= 49 + CoopHostStream.MaximumQueuedGuestCommands + 5; sequence++ )
			Raw( sequence, Fee() );
		host.Frame( 1 / 60.0 );
		var next = 49 + (uint)CoopHostStream.MaximumQueuedGuestCommands;
		answers = Answers( host, guest.Link.Id );
		Assert.IsFalse( answers.Any( answer => answer.Sequence >= next ), "the excess was dropped unanswered" );
		Raw( next + 6, Fee() );
		host.Frame( 1 / 60.0 );
		Assert.AreEqual( next, Answers( host, guest.Link.Id ).Last().Next, "the gap names the first dropped request" );

		// past the answers it keeps, an old request is forgotten
		var sent = next;
		while ( sent < next + CoopHostStream.GuestAnswersKept )
		{
			for ( var index = 0; index < 50; index++ )
				Raw( sent++, Fee() );
			Rounds( 1, random );
		}
		Raw( 1, new byte[1] );
		Raw( sent - 1, Fee() );
		Rounds( 1, random );
		answers = Answers( host, guest.Link.Id );
		Assert.AreEqual( CoopGuestRefusal.Forgotten, answers.Last( answer => answer.Sequence == 1 ).Refusal );
		Assert.IsNull( host.Host.Failure, "no request stops the host" );
		CatchUpAndCompare( host, guests, random );
	}

	[TestMethod]
	public void APausedHostTakesAGuestsRequestBetweenTwoTicks()
	{
		var random = new Random( 307 );
		var (host, guests, _) = Session( 1 );
		var guest = guests[0];
		var requests = Requests( guest );
		Rounds( 20, random );
		Grant( host, guest, random );
		host.Holds = true;
		Rounds( 30, random );
		var tick = host.Level.TickCount;
		var line = requests.Submit( GuestLine );
		Until( () => !line.IsOpen, random, "the request resolved while the host was paused" );
		Assert.IsTrue( line.Result!.Accepted );
		Assert.AreEqual( tick, host.Level.TickCount, "the paused park took it between two ticks" );
		Assert.IsTrue( guest.Level.Guests!.Grid.IsWalkable( 8, 20 ), "and the guest, at the same tick" );
		CatchUpAndCompare( host, guests, random );
	}

	[TestMethod]
	public void AGrantLeavesWithItsMemberAndTheSeatsNextMemberStartsWithoutOne()
	{
		var random = new Random( 308 );
		var (host, guests, network) = Session( 1 );
		var guest = guests[0];
		Rounds( 20, random );
		Grant( host, guest, random );
		var seat = guest.Link.Slot;
		// the guest leaves, and a newcomer takes its seat at once, its first request already on its way in the same frame
		network.Remove( guest.Link );
		guest.Frozen = true;
		var newcomer = network.Add();
		Assert.AreEqual( seat, newcomer.Slot, "the freed seat is the newcomer's" );
		newcomer.Send( host.Link.Id, new CoopGuestCommand( 1, ParkCommandCodec.Encode( GuestLine ) ).Encode() );
		Rounds( 10, random );
		host.Host!.PublishNow();
		Assert.IsFalse( host.Level.HasBuildGrant( seat ), "the grant left with its member" );
		Assert.IsFalse( host.Level.Guests!.Grid.IsWalkable( 8, 20 ), "the newcomer's request was refused" );
		var records = Records( network );
		var revoke = records.Single( item => item.Command is SetBuildGrantCommand { Granted: false } );
		var request = records.Single( item => item.Record.Origin is { Member: var member } && member == newcomer.Id );
		Assert.IsTrue( revoke.Number < request.Number, "the grant was taken back before the newcomer's request applied" );
		Assert.AreEqual( CoopRecordFlags.None, request.Record.Flags );
	}

	[TestMethod]
	public void FourPlayersBuildingTheSameCellsAgreeOnWhoGotThem()
	{
		var random = new Random( 309 );
		var (host, guests, _) = Session( 3 );
		var requests = guests.Select( Requests ).ToArray();
		Rounds( 20, random );
		foreach ( var guest in guests )
			Grant( host, guest, random );
		// the host's placement and the guests' requests for the same cells, in one frame
		var placements = guests.Select( ( guest, index ) =>
		{
			guest.Enter();
			return index < 2 ? requests[index].Submit( guest.Level.BuildCommand( guest.One.Entry, 20, 12, 90 ) ) : requests[index].Submit( new LayLineCommand( CellToolMode.Path, -1, 16, 12, 24, 12 ) );
		} ).ToArray();
		host.Enter();
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( host.One.Entry, 20, 12, 90 ) ).Accepted, "the host's own click comes first" );
		Until( () => placements.All( request => !request.IsOpen ), random, "every request resolved" );
		Assert.IsFalse( placements[0].Result!.Accepted || placements[1].Result!.Accepted, "the host's placement holds the cell" );
		Assert.IsTrue( placements.All( request => request.State == CoopGuestRequestState.Applied ) );
		Assert.IsTrue( placements[0].Record < placements[1].Record && placements[1].Record < placements[2].Record, "each in the order the host took it" );
		Rounds( 10, random );
		CatchUpAndCompare( host, guests, random );
	}

	[TestMethod]
	public void AGrantEndsWithItsSessionAndTheSessionsRightsReplaceTheParksOwn()
	{
		var random = new Random( 310 );
		var (host, guests, _) = Session( 1 );
		var guest = guests[0];
		Rounds( 20, random );
		Grant( host, guest, random );
		var seat = guest.Link.Slot;
		// the session ends: both parks run on without its grants
		host.Enter();
		host.Host!.Dispose();
		host.Host = null;
		guest.Enter();
		guest.Guest!.Dispose();
		guest.Guest = null;
		Assert.IsFalse( host.Level.HasBuildGrant( seat ) || guest.Level.HasBuildGrant( seat ) );
		// the park shared again: a grant left on it, and rights of its own, give way to the new session's
		host.Enter();
		host.Level.SetBuildGrant( seat, true );
		var own = new AllowingAll();
		host.Level.Commands.Rights = own;
		host.Host = new CoopHostStream( host.Level, host.Link, () => clock, HostTime );
		Assert.IsFalse( host.Level.HasBuildGrant( seat ), "a new session starts without grants" );
		Assert.IsInstanceOfType( host.Level.Commands.Rights, typeof( CoopBuildRights ) );
		host.Host.Dispose();
		host.Host = null;
		Assert.AreSame( own, host.Level.Commands.Rights, "the park's own rights come back after the session" );
	}

	/// <summary>Rights that allow every command (a park's own policy before a session).</summary>
	private sealed class AllowingAll : IParkCommandRights
	{
		public bool Allows( int player, ParkCommandKind kind ) => true;
	}

	[TestMethod]
	public void AGrantedGuestMayBuildWhatTheAllowlistNamesAndNothingElse()
	{
		var peer = NewPeer( new CoopTestNetwork( () => clock ).Add( host: true ) );
		peer.Enter();
		var level = peer.Level;
		var rights = new CoopBuildRights( level );
		var place = level.BuildCommand( peer.One.Entry, 20, 12, 90 );
		var building = new ParkCommand[]
		{
			GuestLine,
			new QueueSegmentCommand( peer.One.Runtime.AttractionId, 3, 3, 3, 6 ),
			new EditQueueCommand( 3, 3 ),
			place,
			new RemoveAtCommand( 20, 12 ),
			new RemoveObjectAtCommand( 20, 12 ),
			new RemovePathCellCommand( 4, 1 ),
			new RemoveQueueCellCommand( 3, 3 ),
			new BuyLandCommand( 1, 1, 2, 2 )
		};
		var hostOnly = new ParkCommand[]
		{
			place with { Moving = true },
			place with { AddOnRide = peer.One.Runtime.AttractionId },
			new SetGameSpeedCommand( GameSpeed.Fast ),
			new SetEntranceFeeCommand( 3 ),
			new SetBuildGrantCommand( 1, true ),
			new SetSessionClockCommand( 5 ),
			new UndoLineCommand( CellToolMode.Path, -1, 5, 20, new[] { (5, 20) }, Array.Empty<(int, int)>() ),
			new PlacePrototypeCommand( 1, 2, 3 )
		};
		foreach ( var command in building.Concat( hostOnly ) )
		{
			Assert.IsTrue( rights.Allows( ParkCommands.LocalPlayer, command ), $"the host may {command.Kind}" );
			Assert.IsFalse( rights.Allows( 1, command ), $"no grant, no {command.Kind}" );
		}
		level.SetBuildGrant( 1, true );
		foreach ( var command in building )
			Assert.IsTrue( rights.Allows( 1, command ), $"a granted guest may {command}" );
		foreach ( var command in hostOnly )
			Assert.IsFalse( rights.Allows( 1, command ), $"a granted guest may not {command}" );
		Assert.IsFalse( rights.Allows( 2, GuestLine ), "a grant is its seat's" );
		Assert.IsFalse( rights.Allows( 1, ParkCommandKind.LayLine ), "a kind alone allows the host only" );
		Assert.IsTrue( rights.Allows( ParkCommands.LocalPlayer, ParkCommandKind.LayLine ) );

		// a track ride's station needs a claim: a granted guest may not remove it
		Assert.IsTrue( level.Commands.Apply( place ).Accepted );
		var station = level.Objects!.FindAt( 20, 12 )!;
		// the fixture's park is built without its field initializers: it gets its station table here
		var field = typeof( Level ).GetField( "trackStations", BindingFlags.Instance | BindingFlags.NonPublic )!;
		if ( field.GetValue( level ) is not Dictionary<OriginalObject, TrackStation> stations )
			field.SetValue( level, stations = new Dictionary<OriginalObject, TrackStation>() );
		stations[station] = new TrackStation( 0, 0, (20, 12), (21, 12) );
		try
		{
			Assert.IsFalse( rights.Allows( 1, new RemoveAtCommand( 20, 12 ) ) );
			Assert.IsFalse( rights.Allows( 1, new RemoveObjectAtCommand( 20, 12 ) ) );
		}
		finally
		{
			stations.Remove( station );
		}
		Assert.IsTrue( rights.Allows( 1, new RemoveAtCommand( 20, 12 ) ), "an ordinary object is a granted guest's to remove" );
		level.SetBuildGrant( 1, false );
		Assert.IsFalse( rights.Allows( 1, GuestLine ), "a grant taken back" );
	}

	[TestMethod]
	public void TheGrantsAreSessionStateThatEveryPeerHashesAndAJoinCarries()
	{
		var peer = NewPeer( new CoopTestNetwork( () => clock ).Add( host: true ) );
		peer.Enter();
		var level = peer.Level;
		var before = CoopDigest.Compute( level );
		var world = level.ComputeStateHash();
		Assert.IsTrue( level.Commands.Apply( new SetBuildGrantCommand( 2, true ) ).Accepted );
		Assert.AreNotEqual( before, CoopDigest.Compute( level ), "a grant is in the session digest" );
		Assert.AreEqual( world, level.ComputeStateHash(), "and in no world hash" );
		CollectionAssert.AreEqual( new[] { 2 }, level.CaptureJoinState().BuildGrants!.ToArray() );
		var twin = NewPeer( new CoopTestNetwork( () => clock ).Add( host: true ) );
		twin.Enter();
		twin.Level.ApplyJoinState( CoopJoinState.Decode( level.CaptureJoinState().Encode() ) );
		Assert.IsTrue( twin.Level.HasBuildGrant( 2 ), "a join snapshot carries the grants" );
		peer.Enter();
		Assert.IsTrue( level.Commands.Apply( new SetBuildGrantCommand( 2, false ) ).Accepted );
		Assert.AreEqual( before, CoopDigest.Compute( level ), "a session without grants keeps the digests it had" );
		Assert.IsFalse( new SetBuildGrantCommand( 0, true ).IsWellFormed, "the host's seat needs none" );
		Assert.IsFalse( new SetBuildGrantCommand( CoopJoinState.MaximumPlayerEntries, true ).IsWellFormed );
		Assert.AreEqual( new SetBuildGrantCommand( 3, true ), ParkCommandCodec.Decode( ParkCommandCodec.Encode( new SetBuildGrantCommand( 3, true ) ) ) );
	}
}
