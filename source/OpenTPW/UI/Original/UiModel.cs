using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.UI.Original;

/// <summary>One flattened vertex of an original UI model in the authored 2048×1536 canvas (y down).</summary>
/// <remarks><c>Depth</c> is the model's z after the node matrices; the original draws UI models at depth 0 whatever it is, so nothing orders by it.</remarks>
public readonly record struct UiModelVertex( NVector2 Position, float Depth, NVector2 TexCoords );

/// <summary>Triangles of one texture inside a <see cref="UiModelFrame"/>.</summary>
/// <param name="Transparent">The texture slot sets flag 0x2 (<see cref="UiImages.TransparentFlag"/>).</param>
public sealed record UiModelPart( string TextureName, IReadOnlyList<UiModelVertex> Vertices, IReadOnlyList<int> Indices, bool Transparent = false, IReadOnlyList<int>? DrawnIndices = null )
{
	/// <summary>The triangles tp.exe draws: those <see cref="UiModel.IsClockwise"/> keeps (0x581320); <see cref="Indices"/> stays every triangle the file stores, for shape picking.</summary>
	public IReadOnlyList<int> DrawIndices => DrawnIndices ?? Indices;
}

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
/// matrix (translations included, as the instance traverser 0x57A530 accumulates them); the window angle is applied at draw time (<see cref="UiContext.DrawFrame(UiModelFrame, UiRect, RgbaByte?, float, System.Numerics.Vector2)"/>), and the state is the chain node <see cref="TryGetState"/> selects (UI-003). Texture V runs bottom-up
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
	/// <summary>The control states: the nodes down the first-child chain from the root in order, a node without a drawable mesh as null (<see cref="TryGetState"/>).</summary>
	public IReadOnlyList<UiModelFrame?> States { get; }
	private readonly IReadOnlyList<UiModelFrame> builtFrames;
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
		var states = new List<UiModelFrame?>();
		// [BIN:TPW-EXE:0x57a530 instance traverser] a node's world matrix is its local matrix (node+0x10) combined with the accumulated matrices of its ancestors (the root starts as its local matrix times the caller's, the identity set at 0x57a55b is overwritten, children are visited at node+0xc with the accumulated matrices, 0x57a6ec); no corpus child has a rotation or scale, only translations, which cancel in the selected-mesh fit
		// [BIN:TPW-EXE:0x476e80 state selection] the state of a control is the node reached by walking [model+0x78] (the root) down the first-child link (+0xc) [control+0xc] + [control+0xa] times; the constructor 0x476a90 hides every node of that chain (flags |= 0xc8010) and the selection shows the chosen one (flags &= ~0x8010). A walk that runs out of nodes selects nothing: the control draws no frame
		var worlds = new Dictionary<int, NMatrix4x4>();
		foreach ( var node in ChainNodes( model ) )
		{
			worlds[node.Index] = node.Index == root.Index || !worlds.TryGetValue( node.ParentIndex, out var parentWorld )
				? node.Transform
				: node.Transform * parentWorld;
			UiModelFrame? frame = null;
			if ( node.MeshIndex >= 0 && node.MeshIndex < model.Meshes.Count )
				frame = Flatten( model.Meshes[node.MeshIndex], node.Name, worlds[node.Index] );
			states.Add( frame );
			if ( frame != null )
				frames.Add( frame );
		}
		if ( frames.Count == 0 )
			throw new InvalidDataException( $"UI model {name} has no drawable mesh." );
		Frames = builtFrames = frames;
		States = states;
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

	/// <summary>The frame of control state <paramref name="index"/> as tp.exe selects it (0x476e80): false when the chain has no such node or the node has no mesh, and the control then draws nothing.</summary>
	public bool TryGetState( int index, out UiModelFrame frame )
	{
		// a model whose frame list was replaced after construction (tests build synthetic models that way) has no chain of its own: its frames are its states
		var states = ReferenceEquals( Frames, builtFrames ) ? States : Frames;
		frame = index >= 0 && index < states.Count ? states[index]! : null!;
		return frame != null;
	}

	/// <summary>
	/// Frame for a button state: the index-th drawable frame, the first one for an index past them. [APPROX:UI-003] tp.exe's control draws nothing for a state the chain lacks (<see cref="TryGetState"/>); the applier 0x668AD3 sends
	/// 0x4C000 to the control's slot 3 (0x476F10 -> 0x476E80), which reads the state index only with bit 0x1 (-> +0xC) and the base only with bit 0x80000 (-> +0xA), and the button sets neither — evidence needed: which caller passes 0x1 or 0x80000 to 0x476F10 for model buttons
	/// </summary>
	public UiModelFrame GetFrame( int index ) => Frames[index >= 0 && index < Frames.Count ? index : 0];

	/// <summary>
	/// Whether a triangle of canvas points (y down) is strictly clockwise as tp.exe's face test 0x581320 decides it (w = 1: (y1 - y0) x2 - y1 x0 < (y2 - y0) x1 - y2 x0, every product and difference rounded to a single; ORACLE.md X70).
	/// It is evaluated on model-space (canvas) positions where the original evaluates screen positions with the rhw (0x579690 at 0x5796C7 and 0x57974E); the two agree except for near-collinear slivers within rounding, because the control transform is a translation, a positive scale and a rotation with a constant rhw (m22 = 0). A NaN coordinate gives 1 (kept) in the original, 0 here.
	/// </summary>
	public static bool IsClockwise( NVector2 a, NVector2 b, NVector2 c )
	{
		float left = (b.Y - a.Y) * c.X;
		float leftTail = b.Y * a.X;
		float right = (c.Y - a.Y) * b.X;
		float rightTail = c.Y * a.X;
		return left - leftTail < right - rightTail;
	}

	/// <summary>The root, then each node's first child (the lowest index among its children, as the file lists them) down the chain: the nodes a control's state counts through (0x476e80).</summary>
	private static IEnumerable<ModelNode> ChainNodes( ModelFile model )
	{
		var visited = new HashSet<int>();
		var index = model.RootNodeIndex;
		while ( index >= 0 && visited.Add( index ) )
		{
			yield return model.Nodes[index];
			var child = -1;
			foreach ( var node in model.Nodes )
				if ( node.ParentIndex == index && (child < 0 || node.Index < child) )
					child = node.Index;
			index = child;
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
		// [APPROX:UI-004] items are not grouped by texture cache page (width class), the stashed items of a frame are not permuted among themselves and the sprite batches keep call order (docs/UI.md) — evidence needed: which textures share a cache page (the placement 0x58F190 with 0x58EAE0 and 0x58ECA0, shared with the world's textures), the order and keys the frame's stashed records reach the sort in (0x582AD0 -> 0x5829E0) and which sprite flush the control text goes through
		// [BIN:TPW-EXE:0x476CBF control matrix] every vertex of a UI control model ends at depth 0: the control matrix 0x476C50 stores m22 = 0 (and no z translation), so the clip z of a vertex is 0 whatever the model's z; the part
		// carries flag 0x2000000 (the constructor 0x476AD3 sets model flag 0x200, the instance draw 0x57A750 adds 0x2000000 to 0x8BB624, 0x57A6BF copies it) and 0x579690 maps such a vertex to z' = [0x8BCC20] (0.1) x z = 0. The model's own
		// z (the node translations of the layered HUD models) therefore decides nothing: there is no depth order and no back-to-front sort, only the order the batcher draws the material runs in.
		// [BIN:TPW-EXE:0x581DB0 material runs] the batcher walks a part's materials as runs (material +8..+0xA the vertices, +4/+6 the first triangle and count in the mesh's triangle table [mesh+0x70], 0x580D86..0x580D9D) and every run is one item; the items are grouped by the texture's cache page (the node key is the slot's +0x1C, the page surface, with slot flag 0x1000 set by the page placement 0x590350; pages split by width class, 0x590CF0, 0x58F190), the nodes come out in creation order from the last part and last material, and a part flagged 0x2000000 goes to the node's deferred chain, a stack: within one page the parts' list order and the materials' index order, the triangles of a run in file order. Between pages the texture met last in list order draws first, and the list is the scene list, newest shown first (0x476BF0 -> 0x45BD30 -> 0x576410 -> 0x576270, X82), so pages of their own draw in show order: OpenTPW keeps the call order
		var parts = new List<UiModelPart>();
		var runs = new List<int>[mesh.Materials.Length];
		for ( var face = 0; face + 2 < mesh.Indices.Length; face += 3 )
		{
			var a = (int)mesh.Indices[face];
			var b = (int)mesh.Indices[face + 1];
			var c = (int)mesh.Indices[face + 2];
			if ( a >= vertices.Length || b >= vertices.Length || c >= vertices.Length )
				continue;
			var index = (int)Math.Min( mesh.Vertices[a].TextureIndex, (uint)mesh.Materials.Length - 1 );
			( runs[index] ??= new() ).Add( face );
		}
		for ( var index = 0; index < runs.Length; index++ )
		{
			if ( runs[index] is not { } faces )
				continue;
			var material = mesh.Materials[index];
			var indices = new int[faces.Count * 3];
			var drawn = new List<int>( indices.Length );
			for ( var at = 0; at < faces.Count; at++ )
			{
				for ( var corner = 0; corner < 3; corner++ )
					indices[at * 3 + corner] = (int)mesh.Indices[faces[at] + corner];
				// [BIN:TPW-EXE:0x581320 face test] the primitive builder (0x580530: 0x580FFB, 0x580EAB) keeps a triangle only when it is strictly clockwise on the screen; the others, degenerate ones included, never reach a draw
				if ( IsClockwise( vertices[indices[at * 3]].Position, vertices[indices[at * 3 + 1]].Position, vertices[indices[at * 3 + 2]].Position ) )
					drawn.AddRange( new ArraySegment<int>( indices, at * 3, 3 ) );
			}
			parts.Add( new UiModelPart( material.TextureIndex < 0 ? "" : material.Name, vertices, indices, (material.Flags & UiImages.TransparentFlag) != 0, drawn.Count == indices.Length ? null : drawn.ToArray() ) );
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
