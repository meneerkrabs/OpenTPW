using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

[TestClass]
public class MapViewStateTests
{
	[TestMethod]
	public void ZoomKeepsSourceInsideTheImage()
	{
		var view = MapViewState.Default;
		Assert.AreEqual( 1f, view.Zoom );
		Assert.AreEqual( 0f, view.SourceMin.X );
		Assert.AreEqual( 1f, view.SourceMax.Y );
		view = view.ZoomBy( 10f );
		Assert.AreEqual( 4f, view.Zoom );
		Assert.AreEqual( 0.375f, view.SourceMin.X );
		Assert.AreEqual( 0.625f, view.SourceMax.X );
		view = view.ZoomBy( -10f );
		Assert.AreEqual( 1f, view.Zoom );
		Assert.AreEqual( 0f, view.SourceMin.X );
		Assert.AreEqual( 1f, view.SourceMax.X );
	}

	[TestMethod]
	public void PanningMovesOneEighthOfVisibleImageAndClampsAtEdges()
	{
		var view = MapViewState.Default.ZoomBy( 1f ).PanBy( 1, -1 );
		Assert.AreEqual( 2f, view.Zoom );
		Assert.AreEqual( 0.5625f, view.Center.X );
		Assert.AreEqual( 0.4375f, view.Center.Y );
		view = view.PanBy( 100, -100 );
		Assert.AreEqual( 0.75f, view.Center.X );
		Assert.AreEqual( 0.25f, view.Center.Y );
		Assert.AreEqual( 0f, view.SourceMin.Y );
		Assert.AreEqual( 1f, view.SourceMax.X );
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
