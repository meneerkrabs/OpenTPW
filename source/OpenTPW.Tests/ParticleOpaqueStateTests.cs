using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The state word of a particle's batch (PTCL-905): the decoder 0x567620's reading of the words 0x57C620 builds, and the draw kinds, depth rules and
/// alpha tests OpenTPW derives from them (<see cref="ParticleState"/>, <see cref="ParticleRenderer.KindOf"/>).
/// </summary>
[TestClass]
public class ParticleOpaqueStateTests
{
	private static ParticleSprite Sprite( int alpha, int flags = 0, bool screen = false, int frames = 0 ) => new()
	{
		Colour = ((uint)alpha << 24) | 0x123456u, RenderFlags = flags, ScreenSpace = screen, FrameCount = frames
	};

	private static int Kind( in ParticleSprite sprite ) => ParticleRenderer.KindOf( ParticleState.WordOf( sprite ), sprite.ScreenSpace );

	[TestMethod]
	public void AnUntexturedParticleIsOpaqueWhenItsAlphaByteIsZeroOrAtLeastF0AndBlendedOtherwise()
	{
		foreach ( var alpha in new[] { 0, 1, 15, 16, 239, 240, 255 } )
		{
			var opaque = alpha == 0 || alpha >= 240;
			// [BIN:TPW-EXE:0x57C6FA] and eax, 0xFF000000; je 0x1256; cmp eax, 0xF0000000; jb 0x556
			Assert.AreEqual( opaque ? 0x1256u : 0x556u, ParticleState.WordOf( Sprite( alpha ) ) );
			Assert.AreEqual( opaque ? 0x1A56u : 0xD56u, ParticleState.WordOf( Sprite( alpha, 0x4000 ) ), "flags bit 0x4000 adds the no-depth-write bit 0x800" );
			Assert.AreEqual( opaque ? ParticleState.Blend.Opaque : ParticleState.Blend.Alpha, ParticleState.BlendOf( ParticleState.WordOf( Sprite( alpha ) ) ) );
		}
	}

	[TestMethod]
	public void EverySpriteFrameIsTranslucentBecauseTheirSetsAreLoadedThroughTheTranslucentLoader()
	{
		// [BIN:TPW-EXE:0x5882E0 translucent frames] 0x541310 sets [0x877354] and every set it loads gets flag 0x200 for its frames: state 0x556, never 0x2756
		foreach ( var alpha in new[] { 0, 0x67, 0xF0, 255 } )
			Assert.AreEqual( 0x556u, ParticleState.WordOf( Sprite( alpha, frames: 8 ) ), $"alpha {alpha}" );
	}

	[TestMethod]
	public void AdditiveFlagsReplaceTheStateForTexturedAndUntexturedParticlesAlike()
	{
		foreach ( var alpha in new[] { 0, 240, 255 } )
		foreach ( var frames in new[] { 0, 16 } )
		foreach ( var noWrite in new[] { 0, 0x4000 } )
		{
			var extra = noWrite == 0 ? 0u : 0x800u;
			// [BIN:TPW-EXE:0x57C7C8] bit 4: 0x200522 plus 0x30 when bit 0x2000 is set
			Assert.AreEqual( 0x200552u | extra, ParticleState.WordOf( Sprite( alpha, 0x2004 | noWrite, frames: frames ) ) );
			Assert.AreEqual( 0x200522u | extra, ParticleState.WordOf( Sprite( alpha, 4 | noWrite, frames: frames ) ) );
			// bit 0x2000 alone changes nothing
			Assert.AreEqual( ParticleState.WordOf( Sprite( alpha, noWrite, frames: frames ) ), ParticleState.WordOf( Sprite( alpha, 0x2000 | noWrite, frames: frames ) ) );
		}
		Assert.AreEqual( 0x220522u, ParticleState.WordOf( Sprite( 255, 0x20004 ) ), "bit 0x20000 adds the point MAGFILTER bit" );
	}

	[TestMethod]
	public void TheStateWordsDecodeToTheirBlendsAlphaTestsAndDepthWrites()
	{
		var table = new (uint State, ParticleState.Blend Blend, float Reference, bool WritesDepth)[]
		{
			( 0x1256, ParticleState.Blend.Opaque, 0f, true ),
			( 0x1A56, ParticleState.Blend.Opaque, 0f, false ),
			( 0x556, ParticleState.Blend.Alpha, 0x10 / 255f, true ),
			( 0xD56, ParticleState.Blend.Alpha, 0x10 / 255f, false ),
			( 0x200552, ParticleState.Blend.SourceAlphaOne, 0x10 / 255f, true ),
			( 0x200522, ParticleState.Blend.OneOne, 0x10 / 255f, true ),
			( 0x200D22, ParticleState.Blend.OneOne, 0x10 / 255f, false ),
			( 0x2756, ParticleState.Blend.Alpha, 0xF0 / 255f, true ),
		};
		foreach ( var (state, blend, reference, writesDepth) in table )
		{
			Assert.AreEqual( blend, ParticleState.BlendOf( state ), $"{state:X} blend" );
			Assert.AreEqual( reference, ParticleState.AlphaReferenceOf( state ), 1e-6f, $"{state:X} alpha test" );
			Assert.AreEqual( writesDepth, ParticleState.WritesDepth( state ), $"{state:X} depth write" );
		}
	}

	[TestMethod]
	public void OnlyTheOpaqueStatesAreDrawnWhenTheyAreSubmitted()
	{
		// [BIN:TPW-EXE:0x565590] state & the renderer's mask 0x3000: 0x1256 is drawn at once, 0x556, 0x200552 and 0x200522 are kept for the sorted list
		foreach ( var state in new[] { 0x1256u, 0x1A56u } )
			Assert.IsTrue( ParticleState.DrawsImmediately( state ) );
		foreach ( var state in new[] { 0x556u, 0xD56u, 0x200552u, 0x200522u } )
			Assert.IsFalse( ParticleState.DrawsImmediately( state ) );
		foreach ( var alpha in new[] { 0, 0x80, 255 } )
			foreach ( var flags in new[] { 0, 4, 0x2000, 0x2004 } )
			{
				var sprite = Sprite( alpha, flags );
				Assert.AreEqual( ParticleState.DrawsImmediately( ParticleState.WordOf( sprite ) ), ParticleRenderer.IsImmediateKind( Kind( sprite ) ) );
			}
	}

	[TestMethod]
	public void TheKindsFollowTheBlendTheDepthWriteAndTheScreenSpace()
	{
		Assert.AreEqual( 0, Kind( Sprite( 255 ) ) );
		Assert.AreEqual( 4, Kind( Sprite( 255, 0x4000 ) ) );
		Assert.AreEqual( 1, Kind( Sprite( 128 ) ) );
		Assert.AreEqual( 1, Kind( Sprite( 255, frames: 1 ) ), "a sprite frame is alpha blended and tested whatever its colour's alpha" );
		Assert.AreEqual( 2, Kind( Sprite( 128, 0x2004 ) ) );
		Assert.AreEqual( 3, Kind( Sprite( 128, 4 ) ) );
		Assert.AreEqual( 9, Kind( Sprite( 128, frames: 1, screen: true ) ) );
		Assert.AreEqual( 10, Kind( Sprite( 128, 0x2004, screen: true ) ) );
		Assert.AreEqual( 11, Kind( Sprite( 128, 4, screen: true ) ) );
		for ( var kind = 0; kind < ParticleRenderer.KindCount; kind++ )
			Assert.AreEqual( (kind & 8) != 0, ParticleRenderer.IsScreenKind( kind ) );
	}

	[TestMethod]
	public void TheMaterialsKeepTheDepthTestAndOnlyTheNoWriteKindsAndScreenSpaceDisableIt()
	{
		Assert.AreEqual( MaterialFlags.OpaqueBlend | MaterialFlags.DoubleSided, ParticleRenderer.FlagsForKind( 0 ) );
		Assert.AreEqual( MaterialFlags.OpaqueBlend | MaterialFlags.DoubleSided | MaterialFlags.DisableDepthWrite, ParticleRenderer.FlagsForKind( 4 ) );
		Assert.AreEqual( MaterialFlags.DoubleSided, ParticleRenderer.FlagsForKind( 1 ), "the blended kinds write depth" );
		Assert.AreEqual( MaterialFlags.DoubleSided | MaterialFlags.AdditiveBlend, ParticleRenderer.FlagsForKind( 2 ) );
		Assert.AreEqual( MaterialFlags.DoubleSided | MaterialFlags.OneOneBlend | MaterialFlags.DisableDepthWrite, ParticleRenderer.FlagsForKind( 7 ) );
		Assert.AreEqual( MaterialFlags.DoubleSided | MaterialFlags.AdditiveBlend | MaterialFlags.DisableDepth, ParticleRenderer.FlagsForKind( 10 ) );
		Assert.AreEqual( 0, (int)(MaterialFlags.OpaqueBlend & (MaterialFlags.DisableDepth | MaterialFlags.AdditiveBlend | MaterialFlags.OneOneBlend | MaterialFlags.DoubleSided)) );
	}

	[TestMethod]
	public void OpaqueAlphaIsAHostCompositionAdaptationOnly()
	{
		foreach ( var alpha in new[] { 0, 239, 240, 255 } )
		for ( var kind = 0; kind < ParticleRenderer.KindCount; kind++ )
		{
			var colour = Sprite( alpha ).Colour;
			Assert.AreEqual( ParticleRenderer.BlendOfKind( kind ) == ParticleState.Blend.Opaque ? 0xFF123456u : colour, ParticleRenderer.ColourForKind( colour, kind ) );
		}
	}

	[TestMethod]
	public void CompleteStateRunsNeverReorderQuadsOfOtherKinds()
	{
		var runs = new List<(int Kind, int First, int Count)>();
		var kinds = new[] { 0, 0, 4, 2, 0, 9, 4 };
		for ( var index = 0; index < kinds.Length; index++ )
			ParticleRenderer.AddToRuns( runs, kinds[index], index * 6, 6 );
		CollectionAssert.AreEqual( new[] { (0, 0, 12), (4, 12, 6), (2, 18, 6), (0, 24, 6), (9, 30, 6), (4, 36, 6) }, runs );
	}

	[TestMethod]
	public void AParticleQuadJoinsTheTranslucentListFarToNearWithTheWorldsRecords()
	{
		var particles = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( ParticleRenderer ) ) as ParticleRenderer;
		WorldPass.Abort();
		WorldPass.Begin( System.Numerics.Matrix4x4.Identity );
		try
		{
			WorldPass.SubmitParticle( particles!, 0, 0.2f );
			WorldPass.SubmitParticle( particles!, 1, 0.9f );
			WorldPass.SubmitParticle( particles!, 2, 0f );
			WorldPass.SubmitParticle( particles!, 3, 0.9f );
			WorldPass.SortPending();
			CollectionAssert.AreEqual( new[] { 1, 3, 0, 2 }, WorldPass.Pending.Select( record => record.Quad ).ToArray(), "far to near, equal depths in submission order, a screen-space quad (key 0) last" );
			Assert.IsTrue( WorldPass.Pending.All( record => ReferenceEquals( record.Particles, particles ) && record.Model == null ) );
		}
		finally
		{
			WorldPass.Abort();
		}
		WorldPass.SubmitParticle( particles!, 0, 0.5f );
		Assert.AreEqual( 0, WorldPass.Pending.Count, "no pass is open: nothing is kept" );
	}

	[TestMethod]
	public void TheUniformsCarryTheAlphaTestAndTheFogOfWorldKindsOnly()
	{
		RenderFog.Reset();
		try
		{
			var eye = System.Numerics.Vector3.Zero;
			// the overview has no fog: no range, whatever the kind
			Assert.AreEqual( 0f, ParticleRenderer.UniformsFor( 1, eye )._padding2 );
			RenderFog.SetParkRange( 2 );
			var packed = RenderFog.PackedRange( CompatibilityRuntime.RenderQuality.ViewDistanceScale );
			Assert.IsTrue( packed > 0f );
			// [BIN:TPW-EXE:0x565F90] the fog bit joins every batch the scene submits while bit 0x2000 is set: the world's blends all get it, a screen-space quad (w = 1) is left out
			foreach ( var kind in new[] { 0, 1, 2, 3, 4, 5, 6, 7 } )
				Assert.AreEqual( packed, ParticleRenderer.UniformsFor( kind, eye )._padding2, $"kind {kind}" );
			foreach ( var kind in new[] { 8, 9, 10, 11, 12 } )
				Assert.AreEqual( 0f, ParticleRenderer.UniformsFor( kind, eye )._padding2, $"screen kind {kind}" );
			// the alpha test: none for the opaque state, 0x10 of 255 for the blended ones
			Assert.AreEqual( 0f, ParticleRenderer.UniformsFor( 0, eye ).AlphaReference );
			Assert.AreEqual( 0f, ParticleRenderer.UniformsFor( 8, eye ).AlphaReference );
			foreach ( var kind in new[] { 1, 2, 3, 5, 9, 10, 11 } )
				Assert.AreEqual( 0x10 / 255f, ParticleRenderer.UniformsFor( kind, eye ).AlphaReference, 1e-6f, $"kind {kind}" );
			Assert.AreEqual( System.Numerics.Matrix4x4.Identity, ParticleRenderer.UniformsFor( 9, eye ).g_mView, "a screen-space quad is drawn without the camera" );
		}
		finally
		{
			RenderFog.Reset();
		}
	}
}
