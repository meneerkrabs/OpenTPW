using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>TOUR-008: the engine's rotation builder reads a 4096-entry sine table (tp.exe 0x004708D0, 0x0046F6F0) instead of computing sines.</summary>
[TestClass]
public class OriginalSineTableTests
{
	[TestMethod]
	public void TheTableHoldsTheSineOfEachStepAndWrapsLikeAMaskedIndex()
	{
		Assert.AreEqual( 0f, OriginalSineTable.Sin( 0 ) );
		Assert.AreEqual( 1f, OriginalSineTable.Sin( MathF.PI / 2 ), 1e-7f );
		Assert.AreEqual( 1f, OriginalSineTable.Cos( 0 ), 1e-7f, "a cosine is the sine of the angle plus a quarter turn" );
		Assert.AreEqual( -1f, OriginalSineTable.Cos( MathF.PI ), 1e-7f );
		for ( var step = -9000; step <= 9000; step += 7 )
		{
			var angle = step * (2 * MathF.PI / OriginalSineTable.Size);
			Assert.AreEqual( OriginalSineTable.Sin( angle ), OriginalSineTable.Sin( angle + 2 * MathF.PI ), 2e-3f, $"step {step} wraps with the turn" );
			Assert.AreEqual( MathF.Sin( angle ), OriginalSineTable.Sin( angle ), 1e-3f, $"step {step}" );
			Assert.AreEqual( MathF.Cos( angle ), OriginalSineTable.Cos( angle ), 1e-3f, $"step {step}" );
		}
	}

	[TestMethod]
	public void AnAngleBetweenTwoStepsTakesTheNearerOne()
	{
		var step = 2 * MathF.PI / OriginalSineTable.Size;
		Assert.AreEqual( OriginalSineTable.Sin( 100 * step ), OriginalSineTable.Sin( 100.4f * step ) );
		Assert.AreEqual( OriginalSineTable.Sin( 101 * step ), OriginalSineTable.Sin( 100.6f * step ) );
		Assert.AreNotEqual( MathF.Sin( 100.4f * step ), OriginalSineTable.Sin( 100.4f * step ), "the table quantises: it is not the exact sine" );
	}
}
