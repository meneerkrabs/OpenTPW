using OpenTPW.FrontEnd;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// The original 3D front-end lobby: the lobby terrain (<c>lobby/terrain/Base.MD2</c>: sea meshes and
/// heightfield) with the four theme islands and their gates placed at the ISLANDCAMERAPOSITION of
/// their index, turned by the ISLAND angle (docs/UI.md). Node transforms are composed up the
/// hierarchy (parent-relative, docs/MD2-MODELS.md); MD2 (x, y-up, z) maps to engine (x, z, y-up).
/// Flying meshes, rain, lightning and the online globe are not rendered.
/// </summary>
// [APPROX:UI-018] flying meshes, rain, lightning and island/gate animations are not drawn — evidence needed: binary/capture of the lobby
public sealed class LobbyScene : OriginalModelScene
{
	private const string TerrainDirectory = "/lobby/terrain";

	protected override string TextureDirectory => $"{TerrainDirectory}/textures";

	public LobbyScene( LobbyDefinition definition )
	{
		Definition = definition;
		try
		{
			var terrain = new ModelFile( $"{TerrainDirectory}/Base.MD2" );
			AddModel( terrain, NMatrix4x4.Identity );
			if ( terrain.Heightfield != null )
				AddHeightfield( terrain );
			foreach ( var island in definition.Islands )
			{
				var (x, z) = definition.PositionOf( island );
				var placement = NMatrix4x4.CreateRotationY( island.Angle * MathF.PI / 180 ) * NMatrix4x4.CreateTranslation( x, 0, z );
				foreach ( var name in new[] { island.IslandModel, island.GateModel } )
				{
					var file = FindModel( name );
					if ( file != null )
						AddModel( new ModelFile( file ), placement );
				}
				IslandCount++;
			}
		}
		catch
		{
			Delete();
			throw;
		}
	}

	public LobbyDefinition Definition { get; }
	public int IslandCount { get; private set; }

	/// <summary>Engine-space centre of an island's camera orbit (its position at the ISLAND height).</summary>
	public Vector3 Target( LobbyIslandInfo island )
	{
		var (x, z) = Definition.PositionOf( island );
		return new Vector3( x, z, island.Height );
	}

	private static string? FindModel( string name )
	{
		try
		{
			return FileSystem.GetFiles( TerrainDirectory ).FirstOrDefault( file => file.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase )
				&& string.Equals( Path.GetFileNameWithoutExtension( file ), name, StringComparison.OrdinalIgnoreCase ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
		{
			Log?.Warning( $"Lobby model {name}: {exception.Message}" );
			return null;
		}
	}

	/// <summary>The lobby heightfield (111×110 cells) with each cell's ground texture slot, like the park terrain.</summary>
	private void AddHeightfield( ModelFile model )
	{
		var field = model.Heightfield!;
		var node = model.Nodes.FirstOrDefault( candidate => string.Equals( candidate.Name, "heightfield", StringComparison.OrdinalIgnoreCase ) );
		var world = node == null ? NMatrix4x4.Identity : OriginalTerrain.GetWorldMatrix( model, node.Index );
		var names = new List<string>();
		var vertices = new List<Vertex>();
		var indices = new List<uint>();
		for ( var z = 0; z < field.CellCountZ; z++ )
		{
			for ( var x = 0; x < field.CellCountX; x++ )
			{
				var slot = field.GetCellTextureSlot( x, z );
				if ( slot < 0 || slot >= model.Textures.Count )
					continue;
				var name = Path.GetFileNameWithoutExtension( model.Textures[slot].FrameNames[0] );
				var textureIndex = names.FindIndex( existing => string.Equals( existing, name, StringComparison.OrdinalIgnoreCase ) );
				if ( textureIndex < 0 )
				{
					if ( names.Count >= MaximumTextureSlots )
						continue;
					names.Add( name );
					textureIndex = names.Count - 1;
				}
				var start = (uint)vertices.Count;
				var corners = new[] { (x, z, 0f, 0f), (x + 1, z, 1f, 0f), (x, z + 1, 0f, 1f), (x + 1, z + 1, 1f, 1f) };
				var positions = corners.Select( corner => ToEngine( NVector3.Transform(
					new NVector3( corner.Item1 * field.CellSizeX, field.GetCornerHeight( corner.Item1, corner.Item2 ), corner.Item2 * field.CellSizeZ ), world ) ) ).ToArray();
				var normal = NVector3.Normalize( NVector3.Cross( positions[1] - positions[0], positions[2] - positions[0] ) );
				if ( normal.Z < 0 )
					normal = -normal;
				for ( var corner = 0; corner < 4; corner++ )
					vertices.Add( new Vertex { Position = positions[corner], Normal = normal, TexCoords = new Vector2( corners[corner].Item3, corners[corner].Item4 ), TexIndex = textureIndex } );
				foreach ( var offset in new uint[] { 0, 2, 3, 3, 1, 0 } )
					indices.Add( start + offset );
			}
		}
		Flush( names, vertices, indices );
	}

}
