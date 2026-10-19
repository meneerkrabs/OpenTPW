using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>CPU checks of the native smoke's evidence helpers. These do not claim GPU execution.</summary>
[TestClass]
public class StaffOwnerGpuSmokeEvidenceTests
{
	[TestMethod]
	public void CompleteSmokeSnapshotIncludesAllSeventyWordsAndOwnsItsArrays()
	{
		var state = new StaffSpriteVmState(); StaffOwnerFixture.Stamp( state, 0x1000 ); state.Handle = 0x1001;
		var words = FrontEndSmokeTest.StaffWords( state );
		CollectionAssert.AreEqual( Enumerable.Range( 0, 70 ).Select( index => (uint)(0x1000 + index) ).ToArray(), words );
		state.Stack[19]++; state.DrawCache[16]++; state.Registers[18]++;
		Assert.AreEqual( 0x101bu, words[27] ); Assert.AreEqual( 0x1044u, words[68] ); Assert.AreEqual( 0x1033u, words[51] );
		Assert.AreEqual( 0, FrontEndSmokeTest.StaffWords( null ).Length );
	}

	[TestMethod]
	public void PixelEvidenceRequiresAnRgbChangeInsideTheExpectedRoi()
	{
		var visible = new byte[4 * 4 * 4]; var hidden = (byte[])visible.Clone();
		hidden[(1 * 4 + 1) * 4] = 42; hidden[(3 * 4 + 3) * 4] = 20;
		Assert.AreEqual( 1, FrontEndSmokeTest.StaffPixelChanges( (visible, 4, 4), (hidden, 4, 4), new UiRect( 1, 1, 1, 1 ) ) );
		Assert.AreEqual( 0, FrontEndSmokeTest.StaffPixelChanges( (visible, 4, 4), (hidden, 4, 4), new UiRect( 0, 0, 1, 1 ) ) );
		Assert.AreEqual( 2, FrontEndSmokeTest.StaffPixelChanges( (visible, 4, 4), (hidden, 4, 4), new UiRect( 0, 0, 4, 4 ) ) );
	}

	[TestMethod]
	public void PixelEvidenceClipsFractionalBoundsAndRejectsMismatchedViewports()
	{
		var pixels = new byte[2 * 2 * 4]; var changed = (byte[])pixels.Clone(); changed[0] = 1;
		Assert.AreEqual( 1, FrontEndSmokeTest.StaffPixelChanges( (pixels, 2, 2), (changed, 2, 2), new UiRect( -0.2f, -0.2f, 1, 1 ) ) );
		Assert.AreEqual( 0, FrontEndSmokeTest.StaffPixelChanges( (pixels, 2, 2), (pixels, 2, 2), new UiRect( 0, 0, 2, 2 ) ) );
		Assert.ThrowsException<InvalidOperationException>( () => FrontEndSmokeTest.StaffPixelChanges( (pixels, 2, 2), (changed, 1, 4), new UiRect( 0, 0, 1, 1 ) ) );
	}
	[TestMethod]
	public void PlacementAndFinalIsolationRejectOcclusionAndKeepTwoPixelClearance()
	{
		var target = new UiRect( 100, 500, 40, 80 );
		Assert.IsFalse( FrontEndSmokeTest.StaffRectsSeparate( target, new UiRect( 0, 0, 1024, 512 ) ) );
		Assert.IsFalse( FrontEndSmokeTest.StaffRectsSeparate( target, new UiRect( 90, 580, 60, 10 ) ) );
		Assert.IsFalse( FrontEndSmokeTest.StaffRectsSeparate( target, new UiRect( 142, 510, 20, 20 ) ) );
		Assert.IsTrue( FrontEndSmokeTest.StaffRectsSeparate( target, new UiRect( 143, 510, 20, 20 ) ) );
		Assert.IsTrue( FrontEndSmokeTest.StaffRectsSeparate( target, new UiRect( 0, 0, 1024, 497 ) ) );
		Assert.IsTrue( FrontEndSmokeTest.StaffRectsSeparate( target, new UiRect( 105, 510, 0, 10 ) ) );
	}

}
