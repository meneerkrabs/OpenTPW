using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] A guest's edits beyond the first tools (docs/COOP.md, "Building by every player"; C3b-1): land, the area clear as one command, the remove click on a path or
/// queue cell, Backspace, the sell buttons and the queue's edit. Land, the area clear and the sales send and go on, as the host's tools end at once; Backspace and the sales take no second
/// request for the same thing while the first waits, and the queue's edit starts its tool from what its record read, unless Escape, a right click, another tool or a later record came
/// between. A guest's clear leaves standing what it may not remove and clears the paths around it.
/// </summary>
public partial class CoopSessionTests
{
	private const BindingFlags NonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

	/// <summary>The same synthetic queue on both parks before their first frame (<see cref="ConfigureQueue"/>): five cells from (10, 6) up to its back cell (10, 2), joined to the path at (10, 1).</summary>
	private static void QueueOnBoth( Park host, Park guest )
	{
		foreach ( var park in new[] { host, guest } )
		{
			park.Enter();
			ConfigureQueue( park.Level.Guests!.Grid, park.One, 10, 5 );
		}
	}

	private static byte Links( Park park, int x, int y ) => park.Level.Guests!.Grid.Cells.LinksAt( x, y );

	private static bool IsQueue( Park park, int x, int y ) => park.Level.Guests!.Grid.IsQueue( x, y );

	/// <summary>The HUD's cursor cell, as the pointer over the park sets it.</summary>
	private static void SetCursor( ParkHud hud, (int X, int Y) cell )
	{
		typeof( ParkHud ).GetField( "cursorX", NonPublic )!.SetValue( hud, cell.X );
		typeof( ParkHud ).GetField( "cursorY", NonPublic )!.SetValue( hud, cell.Y );
	}

	/// <summary>The object window's erase button on <paramref name="item"/> (without the confirmation).</summary>
	private static void Sell( ParkHud hud, OriginalObject item )
	{
		typeof( ParkHud ).GetField( "selectedObject", NonPublic )!.SetValue( hud, item );
		typeof( ParkHud ).GetMethod( "DeleteRide", NonPublic )!.Invoke( hud, null );
	}

	/// <summary>Marks <paramref name="item"/> as a track ride's station (a removal of it needs a claim), or takes the mark off.</summary>
	private static void MarkStation( Park park, OriginalObject item, bool station = true )
	{
		var stations = (Dictionary<OriginalObject, TrackStation>)typeof( Level ).GetField( "trackStations", NonPublic )!.GetValue( park.Level )!;
		if ( station )
			stations[item] = new TrackStation( 0, 0, (item.Placement.X, item.Placement.Y), (item.Placement.X + 1, item.Placement.Y) );
		else
			stations.Remove( item );
	}

	/// <summary>
	/// The placed objects type their footprint cells and a removal clears them, as the game's level wires them (<c>Level.ConnectObjectsToGuests</c>), which the asset-free park does not:
	/// the area clear's scans read the typed cells. Before the park's first frame, on every park alike.
	/// </summary>
	private static void TypeFootprints( Park park )
	{
		var level = park.Level;
		level.Objects.ObjectPlaced += item => level.Guests?.Grid.Cells.WriteFootprint( item.Cells, Level.OwnerWord( item ), item.Placement.Rotation );
		level.Objects.ObjectRemoved += item => level.Guests?.Grid.Cells.ClearFootprint( item.Cells );
	}

	/// <summary>The commands of <paramref name="seat"/> the host's log holds.</summary>
	private static ParkCommand[] HostCommandsOf( Park host, int seat ) => host.Level.Commands.Log.Where( entry => entry.Command.Player == seat ).Select( entry => entry.Command ).ToArray();

	/// <summary>The host's frames until it took the guest's next command of <paramref name="kind"/>.</summary>
	private void HostTakes( Park host, int seat, ParkCommandKind kind )
	{
		var before = HostCommandsOf( host, seat ).Count( command => command.Kind == kind );
		for ( var round = 0; round < 600 && HostCommandsOf( host, seat ).Count( command => command.Kind == kind ) == before; round++ )
		{
			clock += 1 / 60.0;
			host.Frame( 1 / 60.0 );
		}
		Assert.AreEqual( before + 1, HostCommandsOf( host, seat ).Count( command => command.Kind == kind ), $"the host took the guest's {kind}" );
	}

	/// <summary>Both parks' frames, without the guest's HUD (so it takes no outcome yet), until <paramref name="done"/> holds on the guest's park.</summary>
	private void FramesUntil( Func<bool> done, Park host, Park guest, string what )
	{
		for ( var round = 0; round < 600 && !done(); round++ )
		{
			clock += 1 / 60.0;
			host.Frame( 1 / 60.0 );
			guest.Frame( 1 / 60.0 );
		}
		Assert.IsTrue( done(), what );
	}

	[TestMethod]
	public void AGuestsQueueEditWaitsThenItsToolStartsFromWhatItsRecordRead()
	{
		var (host, guest, _, _) = Shared();
		QueueOnBoth( host, guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		var ride = guest.One.Visitors;
		// what the host's own click reads before its edit takes the link off
		var expected = guest.Level.ReadQueueToolStart( ride )!;
		Assert.IsNotNull( expected.Stack );
		var joined = Links( guest, 10, 2 );
		HudToolClick.Click( view.Hud, (10, 4) );
		Assert.IsNotNull( guest.Level.WaitingToolStart, "the edit went to the host" );
		Assert.IsFalse( guest.Level.CellTool.IsActive, "no tool before its outcome" );
		Assert.AreEqual( joined, Links( guest, 10, 2 ), "and nothing changed yet" );
		HudToolClick.Click( view.Hud, (10, 5) );
		Assert.AreEqual( 1, guest.Level.WaitingClicks, "another queue click waits" );
		RoundsUntil( () => guest.Level.WaitingToolStart == null, host, guest, view, "the edit came back" );
		var tool = guest.Level.CellTool;
		Assert.IsTrue( tool.IsActive );
		Assert.AreEqual( CellToolMode.Queue, tool.Mode );
		Assert.AreEqual( expected.Start, tool.Start, "the tool starts where the host's click starts it" );
		CollectionAssert.AreEqual( expected.Stack!.ToArray(), tool.Vertices.ToArray(), "from the corner walk before the edit" );
		Assert.AreNotEqual( joined, Links( guest, 10, 2 ), "the edit took the link to the path off" );
		Assert.AreEqual( Links( host, 10, 2 ), Links( guest, 10, 2 ), "on both parks" );
		Assert.AreEqual( 1, HostCommandsOf( host, guest.Level.Commands.Player ).Count( command => command is EditQueueCommand ), "one edit went" );
		// the tool lays its lines through the host
		Assert.IsNotNull( guest.Level.ClickCellTool( (8, 2) )?.Waiting );
		RoundsUntil( () => tool.Waiting == null, host, guest, view, "the segment came back" );
		Assert.AreEqual( (8, 2), tool.Start );
		Assert.IsTrue( IsQueue( host, 9, 2 ) && IsQueue( guest, 9, 2 ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AQueueEditThatWaitsIsLeftByEscapeOrARightClickAndItsOutcomeStartsNoTool()
	{
		var (host, guest, _, _) = Shared();
		QueueOnBoth( host, guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		HudToolClick.Click( view.Hud, (10, 4) );
		var first = guest.Level.WaitingToolStart;
		Assert.IsNotNull( first );
		var screens = view.Hud.Stack.Screens.Count;
		view.Escape();
		Assert.IsNull( guest.Level.WaitingToolStart, "Escape leaves the wait, as it leaves a tool" );
		Assert.AreEqual( screens, view.Hud.Stack.Screens.Count, "and opens no pause menu" );
		RoundsUntil( () => !first!.IsOpen, host, guest, view, "the edit resolved" );
		view.Update();
		Assert.IsTrue( first!.Accepted, "the edit applied at its record on every peer" );
		Assert.IsFalse( guest.Level.CellTool.IsActive, "but no tool starts from it" );
		// a right click leaves it too
		HudToolClick.Click( view.Hud, (10, 4) );
		var second = guest.Level.WaitingToolStart;
		Assert.IsNotNull( second );
		HudToolClick.RightClick( view.Hud, (10, 4) );
		Assert.IsNull( guest.Level.WaitingToolStart, "a right click leaves the wait" );
		RoundsUntil( () => !second!.IsOpen, host, guest, view, "the second edit resolved" );
		view.Update();
		Assert.IsFalse( guest.Level.CellTool.IsActive );
		// another tool that is on when the outcome comes back keeps its place
		HudToolClick.Click( view.Hud, (10, 4) );
		var third = guest.Level.WaitingToolStart;
		Assert.IsNotNull( third );
		guest.Level.IsRemovingObjects = true;
		RoundsUntil( () => !third!.IsOpen, host, guest, view, "the third edit resolved" );
		view.Update();
		Assert.IsNull( guest.Level.WaitingToolStart );
		Assert.IsTrue( guest.Level.IsRemovingObjects );
		Assert.IsFalse( guest.Level.CellTool.IsActive, "the remove tool stays the one on" );
		guest.Level.IsRemovingObjects = false;
		// nor does a tool that started and ended while the edit waited: the wait went with its start
		HudToolClick.Click( view.Hud, (10, 4) );
		var fourth = guest.Level.WaitingToolStart;
		Assert.IsNotNull( fourth );
		Assert.IsTrue( guest.Level.EnterPathTool( 5, 20 ) );
		Assert.IsNull( guest.Level.WaitingToolStart, "the path tool's start leaves the wait" );
		guest.Level.CellTool.Cancel();
		RoundsUntil( () => !fourth!.IsOpen, host, guest, view, "the fourth edit resolved" );
		view.Update();
		Assert.IsFalse( guest.Level.CellTool.IsActive, "no queue tool comes after the path tool's cancel" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AQueueEditStartsNoToolWhenALaterRecordChangedTheQueueOrSoldItsRide()
	{
		var (host, guest, _, _) = Shared();
		QueueOnBoth( host, guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var seat = guest.Level.Commands.Player;
		// the host takes the edit, then lays a segment from the queue's back cell onto the path: the same cells, joined to the path again, before the guest's HUD takes the outcome
		guest.Enter();
		HudToolClick.Click( view.Hud, (10, 4) );
		var edit = guest.Level.WaitingToolStart!;
		HostTakes( host, seat, ParkCommandKind.EditQueue );
		var detached = Links( host, 10, 2 );
		host.Enter();
		var cells = host.One.Visitors.QueueView( host.Level.Guests!.Grid ).QueueCells.ToArray();
		Assert.IsTrue( host.Issue( new QueueSegmentCommand( host.One.Visitors.AttractionId, 10, 2, 10, 1 ) ).Accepted );
		Assert.AreNotEqual( detached, Links( host, 10, 2 ), "joined again" );
		CollectionAssert.AreEqual( cells, host.One.Visitors.QueueView( host.Level.Guests.Grid ).QueueCells.ToArray(), "by its links alone" );
		FramesUntil( () => !edit.IsOpen && Links( guest, 10, 2 ) == Links( host, 10, 2 ), host, guest, "the guest applied both records" );
		Assert.IsNotNull( guest.Level.WaitingToolStart, "its HUD has not taken the outcome yet" );
		view.Update();
		Assert.IsNull( guest.Level.WaitingToolStart );
		Assert.IsFalse( guest.Level.CellTool.IsActive, "the queue changed since the record: no tool starts from its walk" );
		// the host sells the ride after it takes the next edit
		guest.Enter();
		HudToolClick.Click( view.Hud, (10, 4) );
		var second = guest.Level.WaitingToolStart!;
		HostTakes( host, seat, ParkCommandKind.EditQueue );
		Assert.IsTrue( host.Issue( new RemoveAttractionCommand( host.One.Runtime.AttractionId ) ).Accepted );
		FramesUntil( () => !second.IsOpen && guest.One.IsDeleted, host, guest, "the guest applied the edit and the sale" );
		view.Update();
		Assert.IsNull( guest.Level.WaitingToolStart );
		Assert.IsFalse( guest.Level.CellTool.IsActive, "the ride was sold since the record: no tool starts" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGrantedGuestBuysLandOnEveryParkAndARefusedPurchaseIsSaidToItAlone()
	{
		var (host, guest, _, _) = Shared();
		// the land tests' fenced rectangle (x 9 to 25, y 11 to 21, the gate's posts at (21, 11) and (13, 11)) on both parks before their first frame, with its tool
		foreach ( var park in new[] { host, guest } )
		{
			park.Enter();
			var level = park.Level;
			var map = level.Guests!.Grid.Cells;
			var land = ParkLand.FromClicks( new ParkLandSetup( (21, 11), (13, 11), new[] { (0, 0), (4, 0), (4, 10), (-12, 10), (-12, 0), (-8, 0) } ) );
			land.ApplyToNewPark( map );
			map.SetLand( land );
			Func<int, int, bool> barrier = ( _, _ ) => false;
			typeof( Level ).GetProperty( nameof( Level.LandBarrier ), NonPublic )!.SetValue( level, barrier );
			typeof( Level ).GetProperty( nameof( Level.BuyLandTool ) )!.SetValue( level,
				new BuyLandTool( () => level.Guests?.Grid.Cells, () => level.Park?.Economy, barrier, command => level.Commands.Issue( command ) ) );
		}
		var view = ViewOf( guest );
		var hostView = ViewOf( host );
		var charges = new[] { new List<long>(), new List<long>() };
		host.Economy.EventRaised += raised => { if ( raised.Kind == ParkEventKind.CellsBought ) charges[0].Add( raised.Amount ); };
		guest.Economy.EventRaised += raised => { if ( raised.Kind == ParkEventKind.CellsBought ) charges[1].Add( raised.Amount ); };
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		var tool = guest.Level.BuyLandTool!;
		bool Owned( Park park ) => !park.Level.Guests!.Grid.Cells.FlagsAt( 27, 13 ).HasFlag( ParkCellFlags.Unowned );
		Assert.IsFalse( Owned( guest ) );
		Assert.IsTrue( tool.Begin() );
		Assert.AreEqual( BuyLandClick.CornerStored, tool.Click( 26, 12 ) );
		tool.Hover( 30, 16 );
		Assert.AreEqual( LandPurchaseRefusal.None, tool.HoverCheck?.Refusal, "the rectangle east of the fence is for sale" );
		// its price, as the ghost reads it: the land's cell cost times its new cells
		var price = tool.HoverCheck!.Value.Cost;
		Assert.AreEqual( guest.Economy.CellCost( CellPurchase.Land ) * tool.HoverCheck.Value.Cells, price );
		Assert.AreEqual( BuyLandClick.Sent, tool.Click( 30, 16 ) );
		Assert.IsFalse( tool.IsActive, "the tool ends, as the host's does" );
		Assert.IsFalse( Owned( guest ), "nothing is bought before its record" );
		RoundsUntil( () => Owned( guest ), host, guest, view, "bought at its record" );
		Assert.IsTrue( Owned( host ), "on both parks" );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CannotBuyLand ) );
		CollectionAssert.AreEqual( new[] { price }, charges[0], "the bank is charged once, the land's price" );
		Assert.IsTrue( price > 0 );
		CollectionAssert.AreEqual( charges[0], charges[1], "the same charge on both parks" );
		// a rectangle of two cells a side: the park refuses it, and only the guest says so
		Assert.IsTrue( tool.Begin() );
		tool.Click( 30, 12 );
		tool.Hover( 32, 14 );
		Assert.AreEqual( LandPurchaseRefusal.TooSmall, tool.HoverCheck?.Refusal );
		Assert.AreEqual( BuyLandClick.Sent, tool.Click( 32, 14 ) );
		RoundsUntil( () => view.Says( OpenTpwText.CannotBuyLand ) == 1, host, guest, view, "the refusal is said to the guest" );
		hostView.Update();
		Assert.AreEqual( 0, hostView.Says( OpenTpwText.CannotBuyLand ) );
		Assert.AreEqual( 1, charges[0].Count, "a refused purchase charges nothing" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsAreaClearIsOneRequestThatLeavesAClaimedStationAndClearsThePathsAroundIt()
	{
		var (host, guest, _, _) = Shared();
		TypeFootprints( host );
		TypeFootprints( guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( host.One.Entry, 20, 12, 90 ) ).Accepted );
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( host.One.Entry, 24, 12, 90 ) ).Accepted );
		Assert.IsTrue( host.Issue( new LayLineCommand( CellToolMode.Path, -1, 18, 20, 26, 20 ) ).Accepted );
		Grant( host, guest, view );
		RoundsUntil( () => guest.Level.Objects.FindAt( 24, 12 ) != null && Walkable( guest, 22, 20 ), host, guest, view, "the host's building reached the guest" );
		// the ride at (24, 12) is a track ride's station on both parks: a guest may not remove it
		foreach ( var park in new[] { host, guest } )
			MarkStation( park, park.Level.Objects.FindAt( 24, 12 )! );
		guest.Enter();
		var tool = guest.Level.AreaClearTool!;
		tool.Begin();
		Assert.AreEqual( AreaClearClick.CornerStored, tool.Click( 17, 10 ) );
		Assert.AreEqual( AreaClearClick.Cleared, tool.Click( 28, 21 ) );
		Assert.IsFalse( tool.IsActive, "the tool ends, as the host's does" );
		Assert.AreEqual( (0, 0), tool.LastCleared, "the counts are the host's to know" );
		Assert.IsNotNull( guest.Level.Objects.FindAt( 20, 12 ), "nothing is cleared before its record" );
		RoundsUntil( () => guest.Level.Objects.FindAt( 20, 12 ) == null, host, guest, view, "cleared at its record" );
		var seat = guest.Level.Commands.Player;
		CollectionAssert.AreEqual( new[] { ParkCommandKind.ClearArea }, HostCommandsOf( host, seat ).Select( command => command.Kind ).ToArray(), "the rectangle is one request" );
		Assert.IsNull( host.Level.Objects.FindAt( 20, 12 ) );
		Assert.IsNotNull( host.Level.Objects.FindAt( 24, 12 ), "the station stays on the host's park" );
		Assert.IsNotNull( guest.Level.Objects.FindAt( 24, 12 ), "and on the guest's" );
		Assert.IsFalse( Walkable( host, 22, 20 ) || Walkable( guest, 22, 20 ), "the paths around it are cleared" );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopHostOnly ) + view.Says( OpenTpwText.CoopNotGranted ) );
		foreach ( var park in new[] { host, guest } )
			MarkStation( park, park.Level.Objects.FindAt( 24, 12 )!, station: false );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsRemoveClickTakesAPathCellAndAQueueCellWithTheCellsBehindIt()
	{
		var (host, guest, _, _) = Shared();
		QueueOnBoth( host, guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		guest.Level.IsRemovingObjects = true;
		guest.Level.ClickRemoveTool( 12, 1 );
		Assert.IsNotNull( guest.Level.WaitingRemoval, "the path cell's removal went to the host" );
		RoundsUntil( () => guest.Level.WaitingRemoval == null, host, guest, view, "the path cell's removal came back" );
		Assert.IsFalse( Walkable( host, 12, 1 ) || Walkable( guest, 12, 1 ) );
		long Refunds( Park park ) => park.Economy.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherIncome );
		var (hostBefore, guestBefore) = (Refunds( host ), Refunds( guest ));
		// three cells go (10, 4) and the two behind it, each refunded at the queue cell's cost times the ride's scrap share
		host.Enter();
		var instance = host.Level.GetEconomyInstance( host.One );
		Assert.IsNotNull( instance, "the ride has its economy's object" );
		Assert.IsTrue( host.Economy.TryGetObject( instance.Value, out var state ) );
		var refund = 3 * (host.Economy.CellCost( CellPurchase.Queue ) * host.Economy.ScrapPercent( state ) / 100);
		Assert.IsTrue( refund > 0 );
		guest.Level.ClickRemoveTool( 10, 4 );
		Assert.IsNotNull( guest.Level.WaitingRemoval, "the queue cell's removal went to the host" );
		RoundsUntil( () => guest.Level.WaitingRemoval == null, host, guest, view, "the queue cell's removal came back" );
		Assert.AreEqual( refund, Refunds( host ) - hostBefore, "the cells are refunded" );
		Assert.AreEqual( refund, Refunds( guest ) - guestBefore, "the same refund on both parks" );
		foreach ( var park in new[] { host, guest } )
		{
			Assert.IsFalse( IsQueue( park, 10, 4 ) || IsQueue( park, 10, 3 ) || IsQueue( park, 10, 2 ), "the cell and those behind it go" );
			Assert.IsTrue( IsQueue( park, 10, 6 ) && IsQueue( park, 10, 5 ), "the cells before it stay" );
		}
		guest.Level.IsRemovingObjects = false;
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsBackspaceTakesAPathCellBackOneRequestAtATime()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		SetCursor( view.Hud, (12, 1) );
		Assert.IsTrue( view.Hud.BacktrackKey(), "the key sends the cell's backtrack" );
		var waiting = guest.Level.WaitingBacktrack;
		Assert.IsNotNull( waiting );
		Assert.IsTrue( Walkable( guest, 12, 1 ), "nothing is taken back before its record" );
		Assert.IsTrue( view.Hud.BacktrackKey( repeating: true ), "a held key's repeats are taken" );
		Assert.IsTrue( view.Hud.BacktrackKey() );
		Assert.AreSame( waiting, guest.Level.WaitingBacktrack, "but send nothing more" );
		Assert.AreEqual( 1, guest.Level.WaitingClicks, "a new press asks the player to wait" );
		RoundsUntil( () => guest.Level.WaitingBacktrack == null, host, guest, view, "the Backspace came back" );
		Assert.IsFalse( Walkable( host, 12, 1 ) || Walkable( guest, 12, 1 ) );
		Assert.AreEqual( 1, HostCommandsOf( host, guest.Level.Commands.Player ).Count( command => command is BacktrackCommand ), "one request went" );
		// nothing to take back there now: the key sends nothing
		Assert.IsFalse( view.Hud.BacktrackKey() );
		Assert.IsNull( guest.Level.WaitingBacktrack );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsSaleTakesNoSecondWhileItWaitsAndAStationIsTheHostsToSell()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( host.One.Entry, 20, 12, 90 ) ).Accepted );
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( host.One.Entry, 24, 12, 90 ) ).Accepted );
		Grant( host, guest, view );
		RoundsUntil( () => guest.Level.Objects.FindAt( 24, 12 ) != null, host, guest, view, "the host's rides reached the guest" );
		var confirmations = GameOptions.Current.Confirmations;
		GameOptions.Current.Confirmations = false;
		try
		{
			guest.Enter();
			var item = guest.Level.Objects.FindAt( 20, 12 )!;
			Sell( view.Hud, item );
			Assert.IsTrue( guest.Level.IsSaleWaiting( item.Runtime.AttractionId ), "the sale went to the host" );
			Assert.IsFalse( item.IsDeleted, "nothing is sold before its record" );
			Sell( view.Hud, item );
			// the object window's erase button is off for the object while its sale waits
			typeof( ParkHud ).GetField( "selectedObject", NonPublic )!.SetValue( view.Hud, item );
			view.Update();
			var erase = ((UiScreen)typeof( ParkHud ).GetField( "hud", NonPublic )!.GetValue( view.Hud )!).Find( "deleteRide" ) as UiButton;
			Assert.IsNotNull( erase );
			Assert.IsFalse( erase!.Enabled, "the erase button is off while the sale waits" );
			RoundsUntil( () => !guest.Level.IsSaleWaiting( item.Runtime.AttractionId ), host, guest, view, "the sale came back" );
			Assert.IsTrue( item.IsDeleted );
			Assert.IsNull( host.Level.Objects.FindAt( 20, 12 ) );
			Assert.AreEqual( 1, HostCommandsOf( host, guest.Level.Commands.Player ).Count( command => command is RemoveAttractionCommand ), "the second click sent no second sale" );
			// a track ride's station is the host's to sell: refused here, in the host-only words, and nothing goes
			foreach ( var park in new[] { host, guest } )
				MarkStation( park, park.Level.Objects.FindAt( 24, 12 )! );
			guest.Enter();
			var station = guest.Level.Objects.FindAt( 24, 12 )!;
			Sell( view.Hud, station );
			Assert.IsFalse( guest.Level.IsSaleWaiting( station.Runtime.AttractionId ) );
			view.Update();
			Assert.AreEqual( 1, view.Says( OpenTpwText.CoopHostOnly ) );
			foreach ( var park in new[] { host, guest } )
				MarkStation( park, park.Level.Objects.FindAt( 24, 12 )!, station: false );
		}
		finally
		{
			GameOptions.Current.Confirmations = confirmations;
		}
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheSessionsEndEndsTheQueueEditTheBackspaceAndTheSalesThatWait()
	{
		var (host, guest, _, _) = Shared();
		QueueOnBoth( host, guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( host.One.Entry, 20, 12, 90 ) ).Accepted );
		Grant( host, guest, view );
		RoundsUntil( () => guest.Level.Objects.FindAt( 20, 12 ) != null, host, guest, view, "the host's ride reached the guest" );
		var confirmations = GameOptions.Current.Confirmations;
		GameOptions.Current.Confirmations = false;
		try
		{
			guest.Enter();
			HudToolClick.Click( view.Hud, (10, 4) );
			SetCursor( view.Hud, (12, 1) );
			Assert.IsTrue( view.Hud.BacktrackKey() );
			var item = guest.Level.Objects.FindAt( 20, 12 )!;
			Sell( view.Hud, item );
			Assert.IsNotNull( guest.Level.WaitingToolStart );
			Assert.IsNotNull( guest.Level.WaitingBacktrack );
			Assert.IsTrue( guest.Level.IsSaleWaiting( item.Runtime.AttractionId ) );
			guest.Session!.End( "The guest left." );
			Assert.IsNull( guest.Level.WaitingToolStart );
			Assert.IsNull( guest.Level.WaitingBacktrack );
			Assert.IsFalse( guest.Level.IsSaleWaiting( item.Runtime.AttractionId ), "the sell buttons take the object again" );
			view.Update();
			Assert.IsFalse( guest.Level.CellTool.IsActive );
		}
		finally
		{
			GameOptions.Current.Confirmations = confirmations;
		}
	}

	[TestMethod]
	public void TheAreaClearsOneCommandLeavesTheParkAsItsCellCommandsDid()
	{
		var one = NewPark();
		var cells = NewPark();
		foreach ( var park in new[] { one, cells } )
		{
			TypeFootprints( park );
			Assert.IsTrue( park.Issue( park.Level.BuildCommand( park.One.Entry, 20, 12, 90 ) ).Accepted );
			Assert.IsTrue( park.Issue( park.Level.BuildCommand( park.One.Entry, 24, 12, 90 ) ).Accepted );
			// a ride outside the rectangle stays
			Assert.IsTrue( park.Issue( park.Level.BuildCommand( park.One.Entry, 28, 15, 90 ) ).Accepted );
			Assert.IsTrue( park.Issue( new LayLineCommand( CellToolMode.Path, -1, 18, 20, 30, 20 ) ).Accepted );
		}
		var sounds = new List<uint>();
		GameAudio.UiSoundObserver = sounds.Add;
		try
		{
			one.Enter();
			var result = one.Issue( new ClearAreaCommand( 17, 10, 26, 21 ) );
			var oneSounds = sounds.ToArray();
			sounds.Clear();
			// the tool's clear before C3b: a command for each object and path cell, as the scans found them
			cells.Enter();
			var level = cells.Level;
			var old = AreaClear.Run( level.Guests!.Grid.Cells, (17, 10), (26, 21), ( x, y ) => level.Commands.Issue( new RemoveObjectAtCommand( x, y ) ).Accepted,
				( x, y ) => level.Commands.Issue( new RemovePathCellCommand( x, y ) ) is { Value: CellBuildResult removed } ? removed : CellBuildResult.NotPath );
			Assert.IsTrue( result.Accepted );
			Assert.AreEqual( old, result.As<(int Objects, int Paths)>(), "the same counts" );
			// what tp.exe's clear takes there, read off the park and not off the scans: the two rides wholly inside and the path cells x 18 to 26 of the line
			Assert.AreEqual( (2, 9), old );
			CollectionAssert.AreEqual( sounds.ToArray(), oneSounds, "the same cues" );
		}
		finally
		{
			GameAudio.UiSoundObserver = null;
		}
		foreach ( var park in new[] { one, cells } )
		{
			Assert.IsNull( park.Level.Objects.FindAt( 20, 12 ) ?? park.Level.Objects.FindAt( 24, 12 ), "the rides inside go" );
			Assert.IsNotNull( park.Level.Objects.FindAt( 28, 15 ), "the ride outside stays" );
			Assert.IsTrue( Enumerable.Range( 18, 9 ).All( x => !Walkable( park, x, 20 ) ), "the path cells inside go" );
			Assert.IsTrue( Enumerable.Range( 27, 4 ).All( x => Walkable( park, x, 20 ) ), "the path cells outside stay" );
		}
		Assert.AreEqual( cells.Level.LastActionMessage, one.Level.LastActionMessage, "the same last message" );
		Assert.AreEqual( cells.Level.ComputeStateHash(), one.Level.ComputeStateHash(), "the same world" );
		Assert.AreEqual( CoopDigest.Compute( cells.Level ), CoopDigest.Compute( one.Level ), "the same session state" );
	}

	[TestMethod]
	public void AnAreaClearOnAParkWhoseObjectsDoNotTypeTheirCellsFollowsTheObjectOnTheCell()
	{
		var park = NewPark();
		Assert.IsTrue( park.Issue( park.Level.BuildCommand( park.One.Entry, 20, 12, 90 ) ).Accepted );
		var item = park.Level.Objects.FindAt( 20, 12 )!;
		// the object's cells untyped, as a Theme Park Inc layout's are
		var map = park.Level.Guests!.Grid.Cells;
		foreach ( var (x, y, _) in item.Cells )
			map.SetType( x, y, ParkCellType.Empty );
		Assert.AreSame( item, park.Level.Objects.FindAt( 20, 12 ) );
		Assert.AreEqual( (0, 0), park.Issue( new ClearAreaCommand( 17, 10, 26, 16 ) ).As<(int Objects, int Paths)>(), "in a Theme Park World park the cell map's types decide" );
		Assert.IsFalse( item.IsDeleted );
		GameEditions.Select( GameEdition.ThemeParkInc );
		Assert.AreEqual( (1, 0), park.Issue( new ClearAreaCommand( 17, 10, 26, 16 ) ).As<(int Objects, int Paths)>(), "where objects do not type their cells, the object on the cell" );
		Assert.IsTrue( item.IsDeleted );
	}


	[TestMethod]
	public void AClaimedRidesQueueIsTheHostsForAGuest()
	{
		var (host, guest, _, _) = Shared();
		QueueOnBoth( host, guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		var seat = guest.Level.Commands.Player;
		var rights = new CoopBuildRights( guest.Level );
		var ride = guest.One.Runtime.AttractionId;
		ParkCommand[] queue =
		{
			new EditQueueCommand( 10, 4 ),
			new QueueSegmentCommand( ride, 10, 2, 8, 2 ),
			new LayLineCommand( CellToolMode.Queue, ride, 10, 2, 8, 2 ),
			new RemoveAtCommand( 10, 4 ),
			new BacktrackCommand( 10, 4 )
		};
		Assert.AreSame( guest.One, guest.Level.QueueOwnerAt( 10, 4 ) );
		foreach ( var command in queue )
			Assert.IsTrue( rights.Allows( seat, command ), $"an ordinary ride's queue is a granted guest's: {command}" );
		// the ride is a track ride's station: its queue stays the host's until claims come
		MarkStation( guest, guest.One );
		try
		{
			foreach ( var command in queue )
			{
				Assert.IsFalse( rights.Allows( seat, command ), $"a claimed ride's queue is the host's: {command}" );
				Assert.IsTrue( rights.Allows( ParkCommands.LocalPlayer, command ) );
			}
			Assert.IsTrue( rights.Allows( seat, new RemoveAtCommand( 12, 1 ) ), "a path cell stays the guest's" );
			// the path cell its back (10, 2) joins is the host's too: a removal or a queue laid over it takes the link off
			Assert.IsFalse( rights.Allows( seat, new RemoveAtCommand( 10, 1 ) ), "the path cell the claimed queue joins" );
			Assert.IsFalse( rights.Allows( seat, new BacktrackCommand( 10, 1 ) ) );
			Assert.IsTrue( rights.Allows( seat, new BacktrackCommand( 12, 1 ) ) );
			Assert.IsFalse( rights.AllowsPathRemoval( seat, 10, 1 ) );
			Assert.IsTrue( rights.AllowsPathRemoval( seat, 9, 1 ) && rights.AllowsPathRemoval( ParkCommands.LocalPlayer, 10, 1 ) );
			var other = guest.Level.Objects.Objects.First( item => !ReferenceEquals( item, guest.One ) ).Runtime.AttractionId;
			Assert.IsFalse( rights.Allows( seat, new QueueSegmentCommand( other, 12, 1, 8, 1 ) ), "another ride's queue over the path cell the claimed queue joins" );
			Assert.IsFalse( rights.Allows( seat, new LayLineCommand( CellToolMode.Queue, other, 12, 1, 8, 1 ) ) );
			Assert.IsTrue( rights.Allows( seat, new LayLineCommand( CellToolMode.Path, -1, 12, 1, 8, 1 ) ), "a path line along it changes no link of the queue" );
			Assert.IsFalse( rights.Allows( seat, new LayLineCommand( CellToolMode.Path, -1, 12, 3, 8, 3 ) ), "a path line over a claimed queue cell (a path line paves a queue end)" );
			Assert.IsTrue( rights.Allows( seat, new QueueSegmentCommand( other, 12, 9, 8, 9 ) ), "a line that crosses neither is the guest's" );
			// nor may a placement write over them: its footprint clears the paths under it, and it lays the cells outside its entrance and exit
			var entry = guest.Level.Objects.Objects.First( item => !ReferenceEquals( item, guest.One ) ).Entry;
			Assert.IsFalse( rights.Allows( seat, guest.Level.BuildCommand( entry, 10, 1, 0 ) ), "a placement over the path cell the claimed queue joins" );
			Assert.IsFalse( rights.Allows( seat, guest.Level.BuildCommand( entry, 10, 4, 0 ) ), "a placement over the claimed queue" );
			Assert.IsTrue( rights.Allows( seat, guest.Level.BuildCommand( entry, 24, 20, 0 ) ), "a placement away from it" );
			Assert.IsTrue( rights.Allows( ParkCommands.LocalPlayer, guest.Level.BuildCommand( entry, 10, 1, 0 ) ) );
			// the object window offers the guest no tool for that queue
			var canEdit = typeof( ParkHud ).GetMethod( "CanEditQueue", NonPublic )!;
			Assert.IsFalse( (bool)canEdit.Invoke( view.Hud, new object[] { guest.One } )! );
		}
		finally
		{
			MarkStation( guest, guest.One, station: false );
		}
		Assert.IsTrue( (bool)typeof( ParkHud ).GetMethod( "CanEditQueue", NonPublic )!.Invoke( view.Hud, new object[] { guest.One } )!, "an ordinary ride's queue tool is offered" );
		Assert.IsTrue( rights.Allows( seat, new RemoveAtCommand( 10, 1 ) ) && rights.Allows( seat, new LayLineCommand( CellToolMode.Path, -1, 12, 3, 8, 3 ) ), "an ordinary ride's join is the guest's" );
		Assert.IsTrue( rights.Allows( seat, guest.Level.BuildCommand( guest.Level.Objects.Objects.First( item => !ReferenceEquals( item, guest.One ) ).Entry, 10, 1, 0 ) ) );
	}

	[TestMethod]
	public void AGuestsAreaClearLeavesThePathCellAClaimedQueueJoins()
	{
		var park = NewPark();
		park.Enter();
		var level = park.Level;
		var grid = level.Guests!.Grid;
		// One's queue from (10, 6) up to its back cell (10, 2), joined to the path at (10, 1)
		ConfigureQueue( grid, park.One, 10, 5 );
		Assert.AreNotEqual( 0, Links( park, 10, 1 ) & GuestPathGrid.LinkValue( 2 ), "the path cell links to the queue's back" );
		var rights = new CoopBuildRights( level );
		level.SetBuildGrant( 1, true );
		var before = level.Commands.Rights;
		level.Commands.Rights = rights;
		MarkStation( park, park.One );
		try
		{
			// a path line along the path cell the queue joins is the guest's: its link pass leaves the queue's links as they were
			var queueLinks = (Links( park, 10, 2 ), Links( park, 10, 3 ));
			var line = level.Commands.ApplyRemote( new LayLineCommand( CellToolMode.Path, -1, 12, 1, 8, 1 ) { Player = 1 } );
			Assert.IsFalse( line.RightsRefused, line.Reason );
			Assert.AreEqual( queueLinks, (Links( park, 10, 2 ), Links( park, 10, 3 )) );
			Assert.IsTrue( Walkable( park, 10, 1 ) && IsQueue( park, 10, 2 ) );
			var result = level.Commands.ApplyRemote( new ClearAreaCommand( 9, 0, 11, 1 ) { Player = 1 } );
			Assert.IsTrue( result.Accepted );
			Assert.AreEqual( (0, 2), result.As<(int Objects, int Paths)>(), "the guest's clear takes the two path cells beside it" );
			Assert.IsTrue( Walkable( park, 10, 1 ) && !Walkable( park, 9, 1 ) && !Walkable( park, 11, 1 ) );
			Assert.AreNotEqual( 0, Links( park, 10, 2 ) & GuestPathGrid.LinkValue( 0 ), "the queue's back keeps its link to the path" );
			Assert.IsTrue( IsQueue( park, 10, 2 ) && IsQueue( park, 10, 6 ) );
			// the host's clear takes it
			Assert.AreEqual( (0, 1), level.Commands.ApplyRemote( new ClearAreaCommand( 9, 0, 11, 1 ) ).As<(int Objects, int Paths)>() );
			Assert.IsFalse( Walkable( park, 10, 1 ) );
		}
		finally
		{
			MarkStation( park, park.One, station: false );
			level.Commands.Rights = before;
		}
	}

	[TestMethod]
	public void AGuestsQueueLineOfAnotherRideMayNotCrossAClaimedQueue()
	{
		var park = NewPark();
		park.Enter();
		var level = park.Level;
		var grid = level.Guests!.Grid;
		// One's queue from (10, 6) up to (10, 3); a cell its cached line does not hold yet at (10, 2), joined to its back (a grid edit no recompute took in)
		ConfigureQueue( grid, park.One, 10, 4 );
		grid.SetQueue( 10, 2, 2 );
		Assert.IsFalse( park.One.Visitors.QueueCells.Contains( (10, 2) ) );
		Assert.AreSame( park.One, level.QueueOwnerAt( 10, 2 ), "the cell is resolved as its removal resolves it" );
		var rights = new CoopBuildRights( level );
		level.SetBuildGrant( 1, true );
		MarkStation( park, park.One );
		try
		{
			Assert.IsFalse( rights.Allows( 1, new RemoveAtCommand( 10, 2 ) ), "a claimed queue's cell the cached line misses is still the host's" );
			Assert.IsFalse( rights.Allows( 1, new BacktrackCommand( 10, 2 ) ) );
			// a line of another ride across the claimed queue
			var other = (Two: level.Objects.Objects.First( item => !ReferenceEquals( item, park.One ) ), _: 0).Two;
			Assert.IsFalse( rights.Allows( 1, new QueueSegmentCommand( other.Runtime.AttractionId, 14, 4, 6, 4 ) ), "a line across a claimed queue's cell is the host's" );
			Assert.IsFalse( rights.Allows( 1, new LayLineCommand( CellToolMode.Queue, other.Runtime.AttractionId, 14, 4, 6, 4 ) ) );
			Assert.IsTrue( rights.Allows( 1, new QueueSegmentCommand( other.Runtime.AttractionId, 14, 8, 6, 8 ) ), "a line that crosses none is the guest's" );
			Assert.IsFalse( rights.Allows( 1, new LayLineCommand( CellToolMode.Path, -1, 14, 4, 6, 4 ) ), "a path line across it too" );
			Assert.IsTrue( rights.Allows( 1, new LayLineCommand( CellToolMode.Path, -1, 14, 8, 6, 8 ) ) );
			// the cells' owners resolved together, as each alone
			CollectionAssert.AreEqual( new (int, int, OriginalObject?)[] { (10, 2, park.One), (10, 4, park.One) },
				level.QueueOwnersAt( new[] { (10, 2), (9, 9), (10, 4), (-1, 4) } ).ToArray() );
		}
		finally
		{
			MarkStation( park, park.One, station: false );
		}
		Assert.IsTrue( rights.Allows( 1, new RemoveAtCommand( 10, 2 ) ) );
		Assert.IsTrue( rights.Allows( 1, new QueueSegmentCommand( level.Objects.Objects.First( item => !ReferenceEquals( item, park.One ) ).Runtime.AttractionId, 14, 4, 6, 4 ) ) );
	}

	[TestMethod]
	public void ASessionLogReplaysUnderTheSessionsRights()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		// an ungranted guest's line (sent past its own pre-check): refused by the session's rights at its record on every peer
		var seat = guest.Level.Commands.Player;
		var line = guest.Session!.GuestCommands!.Submit( new LayLineCommand( CellToolMode.Path, -1, 5, 20, 12, 20 ) { Player = seat } );
		RoundsUntil( () => !line.IsOpen, host, guest, view, "the line was answered" );
		Assert.IsTrue( line.Result is { Accepted: false, RightsRefused: true } );
		Assert.IsFalse( Walkable( host, 8, 20 ) );
		Rounds( 60, host, guest );
		var hostStream = host.Session!.HostStream!;
		host.Session.End( "Done." );
		var log = ParkCommandLogFile.Deserialize( hostStream.SessionLog!.Serialize() );
		long? Replay( bool withRights )
		{
			var twin = NewPark();
			twin.Enter();
			var hashes = new Dictionary<long, ulong>();
			var commands = twin.Level.Commands;
			var own = new CoopBuildRights( twin.Level );
			commands.Rights = own;
			if ( withRights )
				ParkCommandReplay.Run( commands, log, () => commands.NextTick, () => twin.Level.RunTicks( 1 ), log.Checkpoints[^1].Ticks, ticks => hashes[ticks] = twin.Level.ComputeStateHash(),
					new CoopBuildRights( twin.Level ) );
			else
			{
				// the session's rules alone, without its rights
				commands.Rights = null;
				commands.SessionRules = true;
				try
				{
					ParkCommandReplay.Run( commands, log.Entries, () => commands.NextTick, () => twin.Level.RunTicks( 1 ), log.Checkpoints[^1].Ticks, ticks => hashes[ticks] = twin.Level.ComputeStateHash() );
				}
				finally
				{
					commands.SessionRules = false;
					commands.Rights = own;
				}
			}
			hashes[log.Checkpoints[^1].Ticks] = twin.Level.ComputeStateHash();
			Assert.AreSame( own, commands.Rights, "the replay puts the funnel's own rights back" );
			Assert.IsFalse( commands.SessionRules );
			return ParkCommandReplay.FirstMismatch( log, hashes )?.Ticks;
		}
		Assert.IsNull( Replay( withRights: true ), "under the session's rights every checkpoint matches" );
		Assert.IsNotNull( Replay( withRights: false ), "without them the guest's refused line would be laid" );
		// a session's log never replays without its rights
		var bare = NewPark();
		bare.Enter();
		Assert.ThrowsException<ArgumentException>( () => ParkCommandReplay.Run( bare.Level.Commands, log, () => bare.Level.Commands.NextTick, () => bare.Level.RunTicks( 1 ), log.Checkpoints[^1].Ticks ) );
	}
}

