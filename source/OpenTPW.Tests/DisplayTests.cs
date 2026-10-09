using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Veldrid;
using Matrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

[TestClass]
public class DisplayTests
{
	private static Point2 Size( int width, int height ) => new( width, height );

	private static void AssertSize( int width, int height, Point2 actual, string? message = null ) =>
		Assert.AreEqual( (width, height), (actual.X, actual.Y), message );

	[TestMethod]
	public void NativeRendersTheWorldAtOutputSize()
	{
		var result = RenderScaling.Compute( Size( 2560, 1440 ), UpscaleMode.Native, 50, 16384 );
		Assert.AreEqual( UpscaleMode.Native, result.Mode );
		Assert.AreEqual( 100, result.EffectivePercent, "Native ignores a stored render scale." );
		AssertSize( 2560, 1440, result.InternalSize );
		AssertSize( 2560, 1440, result.OutputSize );
		Assert.IsNull( result.FallbackReason );
	}

	[TestMethod]
	public void PresetsScaleEachDimensionAndRoundToNearestPixel()
	{
		CollectionAssert.AreEqual( new[] { 77, 67, 59, 50 }, RenderScaling.Presets );
		var expected = new Dictionary<int, (int, int)> { [77] = (1478, 832), [67] = (1286, 724), [59] = (1133, 637), [50] = (960, 540) };
		foreach ( var (percent, size) in expected )
		{
			var result = RenderScaling.Compute( Size( 1920, 1080 ), UpscaleMode.Linear, percent, 16384 );
			Assert.AreEqual( size, (result.InternalSize.X, result.InternalSize.Y ), $"{percent}% of 1920x1080" );
			Assert.AreEqual( UpscaleMode.Linear, result.Mode );
		}
		// 1366 × 0.67 = 915.22 -> 915; 768 × 0.67 = 514.56 -> 515; halves round up: 1365 × 0.5 = 682.5 -> 683.
		AssertSize( 915, 515, RenderScaling.Compute( Size( 1366, 768 ), UpscaleMode.Nearest, 67, 16384 ).InternalSize );
		AssertSize( 683, 384, RenderScaling.Compute( Size( 1365, 768 ), UpscaleMode.Linear, 50, 16384 ).InternalSize );
		AssertSize( 1920, 1080, RenderScaling.Compute( Size( 3840, 2160 ), UpscaleMode.Linear, 50, 16384 ).InternalSize, "50% is a quarter of the pixels." );
	}

	[TestMethod]
	public void InternalSizeIsAtLeastOnePixel()
	{
		AssertSize( 1, 1, RenderScaling.Compute( Size( 1, 1 ), UpscaleMode.Linear, 50, 16384 ).InternalSize );
		AssertSize( 1, 1, RenderScaling.Compute( Size( 1, 2 ), UpscaleMode.Linear, 50, 16384 ).InternalSize );
		Assert.AreEqual( 1, RenderScaling.ScaleDimension( 0, 50 ) );
	}

	[TestMethod]
	public void RoundingErrorIsAtMostHalfAnInternalPixel()
	{
		var outputs = new[] { Size( 1280, 720 ), Size( 1366, 768 ), Size( 1920, 1080 ), Size( 2560, 1080 ), Size( 2560, 1440 ), Size( 2880, 1800 ), Size( 3440, 1440 ), Size( 3840, 2160 ), Size( 5120, 1440 ), Size( 800, 600 ), Size( 333, 211 ) };
		foreach ( var output in outputs )
		{
			for ( var percent = DisplaySettings.MinimumRenderScale; percent <= DisplaySettings.MaximumRenderScale; percent++ )
			{
				var result = RenderScaling.Compute( output, UpscaleMode.Linear, percent, 16384 );
				Assert.IsTrue( RenderScaling.RoundingErrorInInternalPixels( output, result.InternalSize, percent ) <= 0.5, $"{output.X}x{output.Y} at {percent}%" );
				Assert.IsTrue( result.InternalSize.X <= output.X && result.InternalSize.Y <= output.Y );
			}
		}
	}

	[TestMethod]
	public void UnsupportedScaleFallsBackToNativeWithReason()
	{
		foreach ( var percent in new[] { 49, 30, 0, -5, 101, 200 } )
		{
			var result = RenderScaling.Compute( Size( 1920, 1080 ), UpscaleMode.Linear, percent, 16384 );
			Assert.AreEqual( UpscaleMode.Native, result.Mode, $"{percent}%" );
			Assert.AreEqual( 100, result.EffectivePercent );
			AssertSize( 1920, 1080, result.InternalSize );
			StringAssert.Contains( result.FallbackReason, "outside 50-100%" );
			StringAssert.Contains( result.Describe(), "requested Linear" );
		}
		var unknown = RenderScaling.Compute( Size( 640, 480 ), (UpscaleMode)9, 50, 16384 );
		Assert.AreEqual( UpscaleMode.Native, unknown.Mode );
		StringAssert.Contains( unknown.FallbackReason, "unknown upscale mode" );
		var movie = RenderScaling.Compute( Size( 640, 480 ), UpscaleMode.Nearest, 50, 16384, worldScalingAllowed: false );
		Assert.AreEqual( UpscaleMode.Native, movie.Mode );
		AssertSize( 640, 480, movie.InternalSize );
		Assert.IsNotNull( movie.FallbackReason );
	}

	[TestMethod]
	public void DeviceTextureLimitClampsAndKeepsAspect()
	{
		var result = RenderScaling.Compute( Size( 7680, 4320 ), UpscaleMode.Native, 100, 4096 );
		AssertSize( 4096, 2304, result.InternalSize );
		AssertSize( 7680, 4320, result.OutputSize );
		Assert.AreEqual( UpscaleMode.Linear, result.Mode, "A clamped native target has to be upscaled." );
		StringAssert.Contains( result.FallbackReason, "texture limit 4096" );
		Assert.IsNull( RenderScaling.Compute( Size( 7680, 4320 ), UpscaleMode.Linear, 50, 4096 ).FallbackReason, "3840x2160 fits." );
	}

	[TestMethod]
	public void ZeroSizeOutputPausesWithoutAllocating()
	{
		foreach ( var output in new[] { Size( 0, 0 ), Size( 1280, 0 ), Size( 0, 720 ), Size( -1, -1 ) } )
		{
			var result = RenderScaling.Compute( output, UpscaleMode.Linear, 50, 16384 );
			Assert.IsTrue( result.IsPaused );
			AssertSize( 0, 0, result.InternalSize );
		}
	}

	[TestMethod]
	public void MetricsConvertBetweenLogicalUnitsAndPixels()
	{
		var retina = new DisplayMetrics( Size( 1440, 900 ), Size( 2880, 1800 ) );
		Assert.AreEqual( new NumericsVector2( 2, 2 ), retina.PixelsPerLogical );
		Assert.AreEqual( new NumericsVector2( 200, 100 ), retina.LogicalToPixel( new NumericsVector2( 100, 50 ) ) );
		Assert.AreEqual( new NumericsVector2( 100, 50 ), retina.PixelToLogical( new NumericsVector2( 200, 100 ) ) );
		Assert.AreEqual( "2880x1800 pixels (1440x900 logical)", retina.ToString() );
		var windows150 = new DisplayMetrics( Size( 1280, 720 ), Size( 1920, 1080 ) );
		Assert.AreEqual( new NumericsVector2( 1.5f, 1.5f ), windows150.PixelsPerLogical );
		Assert.AreEqual( "1920x1080", new DisplayMetrics( Size( 1920, 1080 ), Size( 1920, 1080 ) ).ToString() );
		var empty = new DisplayMetrics( Size( 0, 0 ), Size( 0, 0 ) );
		Assert.IsTrue( empty.IsEmpty );
		Assert.AreEqual( NumericsVector2.One, empty.PixelsPerLogical, "No division by zero while minimised." );
	}

	/// <summary>
	/// A cell projected to output pixels and converted to logical mouse units picks the same cell for
	/// every DPI factor and render scale: render scale is never a second mouse scale.
	/// </summary>
	[TestMethod]
	public void PickingUsesLogicalUnitsIndependentOfDpiAndRenderScale()
	{
		var logical = Size( 1280, 720 );
		var view = Matrix4x4.CreateLookAt( new NumericsVector3( 0, -40, 40 ), NumericsVector3.Zero, NumericsVector3.UnitZ );
		var cells = new[] { new NumericsVector3( 0, 0, 0 ), new NumericsVector3( 10, 10, 0 ), new NumericsVector3( -20, 6, 0 ), new NumericsVector3( 14, -8, 0 ) };
		foreach ( var dpi in new[] { 1, 2, 3 } )
		{
			var metrics = new DisplayMetrics( logical, Size( logical.X * dpi, logical.Y * dpi ) );
			// The camera aspect follows the output; the world target size does not enter picking at all.
			var projection = Matrix4x4.CreatePerspectiveFieldOfView( MathF.PI / 3, (float)metrics.PixelSize.X / metrics.PixelSize.Y, 0.1f, 1000 );
			foreach ( var percent in new[] { 100, 77, 50 } )
			{
				_ = RenderScaling.Compute( metrics.PixelSize, UpscaleMode.Linear, percent, 16384 );
				foreach ( var cell in cells )
				{
					Assert.IsTrue( ScreenMapping.TryProjectToPixel( cell, view, projection, metrics.PixelSize, out var pixel ) );
					var mouse = metrics.PixelToLogical( pixel );
					Assert.IsTrue( ParkPlacement.TryGetPosition( mouse, new Vector2( logical.X, logical.Y ), view, projection, out var picked ), $"{cell} at {dpi}x, {percent}%" );
					Assert.AreEqual( new Vector3( cell.X, cell.Y, 0 ), picked, $"{cell} at {dpi}x, {percent}%" );
				}
			}
		}
		Assert.IsFalse( ScreenMapping.TryProjectToPixel( new NumericsVector3( 0, -80, 80 ), view, Matrix4x4.CreatePerspectiveFieldOfView( 1, 1, 0.1f, 100 ), logical, out _ ), "Behind the camera." );
	}

	[TestMethod]
	public void UiScaleIsAnIntegerChosenFromOutputPixels()
	{
		var cases = new Dictionary<(int, int), int>
		{
			[(800, 600)] = 1, [(1280, 720)] = 1, [(1920, 1080)] = 1, [(2560, 1080)] = 1, [(2560, 1440)] = 2,
			[(2880, 1800)] = 2, [(3440, 1440)] = 2, [(3840, 2160)] = 3, [(5120, 2880)] = 4, [(320, 200)] = 1, [(0, 0)] = 1
		};
		foreach ( var ((width, height), scale) in cases )
			Assert.AreEqual( scale, UiScaling.Automatic( Size( width, height ) ), $"{width}x{height}" );
		Assert.AreEqual( 3, UiScaling.Resolve( 3, Size( 1280, 720 ) ), "A configured scale wins." );
		Assert.AreEqual( 2, UiScaling.Resolve( 0, Size( 2560, 1440 ) ), "0 is automatic." );
		Assert.AreEqual( 8, UiScaling.Automatic( Size( 100000, 100000 ) ) );
	}

	[TestMethod]
	public void UiScaleKeepsPhysicalTextSizeOnHiDpiOutputs()
	{
		// (pixels, density) -> scale: a density-d window matches a 1x window of the same logical size.
		var cases = new Dictionary<(int, int, int), int>
		{
			[(2432, 1368, 2)] = 2, [(2560, 1440, 2)] = 2, [(3024, 1898, 2)] = 2, [(800, 600, 2)] = 2,
			[(1920, 1080, 2)] = 2, [(3840, 2160, 2)] = 3, [(5120, 2880, 2)] = 4, [(2432, 1368, 1)] = 1, [(3840, 2160, 3)] = 3
		};
		foreach ( var ((width, height, density), scale) in cases )
			Assert.AreEqual( scale, UiScaling.Automatic( Size( width, height ), density ), $"{width}x{height} at {density}x" );
		Assert.AreEqual( 2, UiScaling.Resolve( 0, Size( 2432, 1368 ), 2 ) );
		Assert.AreEqual( 1, UiScaling.Resolve( 1, Size( 2432, 1368 ), 2 ), "A configured scale still wins." );
		Assert.AreEqual( 2, new DisplayMetrics( Size( 1216, 684 ), Size( 2432, 1368 ) ).IntegerPixelDensity );
		Assert.AreEqual( 1, new DisplayMetrics( Size( 1280, 720 ), Size( 1920, 1080 ) ).IntegerPixelDensity, "fractional density rounds down" );
		Assert.AreEqual( 1, new DisplayMetrics( Size( 0, 0 ), Size( 0, 0 ) ).IntegerPixelDensity );
	}

	[TestMethod]
	public void SettingsRoundTripThroughJsonAndKeepDefaultsForMissingFields()
	{
		var settings = new DisplaySettings { Width = 3440, Height = 1440, Mode = WindowMode.Borderless, Upscale = UpscaleMode.Nearest, RenderScale = 59, UiScale = 2 };
		var json = settings.ToJson();
		StringAssert.Contains( json, "\"Upscale\": \"Nearest\"" );
		Assert.IsFalse( json.Contains( '\r' ) );
		var diagnostics = new List<string>();
		Assert.AreEqual( settings, DisplaySettings.FromJson( json, diagnostics ) );
		Assert.AreEqual( 0, diagnostics.Count );

		var partial = DisplaySettings.FromJson( "{ \"Width\": 1920, \"Height\": 1080 }", diagnostics );
		Assert.AreEqual( DisplaySettings.Default with { Width = 1920, Height = 1080 }, partial, "Missing fields keep the native defaults." );
		Assert.AreEqual( UpscaleMode.Native, DisplaySettings.FromJson( "{}", diagnostics ).Upscale );
		Assert.AreEqual( 0, diagnostics.Count );
	}

	[TestMethod]
	public void InvalidSettingsFallBackWithDiagnostics()
	{
		var diagnostics = new List<string>();
		Assert.AreEqual( DisplaySettings.Default, DisplaySettings.FromJson( "{ not json", diagnostics ) );
		StringAssert.Contains( diagnostics.Single(), "invalid" );

		diagnostics.Clear();
		var scale = DisplaySettings.FromJson( "{ \"Upscale\": \"Linear\", \"RenderScale\": 30 }", diagnostics );
		Assert.AreEqual( (UpscaleMode.Native, 100), (scale.Upscale, scale.RenderScale) );
		StringAssert.Contains( diagnostics.Single(), "30%" );

		diagnostics.Clear();
		var size = DisplaySettings.FromJson( "{ \"Width\": 100, \"Height\": 50000, \"UiScale\": 40, \"Upscale\": 7 }", diagnostics );
		Assert.AreEqual( (1280, 720, 0, UpscaleMode.Native), (size.Width, size.Height, size.UiScale, size.Upscale) );
		Assert.AreEqual( 3, diagnostics.Count, string.Join( "\n", diagnostics ) );

		diagnostics.Clear();
		Assert.AreEqual( DisplaySettings.Default, DisplaySettings.FromJson( "{ \"Upscale\": \"FSR\" }", diagnostics ), "Unknown method names are not silently mapped to another algorithm." );
		Assert.AreEqual( 1, diagnostics.Count );
	}

	[TestMethod]
	public void SettingsFileRoundTripsInTheConfigDirectory()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-display-{Guid.NewGuid():N}" );
		var previous = Environment.GetEnvironmentVariable( "OPENTPW_CONFIG_DIR" );
		try
		{
			Environment.SetEnvironmentVariable( "OPENTPW_CONFIG_DIR", directory );
			var path = DisplaySettings.GetDefaultPath();
			Assert.AreEqual( Path.Combine( directory, "display.json" ), path );
			var diagnostics = new List<string>();
			Assert.AreEqual( DisplaySettings.Default, DisplaySettings.Load( path, diagnostics ), "A missing file is the default, not an error." );
			var settings = DisplaySettings.Default with { Width = 2560, Height = 1440, Upscale = UpscaleMode.Linear, RenderScale = 77 };
			settings.Save( path );
			Assert.AreEqual( settings, DisplaySettings.Load( path, diagnostics ) );
			Assert.AreEqual( 0, diagnostics.Count );
			Assert.IsFalse( File.Exists( path + ".tmp" ) );
		}
		finally
		{
			Environment.SetEnvironmentVariable( "OPENTPW_CONFIG_DIR", previous );
			if ( Directory.Exists( directory ) )
				Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public void CommandLineSelectsResolutionModeAndScaling()
	{
		var diagnostics = new List<string>();
		var settings = DisplaySettings.Default.ApplyCommandLine( new[] { "--resolution", "3440x1440", "--fullscreen", "--render-scale", "59" }, diagnostics );
		Assert.AreEqual( (3440, 1440, WindowMode.Borderless, UpscaleMode.Linear, 59), (settings.Width, settings.Height, settings.Mode, settings.Upscale, settings.RenderScale ), "A scale without a method selects Linear." );

		settings = DisplaySettings.Default.ApplyCommandLine( new[] { "--upscale", "nearest" }, diagnostics );
		Assert.AreEqual( (UpscaleMode.Nearest, RenderScaling.DefaultPreset), (settings.Upscale, settings.RenderScale) );

		settings = settings.ApplyCommandLine( new[] { "--upscale", "NATIVE", "--render-scale", "50%" }, diagnostics );
		Assert.AreEqual( (UpscaleMode.Native, 100), (settings.Upscale, settings.RenderScale), "Native always renders at 100%." );

		settings = DisplaySettings.Default with { Mode = WindowMode.Borderless };
		Assert.AreEqual( WindowMode.Windowed, settings.ApplyCommandLine( new[] { "--windowed" }, diagnostics ).Mode );
		Assert.AreEqual( WindowMode.Exclusive, settings.ApplyCommandLine( new[] { "--fullscreen-exclusive", "--resolution", "1024x768" }, diagnostics ).Mode );
		Assert.AreEqual( 3, DisplaySettings.Default.ApplyCommandLine( new[] { "--ui-scale", "3" }, diagnostics ).UiScale );
		Assert.AreEqual( 0, (DisplaySettings.Default with { UiScale = 2 }).ApplyCommandLine( new[] { "--ui-scale", "auto" }, diagnostics ).UiScale );
		Assert.AreEqual( 0, diagnostics.Count, string.Join( "\n", diagnostics ) );

		var fallback = DisplaySettings.Default.ApplyCommandLine( new[] { "--render-scale", "30", "--resolution", "200x100" }, diagnostics );
		Assert.AreEqual( (UpscaleMode.Native, 100, 1280, 720), (fallback.Upscale, fallback.RenderScale, fallback.Width, fallback.Height) );
		Assert.AreEqual( 2, diagnostics.Count, string.Join( "\n", diagnostics ) );
	}

	[TestMethod]
	public void MalformedCommandLineValuesAreErrors()
	{
		var diagnostics = new List<string>();
		foreach ( var args in new[]
		{
			new[] { "--resolution", "big" }, new[] { "--resolution", "1920x" }, new[] { "--resolution" }, new[] { "--resolution", "--fullscreen" },
			new[] { "--upscale", "fsr" }, new[] { "--upscale", "1" }, new[] { "--render-scale", "half" }, new[] { "--render-scale", "-50" },
			new[] { "--ui-scale", "big" }, new[] { "--windowed", "--fullscreen" }
		} )
			Assert.ThrowsException<ArgumentException>( () => DisplaySettings.Default.ApplyCommandLine( args, diagnostics ), string.Join( " ", args ) );
		Assert.IsTrue( DisplaySettings.TryParseSize( "2560×1440", out var width, out var height ) && width == 2560 && height == 1440 );
	}

	[TestMethod]
	public void ResolutionListMergesDisplayModesWithOriginalSizes()
	{
		var modes = new[] { Size( 1920, 1080 ), Size( 1280, 720 ), Size( 1920, 1080 ), Size( 640, 480 ), Size( 300, 200 ) };
		var list = DisplayModes.Build( modes, Size( 1920, 1080 ) );
		Assert.AreEqual( list.Count, list.Distinct().Count() );
		Assert.IsFalse( list.Any( size => size.X > 1920 || size.Y > 1080 ), "Original/common sizes larger than the desktop are not offered." );
		Assert.IsFalse( list.Any( size => size.X < DisplaySettings.MinimumWindowWidth ) );
		foreach ( var size in new[] { Size( 400, 300 ), Size( 800, 600 ), Size( 1024, 768 ), Size( 1920, 1080 ), Size( 1366, 768 ) } )
			Assert.IsTrue( list.Contains( size ), $"{size.X}x{size.Y}" );
		Assert.IsTrue( list.Contains( Size( 1280, 1024 ) ) );
		Assert.IsFalse( list.Contains( Size( 1600, 1200 ) ), "1200 rows do not fit a 1080-row desktop." );
		CollectionAssert.AreEqual( list.OrderBy( size => size.X * size.Y ).ToArray(), list.ToArray(), "Sorted by area." );
		Assert.IsTrue( DisplayModes.Build( Array.Empty<Point2>(), Size( 0, 0 ) ).Contains( Size( 3840, 2160 ) ), "Unknown desktop: offer every size." );
	}

	[TestMethod]
	public void ExclusiveFullscreenOnlyOffersDisplayModes()
	{
		var modes = new[] { Size( 1920, 1080 ), Size( 1280, 720 ) };
		var current = new[] { Size( 1500, 900 ), Size( 3000, 1800 ) };
		var exclusive = DisplayModes.Build( modes, Size( 1920, 1080 ), current, WindowMode.Exclusive );
		CollectionAssert.AreEqual( new[] { Size( 1280, 720 ), Size( 1920, 1080 ) }, exclusive.ToArray() );
		var windowed = DisplayModes.Build( modes, Size( 1920, 1080 ), current, WindowMode.Windowed );
		Assert.IsTrue( windowed.Contains( Size( 1500, 900 ) ) && windowed.Contains( Size( 3000, 1800 ) ), "Current window and drawable sizes are always listed." );
		Assert.IsTrue( windowed.Contains( Size( 512, 384 ) ) && windowed.Contains( Size( 1600, 900 ) ) );
	}

	[TestMethod]
	public void UnconfirmedDisplayChangeRevertsAfterTimeout()
	{
		var confirmation = new DisplayChangeConfirmation();
		var original = DisplaySettings.Default;
		var second = TimeSpan.FromSeconds( 1 );
		Assert.IsNull( confirmation.Poll( TimeSpan.Zero ) );
		confirmation.Begin( original, TimeSpan.Zero, 15 * second );
		Assert.IsTrue( confirmation.IsPending );
		Assert.AreEqual( 15, confirmation.SecondsRemaining( TimeSpan.Zero ) );
		Assert.AreEqual( 1, confirmation.SecondsRemaining( 14.2 * second ) );
		Assert.IsNull( confirmation.Poll( 14 * second ) );
		// A second unconfirmed change keeps the original restore point and restarts the timer.
		confirmation.Begin( original with { Width = 800, Height = 600 }, 14 * second, 15 * second );
		Assert.IsNull( confirmation.Poll( 20 * second ) );
		Assert.AreEqual( original, confirmation.Poll( 29 * second ) );
		Assert.IsFalse( confirmation.IsPending );
		Assert.AreEqual( 0, confirmation.SecondsRemaining( 29 * second ) );

		confirmation.Begin( original, TimeSpan.Zero, second );
		Assert.AreEqual( original, confirmation.Take(), "Revert returns the restore point." );
		Assert.IsNull( confirmation.Take(), "Confirm/revert twice is harmless." );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => confirmation.Begin( original, TimeSpan.Zero, TimeSpan.Zero ) );
	}
}
