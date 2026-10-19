using System;
using System.IO;
using System.Linq;
using SVector3 = System.Numerics.Vector3;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.ContentKit;

namespace OpenTPW.Tests;

/// <summary>
/// The winter extension's drawing (docs/WINTER.md, phase W4): the anchor tables (parsing, mirroring, the slot fallback, the shipped tables), the placement of an item at an anchor, which items a guest shows, and the head anchors of the
/// original sets (the measuring rules on made-up frames and the committed table, which the maintainers' tool wrote from the player's own frames: numbers only).
/// </summary>
// [EXT:winter-draw] presentation only
[TestClass]
public class WinterDrawingTests
{
	private static string Folder
	{
		get
		{
			for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
			{
				var candidate = Path.Combine( directory.FullName, "content", "winter" );
				if ( File.Exists( Path.Combine( candidate, "anchors-original.json" ) ) )
					return candidate;
			}
			throw new FileNotFoundException( "content/winter/anchors-original.json not found above the test binaries." );
		}
	}

	private static readonly string[] Kids = { "SPR_BE", "SPR_BI", "SPR_CH", "SPR_FR", "SPR_KI", "SPR_SA", "SPR_SU", "SPR_TA" };
	private static readonly string[] Costumes = { "SPR_TI", "SPR_BK", "SPR_HK", "SPR_SK" };

	private const string RigJson = """
		{"schema":2,"pixelsPerUnit":256,"fields":["headTopX","headTopY","headX","headY","rightHandX","rightHandY","rightHandAngle","leftHandX","leftHandY","leftHandAngle","visible"],
		 "sets":{"X":{"slots":{
		  "stand":{"slot":0,"points":[[[1,-50,1,-40,19,-16,30,-19,-16,-30,3]],[[2,-49,2,-39,14,-6,0,-12,-12,0,1]],[[3,-48,3,-38,1,-1,0,1,-1,0,1]],[[4,-47,4,-37,-12,-5,0,14,-24,0,2]],[[5,-46,5,-36,-19,-14,0,19,-14,0,3]]]},
		  "walk":{"slot":1,"points":[[[0,-46,0,-36,7,-14,0,-7,-14,0,3],[0,-45,0,-35,8,-13,0,-8,-13,0,3]],[[1,-46,1,-36,1,-1,0,1,-1,0,3],[1,-45,1,-35,1,-1,0,1,-1,0,3]],[[1,-46,1,-36,1,-1,0,1,-1,0,3],[1,-45,1,-35,1,-1,0,1,-1,0,3]],[[1,-46,1,-36,1,-1,0,1,-1,0,3],[1,-45,1,-35,1,-1,0,1,-1,0,3]],[[1,-46,1,-36,1,-1,0,1,-1,0,3],[1,-45,1,-35,1,-1,0,1,-1,0,3]]]}}}}}
		""";

	[TestMethod]
	public void AnAnchorIsReadPerSlotDirectionAndStepAndTheSlotFallbackFollowsWalking()
	{
		var table = WinterAnchorTable.Parse( RigJson, 256, name => "SPR_" + name );
		Assert.IsTrue( table.TryGet( "SPR_X", 256, 0, 0, 0, out var stand ) );
		Assert.AreEqual( (1f, -50f, 1f, -40f), (stand.HeadTopX, stand.HeadTopY, stand.HeadX, stand.HeadY) );
		Assert.IsTrue( stand.HasHands );
		Assert.AreEqual( (19f, -16f), (stand.RightX, stand.RightY) );
		Assert.AreEqual( WinterAnchor.RightSeen | WinterAnchor.LeftSeen, stand.Seen );
		Assert.AreEqual( MathF.Sin( 30 * MathF.PI / 180 ), stand.RightSin, 1e-6 );
		Assert.AreEqual( MathF.Cos( 30 * MathF.PI / 180 ), stand.RightCos, 1e-6 );
		Assert.IsTrue( table.TryGet( "SPR_X", 256, 0, 3, 0, out var oneSeen ) );
		Assert.AreEqual( WinterAnchor.LeftSeen, oneSeen.Seen, "only the left hand is seen in this frame" );
		Assert.IsTrue( table.TryGet( "SPR_X", 256, 1, 0, 3, out var wrapped ) );
		Assert.AreEqual( -45f, wrapped.HeadTopY, "step 3 of 2 wraps to 1" );
		Assert.IsTrue( table.TryGet( "SPR_X", 256, 2, 0, 0, out var run ) );
		Assert.AreEqual( -46f, run.HeadTopY, "a slot without a table (running, a ride's pose) follows walking" );
		Assert.IsTrue( table.TryGet( "SPR_X", 256, 14, 4, 0, out var wait ) );
		Assert.AreEqual( 1f, wait.HeadTopX );
		Assert.IsTrue( table.TryGet( "SPR_X", 256, 0, 9, 0, out var clamped ) );
		Assert.AreEqual( 5f, clamped.HeadTopX, "a direction past the table's clamps to the last" );
		Assert.IsFalse( table.TryGet( "SPR_X", 128, 0, 0, 0, out _ ), "another resolution is another table" );
		Assert.IsFalse( table.TryGet( "SPR_Y", 256, 0, 0, 0, out _ ) );
	}

	[TestMethod]
	public void AMirroredAnchorNegatesXAndTheForearmsTurnButNotTheHandsIdentity()
	{
		var table = WinterAnchorTable.Parse( RigJson, 256, name => "SPR_" + name );
		table.TryGet( "SPR_X", 256, 0, 0, 0, out var anchor );
		var mirrored = WinterAnchorTable.Mirrored( anchor );
		Assert.AreEqual( (-1f, -50f, -1f, -40f), (mirrored.HeadTopX, mirrored.HeadTopY, mirrored.HeadX, mirrored.HeadY) );
		Assert.AreEqual( (-19f, -16f, -anchor.RightSin, anchor.RightCos), (mirrored.RightX, mirrored.RightY, mirrored.RightSin, mirrored.RightCos) );
		Assert.AreEqual( (19f, -16f), (mirrored.LeftX, mirrored.LeftY), "the left hand's point mirrors too" );
		Assert.AreEqual( anchor.Seen, mirrored.Seen, "which hand is seen keeps its identity" );
	}

	[TestMethod]
	public void AHeadOnlyTableHasNoHandsAndABrokenOneIsRefused()
	{
		const string head = """{"schema":2,"pixelsPerUnit":128,"fields":["headTopX","headTopY","headX","headY"],"sets":{"SPR_X":{"slots":{"stand":{"slot":0,"points":[[[1,-20,1,-16]]]}}}}}""";
		var table = WinterAnchorTable.Parse( head, 128 );
		Assert.IsTrue( table.TryGet( "SPR_X", 128, 0, 0, 0, out var anchor ) );
		Assert.IsFalse( anchor.HasHands );
		Assert.AreEqual( 0, anchor.Seen );
		Assert.AreEqual( (1f, -20f, 1f, -16f), (anchor.HeadTopX, anchor.HeadTopY, anchor.HeadX, anchor.HeadY) );
		Assert.ThrowsException<InvalidDataException>( () => WinterAnchorTable.Parse( head, 256 ), "a table at another resolution" );
		Assert.ThrowsException<InvalidDataException>( () => WinterAnchorTable.Parse( head.Replace( "\"schema\":2", "\"schema\":1" ), 128 ) );
		Assert.ThrowsException<InvalidDataException>( () => WinterAnchorTable.Parse( head.Replace( "\"points\":[[[1,-20,1,-16]]]", "\"points\":[]" ), 128 ) );
	}

	[TestMethod]
	public void TheShippedTablesCoverEveryKidAndCostumeSetAtItsOwnResolution()
	{
		var table = WinterAnchorTable.Load( Folder );
		Assert.AreEqual( 8 + 8 + 4, table.Count );
		foreach ( var name in Kids )
		{
			Assert.IsTrue( table.TryGet( name, 256, 1, 2, 5, out var rig ), name + " 2x" );
			Assert.IsTrue( rig.HasHands, name + " 2x has hands" );
			Assert.IsTrue( table.TryGet( name, 128, 14, 4, 1, out var original ), name + " 1x" );
			Assert.IsFalse( original.HasHands, name + " 1x has no hands: mittens are not drawn on the original sets" );
		}
		foreach ( var name in Costumes )
		{
			Assert.IsTrue( table.TryGet( name, 128, 0, 0, 0, out var costume ), name );
			Assert.IsFalse( costume.HasHands );
			Assert.IsFalse( table.TryGet( name, 256, 0, 0, 0, out _ ) );
		}
	}

	[TestMethod]
	public void AnItemSitsAtItsAnchorAfterTheGuestsOwnPullAndIsPulledAFurtherShare()
	{
		var eye = SVector3.Zero;
		var forward = new SVector3( 0, 1, 0 );
		var right = new SVector3( 1, 0, 0 );
		var up = new SVector3( 0, 0, 1 );
		var feet = new SVector3( 0, 100, 0 );
		var frame = new GuestAtlasFrame( 0, 0, 10, 10, -5, -10, 256, 256 );
		var mount = new GuestMount( feet, 0.9f, "SPR_X", frame, 0, 0, 0, false );
		var unit = GuestRenderer.UnitsPerPixel * 0.5f;
		var (hotspot, scale) = WarmItemRenderer.Place( mount, 10, -20, right, up, forward, eye );
		var anchor = feet + (right * (10 * unit) + up * (20 * unit)) * 0.9f;
		Assert.AreEqual( anchor * (1f - WarmItemRenderer.CameraPull), hotspot );
		Assert.AreEqual( 0.9f * (1f - WarmItemRenderer.CameraPull), scale, 1e-6 );
		var (mirroredHotspot, _) = WarmItemRenderer.Place( mount with { Mirror = true }, 10, -20, right, up, forward, eye );
		Assert.AreEqual( hotspot.X, -mirroredHotspot.X, 1e-4, "a mirrored frame negates x" );
		Assert.AreEqual( hotspot.Z, mirroredHotspot.Z, 1e-4 );
		Assert.IsTrue( WarmItemRenderer.CameraPull < UmbrellaRenderer.CameraPull, "an umbrella is pulled further, so it covers its guest's hat" );
		Assert.IsTrue( WarmItemRenderer.CameraPull > 0f );
	}

	[TestMethod]
	public void OnlyDefinedItemsAreShownAndAnInvalidValueShowsNothing()
	{
		foreach ( var (items, shown) in new[] { (0, 0), (WinterRules.Hat, WinterRules.Hat), (WinterRules.Earmuffs | WinterRules.Mittens, WinterRules.Earmuffs | WinterRules.Mittens), (WinterRules.Hat | WinterRules.Earmuffs, 0), (8, 0), (-1, 0) } )
			Assert.AreEqual( shown, WarmItemRenderer.ShownItems( new Guest( 1, 0 ) { WarmItems = items } ), $"items {items}" );
	}

	// ---- The head anchors of the original sets -----------------------------------------------------------------------

	[TestMethod]
	public void AnOriginalFramesHeadIsTheRedrawsAtHalfThePixelsMovedToTheOriginalsSilhouette()
	{
		var original = new FrameSilhouette( -13, -34, 13, 3, 0.5 );
		var redraw = new FrameSilhouette( -24, -60, 24, 4, -1.0 );
		var head = HeadAnchorMeasure.Transfer( original, redraw, new HeadAnchorMeasure.Head( new HandAnchorMeasure.Point( 0, -58 ), new HandAnchorMeasure.Point( 0, -48 ) ) );
		Assert.AreEqual( 1.0, head.Top.X, 1e-9, "x: the original's mean x minus the redraw's at half" );
		Assert.AreEqual( -29 + (-34 - -30), head.Top.Y, 1e-9, "the head is the top of the redraw (2 pixels below its top edge), so y moves by the difference of the top edges (4 pixels up)" );
		Assert.AreEqual( -24 + (-34 - -30), head.Centre.Y, 1e-9 );
		var arms = HeadAnchorMeasure.Transfer( original, redraw, new HeadAnchorMeasure.Head( new HandAnchorMeasure.Point( 0, -40 ), new HandAnchorMeasure.Point( 0, -30 ) ) );
		Assert.AreEqual( -20.0, arms.Top.Y, 1e-9, "the redraw's head is well below its top edge (hands up): no vertical move" );
	}

	[TestMethod]
	public void TheHeadTableWriterWritesNumbersOnlyAndParsesBackIntoTheLoader()
	{
		var table = new HeadAnchorMeasure.SetTable( "SPR_XX", "rule", new System.Collections.Generic.Dictionary<int, HeadAnchorMeasure.Head[][]>
		{
			[0] = Enumerable.Range( 0, 5 ).Select( _ => new[] { new HeadAnchorMeasure.Head( new HandAnchorMeasure.Point( 1.234, -25.678 ), new HandAnchorMeasure.Point( 1.0, -20.0 ) ) } ).ToArray()
		} );
		var bytes = HeadAnchorMeasure.Write( new[] { table }, (1.5, 2.5, 10), 8 );
		var anchors = WinterAnchorTable.Parse( System.Text.Encoding.UTF8.GetString( bytes ), 128 );
		Assert.IsTrue( anchors.TryGet( "SPR_XX", 128, 0, 2, 0, out var anchor ) );
		Assert.AreEqual( (1.2f, -25.7f, 1.0f, -20.0f), (anchor.HeadTopX, anchor.HeadTopY, anchor.HeadX, anchor.HeadY), "rounded to a tenth of a pixel" );
	}

	[TestMethod]
	public void TheCommittedOriginalTableIsHeadOnlyAndTheKidsFollowTheRigsAnchors()
	{
		using var original = JsonDocument.Parse( File.ReadAllText( Path.Combine( Folder, "anchors-original.json" ) ) );
		using var rig = JsonDocument.Parse( File.ReadAllText( Path.Combine( Folder, "anchors.json" ) ) );
		var top = original.RootElement;
		Assert.AreEqual( (2, 128, 5), (top.GetProperty( "schema" ).GetInt32(), top.GetProperty( "pixelsPerUnit" ).GetInt32(), top.GetProperty( "directions" ).GetInt32()) );
		var sets = top.GetProperty( "sets" );
		CollectionAssert.AreEquivalent( Kids.Concat( Costumes ).ToArray(), sets.EnumerateObject().Select( set => set.Name ).ToArray() );
		foreach ( var set in sets.EnumerateObject() )
		{
			Assert.AreEqual( Kids.Contains( set.Name ) ? "transfer" : "rule", set.Value.GetProperty( "source" ).GetString(), set.Name );
			foreach ( var (name, slot, steps) in new[] { ("stand", 0, 1), ("walk", 1, 8), ("hands-on-hips", 14, 2) } )
			{
				var slotTable = set.Value.GetProperty( "slots" ).GetProperty( name );
				Assert.AreEqual( slot, slotTable.GetProperty( "slot" ).GetInt32(), $"{set.Name} {name}" );
				var directions = slotTable.GetProperty( "points" ).EnumerateArray().ToArray();
				Assert.AreEqual( 5, directions.Length, $"{set.Name} {name}" );
				foreach ( var direction in directions )
				{
					Assert.AreEqual( steps, direction.GetArrayLength(), $"{set.Name} {name}: one point per step" );
					foreach ( var point in direction.EnumerateArray() )
					{
						Assert.AreEqual( 4, point.GetArrayLength(), "the head's top and centre only: no hands" );
						Assert.IsTrue( Math.Abs( point[0].GetDouble() ) < 30 && point[1].GetDouble() is > -70 and < 0, $"{set.Name} {name}: the head top ({point[0]}, {point[1]}) is by the figure" );
						Assert.IsTrue( point[3].GetDouble() > point[1].GetDouble(), $"{set.Name} {name}: the centre is below the top" );
					}
				}
			}
		}
		foreach ( var kid in Kids )
		{
			var mine = sets.GetProperty( kid ).GetProperty( "slots" );
			var theirs = rig.RootElement.GetProperty( "sets" ).GetProperty( kid[4..] ).GetProperty( "slots" );
			foreach ( var name in new[] { "stand", "walk", "hands-on-hips" } )
				for ( var direction = 0; direction < 5; direction++ )
				{
					var a = mine.GetProperty( name ).GetProperty( "points" )[direction];
					var b = theirs.GetProperty( name ).GetProperty( "points" )[direction];
					for ( var step = 0; step < a.GetArrayLength(); step++ )
					{
						Assert.AreEqual( b[step][0].GetDouble() / 2, a[step][0].GetDouble(), HandAnchorMeasure.MaximumShift + 0.06, $"{kid} {name} d{direction} s{step}: x" );
						Assert.AreEqual( b[step][1].GetDouble() / 2, a[step][1].GetDouble(), HandAnchorMeasure.MaximumShift + 0.06, $"{kid} {name} d{direction} s{step}: y" );
					}
				}
		}
		var error = top.GetProperty( "rule" ).GetProperty( "leaveOneSetOut" );
		Assert.AreEqual( 440, error.GetProperty( "frames" ).GetInt32(), "8 sets x (1 + 8 + 2) steps x 5 directions" );
		Assert.IsTrue( error.GetProperty( "meanPixels" ).GetDouble() < 4, "the costume rule predicts a kid's head top to within a few pixels on average (a kid is 37 pixels tall)" );
	}

	[TestMethod]
	public void TheToolReproducesTheCommittedOriginalTableFromTheLocalGameFrames()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original sprite sets." );
		var data = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( data, "esprites.wad" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks esprites.wad." );
		var pools = Path.Combine( Folder, "..", "pools", "esprites", "Generic", "Kids" );
		var output = Path.Combine( Path.GetTempPath(), $"opentpw-head-anchors-{Guid.NewGuid():N}.json" );
		try
		{
			var result = ContentKitCli.Run( new[] { "measure-head-anchors", "--data", data, "--anchors", Path.Combine( Folder, "anchors.json" ), "--pools", pools, "--out", output }, TextWriter.Null, TextWriter.Null );
			Assert.AreEqual( 0, result );
			CollectionAssert.AreEqual( File.ReadAllBytes( Path.Combine( Folder, "anchors-original.json" ) ), File.ReadAllBytes( output ), "the committed table is what the tool writes from the game's frames" );
		}
		finally
		{
			File.Delete( output );
		}
	}
}
