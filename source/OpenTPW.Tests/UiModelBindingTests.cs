using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class UiModelBindingTests
{
	private static UiModel Model( string assetName, string rootName ) => new( new ModelFile( new MemoryStream( Geometry( rootName ) ) ), assetName );

	[TestMethod]
	public void ModelBinderResolvesStoredRootWhenItDiffersFromFilename()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-ui-binding-{Guid.NewGuid():N}" );
		var original = FileSystem;
		try
		{
			Directory.CreateDirectory( Path.Combine( directory, "ui" ) );
			File.WriteAllBytes( Path.Combine( directory, "ui", "filename_alias.MD2" ), Geometry( "drawing_root" ) );
			FileSystem = new BaseFileSystem( directory );
			var models = new UiModels();
			var byRoot = models.Get( "drawing_root" );
			Assert.IsNotNull( byRoot, "the original registry names the loaded root node, not the WAD member" );
			Assert.AreSame( byRoot, models.Get( "filename_alias" ), "existing presentation asset aliases resolve the same binding" );
		}
		finally
		{
			FileSystem = original;
			Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public void DrawingKeyUsesStoredRootAndPreservesRootNameCase()
	{
		var upper = Model( "first_asset", "CaseRoot" );
		var lower = Model( "second_asset", "caseroot" );
		var models = UiModels.FromAssets( new[] { upper, lower } );
		Assert.AreNotEqual( upper.DrawingKey, lower.DrawingKey );
		Assert.AreSame( upper, models.GetByRootName( "CaseRoot" ) );
		Assert.AreSame( lower, models.GetByRootName( "caseroot" ) );
		Assert.AreSame( upper, models.Get( "CaseRoot" ) );
		Assert.AreSame( lower, models.Get( "caseroot" ), "request cache must not fold two different original roots" );
		Assert.AreSame( upper, models.GetByDrawingKey( upper.DrawingKey ) );
		Assert.AreEqual( 557179197, UiModels.RootNameKey( "b_buy" ) );
		Assert.AreEqual( -47, UiModels.RootNameKey( "ÿ" ), "original hash sign-extends the stored byte" );
	}

	[TestMethod]
	public void SparseDummyRootIsNotReplacedByTheFirstDrawableFrameName()
	{
		var data = Geometry( "mesh_name" );
		var mesh = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 0x70, 4 ) );
		var dummy = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 0x74, 4 ) );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 0x78, 4 ), dummy );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( (int)mesh + 4, 4 ), dummy );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( (int)mesh + 12, 4 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( (int)dummy + 4, 4 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( (int)dummy + 12, 4 ), mesh );
		var model = new UiModel( new ModelFile( new MemoryStream( data ) ), "unrelated_filename" );
		Assert.AreEqual( "dummy", model.RootNodeName );
		Assert.AreEqual( "mesh_name", model.Frames[0].NodeName );
		Assert.AreSame( model, UiModels.FromAssets( new[] { model } ).GetByRootName( "dummy" ) );
	}

	[TestMethod]
	public void DifferentRootNamesWithTheSameHashFailWithBothAssetsNamed()
	{
		var first = Model( "one", "aP" );
		var second = Model( "two", "ba" );
		Assert.AreEqual( first.DrawingKey, second.DrawingKey, "deliberate collision fixture" );
		var error = Assert.ThrowsException<UiModelBindingException>( () => UiModels.FromAssets( new[] { second, first } ) );
		Assert.AreEqual( first.DrawingKey, error.DrawingKey );
		StringAssert.Contains( error.Message, "one ('aP')" );
		StringAssert.Contains( error.Message, "two ('ba')" );
	}

	[TestMethod]
	public void AssetAliasAmbiguityDoesNotLoseDistinctOriginalRoots()
	{
		var first = Model( "shared_filename", "first_root" );
		var second = Model( "shared_filename", "second_root" );
		var models = UiModels.FromAssets( new[] { first, second } );
		Assert.AreSame( first, models.GetByRootName( "first_root" ) );
		Assert.AreSame( second, models.GetByRootName( "second_root" ) );
		Assert.ThrowsException<UiModelBindingException>( () => models.GetAsset( "shared_filename" ) );
		Assert.ThrowsException<UiModelBindingException>( () => models.Get( "shared_filename" ) );
	}

	[TestMethod]
	public void CaseVariantAssetAliasesRequireAnExactOrUnambiguousRequest()
	{
		var first = Model( "Asset", "upper_root" );
		var second = Model( "asset", "lower_root" );
		var models = UiModels.FromAssets( new[] { first, second } );
		Assert.AreSame( first, models.GetAsset( "Asset" ) );
		Assert.AreSame( second, models.GetAsset( "asset" ) );
		Assert.ThrowsException<UiModelBindingException>( () => models.GetAsset( "ASSET" ) );
	}

	[TestMethod]
	public void OriginalSpecialShadowRemainsAnAssetWithoutAnOrdinaryDuplicateBinding()
	{
		var ordinary = Model( "shadow1", "wshadow1" );
		var special = Model( "w_small_shadow", "wshadow1" );
		var models = UiModels.FromAssets( new[] { special, ordinary } );
		Assert.AreEqual( 2, models.Assets.Count );
		Assert.AreEqual( 1, models.BindingCount );
		Assert.AreSame( ordinary, models.GetByDrawingKey( ordinary.DrawingKey ) );
		Assert.AreSame( special, models.GetAsset( "w_small_shadow" ) );
	}

	[TestMethod]
	public void CustomLoaderCachesMissingRequestsAndDetectsLaterRootCollisions()
	{
		var loads = 0;
		var first = Model( "one", "aP" );
		var second = Model( "two", "ba" );
		var models = new UiModels( name =>
		{
			loads++;
			return name switch { "one" => first, "two" => second, _ => throw new FileNotFoundException( name ) };
		} );
		Assert.IsNull( models.Get( "missing" ) );
		Assert.IsNull( models.Get( "missing" ) );
		Assert.AreEqual( 1, loads );
		Assert.AreSame( first, models.Get( "one" ) );
		Assert.ThrowsException<UiModelBindingException>( () => models.Get( "two" ) );
		Assert.ThrowsException<UiModelBindingException>( () => models.GetByDrawingKey( first.DrawingKey ) );
		Assert.ThrowsException<UiModelBindingException>( () => models.Get( "one" ), "a cached first asset must not survive an ambiguous registry" );
	}

	internal static byte[] Geometry( string rootName )
	{
		var fixture = Md2ModelFileTests.CreateGeometry();
		var name = Encoding.Latin1.GetBytes( rootName + "\0" );
		var data = new byte[fixture.Length + name.Length];
		fixture.CopyTo( data, 0 );
		name.CopyTo( data, fixture.Length );
		var rootRecord = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 0x78, 4 ) );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( (int)rootRecord + 84, 4 ), (uint)fixture.Length );
		return data;
	}
}
