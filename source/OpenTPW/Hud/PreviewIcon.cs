using OpenTPW.UI.Original;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Hud;

/// <summary>
/// Build-menu icon from an object's original preview model (<c>P&lt;name&gt;.MD2</c>, see <c>Info.PreviewAnimType</c>/<c>PreviewAnimNum</c> in
/// Rides.sam). The original shows these previews turning in the buy window: 0x486410 makes a preview object with 0x4689f0 and 0x468e50
/// rebuilds its world matrix every frame. OpenTPW projects the model's corners on the CPU with that matrix, keeps the triangles the original's
/// face test keeps, and draws them depth tested into an image (<see cref="UiDepthImage"/>), which the interface draws where the preview stands;
/// the buy window's preview plays the model's animation 5 as the original's does.
/// </summary>
// [BIN:TPW-EXE:0x45bd30 preview camera] the preview object has no park camera: 0x4689f0 hands 0x45bd30 an identity matrix (0x46f150), which
// 0x57a260 multiplies by diag(1, -1, 1, 1) into the instance's own view (+0x10), and the scene's draw 0x57a750 takes that view instead of the
// park camera's for a model with flag 0x200 (set by 0x45bed0), with the draw flag 0x2000000 (0x57a890); the frame's matrix then reaches the
// screen through the viewport alone: 0x579690 maps x and y with the viewport's half size and centre (0x582f70, 0x8bcc00 and 0x8bcbf0) and,
// for the flag 0x2000000 (mode 2), stores the depth as the matrix's depth times [0x8bcc20] = 0.1 (set by 0x582ddf) without the perspective divide
// [BIN:TPW-EXE:0x581320 face test] the batcher's primitive builder 0x580530 keeps a triangle only when the determinant of its corners' x, y
// and w after the view (0x581320) is positive, i.e. when it runs clockwise on the screen; the Direct3D cull mode stays D3DCULL_NONE
// (0x566957) because the engine has already dropped the others
// [EXT:preview-icon-cpu-projection] the preview's corners are projected on the CPU with the original's matrix, view and viewport, and its kept triangles are
// drawn with a depth buffer into an image covering them, which the interface draws in its own pass, rather than as an instance in the engine's scene draw;
// the object windows' previews keep OpenTPW's orthographic 30 degree view centred in their frame
// [APPROX:UI-026] the original lights the preview's vertices in its 3D object pipeline (0x57aa10 -> 0x574660 -> 0x5741b0: ambient plus the scene's lights); which lights the scene holds with the buy window open is not traced, so OpenTPW draws them unlit
public sealed class PreviewIcon
{
	private readonly ModelFile model;
	private readonly List<Triangle> triangles = new();
	private readonly List<MeshPart> meshes = new();
	private readonly NVector3 center;
	private readonly float radius;
	private readonly NVector3 minimum, maximum;
	private readonly ObjectAnimator? animator;
	private readonly ModelAnimation? clip;
	private readonly string clipName = "";
	private double? animationClock;
	// Per-frame work without per-frame allocations: the rest pose is computed once, the animated pose, the arrangement drawn and the texture
	// grouping reuse their buffers (an arrangement drawn through Draw or DrawBuyPreview is consumed before the next one starts).
	private readonly NVector3[]?[] restPoints;
	private readonly IReadOnlyList<NVector2>?[] restTexCoords;
	private NVector3[]?[]? animatedPoints;
	private NVector3[]?[]? animatedBuffers;
	private IReadOnlyList<NVector2>?[]? animatedTexCoords;
	private readonly List<ArrangedTriangle> drawnTriangles = new();
	private readonly UiTexture[] groupTextures;
	private readonly int[] triangleGroups;
	private readonly int[] groupCounts;
	private readonly int[] groupOrder;
	private readonly int[] groupFilled;

	/// <summary>A drawn mesh: its index in <see cref="meshes"/>, its index in the model, its node, its top node and that node's own matrix.</summary>
	private sealed record MeshPart( int Index, int Mesh, int Node, int? Root, NMatrix4x4 RestWorld, NMatrix4x4 RootLocal );

	/// <summary>A preview triangle: its mesh part, its corners' vertex indices, its texture coordinates (rest) and texture.</summary>
	private readonly record struct Triangle( MeshPart Part, int A, int B, int C, NVector2 UvA, NVector2 UvB, NVector2 UvC, UiTexture? Texture, string MaterialName );

	/// <param name="resolveTexture">Game image path for a material name (null: untextured).</param>
	/// <param name="resolveRuntimeTexture">Textures the game makes at run time, e.g. the object's <c>sign1</c>/<c>sign2</c> text; asked first, null means "not one".</param>
	/// <param name="buyClip">The clip the buy window's preview plays (the model's animation 5, <see cref="BuyClipMember"/>), or null.</param>
	public PreviewIcon( ModelFile model, Func<string, string?> resolveTexture, Func<string, UiTexture?>? resolveRuntimeTexture = null, ModelAnimation? buyClip = null, string? buyClipName = null )
	{
		this.model = model;
		var min = new NVector3( float.MaxValue );
		var max = new NVector3( float.MinValue );
		var rest = new List<NVector3[]?>();
		for ( var meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++ )
		{
			var mesh = model.Meshes[meshIndex];
			if ( mesh.Vertices.Length == 0 || mesh.Materials.Length == 0 || mesh.TexCoords.Length < mesh.Vertices.Length )
				continue;
			var world = OriginalTerrain.GetWorldMatrix( model, mesh.NodeIndex );
			var root = RootOf( model, mesh.NodeIndex );
			var part = new MeshPart( meshes.Count, meshIndex, mesh.NodeIndex, root, world, root is { } top ? model.Nodes[top].Transform : NMatrix4x4.Identity );
			meshes.Add( part );
			var textures = new UiTexture?[mesh.Materials.Length];
			var resolved = new bool[mesh.Materials.Length];
			var points = new NVector3[mesh.Vertices.Length];
			for ( var vertex = 0; vertex < points.Length; vertex++ )
			{
				var point = points[vertex] = NVector3.Transform( mesh.Vertices[vertex].Position.GetSystemVector3(), world );
				min = NVector3.Min( min, point );
				max = NVector3.Max( max, point );
			}
			rest.Add( points );
			for ( var index = 0; index + 2 < mesh.Indices.Length; index += 3 )
			{
				int a = (int)mesh.Indices[index], b = (int)mesh.Indices[index + 1], c = (int)mesh.Indices[index + 2];
				if ( a >= mesh.Vertices.Length || b >= mesh.Vertices.Length || c >= mesh.Vertices.Length )
					continue;
				var materialIndex = (int)Math.Min( mesh.Vertices[a].TextureIndex, (uint)mesh.Materials.Length - 1 );
				if ( !resolved[materialIndex] )
				{
					var material = mesh.Materials[materialIndex];
					textures[materialIndex] = material.TextureIndex < 0 ? null : resolveRuntimeTexture?.Invoke( material.Name )
						?? (resolveTexture( material.Name ) is { } path ? UiTexture.Image( path ) : null);
					resolved[materialIndex] = true;
				}
				triangles.Add( new Triangle( part, a, b, c, Uv( mesh, a ), Uv( mesh, b ), Uv( mesh, c ), textures[materialIndex], mesh.Materials[materialIndex].Name ) );
			}
		}
		if ( triangles.Count == 0 )
			throw new InvalidDataException( "Preview model has no textured geometry." );
		center = (min + max) / 2;
		radius = Math.Max( 1e-3f, (max - min).Length() / 2 );
		minimum = min;
		maximum = max;
		restPoints = rest.ToArray();
		restTexCoords = new IReadOnlyList<NVector2>?[meshes.Count];
		// the texture groups of the depth image, in the order the triangles first use them
		var groups = new Dictionary<UiTexture, int>();
		var groupList = new List<UiTexture>();
		triangleGroups = new int[triangles.Count];
		for ( var index = 0; index < triangles.Count; index++ )
		{
			var texture = triangles[index].Texture ?? UiTexture.Solid;
			if ( !groups.TryGetValue( texture, out var group ) )
			{
				groups[texture] = group = groupList.Count;
				groupList.Add( texture );
			}
			triangleGroups[index] = group;
		}
		groupTextures = groupList.ToArray();
		groupCounts = new int[groupTextures.Length];
		groupOrder = new int[groupTextures.Length];
		groupFilled = new int[groupTextures.Length];
		if ( buyClip != null )
		{
			clip = buyClip;
			clipName = buyClipName ?? "";
			animator = new ObjectAnimator( model );
		}
	}

	/// <summary>The animation the buy window's preview plays: id 5 (the letter m of the animation table), variant 0.</summary>
	// [BIN:TPW-EXE:0x4689f0 preview clip] the new preview object starts 0x4732a0(instance, 5, 0, 1, 1.0, 0): animation id 5, variant 0 (the member
	// <model>m1, else <model>m, ObjectAnimations), looping (flag 1) at speed 1.0 on channel 0; the root frame's matrix is the preview's (0x468e50
	// rebuilds it from the base every frame), so the clip moves the nodes below the root
	public const int BuyClipAnimation = 5;

	/// <summary>The member of <paramref name="members"/> the buy window's preview of <paramref name="previewModel"/> plays, or null.</summary>
	public static ObjectAnimationFile? BuyClipMember( string previewModel, IEnumerable<string> members ) =>
		ObjectAnimations.Resolve( ObjectAnimations.Find( Path.GetFileNameWithoutExtension( previewModel ), members ), BuyClipAnimation, 0 );

	/// <summary>A new buy-window preview of this model starts its clip from the beginning at <paramref name="seconds"/> (the interface clock).</summary>
	public void StartBuyAnimation( double seconds )
	{
		if ( animator == null || clip == null )
			return;
		animator.Trigger( 0, clip, clipName, BuyClipAnimation, 0, loop: true, speed: 1f );
		animationClock = seconds;
	}

	/// <summary>Advances the buy-window preview's clip to <paramref name="seconds"/> (the interface clock).</summary>
	private void AdvanceBuyAnimation( double seconds )
	{
		if ( animator == null )
			return;
		if ( animationClock == null )
			StartBuyAnimation( seconds );
		if ( seconds > animationClock )
		{
			animator.Advance( seconds - animationClock.Value );
			animationClock = seconds;
		}
	}

	/// <summary>Whether the buy window's preview plays a clip.</summary>
	internal bool HasBuyClip => clip != null;

	/// <summary>
	/// The corners of every mesh in model space, by <see cref="MeshPart.Index"/>: the rest pose, or the clip's pose (the nodes below the root moved by the clip,
	/// the root keeping its own matrix, and the mesh's own vertex positions when the clip moves them), with the clip's texture coordinates (null: the rest's).
	/// Null points are meshes the clip hides. The arrays are reused by the next call.
	/// </summary>
	private (NVector3[]?[] Points, IReadOnlyList<NVector2>?[] TexCoords) Pose( bool animated )
	{
		if ( !animated || animator == null )
			return (restPoints, restTexCoords);
		animatedPoints ??= new NVector3[]?[meshes.Count];
		animatedBuffers ??= new NVector3[]?[meshes.Count];
		animatedTexCoords ??= new IReadOnlyList<NVector2>?[meshes.Count];
		var nodes = animator.NodeTransforms;
		foreach ( var part in meshes )
		{
			var mesh = model.Meshes[part.Mesh];
			if ( part.Node >= 0 && part.Node < model.Nodes.Count && animator.IsNodeHidden( part.Node ) )
			{
				animatedPoints[part.Index] = null;
				animatedTexCoords[part.Index] = null;
				continue;
			}
			var world = part.RestWorld;
			if ( part.Node >= 0 && part.Node < nodes.Count && part.Root is { } root && root < nodes.Count && NMatrix4x4.Invert( nodes[root], out var inverse ) )
				world = nodes[part.Node] * inverse * part.RootLocal;
			var positions = animator.GetVertexPositions( part.Mesh );
			var points = animatedBuffers[part.Index] ??= new NVector3[mesh.Vertices.Length];
			for ( var index = 0; index < points.Length; index++ )
				points[index] = NVector3.Transform( PosedCorner( mesh, positions, index ), world );
			animatedPoints[part.Index] = points;
			animatedTexCoords[part.Index] = animator.GetTexCoords( part.Mesh );
		}
		return (animatedPoints, animatedTexCoords);
	}

	/// <summary>
	/// The model-space position of corner <paramref name="corner"/> of <paramref name="mesh"/> under a clip's <paramref name="positions"/> (null: the rest pose). A clip's positions are the mesh's
	/// stored positions (<see cref="ModelFile.Mesh.Positions"/>), which corners share, so each corner takes the one it was built from (<see cref="ModelFile.Mesh.CornerPositionIndices"/>),
	/// as the park's parts do (<see cref="ObjectRenderParts.WritePositions"/>).
	/// </summary>
	internal static NVector3 PosedCorner( ModelFile.Mesh mesh, IReadOnlyList<NVector3>? positions, int corner )
	{
		var corners = mesh.CornerPositionIndices;
		return positions != null && corner < corners.Length && corners[corner] < positions.Count ? positions[corners[corner]] : mesh.Vertices[corner].Position.GetSystemVector3();
	}

	private static int? RootOf( ModelFile model, int nodeIndex )
	{
		int? root = null;
		for ( var guard = 0; nodeIndex >= 0 && nodeIndex < model.Nodes.Count && guard <= model.Nodes.Count; guard++ )
		{
			root = nodeIndex;
			nodeIndex = model.Nodes[nodeIndex].ParentIndex;
		}
		return root;
	}

	public int TriangleCount => triangles.Count;
	/// <summary>Triangles drawn with a texture the game makes at run time (the sign faces).</summary>
	public int RuntimeTextureTriangleCount => triangles.Count( triangle => triangle.Texture?.Rendered != null );
	/// <summary>The model's bounds (the node hierarchy applied).</summary>
	internal (NVector3 Minimum, NVector3 Maximum) Bounds => (minimum, maximum);

	private static NVector2 Uv( ModelFile.Mesh mesh, int index ) => new( mesh.TexCoords[index].X, 1 - mesh.TexCoords[index].Y );

	/// <summary>The object windows' preview: the model turned by <paramref name="yaw"/>, tilted 30 degrees and centred in <paramref name="rect"/>.</summary>
	public void Draw( UiBatch batch, UiRect rect, float yaw ) => Add( batch, ArrangeObjectPreview( rect, yaw, drawnTriangles ) );

	/// <summary>The buy window's preview tilt: 45 degrees (tp.exe 0x468e50 tilts by the sine-table entries of ±π/4, 0x6fe7e4 / 0x6fe7e8, through 0x46f240).</summary>
	public const float BuyTilt = MathF.PI / 4;
	/// <summary>The buy window's preview turn in radians per second: 0.0004 a millisecond (tp.exe 0x46908a, 0x6fe804).</summary>
	public const float BuyTurnRate = 0.4f;

	/// <summary>
	/// The buy window's scale for a model of bounds <paramref name="min"/>..<paramref name="max"/> in a frame of <paramref name="width"/> x <paramref name="height"/>:
	/// two thirds of the frame's normalized width over the half-diagonal of the model's ground footprint, or the frame's height over the distance from the
	/// footprint's low corner on the ground to the bounds' high corner, whichever is smaller.
	/// </summary>
	// [BIN:TPW-EXE:0x4689f0 buy preview fit] the preview object takes min(width x 2/3 / sqrt((dx/2)^2 + (dz/2)^2), height / |max - (min x, 0, min z)|) in the
	// normalized screen units of 0x486410 (x over 1024, y over 768 canvas units) and later squeezes x by 0.75 (0x469067), so in canvas units the width term is
	// width / 2 over the half-diagonal
	public static float BuyScale( float width, float height, NVector3 min, NVector3 max )
	{
		var halfDiagonal = MathF.Sqrt( MathF.Pow( (max.X - min.X) / 2, 2 ) + MathF.Pow( (max.Z - min.Z) / 2, 2 ) );
		var reach = (max - new NVector3( min.X, 0, min.Z )).Length();
		return Math.Min( width / 2 / Math.Max( 1e-3f, halfDiagonal ), height / Math.Max( 1e-3f, reach ) );
	}

	/// <summary>
	/// Adds the model as the buy window shows it: turned by <paramref name="yaw"/>, tilted 45 degrees, fitted and placed in the frame <paramref name="rect"/>
	/// by the original's rule, posed by its clip at <paramref name="clockSeconds"/> (the interface clock; null: the rest pose); <paramref name="canvasScale"/>
	/// is the canvas's pixels per virtual unit (<see cref="UiCanvas.Scale"/>).
	/// </summary>
	public void DrawBuyPreview( UiBatch batch, UiRect rect, float yaw, float canvasScale, double? clockSeconds = null )
	{
		if ( clockSeconds is { } seconds )
			AdvanceBuyAnimation( seconds );
		Add( batch, ArrangeBuyPreview( rect, yaw, canvasScale, animated: clockSeconds != null, into: drawnTriangles ) );
	}

	/// <summary>
	/// One triangle as the preview draws it: its index in the model's triangle list, its material, its corners in canvas pixels (y down) with their depth
	/// (smaller is nearer), its outward normal in model space (Y up, not normalized) and whether the original's face test keeps it.
	/// </summary>
	internal readonly record struct ArrangedTriangle( int Index, string MaterialName, NVector3 A, NVector3 B, NVector3 C, NVector3 ModelNormal, bool FacesViewer,
		NVector2 UvA = default, NVector2 UvB = default, NVector2 UvC = default )
	{
		/// <summary>The corners in canvas pixels.</summary>
		public NVector2 A2 => new( A.X, A.Y );
		public NVector2 B2 => new( B.X, B.Y );
		public NVector2 C2 => new( C.X, C.Y );
	}

	/// <summary>A preview's triangles in canvas pixels, the original's face test applied to each, and the depth range they span.</summary>
	internal sealed record Arrangement( UiRect Rect, IReadOnlyList<ArrangedTriangle> Triangles, float NearDepth, float FarDepth );

	/// <summary>
	/// The buy window's preview at <paramref name="yaw"/> in the frame <paramref name="rect"/> (canvas pixels): every triangle with its corners where the
	/// original's matrix puts them.
	/// </summary>
	// [BIN:TPW-EXE:0x4689f0 buy preview object] the preview object's root frame is scaled by the fit s (0x45bf20 → 0x46fad0 scales the matrix's three axis rows,
	// not its translation) and that matrix is kept as the base (0x7afd10); the fit also keeps the ground footprint's half extents s·dx/2 and s·dz/2 (0x7afd64,
	// 0x7afd68), s·|max − ground centre| (0x7afd74, the ground centre being ((min x + max x)/2, 0, (min z + max z)/2)) and the share
	// |max − ground centre| / (|max − ground centre| + half diagonal) (0x7afd6c); 0x486410 passes the frame as left x, bottom y, width and height in the
	// screen's normalized units ((x − 1024) / 1024, (768 − y) / 768 of the 2048 × 1536 canvas)
	// [BIN:TPW-EXE:0x468e50 buy preview matrix] every frame the base is turned about y by the yaw (0x46f420, the sine table's cosine and sine) and about x by
	// −π/4 (0x46f240), the translation row turning with it; then x gains left + width/2 − 0.75 (sin·hd + cos·hw) and y gains bottom + height/2 −
	// 0.35·share·reach + 0.7 (sin·hw − cos·hd) (hw, hd the half extents), the three axis rows' x is scaled by 0.75 and the depth column, translation included,
	// by 0.1
	/// <param name="into">A list to fill instead of a new one (cleared first).</param>
	internal Arrangement ArrangeBuyPreview( UiRect rect, float yaw, float canvasScale, bool animated = false, List<ArrangedTriangle>? into = null )
	{
		var (min, max) = (minimum, maximum);
		var halfWidth = (max.X - min.X) / 2;
		var halfDepth = (max.Z - min.Z) / 2;
		var halfDiagonal = MathF.Sqrt( halfDepth * halfDepth + halfWidth * halfWidth );
		var groundCentre = new NVector3( (min.X + max.X) / 2, 0, (min.Z + max.Z) / 2 );
		var reachFromCentre = (max - groundCentre).Length();
		var share = reachFromCentre / Math.Max( 1e-6f, reachFromCentre + halfDiagonal );
		// pixels per model unit: the fit s in normalized height units, 768 canvas units each
		var pixels = BuyScale( rect.Width, rect.Height, min, max );
		var scale = pixels / (768 * canvasScale);
		var turn = PreviewTurn( yaw );
		var sin = OriginalSineTable.Sin( yaw );
		var cos = OriginalSineTable.Cos( yaw );
		var offsetX = rect.X + rect.Width / 2 - pixels * (sin * halfDepth + cos * halfWidth);
		var offsetY = rect.Y + rect.Height / 2 + pixels * (0.35f * share * reachFromCentre - 0.7f * (sin * halfWidth - cos * halfDepth));
		NVector3 Project( NVector3 point, NVector3 rootTranslation )
		{
			// the base: the axis rows scaled by s, the root's translation not; both turn with the yaw and the tilt
			var axes = NVector3.Transform( (point - rootTranslation) * scale, turn );
			var translation = NVector3.Transform( rootTranslation, turn );
			// normalized units: x = 0.75 × axes + translation; y = axes + translation; the view flips y and the viewport maps 1024 (x) and 768 (y) canvas
			// units to one normalized unit, with screen y down
			return new NVector3(
				offsetX + 768 * canvasScale * axes.X + 1024 * canvasScale * translation.X,
				offsetY - 768 * canvasScale * (axes.Y + translation.Y),
				0.1f * 0.1f * (axes.Z + translation.Z) );
		}
		return Arrange( rect, Project, animated, into );
	}

	/// <summary>The yaw and the −π/4 tilt of 0x468e50 as one matrix (row vectors), with the original's sine table.</summary>
	internal static NMatrix4x4 PreviewTurn( float yaw )
	{
		var sin = OriginalSineTable.Sin( yaw );
		var cos = OriginalSineTable.Cos( yaw );
		// 0x46f420: x' = x cos + z sin, z' = z cos − x sin
		var turn = new NMatrix4x4( cos, 0, -sin, 0, 0, 1, 0, 0, sin, 0, cos, 0, 0, 0, 0, 1 );
		// 0x46f240 with c = sin(π/4) and s = sin(−π/4) from the table: y' = y c − z s, z' = z c + y s
		var c = OriginalSineTable.Sin( 0.7853982f );
		var s = OriginalSineTable.Sin( -0.7853982f );
		var tilt = new NMatrix4x4( 1, 0, 0, 0, 0, c, s, 0, 0, -s, c, 0, 0, 0, 0, 1 );
		return turn * tilt;
	}

	/// <summary>The object windows' preview: OpenTPW's orthographic view, the model turned by <paramref name="yaw"/>, tilted 30 degrees and centred in <paramref name="rect"/>.</summary>
	internal Arrangement ArrangeObjectPreview( UiRect rect, float yaw, List<ArrangedTriangle>? into = null )
	{
		// The same turn and tilt directions as the buy window's (PreviewTurn), so the same faces turn towards the viewer
		var rotation = NMatrix4x4.CreateTranslation( -center ) * NMatrix4x4.CreateRotationY( yaw ) * NMatrix4x4.CreateRotationX( -30f * MathF.PI / 180 );
		var scale = Math.Min( rect.Width, rect.Height ) / (radius * 2);
		var middle = rect.Center;
		NVector3 Project( NVector3 point, NVector3 _ )
		{
			var turned = NVector3.Transform( point, rotation );
			return new NVector3( middle.X + turned.X * scale, middle.Y - turned.Y * scale, turned.Z / radius );
		}
		return Arrange( rect, Project, animated: false, into );
	}

	private Arrangement Arrange( UiRect rect, Func<NVector3, NVector3, NVector3> project, bool animated, List<ArrangedTriangle>? into )
	{
		var (posePoints, poseTexCoords) = Pose( animated );
		var arranged = into ?? new List<ArrangedTriangle>( triangles.Count );
		arranged.Clear();
		var near = float.MaxValue;
		var far = float.MinValue;
		for ( var index = 0; index < triangles.Count; index++ )
		{
			var triangle = triangles[index];
			if ( posePoints[triangle.Part.Index] is not { } points )
				continue;
			var rootTranslation = triangle.Part.RootLocal.Translation;
			var (pointA, pointB, pointC) = (points[triangle.A], points[triangle.B], points[triangle.C]);
			var a = project( pointA, rootTranslation );
			var b = project( pointB, rootTranslation );
			var c = project( pointC, rootTranslation );
			var uvs = poseTexCoords[triangle.Part.Index];
			NVector2 Uv( int vertex, NVector2 rest ) => uvs != null && vertex < uvs.Count ? new NVector2( uvs[vertex].X, 1 - uvs[vertex].Y ) : rest;
			// [BIN:TPW-EXE:0x581320 face test] kept when the corners run clockwise on the screen (y down): the determinant of (x, y, w) is positive
			var keeps = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y) > 0 || IsShownGroundPlate( triangle.MaterialName );
			arranged.Add( new ArrangedTriangle( index, triangle.MaterialName, a, b, c, NVector3.Cross( pointB - pointA, pointC - pointA ), keeps,
				Uv( triangle.A, triangle.UvA ), Uv( triangle.B, triangle.UvB ), Uv( triangle.C, triangle.UvC ) ) );
			near = Math.Min( near, Math.Min( a.Z, Math.Min( b.Z, c.Z ) ) );
			far = Math.Max( far, Math.Max( a.Z, Math.Max( b.Z, c.Z ) ) );
		}
		return new Arrangement( rect, arranged, near, far );
	}

	/// <summary>The material of the ground plates the preview shows from either side (<c>PURPLE_BASE</c>).</summary>
	internal const string GroundPlateMaterial = "PURPLE_BASE";

	/// <summary>Whether a triangle of <paramref name="material"/> is drawn whichever way it faces (the ground plates; see <see cref="GroundPlateMaterial"/>).</summary>
	// [EXT:preview-ground-plates] the ground plates of the Space Racers and The Blobulator previews (Pgokarts.md2, PWATERIDE.MD2: two triangles of
	// the material PURPLE_BASE each, the only previews where that material faces down) face down, so tp.exe's face test (0x581320) drops them when the preview is seen
	// from above; the preview draws them anyway, at the user's request ("Grondplaten toch tonen (EXT)", October 20, 2026); every other triangle
	// keeps the original's face test
	internal static bool IsShownGroundPlate( string material ) => string.Equals( material, GroundPlateMaterial, StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// The image the interface draws for <paramref name="arrangement"/>: the triangles the face test keeps, in an image covering their corners on whole pixels
	/// (the original draws the preview over the screen, not clipped to its frame), with their depth mapped into the depth buffer's range in the same order
	/// (smaller is nearer).
	/// </summary>
	internal UiDepthImage ToDepthImage( Arrangement arrangement )
	{
		var low = new NVector2( float.MaxValue );
		var high = new NVector2( float.MinValue );
		Array.Clear( groupCounts );
		var kept = arrangement.Triangles;
		for ( var index = 0; index < kept.Count; index++ )
		{
			var triangle = kept[index];
			if ( !triangle.FacesViewer )
				continue;
			low = NVector2.Min( low, NVector2.Min( triangle.A2, NVector2.Min( triangle.B2, triangle.C2 ) ) );
			high = NVector2.Max( high, NVector2.Max( triangle.A2, NVector2.Max( triangle.B2, triangle.C2 ) ) );
			groupCounts[triangleGroups[triangle.Index]] += 3;
		}
		if ( low.X > high.X )
			(low, high) = (new NVector2( arrangement.Rect.X, arrangement.Rect.Y ), new NVector2( arrangement.Rect.X + 1, arrangement.Rect.Y + 1 ));
		var left = MathF.Floor( low.X );
		var top = MathF.Floor( low.Y );
		var width = Math.Clamp( (int)MathF.Ceiling( high.X ) - (int)left, 1, MaximumImageSide );
		var height = Math.Clamp( (int)MathF.Ceiling( high.Y ) - (int)top, 1, MaximumImageSide );
		// any affine map that keeps the order keeps the depth test's result; the margin keeps every depth strictly inside (0, 1)
		var span = Math.Max( 1e-6f, arrangement.FarDepth - arrangement.NearDepth );
		var near = arrangement.NearDepth;
		UiDepthVertex Vertex( NVector3 corner, NVector2 uv ) => new( new NVector3( corner.X - left, corner.Y - top, 0.05f + 0.9f * (corner.Z - near) / span ), uv );
		// one draw per texture, in the order the kept triangles first use them; each draw's triangles in the arrangement's order
		var drawCount = 0;
		Array.Fill( groupOrder, -1 );
		var vertices = new UiDepthVertex[groupTextures.Length][];
		Array.Clear( groupFilled );
		for ( var index = 0; index < kept.Count; index++ )
		{
			var triangle = kept[index];
			if ( !triangle.FacesViewer )
				continue;
			var group = triangleGroups[triangle.Index];
			if ( groupOrder[group] < 0 )
			{
				groupOrder[group] = drawCount++;
				vertices[group] = new UiDepthVertex[groupCounts[group]];
			}
			var target = vertices[group];
			var at = groupFilled[group];
			target[at] = Vertex( triangle.A, triangle.UvA );
			target[at + 1] = Vertex( triangle.B, triangle.UvB );
			target[at + 2] = Vertex( triangle.C, triangle.UvC );
			groupFilled[group] = at + 3;
		}
		var draws = new UiDepthDraw[drawCount];
		for ( var group = 0; group < groupTextures.Length; group++ )
			if ( groupOrder[group] >= 0 )
				draws[groupOrder[group]] = new UiDepthDraw( groupTextures[group], vertices[group] );
		return new UiDepthImage( new UiRect( left, top, width, height ), width, height, draws );
	}

	/// <summary>The largest side of a preview's image in pixels.</summary>
	internal const int MaximumImageSide = 4096;

	private void Add( UiBatch batch, Arrangement arrangement ) => batch.AddDepthImage( ToDepthImage( arrangement ) );
}
