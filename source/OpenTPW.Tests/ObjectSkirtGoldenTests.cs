using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// The skirt routines against what tp.exe produced for synthetic inputs (<see cref="ObjectSkirtSyntheticGolden"/>, generated from the x86 oracle, docs/reverse/ORACLE.md X28 to X32): the edge mask
/// (<c>0x0052C420</c>), the coordinate pass (<c>0x00467310</c>), the skirt reader (<c>0x0046AA10</c>), the edge conform (<c>0x00469A80</c>) and the base height (<c>0x00466D70</c>). Floats are compared by their bits.
/// </summary>
[TestClass]
public class ObjectSkirtGoldenTests
{
	private static int Bits( float value ) => BitConverter.SingleToInt32Bits( value );

	private static void AssertBits( float[] expected, IEnumerable<float> actual, string name )
	{
		var array = actual.ToArray();
		Assert.AreEqual( expected.Length, array.Length, name );
		for ( var index = 0; index < expected.Length; index++ )
			Assert.AreEqual( Bits( expected[index] ), Bits( array[index] ), $"{name}: element {index} ({expected[index]:R} against {array[index]:R})" );
	}

	/// <summary>The 20-wide cell array of a golden shape (row-major with its own width as the stride).</summary>
	private static SkirtShape ShapeOf( int width, int height, int[] types )
	{
		var cells = new int[SkirtShape.Stride * 20];
		for ( var row = 0; row < height; row++ )
			for ( var column = 0; column < width; column++ )
				cells[row * SkirtShape.Stride + column] = types[row * width + column];
		return new SkirtShape( width, height, cells );
	}

	private static N.Vector3[] Points( float[] flat ) => Enumerable.Range( 0, flat.Length / 3 ).Select( index => new N.Vector3( flat[index * 3], flat[index * 3 + 1], flat[index * 3 + 2] ) ).ToArray();

	private static int[][] FaceList( int[] flat ) => Enumerable.Range( 0, flat.Length / 3 ).Select( index => new[] { flat[index * 3], flat[index * 3 + 1], flat[index * 3 + 2] } ).ToArray();

	private static SkirtGroup[] GroupList( int[] flat ) => Enumerable.Range( 0, flat.Length / 3 ).Select( index => new SkirtGroup( flat[index * 3], flat[index * 3 + 1], flat[index * 3 + 2] ) ).ToArray();

	[TestMethod]
	public void TheEdgeMaskIsWhatTheOriginalWrote()
	{
		Assert.IsTrue( ObjectSkirtSyntheticGolden.MaskCases.Length >= 30 );
		foreach ( var golden in ObjectSkirtSyntheticGolden.MaskCases )
		{
			var expected = new byte[SkirtReading.MaskSpan];
			BitConverter.TryWriteBytes( expected.AsSpan( 256, 4 ), golden.Width );
			BitConverter.TryWriteBytes( expected.AsSpan( 260, 4 ), golden.Height );
			foreach ( var index in golden.Ones )
				expected[index] = 1;
			CollectionAssert.AreEqual( expected, SkirtReading.Mask( ShapeOf( golden.Width, golden.Height, golden.Types ) ), golden.Name );
		}
	}

	[TestMethod]
	public void TheCoordinatePassIsWhatTheOriginalDid()
	{
		Assert.IsTrue( ObjectSkirtSyntheticGolden.UvCases.Length >= 30 );
		foreach ( var golden in ObjectSkirtSyntheticGolden.UvCases )
		{
			var uvs = Enumerable.Range( 0, golden.Uv.Length / 2 ).Select( index => new N.Vector2( golden.Uv[index * 2], golden.Uv[index * 2 + 1] ) ).ToArray();
			var node = new SkirtNode( ObjectFloorMath.Identity, [], [], uvs, golden.CornerVertex, FaceList( golden.Faces ), GroupList( golden.Groups ) );
			var (actual, slots, logged) = SkirtReading.NormaliseCoordinates( node, golden.SlotsIn.Select( slot => (uint)slot ).ToArray(), golden.Mask, golden.Mask == null ? null : Points( golden.Vertices ), golden.Eps );
			AssertBits( golden.ExpectedUv, actual.SelectMany( uv => new[] { uv.X, uv.Y } ), golden.Name + " uv" );
			CollectionAssert.AreEqual( golden.ExpectedSlots, slots.Select( slot => (int)slot ).ToArray(), golden.Name + " slots" );
			Assert.AreEqual( golden.ExpectedLogged, logged, golden.Name + " logged" );
		}
	}

	[TestMethod]
	public void TheSkirtReaderIsWhatTheOriginalDid()
	{
		Assert.IsTrue( ObjectSkirtSyntheticGolden.FlagsCases.Length >= 30 );
		var cells = 0;
		foreach ( var golden in ObjectSkirtSyntheticGolden.FlagsCases )
		{
			var parents = Enumerable.Range( 0, golden.Parents.Length / 16 ).Select( index => golden.Parents.AsSpan( index * 16, 16 ).ToArray() ).ToList();
			var uvs = Enumerable.Range( 0, golden.Uv.Length / 2 ).Select( index => new N.Vector2( golden.Uv[index * 2], golden.Uv[index * 2 + 1] ) ).ToArray();
			var node = new SkirtNode( golden.Matrix, parents, Points( golden.Vertices ), uvs, golden.CornerVertex, FaceList( golden.Faces ), GroupList( golden.Groups ) );
			var result = SkirtReading.Read( node, ShapeOf( golden.Width, golden.Height, golden.Types ), golden.SlotsIn.Select( slot => (uint)slot ).ToArray(), golden.Header80, golden.CellX, golden.CellZ );
			CollectionAssert.AreEqual( golden.ExpectedEntries, result.Entries.SelectMany( entry => new[] { entry.Code, entry.Slot } ).ToArray(), golden.Name + " entries" );
			CollectionAssert.AreEqual( golden.ExpectedSlots, result.Slots.Select( slot => (int)slot ).ToArray(), golden.Name + " slots" );
			AssertBits( golden.ExpectedUv, result.Uvs.SelectMany( uv => new[] { uv.X, uv.Y } ), golden.Name + " uv" );
			Assert.AreEqual( golden.ExpectedErrors, result.Errors, golden.Name + " errors" );
			Assert.AreEqual( (golden.ExpectedFlipped, golden.ExpectedUnflipped), (result.Flipped, result.NotFlipped), golden.Name + " flipped" );
			cells += result.Entries.Count( entry => entry.HasSkirt );
		}
		Assert.IsTrue( cells > 100, "the cases give cells a skirt" );
	}

	[TestMethod]
	public void TheEdgeConformIsWhatTheOriginalDid()
	{
		var ran = 0;
		var vertices = 0;
		foreach ( var golden in ObjectSkirtSyntheticGolden.ConformCases )
		{
			// the drape branch (node flag 0x20000: queue models and track drapes) is the drape pass's, not this walk's
			if ( golden.Nodes.Any( node => (node.Flags & 0x20000) != 0 ) )
				continue;
			var terrain = new HoardingTerrain( golden.OriginX, golden.OriginZ, golden.CellX, golden.CellZ, golden.CellsX, golden.CellsZ );
			var tree = golden.Nodes.Select( node => new ObjectGroundConform.TreeNode( node.Parent, node.Flags, node.Matrix, Points( node.Vertices ) ) { Bounds = (float[])node.Bounds.Clone() } ).ToList();
			var stride = golden.CellsX + 1;
			ObjectGroundConform.Walk( tree, golden.List, terrain, ( x, y ) => golden.Heights[stride * y + x], ObjectFloorMath.Identity );
			for ( var index = 0; index < tree.Count; index++ )
			{
				AssertBits( golden.Nodes[index].VerticesOut, tree[index].Result.SelectMany( point => new[] { point.X, point.Y, point.Z } ), $"{golden.Name} node {index} vertices" );
				AssertBits( golden.Nodes[index].BoundsOut, tree[index].Bounds, $"{golden.Name} node {index} bounds" );
				vertices += tree[index].Result.Length;
			}
			ran++;
		}
		Assert.IsTrue( ran >= 20 && vertices > 100, $"{ran} conform cases ran" );
	}

	private static char SymbolOf( int type ) => type switch { 0 => '.', 1 => '@', 3 => 'Q', 4 => '*', 9 => '8', 10 => 'N', 11 => '+', 16 => '#', 23 => 'D', _ => throw new InvalidOperationException( $"type {type}" ) };

	private sealed class HeightGrid( int cellsX, float[] heights ) : IParkGrid
	{
		public int Width => cellsX;
		public int Height => cellsX;
		public N.Vector2 Origin => N.Vector2.Zero;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => GetCornerHeight( x, y );
		public float GetCornerHeight( int x, int y ) => heights[(cellsX + 1) * y + x];
	}

	[TestMethod]
	public void TheBaseHeightIsWhatTheOriginalReturned()
	{
		var different = new List<string>();
		foreach ( var golden in ObjectSkirtSyntheticGolden.BaseHeightCases )
		{
			// the golden's rows run from the bottom text row up: row r is text row Height - 1 - r
			var rows = Enumerable.Range( 0, golden.Height ).Select( text => string.Concat( Enumerable.Range( 0, golden.Width ).Select( column => SymbolOf( golden.Types[(golden.Height - 1 - text) * golden.Width + column] ) ) ) ).ToArray();
			if ( rows.All( row => row.All( symbol => symbol == '.' ) ) )
				continue;
			var actual = ParkObjects.GetBaseHeight( new ObjectShape( rows ), new HeightGrid( golden.CellsX, golden.Heights ), golden.AnchorX, golden.AnchorZ, golden.Rotation );
			if ( Bits( actual ) != Bits( golden.Expected ) )
				different.Add( $"{golden.Name}: {actual:R} against {golden.Expected:R}" );
		}
		Assert.AreEqual( 0, different.Count, string.Join( "\n", different ) );
	}
}
