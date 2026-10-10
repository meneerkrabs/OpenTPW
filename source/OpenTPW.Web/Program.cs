using System.Runtime.InteropServices.JavaScript;

namespace OpenTPW.Web;

/// <summary>
/// Feasibility spike (docs/WEB.md): the unchanged OpenTPW file readers running as WebAssembly in a
/// browser. The page copies a few of the player's own game files into the runtime's in-memory file
/// system; the readers then work exactly as on the desktop. Nothing from the game is shipped.
/// </summary>
public static partial class Program
{
	private const string Root = "/game/Data";

	public static void Main()
	{
		Log = new Logger();
		Console.WriteLine( "OpenTPW web spike: runtime ready." );
	}

	/// <summary>Stores one game file at its path below Data (e.g. levels/jungle/terrain.wad).</summary>
	[JSExport]
	public static void AddFile( string relativePath, byte[] data )
	{
		var path = Path.Combine( Root, relativePath );
		Directory.CreateDirectory( Path.GetDirectoryName( path )! );
		File.WriteAllBytes( path, data );
	}

	[JSExport]
	public static void Mount()
	{
		FileSystem = new BaseFileSystem( Root );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		FileSystem.RegisterArchiveHandler<SdtArchive>( ".sdt" );
	}

	/// <summary>The 128x128 attribute map of a level, one byte per cell, plus its title.</summary>
	[JSExport]
	public static byte[] MapCells( string level )
	{
		using var stream = FileSystem.OpenRead( $"/levels/{level}/terrain.wad/base.map" );
		var map = new MapFile( stream );
		Console.WriteLine( $"{map.Title}: {map.Width}x{map.Height}" );
		return map.Cells;
	}

	/// <summary>Triangles of an MD2 model as x,y,z triples in model space (mesh transforms applied).</summary>
	[JSExport]
	public static double[] ModelTriangles( string path )
	{
		var model = new ModelFile( path );
		var result = new List<double>();
		foreach ( var mesh in model.Meshes )
		{
			foreach ( var index in mesh.Indices )
			{
				var position = mesh.Vertices[index].Position;
				var world = System.Numerics.Vector3.Transform( new System.Numerics.Vector3( position.X, position.Y, position.Z ), mesh.TransformMatrix );
				result.Add( world.X );
				result.Add( world.Y );
				result.Add( world.Z );
			}
		}
		Console.WriteLine( $"{path}: {model.Meshes.Count} meshes, {result.Count / 9} triangles" );
		return result.ToArray();
	}

	/// <summary>Width and height of a decoded .wct texture.</summary>
	[JSExport]
	public static int[] TextureSize( string path )
	{
		var texture = new TextureFile( path ).Data;
		return new[] { texture.Width, texture.Height };
	}

	[JSExport]
	public static byte[] TexturePixels( string path ) => new TextureFile( path ).Data.Data;
}
