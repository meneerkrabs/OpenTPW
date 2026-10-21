using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] The coaster tool's writes as commands (docs/COOP.md, "Tools"), without game data: a whole session of clicks, Backspace and the panel's drags and cut leaves the tool as
/// commands, and the commands alone build the same coaster (its nodes, flags, controller and paid cost) on a twin. The commands keep the panel's rules: a drag's steps stay within the range its press
/// gave, one drag runs on a coaster at a time (a press releases the player's own drag still held and waits for another player's), a click that places a pylon, Backspace and the cut release the
/// running drag first, a stack swap and a click on the chain start (the close and the completion) wait for it, and the loop button's gate holds.
/// </summary>
[TestClass]
public class CoasterCommandTests
{
	private const int First = CoasterTrack.StationNodeCount;

	[TestInitialize]
	public void Init() => Log = new();

	private sealed class Rig
	{
		public Rig( Dictionary<string, string>? settings = null )
		{
			Ride = new CoasterRide( CoasterDefinition.FromValues( settings ?? CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Test Coaster" );
			Builder = new CoasterBuilder( Ride, new CoasterSite( 64, 64 ) );
			CoasterTests.PlaceStation( Builder, (20, 20) );
		}

		public CoasterRide Ride { get; }
		public CoasterBuilder Builder { get; }
		public CoasterEdits Edits { get; } = new();
		public List<ParkCommand> Log { get; } = new();

		/// <summary>The issuer's funnel: each command is logged and applied to this rig's builder, as a level applies a panel command.</summary>
		public ParkCommandResult Issue( ParkCommand command )
		{
			Log.Add( command );
			return CoasterPanelCommand.ApplyTo( command, Builder, Edits );
		}

		/// <summary>A click of the build buttons, as <see cref="Level.CoasterClick"/> issues it: with the panel's height and loop edit mode, which the builder then takes as the placement left it.</summary>
		public CoasterClickDone Click( int x, int y, int player = 0 )
		{
			var state = Builder.ToolState;
			var result = Issue( new CoasterClickCommand( 0, x, y, state.Height, state.LoopMode, state.LoopPylonsLeft ) { Player = player } );
			if ( result.Value is not CoasterClickDone done )
				return new CoasterClickDone( CoasterPlaceResult.NotEditable, false, result.Reason ?? "", state );
			Builder.SetToolState( done.Next );
			return done;
		}

		/// <summary>The rig's coaster with its whole circuit, closed and completed.</summary>
		public void BuildCircuit()
		{
			Builder.Height = 40;
			foreach ( var (dx, dy) in CoasterTests.Circuit )
				Click( 20 + dx, 20 + dy );
			var back = Builder.Track.ReturnCell!.Value;
			Click( back.X, back.Y );
			Click( back.X, back.Y );
			Assert.IsTrue( Builder.Track.IsComplete );
		}

		public ulong Hash()
		{
			var hash = new StateHasher();
			Ride.AddCanonicalState( hash );
			hash.Add( Ride.PaidCost );
			return hash.Value;
		}

		/// <summary>The drags of players 0 to 2 as text that compares by value (the builder each holds is this rig's).</summary>
		public string Drags() => string.Join( "; ", Enumerable.Range( 0, 3 ).Select( player =>
			Edits.Of( player ) is { } held ? $"{player}: {held.Station} {held.Edit} {held.Drag} {held.Range}" : $"{player}: none" ) );
	}

	[TestMethod]
	public void ACoasterToolSessionsCommandsBuildTheSameCoasterOnATwin()
	{
		var rig = new Rig();
		var panel = new CoasterToolPanel( rig.Builder, null, 0, rig.Issue );
		rig.Builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit.Take( 3 ) )
			Assert.AreEqual( CoasterPlaceResult.Ok, rig.Click( 20 + dx, 20 + dy ).Result );
		Assert.IsTrue( rig.Issue( new RemoveLastCoasterPylonCommand( 0 ) ).Accepted, "Backspace takes the last pylon back" );
		foreach ( var (dx, dy) in CoasterTests.Circuit.Skip( 2 ) )
			Assert.AreEqual( CoasterPlaceResult.Ok, rig.Click( 20 + dx, 20 + dy ).Result );
		var back = rig.Builder.Track.ReturnCell!.Value;
		Assert.IsTrue( rig.Click( back.X, back.Y ) is { Result: CoasterPlaceResult.Ok, Completed: false }, "the chain start closes the circuit" );
		Assert.IsTrue( rig.Click( back.X, back.Y ) is { Result: CoasterPlaceResult.Ok, Completed: true }, "the next click completes it" );
		Assert.IsTrue( rig.Builder.Track.IsComplete );

		// the panel on the finished track: a height drag, a move, a rotation, then a cut
		panel.Open();
		Assert.IsTrue( panel.Select( CoasterToolButton.Height ) );
		var loft = rig.Builder.Track.Nodes[First].T;
		var paid = rig.Ride.PaidCost;
		Assert.IsTrue( panel.Press( First, new NVector2( 0, 300 ), 24, 20 ) );
		panel.Drag( new NVector2( 0, 260 ), 0, null );
		panel.Drag( new NVector2( 0, 240 ), 0, null );
		Assert.AreEqual( CoasterPanelResult.Edited, panel.Release( null, null ) );
		Assert.AreNotEqual( loft, rig.Builder.Track.Nodes[First].T, "the height drag raised the pylon" );
		Assert.AreNotEqual( paid, rig.Ride.PaidCost, "its release settled the money" );
		Assert.IsTrue( panel.Select( CoasterToolButton.Move ) );
		var moved = rig.Builder.Track.Nodes[First + 1];
		Assert.IsTrue( panel.Press( First + 1, new NVector2( 100, 100 ), moved.X, moved.Y ) );
		panel.Drag( new NVector2( 110, 100 ), 0, (moved.X + 1, moved.Y) );
		Assert.AreEqual( CoasterPanelResult.Edited, panel.Release( First + 1, (moved.X + 1, moved.Y) ) );
		Assert.AreEqual( moved.X + 1, rig.Builder.Track.Nodes[First + 1].X, "the move dropped the pylon on the next cell" );
		Assert.IsTrue( panel.Select( CoasterToolButton.Rotate ) );
		var yaw = rig.Builder.Track.Nodes[First + 2].Yaw;
		Assert.IsTrue( panel.Press( First + 2, new NVector2( 100, 100 ), 0, 0 ), "the standard pylon takes the rotate drag" );
		panel.Drag( new NVector2( 90, 100 ), 0, null );
		Assert.AreEqual( CoasterPanelResult.Edited, panel.Release( null, null ) );
		Assert.AreNotEqual( yaw, rig.Builder.Track.Nodes[First + 2].Yaw, "the rotate drag turned the pylon" );
		Assert.IsTrue( panel.Select( CoasterToolButton.Demolish ) );
		Assert.AreEqual( CoasterPanelResult.CutStarted, panel.Release( First + 1, null ) );
		Assert.AreNotEqual( CoasterPanelResult.Nothing, panel.Release( First + 3, null ), "the second release cuts the range out" );
		Assert.IsFalse( rig.Builder.Track.IsComplete && !rig.Builder.Track.IsCut, "the circuit is open again" );

		var kinds = rig.Log.Select( command => command.Kind ).Distinct().ToArray();
		CollectionAssert.IsSubsetOf( new[] { ParkCommandKind.CoasterClick, ParkCommandKind.RemoveLastCoasterPylon, ParkCommandKind.BeginCoasterNodeEdit, ParkCommandKind.EditCoasterNode,
			ParkCommandKind.EndCoasterNodeEdit, ParkCommandKind.CutCoaster }, kinds );

		// the twin's builder keeps its own tool state: its panel's, never the issuer's
		var twin = new Rig();
		var twinState = twin.Builder.ToolState;
		foreach ( var command in rig.Log )
			twin.Issue( command );
		Assert.AreEqual( twinState, twin.Builder.ToolState, "a peer's clicks leave this machine's height and loop mode as they were" );
		Assert.AreEqual( rig.Hash(), twin.Hash(), "the commands alone build the same coaster, pylons, controller and paid cost" );
		Assert.AreEqual( rig.Ride.PaidCost, twin.Ride.PaidCost );
		Assert.IsNull( twin.Edits.Of( ParkCommands.LocalPlayer ), "every drag ended" );
	}

	[TestMethod]
	public void ADragCommandNeedsTheIssuersOwnEditAndThePylonsControl()
	{
		var rig = new Rig();
		rig.Builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			rig.Click( 20 + dx, 20 + dy );
		var before = rig.Hash();
		Assert.IsFalse( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Loft, 0.5f, 0, 0 ) ).Accepted, "no edit was begun" );
		Assert.IsFalse( rig.Issue( new EndCoasterNodeEditCommand( 0 ) ).Accepted );
		Assert.IsTrue( rig.Issue( new BeginCoasterNodeEditCommand( 0, First, CoasterDrag.Height ) ).Accepted );
		Assert.IsFalse( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Loft, 0.5f, 0, 0 ) { Player = 1 } ).Accepted, "another player's drag holds no edit" );
		Assert.IsFalse( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.LoopRotate, 0.5f, 0, 0 ) ).Accepted, "a height drag sets no loop rotation" );
		Assert.IsFalse( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Yaw, 0.5f, 0, 0 ) ).Accepted, "nor a yaw" );
		Assert.IsFalse( rig.Issue( new BeginCoasterNodeEditCommand( 0, 0, CoasterDrag.Height ) ).Accepted, "a station node cannot be edited" );
		Assert.IsFalse( rig.Issue( new BeginCoasterNodeEditCommand( 0, First, CoasterDrag.LoopTilt ) ).Accepted, "a standard pylon takes no loop tilt" );
		Assert.AreEqual( before, rig.Hash(), "the refusals changed nothing" );
		Assert.IsFalse( rig.Issue( new CoasterClickCommand( 0, 70, 70, 40, false, 0 ) ).Accepted, "a click off the site places nothing" );
		Assert.AreEqual( before, rig.Hash() );
	}

	/// <summary>A drag's step keeps to the range its press gave, as the panel clamps it: a loft far above the range (a pylon kilometres high) is refused and changes nothing.</summary>
	[TestMethod]
	public void ADragStepOutsideItsPresssRangeIsRefused()
	{
		var rig = new Rig();
		rig.BuildCircuit();
		var range = rig.Builder.DragRange( First, CoasterDrag.Height )!.Value;
		Assert.IsTrue( rig.Issue( new BeginCoasterNodeEditCommand( 0, First, CoasterDrag.Height ) ).Accepted );
		var before = rig.Hash();
		foreach ( var loft in new[] { 1000f, 1e9f, range.High + 0.01f, range.Low - 0.01f } )
			Assert.IsFalse( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Loft, loft, 0, 0 ) ).Accepted, $"loft {loft} lies outside [{range.Low}, {range.High}]" );
		Assert.AreEqual( before, rig.Hash(), "the refused steps changed nothing" );
		Assert.IsTrue( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Loft, range.High, 0, 0 ) ).Accepted, "the range's end is the panel's clamp" );
		Assert.IsTrue( rig.Issue( new EndCoasterNodeEditCommand( 0 ) ).Accepted );

		var yaw = rig.Builder.DragRange( First + 1, CoasterDrag.Rotate )!.Value;
		Assert.IsTrue( rig.Issue( new BeginCoasterNodeEditCommand( 0, First + 1, CoasterDrag.Rotate ) ).Accepted );
		Assert.IsFalse( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Yaw, yaw.High + 1, 0, 0 ) ).Accepted, "past the automatic yaw plus the deviance" );
		Assert.IsFalse( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Loft, 0.1f, 0, 0 ) ).Accepted, "a rotate drag sets no loft" );
		Assert.IsTrue( rig.Issue( new EndCoasterNodeEditCommand( 0 ) ).Accepted );

		Assert.IsTrue( rig.Issue( new BeginCoasterNodeEditCommand( 0, First + 2, CoasterDrag.Tilt ) ).Accepted );
		Assert.IsFalse( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Bank, CoasterBuilder.TiltLimit + 1, 0, 0 ) ).Accepted, "past ±3.1416" );
		Assert.IsTrue( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Bank, 0.5f, 0, 0 ) ).Accepted );
		Assert.IsTrue( rig.Issue( new EndCoasterNodeEditCommand( 0 ) ).Accepted );
	}

	/// <summary>
	/// A press while the player still holds a drag releases that drag first, as its release would: a height drag that was never released is paid for, so two presses raise no pylon for free.
	/// </summary>
	[TestMethod]
	public void APressSettlesTheDragThePlayerStillHolds()
	{
		Rig Raised( bool release )
		{
			var rig = new Rig();
			rig.BuildCircuit();
			var high = rig.Builder.DragRange( First, CoasterDrag.Height )!.Value.High;
			Assert.IsTrue( rig.Issue( new BeginCoasterNodeEditCommand( 0, First, CoasterDrag.Height ) ).Accepted );
			Assert.IsTrue( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Loft, high, 0, 0 ) ).Accepted );
			if ( release )
				Assert.IsTrue( rig.Issue( new EndCoasterNodeEditCommand( 0 ) ).Accepted );
			Assert.IsTrue( rig.Issue( new BeginCoasterNodeEditCommand( 0, First + 1, CoasterDrag.Rotate ) ).Accepted );
			Assert.IsTrue( rig.Issue( new EndCoasterNodeEditCommand( 0 ) ).Accepted );
			return rig;
		}
		var released = Raised( release: true );
		var pressedAgain = Raised( release: false );
		Assert.AreEqual( released.Ride.PaidCost, pressedAgain.Ride.PaidCost, "the second press settled the raise as its release would have" );
		Assert.AreEqual( released.Hash(), pressedAgain.Hash() );
	}

	/// <summary>
	/// A command that changes the pylon list (Backspace, a cut, a placing click) releases the drag running on the coaster first: a moved pylon goes back to its cell and no drag settles against another
	/// pylon.
	/// </summary>
	[TestMethod]
	public void BackspaceReleasesAnotherPlayersDragFirst()
	{
		var rig = new Rig();
		rig.Builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit.Take( 4 ) )
			Assert.AreEqual( CoasterPlaceResult.Ok, rig.Click( 20 + dx, 20 + dy ).Result );
		var picked = rig.Builder.Track.Nodes[First + 1];
		var paid = rig.Ride.PaidCost;
		Assert.IsTrue( rig.Issue( new BeginCoasterNodeEditCommand( 0, First + 1, CoasterDrag.Move ) { Player = 1 } ).Accepted );
		Assert.IsTrue( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Move, 0, picked.X + 1, picked.Y ) { Player = 1 } ).Accepted );
		Assert.AreEqual( picked.X + 1, rig.Builder.Track.Nodes[First + 1].X, "player 1 carries the pylon" );
		var count = rig.Builder.Track.Nodes.Count;
		Assert.IsTrue( rig.Issue( new RemoveLastCoasterPylonCommand( 0 ) ).Accepted, "player 0's Backspace" );
		Assert.AreEqual( count - 1, rig.Builder.Track.Nodes.Count );
		Assert.IsNull( rig.Edits.Of( 1 ), "player 1's drag was released" );
		Assert.AreEqual( (picked.X, picked.Y), (rig.Builder.Track.Nodes[First + 1].X, rig.Builder.Track.Nodes[First + 1].Y), "the carried pylon went back to its cell" );
		Assert.AreEqual( paid, rig.Ride.PaidCost, "a move released over nothing settles nothing, and Backspace refunds nothing" );
		Assert.IsFalse( rig.Issue( new EndCoasterNodeEditCommand( 0 ) { Player = 1 } ).Accepted, "player 1's late release finds no drag" );
	}

	/// <summary>
	/// The click keeps the loop button's gate: the loop mode needs the loop button. The click handler tests nothing else of the panel, so a click with the build button's state while a loop pair waits
	/// for its second pylon places a pylon as in a game of one.
	/// </summary>
	[TestMethod]
	public void AClickKeepsTheLoopButtonsGate()
	{
		var settings = CoasterTests.PcSettings();
		foreach ( var key in settings.Keys.Where( key => key.StartsWith( "asPylonControls[1].", StringComparison.Ordinal ) ).ToArray() )
			settings.Remove( key );
		var plain = new Rig( settings );
		Assert.IsFalse( plain.Builder.LoopUnlocked );
		var before = plain.Hash();
		Assert.IsFalse( plain.Issue( new CoasterClickCommand( 0, 24, 20, 40, true, 2 ) ).Accepted, "no loop pylon, no loop mode" );
		Assert.AreEqual( before, plain.Hash() );

		var rig = new Rig();
		Assert.IsTrue( rig.Builder.LoopUnlocked );
		rig.Builder.Height = 40;
		Assert.AreEqual( CoasterPlaceResult.Ok, rig.Click( 24, 20 ).Result );
		rig.Builder.LoopMode = true;
		var first = rig.Click( 28, 20 );
		Assert.AreEqual( 1, first.Next.LoopPylonsLeft, "the pair's first pylon leaves one to place" );
		Assert.IsTrue( rig.Builder.LoopPairPending );
		var count = rig.Builder.Track.Nodes.Count;
		var twin = new Rig();
		foreach ( var command in rig.Log )
			twin.Issue( command );
		Assert.IsTrue( twin.Issue( new CoasterClickCommand( 0, 32, 20, 40, false, 0 ) ).Accepted, "a build click while the pair waits places a pylon" );
		Assert.AreEqual( count + 1, twin.Builder.Track.Nodes.Count );
		Assert.IsTrue( rig.Click( 32, 20 ).Next is { LoopMode: false, LoopPylonsLeft: 0 }, "the second pylon ends the loop mode" );
		Assert.IsFalse( rig.Builder.LoopPairPending );
	}

	/// <summary>
	/// A click that places a pylon releases the drag running on the coaster first, as its own release would: a pylon placed in a cut circuit's gap moves the pylons after it, and another player's height
	/// drag on one of those is settled before, so the coaster and the money come out as if the drag had ended before the click; a carried pylon goes back to its cell first.
	/// </summary>
	[TestMethod]
	public void APlacingClickReleasesTheRunningDragFirst()
	{
		Rig Session( bool endFirst )
		{
			var rig = new Rig();
			rig.BuildCircuit();
			var gone = rig.Builder.Track.Nodes[First + 2];
			Assert.AreEqual( CoasterTrack.CutResult.Cut, rig.Issue( new CutCoasterCommand( 0, First + 1, First + 3 ) ).Value );
			var dragged = First + 2;
			var high = rig.Builder.DragRange( dragged, CoasterDrag.Height )!.Value.High;
			Assert.IsTrue( rig.Issue( new BeginCoasterNodeEditCommand( 0, dragged, CoasterDrag.Height ) { Player = 1 } ).Accepted );
			Assert.IsTrue( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Loft, high, 0, 0 ) { Player = 1 } ).Accepted );
			if ( endFirst )
				Assert.IsTrue( rig.Issue( new EndCoasterNodeEditCommand( 0 ) { Player = 1 } ).Accepted );
			Assert.AreEqual( CoasterPlaceResult.Ok, rig.Click( gone.X, gone.Y ).Result, "player 0 builds into the gap" );
			Assert.IsNull( rig.Edits.Of( 1 ), "player 1's drag ended" );
			Assert.IsFalse( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Loft, high, 0, 0 ) { Player = 1 } ).Accepted, "a step after the click finds no drag" );
			return rig;
		}
		var ended = Session( endFirst: true );
		var released = Session( endFirst: false );
		Assert.AreEqual( ended.Ride.PaidCost, released.Ride.PaidCost, "the click settled the raise as the drag's release would have" );
		Assert.AreEqual( ended.Hash(), released.Hash() );

		var carried = new Rig();
		carried.Builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit.Take( 3 ) )
			Assert.AreEqual( CoasterPlaceResult.Ok, carried.Click( 20 + dx, 20 + dy ).Result );
		var last = carried.Builder.Track.Nodes[^1];
		var paid = carried.Ride.PaidCost;
		Assert.IsTrue( carried.Issue( new BeginCoasterNodeEditCommand( 0, carried.Builder.Track.Nodes.Count - 1, CoasterDrag.Move ) { Player = 1 } ).Accepted );
		Assert.IsTrue( carried.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Move, 0, last.X + 1, last.Y ) { Player = 1 } ).Accepted );
		var (nx, ny) = CoasterTests.Circuit[3];
		carried.Click( 20 + nx, 20 + ny );
		Assert.IsNull( carried.Edits.Of( 1 ) );
		Assert.AreEqual( (last.X, last.Y), (carried.Builder.Track.Nodes[^2].X, carried.Builder.Track.Nodes[^2].Y), "the carried pylon went back before the new one linked to it" );
		var twin = new Rig();
		twin.Builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit.Take( 4 ) )
			twin.Click( 20 + dx, 20 + dy );
		Assert.AreEqual( twin.Hash(), carried.Hash(), "the same coaster as if the pylon had never been carried" );
		Assert.AreNotEqual( paid, carried.Ride.PaidCost );
		Assert.AreEqual( twin.Ride.PaidCost, carried.Ride.PaidCost );
	}

	/// <summary>
	/// One drag runs on a coaster at a time, as one pointer gives in a game of one: another player's press waits while a drag runs (the player's own second press releases the first), and so do a stack
	/// swap (the release could change the stack it was asked on), the close (it checks the last pylon, which a drag can hold) and the completion (it ends the tool every drag needs). A command that
	/// waits changes nothing.
	/// </summary>
	[TestMethod]
	public void APressASwapTheCloseAndTheCompletionWaitForTheRunningDrag()
	{
		var rig = new Rig();
		rig.Builder.Height = 20;
		foreach ( var (x, y) in new[] { (24, 20), (28, 24), (24, 28), (20, 24) } )
			Assert.AreEqual( CoasterPlaceResult.Ok, rig.Click( x, y ).Result );
		rig.Builder.Height = 60;
		Assert.AreEqual( CoasterPlaceResult.Ok, rig.Click( 24, 20 ).Result );
		var (below, above) = (First, First + 4);
		var track = rig.Builder.Track;
		Assert.AreEqual( (0, 1), (track.Nodes[below].StackIndex, track.Nodes[above].StackIndex) );
		Assert.IsTrue( rig.Issue( new BeginCoasterNodeEditCommand( 0, First + 2, CoasterDrag.Rotate ) { Player = 1 } ).Accepted );
		var before = rig.Hash();
		Assert.IsFalse( rig.Issue( new BeginCoasterNodeEditCommand( 0, First + 1, CoasterDrag.Height ) { Player = 2 } ).Accepted, "player 1's drag runs" );
		Assert.IsFalse( rig.Issue( new SwapCoasterStackCommand( 0, above, false ) ).Accepted, "the swap waits too" );
		Assert.AreEqual( before, rig.Hash() );
		Assert.AreEqual( First + 2, rig.Edits.Of( 1 )!.Edit.Index, "the refusals released nobody" );
		Assert.IsNull( rig.Edits.Of( 2 ) );
		Assert.IsTrue( rig.Issue( new BeginCoasterNodeEditCommand( 0, First + 1, CoasterDrag.Rotate ) { Player = 1 } ).Accepted, "player 1's own press releases its drag first" );
		Assert.IsTrue( rig.Issue( new EndCoasterNodeEditCommand( 0 ) { Player = 1 } ).Accepted );
		Assert.IsTrue( rig.Issue( new SwapCoasterStackCommand( 0, above, false ) ).Accepted, "with no drag running the pieces swap" );
		Assert.AreEqual( (1, 0), (track.Nodes[below].StackIndex, track.Nodes[above].StackIndex) );

		var closed = new Rig();
		closed.Builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			closed.Click( 20 + dx, 20 + dy );
		var back = closed.Builder.Track.ReturnCell!.Value;
		Assert.IsTrue( closed.Issue( new BeginCoasterNodeEditCommand( 0, First + 5, CoasterDrag.Move ) { Player = 1 } ).Accepted, "player 1 picks up the last pylon" );
		var open = closed.Hash();
		Assert.IsTrue( closed.Click( back.X, back.Y ) is { Result: not CoasterPlaceResult.Ok, Completed: false }, "the close waits for player 1's drag" );
		Assert.IsFalse( closed.Builder.Track.IsClosed );
		Assert.AreEqual( open, closed.Hash() );
		Assert.IsTrue( closed.Issue( new EndCoasterNodeEditCommand( 0 ) { Player = 1 } ).Accepted );
		Assert.IsTrue( closed.Click( back.X, back.Y ) is { Result: CoasterPlaceResult.Ok, Completed: false } );
		Assert.IsTrue( closed.Builder.Track.IsClosed );
		Assert.IsTrue( closed.Issue( new BeginCoasterNodeEditCommand( 0, First, CoasterDrag.Height ) { Player = 1 } ).Accepted );
		var shut = closed.Hash();
		Assert.IsTrue( closed.Click( back.X, back.Y ) is { Completed: false }, "the completion waits for player 1's drag" );
		Assert.AreEqual( shut, closed.Hash() );
		Assert.IsTrue( closed.Issue( new EndCoasterNodeEditCommand( 0 ) { Player = 1 } ).Accepted );
		Assert.IsTrue( closed.Click( back.X, back.Y ) is { Result: CoasterPlaceResult.Ok, Completed: true } );
	}

	/// <summary>
	/// Random panel commands from three players on a coaster in each state the tool reaches (an open chain, a closed circuit, a finished one, a cut one): none throws, a refused one changes neither the
	/// coaster nor the drags, and a twin that applies each command in step answers it the same way and keeps the same coaster, money and drags after every command.
	/// </summary>
	[TestMethod]
	public void RandomPanelCommandsFromThreePlayersReplayOnATwin()
	{
		var accepted = new Dictionary<ParkCommandKind, int>();
		foreach ( var (state, seed) in new[] { ("open", 1), ("closed", 2), ("finished", 3), ("cut", 4) } )
		{
			var rig = new Rig();
			rig.Builder.Height = 40;
			switch ( state )
			{
				case "open":
					foreach ( var (dx, dy) in CoasterTests.Circuit.Take( 3 ) )
						rig.Click( 20 + dx, 20 + dy );
					break;
				case "closed":
					foreach ( var (dx, dy) in CoasterTests.Circuit )
						rig.Click( 20 + dx, 20 + dy );
					rig.Click( rig.Builder.Track.ReturnCell!.Value.X, rig.Builder.Track.ReturnCell!.Value.Y );
					Assert.IsTrue( rig.Builder.Track.IsClosed && !rig.Builder.Track.IsComplete );
					break;
				default:
					rig.BuildCircuit();
					if ( state == "cut" )
						Assert.IsTrue( rig.Issue( new CutCoasterCommand( 0, First + 1, First + 3 ) ).Accepted );
					break;
			}
			var twin = new Rig();
			foreach ( var command in rig.Log )
				twin.Issue( command );
			var random = new Random( 20261021 + seed );
			var gap = new List<(int X, int Y)>();
			for ( var step = 0; step < 600; step++ )
			{
				var command = RandomPanelCommand( random, rig, gap );
				var cells = rig.Builder.Track.Nodes.Select( node => (node.X, node.Y) ).ToList();
				var before = (rig.Hash(), rig.Drags());
				var result = rig.Issue( command );
				if ( result.Accepted )
				{
					accepted[command.Kind] = accepted.GetValueOrDefault( command.Kind ) + 1;
					if ( command is CutCoasterCommand )
						gap = cells.Except( rig.Builder.Track.Nodes.Select( node => (node.X, node.Y) ) ).ToList();
				}
				else
					Assert.AreEqual( before, (rig.Hash(), rig.Drags()), $"{state} step {step}: the refused {command} ({result.Reason}) changed nothing" );
				Assert.AreEqual( result.Accepted, twin.Issue( command ).Accepted, $"{state} step {step}: the twin answers {command} the same way" );
				Assert.AreEqual( (rig.Hash(), rig.Drags()), (twin.Hash(), twin.Drags()), $"{state} step {step}: the same coaster, money and drags on the twin after {command}" );
			}
		}
		var kinds = string.Join( ", ", accepted.OrderBy( pair => pair.Key ).Select( pair => $"{pair.Key} {pair.Value}" ) );
		foreach ( var kind in new[] { ParkCommandKind.CoasterClick, ParkCommandKind.RemoveLastCoasterPylon, ParkCommandKind.BeginCoasterNodeEdit, ParkCommandKind.EditCoasterNode,
			ParkCommandKind.EndCoasterNodeEdit, ParkCommandKind.SwapCoasterStack, ParkCommandKind.CutCoaster } )
			Assert.IsTrue( accepted.ContainsKey( kind ), $"the fuzz never ran a {kind} ({kinds})" );
	}

	/// <summary>
	/// One panel command of the fuzz, from one of three players: mostly ones that can do something on the coaster as it stands (its next pylon, its pylons, a running drag's range from the player who
	/// holds it), some malformed.
	/// </summary>
	private static ParkCommand RandomPanelCommand( Random random, Rig rig, List<(int X, int Y)> gap )
	{
		var track = rig.Builder.Track;
		var count = track.Nodes.Count;
		int Node() => random.Next( -1, count + 1 );
		int Pylon() => random.Next( 3 ) == 0 ? Node() : random.Next( CoasterTrack.StationNodeCount, Math.Max( CoasterTrack.StationNodeCount + 1, count ) );
		int AnyPlayer() => random.Next( 3 );
		// the player who holds the running drag, mostly: its steps and its release
		int Holder() => Enumerable.Range( 0, 3 ).FirstOrDefault( player => rig.Edits.Of( player ) != null, -1 ) is var holder and >= 0 && random.Next( 4 ) > 0 ? holder : AnyPlayer();
		(int X, int Y) NearNode()
		{
			var node = track.Nodes[random.Next( count )];
			return (Math.Max( 0, node.X + random.Next( -2, 3 ) ), Math.Max( 0, node.Y + random.Next( -2, 3 ) ));
		}
		var roll = random.Next( 100 );
		if ( roll < 25 )
		{
			// the circuit's next pylon, a pylon in a cut's gap, or the chain start that closes and completes the circuit
			var placed = count - CoasterTrack.StationNodeCount;
			var (x, y) = !track.IsComplete && !track.IsClosed && placed < CoasterTests.Circuit.Length ? (20 + CoasterTests.Circuit[placed].X, 20 + CoasterTests.Circuit[placed].Y)
				: track.IsCut && gap.Count > 0 && random.Next( 3 ) > 0 ? gap[random.Next( gap.Count )]
				: track.CutAfter is int cut && cut + 1 < count ? (track.Nodes[cut + 1].X, track.Nodes[cut + 1].Y)
				: track.ReturnCell ?? (20, 20);
			return new CoasterClickCommand( 0, x, y, 40, false, 0 ) { Player = AnyPlayer() };
		}
		if ( roll < 30 )
		{
			// a pylon on another pylon's cell (a stack) or anywhere, maybe in loop mode
			var loop = random.Next( 4 ) == 0;
			var (x, y) = random.Next( 2 ) == 0 ? NearNode() : (6 + random.Next( 30 ), 6 + random.Next( 30 ));
			return new CoasterClickCommand( 0, x, y, 10 + random.Next( 80 ), loop, loop ? random.Next( 3 ) : 0 ) { Player = AnyPlayer() };
		}
		if ( roll < 45 )
			return new BeginCoasterNodeEditCommand( 0, Pylon(), (CoasterDrag)random.Next( 6 ) ) { Player = AnyPlayer() };
		if ( roll < 70 )
		{
			var player = Holder();
			var held = rig.Edits.Of( player );
			var field = (CoasterNodeField)random.Next( 7 );
			var value = (float)(random.NextDouble() * 4 - 2);
			if ( held != null && random.Next( 4 ) > 0 )
			{
				field = held.Drag switch
				{
					CoasterDrag.Move => CoasterNodeField.Move,
					CoasterDrag.Height => CoasterNodeField.Loft,
					CoasterDrag.Rotate => CoasterNodeField.Yaw,
					CoasterDrag.LoopTilt => CoasterNodeField.LoopRotate,
					_ => random.Next( 2 ) == 0 ? CoasterNodeField.Bank : CoasterNodeField.Incline
				};
				var (low, high) = field == CoasterNodeField.Incline ? (held.Range.LowSecond, held.Range.HighSecond) : (held.Range.Low, held.Range.High);
				value = random.Next( 5 ) switch { 0 => low, 1 => high, 2 => low + (high - low) * (float)random.NextDouble(), 3 => high + 1, _ => low - 1 };
			}
			var (x, y) = held != null && held.Edit.Index < count ? (track.Nodes[held.Edit.Index].X + random.Next( -2, 3 ), track.Nodes[held.Edit.Index].Y + random.Next( -2, 3 )) : NearNode();
			return new EditCoasterNodeCommand( 0, field, value, Math.Max( 0, x ), Math.Max( 0, y ) ) { Player = player };
		}
		if ( roll < 80 )
			return new EndCoasterNodeEditCommand( 0 ) { Player = Holder() };
		if ( roll < 86 )
			return new RemoveLastCoasterPylonCommand( 0 ) { Player = AnyPlayer() };
		if ( roll < 93 )
			return new SwapCoasterStackCommand( 0, Pylon(), random.Next( 2 ) == 0 ) { Player = AnyPlayer() };
		return new CutCoasterCommand( 0, Pylon(), Pylon() ) { Player = AnyPlayer() };
	}

	/// <summary>
	/// A loft drag on the lower piece of a stack whose top stands above the coaster's highest pylon has a range whose ends are the wrong way round; the panel's clamp then gives one of the ends, and the
	/// command takes it as the panel sends it.
	/// </summary>
	[TestMethod]
	public void ALoftDragTakesTheEndsOfARangeTheWrongWayRound()
	{
		var rig = new Rig();
		rig.Builder.Height = 20;
		foreach ( var (x, y) in new[] { (24, 20), (28, 24), (24, 28), (20, 24) } )
			Assert.AreEqual( CoasterPlaceResult.Ok, rig.Click( x, y ).Result );
		rig.Builder.Height = 1000;
		Assert.AreEqual( CoasterPlaceResult.Ok, rig.Click( 24, 20 ).Result );
		var range = rig.Builder.DragRange( First, CoasterDrag.Height )!.Value;
		Assert.IsTrue( range.High < range.Low, $"the lower piece's range [{range.Low}, {range.High}] is the wrong way round" );
		Assert.IsTrue( rig.Issue( new BeginCoasterNodeEditCommand( 0, First, CoasterDrag.Height ) ).Accepted );
		Assert.IsTrue( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Loft, range.Low, 0, 0 ) ).Accepted, "the clamp's low end" );
		Assert.IsTrue( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Loft, range.High, 0, 0 ) ).Accepted, "the clamp's high end" );
		Assert.IsFalse( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Loft, (range.Low + range.High) / 2, 0, 0 ) ).Accepted, "between the ends the clamp gives nothing" );
		Assert.IsTrue( rig.Issue( new EndCoasterNodeEditCommand( 0 ) ).Accepted );
	}
}
