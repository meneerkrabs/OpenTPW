using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// <c>--capture-world &lt;file.png&gt;</c>: after <c>--capture-frames</c> frames (default 240) writes the
/// 3D world alone (no interface, no cursor) at its render size and closes the window. For
/// screenshots and before/after comparisons, e.g. of enhanced textures (docs/TEXTURE-PACKS.md).
/// </summary>
// [EXT:world-capture] developer/screenshot command, not original behaviour
public sealed class WorldCapture( string path, int frames )
{
	private int frame;

	public bool Written { get; private set; }

	public void Update()
	{
		if ( Written || ++frame < frames )
			return;
		var source = global::Global.Render.ResolveColorTexture;
		using var staging = Device.ResourceFactory.CreateTexture( TextureDescription.Texture2D( source.Width, source.Height, 1, 1, source.Format, TextureUsage.Staging ) );
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
			if ( source.Format is PixelFormat.R8_G8_B8_A8_UNorm )
				for ( var pixel = 0; pixel < pixels.Length; pixel += 4 )
					(pixels[pixel], pixels[pixel + 2]) = (pixels[pixel + 2], pixels[pixel]);
			for ( var pixel = 3; pixel < pixels.Length; pixel += 4 )
				pixels[pixel] = 255;
			using var image = Image.LoadPixelData<Bgra32>( pixels, (int)source.Width, (int)source.Height );
			var full = Path.GetFullPath( path );
			Directory.CreateDirectory( Path.GetDirectoryName( full )! );
			image.SaveAsPng( full );
			Log.Trace( $"World capture: {source.Width}x{source.Height} written to {full}." );
		}
		finally
		{
			Device.Unmap( staging );
		}
		Written = true;
		global::Global.Render.Window.SdlWindow.Close();
	}
}
