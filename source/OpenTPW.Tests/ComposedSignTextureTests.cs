using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The composed sign faces are alpha blended, not opaque (tp.exe 0x575C60: the slot entries the sign builder marks with bit 0x80, 0x467D09 and 0x467D69, get a texture built from the composed record
/// with the entry flags 0x1000240, alpha type 0x200, state 0x556): the runtime textures carry <see cref="TextureAlphaType.Translucent"/>, so the world pass alpha tests them at 0x10/255 and draws
/// them after the opaque list.
/// </summary>
[TestClass, DoNotParallelize]
public class ComposedSignTextureTests
{
	[TestMethod]
	public void TheComposedSignHalvesAreTranslucentTextures()
	{
		// the texture object alone: the GPU upload is not what carries the type
		var left = ((Texture)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( Texture ) )).AsComposedSign();
		Assert.AreEqual( TextureAlphaType.Translucent, left.AlphaType );
		Assert.IsTrue( WorldTranslucency.IsTranslucent( left.AlphaType ) );
		Assert.AreEqual( 0x556u, WorldTranslucency.StateWord( left.AlphaType ) );
		Assert.AreEqual( WorldTranslucency.AlphaTestLowFlag, WorldTranslucency.FlagFor( left.AlphaType ) );
	}
}
