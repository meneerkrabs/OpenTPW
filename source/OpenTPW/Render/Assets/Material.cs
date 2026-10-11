using System.Diagnostics;
using Veldrid;

namespace OpenTPW;

[Flags]
public enum MaterialFlags
{
	None,

	DisableDepthTest,
	DisableDepthWrite,

	DisableDepth = DisableDepthTest | DisableDepthWrite
}

public partial class Material : Asset
{
	public Shader Shader { get; set; }

	public Type UniformBufferType { get; } = typeof( ObjectUniformBuffer );
	public Pipeline Pipeline { get; private set; } = null!;

	private Dictionary<string, BindableResource> _boundResources = new();

	// [EXT:texture-pack] Textures can swap their GPU texture in place (a texture pack switch); the binding follows the Texture object.
	private readonly Dictionary<string, Texture> _boundTextures = new();
	private readonly Dictionary<string, Texture[]> _boundTextureArrays = new();

	private ResourceLayout[] _resourceLayouts;

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
		// [APPROX:COMPAT-010] Veldrid sampler modes stand in for the original Direct3D filter states — evidence needed: binary render-state setup or captures.
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

	private void ClearBoundResources()
	{
		return;

		if ( _boundResources.Count > 0 )
		{
			_boundResources.Clear();
		}
	}

	public void Set<T>( string name, T obj ) where T : unmanaged
	{
		Render.ImmediateSubmit( cmd =>
		{
			cmd.UpdateBuffer( ScratchBuffer, 0, [obj] );
			_boundResources[name] = ScratchBuffer;
		} );

		Render.ScheduleDelete( ClearBoundResources );
	}

	/// <summary>Binds a texture array with one shared sampler: wrapping by default; clamped for tiles whose edges must not bleed into each other.</summary>
	public void Set( string name, Texture[] texture, SamplerType sampler = SamplerType.AnisotropicWrap )
	{
		_boundTextureArrays[name] = texture;
		for ( int i = 0; i < texture.Length; i++ )
		{
			_boundResources[name + $"{i}"] = texture[i].NativeTexture;
		}

		_boundResources["s_" + name] = Samplers[(int)sampler];

		Render.ScheduleDelete( ClearBoundResources );
	}

	public void Set( string name, Texture texture )
	{
		_boundTextures[name] = texture;
		_boundResources[name] = texture.NativeTexture;
		_boundResources["s_" + name] = Samplers[(int)texture.SamplerType];

		Render.ScheduleDelete( ClearBoundResources );
	}

	internal ResourceLayout[] CreateResourceLayouts()
	{
		return Shader.ResourceLayouts.Select( x => Device.ResourceFactory.CreateResourceLayout( x ) ).ToArray();
	}

	internal ResourceSet[] CreateResourceSets()
	{
		Debug.Assert( _resourceLayouts != null );

		foreach ( var (name, texture) in _boundTextures )
			_boundResources[name] = texture.NativeTexture;
		foreach ( var (name, textures) in _boundTextureArrays )
			for ( var i = 0; i < textures.Length; i++ )
				_boundResources[name + $"{i}"] = textures[i].NativeTexture;

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

		return resourceSetDescriptions.Select( x => Device.ResourceFactory.CreateResourceSet( x ) ).ToArray();
	}

	internal void CreateEphemeralResourceSet( out ResourceSet[] resourceSets )
	{
		// Create a set
		var newResourceSets = CreateResourceSets();
		resourceSets = newResourceSets;

		// Mark set for death
		Render.ScheduleDelete( () => DestroyResourceSets( newResourceSets ) );
	}

	private static void DestroyResourceSets( ResourceSet[] resourceSets )
	{
		if ( resourceSets == null )
		{
			Log.Warning( $"Resource sets were marked for death, but are already dead - we can't kill what's already dead!" );
			return;
		}

		foreach ( var item in resourceSets )
		{
			item.Dispose();
		}
	}

	private void SetupResources( MaterialFlags flags )
	{
		var vertexLayout = new VertexLayoutDescription( Vertex.VertexElementDescriptions );

		//
		// Create resource layout - but only from what we're using/need
		//
		_resourceLayouts ??= CreateResourceLayouts();

		//
		// Create pipeline
		//
		var pipelineDescription = new GraphicsPipelineDescription()
		{
			BlendState = BlendStateDescription.SingleAlphaBlend,

			DepthStencilState = new DepthStencilStateDescription(
				!flags.HasFlag( MaterialFlags.DisableDepthTest ),
				!flags.HasFlag( MaterialFlags.DisableDepthWrite ),
				flags.HasFlag( MaterialFlags.DisableDepthTest | MaterialFlags.DisableDepthWrite ) ? ComparisonKind.Always : ComparisonKind.Less
			),

			RasterizerState = new RasterizerStateDescription(
				FaceCullMode.Back,
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
	}
}

public class Material<T>( string shaderPath, MaterialFlags flags = MaterialFlags.None ) : Material( shaderPath, typeof( T ), flags );
