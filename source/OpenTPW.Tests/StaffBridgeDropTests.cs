using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class StaffBridgeDropTests
{
	[TestMethod]
	public void TypedFootprintStaffDropsRejectBridgeApproachesAndConnectionsUntilRemoved()
	{
		var previousEdition = GameEditions.Current;
		try
		{
			GameEditions.Select( GameEdition.ThemeParkWorld );
			var cells = new ParkCellMap( 20, 20 );
			cells.SetType( 1, 3, ParkCellType.Path );
			cells.SetType( 9, 3, ParkCellType.Path );
			for ( var x = 4; x <= 6; x++ ) cells.SetType( x, 3, ParkCellType.PoolWater );
			var bridges = new PoolBridgeWorld( cells );
			var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
			typeof( Level ).GetField( "<PoolBridges>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, bridges );
			Assert.IsTrue( (bool)typeof( Level ).GetProperty( "TypesFootprints", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( level )! );
			var blocked = (Func<int, int, bool>)typeof( Level ).GetMethod( "IsStaffDropBlocked", BindingFlags.Instance | BindingFlags.NonPublic )!
				.CreateDelegate( typeof( Func<int, int, bool> ), level );
			var landCells = new[] { 1, 2, 3, 7, 8, 9 };
			foreach ( var x in landCells ) Assert.IsTrue( StaffPlacementRules.IsValidDropCell( cells, x, 3, blocked ) );

			Assert.AreEqual( PoolBridgeResult.Ok, bridges.TryAdd( 2, 3, 1 ) );
			foreach ( var x in landCells )
			{
				Assert.IsTrue( StaffPlacementRules.IsValidDropCell( cells, x, 3, null ), "The underlying land/path remains a valid drop type." );
				Assert.IsFalse( StaffPlacementRules.IsValidDropCell( cells, x, 3, blocked ), $"Bridge reservation at ({x}, 3) must reject a drop." );
			}
			for ( var x = 4; x <= 6; x++ ) Assert.IsFalse( StaffPlacementRules.IsValidDropCell( cells, x, 3, blocked ) );
			Assert.IsTrue( StaffPlacementRules.IsValidDropCell( cells, 2, 4, blocked ), "Adjacent ordinary grass remains available." );
			// The existing type rules still accept grass, path, queue and queue-end; all other ordinary types stay refused.
			for ( byte type = 0; type <= 10; type++ )
			{
				cells.SetType( 12, 12, (ParkCellType)type );
				Assert.AreEqual( StaffPlacementRules.IsDropCellType( type ), StaffPlacementRules.IsValidDropCell( cells, 12, 12, blocked ) );
			}
			Assert.IsFalse( StaffPlacementRules.IsValidDropCell( cells, -1, 3, blocked ) );
			Assert.AreEqual( PoolBridgeResult.Ok, bridges.RemoveAt( 2, 3 ) );
			foreach ( var x in landCells ) Assert.IsTrue( StaffPlacementRules.IsValidDropCell( cells, x, 3, blocked ) );
			typeof( Level ).GetField( "<PoolBridges>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, null );
			foreach ( var x in landCells ) Assert.IsTrue( StaffPlacementRules.IsValidDropCell( cells, x, 3, blocked ), "No bridge subsystem preserves existing drops." );
		}
		finally
		{
			GameEditions.Select( previousEdition );
		}
	}
}
