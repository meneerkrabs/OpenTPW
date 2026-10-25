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
		Assert.IsNull( split.OpaqueMap );
		Assert.AreEqual( 0, split.Translucent.Count );
		Assert.AreEqual( 0, split.MixedFaces );
	}

	[TestMethod]
	public void FacesSplitByTheirTextureIntoOneGroupPerTranslucentSlotKeepingTheirOrder()
	{
		// slots 1 and 2 are translucent: faces (0,1,2) and (6,7,8) are opaque, (3,4,5) uses slot 1, (9,10,11) slot 2, (12,13,14) slot 1 again; vertex 15 is unused
		var vertices = Enumerable.Range( 0, 16 ).Select( index => Corner( index is >= 3 and <= 5 or >= 12 and <= 14 ? 1 : index is >= 9 and <= 11 ? 2 : 0, index, 0x10 ) ).ToArray();
		var indices = Enumerable.Range( 0, 15 ).Select( index => (uint)index ).ToArray();
		var split = WorldTranslucency.SplitFaces( vertices, indices, slot => slot is 1 or 2 );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2, 3, 4, 5 }, split.OpaqueIndices );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 6, 7, 8 }, split.OpaqueMap );
		CollectionAssert.AreEqual( new float[] { 0, 1, 2, 6, 7, 8 }, split.OpaqueVertices.Select( vertex => vertex.Position.X ).ToArray() );
		Assert.AreEqual( 2, split.Translucent.Count );
		Assert.AreEqual( 1, split.Translucent[0].Slot, "groups follow the first face of each slot" );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2, 3, 4, 5 }, split.Translucent[0].Indices );
		CollectionAssert.AreEqual( new[] { 3, 4, 5, 12, 13, 14 }, split.Translucent[0].Map );
		Assert.AreEqual( 2, split.Translucent[1].Slot );
		CollectionAssert.AreEqual( new[] { 9, 10, 11 }, split.Translucent[1].Map );
		Assert.AreEqual( 0, split.MixedFaces );
	}

	[TestMethod]
	public void AFaceWithAnyTranslucentCornerIsTranslucentAndCounted()
	{
		var vertices = new[] { Corner( 0, 0 ), Corner( 0, 1 ), Corner( 1, 2 ), Corner( 0, 3 ), Corner( 0, 4 ), Corner( 0, 5 ) };
		var split = WorldTranslucency.SplitFaces( vertices, [0, 1, 2, 3, 4, 5, 0, 99, 1], slot => slot == 1 );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2 }, split.Translucent.Single().Indices );
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
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2 }, split.Translucent.Single().Indices );
	}

	[TestMethod]
	public void EachTexturesAlphaTypeAddsItsOwnAlphaTestFlagToItsVertices()
	{
		var vertices = new[] { Corner( 0, 0, 0x10 ), Corner( 1, 1, 0x10 ), Corner( 2, 2 ), Corner( 3, 3 ), Corner( 9, 4 ) };
		WorldTranslucency.ApplyAlphaFlags( vertices, [TextureAlphaType.Opaque, TextureAlphaType.Cutout, TextureAlphaType.Translucent, TextureAlphaType.Opaque] );
		CollectionAssert.AreEqual( new[] { 0x10u, 0x10u | WorldTranslucency.CutoutFlag, WorldTranslucency.AlphaTestLowFlag, 0u, 0u }, vertices.Select( vertex => vertex.MatFlags ).ToArray(), "a slot outside the table keeps its flags" );
	}

	[TestMethod]
	public void TheAlphaTestFlagsAreFreeOfTheFlagsTheCorpusAndTheShaderUse()
	{
		foreach ( var flag in new[] { WorldTranslucency.AlphaTestLowFlag, WorldTranslucency.CutoutFlag } )
		{
			Assert.AreEqual( 0u, flag & 0xFFu, "the slot flags of the corpus are 0x1..0x73" );
			Assert.AreEqual( 0u, flag & (TextureSlotAddressing.ClampFlag | TextureSlotAddressing.SlushFlag) );
		}
		Assert.AreEqual( 0u, WorldTranslucency.AlphaTestLowFlag & WorldTranslucency.CutoutFlag );
		Assert.AreEqual( 0x10 / 255f, WorldTranslucency.AlphaTestReference );
		Assert.AreEqual( 0xF0 / 255f, WorldTranslucency.CutoutReference );
		Assert.AreEqual( WorldTranslucency.CutoutFlag, WorldTranslucency.FlagFor( TextureAlphaType.Cutout ) );
		Assert.AreEqual( WorldTranslucency.AlphaTestLowFlag, WorldTranslucency.FlagFor( TextureAlphaType.Translucent ) );
		Assert.AreEqual( 0u, WorldTranslucency.FlagFor( TextureAlphaType.Opaque ) );
	}

	/// <summary>A camera at the origin looking down +Z with a 60 degree field of view and a depth range of 1 to 100 (the engine's row-vector, 0..1 depth convention).</summary>
	private static Matrix4x4 Perspective() => Matrix4x4.CreateScale( 1, 1, -1 ) * Matrix4x4.CreatePerspectiveFieldOfView( MathF.PI / 3, 16f / 9f, 1f, 100f );

	[TestMethod]
	public void DepthIsTheProjectedZOverWAndGrowsWithDistance()
	{
		var projection = Perspective();
		var near = WorldTranslucency.Depth( new System.Numerics.Vector3( 0, 0, 2 ), projection );
		var middle = WorldTranslucency.Depth( new System.Numerics.Vector3( 0, 0, 10 ), projection );
		var far = WorldTranslucency.Depth( new System.Numerics.Vector3( 0, 0, 50 ), projection );
		Assert.IsTrue( 0f <= near && near < middle && middle < far && far <= 1f, $"{near} {middle} {far}" );
		var clip = System.Numerics.Vector4.Transform( new System.Numerics.Vector4( 3, 1, 10, 1 ), projection );
		Assert.AreEqual( clip.Z / clip.W, WorldTranslucency.Depth( new System.Numerics.Vector3( 3, 1, 10 ), projection ), 1e-6f );
		Assert.AreEqual( -1f, WorldTranslucency.Depth( new System.Numerics.Vector3( 0, 0, -5 ), projection ), "behind the eye is the nearest" );
		Assert.AreEqual( ( near + far ) / 2, WorldTranslucency.MeanDepth( [new System.Numerics.Vector3( 0, 0, 2 ), new System.Numerics.Vector3( 0, 0, 50 )], projection ), 1e-6f, "the mean of the vertices' depths, not the depth of their mean" );
	}

	[TestMethod]
	public void TrianglesAreDrawnFarthestFirstByTheSumOfTheirCornerDepthsAndTiesKeepTheirOrder()
	{
		var projection = Perspective();
		System.Numerics.Vector3[] positions = [new( 0, 0, 2 ), new( 1, 0, 2 ), new( 0, 1, 2 ), new( 0, 0, 40 ), new( 1, 0, 40 ), new( 0, 1, 40 ), new( 0, 0, 10 ), new( 1, 0, 10 ), new( 0, 1, 10 ), new( 0, 0, 10 ), new( 1, 0, 10 ), new( 0, 1, 10 )];
		uint[] indices = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
		var order = new int[4];
		WorldTranslucency.SortTriangles( positions, indices, projection, order, new ulong[4] );
		CollectionAssert.AreEqual( new[] { 1, 2, 3, 0 }, order, "farthest first, then the two equal ones in their own order, then the nearest" );
	}

	[TestMethod]
	public void ATriangleOrderUploadsOnlyWhenTheOrderChanges()
	{
		var projection = Perspective();
		var vertices = new[]
		{
			new Vertex { Position = new Vector3( 0, 0, 5 ) }, new Vertex { Position = new Vector3( 1, 0, 5 ) }, new Vertex { Position = new Vector3( 0, 1, 5 ) },
			new Vertex { Position = new Vector3( 0, 0, 20 ) }, new Vertex { Position = new Vector3( 1, 0, 20 ) }, new Vertex { Position = new Vector3( 0, 1, 20 ) }
		};
		var order = new TriangleOrder( vertices, [0, 1, 2, 3, 4, 5] );
		order.Sort( projection );
		Assert.IsTrue( order.NeedsUpload, "the far triangle moves in front of the near one" );
		order.MarkUploaded();
		order.Sort( projection * Matrix4x4.CreateTranslation( 0.001f, 0, 0 ) );
		Assert.IsFalse( order.NeedsUpload, "a camera that moved a little leaves the order as it was" );
		order.SetPositions( [vertices[3], vertices[4], vertices[5], vertices[0], vertices[1], vertices[2]] );
		order.Sort( projection );
		Assert.IsTrue( order.NeedsUpload, "moved vertices are sorted again" );
	}

	// Identity-only entities: nothing here draws.
	private static T Empty<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );

	private static void SameOrder( Entity[] expected, Entity[] actual )
	{
		Assert.AreEqual( expected.Length, actual.Length );
		for ( var index = 0; index < expected.Length; index++ )
			Assert.AreSame( expected[index], actual[index], $"entity at draw position {index}" );
	}

	[TestMethod]
	public void TheLateEntitiesAreTheBlendedEffectsAndThePoolWaterAndTheRestKeepTheirOrder()
	{
		var particle = Empty<ParticleRenderer>(); var weather = Empty<WeatherRenderer>(); var spray = Empty<PoolSprayRenderer>();
		var water = Empty<PoolPartEntity>(); water.IsWater = true;
		var deck = Empty<PoolPartEntity>();
		var a = Empty<ModelEntity>(); var b = Empty<Entity>();
		Entity[] input = { a, water, particle, b, spray, deck, weather };
		var (before, after) = Level.SplitLate( input );
		SameOrder( [a, b, deck], before );
		SameOrder( [water, particle, spray, weather], after );
		SameOrder( [a, water, particle, b, spray, deck, weather], input );
	}

	[TestMethod]
	public void ASceneWithoutLateEntitiesKeepsItsSnapshot()
	{
		Entity[] input = { Empty<ModelEntity>(), Empty<Entity>() };
		var (before, after) = Level.SplitLate( input );
		Assert.AreSame( input, before );
		Assert.AreEqual( 0, after.Length );
	}

	[TestMethod]
	public void TheWorldPassKeepsRecordsOnlyWhileItIsOpenAndSortsThemFarthestFirst()
	{
		var entity = Empty<ModelEntity>(); var model = Empty<Model>();
		WorldPass.Abort();
		Assert.IsFalse( WorldPass.Submit( entity, model, Matrix4x4.Identity ), "no pass is open: the caller draws it" );
		WorldPass.Begin( Matrix4x4.Identity );
		try
		{
			Assert.IsTrue( WorldPass.Active );
			foreach ( var depth in new[] { 0.5f, 0.9f, 0.1f, 0.9f } )
				WorldPass.Submit( entity, model, Matrix4x4.Identity, depth );
			WorldPass.SortPending();
			CollectionAssert.AreEqual( new[] { 0.9f, 0.9f, 0.5f, 0.1f }, WorldPass.Pending.Select( record => record.Depth ).ToArray() );
			CollectionAssert.AreEqual( new[] { 1, 3, 0, 2 }, WorldPass.Pending.Select( record => record.Sequence ).ToArray(), "equal depths keep the order they were handed over in" );
		}
		finally
		{
			WorldPass.Abort();
		}
		Assert.IsFalse( WorldPass.Active );
		Assert.AreEqual( 0, WorldPass.Pending.Count );
	}

	[TestMethod]
	public void TheHeaderAlphaByteDoesNotDecideWhetherTheAlphaPlaneIsDecoded()
	{
		// tp.exe loads and decodes the alpha plane whenever the file has 32 bits per pixel (0x564C00); the alpha byte only tells a translucent texture from a cut-out one
		var translucent = TextureDecoderIndexingSyntheticTests.BuildWct( 0x13, 1, 0x20, 2, 16, 16, 0, 0, 0, 0, 8704, 4480, 1, 2 );
		var cutOut = (byte[])translucent.Clone();
		cutOut[1] = 0;
		var withAlphaByte = new TextureFile( new MemoryStream( translucent ) );
		var withoutAlphaByte = new TextureFile( new MemoryStream( cutOut ) );
		CollectionAssert.AreEqual( withAlphaByte.Data.Data, withoutAlphaByte.Data.Data, "the same pixels with the alpha plane" );
		Assert.AreEqual( TextureAlphaType.Translucent, withAlphaByte.AlphaType );
		Assert.AreEqual( TextureAlphaType.Cutout, withoutAlphaByte.AlphaType );
		var alphas = Enumerable.Range( 0, withoutAlphaByte.Data.Data.Length / 4 ).Select( index => withoutAlphaByte.Data.Data[index * 4 + 3] ).Distinct().ToArray();
		Assert.IsTrue( alphas.Length > 1 || alphas[0] != 255, "a cut-out texture's alpha plane is decoded" );
	}

	[TestMethod]
	public void ATextureWithoutAnAlphaPlaneDecodesNoAlphaWhateverItsAlphaByteSays()
	{
		// the one 0x13/0x01/0x18 member of the data: an alpha byte of 1, 24 bits per pixel and no alpha chunk
		var file = TextureDecoderIndexingSyntheticTests.BuildWct( 0x13, 0, 0x18, 2, 16, 16, 0, 0, 0, 0, 8704, 4480, 1, 2 );
		file[1] = 1;
		var texture = new TextureFile( new MemoryStream( file ) );
		Assert.AreEqual( TextureAlphaType.Opaque, texture.AlphaType );
		Assert.IsTrue( Enumerable.Range( 0, texture.Data.Data.Length / 4 ).All( index => texture.Data.Data[index * 4 + 3] == 255 ) );
	}
}
