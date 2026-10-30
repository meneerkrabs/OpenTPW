using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>The run table on the original flume's cross sections (inconclusive without OPENTPW_GAME_PATH): COAST-026, tp.exe <c>0x4402d0</c>, <c>0x43fd60</c>.</summary>
[TestClass]
public class CoasterRunsDataTests
{
	private BaseFileSystem previousFileSystem = null!;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Init()
	{
		previousFileSystem = FileSystem;
		previousLog = Log;
		Log = new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original coaster archives." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previousFileSystem;
		Log = previousLog;
	}

	private static IEnumerable<ObjectCatalogEntry> PcCoasters() =>
		ObjectCatalog.Themes.SelectMany( theme => ObjectCatalog.Load( theme ).Entries ).Where( entry => !entry.IsBonus && CoasterDefinition.IsCoaster( entry.Settings ) );

	/// <summary>A flat run in, a climb of 20 units over 100, a flat top and the way back: the heights of six nodes along Z.</summary>
	private static CoasterKnot[] Hill()
	{
		var heights = new[] { 20f, 20f, 20f, 40f, 60f, 60f, 40f, 20f, 20f, 20f };
		var knots = new List<CoasterKnot>();
		for ( var index = 0; index < heights.Length; index++ )
		{
			var before = heights[Math.Max( index - 1, 0 )];
			var after = heights[Math.Min( index + 1, heights.Length - 1 )];
			var direction = SVector3.Normalize( new SVector3( 0, (after - before) / Math.Max( 1, Math.Min( index + 1, heights.Length - 1 ) - Math.Max( index - 1, 0 ) ), 40f ) );
			knots.Add( new CoasterKnot( new SVector3( 300, heights[index], 300 + index * 40f ), direction, 0f, index ) );
		}
		// the end node stands on the first one
		knots[^1] = knots[^1] with { Position = knots[0].Position };
		return knots.ToArray();
	}

	/// <summary>
	/// The Jungle flume's selects pick the dry section (1) while the track climbs and the wet one (2) elsewhere; each change goes through the transition sections (3, 4) of its direction, whose
	/// weights ramp over their length (+1 after the change, −1 before it).
	/// </summary>
	[TestMethod]
	public void TheFlumeClimbsThroughTheDryAndWetTransitions()
	{
		var flume = PcCoasters().Single( entry => entry.InfoId == 1185 );
		var sections = CoasterCrossSections.FromSettings( flume.CoasterSettings );
		Assert.IsTrue( sections.HasTransitions );
		var knots = Hill();
		var curve = HermiteCoasterCurve.Instance;
		var sampling = curve.Sample( knots, 1.2f );
		var runs = HermiteCoasterCurve.ExtrusionRuns( sections, knots, 1.2f, sampling );
		Assert.IsTrue( runs.Count >= 5, string.Join( "; ", runs ) );
		var order = runs.Select( run => run.Section ).Where( section => section >= 0 ).ToArray();
		Assert.IsTrue( order.Contains( 2 ) && order.Contains( 3 ), $"a transition each way: {string.Join( ' ', order )}" );
		Assert.IsTrue( runs.Zip( runs.Skip( 1 ), ( a, b ) => a.Key <= b.Key ).All( x => x ), "the keys never fall" );
		Assert.AreEqual( CoasterExtrusionRunBuilder.EndKey, runs[^1].Key );
		// the slices at the run keys and the mesh: a ring of a transition run holds points between the two blocks' extremes
		var slices = HermiteCoasterCurve.Slices( knots, 1.2f, closed: false, keys: new CoasterSliceKeys( runs, sampling.KnotCoordinates ) );
		Assert.IsTrue( slices.All( slice => !float.IsNaN( slice.Key ) && !float.IsNaN( slice.Coordinate ) ) );
		var mesh = CoasterTrackMesh.Build( sections, slices, closed: false, runs: runs );
		Assert.IsTrue( mesh.TriangleCount > 0 );
		var fromPoints = sections.Resolve( 2 ).Points;
		var toPoints = sections.PointBlocks[sections.Sections[2].BlendToPointBlock - 1];
		Assert.AreEqual( fromPoints.Count, toPoints.Count );
		var low = fromPoints.Concat( toPoints ).Min( point => point.Y );
		var high = fromPoints.Concat( toPoints ).Max( point => point.Y );
		Assert.IsTrue( high > low, "the two rows differ" );
		var plain = CoasterTrackMesh.Build( sections, HermiteCoasterCurve.Slices( knots, 1.2f, closed: false ), closed: false );
		Assert.AreNotEqual( plain.Vertices.Count, mesh.Vertices.Count, "the transition rings differ from the per-segment selection" );
		// the transition runs' rings: more rings than the plain selection, each run change emitting the previous ring again
		Assert.IsTrue( mesh.Rings > plain.Rings, $"{mesh.Rings} rings against {plain.Rings}" );
		// the two rows of the blend differ at some points (the water's)
		Assert.IsTrue( sections.Resolve( 2 ).Points.Zip( toPoints, ( own, other ) => own.Y != other.Y ).Any( differs => differs ), "the two rows differ at some points" );
	}

	/// <summary>The other PC coasters have no transition sections: their run tables hold only the changes of the select table.</summary>
	[TestMethod]
	public void CoastersWithoutBlendsWriteRunsWithoutWeights()
	{
		var knots = Hill();
		var sampling = HermiteCoasterCurve.Instance.Sample( knots, 1.2f );
		foreach ( var entry in PcCoasters().Where( entry => entry.InfoId != 1185 ) )
		{
			var sections = CoasterCrossSections.FromSettings( entry.CoasterSettings );
			Assert.IsFalse( sections.HasTransitions, $"{entry.InfoId}" );
			var runs = HermiteCoasterCurve.ExtrusionRuns( sections, knots, 1.2f, sampling );
			Assert.IsTrue( runs.Count >= 1, $"{entry.InfoId}" );
			Assert.IsTrue( runs.All( run => run.Weight == 0 ), $"{entry.InfoId}: no transition, no weight" );
		}
	}
}
