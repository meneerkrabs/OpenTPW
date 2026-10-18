using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Rotation.LookAt for a view straight opposite to Forward: a unit half turn, not an unnormalised (axis, angle) quaternion.</summary>
[TestClass]
public class RotationLookAtTests
{
	[TestMethod]
	public void LookingOppositeToForwardIsAUnitHalfTurn()
	{
		var rotation = Rotation.LookAt( -Vector3.Forward );
		Assert.AreEqual( 1f, Rotation.Dot( rotation, rotation ), 1e-4f );
		var turned = rotation.Forward;
		Assert.AreEqual( -1f, Vector3.Dot( turned, Vector3.Forward ), 1e-3f );
	}
}
