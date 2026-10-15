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
		typeof( OpenTPW.Hud.ParkHud ).GetField( "mapCategories", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!
			.SetValue( hud, new System.Collections.Generic.HashSet<BuildCategory> { BuildCategory.Rides, BuildCategory.Shops, BuildCategory.Sideshows, BuildCategory.Features } );
		Assert.IsTrue( hud.MapShowsCategory( (int)BuildCategory.Rides ) && hud.MapShowsCategory( (int)BuildCategory.Features ), "with all four layers on, each shows" );
		hud.ToggleMapCategory( BuildCategory.Rides );
		Assert.IsFalse( hud.MapShowsCategory( (int)BuildCategory.Rides ), "turning a layer off hides only its category" );
		Assert.IsTrue( hud.MapShowsCategory( (int)BuildCategory.Shops ) && hud.MapShowsCategory( (int)BuildCategory.Sideshows ) && hud.MapShowsCategory( (int)BuildCategory.Features ) );
		hud.ToggleMapCategory( BuildCategory.Shops );
		hud.ToggleMapCategory( BuildCategory.Sideshows );
		hud.ToggleMapCategory( BuildCategory.Features );
		Assert.IsFalse( hud.MapShowsCategory( (int)BuildCategory.Shops ) || hud.MapShowsCategory( (int)BuildCategory.Features ), "with every layer off nothing is shown" );
		hud.ToggleMapCategory( BuildCategory.Rides );
		Assert.IsTrue( hud.MapShowsCategory( (int)BuildCategory.Rides ) && !hud.MapShowsCategory( (int)BuildCategory.Shops ), "toggling one layer on leaves the others off" );
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
