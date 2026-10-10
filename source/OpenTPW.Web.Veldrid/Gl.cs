using System.Runtime.InteropServices.JavaScript;

namespace Veldrid;

/// <summary>
/// The WebGL2 side, in opentpw-gl.js (the page registers it under this module name). Objects are
/// integer handles; draws travel as one command stream per <see cref="GraphicsDevice.SubmitCommands"/>.
/// </summary>
internal static partial class Gl
{
	public const string Module = "opentpw-gl";

	public const int Byte = 0x1400, UnsignedByte = 0x1401, Short = 0x1402, UnsignedShort = 0x1403, Int = 0x1404, UnsignedInt = 0x1405, Float = 0x1406, HalfFloat = 0x140B;
	public const int Red = 0x1903, Rgba = 0x1908, Rgba8 = 0x8058, R8 = 0x8229, R32F = 0x822E;
	public const int DepthStencil = 0x84F9, Depth24Stencil8 = 0x88F0, Depth32FStencil8 = 0x8CAD, UnsignedInt248 = 0x84FA, Float32UnsignedInt248Rev = 0x8DAD;
	public const int ArrayBuffer = 0x8892, ElementArrayBuffer = 0x8893, UniformBuffer = 0x8A11;

	[JSImport( "init", Module )]
	public static partial bool Init( string canvasId );

	[JSImport( "maxTextureSize", Module )]
	public static partial int MaxTextureSize();

	/// <summary>A texture (samples 1) or a multisampled renderbuffer.</summary>
	[JSImport( "createTexture", Module )]
	public static partial int CreateTexture( int internalFormat, int width, int height, int mipLevels, int samples );

	[JSImport( "uploadTexture", Module )]
	public static partial void UploadTexture( int texture, int level, int x, int y, int width, int height, int format, int type, [JSMarshalAs<JSType.MemoryView>] Span<byte> data );

	[JSImport( "createFramebuffer", Module )]
	public static partial int CreateFramebuffer( [JSMarshalAs<JSType.MemoryView>] Span<int> colorTextures, int depthTexture );

	[JSImport( "createBuffer", Module )]
	public static partial int CreateBuffer( int target, int size );

	[JSImport( "uploadBuffer", Module )]
	public static partial void UploadBuffer( int buffer, int offset, [JSMarshalAs<JSType.MemoryView>] Span<byte> data );

	[JSImport( "createSampler", Module )]
	public static partial int CreateSampler( int minFilter, int magFilter, int wrapS, int wrapT, int wrapR, double minLod, double maxLod, int anisotropy );

	/// <summary>Compiles and links; throws with the info log when either step fails.</summary>
	[JSImport( "createProgram", Module )]
	public static partial int CreateProgram( string vertex, string fragment );

	/// <summary>Assigns a uniform block binding point or a sampler unit by name; false when the program does not use it.</summary>
	[JSImport( "bindProgramResource", Module )]
	public static partial bool BindProgramResource( int program, string name, bool uniformBlock, int slot );

	[JSImport( "createPipeline", Module )]
	public static partial int CreatePipeline( [JSMarshalAs<JSType.MemoryView>] Span<int> description );

	[JSImport( "createResourceSet", Module )]
	public static partial int CreateResourceSet( [JSMarshalAs<JSType.MemoryView>] Span<int> entries );

	[JSImport( "destroy", Module )]
	public static partial void Destroy( int handle );

	[JSImport( "execute", Module )]
	public static partial void Execute( [JSMarshalAs<JSType.MemoryView>] Span<int> commands, [JSMarshalAs<JSType.MemoryView>] Span<byte> data );

	/// <summary>Sizes the canvas drawing buffer in device pixels.</summary>
	[JSImport( "resizeCanvas", Module )]
	public static partial void ResizeCanvas( int width, int height );

	/// <summary>Copies a framebuffer to the canvas, flipped (docs/WEB.md: row 0 is the top in memory).</summary>
	[JSImport( "present", Module )]
	public static partial void Present( int framebuffer, int width, int height );

	/// <summary>Canvas size in CSS pixels, device pixel ratio, and screen size.</summary>
	[JSImport( "metrics", Module )]
	[return: JSMarshalAs<JSType.Array<JSType.Number>>]
	public static partial double[] Metrics();

	/// <summary>Input since the last call, one event per line (see opentpw-gl.js).</summary>
	[JSImport( "takeEvents", Module )]
	public static partial string TakeEvents();

	[JSImport( "setCursorVisible", Module )]
	public static partial void SetCursorVisible( bool visible );
}

/// <summary>Command stream opcodes shared with opentpw-gl.js.</summary>
internal enum Op
{
	SetFramebuffer = 1,
	Viewport = 2,
	Scissor = 3,
	ClearColor = 4,
	ClearDepth = 5,
	SetPipeline = 6,
	SetVertexBuffer = 7,
	SetIndexBuffer = 8,
	SetResourceSet = 9,
	Draw = 10,
	DrawIndexed = 11,
	UpdateBuffer = 12,
	Resolve = 13,
	GenerateMipmaps = 14,
}
