using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] A guest's Move tool (docs/COOP.md, "Building by every player"; C3b-2): the pick-up goes to the host and the tool arms once it comes back; the placements wait as
/// any placement does; the tool's end, which every guest seat may send, releases the seat's carry. A carry left without its tool (a pick-up whose wait was left, an end the host could not
/// take) is ended by the guest itself, one pick-up is open at a time, and the host releases what a departed seat carried before the seat's next member.
/// </summary>
public partial class CoopSessionTests
{
	/// <summary>The object window's Move button on <paramref name="item"/>, as <c>ParkHud.MoveObject</c> runs it (the asset-free HUD has no buy menu to find the item in).</summary>
	private static CoopGuestCommands.Request StartMove( View view, Park guest, OriginalObject item )
	{
		guest.Enter();
		Assert.IsFalse( guest.Level.BeginMove( item ), "a guest's pick-up goes to the host first" );
		var request = guest.Level.WaitingToolStart;
		Assert.IsNotNull( request );
		Assert.IsInstanceOfType( request!.Command, typeof( PickUpObjectCommand ) );
		var buy = new BuildItem( "ride", item.Entry.InfoId, BuildCategory.Rides, -1, 0, null, Array.Empty<string>(), Entry: item.Entry );
		typeof( ParkHud ).GetField( "waitingMove", NonPublic )!.SetValue( view.Hud, ((CoopGuestCommands.Request Request, BuildItem Item)?)(request, buy) );
		return request;
	}

	/// <summary>
	/// A buy menu of one item, the ride's (set once the park is built), so the object window's Move button finds it (<c>ParkHud.MoveObject</c>): a new copy each time it is asked, so each
	/// click's item is its own.
	/// </summary>
	private sealed class OneRide : IBuildCatalog
	{
		public BuildItem? Item { get; set; }
		public List<BuildItem> Given { get; } = new();
		public IReadOnlyList<BuildItem> GetItems( BuildCategory category )
		{
			if ( Item is not { } item || item.Category != category )
				return Array.Empty<BuildItem>();
			var copy = item with { };
			Given.Add( copy );
			return new[] { copy };
		}
	}

	/// <summary>The object window's Move button on <paramref name="item"/>.</summary>
	private static void PressMove( ParkHud hud, OriginalObject item )
	{
		typeof( ParkHud ).GetProperty( "selectedObject", NonPublic | System.Reflection.BindingFlags.Instance )!.SetValue( hud, item );
		typeof( ParkHud ).GetMethod( "MoveObject", NonPublic )!.Invoke( hud, null );
	}

	/// <summary>The guest's member of the last <see cref="MoveSession"/>.</summary>
	private CoopTestNetwork.Member? moveGuestLink;

	/// <summary>A ride of the host's at (20, 12), granted guest, both parks with it.</summary>
	private (Park Host, Park Guest, View View, int Seat) MoveSession( IBuildCatalog? catalog = null )
	{
		var (host, guest, _, guestLink) = Shared();
		moveGuestLink = guestLink;
		var view = ViewOf( guest, catalog );
		Rounds( 20, host, guest );
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( host.One.Entry, 20, 12, 90 ) ).Accepted );
		Grant( host, guest, view );
		RoundsUntil( () => guest.Level.Objects.FindAt( 20, 12 ) != null, host, guest, view, "the host's ride reached the guest" );
		return (host, guest, view, guest.Level.Commands.Player);
	}

	/// <summary>The guest picks up the ride at (20, 12) and its Move tool arms.</summary>
	private void PickUp( Park host, Park guest, View view )
	{
		StartMove( view, guest, guest.Level.Objects.FindAt( 20, 12 )! );
		RoundsUntil( () => guest.Level.IsMoving, host, guest, view, "the pick-up came back and armed the Move tool" );
	}

	[TestMethod]
	public void AGuestsMoveWaitsForItsPickUpThenPlacesTheObjectThroughTheHost()
	{
		var (host, guest, view, seat) = MoveSession();
		var money = new[] { new List<(ParkEventKind, long)>(), new List<(ParkEventKind, long)>() };
		host.Economy.EventRaised += raised => { if ( raised.Kind is ParkEventKind.ObjectSold or ParkEventKind.ObjectBuilt ) money[0].Add( (raised.Kind, raised.Amount) ); };
		guest.Economy.EventRaised += raised => { if ( raised.Kind is ParkEventKind.ObjectSold or ParkEventKind.ObjectBuilt ) money[1].Add( (raised.Kind, raised.Amount) ); };
		var item = guest.Level.Objects.FindAt( 20, 12 )!;
		StartMove( view, guest, item );
		Assert.IsFalse( guest.Level.IsMoving, "no tool before its outcome" );
		Assert.IsFalse( item.IsDeleted, "nothing is picked up before its record" );
		Assert.IsFalse( guest.Level.BeginMove( item ) );
		Assert.AreEqual( 1, guest.Level.WaitingClicks, "a second Move click waits" );
		RoundsUntil( () => guest.Level.IsMoving, host, guest, view, "the pick-up came back and armed the Move tool" );
		Assert.IsTrue( item.IsDeleted && host.Level.Objects.FindAt( 20, 12 ) == null, "picked up on both parks" );
		Assert.IsTrue( host.Level.CarriesEntry( seat ) && guest.Level.CarriesEntry( seat ), "the seat carries its type on both parks" );
		Assert.AreSame( guest.One.Entry, guest.Level.BuildEntry );
		Assert.IsNotNull( typeof( ParkHud ).GetField( "pendingItem", NonPublic )!.GetValue( view.Hud ), "the HUD's item is armed with it" );
		// placed with Ctrl the tool stays and places again; without it the tool ends, and its end releases the carry
		guest.Level.ClickBuildTool( 24, 12, modifierMask: 0xc );
		Assert.IsNotNull( guest.Level.WaitingPlacement );
		RoundsUntil( () => guest.Level.WaitingPlacement == null, host, guest, view, "the first placement came back" );
		Assert.IsNotNull( host.Level.Objects.FindAt( 24, 12 ) );
		Assert.IsTrue( guest.Level.IsMoving, "kept for exactly Ctrl" );
		guest.Level.ClickBuildTool( 26, 12 );
		RoundsUntil( () => guest.Level.WaitingPlacement == null, host, guest, view, "the second placement came back" );
		Assert.IsNotNull( host.Level.Objects.FindAt( 26, 12 ) );
		Assert.IsFalse( guest.Level.IsMoving, "the tool ends after a placement without Ctrl" );
		RoundsUntil( () => !guest.Level.CarriesEntry( seat ) && !host.Level.CarriesEntry( seat ), host, guest, view, "the end released the carry on both parks" );
		CollectionAssert.AreEqual( new[] { ParkCommandKind.PickUpObject, ParkCommandKind.PlaceObject, ParkCommandKind.PlaceObject, ParkCommandKind.EndMove },
			HostCommandsOf( host, seat ).Select( command => command.Kind ).ToArray() );
		// the money: each moving placement pays the type's price, the same on both parks (the asset-free park keeps no economy record of a placed object to sell at the pick-up)
		CollectionAssert.AreEqual( new[] { ParkEventKind.ObjectBuilt, ParkEventKind.ObjectBuilt }, money[0].Select( entry => entry.Item1 ).ToArray(), string.Join( ", ", money[0] ) );
		Assert.IsTrue( money[0].All( entry => entry.Item2 > 0 ) );
		CollectionAssert.AreEqual( money[0], money[1] );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void APickUpLeftByEscapeEndsItsCarryAndTheNextWaitsForItsRequest()
	{
		var (host, guest, view, seat) = MoveSession();
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( host.One.Entry, 24, 12, 90 ) ).Accepted );
		RoundsUntil( () => guest.Level.Objects.FindAt( 24, 12 ) != null, host, guest, view, "the second ride reached the guest" );
		var first = StartMove( view, guest, guest.Level.Objects.FindAt( 20, 12 )! );
		view.Escape();
		Assert.IsNull( guest.Level.WaitingToolStart, "Escape leaves the wait" );
		// the pick-up is still open in the session's requests: the next Move click waits for it, so no end of its carry can reach the host after a newer pick-up
		var clicks = guest.Level.WaitingClicks;
		Assert.IsFalse( guest.Level.BeginMove( guest.Level.Objects.FindAt( 24, 12 )! ) );
		Assert.AreEqual( clicks + 1, guest.Level.WaitingClicks );
		Assert.AreEqual( 1, guest.Session!.GuestCommands!.Open.Count( request => request.Command is PickUpObjectCommand ) );
		RoundsUntil( () => !first.IsOpen && !guest.Level.CarriesEntry( seat ) && !host.Level.CarriesEntry( seat ), host, guest, view, "the pick-up's carry was ended" );
		Assert.IsTrue( first.Accepted );
		Assert.IsFalse( guest.Level.IsMoving, "no Move tool arms from it" );
		Assert.IsNull( host.Level.Objects.FindAt( 20, 12 ), "the object stays sold, as a cancelled Move leaves it" );
		CollectionAssert.AreEqual( new[] { ParkCommandKind.PickUpObject, ParkCommandKind.EndMove }, HostCommandsOf( host, seat ).Select( command => command.Kind ).ToArray() );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheMoveButtonOfAnotherObjectWhileAPickUpWaitsKeepsTheFirstPickUpsItem()
	{
		var menu = new OneRide();
		var (host, guest, view, seat) = MoveSession( menu );
		var entry = guest.One.Entry;
		menu.Item = new BuildItem( "ride", entry.InfoId, (BuildCategory)entry.WhichUIType, -1, 0, null, Array.Empty<string>(), Entry: entry );
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( host.One.Entry, 24, 12, 90 ) ).Accepted );
		RoundsUntil( () => guest.Level.Objects.FindAt( 24, 12 ) != null, host, guest, view, "the second ride reached the guest" );
		var waitingMove = typeof( ParkHud ).GetField( "waitingMove", NonPublic )!;
		guest.Enter();
		PressMove( view.Hud, guest.Level.Objects.FindAt( 20, 12 )! );
		var first = guest.Level.WaitingToolStart;
		Assert.IsNotNull( first, "the window's Move button sent the pick-up" );
		var firstItem = menu.Given.Single();
		Assert.AreEqual( (first, firstItem), (((CoopGuestCommands.Request Request, BuildItem Item)?)waitingMove.GetValue( view.Hud ))!.Value );
		// the Move button of the other ride while the first pick-up waits sends nothing and binds nothing: the first click's item stays bound
		PressMove( view.Hud, guest.Level.Objects.FindAt( 24, 12 )! );
		Assert.AreEqual( 2, menu.Given.Count, "the second click found its own item" );
		Assert.AreSame( first, guest.Level.WaitingToolStart );
		var bound = (((CoopGuestCommands.Request Request, BuildItem Item)?)waitingMove.GetValue( view.Hud ))!.Value;
		Assert.AreSame( first, bound.Request );
		Assert.AreSame( firstItem, bound.Item, "the item stays the first click's" );
		Assert.AreEqual( 1, guest.Session!.GuestCommands!.Open.Count( request => request.Command is PickUpObjectCommand ) );
		RoundsUntil( () => guest.Level.IsMoving, host, guest, view, "the first pick-up armed the Move tool" );
		Assert.IsNull( host.Level.Objects.FindAt( 20, 12 ), "the first ride was picked up" );
		Assert.IsNotNull( host.Level.Objects.FindAt( 24, 12 ), "the second stands" );
		Assert.AreSame( firstItem, typeof( ParkHud ).GetField( "pendingItem", NonPublic )!.GetValue( view.Hud ), "the Move tool armed with the first click's item" );
		// its placement finishes the tool as the host's does, and the end releases the carry
		guest.Level.ClickBuildTool( 28, 12 );
		RoundsUntil( () => guest.Level.WaitingPlacement == null, host, guest, view, "the placement came back" );
		Assert.IsFalse( guest.Level.IsMoving );
		RoundsUntil( () => !guest.Level.CarriesEntry( seat ) && !host.Level.CarriesEntry( seat ), host, guest, view, "the end released the carry" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AMovesEndTheHostCouldNotTakeIsSentAgain()
	{
		var (host, guest, view, seat) = MoveSession();
		PickUp( host, guest, view );
		// forty requests between two of the host's frames spend its burst: the tool's end after them is too fast, and the host keeps no record of it
		var requests = guest.Session!.GuestCommands!;
		var burst = Enumerable.Range( 0, 40 ).Select( _ => requests.Submit( new LayLineCommand( CellToolMode.Path, -1, 30, 30, 30, 30 ) { Player = seat } ) ).ToArray();
		guest.Enter();
		view.Escape();
		Assert.IsFalse( guest.Level.IsMoving );
		var end = requests.LastSubmitted!;
		Assert.IsInstanceOfType( end.Command, typeof( EndMoveCommand ) );
		RoundsUntil( () => !end.IsOpen && burst.All( request => !request.IsOpen ), host, guest, view, "the end was answered" );
		Assert.AreEqual( (CoopGuestRequestState.Refused, (CoopGuestRefusal?)CoopGuestRefusal.TooFast), (end.State, end.Refusal) );
		Assert.IsTrue( guest.Level.CarriesEntry( seat ), "the carry stays for now" );
		RoundsUntil( () => !guest.Level.CarriesEntry( seat ) && !host.Level.CarriesEntry( seat ), host, guest, view, "the end was sent again and released the carry" );
		Assert.AreEqual( 1, HostCommandsOf( host, seat ).Count( command => command is EndMoveCommand ), "one end has a record" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGrantTakenBackWhileAGuestMovesRefusesItsPlacementAndItsEndStillGoes()
	{
		var (host, guest, view, seat) = MoveSession();
		PickUp( host, guest, view );
		// the placement is sent while the guest is granted; the host takes the grant back before its stream takes the request
		guest.Level.ClickBuildTool( 24, 12 );
		var placement = guest.Level.WaitingPlacement!.Request;
		Assert.IsTrue( host.Issue( new SetBuildGrantCommand( seat, false ) ).Accepted );
		RoundsUntil( () => !placement.IsOpen && guest.Level.WaitingPlacement == null, host, guest, view, "the placement came back" );
		Assert.IsFalse( placement.Accepted );
		Assert.IsTrue( placement.Result!.RightsRefused, "refused at its record by the rights" );
		Assert.IsNull( host.Level.Objects.FindAt( 24, 12 ) );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotGranted ) );
		Assert.IsTrue( guest.Level.IsMoving, "the Move tool stays armed" );
		// its end needs no grant
		view.Escape();
		RoundsUntil( () => !guest.Level.CarriesEntry( seat ) && !host.Level.CarriesEntry( seat ), host, guest, view, "the end released the carry" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ADepartedGuestsCarryIsReleasedByTheHost()
	{
		var (host, guest, view, seat) = MoveSession();
		PickUp( host, guest, view );
		Assert.IsTrue( host.Level.CarriesEntry( seat ) );
		guest.Enter();
		Assert.IsNotNull( typeof( ParkHud ).GetField( "pendingItem", NonPublic )!.GetValue( view.Hud ), "the HUD's item is armed for the Move" );
		guest.Session!.End( "The guest left." );
		// the server takes the member out of the session, and the host hears of it
		network!.Remove( moveGuestLink! );
		Assert.IsFalse( guest.Level.IsMoving, "the guest's Move tool disarms with its session" );
		Assert.IsNull( typeof( ParkHud ).GetField( "pendingItem", NonPublic )!.GetValue( view.Hud ), "and its HUD's item with it" );
		Assert.IsFalse( guest.Level.CarriesEntry( seat ), "and its park keeps no guest's carry" );
		for ( var round = 0; round < 600 && host.Level.CarriesEntry( seat ); round++ )
			Rounds( 1, host );
		Assert.IsFalse( host.Level.CarriesEntry( seat ), "the host released the departed seat's carry" );
		Assert.IsFalse( host.Level.HasBuildGrant( seat ), "and took its grant back" );
		Assert.IsTrue( host.Level.Commands.Log.Any( entry => entry.Command is ReleaseSeatCommand { Seat: var released } && released == seat ), "a record of the stream" );
	}

	[TestMethod]
	public void ASeatTakenAgainWhileTheHostWasAwayHasItsCarryAndGrantReleased()
	{
		var (host, guest, hostLink, guestLink) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( host.One.Entry, 20, 12, 90 ) ).Accepted );
		Grant( host, guest, view );
		RoundsUntil( () => guest.Level.Objects.FindAt( 20, 12 ) != null, host, guest, view, "the host's ride reached the guest" );
		var seat = guest.Level.Commands.Player;
		PickUp( host, guest, view );
		Assert.IsTrue( host.Level.CarriesEntry( seat ) && host.Level.HasBuildGrant( seat ) );
		// while the host's socket is away the guest leaves and another member takes its seat: the host's resume names the new member in that seat, with no event of either
		hostLink.Drop();
		network!.Remove( guestLink );
		var next = network.Add();
		Assert.AreEqual( seat, (int)next.Slot, "the freed seat is taken again" );
		hostLink.Resume();
		NextMembersFirstRequestComesAfterTheRelease( host, next, seat );
	}

	[TestMethod]
	public void ASeatTakenAgainBeforeTheHostsNextFrameHasItsCarryAndGrantReleased()
	{
		var (host, guest, view, seat) = MoveSession();
		PickUp( host, guest, view );
		// the guest leaves and another member takes its seat between two of the host's frames
		guest.Enter();
		guest.Session!.End( "The guest left." );
		network!.Remove( moveGuestLink! );
		var next = network.Add();
		Assert.AreEqual( seat, (int)next.Slot, "the freed seat is taken again" );
		NextMembersFirstRequestComesAfterTheRelease( host, next, seat );
	}

	/// <summary>
	/// The new member of <paramref name="seat"/> sends, as its first request, a moving placement of the type the seat carried: the host's stream releases the seat before it, its carry and
	/// its rights in one record, so the placement is refused.
	/// </summary>
	private void NextMembersFirstRequestComesAfterTheRelease( Park host, CoopTestNetwork.Member next, int seat )
	{
		host.Enter();
		var log = host.Level.Commands.Log.Count;
		next.Send( network!.Host.Id, new CoopGuestCommand( 1, ParkCommandCodec.Encode( host.Level.BuildCommand( host.One.Entry, 24, 12, 90 ) with { Moving = true } ) ).Encode() );
		for ( var round = 0; round < 600 && !host.Level.Commands.Log.Skip( log ).Any( entry => entry.Command is PlaceObjectCommand ); round++ )
			Rounds( 1, host );
		var since = host.Level.Commands.Log.Skip( log ).Select( entry => entry.Command ).ToArray();
		Assert.IsFalse( host.Level.CarriesEntry( seat ), "the seat's next member starts with nothing in hand" );
		Assert.IsFalse( host.Level.HasBuildGrant( seat ), "and without the last member's grant" );
		var release = Array.FindIndex( since, command => command is ReleaseSeatCommand { Seat: var released } && released == seat );
		var request = Array.FindIndex( since, command => command is PlaceObjectCommand && command.Player == seat );
		Assert.IsTrue( release >= 0 && request > release, string.Join( ", ", since.Select( command => command.Kind ) ) );
		Assert.IsFalse( since.Any( command => command is SetBuildGrantCommand ), "the one record released its carry and its rights" );
		Assert.IsNull( host.Level.Objects.FindAt( 24, 12 ), "its placement was refused" );
	}

	[TestMethod]
	public void APickUpAndASaleOfOneObjectHaveOneWinnerOnEveryPeer()
	{
		var (host, guest, view, seat) = MoveSession();
		// the host's sale first: the pick-up the guest sent before it is refused at its record
		var item = guest.Level.Objects.FindAt( 20, 12 )!;
		var refused = StartMove( view, guest, item );
		Assert.IsTrue( host.Issue( new RemoveAttractionCommand( host.Level.Objects.FindAt( 20, 12 )!.Runtime.AttractionId ) ).Accepted );
		RoundsUntil( () => !refused.IsOpen && guest.Level.WaitingToolStart == null, host, guest, view, "the pick-up came back" );
		Assert.IsFalse( refused.Accepted );
		Assert.IsFalse( guest.Level.IsMoving || guest.Level.CarriesEntry( seat ) || host.Level.CarriesEntry( seat ) );
		// the pick-up first: the host's sale after it finds no object
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( host.One.Entry, 20, 12, 90 ) ).Accepted );
		RoundsUntil( () => guest.Level.Objects.FindAt( 20, 12 ) != null, host, guest, view, "the new ride reached the guest" );
		var picked = StartMove( view, guest, guest.Level.Objects.FindAt( 20, 12 )! );
		var attraction = host.Level.Objects.FindAt( 20, 12 )!.Runtime.AttractionId;
		HostTakes( host, seat, ParkCommandKind.PickUpObject );
		Assert.IsFalse( host.Issue( new RemoveAttractionCommand( attraction ) ).Accepted, "the object is the guest's to place again" );
		RoundsUntil( () => guest.Level.IsMoving, host, guest, view, "the pick-up armed the Move tool" );
		Assert.IsTrue( picked.Accepted );
		view.Escape();
		RoundsUntil( () => !guest.Level.CarriesEntry( seat ) && !host.Level.CarriesEntry( seat ), host, guest, view, "the end released the carry" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void APickUpAndAQueueEditOfOneRideHaveOneOrderOnEveryPeer()
	{
		var (host, guest, _, _) = Shared();
		QueueOnBoth( host, guest );
		// a sold ride leaves the guests' attractions, as the game's level wires it
		foreach ( var park in new[] { host, guest } )
			park.Level.Objects.ObjectRemoved += item => park.Level.Guests?.Unregister( item.Visitors );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var seat = guest.Level.Commands.Player;
		// the host's edit first, then the guest's pick-up of the ride: both apply
		host.Enter();
		Assert.IsTrue( host.Issue( new EditQueueCommand( 10, 4 ) ).Accepted );
		var picked = StartMove( view, guest, guest.One );
		RoundsUntil( () => guest.Level.IsMoving, host, guest, view, "the pick-up armed the Move tool" );
		Assert.IsTrue( picked.Accepted );
		Assert.IsTrue( host.One.IsDeleted && guest.One.IsDeleted );
		// the pick-up first: an edit of the ride's queue after it finds no ride
		host.Enter();
		Assert.IsFalse( host.Issue( new EditQueueCommand( 10, 4 ) ).Accepted, "the ride was picked up" );
		view.Escape();
		RoundsUntil( () => !guest.Level.CarriesEntry( seat ) && !host.Level.CarriesEntry( seat ), host, guest, view, "the end released the carry" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsMoveToolWhoseCarryTheHostReleasedDisarmsWithoutAnEnd()
	{
		var (host, guest, view, seat) = MoveSession();
		PickUp( host, guest, view );
		Assert.IsTrue( host.Level.CarriesEntry( seat ) && guest.Level.CarriesEntry( seat ) );
		// the host releases the seat's carry (as a resume under a new member id makes it do): every placement of the guest's armed tool would be refused
		var log = host.Level.Commands.Log.Count;
		Assert.IsTrue( host.Issue( new ReleaseSeatCommand( seat ) ).Accepted );
		RoundsUntil( () => !guest.Level.IsMoving, host, guest, view, "the guest's frame disarmed the Move tool" );
		Assert.IsFalse( guest.Level.CarriesEntry( seat ) );
		Assert.IsNull( typeof( ParkHud ).GetField( "pendingItem", NonPublic )!.GetValue( view.Hud ), "the HUD's item went with it" );
		Rounds( 120, host, guest );
		Assert.IsFalse( host.Level.Commands.Log.Skip( log ).Any( entry => entry.Command is EndMoveCommand ), "no end: there was no carry to end" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void APickUpsBuyItemGoesWithItsDroppedWait()
	{
		var (host, guest, view, _) = MoveSession();
		StartMove( view, guest, guest.Level.Objects.FindAt( 20, 12 )! );
		var waitingMove = typeof( ParkHud ).GetField( "waitingMove", NonPublic )!;
		Assert.IsNotNull( waitingMove.GetValue( view.Hud ) );
		// Escape drops the wait: the HUD's buy item for it goes too
		view.Escape();
		Assert.IsNull( guest.Level.WaitingToolStart );
		view.Update();
		Assert.IsNull( waitingMove.GetValue( view.Hud ), "the buy item went with the wait" );
		RoundsUntil( () => !guest.Level.CarriesEntry( guest.Level.Commands.Player ), host, guest, view, "the carry of the pick-up its tool lost was ended" );
		Assert.IsFalse( guest.Level.IsMoving );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AHostLinkWithoutItsSessionsStateTakesNoGrantOrCarryBack()
	{
		var (host, guest, view, seat) = MoveSession();
		PickUp( host, guest, view );
		var hostLink = network!.Host;
		var log = host.Level.Commands.Log.Count;
		// the host's socket lost its session's state (between a loss and its resume): its frames read no seat
		var state = hostLink.Snapshot;
		hostLink.Snapshot = null;
		Rounds( 10, host );
		Assert.IsTrue( host.Level.HasBuildGrant( seat ) && host.Level.CarriesEntry( seat ), "no seat counts as left" );
		hostLink.Snapshot = state;
		Rounds( 10, host, guest );
		Assert.IsTrue( host.Level.HasBuildGrant( seat ) && guest.Level.HasBuildGrant( seat ), "the seat's member is the same once the state is back" );
		Assert.IsTrue( host.Level.CarriesEntry( seat ) && guest.Level.IsMoving, "and the carry and its Move tool stay" );
		Assert.IsFalse( host.Level.Commands.Log.Skip( log ).Any( entry => entry.Command is SetBuildGrantCommand or ReleaseSeatCommand ), "nothing was taken back" );
		view.Escape();
		RoundsUntil( () => !host.Level.CarriesEntry( seat ), host, guest, view, "the Move's end released the carry" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestThatLeavesWhileTheHostLinkLacksItsSessionsStateHasItsGrantAndCarryReleased()
	{
		var (host, guest, view, seat) = MoveSession();
		PickUp( host, guest, view );
		var hostLink = network!.Host;
		hostLink.Snapshot = null;
		// the departure is an event of its own: the next frame takes the grant back and releases the carry, whatever state the link holds
		guest.Enter();
		guest.Session!.End( "The guest left." );
		network.Remove( moveGuestLink! );
		for ( var round = 0; round < 600 && (host.Level.CarriesEntry( seat ) || host.Level.HasBuildGrant( seat )); round++ )
			Rounds( 1, host );
		Assert.IsFalse( host.Level.CarriesEntry( seat ) || host.Level.HasBuildGrant( seat ) );
		Assert.IsTrue( host.Level.Commands.Log.Any( entry => entry.Command is ReleaseSeatCommand { Seat: var released } && released == seat ), "a record of the stream" );
	}
}
