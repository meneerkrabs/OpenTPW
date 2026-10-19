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

	[TestMethod]
	public void AtlasSlotsCoverEveryPaddedAliasForOneThroughEightLoadedTextures()
	{
		for ( var count = 1; count <= 8; count++ )
		{
			var slots = FrontEndSmokeTest.StaffAtlasSlots( count );
			CollectionAssert.AreEqual( Enumerable.Range( 0, 8 ).Select( index => Math.Min( index, count - 1 ) ).ToArray(), slots );
			for ( var selected = 0; selected < count; selected++ )
				Assert.AreEqual( selected == count - 1 ? 9 - count : 1, slots.Count( index => index == selected ) );
			slots[0] = 99;
			Assert.AreEqual( 0, FrontEndSmokeTest.StaffAtlasSlots( count )[0], "Each binding plan is an independent complete array." );
		}
		foreach ( var count in new[] { -1, 0, 9 } )
			Assert.ThrowsException<InvalidOperationException>( () => FrontEndSmokeTest.StaffAtlasSlots( count ) );
	}

	[TestMethod]
	public void TargetedReloadRequiresOneNormalizedLoadedPackKey()
	{
		var keys = new[] { "/ESPRITES/STAFF/BANK.ATLAS", "esprites/staff/other.atlas" };
		Assert.AreEqual( "esprites/staff/bank.atlas.png", FrontEndSmokeTest.StaffUniqueAtlasKey( keys, 0 ) );
		Assert.ThrowsException<InvalidOperationException>( () => FrontEndSmokeTest.StaffUniqueAtlasKey(
			new[] { keys[0], @"esprites\staff\bank.atlas" }, 0 ) );
		Assert.ThrowsException<InvalidOperationException>( () => FrontEndSmokeTest.StaffUniqueAtlasKey( keys, -1 ) );
		Assert.ThrowsException<InvalidOperationException>( () => FrontEndSmokeTest.StaffUniqueAtlasKey( keys, 2 ) );
	}

	[TestMethod]
	public void UnavailableEvidenceKeepsThePreviousValidRoiWithoutResolvingAMissingFrame()
	{
		var saved = new UiRect( 200, 510, 40, 70 );
		var actual = FrontEndSmokeTest.StaffEvidenceRect( saved, () => throw new InvalidOperationException( "No drawable frame during resource loss." ) );
		Assert.AreEqual( saved, actual );
		Assert.AreEqual( saved, FrontEndSmokeTest.StaffEvidenceRect( null, () => saved ) );
		var before = new byte[4 * 4 * 4]; before[(2 * 4 + 2) * 4] = 255;
		var unavailable = new byte[before.Length]; var recovered = (byte[])before.Clone();
		var roi = new UiRect( 2, 2, 1, 1 );
		Assert.AreEqual( 1, FrontEndSmokeTest.StaffPixelChanges( (before, 4, 4), (unavailable, 4, 4), roi ) );
		Assert.AreEqual( 0, FrontEndSmokeTest.StaffPixelChanges( (before, 4, 4), (recovered, 4, 4), roi ) );
	}

	[TestMethod]
	public void FaultHandleCleanupDisposesPartialAndAlreadyDisposedAllocationsOnlyOnce()
	{
		var native = new DisposalProbe(); var nativeDisposed = false; var missingViewDisposed = false;
		FrontEndSmokeTest.StaffDisposeOnce( null, ref missingViewDisposed );
		FrontEndSmokeTest.StaffDisposeOnce( native, ref nativeDisposed );
		FrontEndSmokeTest.StaffDisposeOnce( native, ref nativeDisposed );
		Assert.AreEqual( 1, native.Calls ); Assert.IsFalse( missingViewDisposed );
		var view = new DisposalProbe { Throw = true }; var viewDisposed = false;
		Assert.ThrowsException<InvalidOperationException>( () => FrontEndSmokeTest.StaffDisposeOnce( view, ref viewDisposed ) );
		FrontEndSmokeTest.StaffDisposeOnce( view, ref viewDisposed );
		Assert.AreEqual( 1, view.Calls, "Even a throwing native disposal must not decrement ownership twice during cleanup." );
	}

	[TestMethod]
	public void RealFrameEnvelopeRejectsOverlapThatTheOldPixelHeuristicMissed()
	{
		var bank = new[] { new GuestAtlasFrame( 0, 0, 160, 180, -60, -160 ) };
		var envelope = FrontEndSmokeTest.StaffFrameEnvelope( bank );
		var unit = GuestRenderer.UnitsPerPixel;
		Assert.AreEqual( -100 * unit, envelope.Left ); Assert.AreEqual( 100 * unit, envelope.Right );
		var projection = System.Numerics.Matrix4x4.CreateScale( 2 / (1280 * unit), 2 / (800 * unit), 1 );
		var actual = FrontEndSmokeTest.StaffProjectFrameRect( new( 0, -250 * unit, 0 ), envelope,
			System.Numerics.Matrix4x4.Identity, projection, 1280, 800, pulled: false )!.Value;
		var oldGuess = new UiRect( 640 - 40, 650 - 100, 80, 108 );
		var window = new UiRect( 200, 80, 880, 440 );
		Assert.IsTrue( FrontEndSmokeTest.StaffRectsSeparate( oldGuess, window ) );
		Assert.IsTrue( actual.Width > oldGuess.Width && actual.Height > oldGuess.Height );
		Assert.IsFalse( FrontEndSmokeTest.StaffRectsSeparate( actual, window ), "The all-frame precheck rejects this cell before actual placement." );
		Assert.AreEqual( 490, actual.Y, 0.01 );
		Assert.ThrowsException<InvalidOperationException>( () => FrontEndSmokeTest.StaffFrameEnvelope( Array.Empty<GuestAtlasFrame>() ) );
	}

	[TestMethod]
	public void SharedWorldProjectionPreservesPulledPixelBoundsAndRejectsBehindOrNonfinitePoints()
	{
		var view = System.Numerics.Matrix4x4.Identity;
		var projection = System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView( MathF.PI / 2, 1.6f, 0.1f, 1000 );
		var extents = (-2f, 3f, 4f, -1f);
		var normal = FrontEndSmokeTest.StaffProjectFrameRect( new( 2, 3, -100 ), extents, view, projection, 1280, 800, false )!.Value;
		var pulled = FrontEndSmokeTest.StaffProjectFrameRect( new( 2, 3, -100 ), extents, view, projection, 1280, 800, true )!.Value;
		Assert.AreEqual( normal.X, pulled.X, 0.001 ); Assert.AreEqual( normal.Y, pulled.Y, 0.001 );
		Assert.AreEqual( normal.Width, pulled.Width, 0.001 ); Assert.AreEqual( normal.Height, pulled.Height, 0.001 );
		Assert.IsNull( FrontEndSmokeTest.StaffProjectFrameRect( new( 0, 0, 100 ), extents, view, projection, 1280, 800, true ) );
		Assert.IsNull( FrontEndSmokeTest.StaffProjectFrameRect( new( float.NaN, 0, -100 ), extents, view, projection, 1280, 800, false ) );
	}

	[TestMethod]
	public void FullViewportSearchKeepsAnIsolatedSideGapBesideTheEmployeeWindow()
	{
		var side = new UiRect( 1050, 503, 169, 194 );
		var window = new UiRect( 277.5f, 67.7f, 725, 401.6f );
		var speeds = new UiRect( 1012, 744, 268, 56 );
		Assert.IsTrue( side.X >= 0 && side.Right <= 1280 && side.Y >= 0 && side.Bottom <= 800 );
		Assert.IsTrue( side.Center.X > 1280 * 0.8f, "The former central-band heuristic would wrongly exclude this isolated side gap." );
		Assert.IsTrue( FrontEndSmokeTest.StaffRectsSeparate( side, window ) );
		Assert.IsTrue( FrontEndSmokeTest.StaffRectsSeparate( side, speeds ) );
	}

	private sealed class DisposalProbe : IDisposable
	{
		public int Calls; public bool Throw;
		public void Dispose() { Calls++; if ( Throw ) throw new InvalidOperationException(); }
	}

}
