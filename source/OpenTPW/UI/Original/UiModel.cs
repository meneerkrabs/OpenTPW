using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.UI.Original;

/// <summary>One flattened vertex of an original UI model in the authored 2048×1536 canvas (y down).</summary>
public readonly record struct UiModelVertex( NVector2 Position, float Depth, NVector2 TexCoords );

/// <summary>Triangles of one texture inside a <see cref="UiModelFrame"/>.</summary>
public sealed record UiModelPart( string TextureName, IReadOnlyList<UiModelVertex> Vertices, IReadOnlyList<int> Indices );

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
}

/// <summary>
/// CPU view of an original UI model (docs/UI.md). Evidence from all 278 <c>ui.wad</c> models:
/// full-screen frames span x 0..2048 and y 0..−1536, so the authoring space is a 2048×1536 virtual
/// screen with y pointing up; HUD and lobby models are stored at their on-screen place (the root
/// node translation), generic buttons and windows at the origin. Child nodes are alternative state
/// frames: each child is translated one button width further along x from its parent in the authoring
/// scene, so neither composed nor uncomposed matrices place it on screen. A child frame is therefore
/// drawn with its own rotation/scale but the root's pose, which puts every state exactly over the root
/// mesh. (The header bounds of UI files equal the box of each node's matrix applied alone, which is
/// what docs/MD2-MODELS.md recorded as "matching uncomposed matrices".) Texture V runs bottom-up
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
		foreach ( var node in OrderedNodes( model ) )
		{
			if ( node.MeshIndex < 0 || node.MeshIndex >= model.Meshes.Count )
				continue;
			// [APPROX:UI-003] state frames use the root pose (child translation dropped) — evidence needed: binary UI model drawing code
			var transform = node.Index == root.Index ? root.Transform : WithoutTranslation( node.Transform ) * root.Transform;
			var frame = Flatten( model.Meshes[node.MeshIndex], node.Name, transform );
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

	private static NMatrix4x4 WithoutTranslation( NMatrix4x4 matrix )
	{
		matrix.M41 = 0;
		matrix.M42 = 0;
		matrix.M43 = 0;
		return matrix;
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
		// [APPROX:UI-004] back-to-front by Z per texture group — evidence needed: binary or capture of overlapping UI parts
		// Group triangles by material texture and order groups back to front (lower Z first), as the
		// panels layer flat inner faces over their bevels.
		var groups = new Dictionary<string, List<(float Depth, int A, int B, int C)>>( StringComparer.OrdinalIgnoreCase );
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
			list.Add( ((vertices[a].Depth + vertices[b].Depth + vertices[c].Depth) / 3, a, b, c) );
		}
		var parts = groups.Select( group =>
		{
			var triangles = group.Value.OrderBy( triangle => triangle.Depth ).ToList();
			return (Depth: triangles.Average( triangle => triangle.Depth ), Part: new UiModelPart( group.Key, vertices,
				triangles.SelectMany( triangle => new[] { triangle.A, triangle.B, triangle.C } ).ToArray() ));
		} ).OrderBy( part => part.Depth ).Select( part => part.Part ).ToArray();
		return new UiModelFrame( nodeName, parts, minX, minY, maxX, maxY );
	}
}
