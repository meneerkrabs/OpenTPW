using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] The coaster tool's writes as commands (docs/COOP.md, "Tools"), without game data: a whole session of clicks, Backspace and the panel's drags, cut and stack swap
/// leaves the tool as commands, and the commands alone build the same coaster (its nodes, flags, controller and paid cost) on a twin.
/// </summary>
[TestClass]
public class CoasterCommandTests
{
	private const int First = CoasterTrack.StationNodeCount;

	[TestInitialize]
	public void Init() => Log = new();

	private sealed class Rig
	{
		public Rig()
		{
			Ride = new CoasterRide( CoasterDefinition.FromValues( CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Test Coaster" );
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

		/// <summary>A click of the build buttons, as <see cref="Level.CoasterClick"/> issues it: with the panel's height and loop edit mode.</summary>
		public CoasterClickDone Click( int x, int y ) =>
			(CoasterClickDone)Issue( new CoasterClickCommand( 0, x, y, Builder.Height, Builder.LoopMode, Builder.LoopPylonsLeft ) ).Value!;

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
		if ( panel.Press( First + 2, new NVector2( 100, 100 ), 0, 0 ) )
		{
			panel.Drag( new NVector2( 90, 100 ), 0, null );
			panel.Release( null, null );
		}
		Assert.IsTrue( panel.Select( CoasterToolButton.Demolish ) );
		Assert.AreEqual( CoasterPanelResult.CutStarted, panel.Release( First + 1, null ) );
		Assert.AreNotEqual( CoasterPanelResult.Nothing, panel.Release( First + 3, null ), "the second release cuts the range out" );
		Assert.IsFalse( rig.Builder.Track.IsComplete && !rig.Builder.Track.IsCut, "the circuit is open again" );

		var kinds = rig.Log.Select( command => command.Kind ).Distinct().ToArray();
		CollectionAssert.IsSubsetOf( new[] { ParkCommandKind.CoasterClick, ParkCommandKind.RemoveLastCoasterPylon, ParkCommandKind.BeginCoasterNodeEdit, ParkCommandKind.EditCoasterNode,
			ParkCommandKind.EndCoasterNodeEdit, ParkCommandKind.CutCoaster }, kinds );

		var twin = new Rig();
		foreach ( var command in rig.Log )
			twin.Issue( command );
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
		Assert.IsFalse( rig.Issue( new EndCoasterNodeEditCommand( 0, CoasterEditEnd.Settle ) ).Accepted );
		Assert.IsTrue( rig.Issue( new BeginCoasterNodeEditCommand( 0, First, false ) ).Accepted );
		Assert.IsFalse( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.Loft, 0.5f, 0, 0 ) { Player = 1 } ).Accepted, "another player's drag holds no edit" );
		Assert.IsFalse( rig.Issue( new EditCoasterNodeCommand( 0, CoasterNodeField.LoopRotate, 0.5f, 0, 0 ) ).Accepted, "a standard pylon has no loop rotation" );
		Assert.IsFalse( rig.Issue( new BeginCoasterNodeEditCommand( 0, 0, false ) ).Accepted, "a station node cannot be edited" );
		Assert.AreEqual( before, rig.Hash(), "the refusals changed nothing" );
		Assert.IsFalse( rig.Issue( new CoasterClickCommand( 0, 70, 70, 40, false, 0 ) ).Accepted, "a click off the site places nothing" );
		Assert.AreEqual( before, rig.Hash() );
	}
}
