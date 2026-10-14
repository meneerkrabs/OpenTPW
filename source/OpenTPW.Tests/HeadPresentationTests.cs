using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>GPU-free head presentation: frame binning, roll, mirroring and hiding (docs/GUESTS.md, "Riders' heads").</summary>
[TestClass]
public class HeadPresentationTests
{
	private static readonly System.Numerics.Vector3 Anchor = new( 3, 5, 7 );

	private static Matrix4x4 At( Matrix4x4 rotation ) => rotation * Matrix4x4.CreateTranslation( Anchor );

	[TestMethod]
	public void CameraStraightOverheadIsFrameZeroAtTheAnchor()
	{
		var placement = HeadPresentation.Compute( At( Matrix4x4.Identity ), 0, Anchor + new System.Numerics.Vector3( 0, 50, 0 ) );
		Assert.IsTrue( placement.Visible );
		Assert.AreEqual( 0, placement.Frame );
		Assert.AreEqual( Anchor, placement.Anchor );
		Assert.AreEqual( 0f, placement.Roll, 1e-6f );
		Assert.AreEqual( 0.685f, placement.Scale );
	}

	[TestMethod]
	public void ElevationRowsStepThirtyDegreesAndSideRowsPickAnAzimuthColumn()
	{
		// 90 degrees from the pole is row 3; azimuth steps are 45 degrees.
		for ( var column = 0; column < 8; column++ )
		{
			var azimuth = column * MathF.PI / 4;
			var camera = Anchor + new System.Numerics.Vector3( MathF.Cos( azimuth ), 0, MathF.Sin( azimuth ) ) * 20;
			var placement = HeadPresentation.Compute( At( Matrix4x4.Identity ), 0, camera );
			Assert.AreEqual( 3, placement.Row );
			Assert.AreEqual( column, placement.Column );
			Assert.AreEqual( 24 + column, placement.Frame );
		}
		var rows = new[] { 0, 1, 2, 3, 4, 5, 6 };
		foreach ( var row in rows )
		{
			var elevation = row * MathF.PI / 6;
			var camera = Anchor + new System.Numerics.Vector3( MathF.Sin( elevation ), MathF.Cos( elevation ), 0 ) * 20;
			Assert.AreEqual( row, HeadPresentation.Compute( At( Matrix4x4.Identity ), 0, camera ).Row );
		}
	}

	[TestMethod]
	public void ColumnIsForcedToZeroAtTheFirstAndLastRows()
	{
		var below = HeadPresentation.Compute( At( Matrix4x4.Identity ), 0, Anchor + new System.Numerics.Vector3( 0.01f, -50, 0.01f ) );
		Assert.AreEqual( 6, below.Row );
		Assert.AreEqual( 0, below.Column );
		var nearPole = HeadPresentation.Compute( At( Matrix4x4.Identity ), 0, Anchor + new System.Numerics.Vector3( 0.5f, 50, 0.5f ) );
		Assert.AreEqual( 0, nearPole.Row );
		Assert.AreEqual( 0, nearPole.Column );
	}

	[TestMethod]
	public void NinetyDegreeBankRollsAQuarterTurn()
	{
		// The node's up axis leans from +Y to -X: atan2(-m10, m11) = atan2(1, 0).
		var bank = Matrix4x4.CreateRotationZ( MathF.PI / 2 );
		var placement = HeadPresentation.Compute( At( bank ), 0, Anchor + new System.Numerics.Vector3( 0, 0, 20 ) );
		Assert.AreEqual( 0.25f, placement.Roll, 1e-5f );
		var other = HeadPresentation.Compute( At( Matrix4x4.CreateRotationZ( -MathF.PI / 2 ) ), 0, Anchor + new System.Numerics.Vector3( 0, 0, 20 ) );
		Assert.AreEqual( -0.25f, other.Roll, 1e-5f );
	}

	[TestMethod]
	public void CameraIsBinnedInTheNodesOwnFrame()
	{
		// A node banked 90 degrees about Z sees a camera overhead in the world as on its side.
		var bank = Matrix4x4.CreateRotationZ( MathF.PI / 2 );
		var placement = HeadPresentation.Compute( At( bank ), 0, Anchor + new System.Numerics.Vector3( 0, 50, 0 ) );
		Assert.AreEqual( 3, placement.Row );
	}

	[TestMethod]
	public void UniformScaleOfTheMatrixDoesNotChangeTheBinning()
	{
		var scaled = Matrix4x4.CreateScale( 0.2f ) * Matrix4x4.CreateRotationZ( MathF.PI / 2 );
		var placement = HeadPresentation.Compute( At( scaled ), 0, Anchor + new System.Numerics.Vector3( 0, 0, 20 ) );
		Assert.AreEqual( 0.25f, placement.Roll, 1e-5f );
	}

	[TestMethod]
	public void MirroredNodeNegatesRowsZeroAndTwo()
	{
		var camera = Anchor + new System.Numerics.Vector3( 10, 10, 10 );
		var plain = HeadPresentation.Compute( At( Matrix4x4.Identity ), 0, camera );
		var mirrored = HeadPresentation.Compute( At( Matrix4x4.Identity ), HeadPresentation.MirroredFlag, camera );
		Assert.AreNotEqual( plain.Frame, mirrored.Frame );
		// Negating local X and Z turns azimuth by 180 degrees: 45 degrees becomes 225 degrees.
		Assert.AreEqual( plain.Row, mirrored.Row );
		Assert.AreEqual( (plain.Column + 4) % 8, mirrored.Column );
		Assert.AreEqual( Anchor, mirrored.Anchor );
	}

	[TestMethod]
	public void HiddenNodeDrawsNoHead()
	{
		var placement = HeadPresentation.Compute( At( Matrix4x4.Identity ), HeadPresentation.HiddenFlag | HeadPresentation.MirroredFlag, Anchor + new System.Numerics.Vector3( 0, 50, 0 ) );
		Assert.IsFalse( placement.Visible );
		Assert.AreEqual( -1, placement.Frame );
	}

	[TestMethod]
	public void SingularNodeFallsBackToFrameZero()
	{
		var placement = HeadPresentation.Compute( new Matrix4x4(), 0, new System.Numerics.Vector3( 1, 2, 3 ) );
		Assert.IsTrue( placement.Visible );
		Assert.AreEqual( 0, placement.Frame );
		Assert.AreEqual( 0f, placement.Roll );
	}
}
