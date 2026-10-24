using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The original's translucent pass for world geometry: which textures are translucent (tp.exe 0x564C00, 0x5755F0, 0x590C40), the state words and which of them the batcher draws at once (0x581DB0, mask 0x3000),
/// the far-to-near sort of the deferred triangles (0x565490) and of the deferred records (0x5829E0), and the level's draw sequence that follows from them.
/// </summary>
[TestClass]
[DoNotParallelize]
public class WorldTranslucencyTests
{
	private static byte[] Header( byte flags, byte alpha, byte bitsPerPixel ) => [flags, alpha, bitsPerPixel, 0x02, 0, 0, 0, 0];

	[TestMethod]
	public void TheHeaderDecidesTheAlphaTypeLikeTheOriginalsLoader()
	{
		// 0x564C00 loads the alpha chunk only for 32 bits per pixel; 0x574D3A: 0x25 for that and 0x27 when the alpha byte is also non-zero; 0x5755F0: bit 0 is flag 0x100, bit 1 is 0x200
		Assert.AreEqual( TextureAlphaType.Opaque, TextureFile.ReadAlphaType( new MemoryStream( Header( 0x13, 0x00, 0x18 ) ) ) );
		Assert.AreEqual( TextureAlphaType.Opaque, TextureFile.ReadAlphaType( new MemoryStream( Header( 0x13, 0x01, 0x18 ) ) ), "an alpha byte without an alpha plane is nothing" );
		Assert.AreEqual( TextureAlphaType.Cutout, TextureFile.ReadAlphaType( new MemoryStream( Header( 0x13, 0x00, 0x20 ) ) ) );
		Assert.AreEqual( TextureAlphaType.Translucent, TextureFile.ReadAlphaType( new MemoryStream( Header( 0x13, 0x01, 0x20 ) ) ) );
		Assert.AreEqual( TextureAlphaType.Translucent, TextureFile.ReadAlphaType( new MemoryStream( Header( 0x12, 0x02, 0x20 ) ) ), "any non-zero alpha byte, as the loader tests it" );
		Assert.AreEqual( TextureAlphaType.Opaque, TextureFile.ReadAlphaType( new MemoryStream( Header( 0x14, 0x01, 0x20 ) ) ), "the loader rejects a first byte outside 0x12..0x13" );
		Assert.AreEqual( TextureAlphaType.Opaque, TextureFile.ReadAlphaType( new MemoryStream( [0x13, 0x01] ) ), "a truncated header" );
		Assert.AreEqual( TextureAlphaType.Opaque, TextureFile.ReadAlphaType( new MemoryStream() ) );
	}

	[TestMethod]
	public void OnlyTheTranslucentStateIsKeptForTheSortedPass()
	{
		Assert.AreEqual( 0x1256u, WorldTranslucency.StateWord( TextureAlphaType.Opaque ) );
		Assert.AreEqual( 0x2756u, WorldTranslucency.StateWord( TextureAlphaType.Cutout ) );
		Assert.AreEqual( 0x556u, WorldTranslucency.StateWord( TextureAlphaType.Translucent ) );
		// the renderer's mask is 0x3000 (0x5667D0): bit 0x1000 or 0x2000 is drawn at once, neither is deferred
		Assert.IsTrue( WorldTranslucency.DrawsImmediately( 0x1256 ) );
		Assert.IsTrue( WorldTranslucency.DrawsImmediately( 0x2756 ) );
		Assert.IsFalse( WorldTranslucency.DrawsImmediately( 0x556 ) );
		Assert.IsFalse( WorldTranslucency.DrawsImmediately( 0x200552 ), "the additive state is deferred as well" );
		foreach ( var type in Enum.GetValues<TextureAlphaType>() )
			Assert.AreEqual( !WorldTranslucency.DrawsImmediately( WorldTranslucency.StateWord( type ) ), WorldTranslucency.IsTranslucent( type ), type.ToString() );
	}

	private static Vertex Corner( int slot, float x, uint flags = 0 ) => new() { Position = new Vector3( x, 0, 0 ), TexIndex = slot, MatFlags = flags };

	[TestMethod]
	public void GeometryWithoutTranslucentTexturesIsReturnedUntouched()
	{
		var vertices = new[] { Corner( 0, 0 ), Corner( 0, 1 ), Corner( 1, 2 ) };
		var indices = new uint[] { 0, 1, 2 };
		var split = WorldTranslucency.SplitFaces( vertices, indices, _ => false );
		Assert.AreSame( vertices, split.OpaqueVertices );
		Assert.AreSame( indices, split.OpaqueIndices );
		Assert.IsNull( split.TranslucentVertices );
		Assert.IsNull( split.TranslucentIndices );
		Assert.AreEqual( 0, split.MixedFaces );
	}

	[TestMethod]
	public void FacesSplitByTheirTextureKeepTheirOrderAndGetTheAlphaTestFlagOnlyWhenTranslucent()
	{
		// slot 1 is translucent: faces (0,1,2) and (6,7,8) are opaque, (3,4,5) is translucent; vertex 9 is unused
		var vertices = Enumerable.Range( 0, 10 ).Select( index => Corner( index is >= 3 and <= 5 ? 1 : 0, index, 0x10 ) ).ToArray();
		var indices = new uint[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
		var split = WorldTranslucency.SplitFaces( vertices, indices, slot => slot == 1 );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2, 3, 4, 5 }, split.OpaqueIndices );
		CollectionAssert.AreEqual( new float[] { 0, 1, 2, 6, 7, 8 }, split.OpaqueVertices.Select( vertex => vertex.Position.X ).ToArray() );
		Assert.IsTrue( split.OpaqueVertices.All( vertex => vertex.MatFlags == 0x10 ), "opaque vertices keep their flags" );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2 }, split.TranslucentIndices );
		CollectionAssert.AreEqual( new float[] { 3, 4, 5 }, split.TranslucentVertices!.Select( vertex => vertex.Position.X ).ToArray() );
		Assert.IsTrue( split.TranslucentVertices.All( vertex => vertex.MatFlags == (0x10 | WorldTranslucency.AlphaTestLowFlag) ), "translucent vertices add the alpha test flag" );
		Assert.AreEqual( 0, split.MixedFaces );
	}

	[TestMethod]
	public void AFaceWithAnyTranslucentCornerIsTranslucentAndCounted()
	{
		var vertices = new[] { Corner( 0, 0 ), Corner( 0, 1 ), Corner( 1, 2 ), Corner( 0, 3 ), Corner( 0, 4 ), Corner( 0, 5 ) };
		var split = WorldTranslucency.SplitFaces( vertices, [0, 1, 2, 3, 4, 5, 0, 99, 1], slot => slot == 1 );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2 }, split.TranslucentIndices );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2 }, split.OpaqueIndices, "a face with an index outside the vertices is drawn by neither part" );
		Assert.AreEqual( 1, split.MixedFaces );
	}

	[TestMethod]
	public void AWholeTranslucentPartLeavesNoOpaqueFaces()
	{
		var vertices = new[] { Corner( 1, 0 ), Corner( 1, 1 ), Corner( 1, 2 ) };
		var split = WorldTranslucency.SplitFaces( vertices, [0, 1, 2], _ => true );
		Assert.AreEqual( 0, split.OpaqueIndices.Length );
		Assert.AreEqual( 0, split.OpaqueVertices.Length );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2 }, split.TranslucentIndices );
		Assert.IsTrue( WorldTranslucency.AnyTranslucentFace( vertices, [0, 1, 2], _ => true ) );
		Assert.IsFalse( WorldTranslucency.AnyTranslucentFace( vertices, [0, 1, 2], _ => false ) );
		var flagged = vertices.ToArray();
		WorldTranslucency.FlagAll( flagged );
		Assert.IsTrue( flagged.All( vertex => vertex.MatFlags == WorldTranslucency.AlphaTestLowFlag ) );
	}

	[TestMethod]
	public void TheAlphaTestFlagIsFreeOfTheFlagsTheCorpusAndTheShaderUse()
	{
		Assert.AreEqual( 0u, WorldTranslucency.AlphaTestLowFlag & 0xFFu, "the slot flags of the corpus are 0x1..0x73" );
		Assert.AreEqual( 0u, WorldTranslucency.AlphaTestLowFlag & (TextureSlotAddressing.ClampFlag | TextureSlotAddressing.SlushFlag) );
		Assert.AreEqual( 0x10 / 255f, WorldTranslucency.AlphaTestReference );
	}

	[TestMethod]
	public void TrianglesAreDrawnFarthestFirstByTheSumOfTheirCornerDepths()
	{
		// a camera looking down +Z: w = z + 1
		var viewProjection = new Matrix4x4( 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 1, 0, 0, 0, 1 );
		System.Numerics.Vector3[] positions = [new System.Numerics.Vector3( 0, 0, 1 ), new System.Numerics.Vector3( 1, 0, 1 ), new System.Numerics.Vector3( 0, 1, 1 ), new System.Numerics.Vector3( 0, 0, 5 ), new System.Numerics.Vector3( 1, 0, 5 ), new System.Numerics.Vector3( 0, 1, 5 ), new System.Numerics.Vector3( 0, 0, 3 ), new System.Numerics.Vector3( 1, 0, 3 ), new System.Numerics.Vector3( 0, 1, 3 ), new System.Numerics.Vector3( 0, 0, 3 ), new System.Numerics.Vector3( 1, 0, 3 ), new System.Numerics.Vector3( 0, 1, 3 )];
		uint[] indices = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
		var order = new int[4];
		WorldTranslucency.SortTriangles( positions, indices, viewProjection, order, new ulong[4] );
		CollectionAssert.AreEqual( new[] { 1, 2, 3, 0 }, order, "farthest (z 5) first, then the two equal ones in their own order, then the nearest" );
		Assert.AreEqual( 6f, WorldTranslucency.Depth( new System.Numerics.Vector3( 0, 0, 5 ), viewProjection ) );
	}

	// Identity-only entities: nothing here draws.
	private static T Empty<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );

	private static ModelEntity TranslucentPart() { var part = Empty<ModelEntity>(); part.Translucent = true; return part; }

	private static void SameOrder( Entity[] expected, Entity[] actual )
	{
		Assert.AreEqual( expected.Length, actual.Length );
		for ( var index = 0; index < expected.Length; index++ )
			Assert.AreSame( expected[index], actual[index], $"entity at draw position {index}" );
	}

	[TestMethod]
	public void TranslucentPartsDrawAfterEveryOtherEntityFarthestFirstAndTheParticlesFollowThem()
	{
		var near = TranslucentPart(); var far = TranslucentPart(); var middle = TranslucentPart(); var tie = TranslucentPart();
		var opaque1 = Empty<ModelEntity>(); var opaque2 = Empty<ModelEntity>(); var sprites = Empty<Entity>();
		var particle = Empty<ParticleRenderer>(); var weather = Empty<WeatherRenderer>();
		var depths = new System.Collections.Generic.Dictionary<Entity, float> { [near] = 2, [far] = 9, [middle] = 5, [tie] = 5 };
		Entity[] input = { near, opaque1, far, particle, sprites, weather, middle, opaque2, tie };
		var copy = (Entity[])input.Clone();
		var ordered = Level.WorldPassOrder( input, part => depths[part] );
		// the others keep their order (the pass is after all of them, so a late object's opaque parts are no longer behind its glass), then far to near with the tie in entity order, then the particles and the weather
		SameOrder( [opaque1, sprites, opaque2, far, middle, tie, near, particle, weather], ordered );
		SameOrder( copy, input );
	}

	[TestMethod]
	public void ASceneWithoutTranslucentPartsKeepsItsSnapshot()
	{
		var opaque = Empty<ModelEntity>(); var particle = Empty<ParticleRenderer>(); var weather = Empty<WeatherRenderer>();
		Entity[] input = { weather, opaque, particle, Empty<Entity>() };
		Assert.AreSame( input, Level.WorldPassOrder( input, _ => throw new AssertFailedException( "no depth is needed" ) ) );
	}

	[TestMethod]
	public void WeatherStaysBehindTheParticlesWhenTheWorldPassIsAppliedAfterWeatherAfterParticles()
	{
		var part = TranslucentPart(); var weather = Empty<WeatherRenderer>(); var particle = Empty<ParticleRenderer>(); var late = Empty<ModelEntity>();
		var ordered = Level.WorldPassOrder( Level.WeatherAfterParticles( [weather, part, particle, late] ), _ => 0f );
		SameOrder( [late, part, particle, weather], ordered );
	}
}
