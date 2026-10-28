using Veldrid;

namespace OpenTPW;

/// <summary>GPU-independent sampler descriptions for Material's RGBA textures.</summary>
internal static class ColorSampler
{
	internal static SamplerDescription Describe( SamplerType type, RenderQuality quality )
	{
		// World textures follow the graphics preset ([DATA:low/med/high.sam:TEXTUREFILTERING, MIPMAP]); see docs/COMPATIBILITY.md.
		// [EXT:texture-filter-samplers] the Veldrid sampler modes (point, linear with point mip, linear with linear mip; anisotropy) are an OpenTPW design choice for the graphics presets
		// [BIN:TPW-EXE:0x567620 stage 0 from the state word] 0x20000 MAGFILTER point (from mesh flag 0x8000000, 0x5817D8), 0x40000 MIPFILTER none (from texture flag 0x100000, 0x581841), 0x400000 wrap with a linear MINFILTER (from texture flags 0x30000), else clamp with a point MINFILTER; the preset filter is TEXTUREFILTERING [0x785878] (bits 0x18 of DAT_008BD550, 0x44D8F0, 0x566840), and [0x785898] is SPRITESHADOW, whose engine flags never reach a state word
		// [APPROX:COMPAT-010] the per-face bits 0x20000 and 0x40000 are not applied, and a clamped MD2 slot keeps the preset's MINFILTER instead of the original's point MINFILTER (the texture-slot wrap and clamp itself is applied per slot by TextureSlotAddressing) — evidence needed: the registration that binds option names to variables (the table position is inferred)
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
