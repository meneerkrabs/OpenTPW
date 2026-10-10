// The description structs of Veldrid 4.9.0 (MIT, Copyright (c) 2017 Eric Mellino and Veldrid
// contributors) with the same fields, constructors and presets, so game code compiles unchanged
// against the browser renderer (docs/WEB.md). Only what OpenTPW uses is declared.
using System.Numerics;

namespace Veldrid;

public struct RgbaFloat : IEquatable<RgbaFloat>
{
	private readonly Vector4 channels;
	public float R => channels.X;
	public float G => channels.Y;
	public float B => channels.Z;
	public float A => channels.W;

	public RgbaFloat( float r, float g, float b, float a ) => channels = new Vector4( r, g, b, a );
	public RgbaFloat( Vector4 channels ) => this.channels = channels;

	public static readonly RgbaFloat Black = new( 0, 0, 0, 1 );
	public static readonly RgbaFloat White = new( 1, 1, 1, 1 );
	public static readonly RgbaFloat Clear = new( 0, 0, 0, 0 );
	public Vector4 ToVector4() => channels;
	public bool Equals( RgbaFloat other ) => channels.Equals( other.channels );
	public override bool Equals( object? obj ) => obj is RgbaFloat other && Equals( other );
	public override int GetHashCode() => channels.GetHashCode();
	public override string ToString() => $"{{R:{R}, G:{G}, B:{B}, A:{A}}}";
	public static bool operator ==( RgbaFloat left, RgbaFloat right ) => left.Equals( right );
	public static bool operator !=( RgbaFloat left, RgbaFloat right ) => !left.Equals( right );
}

public struct RgbaByte : IEquatable<RgbaByte>
{
	public byte R;
	public byte G;
	public byte B;
	public byte A;

	public RgbaByte( byte r, byte g, byte b, byte a )
	{
		R = r;
		G = g;
		B = b;
		A = a;
	}

	public static readonly RgbaByte White = new( 255, 255, 255, 255 );
	public static readonly RgbaByte Black = new( 0, 0, 0, 255 );
	public static readonly RgbaByte Clear = new( 0, 0, 0, 0 );
	public bool Equals( RgbaByte other ) => R == other.R && G == other.G && B == other.B && A == other.A;
	public override bool Equals( object? obj ) => obj is RgbaByte other && Equals( other );
	public override int GetHashCode() => HashCode.Combine( R, G, B, A );
	public override string ToString() => $"{{R:{R}, G:{G}, B:{B}, A:{A}}}";
	public static bool operator ==( RgbaByte left, RgbaByte right ) => left.Equals( right );
	public static bool operator !=( RgbaByte left, RgbaByte right ) => !left.Equals( right );
}

public struct Viewport
{
	public float X;
	public float Y;
	public float Width;
	public float Height;
	public float MinDepth;
	public float MaxDepth;

	public Viewport( float x, float y, float width, float height, float minDepth, float maxDepth )
	{
		X = x;
		Y = y;
		Width = width;
		Height = height;
		MinDepth = minDepth;
		MaxDepth = maxDepth;
	}
}

public struct BufferDescription
{
	public uint SizeInBytes;
	public BufferUsage Usage;
	public uint StructureByteStride;
	public bool RawBuffer;

	public BufferDescription( uint sizeInBytes, BufferUsage usage ) : this( sizeInBytes, usage, 0, false ) { }
	public BufferDescription( uint sizeInBytes, BufferUsage usage, uint structureByteStride ) : this( sizeInBytes, usage, structureByteStride, false ) { }

	public BufferDescription( uint sizeInBytes, BufferUsage usage, uint structureByteStride, bool rawBuffer )
	{
		SizeInBytes = sizeInBytes;
		Usage = usage;
		StructureByteStride = structureByteStride;
		RawBuffer = rawBuffer;
	}
}

public struct TextureDescription
{
	public uint Width;
	public uint Height;
	public uint Depth;
	public uint MipLevels;
	public uint ArrayLayers;
	public PixelFormat Format;
	public TextureUsage Usage;
	public TextureType Type;
	public TextureSampleCount SampleCount;

	public TextureDescription( uint width, uint height, uint depth, uint mipLevels, uint arrayLayers, PixelFormat format, TextureUsage usage, TextureType type )
		: this( width, height, depth, mipLevels, arrayLayers, format, usage, type, TextureSampleCount.Count1 ) { }

	public TextureDescription( uint width, uint height, uint depth, uint mipLevels, uint arrayLayers, PixelFormat format, TextureUsage usage, TextureType type, TextureSampleCount sampleCount )
	{
		Width = width;
		Height = height;
		Depth = depth;
		MipLevels = mipLevels;
		ArrayLayers = arrayLayers;
		Format = format;
		Usage = usage;
		Type = type;
		SampleCount = sampleCount;
	}

	public static TextureDescription Texture2D( uint width, uint height, uint mipLevels, uint arrayLayers, PixelFormat format, TextureUsage usage )
		=> new( width, height, 1, mipLevels, arrayLayers, format, usage, TextureType.Texture2D, TextureSampleCount.Count1 );

	public static TextureDescription Texture2D( uint width, uint height, uint mipLevels, uint arrayLayers, PixelFormat format, TextureUsage usage, TextureSampleCount sampleCount )
		=> new( width, height, 1, mipLevels, arrayLayers, format, usage, TextureType.Texture2D, sampleCount );
}

public struct SamplerDescription
{
	public SamplerAddressMode AddressModeU;
	public SamplerAddressMode AddressModeV;
	public SamplerAddressMode AddressModeW;
	public SamplerFilter Filter;
	public ComparisonKind? ComparisonKind;
	public uint MaximumAnisotropy;
	public uint MinimumLod;
	public uint MaximumLod;
	public int LodBias;
	public SamplerBorderColor BorderColor;

	public SamplerDescription( SamplerAddressMode addressModeU, SamplerAddressMode addressModeV, SamplerAddressMode addressModeW, SamplerFilter filter,
		ComparisonKind? comparisonKind, uint maximumAnisotropy, uint minimumLod, uint maximumLod, int lodBias, SamplerBorderColor borderColor )
	{
		AddressModeU = addressModeU;
		AddressModeV = addressModeV;
		AddressModeW = addressModeW;
		Filter = filter;
		ComparisonKind = comparisonKind;
		MaximumAnisotropy = maximumAnisotropy;
		MinimumLod = minimumLod;
		MaximumLod = maximumLod;
		LodBias = lodBias;
		BorderColor = borderColor;
	}

	public static readonly SamplerDescription Point = new( SamplerAddressMode.Wrap, SamplerAddressMode.Wrap, SamplerAddressMode.Wrap, SamplerFilter.MinPoint_MagPoint_MipPoint, null, 0, 0, uint.MaxValue, 0, SamplerBorderColor.TransparentBlack );
	public static readonly SamplerDescription Linear = new( SamplerAddressMode.Wrap, SamplerAddressMode.Wrap, SamplerAddressMode.Wrap, SamplerFilter.MinLinear_MagLinear_MipLinear, null, 0, 0, uint.MaxValue, 0, SamplerBorderColor.TransparentBlack );
	public static readonly SamplerDescription Aniso4x = new( SamplerAddressMode.Wrap, SamplerAddressMode.Wrap, SamplerAddressMode.Wrap, SamplerFilter.Anisotropic, null, 4, 0, uint.MaxValue, 0, SamplerBorderColor.TransparentBlack );
}

public struct FramebufferAttachmentDescription
{
	public Texture Target;
	public uint ArrayLayer;
	public uint MipLevel;

	public FramebufferAttachmentDescription( Texture target, uint arrayLayer ) : this( target, arrayLayer, 0 ) { }

	public FramebufferAttachmentDescription( Texture target, uint arrayLayer, uint mipLevel )
	{
		Target = target;
		ArrayLayer = arrayLayer;
		MipLevel = mipLevel;
	}
}

public struct FramebufferDescription
{
	public FramebufferAttachmentDescription? DepthTarget;
	public FramebufferAttachmentDescription[] ColorTargets;

	public FramebufferDescription( Texture? depthTarget, params Texture[] colorTargets )
	{
		DepthTarget = depthTarget == null ? null : new FramebufferAttachmentDescription( depthTarget, 0 );
		ColorTargets = colorTargets.Select( target => new FramebufferAttachmentDescription( target, 0 ) ).ToArray();
	}

	public FramebufferDescription( FramebufferAttachmentDescription? depthTarget, params FramebufferAttachmentDescription[] colorTargets )
	{
		DepthTarget = depthTarget;
		ColorTargets = colorTargets;
	}
}

public readonly struct FramebufferAttachment
{
	public Texture Target { get; }
	public uint ArrayLayer { get; }
	public uint MipLevel { get; }

	public FramebufferAttachment( Texture target, uint arrayLayer ) : this( target, arrayLayer, 0 ) { }

	public FramebufferAttachment( Texture target, uint arrayLayer, uint mipLevel )
	{
		Target = target;
		ArrayLayer = arrayLayer;
		MipLevel = mipLevel;
	}
}

public struct OutputAttachmentDescription
{
	public PixelFormat Format;
	public OutputAttachmentDescription( PixelFormat format ) => Format = format;
}

public struct OutputDescription
{
	public OutputAttachmentDescription? DepthAttachment;
	public OutputAttachmentDescription[] ColorAttachments;
	public TextureSampleCount SampleCount;

	public OutputDescription( OutputAttachmentDescription? depthAttachment, params OutputAttachmentDescription[] colorAttachments )
		: this( depthAttachment, colorAttachments, TextureSampleCount.Count1 ) { }

	public OutputDescription( OutputAttachmentDescription? depthAttachment, OutputAttachmentDescription[] colorAttachments, TextureSampleCount sampleCount )
	{
		DepthAttachment = depthAttachment;
		ColorAttachments = colorAttachments;
		SampleCount = sampleCount;
	}
}

public struct BlendAttachmentDescription
{
	public bool BlendEnabled;
	public ColorWriteMask? ColorWriteMask;
	public BlendFactor SourceColorFactor;
	public BlendFactor DestinationColorFactor;
	public BlendFunction ColorFunction;
	public BlendFactor SourceAlphaFactor;
	public BlendFactor DestinationAlphaFactor;
	public BlendFunction AlphaFunction;

	public BlendAttachmentDescription( bool blendEnabled, BlendFactor sourceColorFactor, BlendFactor destinationColorFactor, BlendFunction colorFunction,
		BlendFactor sourceAlphaFactor, BlendFactor destinationAlphaFactor, BlendFunction alphaFunction )
	{
		BlendEnabled = blendEnabled;
		ColorWriteMask = null;
		SourceColorFactor = sourceColorFactor;
		DestinationColorFactor = destinationColorFactor;
		ColorFunction = colorFunction;
		SourceAlphaFactor = sourceAlphaFactor;
		DestinationAlphaFactor = destinationAlphaFactor;
		AlphaFunction = alphaFunction;
	}

	public BlendAttachmentDescription( bool blendEnabled, ColorWriteMask colorWriteMask, BlendFactor sourceColorFactor, BlendFactor destinationColorFactor, BlendFunction colorFunction,
		BlendFactor sourceAlphaFactor, BlendFactor destinationAlphaFactor, BlendFunction alphaFunction )
		: this( blendEnabled, sourceColorFactor, destinationColorFactor, colorFunction, sourceAlphaFactor, destinationAlphaFactor, alphaFunction )
		=> ColorWriteMask = colorWriteMask;

	public static readonly BlendAttachmentDescription OverrideBlend = new( true, BlendFactor.One, BlendFactor.Zero, BlendFunction.Add, BlendFactor.One, BlendFactor.Zero, BlendFunction.Add );
	public static readonly BlendAttachmentDescription AlphaBlend = new( true, BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha, BlendFunction.Add, BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha, BlendFunction.Add );
	public static readonly BlendAttachmentDescription AdditiveBlend = new( true, BlendFactor.SourceAlpha, BlendFactor.One, BlendFunction.Add, BlendFactor.SourceAlpha, BlendFactor.One, BlendFunction.Add );
	public static readonly BlendAttachmentDescription Disabled = new( false, BlendFactor.One, BlendFactor.Zero, BlendFunction.Add, BlendFactor.One, BlendFactor.Zero, BlendFunction.Add );
}

public struct BlendStateDescription
{
	public RgbaFloat BlendFactor;
	public BlendAttachmentDescription[] AttachmentStates;
	public bool AlphaToCoverageEnabled;

	public BlendStateDescription( RgbaFloat blendFactor, params BlendAttachmentDescription[] attachmentStates ) : this( blendFactor, false, attachmentStates ) { }

	public BlendStateDescription( RgbaFloat blendFactor, bool alphaToCoverageEnabled, params BlendAttachmentDescription[] attachmentStates )
	{
		BlendFactor = blendFactor;
		AlphaToCoverageEnabled = alphaToCoverageEnabled;
		AttachmentStates = attachmentStates;
	}

	public static readonly BlendStateDescription SingleOverrideBlend = new( RgbaFloat.Clear, BlendAttachmentDescription.OverrideBlend );
	public static readonly BlendStateDescription SingleAlphaBlend = new( RgbaFloat.Clear, BlendAttachmentDescription.AlphaBlend );
	public static readonly BlendStateDescription SingleAdditiveBlend = new( RgbaFloat.Clear, BlendAttachmentDescription.AdditiveBlend );
	public static readonly BlendStateDescription SingleDisabled = new( RgbaFloat.Clear, BlendAttachmentDescription.Disabled );
}

public struct StencilBehaviorDescription
{
	public StencilOperation Fail;
	public StencilOperation Pass;
	public StencilOperation DepthFail;
	public ComparisonKind Comparison;

	public StencilBehaviorDescription( StencilOperation fail, StencilOperation pass, StencilOperation depthFail, ComparisonKind comparison )
	{
		Fail = fail;
		Pass = pass;
		DepthFail = depthFail;
		Comparison = comparison;
	}
}

public struct DepthStencilStateDescription
{
	public bool DepthTestEnabled;
	public bool DepthWriteEnabled;
	public ComparisonKind DepthComparison;
	public bool StencilTestEnabled;
	public StencilBehaviorDescription StencilFront;
	public StencilBehaviorDescription StencilBack;
	public byte StencilReadMask;
	public byte StencilWriteMask;
	public uint StencilReference;

	public DepthStencilStateDescription( bool depthTestEnabled, bool depthWriteEnabled, ComparisonKind comparisonKind ) : this()
	{
		DepthTestEnabled = depthTestEnabled;
		DepthWriteEnabled = depthWriteEnabled;
		DepthComparison = comparisonKind;
	}

	public static readonly DepthStencilStateDescription DepthOnlyLessEqual = new( true, true, ComparisonKind.LessEqual );
	public static readonly DepthStencilStateDescription DepthOnlyLessEqualRead = new( true, false, ComparisonKind.LessEqual );
	public static readonly DepthStencilStateDescription Disabled = new( false, false, ComparisonKind.LessEqual );
}

public struct RasterizerStateDescription
{
	public FaceCullMode CullMode;
	public PolygonFillMode FillMode;
	public FrontFace FrontFace;
	public bool DepthClipEnabled;
	public bool ScissorTestEnabled;

	public RasterizerStateDescription( FaceCullMode cullMode, PolygonFillMode fillMode, FrontFace frontFace, bool depthClipEnabled, bool scissorTestEnabled )
	{
		CullMode = cullMode;
		FillMode = fillMode;
		FrontFace = frontFace;
		DepthClipEnabled = depthClipEnabled;
		ScissorTestEnabled = scissorTestEnabled;
	}

	public static readonly RasterizerStateDescription Default = new( FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.Clockwise, true, false );
	public static readonly RasterizerStateDescription CullNone = new( FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false );
}

public struct VertexElementDescription
{
	public string Name;
	public VertexElementSemantic Semantic;
	public VertexElementFormat Format;
	public uint Offset;

	public VertexElementDescription( string name, VertexElementSemantic semantic, VertexElementFormat format ) : this( name, semantic, format, 0 ) { }
	public VertexElementDescription( string name, VertexElementFormat format, VertexElementSemantic semantic ) : this( name, semantic, format, 0 ) { }

	public VertexElementDescription( string name, VertexElementSemantic semantic, VertexElementFormat format, uint offset )
	{
		Name = name;
		Semantic = semantic;
		Format = format;
		Offset = offset;
	}
}

public struct VertexLayoutDescription
{
	public uint Stride;
	public VertexElementDescription[] Elements;
	public uint InstanceStepRate;

	public VertexLayoutDescription( uint stride, params VertexElementDescription[] elements ) : this( stride, 0, elements ) { }

	public VertexLayoutDescription( uint stride, uint instanceStepRate, params VertexElementDescription[] elements )
	{
		Stride = stride;
		InstanceStepRate = instanceStepRate;
		Elements = elements;
	}

	public VertexLayoutDescription( params VertexElementDescription[] elements )
	{
		Elements = elements;
		InstanceStepRate = 0;
		uint stride = 0;
		foreach ( var element in elements )
			stride = (element.Offset != 0 ? element.Offset : stride) + Formats.SizeInBytes( element.Format );
		Stride = stride;
	}
}

public struct ShaderDescription
{
	public ShaderStages Stage;
	public byte[] ShaderBytes;
	public string EntryPoint;
	public bool Debug;

	public ShaderDescription( ShaderStages stage, byte[] shaderBytes, string entryPoint ) : this( stage, shaderBytes, entryPoint, false ) { }

	public ShaderDescription( ShaderStages stage, byte[] shaderBytes, string entryPoint, bool debug )
	{
		Stage = stage;
		ShaderBytes = shaderBytes;
		EntryPoint = entryPoint;
		Debug = debug;
	}
}

public struct ShaderSetDescription
{
	public VertexLayoutDescription[] VertexLayouts;
	public Shader[] Shaders;

	public ShaderSetDescription( VertexLayoutDescription[] vertexLayouts, Shader[] shaders )
	{
		VertexLayouts = vertexLayouts;
		Shaders = shaders;
	}
}

public struct ResourceLayoutElementDescription
{
	public string Name;
	public ResourceKind Kind;
	public ShaderStages Stages;
	public ResourceLayoutElementOptions Options;

	public ResourceLayoutElementDescription( string name, ResourceKind kind, ShaderStages stages ) : this( name, kind, stages, ResourceLayoutElementOptions.None ) { }

	public ResourceLayoutElementDescription( string name, ResourceKind kind, ShaderStages stages, ResourceLayoutElementOptions options )
	{
		Name = name;
		Kind = kind;
		Stages = stages;
		Options = options;
	}
}

public struct ResourceLayoutDescription
{
	public ResourceLayoutElementDescription[] Elements;
	public ResourceLayoutDescription( params ResourceLayoutElementDescription[] elements ) => Elements = elements;
}

public struct ResourceSetDescription
{
	public ResourceLayout Layout;
	public BindableResource[] BoundResources;

	public ResourceSetDescription( ResourceLayout layout, params BindableResource[] boundResources )
	{
		Layout = layout;
		BoundResources = boundResources;
	}
}

public struct GraphicsPipelineDescription
{
	public BlendStateDescription BlendState;
	public DepthStencilStateDescription DepthStencilState;
	public RasterizerStateDescription RasterizerState;
	public PrimitiveTopology PrimitiveTopology;
	public ShaderSetDescription ShaderSet;
	public ResourceLayout[] ResourceLayouts;
	public OutputDescription Outputs;
	public ResourceBindingModel? ResourceBindingModel;

	public GraphicsPipelineDescription( BlendStateDescription blendState, DepthStencilStateDescription depthStencilStateDescription, RasterizerStateDescription rasterizerState,
		PrimitiveTopology primitiveTopology, ShaderSetDescription shaderSet, ResourceLayout[] resourceLayouts, OutputDescription outputs )
	{
		BlendState = blendState;
		DepthStencilState = depthStencilStateDescription;
		RasterizerState = rasterizerState;
		PrimitiveTopology = primitiveTopology;
		ShaderSet = shaderSet;
		ResourceLayouts = resourceLayouts;
		Outputs = outputs;
		ResourceBindingModel = null;
	}

	public GraphicsPipelineDescription( BlendStateDescription blendState, DepthStencilStateDescription depthStencilStateDescription, RasterizerStateDescription rasterizerState,
		PrimitiveTopology primitiveTopology, ShaderSetDescription shaderSet, ResourceLayout[] resourceLayouts, OutputDescription outputs, ResourceBindingModel resourceBindingModel )
		: this( blendState, depthStencilStateDescription, rasterizerState, primitiveTopology, shaderSet, resourceLayouts, outputs )
		=> ResourceBindingModel = resourceBindingModel;

	public GraphicsPipelineDescription( BlendStateDescription blendState, DepthStencilStateDescription depthStencilStateDescription, RasterizerStateDescription rasterizerState,
		PrimitiveTopology primitiveTopology, ShaderSetDescription shaderSet, ResourceLayout resourceLayout, OutputDescription outputs )
		: this( blendState, depthStencilStateDescription, rasterizerState, primitiveTopology, shaderSet, [resourceLayout], outputs ) { }
}

public struct GraphicsDeviceOptions
{
	public bool Debug;
	public bool HasMainSwapchain;
	public PixelFormat? SwapchainDepthFormat;
	public bool SyncToVerticalBlank;
	public ResourceBindingModel ResourceBindingModel;
	public bool PreferDepthRangeZeroToOne;
	public bool PreferStandardClipSpaceYDirection;
	public bool SwapchainSrgbFormat;
}

public struct SwapchainDescription
{
	public SwapchainSource Source;
	public uint Width;
	public uint Height;
	public PixelFormat? DepthFormat;
	public bool SyncToVerticalBlank;
	public bool ColorSrgb;

	public SwapchainDescription( SwapchainSource source, uint width, uint height, PixelFormat? depthFormat, bool syncToVerticalBlank ) : this( source, width, height, depthFormat, syncToVerticalBlank, false ) { }

	public SwapchainDescription( SwapchainSource source, uint width, uint height, PixelFormat? depthFormat, bool syncToVerticalBlank, bool colorSrgb )
	{
		Source = source;
		Width = width;
		Height = height;
		DepthFormat = depthFormat;
		SyncToVerticalBlank = syncToVerticalBlank;
		ColorSrgb = colorSrgb;
	}
}

public struct PixelFormatProperties
{
	public uint MaxWidth;
	public uint MaxHeight;
	public uint MaxDepth;
	public uint MaxMipLevels;
	public uint MaxArrayLayers;
}

public struct MappedResource
{
	public MappableResource Resource;
	public MapMode Mode;
	public IntPtr Data;
	public uint SizeInBytes;
	public uint Subresource;
	public uint RowPitch;
	public uint DepthPitch;
}
