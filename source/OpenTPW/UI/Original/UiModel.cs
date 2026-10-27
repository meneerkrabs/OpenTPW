using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.UI.Original;

/// <summary>One flattened vertex of an original UI model in the authored 2048×1536 canvas (y down).</summary>
public readonly record struct UiModelVertex( NVector2 Position, float Depth, NVector2 TexCoords );

/// <summary>Triangles of one texture inside a <see cref="UiModelFrame"/>.</summary>
/// <param name="Transparent">The texture slot sets flag 0x2 (<see cref="UiImages.TransparentFlag"/>).</param>
public sealed record UiModelPart( string TextureName, IReadOnlyList<UiModelVertex> Vertices, IReadOnlyList<int> Indices, bool Transparent = false );

/// <summary>
/// One drawable frame of an original <c>ui.wad</c> model. Frame 0 is the root mesh; further frames are
/// the alternative states stored as child meshes (for buttons: disabled, highlighted, highlighted+down,
/// held down, down). Coordinates are in the authored 2048×1536 canvas, x right and y down.
/// </summary>
public sealed record UiModelFrame( string NodeName, IReadOnlyList<UiModelPart> Parts, float MinX, float MinY, float MaxX, float MaxY )
{
	public float Width => MaxX - MinX;
	public float Height => MaxY - MinY;
	public NVector2 Center => new( (MinX + MaxX) / 2, (MinY + MaxY) / 2 );

	/// <summary>
	/// Selected mesh's stored bound endpoints, transformed and ordered in the same y-down space as
	/// the vertices. Used only to fit a control and invert its shape pick; geometric bounds stay separate.
	/// </summary>
	public UiRect? FitBounds { get; init; }

	/// <summary>Shared drawing/picking fit, with geometric fallback for manual frames or unusable stored bounds.</summary>
	internal bool TryGetFitBounds( out UiRect bounds )
	{
		// Host robustness, not a claim about the original's malformed-input behavior. Do not impose
		// an epsilon on small positive extents or change signed destination-rectangle drawing.
		static bool Usable( UiRect rect ) => float.IsFinite( rect.X ) && float.IsFinite( rect.Y )
			&& float.IsFinite( rect.Width ) && float.IsFinite( rect.Height ) && rect.Width > 0 && rect.Height > 0
			&& float.IsFinite( rect.Right ) && float.IsFinite( rect.Bottom );
		bounds = FitBounds is { } fit && Usable( fit ) ? fit : new UiRect( MinX, MinY, Width, Height );
		return Usable( bounds );
	}
}

/// <summary>
/// CPU view of an original UI model (docs/UI.md). Evidence from all 278 <c>ui.wad</c> models:
/// full-screen frames span x 0..2048 and y 0..−1536, so the authoring space is a 2048×1536 virtual
/// screen with y pointing up; HUD and lobby models are stored at their on-screen place (the root
/// node translation), generic buttons and windows at the origin. Child nodes are alternative state
/// frames. Native drawing composes ancestors, then fits the selected mesh's stored bound endpoints
/// to the control; a common XY translation of geometry and endpoints cancels in that fit. Each frame's
/// geometry and bound endpoints use the node's world matrix, its local matrix times its parent's world
/// matrix (translations included, as the instance traverser 0x57A530 accumulates them); the control
/// rotation of the original draw is not read (UI-003). Texture V runs bottom-up
/// (as in the 3D shaders): button art occupies the top-left of its texture while the UVs span
/// v 0.42..1, so V is flipped here.
/// </summary>
public sealed class UiModel
{
	/// <summary>Asset filename/display alias; this is not the original drawing registry key.</summary>
	public string Name { get; }
	/// <summary>Exact stored root-node name, including case and spaces.</summary>
	public string RootNodeName { get; }
	/// <summary>Original signed-byte XOR/multiply-47 drawing key.</summary>
	public int DrawingKey { get; }
	public IReadOnlyList<UiModelFrame> Frames { get; }
	/// <summary>Root translation in canvas pixels (x right, y down): the authored on-screen centre.</summary>
	public NVector2 AuthoredCenter { get; }
	/// <summary>True when the root sits at the origin, i.e. the code positions the model.</summary>
	public bool IsPositionedByCode => AuthoredCenter.LengthSquared() < 1e-3f;

	public UiModel( ModelFile model, string name )
	{
		ArgumentNullException.ThrowIfNull( model );
		Name = name;
		if ( model.Kind != ModelFileKind.Geometry || model.RootNodeIndex < 0 )
			throw new InvalidDataException( $"UI model {name} has no geometry." );
		var root = model.Nodes[model.RootNodeIndex];
		RootNodeName = root.Name;
		DrawingKey = UiModels.RootNameKey( RootNodeName );
		AuthoredCenter = new NVector2( root.Transform.M41, -root.Transform.M42 );
		var frames = new List<UiModelFrame>();
		// [BIN:TPW-EXE:0x57a530 instance traverser] a node's world matrix is its local matrix (node+0x10) combined with the accumulated matrices of its ancestors (the root starts from identity at 0x57a55b, children are visited at node+0xc with the accumulated matrices, 0x57a6ec); no corpus child has a rotation or scale, only translations, which cancel in the selected-mesh fit
		var worlds = new Dictionary<int, NMatrix4x4>();
		foreach ( var node in OrderedNodes( model ) )
		{
			worlds[node.Index] = node.Index == root.Index || !worlds.TryGetValue( node.ParentIndex, out var parentWorld )
				? node.Transform
				: node.Transform * parentWorld;
			if ( node.MeshIndex < 0 || node.MeshIndex >= model.Meshes.Count )
				continue;
			// [APPROX:UI-003] the control rotation of the original draw is not read (no rotation field was seen in the traverser 0x57a530); a UI model is flattened without one — evidence needed: the UI control draw's use of a rotation, if any
			var frame = Flatten( model.Meshes[node.MeshIndex], node.Name, worlds[node.Index] );
			if ( frame != null )
				frames.Add( frame );
		}
		if ( frames.Count == 0 )
			throw new InvalidDataException( $"UI model {name} has no drawable mesh." );
		Frames = frames;
	}

	/// <summary>Loads an explicitly named asset; original drawing-key lookup is provided by <see cref="UiModels"/>.</summary>
	public static UiModel Load( string name )
	{
		var candidates = FileSystem.GetFiles( "/ui" ).Where( path => string.Equals( Path.GetFileNameWithoutExtension( path ), name, StringComparison.OrdinalIgnoreCase )
			&& path.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase ) ).ToArray();
		var exact = candidates.Where( path => string.Equals( Path.GetFileNameWithoutExtension( path ), name, StringComparison.Ordinal ) ).ToArray();
		var selected = exact.Length > 0 ? exact : candidates;
		if ( selected.Length == 0 ) throw new FileNotFoundException( $"Original UI asset {name} is missing from ui.wad." );
		if ( selected.Length != 1 ) throw new UiModelBindingException( $"UI asset alias '{name}' is ambiguous: {string.Join( ", ", selected.OrderBy( path => path, StringComparer.Ordinal ) )}." );
		return LoadAssetPath( selected[0] );
	}

	internal static UiModel LoadAssetPath( string path ) => new( new ModelFile( path ), Path.GetFileNameWithoutExtension( path ) );

	/// <summary>Loads a model file of the host file system (a language folder's banner mesh, <see cref="GameLanguage.EnumerateMeshes"/>).</summary>
	internal static UiModel LoadHostPath( string path )
	{
		using var stream = File.Open( path, FileMode.Open, FileAccess.Read, FileShare.Read );
		return new( new ModelFile( stream ), Path.GetFileNameWithoutExtension( path ) );
	}

	/// <summary>Frame for a button state; missing states fall back to the root frame.</summary>
	public UiModelFrame GetFrame( int index ) => Frames[index >= 0 && index < Frames.Count ? index : 0];

	/// <summary>Root first, then the child chain in hierarchy order (the stored order of state frames).</summary>
	private static IEnumerable<ModelNode> OrderedNodes( ModelFile model )
	{
		var visited = new HashSet<int>();
		var queue = new Queue<int>();
		queue.Enqueue( model.RootNodeIndex );
		while ( queue.Count > 0 )
		{
			var index = queue.Dequeue();
			if ( !visited.Add( index ) )
				continue;
			yield return model.Nodes[index];
			foreach ( var child in model.Nodes.Where( node => node.ParentIndex == index ).OrderBy( node => node.Index ) )
				queue.Enqueue( child.Index );
		}
	}

	private static UiModelFrame? Flatten( ModelFile.Mesh mesh, string nodeName, NMatrix4x4 transform )
	{
		if ( mesh.Vertices.Length == 0 || mesh.Indices.Length < 3 || mesh.TexCoords.Length < mesh.Vertices.Length || mesh.Materials.Length == 0 )
			return null;
		var vertices = new UiModelVertex[mesh.Vertices.Length];
		float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
		for ( var index = 0; index < vertices.Length; index++ )
		{
			var position = NVector3.Transform( mesh.Vertices[index].Position.GetSystemVector3(), transform );
			var point = new NVector2( position.X, -position.Y );
			// [DATA:ui.wad:button UVs v 0.42..1 vs art in the top 58%] V flipped as in the 3D shaders
			vertices[index] = new UiModelVertex( point, position.Z, new NVector2( mesh.TexCoords[index].X, 1 - mesh.TexCoords[index].Y ) );
			minX = Math.Min( minX, point.X );
			minY = Math.Min( minY, point.Y );
			maxX = Math.Max( maxX, point.X );
			maxY = Math.Max( maxY, point.Y );
		}
		// [APPROX:UI-004] back-to-front by Z per texture group; the original depth-tests the parts of a UI node in file order (immediate states 0x1256/0x2756, 0x581db0) and defers blended parts far to near (0x5829e0), but how a UI vertex's depth is formed (0x87b1d8 +0xc) and whether the UI pass shares the scene's depth buffer are not read — evidence needed: the UI vertex transform's depth
		// Group triangles by material texture and order groups back to front (lower Z first), as the
		// panels layer flat inner faces over their bevels.
		var groups = new Dictionary<string, List<(float Depth, int A, int B, int C, bool Transparent)>>( StringComparer.OrdinalIgnoreCase );
		for ( var face = 0; face + 2 < mesh.Indices.Length; face += 3 )
		{
			var a = (int)mesh.Indices[face];
			var b = (int)mesh.Indices[face + 1];
			var c = (int)mesh.Indices[face + 2];
			if ( a >= vertices.Length || b >= vertices.Length || c >= vertices.Length )
				continue;
			var material = mesh.Materials[(int)Math.Min( mesh.Vertices[a].TextureIndex, (uint)mesh.Materials.Length - 1 )];
			var key = material.TextureIndex < 0 ? "" : material.Name;
			if ( !groups.TryGetValue( key, out var list ) )
				groups[key] = list = new();
			list.Add( ((vertices[a].Depth + vertices[b].Depth + vertices[c].Depth) / 3, a, b, c, (material.Flags & UiImages.TransparentFlag) != 0) );
		}
		var parts = new List<UiModelPart>();
		// Keep the existing texture-group and triangle painter order. A filename can belong to
		// distinct slots: split only adjacent flag runs, rather than applying one slot's key to all.
		foreach ( var group in groups.Select( group => (group.Key, Triangles: group.Value.OrderBy( triangle => triangle.Depth ).ToArray()) )
			.OrderBy( group => group.Triangles.Average( triangle => triangle.Depth ) ) )
		{
			var triangles = group.Triangles;
			for ( var start = 0; start < triangles.Length; )
			{
				var end = start + 1;
				while ( end < triangles.Length && triangles[end].Transparent == triangles[start].Transparent )
					end++;
				var indices = new int[(end - start) * 3];
				for ( var index = start; index < end; index++ )
				{
					indices[(index - start) * 3] = triangles[index].A;
					indices[(index - start) * 3 + 1] = triangles[index].B;
					indices[(index - start) * 3 + 2] = triangles[index].C;
				}
				parts.Add( new UiModelPart( group.Key, vertices, indices, triangles[start].Transparent ) );
				start = end;
			}
		}
		// [BIN:TPW-EXE:0x476fd3..0x47708f selected-mesh fitting] transform only the two stored
		// endpoints, then order X/Y. This is not the header box, a corner AABB or an eight-corner box.
		// Keep them in exactly the same flattened space as the vertices above.
		var first = NVector3.Transform( mesh.BoundsMin.GetSystemVector3(), transform );
		var second = NVector3.Transform( mesh.BoundsMax.GetSystemVector3(), transform );
		return new UiModelFrame( nodeName, parts.ToArray(), minX, minY, maxX, maxY )
		{
			FitBounds = new UiRect( Math.Min( first.X, second.X ), Math.Min( -first.Y, -second.Y ),
				Math.Abs( second.X - first.X ), Math.Abs( second.Y - first.Y ) )
		};
	}
}
