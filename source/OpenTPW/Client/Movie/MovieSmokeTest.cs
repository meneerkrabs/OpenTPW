using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// Native movie check: plays the first <see cref="FirstCheckFrame"/>..<see cref="SecondCheckFrame"/> frames on the
/// GPU, reads the resolved framebuffer back twice and requires the presented picture to match the CPU-converted
/// frame inside the aspect-correct rectangle, black bars outside it, and a changed picture between the reads.
/// </summary>
internal sealed class MovieSmokeTest
{
	public const int FirstCheckFrame = 15;
	public const int SecondCheckFrame = 60;
	public const double MaximumMeanChannelError = 6;

	private readonly MovieScreen screen;
	private readonly string name;
	private byte[]? firstCapture;
	private bool completed;

	public MovieSmokeTest( MovieScreen screen, string name )
	{
		this.screen = screen;
		this.name = name;
	}

	public void Update()
	{
		if ( completed )
			return;
		var playback = screen.Playback;
		Require( !playback.IsFinished || playback.CurrentFrameIndex >= SecondCheckFrame, "movie plays past the checked frames" );
		if ( firstCapture == null && playback.CurrentFrameIndex >= FirstCheckFrame )
			firstCapture = CheckFrame( "first" );
		else if ( firstCapture != null && playback.CurrentFrameIndex >= SecondCheckFrame )
		{
			var second = CheckFrame( "second" );
			Require( !second.AsSpan().SequenceEqual( firstCapture ), "presented picture changes as the movie advances" );
			completed = true;
			Log.Trace( $"Native movie smoke test passed: {name}, frames shown {playback.FramesDecoded}, dropped {playback.FramesDropped}, clock {playback.Clock:F2} s ({playback.ClockSource}), GPU readback matches CPU conversion." );
			global::Global.Render.Window.SdlWindow.Close();
		}
	}

	public void VerifyCompleted() => Require( completed, "complete the movie readback checks" );

	private byte[] CheckFrame( string label )
	{
		Device.WaitForIdle();
		var source = global::Global.Render.ResolveColorTexture;
		var width = (int)source.Width;
		var height = (int)source.Height;
		var pixels = ReadBack( source );
		var presenter = screen.Presenter;
		var (left, top, fitWidth, fitHeight) = MoviePresenter.Fit( presenter.Width, presenter.Height, width, height );
		Require( fitWidth > 0 && fitHeight > 0, "movie rectangle fits the framebuffer" );

		// Mean colour inside the movie rectangle (BGRA readback) against the CPU RGBA frame; resampling keeps means.
		var gpu = new double[3];
		var cpu = new double[3];
		long count = 0;
		for ( var row = top + 2; row < top + fitHeight - 2; row++ )
			for ( var column = left + 2; column < left + fitWidth - 2; column++, count++ )
				for ( var channel = 0; channel < 3; channel++ )
					gpu[channel] += pixels[(row * width + column) * 4 + 2 - channel];
		var rgba = presenter.Pixels;
		for ( var index = 0; index < presenter.Width * presenter.Height; index++ )
			for ( var channel = 0; channel < 3; channel++ )
				cpu[channel] += rgba[index * 4 + channel];
		for ( var channel = 0; channel < 3; channel++ )
		{
			var error = Math.Abs( gpu[channel] / count - cpu[channel] / (presenter.Width * presenter.Height) );
			Require( error <= MaximumMeanChannelError, $"{label} frame channel {channel} mean matches the CPU conversion (error {error:F2})" );
		}
		Require( cpu.Sum() > 0, $"{label} movie frame is not black" );

		// Everything outside the rectangle (letterbox/pillarbox) stays black.
		for ( var row = 0; row < height; row++ )
			for ( var column = 0; column < width; column++ )
			{
				var inside = column >= left - 1 && column <= left + fitWidth && row >= top - 1 && row <= top + fitHeight;
				if ( inside )
					continue;
				var offset = (row * width + column) * 4;
				Require( pixels[offset] == 0 && pixels[offset + 1] == 0 && pixels[offset + 2] == 0, $"{label} frame bars are black at {column},{row}" );
			}

		using var image = Image.LoadPixelData<Bgra32>( pixels, width, height );
		var artifactDirectory = Path.GetFullPath( "artifacts" );
		Directory.CreateDirectory( artifactDirectory );
		image.SaveAsPng( Path.Combine( artifactDirectory, $"native-movie-{name}-{label}.png" ) );
		return pixels;
	}

	private static byte[] ReadBack( Veldrid.Texture source )
	{
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
			return pixels;
		}
		finally
		{
			Device.Unmap( staging );
		}
	}

	private static void Require( bool condition, string step )
	{
		if ( !condition )
			throw new InvalidOperationException( $"Native movie smoke test failed: {step}." );
	}
}
