namespace Veldrid;

/// <summary>Veldrid enums translated to WebGL2 constants.</summary>
internal static class Formats
{
	public static uint SizeInBytes( VertexElementFormat format ) => format switch
	{
		VertexElementFormat.Float1 or VertexElementFormat.UInt1 or VertexElementFormat.Int1 => 4,
		VertexElementFormat.Float2 or VertexElementFormat.UInt2 or VertexElementFormat.Int2 => 8,
		VertexElementFormat.Float3 or VertexElementFormat.UInt3 or VertexElementFormat.Int3 => 12,
		VertexElementFormat.Float4 or VertexElementFormat.UInt4 or VertexElementFormat.Int4 => 16,
		VertexElementFormat.Byte2_Norm or VertexElementFormat.Byte2 or VertexElementFormat.SByte2_Norm or VertexElementFormat.SByte2 or VertexElementFormat.Half1 => 2,
		VertexElementFormat.Byte4_Norm or VertexElementFormat.Byte4 or VertexElementFormat.SByte4_Norm or VertexElementFormat.SByte4 => 4,
		VertexElementFormat.UShort2_Norm or VertexElementFormat.UShort2 or VertexElementFormat.Short2_Norm or VertexElementFormat.Short2 or VertexElementFormat.Half2 => 4,
		VertexElementFormat.UShort4_Norm or VertexElementFormat.UShort4 or VertexElementFormat.Short4_Norm or VertexElementFormat.Short4 or VertexElementFormat.Half4 => 8,
		_ => throw new VeldridException( $"Unknown vertex format {format}." )
	};

	/// <summary>Component count, GL type, normalised, and whether the shader input is an integer.</summary>
	public static (int Size, int Type, bool Normalized, bool Integer) Attribute( VertexElementFormat format ) => format switch
	{
		VertexElementFormat.Float1 => (1, Gl.Float, false, false),
		VertexElementFormat.Float2 => (2, Gl.Float, false, false),
		VertexElementFormat.Float3 => (3, Gl.Float, false, false),
		VertexElementFormat.Float4 => (4, Gl.Float, false, false),
		VertexElementFormat.Half1 => (1, Gl.HalfFloat, false, false),
		VertexElementFormat.Half2 => (2, Gl.HalfFloat, false, false),
		VertexElementFormat.Half4 => (4, Gl.HalfFloat, false, false),
		VertexElementFormat.Byte2_Norm => (2, Gl.UnsignedByte, true, false),
		VertexElementFormat.Byte4_Norm => (4, Gl.UnsignedByte, true, false),
		VertexElementFormat.SByte2_Norm => (2, Gl.Byte, true, false),
		VertexElementFormat.SByte4_Norm => (4, Gl.Byte, true, false),
		VertexElementFormat.UShort2_Norm => (2, Gl.UnsignedShort, true, false),
		VertexElementFormat.UShort4_Norm => (4, Gl.UnsignedShort, true, false),
		VertexElementFormat.Short2_Norm => (2, Gl.Short, true, false),
		VertexElementFormat.Short4_Norm => (4, Gl.Short, true, false),
		VertexElementFormat.Byte2 => (2, Gl.UnsignedByte, false, true),
		VertexElementFormat.Byte4 => (4, Gl.UnsignedByte, false, true),
		VertexElementFormat.SByte2 => (2, Gl.Byte, false, true),
		VertexElementFormat.SByte4 => (4, Gl.Byte, false, true),
		VertexElementFormat.UShort2 => (2, Gl.UnsignedShort, false, true),
		VertexElementFormat.UShort4 => (4, Gl.UnsignedShort, false, true),
		VertexElementFormat.Short2 => (2, Gl.Short, false, true),
		VertexElementFormat.Short4 => (4, Gl.Short, false, true),
		VertexElementFormat.UInt1 => (1, Gl.UnsignedInt, false, true),
		VertexElementFormat.UInt2 => (2, Gl.UnsignedInt, false, true),
		VertexElementFormat.UInt3 => (3, Gl.UnsignedInt, false, true),
		VertexElementFormat.UInt4 => (4, Gl.UnsignedInt, false, true),
		VertexElementFormat.Int1 => (1, Gl.Int, false, true),
		VertexElementFormat.Int2 => (2, Gl.Int, false, true),
		VertexElementFormat.Int3 => (3, Gl.Int, false, true),
		VertexElementFormat.Int4 => (4, Gl.Int, false, true),
		_ => throw new VeldridException( $"Unknown vertex format {format}." )
	};

	/// <summary>
	/// Internal format, upload format and type. WebGL2 has no BGRA8; OpenTPW only renders into BGRA
	/// targets and never uploads to them, so they are stored as RGBA (uploads are swizzled anyway).
	/// </summary>
	public static (int Internal, int Format, int Type, int BytesPerPixel) Texture( PixelFormat format ) => format switch
	{
		PixelFormat.R8_G8_B8_A8_UNorm or PixelFormat.B8_G8_R8_A8_UNorm => (Gl.Rgba8, Gl.Rgba, Gl.UnsignedByte, 4),
		PixelFormat.R8_UNorm => (Gl.R8, Gl.Red, Gl.UnsignedByte, 1),
		PixelFormat.R32_Float => (Gl.R32F, Gl.Red, Gl.Float, 4),
		PixelFormat.D24_UNorm_S8_UInt => (Gl.Depth24Stencil8, Gl.DepthStencil, Gl.UnsignedInt248, 4),
		PixelFormat.D32_Float_S8_UInt => (Gl.Depth32FStencil8, Gl.DepthStencil, Gl.Float32UnsignedInt248Rev, 8),
		_ => throw new VeldridException( $"The browser renderer does not support {format}." )
	};

	public static bool IsDepth( PixelFormat format ) => format is PixelFormat.D24_UNorm_S8_UInt or PixelFormat.D32_Float_S8_UInt;

	public static int Comparison( ComparisonKind kind ) => 0x0200 + (int)kind;

	public static int Topology( PrimitiveTopology topology ) => topology switch
	{
		PrimitiveTopology.TriangleList => 0x0004,
		PrimitiveTopology.TriangleStrip => 0x0005,
		PrimitiveTopology.LineList => 0x0001,
		PrimitiveTopology.LineStrip => 0x0003,
		_ => 0x0000
	};

	public static int Blend( BlendFactor factor ) => factor switch
	{
		BlendFactor.Zero => 0,
		BlendFactor.One => 1,
		BlendFactor.SourceAlpha => 0x0302,
		BlendFactor.InverseSourceAlpha => 0x0303,
		BlendFactor.DestinationAlpha => 0x0304,
		BlendFactor.InverseDestinationAlpha => 0x0305,
		BlendFactor.SourceColor => 0x0300,
		BlendFactor.InverseSourceColor => 0x0301,
		BlendFactor.DestinationColor => 0x0306,
		BlendFactor.InverseDestinationColor => 0x0307,
		BlendFactor.BlendFactor => 0x8001,
		_ => 0x8002
	};

	public static int Blend( BlendFunction function ) => function switch
	{
		BlendFunction.Add => 0x8006,
		BlendFunction.Subtract => 0x800A,
		BlendFunction.ReverseSubtract => 0x800B,
		BlendFunction.Minimum => 0x8007,
		_ => 0x8008
	};

	public static (int Min, int Mag) Filter( SamplerFilter filter ) => filter switch
	{
		SamplerFilter.MinPoint_MagPoint_MipPoint => (0x2700, 0x2600),
		SamplerFilter.MinPoint_MagPoint_MipLinear => (0x2702, 0x2600),
		SamplerFilter.MinPoint_MagLinear_MipPoint => (0x2700, 0x2601),
		SamplerFilter.MinPoint_MagLinear_MipLinear => (0x2702, 0x2601),
		SamplerFilter.MinLinear_MagPoint_MipPoint => (0x2701, 0x2600),
		SamplerFilter.MinLinear_MagPoint_MipLinear => (0x2703, 0x2600),
		SamplerFilter.MinLinear_MagLinear_MipPoint => (0x2701, 0x2601),
		_ => (0x2703, 0x2601)
	};

	public static int Address( SamplerAddressMode mode ) => mode switch
	{
		SamplerAddressMode.Wrap => 0x2901,
		SamplerAddressMode.Mirror => 0x8370,
		_ => 0x812F
	};
}
