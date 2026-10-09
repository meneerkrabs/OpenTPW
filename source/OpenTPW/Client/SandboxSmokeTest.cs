using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;

namespace OpenTPW;

internal sealed class SandboxSmokeTest : IDisposable
{
	private readonly Level level;
	private readonly BaseFileSystem originalSaveFileSystem;
	private readonly string temporaryDirectory = Path.Combine( Path.GetTempPath(), $"opentpw-smoke-{Guid.NewGuid():N}" );
	private const double MaximumSecondsUntilMotion = 30;
	private readonly System.Diagnostics.Stopwatch sinceOpened = System.Diagnostics.Stopwatch.StartNew();
	private int frame;
	private int motionFrame;
	private bool completed;

	public SandboxSmokeTest( Level level )
	{
		this.level = level;
		originalSaveFileSystem = SaveFileSystem;
		Directory.CreateDirectory( temporaryDirectory );
		SaveFileSystem = new BaseFileSystem( temporaryDirectory );
		Require( level.PlaceRide( Vector3.Zero ), "place original Totem" );
		level.PlacedRide!.Start();
	}

	public void Update()
	{
		++frame;
		if ( motionFrame == 0 )
		{
			// Totem.RSE waits up to 10 s for passengers before it triggers its main animation.
			if ( level.PlacedRide!.MotionHeight > 0 )
				motionFrame = frame;
			else
				Require( sinceOpened.Elapsed.TotalSeconds < MaximumSecondsUntilMotion, "original Totem script starts the ride motion" );
			return;
		}
		var step = frame - motionFrame;
		if ( step == 30 )
		{
			Require( level.PlacedRide!.Script.State != RideVMState.Faulted && level.PlacedRide.Script[RideVariables.VAR_RUNNING] == 1, "original script reports the ride running" );
			VerifyText( CaptureFrame( "park.png" ) );
			level.SaveSandbox();
			level.PlacedRide!.Stop();
			Require( !level.PlacedRide.IsOpen, "close ride" );
			level.RemoveRide();
			Require( level.PlacedRide == null, "remove ride" );
			level.LoadSandbox();
			Require( level.PlacedRide != null && level.PlacedRide.IsOpen, "restore open ride" );
		}
		if ( step == 60 )
		{
			level.RemoveRide();
			level.SaveSandbox();
			level.LoadSandbox();
			Require( level.PlacedRide == null, "restore empty park" );
		}
		if ( step == 65 )
		{
			CaptureFrame( "terrain.png" );
			Require( level.PlaceRide( new Vector3( 10, 10, 0 ) ), "place ride again" );
		}
		if ( step == 90 )
		{
			Device.WaitForIdle();
			completed = true;
			Log.Trace( $"Native sandbox smoke test passed: {frame} frames, original ride and RSE script, script-triggered motion, close, remove, isolated save/load, BF4 text and GPU readback." );
			Render.Window.SdlWindow.Close();
		}
	}

	private static (byte[] Pixels, int Width, int Height) CaptureFrame( string name )
	{
		var source = Render.ResolveColorTexture;
		using var staging = Device.ResourceFactory.CreateTexture( TextureDescription.Texture2D(
			source.Width, source.Height, 1, 1, source.Format, TextureUsage.Staging ) );
		using var commands = Device.ResourceFactory.CreateCommandList();
		commands.Begin();
		commands.CopyTexture( source, staging );
		commands.End();
		Device.SubmitCommands( commands );
		Device.WaitForIdle();
		var mapped = Device.Map( staging, MapMode.Read );
		try
		{
			var rowBytes = checked((int)source.Width * 4);
			var pixels = new byte[checked(rowBytes * (int)source.Height)];
			for ( var row = 0; row < source.Height; ++row )
				Marshal.Copy( IntPtr.Add( mapped.Data, checked((int)(row * mapped.RowPitch)) ), pixels, row * rowBytes, rowBytes );
			Require( pixels.Where( ( value, index ) => index % 4 != 3 ).Any( value => value != 0 ), "terrain framebuffer is not black" );
			var colors = new HashSet<uint>();
			for ( var pixel = 0; pixel < pixels.Length && colors.Count <= 32; pixel += 4 )
				colors.Add( BitConverter.ToUInt32( pixels, pixel ) );
			Require( colors.Count > 32, "terrain framebuffer retains texture detail" );
			using var image = Image.LoadPixelData<Bgra32>( pixels, (int)source.Width, (int)source.Height );
			var artifactDirectory = Path.GetFullPath( "artifacts" );
			Directory.CreateDirectory( artifactDirectory );
			image.SaveAsPng( Path.Combine( artifactDirectory, $"native-smoke-{name}" ) );
			return (pixels, (int)source.Width, (int)source.Height);
		}
		finally
		{
			Device.Unmap( staging );
		}
	}

	/// <summary>
	/// Compares the BF4 text panel in GPU readback with the CPU composite of the same quads.
	/// </summary>
	private void VerifyText( (byte[] Pixels, int Width, int Height) frame )
	{
		var overlay = level.TextOverlay;
		var (x, y, width, height) = overlay.Bounds;
		Require( width > 0 && x + width <= frame.Width && y + height <= frame.Height, "text panel fits the framebuffer" );
		var expected = (byte[])frame.Pixels.Clone();
		overlay.Batch.Composite( expected, frame.Width, frame.Height );
		var background = expected.AsSpan( (y * frame.Width + x) * 4, 3 ).ToArray();
		var textPixels = 0;
		var maximumDifference = 0;
		for ( var row = y; row < y + height; ++row )
		{
			for ( var column = x; column < x + width; ++column )
			{
				var pixel = (row * frame.Width + column) * 4;
				for ( var channel = 0; channel < 3; ++channel )
					maximumDifference = Math.Max( maximumDifference, Math.Abs( frame.Pixels[pixel + channel] - expected[pixel + channel] ) );
				if ( !frame.Pixels.AsSpan( pixel, 3 ).SequenceEqual( background ) )
					++textPixels;
			}
		}
		var inkPixels = overlay.Batch.Quads.Where( quad => quad.Atlas != null ).Sum( quad =>
			Enumerable.Range( 0, quad.Height ).Sum( row => quad.Atlas!.Alpha.AsSpan( (quad.AtlasY + row) * quad.Atlas.Width + quad.AtlasX, quad.Width ).ToArray().Count( value => value != 0 ) ) );
		Log.Trace( $"BF4 text readback: {textPixels} text pixels in {width}x{height} panel; {inkPixels} atlas ink texels; max channel difference {maximumDifference}." );
		Require( textPixels >= inkPixels * 9 / 10 && inkPixels > 200, "BF4 text pixels are present in GPU readback" );
		Require( maximumDifference <= 2, "BF4 text readback matches the CPU composite" );
		var crop = new byte[width * height * 4];
		for ( var row = 0; row < height; ++row )
			Array.Copy( frame.Pixels, ((y + row) * frame.Width + x) * 4, crop, row * width * 4, width * 4 );
		using var image = Image.LoadPixelData<Bgra32>( crop, width, height );
		image.SaveAsPng( Path.Combine( Path.GetFullPath( "artifacts" ), "native-smoke-text.png" ) );
	}

	public void VerifyCompleted() => Require( completed, "complete all native smoke-test frames" );

	private static void Require( bool condition, string step )
	{
		if ( !condition )
			throw new InvalidOperationException( $"Native sandbox smoke test failed: {step}." );
	}

	public void Dispose()
	{
		Render.PostUpdate -= Update;
		SaveFileSystem = originalSaveFileSystem;
		Directory.Delete( temporaryDirectory, true );
	}
}
