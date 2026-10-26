using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The gate in front of the original's cell list (<see cref="TerrainListGate"/>, tp.exe 0x56F0A2, 0x576EC0, 0x5773BE to 0x5773FB): when the list is built and looked up.</summary>
[TestClass]
public class TerrainListGateTests
{
	private static Matrix4x4 Camera( float x ) => Matrix4x4.CreateTranslation( x, 0, 0 );

	[TestMethod]
	public void BeforeAnySceneChangeOnlyAMovedCameraBuildsTheListAndOnlyForThatFrame()
	{
		var gate = new TerrainListGate();
		Assert.IsFalse( gate.Frame( Camera( 5 ) ), "the very first frame compares nothing and sets nothing" );
		Assert.IsTrue( gate.Frame( Camera( 5 ) ), "the copy at 0x87A680 starts as the identity, which this camera is not" );
		Assert.IsFalse( gate.Frame( Camera( 5 ) ), "the same 64 bytes: no list, no lookup" );
		Assert.IsFalse( gate.Frame( Camera( 5 ) ) );
		Assert.IsTrue( gate.Frame( Camera( 6 ) ), "a moved camera" );
		Assert.IsFalse( gate.Frame( Camera( 6 ) ), "the flag the compare set lasted that frame only (0x577326 restores the frame's start flags)" );
		Assert.IsFalse( gate.Persistent );
	}

	[TestMethod]
	public void ASceneChangeStaysSetSoEveryFrameBuildsTheListFromThenOn()
	{
		var gate = new TerrainListGate();
		gate.Frame( Camera( 1 ) );
		gate.Frame( Camera( 1 ) );
		Assert.IsFalse( gate.Frame( Camera( 1 ) ) );
		gate.MarkSceneChanged();
		for ( var frame = 0; frame < 5; frame++ )
			Assert.IsTrue( gate.Frame( Camera( 1 ) ), $"frame {frame}: set before the frame, so part of its start flags" );
		Assert.IsTrue( gate.Persistent );
	}

	[TestMethod]
	public void BitFortyBuildsTheListInEveryFrame()
	{
		var gate = new TerrainListGate { Always = true };
		for ( var frame = 0; frame < 4; frame++ )
			Assert.IsTrue( gate.Frame( Camera( 2 ) ) );
	}

	[TestMethod]
	public void TheMatrixCompareIsByteWiseSoASignedZeroDiffers()
	{
		var gate = new TerrainListGate();
		var positive = Matrix4x4.Identity;
		var negative = Matrix4x4.Identity;
		negative.M12 = -0f;
		gate.Frame( positive );
		Assert.IsFalse( gate.Frame( positive ) );
		Assert.IsTrue( gate.Frame( negative ), "-0 and 0 are equal as numbers and differ as bytes" );
	}
}
