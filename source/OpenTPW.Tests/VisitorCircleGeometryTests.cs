using System;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>Synthetic geometry only: no original assets, renderer, input polling or original runtime.</summary>
[TestClass]
[DoNotParallelize]
public class VisitorCircleGeometryTests
{
	private static readonly N.Vector2 Canvas = new(1024, 768);
	private static readonly GuestAtlasFrame Frame = new(0, 0, 30, 43, -15, -43);

	[TestMethod]
	public void CircleBoundaryIsExcludedWithAdjacentRepresentablePointsInsideAndOutside()
	{
		var circle = new VisitorPickCircle(.25, -.5, .25);
		Assert.IsTrue(circle.Contains(.25, -.5));
		Assert.IsFalse(circle.Contains(.75, -.5));
		Assert.IsFalse(circle.Contains(-.25, -.5));
		Assert.IsFalse(circle.Contains(.25, 0));
		Assert.IsFalse(circle.Contains(.25, -1));
		Assert.IsTrue(circle.Contains(Math.BitDecrement(.75), -.5));
		Assert.IsFalse(circle.Contains(Math.BitIncrement(.75), -.5));
		Assert.IsFalse(circle.Contains(double.NaN, -.5));
		Assert.IsFalse(circle.Contains(.25, double.PositiveInfinity));
		Assert.IsFalse(new VisitorPickCircle(0, 0, 0).Contains(0, 0));
	}

	[TestMethod]
	public void LiteralFrameGeometryHasTheUnprojectedRadiusAndOriginalEdgeCenter()
	{
		var circle = Create(new N.Vector3(0, 0, -20), Frame, false, Canvas, out var pointer, out var depth);
		Assert.AreEqual(-0.024859222776089564, circle.X, 2e-9);
		Assert.AreEqual(0d, circle.Y, 0d);
		Assert.AreEqual(0.011500243850104397, circle.RadiusSquared, 1e-12);
		Assert.AreEqual(0f, pointer.X);
		Assert.AreEqual(0.0006514489650726318f, pointer.Y);
		var projection = OriginalProjection.Create(Canvas.X / Canvas.Y);
		var clip = N.Vector4.Transform(new N.Vector4(0, 0, -20, 1), projection);
		Assert.AreEqual(clip.Z / clip.W, depth, 1e-7f);
	}

	[DataTestMethod]
	[DataRow(2f, 512f, 192f)]
	[DataRow(20f, 532f, 320f)]
	public void NearAndFarCirclesAndRectanglesEachHaveHitsTheOtherShapeMisses(float hostDepth, float rectangleX, float rectangleY)
	{
		var hotspot = new N.Vector3(0, 0, -hostDepth);
		var projection = OriginalProjection.Create(Canvas.X / Canvas.Y);
		var rectangleOnly = new N.Vector2(rectangleX, rectangleY);
		Assert.IsTrue(GuestWorldPicker.TryHit(hotspot, Frame, false, N.Matrix4x4.Identity, projection, Canvas, rectangleOnly, out var rectangleDepth));
		Assert.IsTrue(VisitorCirclePicker.TryCreate(hotspot, Frame, false, N.Matrix4x4.Identity, projection, Canvas, rectangleOnly,
			out var circle, out var pointer, out var circleDepth), "Eligibility must not incorporate the circle hit result.");
		Assert.IsFalse(circle.Contains(pointer.X, pointer.Y));
		Assert.AreEqual(rectangleDepth, circleDepth);

		// Below the hotspot is outside this upward-facing rectangle, but remains inside the circle.
		var circleOnly = new N.Vector2((float)((circle.X + 1) * Canvas.X / 2), 400);
		Assert.IsTrue(VisitorCirclePicker.TryCreate(hotspot, Frame, false, N.Matrix4x4.Identity, projection, Canvas, circleOnly,
			out circle, out pointer, out _));
		Assert.IsTrue(circle.Contains(pointer.X, pointer.Y));
		Assert.IsFalse(GuestWorldPicker.TryHit(hotspot, Frame, false, N.Matrix4x4.Identity, projection, Canvas, circleOnly, out _));
	}

	[TestMethod]
	public void MirroringNegatesTheSignedOriginRatherThanUsingTheSortedRectangleEdge()
	{
		var frame = Frame with { OriginX = -7 };
		var hotspot = new N.Vector3(2, 1, -20);
		var ordinary = Create(hotspot, frame, false, Canvas, out _, out _);
		var mirrored = Create(hotspot, frame, true, Canvas, out _, out _);
		var projection = OriginalProjection.Create(Canvas.X / Canvas.Y);
		var clip = N.Vector4.Transform(new N.Vector4(hotspot, 1), projection);
		var baseX = (double)clip.X / clip.W;
		var nativeW = Math.Sin(Math.PI / 4) * clip.W / .2;
		var offset = -7d / 128 * 20 * .75 / nativeW;
		Assert.AreEqual(baseX + offset, ordinary.X, 2e-9);
		Assert.AreEqual(baseX - offset, mirrored.X, 2e-9);
		Assert.AreEqual(ordinary.RadiusSquared, mirrored.RadiusSquared);
		Assert.AreEqual(ordinary.Y, mirrored.Y);
		Assert.IsTrue(mirrored.X > baseX);
		var sortedLeftOffset = -(frame.OriginX + frame.Width) / 128d * 20 * .75 / nativeW;
		Assert.IsTrue(Math.Abs(mirrored.X - (baseX + sortedLeftOffset)) > .01,
			"The mirrored quad's left edge is not the circle's logical first edge.");
		var positiveOrigin = Create(hotspot, frame with { OriginX = 7 }, false, Canvas, out _, out _);
		Assert.AreEqual(mirrored.X, positiveOrigin.X, 0d);
	}

	[TestMethod]
	public void VerticalOriginAndAtlasPositionDoNotMoveOrResizeTheCircle()
	{
		var hotspot = new N.Vector3(1, -2, -20);
		var original = Create(hotspot, Frame, false, Canvas, out _, out _);
		foreach (var originY in new[] { -100, -43, 0, 19, 100 })
		{
			var moved = Create(hotspot, Frame with { X = 321, Y = 234, OriginY = originY }, false, Canvas, out _, out _);
			Assert.AreEqual(original, moved);
		}
	}

	[TestMethod]
	public void DivisorsAndDisplayAspectApplyToTheirOwnDimensions()
	{
		foreach (var viewport in new[] { Canvas, new N.Vector2(1920, 1080), new N.Vector2(800, 800), new N.Vector2(768, 1024) })
		{
			foreach (var divisors in new[] { (A: (ushort)128, B: (ushort)128), ((ushort)64, (ushort)256), ((ushort)256, (ushort)64) })
			{
				var frame = Frame with { PixelsPerUnitX = divisors.Item1, PixelsPerUnitY = divisors.Item2 };
				var circle = Create(new N.Vector3(0, 0, -20), frame, false, viewport, out _, out _);
				var aspect = (double)viewport.Y / viewport.X;
				var width = 30d / divisors.Item1 * 20 * aspect;
				var height = 43d / divisors.Item2 * 20;
				Assert.AreEqual((double)0.0002f * (width * width + height * height), circle.RadiusSquared, 1e-12);
				Assert.AreEqual(-15d / divisors.Item1 * 20 * aspect / (Math.Sin(Math.PI / 4) * 100), circle.X, 3e-9);
			}
		}
	}

	[TestMethod]
	public void RadiusIsIndependentOfDepthWhileOriginOffsetAndProjectedDepthChange()
	{
		var near = Create(new N.Vector3(0, 0, -5), Frame, false, Canvas, out _, out var nearDepth);
		var far = Create(new N.Vector3(0, 0, -20), Frame, false, Canvas, out _, out var farDepth);
		Assert.AreEqual(near.RadiusSquared, far.RadiusSquared);
		Assert.AreEqual(near.X / 4, far.X, 1e-12);
		Assert.IsTrue(nearDepth < farDepth, "Keep projected Z for aggregation with other host renderers.");
	}

	[TestMethod]
	public void ActualProjectionFieldsMatchTheIndependentRowVectorDerivation()
	{
		foreach (var viewport in new[] { Canvas, new N.Vector2(1920, 1080), new N.Vector2(800, 800), new N.Vector2(768, 1024) })
		{
			var projection = OriginalProjection.Create(viewport.X / viewport.Y, 90);
			Assert.AreEqual(2d * viewport.Y / viewport.X, projection.M11, 3e-7);
			Assert.AreEqual(2f, projection.M22, 3e-7f);
			Assert.AreEqual(10000f / (.1f - 10000f), projection.M33, 1e-7f);
			Assert.AreEqual(-1f, projection.M34);
			Assert.AreEqual(.1f * (10000f / (.1f - 10000f)), projection.M43, 1e-7f);
			foreach (var value in new[] { projection.M12, projection.M13, projection.M14, projection.M21, projection.M23,
				projection.M24, projection.M31, projection.M32, projection.M41, projection.M42, projection.M44 })
				Assert.AreEqual(0f, value);
		}
	}

	[TestMethod]
	public void TranslatedRotatedLookToViewsMatchIndependentNativeCompositionAndTheFactorTwoWitness()
	{
		Assert.AreEqual(.2f, ObjectPlacement.ModelScale);
		Assert.AreEqual(.2f, ParkCameraMode.UnitScale);
		var sine = Math.Sin(Math.PI / 4);
		var cosine = Math.Cos(Math.PI / 4);
		foreach (var viewport in new[] { Canvas, new N.Vector2(1920, 1080), new N.Vector2(800, 800), new N.Vector2(768, 1024) })
		foreach (var pose in new[] { (Eye: new N.Vector3(3, -4, 8), Direction: new N.Vector3(0, 1, -1)),
			(new N.Vector3(-5, 2, 4), new N.Vector3(1, 2, -3)) })
		foreach (var original in new[] { new N.Vector3(8, -6, 40), new N.Vector3(-3, 5, 100),
			new N.Vector3(0, 0, 130), new N.Vector3(17, 11, 250) })
		{
			var originalDepth = original.Z;
			var view = N.Matrix4x4.CreateLookTo(pose.Item1, pose.Item2, N.Vector3.UnitZ);
			Assert.IsTrue(N.Matrix4x4.Invert(view, out var inverse));
			var world = N.Vector3.Transform(new N.Vector3(original.X * .2f, original.Y * .2f, -originalDepth * .2f), inverse);
			var projection = OriginalProjection.Create(viewport.X / viewport.Y);
			using var camera = new CameraScope(view, projection);
			Assert.IsTrue(VisitorCirclePicker.IsCanonicalView(view, projection, viewport));
			Assert.IsTrue(VisitorCirclePicker.TryCreate(world, Frame, false, view, projection, viewport, viewport / 2,
				out var circle, out _, out var depth));
			var clip = N.Vector4.Transform(new N.Vector4(world, 1), view * projection);
			var aspect = (double)viewport.Y / viewport.X;
			var nativeW = sine * originalDepth;
			var nativeHotspotX = 2 * aspect * cosine * original.X / nativeW;
			var nativeHotspotY = -2 * cosine * original.Y / nativeW;
			var nativeOriginOffset = -15d / 128 * 20 * aspect / nativeW;
			Assert.AreEqual(nativeW, sine * clip.W / .2, 2e-5);
			Assert.AreEqual(nativeHotspotX, (double)clip.X / clip.W, 2e-6);
			Assert.AreEqual(nativeHotspotY, -(double)clip.Y / clip.W, 2e-6);
			Assert.AreEqual(nativeHotspotX + nativeOriginOffset, circle.X, 2e-6);
			Assert.AreEqual(nativeHotspotY, circle.Y, 2e-6);
			Assert.AreEqual(clip.Z / clip.W, depth, 3e-7f);

			// Independently project the existing rendered quad's origin, without borrowing circle extents.
			var right = new N.Vector3(view.M11, view.M21, view.M31);
			var hostOrigin = world + right * (Frame.OriginX * GuestRenderer.UnitsPerPixel * Frame.ScaleX);
			var hostOriginClip = N.Vector4.Transform(new N.Vector4(hostOrigin, 1), view * projection);
			var hostQuadOffset = (double)hostOriginClip.X / hostOriginClip.W - (double)clip.X / clip.W;
			Assert.AreEqual(2 * nativeOriginOffset, hostQuadOffset, 2e-6,
				"The existing host quad is twice the literal native origin displacement; do not reuse its projected corners as circle size.");
		}
	}

	[TestMethod]
	public void PointerCenterUsesTheAsymmetricVerticalConstantAndOnlyOneFinalFloatStore()
	{
		Assert.IsTrue(VisitorCirclePicker.TryPointer(new N.Vector2(2048, 1536), new N.Vector2(1024, 768), out var pointer));
		Assert.AreEqual(0f, pointer.X);
		Assert.AreEqual(0.0006514489650726318f, pointer.Y);
		Assert.AreNotEqual(0.0006514787673950195f, pointer.Y, "An intermediate float product loses the intended final-store result.");
		Assert.IsTrue(pointer.Y > 0);
	}

	[TestMethod]
	public void FractionalDrawableInputIsTruncatedAfterScalingIntoVirtualCoordinates()
	{
		Assert.IsTrue(VisitorCirclePicker.TryPointer(Canvas, new N.Vector2(512.24f, 384.24f), out var withinCell));
		Assert.IsTrue(VisitorCirclePicker.TryPointer(Canvas, new N.Vector2(512.49f, 384.49f), out var sameCell));
		Assert.AreEqual(withinCell, sameCell);
		Assert.AreEqual(new N.Vector2(0, 0.0006514489650726318f), withinCell);
		Assert.IsTrue(VisitorCirclePicker.TryPointer(Canvas, new N.Vector2(512.5f, 384.5f), out var nextCell));
		Assert.AreEqual(new N.Vector2(1f / 1024, 0.0019543804228305817f), nextCell);
		Assert.IsTrue(VisitorCirclePicker.TryPointer(Canvas * 2, new N.Vector2(1025, 769), out var drawable));
		Assert.AreEqual(nextCell, drawable, "A proportional drawable viewport and pointer must produce the same virtual pixel.");
		Assert.IsFalse(VisitorCirclePicker.TryPointer(Canvas, new N.Vector2(1025, 769), out _),
			"Drawable input outside a mistakenly supplied logical viewport must be rejected.");
	}

	[TestMethod]
	public void PointerHasInclusiveTopLeftAndExclusiveBottomRightExtents()
	{
		Assert.IsTrue(VisitorCirclePicker.TryPointer(Canvas, N.Vector2.Zero, out var first));
		Assert.AreEqual(new N.Vector2(-1), first);
		Assert.IsTrue(VisitorCirclePicker.TryPointer(Canvas,
			new N.Vector2(MathF.BitDecrement(Canvas.X), MathF.BitDecrement(Canvas.Y)), out var last));
		Assert.AreEqual(2047f / 1024 - 1, last.X);
		Assert.AreEqual(0.9999999403953552f, last.Y);
		Assert.IsFalse(VisitorCirclePicker.TryPointer(Canvas, new N.Vector2(Canvas.X, 0), out _));
		Assert.IsFalse(VisitorCirclePicker.TryPointer(Canvas, new N.Vector2(0, Canvas.Y), out _));
		Assert.IsFalse(VisitorCirclePicker.TryPointer(Canvas, new N.Vector2(-float.Epsilon, 0), out _));
		Assert.IsFalse(VisitorCirclePicker.TryPointer(Canvas, new N.Vector2(0, -float.Epsilon), out _));
	}

	[TestMethod]
	public void PointerScalingUsesDoubleBeforeMultiplyingLargeFiniteDrawableCoordinates()
	{
		var viewport = new N.Vector2(float.MaxValue, float.MaxValue);
		Assert.IsTrue(VisitorCirclePicker.TryPointer(viewport, viewport / 2, out var pointer));
		Assert.AreEqual(new N.Vector2(0, 0.0006514489650726318f), pointer);
		Assert.IsTrue(VisitorCirclePicker.TryPointer(new N.Vector2(float.Epsilon), N.Vector2.Zero, out var tiny));
		Assert.AreEqual(new N.Vector2(-1), tiny);
	}

	[TestMethod]
	public void InvalidViewportAndPointerValuesNeverProduceGeometry()
	{
		var projection = OriginalProjection.Create(Canvas.X / Canvas.Y);
		foreach (var viewport in new[] { N.Vector2.Zero, new N.Vector2(-1, 768), new N.Vector2(1024, 0),
			new N.Vector2(float.NaN, 768), new N.Vector2(1024, float.NaN), new N.Vector2(float.PositiveInfinity, 768),
			new N.Vector2(1024, float.NegativeInfinity) })
		{
			Assert.IsFalse(VisitorCirclePicker.TryPointer(viewport, N.Vector2.Zero, out _));
			Assert.IsFalse(VisitorCirclePicker.TryCreate(new N.Vector3(0, 0, -20), Frame, false, N.Matrix4x4.Identity,
				projection, viewport, N.Vector2.Zero, out _, out _, out _));
		}
		foreach (var mouse in new[] { new N.Vector2(float.NaN, 10), new N.Vector2(10, float.NaN),
			new N.Vector2(float.PositiveInfinity, 10), new N.Vector2(10, float.NegativeInfinity),
			new N.Vector2(-1, 10), new N.Vector2(10, -1), new N.Vector2(1024, 10), new N.Vector2(10, 768) })
		{
			Assert.IsFalse(VisitorCirclePicker.TryPointer(Canvas, mouse, out _));
			Assert.IsFalse(VisitorCirclePicker.TryCreate(new N.Vector3(0, 0, -20), Frame, false, N.Matrix4x4.Identity,
				projection, Canvas, mouse, out _, out _, out _));
		}
	}

	[TestMethod]
	public void InvalidDimensionsDivisorsAndNonfiniteOrOverflowingHotspotsAreIneligible()
	{
		var projection = OriginalProjection.Create(Canvas.X / Canvas.Y);
		foreach (var frame in new[] { Frame with { Width = 0 }, Frame with { Width = -1 }, Frame with { Height = 0 },
			Frame with { Height = -1 }, Frame with { PixelsPerUnitX = 0 }, Frame with { PixelsPerUnitY = 0 } })
			Assert.IsFalse(VisitorCirclePicker.TryCreate(new N.Vector3(0, 0, -20), frame, false, N.Matrix4x4.Identity,
				projection, Canvas, Canvas / 2, out _, out _, out _));
		foreach (var hotspot in new[] { new N.Vector3(float.NaN, 0, -20), new N.Vector3(0, float.PositiveInfinity, -20),
			new N.Vector3(0, 0, float.NegativeInfinity), new N.Vector3(float.MaxValue, float.MaxValue, -20),
			N.Vector3.Zero, new N.Vector3(0, 0, 20) })
			Assert.IsFalse(VisitorCirclePicker.TryCreate(hotspot, Frame, false, N.Matrix4x4.Identity,
				projection, Canvas, Canvas / 2, out _, out _, out _));
		var overflowingProjection = projection; overflowingProjection.M11 = float.MaxValue;
		Assert.IsFalse(VisitorCirclePicker.TryCreate(new N.Vector3(2, 0, -20), Frame, false, N.Matrix4x4.Identity,
			overflowingProjection, Canvas, Canvas / 2, out _, out _, out _));
		var overflowingView = N.Matrix4x4.Identity; overflowingView.M41 = float.MaxValue;
		Assert.IsFalse(VisitorCirclePicker.TryCreate(new N.Vector3(float.MaxValue, 0, -20), Frame, false, overflowingView,
			projection, Canvas, Canvas / 2, out _, out _, out _));
	}

	[TestMethod]
	public void OverflowingOrUnnegatableFrameOriginsDoNotReachLegacyIntegerExtents()
	{
		var projection = OriginalProjection.Create(Canvas.X / Canvas.Y);
		foreach (var frame in new[] { Frame with { OriginX = int.MaxValue }, Frame with { OriginY = int.MaxValue },
			Frame with { OriginX = int.MinValue }, Frame with { OriginY = int.MinValue },
			Frame with { OriginX = 1, Width = int.MaxValue }, Frame with { OriginY = 1, Height = int.MaxValue } })
		foreach (var mirror in new[] { false, true })
			Assert.IsFalse(VisitorCirclePicker.TryCreate(new N.Vector3(0, 0, -20), frame, mirror, N.Matrix4x4.Identity,
				projection, Canvas, Canvas / 2, out _, out _, out _));
	}

	[TestMethod]
	public void LowLevelGeometryPreservesTheLegacyFinalCornerDepthRatherThanHotspotDepth()
	{
		var view = N.Matrix4x4.Identity; view.M31 = .1f;
		var hotspot = new N.Vector3(0, 0, .5f);
		var mouse = new N.Vector2(540, 380);
		Assert.IsTrue(GuestWorldPicker.TryHit(hotspot, Frame, false, view, N.Matrix4x4.Identity, Canvas, mouse, out var rectangleDepth));
		Assert.IsTrue(VisitorCirclePicker.TryCreate(hotspot, Frame, false, view, N.Matrix4x4.Identity,
			Canvas, mouse, out _, out _, out var circleDepth));
		Assert.AreEqual(rectangleDepth, circleDepth);
		Assert.AreNotEqual(.5f, circleDepth, "This deliberately noncanonical fixture separates final-corner and hotspot projected Z.");
	}

	[TestMethod]
	public void LegacyProjectedDepthClipRemainsInclusiveAtZeroAndOne()
	{
		// The low-level geometry helper deliberately does not establish the global canonical-view gate.
		foreach (var depth in new[] { 0f, 1f })
		{
			var hotspot = new N.Vector3(0, 0, depth);
			Assert.IsTrue(VisitorCirclePicker.TryCreate(hotspot, Frame, false, N.Matrix4x4.Identity, N.Matrix4x4.Identity,
				Canvas, Canvas / 2, out _, out _, out var actual));
			Assert.AreEqual(depth, actual);
		}
		foreach (var depth in new[] { -.001f, MathF.BitIncrement(1f) })
			Assert.IsFalse(VisitorCirclePicker.TryCreate(new N.Vector3(0, 0, depth), Frame, false,
				N.Matrix4x4.Identity, N.Matrix4x4.Identity, Canvas, Canvas / 2, out _, out _, out _));
	}

	[TestMethod]
	public void OriginalProjectionRejectsGeometryBeforeNearAndBeyondFarPlanes()
	{
		var projection = OriginalProjection.Create(Canvas.X / Canvas.Y);
		foreach (var depth in new[] { .05f, 100000f })
		{
			var hotspot = new N.Vector3(0, 0, -depth);
			Assert.IsFalse(GuestWorldPicker.TryHit(hotspot, Frame, false, N.Matrix4x4.Identity, projection, Canvas, Canvas / 2, out _));
			Assert.IsFalse(VisitorCirclePicker.TryCreate(hotspot, Frame, false, N.Matrix4x4.Identity, projection,
				Canvas, Canvas / 2, out _, out _, out _));
		}
		foreach (var depth in new[] { .1001f, 1000f })
			Assert.IsTrue(VisitorCirclePicker.TryCreate(new N.Vector3(0, 0, -depth), Frame, false, N.Matrix4x4.Identity,
				projection, Canvas, Canvas / 2, out _, out _, out _));
	}

	[TestMethod]
	public void AValidHotspotDoesNotBypassLegacyNearOrFarCornerClipping()
	{
		var view = N.Matrix4x4.Identity; view.M31 = 1;
		var hotspot = new N.Vector3(0, 0, .5f);
		var clip = N.Vector4.Transform(new N.Vector4(hotspot, 1), view);
		Assert.IsTrue(clip.W > 0 && clip.Z / clip.W > 0 && clip.Z / clip.W < 1);
		foreach (var originX in new[] { 0, -30 })
		{
			var frame = Frame with { OriginX = originX };
			Assert.IsFalse(GuestWorldPicker.TryHit(hotspot, frame, false, view, N.Matrix4x4.Identity, Canvas, Canvas / 2, out _));
			Assert.IsFalse(VisitorCirclePicker.TryCreate(hotspot, frame, false, view, N.Matrix4x4.Identity,
				Canvas, Canvas / 2, out _, out _, out _), "Checking only the hotspot would admit a clipped legacy corner.");
		}
	}

	[TestMethod]
	public void CanonicalGateRequiresParkModeOriginalProjectionAndExactlyNinetyDegrees()
	{
		var view = N.Matrix4x4.Identity;
		var projection = OriginalProjection.Create(Canvas.X / Canvas.Y);
		using var camera = new CameraScope(view, projection);
		Assert.IsTrue(VisitorCirclePicker.IsCanonicalView(view, projection, Canvas));
		Camera.Mode = new CameraMode { UsesOriginalProjection = true, FieldOfView = 90 };
		Assert.IsFalse(VisitorCirclePicker.IsCanonicalView(view, projection, Canvas));
		Camera.Mode = ParkMode();
		Camera.Mode.UsesOriginalProjection = false;
		Assert.IsFalse(VisitorCirclePicker.IsCanonicalView(view, projection, Canvas));
		Camera.Mode.UsesOriginalProjection = true;
		foreach (var fov in new[] { MathF.BitDecrement(90), MathF.BitIncrement(90), 89f, 91f, float.NaN, float.PositiveInfinity })
		{
			Camera.Mode.FieldOfView = fov;
			Assert.IsFalse(VisitorCirclePicker.IsCanonicalView(view, projection, Canvas));
		}
		Camera.Mode = null!;
		Assert.IsFalse(VisitorCirclePicker.IsCanonicalView(view, projection, Canvas));
	}

	[TestMethod]
	public void SuppliedMatricesMustMatchEveryCurrentCameraFieldExactly()
	{
		var view = N.Matrix4x4.Identity;
		var projection = OriginalProjection.Create(Canvas.X / Canvas.Y);
		using var camera = new CameraScope(view, projection);
		for (var field = 0; field < 16; field++)
		{
			Camera.ViewMatrix = ChangeField(view, field, value => MathF.BitIncrement(value));
			Assert.IsFalse(VisitorCirclePicker.IsCanonicalView(view, projection, Canvas), $"Camera view field {field} mismatch.");
			Camera.ViewMatrix = view;
			Camera.ProjMatrix = ChangeField(projection, field, value => MathF.BitIncrement(value));
			Assert.IsFalse(VisitorCirclePicker.IsCanonicalView(view, projection, Canvas), $"Camera projection field {field} mismatch.");
			Camera.ProjMatrix = projection;
		}
	}

	[TestMethod]
	public void EvenMatchingCameraAndCallerProjectionsMustMatchEveryCanonicalFieldExactly()
	{
		var view = N.Matrix4x4.Identity;
		var projection = OriginalProjection.Create(Canvas.X / Canvas.Y);
		using var camera = new CameraScope(view, projection);
		for (var field = 0; field < 16; field++)
		{
			var changed = ChangeField(projection, field, value => MathF.BitIncrement(value));
			Camera.ProjMatrix = changed;
			Assert.IsFalse(VisitorCirclePicker.IsCanonicalView(view, changed, Canvas), $"Canonical projection field {field} mismatch.");
		}
		foreach (var changed in new[] { N.Matrix4x4.Identity, N.Matrix4x4.CreateOrthographic(10, 10, .1f, 10000),
			OriginalProjection.Create(Canvas.X / Canvas.Y, 89), N.Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2, Canvas.X / Canvas.Y, .1f, 10000) })
		{
			Camera.ProjMatrix = changed;
			Assert.IsFalse(VisitorCirclePicker.IsCanonicalView(view, changed, Canvas));
		}
		var reversedDepth = projection; reversedDepth.M33 = -reversedDepth.M33; reversedDepth.M43 = -reversedDepth.M43;
		Camera.ProjMatrix = reversedDepth;
		Assert.IsFalse(VisitorCirclePicker.IsCanonicalView(view, reversedDepth, Canvas));
	}

	[TestMethod]
	public void CanonicalGateRejectsViewportMismatchInvalidDimensionsAndOverflowingAspect()
	{
		var projection = OriginalProjection.Create(Canvas.X / Canvas.Y);
		using var camera = new CameraScope(N.Matrix4x4.Identity, projection);
		foreach (var viewport in new[] { new N.Vector2(1920, 1080), new N.Vector2(1024, 769), N.Vector2.Zero,
			new N.Vector2(-1024, 768), new N.Vector2(1024, float.NaN), new N.Vector2(float.PositiveInfinity, 768),
			new N.Vector2(float.MaxValue, float.Epsilon), new N.Vector2(float.Epsilon, float.MaxValue) })
			Assert.IsFalse(VisitorCirclePicker.IsCanonicalView(N.Matrix4x4.Identity, projection, viewport));
	}

	[TestMethod]
	public void NonfiniteValuesInAnyViewOrProjectionFieldAreIneligible()
	{
		var projection = OriginalProjection.Create(Canvas.X / Canvas.Y);
		using var camera = new CameraScope(N.Matrix4x4.Identity, projection);
		foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
		for (var field = 0; field < 16; field++)
		{
			var view = ChangeField(N.Matrix4x4.Identity, field, _ => value);
			Camera.ViewMatrix = view;
			Assert.IsFalse(VisitorCirclePicker.IsCanonicalView(view, projection, Canvas));
			Assert.IsFalse(VisitorCirclePicker.TryCreate(new N.Vector3(0, 0, -20), Frame, false, view, projection,
				Canvas, Canvas / 2, out _, out _, out _));
			Camera.ViewMatrix = N.Matrix4x4.Identity;
			var invalidProjection = ChangeField(projection, field, _ => value);
			Camera.ProjMatrix = invalidProjection;
			Assert.IsFalse(VisitorCirclePicker.IsCanonicalView(N.Matrix4x4.Identity, invalidProjection, Canvas));
			Assert.IsFalse(VisitorCirclePicker.TryCreate(new N.Vector3(0, 0, -20), Frame, false, N.Matrix4x4.Identity,
				invalidProjection, Canvas, Canvas / 2, out _, out _, out _));
			Camera.ProjMatrix = projection;
		}
	}

	[TestMethod]
	public void CanonicalViewRejectsScaleShearReflectionNonaffineAndOverflowingBases()
	{
		var projection = OriginalProjection.Create(Canvas.X / Canvas.Y);
		using var camera = new CameraScope(N.Matrix4x4.Identity, projection);
		var shear = N.Matrix4x4.Identity; shear.M12 = .01f;
		var overflow = N.Matrix4x4.Identity; overflow.M11 = float.MaxValue;
		foreach (var view in new[] { N.Matrix4x4.CreateScale(2), N.Matrix4x4.CreateScale(1.001f, 1, 1),
			N.Matrix4x4.CreateScale(-1, 1, 1), N.Matrix4x4.CreateScale(0), shear, overflow,
			ChangeField(N.Matrix4x4.Identity, 3, _ => float.Epsilon),
			ChangeField(N.Matrix4x4.Identity, 7, _ => float.Epsilon),
			ChangeField(N.Matrix4x4.Identity, 11, _ => float.Epsilon),
			ChangeField(N.Matrix4x4.Identity, 15, value => MathF.BitIncrement(value)) })
		{
			Camera.ViewMatrix = view;
			Assert.IsFalse(VisitorCirclePicker.IsCanonicalView(view, projection, Canvas));
		}
	}

	[TestMethod]
	public void BasisDotToleranceIncludesItsExactFloatBoundaryButExcludesTheNextFloat()
	{
		var projection = OriginalProjection.Create(Canvas.X / Canvas.Y);
		using var camera = new CameraScope(N.Matrix4x4.Identity, projection);
		const float tolerance = 1e-5f;
		foreach (var amount in new[] { MathF.BitDecrement(tolerance), tolerance, MathF.BitIncrement(tolerance),
			-MathF.BitDecrement(tolerance), -tolerance, -MathF.BitIncrement(tolerance) })
		{
			var view = N.Matrix4x4.Identity; view.M12 = amount;
			Camera.ViewMatrix = view;
			Assert.AreEqual(MathF.Abs(amount) <= tolerance, VisitorCirclePicker.IsCanonicalView(view, projection, Canvas), $"Shear {amount:R}.");
		}
	}

	[TestMethod]
	public void BasisLengthAndPositiveDeterminantAreCheckedIndependently()
	{
		var projection = OriginalProjection.Create(Canvas.X / Canvas.Y);
		using var camera = new CameraScope(N.Matrix4x4.Identity, projection);
		var acceptable = N.Matrix4x4.CreateScale(1.000004f, 1, 1);
		Camera.ViewMatrix = acceptable;
		Assert.IsTrue(VisitorCirclePicker.IsCanonicalView(acceptable, projection, Canvas));
		var badLength = N.Matrix4x4.CreateScale(1.000006f, 1, 1);
		Camera.ViewMatrix = badLength;
		Assert.IsFalse(VisitorCirclePicker.IsCanonicalView(badLength, projection, Canvas), "Length squared exceeds tolerance even though the determinant does not.");
		var badDeterminant = N.Matrix4x4.CreateScale(1.000004f);
		Camera.ViewMatrix = badDeterminant;
		Assert.IsFalse(VisitorCirclePicker.IsCanonicalView(badDeterminant, projection, Canvas), "Each basis length passes, but their determinant does not.");
	}

	[TestMethod]
	public void FiniteTranslationDoesNotInvalidateAnOtherwiseCanonicalView()
	{
		var projection = OriginalProjection.Create(Canvas.X / Canvas.Y);
		using var camera = new CameraScope(N.Matrix4x4.Identity, projection);
		foreach (var translation in new[] { new N.Vector3(12, -34, 56), new N.Vector3(float.MaxValue, -float.MaxValue, float.Epsilon) })
		{
			var view = N.Matrix4x4.CreateTranslation(translation);
			Camera.ViewMatrix = view;
			Assert.IsTrue(VisitorCirclePicker.IsCanonicalView(view, projection, Canvas),
				"Finite translation is allowed; per-hotspot overflow is checked when geometry is created.");
		}
	}

	[TestMethod]
	public void RectangleRefactorMatchesFrozenLegacyArithmeticAcrossSyntheticViews()
	{
		var shear = N.Matrix4x4.Identity; shear.M31 = .5f;
		foreach (var viewport in new[] { Canvas, new N.Vector2(1920, 1080) })
		foreach (var view in new[] { N.Matrix4x4.Identity, shear,
			N.Matrix4x4.CreateLookTo(new N.Vector3(3, -4, 8), new N.Vector3(0, 1, -1), N.Vector3.UnitZ) })
		foreach (var projection in new[] { N.Matrix4x4.Identity, OriginalProjection.Create(viewport.X / viewport.Y) })
		foreach (var frame in new[] { Frame, new GuestAtlasFrame(12, 34, 10, 20, 5, -20),
			Frame with { PixelsPerUnitX = 64, PixelsPerUnitY = 256 }, Frame with { PixelsPerUnitX = 0, PixelsPerUnitY = 0 } })
		foreach (var hotspot in new[] { new N.Vector3(0, 0, .25f), new N.Vector3(.1f, -.2f, .75f),
			new N.Vector3(0, 0, -.05f), new N.Vector3(0, 0, -2), new N.Vector3(0, 0, -20), new N.Vector3(0, 0, -100000) })
		foreach (var mirror in new[] { false, true })
		foreach (var rotation in new[] { 0f, .3f, -.7f, MathF.PI / 2 })
		foreach (var mouse in new[] { N.Vector2.Zero, viewport / 2, viewport * .25f,
			new N.Vector2(viewport.X - 1, viewport.Y - 1) })
			CompareLegacy(hotspot, frame, mirror, view, projection, viewport, mouse, rotation);
	}

	[TestMethod]
	public void RectangleRefactorPreservesExactFailureDepthForInvalidAndPartlyClippedInputs()
	{
		var view = N.Matrix4x4.Identity;
		var projection = OriginalProjection.Create(Canvas.X / Canvas.Y);
		var hotspot = new N.Vector3(0, 0, -20);
		foreach (var viewport in new[] { N.Vector2.Zero, new N.Vector2(-1, 768), new N.Vector2(1024, 0),
			new N.Vector2(float.NaN, 768), new N.Vector2(1024, float.PositiveInfinity) })
			CompareLegacy(hotspot, Frame, false, view, projection, viewport, N.Vector2.Zero);
		foreach (var mouse in new[] { new N.Vector2(-1, 0), new N.Vector2(0, -1), new N.Vector2(1024, 0),
			new N.Vector2(0, 768), new N.Vector2(float.NaN, 384), new N.Vector2(512, float.PositiveInfinity) })
			CompareLegacy(hotspot, Frame, false, view, projection, Canvas, mouse);
		foreach (var frame in new[] { Frame with { Width = 0 }, Frame with { Width = -1 }, Frame with { Height = 0 },
			Frame with { Height = -1 }, Frame with { OriginX = int.MinValue }, Frame with { OriginY = int.MaxValue } })
		foreach (var mirror in new[] { false, true })
			CompareLegacy(hotspot, frame, mirror, view, projection, Canvas, Canvas / 2);
		foreach (var invalidHotspot in new[] { N.Vector3.Zero, new N.Vector3(0, 0, 20), new N.Vector3(float.NaN, 0, -20),
			new N.Vector3(0, float.PositiveInfinity, -20), new N.Vector3(float.MaxValue, float.MaxValue, -20) })
			CompareLegacy(invalidHotspot, Frame, false, view, projection, Canvas, Canvas / 2);
		foreach (var rotation in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
			CompareLegacy(hotspot, Frame, false, view, projection, Canvas, Canvas / 2, rotation);
		foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue })
		for (var field = 0; field < 16; field++)
		{
			CompareLegacy(hotspot, Frame, false, ChangeField(view, field, _ => value), projection, Canvas, Canvas / 2);
			CompareLegacy(hotspot, Frame, false, view, ChangeField(projection, field, _ => value), Canvas, Canvas / 2);
		}

		// First corner passes and sets depth, then the right corner crosses the far plane.
		var partlyClipped = view; partlyClipped.M31 = .5f;
		var partlyClippedHotspot = new N.Vector3(0, 0, .75f);
		Assert.IsFalse(FrozenLegacyTryHit(partlyClippedHotspot, Frame, false, partlyClipped, N.Matrix4x4.Identity,
			Canvas, Canvas / 2, out var partialDepth));
		Assert.IsTrue(partialDepth > 0 && partialDepth < 1, "This fixture must exercise a nonzero depth on failure.");
		CompareLegacy(partlyClippedHotspot, Frame, false, partlyClipped, N.Matrix4x4.Identity, Canvas, Canvas / 2);
	}

	private static void CompareLegacy(N.Vector3 hotspot, GuestAtlasFrame frame, bool mirror, N.Matrix4x4 view,
		N.Matrix4x4 projection, N.Vector2 viewport, N.Vector2 mouse, float rotation = 0)
	{
		var expected = FrozenLegacyTryHit(hotspot, frame, mirror, view, projection, viewport, mouse, out var expectedDepth, rotation);
		var actual = GuestWorldPicker.TryHit(hotspot, frame, mirror, view, projection, viewport, mouse, out var actualDepth, rotation);
		Assert.AreEqual(expected, actual, "The rectangle refactor changed the legacy hit outcome.");
		Assert.AreEqual(BitConverter.SingleToInt32Bits(expectedDepth), BitConverter.SingleToInt32Bits(actualDepth),
			"The rectangle refactor changed depth bits, including on failure.");
	}

	// Frozen pre-refactor GuestWorldPicker.TryHit arithmetic, copied from the base commit.
	private static bool FrozenLegacyTryHit(N.Vector3 feet, GuestAtlasFrame frame, bool mirror, N.Matrix4x4 view,
		N.Matrix4x4 projection, N.Vector2 viewport, N.Vector2 mouse, out float depth, float rotation = 0f)
	{
		depth = 0;
		if (!float.IsFinite(viewport.X) || !float.IsFinite(viewport.Y) || viewport.X <= 0 || viewport.Y <= 0 ||
			!float.IsFinite(mouse.X) || !float.IsFinite(mouse.Y) || mouse.X < 0 || mouse.Y < 0 ||
			mouse.X >= viewport.X || mouse.Y >= viewport.Y || frame.Width <= 0 || frame.Height <= 0)
			return false;
		var right = new N.Vector3(view.M11, view.M21, view.M31);
		var up = new N.Vector3(view.M12, view.M22, view.M32);
		// Same hotspot, mirroring, scale and camera basis used by GuestRenderer.OnRender.
		var (x0, x1, top, bottom) = frame.Extents(mirror, GuestRenderer.UnitsPerPixel);
		var transform = view * projection;
		var min = new N.Vector2(float.PositiveInfinity);
		var max = new N.Vector2(float.NegativeInfinity);
		for (var corner = 0; corner < 4; corner++)
		{
			var (cx, cy) = ((corner & 1) == 0 ? x0 : x1, (corner & 2) == 0 ? top : bottom);
			var (sin, cos) = MathF.SinCos( rotation );
			var point = feet + right * (cx * cos + cy * sin) + up * (-cx * sin + cy * cos);
			var clip = N.Vector4.Transform(new N.Vector4(point, 1), transform);
			if (!float.IsFinite(clip.W) || clip.W <= 0)
				return false;
			var z = clip.Z / clip.W;
			var screen = new N.Vector2((clip.X / clip.W + 1) * viewport.X / 2, (1 - clip.Y / clip.W) * viewport.Y / 2);
			if (!float.IsFinite(z) || z < 0 || z > 1 || !float.IsFinite(screen.X) || !float.IsFinite(screen.Y))
				return false;
			depth = z;
			min = N.Vector2.Min(min, screen);
			max = N.Vector2.Max(max, screen);
		}
		return max.X > min.X && max.Y > min.Y && mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;
	}

	private static VisitorPickCircle Create(N.Vector3 hotspot, GuestAtlasFrame frame, bool mirror, N.Vector2 viewport,
		out N.Vector2 pointer, out float depth)
	{
		Assert.IsTrue(VisitorCirclePicker.TryCreate(hotspot, frame, mirror, N.Matrix4x4.Identity,
			OriginalProjection.Create(viewport.X / viewport.Y), viewport, viewport / 2, out var circle, out pointer, out depth));
		return circle;
	}

	private static ParkCameraMode ParkMode()
	{
		// This guard needs only mode identity and its two flags; avoid constructing or consulting a park.
		var mode = (ParkCameraMode)RuntimeHelpers.GetUninitializedObject(typeof(ParkCameraMode));
		mode.UsesOriginalProjection = true;
		mode.FieldOfView = 90;
		return mode;
	}

	private sealed class CameraScope : IDisposable
	{
		private readonly CameraMode mode = Camera.Mode;
		private readonly N.Matrix4x4 view = Camera.ViewMatrix;
		private readonly N.Matrix4x4 projection = Camera.ProjMatrix;

		internal CameraScope(N.Matrix4x4 currentView, N.Matrix4x4 currentProjection)
		{
			Camera.Mode = ParkMode();
			Camera.ViewMatrix = currentView;
			Camera.ProjMatrix = currentProjection;
		}

		public void Dispose()
		{
			Camera.Mode = mode;
			Camera.ViewMatrix = view;
			Camera.ProjMatrix = projection;
		}
	}

	private static N.Matrix4x4 ChangeField(N.Matrix4x4 value, int field, Func<float, float> change)
	{
		switch (field)
		{
			case 0: value.M11 = change(value.M11); break;
			case 1: value.M12 = change(value.M12); break;
			case 2: value.M13 = change(value.M13); break;
			case 3: value.M14 = change(value.M14); break;
			case 4: value.M21 = change(value.M21); break;
			case 5: value.M22 = change(value.M22); break;
			case 6: value.M23 = change(value.M23); break;
			case 7: value.M24 = change(value.M24); break;
			case 8: value.M31 = change(value.M31); break;
			case 9: value.M32 = change(value.M32); break;
			case 10: value.M33 = change(value.M33); break;
			case 11: value.M34 = change(value.M34); break;
			case 12: value.M41 = change(value.M41); break;
			case 13: value.M42 = change(value.M42); break;
			case 14: value.M43 = change(value.M43); break;
			case 15: value.M44 = change(value.M44); break;
			default: throw new ArgumentOutOfRangeException(nameof(field));
		}
		return value;
	}
}
