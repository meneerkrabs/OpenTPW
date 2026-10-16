using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>UI-930: the picked visitor must stand in the hovered cell or one of its eight neighbours (tp.exe 0x532bd0, offset table 0x81ed40).</summary>
[TestClass]
[DoNotParallelize]
public class VisitorPickNeighbourhoodTests
{
	private static void Set( object o, string field, object value ) => o.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( o, value );

	[DataTestMethod]
	[DataRow( 5, 5, 5, 5, true )]
	[DataRow( 5, 5, 4, 4, true )]
	[DataRow( 5, 5, 6, 6, true )]
	[DataRow( 5, 5, 5, 6, true )]
	[DataRow( 5, 5, 7, 5, false )]
	[DataRow( 5, 5, 5, 3, false )]
	[DataRow( 0, 0, 1, 1, true )]
	[DataRow( 0, 0, -2, 0, false )]
	public void TheNeighbourhoodIsTheHoveredCellAndItsEightNeighbours( int hoveredX, int hoveredY, int thingX, int thingY, bool expected ) =>
		Assert.AreEqual( expected, Level.IsInHoveredNeighbourhood( (hoveredX, hoveredY), (thingX, thingY) ) );

	/// <summary>An original level around the fixture: the cursor ray (identity camera, straight along +Z) hits the terrain at cell (5, 5).</summary>
	private static (Level Level, Guest Guest) OriginalFixture( int guestCellX, int guestCellY )
	{
		var hud = VisitorWorldSelectionTests.Fixture( out var simulation, out var renderer );
		var level = (Level)typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
		var field = (ModelHeightfield)typeof( GuestRenderer ).GetField( "field", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( renderer )!;
		var park = (OriginalPark)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( OriginalPark ) );
		Set( park, "<Heightfield>k__BackingField", field );
		Set( level, "<OriginalPark>k__BackingField", park );
		var guest = new Guest( 4, 0 ) { State = GuestState.WalkingAround, DrawX = 5, DrawY = 5 };
		guest.Navigation.X = (guestCellX << 16) + 0x8000;
		guest.Navigation.Y = (guestCellY << 16) + 0x8000;
		Set( simulation, "guests", new List<Guest> { guest } );
		return (level, guest);
	}

	[TestMethod]
	public void AVisitorOnTheHoveredCellOrNextToItIsPicked()
	{
		var view = N.Matrix4x4.Identity;
		foreach ( var (x, y) in new[] { (5, 5), (4, 4), (6, 6), (5, 4) } )
		{
			var (level, guest) = OriginalFixture( x, y );
			Assert.AreSame( guest, level.PickVisitor( new N.Vector2( 500, 400 ), new N.Vector2( 1000 ), view, view ), $"visitor on cell ({x}, {y})" );
		}
	}

	[TestMethod]
	public void AVisitorTwoCellsFromTheHoveredCellIsNotPickedEvenWhenItsSpriteIsUnderTheCursor()
	{
		var view = N.Matrix4x4.Identity;
		foreach ( var (x, y) in new[] { (7, 5), (5, 3), (0, 0), (9, 9) } )
		{
			var (level, _) = OriginalFixture( x, y );
			Assert.IsNull( level.PickVisitor( new N.Vector2( 500, 400 ), new N.Vector2( 1000 ), view, view ), $"visitor on cell ({x}, {y})" );
		}
	}

	[TestMethod]
	public void TheSandboxHasNoTerrainRayAndKeepsTheSpriteOnlyPick()
	{
		var hud = VisitorWorldSelectionTests.Fixture( out var simulation, out _ );
		var level = (Level)typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
		var guest = new Guest( 4, 0 ) { State = GuestState.WalkingAround, DrawX = 5, DrawY = 5 };
		Set( simulation, "guests", new List<Guest> { guest } );
		var view = N.Matrix4x4.Identity;
		Assert.IsNull( level.OriginalPark );
		Assert.AreSame( guest, level.PickVisitor( new N.Vector2( 500, 400 ), new N.Vector2( 1000 ), view, view ) );
	}
}
