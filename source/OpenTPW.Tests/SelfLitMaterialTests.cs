using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Material word +0xC bit 0x1 (the model reader's <see cref="ModelFile.MaterialData.Pad"/>): tp.exe's vertex fill 0x580530 gives the corners of such a material the draw's constant colour instead of the
/// vertex lighting colour (0x580C51), so the texture is drawn full-bright. Issue 829: the gap between the Gift Shop chests' base and lid (their glow and gold interior) is self-lit art and was drawn lit, so it looked dark.
/// The corpus pins need OPENTPW_GAME_PATH and are inconclusive without it.
/// </summary>
[TestClass]
public class SelfLitMaterialTests
{
	private static ModelFile.MaterialData Material( uint slotFlags, uint pad ) => new() { Flags = slotFlags, Pad = pad, TextureIndex = 0 };

	[TestMethod]
	public void OnlyMaterialBitOneMakesACornerSelfLit()
	{
		Assert.AreEqual( 0u, TextureSlotAddressing.VertexFlags( Material( 0x11, 0 ) ) & TextureSlotAddressing.SelfLitFlag );
		Assert.AreEqual( TextureSlotAddressing.SelfLitFlag, TextureSlotAddressing.VertexFlags( Material( 0x11, 1 ) ) & TextureSlotAddressing.SelfLitFlag );
		// bit 0x2 of the word is the state bit 0x800000 of 0x581790, which no shipped material sets, and it lights nothing
		Assert.AreEqual( 0u, TextureSlotAddressing.VertexFlags( Material( 0x11, 2 ) ) & TextureSlotAddressing.SelfLitFlag );
		Assert.AreEqual( TextureSlotAddressing.SelfLitFlag, TextureSlotAddressing.VertexFlags( Material( 0x11, 3 ) ) & TextureSlotAddressing.SelfLitFlag );
	}

	[TestMethod]
	public void ASelfLitCornerKeepsTheAddressingAndTheFlagsOfItsSlot()
	{
		foreach ( var slotFlags in new[] { 0x1u, 0x3u, 0x13u, 0x33u } )
		{
			var plain = TextureSlotAddressing.VertexFlags( slotFlags );
			var selfLit = TextureSlotAddressing.VertexFlags( Material( slotFlags, 1 ) );
			Assert.AreEqual( plain | TextureSlotAddressing.SelfLitFlag, selfLit, $"slot flags 0x{slotFlags:X}: the self-lit flag is the only difference" );
			Assert.AreEqual( plain, TextureSlotAddressing.VertexFlags( Material( slotFlags, 0 ) ), "a lit material is as it was" );
		}
	}

	[TestMethod]
	public void TheSelfLitVertexFlagIsAFreeBit()
	{
		var others = new[]
		{
			TextureSlotAddressing.ClampFlag, TextureSlotAddressing.SlushFlag, WorldTranslucency.AlphaTestLowFlag, WorldTranslucency.CutoutFlag, WorldTranslucency.OpaqueSlotFlag
		};
		Assert.AreEqual( 1, System.Numerics.BitOperations.PopCount( TextureSlotAddressing.SelfLitFlag ) );
		Assert.AreEqual( 0u, TextureSlotAddressing.SelfLitFlag & 0xFFu, "the original slot flags use the low byte only" );
		foreach ( var other in others )
			Assert.AreEqual( 0u, TextureSlotAddressing.SelfLitFlag & other );
	}

	[TestMethod]
	public void TheLitShaderShadesASelfLitCornerWithTheConstantColourAndNoLight()
	{
		var text = File.ReadAllText( Path.Combine( RepositoryRoot(), "content", "shaders", "test.shader" ) );
		StringAssert.Contains( text, $"const uint FLAG_SELF_LIT = 0x{TextureSlotAddressing.SelfLitFlag:X8}u;", "the shader's bit is the vertex flag's" );
		StringAssert.Contains( text, "vec3 vShading = selfLit ? vec3( 1.0 ) : vDiffuse + vAmbient;" );
		StringAssert.Contains( text, "if ( !selfLit && g_oUbo.g_flLightCount > 0.5 )", "the park's point lights are part of the lighting colour a self-lit corner does not take" );
		StringAssert.Contains( text, "if ( !selfLit && vAmbientTint.x", "the evening shoulder is for lit surfaces" );
	}

	[TestMethod]
	public void TheAdvisorShaderShadesASelfLitCornerWithTheConstantColourToo()
	{
		var text = File.ReadAllText( Path.Combine( RepositoryRoot(), "content", "shaders", "advisor.shader" ) );
		StringAssert.Contains( text, $"const uint FLAG_SELF_LIT = 0x{TextureSlotAddressing.SelfLitFlag:X8}u;" );
		StringAssert.Contains( text, "vec3 vShading = ( outMatFlags & FLAG_SELF_LIT ) != 0u ? vec3( 1.0 ) : vs_out.vLight;" );
	}

	[TestMethod]
	public void TheAdvisorsMouthsAndGoldTicketsAreTheSelfLitMeshesAndTheirVerticesCarryIt()
	{
		var dataPath = Md2CorpusTests.OriginalDataPath();
		var checkedModels = 0;
		foreach ( var (name, model) in OriginalModels( dataPath ).Where( entry => entry.Name.StartsWith( "Advisor", StringComparison.OrdinalIgnoreCase ) ) )
		{
			foreach ( var mesh in model.Meshes )
			{
				var selfLit = mesh.Name.StartsWith( "Mouth", StringComparison.OrdinalIgnoreCase ) || mesh.Name is "Ticket" or "Ticket2";
				Assert.AreEqual( selfLit, mesh.Materials.Length > 0 && mesh.Materials.All( material => (material.Pad & ModelBuilder.SelfLitMaterialFlag) != 0 ), $"{name}: {mesh.Name}" );
				var flagged = ObjectAssets.ConvertMesh( mesh ).Count( vertex => (vertex.MatFlags & TextureSlotAddressing.SelfLitFlag) != 0 );
				Assert.AreEqual( selfLit ? mesh.Vertices.Length : 0, flagged, $"{name}: {mesh.Name} vertices the advisor shader shades with 1" );
			}
			checkedModels++;
		}
		Assert.AreEqual( 5, checkedModels, "the global advisor and the four themes' (Advisor.MD2 has five mouths and no ticket)" );
	}

	[TestMethod]
	public void EveryShippedMaterialWordIsZeroOrOne()
	{
		var dataPath = Md2CorpusTests.OriginalDataPath();
		var words = new SortedDictionary<uint, int>();
		foreach ( var model in OriginalModels( dataPath ) )
			foreach ( var mesh in model.Model.Meshes )
				foreach ( var material in mesh.Materials )
					words[material.Pad] = words.GetValueOrDefault( material.Pad ) + 1;
		Console.WriteLine( "Material words +0xC: " + string.Join( ", ", words.Select( pair => $"0x{pair.Key:X}={pair.Value}" ) ) );
		// bit 0x2 (state bit 0x800000 of 0x581790) and every higher bit never arise from data; 920 of 12,951 materials are self-lit
		CollectionAssert.AreEqual( new uint[] { 0, 1 }, words.Keys.ToArray() );
		Assert.AreEqual( 12951, words.Values.Sum() );
		Assert.AreEqual( 920, words[1] );
	}

	[TestMethod]
	public void TheGiftShopChestsDrawTheirGlowAndTreasureSelfLitAndTheirWoodAndMetalLit()
	{
		var dataPath = Md2CorpusTests.OriginalDataPath();
		// theme, member, [mesh: self-lit materials], [mesh: lit materials]
		var shops = new (string Theme, string Member, (string Mesh, string Material)[] SelfLit, (string Mesh, string Material)[] Lit)[]
		{
			( "jungle", "giftshop.md2",
				[("jt_head1", "b_glow2"), ("jt_head1", "jt_box1a"), ("jt_head03", "b_glow2")],
				[("jt_head1", "jt_topa"), ("jt_head1", "jt_wallb"), ("jt_head1", "jt_hingea"), ("jt_head1", "JJ_heada"), ("jt_head1", "jt_ring2"), ("jt_head1", "jt_keya"), ("jt_head1", "jt_eyeB")] ),
			( "hallow", "giftshop.MD2",
				[("glow01", "glow2"), ("chest", "h_glow2"), ("chest", "treasure"), ("web04", "web2"), ("web1", "web2"), ("web3", "web2")],
				[("chest", "lidside"), ("chest", "toplid"), ("chest", "chestside"), ("chest", "chestback"), ("chest", "handles"), ("chest", "scull3")] ),
			( "space", "giftshop.MD2",
				[("ship", "dome"), ("ship", "gold")],
				[("ship", "hull2"), ("ship", "newhull"), ("ship", "rim3"), ("ship", "doortop")] )
		};
		foreach ( var (theme, member, selfLit, lit) in shops )
		{
			using var archive = new WadArchive( Path.Combine( dataPath, "levels", theme, "shops", "giftshop.wad" ) );
			var model = new ModelFile( new MemoryStream( archive.GetFile( member ).GetData() ) );
			foreach ( var (meshName, materialName) in selfLit )
				Assert.IsTrue( MaterialsOf( model, meshName, materialName ).All( material => (material.Pad & ModelBuilder.SelfLitMaterialFlag) != 0 ), $"{theme}: {meshName}/{materialName} is self-lit" );
			foreach ( var (meshName, materialName) in lit )
				Assert.IsTrue( MaterialsOf( model, meshName, materialName ).All( material => material.Pad == 0 ), $"{theme}: {meshName}/{materialName} is lit" );
		}
	}

	[TestMethod]
	public void TheChestGlowIsAGoldTextureWithAnAlphaRampDrawnAtTheTranslucentState()
	{
		var dataPath = Md2CorpusTests.OriginalDataPath();
		// the glow slots carry slot flags 0x13 (hallow glow2) and 0x33 (jungle b_glow2), their textures an alpha plane whose header alpha byte is non-zero: state 0x556, blended by source alpha
		// the hallow shop's own archive holds its glow, the jungle one's glow is in the theme's shared textures (sharetex.wad holds the full-size set at its root)
		foreach ( var (theme, member, mesh, material, slotFlags, textureArchive, texture) in new[]
		{
			( "hallow", "giftshop.MD2", "glow01", "glow2", 0x13u, "shops/giftshop.wad", "textures/glow2.wct" ),
			( "jungle", "giftshop.md2", "jt_head03", "b_glow2", 0x33u, "sharetex.wad", "b_glow2.wct" )
		} )
		{
			using var archive = new WadArchive( Path.Combine( dataPath, "levels", theme, "shops", "giftshop.wad" ) );
			var model = new ModelFile( new MemoryStream( archive.GetFile( member ).GetData() ) );
			var glow = MaterialsOf( model, mesh, material ).Single();
			Assert.AreEqual( slotFlags, glow.Flags, $"{theme}: the glow slot's flags" );
			var file = ArchiveTexture( dataPath, theme, textureArchive, texture );
			Assert.AreEqual( TextureAlphaType.Translucent, file.AlphaType, $"{theme}: {texture}" );
			// one colour over the whole texture and an alpha that ramps from clear to opaque
			var data = file.Data.Data;
			var colours = new HashSet<(byte, byte, byte)>();
			byte least = 255, most = 0;
			for ( var index = 0; index < data.Length; index += 4 )
			{
				colours.Add( (data[index], data[index + 1], data[index + 2]) );
				least = Math.Min( least, data[index + 3] );
				most = Math.Max( most, data[index + 3] );
			}
			Assert.AreEqual( 1, colours.Count, $"{theme}: {texture} is one flat colour" );
			Assert.IsTrue( least < 0x10 && most > 0xF0, $"{theme}: {texture} alpha runs {least}..{most}" );
		}
	}

	/// <summary>The texture <paramref name="name"/> of a theme archive's full-size set (the folder <c>textures</c>; <c>stexture</c> holds the small copies).</summary>
	private static TextureFile ArchiveTexture( string dataPath, string theme, string wad, string name )
	{
		using var archive = new WadArchive( Path.Combine( dataPath, "levels", theme, wad ) );
		var members = Files( archive.Root, "" ).ToArray();
		var found = members.FirstOrDefault( member => string.Equals( member.Name, name, StringComparison.OrdinalIgnoreCase ) );
		Assert.IsNotNull( found.File, $"{theme}: {name} is in {wad}" );
		return new TextureFile( new MemoryStream( found.File!.GetData() ) );
	}

	private static IEnumerable<(string Name, WadArchiveFile? File)> Files( ArchiveDirectory directory, string prefix )
	{
		foreach ( var child in directory.Children )
		{
			if ( child is ArchiveDirectory subdirectory )
			{
				foreach ( var entry in Files( subdirectory, $"{prefix}{subdirectory.Name}/" ) )
					yield return entry;
			}
			else if ( child is WadArchiveFile file )
				yield return ($"{prefix}{file.Name}", file);
		}
	}

	private static IEnumerable<ModelFile.MaterialData> MaterialsOf( ModelFile model, string meshName, string materialName )
	{
		var mesh = model.Meshes.Single( candidate => candidate.Name == meshName );
		var found = mesh.Materials.Where( material => string.Equals( material.Name, materialName, StringComparison.OrdinalIgnoreCase ) ).ToArray();
		Assert.IsTrue( found.Length > 0, $"{meshName} has a material {materialName}" );
		return found;
	}

	private static IEnumerable<(string Name, ModelFile Model)> OriginalModels( string dataPath )
	{
		foreach ( var wad in Directory.EnumerateFiles( dataPath, "*", SearchOption.AllDirectories ).Where( file => file.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ) )
		{
			using var archive = new WadArchive( wad );
			foreach ( var (name, file) in Md2CorpusTests.Md2Members( archive.Root, "" ) )
			{
				ModelFile model;
				try { model = new ModelFile( new MemoryStream( file.GetData() ) ); }
				catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException ) { continue; }
				file.Free();
				if ( model.Kind == ModelFileKind.Geometry )
					yield return (name, model);
			}
		}
	}

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !Directory.Exists( Path.Combine( directory.FullName, "content", "shaders" ) ) )
			directory = directory.Parent;
		return directory?.FullName ?? throw new DirectoryNotFoundException( "content/shaders" );
	}
}
