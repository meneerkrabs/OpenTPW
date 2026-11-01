using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

[TestClass]
public class MapViewStateTests
{
	[TestMethod]
	public void ZoomStepsHalfAndClampsToOneThroughThree()
	{
		var view = MapViewState.Default;
		Assert.AreEqual( 1f, view.Zoom );
		var (min, max) = view.Source( 96, 85, true );
		Assert.AreEqual( 0f, min.X );
		Assert.AreEqual( 1f, max.Y );
		view = view.ZoomBy( 0.5f );
		Assert.AreEqual( 1.5f, view.Zoom );
		view = view.ZoomBy( 10f );
		Assert.AreEqual( 3f, view.Zoom );
		(min, max) = view.Source( 96, 85, true );
		Assert.AreEqual( 1f / 3f, max.X - min.X, 1e-5f );
		Assert.IsTrue( min.X >= 0f && max.X <= 1f && min.Y >= 0f && max.Y <= 1f );
		view = view.ZoomBy( -10f );
		Assert.AreEqual( 1f, view.Zoom );
		view = view.ZoomBy( -0.5f );
		Assert.AreEqual( 1f, view.Zoom );
	}

	[TestMethod]
	public void PanningMovesOneCellWhateverTheZoomAndClampsToTheMap()
	{
		var view = MapViewState.Default.PanBy( 0, 0, 96, 85 );
		Assert.AreEqual( new System.Numerics.Vector2( 47.5f, 42f ), view.Cell );
		var zoomed = view.ZoomBy( 1.5f ).PanBy( 1, -1, 96, 85 );
		Assert.AreEqual( 48.5f, zoomed.Cell.X );
		Assert.AreEqual( 41f, zoomed.Cell.Y );
		Assert.AreEqual( view.PanBy( 1, -1, 96, 85 ).Cell, zoomed.Cell );
		zoomed = zoomed.PanBy( 1000, -1000, 96, 85 );
		Assert.AreEqual( 95f, zoomed.Cell.X );
		Assert.AreEqual( 0f, zoomed.Cell.Y );
		var (min, max) = zoomed.Source( 96, 85, true );
		Assert.IsTrue( min.X >= 0f && max.X <= 1f && min.Y >= 0f && max.Y <= 1f );
	}

	[TestMethod]
	public void SuccessivePansMoveTheWindowOneCellEach()
	{
		var view = MapViewState.Default.ZoomBy( 2f ).PanBy( 0, 0, 96, 85 );
		var cell = 5f / ParkMapGeometry.ImagePixels;
		var previous = view.Source( 96, 85, true ).Min.X;
		for ( var step = 0; step < 3; step++ )
		{
			view = view.PanBy( 1, 0, 96, 85 );
			var min = view.Source( 96, 85, true ).Min.X;
			Assert.AreEqual( cell, min - previous, 1e-5f, $"pan {step + 1} moves the window exactly one cell, not a rounded one" );
			previous = min;
		}
	}

	[TestMethod]
	public void CategoryButtonsAreIndependentLayerToggles()
	{
		var hud = (OpenTPW.Hud.ParkHud)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( OpenTPW.Hud.ParkHud ) );
		// the layer word 0xf: the four category bits
		typeof( OpenTPW.Hud.ParkHud ).GetField( "mapLayers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( hud, 0xFu );
		Assert.IsTrue( hud.MapCategoryShown( BuildCategory.Rides ) && hud.MapCategoryShown( BuildCategory.Features ), "with all four layers on, each shows" );
		hud.ToggleMapCategory( BuildCategory.Rides );
		Assert.IsFalse( hud.MapCategoryShown( BuildCategory.Rides ), "turning a layer off hides only its category" );
		Assert.IsTrue( hud.MapCategoryShown( BuildCategory.Shops ) && hud.MapCategoryShown( BuildCategory.Sideshows ) && hud.MapCategoryShown( BuildCategory.Features ) );
		hud.ToggleMapCategory( BuildCategory.Shops );
		hud.ToggleMapCategory( BuildCategory.Sideshows );
		hud.ToggleMapCategory( BuildCategory.Features );
		Assert.IsFalse( hud.MapCategoryShown( BuildCategory.Shops ) || hud.MapCategoryShown( BuildCategory.Features ), "with every layer off nothing is shown" );
		hud.ToggleMapCategory( BuildCategory.Rides );
		Assert.IsTrue( hud.MapCategoryShown( BuildCategory.Rides ) && !hud.MapCategoryShown( BuildCategory.Shops ), "toggling one layer on leaves the others off" );
	}

	[TestMethod]
	public void OriginalCellsAlignWithCentredFivePixelThemeArt()
	{
		var first = ParkMapGeometry.CellImageRect( 96, 85, true, 0, 0 );
		var last = ParkMapGeometry.CellImageRect( 96, 85, true, 95, 84 );
		Assert.AreEqual( 16f, first.X );
		Assert.AreEqual( 43.5f, first.Y );
		Assert.AreEqual( 5f, first.Width );
		Assert.AreEqual( 496f, last.Right );
		Assert.AreEqual( 468.5f, last.Bottom );
	}

	[TestMethod]
	public void SandboxCellsFillTheWholeMapImage()
	{
		var first = ParkMapGeometry.CellImageRect( 32, 32, false, 0, 0 );
		var last = ParkMapGeometry.CellImageRect( 32, 32, false, 31, 31 );
		Assert.AreEqual( 0f, first.X );
		Assert.AreEqual( 0f, first.Y );
		Assert.AreEqual( 16f, first.Width );
		Assert.AreEqual( 512f, last.Right );
		Assert.AreEqual( 512f, last.Bottom );
	}
}

/// <summary>UI-103: the map view's edge scroll (tp.exe 0x5f1130 message 0x1e, 0x5f35b0).</summary>
[TestClass]
public class MapEdgeScrollTests
{
	[TestMethod]
	public void TheStepIsTwoHundredFiftyCellsPerUnitOfDepthIntoTheOuterOnePerCent()
	{
		Assert.AreEqual( (0f, 0f), ParkHud.MapEdgeStep( 0.5f, 0.5f ) );
		Assert.AreEqual( (0f, 0f), ParkHud.MapEdgeStep( 0.01f, 0.99f ), "the inner 98 per cent does nothing" );
		Assert.AreEqual( 2.5f, ParkHud.MapEdgeStep( 1f, 0.5f ).X, 1e-5f, "the right edge: (1 - 0.99) x 250" );
		Assert.AreEqual( -2.5f, ParkHud.MapEdgeStep( 0f, 0.5f ).X, 1e-5f, "the left edge moves x less (direction 0)" );
		Assert.AreEqual( -1.25f, ParkHud.MapEdgeStep( 0.5f, 0.005f ).Y, 1e-5f, "the top edge is the up button's direction" );
		Assert.AreEqual( 1.25f, ParkHud.MapEdgeStep( 0.5f, 0.995f ).Y, 1e-5f );
		Assert.AreEqual( 2.5f, ParkHud.MapEdgeStep( 7f, 0.5f ).X, 1e-5f, "the share is clamped to 0..1" );
	}

	[TestMethod]
	public void OnlyPushscrollScrollsAndTheCentreStaysInTheMap()
	{
		var view = MapViewState.Default.PanBy( 0, 0, 96, 85 );
		var centre = view.Cell;
		var edge = new System.Numerics.Vector2( 999, 400 );
		Assert.AreEqual( centre, ParkHud.EdgeScrolled( view, 3, edge, 1000, 800, ScrollMode.RightButton, 96, 85 ).Cell, "the flag 0x78d910 set (Right button) disables it" );
		Assert.AreEqual( centre, ParkHud.EdgeScrolled( view, 0, edge, 1000, 800, ScrollMode.Pushscroll, 96, 85 ).Cell, "no frame, no step" );
		Assert.AreEqual( centre, ParkHud.EdgeScrolled( view, 3, new System.Numerics.Vector2( -1, -1 ), 1000, 800, ScrollMode.Pushscroll, 96, 85 ).Cell, "a pointer outside the window sends no message" );
		var moved = ParkHud.EdgeScrolled( view, 2, edge, 1000, 800, ScrollMode.Pushscroll, 96, 85 );
		Assert.AreEqual( centre.X + 2 * 2.25f, moved.Cell.X, 0.01f, "(0.999 - 0.99) x 250 = 2.25 a frame, two frames" );
		Assert.AreEqual( centre.Y, moved.Cell.Y );
		Assert.AreEqual( 95f, ParkHud.EdgeScrolled( moved, 100, edge, 1000, 800, ScrollMode.Pushscroll, 96, 85 ).Cell.X, "0x5f35b0 clamps the centre to the map" );
	}
}
