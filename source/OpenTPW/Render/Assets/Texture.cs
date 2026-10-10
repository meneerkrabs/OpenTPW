using StbImageSharp;
using System.Runtime.InteropServices;
using Veldrid;

namespace OpenTPW;

public partial class Texture : Asset
{
	public SamplerType SamplerType { get; private set; } = SamplerType.AnisotropicRepeat;

	public uint Width { get; private set; }
	public uint Height { get; private set; }

	public static Texture Missing => new Texture( [255, 255, 255, 255], 1, 1 );

	internal Veldrid.Texture NativeTexture;
	internal TextureView NativeTextureView;

	// [EXT:texture-pack] Textures made from a game `.wct` remember their source so a pack switch can reload them in place.
	private string? wctPath;
	private TextureFlags wctFlags;
	private static readonly List<WeakReference<Texture>> wctTextures = new();

	/// <summary>
	/// From path on disk, or from a game resource
	/// </summary>
	public Texture( string path, TextureFlags flags = TextureFlags.None )
	{
		if ( path.HasExtension( ".wct" ) )
		{
			wctPath = path;
			wctFlags = flags;
			lock ( wctTextures )
				wctTextures.Add( new WeakReference<Texture>( this ) );
			UpdateFromWct( path, flags );
		}
		else
			UpdateFromStb( path, flags );
	}

	/// <summary>
	/// From data as bytes
	/// </summary>
	public Texture( byte[] data, int width, int height, TextureFlags flags = TextureFlags.None )
	{
		CreateTexture( "", data, (uint)width, (uint)height, flags );
	}

	public Texture( Stream stream, TextureFlags flags = TextureFlags.None )
	{
		var fileData = new byte[stream.Length];
		stream.Read( fileData, 0, fileData.Length );

		var image = ImageResult.FromMemory( fileData, ColorComponents.RedGreenBlueAlpha );

		var data = image.Data;
		var width = (uint)image.Width;
		var height = (uint)image.Height;
		var debugName = $"Stream {stream.GetHashCode()}";

		CreateTexture( debugName, data, width, height, flags );
	}

	/// <summary>
	/// Update from an image format supported by the STB library (jpg, png, gif, tga, etc.)
	/// </summary>
	private void UpdateFromStb( string path, TextureFlags flags )
	{
		var fileData = File.ReadAllBytes( path );
		var image = ImageResult.FromMemory( fileData, ColorComponents.RedGreenBlueAlpha );

		var data = image.Data;
		var width = (uint)image.Width;
		var height = (uint)image.Height;

		CreateTexture( path, data, width, height, flags );
	}

	/// <summary>
	/// Update from a Bullfrog WCT file
	/// </summary>
	private void UpdateFromWct( string path, TextureFlags flags )
	{
		if ( TryGetCachedTexture( path, out var cached ) )
		{
			NativeTexture = cached!.NativeTexture;
			NativeTextureView = cached.NativeTextureView;
			Width = cached.Width;
			Height = cached.Height;
			return;
		}

		var (data, width, height) = DecodeWct( path );
		CreateTexture( path, data, (uint)width, (uint)height, flags );
	}

	/// <summary>
	/// Decodes a game texture: the active texture pack's replacement when it has one, else the original `.wct`.
	/// No GPU access, so a pack switch may run it on a worker thread.
	/// </summary>
	// [EXT:texture-pack] pack lookup; a missing file falls back to the original.
	internal static (byte[] Data, int Width, int Height) DecodeWct( string path )
	{
		if ( TexturePack.Find( path ) is { } replacement )
		{
			var image = ImageResult.FromMemory( File.ReadAllBytes( replacement ), ColorComponents.RedGreenBlueAlpha );
			return (image.Data, image.Width, image.Height);
		}
		var textureFileData = new TextureFile( path ).Data;
		return (textureFileData.Data, textureFileData.Width, textureFileData.Height);
	}

	/// <summary>A live texture made from the `.wct` at <paramref name="path"/>, or null (smoke tests).</summary>
	internal static Texture? FindLoaded( string path )
	{
		lock ( wctTextures )
			foreach ( var reference in wctTextures )
				if ( reference.TryGetTarget( out var texture ) && texture.wctPath == path )
					return texture;
		return null;
	}

	/// <summary>Mip level 0 of the GPU texture as RGBA bytes (smoke tests).</summary>
	internal byte[] ReadPixels()
	{
		using var staging = Device.ResourceFactory.CreateTexture( TextureDescription.Texture2D( Width, Height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging ) );
		using var commands = Device.ResourceFactory.CreateCommandList();
		commands.Begin();
		commands.CopyTexture( NativeTexture, 0, 0, 0, 0, 0, staging, 0, 0, 0, 0, 0, Width, Height, 1, 1 );
		commands.End();
		Device.SubmitCommands( commands );
		Device.WaitForIdle();
		var mapped = Device.Map( staging, MapMode.Read );
		try
		{
			var rowBytes = (int)Width * 4;
			var pixels = new byte[rowBytes * (int)Height];
			for ( var row = 0; row < Height; row++ )
				Marshal.Copy( IntPtr.Add( mapped.Data, (int)(row * mapped.RowPitch) ), pixels, row * rowBytes, rowBytes );
			return pixels;
		}
		finally
		{
			Device.Unmap( staging );
		}
	}

	/// <summary>The distinct `.wct` paths of live textures with the flags of their first creator, for a pack switch.</summary>
	internal static List<(string Path, TextureFlags Flags)> ReloadTargets()
	{
		var result = new List<(string, TextureFlags)>();
		var seen = new HashSet<string>( StringComparer.Ordinal );
		lock ( wctTextures )
		{
			wctTextures.RemoveAll( reference => !reference.TryGetTarget( out _ ) );
			foreach ( var reference in wctTextures )
				if ( reference.TryGetTarget( out var texture ) && seen.Add( texture.wctPath! ) )
					result.Add( (texture.wctPath!, texture.wctFlags) );
		}
		return result;
	}

	/// <summary>
	/// Gives every live texture of <paramref name="path"/> new pixels (possibly another size) and schedules the old GPU
	/// texture for deletion after the current frame. Render thread only.
	/// </summary>
	internal static void Reload( string path, TextureFlags flags, byte[] data, int width, int height )
	{
		PreprocessTextureData( ref data, flags );
		var (texture, view) = CreateNative( data, (uint)width, (uint)height );
		var old = new HashSet<Veldrid.Texture>();
		var oldViews = new HashSet<TextureView>();
		lock ( wctTextures )
			foreach ( var reference in wctTextures )
			{
				if ( !reference.TryGetTarget( out var target ) || target.wctPath != path )
					continue;
				if ( target.NativeTexture != null )
					old.Add( target.NativeTexture );
				if ( target.NativeTextureView != null )
					oldViews.Add( target.NativeTextureView );
				target.NativeTexture = texture;
				target.NativeTextureView = view;
				target.Width = (uint)width;
				target.Height = (uint)height;
			}
		Render.ScheduleDelete( () =>
		{
			foreach ( var oldView in oldViews )
				oldView.Dispose();
			foreach ( var oldTexture in old )
				oldTexture.Dispose();
		} );
	}

	private static int CalculateMipLevels( int width, int height, int depth )
	{
		int maxDimension = Math.Max( width, Math.Max( height, depth ) );
		int mipLevels = (int)Math.Floor( Math.Log( maxDimension, 2 ) ) + 1;

		return mipLevels;
	}

	private static void PreprocessTextureData( ref byte[] data, TextureFlags flags )
	{
		if ( flags == TextureFlags.None )
			return;

		if ( flags.HasFlag( TextureFlags.PinkChromaKey ) )
		{
			for ( int i = 0; i < data.Length; i += 4 )
			{
				var r = data[i];
				var g = data[i + 1];
				var b = data[i + 2];

				if ( r == 255 && g == 0 && b == 255 )
				{
					// Set alpha to 0
					data[i + 3] = 0;
				}
			}
		}
	}

	private void CreateTexture( string debugName, byte[] data, uint width, uint height, TextureFlags flags )
	{
		if ( TryGetCachedTexture( debugName, out var cachedTexture ) )
		{
			NativeTexture = cachedTexture!.NativeTexture;
			NativeTextureView = cachedTexture!.NativeTextureView;
			Width = cachedTexture.Width;
			Height = cachedTexture.Height;

			return;
		}

		PreprocessTextureData( ref data, flags );

		if ( flags.HasFlag( TextureFlags.PointFilter ) )
			SamplerType = SamplerType.Point;

		if ( flags.HasFlag( TextureFlags.Wrap ) )
			SamplerType = SamplerType.AnisotropicWrap;

		if ( flags.HasFlag( TextureFlags.Repeat ) )
			SamplerType = SamplerType.AnisotropicRepeat;

		var (texture, textureView) = CreateNative( data, width, height );

		Path = debugName;
		NativeTexture = texture;
		NativeTextureView = textureView;
		Width = width;
		Height = height;

		All.Add( this );
	}

	private static (Veldrid.Texture, TextureView) CreateNative( byte[] data, uint width, uint height )
	{
		uint mipLevels = (uint)CalculateMipLevels( (int)width, (int)height, 1 );

		var textureDescription = TextureDescription.Texture2D(
			width,
			height,
			mipLevels,
			1,
			PixelFormat.R8_G8_B8_A8_UNorm,
			TextureUsage.Sampled | TextureUsage.GenerateMipmaps
		);

		var texture = Device.ResourceFactory.CreateTexture( textureDescription );

		var textureDataPtr = Marshal.AllocHGlobal( data.Length );
		Marshal.Copy( data, 0, textureDataPtr, data.Length );
		Device.UpdateTexture( texture, textureDataPtr, (uint)data.Length, 0, 0, 0, width, height, 1, 0, 0 );
		Marshal.FreeHGlobal( textureDataPtr );

		var textureView = Device.ResourceFactory.CreateTextureView( texture );

		Render.ImmediateSubmit( cmd =>
		{
			cmd.GenerateMipmaps( texture );
		} );

		return (texture, textureView);
	}
}
