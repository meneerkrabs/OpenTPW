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

	/// <summary>
	/// The guest places its carry back at (20, 12) with the modifiers <paramref name="modifiers"/>; the host applies the placement and the guest never gets the frame that holds its record, so
	/// the placement still waits when the resync replaces the park, and the cut holds it. The HUD's frame on the swapped-in park, after its first.
	/// </summary>
	private Action ResyncedWithAPlacementInsideTheCut( Peer guest, ObjectCatalogEntry entry, int modifiers )
	{
		var seat = guest.Link.Slot;
		guest.Link.DropFrame = _ => true;
		guest.Enter();
		guest.Level!.ClickBuildTool( 20, 12, modifiers );
		var placement = guest.Level.WaitingPlacement!;
		Assert.AreEqual( modifiers != 0, placement.KeepTool );
		RoundsUntil( () => host.Level!.Objects.FindAt( 20, 12 ) != null, "the host applied the placement" );
		Assert.IsTrue( host.Level!.CarriesEntry( seat ), "a Move's placement keeps the carry until the tool's end" );
		StartResync( guest );
		guest.Link.DropFrame = null;
		var old = guest.Level;
		RoundsUntil( () => guest.Level != old && !placement.Request.IsOpen, "the park was swapped in" );
		Assert.AreEqual( CoopGuestRequestState.InSnapshot, placement.Request.State, "the placement is inside the cut, its result here not known" );
		Assert.IsTrue( guest.Level!.CarriesEntry( seat ), "the new park holds the seat's carry" );
		var (_, frame) = HudOf( guest, NewParksEntry( guest, entry ) );
		frame();
		return frame;
	}

	/// <summary>The swapped-in park's own catalogue entry of <paramref name="entry"/>'s type, which its HUD's buy menu offers.</summary>
	private static ObjectCatalogEntry NewParksEntry( Peer guest, ObjectCatalogEntry entry ) => guest.Level!.ResolveCommandEntry( ParkCommandCodec.EntryKey( entry ) )!;

	/// <summary>Rounds of the HUD's <paramref name="frame"/> until neither park carries for the guest's seat.</summary>
	private void UntilTheCarryEndsWith( Peer guest, Action frame, string what ) => RoundsUntil( () =>
	{
		frame();
		return !guest.Session!.GuestCommands!.Open.Any() && !guest.Level!.CarriesEntry( guest.Link.Slot ) && !host.Level!.CarriesEntry( guest.Link.Slot );
	}, what );

	[TestMethod]
	public void AResyncArmsNoMoveToolThatItsPlacementInsideTheCutMayHaveEnded()
	{
		var guest = MoveHosted();
		var seat = guest.Link.Slot;
		var (entry, _) = ArmedMove( guest );
		// a placement without Ctrl ends the tool once accepted, and the new park cannot tell: the tool is not armed again, so the next click buys no second copy
		var frame = ResyncedWithAPlacementInsideTheCut( guest, entry, modifiers: 0 );
		Assert.IsFalse( guest.Level!.IsMoving, "the placement inside the cut may have ended the tool" );
		Assert.IsNull( guest.Level.MoveBeforeResync );
		UntilTheCarryEndsWith( guest, frame, "the carry its tool left was ended" );
		Assert.AreEqual( entry.InfoId, host.Level!.Objects.FindAt( 20, 12 )!.Entry.InfoId, "the object stands where it was placed" );
		Assert.AreEqual( 1, host.Level.Commands.Log.Count( item => item.Command is PlaceObjectCommand { Moving: true } && item.Command.Player == seat ), "placed once" );
		CatchUpAndCompare( guest );
	}

	[TestMethod]
	public void AResyncArmsNoMoveToolWhoseCtrlPlacementInsideTheCutMayHaveOpenedAQueueTool()
	{
		var guest = MoveHosted();
		var (entry, _) = ArmedMove( guest );
		// Ctrl keeps the tool only for a placed object that opens no tool of its own: a ride with a queue may open its queue tool, which ends the Move whatever the click held
		Assert.IsTrue( entry.HasQueue );
		Assert.IsFalse( Level.PlacementKeepsBuildTool( entry ) );
		var frame = ResyncedWithAPlacementInsideTheCut( guest, entry, modifiers: 0xc );
		Assert.IsFalse( guest.Level!.IsMoving, "the placement inside the cut may have ended the tool" );
		UntilTheCarryEndsWith( guest, frame, "the carry its tool left was ended" );
		Assert.AreEqual( entry.InfoId, host.Level!.Objects.FindAt( 20, 12 )!.Entry.InfoId, "the object stands where it was placed" );
		CatchUpAndCompare( guest );
	}

	/// <summary>A synthetic catalogue entry of one cell with the given settings lines (no model or script is loaded).</summary>
	private static ObjectCatalogEntry SettingsEntry( string settings ) =>
		new( "jungle", ObjectCategory.Feature, "/synthetic", new ObjectSettings( new[] { new ObjectSettingsFile( settings, "synthetic.sam" ) } ), new ObjectShape( new[] { "*" } ), "/unused.MD2", null, null,
			Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), null!, null );

	[TestMethod]
	public void CtrlKeepsTheBuildToolAfterAPlacementOfATypeThatOpensNoToolOfItsOwn()
	{
		Assert.IsTrue( Level.PlacementKeepsBuildTool( SettingsEntry( "Info.Id 9201\n" ) ), "an ordinary object" );
		Assert.IsFalse( Level.PlacementKeepsBuildTool( SettingsEntry( "Info.Id 9202\nInfo.HasQueue 1\n" ) ), "a ride with a queue, whose queue tool may follow" );
		Assert.IsFalse( Level.PlacementKeepsBuildTool( SettingsEntry( $"Info.Id 9203\nBumper.WhichTrackType {CoasterDefinition.TrackType}\n" ) ), "a coaster, whose track follows" );
		Assert.IsFalse( Level.PlacementKeepsBuildTool( SettingsEntry( "Info.Id 9204\nBumper.WhichTrackType 1\nBumper.BumperType -4\n" ) ), "a kart ride, whose track follows" );
		Assert.IsFalse( Level.PlacementKeepsBuildTool( SettingsEntry( "Info.Id 9205\nBumper.WhichTrackType 2\nBumper.BumperType -5\n" ) ), "a water ride, whose track follows" );
		Assert.IsFalse( Level.PlacementKeepsBuildTool( SettingsEntry( "Info.Id 9206\nBumper.WhichTrackType 0\nBumper.BumperType -1\n" ) ), "the bumper cars, whose arena follows" );
	}

	/// <summary>The guest's Move tool armed, a resync started, and a placement of it clicked while the resync runs, which waits for the new park: the old park and the placement's request.</summary>
	private (Level Old, CoopGuestCommands.Request Placement) PlacementWaitingForTheResync( Peer guest )
	{
		StartResync( guest );
		guest.Enter();
		var old = guest.Level!;
		old.ClickBuildTool( 20, 12 );
		var placement = old.WaitingPlacement!.Request;
		Assert.AreEqual( CoopGuestRequestState.Waiting, placement.State, "it goes out after the resync" );
		return (old, placement);
	}

	[TestMethod]
	public void AResyncsMoveToolWaitsForItsPlacementAfterTheCutWhichEndsItAsEver()
	{
		var guest = MoveHosted();
		var seat = guest.Link.Slot;
		var (entry, rotation) = ArmedMove( guest );
		var (old, placement) = PlacementWaitingForTheResync( guest );
		guest.Link.DropFrame = null;
		RoundsUntil( () => guest.Level != old, "the park was swapped in" );
		var (_, frame) = HudOf( guest, NewParksEntry( guest, entry ) );
		frame();
		Assert.IsTrue( guest.Level!.IsMoving, "the tool arms again at its angle" );
		Assert.AreEqual( rotation, guest.Level.BuildRotation );
		Assert.AreSame( placement, guest.Level.WaitingPlacement?.Request, "and waits for its placement again" );
		// its outcome finishes the tool as the placement's would have without the resync: placed without Ctrl, the tool ends, and its end releases the carry
		RoundsUntil( () =>
		{
			frame();
			return !guest.Level.IsMoving;
		}, "the placement came back and ended the tool" );
		Assert.IsTrue( placement.Accepted );
		UntilTheCarryEndsWith( guest, frame, "the tool's end released the carry" );
		Assert.AreEqual( entry.InfoId, host.Level!.Objects.FindAt( 20, 12 )!.Entry.InfoId, "the object stands where it was placed" );
		Assert.AreEqual( 1, host.Level.Commands.Log.Count( item => item.Command is PlaceObjectCommand { Moving: true } && item.Command.Player == seat ), "placed once" );
		CatchUpAndCompare( guest );
	}

	[TestMethod]
	public void AResyncsMoveToolEndsWithItsPlacementThatCameBackBeforeTheNewHudsFirstFrame()
	{
		var guest = MoveHosted();
		var seat = guest.Link.Slot;
		var (entry, _) = ArmedMove( guest );
		var (old, placement) = PlacementWaitingForTheResync( guest );
		guest.Link.DropFrame = null;
		// the placement goes out after the cut and comes back before the new park's HUD runs: the tool arms and waits again before the outcomes are taken, so this one finishes it
		RoundsUntil( () => guest.Level != old && !placement.IsOpen, "the park was swapped in and the placement came back" );
		Assert.IsTrue( placement.Accepted );
		var (_, frame) = HudOf( guest, NewParksEntry( guest, entry ) );
		frame();
		Assert.IsFalse( guest.Level!.IsMoving, "placed without Ctrl, the tool ended" );
		Assert.IsNull( guest.Level.WaitingPlacement );
		UntilTheCarryEndsWith( guest, frame, "the tool's end released the carry" );
		Assert.AreEqual( 1, host.Level!.Commands.Log.Count( item => item.Command is PlaceObjectCommand { Moving: true } && item.Command.Player == seat ), "placed once" );
		CatchUpAndCompare( guest );
	}

	[TestMethod]
	public void AResyncsMoveToolStaysArmedWhenItsPlacementAfterTheCutIsRefused()
	{
		var guest = MoveHosted();
		var seat = guest.Link.Slot;
		var (entry, rotation) = ArmedMove( guest );
		var (old, placement) = PlacementWaitingForTheResync( guest );
		// the host builds on the cell first, so the guest's placement is refused there
		Assert.IsTrue( host.Issue( host.Level!.BuildCommand( host.Level.Objects.Objects.First().Entry, 20, 12, 90 ) ).Accepted );
		guest.Link.DropFrame = null;
		RoundsUntil( () => guest.Level != old, "the park was swapped in" );
		var (_, frame) = HudOf( guest, NewParksEntry( guest, entry ) );
		RoundsUntil( () =>
		{
			frame();
			return !placement.IsOpen && guest.Level!.WaitingPlacement == null;
		}, "the placement came back" );
		Assert.IsFalse( placement.Accepted, "the cell was taken" );
		// a refused placement leaves the tool armed, as without the resync: it takes clicks again, with its carry
		Assert.IsTrue( guest.Level!.IsMoving );
		Assert.AreEqual( rotation, guest.Level.BuildRotation );
		Assert.IsTrue( guest.Level.CarriesEntry( seat ) && host.Level.CarriesEntry( seat ) );
		frame();
		Assert.IsFalse( guest.Session!.GuestCommands!.Open.Any( request => request.Command is EndMoveCommand ), "its carry is no stray one" );
		guest.Level.BuildEntry = null;
		UntilTheCarryEndsWith( guest, frame, "the end released the carry" );
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
