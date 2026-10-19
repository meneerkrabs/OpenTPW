using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>Selected-mesh fitting and inverse picking, using only synthetic model data and CPU batches.</summary>
[TestClass]
public class UiModelFitTests
{
	private static readonly UiRect Destination = new( 200, 300, 400, 600 );
	private static readonly NVector2[] PaddedPoints = { new( 280, 420 ), new( 520, 420 ), new( 280, 780 ) };
	private static readonly NVector2[] TightPoints = { new( 200, 300 ), new( 600, 300 ), new( 200, 900 ) };

	private static ModelFile Source( params NMatrix4x4[] transforms )
	{
		var builder = new ModelBuilder();
		ModelNodeBuilder? parent = null;
		if ( transforms.Length == 0 ) transforms = new[] { NMatrix4x4.Identity };
		for ( var index = 0; index < transforms.Length; index++ )
		{
			var node = builder.AddMesh( $"state_{index}", parent, transforms[index] );
			var material = node.Mesh!.AddMaterial( null );
			var a = material.AddVertex( new NVector3( 30, -60, 0 ), -NVector3.UnitZ, NVector2.Zero );
			var b = material.AddVertex( new NVector3( 90, -60, 0 ), -NVector3.UnitZ, NVector2.UnitX );
			var c = material.AddVertex( new NVector3( 30, -180, 0 ), -NVector3.UnitZ, NVector2.UnitY );
			material.AddTriangle( a, b, c );
			parent = node;
		}
		var source = builder.Build();
		foreach ( var mesh in source.Meshes )
		{
			// Deliberate nonzero-origin padding; the header and corner bounds remain tight.
			mesh.BoundsMin = new Vector3( 10, -220, 0 );
			mesh.BoundsMax = new Vector3( 110, -20, 0 );
		}
		return source;
	}

	private static UiModel Model( params NMatrix4x4[] transforms ) => new( Source( transforms ), "fit" );

	private static UiContext Context( UiModel model ) => new( OriginalUiTests.FakeStrings(), null!, UiModels.FromAssets( new[] { model } ) )
	{
		Canvas = new UiCanvas( 2048, 1536 ), ResolveTexture = _ => null
	};

	private static UiModelImage Image() => new() { Model = "fit", Bounds = Destination, Anchor = UiAnchor.TopLeft, CoversShape = true };

	private static NVector2[] Draw( UiModelFrame frame, UiRect? destination = null )
	{
		var context = OriginalUiTests.FakeContext();
		context.DrawFrame( frame, destination ?? Destination );
		return Points( context.Batch );
	}

	private static NVector2[] Points( UiBatch batch ) => batch.Draws.SelectMany( draw => draw.Vertices ).Select( vertex => vertex.Position ).ToArray();

	private static void AssertPoints( NVector2[] expected, NVector2[] actual )
	{
		Assert.AreEqual( expected.Length, actual.Length );
		for ( var index = 0; index < expected.Length; index++ )
		{
			Assert.AreEqual( expected[index].X, actual[index].X, 1e-4f, $"vertex {index} X" );
			Assert.AreEqual( expected[index].Y, actual[index].Y, 1e-4f, $"vertex {index} Y" );
		}
	}

	[TestMethod]
	public void DrawFrameFitsSelectedStoredEndpointsIncludingNonzeroPadding()
	{
		var frame = Model().GetFrame( 0 );
		Assert.AreEqual( new UiRect( 10, 20, 100, 200 ), frame.FitBounds!.Value );
		AssertPoints( PaddedPoints, Draw( frame ) );
	}

	[TestMethod]
	public void ShapePickingInvertsTheDrawnFitAndLeavesPaddingUncovered()
	{
		var context = Context( Model() );
		var image = Image();
		Assert.IsFalse( image.CoversPoint( context.Canvas, PaddedPoints[0] ), "no shape before its first draw" );
		image.Draw( context, false, false );
		AssertPoints( PaddedPoints, Points( context.Batch ) );
		foreach ( var point in PaddedPoints.Append( new NVector2( 320, 480 ) ).Append( new NVector2( 400, 600 ) ) )
			Assert.IsTrue( image.CoversPoint( context.Canvas, point ), $"triangle interior or edge {point}" );
		foreach ( var point in new[] { new NVector2( 200, 300 ), new NVector2( 600, 900 ), new NVector2( 520, 780 ) } )
			Assert.IsFalse( image.CoversPoint( context.Canvas, point ), $"padding or outside the triangle {point}" );
	}

	[TestMethod]
	public void FitMetadataDoesNotChangeGeometricLayoutOrDirectOriginSubmission()
	{
		var model = Model();
		var frame = model.GetFrame( 0 );
		Assert.AreEqual( (30f, 60f, 90f, 180f), (frame.MinX, frame.MinY, frame.MaxX, frame.MaxY) );
		Assert.AreEqual( (60f, 120f), (frame.Width, frame.Height) );
		Assert.AreEqual( new NVector2( 60, 120 ), frame.Center );
		Assert.AreEqual( new UiRect( 30, 60, 60, 120 ), Context( model ).AuthoredRect( "fit" ) );
		var batch = new UiBatch();
		batch.AddModelFrame( frame, new NVector2( 10, 20 ), new NVector2( 200, 300 ), 2, _ => null );
		AssertPoints( new[] { new NVector2( 240, 380 ), new NVector2( 360, 380 ), new NVector2( 240, 620 ) }, Points( batch ) );
	}

	[TestMethod]
	public void CommonTranslationOfGeometryAndFitEndpointsCancelsInDrawAndPick()
	{
		var model = Model( NMatrix4x4.CreateTranslation( 123, -456, 9 ) );
		var frame = model.GetFrame( 0 );
		Assert.AreEqual( new UiRect( 133, 476, 100, 200 ), frame.FitBounds!.Value );
		Assert.AreEqual( new NVector2( 153, 516 ), frame.Parts[0].Vertices[0].Position );
		AssertPoints( PaddedPoints, Draw( frame ) );
		var context = Context( model );
		var image = Image();
		image.Draw( context, false, false );
		Assert.IsTrue( image.CoversPoint( context.Canvas, new NVector2( 320, 480 ) ) );
		Assert.IsFalse( image.CoversPoint( context.Canvas, new NVector2( 200, 300 ) ) );
	}

	[TestMethod]
	public void ReversedTransformedEndpointsAreOrderedWithoutRemovingVertexReflection()
	{
		var frame = Model( NMatrix4x4.CreateScale( -1, -1, 1 ) ).GetFrame( 0 );
		Assert.AreEqual( new UiRect( -110, -220, 100, 200 ), frame.FitBounds!.Value );
		AssertPoints( new[] { new NVector2( 520, 780 ), new NVector2( 280, 780 ), new NVector2( 520, 420 ) }, Draw( frame ) );
	}

	[TestMethod]
	public void ShearedMeshFitsTwoEndpointsRatherThanEightBoxCorners()
	{
		var shear = NMatrix4x4.Identity;
		shear.M21 = -1; // x' = x - y: the stored endpoints reverse X, while other box corners extend farther.
		var frame = Model( shear ).GetFrame( 0 );
		Assert.AreEqual( new UiRect( 130, 20, 100, 200 ), frame.FitBounds!.Value );
		Assert.AreNotEqual( new UiRect( 30, 20, 300, 200 ), frame.FitBounds.Value, "an eight-corner box would have width 300" );
		AssertPoints( new[] { new NVector2( 40, 420 ), new NVector2( 280, 420 ), new NVector2( 520, 780 ) }, Draw( frame ) );
		var context = Context( Model( shear ) );
		var image = Image();
		image.Draw( context, false, false );
		Assert.IsTrue( image.CoversPoint( context.Canvas, new NVector2( 160, 420 ) ), "geometry outside the destination is not clipped by fitting" );
	}

	[TestMethod]
	public void SignedDestinationDimensionsStillReflectDrawingButDoNotEnableShapePicking()
	{
		var model = Model();
		AssertPoints( new[] { new NVector2( 520, 420 ), new NVector2( 280, 420 ), new NVector2( 520, 780 ) },
			Draw( model.GetFrame( 0 ), new UiRect( 600, 300, -400, 600 ) ) );
		AssertPoints( new[] { new NVector2( 280, 780 ), new NVector2( 520, 780 ), new NVector2( 280, 420 ) },
			Draw( model.GetFrame( 0 ), new UiRect( 200, 900, 400, -600 ) ) );
		var context = Context( model );
		foreach ( var rect in new[] { new UiRect( 600, 300, -400, 600 ), new UiRect( 200, 900, 400, -600 ) } )
		{
			var image = Image();
			image.Bounds = rect;
			image.Draw( context, false, false );
			Assert.IsFalse( image.CoversPoint( context.Canvas, new NVector2( 400, 600 ) ), "existing nonpositive destination policy" );
		}
	}

	[TestMethod]
	public void LegacySixArgumentFramesAndInvalidOptionalMetadataUseGeometricFit()
	{
		var original = Model().GetFrame( 0 );
		var manual = new UiModelFrame( original.NodeName, original.Parts, original.MinX, original.MinY, original.MaxX, original.MaxY );
		Assert.IsNull( manual.FitBounds );
		AssertPoints( TightPoints, Draw( manual ) );
		foreach ( var invalid in new[]
		{
			new UiRect( float.NaN, 20, 100, 200 ), new UiRect( 10, float.NegativeInfinity, 100, 200 ),
			new UiRect( 10, 20, float.PositiveInfinity, 200 ), new UiRect( 10, 20, 100, float.NaN ),
			new UiRect( 10, 20, 0, 200 ), new UiRect( 10, 20, 100, 0 ), new UiRect( 10, 20, -100, 200 ),
			new UiRect( float.MaxValue, 20, float.MaxValue, 200 )
		} )
			AssertPoints( TightPoints, Draw( manual with { FitBounds = invalid } ) );
	}

	[TestMethod]
	public void SmallPositiveFitExtentsAreNotRejectedByAnEpsilon()
	{
		AssertPoints( PaddedPoints, Draw( Model( NMatrix4x4.CreateScale( 1e-9f ) ).GetFrame( 0 ) ) );
	}

	[TestMethod]
	public void InvalidStoredMeshEndpointsFallBackCoherentlyForDrawingAndPicking()
	{
		foreach ( var (min, max) in new[]
		{
			(new Vector3( float.NaN, -220, 0 ), new Vector3( 110, -20, 0 )),
			(new Vector3( 10, -220, 0 ), new Vector3( 110, float.PositiveInfinity, 0 )),
			(new Vector3( 10, -220, float.NaN ), new Vector3( 110, -20, 0 )),
			(new Vector3( 10, -220, 0 ), new Vector3( 10, -20, 0 )),
			(new Vector3( 10, -20, 0 ), new Vector3( 110, -20, 0 )),
			(new Vector3( -float.MaxValue, -220, 0 ), new Vector3( float.MaxValue, -20, 0 ))
		} )
		{
			var source = Source();
			source.Meshes[0].BoundsMin = min;
			source.Meshes[0].BoundsMax = max;
			var model = new UiModel( source, "fit" );
			AssertPoints( TightPoints, Draw( model.GetFrame( 0 ) ) );
			var context = Context( model );
			var image = Image();
			image.Draw( context, false, false );
			AssertPoints( TightPoints, Points( context.Batch ) );
			Assert.IsTrue( image.CoversPoint( context.Canvas, new NVector2( 200, 300 ) ), "fallback uses the tight triangle edge" );
			Assert.IsTrue( image.CoversPoint( context.Canvas, new NVector2( 300, 450 ) ) );
			Assert.IsFalse( image.CoversPoint( context.Canvas, new NVector2( 599, 899 ) ) );
		}
	}

	[TestMethod]
	public void UnusableFitAndGeometryProduceNoDrawOrShapeHit()
	{
		var source = Source();
		var mesh = source.Meshes[0];
		mesh.BoundsMin = mesh.BoundsMax = new Vector3( 0, 0, 0 );
		for ( var index = 0; index < mesh.Vertices.Length; index++ )
			mesh.Vertices[index].Position = new Vector3( 0, 0, 0 );
		var model = new UiModel( source, "fit" );
		var context = Context( model );
		var image = Image();
		image.Draw( context, false, false );
		Assert.AreEqual( 0, context.Batch.Draws.Count );
		Assert.IsFalse( image.CoversPoint( context.Canvas, Destination.Center ) );
		var manual = new UiModelFrame( "invalid", model.GetFrame( 0 ).Parts, float.NaN, 0, 1, 1 );
		Assert.AreEqual( 0, Draw( manual ).Length );
	}

	[TestMethod]
	public void ThreeStateChainKeepsSelectionFallbackAndEachSelectedMeshesFit()
	{
		var source = Source( NMatrix4x4.CreateTranslation( 100, -200, 0 ),
			NMatrix4x4.CreateTranslation( 1000, -2000, 0 ), NMatrix4x4.CreateTranslation( -500, 50, 0 ) );
		source.Meshes[1].BoundsMin = new Vector3( 0, -240, 0 );
		source.Meshes[1].BoundsMax = new Vector3( 120, 0, 0 );
		source.Meshes[2].BoundsMin = new Vector3( 20, -200, 0 );
		source.Meshes[2].BoundsMax = new Vector3( 100, -40, 0 );
		var model = new UiModel( source, "fit" );
		CollectionAssert.AreEqual( new[] { "state_0", "state_1", "state_2" }, model.Frames.Select( frame => frame.NodeName ).ToArray() );
		Assert.AreSame( model.Frames[0], model.GetFrame( -1 ) );
		Assert.AreSame( model.Frames[0], model.GetFrame( 3 ) );
		var context = Context( model );
		var state = 0;
		var image = Image();
		image.Frame = () => state;
		var firstPoints = new[] { new NVector2( 280, 420 ), new NVector2( 300, 450 ), new NVector2( 250, 375 ) };
		foreach ( var selection in new[] { 0, 1, 2, 1, 0, 3, -1 } )
		{
			state = selection;
			context.Batch.Clear();
			image.Draw( context, false, false );
			var expected = firstPoints[selection >= 0 && selection < 3 ? selection : 0];
			AssertPoints( new[] { expected }, Points( context.Batch ).Take( 1 ).ToArray() );
			Assert.IsTrue( image.CoversPoint( context.Canvas, expected ) );
			Assert.IsFalse( image.CoversPoint( context.Canvas, new NVector2( 200, 300 ) ) );
		}
	}
}
