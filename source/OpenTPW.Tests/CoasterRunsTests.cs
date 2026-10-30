using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// The run table of the track mesh without game data (COAST-026, tp.exe <c>0x4402d0</c>, <c>0x43f030</c>, <c>0x43fd60</c>): the select table, the transition runs, the slices at run keys and the
/// blend of a transition section's points. The run builder against the original itself is <see cref="CoasterRunsGoldenTests"/>.
/// </summary>
[TestClass]
public class CoasterRunsTests
{
	/// <summary>
	/// A flume-like table: section 0 (climbing, the pitch window −90..−1 degrees), section 1 (everything else) and the two transition sections 2 (0 to 1, blend length +1) and 3 (1 to 0, blend length −1),
	/// both blending point block 2 to point block 3 over the edge block 3 the plain sections do not use.
	/// </summary>
	private static Dictionary<string, string> FlumeSettings() => new( StringComparer.OrdinalIgnoreCase )
	{
		["asTextureData[0].pcTextureFilename"] = "\"dry.tga\"",
		["asTextureData[1].pcTextureFilename"] = "\"wet.tga\"",
		["asCrossSectionPoints1[0].fX"] = "-1", ["asCrossSectionPoints1[0].fY"] = "0", ["asCrossSectionPoints1[0].fU"] = "0", ["asCrossSectionPoints1[0].fNx"] = "0", ["asCrossSectionPoints1[0].fNy"] = "1",
		["asCrossSectionPoints1[1].fX"] = "1", ["asCrossSectionPoints1[1].fY"] = "0", ["asCrossSectionPoints1[1].fU"] = "1", ["asCrossSectionPoints1[1].fNx"] = "0", ["asCrossSectionPoints1[1].fNy"] = "1",
		["asCrossSectionPoints2[0].fX"] = "-2", ["asCrossSectionPoints2[0].fY"] = "0", ["asCrossSectionPoints2[0].fU"] = "0", ["asCrossSectionPoints2[0].fNx"] = "0", ["asCrossSectionPoints2[0].fNy"] = "1",
		["asCrossSectionPoints2[1].fX"] = "2", ["asCrossSectionPoints2[1].fY"] = "0", ["asCrossSectionPoints2[1].fU"] = "1", ["asCrossSectionPoints2[1].fNx"] = "0", ["asCrossSectionPoints2[1].fNy"] = "1",
		["asCrossSectionPoints3[0].fX"] = "-4", ["asCrossSectionPoints3[0].fY"] = "2", ["asCrossSectionPoints3[0].fU"] = "0", ["asCrossSectionPoints3[0].fNx"] = "0", ["asCrossSectionPoints3[0].fNy"] = "1",
		["asCrossSectionPoints3[1].fX"] = "4", ["asCrossSectionPoints3[1].fY"] = "2", ["asCrossSectionPoints3[1].fU"] = "1", ["asCrossSectionPoints3[1].fNx"] = "0", ["asCrossSectionPoints3[1].fNy"] = "1",
		["asCrossSectionEdges1[0].usTexture"] = "0", ["asCrossSectionEdges1[0].usCSPoint1"] = "0", ["asCrossSectionEdges1[0].usCSPoint2"] = "1",
		["asCrossSectionEdges2[0].usTexture"] = "1", ["asCrossSectionEdges2[0].usCSPoint1"] = "0", ["asCrossSectionEdges2[0].usCSPoint2"] = "1",
		["asCrossSectionEdges3[0].usTexture"] = "1", ["asCrossSectionEdges3[0].usCSPoint1"] = "0", ["asCrossSectionEdges3[0].usCSPoint2"] = "1",
		["asCrossSections[0].usCSPointBlockIndex"] = "1", ["asCrossSections[0].usCSEdgeBlockIndex"] = "2",
		["asCrossSections[1].usCSPointBlockIndex"] = "1", ["asCrossSections[1].usCSEdgeBlockIndex"] = "1",
		["asCrossSections[2].usCSPointBlockIndex"] = "2", ["asCrossSections[2].usCSEdgeBlockIndex"] = "3", ["asCrossSections[2].usBlendToCSPointBlockIndex"] = "3", ["asCrossSections[2].fBlendLength"] = "1.0",
		["asCrossSections[3].usCSPointBlockIndex"] = "3", ["asCrossSections[3].usCSEdgeBlockIndex"] = "3", ["asCrossSections[3].usBlendToCSPointBlockIndex"] = "2", ["asCrossSections[3].fBlendLength"] = "-1.0",
		["asCrossSectionSelects[0].usCrossSectionIndex"] = "1", ["asCrossSectionSelects[0].fPitchRangeMin"] = "-90", ["asCrossSectionSelects[0].fPitchRangeMax"] = "-1",
		["asCrossSectionSelects[1].usCrossSectionIndex"] = "2",
		["asCrossSectionSelects[2].usCrossSectionIndex"] = "3", ["asCrossSectionSelects[2].usBlendFromCrossSectionIndex"] = "1", ["asCrossSectionSelects[2].usBlendToCrossSectionIndex"] = "2",
		["asCrossSectionSelects[3].usCrossSectionIndex"] = "4", ["asCrossSectionSelects[3].usBlendFromCrossSectionIndex"] = "2", ["asCrossSectionSelects[3].usBlendToCrossSectionIndex"] = "1"
	};

	private static CoasterCrossSections Flume() => CoasterCrossSections.FromValues( FlumeSettings() );

	/// <summary>The select table walks every entry but the last (the transition entries here cannot be reached after the catch-all) and the windows are inclusive, in radians.</summary>
	[TestMethod]
	public void TheSelectTableTestsTheWindowsInclusivelyAndTheLastEntryIsTheFallback()
	{
		var sections = Flume();
		Assert.IsTrue( sections.HasTransitions );
		Assert.AreEqual( 0, sections.SelectAnswer( 0f, 0f, -0.5f, 0f ), "a climb takes the first window" );
		Assert.AreEqual( 0, sections.SelectAnswer( 0f, 0f, -1f * 0.017453292f, 0f ), "the window's end value is inside" );
		Assert.AreEqual( 1, sections.SelectAnswer( 0f, 0f, 0f, 0f ), "flat track falls through to the catch-all select" );
		Assert.AreEqual( 1, sections.SelectAnswer( 0f, 0f, 0.4f, 0f ) );
		Assert.AreEqual( 2, sections.SelectTransition( 0, 1 ) );
		Assert.AreEqual( 3, sections.SelectTransition( 1, 0 ) );
		Assert.AreEqual( -1, sections.SelectTransition( 1, 1 ) );
		// every pair of windows counts: a select with a bank window only
		var values = FlumeSettings();
		values["asCrossSectionSelects[0].fPitchRangeMin"] = "0";
		values["asCrossSectionSelects[0].fPitchRangeMax"] = "0";
		values["asCrossSectionSelects[0].fBankRangeMin"] = "10";
		values["asCrossSectionSelects[0].fBankRangeMax"] = "20";
		var bank = CoasterCrossSections.FromValues( values );
		Assert.AreEqual( 1, bank.SelectAnswer( 0f, 0f, -0.5f, 0f ), "a pair of zeros names no window" );
		Assert.AreEqual( 0, bank.SelectAnswer( 0f, 0f, 0f, 15 * 0.017453292f ) );
		Assert.AreEqual( 1, bank.SelectAnswer( 0f, 0f, 0f, 25 * 0.017453292f ) );
	}

	/// <summary>A synthetic frame: flat, then climbing from sample coordinate 20 to 30, then flat again.</summary>
	private static CoasterExtrusionRunBuilder.Frame HillFrame( float position ) => new( 0f, position >= 20f && position < 30f ? -0.2f : 0f, 0f );

	private static float WholeKey( float position ) => position / 10f;

	[TestMethod]
	public void ARunIsWrittenAtEveryChangeAndTheTableEndsWithTheSentinel()
	{
		var values = FlumeSettings();
		// no section blends: the flag clear, a plain change
		foreach ( var key in values.Keys.Where( key => key.Contains( "Blend", StringComparison.OrdinalIgnoreCase ) && key.StartsWith( "asCrossSections", StringComparison.OrdinalIgnoreCase ) ).ToArray() )
			values.Remove( key );
		var plain = CoasterCrossSections.FromValues( values );
		Assert.IsFalse( plain.HasTransitions );
		var runs = CoasterExtrusionRunBuilder.Build( plain, 40f, HillFrame, WholeKey );
		// the section before the first change is the first select's (the climbing one), the track is flat at 0..20 (the catch-all), climbs at 20..30, flat again
		CollectionAssert.AreEqual( new[] { 0, 1, 0, 1 }, runs.Select( run => run.Section ).ToArray(), "climb (the table's first answer), flat, climb, flat" );
		Assert.AreEqual( 0f, runs[0].Position, "the first run ends where the first change is found, here the very start" );
		Assert.AreEqual( 20f, runs[1].Position );
		Assert.AreEqual( 2f, runs[1].Key, 1e-6f );
		Assert.AreEqual( 30f, runs[2].Position );
		Assert.AreEqual( CoasterExtrusionRunBuilder.EndKey, runs[^1].Key );
		Assert.AreEqual( 40f + 0.25f + 1f, runs[^1].Position, "the sentinel's position is the position after the last step plus 1" );
	}

	[TestMethod]
	public void APositiveTransitionBlendsAfterTheChangeAndANegativeOneBeforeIt()
	{
		var sections = Flume();
		// flat track (answer 1), a climb from 20 to 30 (answer 0), flat again; the table starts with answer 0
		var runs = CoasterExtrusionRunBuilder.Build( sections, 60f, HillFrame, WholeKey );
		// at 0 the answer 1 differs from the first select's 0: the transition 0 -> 1 (length +1) follows the change at 0; at 20 the climb is 1 -> 0 (length -1): its blend lies before the change; at 30 the flat track is 0 -> 1 (+1) again
		var table = runs.Select( run => (run.Section, run.Position, run.Weight) ).ToArray();
		CollectionAssert.AreEqual( new[] { 0, 2, 1, 3, 0, 2, 1 }, table.Select( run => run.Section ).ToArray(), string.Join( ", ", table ) );
		Assert.AreEqual( 0f, table[0].Position );
		Assert.AreEqual( 1f, table[1].Position, "a positive length of 1 opens a blend that ends one sample coordinate after the change" );
		Assert.AreEqual( 1f, table[1].Weight );
		Assert.AreEqual( 19f, table[2].Position, "the negative blend takes the stretch before the change: the flat run now ends one before 20" );
		Assert.AreEqual( 20f, table[3].Position );
		Assert.AreEqual( 1f, table[3].Weight );
	}

	/// <summary>The blend weight of a ring: the coordinate's place between the two run positions, times the weight, plus 1 − weight for a positive length, clamped.</summary>
	[TestMethod]
	public void TheBlendWeightFollowsTheCoordinateBetweenTheRunPositions()
	{
		var forward = new CoasterCrossSection( 2, 3, 1, 3, 1f );
		var backward = new CoasterCrossSection( 3, 3, 1, 2, -1f );
		var runs = new List<CoasterExtrusionRun> { new( 0f, 10f, 0f, 1 ), new( 0f, 11f, 1f, 2 ) };
		Assert.AreEqual( 0f, CoasterTrackMesh.BlendWeight( forward, runs, 1, 10f ) );
		Assert.AreEqual( 0.25f, CoasterTrackMesh.BlendWeight( forward, runs, 1, 10.25f ) );
		Assert.AreEqual( 1f, CoasterTrackMesh.BlendWeight( forward, runs, 1, 12f ), "clamped to 1" );
		Assert.AreEqual( 0f, CoasterTrackMesh.BlendWeight( backward, runs, 1, 9f ), "clamped to 0" );
		// a merged run keeps a weight below 1: a positive length starts from 1 − weight, a negative one from 0
		var merged = new List<CoasterExtrusionRun> { new( 0f, 10f, 0f, 1 ), new( 0f, 12f, 0.5f, 2 ) };
		Assert.AreEqual( 0.5f, CoasterTrackMesh.BlendWeight( forward, merged, 1, 10f ) );
		Assert.AreEqual( 0.75f, CoasterTrackMesh.BlendWeight( forward, merged, 1, 11f ) );
		Assert.AreEqual( 0f, CoasterTrackMesh.BlendWeight( backward, merged, 1, 10f ) );
		Assert.AreEqual( 0.25f, CoasterTrackMesh.BlendWeight( backward, merged, 1, 11f ) );
		var blended = CoasterTrackMesh.Blend( new CoasterSectionPoint( 0, 0, 0, 0, 1 ), new CoasterSectionPoint( 4, 2, 1, 1, 0 ), 0.25f );
		Assert.AreEqual( new CoasterSectionPoint( 1f, 0.5f, 0.25f, 0.25f, 0.75f ), blended, "all five floats blend" );
	}

	private static CoasterKnot[] Straight( int count ) =>
		Enumerable.Range( 0, count ).Select( index => new CoasterKnot( new SVector3( 0, 20, index * 20f ), SVector3.UnitZ, 0f, index ) ).ToArray();

	/// <summary>The run keys inside a link become slices of their own, at the key's place on the pair, with the lerped sample coordinate.</summary>
	[TestMethod]
	public void ARunKeyInsideALinkGetsASliceOfItsOwn()
	{
		var knots = Straight( 4 );
		var coordinates = new[] { 0f, 10f, 20f, 30f };
		var runs = new List<CoasterExtrusionRun> { new( 1.25f, 12.5f, 0f, 0 ), new( 1.3f, 13f, 0f, 1 ), new( 1.5f, 15f, 0f, 1 ), new( 2.0f, 20f, 0f, 1 ), new( CoasterExtrusionRunBuilder.EndKey, 31f, 0f, 1 ) };
		var slices = HermiteCoasterCurve.Slices( knots, 1f, closed: false, keys: new CoasterSliceKeys( runs, coordinates ) );
		var keys = slices.Select( slice => slice.Key ).ToArray();
		var around = keys.Where( key => key > 1.1f && key < 1.6f ).ToArray();
		// the grid has 1.2, 1.3, 1.4, 1.5 and 1.6; 1.25 is inserted between 1.2 and 1.3 (1.3 and 1.5 equal a grid value up to the single's rounding: only a key that is not the grid value is inserted)
		Assert.IsTrue( around.Contains( 1.25f ), string.Join( ", ", around ) );
		Assert.AreEqual( keys.OrderBy( key => key ).ToArray().Length, keys.Length );
		CollectionAssert.AreEqual( keys.OrderBy( key => key ).ToArray(), keys, "keys ascend along the chain" );
		var inserted = slices.Single( slice => slice.Key == 1.25f );
		Assert.AreEqual( 0.25f, inserted.T, 1e-6f );
		Assert.AreEqual( 12.5f, inserted.Coordinate, 1e-4f, "the coordinate is the lerp of the two knots' coordinates" );
		Assert.AreEqual( 20f + 20f * 0.25f, inserted.Position.Z, 1e-3f );
		// without keys the slices are the plain grid
		Assert.IsTrue( HermiteCoasterCurve.Slices( knots, 1f, closed: false ).All( slice => float.IsNaN( slice.Key ) ) );
	}

	/// <summary>A transition run's rings blend the points: the ring at the start of the run holds the section's own row, the ring at its end the blend-to row.</summary>
	[TestMethod]
	public void ATransitionRunBlendsItsRingsAlongTheSampleCoordinate()
	{
		var sections = Flume();
		var slices = Enumerable.Range( 0, 5 ).Select( index => new CoasterSlice( new SVector3( 0, 10, index * 3f ), 0f, 0f, 0f, 0, 0f, 0, index, 10f + index * 0.25f ) ).ToArray();
		// the run before the transition ends at 10, the transition run (section 2: block 2 to block 3) at 11; the slices with keys 1..3 (coordinates 10.25, 10.5, 10.75) belong to it, the one with key 4 to the plain run after it
		var runs = new List<CoasterExtrusionRun> { new( 0.5f, 10f, 0f, 1 ), new( 3.5f, 11f, 1f, 2 ), new( CoasterExtrusionRunBuilder.EndKey, 20f, 0f, 1 ) };
		var mesh = CoasterTrackMesh.Build( sections, slices, closed: false, runs: runs );
		// rings: the transition section's own points (X = −2, 2, Y = 0) to the blend-to row (X = −4, 4, Y = 2); the ring at coordinate 10 + k/4 has the weight k/4, the last ring is the plain section's (X = −1)
		var xs = mesh.Vertices.Select( vertex => Math.Round( vertex.Position.X, 4 ) ).Where( x => x < 0 ).Distinct().OrderBy( x => x ).ToArray();
		CollectionAssert.AreEqual( new[] { -3.5, -3.0, -2.5, -2.0, -1.0 }, xs, string.Join( ", ", xs ) );
		var ys = mesh.Vertices.Where( vertex => vertex.Position.X < 0 ).Select( vertex => Math.Round( vertex.Position.Y, 4 ) ).Distinct().OrderBy( y => y ).ToArray();
		Assert.AreEqual( 10.0, ys[0], 1e-3 );
		Assert.AreEqual( 11.5, ys[^1], 1e-3 );
		// without the run table the same slices draw the plain section by the pitch select
		var plain = CoasterTrackMesh.Build( sections, slices.Select( slice => slice with { Key = float.NaN, Coordinate = float.NaN } ).ToArray(), closed: false );
		Assert.AreEqual( 1, plain.Vertices.Select( vertex => Math.Round( vertex.Position.X, 4 ) ).Where( x => x < 0 ).Distinct().Count() );
	}

	/// <summary>A change of run emits the previous slice's ring again, so the strips of a section never join another section's ring.</summary>
	[TestMethod]
	public void AChangeOfRunEmitsTheRingAgain()
	{
		var sections = Flume();
		var slices = Enumerable.Range( 0, 5 ).Select( index => new CoasterSlice( new SVector3( 0, 10, index * 3f ), 0f, 0f, 0f, 0, 0f, 0, index, index ) ).ToArray();
		var one = new List<CoasterExtrusionRun> { new( 100f, 100f, 0f, 1 ), new( CoasterExtrusionRunBuilder.EndKey, 101f, 0f, 1 ) };
		var two = new List<CoasterExtrusionRun> { new( 2f, 2f, 0f, 1 ), new( CoasterExtrusionRunBuilder.EndKey, 101f, 0f, 0 ) };
		var steady = CoasterTrackMesh.Build( sections, slices, closed: false, runs: one );
		var changed = CoasterTrackMesh.Build( sections, slices, closed: false, runs: two );
		Assert.AreEqual( steady.Rings + 1, changed.Rings, "the slice at key 2 is the last of the first run: the next one moves the pointer" );
		Assert.IsTrue( changed.Vertices.Select( vertex => vertex.Texture ).Distinct().Count() > steady.Vertices.Select( vertex => vertex.Texture ).Distinct().Count() - 1 );
	}
}
