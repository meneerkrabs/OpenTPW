using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-figures] Where a shared park's own figure goes (C2d-4; docs/COOP.md, "Co-builders as figures"): the centre of the player's view in cells, the overview's
/// target or the free walk's place, mapped back the way the figures are drawn.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CoopFiguresViewTests
{
	private static void Set( object o, string field, object? value ) => o.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( o, value );

	[TestMethod]
	public void TheViewsCentreIsTheOverviewsTargetOrTheWalksPlaceInCells()
	{
		var field = new ModelHeightfield( 10, 10, 10, 10, 0, 0, Array.Empty<uint>(), Enumerable.Repeat( 2.5f, 121 ).ToArray(), new uint[100] );
		// the cells of an engine point undo the figures' drawing (GuestRenderer.CellToEngine)
		var engine = GuestRenderer.CellToEngine( field, 3.25f, 7.5f );
		var cells = OriginalParkPlacement.ToCells( field, engine.X, engine.Y );
		Assert.AreEqual( 3.25f, cells.X, 1e-4f );
		Assert.AreEqual( 7.5f, cells.Y, 1e-4f );

		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		var park = (OriginalPark)RuntimeHelpers.GetUninitializedObject( typeof( OriginalPark ) );
		Set( park, "<Heightfield>k__BackingField", field );
		Set( level, "<OriginalPark>k__BackingField", park );
		var (current, camera, extent) = (Level.Current, Camera.Mode, ParkCameraMode.TargetExtent);
		try
		{
			Level.Current = level;
			ParkCameraMode.TargetExtent = 1000f;
			var overview = new ParkCameraMode();
			overview.Focus( engine );
			Camera.Mode = overview;
			var centre = GameFlow.ViewCentre( level );
			Assert.IsNotNull( centre );
			Assert.AreEqual( 3.25f, centre.Value.X, 1e-3f );
			Assert.AreEqual( 7.5f, centre.Value.Y, 1e-3f );

			// the free walk: the walker's place
			var place = GuestRenderer.CellToEngine( field, 6.5f, 2.25f );
			Assert.IsTrue( FreeWalkCameraMode.TryGround( field, place.X, place.Y, out var ground ) );
			var walk = new FreeWalkCameraMode( field, ground, Vector3.Forward );
			level.UseFreeWalkForTests( walk );
			Camera.Mode = walk;
			centre = GameFlow.ViewCentre( level );
			Assert.IsNotNull( centre );
			Assert.AreEqual( 6.5f, centre.Value.X, 1e-3f );
			Assert.AreEqual( 2.25f, centre.Value.Y, 1e-3f );

			// a walk that is not this park's, another view (the ride view, the globe), or a park that is not the live one: no centre, and the figure stays
			level.UseFreeWalkForTests( null );
			Assert.IsNull( GameFlow.ViewCentre( level ) );
			Camera.Mode = new CameraMode();
			Assert.IsNull( GameFlow.ViewCentre( level ) );
			Camera.Mode = overview;
			Level.Current = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
			Assert.IsNull( GameFlow.ViewCentre( level ) );
		}
		finally
		{
			Level.Current = current!;
			Camera.Mode = camera;
			ParkCameraMode.TargetExtent = extent;
		}
	}
}
