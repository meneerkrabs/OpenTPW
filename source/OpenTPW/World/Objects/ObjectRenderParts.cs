using System.Numerics;

namespace OpenTPW;

/// <summary>
/// A drawable piece of an object model: vertices in engine axes (MD2 Y/Z swapped), at most 16 texture
/// slots, and the node whose animated matrix moves it (-1: static, already baked into the vertices).
/// Unbaked parts name their mesh; their vertex <c>i</c> is corner <c>i</c> of that mesh (also for parts
/// split by material), so <see cref="ObjectRenderParts.WritePositions"/> can move them.
/// </summary>
public sealed record ObjectRenderPart( int NodeIndex, Vertex[] Vertices, uint[] Indices, string[] Textures, string Name, int MeshIndex = -1 );

/// <summary>
/// Splits an object model into render parts. Meshes on nodes that no animation of the object moves (nor any
/// ancestor of them) are baked with their rest hierarchy matrix and merged into as few static parts as the
/// 16-texture limit allows, so a ride needs few materials; animated meshes stay one part per mesh.
/// </summary>
public static class ObjectRenderParts
{
	public static bool[] FindAnimatedNodes( ObjectCatalogEntry entry, ModelFile model )
	{
		var animated = new bool[model.Nodes.Count];
		foreach ( var file in entry.Animations )
		{
			ModelAnimation? clip;
			try
			{
				clip = ObjectAssets.LoadModel( entry.FileSystem, file.Path ).Clip;
			}
			catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException )
			{
				continue;
			}
			if ( clip == null )
				continue;
			foreach ( var track in clip.Tracks.Where( track => track.NodeIndex < animated.Length ) )
				animated[track.NodeIndex] = true;
		}
		// Children of animated nodes move with them.
		var changed = true;
		while ( changed )
		{
			changed = false;
			foreach ( var node in model.Nodes )
			{
				if ( !animated[node.Index] && node.ParentIndex >= 0 && animated[node.ParentIndex] )
					changed = animated[node.Index] = true;
			}
		}
		return animated;
	}

	/// <summary>Meshes that a quantised vertex track of one of the object's animations moves.</summary>
	public static bool[] FindVertexAnimatedMeshes( ObjectCatalogEntry entry, ModelFile model )
	{
		var animated = new bool[model.Meshes.Count];
		foreach ( var file in entry.Animations )
		{
			ModelAnimation? clip;
			try
			{
				clip = ObjectAssets.LoadModel( entry.FileSystem, file.Path ).Clip;
			}
			catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException )
			{
				continue;
			}
			foreach ( var track in clip?.Tracks.Where( track => track.VertexAnimation != null ) ?? Enumerable.Empty<ModelAnimationTrack>() )
			{
				var mesh = model.Meshes.FindIndex( candidate => candidate.NodeIndex == track.NodeIndex );
				if ( mesh >= 0 )
					animated[mesh] = true;
			}
		}
		return animated;
	}

	/// <summary>
	/// Writes a mesh's positions (MD2 axes, indexed like <see cref="ModelFile.Mesh.Positions"/>; null for the
	/// stored ones) into an unbaked part's vertices through the corner order, swapping to engine axes.
	/// Normals keep their stored values: what the original does with them after a vertex pass (it sets
	/// node-state flag 0x10000) was not traced.
	/// </summary>
	public static void WritePositions( ModelFile.Mesh mesh, IReadOnlyList<System.Numerics.Vector3>? positions, Span<Vertex> vertices )
	{
		if ( vertices.Length != mesh.CornerPositionIndices.Length )
			throw new ArgumentException( "A part of an unbaked mesh has one vertex per corner.", nameof( vertices ) );
		if ( positions != null && positions.Count != mesh.Positions.Length )
			throw new ArgumentException( "Positions must match the mesh's stored positions.", nameof( positions ) );
		for ( var corner = 0; corner < vertices.Length; corner++ )
		{
			var index = mesh.CornerPositionIndices[corner];
			vertices[corner].Position = ObjectAssets.ConvertAxes( positions?[index] ?? mesh.Positions[index].GetSystemVector3() );
		}
	}

	public static IReadOnlyList<ObjectRenderPart> Build( ObjectCatalogEntry entry, ModelFile model )
	{
		var animated = FindAnimatedNodes( entry, model );
		var rest = ModelAnimationPlayer.ComputeRestTransforms( model );
		var parts = new List<ObjectRenderPart>();
		var batchVertices = new List<Vertex>();
		var batchIndices = new List<uint>();
		var batchTextures = new List<string>();
		void Flush()
		{
			if ( batchVertices.Count > 0 )
				parts.Add( new ObjectRenderPart( -1, batchVertices.ToArray(), batchIndices.ToArray(), batchTextures.ToArray(), $"{entry.ArchiveName} static {parts.Count}" ) );
			batchVertices.Clear();
			batchIndices.Clear();
			batchTextures.Clear();
		}
		for ( var meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++ )
		{
			var mesh = model.Meshes[meshIndex];
			if ( ObjectAssets.GetUnrenderableReason( mesh ) is "no geometry" or "no materials" )
				continue;
			var node = mesh.NodeIndex >= 0 && mesh.NodeIndex < model.Nodes.Count ? mesh.NodeIndex : model.RootNodeIndex;
			if ( node >= 0 && animated[node] )
			{
				foreach ( var (vertices, indices, textures) in ObjectAssets.ConvertMeshParts( mesh ) )
					parts.Add( new ObjectRenderPart( node, vertices, indices, textures, mesh.Name, meshIndex ) );
				continue;
			}
			var bake = node >= 0 ? rest[node] : Matrix4x4.Identity;
			foreach ( var (vertices, indices, textures) in ObjectAssets.ConvertMeshParts( mesh, bake ) )
			{
				if ( batchTextures.Union( textures, StringComparer.OrdinalIgnoreCase ).Count() > 16 )
					Flush();
				var slots = textures.Select( name =>
				{
					var slot = batchTextures.FindIndex( existing => string.Equals( existing, name, StringComparison.OrdinalIgnoreCase ) );
					if ( slot < 0 )
					{
						batchTextures.Add( name );
						slot = batchTextures.Count - 1;
					}
					return slot;
				} ).ToArray();
				var first = (uint)batchVertices.Count;
				foreach ( var vertex in vertices )
				{
					var copy = vertex;
					copy.TexIndex = slots[vertex.TexIndex];
					batchVertices.Add( copy );
				}
				foreach ( var index in indices )
					batchIndices.Add( first + index );
			}
		}
		Flush();
		return parts;
	}
}
