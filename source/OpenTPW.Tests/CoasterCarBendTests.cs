using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

using Vector3 = System.Numerics.Vector3;

/// <summary>The bend of a bend car's mesh along the track under it (COAST-034, tp.exe <c>0x438800</c>, <c>0x438ab0</c>).</summary>
[TestClass]
public class CoasterCarBendTests
{
	private const float Spacing = 3f;

	/// <summary>Samples on a straight line along +Z (the car's forward axis), a sample every 3 units.</summary>
	private static CoasterSample[] Straight( int count ) =>
		Enumerable.Range( 0, count ).Select( index => new CoasterSample { Position = new Vector3( 0, 5, index * Spacing ), Yaw = 0, Pitch = 0 } ).ToArray();

	/// <summary>Samples on a horizontal circle of the given radius turning toward +X (yaw growing), 3 units of arc apart.</summary>
	private static CoasterSample[] Arc( int count, float radius ) =>
		Enumerable.Range( 0, count ).Select( index =>
		{
			var angle = index * Spacing / radius;
			return new CoasterSample { Position = new Vector3( radius * (1 - MathF.Cos( angle )), 5, radius * MathF.Sin( angle ) ), Yaw = angle };
		} ).ToArray();

	/// <summary>The car's direction at a coordinate of an arc.</summary>
	private static Vector3 ArcHeading( float coordinate, float radius )
	{
		var angle = coordinate * Spacing / radius;
		return new Vector3( MathF.Sin( angle ), 0, MathF.Cos( angle ) );
	}

	[TestMethod]
	public void AStraightTrackBendsNothingAndOnlyAddsTheElevation()
	{
		var samples = Straight( 12 );
		// rear extent 5 → trunc(5 / 3) + 1 = 2 samples behind, front extent 4 → 2 ahead
		var tables = CoasterCarBend.Tables( samples, Spacing, 0.5f, 2f, 5f, 4f, Vector3.UnitZ, 5.3f );
		Assert.AreEqual( (2, 5), (tables.Rear, tables.Count) );
		for ( var index = 0; index < tables.Count; index++ )
		{
			Assert.AreEqual( 0f, tables.Yaw[index], 1e-4f, $"yaw {index}" );
			Assert.AreEqual( 0f, tables.Pitch[index], 1e-3f, $"pitch {index}" );
			Assert.AreEqual( 0.5f, tables.Elevation[index], 1e-5f, $"elevation {index}: fBendElevationLow" );
		}
		var vertex = new Vector3( 0.7f, 1.2f, 2.5f );
		var bent = CoasterCarBend.Bend( tables, Spacing, vertex );
		Assert.AreEqual( vertex.X, bent.X, 1e-3f );
		Assert.AreEqual( vertex.Y + 0.5f, bent.Y, 1e-3f, "the car rises by the elevation" );
		Assert.AreEqual( vertex.Z, bent.Z, 1e-3f );
	}

	[TestMethod]
	public void ASampleOnTheChainLiftRaisesTheCarByTheHighElevation()
	{
		var samples = Straight( 12 );
		samples[6].Flags = CoasterSampleFlags.Friction;
		var tables = CoasterCarBend.Tables( samples, Spacing, 0.5f, 2f, 5f, 4f, Vector3.UnitZ, 5.25f );
		// entries: samples 3 and 4 behind, the car's own at sample 5 + 0.25, then samples 6 and 7: each lerps its flag with the next sample's by the fraction
		Assert.AreEqual( 0.5f, tables.Elevation[0], 1e-5f, "sample 3: no flag" );
		Assert.AreEqual( 0.5f, tables.Elevation[1], 1e-5f, "sample 4: no flag, nor sample 5" );
		Assert.AreEqual( 0.5f * 0.75f + 2f * 0.25f, tables.Elevation[2], 1e-5f, "the car's own entry: sample 5 against sample 6 (flag 8)" );
		Assert.AreEqual( 2f * 0.75f + 0.5f * 0.25f, tables.Elevation[3], 1e-5f, "sample 6 (flag 8) against sample 7" );
		Assert.AreEqual( 0.5f, tables.Elevation[4], 1e-5f, "sample 7: no flag, nor sample 8" );
	}

	[TestMethod]
	public void ACurveTurnsTheFrontOneWayAndTheRearTheOther()
	{
		const float Radius = 20f;
		var samples = Arc( 30, Radius );
		var coordinate = 12.4f;
		var tables = CoasterCarBend.Tables( samples, Spacing, 0, 0, 9f, 9f, ArcHeading( coordinate, Radius ), coordinate );
		Assert.AreEqual( (4, 9), (tables.Rear, tables.Count) );
		Assert.AreEqual( 0f, tables.Yaw[tables.Rear], 1e-6f, "the car's own entry bends nothing" );
		for ( var step = 1; step <= 3; step++ )
		{
			Assert.IsTrue( tables.Yaw[tables.Rear + step] > 0, $"ahead {step}: the track turns toward +X" );
			Assert.IsTrue( tables.Yaw[tables.Rear - step] < 0, $"behind {step}: the track turns back the other way" );
			Assert.AreEqual( 0f, tables.Pitch[tables.Rear + step], 1e-2f, "a flat curve has no pitch" );
		}
		Assert.IsTrue( tables.Yaw[tables.Rear + 2] > tables.Yaw[tables.Rear + 1] && tables.Yaw[tables.Rear + 3] > tables.Yaw[tables.Rear + 2], "the further, the more it turns" );
		Assert.IsTrue( tables.Yaw[tables.Rear - 2] < tables.Yaw[tables.Rear - 1] && tables.Yaw[tables.Rear - 3] < tables.Yaw[tables.Rear - 2] );
		// an arc of radius R: the point s ahead is turned by about s / 2R from the car's heading
		Assert.AreEqual( Spacing / (2 * Radius), tables.Yaw[tables.Rear + 1], 0.02f, "one sample of arc ahead" );

		// the mesh follows: a vertex ahead of the pivot swings toward +X, one behind it too (the curve is on that side of the car's line), the pivot stays
		var front = CoasterCarBend.Bend( tables, Spacing, new Vector3( 0, 0, 3f ) );
		var rear = CoasterCarBend.Bend( tables, Spacing, new Vector3( 0, 0, -3f ) );
		var middle = CoasterCarBend.Bend( tables, Spacing, new Vector3( 0, 0, 0 ) );
		Assert.IsTrue( front.X > 0.05f && rear.X > 0.05f, $"front {front}, rear {rear}" );
		Assert.AreEqual( 0f, middle.X, 1e-3f );
	}

	[TestMethod]
	public void ACrestPitchesTheFrontDownAndTheRearTheOtherWay()
	{
		// a hill: heights fall away on both sides of the car, heading along +Z
		var samples = Enumerable.Range( 0, 30 ).Select( index =>
		{
			var z = index * Spacing;
			var y = 20 - 0.02f * (z - 45) * (z - 45);
			return new CoasterSample { Position = new Vector3( 0, y, z ), Yaw = 0 };
		} ).ToArray();
		var coordinate = 15.0f;
		var tables = CoasterCarBend.Tables( samples, Spacing, 0, 0, 9f, 9f, Vector3.UnitZ, coordinate );
		Assert.AreEqual( 0f, tables.Yaw[tables.Rear + 1], 1e-3f, "no turn on a straight hill" );
		Assert.IsTrue( tables.Pitch[tables.Rear + 1] > 0f && tables.Pitch[tables.Rear + 2] > tables.Pitch[tables.Rear + 1], "ahead: the track falls away" );
		Assert.IsTrue( tables.Pitch[tables.Rear - 1] < 0f && tables.Pitch[tables.Rear - 2] < tables.Pitch[tables.Rear - 1], "behind: the track falls away the other way" );
	}

	[TestMethod]
	public void TheTablesZeroWhatTheTrackDoesNotHave()
	{
		var samples = Arc( 12, 20f );
		// at the start of the list there is nothing behind the car: the leading entries are zero
		var start = CoasterCarBend.Tables( samples, Spacing, 0.5f, 2f, 9f, 9f, ArcHeading( 0.4f, 20f ), 0.4f );
		Assert.AreEqual( (4, 9), (start.Rear, start.Count) );
		for ( var index = 0; index < 4; index++ )
			Assert.AreEqual( (0f, 0f, 0f), (start.Yaw[index], start.Pitch[index], start.Elevation[index]), "no samples behind" );
		Assert.IsTrue( start.Yaw[5] > 0, "the track ahead is there" );
		// near the end the entries past the last sample are zero, elevation included
		var end = CoasterCarBend.Tables( samples, Spacing, 0.5f, 2f, 9f, 9f, ArcHeading( 10.5f, 20f ), 10.5f );
		Assert.AreEqual( 9, end.Count );
		Assert.AreEqual( 0f, end.Elevation[end.Count - 1], "past the last sample" );
		Assert.IsTrue( end.Elevation[end.Rear] > 0.4f );
		// a car with no track under it bends nothing
		var none = CoasterCarBend.Tables( Array.Empty<CoasterSample>(), Spacing, 0.5f, 2f, 9f, 9f, Vector3.UnitZ, 0 );
		Assert.AreEqual( 0, none.Count );
		var vertex = new Vector3( 1, 2, 3 );
		Assert.AreEqual( vertex, CoasterCarBend.Bend( none, Spacing, vertex ) );
	}
}
