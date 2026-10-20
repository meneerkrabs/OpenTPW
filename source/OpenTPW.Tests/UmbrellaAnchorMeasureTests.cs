using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.ContentKit;

namespace OpenTPW.Tests;

/// <summary>
/// The hand anchors of the original sprite sets (docs/UMBRELLAS.md, "Anchors for the original sets"): the measuring rules on made-up frames, and the committed table
/// <c>content/umbrellas/anchors-original.json</c>, which the maintainers' tool <c>opentpw-kit measure-anchors</c> wrote from the player's own frames (numbers only).
/// </summary>
// [EXT:umbrellas-content] OpenTPW's own measurements of the game's frames; nothing of the frames is kept
[TestClass]
public class UmbrellaAnchorMeasureTests
{
	private static SpriteFrame Frame( int width, int height, int originX, int originY, Func<int, int, bool> opaque )
	{
		var indices = new byte[width * height];
		for ( var y = 0; y < height; y++ )
			for ( var x = 0; x < width; x++ )
				indices[y * width + x] = opaque( x, y ) ? (byte)1 : (byte)0;
		return new SpriteFrame( width, height, originX, originY, 128, 128, indices );
	}

	[TestMethod]
	public void ASilhouetteIsTheBoxAndTheMeanXOfTheOpaquePixelsFromTheHotspot()
	{
		// a 6 x 8 frame whose top-left corner is 3 left of and 7 above the hotspot; only columns 1..3 and rows 2..6 are opaque
		var outline = FrameSilhouette.Of( Frame( 6, 8, -3, -7, ( x, y ) => x is >= 1 and <= 3 && y is >= 2 and <= 6 ) )!.Value;
		Assert.AreEqual( (-2.0, -5.0, 1.0, 0.0), (outline.Left, outline.Top, outline.Right, outline.Bottom) );
		Assert.AreEqual( -0.5, outline.CentroidX, 1e-9, "the pixel centres -1.5, -0.5 and 0.5" );
		Assert.AreEqual( -0.5, outline.CenterX, 1e-9 );
		Assert.IsNull( FrameSilhouette.Of( Frame( 3, 3, 0, 0, ( x, y ) => false ) ), "an empty frame has no silhouette" );
	}

	[TestMethod]
	public void AnOriginalFramesAnchorIsTheRedrawsAtHalfThePixelsMovedToTheOriginalsSilhouette()
	{
		var original = new FrameSilhouette( -13, -34, 13, 3, 0.5 );
		var redraw = new FrameSilhouette( -24, -60, 24, 4, -1.0 );
		var anchor = HandAnchorMeasure.Transfer( original, redraw, new HandAnchorMeasure.Point( 19, -16 ) );
		Assert.AreEqual( 9.5 + 1.0, anchor.X, 1e-9, "x: half of 19, plus the original's mean x (0.5) minus the redraw's at half (-0.5)" );
		Assert.AreEqual( -8.0, anchor.Y, 1e-9 );
		var far = HandAnchorMeasure.Transfer( original, new FrameSilhouette( -24, -60, 24, 4, 40 ), new HandAnchorMeasure.Point( 0, 0 ) );
		Assert.AreEqual( -HandAnchorMeasure.MaximumShift, far.X, 1e-9, "frames that are not the same pose never move an anchor more than the limit" );
	}

	[TestMethod]
	public void ARuleGivesTheHandBackFromItsRatiosAndTheFitIsTheMeanOfTheSamples()
	{
		var outline = new FrameSilhouette( -10, -30, 14, 2, 1 );
		var hand = new HandAnchorMeasure.Point( 8, -9 );
		var ratios = HandAnchorMeasure.RatiosOf( outline, hand );
		Assert.AreEqual( (2 + 9) / 32.0, ratios.Height, 1e-9, "11 pixels above the bottom edge of a 32 pixel tall figure" );
		Assert.AreEqual( (8 - 2) / 12.0, ratios.Side, 1e-9, "6 pixels right of the box's centre, half of which is 12 wide" );
		var back = HandAnchorMeasure.Apply( ratios, outline );
		Assert.AreEqual( (hand.X, hand.Y), (back.X, back.Y) );
		var bigger = new FrameSilhouette( -20, -60, 28, 4, 2 );
		var scaled = HandAnchorMeasure.Apply( ratios, bigger );
		Assert.AreEqual( (4 + 24 * ratios.Side, 4 - 64 * ratios.Height), (scaled.X, scaled.Y), "a larger figure has the hand in the same place of it" );

		var rule = HandAnchorMeasure.FitRule( new[]
		{
			new HandAnchorMeasure.Sample( 0, 1, outline, hand ),
			new HandAnchorMeasure.Sample( 0, 1, bigger, new HandAnchorMeasure.Point( 4 + 24 * 0.2, 4 - 64 * 0.4 ) ),
			new HandAnchorMeasure.Sample( 1, 0, outline, hand )
		} );
		Assert.AreEqual( 2, rule.Count, "one entry per slot and direction that has samples" );
		Assert.AreEqual( (ratios.Height + 0.4) / 2, rule[(0, 1)].Height, 1e-9 );
		Assert.AreEqual( (ratios.Side + 0.2) / 2, rule[(0, 1)].Side, 1e-9 );
	}

	[TestMethod]
	public void LeavingASetOutMeasuresWhatTheRuleCannotPredict()
	{
		HandAnchorMeasure.Sample[] Set( double height, double side ) => Enumerable.Range( 0, 5 ).Select( direction =>
		{
			var outline = new FrameSilhouette( -10, -30 - direction, 10, 2, 0 );
			return new HandAnchorMeasure.Sample( 0, direction, outline, HandAnchorMeasure.Apply( new HandAnchorMeasure.Ratios( height, side ), outline ) );
		} ).ToArray();
		var same = HandAnchorMeasure.LeaveOneSetOut( new IReadOnlyList<HandAnchorMeasure.Sample>[] { Set( 0.3, 0.5 ), Set( 0.3, 0.5 ), Set( 0.3, 0.5 ) } );
		Assert.AreEqual( (15, 0.0, 0.0), (same.Frames, same.Mean, same.Max), "three sets with the same ratios: the rule predicts each exactly" );
		var apart = HandAnchorMeasure.LeaveOneSetOut( new IReadOnlyList<HandAnchorMeasure.Sample>[] { Set( 0.3, 0.5 ), Set( 0.3, 0.5 ), Set( 0.5, 0.5 ) } );
		Assert.IsTrue( apart.Mean > 1 && apart.Max > apart.Mean, $"one odd set is predicted badly (mean {apart.Mean}, max {apart.Max})" );
	}

	[TestMethod]
	public void TheTableWriterWritesNumbersOnlyAndParses()
	{
		var rule = new Dictionary<(int Slot, int Direction), HandAnchorMeasure.Ratios> { [(0, 0)] = new( 0.3, 0.7 ) };
		var table = new HandAnchorMeasure.SetTable( "SPR_XX", "rule", new Dictionary<int, HandAnchorMeasure.Point[][]>
		{
			[0] = Enumerable.Range( 0, 5 ).Select( _ => new[] { new HandAnchorMeasure.Point( 1.234, -5.678 ) } ).ToArray()
		} );
		using var document = JsonDocument.Parse( HandAnchorMeasure.Write( new[] { table }, rule, (1.5, 2.5, 10), 8 ) );
		var set = document.RootElement.GetProperty( "sets" ).GetProperty( "SPR_XX" );
		Assert.AreEqual( "rule", set.GetProperty( "source" ).GetString() );
		Assert.AreEqual( 0, set.GetProperty( "stand" ).GetProperty( "slot" ).GetInt32() );
		Assert.AreEqual( 1.2, set.GetProperty( "stand" ).GetProperty( "frames" )[0][0][0].GetDouble(), 1e-9, "rounded to a tenth of a pixel" );
		Assert.AreEqual( -5.7, set.GetProperty( "stand" ).GetProperty( "frames" )[0][0][1].GetDouble(), 1e-9 );
		Assert.AreEqual( 2.5, document.RootElement.GetProperty( "rule" ).GetProperty( "leaveOneSetOut" ).GetProperty( "maxPixels" ).GetDouble() );
	}

	// ---- The committed table ---------------------------------------------------------------------------------------

	private static string Folder
	{
		get
		{
			for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
			{
				var candidate = Path.Combine( directory.FullName, "content", "umbrellas" );
				if ( File.Exists( Path.Combine( candidate, "anchors-original.json" ) ) )
					return candidate;
			}
			throw new FileNotFoundException( "content/umbrellas/anchors-original.json not found above the test binaries." );
		}
	}

	private static readonly string[] Kids = { "SPR_BE", "SPR_BI", "SPR_CH", "SPR_FR", "SPR_KI", "SPR_SA", "SPR_SU", "SPR_TA" };
	private static readonly string[] Costumes = { "SPR_TI", "SPR_BK", "SPR_HK", "SPR_SK" };

	[TestMethod]
	public void TheCommittedTableCoversTheEightKidSetsAndTheFourCostumeSets()
	{
		using var document = JsonDocument.Parse( File.ReadAllText( Path.Combine( Folder, "anchors-original.json" ) ) );
		var top = document.RootElement;
		Assert.AreEqual( (1, 128, 5), (top.GetProperty( "schema" ).GetInt32(), top.GetProperty( "pixelsPerUnit" ).GetInt32(), top.GetProperty( "directions" ).GetInt32()) );
		var sets = top.GetProperty( "sets" );
		CollectionAssert.AreEquivalent( Kids.Concat( Costumes ).ToArray(), sets.EnumerateObject().Select( set => set.Name ).ToArray() );
		foreach ( var set in sets.EnumerateObject() )
		{
			Assert.AreEqual( Kids.Contains( set.Name ) ? "transfer" : "rule", set.Value.GetProperty( "source" ).GetString(), set.Name );
			foreach ( var (name, slot, steps) in new[] { ("stand", 0, 1), ("walk", 1, 8), ("wait", 14, 2) } )
			{
				var table = set.Value.GetProperty( name );
				Assert.AreEqual( slot, table.GetProperty( "slot" ).GetInt32(), $"{set.Name} {name}" );
				var directions = table.GetProperty( "frames" ).EnumerateArray().ToArray();
				Assert.AreEqual( 5, directions.Length, $"{set.Name} {name}" );
				foreach ( var direction in directions )
				{
					Assert.AreEqual( steps, direction.GetArrayLength(), $"{set.Name} {name}: one point per step" );
					foreach ( var point in direction.EnumerateArray() )
					{
						Assert.AreEqual( 2, point.GetArrayLength() );
						Assert.IsTrue( Math.Abs( point[0].GetDouble() ) < 40 && point[1].GetDouble() is > -60 and < 10, $"{set.Name} {name}: the hand ({point[0]}, {point[1]}) is by the figure" );
					}
				}
			}
		}
	}

	[TestMethod]
	public void TheKidTablesFollowTheRigsAnchorsAtHalfThePixelsAndTheRuleIsAboutAsGoodAsItsMeasuredError()
	{
		using var original = JsonDocument.Parse( File.ReadAllText( Path.Combine( Folder, "anchors-original.json" ) ) );
		using var rig = JsonDocument.Parse( File.ReadAllText( Path.Combine( Folder, "anchors.json" ) ) );
		foreach ( var kid in Kids )
		{
			var mine = original.RootElement.GetProperty( "sets" ).GetProperty( kid );
			var theirs = rig.RootElement.GetProperty( "sets" ).GetProperty( kid[4..] );
			foreach ( var name in new[] { "stand", "walk", "wait" } )
				for ( var direction = 0; direction < 5; direction++ )
				{
					var a = mine.GetProperty( name ).GetProperty( "frames" )[direction];
					var b = theirs.GetProperty( name ).GetProperty( "frames" )[direction];
					for ( var step = 0; step < a.GetArrayLength(); step++ )
					{
						Assert.AreEqual( b[step][0].GetDouble() / 2, a[step][0].GetDouble(), HandAnchorMeasure.MaximumShift + 0.06, $"{kid} {name} d{direction} s{step}: x" );
						Assert.AreEqual( b[step][1].GetDouble() / 2, a[step][1].GetDouble(), 0.06, $"{kid} {name} d{direction} s{step}: y" );
					}
				}
		}
		var error = original.RootElement.GetProperty( "rule" ).GetProperty( "leaveOneSetOut" );
		Assert.AreEqual( 440, error.GetProperty( "frames" ).GetInt32(), "8 sets x (1 + 8 + 2) steps x 5 directions" );
		Assert.IsTrue( error.GetProperty( "meanPixels" ).GetDouble() < 5, "the costume rule predicts a kid's transferred anchor to within a few pixels on average (a kid is 37 pixels tall)" );
	}

	[TestMethod]
	public void TheToolReproducesTheCommittedTableFromTheLocalGameFrames()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original sprite sets." );
		var data = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( data, "esprites.wad" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks esprites.wad." );
		var pools = Path.Combine( Folder, "..", "pools", "esprites", "Generic", "Kids" );
		var output = Path.Combine( Path.GetTempPath(), $"opentpw-anchors-{Guid.NewGuid():N}.json" );
		try
		{
			var result = ContentKitCli.Run( new[] { "measure-anchors", "--data", data, "--anchors", Path.Combine( Folder, "anchors.json" ), "--pools", pools, "--out", output }, TextWriter.Null, TextWriter.Null );
			Assert.AreEqual( 0, result );
			CollectionAssert.AreEqual( File.ReadAllBytes( Path.Combine( Folder, "anchors-original.json" ) ), File.ReadAllBytes( output ), "the committed table is what the tool writes from the game's frames" );
		}
		finally
		{
			File.Delete( output );
		}
	}
}
