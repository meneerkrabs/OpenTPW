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
	OneOneBlend = 16
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

		All.Add( this );
		SetupResources( flags );
	}

	protected Material( string shaderPath, Type uniformBufferType, MaterialFlags flags = MaterialFlags.None )
	{
		Shader = new Shader( shaderPath );
		Shader.OnRecompile += () => SetupResources( flags );
		UniformBufferType = uniformBufferType;

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
		Action dispose = () =>
		{
			Pipeline?.Dispose();
			ScratchBuffer?.Dispose();
			foreach ( var layout in _resourceLayouts )
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
		// World textures follow the graphics preset ([DATA:low/med/high.sam:TEXTUREFILTERING, MIPMAP]); see docs/COMPATIBILITY.md.
		var quality = CompatibilityRuntime.RenderQuality;
		// [EXT:texture-filter-samplers] the Veldrid sampler modes (point, linear with point mip, linear with linear mip; anisotropy) are an OpenTPW design choice for the graphics presets
		// [APPROX:COMPAT-010] tp.exe sets stage 0 from the state word (0x567620: 0x20000 MAGFILTER point, 0x40000 MIPFILTER none, else the device's mip mode; 0x400000 wrap with linear MINFILTER, else clamp with point MINFILTER) and its option apply 0x44D8F0 sets 0x20000 for [0x785898] = 0 and 0x40000 for 1 — evidence needed: that [0x785898] is TEXTUREFILTERING and how the engine flags reach each state word.
		var world = type is SamplerType.Anisotropic or SamplerType.AnisotropicWrap or SamplerType.AnisotropicRepeat;
		var samplerFilter = type switch
		{
			_ when world => quality.Filter switch
			{
				TextureFilterMode.Point => SamplerFilter.MinPoint_MagPoint_MipPoint,
				TextureFilterMode.Bilinear => SamplerFilter.MinLinear_MagLinear_MipPoint,
				TextureFilterMode.Trilinear => SamplerFilter.MinLinear_MagLinear_MipLinear,
				_ => SamplerFilter.Anisotropic
			},
			SamplerType.Linear => SamplerFilter.MinLinear_MagLinear_MipLinear,
			SamplerType.Point => SamplerFilter.MinPoint_MagPoint_MipPoint,
			_ => throw new NotImplementedException()
		};

		var samplerAddressMode = type switch
		{
			SamplerType.Anisotropic or SamplerType.Linear or SamplerType.Point => SamplerAddressMode.Clamp,
			SamplerType.AnisotropicWrap => SamplerAddressMode.Wrap,
			SamplerType.AnisotropicRepeat => SamplerAddressMode.Mirror,
			_ => throw new NotImplementedException()
		};

		var samplerDescription = new SamplerDescription(
			samplerAddressMode,
			samplerAddressMode,
			samplerAddressMode,
			samplerFilter,
			ComparisonKind.Always,
			world && quality.Filter == TextureFilterMode.Anisotropic ? (uint)quality.MaxAnisotropy : 0u,
			0,
			world && !quality.Mipmaps ? 0u : 10u,
			0,
			SamplerBorderColor.TransparentBlack
		);

		return Device.ResourceFactory.CreateSampler( samplerDescription );
	}

	public void Set<T>( string name, T obj ) where T : unmanaged
	{
		Render.UpdateUniform( ScratchBuffer, obj );
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
				: flags.HasFlag( MaterialFlags.AdditiveBlend ) ? BlendStateDescription.SingleAdditiveBlend : BlendStateDescription.SingleAlphaBlend,

			DepthStencilState = new DepthStencilStateDescription(
				!flags.HasFlag( MaterialFlags.DisableDepthTest ),
				!flags.HasFlag( MaterialFlags.DisableDepthWrite ),
				flags.HasFlag( MaterialFlags.DisableDepthTest | MaterialFlags.DisableDepthWrite ) ? ComparisonKind.Always : ComparisonKind.Less
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

		Pipeline = Device.ResourceFactory.CreateGraphicsPipeline( pipelineDescription );

		var bufferDescription = new BufferDescription( 16 * 128, BufferUsage.UniformBuffer | BufferUsage.Dynamic );
		ScratchBuffer = Device.ResourceFactory.CreateBuffer( bufferDescription );
		foreach ( var name in _boundResources.Where( pair => ReferenceEquals( pair.Value, oldBuffer ) ).Select( pair => pair.Key ).ToArray() )
			_boundResources[name] = ScratchBuffer;
	}
}

public class Material<T>( string shaderPath, MaterialFlags flags = MaterialFlags.None ) : Material( shaderPath, typeof( T ), flags );
