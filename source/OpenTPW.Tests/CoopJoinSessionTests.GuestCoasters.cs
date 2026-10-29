using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Coop;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-claims] A guest's coaster tool across a capture (docs/COOP.md, "A guest's coaster tool (C3c-2)"): a seat that asked for the park has fenced its requests, so the host lets go of
/// the coaster claim of a seat it captures for that holds a coaster edit, at the next frame's start after the guests' commands, and the capture is taken once the claim's end released the edit.
/// </summary>
public partial class CoopJoinSessionTests
{
	[TestMethod]
	public void AResyncingGuestsHeldCoasterDragIsReleasedBeforeItsCapture()
	{
		prepareParks = level => CoopSessionTests.PlaceCoasterOn( level, fixture! );
		var guest = Hosted( 1 ).Single();
		var seat = guest.Link.Slot;
		Assert.IsTrue( host.Issue( new SetBuildGrantCommand( seat, true ) ).Accepted );
		RoundsUntil( () => guest.Level!.HasBuildGrant( seat ), "the grant reached the guest" );
		var coaster = guest.Level!.Objects.Objects.Single( item => item.Runtime.Coaster != null );
		var ride = coaster.Runtime.AttractionId;
		var (_, frame) = HudOf( guest, coaster.Entry );
		// rounds with the guest's HUD, which takes its outcomes and sends its coaster commands
		void Until( Func<bool> done, string what ) => RoundsUntil( () =>
		{
			frame();
			return done();
		}, what );
		guest.Enter();
		Assert.AreEqual( TrackToolEntry.Pending, guest.Level.BeginCoasterToolEntry( coaster ) );
		Until( () => guest.Level.CoasterPanel != null, "the panel opened" );
		var panel = guest.Level.CoasterPanel!;
		guest.Level.CoasterTool!.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit.Take( 2 ) )
		{
			guest.Enter();
			Assert.IsTrue( guest.Level.SendCoasterClick( 20 + dx, 20 + dy, false ) );
			Until( () => !panel.IsWaiting, "the click came back" );
		}
		guest.Enter();
		Assert.IsTrue( panel.Select( CoasterToolButton.Height ) );
		Assert.IsTrue( panel.Press( CoasterTrack.StationNodeCount, new N.Vector2( 500, 400 ), 24, 20 ) );
		Until( () => panel.IsDragging && host.Level!.CoasterEdits.Of( seat ) != null, "the drag runs on the host" );
		// the guest loses the stream and asks for the park: its requests are fenced, so its drag's end would never come
		StartResync( guest );
		guest.Link.DropFrame = null;
		var old = guest.Level;
		var stream = host.Session!.HostStream!;
		RoundsUntil( () => guest.Level != old, "the park was swapped in" );
		Assert.AreEqual( 1, stream.CapturesTaken );
		Assert.IsFalse( host.Link.Sent.Any( sent => sent.Message is CoopSnapshotFailed ), "the capture waited for no held drag" );
		Assert.AreEqual( 1, host.Level!.Commands.Log.Count( entry => entry.Command is ReleaseRideCommand release && release.Ride == ride && release.Player == ParkCommands.LocalPlayer ),
			"the host let go of the claim" );
		Assert.IsNull( host.Level.CoasterEdits.Of( seat ), "and the claim's end released the drag" );
		Assert.IsNull( host.Level.ClaimOf( ride ) );
		Assert.IsFalse( host.Level.ResolveCommandObject( ride )!.Runtime.Coaster!.BeingEdited, "the coaster left the tool" );
		guest.Enter();
		Assert.IsNull( guest.Level!.CoasterPanel, "the new park has no panel" );
		Assert.IsNull( guest.Level.ClaimOf( ride ), "nor the claim" );
		Assert.IsFalse( guest.Level.ResolveCommandObject( ride )!.Runtime.Coaster!.BeingEdited );
		CatchUpAndCompare( guest );
	}

	/// <summary>A joiner gets an upgraded coaster at its tier: the host's capture writes the coaster's catalogue tier as any ride's (issue #903).</summary>
	[TestMethod]
	public void AJoinerGetsAnUpgradedCoasterAtItsTier()
	{
		prepareParks = level =>
		{
			var station = CoopSessionTests.PlaceCoasterOn( level, fixture! );
			Assert.IsTrue( level.Park!.Economy.TryGetObject( level.GetEconomyInstance( station )!.Value, out var state ) );
			state.Level = 1;
			Assert.IsTrue( station.Runtime.TryApplyCatalogueTierDefaults( 1, out var reason ), reason );
		};
		var other = Hosted( 1 ).Single();
		var joiner = Joiner();
		RoundsUntil( () => joiner.Level != null, "the joiner swapped its capture in" );
		Assert.AreEqual( 1, host.Session!.HostStream!.CapturesTaken );
		var coaster = joiner.Level!.Objects.Objects.Single( item => item.Runtime.Coaster != null );
		Assert.AreEqual( 1, coaster.Runtime.CatalogueTier );
		CatchUpAndCompare( joiner, other );
	}
}
