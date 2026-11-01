using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>The map window's items, their areas, the track layer and the pointer (tp.exe 0x5F3940, 0x5F44D0, 0x5F2FD0, 0x5F198E; UI-086).</summary>
[TestClass]
public class MapItemTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

	[DataTestMethod]
	[DataRow( 1, 0x20, 0x21u, 0, 0, true )]
	[DataRow( 1, 0x20, 0x01u, 0, 0, false )]
	[DataRow( 2, 3, 0u, 3, 0, true )]
	[DataRow( 2, 3, 0u, 1, 0, false )]
	[DataRow( 3, 1, 0u, 0, 1, true )]
	[DataRow( 3, 1, 0u, 0, 2, false )]
	[DataRow( 9, 1, 0xFFu, 1, 1, false )]
	public void AnItemShowsWhileItsLayerWordStaffRoleOrValueModeFilterHolds( int mode, int value, uint layers, int staff, int valueMode, bool shown ) =>
		Assert.AreEqual( shown, ParkMapDots.Visible( new MapDot { Mode = mode, Value = value }, layers, staff, valueMode ) );

	[TestMethod]
	public void TheHappinessRampRunsFromBlueThroughOrangeToYellowInSixteenBitSteps()
	{
		Assert.AreEqual( new RgbaByte( 32, 168, 248, 255 ), ParkMapPalette.DotRamp( 0 ) );
		Assert.AreEqual( new RgbaByte( 248, 132, 0, 255 ), ParkMapPalette.DotRamp( 50 ) );
		Assert.AreEqual( new RgbaByte( 248, 252, 0, 255 ), ParkMapPalette.DotRamp( 100 ) );
		// 25: fraction (25 << 16) / 50 = 0x8000; each channel steps by (difference * fraction) >> 16 with an arithmetic shift (-36 * 0x8000 >> 16 = -18)
		Assert.AreEqual( new RgbaByte( 140, 150, 124, 255 ), ParkMapPalette.DotRamp( 25 ) );
		Assert.AreEqual( ParkMapPalette.DotRamp( 0 ), ParkMapPalette.DotRamp( -7 ) );
		Assert.AreEqual( ParkMapPalette.DotRamp( 100 ), ParkMapPalette.DotRamp( 500 ) );
		Assert.AreEqual( 20, ParkMapPalette.RampByte( 276.9f ), "the low byte of the truncation" );
		Assert.IsNull( ParkMapPalette.RampByte( float.NaN ) );
	}

	[TestMethod]
	public void AnItemTakesTheColourOfItsKindAndTheHappinessOfItsGuest()
	{
		var guest = new Guest( 1, 0 ) { Happiness = 100 };
		Assert.AreEqual( ParkMapPalette.DotRamp( 100 ), ParkMapDots.ColorOf( new MapDot { Kind = 0, Guest = guest } ) );
		Assert.AreEqual( ParkMapPalette.DotWhite, ParkMapDots.ColorOf( new MapDot { Kind = 0 } ), "a kind 0 item without a guest keeps the default record" );
		Assert.AreEqual( new RgbaByte( 0, 0, 0, 255 ), ParkMapDots.ColorOf( new MapDot { Kind = 1 } ) );
		Assert.AreEqual( new RgbaByte( 255, 0, 0, 255 ), ParkMapDots.ColorOf( new MapDot { Kind = 2 } ) );
		Assert.AreEqual( ParkMapPalette.DotWhite, ParkMapDots.ColorOf( new MapDot { Kind = 3 } ) );
		Assert.AreEqual( new RgbaByte( 200, 250, 200, 255 ), ParkMapDots.ColorOf( new MapDot { Kind = 4 } ) );
		Assert.AreEqual( ParkMapPalette.DotWhite, ParkMapDots.ColorOf( new MapDot { Kind = 5 } ) );
	}

	[TestMethod]
	public void ASpriteIsSquareAndItsSideFollowsTheImageWidthAndTheItemScale()
	{
		Assert.AreEqual( 6f, ParkMapDots.SurfaceSize( new MapDot { Image = 0 } ) );
		Assert.AreEqual( 24f, ParkMapDots.SurfaceSize( new MapDot { Image = 4 } ) );
		Assert.AreEqual( 12f, ParkMapDots.SurfaceSize( new MapDot { Image = 3, Scale = 0.5f } ) );
		Assert.IsTrue( ParkMapDots.IsBehind( new MapDot { Kind = 3 } ) );
		Assert.IsFalse( ParkMapDots.IsBehind( new MapDot { Kind = 2 } ) );
	}

	private static StaffMember Member( int id, (int, int, int, int) area ) => new() { Id = id, Type = StaffType.Handyman, NameIndex = 0, Grade = default, PatrolArea = area };

	[TestMethod]
	public void AStaffMembersAreaIsDrawnOnlyWhileHisItemShowsAndNeverFromTheEdges()
	{
		var canvas = new ParkMapCanvas( new MapSurface( 96, 85, true ) );
		var inside = Member( 1, (10, 20, 12, 22) );
		var wholeMap = Member( 2, StaffMember.WholeMap );
		var fromColumnZero = Member( 3, (0, 5, 8, 9) );
		var toTheLastRow = Member( 4, (4, 5, 8, 127) );
		foreach ( var member in new[] { inside, wholeMap, fromColumnZero, toTheLastRow } )
			canvas.Dots.Add( new MapDot { Kind = 2, Mode = 2, Value = 1, Staff = member } );
		canvas.Dots.Add( new MapDot { Kind = 4, Mode = 2, Value = 1, Staff = inside } );
		canvas.RenderItems( 0, 1, 0 );
		// 96 x 85 cells are 5 x 5 pixels from (15, 42)
		CollectionAssert.AreEqual( new[] { new MapPrim( 65, 142, 15, 15, ParkMapPalette.Zone ) }, canvas.Prims.ToArray() );
		canvas.Hovered = canvas.Dots[0];
		canvas.Prims.Clear();
		canvas.RenderItems( 0, 1, 0 );
		Assert.AreEqual( ParkMapPalette.ZoneHovered, canvas.Prims.Single().Color, "the item under the pointer shows its area in the brighter blue" );
		canvas.Prims.Clear();
		canvas.RenderItems( 0, 2, 0 );
		Assert.AreEqual( 0, canvas.Prims.Count, "another staff role shows no area" );
	}

	[TestMethod]
	public void TheTrackLayerWalksEveryPairInTenSamplesAndJoinsThemWithAntiAliasedLines()
	{
		var canvas = new ParkMapCanvas( new MapSurface( 96, 85, true ) );
		// a straight pair along x of 20 units: the Bezier with controls at a third and two thirds is linear, x = 20 t; 480 / (96 x 10) = 0.5 pixels per unit from (15, 42)
		var line = (new NVector3( 0, 3, 0 ), new NVector3( 20 / 3f, 3, 0 ), new NVector3( 40 / 3f, 3, 0 ), new NVector3( 20, 3, 0 ));
		canvas.RenderTrack( new[] { line }, new RgbaByte( 248, 0, 0, 255 ) );
		Assert.IsTrue( canvas.Prims.Count > 0 );
		Assert.IsTrue( canvas.Prims.All( prim => prim.Width == 1 && prim.Height == 1 && prim.Color is { R: 248, G: 0, B: 0 } ) );
		Assert.IsTrue( canvas.Prims.All( prim => prim.X is >= 15 and <= 24 && prim.Y is >= 41 and <= 43 ), "the ground z gives the row: 42 for z = 0, the height y is not drawn" );
		var middle = canvas.Prims.Where( prim => prim.Y == 42 ).ToArray();
		Assert.IsTrue( middle.Any( prim => prim.Color.A == 255 ), "the pixels on the line take its full coverage" );
		// the last sample is at t = 0.9 (0.1 added ten times in single precision exceeds 1), so the line ends before x = 15 + 10
		Assert.IsTrue( canvas.Prims.Max( prim => prim.X ) < 25 );
		var twice = new ParkMapCanvas( new MapSurface( 96, 85, true ) );
		twice.RenderTrack( new[] { line, line }, new RgbaByte( 248, 0, 0, 255 ) );
		Assert.IsTrue( twice.Prims.Count > canvas.Prims.Count, "a second pair is joined to the first one's last point" );
	}

	private static (ParkHud Hud, ParkMapCanvas Canvas) HoverRig()
	{
		var hud = (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
		var canvas = new ParkMapCanvas( new MapSurface( 96, 85, true ) );
		void Set( string field, object? value ) => typeof( ParkHud ).GetField( field, Private )!.SetValue( hud, value );
		Set( "mapCanvas", canvas );
		Set( "mapView", MapViewState.Default );
		Set( "mapLayers", 0x20u );
		Set( "mapStaff", 1 );
		Set( "mapValue", 1 );
		Set( "strings", OriginalUiTests.FakeStrings() );
		return (hud, canvas);
	}

	[TestMethod]
	public void ThePointerPicksTheNearestShownItemWithinFivePixelsAndTheFirstOfEqualOnes()
	{
		var (hud, canvas) = HoverRig();
		// a guest at cell (10, 10): the surface pixel (15 + 50, 42 + 50)
		var near = new MapDot { X = 10, Y = 10, Mode = 1, Value = 0x20, Guest = new Guest( 1, 0 ) };
		var far = new MapDot { X = 10.4f, Y = 10, Mode = 1, Value = 0x20, Guest = new Guest( 2, 0 ) };
		var hidden = new MapDot { X = 10, Y = 10, Mode = 1, Value = 0x01 };
		canvas.Dots.AddRange( new[] { hidden, far, near } );
		// 1 image unit = 1 pixel, the image at the origin
		var geometry = (new UiRect( 0, 0, 512, 512 ), NVector2.Zero, 512f);
		hud.UpdateMapHover( geometry, new NVector2( 65, 92 ) );
		Assert.AreSame( near, canvas.Hovered, "the first of two items at the same distance is not the case here: near is closer" );
		hud.UpdateMapHover( geometry, new NVector2( 66.9f, 92 ) );
		Assert.AreSame( far, canvas.Hovered, "far is the nearer one now: 65 + 2 = 67 is 0.1 cell from it" );
		hud.UpdateMapHover( geometry, new NVector2( 80, 92 ) );
		Assert.IsNull( canvas.Hovered, "more than 5 pixels from every item" );
		hud.UpdateMapHover( geometry, new NVector2( 1000, 1000 ) );
		Assert.IsNull( canvas.Hovered );
		canvas.Dots.Clear();
		canvas.Dots.AddRange( new[] { new MapDot { X = 10, Y = 10, Mode = 1, Value = 0x20 }, new MapDot { X = 10, Y = 10, Mode = 1, Value = 0x20 } } );
		hud.UpdateMapHover( geometry, new NVector2( 65, 92 ) );
		Assert.AreSame( canvas.Dots[0], canvas.Hovered, "an equal distance does not replace the first item" );
	}
}
