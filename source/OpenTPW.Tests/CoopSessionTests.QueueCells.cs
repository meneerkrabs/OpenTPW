using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Round 11 package D at the level: the track tool's globals the select writes, the tiles a demolishing command carries to every peer and a replay, the refunds per owner word and the cells a queue's
/// demolition leaves standing (QUEUE-015, ECON-050, ECON-049; docs/reverse/ORACLE.md 7.30, X44). The rig is the stream tests' asset-free park.
/// </summary>
public partial class CoopSessionTests
{
	private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

	private static OriginalObject Other( Park park ) => park.Level.Objects.Objects.First( item => !ReferenceEquals( item, park.One ) && item.Runtime.IsAttraction );

	private static ToolTiles TilesOf( OriginalObject item )
	{
		var station = TrackStation.FromPlacement( item.Placement.X, item.Placement.Y, item.Placement.Rotation );
		return new ToolTiles( station.Exit.X, station.Exit.Y, station.Entry.X, station.Entry.Y );
	}

	[TestMethod]
	public void ASelectWritesTheGlobalsAndTheSaleCarriesThemToAPeerThatSelectedNothing()
	{
		var issuer = NewPark();
		var twin = NewPark();
		issuer.Enter();
		// the select of the park view's click (0x5276D0): the thing's exit and entry tiles go into the issuer's globals
		issuer.Level.NoteToolSelection( Other(issuer) );
		var selected = TilesOf( Other(issuer) );
		Assert.AreEqual( selected, issuer.Level.ClientTiles );
		// a machine of its own: its globals say something else and stay out of the command
		twin.Level.ClientToolGlobals.Exit = (3, 3);
		twin.Level.ClientToolGlobals.Entry = (4, 4);
		var sale = issuer.Issue( new RemoveAttractionCommand( issuer.One.Runtime.AttractionId ) );
		Assert.IsTrue( sale.Accepted, sale.Reason );
		var logged = (RemoveAttractionCommand)issuer.Level.Commands.Log.Single().Command;
		Assert.AreEqual( selected, logged.Tiles, "the command carries the issuer's tiles" );
		// the bytes of the log keep them
		Assert.AreEqual( logged, ParkCommandCodec.Decode( ParkCommandCodec.Encode( logged ) ) );
		twin.Enter();
		Assert.IsTrue( twin.Level.Commands.Apply( logged ).Accepted );
		Assert.AreEqual( issuer.Level.ComputeStateHash(), twin.Level.ComputeStateHash(), "the peer ran the sale on the issuer's tiles, not its own" );
		Assert.AreEqual( (3, 3), twin.Level.ClientToolGlobals.Exit, "applying a command touches no client's own globals" );
	}

	[TestMethod]
	public void ACommandIssuedWithTilesKeepsThemAndOneWithoutTakesTheIssuersGlobals()
	{
		var park = NewPark();
		park.Enter();
		park.Level.ClientToolGlobals.Exit = (11, 12);
		park.Level.ClientToolGlobals.Entry = (13, 14);
		park.Issue( new RemoveObjectAtCommand( 1, 1 ) );
		park.Issue( new RemoveObjectAtCommand( 2, 2, new ToolTiles( 1, 2, 3, 4 ) ) );
		var tiles = park.Level.Commands.Log.Select( entry => ((RemoveObjectAtCommand)entry.Command).Tiles ).ToArray();
		CollectionAssert.AreEqual( new ToolTiles?[] { new ToolTiles( 11, 12, 13, 14 ), new ToolTiles( 1, 2, 3, 4 ) }, tiles );
	}

	[TestMethod]
	public void TheBuyLandSelectTakesTheGatePostBranchAndThePlacementResetsTheExit()
	{
		var park = NewPark();
		park.Enter();
		var level = park.Level;
		// the fence modes' select (0x52772A..0x527776): the post is the exit, the entry the post mapped by rotation 0 (0x52A420), (x - 2, y + 1)
		typeof( Level ).GetMethod( "NoteFenceSelection", Private )!.Invoke( level, new object[] { (30, 40) } );
		Assert.AreEqual( ((30, 40), (28, 41)), (level.ClientToolGlobals.Exit, level.ClientToolGlobals.Entry) );
		// a placement commit resets the exit (0x528ACE) and leaves the entry; with nothing selected its select is a no-op
		level.Selection = null;
		level.ClientToolGlobals.Entry = (5, 6);
		typeof( Level ).GetMethod( "NoteToolPlacement", Private )!.Invoke( level, new object[] { park.One.Entry } );
		Assert.AreEqual( ((-1, -1), (5, 6)), (level.ClientToolGlobals.Exit, level.ClientToolGlobals.Entry) );
		// with a selection the commit's own select (0x528F36) names its tiles after the reset
		level.Selection = Other(park);
		typeof( Level ).GetMethod( "NoteToolPlacement", Private )!.Invoke( level, new object[] { park.One.Entry } );
		var tiles = TilesOf( Other(park) );
		Assert.AreEqual( ((tiles.ExitX, tiles.ExitY), (tiles.EntryX, tiles.EntryY)), (level.ClientToolGlobals.Exit, level.ClientToolGlobals.Entry) );
		// the placement command selects the new object first (0x52A02E), so the commit's select maps the placed object, not the previous selection
		level.Selection = park.One;
		level.ClientToolGlobals.Exit = (7, 7);
		var placed = park.Issue( level.BuildCommand( park.One.Entry, 24, 12, 90 ) ).As<OriginalObject>()!;
		Assert.AreSame( placed, level.Selection );
		var placedTiles = TilesOf( placed );
		Assert.AreEqual( ((placedTiles.ExitX, placedTiles.ExitY), (placedTiles.EntryX, placedTiles.EntryY)), (level.ClientToolGlobals.Exit, level.ClientToolGlobals.Entry) );
	}

	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Level, object> hookedLevels = new();
	private static readonly HookSet hooked = new();

	private sealed class HookSet
	{
		public bool Contains( Level level ) => hookedLevels.TryGetValue( level, out _ );
		public void Add( Level level ) => hookedLevels.Add( level, new object() );
	}

	/// <summary>A typed queue of the ride: its entrance is the access cell, the front the first queue cell.</summary>
	private static void Typed( Park park, OriginalObject item, int x )
	{
		// the demolition's hooks of a started level (the rig's ConfigureObjectHooks leaves them out): the queue settled while its cells are laid, cleared once the ride is gone
		if ( !hooked.Contains( park.Level ) )
		{
			hooked.Add( park.Level );
			var queues = typeof( Level ).GetField( "demolishedQueues", Private )!;
			if ( queues.GetValue( park.Level ) == null )
				queues.SetValue( park.Level, Activator.CreateInstance( queues.FieldType ) );
			park.Level.Objects.ObjectRemoving += (Action<OriginalObject>)Delegate.CreateDelegate( typeof( Action<OriginalObject> ), park.Level, typeof( Level ).GetMethod( "SettleDemolishedQueue", Private )! );
			park.Level.Objects.ObjectRemoved += (Action<OriginalObject>)Delegate.CreateDelegate( typeof( Action<OriginalObject> ), park.Level, typeof( Level ).GetMethod( "ClearDemolishedQueue", Private )! );
			var rideLevel = park.Level;
			park.Level.Objects.ObjectRemoved += item => rideLevel.Guests?.Unregister( item.Visitors );
		}
		ConfigureQueue( park.Level.Guests!.Grid, item, x, 5 );
		var ride = item.Visitors;
		park.Level.Guests!.Grid.Cells.SetOwner( x, 7, ride.OwnerWord );
		ride.AccessCell = (x, 7);
		ride.Rotation = 0;
		ride.Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 );
		ride.QueueEntryResolver = () => (x, 7);
	}

	[TestMethod]
	public void AQueueCellRefundsAtTheScrapPercentageOfTheRideItsOwnerWordNames()
	{
		// the same queue demolished twice: all its cells owned by the demolished ride, then two of them owned by another ride whose percentage is lower
		var credits = new long[2];
		for ( var run = 0; run < 2; run++ )
		{
			var park = NewPark();
			park.Enter();
			var level = park.Level;
			Typed( park, park.One, 10 );
			var cells = level.Guests!.Grid.Cells;
			var other = Level.OwnerWord( Other(park) );
			// the owner cells must hold something for a removal to credit (the cell remover's test)
			cells.SetType( Other(park).Placement.X, Other(park).Placement.Y, ParkCellTypes.Object );
			cells.SetType( park.One.Placement.X, park.One.Placement.Y, ParkCellTypes.Object );
			var state = park.Economy.TryGetObject( level.GetEconomyInstance( Other(park) )!.Value, out var found ) ? found : throw new AssertInconclusiveException( "no economy state" );
			state.TimesUsed = 9;
			var one = park.Economy.TryGetObject( level.GetEconomyInstance( park.One )!.Value, out var ownState ) ? ownState : throw new AssertInconclusiveException();
			Assert.AreNotEqual( park.Economy.ScrapPercent( ownState ), park.Economy.ScrapPercent( state ), "the rides' percentages differ" );
			if ( run == 1 )
			{
				cells.SetOwner( 10, 3, other );
				cells.SetOwner( 10, 4, other );
			}
			var before = park.Economy.Balance;
			var sale = park.Issue( new RemoveAttractionCommand( park.One.Runtime.AttractionId ) );
			Assert.IsTrue( sale.Accepted, sale.Reason );
			credits[run] = park.Economy.Balance - before;
		}
		Assert.AreNotEqual( credits[0], credits[1], "two cells credited at the other ride's percentage" );
	}

	[TestMethod]
	public void ACellTheWalkDoesNotReachStaysAndASaleOfTheRideLeavesItAsAStrayOfItsOwnerWord()
	{
		var park = NewPark();
		park.Enter();
		var level = park.Level;
		Typed( park, park.One, 10 );
		var cells = level.Guests!.Grid.Cells;
		var owner = Level.OwnerWord( park.One );
		// a stray cell of the ride beside its queue, joined to nothing the walk follows
		cells.SetType( 14, 4, (byte)ParkCellType.Queue );
		cells.SetOwner( 14, 4, owner );
		var sale = park.Issue( new RemoveAttractionCommand( park.One.Runtime.AttractionId ) );
		Assert.IsTrue( sale.Accepted, sale.Reason );
		Assert.AreEqual( (byte)(byte)ParkCellType.Queue, cells.RawTypeAt( 14, 4 ), "the walk did not reach it: it stays" );
		Assert.AreEqual( owner, cells.OwnerAt( 14, 4 ), "with the sold ride's owner word" );
		// it is state: a full park save (the file's bytes) keeps it
		var saved = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( level.CaptureResumeSnapshot( null, "strays", level.AutosaveAssets() ) ) );
		var restored = ParkLayoutSnapshot.RestoreCellMap( saved.Layout );
		Assert.AreEqual( ((byte)ParkCellType.Queue, owner), (restored.RawTypeAt( 14, 4 ), restored.OwnerAt( 14, 4 )), "the stray cell is in the saved park" );
		var withStray = level.ComputeStateHash();
		cells.SetType( 14, 4, (byte)0 );
		cells.SetOwner( 14, 4, 0 );
		Assert.AreNotEqual( withStray, level.ComputeStateHash() );
	}

	[TestMethod]
	public void AStrandedCellIsRemovedAsAStrayWithNoRefundUntilARideWithItsOwnerWordTakesItOver()
	{
		var cells = new ParkCellMap( 128, 128 );
		var owner = ParkCellMap.CellWord( 40, 40 );
		cells.SetType( 20, 20, (byte)ParkCellType.Queue );
		cells.SetOwner( 20, 20, owner );
		var footprint = new[] { (20, 20, new ObjectShapeCell( 0, 0, ObjectCellKind.Occupied, '*' )) };
		var credited = 0;
		cells.DemolishFootprint( footprint, _ => credited++ );
		Assert.AreEqual( 0, credited, "the owner cell is empty: the stray goes without a refund" );
		Assert.AreEqual( 0, cells.RawTypeAt( 20, 20 ) );
		// a ride whose anchor is the owner's cell makes the same removal a refund
		cells.SetType( 20, 20, (byte)ParkCellType.Queue );
		cells.SetOwner( 20, 20, owner );
		cells.SetType( 40, 40, ParkCellTypes.Object );
		cells.DemolishFootprint( footprint, _ => credited++ );
		Assert.AreEqual( 1, credited, "the owner cell holds the new ride: the cell refunds" );
	}

	[TestMethod]
	public void AGuestOnTheClearedQueueIsDismissedAndTheBranchOfARealJunctionKeepsItsCells()
	{
		var park = NewPark();
		park.Enter();
		var level = park.Level;
		var grid = level.Guests!.Grid;
		Typed( park, park.One, 10 );
		var cells = grid.Cells;
		var owner = Level.OwnerWord( park.One );
		var east = Enumerable.Range( 0, 8 ).First( direction => GuestPathGrid.Directions[direction] == (1, 0) );
		var west = Enumerable.Range( 0, 8 ).First( direction => GuestPathGrid.Directions[direction] == (-1, 0) );
		// a real three-link junction: the queue cell (10, 4) also links east, to a branch of the ride's own cells the walk does not follow
		cells.SetLinks( 10, 4, (byte)(cells.LinksAt( 10, 4 ) | GuestPathGrid.LinkValue( east )) );
		foreach ( var x in new[] { 11, 12 } )
		{
			cells.SetType( x, 4, (byte)ParkCellType.Queue );
			cells.SetOwner( x, 4, owner );
			cells.SetLinks( x, 4, (byte)(GuestPathGrid.LinkValue( west ) | (x == 11 ? GuestPathGrid.LinkValue( east ) : 0)) );
			cells.SetQueueLink( x, 4, GuestPathGrid.LinkValue( west ) );
		}
		cells.SetLinks( 12, 4, GuestPathGrid.LinkValue( west ) );
		Assert.AreEqual( 3, System.Numerics.BitOperations.PopCount( (uint)cells.LinksAt( 10, 4 ) ), "a three-link cell" );
		grid.Invalidate();
		// guests stand on the queue's cells, in its line
		var queued = Enumerable.Range( 0, 4 ).Select( _ => level.Guests.SpawnInPark( 10, 1 ) ).ToArray();
		var places = new[] { (10, 6), (10, 5), (10, 3), (10, 2) };
		for ( var index = 0; index < queued.Length; index++ )
		{
			var guest = queued[index];
			park.One.Visitors.AppendToQueue( guest.Id );
			guest.AttractionId = park.One.Runtime.AttractionId;
			guest.State = GuestState.Queueing;
			guest.QueuePosition = index;
			guest.QueueJoinTurn = level.Guests.ParkTurn;
			guest.Happiness = 90;
			guest.OnGrid = true;
			(guest.CellX, guest.CellY) = places[index];
			guest.Navigation.OffGrid = false;
			(guest.Navigation.X, guest.Navigation.Y) = GuestNavigation.BytePoint( places[index], 128, 128 );
			guest.Navigation.SetDestination( guest.Navigation.X, guest.Navigation.Y, Array.Empty<(int X, int Y)>() );
			guest.HasWaypoint = false;
		}
		Assert.IsTrue( queued.All( guest => guest.State == GuestState.Queueing ) );
		var sale = park.Issue( new RemoveAttractionCommand( park.One.Runtime.AttractionId ) );
		Assert.IsTrue( sale.Accepted, sale.Reason );
		for ( var frame = 0; frame < 120; frame++ )
		{
			foreach ( var guest in queued )
				guest.Hunger = guest.Thirst = guest.Toilet = 0;
			park.Frame( 1 / 30.0 );
		}
		Assert.IsFalse( queued.Any( guest => guest.State == GuestState.Queueing ), "the guests of the cleared queue are dismissed: " + string.Join( ", ", queued.Select( guest => guest.State ) ) );
		Assert.IsFalse( Enumerable.Range( 2, 5 ).Any( y => grid.IsQueue( 10, y ) && y != 4 ), "the queue's own line is cleared" );
		Assert.IsTrue( grid.IsQueue( 11, 4 ) && grid.IsQueue( 12, 4 ), "the branch beyond the junction is stranded with its cells" );
	}

	[TestMethod]
	public void ASelectThenASaleReplaysTheSameOnATwinFromTheCommandLogAlone()
	{
		var issuer = NewPark();
		var twin = NewPark();
		foreach ( var park in new[] { issuer, twin } )
		{
			park.Enter();
			Typed( park, park.One, 10 );
		}
		issuer.Enter();
		issuer.Level.NoteToolSelection( issuer.One );
		Assert.IsTrue( issuer.Issue( new RemoveAttractionCommand( issuer.One.Runtime.AttractionId ) ).Accepted );
		var bytes = issuer.Level.Commands.Log.Select( entry => ParkCommandCodec.Encode( entry.Command ) ).ToList();
		twin.Enter();
		foreach ( var encoded in bytes )
			Assert.IsTrue( twin.Level.Commands.Apply( ParkCommandCodec.Decode( encoded ) ).Accepted );
		Assert.AreEqual( issuer.Level.ComputeStateHash(), twin.Level.ComputeStateHash() );
		Assert.AreEqual( CoopDigest.Compute( issuer.Level ), CoopDigest.Compute( twin.Level ) );
	}
}
