using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Asset-free tests of the track edit sub-modes (docs/TRACKS.md, "The track edit sub-modes"; TRACK-018): the pick test, the cut between two tiles of a closed circuit with its refunds and the
/// repairs at the cut's ends, the walk to the open end where the line tool goes on, and the edit tool inside the cell build tool.
/// </summary>
[TestClass]
public class TrackEditTests
{
	private sealed class Grid : IParkGrid
	{
		public int Width => 40;
		public int Height => 40;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => x < 0 || y < 0 || x >= Width || y >= Height ? OriginalPlacementResult.OutsideTerrain : OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	/// <summary>
	/// A closed loop of eight tiles (the two station tiles and six laid ones) on the station of an attraction cell (10, 9): the walk numbers them First (10, 10) 0, Second (12, 10) 1,
	/// (14, 10) 2, (14, 12) 3, (12, 12) 4, (10, 12) 5, (8, 12) 6 and the tile before the station (8, 10) 7.
	/// </summary>
	private sealed class Rig
	{
		public readonly ParkCellMap Cells = new( 40, 40 );
		public readonly TrackBuilder Builder;
		public readonly TrackStation Station = TrackStation.FromPlacement( 10, 9, 0 );
		public readonly List<(int X, int Y)> Refunded = new();
		public readonly List<(int X, int Y)> AddOnsRemoved = new();
		public readonly TrackEditor Editor;

		public Rig( bool loop = true )
		{
			Builder = new TrackBuilder( Cells, TrackFamily.Kart, () => null, new Grid() );
			Builder.PlaceStation( Station );
			if ( loop )
			{
				var writer = new TrackLineWriter( Builder, owner: Builder.OwnerOf( Station ) );
				Assert.IsTrue( writer.Commit( (14, 10), (14, 12) ).Completed );
				Assert.IsTrue( writer.Commit( (14, 12), (8, 12) ).Completed );
				Assert.IsTrue( writer.Commit( (8, 12), (8, 10) ).Completed );
				Assert.IsTrue( Walk().IsValid, "the loop is a valid circuit" );
			}
			Editor = new TrackEditor( Builder, Station, new TrackEditHooks { TileCleared = at => Refunded.Add( at ), RemoveAddOn = at => AddOnsRemoved.Add( at ) } );
		}

		public TrackCellLayer Tracks => Builder.Tracks;
		public TrackCircuit Walk() => TrackCircuitWalker.WalkAndRefresh( Builder, Station );
		public bool Laid( int x, int y ) => Tracks.Get( x, y ).IsOrigin;
	}

	[TestMethod]
	public void TheWalkNumbersTheTilesFromTheFirstStationTile()
	{
		var rig = new Rig();
		var steps = new[] { (10, 10), (12, 10), (14, 10), (14, 12), (12, 12), (10, 12), (8, 12), (8, 10) };
		for ( var index = 0; index < steps.Length; index++ )
			Assert.AreEqual( index, rig.Editor.StepOf( steps[index] ), $"step of {steps[index]}" );
		Assert.AreEqual( 3, rig.Editor.StepOf( (15, 13) ), "an extension cell reads its origin's step (0x53AEC0)" );
	}

	[TestMethod]
	public void ThePickTestRefusesStationTilesAndAcceptsTheRidesOwnTilesWithFewerThanThreeArms()
	{
		var rig = new Rig();
		Assert.AreEqual( (TrackEditPick.None, null), rig.Editor.Pick( 10, 10 ), "a station tile is left alone (0x53ADB0)" );
		Assert.AreEqual( (TrackEditPick.None, null), rig.Editor.Pick( 30, 30 ), "a cell without track picks nothing" );
		Assert.AreEqual( (TrackEditPick.Accepted, ((int, int)?)(12, 12)), rig.Editor.Pick( 12, 12 ) );
		Assert.AreEqual( (TrackEditPick.Accepted, ((int, int)?)(14, 12)), rig.Editor.Pick( 15, 13 ), "an extension cell takes its origin (0x53AD90)" );
		// a branch off the straight at (12, 12) makes it a tee: three arms, answer 1
		Assert.IsTrue( new TrackLineWriter( rig.Builder, owner: rig.Builder.OwnerOf( rig.Station ) ).Commit( (12, 12), (12, 14) ).Completed );
		Assert.AreEqual( TrackEditPick.Rejected, rig.Editor.Pick( 12, 12 ).Result, "a junction is refused (0x53A4D8)" );
		Assert.AreEqual( TrackEditPick.Accepted, rig.Editor.Pick( 12, 14 ).Result, "the stub of the branch is a tile with one arm" );
	}

	[TestMethod]
	public void AnotherRidesTileIsRefused()
	{
		var rig = new Rig();
		var other = rig.Builder.OwnerOf( TrackStation.FromPlacement( 20, 20, 0 ) );
		Assert.IsTrue( new TrackLineWriter( rig.Builder, owner: other ).Commit( (20, 20), (20, 24) ).Completed );
		Assert.AreEqual( TrackEditPick.Rejected, rig.Editor.Pick( 20, 22 ).Result, "its ride word is not the selected ride's" );
	}

	[TestMethod]
	public void TheHoverReportsTheTilesBetweenTwoPicksAlongTheFlow()
	{
		var rig = new Rig();
		var path = rig.Editor.Cut( (14, 12), (10, 12), commit: false );
		Assert.IsFalse( path.Failed );
		CollectionAssert.AreEqual( new[] { (14, 12), (12, 12), (10, 12) }, path.Path.ToArray(), "the walk turns at the corner and ends at the second pick" );
		Assert.IsTrue( rig.Laid( 12, 12 ) && rig.Refunded.Count == 0, "a preview changes nothing" );
	}

	[TestMethod]
	public void ACutClearsTheTilesBetweenTheTwoPicksAndRefundsEach()
	{
		var rig = new Rig();
		var cut = rig.Editor.Cut( (12, 12), (10, 12), commit: true );
		rig.Editor.RepairStation();
		Assert.IsFalse( cut.Failed );
		CollectionAssert.AreEqual( new[] { (12, 12), (10, 12) }, cut.Cleared.ToArray() );
		CollectionAssert.AreEqual( new[] { (12, 12), (10, 12) }, rig.Refunded.ToArray(), "each cleared tile refunds once" );
		Assert.IsFalse( rig.Laid( 12, 12 ) || rig.Laid( 10, 12 ), "both picked tiles are gone, with their four cells" );
		Assert.AreEqual( 0, rig.Cells.RawTypeAt( 13, 13 ), "the extension cells are cleared too" );
		Assert.AreEqual( 0x01, rig.Tracks.Get( 14, 12 ).Mask, "the tile before the cut lost its arm toward the cleared tile and is a stub" );
		Assert.AreEqual( 0x01, rig.Tracks.Get( 8, 12 ).Mask, "so did the tile after it" );
		Assert.IsFalse( rig.Walk().IsValid, "the circuit is open" );
	}

	[TestMethod]
	public void ACutOfOneTileRelinksItsNeighboursAndClearsIt()
	{
		var rig = new Rig();
		var cut = rig.Editor.Cut( (10, 12), (10, 12), commit: true );
		CollectionAssert.AreEqual( new[] { (10, 12) }, cut.Cleared.ToArray() );
		Assert.IsFalse( rig.Laid( 10, 12 ) );
		Assert.AreEqual( 0x04, rig.Tracks.Get( 12, 12 ).Mask, "the neighbour ahead lost the arm toward the cleared tile" );
		Assert.AreEqual( 0x01, rig.Tracks.Get( 8, 12 ).Mask, "so did the one behind" );
		Assert.AreEqual( 0x41, rig.Tracks.Get( 14, 12 ).Mask, "a tile further away is not touched" );
	}

	[TestMethod]
	public void TheTileBeforeTheStationIsSparedAndBecomesTheCutEnd()
	{
		var rig = new Rig();
		var cut = rig.Editor.Cut( (8, 12), (8, 10), commit: true );
		rig.Editor.RepairStation();
		CollectionAssert.AreEqual( new[] { (8, 12) }, cut.Cleared.ToArray(), "the clear skips the tile before the first station tile (0x7639E8/EC)" );
		Assert.IsTrue( rig.Laid( 8, 10 ) );
		Assert.IsFalse( rig.Laid( 8, 12 ) );
		Assert.AreEqual( 0, rig.Tracks.Get( 8, 10 ).Mask & 0x10, "the spared tile lost its arm toward the cleared one" );
	}

	[TestMethod]
	public void TheStationTileDropsItsArmToAMissingTileAndTakesItBackWhenATileIsLaidThere()
	{
		var rig = new Rig();
		rig.Builder.EraseTile( 8, 10 );
		rig.Editor.RepairStation();
		Assert.AreEqual( 0x04, rig.Tracks.Get( 10, 10 ).Mask, "an arm toward a cell without track goes (0x52FD6B)" );
		rig.Builder.PlaceTile( 8, 10, rig.Builder.OwnerOf( rig.Station ) );
		Assert.AreEqual( 0x44, rig.Tracks.Get( 10, 10 ).Mask, "a tile laid on the station's axis joins it again" );
		Assert.AreEqual( 0x04, rig.Tracks.Get( 8, 10 ).Mask & 0x44, "and takes the arm toward it" );
	}

	[TestMethod]
	public void ACrossingLosesOnePassOnly()
	{
		// a taller loop and a line across its right side: the crossing at (14, 12) keeps the cross line when the cut runs down the side
		var rig = new Rig( loop: false );
		var owner = rig.Builder.OwnerOf( rig.Station );
		var writer = new TrackLineWriter( rig.Builder, owner: owner );
		Assert.IsTrue( writer.Commit( (14, 10), (14, 16) ).Completed );
		Assert.IsTrue( writer.Commit( (14, 16), (8, 16) ).Completed );
		Assert.IsTrue( writer.Commit( (8, 16), (8, 10) ).Completed );
		Assert.IsTrue( writer.Commit( (16, 12), (12, 12) ).Completed );
		Assert.AreEqual( 0x55, rig.Tracks.Get( 14, 12 ).Mask, "the tile is a crossing" );
		Assert.AreEqual( 1, rig.Tracks.Get( 14, 12 ).Overlap, "one pass more than a plain tile" );
		Assert.IsTrue( rig.Walk().IsValid );
		var cut = rig.Editor.Cut( (14, 10), (14, 14), commit: true );
		Assert.IsFalse( cut.Failed );
		CollectionAssert.AreEqual( new[] { (14, 14) }, cut.Cleared.ToArray(), "the crossing and protected exit are not cleared" );
		Assert.IsTrue( rig.Laid( 14, 12 ), "it stays with the cross line" );
		Assert.AreEqual( 0x44, rig.Tracks.Get( 14, 12 ).Mask, "the arms along the cut went, the arms of the other pass stay" );
		Assert.AreEqual( 0, rig.Tracks.Get( 14, 12 ).Overlap, "one pass counted down" );
	}

	[TestMethod]
	public void ATeeOnTheWayIsClearedWithTheRest()
	{
		// the clear before the tee took one of its arms, so it is no junction any more when the cut reaches it (0x53AE90): it goes and its branch is left without an arm
		var rig = new Rig();
		var owner = rig.Builder.OwnerOf( rig.Station );
		Assert.IsTrue( new TrackLineWriter( rig.Builder, owner: owner ).Commit( (12, 12), (12, 14) ).Completed );
		Assert.AreEqual( 0x54, rig.Tracks.Get( 12, 12 ).Mask, "the tile is a tee" );
		Assert.IsTrue( rig.Walk().IsValid );
		var cut = rig.Editor.Cut( (14, 12), (10, 12), commit: true );
		CollectionAssert.AreEqual( new[] { (14, 12), (12, 12), (10, 12) }, cut.Cleared.ToArray() );
		Assert.IsTrue( rig.Laid( 12, 14 ) && rig.Tracks.Get( 12, 14 ).Mask == 0, "the branch tile stays, armless" );
	}

	[TestMethod]
	public void AClearedTileTakesItsAddOnObjectWithIt()
	{
		var rig = new Rig();
		var cell = rig.Tracks.Get( 12, 12 );
		cell.Flags |= TrackCell.FlagAddOn;
		rig.Tracks.Set( 12, 12, cell );
		rig.Editor.Cut( (12, 12), (12, 12), commit: true );
		CollectionAssert.AreEqual( new[] { (12, 12) }, rig.AddOnsRemoved.ToArray() );
	}

	[TestMethod]
	public void TheOpenEndOfABrokenCircuitIsTheLastTileAlongTheFlow()
	{
		var rig = new Rig();
		rig.Editor.Cut( (12, 12), (10, 12), commit: true );
		rig.Editor.RepairStation();
		rig.Walk();
		var open = rig.Editor.FindOpenEnd();
		Assert.AreEqual( (14, 12), open.End, "the walk from the exit turns at (14, 10) and ends at the stub" );
		CollectionAssert.AreEqual( new[] { (14, 10), (14, 10), (14, 12) }, open.Corners.ToArray(), "the start tile, the corner and the end are recorded" );
		Assert.IsFalse( open.ReachedStation );
	}

	[TestMethod]
	public void TheLineToolFromTheOpenEndMendsTheCircuit()
	{
		var rig = new Rig();
		var cut = rig.Editor.Cut( (12, 12), (10, 12), commit: true );
		rig.Editor.RepairStation();
		Assert.IsFalse( rig.Walk().IsValid );
		var end = rig.Editor.FindOpenEnd().End;
		var line = new TrackLineWriter( rig.Builder, owner: rig.Builder.OwnerOf( rig.Station ) ).Commit( end, (8, 12) );
		Assert.IsTrue( line.Completed, $"the line lays {line.Built.Count} tiles ({line.StoppedBy})" );
		CollectionAssert.AreEqual( new[] { (12, 12), (10, 12) }, line.Built.ToArray() );
		var walk = rig.Walk();
		Assert.IsTrue( walk.IsValid, walk.Failure );
		Assert.AreEqual( 8, walk.Steps.Count );
	}

	[TestMethod]
	public void TheOpenEndOfAFreshRideIsTheExitAndAClosedCircuitReachesTheStation()
	{
		var fresh = new Rig( loop: false );
		var open = fresh.Editor.FindOpenEnd();
		Assert.AreEqual( fresh.Station.Exit, open.End );
		CollectionAssert.AreEqual( new[] { fresh.Station.Exit }, open.Corners.ToArray() );
		var closed = new Rig();
		var end = closed.Editor.FindOpenEnd();
		Assert.IsTrue( end.ReachedStation, "a circuit that leads home ends on the station tile whose arm points back" );
		Assert.AreEqual( closed.Station.First, end.End );
	}

	[TestMethod]
	public void TheOpenEndWalkRecountsTheOverlapWords()
	{
		var rig = new Rig();
		rig.Editor.FindOpenEnd();
		Assert.AreEqual( 1, rig.Tracks.Get( 14, 10 ).Overlap, "a corner counts 1 (0x5228A0)" );
		Assert.AreEqual( 0, rig.Tracks.Get( 12, 12 ).Overlap, "a straight counts 0" );
	}

	// ---- The demolition replays (TRACK-007) ---------------------------------------------------------------

	[TestMethod]
	public void TheReplaysClearAClosedLoopAndRefundEveryTileOnce()
	{
		var rig = new Rig();
		var cleared = rig.Editor.Demolish();
		Assert.AreEqual( 7, cleared.Distinct().Count(), "the six laid tiles go, and the first station tile where the forward chain ends (0x530830 stands on it, so the first replayed line starts there)" );
		Assert.AreEqual( 6, rig.Refunded.Count, "each laid tile refunds once; a station tile has no cell cost (0x53B5C6: only types 0xB and 0x10)" );
		Assert.IsFalse( rig.Laid( 10, 10 ), "the first station tile went with the replay" );
		Assert.AreEqual( 1, rig.Tracks.Cells().Count( item => item.Cell.IsOrigin ), "the second station tile is left for the ride's removal" );
		rig.Builder.RemoveStation( rig.Station );
		Assert.AreEqual( 0, rig.Tracks.Count );
	}

	[TestMethod]
	public void ATileNoReplayReachesStaysAndRefundsNothing()
	{
		var rig = new Rig();
		var owner = rig.Builder.OwnerOf( rig.Station );
		Assert.IsTrue( new TrackLineWriter( rig.Builder, owner: owner ).Commit( (20, 20), (20, 24) ).Completed, "a detached piece of the same ride" );
		Assert.IsTrue( new TrackLineWriter( rig.Builder, owner: owner ).Commit( (12, 12), (12, 14) ).Completed, "a branch off the loop" );
		rig.Editor.Demolish();
		Assert.IsTrue( rig.Laid( 20, 20 ) && rig.Laid( 20, 22 ) && rig.Laid( 20, 24 ), "the detached piece stays" );
		Assert.IsTrue( rig.Laid( 12, 14 ), "so does the branch tile" );
		// the junction is passed once by the forward chain: its pass counter (1, set by the collector) runs out to 0, so it stays with the arm of the branch and the branch keeps its arm back (0x53B3A7 step rule)
		Assert.IsTrue( rig.Laid( 12, 12 ), "the junction keeps one pass" );
		Assert.AreEqual( 0x10, rig.Tracks.Get( 12, 12 ).Mask, "only the arm toward the branch is left: the line step took both arms of the loop's axis" );
		Assert.AreEqual( 0x01, rig.Tracks.Get( 12, 14 ).Mask, "the branch keeps its arm toward the junction" );
		Assert.AreEqual( 5, rig.Refunded.Count, "five of the loop's six tiles refund; the junction, the branch and the detached piece do not" );
		Assert.IsFalse( rig.Refunded.Contains( (12, 12) ) || rig.Refunded.Contains( (12, 14) ) || rig.Refunded.Contains( (20, 20) ) );
	}

	[TestMethod]
	public void AnOpenTrackIsClearedFromBothSidesOfTheStation()
	{
		var rig = new Rig( loop: false );
		var writer = new TrackLineWriter( rig.Builder, owner: rig.Builder.OwnerOf( rig.Station ) );
		Assert.IsTrue( writer.Commit( (14, 10), (14, 14) ).Completed, "a line from the exit side" );
		Assert.IsTrue( writer.Commit( (8, 10), (8, 14) ).Completed, "a line from the entry side" );
		var cleared = rig.Editor.Demolish();
		Assert.AreEqual( 6, cleared.Count );
		Assert.AreEqual( 2, rig.Tracks.Cells().Count( item => item.Cell.IsOrigin ), "both lines are gone, the station tiles remain" );
	}

	[TestMethod]
	public void ARideWithoutAnyTrackDemolishesNothing()
	{
		var rig = new Rig( loop: false );
		Assert.AreEqual( 0, rig.Editor.Demolish().Count );
		Assert.AreEqual( 0, rig.Refunded.Count );
	}

	// ---- The tool inside the cell build tool ------------------------------------------------------------

	[TestMethod]
	public void TheEditToolCutsOnTwoClicksAndAnUnacceptedSecondClickEndsIt()
	{
		var rig = new Rig();
		var tool = new CellBuildTool();
		var cuts = new List<(int X, int Y)>();
		var ended = false;
		var writer = new TrackEditWriter( rig.Editor, TrackFamily.Kart, second => cuts.Add( second ), () => { ended = true; tool.Cancel(); } );
		tool.Enter( writer, null );
		Assert.AreEqual( CellToolMode.KartEdit, tool.Mode );
		Assert.IsNull( tool.Click( (10, 10) ), "a station tile is no pick: the tool stays on without a start" );
		Assert.IsNull( tool.Start );
		var ghost = tool.Hover( (15, 13) );
		Assert.IsTrue( ghost!.Completed && ghost.Line.Count == 4, "the hover marks the tile under the pointer" );
		Assert.IsNull( tool.Click( (15, 13) ) );
		Assert.AreEqual( (15, 13), tool.Start, "the first pick is the start" );
		Assert.AreEqual( 12, tool.Hover( (11, 13) )!.Line.Count, "the hover marks the three tiles of the cut between the two" );
		var result = tool.Click( (11, 13) );
		Assert.IsNotNull( result );
		CollectionAssert.AreEqual( new[] { (10, 12), (12, 12), (14, 12) }, result!.Built.OrderBy( tile => tile.X ).ToArray(), "the lower step goes first, the tiles between go too" );
		Assert.AreEqual( 1, cuts.Count );
		Assert.AreEqual( (10, 12), cuts[0], "the tile of the second click" );
		Assert.IsFalse( ended );

		var second = new Rig();
		var again = new CellBuildTool();
		var closer = new TrackEditWriter( second.Editor, TrackFamily.Kart, _ => Assert.Fail( "no cut" ), () => again.Cancel() );
		again.Enter( closer, null );
		again.Click( (12, 12) );
		again.Click( (10, 10) );
		Assert.IsFalse( again.IsActive, "a second click that picks nothing leaves the tool (0x52F200)" );
		Assert.IsTrue( second.Laid( 12, 12 ), "and cuts nothing" );
	}

	[TestMethod]
	public void TheEditHoverDrawsTheBreakSymbolOnAPickableTileAndTheEraseSymbolOnTheCutsTiles()
	{
		var rig = new Rig();
		var tool = new CellBuildTool();
		tool.Enter( new TrackEditWriter( rig.Editor, TrackFamily.Kart, _ => { }, tool.Cancel ), null );
		// before the first pick: the chooser answers 9 for a tile of the ride with at most two arms, the texture m_break over the whole tile
		var hover = tool.Hover( (15, 13) )!;
		CollectionAssert.AreEqual( new[] { ((14, 12), BlueprintMarker.Break) }, hover.Symbols.ToArray() );
		var cells = BlueprintGhosts.LineCells( hover.Line, hover.StoppedAt, hover.Symbols );
		CollectionAssert.AreEqual( new[] { (14, 12, BlueprintMarker.Break) }, cells.ToArray(), "one face for the tile: the origin carries it and the other three cells give way" );
		// a station tile and bare ground draw no symbol
		Assert.AreEqual( 0, tool.Hover( (10, 10) )!.Symbols.Count );
		Assert.AreEqual( 0, tool.Hover( (30, 30) )!.Symbols.Count );
		// after it: every tile of the cut between the picks is m_erase (the chooser's answer 0xD for the mode 0xF of the op 0x10F)
		tool.Click( (15, 13) );
		var cut = tool.Hover( (11, 13) )!;
		CollectionAssert.AreEqual( new[] { (14, 12), (12, 12), (10, 12) }, cut.Symbols.Select( symbol => symbol.Origin ).ToArray() );
		Assert.IsTrue( cut.Symbols.All( symbol => symbol.Symbol == BlueprintMarker.Erase ) );
		Assert.AreEqual( 3, BlueprintGhosts.LineCells( cut.Line, cut.StoppedAt, cut.Symbols ).Count, "three faces, not twelve cell marks" );
	}

	[TestMethod]
	public void TheGhostSymbolOfATileFollowsTheChoosersAnswers()
	{
		var rig = new Rig();
		new TrackLineWriter( rig.Builder, owner: rig.Builder.OwnerOf( rig.Station ) ).Commit( (12, 12), (12, 14) );
		Assert.AreEqual( BlueprintMarker.Break, rig.Editor.GhostSymbol( (14, 12), cut: false ) );
		Assert.IsNull( rig.Editor.GhostSymbol( (12, 12), cut: false ), "a junction answers 1" );
		Assert.AreEqual( BlueprintMarker.Erase, rig.Editor.GhostSymbol( (12, 12), cut: true ), "a tile of the cut answers 0xD whatever its arms" );
		Assert.IsNull( rig.Editor.GhostSymbol( (10, 10), cut: true ), "a station tile is no tile of the cut" );
		Assert.IsNull( rig.Editor.GhostSymbol( (30, 30), cut: false ) );
	}

	[TestMethod]
	public void ARefusedFirstTileEndsTheToolWithTheRefusedGhostAndASecondTileThatPicksNothingDoesNot()
	{
		// [BIN:TPW-EXE:0x527B00] the first-tile hover sets the refused-ghost latch for a junction: the release plays 0xAF and the tool ends (0x5266A3)
		var rig = new Rig();
		new TrackLineWriter( rig.Builder, owner: rig.Builder.OwnerOf( rig.Station ) ).Commit( (12, 12), (12, 14) );
		var tool = new CellBuildTool();
		tool.Enter( new TrackEditWriter( rig.Editor, TrackFamily.Kart, _ => { }, tool.Cancel ), null );
		var refused = tool.Click( (12, 12) );
		Assert.IsTrue( refused is { GhostRefused: true }, "a junction is a refused first tile: the click carries the refused ghost" );
		Assert.IsFalse( tool.IsActive, "and the tool ends" );
		// a station tile answers nothing (no latch): the tool stays on; a refused second tile ends it without the refused ghost (0x526ADC)
		tool.Enter( new TrackEditWriter( rig.Editor, TrackFamily.Kart, _ => { }, tool.Cancel ), null );
		Assert.IsNull( tool.Click( (10, 10) ) );
		Assert.IsTrue( tool.IsActive );
		tool.Click( (14, 12) );
		Assert.AreEqual( (14, 12), tool.Start );
		var second = tool.Click( (12, 12) );
		Assert.IsFalse( tool.IsActive, "a refused second tile ends the tool" );
		Assert.IsFalse( second is { GhostRefused: true }, "without the refusal cue" );
	}
}
