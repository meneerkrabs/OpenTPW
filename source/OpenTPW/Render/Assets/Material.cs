using Veldrid;

namespace OpenTPW;

[Flags]
public enum MaterialFlags
{
	None,

	DisableDepthTest,
	DisableDepthWrite,

	DisableDepth = DisableDepthTest | DisableDepthWrite,

	/// <summary>Source-alpha-over-one blending instead of the default alpha blend (the weather's rain streaks).</summary>
	AdditiveBlend = 4,

	/// <summary>No face culling, so camera-facing particles survive whichever way they wind.</summary>
	DoubleSided = 8,

	/// <summary>Plain one-plus-one additive blending (the particles' render flag 0x4); wins over <see cref="AdditiveBlend"/>.</summary>
	OneOneBlend = 16,

	/// <summary>Replace colour and alpha without blending; additive flags retain precedence.</summary>
	OpaqueBlend = 32
}

public partial class Material : Asset
{
	public Shader Shader { get; set; }

	public Type UniformBufferType { get; } = typeof( ObjectUniformBuffer );
	public Pipeline Pipeline { get; private set; } = null!;

	private Dictionary<string, BindableResource> _boundResources = new();

	// [EXT:texture-pack] Textures can swap their GPU texture in place (a texture pack switch); the binding follows the Texture object.
	private readonly Dictionary<string, Texture> _boundTextures = new();
	private readonly Dictionary<string, (Texture[] Textures, string[] Names)> _boundTextureArrays = new();

	private ResourceLayout[] _resourceLayouts = Array.Empty<ResourceLayout>();
	private ResourceSet[]? resourceSets;

	public Material( string shaderPath, MaterialFlags flags = MaterialFlags.None )
	{
		Shader = new Shader( shaderPath );
		Shader.OnRecompile += () => SetupResources( flags );
		ReceivesPointLights = ShaderReadsPointLights( shaderPath );
		IsWorldShader = System.IO.Path.GetFileName( shaderPath ).Equals( "test.shader", StringComparison.OrdinalIgnoreCase );

		All.Add( this );
		SetupResources( flags );
	}

	protected Material( string shaderPath, Type uniformBufferType, MaterialFlags flags = MaterialFlags.None )
	{
		Shader = new Shader( shaderPath );
		Shader.OnRecompile += () => SetupResources( flags );
		UniformBufferType = uniformBufferType;
		ReceivesPointLights = ShaderReadsPointLights( shaderPath );
		IsWorldShader = System.IO.Path.GetFileName( shaderPath ).Equals( "test.shader", StringComparison.OrdinalIgnoreCase );

		All.Add( this );
		SetupResources( flags );
	}

	private DeviceBuffer ScratchBuffer;

	private bool released;

	/// <summary>Releases a renderer-owned material after its last submitted frame, including its own shader.</summary>
	internal void ReleaseOwnedResources()
	{
		if ( released )
			return;
		released = true;
		ClearResourceSets();
		Asset.All.Remove( this );
		Shader.OnRecompile = null!;
		DisposeOwnedResources();
		Shader.ReleaseOwnedResources();
	}

	/// <summary>
	/// Retires the material's own GPU objects. A failed construction can leave the pipeline, scratch
	/// buffer or layouts unset, so every native object here is optional. With a live renderer the
	/// objects can still be referenced by the last submitted frame; without one the material was
	/// never drawn and is freed at once.
	/// </summary>
	private void DisposeOwnedResources()
	{
		// the objects of now, as SetupResources captures them: a deferred deletion must not reach ones made after it was scheduled
		var pipeline = Pipeline;
		var scratchBuffer = ScratchBuffer;
		var layouts = _resourceLayouts;
		Action dispose = () =>
		{
			pipeline?.Dispose();
			scratchBuffer?.Dispose();
			foreach ( var layout in layouts ?? [] )
				layout.Dispose();
			_boundResources.Clear();
			_boundTextures.Clear();
			_boundTextureArrays.Clear();
		};
		if ( Render is { } renderer )
			renderer.ScheduleDelete( dispose );
		else
			dispose();
	}

	private static readonly Sampler[] Samplers =
	[
		CreateSampler( SamplerType.Anisotropic ),
		CreateSampler( SamplerType.Linear ),
		CreateSampler( SamplerType.Point ),
		CreateSampler( SamplerType.AnisotropicWrap ),
		CreateSampler( SamplerType.AnisotropicRepeat ),
	];

	private static Sampler CreateSampler( SamplerType type )
	{
		return Device.ResourceFactory.CreateSampler( ColorSampler.Describe( type, CompatibilityRuntime.RenderQuality ) );
	}

	private byte[]? lastBlock;

	/// <summary>
	/// <see cref="Set{T}(string, T)"/> that skips the upload when the block is the one the buffer already holds: the draws of one frame that share their model matrix, camera and lights (the translucent
	/// meshes of a terrain, the faces of one object) write the block once. Every other write to the buffer forgets the remembered block.
	/// </summary>
	internal void SetUnchanged<T>( string name, in T value ) where T : unmanaged
	{
		var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes( new ReadOnlySpan<T>( in value ) );
		if ( lastBlock != null && bytes.SequenceEqual( lastBlock ) )
			return;
		Set( name, value );
		lastBlock = bytes.ToArray();
	}

	public void Set<T>( string name, T obj ) where T : unmanaged
	{
		lastBlock = null;
		Render.UpdateUniform( ScratchBuffer, obj );
		Bind( name, ScratchBuffer );
	}

	/// <summary>
	/// [EXT:point-lights] Whether the shader reads the point lights after the uniform block (<c>g_vPointLight</c>): the park's lit shaders (test.shader for the ground, objects,
	/// coasters and tracks, pool-deck.shader and pool-basin.shader for the pools). The sprite, particle, sky and water shaders do not, and a draw of theirs never uploads lights.
	/// </summary>
	public bool ReceivesPointLights { get; }

	/// <summary>Whether this is the park's lit world shader (test.shader), which reads the alpha test flags of <see cref="WorldTranslucency"/> from the vertices.</summary>
	internal bool IsWorldShader { get; }

	/// <summary>
	/// The alpha type of each texture bound as the shader's "Color" array (<see cref="Texture.AlphaType"/>, as the textures were when the model was made), or null when this is not the world shader,
	/// has no such array or every texture is opaque: the geometry drawn with it then needs no alpha test flag and no translucent pass.
	/// </summary>
	internal TextureAlphaType[]? WorldSlotTypes()
	{
		if ( !IsWorldShader || !_boundTextureArrays.TryGetValue( "Color", out var bound ) )
			return null;
		var types = new TextureAlphaType[bound.Textures.Length];
		var any = false;
		for ( var index = 0; index < types.Length; index++ )
		{
			var type = bound.Textures[index].AlphaType;
			if ( slotAlternatives != null && slotAlternatives[index] > type )
				type = slotAlternatives[index];
			any |= ( types[index] = type ) != TextureAlphaType.Opaque;
		}
		return any ? types : null;
	}

	private TextureAlphaType[]? slotAlternatives;

	/// <summary>
	/// The frames a texture-frame animation can show in a slot of the "Color" array. A model made afterwards classifies the slot by the most translucent of its frames, so the faces of a slot that
	/// is translucent in some frame are split off and drawn in the translucent pass whenever the slot's current texture is translucent (<see cref="CurrentSlotType"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x581790 state word] the original rebuilds the face's state word from the texture the slot shows now (0x590C40), so a slot whose frames differ in alpha type changes pass with its frame
	internal void AddSlotAlternatives( int slot, IEnumerable<Texture?> frames )
	{
		if ( !IsWorldShader || !_boundTextureArrays.TryGetValue( "Color", out var bound ) || (uint)slot >= (uint)bound.Textures.Length )
			return;
		slotAlternatives ??= new TextureAlphaType[bound.Textures.Length];
		foreach ( var frame in frames )
			if ( frame != null && frame.AlphaType > slotAlternatives[slot] )
				slotAlternatives[slot] = frame.AlphaType;
	}

	/// <summary>The alpha type of the texture a slot of the "Color" array shows now (opaque without the array).</summary>
	internal TextureAlphaType CurrentSlotType( int slot )
		=> _boundTextureArrays.TryGetValue( "Color", out var bound ) && (uint)slot < (uint)bound.Textures.Length ? bound.Textures[slot].AlphaType : TextureAlphaType.Opaque;

	/// <summary>
	/// The depth comparison of a material's pipeline: the original's, LESSEQUAL, so a fragment at exactly the stored depth passes and the later draw wins a tie (a floor drawn over the terrain it lies in); a
	/// material with neither the depth test nor the depth write has no comparison (always).
	/// </summary>
	// [BIN:TPW-EXE:0x0057360F depth compare] the Direct3D device set-up 0x00573080 enables the depth buffer (D3DRS_ZENABLE 7, when the device flag 0x10 is set) and sets D3DRS_ZFUNC (0x17) to 4, LESSEQUAL, once (0x00573601–0x00573614); Theme Park Inc's no-CD Game.exe sets it the same way, once in its device set-up (0x006D6574, ZENABLE guarded by [ebp+0x220] & 0x10) and once in the restore helper 0x006D5C00 (0x006D5C1E), always to 4; no other code sets ZFUNC in tp.exe: the per-face state decoder 0x00567620 writes ZENABLE (bit 0x8000 of the state word), ZWRITEENABLE (0x800), the alpha test and blend states and the texture stage states only, and a scan of every immediate 0x17 operand of the executable found no other render state write
	internal static ComparisonKind DepthComparison( MaterialFlags flags ) =>
		flags.HasFlag( MaterialFlags.DisableDepthTest | MaterialFlags.DisableDepthWrite ) ? ComparisonKind.Always : ComparisonKind.LessEqual;

	private static bool ShaderReadsPointLights( string shaderPath )
	{
		var file = System.IO.Path.GetFileName( shaderPath );
		return file.Equals( "test.shader", StringComparison.OrdinalIgnoreCase ) || file.Equals( "pool-deck.shader", StringComparison.OrdinalIgnoreCase )
			|| file.Equals( "pool-basin.shader", StringComparison.OrdinalIgnoreCase );
	}

	/// <summary>
	/// [EXT:point-lights] Writes the block and the first <paramref name="lightCount"/> lights of <paramref name="value"/> in one upload (the block's own bytes when there are no lights).
	/// </summary>
	internal void Set( string name, ref LitObjectUniformBuffer value, int lightCount )
	{
		value.Base.LightCount = lightCount;
		lastBlock = null;
		Render.UpdateUniform( ScratchBuffer, ref value, LitObjectUniformBuffer.UploadSize( lightCount ) );
		Bind( name, ScratchBuffer );
	}

	/// <summary>Binds a texture array with one shared sampler: wrapping by default; clamped for tiles whose edges must not bleed into each other.</summary>
	public void Set( string name, Texture[] texture, SamplerType sampler = SamplerType.AnisotropicWrap )
	{
		var names = new string[texture.Length];
		for ( int i = 0; i < texture.Length; i++ )
		{
			names[i] = name + i;
			Bind( names[i], texture[i].NativeTexture );
		}
		_boundTextureArrays[name] = (texture, names);
		Bind( "s_" + name, Samplers[(int)sampler] );
	}

	public void Set( string name, Texture texture )
	{
		_boundTextures[name] = texture;
		Bind( name, texture.NativeTexture );
		Bind( "s_" + name, Samplers[(int)texture.SamplerType] );
	}

	/// <summary>
	/// Replaces one element of a texture array bound with <see cref="Set(string,Texture[],SamplerType)"/>
	/// (an MD2 texture-frame animation swapping the frame a material slot shows). The next draw rebinds the
	/// changed element; nothing is reallocated while the frame stays the same.
	/// </summary>
	public void SetTextureArrayElement( string name, int index, Texture texture )
	{
		if ( !_boundTextureArrays.TryGetValue( name, out var bound ) )
			throw new ArgumentException( $"No texture array '{name}' was bound.", nameof( name ) );
		if ( (uint)index >= (uint)bound.Textures.Length )
			throw new ArgumentOutOfRangeException( nameof( index ) );
		if ( ReferenceEquals( bound.Textures[index], texture ) )
			return;
		bound.Textures[index] = texture;
		Bind( bound.Names[index], texture.NativeTexture );
	}

	private void Bind( string name, BindableResource resource )
	{
		if ( _boundResources.TryGetValue( name, out var previous ) && ReferenceEquals( previous, resource ) )
			return;
		_boundResources[name] = resource;
		ClearResourceSets();
	}

	internal ResourceLayout[] CreateResourceLayouts()
	{
		return Shader.ResourceLayouts.Select( x => Device.ResourceFactory.CreateResourceLayout( x ) ).ToArray();
	}

	/// <summary>Bindings are reused while their native identities and shader layouts stay unchanged.</summary>
	internal ResourceSet[] GetResourceSets()
	{
		foreach ( var (name, texture) in _boundTextures )
			Bind( name, texture.NativeTexture );
		foreach ( var (_, (textures, names)) in _boundTextureArrays )
			for ( var i = 0; i < textures.Length; i++ )
				Bind( names[i], textures[i].NativeTexture );
		if ( resourceSets != null )
			return resourceSets;

		using var mark = FrameMarks.Measure( "draw.materialResourceSets" );
		List<ResourceSetDescription> resourceSetDescriptions = new();

		for ( int i = 0; i < Shader.ResourceLayouts.Length; i++ )
		{
			ResourceLayoutDescription resourceLayout = Shader.ResourceLayouts[i];
			var sortedBoundResources = new List<BindableResource>();

			foreach ( var resource in resourceLayout.Elements )
			{
				if ( _boundResources.TryGetValue( resource.Name, out var boundResource ) )
				{
					sortedBoundResources.Add( boundResource );
				}
				else
				{
					throw new Exception( $"{resource.Name} wasn't bound at draw time!" );
				}
			}

			var resourceSetDescription = new ResourceSetDescription()
			{
				Layout = _resourceLayouts[i],
				BoundResources = [.. sortedBoundResources]
			};

			resourceSetDescriptions.Add( resourceSetDescription );
		}

		return resourceSets = resourceSetDescriptions.Select( x => Device.ResourceFactory.CreateResourceSet( x ) ).ToArray();
	}

	/// <summary>A retiring model can clear bindings without releasing a material shared by other models.</summary>
	internal void ClearResourceSets()
	{
		var old = resourceSets;
		resourceSets = null;
		if ( old != null )
			Render.ScheduleDelete( () => DestroyResourceSets( old ) );
	}

	private static void DestroyResourceSets( ResourceSet[] resourceSets )
	{
		foreach ( var item in resourceSets )
		{
			item.Dispose();
		}
	}

	private static readonly BlendStateDescription OneOneBlendState = new( RgbaFloat.Clear,
		new BlendAttachmentDescription( true, BlendFactor.One, BlendFactor.One, BlendFunction.Add, BlendFactor.One, BlendFactor.One, BlendFunction.Add ) );

	private void SetupResources( MaterialFlags flags )
	{
		var vertexLayout = new VertexLayoutDescription( Vertex.VertexElementDescriptions );
		ClearResourceSets();
		var oldPipeline = Pipeline;
		var oldBuffer = ScratchBuffer;
		lastBlock = null;
		var oldLayouts = _resourceLayouts;
		if ( oldPipeline != null )
			Render.ScheduleDelete( () =>
			{
				oldPipeline.Dispose();
				oldBuffer.Dispose();
				foreach ( var layout in oldLayouts )
					layout.Dispose();
			} );

		//
		// Create resource layout - but only from what we're using/need
		//
		_resourceLayouts = CreateResourceLayouts();

		//
		// Create pipeline
		//
		var pipelineDescription = new GraphicsPipelineDescription()
		{
			BlendState = flags.HasFlag( MaterialFlags.OneOneBlend ) ? OneOneBlendState
				: flags.HasFlag( MaterialFlags.AdditiveBlend ) ? BlendStateDescription.SingleAdditiveBlend
				: flags.HasFlag( MaterialFlags.OpaqueBlend ) ? BlendStateDescription.SingleOverrideBlend : BlendStateDescription.SingleAlphaBlend,

			DepthStencilState = new DepthStencilStateDescription(
				!flags.HasFlag( MaterialFlags.DisableDepthTest ),
				!flags.HasFlag( MaterialFlags.DisableDepthWrite ),
				DepthComparison( flags )
			),

			RasterizerState = new RasterizerStateDescription(
				flags.HasFlag( MaterialFlags.DoubleSided ) ? FaceCullMode.None : FaceCullMode.Back,
				PolygonFillMode.Solid,
				FrontFace.Clockwise,
				true,
				false
			),

			PrimitiveTopology = PrimitiveTopology.TriangleList,
			ResourceBindingModel = ResourceBindingModel.Improved,
			ResourceLayouts = [.. _resourceLayouts],
			ShaderSet = new ShaderSetDescription( [vertexLayout], Shader.ShaderProgram ),
			Outputs = Render.MultisampledFramebuffer.OutputDescription
		};

		using ( LoadProfile.Measure( "pipeline" ) )
			Pipeline = Device.ResourceFactory.CreateGraphicsPipeline( pipelineDescription );
		LoadProfile.Count( "pipelines" );

		var bufferDescription = new BufferDescription( 16 * 128, BufferUsage.UniformBuffer | BufferUsage.Dynamic );
		ScratchBuffer = Device.ResourceFactory.CreateBuffer( bufferDescription );
		foreach ( var name in _boundResources.Where( pair => ReferenceEquals( pair.Value, oldBuffer ) ).Select( pair => pair.Key ).ToArray() )
			_boundResources[name] = ScratchBuffer;
	}
}

public class Material<T>( string shaderPath, MaterialFlags flags = MaterialFlags.None ) : Material( shaderPath, typeof( T ), flags );
