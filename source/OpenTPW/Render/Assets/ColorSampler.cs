using Veldrid;

namespace OpenTPW;

/// <summary>GPU-independent sampler descriptions for Material's RGBA textures.</summary>
internal static class ColorSampler
{
	internal static SamplerDescription Describe( SamplerType type, RenderQuality quality )
	{
		// World textures follow the graphics preset ([DATA:low/med/high.sam:TEXTUREFILTERING, MIPMAP]); see docs/COMPATIBILITY.md.
		// [EXT:texture-filter-samplers] the Veldrid sampler modes (point, linear with point mip, linear with linear mip; anisotropy) are an OpenTPW design choice for the graphics presets
		// [APPROX:COMPAT-010] tp.exe sets stage 0 from the state word (0x567620: 0x20000 MAGFILTER point, 0x40000 MIPFILTER none, else the device's mip mode; 0x400000 wrap with linear MINFILTER, else clamp with point MINFILTER) and its option apply 0x44D8F0 sets 0x20000 for [0x785898] = 0 and 0x40000 for 1 — by the option table's position [0x785898] is the tenth option (SPRITESHADOW) and TEXTUREFILTERING is [0x785878] (bits 0x18 of DAT_008BD550), so which option the engine's filter bits belong to and how the engine flags reach each state word are not read; the 0x400000 address mode is applied per MD2 slot (TextureSlotAddressing), but a clamped slot keeps the preset's MINFILTER instead of point.
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

		return new SamplerDescription(
			samplerAddressMode,
			samplerAddressMode,
			samplerAddressMode,
			samplerFilter,
			null, // Color textures use ordinary sampler2D reads, never depth comparisons.
			world && quality.Filter == TextureFilterMode.Anisotropic ? (uint)quality.MaxAnisotropy : 0u,
			0,
			world && !quality.Mipmaps ? 0u : 10u,
			0,
			SamplerBorderColor.TransparentBlack
		);
	}
}
