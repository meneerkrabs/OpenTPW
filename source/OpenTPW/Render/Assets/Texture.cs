using StbImageSharp;
using System.Runtime.InteropServices;
using Veldrid;

namespace OpenTPW;

public partial class Texture : Asset
{
	public SamplerType SamplerType { get; private set; } = SamplerType.AnisotropicRepeat;

	public uint Width { get; private set; }
	public uint Height { get; private set; }

	private static readonly Lazy<Texture> missing = new( () => new Texture( [255, 255, 255, 255], 1, 1 ) );

	/// <summary>
	/// The white placeholder for a missing image: one texture shared by every user and never released (it used to be a new texture
	/// on every call, each kept for the rest of the session).
	/// </summary>
	public static Texture Missing => missing.Value;

	private static readonly Lazy<Texture> signBoard = new( () => new Texture( [38, 28, 18, 255], 1, 1 ) );

	/// <summary>
	/// The plain brown board a sign slot shows when its runtime sign cannot be drawn (<see cref="SignSlotPlan"/>): one texture shared by every
	/// part, park and lobby and, like <see cref="Missing"/>, never released.
	/// </summary>
	internal static Texture SignBoard => signBoard.Value;

	/// <summary>
	/// Releases a texture its owner made from bytes and no one else uses: it leaves <see cref="Asset.All"/> and its GPU objects go
	/// after the last frame that may draw them. Never for a texture loaded by path (those are shared through the cache) or for
	/// <see cref="Missing"/> and <see cref="SignBoard"/>.
	/// </summary>
	internal void ReleaseOwned()
	{
		if ( ReferenceEquals( this, missing.IsValueCreated ? missing.Value : null ) || ReferenceEquals( this, signBoard.IsValueCreated ? signBoard.Value : null ) || !string.IsNullOrEmpty( Path ) )
			return;
		Asset.All.Remove( this );
		var (texture, view) = (NativeTexture, NativeTextureView);
		void Dispose()
		{
			view?.Dispose();
			texture?.Dispose();
		}
		if ( global::Global.Render is { } renderer )
			renderer.ScheduleDelete( Dispose );
		else
			Dispose();
	}

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
			UpdateFromImage( path, flags );
	}

	/// <summary>
	/// From data as bytes
	/// </summary>
	public Texture( byte[] data, int width, int height, TextureFlags flags = TextureFlags.None,
		[System.Runtime.CompilerServices.CallerFilePath] string callerFile = "", [System.Runtime.CompilerServices.CallerLineNumber] int callerLine = 0 )
	{
		// the browser's AOT build has no method names in its stack traces, so the calling line comes first
		if ( TrackOrigins )
			Origin = $"{System.IO.Path.GetFileName( callerFile.Replace( '\\', '/' ) )}:{callerLine} " + string.Join( " < ", new System.Diagnostics.StackTrace( 1, false ).GetFrames().Take( 3 ).Select( frame => $"{frame.GetMethod()?.DeclaringType?.Name}.{frame.GetMethod()?.Name}" ) );
		CreateTexture( "", data, (uint)width, (uint)height, flags );
	}

	/// <summary>Records where textures made from bytes come from (<see cref="Origin"/>), for the memory diagnostics of the browser harness.</summary>
	internal static bool TrackOrigins { get; set; }

	/// <summary>The methods that made this texture from bytes, when <see cref="TrackOrigins"/> was on.</summary>
	internal string? Origin { get; private set; }

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
	/// Update from a loose file, or from a game-archive member: an EA `.fsh` or an image format supported by the STB
	/// library (jpg, png, gif, tga, etc.). A texture already made from this path is shared.
	/// </summary>
	private void UpdateFromImage( string path, TextureFlags flags )
	{
		if ( TryGetCachedTexture( path, out var cached ) )
		{
			NativeTexture = cached!.NativeTexture;
			NativeTextureView = cached.NativeTextureView;
			Width = cached.Width;
			Height = cached.Height;
			// a texture that shares the cached one's GPU objects is a path texture too: ReleaseOwned must leave it alone, or it disposes what the first owner still draws
			Path = path;
			return;
		}

		var (data, width, height) = DecodeImage( path );
		CreateTexture( path, data, (uint)width, (uint)height, flags );
	}

	/// <summary>
	/// Decodes a texture that is not a `.wct`: an EA `.fsh` or an stb image (png, tga, ...), from disk when the path
	/// exists there, else from the game data (Theme Park Inc keeps `.tga` textures inside its WADs). No GPU access, so
	/// workers may call it.
	/// </summary>
	// [EXT:tpi-viewer] `.fsh` textures exist only in Theme Park Inc data. Opaque 0x24 palettes: the original converter does the same (docs/FSH.md).
	internal static (byte[] Data, int Width, int Height) DecodeImage( string path )
	{
		using var profile = LoadProfile.Measure( "image-decode" );
		if ( path.HasExtension( ".fsh" ) )
		{
			var fsh = new FshFile( path ).Images[0];
			return (fsh.Rgba, fsh.Width, fsh.Height);
		}
		var fileData = File.Exists( path ) ? File.ReadAllBytes( path ) : FileSystem.ReadAllBytes( path );
		var image = ImageResult.FromMemory( fileData, ColorComponents.RedGreenBlueAlpha );
		return (image.Data, image.Width, image.Height);
	}

	/// <summary>
	/// Decodes a texture of another file system than the game data (an official bonus archive, or the install a mod reads objects from), by its extension:
	/// a Bullfrog `.wct`, an EA `.fsh` (Theme Park Inc) or an stb image such as the `.tga` files inside Theme Park Inc's WADs. No GPU access, no texture pack.
	/// </summary>
	// [EXT:cross-edition-objects] Theme Park Inc textures reach a Theme Park World park as `.fsh` and `.tga`, not only `.wct`
	internal static (byte[] Data, int Width, int Height) DecodeImage( BaseFileSystem fileSystem, string path )
	{
		using var stream = fileSystem.OpenRead( path );
		if ( path.HasExtension( ".wct" ) )
		{
			var wct = new TextureFile( stream ).Data;
			return (wct.Data, wct.Width, wct.Height);
		}
		if ( path.HasExtension( ".fsh" ) )
		{
			var fsh = new FshFile( stream ).Images[0];
			return (fsh.Rgba, fsh.Width, fsh.Height);
		}
		using var memory = new MemoryStream();
		stream.CopyTo( memory );
		var image = ImageResult.FromMemory( memory.ToArray(), ColorComponents.RedGreenBlueAlpha );
		return (image.Data, image.Width, image.Height);
	}

	/// <summary>A repeating texture made from <paramref name="path"/> of <paramref name="fileSystem"/> (see <see cref="DecodeImage(BaseFileSystem, string)"/>).</summary>
	internal static Texture FromFileSystem( BaseFileSystem fileSystem, string path, TextureFlags flags )
	{
		var (data, width, height) = DecodeImage( fileSystem, path );
		return new Texture( data, width, height, flags );
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
			// a texture that shares the cached one's GPU objects is a path texture too: ReleaseOwned must leave it alone, or it disposes what the first owner still draws
			Path = path;
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
		using var profile = LoadProfile.Measure( "texture-decode" );
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

	/// <summary>
	/// Gives this texture new pixels (possibly another size): a new GPU texture replaces the native identity, which materials
	/// follow, and the old one is deleted after the current frame. Render thread only.
	/// </summary>
	internal void Replace( byte[] data, int width, int height )
	{
		var (texture, view) = CreateNative( data, (uint)width, (uint)height );
		var oldTexture = NativeTexture;
		var oldView = NativeTextureView;
		NativeTexture = texture;
		NativeTextureView = view;
		Width = (uint)width;
		Height = (uint)height;
		Render.ScheduleDelete( () =>
		{
			oldView?.Dispose();
			oldTexture?.Dispose();
		} );
	}

	private static int CalculateMipLevels( int width, int height, int depth )
	{
		int maxDimension = Math.Max( width, Math.Max( height, depth ) );
		int mipLevels = (int)Math.Floor( Math.Log( maxDimension, 2 ) ) + 1;

		return mipLevels;
	}

	internal static void PreprocessTextureData( ref byte[] data, TextureFlags flags )
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

	/// <summary>CPU selection used by uncached texture creation; explicit addressing keeps its existing precedence.</summary>
	internal static SamplerType SamplerForFlags( TextureFlags flags )
	{
		var sampler = SamplerType.AnisotropicRepeat;
		if ( flags.HasFlag( TextureFlags.PointFilter ) )
			sampler = SamplerType.Point;
		if ( flags.HasFlag( TextureFlags.Wrap ) )
			sampler = SamplerType.AnisotropicWrap;
		if ( flags.HasFlag( TextureFlags.Repeat ) )
			sampler = SamplerType.AnisotropicRepeat;
		return sampler;
	}

	private void CreateTexture( string debugName, byte[] data, uint width, uint height, TextureFlags flags )
	{
		using var profile = LoadProfile.Measure( "texture-create" );
		if ( TryGetCachedTexture( debugName, out var cachedTexture ) )
		{
			NativeTexture = cachedTexture!.NativeTexture;
			NativeTextureView = cachedTexture!.NativeTextureView;
			Width = cachedTexture.Width;
			Height = cachedTexture.Height;
			// shares the cached texture's GPU objects, so ReleaseOwned must leave it alone
			Path = debugName;

			return;
		}

		PreprocessTextureData( ref data, flags );

		SamplerType = SamplerForFlags( flags );

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
