using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Chat;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-figures] The members of a shared park as figures over the loopback server (C2d-4; docs/COOP.md, "Co-builders as figures"): each member's first place in the
/// session's room is answered with where the others stand, the places that follow are relayed, and a member who leaves is gone from the others' parks.
/// </summary>
public partial class CoopLinkTests
{
	[TestMethod]
	public async Task TheMembersFiguresFollowTheirViewsInTheSharedParksRoom() => await WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var guest = await players.Add( server, "Guest" );
		await Open( host, guest );
		var shared = await Share( host, guest );
		var room = ChatProtocol.CoopRoom( shared.Id );
		var clock = 0.0;
		(float X, float Y)? hostView = (10.5f, 10.5f), guestView = (40.5f, 20.5f);
		ParkPresence Figure( Player player, (float X, float Y) start, Func<(float X, float Y)?> view ) =>
			new( player.Session, room, player.Name, new AnyGround(), start, () => clock, sharedPark: true ) { View = view, Running = () => player.Link.Session != null };
		using var hostFigure = Figure( host, (10.5f, 10.5f), () => hostView );
		using var guestFigure = Figure( guest, (40.5f, 20.5f), () => guestView );
		async Task Frames( Func<bool> until, string what )
		{
			var deadline = DateTime.UtcNow + OnlineIntegrationTests.Deadline;
			while ( !until() )
			{
				Assert.IsTrue( DateTime.UtcNow < deadline, $"Waited in vain for {what}: the host sees {Seen( hostFigure )}, the guest {Seen( guestFigure )}." );
				host.Session.Pump();
				guest.Session.Pump();
				clock += 0.05;
				hostFigure.Update( 0.05f );
				guestFigure.Update( 0.05f );
				await Task.Delay( 5 );
			}
		}
		static string Seen( ParkPresence presence ) => string.Join( ", ", presence.State.Others.Select( other => $"{other.Name} at ({other.X:0.0}, {other.Y:0.0})" ) );
		static bool Sees( ParkPresence presence, string name, float x, float y ) =>
			presence.State.Others.FirstOrDefault( other => other.Name == name ) is { } figure && MathF.Abs( figure.X - x ) < 0.25f && MathF.Abs( figure.Y - y ) < 0.25f;

		// both chats are in the session's room: each first place is answered with the other's, so each member sees the other where they stand
		await Frames( () => hostFigure.InRoom && guestFigure.InRoom && Sees( hostFigure, "Guest", 40.5f, 20.5f ) && Sees( guestFigure, "Hostess", 10.5f, 10.5f ), "each member to see the other" );
		Assert.AreEqual( 1, hostFigure.State.Others.Count );
		// the shared room is the connection's only while its own version is read and no join is on its way or waiting (a place now could reach another room)
		Assert.IsTrue( host.Session.SharedRoomReady );
		foreach ( var (flag, notReady) in new[] { ("joinPending", true), ("coopJoinWaits", true), ("protocolCurrent", false) } )
		{
			var field = typeof( OnlineSession ).GetField( flag, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!;
			var was = field.GetValue( host.Session );
			field.SetValue( host.Session, notReady );
			Assert.IsFalse( host.Session.SharedRoomReady, flag );
			Assert.IsFalse( hostFigure.InRoom, flag );
			field.SetValue( host.Session, was );
		}
		Assert.IsTrue( host.Session.SharedRoomReady );
		// the guest's view moves on: its figure walks after it, and the host sees it walk
		guestView = (44.5f, 20.5f);
		await Frames( () => Sees( hostFigure, "Guest", 44.5f, 20.5f ), "the host to see the guest walk" );
		// the host's view jumps across the park: the guest sees the host's figure there at once
		hostView = (90.5f, 90.5f);
		await Frames( () => Sees( guestFigure, "Hostess", 90.5f, 90.5f ), "the guest to see the host's jump" );
		// the guest leaves the session: its chat goes back to the lobby, and its figure leaves the host's park
		Assert.IsTrue( guest.Link.Leave( "Off home." ) );
		await Frames( () => hostFigure.State.Others.Count == 0 && !guestFigure.InRoom, "the guest's figure to go" );
		Assert.AreEqual( 0, guestFigure.State.Others.Count );
	} );
}
