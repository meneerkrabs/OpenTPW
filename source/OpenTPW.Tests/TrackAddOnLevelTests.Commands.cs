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
		CollectionAssert.AreEqual( new[] { ParkCommandKind.LayTrackLine, ParkCommandKind.LayTrackLine, ParkCommandKind.UndoTrackLine, ParkCommandKind.LayTrackLine,
			ParkCommandKind.LayTrackLine, ParkCommandKind.LayTrackLine, ParkCommandKind.CutTrack }, kinds );

		var (twin, twinRide, _) = World( laid: false );
		foreach ( var entry in level.Commands.Log )
			Assert.IsTrue( twin.Commands.Apply( entry.Command ).Accepted, entry.Command.ToString() );
		Assert.AreEqual( TrackHash( level, ride ), TrackHash( twin, twinRide ), "the commands alone build the same track and controller" );
		Assert.IsFalse( twin.CellTool.IsActive, "a peer's or a replayed command moves no tool" );
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
		Assert.AreEqual( closed, TrackHash( laid, laidRide ) );
	}
}
