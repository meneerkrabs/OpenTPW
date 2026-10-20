using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class TextureCreationFlagsTests
{
	[TestMethod]
	public void HostFlagsHaveIndependentBitsAndRetainOtherNumericValues()
	{
		Assert.AreEqual( 0, (int)TextureFlags.None );
		Assert.AreEqual( 1, (int)TextureFlags.PinkChromaKey );
		Assert.AreEqual( 2, (int)TextureFlags.PointFilter );
		Assert.AreEqual( 4, (int)TextureFlags.Repeat );
		Assert.AreEqual( 8, (int)TextureFlags.Wrap );
		var flags = Enum.GetValues<TextureFlags>().Where( flag => flag != TextureFlags.None ).ToArray();
		foreach ( var flag in flags )
		{
			Assert.AreEqual( 0, (int)flag & ((int)flag - 1), $"{flag} must be one bit" );
			foreach ( var other in flags.Where( other => other != flag ) )
				Assert.AreEqual( TextureFlags.None, flag & other );
		}
	}

	[TestMethod]
	public void WrappingDoesNotKeyOpaqueMagentaAndPointPlusKeyDoesNotWrap()
	{
		var wrapping = Pixels();
		Texture.PreprocessTextureData( ref wrapping, TextureFlags.Wrap );
		CollectionAssert.AreEqual( Pixels(), wrapping );
		Assert.AreEqual( SamplerType.AnisotropicWrap, Texture.SamplerForFlags( TextureFlags.Wrap ) );

		var keyedPoint = Pixels();
		var flags = TextureFlags.PinkChromaKey | TextureFlags.PointFilter;
		Texture.PreprocessTextureData( ref keyedPoint, flags );
		CollectionAssert.AreEqual( new byte[] { 255, 0, 255, 0, 254, 0, 255, 123 }, keyedPoint );
		Assert.AreEqual( SamplerType.Point, Texture.SamplerForFlags( flags ) );
	}

	[TestMethod]
	public void EveryCombinationKeepsExplicitKeyAndAddressingPrecedence()
	{
		for ( var pink = 0; pink <= 1; pink++ )
		for ( var point = 0; point <= 1; point++ )
		for ( var wrap = 0; wrap <= 1; wrap++ )
		for ( var repeat = 0; repeat <= 1; repeat++ )
		{
			var flags = (pink == 1 ? TextureFlags.PinkChromaKey : TextureFlags.None)
				| (point == 1 ? TextureFlags.PointFilter : TextureFlags.None)
				| (wrap == 1 ? TextureFlags.Wrap : TextureFlags.None)
				| (repeat == 1 ? TextureFlags.Repeat : TextureFlags.None);
			var data = Pixels();
			Texture.PreprocessTextureData( ref data, flags );
			CollectionAssert.AreEqual( new byte[] { 255, 0, 255, (byte)(pink == 1 ? 0 : 255), 254, 0, 255, 123 }, data, flags.ToString() );
			var sampler = repeat == 1 ? SamplerType.AnisotropicRepeat
				: wrap == 1 ? SamplerType.AnisotropicWrap : point == 1 ? SamplerType.Point : SamplerType.AnisotropicRepeat;
			Assert.AreEqual( sampler, Texture.SamplerForFlags( flags ), flags.ToString() );
		}
	}

	private static byte[] Pixels() => new byte[] { 255, 0, 255, 255, 254, 0, 255, 123 };
}
