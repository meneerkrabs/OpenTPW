using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class Md2CorpusTests
{
	[TestMethod]
	public void EveryOriginalWadMd2MemberParsesExceptTheTwoVersion207Members()
	{
		var dataPath = OriginalDataPath();
		var wads = Directory.EnumerateFiles( dataPath, "*", SearchOption.AllDirectories )
			.Where( file => file.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ).ToArray();
		Assert.AreEqual( 312, wads.Length );
		int members = 0, geometry = 0, animation = 0;
		long meshes = 0, nodes = 0, faces = 0, corners = 0, positions = 0, materials = 0, untextured = 0, textureSlots = 0, multiFrame = 0, extra = 0, attributes = 0;
		var unsupported = new List<string>();
		var failures = new List<string>();
		foreach ( var wad in wads )
		{
			using var archive = new WadArchive( wad );
			foreach ( var (name, file) in Md2Members( archive.Root, "" ) )
			{
				members++;
				var label = $"{Path.GetRelativePath( dataPath, wad ).Replace( '\\', '/' )}/{name}";
				try
				{
					var model = new ModelFile( new MemoryStream( file.GetData() ) );
					if ( model.Kind == ModelFileKind.Animation )
					{
						animation++;
						continue;
					}
					geometry++;
					meshes += model.Meshes.Count;
					nodes += model.Nodes.Count;
					faces += model.Meshes.Sum( mesh => mesh.FaceCount );
					corners += model.Meshes.Sum( mesh => mesh.Vertices.Length );
					positions += model.Meshes.Sum( mesh => mesh.Positions.Length );
					materials += model.Meshes.Sum( mesh => mesh.Materials.Length );
					untextured += model.Meshes.Sum( mesh => mesh.Materials.Count( material => material.TextureIndex < 0 ) );
					textureSlots += model.Textures.Count;
					multiFrame += model.Textures.Count( texture => texture.FrameNames.Count > 1 );
					extra += model.ExtraRecords.Count;
					attributes += model.DummyAttributes.Count;
				}
				catch ( NotSupportedException )
				{
					unsupported.Add( label );
				}
				catch ( Exception exception )
				{
					failures.Add( $"{label}: {exception.Message}" );
				}
				finally
				{
					file.Free();
				}
			}
		}
		Assert.AreEqual( 0, failures.Count, string.Join( Environment.NewLine, failures ) );
		Assert.AreEqual( 2118, members );
		Assert.AreEqual( 838, geometry );
		Assert.AreEqual( 1278, animation );
		CollectionAssert.AreEquivalent( new[] { "levels/jungle/rides/wateride.wad/wr_tunnel.md2", "levels/jungle/rides/wateride.wad/wr_tunnelm.md2" },
			unsupported.Select( label => label.ToLowerInvariant() ).ToArray() );
		Assert.AreEqual( 4914, meshes );
		Assert.AreEqual( 7520, nodes );
		Assert.AreEqual( 210034, faces );
		Assert.AreEqual( 303199, corners );
		Assert.AreEqual( 133707, positions );
		Assert.AreEqual( 12951, materials );
		Assert.AreEqual( 3, untextured );
		Assert.AreEqual( 7282, textureSlots );
		Assert.AreEqual( 124, multiFrame );
		Assert.AreEqual( 27, extra );
		Assert.AreEqual( 2452, attributes );
	}

	[DataTestMethod]
	[DataRow( "levels/jungle/rides/totem.wad", "totem.MD2", "1B499E5A2E43EBB3AC4E9252A24A66F4A2EFE0285AFE522A573CB226438BA42A", 13, 31, 34, 316, "E0B32787AA1916F403C4E952B2F7025E65866BFCF25F8EC98297967E1EBB4F52" )]
	[DataRow( "global/advisor.wad", "Advisor.MD2", "F518A91BCEBD1770A2927A0143549A547D02EED5EE91705E626FC08C2BC3B51C", 25, 29, 41, 1321, "A0174988C4A8D423F329C85BB7B5187F9F71CF157BFE8CAD82AB4030D57FE733" )]
	[DataRow( "levels/fantasy/features/gates.wad", "gates.MD2", "1F3013C8140BE310AE885FF240D96BE228B18DB4B36120A91217B081BF9A454D", 2, 5, 9, 544, "66DA2A022E956B79081EAB4FFEB48B9593DBCAB66270987FC9FDF967DB39532D" )]
	[DataRow( "levels/fantasy/terrain.wad", "base.MD2", "1B783EE1735855575E6F8D2BBFA694B4A27D2F04EC979F4ADDC9DA049514F495", 195, 196, 40, 17450, "3AD596D88E48ACA3BB05365D806E23517B29EEE152779C7568C64A7B090F586E" )]
	public void OriginalGeometryMatchesIdentityAndIndependentGeometryHash( string wad, string member, string fileHash, int meshCount, int nodeCount, int textureCount, int faceCount, string geometryHash )
	{
		var data = ReadMember( wad, member );
		Assert.AreEqual( fileHash, Convert.ToHexString( SHA256.HashData( data ) ) );
		var model = new ModelFile( new MemoryStream( data ) );
		Assert.AreEqual( ModelFileKind.Geometry, model.Kind );
		Assert.AreEqual( meshCount, model.Meshes.Count );
		Assert.AreEqual( nodeCount, model.Nodes.Count );
		Assert.AreEqual( textureCount, model.Textures.Count );
		Assert.AreEqual( faceCount, model.Meshes.Sum( mesh => (int)mesh.FaceCount ) );
		Assert.AreEqual( geometryHash, GeometryHash( model ) );
	}

	[TestMethod]
	public void OriginalTotemDetailsAndAnimationSignature()
	{
		var model = new ModelFile( new MemoryStream( ReadMember( "levels/jungle/rides/totem.wad", "totem.MD2" ) ) );
		Assert.AreEqual( "tp_ground", model.Nodes[model.RootNodeIndex].Name );
		var cart = model.Meshes.Single( mesh => mesh.Name == "tp_cart" );
		var cartDummies = model.Nodes.Where( node => node.ParentIndex == cart.NodeIndex && node.MeshIndex < 0 ).Select( node => node.Name ).ToArray();
		CollectionAssert.AreEquivalent( Enumerable.Range( 1, 12 ).Select( index => $"Head{index:00}" ).Append( "camera01" ).ToArray(), cartDummies );
		Assert.AreEqual( 5, model.Nodes.Count( node => node.ParentIndex == model.RootNodeIndex && node.MeshIndex < 0 ) );
		Assert.AreEqual( 18, model.DummyAttributes.Count );
		Assert.AreEqual( 5, model.Textures.Count( texture => texture.FrameNames[0].Equals( "jt_f1.TGA", StringComparison.Ordinal ) ) );

		var data = ReadMember( "levels/jungle/rides/totem.wad", "totemm1.MD2" );
		Assert.AreEqual( "6467B35FAADD40FEF639C2B10F3F0BB2754675F15509172778EF399B74AE76FE", Convert.ToHexString( SHA256.HashData( data ) ) );
		var animation = new ModelFile( new MemoryStream( data ) );
		Assert.AreEqual( ModelFileKind.Animation, animation.Kind );
		Assert.AreEqual( model.Counts, animation.Counts );
		Assert.AreEqual( model.BoundsMin, animation.BoundsMin );
		Assert.AreEqual( 0x4F0, animation.Animation!.Offset );
		Assert.AreEqual( 430u, animation.Animation.Words[2] );
	}

	/// <summary>
	/// Per mesh: name + NUL; per corner float32 position X/Y/Z and U/V; per face uint32 corner indices;
	/// per material int32 texture slot and uint16 start/end. Matches an independent Python calculation.
	/// </summary>
	private static string GeometryHash( ModelFile model )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream, Encoding.ASCII, true );
		foreach ( var mesh in model.Meshes )
		{
			writer.Write( Encoding.ASCII.GetBytes( mesh.Name ) );
			writer.Write( (byte)0 );
			for ( var corner = 0; corner < mesh.Vertices.Length; corner++ )
			{
				writer.Write( mesh.Vertices[corner].Position.X );
				writer.Write( mesh.Vertices[corner].Position.Y );
				writer.Write( mesh.Vertices[corner].Position.Z );
				writer.Write( mesh.TexCoords[corner].X );
				writer.Write( mesh.TexCoords[corner].Y );
			}
			foreach ( var index in mesh.Indices )
				writer.Write( index );
			foreach ( var material in mesh.Materials )
			{
				writer.Write( material.TextureIndex );
				writer.Write( material.StartIndex );
				writer.Write( material.EndIndex );
			}
		}
		writer.Flush();
		return Convert.ToHexString( SHA256.HashData( stream.ToArray() ) );
	}

	private static IEnumerable<(string Name, WadArchiveFile File)> Md2Members( ArchiveDirectory directory, string prefix )
	{
		foreach ( var child in directory.Children )
		{
			if ( child is ArchiveDirectory subdirectory )
			{
				foreach ( var entry in Md2Members( subdirectory, $"{prefix}{subdirectory.Name}/" ) )
					yield return entry;
			}
			else if ( child is WadArchiveFile file && file.Name!.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase ) )
			{
				yield return ($"{prefix}{file.Name}", file);
			}
		}
	}

	private static byte[] ReadMember( string wad, string member )
	{
		var path = Path.Combine( OriginalDataPath(), wad );
		if ( !File.Exists( path ) )
			Assert.Inconclusive( $"Original archive is missing: {wad}" );
		using var archive = new WadArchive( path );
		var file = Md2Members( archive.Root, "" ).Where( entry => entry.Name == member ).Select( entry => entry.File ).FirstOrDefault();
		if ( file == null )
			Assert.Inconclusive( $"Original member is missing: {wad}/{member}" );
		return file!.GetData();
	}

	private static string OriginalDataPath()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original MD2 corpus." );
		return Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
	}
}
