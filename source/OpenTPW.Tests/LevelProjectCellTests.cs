using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>Level.TryProjectCell answers false for a cell off the grid instead of throwing from the heightfield (the jungle's guest map is larger than its landscape).</summary>
[TestClass]
public class LevelProjectCellTests
{
	/// <summary>A grid like the original park's: its height lookup throws outside the landscape.</summary>
	private sealed class StrictGrid : IParkGrid
	{
		public int Width => 8;
		public int Height => 6;
		public N.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) =>
			x < 0 || y < 0 || x >= Width || y >= Height ? throw new System.ArgumentOutOfRangeException( x < 0 || x >= Width ? nameof( x ) : nameof( y ) ) : 0;
	}

	[TestMethod]
	public void CellsOffTheGridDoNotProject()
	{
		var view = Camera.ViewMatrix; var projection = Camera.ProjMatrix;
		try
		{
			Camera.ViewMatrix = Camera.ProjMatrix = N.Matrix4x4.Identity;
			var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
			typeof( Level ).GetField( "<Objects>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, new ParkObjects( null!, new StrictGrid() ) );
			var viewport = new N.Vector2( 100, 100 );
			Assert.IsTrue( level.TryProjectCell( 0, 0, viewport, out _ ) );
			Assert.IsTrue( level.TryProjectCell( 7, 5, viewport, out _ ) );
			foreach ( var cell in new[] { (-1, 0), (0, -1), (8, 0), (0, 6), (60, 60) } )
				Assert.IsFalse( level.TryProjectCell( cell.Item1, cell.Item2, viewport, out _ ), $"cell {cell} lies off the grid" );
		}
		finally { Camera.ViewMatrix = view; Camera.ProjMatrix = projection; }
	}
}
