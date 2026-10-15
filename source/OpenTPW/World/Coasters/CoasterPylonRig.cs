using System.Numerics;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>One mesh part of a posed coaster model (a pylon or a car): the vertices (original units, engine axis order X, Z, Y; a pylon's are relative to its cell's corner, a car's to its pivot) and indices with the part's texture names.</summary>
public sealed record CoasterModelPart( string MeshName, string[] Textures, Vertex[] Vertices, uint[] Indices );

/// <summary>
/// A pylon model (<c>StdPylon.MD2</c> or <c>LoopPylon.MD2</c> with its <c>L</c> loft clip and <c>R</c> rotate clip) posed for one node. The loft clip stretches
/// the pylon to a height (it adds a vertex animation to the pylon mesh and a position key to the track tag and stack-height dummies), the rotate clip turns
/// it about the cell centre to line its track tag up with the track.
/// </summary>
// [BIN:TPW-EXE:0x42fad0 coaster model load] the TrackMaster loader opens the pylon models with the relative-animation flag 0x1013405 (the only caller to do so, with "GTexture" as the extra texture directory): a vertex key or position key is added to the stored mesh or node, not set
// [BIN:TPW-EXE:0x430590 pylon model tags] the loft, rotate, incline and bank clips and the track and stack dummies are looked up on the loaded model by node attribute (0x2, 0x3, 0x9, 0xa, 0x1000000, 0x800000, 0x2000000, 0x100000, 0x80000) when the control is set up (0x42eb70)
// [APPROX:COAST-033] a pylon is the geometry of its model with the loft clip (to the node height) and the rotate clip (to the knot yaw, yaw/2π of the clip) posed once per track change; the incline and bank clips (the tag's tilt and the pylon's bank lean) are not played, a ground-tile mesh under the pylon is skipped, the pylon takes the node's height by scaling or sinking when the node lies outside the loft range, and the bridge or stacker head above the track tag is drawn only when a pylon is stacked on it (an assumption: the loft clip moves it with the tag, and no node flag that hides it was found); the heights h0/h1, the raised fMinHeight and the stack offset are the model's since the pylon setup 0x430590 was read — evidence needed: the pose routine that feeds node +232 to the clips and the rotate/incline/bank clip setup of 0x42eb70
public sealed class CoasterPylonRig
{
	private readonly ModelFile model;
	private readonly ModelAnimation? loft;
	private readonly ModelAnimation? rotate;
	private readonly (NVector3 Scale, Quaternion Rotation, NVector3 Translation)[] rest;
	private readonly ModelAnimationTrack?[] loftTracks;
	private readonly ModelAnimationTrack?[] rotateTracks;
	private readonly int[] order;
	private readonly Matrix4x4[] worlds;

	/// <param name="model">The pylon's geometry.</param>
	/// <param name="loft">The loft clip (<c>&lt;pylon&gt;L.MD2</c>), or null.</param>
	/// <param name="rotate">The rotate clip (<c>&lt;pylon&gt;R.MD2</c> or <c>R1</c>), or null.</param>
	/// <param name="control">The pylon control whose loft caps, multiplier and add (<c>fLoftAnim*</c>) map the node's height parameter to the clip; null takes the defaults.</param>
	/// <summary>The hand edits the pylon's animations allow; set by the asset loader from the models of the same family (<c>I</c>, <c>B</c>, <c>R2</c>).</summary>
	public CoasterPylonCapabilities Capabilities { get; internal set; }

	public CoasterPylonRig( ModelFile model, ModelAnimation? loft, ModelAnimation? rotate, CoasterPylonControl? control = null )
	{
		ArgumentNullException.ThrowIfNull( model );
		if ( model.Kind != ModelFileKind.Geometry )
			throw new ArgumentException( "A pylon needs a geometry model.", nameof( model ) );
		this.model = model;
		this.loft = loft;
		this.rotate = rotate;
		var count = model.Nodes.Count;
		rest = new (NVector3, Quaternion, NVector3)[count];
		for ( var index = 0; index < count; index++ )
		{
			if ( !Matrix4x4.Decompose( model.Nodes[index].Transform, out var scale, out var rotation, out var translation ) )
			{
				scale = NVector3.One;
				rotation = Quaternion.Identity;
				translation = model.Nodes[index].Transform.Translation;
			}
			rest[index] = (scale, rotation, translation);
		}
		loftTracks = TracksByNode( loft, count );
		rotateTracks = TracksByNode( rotate, count );
		order = ParentFirst( model );
		worlds = new Matrix4x4[count];
		// [BIN:TPW-EXE:0x430590 pylon track dummy] the track dummy is the dummy numbered by the first attribute record with bit 0x800000 (0x4306be..0x4306e9: record index +0x2c, dummy number − 1 at +0x30), the stack dummy the one of the first record with 0x100000 (0x430780..0x4307a6); the numbers count the model's dummy nodes from 1 in file order; a model without such a record falls back to the first dummy named Track* or animated by the loft clip
		TagNode = DummyByAttribute( model, TrackDummyAttribute ) ?? FindTag( model, loftTracks );
		StackNode = DummyByAttribute( model, StackDummyAttribute ) ?? -1;
		Duration = loft?.Duration ?? 0;
		if ( control != null )
			(loftStartCap, loftEndCap, loftMultiplier, loftAdd) = (control.LoftStartCap, control.LoftEndCap, control.LoftMultiplier, control.LoftAdd);
		// [BIN:TPW-EXE:0x430590 pylon heights] h0 = the track dummy's Y with the loft clip at clamp(fLoftAnimAdd, StartCap, EndCap) (0x430923..0x430990), h1 = its Y at clamp(fLoftAnimMultiplier + fLoftAnimAdd, StartCap, EndCap) (0x430a0b..0x430a92), the stack offset the stack dummy's Y at the first pose minus h0, or 0 without a stack dummy (0x4309be..0x430a01)
		var first = LoftFraction( 0 );
		var low = TagHeightAtFraction( first );
		TagLow = low;
		TagHigh = TagHeightAtFraction( LoftFraction( 1 ) );
		StackOffset = StackNode >= 0 ? NodeHeightAtFraction( StackNode, first ) - low : 0;
	}

	/// <summary>Attribute bit of the record that names a pylon's track dummy.</summary>
	public const uint TrackDummyAttribute = 0x800000;
	/// <summary>Attribute bit of the record that names a pylon's stack dummy.</summary>
	public const uint StackDummyAttribute = 0x100000;
	private const uint DummyNodeType = 0x200;

	private float loftStartCap = 0, loftEndCap = 1, loftMultiplier = 1, loftAdd = 0;

	/// <summary>The loft clip's fraction for a height parameter t: clamp(t · fLoftAnimMultiplier + fLoftAnimAdd, StartCap, EndCap) (<c>0x434060</c>).</summary>
	public float LoftFraction( float t ) => Math.Clamp( t * loftMultiplier + loftAdd, loftStartCap, MathF.Max( loftStartCap, loftEndCap ) );

	/// <summary>The dummy node numbered (from 1, in file order of the dummy nodes) by the first attribute record that has any of <paramref name="mask"/>, or null.</summary>
	private static int? DummyByAttribute( ModelFile model, uint mask )
	{
		foreach ( var attribute in model.DummyAttributes )
		{
			if ( (attribute.Type & mask) == 0 )
				continue;
			var number = (int)attribute.Value - 1;
			var dummies = model.Nodes.Where( node => (node.Type & DummyNodeType) != 0 ).ToList();
			return number >= 0 && number < dummies.Count ? dummies[number].Index : null;
		}
		return null;
	}

	/// <summary>The node the stack dummy names, -1 when the model has none (the next pylon of a stack then stands at the track dummy).</summary>
	public int StackNode { get; }
	/// <summary>How far above the track dummy the next pylon of a stack stands (pylon <c>+0xc4</c>): the stack dummy's height at the lowest loft pose minus h0.</summary>
	public float StackOffset { get; }

	private float TagHeightAtFraction( float fraction ) => NodeHeightAtFraction( TagNode, fraction );

	private float NodeHeightAtFraction( int node, float fraction )
	{
		if ( node < 0 || node >= worlds.Length )
			return 0;
		Worlds( fraction * Duration, 0, worlds );
		return worlds[node].Translation.Y;
	}

	public ModelFile Model => model;
	/// <summary>The node the track attaches to (<c>TrackDummyCentre</c>, <c>TrackCentreDummy</c> or the first animated dummy), -1 when the model has none.</summary>
	public int TagNode { get; }
	public int Duration { get; }
	/// <summary>The height of the track tag above the pylon base at loft 0 (MD2 units).</summary>
	public float TagLow { get; }
	/// <summary>The height of the track tag above the pylon base at loft 1 (MD2 units).</summary>
	public float TagHigh { get; }
	public bool HasLoft => loft != null && TagHigh > TagLow;

	/// <summary>The height above the pylon base of any node of the model at a clip fraction in [0, 1] (the node's world Y after the loft clip).</summary>
	public float NodeHeightAt( int node, float loftParameter ) => NodeHeightAtFraction( node, loftParameter );

	/// <summary>The tag height at a clip fraction in [0, 1].</summary>
	public float TagHeightAt( float loftParameter ) => TagHeightAtFraction( loftParameter );

	/// <summary>
	/// How a pylon reaches a node height: the loft parameter (clamped to the clip), a vertical scale about the base when the height lies above the loft's
	/// top, and a vertical offset when it lies below the loft's bottom (the pylon sinks into the ground).
	/// </summary>
	// [BIN:STP-PPC:0x100369BC pylon height] the node height is h0 + t(h1 − h0) with t = clamp((h − h0)/(h1 − h0), 0, 1): the loft clip's two ends are the pylon's heights
	public static (float Loft, float Scale, float Offset) FitHeight( float low, float high, float height )
	{
		if ( high <= low )
			return (0, high > 0 ? height / high : 1, high > 0 ? 0 : height - low);
		if ( height <= low )
			return (0, 1, height - low);
		if ( height >= high )
			return (1, high > 0 ? height / high : 1, 0);
		return ((height - low) / (high - low), 1, 0);
	}

	/// <summary>The loft clip fraction, scale and offset that put the track tag at <paramref name="height"/> above the base: the height parameter t of h0 + t(h1 − h0) goes through the loft mapping (<see cref="LoftFraction"/>).</summary>
	public (float Loft, float Scale, float Offset) Fit( float height )
	{
		var (loft, scale, offset) = FitHeight( TagLow, TagHigh, height );
		return (LoftFraction( loft ), scale, offset);
	}

	/// <summary>The height of the tag after <see cref="Fit"/> and posing (loft, then the vertical scale and offset): the node height itself.</summary>
	public float PosedTagHeight( float height )
	{
		var (loftParameter, scale, offset) = Fit( height );
		return TagHeightAt( loftParameter ) * scale + offset;
	}

	/// <summary>The pylon's mesh parts for a node height and a knot yaw (radians); positions relative to the cell corner.</summary>
	public IReadOnlyList<CoasterModelPart> Pose( float height, float yaw, bool stackedAbove = false )
	{
		var (loftParameter, scale, offset) = Fit( height );
		var turn = yaw / (2 * MathF.PI);
		turn -= MathF.Floor( turn );
		return Pose( loftParameter, turn, scale, offset, stackedAbove );
	}

	/// <summary>
	/// The mesh parts at explicit clip parameters: loft in [0, 1], rotate in [0, 1) of the rotate clip. A part that lies above the track tag (the bridge or
	/// stacker head that carries a pylon stacked on this one) is left out unless <paramref name="stackedAbove"/>: a pylon ends at the track's underside.
	/// </summary>
	public IReadOnlyList<CoasterModelPart> Pose( float loftParameter, float rotateParameter, float verticalScale, float verticalOffset, bool stackedAbove = false )
	{
		var tagHeight = TagHeightAt( loftParameter ) * verticalScale + verticalOffset;
		var loftTick = Math.Clamp( loftParameter, 0f, 1f ) * Duration;
		var rotateTick = rotate == null ? 0 : Math.Clamp( rotateParameter, 0f, 1f ) * rotate.Duration;
		var matrices = new Matrix4x4[model.Nodes.Count];
		Worlds( loftTick, rotateTick, matrices );
		var parts = new List<CoasterModelPart>();
		foreach ( var mesh in model.Meshes )
		{
			if ( ObjectAssets.GetUnrenderableReason( mesh ) is { } reason && reason != "more than 16 texture slots" )
				continue;
			if ( IsGroundTile( mesh ) )
				continue;
			var node = mesh.NodeIndex >= 0 && mesh.NodeIndex < matrices.Length ? mesh.NodeIndex : model.RootNodeIndex;
			var source = mesh;
			if ( loft != null && node >= 0 && loftTracks[node]?.VertexAnimation is { } animation && animation.GetPoseLimitation( mesh.Positions.Length, loft.Duration ) == null )
			{
				var positions = mesh.Positions.Select( position => position.GetSystemVector3() ).ToArray();
				if ( animation.HasStaticGroup )
					animation.ApplyStaticGroup( positions, add: true );
				animation.ApplyAnimatedGroups( loftTick, positions, add: true );
				source = WithPositions( mesh, positions );
			}
			var bake = node >= 0 ? matrices[node] : Matrix4x4.Identity;
			foreach ( var (vertices, indices, textures, _) in ObjectAssets.ConvertMeshParts( source, bake ) )
			{
				for ( var index = 0; index < vertices.Length; index++ )
				{
					// Engine axis order: Z is the model's up.
					var position = vertices[index].Position.GetSystemVector3();
					vertices[index].Position = new NVector3( position.X, position.Y, position.Z * verticalScale + verticalOffset );
				}
				if ( !stackedAbove && TagNode >= 0 && vertices.Length > 0 )
				{
					var low = float.MaxValue;
					var high = float.MinValue;
					foreach ( var vertex in vertices )
					{
						var z = vertex.Position.GetSystemVector3().Z;
						low = MathF.Min( low, z );
						high = MathF.Max( high, z );
					}
					if ( (low + high) / 2 > tagHeight + 0.01f )
						continue;
				}
				parts.Add( new CoasterModelPart( mesh.Name, textures, vertices, indices ) );
			}
		}
		return parts;
	}

	/// <summary>A flat tile under the pylon (the ground patch of the Gorilla Thrilla pylon): four corners spanning most of the cell with no height.</summary>
	private static bool IsGroundTile( ModelFile.Mesh mesh )
	{
		if ( mesh.Positions.Length != 4 )
			return false;
		var low = new NVector3( float.MaxValue );
		var high = new NVector3( float.MinValue );
		foreach ( var position in mesh.Positions )
		{
			low = NVector3.Min( low, position.GetSystemVector3() );
			high = NVector3.Max( high, position.GetSystemVector3() );
		}
		return high.Y - low.Y < 0.5f && high.X - low.X > 8f && high.Z - low.Z > 8f;
	}

	private static ModelFile.Mesh WithPositions( ModelFile.Mesh mesh, NVector3[] positions )
	{
		var vertices = new ModelFile.Vertex[mesh.Vertices.Length];
		for ( var index = 0; index < vertices.Length; index++ )
			vertices[index] = new ModelFile.Vertex { Position = positions[mesh.CornerPositionIndices[index]], TextureIndex = mesh.Vertices[index].TextureIndex };
		return new ModelFile.Mesh
		{
			Name = mesh.Name,
			NodeIndex = mesh.NodeIndex,
			Vertices = vertices,
			Normals = mesh.Normals,
			TexCoords = mesh.TexCoords,
			Materials = mesh.Materials,
			Indices = mesh.Indices
		};
	}

	private void Worlds( float loftTick, float rotateTick, Matrix4x4[] output )
	{
		foreach ( var node in order )
		{
			var (scale, rotation, translation) = rest[node];
			if ( rotateTracks[node]?.SampleRotation( rotateTick ) is { } turn )
				rotation = turn;
			// Add mode: the loft clip's position key moves the node away from its stored place.
			if ( loftTracks[node]?.SampleTranslation( loftTick ) is { } move )
				translation += move;
			var local = Multiply( Multiply( Matrix4x4.CreateScale( scale ), Matrix4x4.CreateFromQuaternion( rotation ) ), Matrix4x4.CreateTranslation( translation ) );
			var parent = model.Nodes[node].ParentIndex;
			output[node] = parent < 0 ? local : Multiply( local, output[parent] );
		}
	}

	/// <summary>
	/// Row-major 4x4 product with plain single-precision multiplies and adds. The pylon heights (h0, h1, the stack offset) feed the canonical
	/// state hash, and <see cref="Matrix4x4"/>'s operator may fuse the multiply-add on hardware that has it (a CPU-dependent last bit), so the
	/// heights are composed here instead.
	/// </summary>
	private static Matrix4x4 Multiply( in Matrix4x4 a, in Matrix4x4 b ) => new(
		a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31 + a.M14 * b.M41,
		a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32 + a.M14 * b.M42,
		a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33 + a.M14 * b.M43,
		a.M11 * b.M14 + a.M12 * b.M24 + a.M13 * b.M34 + a.M14 * b.M44,
		a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31 + a.M24 * b.M41,
		a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32 + a.M24 * b.M42,
		a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33 + a.M24 * b.M43,
		a.M21 * b.M14 + a.M22 * b.M24 + a.M23 * b.M34 + a.M24 * b.M44,
		a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31 + a.M34 * b.M41,
		a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32 + a.M34 * b.M42,
		a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33 + a.M34 * b.M43,
		a.M31 * b.M14 + a.M32 * b.M24 + a.M33 * b.M34 + a.M34 * b.M44,
		a.M41 * b.M11 + a.M42 * b.M21 + a.M43 * b.M31 + a.M44 * b.M41,
		a.M41 * b.M12 + a.M42 * b.M22 + a.M43 * b.M32 + a.M44 * b.M42,
		a.M41 * b.M13 + a.M42 * b.M23 + a.M43 * b.M33 + a.M44 * b.M43,
		a.M41 * b.M14 + a.M42 * b.M24 + a.M43 * b.M34 + a.M44 * b.M44 );

	private static ModelAnimationTrack?[] TracksByNode( ModelAnimation? clip, int count )
	{
		var tracks = new ModelAnimationTrack?[count];
		if ( clip == null )
			return tracks;
		foreach ( var track in clip.Tracks )
		{
			if ( track.NodeIndex >= 0 && track.NodeIndex < count )
				tracks[track.NodeIndex] = track;
		}
		return tracks;
	}

	private static int[] ParentFirst( ModelFile model )
	{
		var result = new List<int>( model.Nodes.Count );
		var seen = new bool[model.Nodes.Count];
		void Visit( int node )
		{
			if ( node < 0 || node >= seen.Length || seen[node] )
				return;
			Visit( model.Nodes[node].ParentIndex );
			seen[node] = true;
			result.Add( node );
		}
		for ( var node = 0; node < model.Nodes.Count; node++ )
			Visit( node );
		return result.ToArray();
	}

	private static int FindTag( ModelFile model, ModelAnimationTrack?[] loftTracks )
	{
		for ( var index = 0; index < model.Nodes.Count; index++ )
		{
			if ( model.Nodes[index].Name.StartsWith( "Track", StringComparison.OrdinalIgnoreCase ) && loftTracks[index]?.Position != null )
				return index;
		}
		for ( var index = 0; index < loftTracks.Length; index++ )
		{
			if ( loftTracks[index]?.Position != null && model.Nodes[index].MeshIndex < 0 )
				return index;
		}
		return -1;
	}
}
