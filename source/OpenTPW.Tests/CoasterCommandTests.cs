using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] The coaster tool's writes as commands (docs/COOP.md, "Tools"), without game data: a whole session of clicks, Backspace and the panel's drags and cut leaves the tool as
/// commands, and the commands alone build the same coaster (its nodes, flags, controller and paid cost) on a twin. The commands keep the panel's rules: a drag's steps stay within the range its press
/// gave, a press releases the player's drag still held, a change of the pylon list releases every drag first, and the loop button's gate holds.
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
	/// A command that removes or reorders pylons (Backspace, a cut, a stack swap) releases every drag on the coaster first: a moved pylon goes back to its cell and nobody's drag settles against another pylon.
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

	/// <summary>The click keeps the panel's buttons: the loop mode needs the loop button, and while a loop pair waits for its second pylon only that pylon may follow.</summary>
	[TestMethod]
	public void AClickKeepsTheLoopButtonsRules()
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
		var pending = rig.Hash();
		Assert.IsFalse( rig.Issue( new CoasterClickCommand( 0, 32, 20, 40, false, 0 ) ).Accepted, "the build button is off while the pair waits" );
		Assert.IsFalse( rig.Issue( new CoasterClickCommand( 0, 32, 20, 40, true, 2 ) ).Accepted, "a new pair cannot start over a waiting one" );
		Assert.AreEqual( pending, rig.Hash() );
		Assert.IsTrue( rig.Click( 32, 20 ).Next is { LoopMode: false, LoopPylonsLeft: 0 }, "the second pylon ends the loop mode" );
		Assert.IsFalse( rig.Builder.LoopPairPending );
	}
}
