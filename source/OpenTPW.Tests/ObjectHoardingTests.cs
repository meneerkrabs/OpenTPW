using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// The hoarding the original raises around closed, broken down, condemned and upgrading rides (tp.exe <c>0x00452E70</c> builds it, <c>0x00454550</c>,
/// <c>0x004547C0</c> and <c>0x004548F0</c> drive it; docs/OBJECTS.md, "Hoarding").
/// </summary>
[TestClass]
public class ObjectHoardingTests
{
	private static ObjectMapGrid Hoarding( params string[] rows ) => ObjectMapGrid.Parse( rows, ObjectHoardingSymbols.Lookup )!;

	private static ObjectHoardingState Present()
	{
		var state = new ObjectHoardingState { Present = true };
		return state;
	}

	[TestMethod]
	public void TheMapParserSkipsSpacesAndStoresTheLastTextRowFirst()
	{
		// tp.exe 0x00402720: a space takes no column, and the closing --- swaps the rows
		var grid = Hoarding( "F^7", "[ .]", "L_J" );
		Assert.AreEqual( (3, 3), (grid.Width, grid.Height) );
		Assert.AreEqual( 0x50, grid.DirectionAt( 0, 0 ), "row 0 is the last text row: L" );
		Assert.AreEqual( 0x41, grid.DirectionAt( 0, 2 ), "the top text row is row 2: F" );
		Assert.AreEqual( 0x00, grid.DirectionAt( 1, 1 ), "the space did not take the middle column" );
		Assert.AreEqual( 0x04, grid.DirectionAt( 2, 1 ) );
	}

	[TestMethod]
	public void AnUnknownSymbolOrATooWideRowFailsTheParse()
	{
		Assert.IsNull( ObjectMapGrid.Parse( new[] { "F*7" }, ObjectHoardingSymbols.Lookup ), "'*' is not in the hoarding table" );
		Assert.IsNull( ObjectMapGrid.Parse( new[] { new string( '.', 21 ) }, ObjectHoardingSymbols.Lookup ), "Map too wide" );
		Assert.IsNotNull( ObjectMapGrid.Parse( new[] { new string( '.', 20 ) }, ObjectHoardingSymbols.Lookup ) );
		Assert.IsNull( ObjectMapGrid.Parse( Enumerable.Repeat( ".", 21 ).ToArray(), ObjectHoardingSymbols.Lookup ), "Map too high" );
	}

	[TestMethod]
	public void ASingleCellTakesTheFixedPullsAndItsBoardsSortByTheAngleOfTheirMiddles()
	{
		// 'O' has all four sides; keys fmod(atan2 + 5π/4, 2π): west π/4, south 3π/4, east 5π/4, north 7π/4
		var strips = ObjectHoardingGeometry.BuildStrips( Hoarding( "O" ), Array.Empty<N.Vector3>() ).Strips;
		Assert.AreEqual( 4, strips.Count );
		CollectionAssert.AreEqual( new[] { -N.Vector3.UnitX, -N.Vector3.UnitZ, N.Vector3.UnitX, N.Vector3.UnitZ }, strips.Select( strip => strip.Normal ).ToArray() );
		AssertCorners( strips[0], (0.3f, 9.7f), (0.3f, 0.3f) );
		// the south board's east end takes the third corner's pull +0.3 (tp.exe's fixed pulls (0.3, 0.3) for the corner at (10, 0))
		AssertCorners( strips[1], (0.3f, 0.3f), (10.3f, 0.3f) );
		AssertCorners( strips[2], (9.7f, 0.3f), (9.7f, 9.7f) );
		AssertCorners( strips[3], (9.7f, 9.7f), (0.3f, 9.7f) );
		Assert.AreEqual( ((0, 1), (0, 0)), (strips[0].EndA, strips[0].EndB), "the west board's ends" );
		Assert.AreEqual( ((1, 1), (0, 1)), (strips[3].EndA, strips[3].EndB), "the north board's ends" );
		foreach ( var strip in strips )
		{
			Assert.AreEqual( ObjectHoardingGeometry.BoardHeight, strip.Corners[0].Y );
			Assert.AreEqual( 0f, strip.Corners[2].Y );
		}
	}

	private static void AssertCorners( ObjectHoardingStrip strip, (float X, float Z) a, (float X, float Z) b )
	{
		Assert.AreEqual( a.X, strip.Corners[0].X, 1e-5f );
		Assert.AreEqual( a.Z, strip.Corners[0].Z, 1e-5f );
		Assert.AreEqual( b.X, strip.Corners[1].X, 1e-5f );
		Assert.AreEqual( b.Z, strip.Corners[1].Z, 1e-5f );
		Assert.AreEqual( (strip.Corners[0].X, strip.Corners[0].Z), (strip.Corners[2].X, strip.Corners[2].Z), "a board's bottom stands under its top" );
		Assert.AreEqual( (strip.Corners[1].X, strip.Corners[1].Z), (strip.Corners[3].X, strip.Corners[3].Z) );
	}

	[TestMethod]
	public void EqualAnglesKeepTheCellOrder()
	{
		// "]]": both east middles lie on the centre's line at angle 0, so the stable merge sort keeps column 0 first
		var strips = ObjectHoardingGeometry.BuildStrips( Hoarding( "]]" ), Array.Empty<N.Vector3>() ).Strips;
		Assert.AreEqual( 2, strips.Count );
		Assert.AreEqual( 10f, strips[0].Corners[0].X );
		Assert.AreEqual( 20f, strips[1].Corners[0].X );
	}

	[TestMethod]
	public void ACornerIsPulledHalfwayToTheSecondNearestGroundVertex()
	{
		// the north-west corner (0, 10) of cell 0: nearest (0, 10), second (1, 9): pull (0.5, -0.5) on the north board's Z
		var ground = new[] { new N.Vector3( 0, 0, 10 ), new N.Vector3( 1, 0, 9 ), new N.Vector3( 5, 0, 5 ) };
		var north = ObjectHoardingGeometry.BuildStrips( Hoarding( "^^" ), ground ).Strips.First( strip => strip.EndB == (0, 1) );
		Assert.AreEqual( 9.5f, north.Corners[0].Z, 1e-5f );
		Assert.AreEqual( 0f, north.Corners[1].X, "no west side: the pull's X is not taken" );
	}

	[TestMethod]
	public void TheRiseSweepsThroughTheStripsAndOddBoardsAreShorter()
	{
		Assert.AreEqual( (0.0, 1f), ObjectHoardingGeometry.Rise( 0, 4, 0f ) );
		var (height, v) = ObjectHoardingGeometry.Rise( 0, 4, 0.25f );
		Assert.AreEqual( (10.0, 0f), (height, v), "strip 0 is up at level 0.25" );
		(height, v) = ObjectHoardingGeometry.Rise( 1, 4, 0.25f );
		Assert.AreEqual( 1.6, height, 1e-5, "strip 1 has risen 0.2 of its 0.8 board" );
		Assert.AreEqual( 0.84f, v, 1e-6f );
		(height, v) = ObjectHoardingGeometry.Rise( 3, 4, 1f );
		Assert.AreEqual( 8.0, height, 1e-5, "an odd board stands at 0.8 of the full height" );
		Assert.AreEqual( 0.2f, v, 1e-6f );
	}

	[TestMethod]
	public void TheTerrainCornerOfABoardEndFollowsTheRotationAndTakesTheLastCell()
	{
		var box = (X0: 10, Y0: 20, X1: 12, Y1: 23);
		// tp.exe 0x00454050: the fraction runs over x0..x1, the last cell's index, not its far corner
		Assert.AreEqual( (12, 20), ObjectHoardingGeometry.MapCorner( 0, box, 1f, 0f ) );
		Assert.AreEqual( (10, 22), ObjectHoardingGeometry.MapCorner( 0, box, 0f, 2f / 3f ) );
		Assert.AreEqual( (12, 23), ObjectHoardingGeometry.MapCorner( 90, box, 0f, 1f ) );
		Assert.AreEqual( (12, 23), ObjectHoardingGeometry.MapCorner( 180, box, 0f, 0f ) );
		Assert.AreEqual( (12, 20), ObjectHoardingGeometry.MapCorner( 270, box, 0f, 0f ) );
	}

	[TestMethod]
	public void WithoutAHoardingEveryModelCallDoesNothing()
	{
		var state = new ObjectHoardingState();
		state.SetState( ObjectHoardingState.CloseMode );
		state.StartOpenFade();
		state.Advance( 1f );
		Assert.AreEqual( (0u, 0f, 0), (state.Flags, state.Level, state.Version) );
		Assert.IsNull( state.Capture() );
	}

	[TestMethod]
	public void AClosingRaisesTheClosedSetAtAFifthPerSecondAndStopsOneFrameAfterTheTop()
	{
		var state = Present();
		Assert.IsNull( state.Capture(), "a hoarding that never stood is not saved" );
		state.SetState( ObjectHoardingState.CloseMode );
		Assert.AreEqual( ObjectHoardingState.Standing | ObjectHoardingState.Rising | ObjectHoardingState.ClosedVariant, state.Flags );
		Assert.AreEqual( 0, state.Variant );
		Assert.IsFalse( state.Shaped, "the boards keep the builder's shape until the level moves" );
		for ( var frame = 0; frame < 5; frame++ )
			state.Advance( 1f );
		Assert.AreEqual( 1f, state.Level, 1e-6f );
		Assert.IsTrue( state.Shaped );
		Assert.AreNotEqual( 0u, state.Flags & ObjectHoardingState.Rising, "the frame that reaches the top still counts as rising" );
		state.Advance( 1f );
		Assert.AreEqual( 0u, state.Flags & ObjectHoardingState.Rising, "the next frame, unchanged at 1, stops the rise" );
		Assert.IsTrue( state.IsStanding );
	}

	[TestMethod]
	public void AnOpeningSinksItAtThreeTenthsPerSecondAndItLeavesWithTheClosedSet()
	{
		var state = Present();
		state.SetState( ObjectHoardingState.BreakdownMode );
		state.Advance( 10f );
		state.StartOpenFade();
		Assert.AreEqual( ObjectHoardingState.SinkRate, state.Rate );
		Assert.AreNotEqual( 0u, state.Flags & ObjectHoardingState.Sinking );
		state.Advance( 1f );
		Assert.AreEqual( 0.7f, state.Level, 1e-6f );
		state.Advance( 3f );
		Assert.AreEqual( 0f, state.Level );
		Assert.IsTrue( state.IsStanding );
		Assert.AreEqual( 1, state.Variant, "still the broken-down set on the frame it reaches the ground" );
		state.Advance( 1f );
		Assert.IsFalse( state.IsStanding );
		Assert.AreEqual( 0, state.Variant, "a sunk hoarding goes back to the closed set (0x00454670(0))" );
	}

	[TestMethod]
	public void TheBreakdownsSetStaysThroughItsStateChangeClose()
	{
		// tp.exe 0x004E14E0: 0x00454550(2), then SetState(1)'s 0x00454550(1)
		var state = Present();
		state.SetState( ObjectHoardingState.BreakdownMode );
		state.SetState( ObjectHoardingState.CloseMode );
		Assert.AreEqual( 1, state.Variant );
		var condemned = Present();
		condemned.SetState( ObjectHoardingState.CondemnMode );
		condemned.SetState( ObjectHoardingState.CloseMode );
		Assert.AreEqual( 2, condemned.Variant );
		// tp.exe 0x004E0050(1): SetState(2)'s 0x00454550(1), then 0x00454550(8)
		var upgrade = Present();
		upgrade.SetState( ObjectHoardingState.CloseMode );
		upgrade.SetState( ObjectHoardingState.UpgradeMode );
		Assert.AreEqual( 3, upgrade.Variant );
	}

	[TestMethod]
	public void AClosingOverASinkingHoardingShowsTheClosedSetAgain()
	{
		// the repair tail: 0x004547C0, then the coaster's re-close 0x00454550(1) with the sinking bit set
		var state = Present();
		state.SetState( ObjectHoardingState.BreakdownMode );
		state.Advance( 2f );
		state.StartOpenFade();
		state.SetState( ObjectHoardingState.CloseMode );
		Assert.AreEqual( 0, state.Variant );
		Assert.AreEqual( ObjectHoardingState.RiseRate, state.Rate );
		Assert.AreEqual( 0u, state.Flags & ObjectHoardingState.Sinking );
		Assert.AreEqual( 0.4f, state.Level, 1e-6f, "the rise goes on from where the sinking began" );
	}

	[TestMethod]
	public void AnOpeningOfAHoardingThatDoesNotStandOnlySetsTheSinkRate()
	{
		var state = Present();
		state.StartOpenFade();
		Assert.AreEqual( (0u, ObjectHoardingState.SinkRate), (state.Flags, state.Rate) );
		state.Advance( 1f );
		Assert.AreEqual( 0f, state.Level, "neither rising nor sinking: the level stays" );
	}

	[TestMethod]
	public void ALoadedHoardingRisesOrSinksOnByItsFlagsAndShowsTheClosedSetWithoutAVariant()
	{
		var state = Present();
		state.Restore( ObjectHoardingState.Standing | ObjectHoardingState.Sinking | ObjectHoardingState.HoardingVariant, 0.5f );
		Assert.AreEqual( (ObjectHoardingState.SinkRate, 1, 0.5f), (state.Rate, state.Variant, state.Level) );
		Assert.IsTrue( state.Shaped, "a load shapes the boards (0x004547F0)" );
		var record = state.Capture()!;
		Assert.AreEqual( ObjectHoardingState.Standing | ObjectHoardingState.Sinking | ObjectHoardingState.HoardingVariant, record.Flags );
		var plain = Present();
		plain.Restore( 0, 0f );
		Assert.AreEqual( ObjectHoardingState.ClosedVariant, plain.Flags );
		Assert.AreEqual( 0xFE0u, ObjectHoardingState.UnpackSavedFlags( 0x7F ) );
		Assert.AreEqual( ObjectHoardingState.Standing | ObjectHoardingState.ClosedVariant, ObjectHoardingState.UnpackSavedFlags( 0x09 ) );
		Assert.AreEqual( 0u, ObjectHoardingState.UnpackSavedFlags( 0x180 ), "bits 7 and 8 are other thing flags" );
	}

	/// <summary>The save's +0x22 level is copied raw: a loaded level outside 0..1 is held to 0..1, so the snapshot of the park stays valid.</summary>
	[TestMethod]
	public void ALoadedLevelOutsideZeroToOneIsHeldToIt()
	{
		var high = Present();
		high.Restore( ObjectHoardingState.Standing | ObjectHoardingState.Rising, 1.5f );
		Assert.AreEqual( 1f, high.Level );
		Assert.AreEqual( 1f, high.Capture()!.Level );
		var low = Present();
		low.Restore( ObjectHoardingState.Standing | ObjectHoardingState.Sinking, -0.5f );
		Assert.AreEqual( 0f, low.Level );
		var nan = Present();
		nan.Restore( ObjectHoardingState.Standing, float.NaN );
		Assert.AreEqual( 0f, nan.Level );
	}
}

/// <summary>The shipped hoardings; inconclusive without OPENTPW_GAME_PATH (Theme Park Inc's textures also need OPENTPW_TPI_PATH).</summary>
[TestClass]
[DoNotParallelize]
public class ObjectHoardingCorpusTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "fantasy", "space" };
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the hoarding corpus." );
		originalFileSystem = previous;
		initialized = true;
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
		ObjectCatalog.BonusDataRoot = null;
	}

	private static ModelFile ModelOf( ObjectCatalogEntry entry )
	{
		using var stream = entry.FileSystem.OpenRead( entry.ModelPath );
		return new ModelFile( stream );
	}

	private static HoardingTerrain TerrainOf( string theme ) => HoardingTerrain.Of( new ModelFile( $"/levels/{theme}/terrain/base.MD2" ).Heightfield! );

	[TestMethod]
	public void EveryShippedHoardingParsesAndEveryNonFixedTypeWithOneGetsABoardPerSide()
	{
		var types = 0;
		var failures = new List<string>();
		foreach ( var theme in Themes )
		{
			var terrain = TerrainOf( theme );
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( entry => !entry.IsBonus && entry.Settings.Blocks.ContainsKey( "Info.Hoarding" ) ) )
			{
				types++;
				if ( ObjectMapGrid.HoardingOf( entry.Settings ) is not { } grid )
				{
					failures.Add( $"{theme}/{entry.ArchiveName}: Info.Hoarding does not parse" );
					continue;
				}
				var sides = 0;
				for ( var row = 0; row < grid.Height; row++ )
					for ( var column = 0; column < grid.Width; column++ )
						sides += System.Numerics.BitOperations.PopCount( (uint)(grid.DirectionAt( column, row ) & 0x55) );
				var geometry = ObjectHoardingGeometry.Build( entry.Settings, ModelOf( entry ), terrain );
				if ( entry.IsFixedItem )
				{
					if ( geometry != null )
						failures.Add( $"{theme}/{entry.ArchiveName}: a fixed item got a hoarding" );
				}
				else if ( geometry == null )
					failures.Add( $"{theme}/{entry.ArchiveName}: no ground node" );
				else if ( geometry.Strips.Count != sides )
					failures.Add( $"{theme}/{entry.ArchiveName}: {geometry.Strips.Count} boards for {sides} sides" );
			}
		}
		Assert.AreEqual( 129, types, "the four themes ship 129 types with an Info.Hoarding block" );
		CollectionAssert.AreEqual( Array.Empty<string>(), failures, string.Join( "\n", failures ) );
	}

	[TestMethod]
	public void TheTotemsGroundNodeAndBoardsAreThePortsPinnedResult()
	{
		var entry = ObjectCatalog.Load( "jungle" ).Entries.Single( candidate => candidate.ArchiveName == "totem" && !candidate.IsBonus );
		var model = ModelOf( entry );
		var terrain = TerrainOf( "jungle" );
		var (count, node) = ObjectHoardingGeometry.SelectGroundNode( model, ObjectMapGrid.ShapeOf( entry.Settings ), terrain );
		Assert.AreEqual( (14, "tp_ground"), (count, model.Nodes[node].Name), "the ground vertices on the footprint's boundary corners and the node with most of them" );
		var geometry = ObjectHoardingGeometry.Build( entry.Settings, model, terrain )!;
		Assert.AreEqual( (3, 4, 12), (geometry.Width, geometry.Height, geometry.Strips.Count) );
		var first = geometry.Strips[0];
		Assert.AreEqual( -N.Vector3.UnitX, first.Normal, "the sweep starts at the west side's top cell" );
		Assert.AreEqual( 0.81020826f, first.Corners[0].X, 1e-6f );
		Assert.AreEqual( 30f, first.Corners[0].Z, 1e-6f );
		Assert.AreEqual( 20f, first.Corners[1].Z, 1e-6f );
	}

	[TestMethod]
	public void ABoardStandsHalfwayBetweenTheBaseAndTheTerrainCorner()
	{
		var entry = ObjectCatalog.Load( "jungle" ).Entries.Single( candidate => candidate.ArchiveName == "totem" && !candidate.IsBonus );
		var field = new ModelFile( "/levels/jungle/terrain/base.MD2" ).Heightfield!;
		var geometry = ObjectHoardingGeometry.Build( entry.Settings, ModelOf( entry ), HoardingTerrain.Of( field ) )!;
		var box = ObjectFootprint.GetBounds( entry.Shape, 40, 40, 0 );
		var bottoms = ObjectHoardingGeometry.Conform( geometry, field, 0, box, 3f );
		var strip = geometry.Strips[0];
		var (x, y) = ObjectHoardingGeometry.MapCorner( 0, box, (float)strip.EndA.Column / geometry.Width, (float)strip.EndA.Row / geometry.Height );
		Assert.AreEqual( (field.GetCornerHeight( x, y ) - 3f) * 0.5f, bottoms[0].BottomA, 1e-5f );
	}

	[TestMethod]
	public void EveryThemeShipsTheFourHoardingTextures()
	{
		foreach ( var theme in Themes )
		{
			foreach ( var name in ObjectHoardingGeometry.TextureNames )
			{
				var path = ObjectHoardingRenderer.ResolveTexture( theme, name );
				Assert.IsNotNull( path, $"{theme}: {name}" );
				StringAssert.EndsWith( path, ".wct" );
				var (data, width, height) = Texture.DecodeImage( FileSystem, path );
				Assert.AreEqual( (128, 128), (width, height), $"{theme}: {name}" );
				Assert.AreEqual( width * height * 4, data.Length );
			}
		}
	}

	[TestMethod]
	public void EveryThemeParkIncThemeShipsTheFourHoardingTextures()
	{
		var tpi = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( tpi ) || !Directory.Exists( Path.Combine( tpi, "Data", "levels" ) ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to the extracted Theme Park Inc CD." );
		var previous = FileSystem;
		try
		{
			FileSystem = new BaseFileSystem( Path.Combine( tpi, "Data" ) );
			FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			foreach ( var theme in new[] { "arabian", "science", "water" } )
			{
				foreach ( var name in ObjectHoardingGeometry.TextureNames )
				{
					var path = ObjectHoardingRenderer.ResolveTexture( theme, name );
					Assert.IsNotNull( path, $"{theme}: {name}" );
					StringAssert.EndsWith( path, ".fsh" );
					var (data, width, height) = Texture.DecodeImage( FileSystem, path );
					Assert.IsTrue( width > 0 && height > 0 && data.Length == width * height * 4, $"{theme}: {name}" );
				}
			}
		}
		finally
		{
			FileSystem = previous;
		}
	}

	/// <summary>A runtime whose loaded hoarding level was out of range captures a snapshot that validates (the park-resume save and the autosave serialize it).</summary>
	[TestMethod]
	public void ARuntimeWithAnOutOfRangeLoadedLevelCapturesAValidSnapshot()
	{
		var entry = ObjectCatalog.Load( "jungle" ).Entries.Single( item => item.InfoId == 1100 );
		var runtime = new OriginalObjectRuntime( entry, seed: 3 );
		runtime.Hoarding.Present = true;
		runtime.Hoarding.Restore( ObjectHoardingState.Standing | ObjectHoardingState.Rising | ObjectHoardingState.HoardingVariant, 1.5f );
		var snapshot = runtime.CaptureSnapshot();
		snapshot.ValidateSnapshot();
		Assert.AreEqual( 1f, snapshot.Hoarding!.Level );
	}

	/// <summary>The level's cache builds a type's boards once and loads the level's textures once.</summary>
	[TestMethod]
	public void TheLevelCacheBuildsATypesBoardsAndTheTexturesOnce()
	{
		var entry = ObjectCatalog.Load( "jungle" ).Entries.Single( item => item.InfoId == 1100 );
		var field = new ModelFile( "/levels/jungle/terrain/base.MD2" ).Heightfield!;
		var cache = new ObjectHoardingCache();
		var first = cache.Geometry( entry, ModelOf( entry ), field );
		Assert.IsNotNull( first );
		Assert.AreSame( first, cache.Geometry( entry, ModelOf( entry ), field ) );
		cache.Clear();
		Assert.AreNotSame( first, cache.Geometry( entry, ModelOf( entry ), field ), "a cleared cache builds again" );
	}
}
