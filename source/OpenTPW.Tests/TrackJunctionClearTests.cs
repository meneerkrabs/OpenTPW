using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Characterization tests of the clearing that already exists in <see cref="TrackEditor"/> on main
/// 75fdaed088bd20a23b7157dae565d2d52ace7911: a junction pass removes the perpendicular physical mask pair
/// based on the remaining single flow link, while a cut removes the cut line's link bit first. These tests
/// encode current behavior only; they are not an original execution trace and assert no native parity.
/// </summary>
[TestClass]
public class TrackJunctionClearTests
{
	private sealed class Grid : IParkGrid
	{
		public int Width => 40;
		public int Height => 40;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => x < 0 || y < 0 || x >= Width || y >= Height ? OriginalPlacementResult.OutsideTerrain : OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	private sealed class Rig
	{
		public readonly ParkCellMap Cells = new( 40, 40 );
		public readonly TrackBuilder Builder;
		public readonly TrackStation Station = TrackStation.FromPlacement( 10, 9, 0 );
		public readonly List<(int X, int Y)> Cleared = new();
		public readonly TrackEditor Editor;

		public Rig( TrackFamily family = TrackFamily.Kart )
		{
			Builder = new TrackBuilder( Cells, family, () => null, new Grid() );
			Builder.PlaceStation( Station );
			Editor = new TrackEditor( Builder, Station, new TrackEditHooks { TileCleared = at => Cleared.Add( at ) } );
		}

		public TrackCellLayer Tracks => Builder.Tracks;
		public int Owner => Builder.OwnerOf( Station );

		public TrackCell Put( (int X, int Y) at, TrackCell cell )
		{
			cell.Owner = Owner;
			cell.Type = Builder.OriginType;
			Tracks.Set( at.X, at.Y, cell );
			return cell;
		}
	}

	[TestMethod]
	public void ClearCellOnACrossingRemovesThePerpendicularMaskPairAndKeepsTheSingleLink()
	{
		var cases = new (byte Heading, byte ExpectedMask, int ExpectedRotation)[]
		{
			(TrackHeading.NegY, 0x11, 0),
			(TrackHeading.PosX, 0x44, 90),
			(TrackHeading.PosY, 0x11, 180),
			(TrackHeading.NegX, 0x44, 270)
		};

		foreach (var (heading, expectedMask, expectedRotation) in cases)
		{
			var rig = new Rig();
			var at = (20, 20);
			var before = rig.Put( at, MakeCell( 0x55, heading, 1 ) );

			var cleared = rig.Editor.ClearCell( at, pass: true );
			var after = rig.Tracks.Get( at.Item1, at.Item2 );

			Assert.IsFalse( cleared, "overlap 1 counts down to 0, so the crossing stays" );
			Assert.AreEqual( expectedMask, after.Mask, $"heading 0x{heading:X2} removes its perpendicular mask pair" );
			Assert.AreEqual( heading, after.Link, "the single surviving link is not cleared" );
			Assert.AreEqual( 0, after.Overlap, "the overlap counts down once" );
			AssertMetadataPreserved( before, after );
			Assert.AreEqual( 3, after.Family, "the piece is repicked" );
			Assert.AreEqual( 2, after.Piece, "the remaining mask is a straight" );
			Assert.AreEqual( expectedRotation, after.Rotation, "the straight's flow rotation is repicked" );
		}
	}

	[TestMethod]
	public void ClearCellOnEveryTeeMaskRemovesThePerpendicularPair()
	{
		var cases = new (byte Mask, byte Heading, byte ExpectedMask)[]
		{
			(0x15, TrackHeading.NegY, 0x11), (0x15, TrackHeading.PosX, 0x04), (0x15, TrackHeading.PosY, 0x11), (0x15, TrackHeading.NegX, 0x04),
			(0x45, TrackHeading.NegY, 0x01), (0x45, TrackHeading.PosX, 0x44), (0x45, TrackHeading.PosY, 0x01), (0x45, TrackHeading.NegX, 0x44),
			(0x51, TrackHeading.NegY, 0x11), (0x51, TrackHeading.PosX, 0x40), (0x51, TrackHeading.PosY, 0x11), (0x51, TrackHeading.NegX, 0x40),
			(0x54, TrackHeading.NegY, 0x10), (0x54, TrackHeading.PosX, 0x44), (0x54, TrackHeading.PosY, 0x10), (0x54, TrackHeading.NegX, 0x44)
		};

		foreach (var (mask, heading, expectedMask) in cases)
		{
			var rig = new Rig();
			var at = (20, 20);
			var before = rig.Put( at, MakeCell( mask, heading, 1 ) );

			var cleared = rig.Editor.ClearCell( at, pass: true );
			var after = rig.Tracks.Get( at.Item1, at.Item2 );

			Assert.IsFalse( cleared, $"tee 0x{mask:X2} stays at overlap 0" );
			Assert.AreEqual( expectedMask, after.Mask, $"tee 0x{mask:X2} / heading 0x{heading:X2}" );
			Assert.AreEqual( heading, after.Link, $"tee 0x{mask:X2} keeps its single link" );
			Assert.AreEqual( 0, after.Overlap, $"tee 0x{mask:X2} counts down once" );
			AssertMetadataPreserved( before, after );
			Assert.AreEqual( 3, after.Family, $"tee 0x{mask:X2} repicks its piece" );
		}
	}

	[TestMethod]
	public void PositiveOverlapKeepsTheTileAndRepeatedClearsCountDownAgain()
	{
		var rig = new Rig();
		var at = (20, 20);
		rig.Put( at, MakeCell( 0x55, TrackHeading.PosY, 2 ) );

		Assert.IsFalse( rig.Editor.ClearCell( at, pass: true ) );
		var first = rig.Tracks.Get( at.Item1, at.Item2 );
		Assert.AreEqual( 1, first.Overlap );
		Assert.AreEqual( (byte)0x11, first.Mask, "the first pass removed the X pair" );
		Assert.AreEqual( TrackHeading.PosY, first.Link );

		Assert.IsFalse( rig.Editor.ClearCell( at, pass: true ) );
		var second = rig.Tracks.Get( at.Item1, at.Item2 );
		Assert.AreEqual( 0, second.Overlap, "the survivor counts down again rather than treating the clear as idempotent" );
		Assert.AreEqual( (byte)0x11, second.Mask, "the second pass has no perpendicular arm left to remove" );
		Assert.AreEqual( TrackHeading.PosY, second.Link );
		Assert.IsTrue( second.IsOrigin, "overlap 0 still keeps the tile" );
	}

	[TestMethod]
	public void ZeroOverlapRemovesANonStationTileAndUnlinksTheSurvivingAxisNeighbours()
	{
		var rig = new Rig();
		var at = (20, 20);
		rig.Put( at, MakeCell( 0x55, TrackHeading.PosY, 0 ) );
		var ahead = (20, 22);
		var behind = (20, 18);
		var side = (22, 20);
		rig.Put( ahead, MakeCell( TrackHeading.NegY, TrackHeading.NegY, 0 ) );
		rig.Put( behind, MakeCell( TrackHeading.PosY, TrackHeading.PosY, 0 ) );
		rig.Put( side, MakeCell( TrackHeading.NegX, TrackHeading.NegX, 0 ) );

		Assert.IsTrue( rig.Editor.ClearCell( at, pass: true ) );
		Assert.AreEqual( 0, rig.Tracks.Get( at.Item1, at.Item2 ).Type, "overlap below zero clears the tile" );
		Assert.AreEqual( 0, rig.Tracks.Get( ahead.Item1, ahead.Item2 ).Mask, "the +Y neighbour loses the arm toward the cleared tile" );
		Assert.AreEqual( 0, rig.Tracks.Get( behind.Item1, behind.Item2 ).Mask, "the −Y neighbour loses the arm toward the cleared tile" );
		Assert.AreEqual( (byte)0x40, rig.Tracks.Get( side.Item1, side.Item2 ).Mask,
			"the pass removed the crossing's +X arm before ClearOrigin, so the perpendicular neighbour keeps its arm in the current code" );
		CollectionAssert.AreEqual( new[] { at }, rig.Cleared, "only the cleared origin tile reports a refund" );
	}

	[TestMethod]
	public void ZeroOverlapSparesTheFirstStationTileAndTheEntryTile()
	{
		var rig = new Rig();
		var first = rig.Station.First;
		var entry = rig.Station.Entry;

		Assert.IsFalse( rig.Editor.ClearCell( first, pass: true ) );
		var firstAfter = rig.Tracks.Get( first.Item1, first.Item2 );
		Assert.AreEqual( rig.Builder.StartType, firstAfter.Type, "the first station tile stays" );
		Assert.AreEqual( -1, firstAfter.Overlap, "its overlap still counts below zero" );

		rig.Put( entry, MakeCell( 0x11, TrackHeading.PosY, 0 ) );
		Assert.IsFalse( rig.Editor.ClearCell( entry, pass: true ) );
		var entryAfter = rig.Tracks.Get( entry.Item1, entry.Item2 );
		Assert.AreEqual( TrackCellTypes.KartOrigin, entryAfter.Type, "the tile before the station stays" );
		Assert.AreEqual( -1, entryAfter.Overlap );
		Assert.AreEqual( 0, rig.Cleared.Count, "a spared station-side tile refunds nothing" );
	}

	[TestMethod]
	public void ReplayClearBypassesTheStationSpare()
	{
		var rig = new Rig();
		var first = rig.Station.First;

		Assert.IsTrue( rig.Editor.ClearCell( first, pass: true, replay: true ) );
		Assert.AreEqual( 0, rig.Tracks.Get( first.Item1, first.Item2 ).Type, "the replay flag removes the station tile the ordinary clear spares" );
		CollectionAssert.AreEqual( new[] { first }, rig.Cleared );
	}

	[TestMethod]
	public void MultibitOrZeroLinkRemovesNoMaskArmsInTheCurrentClear()
	{
		foreach ( var link in new byte[] { 0x00, 0x05, 0x11, 0x14, 0x41, 0x44, 0x50, 0x55, 0xFF } )
		{
			var rig = new Rig();
			var at = (20, 20);
			var before = rig.Put( at, MakeCell( 0x55, link, 1 ) );

			var cleared = rig.Editor.ClearCell( at, pass: true );
			var after = rig.Tracks.Get( at.Item1, at.Item2 );

			Assert.IsFalse( cleared, $"link 0x{link:X2} stays at overlap 0" );
			Assert.AreEqual( (byte)0x55, after.Mask, $"link 0x{link:X2} removes no mask arm in the current switch" );
			Assert.AreEqual( link, after.Link, $"link 0x{link:X2} is left unchanged" );
			Assert.AreEqual( 0, after.Overlap );
			AssertMetadataPreserved( before, after );
		}
	}

	[TestMethod]
	public void CutRemovesOneDirectionalLinkBeforeClearCellOnTheFourCrossingLinks()
	{
		// Original crossing links, the cut direction taken from each pair, the surviving link and the mask ClearCell leaves.
		var cases = new (byte CrossingLink, byte CutHeading, byte RemainingLink, byte ExpectedMask)[]
		{
			(0x14, TrackHeading.PosX, TrackHeading.PosY, 0x11),
			(0x41, TrackHeading.NegX, TrackHeading.NegY, 0x11),
			(0x50, TrackHeading.PosY, TrackHeading.NegX, 0x44),
			(0x05, TrackHeading.NegY, TrackHeading.PosX, 0x44)
		};

		// Both families use the same clear path; water is the family named by TRACK-004.
		foreach ( var family in new[] { TrackFamily.Kart, TrackFamily.Water } )
		{
			foreach ( var (crossingLink, cutHeading, remainingLink, expectedMask) in cases )
			{
				var rig = new Rig( family );
				var crossing = (20, 20);
				var first = (crossing.Item1 - TrackStation.TileCells * TrackHeading.Dx( cutHeading ), crossing.Item2 - TrackStation.TileCells * TrackHeading.Dy( cutHeading ));
				var second = (crossing.Item1 + TrackStation.TileCells * TrackHeading.Dx( cutHeading ), crossing.Item2 + TrackStation.TileCells * TrackHeading.Dy( cutHeading ));
				var opposite = TrackHeading.Opposite( cutHeading );

				var firstCell = rig.Put( first, MakeCell( cutHeading, cutHeading, 0 ) );
				firstCell.Segment = 0;
				rig.Tracks.Set( first.Item1, first.Item2, firstCell );
				var crossingCell = rig.Put( crossing, MakeCell( 0x55, crossingLink, 1 ) );
				crossingCell.Segment = 1;
				rig.Tracks.Set( crossing.Item1, crossing.Item2, crossingCell );
				var secondCell = rig.Put( second, MakeCell( opposite, opposite, 0 ) );
				secondCell.Segment = 2;
				rig.Tracks.Set( second.Item1, second.Item2, secondCell );

				var cut = rig.Editor.Cut( first, second, commit: true );
				var after = rig.Tracks.Get( crossing.Item1, crossing.Item2 );

				Assert.IsFalse( cut.Failed, $"cut of crossing link 0x{crossingLink:X2}" );
				Assert.IsTrue( after.IsOrigin, $"crossing link 0x{crossingLink:X2} survives at overlap 0" );
				Assert.AreEqual( remainingLink, after.Link, $"cut heading 0x{cutHeading:X2} removes only its own bit from 0x{crossingLink:X2}" );
				Assert.AreEqual( expectedMask, after.Mask, $"the remaining link 0x{remainingLink:X2} removes the other axis' mask pair" );
				Assert.AreEqual( 0, after.Overlap, $"crossing link 0x{crossingLink:X2} counts down once" );
			}
		}
	}

	[TestMethod]
	public void APreviewOfACutAcrossACrossingLeavesTheCellsSelectionAndRefundsUnchanged()
	{
		// Detached synthetic origins only: this characterizes preview observation, not a placed ride's runtime circuit.
		var cases = new (byte CrossingLink, byte CutHeading)[]
		{
			(0x14, TrackHeading.PosX), (0x41, TrackHeading.NegX),
			(0x50, TrackHeading.PosY), (0x05, TrackHeading.NegY)
		};

		foreach ( var family in new[] { TrackFamily.Kart, TrackFamily.Water } )
		{
			foreach ( var (crossingLink, cutHeading) in cases )
			{
				var rig = new Rig( family );
				var crossing = (20, 20);
				var first = (crossing.Item1 - TrackStation.TileCells * TrackHeading.Dx( cutHeading ), crossing.Item2 - TrackStation.TileCells * TrackHeading.Dy( cutHeading ));
				var second = (crossing.Item1 + TrackStation.TileCells * TrackHeading.Dx( cutHeading ), crossing.Item2 + TrackStation.TileCells * TrackHeading.Dy( cutHeading ));
				var crossingCell = rig.Put( crossing, MakeCell( 0x55, crossingLink, 1 ) );
				crossingCell.Segment = 1;
				rig.Tracks.Set( crossing.Item1, crossing.Item2, crossingCell );
				foreach ( var (x, y, mask) in new[] { (18, 20, TrackHeading.PosX), (22, 20, TrackHeading.NegX), (20, 18, TrackHeading.PosY), (20, 22, TrackHeading.NegY) } )
				{
					var cell = rig.Put( (x, y), MakeCell( mask, mask, 0 ) );
					cell.Segment = (short)((x, y) == first ? 0 : (x, y) == second ? 2 : 3);
					rig.Tracks.Set( x, y, cell );
				}

				var writer = new TrackEditWriter( rig.Editor, family, _ => Assert.Fail( "a preview cuts nothing" ), () => Assert.Fail( "a preview refuses nothing" ) );
				Assert.IsNull( writer.FirstPick );
				Assert.IsTrue( writer.CanStart( first ) );
				Assert.AreEqual( first, writer.FirstPick );

				var before = Snapshot( rig.Tracks );
				var count = rig.Tracks.Count;
				var version = rig.Tracks.Version;
				var cut = rig.Editor.Cut( first, second, commit: false );
				var preview = writer.Preview( first, second );

				Assert.IsFalse( cut.Failed );
				CollectionAssert.AreEqual( new[] { first, crossing, second }, cut.Path.ToArray() );
				Assert.AreEqual( 0, cut.Cleared.Count );
				var expectedLine = new List<(int X, int Y)>();
				foreach ( var (x, y) in new[] { first, crossing, second } )
				{
					expectedLine.Add( (x, y) );
					expectedLine.Add( (x + 1, y) );
					expectedLine.Add( (x, y + 1) );
					expectedLine.Add( (x + 1, y + 1) );
				}
				CollectionAssert.AreEqual( expectedLine, preview.Line.ToArray() );
				Assert.IsTrue( preview.Completed );
				Assert.AreEqual( 0, preview.Built.Count );
				Assert.AreEqual( first, writer.FirstPick );

				CollectionAssert.AreEqual( before, Snapshot( rig.Tracks ), "a preview changes no cell word" );
				Assert.AreEqual( count, rig.Tracks.Count );
				Assert.AreEqual( version, rig.Tracks.Version );
				Assert.AreEqual( 0, rig.Cleared.Count, "a preview refunds nothing" );
			}
		}
	}

	private static TrackCell[] Snapshot( TrackCellLayer tracks )
	{
		var cells = new TrackCell[tracks.Width * tracks.Height];
		for ( var y = 0; y < tracks.Height; y++ )
			for ( var x = 0; x < tracks.Width; x++ )
				cells[tracks.IndexOf( x, y )] = tracks.Get( x, y );
		return cells;
	}

	private static TrackCell MakeCell( byte mask, byte link, short overlap ) => new()
	{
		Type = TrackCellTypes.KartOrigin,
		Mask = mask,
		Link = link,
		Flags = TrackCell.FlagAddOn,
		Parent = -1,
		Family = 99,
		Piece = 99,
		Rotation = 99,
		Overlap = overlap,
		AltMask = 0x1234,
		Segment = 7
	};

	private static void AssertMetadataPreserved( TrackCell before, TrackCell after )
	{
		Assert.AreEqual( before.Type, after.Type );
		Assert.AreEqual( before.Flags, after.Flags );
		Assert.AreEqual( before.Parent, after.Parent );
		Assert.AreEqual( before.AltMask, after.AltMask );
		Assert.AreEqual( before.Segment, after.Segment );
		Assert.AreEqual( before.Owner, after.Owner );
	}
}
