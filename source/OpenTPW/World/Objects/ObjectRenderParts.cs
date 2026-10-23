using System.Numerics;

namespace OpenTPW;

/// <summary>
/// A drawable piece of an object model: vertices in engine axes (MD2 Y/Z swapped), at most 16 texture
/// slots, and the node whose animated matrix moves it (-1: static, already baked into the vertices).
/// Unbaked parts name their mesh; their vertex <c>i</c> is corner <c>i</c> of that mesh (also for parts
/// split by material), so <see cref="ObjectRenderParts.WritePositions"/> can move them.
/// <see cref="TextureSlots"/> parallels <see cref="Textures"/> with the MD2 texture-slot index each name
/// came from (-1 when it did not come from a material), so a texture-frame track of that slot can swap
/// the rendered frame (ObjectAnimator.GetTextureFrame).
/// </summary>
public sealed record ObjectRenderPart( int NodeIndex, Vertex[] Vertices, uint[] Indices, string[] Textures, string Name, int MeshIndex = -1, int[]? TextureSlots = null );

/// <summary>
/// Splits an object model into render parts. Meshes on nodes that no animation of the object moves (nor any
/// ancestor of them) are baked with their rest hierarchy matrix and merged into as few static parts as the
/// 16-texture limit allows, so a ride needs few materials; animated meshes stay one part per mesh.
/// </summary>
public static class ObjectRenderParts
{
	public static bool[] FindAnimatedNodes( ObjectCatalogEntry entry, ModelFile model ) => FindAnimatedNodes( entry, model, entry.Animations );

	/// <summary>The nodes that one of <paramref name="animations"/> (members of <paramref name="entry"/>'s archive) moves, with their descendants.</summary>
	public static bool[] FindAnimatedNodes( ObjectCatalogEntry entry, ModelFile model, IReadOnlyList<ObjectAnimationFile> animations )
	{
		var animated = new bool[model.Nodes.Count];
		foreach ( var file in animations )
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
			// Listed nodes get node-state bit 0x10 at the clip's start (ObjectAnimator.IsNodeHidden), so they stay separate parts.
			foreach ( var node in clip.NodeList.Where( node => node < animated.Length ) )
				animated[node] = true;
		}
		// A track add-on's open ends are shown and hidden by the circuit walk (TrackAddOns.OpenEndsOf), so they stay separate parts too.
		if ( TrackAddOns.IsTrackAddOn( entry ) )
		{
			foreach ( var node in new[] { TrackAddOns.EndNode( model, TrackAddOns.EndBehind ), TrackAddOns.EndNode( model, TrackAddOns.EndAhead ) } )
				if ( node >= 0 )
					animated[node] = true;
		}
		// [EXT:evening-torches-flicker] a node the evening lamp table names as a flame (a torch's) is moved and hidden by the evening extension each frame (EveningFlames), so it stays a separate part too
		if ( EveningLampTable.Shipped.Find( entry ) is { Flame: { } flame } && model.Nodes.FirstOrDefault( node => string.Equals( node.Name, flame, StringComparison.OrdinalIgnoreCase ) ) is { } flameNode && flameNode.Index < animated.Length )
			animated[flameNode.Index] = true;
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

	/// <summary>Meshes whose positions or texture coordinates a vertex or UV track of one of the object's animations changes.</summary>
	public static bool[] FindVertexAnimatedMeshes( ObjectCatalogEntry entry, ModelFile model ) => FindVertexAnimatedMeshes( entry, model, entry.Animations );

	/// <summary>Meshes whose positions or texture coordinates a vertex or UV track of one of <paramref name="animations"/> changes.</summary>
	public static bool[] FindVertexAnimatedMeshes( ObjectCatalogEntry entry, ModelFile model, IReadOnlyList<ObjectAnimationFile> animations )
	{
		var animated = new bool[model.Meshes.Count];
		foreach ( var file in animations )
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
			foreach ( var track in clip?.Tracks.Where( track => track.VertexAnimation != null || track.UvAnimation != null ) ?? Enumerable.Empty<ModelAnimationTrack>() )
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

	/// <summary>
	/// Writes a mesh's texture coordinates (indexed like <see cref="ModelFile.Mesh.TexCoords"/>; null for the stored
	/// ones) into an unbaked part's vertices, corner by corner.
	/// </summary>
	public static void WriteTexCoords( ModelFile.Mesh mesh, IReadOnlyList<System.Numerics.Vector2>? texCoords, Span<Vertex> vertices )
	{
		if ( vertices.Length != mesh.CornerPositionIndices.Length )
			throw new ArgumentException( "A part of an unbaked mesh has one vertex per corner.", nameof( vertices ) );
		if ( texCoords != null && texCoords.Count != mesh.TexCoords.Length )
			throw new ArgumentException( "Texture coordinates must match the mesh's corners.", nameof( texCoords ) );
		for ( var corner = 0; corner < vertices.Length; corner++ )
		{
			var value = texCoords?[corner];
			vertices[corner].TexCoords = value is { } animated ? new Vector2( animated.X, animated.Y ) : mesh.TexCoords[corner];
		}
	}

	public static IReadOnlyList<ObjectRenderPart> Build( ObjectCatalogEntry entry, ModelFile model ) => Build( entry, model, entry.Animations );

	/// <summary>
	/// The texture names the parts of <paramref name="model"/> draw (the <see cref="ObjectRenderPart.Textures"/> of <see cref="Build"/>'s parts, each once, case ignored), without
	/// building them: the materials of the meshes <see cref="Build"/> keeps, in the runs <see cref="ObjectAssets.ConvertMeshParts"/> makes parts of
	/// (<see cref="ObjectAssets.PartMaterialRanges"/>). Which parts batch together changes no name.
	/// </summary>
	public static IReadOnlyList<string> TextureNames( ModelFile model )
	{
		var names = new List<string>();
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var mesh in model.Meshes )
		{
			if ( ObjectAssets.GetUnrenderableReason( mesh ) is "no geometry" or "no materials" )
				continue;
			foreach ( var (first, count) in ObjectAssets.PartMaterialRanges( mesh ) )
				for ( var material = first; material < first + count; material++ )
					if ( seen.Add( mesh.Materials[material].Name ) )
						names.Add( mesh.Materials[material].Name );
		}
		return names;
	}

	/// <summary>The render parts of <paramref name="model"/> with the nodes <paramref name="animations"/> move kept apart (a ride's supplemental mesh with its own clips).</summary>
	public static IReadOnlyList<ObjectRenderPart> Build( ObjectCatalogEntry entry, ModelFile model, IReadOnlyList<ObjectAnimationFile> animations )
	{
		var animated = FindAnimatedNodes( entry, model, animations );
		var rest = ModelAnimationPlayer.ComputeRestTransforms( model );
		var parts = new List<ObjectRenderPart>();
		var batchVertices = new List<Vertex>();
		var batchIndices = new List<uint>();
		var batchTextures = new List<string>();
		// The MD2 texture-slot index of every batched name (-1 when it did not come from a material).
		var batchSlots = new List<int>();
		void Flush()
		{
			if ( batchVertices.Count > 0 )
				parts.Add( new ObjectRenderPart( -1, batchVertices.ToArray(), batchIndices.ToArray(), batchTextures.ToArray(), $"{entry.ArchiveName} static {parts.Count}", -1, batchSlots.ToArray() ) );
			batchVertices.Clear();
			batchIndices.Clear();
			batchTextures.Clear();
			batchSlots.Clear();
		}
		for ( var meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++ )
		{
			var mesh = model.Meshes[meshIndex];
			if ( ObjectAssets.GetUnrenderableReason( mesh ) is "no geometry" or "no materials" )
				continue;
			var node = mesh.NodeIndex >= 0 && mesh.NodeIndex < model.Nodes.Count ? mesh.NodeIndex : model.RootNodeIndex;
			if ( node >= 0 && animated[node] )
			{
				foreach ( var (vertices, indices, textures, slots) in ObjectAssets.ConvertMeshParts( mesh ) )
					parts.Add( new ObjectRenderPart( node, vertices, indices, textures, mesh.Name, meshIndex, slots ) );
				continue;
			}
			var bake = node >= 0 ? rest[node] : Matrix4x4.Identity;
			foreach ( var (vertices, indices, textures, slots) in ObjectAssets.ConvertMeshParts( mesh, bake ) )
			{
				if ( batchTextures.Select( ( name, index ) => (batchSlots[index], name.ToUpperInvariant()) )
					.Concat( textures.Select( ( name, index ) => (slots[index], name.ToUpperInvariant()) ) ).Distinct().Count() > 16 )
					Flush();
				var remapped = textures.Select( ( name, material) =>
				{
					var slot = -1;
					for ( var index = 0; index < batchTextures.Count; index++ )
						if ( batchSlots[index] == slots[material] && string.Equals( batchTextures[index], name, StringComparison.OrdinalIgnoreCase ) )
						{
							slot = index;
							break;
						}
					if ( slot < 0 )
					{
						batchTextures.Add( name );
						// Slots with identical first-frame art can animate independently.
						batchSlots.Add( slots[material] );
						slot = batchTextures.Count - 1;
					}
					return slot;
				} ).ToArray();
				var first = (uint)batchVertices.Count;
				foreach ( var vertex in vertices )
				{
					var copy = vertex;
					copy.TexIndex = remapped[vertex.TexIndex];
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
