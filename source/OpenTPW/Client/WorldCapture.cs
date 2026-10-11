using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// <c>--capture-world &lt;file.png&gt;</c>: after <c>--capture-frames</c> frames (default 240) writes the
/// 3D world alone (no interface, no cursor) at its render size and closes the window. For
/// screenshots and before/after comparisons, e.g. of enhanced textures (docs/TEXTURE-PACKS.md).
/// <c>--capture-interface</c> writes the composed output instead (world plus BF4 interface, no ImGui);
/// <c>--capture-camera x,y,height,yaw</c> holds the park camera at that pose for the last frames before the capture;
/// <c>--capture-sequence count,interval</c> writes <c>count</c> numbered images, one every <c>interval</c> frames.
/// </summary>
// [EXT:world-capture] developer/screenshot command, not original behaviour
public sealed class WorldCapture
{
	private readonly string path;
	private readonly int frames;
	private readonly bool includeInterface;
	private readonly CameraPose? pose;
	private readonly int count;
	private readonly int interval;
	private int frame;
	private int written;
	private const int PoseFrames = 30;

	public readonly record struct CameraPose( float X, float Y, float Height, float Yaw );

	public WorldCapture( string path, int frames, bool includeInterface = false, CameraPose? pose = null, int count = 1, int interval = 1 )
	{
		this.path = path;
		this.frames = frames;
		this.count = Math.Max( 1, count );
		this.interval = Math.Max( 1, interval );
		this.includeInterface = includeInterface;
		this.pose = pose;
		if ( includeInterface )
			global::Global.Render.CaptureOutput = true;
	}

	public bool Written { get; private set; }

	/// <summary>File name of sequence image <paramref name="index"/>: <c>shot.png</c> becomes <c>shot-0003.png</c>.</summary>
	public static string SequencePath( string path, int index ) =>
		Path.Combine( Path.GetDirectoryName( path ) ?? "", $"{Path.GetFileNameWithoutExtension( path )}-{index:D4}{Path.GetExtension( path )}" );

	/// <summary>Parses <c>x,y,height,yaw</c> (invariant culture); null when malformed.</summary>
	public static CameraPose? ParsePose( string? text )
	{
		var parts = text?.Split( ',' );
		if ( parts is not { Length: 4 } )
			return null;
		var values = new float[4];
		for ( var i = 0; i < 4; i++ )
			if ( !float.TryParse( parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out values[i] ) || !float.IsFinite( values[i] ) )
				return null;
		return new CameraPose( values[0], values[1], values[2], values[3] );
	}

	public void Update()
	{
		// The pose is held only for the last frames, so whatever runs before the capture keeps its own camera.
		if ( pose is { } held && frame >= frames - PoseFrames && Camera.Mode is ParkCameraMode camera )
			camera.SetPose( new System.Numerics.Vector3( held.X, held.Y, 0 ), held.Height, held.Yaw );
		if ( Written || ++frame < frames || (frame - frames) % interval != 0 )
			return;
		var source = includeInterface && global::Global.Render.OutputCaptureTexture is { } output ? output : global::Global.Render.ResolveColorTexture;
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
			var full = Path.GetFullPath( count == 1 ? path : SequencePath( path, written ) );
			Directory.CreateDirectory( Path.GetDirectoryName( full )! );
			image.SaveAsPng( full );
			var cameraText = Camera.Mode is ParkCameraMode park ? FormattableString.Invariant( $" Camera {park.Target.X:0.#},{park.Target.Y:0.#},{park.Height:0.#},{park.Yaw:0.#}." ) : "";
			Log.Trace( $"World capture: {source.Width}x{source.Height} written to {full}.{cameraText}" );
		}
		finally
		{
			Device.Unmap( staging );
		}
		if ( ++written < count )
			return;
		Written = true;
		global::Global.Render.Window.SdlWindow.Close();
	}
}
