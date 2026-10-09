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
	private int frame;
	private bool observedMotion;
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
		if ( frame <= 90 )
			observedMotion |= level.PlacedRide!.MotionHeight > 0;
		if ( frame == 90 )
		{
			Require( observedMotion, "running ride moves" );
			CaptureFrame( "park.png" );
			level.SaveSandbox();
			level.PlacedRide!.Stop();
			Require( !level.PlacedRide.IsRunning && level.PlacedRide.MotionHeight == 0, "stop resets motion" );
			level.RemoveRide();
			Require( level.PlacedRide == null, "remove ride" );
			level.LoadSandbox();
			Require( level.PlacedRide != null && level.PlacedRide.IsRunning, "restore running ride" );
		}
		if ( frame == 120 )
		{
			level.RemoveRide();
			level.SaveSandbox();
			level.LoadSandbox();
			Require( level.PlacedRide == null, "restore empty park" );
		}
		if ( frame == 125 )
		{
			CaptureFrame( "terrain.png" );
			Require( level.PlaceRide( new Vector3( 10, 10, 0 ) ), "place ride again" );
		}
		if ( frame == 150 )
		{
			Device.WaitForIdle();
			completed = true;
			Log.Trace( $"Native sandbox smoke test passed: {frame} frames, original ride, motion, stop, remove, isolated save/load and GPU readback." );
			Render.Window.SdlWindow.Close();
		}
	}

	private static void CaptureFrame( string name )
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
		}
		finally
		{
			Device.Unmap( staging );
		}
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
