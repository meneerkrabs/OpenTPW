using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Chat;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-figures] A host's game and the co-builders' figures (C2d-4; docs/COOP.md, "Co-builders as figures"): the shared park's presence is made once the park is
/// hosted and its chat follows the session's room, kept frame after frame, never put in a visit's place, and dropped with the session. The asset-free park has no terrain to
/// draw on, so the test makes the presence without its drawing (<see cref="GameFlow.CoopPresenceOverride"/>).
/// </summary>
public partial class CoopShareTests
{
	private static void SetFlowField( GameFlow flow, string name, object? value ) =>
		typeof( GameFlow ).GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( flow, value );

	[TestMethod]
	public async Task AHostedParkHasItsFiguresWhileItsSessionRuns() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var (other, _) = NewPark();
		var (level, park) = NewPark();
		var share = Share( level, host );
		// the host's game: its park and its online session; its HUD comes later
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
		SetFlowField( flow, "onlineSession", host.Session );
		SetFlowField( flow, "<Level>k__BackingField", level );
		var made = new List<(Level Level, string Room)>();
		Func<bool>? running = null;
		flow.CoopPresenceOverride = ( session, shared, room, view, isRunning ) =>
		{
			made.Add( (shared, room) );
			running = isRunning;
			return new ParkPresence( session, room, "Hostess", new ParkPresence.PresenceGround( shared.Guests!.Grid.Cells ), (2.5f, 1.5f), () => clock, sharedPark: true )
			{
				View = view,
				Running = isRunning,
			};
		};
		flow.StepCoopPresence();
		Assert.IsNull( flow.Presence, "an own park that is not shared has no figures" );
		await Frames( share, () => share.Stage == CoopShareStage.Hosting && host.Session.SharedParkRoom != null, "the park to be hosted", new[] { host }, park: park );
		var room = ChatProtocol.CoopRoom( share.State!.Id );
		Assert.AreEqual( room, host.Session.SharedParkRoom );

		// the figures' names are the HUD's to draw: none before the park has its HUD
		flow.StepCoopPresence();
		Assert.IsNull( flow.Presence );
		SetFlowField( flow, "<Hud>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( Hud.ParkHud ) ) );

		// a visit's presence is the visit's own (the visit smoke's scripted players stand in one): the step never takes its place
		var visit = new ParkPresence( host.Session, ChatProtocol.ParkRoom( "park1" ), "Hostess", new ParkPresence.PresenceGround( level.Guests!.Grid.Cells ), (1.5f, 1.5f), () => clock );
		flow.AttachPresence( visit );
		flow.StepCoopPresence();
		Assert.AreSame( visit, flow.Presence );
		Assert.AreEqual( 0, made.Count );
		visit.Dispose();
		SetFlowField( flow, "presence", null );

		// the hosted park gets its presence in the session's room, once
		flow.StepCoopPresence();
		var presence = flow.CoopPresence;
		Assert.IsNotNull( presence );
		Assert.AreSame( presence, flow.Presence );
		Assert.IsTrue( presence.SharedPark );
		CollectionAssert.AreEqual( new[] { (level, room) }, made );
		await Frames( share, () =>
		{
			flow.StepCoopPresence();
			flow.Presence?.Update( 0.05f );
			return presence.InRoom;
		}, "the host's figure in the session's room", new[] { host }, park: park );
		Assert.AreSame( presence, flow.Presence );
		Assert.AreEqual( 1, made.Count, "one presence for a park and its session" );
		Assert.IsTrue( running!(), "its session runs" );

		// another park in the same session (the game's park changed without a teardown): a presence of its own
		other.CoopSession = level.CoopSession;
		SetFlowField( flow, "<Level>k__BackingField", other );
		flow.StepCoopPresence();
		Assert.AreEqual( (other, room), made[^1] );
		Assert.IsFalse( presence.Walks, "the other park's presence was disposed" );
		other.CoopSession = null;
		SetFlowField( flow, "<Level>k__BackingField", level );
		flow.StepCoopPresence();
		Assert.AreEqual( (level, room), made[^1] );
		presence = flow.CoopPresence!;

		// a teardown disposes it as it disposes every presence: the next step makes it anew
		presence.Dispose();
		SetFlowField( flow, "presence", null );
		flow.StepCoopPresence();
		Assert.IsNotNull( flow.CoopPresence );
		Assert.AreNotSame( presence, flow.CoopPresence );
		Assert.AreEqual( 4, made.Count );

		// the host ends the session: the presence knows at once (a session can end during the level's update), and the figures go with it
		var last = flow.CoopPresence!;
		share.End();
		Assert.IsFalse( running!(), "the session ended" );
		flow.StepCoopPresence();
		Assert.IsNull( flow.Presence );
		Assert.IsNull( flow.CoopPresence );
		Assert.IsFalse( last.Walks, "disposed" );
		flow.StepCoopPresence();
		Assert.AreEqual( 4, made.Count );
	} );
}
