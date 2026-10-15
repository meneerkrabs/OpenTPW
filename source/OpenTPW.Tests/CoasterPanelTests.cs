using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// The coaster tool's panel (COAST-028): the nine buttons, their enable mask, the drags of the height, rotate and tilt buttons, the move button, the stack swaps and the demolish button's cut,
/// which reopens a finished circuit so it can be extended again, without game data (tp.exe <c>0x4982a0</c>, <c>0x446fa0</c>, <c>0x445910</c>, <c>0x446300</c>, <c>0x446740</c>).
/// </summary>
[TestClass]
public class CoasterPanelTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private const int First = CoasterTrack.StationNodeCount;

	[TestInitialize]
	public void Init() => Log = new();

	private static (CoasterBuilder Builder, CoasterRide Ride) NewCoaster( Dictionary<string, string>? settings = null )
	{
		var ride = new CoasterRide( CoasterDefinition.FromValues( settings ?? CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, (20, 20) );
		return (builder, ride);
	}

	private static (CoasterBuilder Builder, CoasterRide Ride) Finished()
	{
		var (builder, ride) = NewCoaster();
		builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + dx, 20 + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		return (builder, ride);
	}

	// ---- The buttons --------------------------------------------------------------------------------------

	[TestMethod]
	public void TheMaskEnablesBuildOnAnUnfinishedTrackAndDemolishOnAFinishedOne()
	{
		var (builder, ride) = NewCoaster();
		var panel = new CoasterToolPanel( builder );
		Assert.AreEqual( CoasterToolButton.Build, panel.Selected, "0x4455c0: an unfinished track opens on the build button" );
		const CoasterToolButton Edits = CoasterToolButton.Move | CoasterToolButton.Tilt | CoasterToolButton.Height | CoasterToolButton.Rotate | CoasterToolButton.StackUp | CoasterToolButton.StackDown;
		Assert.AreEqual( Edits | CoasterToolButton.Build | CoasterToolButton.Loop, panel.EnabledMask, "0x1fc plus build and loop; demolish is off while the circuit is not finished" );

		var (finished, _) = Finished();
		var done = new CoasterToolPanel( finished );
		Assert.AreEqual( CoasterToolButton.Height, done.Selected, "a finished circuit opens on the height button" );
		Assert.AreEqual( Edits | CoasterToolButton.Demolish, done.EnabledMask, "a complete track clears the two build buttons and keeps the demolish button" );
		Assert.IsFalse( done.Select( CoasterToolButton.Build ), "a disabled button does nothing" );
		Assert.AreEqual( CoasterToolButton.Height, done.Selected );
		Assert.IsTrue( done.Select( CoasterToolButton.Demolish ) );
	}

	[TestMethod]
	public void TheLoopButtonStartsAPairAndNeedsTheSecondPylonBeforeBuildComesBack()
	{
		var (builder, _) = NewCoaster();
		var panel = new CoasterToolPanel( builder );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		Assert.IsTrue( panel.Select( CoasterToolButton.Loop ) );
		Assert.IsTrue( builder.LoopMode, "0x442cd0: the pair counter is 2" );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 26 ) );
		Assert.IsTrue( panel.PairPending );
		Assert.IsFalse( panel.IsEnabled( CoasterToolButton.Build ), "0x445910: the first pylon of a pair is placed, only the loop button builds" );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20, 29 ) );
		Assert.IsFalse( builder.LoopMode, "the second pylon ends the pair" );
		Assert.IsTrue( panel.IsEnabled( CoasterToolButton.Build ) );
	}

	// ---- Drags --------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheHeightDragFollowsTheMouseAtHalfAPercentPerPixelWithinItsRangeAndSettlesTheMoneyOnRelease()
	{
		var (builder, ride) = NewCoaster();
		builder.Height = 40;
		builder.Place( 24, 20 );
		builder.Place( 28, 24 );
		var panel = new CoasterToolPanel( builder );
		panel.Select( CoasterToolButton.Height );
		var node = First;
		var start = builder.Track.Nodes[node].T;
		var (low, high) = builder.Track.LoftRange( builder.Site, node );
		Assert.IsTrue( low <= start && start <= high );
		Assert.IsTrue( panel.Press( node, new NVector2( 500, 400 ), 24, 20 ) );
		Assert.IsTrue( panel.IsDragging );

		panel.Drag( new NVector2( 500, 380 ), 0, null );
		Assert.AreEqual( Math.Clamp( start + 20 * CoasterToolPanel.HeightRate, low, high ), builder.Track.Nodes[node].T, 1e-5f, "dragging up 20 px adds 20 × 0.005 to the loft parameter" );
		panel.Drag( new NVector2( 500, 400 ), 0, null );
		Assert.AreEqual( start, builder.Track.Nodes[node].T, 1e-5f, "back at the press position: the start value" );

		// A drag far beyond the range stops at its end, and the origin follows: coming back by a few pixels changes the value at once.
		panel.Drag( new NVector2( 500, 400 - 10000 ), 0, null );
		Assert.AreEqual( high, builder.Track.Nodes[node].T, 1e-5f );
		panel.Drag( new NVector2( 500, 400 - 10000 + 10 ), 0, null );
		Assert.AreEqual( high - 10 * CoasterToolPanel.HeightRate, builder.Track.Nodes[node].T, 1e-5f, "a clamped value moves the origin (0x4434d5)" );
		Assert.AreEqual( CoasterPanelResult.Edited, panel.Release( node, (24, 20) ) );
		Assert.IsFalse( panel.IsDragging );
	}

	[TestMethod]
	public void TheHeightReleaseRefundsTheOldCostAndChargesTheNewOne()
	{
		var economy = EconomyTestData.Park( initialCash: 50000 );
		var ride = new CoasterRide( CoasterDefinition.FromValues( CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ), () => economy );
		CoasterTests.PlaceStation( builder, (20, 20) );
		builder.Height = 20;
		builder.Place( 24, 20 );
		var panel = new CoasterToolPanel( builder );
		panel.Select( CoasterToolButton.Height );
		var balance = economy.Balance;
		var oldCost = CoasterTrack.Truncate( builder.Track.NodeCost( First, false ) );
		Assert.IsTrue( panel.Press( First, new NVector2( 100, 100 ), 24, 20 ) );
		panel.Drag( new NVector2( 100, 40 ), 0, null );
		Assert.IsTrue( panel.DragCost > 0, "a higher pylon costs more: the HUD shows the difference" );
		Assert.AreEqual( balance, economy.Balance, "nothing moves while the drag runs" );
		var newCost = CoasterTrack.RoundUp( builder.Track.NodeCost( First, false ) );
		panel.Release( First, (24, 20) );
		Assert.AreEqual( balance + oldCost - newCost, economy.Balance, "0x441a00: credit trunc(old cost), debit ceil(new cost)" );
	}

	[TestMethod]
	public void TheRotateDragTurnsThePylonWithinTheDeviationAndSetsFlag1()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		builder.Place( 24, 20 );
		builder.Place( 28, 24 );
		builder.Place( 24, 28 );
		var track = builder.Track;
		var panel = new CoasterToolPanel( builder );
		panel.Select( CoasterToolButton.Rotate );
		var automatic = track.AutomaticYaw( builder.Site, First + 1 );
		var range = track.Definition.StandardPylon.MaxRotationDeviance;
		Assert.IsTrue( panel.Press( First + 1, new NVector2( 300, 300 ), 28, 24 ) );
		panel.Drag( new NVector2( 290, 300 ), 0, null );
		var node = track.Nodes[First + 1];
		Assert.AreEqual( CoasterNodeFlags.ManualYaw, node.Flags & CoasterNodeFlags.ManualYaw, "0x441e80 sets node flag 1" );
		Assert.AreEqual( automatic + 10 * CoasterToolPanel.AngleRate, node.Yaw, 1e-4f, "10 px to the left add 10 × 0.02 radians (start − mouse)" );
		var knots = track.BuildKnots( builder.Site );
		Assert.AreEqual( node.Yaw, MathF.Atan2( knots[First + 1].Direction.X, knots[First + 1].Direction.Z ), 1e-4f, "the knot direction takes the hand-set yaw" );
		panel.Drag( new NVector2( 300 - 5000, 300 ), 0, null );
		Assert.AreEqual( automatic + range, track.Nodes[First + 1].Yaw, 1e-4f, "stops at the automatic yaw plus fMaxRotnAngleDeviance" );
		var cost = builder.BuildCost;
		panel.Release( First + 1, (28, 24) );
		Assert.AreEqual( cost, builder.BuildCost, "rotating moves no money" );
	}

	[TestMethod]
	public void TheTiltDragSetsTheBankAndTheInclineAndStopsTheAutoBank()
	{
		var (builder, _) = Finished();
		var track = builder.Track;
		var panel = new CoasterToolPanel( builder );
		panel.Select( CoasterToolButton.Tilt );
		var index = First + 2;
		var auto = track.Nodes[index].Bank;
		Assert.IsTrue( panel.Press( index, new NVector2( 400, 400 ), 0, 0 ) );
		// The camera looks along the pylon's yaw: the horizontal drag banks and the vertical one inclines.
		var yaw = track.AutomaticYaw( builder.Site, index );
		panel.Drag( new NVector2( 390, 390 ), yaw, null );
		var node = track.Nodes[index];
		Assert.AreEqual( CoasterNodeFlags.NoAutoBank | CoasterNodeFlags.ManualIncline, node.Flags & (CoasterNodeFlags.NoAutoBank | CoasterNodeFlags.ManualIncline), "flags 4 and 2" );
		Assert.AreEqual( auto + 10 * CoasterToolPanel.AngleRate, node.Bank, 1e-4f, "the drag counts from the bank the node has" );
		Assert.AreEqual( 10 * CoasterToolPanel.AngleRate, node.Incline, 1e-4f );
		var knots = track.BuildKnots( builder.Site );
		Assert.AreEqual( node.Bank, knots[index].Roll, 1e-4f, "a node with flag 4 keeps its hand-set bank through the auto-bank pass" );
		panel.Drag( new NVector2( 400 - 10000, 400 - 10000 ), yaw, null );
		Assert.AreEqual( CoasterToolPanel.TiltLimit, track.Nodes[index].Bank, 1e-4f );
		Assert.AreEqual( CoasterToolPanel.TiltLimit, track.Nodes[index].Incline, 1e-4f, "±π" );
		panel.Release( index, null );
	}

	[TestMethod]
	public void TheLoopTiltDragMovesTheLoopRotationWithinItsRange()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		builder.Place( 24, 20 );
		builder.LoopMode = true;
		builder.Place( 24, 26 );
		builder.Place( 20, 29 );
		var track = builder.Track;
		var panel = new CoasterToolPanel( builder );
		panel.Select( CoasterToolButton.Tilt );
		Assert.AreEqual( 0f, track.Nodes[First + 1].LoopRotate, "the first pylon of a pair starts at 0" );
		Assert.IsTrue( panel.Press( First + 1, new NVector2( 100, 100 ), 24, 26 ) );
		panel.Drag( new NVector2( 100 - 10000, 100 ), 0, null );
		Assert.AreEqual( 0.35f, track.Nodes[First + 1].LoopRotate, 1e-5f, "the first of a pair stops at 0.35" );
		panel.Release( First + 1, null );
		Assert.IsTrue( panel.Press( First + 2, new NVector2( 100, 100 ), 20, 29 ) );
		panel.Drag( new NVector2( 100 + 10000, 100 ), 0, null );
		Assert.AreEqual( 0.15f, track.Nodes[First + 2].LoopRotate, 1e-5f, "any other loop pylon stops at 0.15" );
		panel.Drag( new NVector2( 100 - 10000, 100 ), 0, null );
		Assert.AreEqual( 0.85f, track.Nodes[First + 2].LoopRotate, 1e-5f );
	}

	[TestMethod]
	public void ThePanelRefusesWhatThePylonModelCannotDo()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		builder.Place( 24, 20 );
		builder.Track.Definition.StandardPylon.Capabilities = CoasterPylonCapabilities.Loft;
		var panel = new CoasterToolPanel( builder );
		panel.Select( CoasterToolButton.Rotate );
		Assert.IsFalse( panel.Press( First, NVector2.Zero, 24, 20 ), "no rotate clip, no rotate drag" );
		panel.Select( CoasterToolButton.Tilt );
		Assert.IsFalse( panel.Press( First, NVector2.Zero, 24, 20 ), "neither incline nor bank clip" );
		panel.Select( CoasterToolButton.Height );
		Assert.IsTrue( panel.Press( First, NVector2.Zero, 24, 20 ) );
		panel.Release( First, null );
		for ( var station = 0; station < First; station++ )
			Assert.IsFalse( panel.Press( station, NVector2.Zero, 0, 0 ), "the station's nodes cannot be edited" );
	}

	[TestMethod]
	public void TheMoveDragFollowsTheCellAndDropsOrReturns()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		builder.Place( 24, 20 );
		builder.Place( 28, 24 );
		builder.Place( 24, 28 );
		var panel = new CoasterToolPanel( builder );
		panel.Select( CoasterToolButton.Move );
		Assert.IsTrue( panel.Press( First + 1, new NVector2( 50, 50 ), 28, 24 ) );
		panel.Drag( new NVector2( 60, 60 ), 0, (29, 25) );
		Assert.AreEqual( (29, 25), (builder.Track.Nodes[First + 1].X, builder.Track.Nodes[First + 1].Y), "the node follows the hovered cell" );
		Assert.AreEqual( CoasterPanelResult.Edited, panel.Release( First + 1, (29, 25) ) );
		Assert.AreEqual( (29, 25), (builder.Track.Nodes[First + 1].X, builder.Track.Nodes[First + 1].Y) );

		Assert.IsTrue( panel.Press( First + 1, new NVector2( 50, 50 ), 29, 25 ) );
		panel.Drag( new NVector2( 70, 70 ), 0, (30, 26) );
		panel.Release( First + 1, null );
		Assert.AreEqual( (29, 25), (builder.Track.Nodes[First + 1].X, builder.Track.Nodes[First + 1].Y), "released over no cell: the node goes back" );
	}

	// ---- Stacks -------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheStackButtonsSwapAPieceWithItsNeighbourInTheCellStack()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 20;
		foreach ( var (x, y) in new[] { (24, 20), (28, 24), (24, 28), (20, 24) } )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( x, y ) );
		builder.Height = 60;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		var track = builder.Track;
		var below = First;
		var above = First + 4;
		Assert.AreEqual( (0, 1), (track.Nodes[below].StackIndex, track.Nodes[above].StackIndex) );
		var (belowT, aboveT) = (track.Nodes[below].T, track.Nodes[above].T);
		var panel = new CoasterToolPanel( builder );
		panel.Select( CoasterToolButton.StackDown );
		Assert.AreEqual( CoasterPanelResult.Nothing, panel.Release( below, null ), "the lowest piece cannot go down" );
		Assert.AreEqual( CoasterPanelResult.Swapped, panel.Release( above, null ) );
		Assert.AreEqual( (1, 0), (track.Nodes[below].StackIndex, track.Nodes[above].StackIndex), "the two exchange their stack index" );
		Assert.AreEqual( (aboveT, belowT), (track.Nodes[below].T, track.Nodes[above].T), "and their loft parameters" );
		Assert.AreEqual( (First, First + 4), (Array.FindIndex( track.Nodes.ToArray(), node => ReferenceEquals( node, track.Nodes[below] ) ), Array.FindIndex( track.Nodes.ToArray(), node => ReferenceEquals( node, track.Nodes[above] ) )), "the chain order does not change" );
		panel.Select( CoasterToolButton.StackUp );
		Assert.AreEqual( CoasterPanelResult.Nothing, panel.Release( below, null ), "the piece now on top cannot go up" );
		Assert.AreEqual( CoasterPanelResult.Swapped, panel.Release( above, null ) );
		Assert.AreEqual( (0, 1), (track.Nodes[below].StackIndex, track.Nodes[above].StackIndex) );
	}

	// ---- Demolish: the cut --------------------------------------------------------------------------------

	[TestMethod]
	public void ACutToTheEndOfAFinishedCircuitReopensItSoItCanBeExtendedAndCompletedAgain()
	{
		var (builder, ride) = Finished();
		// the ride runs with guests when the tool opens
		ride.Command( 8, 0 );
		ride.Command( 5, 0 );
		ride.Command( 6, 6 );
		var boarded = new List<int>();
		for ( var guest = 101; guest < 107; guest++ )
		{
			ride.Board( guest );
			boarded.Add( guest );
		}
		for ( var tick = 0; tick < 240 * 60 && ride.RidersOnBoard == 0; tick++ )
			ride.Advance( Tick );
		Assert.IsTrue( ride.RidersOnBoard > 0, "a train seated guests" );

		ride.EnterTool();
		Assert.AreEqual( CoasterControllerState.Built, ride.State );
		var panel = new CoasterToolPanel( builder );
		Assert.AreEqual( CoasterToolButton.Height, panel.Selected );
		var track = builder.Track;
		var cut = First + 2;
		var removed = CoasterTests.Circuit.Skip( 3 ).ToArray();
		Assert.IsTrue( panel.Select( CoasterToolButton.Demolish ) );
		Assert.AreEqual( CoasterPanelResult.CutStarted, panel.Release( cut, null ), "the first release over a pylon starts the range" );
		Assert.AreEqual( First + 2, panel.CutStart );
		Assert.AreEqual( CoasterPanelResult.Reopened, panel.Release( 0, null ), "the second over S0 (ordinal 0 stands for the last node) cuts to the end" );

		Assert.AreEqual( CoasterControllerState.Incomplete, ride.State, "0x446740 sets state 1: the trains are removed" );
		Assert.AreEqual( 0, ride.Trains.Count );
		Assert.IsFalse( track.IsComplete || track.IsClosed || track.IsCut );
		Assert.AreEqual( cut + 1, track.Nodes.Count, "the pylons after the cut and the station end node are gone" );
		Assert.AreEqual( CoasterToolButton.Build, panel.Selected, "the cut reselects the build button" );
		Assert.IsTrue( panel.IsEnabled( CoasterToolButton.Build ) && !panel.IsEnabled( CoasterToolButton.Demolish ) );

		// Extend again with the pylons the cut removed, close, complete.
		builder.Height = 40;
		foreach ( var (dx, dy) in removed )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + dx, 20 + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		Assert.IsTrue( track.IsRunnable && ride.IsOperational );
		Assert.AreEqual( CoasterControllerState.Built, ride.State, "completion sets state 2" );
		ride.Command( 5, 0 );
		Assert.AreEqual( CoasterControllerState.Open, ride.State );

		// Nobody is stranded: every guest the ride took leaves through COAST 3 exactly once.
		var released = new List<int>();
		for ( var guest = ride.Command( 3, 0 ); guest != 0; guest = ride.Command( 3, 0 ) )
			released.Add( guest );
		CollectionAssert.AreEquivalent( boarded.ToArray(), released.ToArray(), "boarded == released" );
	}

	[TestMethod]
	public void ACutInTheMiddleOpensAGapThatNewPylonsFillAndTheCircuitClosesOnTheChainStartAgain()
	{
		var (builder, ride) = Finished();
		ride.EnterTool();
		var track = builder.Track;
		var before = track.Nodes.Select( node => (node.X, node.Y) ).ToArray();
		var panel = new CoasterToolPanel( builder );
		Assert.IsTrue( panel.Select( CoasterToolButton.Demolish ) );
		// Cut from the second pylon to the fifth: the third and fourth go.
		panel.Release( First + 1, null );
		Assert.AreEqual( CoasterPanelResult.Cut, panel.Release( First + 4, null ) );
		Assert.AreEqual( First + 1, track.CutAfter );
		Assert.AreEqual( before.Length - 2, track.Nodes.Count );
		Assert.IsFalse( track.IsRunnable );
		Assert.AreEqual( CoasterControllerState.Incomplete, ride.State );
		Assert.IsTrue( (track.Nodes[First + 1].Flags & CoasterNodeFlags.CutEnd) != 0 && (track.Nodes[First + 2].Flags & CoasterNodeFlags.CutStart) != 0 );
		Assert.IsTrue( track.IsChainStart( before[First + 4].X, before[First + 4].Y ), "the node after the cut is the chain start now" );
		Assert.IsFalse( track.IsChainStart( before[0].X, before[0].Y ), "and S0 is not" );
		Assert.AreEqual( CoasterToolButton.Build, panel.Selected );

		// The knots do not run through the gap.
		var knots = track.BuildKnots( builder.Site );
		var gapDirectionBefore = knots[First + 1].Direction;
		Assert.AreEqual( SVector3.Normalize( knots[First + 1].Position - knots[First].Position ), gapDirectionBefore, "the node before the cut has no next neighbour: it leaves away from the previous one" );

		// Fill the gap with the cells the cut removed, then close on the chain start.
		builder.Height = 40;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( before[First + 2].X, before[First + 2].Y ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( before[First + 3].X, before[First + 3].Y ) );
		Assert.AreEqual( First + 3, track.CutAfter, "new pylons go into the gap" );
		Assert.IsNotNull( track.BuildPreviewChain( (30, 30), 40, false ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( before[First + 4].X, before[First + 4].Y ), "a click on the chain start closes the gap" );
		Assert.IsFalse( track.IsCut );
		Assert.IsTrue( track.IsRunnable && ride.IsOperational );
		Assert.AreEqual( CoasterControllerState.Built, ride.State, "0x442c70 sets state 2" );
		CollectionAssert.AreEqual( before, track.Nodes.Select( node => (node.X, node.Y) ).ToArray() );
	}

	[TestMethod]
	public void TheCutNeverSplitsALoopPairAndWidensASingleNodeToItsNeighbours()
	{
		var (builder, ride) = NewCoaster();
		builder.Height = 40;
		var circuit = CoasterTests.Circuit;
		for ( var index = 0; index < circuit.Length; index++ )
		{
			if ( index == 1 )
				builder.LoopMode = true;
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + circuit[index].X, 20 + circuit[index].Y ) );
		}
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		var track = builder.Track;
		var count = track.Nodes.Count;

		// the same node twice cuts that node out
		Assert.AreEqual( CoasterTrack.CutResult.Cut, builder.Cut( First + 4, First + 4 ) );
		Assert.AreEqual( count - 1, track.Nodes.Count );
		Assert.AreEqual( First + 3, track.CutAfter );
		var (again, _) = NewCoaster();
		again.Height = 40;
		for ( var index = 0; index < circuit.Length; index++ )
		{
			if ( index == 1 )
				again.LoopMode = true;
			again.Place( 20 + circuit[index].X, 20 + circuit[index].Y );
		}
		CoasterTests.CloseAndComplete( again );

		// the pair itself widens to the nodes around it
		Assert.AreEqual( CoasterTrack.CutResult.Cut, again.Cut( First + 1, First + 2 ) );
		Assert.AreEqual( count - 2, again.Track.Nodes.Count, "both pylons of the pair go, whichever of them was clicked" );
		Assert.AreEqual( First, again.Track.CutAfter );

		// an end that touches only the second pylon of the pair moves back over the first
		var (third, _) = NewCoaster();
		third.Height = 40;
		for ( var index = 0; index < circuit.Length; index++ )
		{
			if ( index == 1 )
				third.LoopMode = true;
			third.Place( 20 + circuit[index].X, 20 + circuit[index].Y );
		}
		CoasterTests.CloseAndComplete( third );
		Assert.AreEqual( CoasterTrack.CutResult.Cut, third.Cut( First + 1, First + 4 ) );
		Assert.AreEqual( First, third.Track.CutAfter, "a cut that would start between the pair's pylons moves back over the first" );
		var (fourth, _) = NewCoaster();
		fourth.Height = 40;
		for ( var index = 0; index < circuit.Length; index++ )
		{
			if ( index == 1 )
				fourth.LoopMode = true;
			fourth.Place( 20 + circuit[index].X, 20 + circuit[index].Y );
		}
		CoasterTests.CloseAndComplete( fourth );
		Assert.AreEqual( CoasterTrack.CutResult.Cut, fourth.Cut( First, First + 2 ) );
		Assert.AreEqual( (First, count - 2), (fourth.Track.CutAfter, fourth.Track.Nodes.Count), "an end on the second pylon of a pair moves on over it: the whole pair goes" );
	}

	[TestMethod]
	public void ACutNeedsAFinishedCircuitAndPylonEnds()
	{
		var (builder, ride) = NewCoaster();
		builder.Place( 24, 20 );
		Assert.AreEqual( CoasterTrack.CutResult.Refused, builder.Cut( First, 0 ), "an unfinished circuit has nothing to cut" );
		var (finished, _) = Finished();
		Assert.AreEqual( CoasterTrack.CutResult.Refused, finished.Cut( 1, First + 2 ), "S1 is pylon-less and cannot border a cut" );
		Assert.AreEqual( CoasterTrack.CutResult.Refused, finished.Cut( finished.Track.Nodes.Count - 1, First + 2 ), "the station end node cannot either" );
	}

	[TestMethod]
	public void HandEditsAndACutCircuitRoundTripThroughTheParkSave()
	{
		var (builder, ride) = Finished();
		ride.EnterTool();
		var panel = new CoasterToolPanel( builder );
		panel.Select( CoasterToolButton.Rotate );
		Assert.IsTrue( panel.Press( First + 1, new NVector2( 100, 100 ), 0, 0 ) );
		panel.Drag( new NVector2( 80, 100 ), 0, null );
		panel.Release( First + 1, null );
		panel.Select( CoasterToolButton.Tilt );
		Assert.IsTrue( panel.Press( First + 2, new NVector2( 100, 100 ), 0, 0 ) );
		panel.Drag( new NVector2( 90, 95 ), 0, null );
		panel.Release( First + 2, null );
		panel.Select( CoasterToolButton.Demolish );
		panel.Release( First + 3, null );
		Assert.AreEqual( CoasterPanelResult.Cut, panel.Release( First + 5, null ) );

		var captured = ParkResumeJson.Deserialize<CoasterRideSaveData>( ParkResumeJson.Serialize( CoasterSave.CaptureRide( ride, attractionId: 7 ) ) );
		Assert.AreEqual( builder.Track.CutAfter, captured.Track.CutAfter );
		var resumed = new CoasterRide( ride.Definition, new CoasterWorld( 1 ), "Test Coaster" );
		var other = new CoasterBuilder( resumed, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( other, (20, 20) );
		resumed.RestoreSnapshot( captured );
		var (a, b) = (builder.Track, other.Track);
		Assert.AreEqual( a.CutAfter, b.CutAfter );
		Assert.AreEqual( a.Nodes.Count, b.Nodes.Count );
		for ( var index = 0; index < a.Nodes.Count; index++ )
		{
			Assert.AreEqual( a.Nodes[index].Flags, b.Nodes[index].Flags, $"node {index}" );
			Assert.AreEqual( (a.Nodes[index].Yaw, a.Nodes[index].Incline, a.Nodes[index].Bank), (b.Nodes[index].Yaw, b.Nodes[index].Incline, b.Nodes[index].Bank), $"node {index}" );
		}
		Assert.AreEqual( CoasterControllerState.Incomplete, resumed.State );
		var (x, y) = (a.Nodes[a.CutAfter!.Value + 1].X, a.Nodes[a.CutAfter!.Value + 1].Y);
		Assert.IsTrue( b.IsChainStart( x, y ) );
	}

	// ---- The cursor and the placement area ----------------------------------------------------------------

	[TestMethod]
	public void ThePlacementAreaIsTheBoxAroundTheLastNodeMinusWhatTheValidatorOrOccupancyRefuses()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		builder.Place( 24, 20 );
		var occupied = new HashSet<(int, int)> { (26, 20) };
		var panel = new CoasterToolPanel( builder, ( x, y ) => !occupied.Contains( (x, y) ) );
		var pylon = builder.Track.Definition.StandardPylon;
		var half = pylon.MaxPlaceDistance + 1;
		var area = panel.PlacementArea;
		Assert.IsTrue( area.Count > 0 );
		Assert.IsTrue( area.All( cell => cell.X >= 24 - half && cell.X < 24 + half && cell.Y >= 20 - half && cell.Y < 20 + half ), "half size = max place distance + 1, upper edges exclusive" );
		Assert.IsTrue( area.All( cell => builder.Check( cell.X, cell.Y ) == CoasterPlaceResult.Ok ) );
		Assert.IsFalse( area.Contains( (26, 20) ), "an occupied cell is not marked" );
		Assert.IsFalse( area.Contains( (24, 20) ), "the last node's own cell is the same cell" );
		Assert.IsTrue( area.Contains( (28, 24) ) );
		panel.Select( CoasterToolButton.Height );
		Assert.AreEqual( 0, panel.PlacementArea.Count, "only the build buttons mark the area" );
	}

	[TestMethod]
	public void TheCursorShowsTheValidatorsVerdictTheCostAndTheClosingTarget()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		var panel = new CoasterToolPanel( builder );
		panel.Hover( 24, 20 );
		Assert.AreEqual( CoasterPlaceResult.Ok, panel.Cursor!.Value.Result );
		Assert.IsTrue( panel.Cursor.Value.Cost > 0 );
		panel.Hover( 40, 40 );
		Assert.AreEqual( CoasterPlaceResult.TooFar, panel.Cursor!.Value.Result );
		builder.Place( 24, 20 );
		builder.Place( 28, 24 );
		panel.Hover( 17, 20 );
		Assert.IsTrue( panel.Cursor!.Value.Closing, "S0's cell is the closing target" );
		panel.Select( CoasterToolButton.Move );
		panel.Hover( 24, 20 );
		Assert.IsNull( panel.Cursor, "other buttons show no cursor" );
	}
}
