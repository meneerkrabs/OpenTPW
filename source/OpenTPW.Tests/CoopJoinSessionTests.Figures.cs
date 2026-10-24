using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Chat;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-presence] A joined guest's game and the co-builders' figures (C2d-4; docs/COOP.md, "Co-builders as figures"), over real links to the loopback server: the game
/// makes the shared park's presence once it is in the park, shows the host's figure there and the host its own, makes it anew for the park a resync swaps in, and drops it with the
/// session. The asset-free park has no terrain to draw on, so the test makes each presence without its drawing (<see cref="GameFlow.CoopPresenceOverride"/>).
/// </summary>
public partial class CoopJoinSessionTests
{
	[TestMethod]
	public async Task AJoinedGuestsGameShowsTheHostsFigureAndMakesItsPresenceAnewForAResyncsPark()
	{
		await CoopLinkTests.WithServer( async ( server, players ) =>
		{
			var (hostPlayer, state, frames) = await RealHost( server, players );
			var room = ChatProtocol.CoopRoom( state.Id );
			// the host's game is the test's: its figure in the session's room
			host.Presence = new ParkPresence( hostPlayer.Session, room, "Hostess", new ParkPresence.PresenceGround( host.Level!.Guests!.Grid.Cells ), (5.5f, 1.5f), () => clock, sharedPark: true )
			{
				View = () => (5.5f, 1.5f),
				Running = () => host.Session!.EndedWith == null,
			};
			var joinPlayer = await players.Add( server, "Latecomer", firstFrame: true );
			await frames( 60, null, "the host's first frames" );
			var joiner = LobbyGuest( joinPlayer );
			var flow = joiner.Flow!;
			var made = new List<(Level Level, string Room)>();
			flow.CoopPresenceOverride = ( session, level, chatRoom, view, running ) =>
			{
				made.Add( (level, chatRoom) );
				return new ParkPresence( session, chatRoom, "Latecomer", new ParkPresence.PresenceGround( level.Guests!.Grid.Cells ), (2.5f, 1.5f), () => clock, sharedPark: true )
				{
					View = view,
					Running = running,
				};
			};
			await frames( 10, null, "the guest's game in the lobby" );
			Assert.IsNull( flow.Presence, "no figures in the lobby" );

			// the join: the game makes the presence for the host's park, in the session's room, and each game shows the other's figure
			Assert.IsTrue( flow.StartJoining( state.Code ) );
			await frames( 0, () => flow.CoopPresence is { InRoom: true } figures && figures.State.Others.Any( other => other.Name == "Hostess" )
				&& host.Presence.State.Others.Any( other => other.Name == "Latecomer" ), "each game to show the other's figure" );
			var session = flow.Level!.CoopSession!;
			joiner.Session = session;
			Assert.AreSame( flow.CoopPresence, flow.Presence );
			CollectionAssert.AreEqual( new[] { (flow.Level, room) }, made );
			Assert.IsTrue( flow.CoopPresence!.SharedPark );

			// a resync swaps the host's park in again: the old park's presence goes with it, and the new park gets its own
			var (oldPark, oldPresence) = (flow.Level, flow.CoopPresence);
			joiner.Enter();
			joiner.Economy.Ledger.Post( LedgerCategory.OtherIncome, 1 );
			await frames( 0, () => flow.Level != oldPark && flow.CoopPresence is { InRoom: true } figures && figures.State.Others.Any( other => other.Name == "Hostess" ),
				"the resync's park and its figures" );
			Assert.IsFalse( oldPresence.Walks, "the old park's presence was disposed" );
			Assert.AreSame( session, flow.Level!.CoopSession );
			Assert.AreEqual( 2, made.Count );
			Assert.AreSame( flow.Level, made[^1].Level );
			await frames( 30, null, "the guest follows" );
			Assert.AreEqual( 2, made.Count, "one presence for a park and its session" );

			// the host closes the park: the guest's figures go with the session
			var figuresBefore = flow.CoopPresence;
			host.Session!.End( "The host closed the park." );
			await frames( 0, () => session.EndedWith != null, "the guest hears the end" );
			await frames( 2, null, "the frames after the end" );
			Assert.IsNull( flow.CoopPresence );
			Assert.IsNull( flow.Presence );
			Assert.IsFalse( figuresBefore!.Walks );
			Assert.AreEqual( 2, made.Count );
			host.Presence.Dispose();
		} );
	}
}
