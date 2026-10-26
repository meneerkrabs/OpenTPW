using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] A guest's requests across a resync (docs/COOP.md, "Building by every player"): the requests are the session's, so the swapped-in park's cut settles the one it holds
/// and the one made during the resync goes out after it.
/// </summary>
public partial class CoopJoinSessionTests
{
	[TestMethod]
	public void AGuestsRequestsAreSettledAgainstTheResyncsCut()
	{
		var guest = Hosted( 1 ).Single();
		Build( 120, 61 );
		var seat = guest.Link.Slot;
		host.Issue( new SetBuildGrantCommand( seat, true ) );
		RoundsUntil( () => guest.Level!.HasBuildGrant( seat ), "the grant reached the guest" );
		var requests = guest.Session!.GuestCommands!;
		var inside = new LayLineCommand( CellToolMode.Path, -1, 13, 21, 15, 21 );
		// the host applies the request, and the guest never gets the frame that holds its record: a resync follows
		guest.Link.DropFrame = _ => true;
		var first = requests.Submit( inside );
		RoundsUntil( () => host.Level!.Commands.Log.Any( entry => entry.Command is LayLineCommand { StartX: 13 } && entry.Command.Player == seat ), "the host applied the request" );
		guest.Link.Send( host.Link.Id, new CoopResendRequest( 1, 0 ).Encode() );
		RoundsUntil( () => guest.Session.Phase == CoopGuestPhase.Resyncing, "the guest lost the stream and asked for the park" );
		Assert.IsTrue( first.IsOpen, "its record never applied in the old park" );
		Assert.IsTrue( requests.Fenced, "requests wait while the park is resynced" );
		var during = requests.Submit( new LayLineCommand( CellToolMode.Path, -1, 13, 23, 15, 23 ) );
		Assert.AreEqual( CoopGuestRequestState.Waiting, during.State );
		guest.Link.DropFrame = null;
		var old = guest.Level;
		RoundsUntil( () => guest.Level != old && !first.IsOpen && !during.IsOpen, "the park was swapped in and both requests settled" );
		Assert.AreEqual( CoopGuestRequestState.InSnapshot, first.State, "its record is inside the cut" );
		Assert.IsTrue( first.Record > 0 && first.Record <= requests.Included );
		Assert.IsNull( first.Result, "its result in the new park is not known" );
		Assert.AreEqual( CoopGuestRequestState.Applied, during.State, "the request made during the resync went out after it" );
		Assert.AreEqual( 1, host.Level!.Commands.Log.Count( entry => entry.Command is LayLineCommand { StartX: 13, StartY: 21 } ), "the request sent again applied once" );
		Assert.IsFalse( requests.Fenced );
		Build( 60, 62 );
		CatchUpAndCompare( guest );
	}

	/// <summary>A granted guest of a hosted session and two rides of the host's at (20, 12) and (24, 12) on every park (C3b-2's Move).</summary>
	private Peer MoveHosted()
	{
		var guest = Hosted( 1 ).Single();
		Build( 60, 63 );
		var seat = guest.Link.Slot;
		var entry = host.Level!.Objects.Objects.First().Entry;
		Assert.IsTrue( host.Issue( new SetBuildGrantCommand( seat, true ) ).Accepted );
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( entry, 20, 12, 90 ) ).Accepted );
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( entry, 24, 12, 90 ) ).Accepted );
		RoundsUntil( () => guest.Level!.HasBuildGrant( seat ) && guest.Level.Objects.FindAt( 24, 12 ) != null, "the grant and the rides reached the guest" );
		return guest;
	}

	/// <summary>The guest stops getting the stream and asks for the park: a resync follows once frames flow again.</summary>
	private void StartResync( Peer guest )
	{
		guest.Link.DropFrame = _ => true;
		guest.Link.Send( host.Link.Id, new CoopResendRequest( 1, 0 ).Encode() );
		RoundsUntil( () => guest.Session!.Phase == CoopGuestPhase.Resyncing, "the guest lost the stream and asked for the park" );
	}

	/// <summary>Rounds in which the guest's game ends a carry its Move tool lacks (<see cref="Level.EndStrayCarry"/>, which its HUD runs each frame), until neither park carries for the seat.</summary>
	private void UntilTheCarryEnds( Peer guest, int seat ) => RoundsUntil( () =>
	{
		guest.Enter();
		guest.Level!.EndStrayCarry( clock );
		return !guest.Level.CarriesEntry( seat ) && !host.Level!.CarriesEntry( seat );
	}, "the guest ended its carry" );

	[TestMethod]
	public void AResyncsParkWithAGuestsCarryEndsIt()
	{
		var guest = MoveHosted();
		var seat = guest.Link.Slot;
		// the host applies the pick-up, and the guest never gets the frame that holds its record
		guest.Enter();
		Assert.IsFalse( guest.Level!.BeginMove( guest.Level.Objects.FindAt( 20, 12 )! ) );
		var pick = guest.Level.WaitingToolStart!;
		RoundsUntil( () => host.Level!.CarriesEntry( seat ), "the host applied the pick-up" );
		StartResync( guest );
		guest.Link.DropFrame = null;
		var old = guest.Level;
		RoundsUntil( () => guest.Level != old && !pick.IsOpen, "the park was swapped in" );
		Assert.AreEqual( CoopGuestRequestState.InSnapshot, pick.State, "the pick-up is inside the cut" );
		Assert.IsTrue( guest.Level!.CarriesEntry( seat ), "the new park holds the seat's carry" );
		Assert.IsFalse( guest.Level.IsMoving, "but no Move tool arms from it" );
		UntilTheCarryEnds( guest, seat );
		Assert.IsNull( host.Level!.Objects.FindAt( 20, 12 ), "the object stays sold" );
		CatchUpAndCompare( guest );
	}

	/// <summary>A buy menu of one item.</summary>
	private sealed class OneItem( BuildItem item ) : IBuildCatalog
	{
		public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => item.Category == category ? new[] { item } : Array.Empty<BuildItem>();
	}

	/// <summary>A HUD over the guest's park as it stands now (a resync's park gets a new one), whose buy menu offers <paramref name="entry"/>, and the HUD's frame.</summary>
	private static (ParkHud Hud, Action Frame) HudOf( Peer guest, ObjectCatalogEntry entry )
	{
		guest.Enter();
		var strings = OriginalUiTests.FakeStrings();
		var item = new BuildItem( "ride", entry.InfoId, (BuildCategory)entry.WhichUIType, -1, 0, null, Array.Empty<string>(), Entry: entry );
		var hud = new ParkHud( guest.Level!, strings, new EconomyParkStatus( () => guest.Economy ), new OneItem( item ), new HudHost() );
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		var context = new UiContext( strings, new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) )
		{ Canvas = new UiCanvas( 2048, 1536 ), ResolveTexture = _ => null };
		return (hud, () =>
		{
			guest.Enter();
			hud.Update( context, UiInput.Idle( new N.Vector2( -1 ) ), false, false, false );
		});
	}

	/// <summary>The guest picks up the ride at (20, 12) and arms its Move tool with what the pick-up carries, as its HUD does; the type and its angle.</summary>
	private (ObjectCatalogEntry Entry, int Rotation) ArmedMove( Peer guest )
	{
		guest.Enter();
		Assert.IsFalse( guest.Level!.BeginMove( guest.Level.Objects.FindAt( 20, 12 )! ) );
		var pick = guest.Level.WaitingToolStart!;
		RoundsUntil( () => !pick.IsOpen, "the pick-up came back" );
		guest.Enter();
		var (entry, rotation) = pick.Result!.As<(ObjectCatalogEntry Entry, int Rotation)>();
		guest.Level.EndWaiting( pick );
		Assert.IsTrue( guest.Level.CompleteMove( entry, rotation ) );
		return (entry, rotation);
	}

	[TestMethod]
	public void AResyncArmsAGuestsMoveToolAgainFromItsCarryAtItsFirstHudFrame()
	{
		var guest = MoveHosted();
		var seat = guest.Link.Slot;
		var (entry, rotation) = ArmedMove( guest );
		Assert.AreNotEqual( 0, rotation, "the ride stands turned, so the angle tells" );
		StartResync( guest );
		guest.Link.DropFrame = null;
		var old = guest.Level;
		RoundsUntil( () => guest.Level != old, "the park was swapped in" );
		guest.Enter();
		Assert.IsTrue( guest.Level!.CarriesEntry( seat ), "the new park holds the seat's carry" );
		Assert.IsFalse( guest.Level.IsMoving, "a new park starts with no tool" );
		// the new park's HUD arms it again at its first frame, with the buy item of its type, at the angle it had
		var (hud, frame) = HudOf( guest, entry );
		frame();
		Assert.IsTrue( guest.Level.IsMoving );
		Assert.AreEqual( entry.InfoId, guest.Level.BuildEntry!.InfoId );
		Assert.AreEqual( rotation, guest.Level.BuildRotation );
		Assert.AreEqual( entry.InfoId, ((BuildItem?)typeof( ParkHud ).GetField( "pendingItem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.GetValue( hud ))?.InfoId );
		Assert.IsNull( guest.Level.TakeMoveToRestore(), "once" );
		frame();
		Assert.IsFalse( guest.Session!.GuestCommands!.Open.Any( request => request.Command is EndMoveCommand ), "its carry is no stray one" );
		// its end releases the carry, as ever
		guest.Level.BuildEntry = null;
		RoundsUntil( () => !guest.Level.CarriesEntry( seat ) && !host.Level!.CarriesEntry( seat ), "the end released the carry" );
		Assert.IsNull( host.Level!.Objects.FindAt( 20, 12 ), "the object stays sold" );
		CatchUpAndCompare( guest );
	}

	[TestMethod]
	public void AResyncArmsNoMoveToolThatWasNotArmed()
	{
		var guest = MoveHosted();
		var seat = guest.Link.Slot;
		var (entry, _) = ArmedMove( guest );
		// the Move ends (its end on its way) and a new pick-up goes before the resync: neither arms the new park's tool
		guest.Enter();
		guest.Level!.BuildEntry = null;
		Assert.IsFalse( guest.Level.BeginMove( guest.Level.Objects.FindAt( 24, 12 )! ) );
		StartResync( guest );
		guest.Link.DropFrame = null;
		var old = guest.Level;
		RoundsUntil( () => guest.Level != old, "the park was swapped in" );
		var (_, frame) = HudOf( guest, entry );
		frame();
		Assert.IsFalse( guest.Level!.IsMoving, "no Move tool was armed when the park was replaced" );
		Assert.IsNull( guest.Level.MoveBeforeResync );
		RoundsUntil( () =>
		{
			frame();
			return !guest.Session!.GuestCommands!.Open.Any() && !guest.Level.CarriesEntry( seat ) && !host.Level!.CarriesEntry( seat );
		}, "the end went, and the second pick-up's carry, without its tool, was ended" );
		CatchUpAndCompare( guest );
	}

	[TestMethod]
	public void APickUpOpenAcrossAResyncTakesNoSecondAndItsCarryIsEnded()
	{
		var guest = MoveHosted();
		var seat = guest.Link.Slot;
		StartResync( guest );
		// a pick-up clicked while the resync runs waits for the new park and goes out after it
		guest.Enter();
		var old = guest.Level!;
		Assert.IsFalse( old.BeginMove( old.Objects.FindAt( 20, 12 )! ) );
		var pick = old.WaitingToolStart!;
		Assert.AreEqual( CoopGuestRequestState.Waiting, pick.State );
		guest.Link.DropFrame = null;
		RoundsUntil( () => guest.Level != old, "the park was swapped in" );
		// the new park's Move button waits for the pick-up still open in the session's requests
		guest.Enter();
		var level = guest.Level!;
		Assert.IsTrue( pick.IsOpen, "the pick-up is still on its way" );
		var clicks = level.WaitingClicks;
		Assert.IsFalse( level.BeginMove( level.Objects.FindAt( 24, 12 )! ) );
		Assert.AreEqual( clicks + 1, level.WaitingClicks, "a second pick-up waits for the first" );
		Assert.IsNull( level.WaitingToolStart );
		RoundsUntil( () => !pick.IsOpen, "the pick-up applied" );
		Assert.IsTrue( pick.Accepted );
		Assert.AreEqual( 1, host.Level!.Commands.Log.Count( entry => entry.Command is PickUpObjectCommand && entry.Command.Player == seat ), "one pick-up went" );
		Assert.IsNotNull( host.Level.Objects.FindAt( 24, 12 ), "the second ride stands" );
		UntilTheCarryEnds( guest, seat );
		CatchUpAndCompare( guest );
	}
	[TestMethod]
	public void AGuestsPoolStrokeAcrossAResyncLeavesItsRequestsToTheSessionAndTheNewParksToolHasNone()
	{
		var guest = Hosted( 1 ).Single();
		Build( 60, 64 );
		var seat = guest.Link.Slot;
		host.Issue( new SetBuildGrantCommand( seat, true ) );
		RoundsUntil( () => guest.Level!.HasBuildGrant( seat ), "the grant reached the guest" );
		guest.Enter();
		var old = guest.Level!;
		var tool = old.PoolTool!;
		Assert.IsTrue( tool.Begin( erase: false ) );
		// the host applies the press's batch, and the guest never gets the frame that holds its record: a resync follows
		guest.Link.DropFrame = _ => true;
		tool.Press( 20, 26 );
		tool.SendGuestBatches( clock );
		var requests = guest.Session!.GuestCommands!;
		var first = requests.LastSubmitted!;
		Assert.IsInstanceOfType( first.Command, typeof( PoolCellsCommand ) );
		RoundsUntil( () => host.Level!.Commands.Log.Any( entry => entry.Command is PoolCellsCommand && entry.Command.Player == seat ), "the host applied the batch" );
		guest.Link.Send( host.Link.Id, new CoopResendRequest( 1, 0 ).Encode() );
		RoundsUntil( () => guest.Session.Phase == CoopGuestPhase.Resyncing, "the guest lost the stream and asked for the park" );
		Assert.AreEqual( 1, old.OpenPoolRequests, "the batch is open in the session's requests" );
		guest.Link.DropFrame = null;
		RoundsUntil( () => guest.Level != old && !first.IsOpen, "the park was swapped in" );
		Assert.AreEqual( CoopGuestRequestState.InSnapshot, first.State, "the batch is inside the cut" );
		guest.Enter();
		var level = guest.Level!;
		Assert.AreNotSame( tool, level.PoolTool, "the new park has its own tool" );
		Assert.IsFalse( level.PoolTool!.HasGuestBatches, "with no stroke" );
		Assert.AreEqual( ParkCellTypes.PoolWater, level.Guests!.Grid.Cells.RawTypeAt( 20, 26 ), "the snapshot holds the painted cell" );
		Build( 30, 65 );
		CatchUpAndCompare( guest );
	}
}
