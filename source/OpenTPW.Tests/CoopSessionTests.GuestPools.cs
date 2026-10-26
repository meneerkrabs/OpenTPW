using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] A guest's pools (docs/COOP.md, "A guest's pools"; C3b-3): the pool tool's strokes go to the host in batches (<see cref="PoolCellsCommand"/>,
/// <see cref="LayPoolSlidePiecesCommand"/>), at most four pool requests open and one queue of unsent cells, and a stroke reports once it is back, as the host's release does; a bridge, a
/// slide's undo and Backspace over a bridge or a pool cell are requests of their own. Each session test ends with equal digests, world hashes and logs.
/// </summary>
public partial class CoopSessionTests
{
	/// <summary>Frames of the host and the guest with the guest's session step but without its HUD: the guest takes no outcome yet.</summary>
	private void GuestFrames( int count, Park host, Park guest, bool hostRuns = true )
	{
		for ( var round = 0; round < count; round++ )
		{
			clock += 1 / 60.0;
			if ( hostRuns )
				host.Frame( 1 / 60.0 );
			guest.Frame( 1 / 60.0 );
			guest.Session!.Step();
		}
	}

	/// <summary><see cref="GuestFrames"/> until <paramref name="done"/> holds: a stroke the guest's HUD would release (its pointer is outside the park in these tests) goes on.</summary>
	private void GuestFramesUntil( Func<bool> done, Park host, Park guest, string what )
	{
		for ( var round = 0; round < 600 && !done(); round++ )
			GuestFrames( 1, host, guest );
		Assert.IsTrue( done(), what );
	}

	/// <summary>The cells of the guest's batches the host's log holds, in order.</summary>
	private static (int X, int Y)[] HostBatchCells( Park host, int seat ) => HostCommandsOf( host, seat ).OfType<PoolCellsCommand>().SelectMany( batch => batch.Cells ).ToArray();

	private static bool IsWater( Park park, int x, int y ) => park.Level.Guests!.Grid.Cells.RawTypeAt( x, y ) == ParkCellTypes.PoolWater;

	private static (int X, int Y)[] Row( int fromX, int toX, int y ) => Enumerable.Range( Math.Min( fromX, toX ), Math.Abs( toX - fromX ) + 1 ).Select( x => (x, y) ).ToArray();

	[TestMethod]
	public void AGuestsPaintStrokeGoesInBatchesAndReportsOnceItIsBack()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		var tool = guest.Level.PoolTool!;
		Assert.AreEqual( PoolBuildResult.Ok, guest.Level.PoolBuilder!.Check( 14, 26 ), "the ground takes a pool" );
		Assert.IsTrue( tool.Begin( erase: false ) );
		Assert.IsNull( tool.Press( 14, 26 ), "a guest's press writes nothing yet" );
		tool.SendGuestBatches( Time.Now );
		Assert.AreEqual( 1, guest.Level.OpenPoolRequests, "the press's cell went at once" );
		CollectionAssert.AreEqual( new[] { (14, 26) }, tool.GuestStrokeCells.ToArray(), "and is the stroke's ghost" );
		Assert.IsFalse( IsWater( guest, 14, 26 ), "nothing is painted before its record" );
		tool.Hover( 18, 26 );
		GuestFrames( 2, host, guest );
		tool.SendGuestBatches( Time.Now );
		Assert.AreEqual( 5, tool.GuestStrokeCells.Count(), "the dragged cells wait for the interval" );
		GuestFrames( 6, host, guest );
		tool.SendGuestBatches( Time.Now );
		tool.Hover( 25, 26 );
		Assert.IsNull( tool.Release() );
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the stroke came back" );
		var seat = guest.Level.Commands.Player;
		CollectionAssert.AreEqual( Row( 14, 25, 26 ), HostBatchCells( host, seat ), "every cell went once, in the order the host's tool writes them" );
		Assert.IsTrue( HostCommandsOf( host, seat ).OfType<PoolCellsCommand>().Count() is >= 3 and <= 4, "in a few batches" );
		foreach ( var (x, y) in Row( 14, 25, 26 ) )
			Assert.IsTrue( IsWater( host, x, y ) && IsWater( guest, x, y ), $"({x}, {y}) is water on both parks" );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CannotBuildHere ) + view.Says( OpenTpwText.CoopHostCouldNotTake ) + view.Says( OpenTpwText.CoopNotGranted ) );
		Assert.IsFalse( tool.Undo(), "a guest's strokes are the host's to undo" );
		Assert.AreEqual( 0, guest.Level.OpenPoolRequests );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsStrokeSaysItsFirstRefusalOnceAndTheRiverMarksNone()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		// a path across the stroke at x 17
		Assert.IsTrue( host.Issue( new LayLineCommand( CellToolMode.Path, -1, 17, 24, 17, 28 ) ).Accepted );
		Grant( host, guest, view );
		RoundsUntil( () => Walkable( guest, 17, 26 ), host, guest, view, "the host's path reached the guest" );
		guest.Enter();
		var tool = guest.Level.PoolTool!;
		Assert.IsTrue( tool.Begin( erase: false ) );
		tool.Press( 14, 26 );
		tool.Hover( 20, 26 );
		tool.Release();
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the stroke came back" );
		Assert.IsTrue( IsWater( guest, 16, 26 ) && !IsWater( guest, 17, 26 ) && IsWater( guest, 20, 26 ), "the cells around the path are painted" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CannotBuildHere ), "the path cell's refusal, said once" );
		// the river option over the painted water marks it: no refusal
		Assert.IsTrue( tool.Begin( erase: false, river: true ) );
		tool.Press( 14, 26 );
		tool.Hover( 16, 26 );
		tool.Release();
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the river stroke came back" );
		foreach ( var park in new[] { host, guest } )
			Assert.IsTrue( park.Level.Guests!.Grid.Cells.FlagsAt( 15, 26 ).HasFlag( ParkCellFlags.PoolRiver ) );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CannotBuildHere ), "no second refusal" );
		// an erase stroke over water and land: the land cells are its refusal
		Assert.IsTrue( tool.Begin( erase: true ) );
		tool.Press( 18, 26 );
		tool.Hover( 18, 29 );
		tool.Release();
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the erase stroke came back" );
		Assert.IsFalse( IsWater( host, 18, 26 ) || IsWater( guest, 18, 26 ) );
		Assert.AreEqual( 2, view.Says( OpenTpwText.CannotBuildHere ), "the land cells' refusal, said once" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void APoolBatchWritesWhatItsCellCommandsWriteOnTheSamePark()
	{
		var cells = new[] { (14, 26), (15, 26), (15, 26), (12, 1), (16, 26), (16, 27) };
		var each = NewPark();
		var batch = NewPark();
		var written = new List<PoolCellWrite>();
		foreach ( var (river, erase) in new[] { (false, false), (true, false), (false, true) } )
		{
			each.Enter();
			written.Clear();
			foreach ( var (x, y) in cells )
				written.Add( (PoolCellWrite)each.Issue( erase ? new ErasePoolCellCommand( x, y ) : new PaintPoolCellCommand( x, y, river ) ).Value! );
			batch.Enter();
			var result = batch.Issue( new PoolCellsCommand( erase, river, cells ) );
			CollectionAssert.AreEqual( written.ToArray(), (PoolCellWrite[])result.Value!, $"the writes, river {river}, erase {erase}" );
			Assert.AreEqual( written.Any( write => write.Result is PoolBuildResult.Ok or PoolBuildResult.RiverMarked ), result.Accepted );
			Assert.AreEqual( each.Level.ComputeStateHash(), batch.Level.ComputeStateHash(), "the same park" );
			Assert.AreEqual( each.Economy.Balance, batch.Economy.Balance, "the same money" );
		}
		Assert.IsTrue( written.Any( write => write.Result == PoolBuildResult.Ok ) && written.Any( write => write.Result == PoolBuildResult.NotPool ), "the erase met water and land" );
	}
	[TestMethod]
	public void AGuestsStrokesKeepAtMostFourPoolRequestsOpenAndOneQueueOfCells()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		var tool = guest.Level.PoolTool!;
		Assert.IsTrue( tool.Begin( erase: false ) );
		var waits = guest.Level.WaitingClicks;
		// the host takes nothing meanwhile: the requests stay open, and the drag goes back and forth over 24 cells a pass
		tool.Press( 4, 20 );
		tool.SendGuestBatches( Time.Now );
		for ( var pass = 0; pass < 24; pass++ )
		{
			tool.Hover( pass % 2 == 0 ? 28 : 4, 20 );
			GuestFrames( 13, host, guest, hostRuns: false );
			tool.SendGuestBatches( Time.Now );
			Assert.IsTrue( guest.Level.OpenPoolRequests <= PoolTool.MaximumOpenRequests );
		}
		tool.Release();
		Assert.AreEqual( PoolTool.MaximumOpenRequests, guest.Level.OpenPoolRequests, "four requests went" );
		Assert.AreEqual( waits + 1, guest.Level.WaitingClicks, "the full queue asked the player to wait, once" );
		var seat = guest.Level.Commands.Player;
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the strokes came back" );
		var batches = HostCommandsOf( host, seat ).OfType<PoolCellsCommand>().ToArray();
		Assert.IsTrue( batches.All( batch => batch.Cells.Count <= PoolCellsCommand.MaximumCells ) );
		Assert.AreEqual( PoolTool.MaximumUnsentCells, batches.Skip( PoolTool.MaximumOpenRequests ).Sum( batch => batch.Cells.Count ), "the queue's cells went after the first four" );
		Assert.IsTrue( Row( 4, 28, 20 ).All( cell => IsWater( host, cell.X, cell.Y ) && IsWater( guest, cell.X, cell.Y ) ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void EscapeDropsAGuestsUnsentCellsAndTheStrokeReportsNothing()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		var tool = guest.Level.PoolTool!;
		Assert.IsTrue( tool.Begin( erase: false ) );
		tool.Press( 4, 20 );
		tool.SendGuestBatches( Time.Now );
		tool.Hover( 12, 20 );
		Assert.AreEqual( 9, tool.GuestStrokeCells.Count() );
		tool.Cancel();
		Assert.IsFalse( tool.IsActive || tool.HasGuestBatches, "the unsent cells went, and the sent request is an orphan" );
		RoundsUntil( () => IsWater( guest, 4, 20 ), host, guest, view, "the sent cell came back" );
		Rounds( 30, host, guest );
		view.Update();
		Assert.IsFalse( IsWater( host, 5, 20 ) || IsWater( guest, 5, 20 ), "the cells that waited never went" );
		Assert.AreEqual( 1, HostCommandsOf( host, guest.Level.Commands.Player ).OfType<PoolCellsCommand>().Count() );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CannotBuildHere ) + view.Says( OpenTpwText.CoopHostCouldNotTake ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGrantTakenBackMidStrokeRefusesTheRestAndTheGuestIsToldOnce()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		var tool = guest.Level.PoolTool!;
		Assert.IsTrue( tool.Begin( erase: false ) );
		tool.Press( 4, 22 );
		tool.SendGuestBatches( Time.Now );
		GuestFramesUntil( () => IsWater( guest, 4, 22 ), host, guest, "the press's cell came back" );
		var seat = guest.Level.Commands.Player;
		// the HUD says a refusal of the rights at most once in its notice's time: well past the start
		Time.Update( ParkHud.SessionRefusalNoticeSeconds );
		Assert.IsTrue( host.Issue( new SetBuildGrantCommand( seat, false ) ).Accepted );
		tool.Hover( 10, 22 );
		tool.Release();
		RoundsUntil( () => !tool.HasGuestBatches && !guest.Level.HasBuildGrant( seat ), host, guest, view, "the stroke ended" );
		Rounds( 10, host, guest );
		view.Update();
		Assert.IsFalse( IsWater( host, 6, 22 ) || IsWater( guest, 6, 22 ), "nothing after the grant went" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotGranted ), "told once" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheSessionsEndEndsAGuestsPoolToolAndDropsItsStrokes()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		var tool = guest.Level.PoolTool!;
		Assert.IsTrue( tool.Begin( erase: false ) );
		tool.Press( 4, 24 );
		tool.SendGuestBatches( Time.Now );
		tool.Hover( 10, 24 );
		Assert.IsTrue( tool.HasGuestBatches );
		guest.Session!.End( "The guest left." );
		Assert.IsFalse( tool.IsActive || tool.HasGuestBatches );
		Assert.AreEqual( 0, tool.TakeGuestReports().Count );
	}

	/// <summary>A pool 3 cells wide (x 12 to 14, y 10 to 20) painted on both parks before their first frame, as a pool the host built would stand.</summary>
	private static void PoolOnBoth( Park host, Park guest, (int X, int Y) from, (int X, int Y) to )
	{
		foreach ( var park in new[] { host, guest } )
		{
			park.Enter();
			Assert.IsTrue( park.Level.PoolBuilder!.PaintCells( PoolBuilder.Rectangle( from, to ) ).Completed );
		}
	}

	[TestMethod]
	public void AGuestsBridgeAndItsRefusalAndBackspaceOverABridgeAndOverWater()
	{
		var (host, guest, _, _) = Shared();
		PoolOnBoth( host, guest, (12, 10), (14, 20) );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		var tool = guest.Level.PoolTool!;
		Assert.IsTrue( tool.BeginBridge( 1 ) );
		tool.Press( 10, 15 );
		tool.Release();
		RoundsUntil( () => !tool.HasGuestBatches && guest.Level.PoolBridges!.At( 12, 15 ) != null, host, guest, view, "the bridge came back" );
		Assert.IsNotNull( host.Level.PoolBridges!.At( 12, 15 ), "built on both parks" );
		// the same bridge again is refused, and says why once its outcome is back
		var again = guest.Level.PoolBridges!.Check( 10, 15, 1 );
		Assert.AreNotEqual( PoolBridgeResult.Ok, again );
		tool.Press( 10, 15 );
		tool.Release();
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the refused bridge came back" );
		view.Update();
		Assert.AreEqual( 1, view.Says( ParkHud.BridgeRefusalText( again ) ) );
		tool.Cancel();
		// Backspace with no tool on over the bridge, then over a water cell: requests of their own
		SetCursor( view.Hud, (12, 15) );
		Assert.IsTrue( view.Hud.BacktrackKey() );
		Assert.IsNotNull( guest.Level.WaitingBacktrack );
		RoundsUntil( () => guest.Level.WaitingBacktrack == null, host, guest, view, "the bridge's removal came back" );
		Assert.IsNull( host.Level.PoolBridges.At( 12, 15 ) ?? guest.Level.PoolBridges.At( 12, 15 ), "taken down on both parks" );
		SetCursor( view.Hud, (13, 11) );
		Assert.IsTrue( view.Hud.BacktrackKey() );
		RoundsUntil( () => guest.Level.WaitingBacktrack == null, host, guest, view, "the water's removal came back" );
		Assert.IsFalse( IsWater( host, 13, 11 ) || IsWater( guest, 13, 11 ) );
		CatchUpAndCompare( host, guest );
	}

	/// <summary>A synthetic slide tower: 2 x 2, no entrance, <c>Pools.SlideTower 1</c>, its stairs facing north at rotation 0 (as the slide level tests build it).</summary>
	private static ObjectCatalogEntry SlideTower( Park park ) =>
		new( "jungle", ObjectCategory.Feature, "/synthetic", new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 65100\nInfo.Name SyntheticTower65100\nInfo.OverwritePriority 5\nPools.SlideTower 1\n", "tower65100.sam" ) } ),
			new ObjectShape( new[] { "**", "**" } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(),
			park.One.Entry.FileSystem, null );

	/// <summary>The pool of 7 x 7 cells at x 12 to 18, y 12 to 18 and a slide tower at (13, 19) on both parks before their first frame; the guest's tower.</summary>
	private static OriginalObject TowerOnBoth( Park host, Park guest )
	{
		PoolOnBoth( host, guest, (12, 12), (18, 18) );
		OriginalObject? placed = null;
		foreach ( var park in new[] { host, guest } )
		{
			park.Enter();
			park.Level.Objects.Things ??= new ThingPool();
			placed = park.Level.Objects.TryPlace( SlideTower( park ), 13, 19, 0, out _ );
			Assert.IsNotNull( placed, "a tower beside the pool" );
			park.Level.Guests!.Grid.Cells.WriteFootprint( placed!.Cells );
		}
		return placed!;
	}

	private static PoolSlide? SlideOf( Park park ) => park.Level.PoolSlides!.Find( PoolSlide.KeyOf( 13, 19 ) );

	[TestMethod]
	public void AGuestsSlideIsLaidInBatchesAndItsSplashEndsTheSlideModeWithTheDoneMessage()
	{
		var (host, guest, _, _) = Shared();
		var tower = TowerOnBoth( host, guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		Assert.IsTrue( guest.Level.EnterSlideTool( tower ) );
		var tool = guest.Level.PoolTool!;
		tool.Press( 15, 19 );
		tool.Hover( 16, 19 );
		tool.Hover( 17, 19 );
		tool.Hover( 17, 18 );
		// a cell past the splash: the batch tries it no more, and it is no refusal
		tool.Hover( 16, 18 );
		tool.Release();
		Assert.IsTrue( tool.IsSlide, "the slide mode waits for its outcome" );
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the slide came back" );
		Assert.IsTrue( SlideOf( host ) is { IsComplete: true } && SlideOf( guest ) is { IsComplete: true }, "finished on both parks" );
		Assert.IsFalse( tool.IsActive, "the splash ended the slide mode" );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.PoolSlideDone ) );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CannotBuildHere ) + view.Says( OpenTpwText.PoolSlideNotNext ) );
		// a finished slide's last piece is the host's: a guest's undo of it is refused
		var rights = new CoopBuildRights( guest.Level );
		Assert.IsFalse( rights.Allows( guest.Level.Commands.Player, new UndoPoolSlidePieceCommand( 13, 19 ) ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsSlideUndoGoesBehindItsQueuedPiecesOneAtATime()
	{
		var (host, guest, _, _) = Shared();
		var tower = TowerOnBoth( host, guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		Assert.IsTrue( guest.Level.EnterSlideTool( tower ) );
		var tool = guest.Level.PoolTool!;
		tool.Press( 15, 19 );
		tool.SendGuestBatches( Time.Now );
		tool.Hover( 17, 19 );
		tool.Release();
		// at once, before the dragged pieces went: the undo waits behind them
		var waits = guest.Level.WaitingClicks;
		Assert.IsTrue( tool.Undo() );
		Assert.IsFalse( tool.Undo(), "a second undo while the first waits" );
		Assert.AreEqual( waits + 1, guest.Level.WaitingClicks );
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the pieces and the undo came back" );
		Assert.AreEqual( 2, SlideOf( host )!.Pieces.Count, "three laid, the last taken back" );
		Assert.AreEqual( 2, SlideOf( guest )!.Pieces.Count );
		Assert.IsTrue( tool.IsSlide, "the slide is not finished: the mode stays" );
		CatchUpAndCompare( host, guest );
	}
	[TestMethod]
	public void AGuestsSlideUndoWithNothingToTakeBackOrAfterItsSplashSaysNothing()
	{
		var (host, guest, _, _) = Shared();
		var tower = TowerOnBoth( host, guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		Assert.IsTrue( guest.Level.EnterSlideTool( tower ) );
		var tool = guest.Level.PoolTool!;
		var seat = guest.Level.Commands.Player;
		Assert.IsTrue( new CoopBuildRights( guest.Level ).Allows( seat, new UndoPoolSlidePieceCommand( 13, 19 ) ), "a slide with no piece is the park's to refuse" );
		// Backspace with no piece laid: the park refuses it quietly, as the host's Backspace
		Assert.IsTrue( tool.Undo() );
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the undo came back" );
		Assert.AreEqual( 1, HostCommandsOf( host, seat ).Count( command => command is UndoPoolSlidePieceCommand ), "it went to the host" );
		// the pieces to the splash, and an undo queued behind them before they went: the slide they finish meets it
		tool.Press( 15, 19 );
		tool.Hover( 16, 19 );
		tool.Hover( 17, 19 );
		tool.Hover( 17, 18 );
		tool.Release();
		Assert.IsTrue( tool.Undo(), "behind the pieces" );
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the pieces and the undo came back" );
		Assert.IsTrue( SlideOf( host ) is { IsComplete: true } && SlideOf( guest ) is { IsComplete: true }, "the undo took nothing back" );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.PoolSlideDone ) );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopHostOnly ) + view.Says( OpenTpwText.CoopNotGranted ) + view.Says( OpenTpwText.CoopHostCouldNotTake ), "nothing else is said" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsSlideUndoRefusedOnceTheGrantWentSaysSo()
	{
		var (host, guest, _, _) = Shared();
		var tower = TowerOnBoth( host, guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		Assert.IsTrue( guest.Level.EnterSlideTool( tower ) );
		var tool = guest.Level.PoolTool!;
		var seat = guest.Level.Commands.Player;
		tool.Press( 15, 19 );
		tool.Release();
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the piece came back" );
		Assert.AreEqual( 1, SlideOf( host )!.Pieces.Count );
		// the grant goes while the guest's Backspace is on its way: the host's rights refuse it, and its report says why (the slide is not finished, so it is no quiet undo)
		Assert.IsTrue( host.Issue( new SetBuildGrantCommand( seat, false ) ).Accepted );
		Assert.IsTrue( tool.Undo() );
		tool.SendGuestBatches( Time.Now );
		RoundsUntil( () => !tool.HasGuestBatches && !guest.Level.HasBuildGrant( seat ), host, guest, view, "the undo came back" );
		view.Update();
		Assert.AreEqual( 1, HostCommandsOf( host, seat ).Count( command => command is UndoPoolSlidePieceCommand ), "it went to the host" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotGranted ) );
		// once the guest's park knows, its Backspace is refused before it goes, and says so too
		Assert.IsTrue( tool.Undo() );
		tool.SendGuestBatches( Time.Now );
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the refused undo reported" );
		view.Update();
		Assert.AreEqual( 2, view.Says( OpenTpwText.CoopNotGranted ) );
		Assert.AreEqual( 1, HostCommandsOf( host, seat ).Count( command => command is UndoPoolSlidePieceCommand ), "the second went nowhere" );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopHostOnly ) );
		Assert.IsTrue( SlideOf( host )!.Pieces.Count == 1 && SlideOf( guest )!.Pieces.Count == 1, "nothing was taken back" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ASlideUndosNoteFollowsWhyItWasRefusedNotTheGrantAtItsReport()
	{
		var (host, guest, _, _) = Shared();
		var tower = TowerOnBoth( host, guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		Assert.IsTrue( guest.Level.EnterSlideTool( tower ) );
		var tool = guest.Level.PoolTool!;
		var seat = guest.Level.Commands.Player;
		var requests = guest.Level.CoopSession!.GuestCommands!;
		// sends the guest's batches and runs frames, with no HUD to take their outcomes, until every request was answered and the host took the slide undos
		void AnsweredWithoutTheHud( int undos )
		{
			for ( var round = 0; round < 600 && (requests.Open.Count > 0 || HostCommandsOf( host, seat ).Count( command => command is UndoPoolSlidePieceCommand ) < undos); round++ )
			{
				guest.Enter();
				tool.SendGuestBatches( Time.Now );
				GuestFrames( 1, host, guest );
			}
			Assert.AreEqual( 0, requests.Open.Count, "every request was answered" );
		}
		// a piece; then the host takes the grant back while the guest's undo is on its way, and gives it again before the guest's HUD takes the refusal: it was for want of the grant
		tool.Press( 15, 19 );
		tool.Release();
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the piece came back" );
		Assert.IsTrue( host.Issue( new SetBuildGrantCommand( seat, false ) ).Accepted );
		guest.Enter();
		Assert.IsTrue( tool.Undo() );
		AnsweredWithoutTheHud( 1 );
		Assert.IsTrue( host.Issue( new SetBuildGrantCommand( seat, true ) ).Accepted );
		GuestFramesUntil( () => guest.Level.HasBuildGrant( seat ), host, guest, "the grant is back on the guest's park" );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotGranted ), "said, the grant given back since or not" );
		Assert.AreEqual( 1, SlideOf( guest )!.Pieces.Count );
		// the pieces to the splash and an undo behind them, answered before the HUD takes them; the host takes the grant back before it does: the undo met a finished slide
		guest.Enter();
		tool.Press( 16, 19 );
		tool.Hover( 17, 19 );
		tool.Hover( 17, 18 );
		tool.Release();
		Assert.IsTrue( tool.Undo() );
		AnsweredWithoutTheHud( 2 );
		Assert.IsTrue( SlideOf( guest ) is { IsComplete: true } && SlideOf( host ) is { IsComplete: true }, "the undo took nothing back" );
		Assert.IsTrue( host.Issue( new SetBuildGrantCommand( seat, false ) ).Accepted );
		GuestFramesUntil( () => !guest.Level.HasBuildGrant( seat ), host, guest, "the revoke reached the guest" );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.PoolSlideDone ) );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotGranted ), "the finished slide's undo stays quiet, the grant taken back since or not" );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopHostOnly ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ABackspaceInTheSlideModeDuringADragEndsTheDragsStroke()
	{
		var (host, guest, _, _) = Shared();
		var tower = TowerOnBoth( host, guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		Assert.IsTrue( guest.Level.EnterSlideTool( tower ) );
		var tool = guest.Level.PoolTool!;
		tool.Press( 15, 19 );
		tool.Hover( 16, 19 );
		Assert.IsTrue( tool.Undo(), "Backspace during the drag" );
		Assert.IsNull( typeof( PoolTool ).GetField( "guestStroke", NonPublic )!.GetValue( tool ), "the drag's stroke was released: it reports once back" );
		tool.Hover( 17, 19 );
		tool.Release();
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the pieces and the undo came back" );
		Assert.AreEqual( 1, SlideOf( host )!.Pieces.Count, "two laid and the last taken back; the drag took no cell after the Backspace" );
		Assert.AreEqual( 1, SlideOf( guest )!.Pieces.Count );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AStrokeStopsAtTheFirstCellAFullQueueDrops()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		var tool = guest.Level.PoolTool!;
		Assert.IsTrue( tool.Begin( erase: false ) );
		var waits = guest.Level.WaitingClicks;
		// the host takes nothing meanwhile: back and forth until the queue is full
		tool.Press( 4, 20 );
		tool.SendGuestBatches( Time.Now );
		for ( var pass = 0; pass < 24; pass++ )
		{
			tool.Hover( pass % 2 == 0 ? 28 : 4, 20 );
			GuestFrames( 13, host, guest, hostRuns: false );
			tool.SendGuestBatches( Time.Now );
		}
		Assert.AreEqual( waits + 1, guest.Level.WaitingClicks, "the full queue asked the player to wait" );
		// the host takes what went and waits: the queue drains, and the stroke, stopped at its dropped cell, takes no cell more
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the queue drained" );
		tool.Hover( 4, 22 );
		tool.Hover( 28, 22 );
		Assert.IsFalse( tool.HasGuestBatches, "the stroke takes no more cells" );
		tool.Release();
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the stroke reported" );
		Assert.IsFalse( Row( 4, 28, 22 ).Any( cell => IsWater( host, cell.X, cell.Y ) || IsWater( guest, cell.X, cell.Y ) ), "no cell after the drop" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AStrokesBatchTheHostCouldNotTakeIsSaidOnceForTheStroke()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		var seat = guest.Level.Commands.Player;
		var tool = guest.Level.PoolTool!;
		Assert.IsTrue( tool.Begin( erase: false ) );
		// forty requests between two of the host's frames spend its burst: the stroke's batch after them is too fast, and the host keeps no record of it
		var requests = guest.Session!.GuestCommands!;
		var burst = Enumerable.Range( 0, 40 ).Select( _ => requests.Submit( new LayLineCommand( CellToolMode.Path, -1, 30, 30, 30, 30 ) { Player = seat } ) ).ToArray();
		tool.Press( 4, 26 );
		tool.SendGuestBatches( Time.Now );
		tool.Release();
		RoundsUntil( () => !tool.HasGuestBatches && burst.All( request => !request.IsOpen ), host, guest, view, "the batch was answered" );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopHostCouldNotTake ), "said once for the stroke" );
		Assert.IsFalse( IsWater( host, 4, 26 ) || IsWater( guest, 4, 26 ) );
		Assert.AreEqual( 0, HostCommandsOf( host, seat ).OfType<PoolCellsCommand>().Count(), "no record of it" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsStrokeGoesOnAcrossALostLinkAndEachCellAppliesOnce()
	{
		var (host, guest, _, guestLink) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		var tool = guest.Level.PoolTool!;
		Assert.IsTrue( tool.Begin( erase: false ) );
		tool.Press( 4, 28 );
		tool.SendGuestBatches( Time.Now );
		// the guest's link goes while the stroke sends: its requests go again on the resume
		guestLink.Drop();
		tool.Hover( 14, 28 );
		GuestFrames( 15, host, guest );
		tool.SendGuestBatches( Time.Now );
		tool.Release();
		GuestFrames( 30, host, guest );
		guestLink.Resume();
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the stroke came back after the resume" );
		var seat = guest.Level.Commands.Player;
		CollectionAssert.AreEqual( Row( 4, 14, 28 ), HostBatchCells( host, seat ), "each cell applied once" );
		Assert.IsTrue( Row( 4, 14, 28 ).All( cell => IsWater( host, cell.X, cell.Y ) && IsWater( guest, cell.X, cell.Y ) ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AnotherPlayersSplashOrATowerRemovedBeforeTheReportGivesTheGuestNoDoneMessage()
	{
		var (host, guest, _, _) = Shared();
		var tower = TowerOnBoth( host, guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		Assert.IsTrue( guest.Level.EnterSlideTool( tower ) );
		var tool = guest.Level.PoolTool!;
		// the guest lays the first piece; the host finishes the slide before the guest's next cells go
		tool.Press( 15, 19 );
		tool.SendGuestBatches( Time.Now );
		GuestFramesUntil( () => SlideOf( guest ) is { Pieces.Count: 1 }, host, guest, "the guest's first piece came back" );
		foreach ( var (x, y) in new[] { (16, 19), (17, 19), (17, 18) } )
			Assert.IsTrue( host.Issue( new LayPoolSlidePieceCommand( 13, 19, x, y ) ).Accepted );
		tool.Hover( 16, 19 );
		tool.Release();
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the guest's stroke came back" );
		view.Update();
		Assert.IsTrue( SlideOf( guest ) is { IsComplete: true } );
		Assert.AreEqual( 0, view.Says( OpenTpwText.PoolSlideDone ), "the host laid the splash" );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CannotBuildHere ), "the finished slide's batch is no refusal" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ASlideTowerRemovedBeforeTheStrokeReportsGivesNoDoneMessage()
	{
		var (host, guest, _, _) = Shared();
		var tower = TowerOnBoth( host, guest );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		Assert.IsTrue( guest.Level.EnterSlideTool( tower ) );
		var tool = guest.Level.PoolTool!;
		tool.Press( 15, 19 );
		tool.Hover( 16, 19 );
		tool.Hover( 17, 19 );
		tool.Hover( 17, 18 );
		tool.Release();
		tool.SendGuestBatches( Time.Now );
		GuestFrames( 15, host, guest );
		tool.SendGuestBatches( Time.Now );
		// the host demolishes the tower once the guest's splash applied, before the guest's HUD takes the outcome
		GuestFramesUntil( () => SlideOf( host ) is { IsComplete: true }, host, guest, "the guest's slide reached the host" );
		host.Enter();
		Assert.IsTrue( host.Issue( new RemoveAtCommand( 13, 19 ) ).Accepted );
		GuestFramesUntil( () => SlideOf( guest ) == null, host, guest, "the tower's removal reached the guest" );
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the stroke reported" );
		view.Update();
		Assert.AreEqual( 0, view.Says( OpenTpwText.PoolSlideDone ), "no slide stands to be done" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsSaleOfASlideTowerTakesItsSlideAndRefundsItOnBothParks()
	{
		var (host, guest, _, _) = Shared();
		var tower = TowerOnBoth( host, guest );
		foreach ( var park in new[] { host, guest } )
		{
			park.Enter();
			foreach ( var (x, y) in new[] { (15, 19), (16, 19), (17, 19), (17, 18) } )
				Assert.AreEqual( PoolSlideResult.Ok, park.Level.PoolSlides!.AddCell( 13, 19, x, y ) );
		}
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		long Refunds( Park park ) => park.Economy.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherIncome );
		var before = (Refunds( host ), Refunds( guest ));
		guest.Enter();
		var rights = new CoopBuildRights( guest.Level );
		Assert.IsTrue( rights.Allows( guest.Level.Commands.Player, new RemoveAttractionCommand( tower.Runtime.AttractionId ) ), "a slide tower is a guest's to sell" );
		Assert.IsTrue( rights.AllowsRemoval( guest.Level.Commands.Player, tower ), "and its area clear takes it" );
		var sale = guest.Level.Commands.Issue( new RemoveAttractionCommand( tower.Runtime.AttractionId ) );
		Assert.IsTrue( sale.IsPending );
		RoundsUntil( () => SlideOf( guest ) == null && SlideOf( host ) == null, host, guest, view, "the sale took the slide on both parks" );
		Assert.AreEqual( Refunds( host ) - before.Item1, Refunds( guest ) - before.Item2, "the same refund on both parks" );
		CatchUpAndCompare( host, guest );
	}
	[TestMethod]
	public void AGrantTakenBackBeforeABatchGoesStopsTheStrokeAndItsReportSaysSoOnce()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		var seat = guest.Level.Commands.Player;
		var tool = guest.Level.PoolTool!;
		Assert.IsTrue( tool.Begin( erase: false ) );
		tool.Press( 4, 22 );
		tool.SendGuestBatches( Time.Now );
		GuestFramesUntil( () => IsWater( guest, 4, 22 ), host, guest, "the press's cell came back" );
		// the grant goes and reaches the guest's park before the stroke's next batch: the funnel refuses it there
		Assert.IsTrue( host.Issue( new SetBuildGrantCommand( seat, false ) ).Accepted );
		GuestFramesUntil( () => !guest.Level.HasBuildGrant( seat ), host, guest, "the revoke reached the guest" );
		var refusals = guest.Level.SessionRefusals;
		tool.Hover( 8, 22 );
		GuestFrames( 15, host, guest );
		tool.SendGuestBatches( Time.Now );
		// (the press's batch stays the stroke's until the HUD takes its outcome)
		Assert.IsFalse( tool.GuestStrokeCells.Any( cell => cell.X > 4 ), "the refused batch went nowhere" );
		tool.Hover( 12, 22 );
		Assert.IsFalse( tool.GuestStrokeCells.Any( cell => cell.X > 4 ), "and the stroke takes no more cells" );
		tool.Release();
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the stroke reported" );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotGranted ), "its report says why, once" );
		Assert.AreEqual( refusals, guest.Level.SessionRefusals, "the pool tool's refusal is its report's, not the session's note" );
		// a pool command the tool does not send keeps the session's note
		guest.Enter();
		Assert.IsTrue( guest.Level.Commands.Issue( new PaintPoolCellCommand( 20, 20, false ) ).RightsRefused );
		Assert.AreEqual( refusals + 1, guest.Level.SessionRefusals );
		Assert.IsFalse( IsWater( host, 6, 22 ) || IsWater( guest, 6, 22 ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AStrokesFirstRefusalIsTheFirstInDragOrderWhenALaterBatchIsAnsweredFirst()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		var seat = guest.Level.Commands.Player;
		var tool = guest.Level.PoolTool!;
		Assert.IsTrue( tool.Begin( erase: false ) );
		// the press is on a path cell (refused at its record); with it, forty requests spend the host's burst, so the next batch is too fast and answered without a record
		tool.Press( 6, 1 );
		tool.SendGuestBatches( Time.Now );
		var requests = guest.Session!.GuestCommands!;
		var first = requests.LastSubmitted!;
		var burst = Enumerable.Range( 0, 39 ).Select( _ => requests.Submit( new LayLineCommand( CellToolMode.Path, -1, 30, 30, 30, 30 ) { Player = seat } ) ).ToArray();
		tool.Hover( 6, 4 );
		GuestFrames( 1, host, guest, hostRuns: false );
		tool.SendGuestBatches( Time.Now + 1 );
		var later = requests.LastSubmitted!;
		Assert.IsInstanceOfType( later.Command, typeof( PoolCellsCommand ) );
		tool.Release();
		GuestFramesUntil( () => !first.IsOpen && !later.IsOpen && burst.All( request => !request.IsOpen ), host, guest, "every request resolved" );
		Assert.AreEqual( CoopGuestRequestState.Applied, first.State );
		Assert.AreEqual( CoopGuestRequestState.Refused, later.State, "the later batch had no record" );
		// the host's answer to the later batch resolved it before the earlier batch's record applied, and the tool takes them in that order: the stroke waits for its earlier batch, and its
		// report still names the first refusal in drag order
		var taken = requests.TakeCompleted().ToList();
		Assert.IsTrue( taken.IndexOf( later ) >= 0 && taken.IndexOf( later ) < taken.IndexOf( first ), "the answer came before the record" );
		Assert.IsTrue( tool.CompleteGuestRequest( later ) );
		Assert.AreEqual( 0, tool.TakeGuestReports().Count, "the stroke waits for its earlier batch" );
		Assert.IsTrue( tool.CompleteGuestRequest( first ) );
		var report = tool.TakeGuestReports().Single();
		Assert.AreEqual( ((6, 1), PoolBuildResult.Occupied), report.Refusal, "the path cell under the press" );
		Assert.IsTrue( report.NotTaken );
		Assert.IsFalse( report.Wrote );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsBridgeMayNotTouchAClaimedQueuesJoinNorItsBackspaceTakeOneThatDoes()
	{
		var park = NewPark();
		park.Enter();
		var level = park.Level;
		// One's queue from (10, 6) up to its back cell (10, 2), joined to the path at (10, 1)
		ConfigureQueue( level.Guests!.Grid, park.One, 10, 5 );
		var rights = new CoopBuildRights( level );
		level.SetBuildGrant( 1, true );
		// a bridge east from (11, 1): its outside end before it is (10, 1)
		var bridge = new PoolBridge( 11, 1, 1, 0 );
		Assert.AreEqual( (10, 1), bridge.Cell( -1 ) );
		typeof( PoolBridgeWorld ).GetMethod( "Insert", NonPublic )!.Invoke( level.PoolBridges, new object[] { bridge } );
		Assert.AreEqual( Level.BacktrackTarget.PoolBridge, Level.BacktrackTargetAt( level.Paths!.Cells, 13, 1, level.PoolBridges ) );
		MarkStation( park, park.One );
		try
		{
			Assert.IsFalse( rights.Allows( 1, new PlacePoolBridgeCommand( 11, 1, 1 ) ), "a bridge with an end on the path cell the claimed queue joins" );
			Assert.IsFalse( rights.Allows( 1, new BacktrackCommand( 13, 1 ) ), "nor Backspace over one standing so" );
			Assert.IsTrue( rights.Allows( 1, new PlacePoolBridgeCommand( 20, 24, 1 ) ), "a bridge away from it" );
			Assert.IsTrue( rights.Allows( ParkCommands.LocalPlayer, new BacktrackCommand( 13, 1 ) ) );
		}
		finally
		{
			MarkStation( park, park.One, station: false );
		}
		Assert.IsTrue( rights.Allows( 1, new PlacePoolBridgeCommand( 11, 1, 1 ) ) && rights.Allows( 1, new BacktrackCommand( 13, 1 ) ), "an ordinary ride's join" );
	}
}
