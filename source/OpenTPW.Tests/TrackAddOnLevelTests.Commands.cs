using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] The kart and water track tool's writes as commands (docs/COOP.md, "Tools"), on the same asset-free level: each line, its undo and a cut leave the tool as one
/// command, the tool still ends and continues as it did, and the logged commands alone build the same track on a twin level.
/// </summary>
public partial class TrackAddOnLevelTests
{
	/// <summary>The track layer and the ride's controller (the parts of the world hash a track edit writes).</summary>
	private static ulong TrackHash( Level level, OriginalObject ride )
	{
		var hash = new StateHasher();
		level.Guests!.Grid.Cells.AddCanonicalState( hash );
		ride.Runtime.Bump!.AddCanonicalState( hash );
		return hash.Value;
	}

	[TestMethod]
	public void TheTrackToolsLinesUndoAndCutAreCommandsThatBuildTheSameTrackOnATwin()
	{
		var (level, ride, _) = World( laid: false );
		Assert.IsTrue( level.EnterTrackTool( ride ) );
		Assert.IsInstanceOfType( level.CellTool.Writer, typeof( ParkCommandTrackWriter ), "an open circuit's line tool lays its lines through commands" );
		Assert.AreEqual( (14, 10), level.CellTool.Start, "the line starts at the station's exit" );
		level.CellTool.Click( (30, 10) );
		level.CellTool.Click( (30, 14) );
		Assert.IsTrue( level.CellTool.Undo(), "Backspace takes the second line back" );
		Assert.IsFalse( level.KartTracks!.IsTile( 30, 14 ) );
		level.CellTool.Click( (30, 14) );
		level.CellTool.Click( (8, 14) );
		Assert.AreSame( ride, level.TrackToolStation, "an open circuit keeps the tool" );
		level.CellTool.Click( (8, 10) );
		Assert.IsTrue( ride.Runtime.Bump!.IsTrackValid, "the last line closes the circuit" );
		Assert.IsFalse( level.CellTool.IsActive, "a line that closes the circuit ends the tool, as the walk did" );

		Assert.IsTrue( level.EnterTrackTool( ride ) );
		Assert.IsInstanceOfType( level.CellTool.Writer, typeof( TrackEditWriter ), "a closed circuit's track button cuts" );
		level.CellTool.Click( (20, 10) );
		level.CellTool.Click( (24, 10) );
		Assert.IsFalse( ride.Runtime.Bump.IsTrackValid, "the cut opens the circuit" );
		Assert.IsInstanceOfType( level.CellTool.Writer, typeof( ParkCommandTrackWriter ), "the open circuit's line tool follows the cut" );

		var kinds = level.Commands.Log.Select( entry => entry.Command.Kind ).ToArray();
		CollectionAssert.AreEqual( new[] { ParkCommandKind.OpenTrackLineTool, ParkCommandKind.LayTrackLine, ParkCommandKind.LayTrackLine, ParkCommandKind.UndoTrackLine,
			ParkCommandKind.LayTrackLine, ParkCommandKind.LayTrackLine, ParkCommandKind.LayTrackLine, ParkCommandKind.CutTrack }, kinds );

		var (twin, twinRide, _) = World( laid: false );
		foreach ( var entry in level.Commands.Log )
			Assert.IsTrue( twin.Commands.Apply( entry.Command ).Accepted, entry.Command.ToString() );
		Assert.AreEqual( TrackHash( level, ride ), TrackHash( twin, twinRide ), "the commands alone build the same track and controller" );
		Assert.AreEqual( level.ComputeStateHash(), twin.ComputeStateHash(), "and the same world" );
		Assert.IsFalse( twin.CellTool.IsActive, "a peer's or a replayed command moves no tool" );
	}

	/// <summary>
	/// The line tool's walk to the open end sets every walked tile's overlap word (1 at corners and junctions, else 0) and Backspace reads that word to decide whether a tile goes: on a
	/// straight joint between two lines the line's own walk left another value, so the walk is a command, run on the twin too.
	/// </summary>
	[TestMethod]
	public void ReopeningTheLineToolAndItsBackspaceKeepATwinInStep()
	{
		var (level, ride, _) = World( laid: false );
		Assert.IsTrue( level.EnterTrackTool( ride ) );
		level.CellTool.Click( (20, 10) );
		level.CellTool.Click( (26, 10) );
		level.CellTool.Cancel();
		var joint = level.KartTracks!.Tracks.Get( 20, 10 ).Overlap;
		Assert.IsTrue( level.EnterTrackTool( ride ), "the tool opens again at the open end" );
		Assert.AreEqual( (26, 10), level.CellTool.Start );
		Assert.AreNotEqual( joint, level.KartTracks.Tracks.Get( 20, 10 ).Overlap, "the walk to the open end rewrote the joint's overlap word" );
		Assert.IsTrue( level.CellTool.Undo(), "Backspace takes the joined lines back to the station" );
		CollectionAssert.AreEqual( new[] { ParkCommandKind.OpenTrackLineTool, ParkCommandKind.LayTrackLine, ParkCommandKind.LayTrackLine, ParkCommandKind.OpenTrackLineTool,
			ParkCommandKind.UndoTrackLine }, level.Commands.Log.Select( entry => entry.Command.Kind ).ToArray() );

		var (twin, twinRide, _) = World( laid: false );
		foreach ( var entry in level.Commands.Log )
			Assert.IsTrue( twin.Commands.Apply( entry.Command ).Accepted, entry.Command.ToString() );
		Assert.AreEqual( level.KartTracks.IsTile( 20, 10 ), twin.KartTracks!.IsTile( 20, 10 ), "the joint goes on both or neither" );
		Assert.AreEqual( TrackHash( level, ride ), TrackHash( twin, twinRide ), "the same overlap words and tiles" );
		Assert.AreEqual( level.ComputeStateHash(), twin.ComputeStateHash(), "and the same world" );
		Assert.AreEqual( level.Park?.Economy.Balance, twin.Park?.Economy.Balance, "the same refunds" );
	}

	/// <summary>A cut that leaves the circuit open hands the line tool the open end: the walk there is <see cref="CutTrackCommand"/>'s, so Backspace after the cut takes the same tiles on a twin.</summary>
	[TestMethod]
	public void ACutsWalkToTheOpenEndIsTheCommandsSoBackspaceAfterItMatchesOnATwin()
	{
		var (level, ride, _) = World( laid: false );
		Assert.IsTrue( level.EnterTrackTool( ride ) );
		// the first two lines meet on a straight joint at (20, 10)
		foreach ( var cell in new[] { (20, 10), (30, 10), (30, 14), (8, 14), (8, 10) } )
			level.CellTool.Click( cell );
		Assert.IsTrue( ride.Runtime.Bump!.IsTrackValid, "the circuit is closed" );
		Assert.IsTrue( level.EnterTrackTool( ride ) );
		level.CellTool.Click( (24, 10) );
		level.CellTool.Click( (28, 10) );
		Assert.IsInstanceOfType( level.CellTool.Writer, typeof( ParkCommandTrackWriter ), "the open circuit's line tool follows the cut" );
		Assert.IsTrue( level.CellTool.Undo(), "Backspace after the cut" );
		Assert.AreEqual( ParkCommandKind.UndoTrackLine, level.Commands.Log[^1].Command.Kind );
		Assert.AreEqual( ParkCommandKind.CutTrack, level.Commands.Log[^2].Command.Kind, "the cut's command walks to the open end; the tool opens no other" );

		var (twin, twinRide, _) = World( laid: false );
		foreach ( var entry in level.Commands.Log )
			Assert.IsTrue( twin.Commands.Apply( entry.Command ).Accepted, entry.Command.ToString() );
		Assert.AreEqual( TrackHash( level, ride ), TrackHash( twin, twinRide ), "the same overlap words and tiles after the cut and its Backspace" );
		Assert.AreEqual( level.ComputeStateHash(), twin.ComputeStateHash(), "and the same world" );
		Assert.AreEqual( level.Park?.Economy.Balance, twin.Park?.Economy.Balance );
	}

	[TestMethod]
	public void ATrackCommandForARideWithoutATrackOrAnOpenCircuitIsRefusedAndChangesNothing()
	{
		var (level, ride, _) = World( laid: false );
		var before = TrackHash( level, ride );
		var id = ride.Runtime.AttractionId;
		Assert.IsFalse( level.Commands.Issue( new LayTrackLineCommand( id + 1000, 14, 10, 30, 10 ) ).Accepted, "no such ride" );
		Assert.IsFalse( level.Commands.Issue( new CutTrackCommand( id, 14, 10, 16, 10 ) ).Accepted, "an open circuit has no edit tool" );
		Assert.AreEqual( before, TrackHash( level, ride ) );
		var (laid, laidRide, _) = World();
		var closed = TrackHash( laid, laidRide );
		Assert.IsFalse( laid.Commands.Issue( new CutTrackCommand( laidRide.Runtime.AttractionId, 10, 10, 20, 10 ) ).Accepted, "a station tile takes no cut" );
		Assert.IsFalse( laid.Commands.Issue( new OpenTrackLineToolCommand( laidRide.Runtime.AttractionId ) ).Accepted, "a closed circuit opens the edit tool, not the line tool" );
		Assert.AreEqual( closed, TrackHash( laid, laidRide ) );
	}
}
