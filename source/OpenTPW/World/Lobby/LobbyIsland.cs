using System.Numerics;

namespace OpenTPW;

public sealed class LobbyIsland : Entity
{
	public LobbyIsland( Vector3 _position, string themeName )
	{
		Position = _position;

		var modelPrefix = themeName[0..3];
		var modelFile = new ModelFile( $"lobby/terrain/{modelPrefix}_isle.md2" );
		var nodeTransforms = ModelAnimationPlayer.ComputeRestTransforms( modelFile );

		foreach ( var mesh in modelFile.Meshes )
		{
			var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
			var textures = new List<Texture>();

			for ( int i = 0; i < 16; ++i )
			{
				if ( mesh.Materials.Length <= i )
				{
					textures.Add( Texture.Missing );
				}
				else if ( mesh.Materials[i].TextureIndex < 0 )
				{
					// Untextured material (no texture pointer): render with the plain white texture.
					textures.Add( Texture.Missing );
				}
				else
				{
					var j = mesh.Materials[i];
					textures.Add( new Texture( $"lobby/terrain/textures/{j.Name}.wct", TextureFlags.Repeat ) );
				}
			}

			material.Set( $"Color", [.. textures] );

			var vertices = new List<Vertex>();
			for ( int i = 0; i < mesh.Vertices.Length; ++i )
			{
				vertices.Add( new Vertex()
				{
					Position = new Vector3( mesh.Vertices[i].Position.X, mesh.Vertices[i].Position.Z, mesh.Vertices[i].Position.Y ),
					Normal = new Vector3( mesh.Normals[i].X, mesh.Normals[i].Z, mesh.Normals[i].Y ),
					TexCoords = mesh.TexCoords[i],
					TexIndex = (int)mesh.Vertices[i].TextureIndex,
					MatFlags = mesh.Materials[(int)mesh.Vertices[i].TextureIndex].Flags
				} );
			}

			var model = new Model( [.. vertices], mesh.Indices, material );
			// Node matrices are parent-relative; swap Y/Z on both sides like the vertices.
			var swap = new Matrix4x4( 1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1 );
			var transform = swap * nodeTransforms[mesh.NodeIndex] * swap * Matrix4x4.CreateTranslation( Position.X, Position.Y, Position.Z - 2.5f );

			_ = new ModelEntity()
			{
				Model = model,
				TransformOverride = transform,
				Position = new Vector3( transform.M41, transform.M42, transform.M43 ),
			};
		}
	}
}
