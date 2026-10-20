using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Veldrid;

namespace OpenTPW.Tests;

[TestClass]
public class ColorSamplerTests
{
	[TestMethod]
	public void EveryColorSamplerDisablesComparisonForEveryQualityMode()
	{
		foreach ( var type in Enum.GetValues<SamplerType>() )
		foreach ( var filter in Enum.GetValues<TextureFilterMode>() )
		foreach ( var mipmaps in new[] { false, true } )
		{
			var description = ColorSampler.Describe( type, new RenderQuality( filter, 8, mipmaps, 1f ) );
			Assert.IsNull( description.ComparisonKind, $"{type}/{filter}/mipmaps={mipmaps}: RGBA sampling must not compare depth" );
		}
	}

	[TestMethod]
	public void ColorSamplersPreserveAddressingFilteringAndLodPolicy()
	{
		foreach ( var type in Enum.GetValues<SamplerType>() )
		foreach ( var filter in Enum.GetValues<TextureFilterMode>() )
		foreach ( var mipmaps in new[] { false, true } )
		{
			var description = ColorSampler.Describe( type, new RenderQuality( filter, 8, mipmaps, 1f ) );
			var world = type is SamplerType.Anisotropic or SamplerType.AnisotropicWrap or SamplerType.AnisotropicRepeat;
			var address = type switch
			{
				SamplerType.AnisotropicWrap => SamplerAddressMode.Wrap,
				SamplerType.AnisotropicRepeat => SamplerAddressMode.Mirror,
				_ => SamplerAddressMode.Clamp
			};
			var expectedFilter = world ? filter switch
			{
				TextureFilterMode.Point => SamplerFilter.MinPoint_MagPoint_MipPoint,
				TextureFilterMode.Bilinear => SamplerFilter.MinLinear_MagLinear_MipPoint,
				TextureFilterMode.Trilinear => SamplerFilter.MinLinear_MagLinear_MipLinear,
				_ => SamplerFilter.Anisotropic
			} : type == SamplerType.Point ? SamplerFilter.MinPoint_MagPoint_MipPoint : SamplerFilter.MinLinear_MagLinear_MipLinear;
			Assert.AreEqual( address, description.AddressModeU );
			Assert.AreEqual( address, description.AddressModeV );
			Assert.AreEqual( address, description.AddressModeW );
			Assert.AreEqual( expectedFilter, description.Filter );
			Assert.AreEqual( world && filter == TextureFilterMode.Anisotropic ? 8u : 0u, description.MaximumAnisotropy );
			Assert.AreEqual( 0u, description.MinimumLod );
			Assert.AreEqual( world && !mipmaps ? 0u : 10u, description.MaximumLod );
			Assert.AreEqual( 0, description.LodBias );
			Assert.AreEqual( SamplerBorderColor.TransparentBlack, description.BorderColor );
		}
	}
}
