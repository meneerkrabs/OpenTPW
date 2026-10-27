using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace OpenTPW.Tests;

/// <summary>
/// Draws the park's textured shaders on a real GPU backend into an offscreen target and checks that the texture's detail reaches
/// the picture (issue #754: on Direct3D 11 every world surface showed one flat colour per face). Direct3D 11 runs on every Windows
/// machine, the CI runner's WARP adapter included; Metal runs on macOS with OPENTPW_NATIVE_SHADER_TESTS=1 (architecture-matching
/// native SPIR-V libraries). The probe follows the engine's path: the engine's GLSL to SPIR-V compile, Veldrid's cross-compile for
/// the device, the engine's Vertex layout and reflected resource layouts, and a texture made like Texture.CreateNative (RGBA8,
/// full mip chain, level 0 uploaded, the rest generated on the GPU).
/// </summary>
[TestClass]
[DoNotParallelize]
public class NativeWorldShaderRenderTests
{
	private const int Size = 64; // the target and the texture: one texel per pixel, so mip 0 is sampled
	private const int Cell = 8; // the checkerboard's cell size in texels
	private const uint FlagClamp = 0x80000000u; // TextureSlotAddressing.ClampFlag: the SampleGrad + textureSize path

	[DataTestMethod]
	[DataRow( "test.shader", 0u )]
	[DataRow( "test.shader", FlagClamp )]
	[DataRow( "advisor.shader", 0u )]
	[DataRow( "sprite.shader", 0u )]
	[DataRow( "unlit.shader", 0u )]
	public void TexturedShaderKeepsTheTextureDetail( string shader, uint matFlags ) => AssertTextureDetail( shader, matFlags, materialScratchBuffer: false );

	/// <summary>
	/// The uniform block as Material uploads it: a 2048-byte dynamic uniform buffer of which each draw records only the block's bytes (272, more with point
	/// lights). On Direct3D 11 Veldrid writes such a partial update through a staging buffer and a GPU copy.
	/// </summary>
	[DataTestMethod]
	[DataRow( "test.shader" )]
	[DataRow( "unlit.shader" )]
	public void MaterialScratchBufferUploadReachesTheShader( string shader ) => AssertTextureDetail( shader, 0u, materialScratchBuffer: true );

	private static void AssertTextureDetail( string shader, uint matFlags, bool materialScratchBuffer )
	{
		using var device = CreateDevice();
		Log = new();
		var pixels = Draw( device, shader, matFlags, materialScratchBuffer );

		// the picture must follow the checkerboard: the mean of the pixels under white cells against those under black cells
		// (either orientation, the backends' texture origins differ), and the cells must not be one averaged colour
		double white = 0, black = 0;
		int whiteCount = 0, blackCount = 0;
		for ( int y = 0; y < Size; y++ )
			for ( int x = 0; x < Size; x++ )
			{
				var offset = (y * Size + x) * 4;
				double luma = 0.299 * pixels[offset] + 0.587 * pixels[offset + 1] + 0.114 * pixels[offset + 2];
				if ( IsWhite( x, y ) ) { white += luma; whiteCount++; }
				else { black += luma; blackCount++; }
			}
		white /= whiteCount;
		black /= blackCount;
		var contrast = Math.Abs( white - black );
		Console.WriteLine( $"{device.BackendType} {shader} flags 0x{matFlags:X8}{(materialScratchBuffer ? " (Material scratch buffer)" : "")}: white cells {white:F1}, black cells {black:F1}, centre {Describe( pixels, Size / 2, Size / 2 )}" );
		// a coarse picture of the target (every second row and column) for the CI log
		for ( int y = 0; y < Size; y += 2 )
		{
			var row = new char[Size / 2];
			for ( int x = 0; x < Size; x += 2 )
				row[x / 2] = pixels[(y * Size + x) * 4 + 1] switch { > 64 => '#', > 8 => '+', _ => '.' };
			Console.WriteLine( new string( row ) );
		}
		Assert.IsTrue( contrast > 20, $"{shader} on {device.BackendType} lost the texture detail: white cells average {white:F1}, black cells {black:F1}" );
	}

	/// <summary>
	/// A self-lit material (material word +0xC bit 0x1, TextureSlotAddressing.SelfLitFlag) takes the draw's constant colour in tp.exe (0x580C51), not the lighting colour: with the ambient-only light of
	/// this draw a lit white texel comes out at the ambient (0.4 in the park shader, 0.15 in the advisor's), a self-lit one at the texel itself (issue 829: the Gift Shop chest's glow and gold interior were drawn dark).
	/// </summary>
	[DataTestMethod]
	[DataRow( "test.shader", 80, 125 )]
	[DataRow( "advisor.shader", 25, 55 )]
	public void ASelfLitCornerIsDrawnFullBrightAndALitOneAtTheAmbient( string shader, int litLow, int litHigh )
	{
		using var device = CreateDevice();
		Log = new();
		double Whites( byte[] pixels )
		{
			double sum = 0;
			var count = 0;
			for ( var y = 0; y < Size; y++ )
				for ( var x = 0; x < Size; x++ )
					if ( IsWhite( x, y ) )
					{
						sum += pixels[(y * Size + x) * 4 + 1];
						count++;
					}
			return sum / count;
		}
		var lit = Whites( Draw( device, shader, TextureSlotAddressing.ClampFlag, materialScratchBuffer: false ) );
		var selfLit = Whites( Draw( device, shader, TextureSlotAddressing.ClampFlag | TextureSlotAddressing.SelfLitFlag, materialScratchBuffer: false ) );
		Console.WriteLine( $"{device.BackendType} {shader}: white cells lit {lit:F1}, self-lit {selfLit:F1}" );
		Assert.IsTrue( lit > litLow && lit < litHigh, $"{shader}: the lit white cells are at the ambient: {lit:F1}" );
		Assert.IsTrue( selfLit > 245, $"{shader}: the self-lit white cells are the texel itself: {selfLit:F1}" );
	}

	/// <summary>
	/// [EXT:evening] At night a self-lit material that is no light source takes the night tint of the extension's other unlit content (the sprites'), which ModelEntity writes into the camera position that
	/// test.shader never reads; a light source (glow, flame, fire, torch, lamp, light) stays full-bright; a lit material is as before; and with the tinted ambient off (day) the camera position is not read at all.
	/// </summary>
	[TestMethod]
	public void ASelfLitCornerTakesTheNightTintUnlessItIsALightSourceAndAtDayNothingChanges()
	{
		using var device = CreateDevice();
		Log = new();
		double Whites( byte[] pixels, int channel )
		{
			double sum = 0;
			var count = 0;
			for ( var y = 0; y < Size; y++ )
				for ( var x = 0; x < Size; x++ )
					if ( IsWhite( x, y ) )
					{
						sum += pixels[(y * Size + x) * 4 + channel];
						count++;
					}
			return sum / count;
		}
		var tint = EveningLook.At( 0f ).UnlitTint;
		ObjectUniformBuffer Night( ObjectUniformBuffer ubo )
		{
			ubo.g_vCameraPos = tint;
			ubo._padding3 = 0.2f;
			ubo._padding4 = 0.3f;
			ubo._padding5 = 0.5f;
			return ubo;
		}
		ObjectUniformBuffer DayWithAPosition( ObjectUniformBuffer ubo )
		{
			ubo.g_vCameraPos = new System.Numerics.Vector3( 300f, -40f, 12f );
			return ubo;
		}
		var clamp = TextureSlotAddressing.ClampFlag;
		var night = Draw( device, "test.shader", clamp | TextureSlotAddressing.SelfLitFlag, false, adjustUniforms: Night );
		var source = Draw( device, "test.shader", clamp | TextureSlotAddressing.SelfLitFlag | TextureSlotAddressing.LightSourceFlag, false, adjustUniforms: Night );
		var day = Draw( device, "test.shader", clamp | TextureSlotAddressing.SelfLitFlag, false, adjustUniforms: DayWithAPosition );
		Console.WriteLine( $"{device.BackendType}: self-lit at night {Whites( night, 0 ):F1},{Whites( night, 1 ):F1},{Whites( night, 2 ):F1}, light source {Whites( source, 1 ):F1}, day {Whites( day, 1 ):F1}" );
		for ( var channel = 0; channel < 3; channel++ )
		{
			var expected = new[] { tint.X, tint.Y, tint.Z }[channel] * 255;
			Assert.AreEqual( expected, Whites( night, channel ), 2.5, $"channel {channel}: the night tint" );
			Assert.IsTrue( Whites( source, channel ) > 245, $"channel {channel}: a light source stays full-bright" );
			Assert.IsTrue( Whites( day, channel ) > 245, $"channel {channel}: at day the camera position is not a tint" );
		}
	}

	/// <summary>
	/// Direct3D 11 links the vertex shader's outputs to the pixel shader's inputs by register: every pixel shader input must sit in
	/// the register where the vertex shader writes the same semantic, with at least its components (issue #754). This compiles
	/// every shader as the engine does on Windows (Veldrid.SPIRV's HLSL, then the system HLSL compiler with Veldrid's vs_5_0/ps_5_0
	/// profile and optimisation level 3) and compares the two signatures, so it needs no GPU.
	/// </summary>
	[TestMethod]
	public void EveryShaderLinksItsStagesByRegisterOnDirect3D11()
	{
		if ( !OperatingSystem.IsWindows() )
			Assert.Inconclusive( "Needs the Windows HLSL compiler (d3dcompiler_47.dll)." );
		Log = new();
		var directory = Path.GetDirectoryName( GetShaderPath( "test.shader" ) )!;
		var problems = new System.Collections.Generic.List<string>();
		foreach ( var path in Directory.GetFiles( directory, "*.shader" ).Order( StringComparer.Ordinal ) )
		{
			var program = ShaderCompiler.CompileProgram( path, Veldrid.SPIRV.CrossCompileTarget.HLSL );
			var hlsl = Veldrid.SPIRV.SpirvCompilation.CompileVertexFragment( program.Vertex, program.Fragment, Veldrid.SPIRV.CrossCompileTarget.HLSL, new Veldrid.SPIRV.CrossCompileOptions() );
			var vertex = Signature( hlsl.VertexShader, "vs_5_0", input: false );
			var pixel = Signature( hlsl.FragmentShader, "ps_5_0", input: true );
			foreach ( var element in pixel )
			{
				// values the rasterizer makes itself, not the vertex shader
				if ( element.Name is "SV_ISFRONTFACE" or "SV_PRIMITIVEID" or "SV_SAMPLEINDEX" or "SV_COVERAGE" )
					continue;
				var match = vertex.FirstOrDefault( output => output.Name == element.Name && output.Index == element.Index );
				if ( match.Name == null )
					problems.Add( $"{Path.GetFileName( path )}: {element.Name}{element.Index} is not written by the vertex shader" );
				else if ( match.Register != element.Register || (match.Mask & element.Mask) != element.Mask )
					problems.Add( $"{Path.GetFileName( path )}: {element.Name}{element.Index} is read from register {element.Register} (mask {element.Mask}) but written to register {match.Register} (mask {match.Mask})" );
			}
		}
		Assert.AreEqual( 0, problems.Count, string.Join( "\n", problems ) );
	}

	private static (string Name, int Index, int Register, int Mask)[] Signature( string hlsl, string profile, bool input )
	{
		var bytecode = Vortice.D3DCompiler.Compiler.Compile( hlsl, "main", profile, profile, Vortice.D3DCompiler.ShaderFlags.OptimizationLevel3, Vortice.D3DCompiler.EffectFlags.None );
		using var reflection = Vortice.D3DCompiler.Compiler.Reflect<Vortice.Direct3D11.Shader.ID3D11ShaderReflection>( bytecode.Span );
		return (input ? reflection.InputParameters : reflection.OutputParameters)
			.Select( element => (element.SemanticName.ToUpperInvariant(), (int)element.SemanticIndex, (int)element.Register, (int)element.UsageMask) )
			.ToArray();
	}

	private static bool IsWhite( int x, int y ) => ((x / Cell) + (y / Cell)) % 2 == 0;

	private static string Describe( byte[] pixels, int x, int y )
	{
		var offset = (y * Size + x) * 4;
		return $"({pixels[offset]}, {pixels[offset + 1]}, {pixels[offset + 2]}, {pixels[offset + 3]})";
	}

	/// <summary>
	/// Issue #754 (a black window for about 30 seconds before the lobby and freezes when an object is placed, on Direct3D 11): every material used to make its
	/// own native shader pair, and Veldrid compiles HLSL for each new Direct3D 11 shader. Now the engine's shaders share one native program per file
	/// (ShaderCompiler.CompileShader): the second owner gets the same objects without a compile. The log line gives the device's cost of one native
	/// program, the compile a material used to pay (docs/ENGINE-PERFORMANCE.md, "Start and build hitches").
	/// </summary>
	[DataTestMethod]
	[DataRow( "test.shader" )]
	[DataRow( "sprite.shader" )]
	[DataRow( "particle.shader" )]
	[DataRow( "ui-batch.shader" )]
	public void NativeProgramIsCompiledOnceAndShared( string shader )
	{
		using var device = CreateDevice();
		Log = new();
		var previousDevice = global::Global.Device;
		global::Global.Device = device;
		try
		{
			var path = GetShaderPath( shader );
			// the SPIR-V step is cached per file and is not the backend's compile: made (and the native pair released) first
			ShaderCompiler.Release( ShaderCompiler.CompileShader( path ) );
			var watch = System.Diagnostics.Stopwatch.StartNew();
			var first = ShaderCompiler.CompileShader( path );
			var compile = watch.Elapsed.TotalMilliseconds;
			watch.Restart();
			var second = ShaderCompiler.CompileShader( path );
			var shared = watch.Elapsed.TotalMilliseconds;
			try
			{
				Console.WriteLine( $"{device.BackendType} {shader}: native program {compile:F1} ms, shared by a second owner in {shared:F3} ms" );
				Assert.AreSame( first.VertexShader, second.VertexShader );
				Assert.AreSame( first.FragmentShader, second.FragmentShader );
			}
			finally
			{
				ShaderCompiler.Release( first );
				ShaderCompiler.Release( second );
			}
			Assert.IsTrue( first.VertexShader.IsDisposed && first.FragmentShader.IsDisposed, "the last release disposes the shared program" );
		}
		finally
		{
			global::Global.Device = previousDevice;
		}
	}

	/// <summary>
	/// Issue #754 (start time on Windows): the engine keeps the Direct3D 11 bytecode of its shaders between runs (Direct3D11Bytecode). The kept bytecode must be
	/// exactly what Veldrid's own HLSL compile makes for the same shader, the second request must read it instead of compiling, and the engine's shaders made
	/// from it must draw the same picture as Veldrid's.
	/// </summary>
	[DataTestMethod]
	[DataRow( "test.shader" )]
	[DataRow( "sprite.shader" )]
	[DataRow( "unlit.shader" )]
	public void KeptDirect3D11BytecodeIsVeldridsOwnCompile( string shader )
	{
		if ( OperatingSystem.IsWindows() )
			KeptBytecode( shader );
		else
			Assert.Inconclusive( "Direct3D 11 runs on Windows." );
	}

	[System.Runtime.Versioning.SupportedOSPlatform( "windows" )]
	private static void KeptBytecode( string shader )
	{
		using var device = CreateDevice();
		Log = new();
		var cache = Directory.CreateTempSubdirectory( "opentpw-dxbc-" ).FullName;
		var previousCache = ShaderCompiler.SpirvCacheDirectory;
		try
		{
			Assert.IsNotNull( Direct3D11Bytecode.CompilerIdentity, "the system HLSL compiler is found, so its bytecode is kept" );
			var program = ShaderCompiler.CompileProgram( GetShaderPath( shader ), Veldrid.SPIRV.CrossCompileTarget.HLSL );
			var veldrid = Veldrid.SPIRV.ResourceFactoryExtensions.CreateFromSpirv( device.ResourceFactory,
				new ShaderDescription( ShaderStages.Vertex, program.Vertex, "main" ),
				new ShaderDescription( ShaderStages.Fragment, program.Fragment, "main" ) );
			var hlsl = Veldrid.SPIRV.SpirvCompilation.CompileVertexFragment( program.Vertex, program.Fragment, Veldrid.SPIRV.CrossCompileTarget.HLSL, new Veldrid.SPIRV.CrossCompileOptions() );
			var vertexFile = Direct3D11Bytecode.CacheFile( cache, System.Text.Encoding.ASCII.GetBytes( hlsl.VertexShader ), "vs_5_0" )!;
			byte[] vertex, fragment;
			try
			{
				LoadProfile.Enable( true );
				vertex = Direct3D11Bytecode.Get( cache, hlsl.VertexShader, ShaderStages.Vertex, true, out var keptVertex );
				fragment = Direct3D11Bytecode.Get( cache, hlsl.FragmentShader, ShaderStages.Fragment, true, out var keptFragment );
				Assert.AreEqual( 2, LoadProfile.Counters()["hlsl compiles"] );
				Assert.IsNull( keptVertex ?? keptFragment, "an empty cache compiles" );
				CollectionAssert.AreEqual( VeldridBytecode( veldrid[0] ), vertex, "the kept vertex bytecode is Veldrid's own compile" );
				CollectionAssert.AreEqual( VeldridBytecode( veldrid[1] ), fragment, "the kept fragment bytecode is Veldrid's own compile" );
				LoadProfile.Enable( true );
				CollectionAssert.AreEqual( vertex, Direct3D11Bytecode.Get( cache, hlsl.VertexShader, ShaderStages.Vertex, true, out keptVertex ) );
				CollectionAssert.AreEqual( fragment, Direct3D11Bytecode.Get( cache, hlsl.FragmentShader, ShaderStages.Fragment, true, out keptFragment ) );
				Assert.AreEqual( vertexFile, keptVertex );
				var counters = LoadProfile.Counters();
				Assert.AreEqual( 2, counters["hlsl cache hits"] );
				Assert.IsFalse( counters.ContainsKey( "hlsl compiles" ), "kept bytecode is not compiled again" );

				// a file cut short after it was written fails its digest: compiled again and replaced
				var whole = File.ReadAllBytes( vertexFile );
				File.WriteAllBytes( vertexFile, whole.Take( whole.Length / 2 ).ToArray() );
				LoadProfile.Enable( true );
				CollectionAssert.AreEqual( vertex, Direct3D11Bytecode.Get( cache, hlsl.VertexShader, ShaderStages.Vertex, true, out keptVertex ) );
				Assert.IsNull( keptVertex );
				Assert.AreEqual( 1, LoadProfile.Counters()["hlsl compiles"] );
				CollectionAssert.AreEqual( whole, File.ReadAllBytes( vertexFile ), "the damaged file is replaced" );
			}
			finally
			{
				LoadProfile.Enable( false );
				foreach ( var compiled in veldrid )
					compiled.Dispose();
			}

			// the engine's path (ShaderCompiler.CreateNative with a cache folder) draws what Veldrid's draws
			var expected = Draw( device, shader, 0u, materialScratchBuffer: false );
			ShaderCompiler.SpirvCacheDirectory = cache;
			var kept = Draw( device, shader, 0u, materialScratchBuffer: false, ShaderCompiler.CreateNative );
			CollectionAssert.AreEqual( expected, kept, $"{shader} draws the same picture from the kept bytecode" );

			// a whole file (its digest matches) that starts as a DXBC container of the right size but is not what the compiler made: the device refuses it,
			// CreateNative removes the kept files, compiles the HLSL again and draws the same picture, and the file holds the compiler's bytecode again
			var garbage = (byte[])vertex.Clone();
			Array.Fill( garbage, (byte)0x5A, 32, garbage.Length - 32 );
			ShaderCache.Write( cache, vertexFile, garbage );
			LoadProfile.Enable( true );
			var recovered = Draw( device, shader, 0u, materialScratchBuffer: false, ShaderCompiler.CreateNative );
			Assert.AreEqual( 1, LoadProfile.Counters()["hlsl cache refused"] );
			CollectionAssert.AreEqual( expected, recovered, $"{shader} draws the same picture after its refused bytecode was compiled again" );
			CollectionAssert.AreEqual( vertex, ShaderCache.Read( vertexFile, _ => true ), "the refused file holds the compiler's bytecode again" );
		}
		finally
		{
			LoadProfile.Enable( false );
			ShaderCompiler.SpirvCacheDirectory = previousCache;
			Directory.Delete( cache, true );
		}
	}

	/// <summary>The bytecode Veldrid compiled for a Direct3D 11 shader (D3D11Shader.Bytecode, internal to Veldrid).</summary>
	private static byte[] VeldridBytecode( Veldrid.Shader shader ) =>
		(byte[])shader.GetType().GetProperty( "Bytecode" )!.GetValue( shader )!;

	private static GraphicsDevice CreateDevice()
	{
		// the engine's options (Renderer.CreateDevice) without its swapchain
		var options = new GraphicsDeviceOptions
		{
			PreferStandardClipSpaceYDirection = true,
			PreferDepthRangeZeroToOne = true,
			SwapchainDepthFormat = null,
			SwapchainSrgbFormat = false,
			SyncToVerticalBlank = false,
			HasMainSwapchain = false
		};
		if ( OperatingSystem.IsWindows() )
			return GraphicsDevice.CreateD3D11( options );
		if ( OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable( "OPENTPW_NATIVE_SHADER_TESTS" ) == "1" )
			return GraphicsDevice.CreateMetal( options );
		Assert.Inconclusive( "Direct3D 11 on Windows; Metal on macOS with OPENTPW_NATIVE_SHADER_TESTS=1." );
		return null!;
	}

	private static byte[] Draw( GraphicsDevice device, string shader, uint matFlags, bool materialScratchBuffer, Func<ResourceFactory, byte[], byte[], Veldrid.Shader[]>? createShaders = null, Func<ObjectUniformBuffer, ObjectUniformBuffer>? adjustUniforms = null )
	{
		var factory = device.ResourceFactory;
		var target = device.BackendType switch
		{
			GraphicsBackend.Direct3D11 => Veldrid.SPIRV.CrossCompileTarget.HLSL,
			GraphicsBackend.Metal => Veldrid.SPIRV.CrossCompileTarget.MSL,
			_ => Veldrid.SPIRV.CrossCompileTarget.GLSL
		};
		var program = ShaderCompiler.CompileProgram( GetShaderPath( shader ), target );
		var shaders = createShaders?.Invoke( factory, program.Vertex, program.Fragment ) ?? Veldrid.SPIRV.ResourceFactoryExtensions.CreateFromSpirv( factory,
			new ShaderDescription( ShaderStages.Vertex, program.Vertex, "main" ),
			new ShaderDescription( ShaderStages.Fragment, program.Fragment, "main" ) );

		using var colour = factory.CreateTexture( TextureDescription.Texture2D( Size, Size, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget ) );
		using var staging = factory.CreateTexture( TextureDescription.Texture2D( Size, Size, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging ) );
		using var depth = factory.CreateTexture( TextureDescription.Texture2D( Size, Size, 1, 1, PixelFormat.D32_Float_S8_UInt, TextureUsage.DepthStencil ) );
		using var framebuffer = factory.CreateFramebuffer( new FramebufferDescription( depth, colour ) );

		// the texture as Texture.CreateNative makes it
		var checker = new byte[Size * Size * 4];
		for ( int y = 0; y < Size; y++ )
			for ( int x = 0; x < Size; x++ )
			{
				var value = IsWhite( x, y ) ? (byte)255 : (byte)0;
				var offset = (y * Size + x) * 4;
				checker[offset] = checker[offset + 1] = checker[offset + 2] = value;
				checker[offset + 3] = 255;
			}
		var mipLevels = (uint)Math.Floor( Math.Log2( Size ) ) + 1;
		using var texture = factory.CreateTexture( TextureDescription.Texture2D( Size, Size, mipLevels, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled | TextureUsage.GenerateMipmaps ) );
		var handle = GCHandle.Alloc( checker, GCHandleType.Pinned );
		try { device.UpdateTexture( texture, handle.AddrOfPinnedObject(), (uint)checker.Length, 0, 0, 0, Size, Size, 1, 0, 0 ); }
		finally { handle.Free(); }
		using var view = factory.CreateTextureView( texture );
		using var sampler = factory.CreateSampler( ColorSampler.Describe( SamplerType.AnisotropicWrap, new RenderQuality( TextureFilterMode.Anisotropic, 16, true, 1f ) ) );

		// one quad over the whole target in clip space (identity matrices), UVs 0..1, slot 0, ambient-only light
		var ubo = new ObjectUniformBuffer
		{
			g_mModel = Matrix4x4.Identity,
			g_mView = Matrix4x4.Identity,
			g_mProj = Matrix4x4.Identity,
			g_vLightPos = new Vector3( 0, 0, -10 ),
			g_vLightColor = Vector3.Zero,
		};
		if ( adjustUniforms != null )
			ubo = adjustUniforms( ubo );
		using var uniforms = factory.CreateBuffer( materialScratchBuffer
			? new BufferDescription( 16 * 128, BufferUsage.UniformBuffer | BufferUsage.Dynamic ) // Material.SetupResources' ScratchBuffer
			: new BufferDescription( 4096, BufferUsage.UniformBuffer ) );

		Vertex Corner( float x, float y, float u, float v ) => new Vertex( new Vector3( x, y, 0.5f ), new Vector3( 0, 0, -1 ), new Vector2( u, v ) ) { TexIndex = 0, MatFlags = matFlags };
		var vertices = new[]
		{
			Corner( -1, -1, 0, 0 ), Corner( 1, -1, 1, 0 ), Corner( 1, 1, 1, 1 ),
			Corner( -1, -1, 0, 0 ), Corner( 1, 1, 1, 1 ), Corner( -1, 1, 0, 1 ),
		};
		using var vertexBuffer = factory.CreateBuffer( new BufferDescription( (uint)vertices.Length * Vertex.SizeInBytes, BufferUsage.VertexBuffer ) );

		var layouts = program.Reflection.ResourceLayouts.Select( factory.CreateResourceLayout ).ToArray();
		var sets = program.Reflection.ResourceLayouts.Select( ( description, index ) => factory.CreateResourceSet( new ResourceSetDescription( layouts[index],
			description.Elements.Select( element => element.Kind switch
			{
				ResourceKind.UniformBuffer => (BindableResource)uniforms,
				ResourceKind.TextureReadOnly => view,
				ResourceKind.Sampler => sampler,
				_ => throw new InvalidOperationException( $"{shader}: unexpected resource {element.Name} ({element.Kind})" )
			} ).ToArray() ) ) ).ToArray();

		using var pipeline = factory.CreateGraphicsPipeline( new GraphicsPipelineDescription(
			BlendStateDescription.SingleOverrideBlend,
			DepthStencilStateDescription.Disabled,
			new RasterizerStateDescription( FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false ),
			PrimitiveTopology.TriangleList,
			new ShaderSetDescription( [new VertexLayoutDescription( Vertex.VertexElementDescriptions )], shaders ),
			layouts,
			framebuffer.OutputDescription,
			ResourceBindingModel.Improved ) ); // as Material builds its pipelines

		using var commands = factory.CreateCommandList();
		commands.Begin();
		commands.UpdateBuffer( uniforms, 0, ubo );
		commands.UpdateBuffer( vertexBuffer, 0, vertices );
		commands.GenerateMipmaps( texture );
		commands.SetFramebuffer( framebuffer );
		commands.SetFullViewports();
		commands.SetFullScissorRects();
		commands.ClearDepthStencil( 1f );
		commands.ClearColorTarget( 0, RgbaFloat.Black );
		commands.SetVertexBuffer( 0, vertexBuffer );
		commands.SetPipeline( pipeline );
		for ( uint i = 0; i < sets.Length; i++ )
			commands.SetGraphicsResourceSet( i, sets[i] );
		commands.Draw( (uint)vertices.Length );
		commands.CopyTexture( colour, staging );
		commands.End();
		device.SubmitCommands( commands );
		device.WaitForIdle();

		var pixels = new byte[Size * Size * 4];
		var mapped = device.Map( staging, MapMode.Read );
		try
		{
			for ( int y = 0; y < Size; y++ )
				Marshal.Copy( mapped.Data + (int)(y * mapped.RowPitch), pixels, y * Size * 4, Size * 4 );
		}
		finally
		{
			device.Unmap( staging );
		}

		foreach ( var set in sets )
			set.Dispose();
		foreach ( var layout in layouts )
			layout.Dispose();
		foreach ( var compiled in shaders )
			compiled.Dispose();
		return pixels;
	}

	private static string GetShaderPath( string shader )
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !File.Exists( Path.Combine( directory.FullName, "content", "shaders", shader ) ) )
			directory = directory.Parent;
		Assert.IsNotNull( directory, "Repository shader assets must be available for this test." );
		return Path.Combine( directory!.FullName, "content", "shaders", shader );
	}
}
