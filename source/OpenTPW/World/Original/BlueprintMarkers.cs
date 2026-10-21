namespace OpenTPW;

/// <summary>Texture of a blueprint marker cell, as an index into the original's dynamic texture table.</summary>
// [BIN:STP-PPC:0x1022C374 dynamic texture table] 16-byte records "blue.tga", "red.tga", "orange.tga", "m_enter.tga", … from Data:Generic:Dynamic:Textures; a face's texture is its index in this table (docs/reverse/BLUEPRINT.md)
public enum BlueprintMarker
{
	/// <summary>A cell the tool may build on.</summary>
	Allowed = 0,
	/// <summary>A cell the tool refuses (including a cell it cannot afford).</summary>
	Refused = 1,
	/// <summary>A cell of the coaster tool's placement area: a yellow and black chequered face (the original draws face type 0x11f from <c>0x5234d0</c>).</summary>
	Area = 2,
	/// <summary>The entrance's outside cell: <c>m_enter</c>, an arrow turned to the direction byte (texture 3).</summary>
	Enter = 3,
	/// <summary>The exit's outside cell: <c>m_exit</c>, an arrow turned to the direction byte (texture 4).</summary>
	Exit = 4,
	/// <summary>The front row of an object's footprint: <c>m_front</c>, turned to the build direction (texture 6).</summary>
	Front = 6,
	/// <summary>The entrance's outside cell when no queue is laid: <c>m_inout</c> (texture 7).</summary>
	InOut = 7,
}

/// <summary>
/// The original's BlueprintMesh: build-tool ghosts drawn as blue (or red) squares over the cells of a path or queue
/// line and over an object's footprint while it is being placed. Each cell gets a flat face 10 units square whose
/// vertices follow the terrain plus a supported occupied offset, 1.5 above it, and ripple with a travelling sine (docs/reverse/BLUEPRINT.md). The geometry is rebuilt when
/// the set of marked cells or sampled occupied heights changes and, while the game runs, every frame (the ripple phase moves).
/// </summary>
// [BIN:STP-PPC:0x100BCB60 dynamic meshes] BlueprintMesh, WaterMesh, FlagsMesh; [BIN:STP-PPC:0x100BD3D4 AddFace] square faces of mesh 0 with a texture index; [BIN:STP-PPC:0x100BF540 square face] code 0 is the flat face at (x, y, z)-(x+10, y, z+10), codes 1/4/0x10/0x40 the vertical faces on the −Y/+X/+Y/−X edges, 10 high
public sealed class BlueprintMarkers : ModelEntity
{
	/// <summary>Height of the marker faces above the cell's marker height.</summary>
	// [BIN:STP-PPC:0x10086998 SetCell ghost] f2 = 1.5 (TOC −0x3070) + the cell height
	public const float Lift = 1.5f;
	/// <summary>Size of a marker face and height of one wall step, in original units (one cell).</summary>
	// [BIN:STP-PPC:0x100BF540 square face] DAT_101f1240 = 10.0
	public const float FaceSize = 10f;

	/// <summary>[EXT:evening] The placement markers keep their daylight look at night so they stay readable.</summary>
	protected override bool FollowsTimeOfDay => false;

	private const int TextureSlots = 16;
	private static readonly string[] TextureNames = ["blue", "red"];
	// [BIN:STP-PPC:0x1022C374 dynamic texture table] slots 3, 4, 6 and 7 are m_enter, m_exit, m_front and m_inout (docs/reverse/BLUEPRINT.md)
	private static readonly (int Slot, string Name)[] ArrowTextures = [(3, "m_enter"), (4, "m_exit"), (6, "m_front"), (7, "m_inout")];

	private readonly ModelHeightfield field;
	private readonly Material material;
	/// <summary>The placement area's texture: made from bytes for these markers alone, so it goes with them (<see cref="OnDelete"/>).</summary>
	private readonly Texture areaTexture;
	private (int X, int Y, BlueprintMarker Marker)[] shown = [];
	private readonly Func<int, int, float?>? heightOffset;
	private Dictionary<(int X, int Y), float?> shownHeights = [];

	public BlueprintMarkers( ModelHeightfield field, Func<int, int, float?>? heightOffset = null )
	{
		this.field = field;
		this.heightOffset = heightOffset;
		Name = "blueprint markers";
		Hidden = true;
		material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
		var textures = TextureNames.Select( name => LoadTexture( $"/generic/dynamic/textures/{name}.wct" ) ).ToList();
		areaTexture = AreaTexture();
		textures.Add( areaTexture );
		while ( textures.Count < TextureSlots )
			textures.Add( textures[0] );
		// The arrow markers are optional art: a data set without them (Theme Park Inc) draws those cells blue.
		foreach ( var (slot, name) in ArrowTextures )
			if ( ResolveTexturePath( $"/generic/dynamic/textures/{name}.wct" ) is { } arrowPath )
				textures[slot] = new Texture( arrowPath, TextureFlags.Repeat );
		material.Set( "Color", textures.ToArray(), SamplerType.Anisotropic );
	}

	/// <summary>
	/// The placement area's texture: translucent yellow and black squares, two by two to a cell.
	/// </summary>
	// [BIN:TPW-EXE:0x5243a6 area face] the cells of the coaster tool's placement area get face type 0x11f; the video (73:20-75:58) shows them as a translucent yellow with dark mottling, and the dynamic texture table holds no such texture (orange.tga is not on the PC CD)
	// [APPROX:COAST-028] the texture is generated: 4 x 4 squares of yellow (255, 214, 0) and near-black (40, 34, 0), both at alpha 205 — evidence needed: the original texture behind face type 0x11f (the dynamic texture table 0x763b3c lists blue, red, orange, m_enter, m_exit; orange.tga is not on the PC CD, and the slot of face type 0x11f is not traced)
	private static Texture AreaTexture()
	{
		const int Size = 8, Square = 4;
		var data = new byte[Size * Size * 4];
		for ( var y = 0; y < Size; y++ )
		{
			for ( var x = 0; x < Size; x++ )
			{
				var dark = ((x / Square) + (y / Square)) % 2 == 1;
				var offset = (y * Size + x) * 4;
				data[offset] = dark ? (byte)40 : (byte)255;
				data[offset + 1] = dark ? (byte)34 : (byte)214;
				data[offset + 2] = 0;
				data[offset + 3] = 205;
			}
		}
		return new Texture( data, Size, Size, TextureFlags.Repeat );
	}

	/// <summary>A texture of the dynamic texture folder (<c>Data/Generic/Dynamic/Textures</c>) by name, <see cref="Texture.Missing"/> when absent.</summary>
	internal static Texture LoadDynamicTexture( string name ) => LoadTexture( $"/generic/dynamic/textures/{name}.wct" );

	private static Texture LoadTexture( string path )
	{
		if ( ResolveTexturePath( path ) is { } resolved )
			return new Texture( resolved, TextureFlags.Repeat );
		Log.Warning( $"Blueprint markers: {path} is missing." );
		return Texture.Missing;
	}

	/// <summary>
	/// The marker texture file: the exact <c>.wct</c> path (Theme Park World), else the same name with any extension in
	/// the same directory (Theme Park Inc ships <c>blue.tga</c>/<c>red.tga</c> there; name lookup as in
	/// <see cref="OriginalTerrain.FindTexture"/>, docs/THEME-PARK-INC.md "Textures").
	/// </summary>
	private static string? ResolveTexturePath( string path )
	{
		if ( FileSystem.FileExists( path ) )
			return path;
		try
		{
			var directory = Path.GetDirectoryName( path )!.Replace( '\\', '/' );
			var name = Path.GetFileNameWithoutExtension( path );
			return FileSystem.GetFiles( directory ).FirstOrDefault( file =>
				string.Equals( Path.GetFileNameWithoutExtension( file ), name, StringComparison.OrdinalIgnoreCase ) );
		}
		catch ( DirectoryNotFoundException )
		{
			return null;
		}
	}

	/// <summary>The cells currently marked (empty while no tool shows a ghost).</summary>
	public IReadOnlyList<(int X, int Y, BlueprintMarker Marker)> Cells => shown;

	/// <summary>The ripple phase (the original's global <c>0x874FC0</c>).</summary>
	public float Phase => phase;

	/// <summary>Shows exactly these cells (later duplicates of a cell win); an empty list hides the markers.</summary>
	public void Show( IReadOnlyList<(int X, int Y, BlueprintMarker Marker)> cells ) => Show( cells, null );

	/// <summary>
	/// Shows exactly these cells; <paramref name="directions"/> gives the direction byte (1, 4, 0x10 or 0x40) of the cells
	/// whose texture is an arrow (<see cref="BlueprintMarker.Front"/>, <see cref="BlueprintMarker.Enter"/>, <see cref="BlueprintMarker.Exit"/>).
	/// </summary>
	public void Show( IReadOnlyList<(int X, int Y, BlueprintMarker Marker)> cells, IReadOnlyDictionary<(int X, int Y), byte>? directions )
	{
		var unique = cells
			.Where( cell => cell.X >= 0 && cell.Y >= 0 && cell.X < field.CellCountX && cell.Y < field.CellCountZ )
			.GroupBy( cell => (cell.X, cell.Y) ).Select( group => group.Last() ).ToArray();
		var directionList = directions == null ? [] : unique.Where( cell => directions.ContainsKey( (cell.X, cell.Y) ) ).Select( cell => directions[(cell.X, cell.Y)] ).ToArray();
		var heights = CaptureHeights( field, unique, heightOffset );
		if ( unique.SequenceEqual( shown ) && directionList.AsSpan().SequenceEqual( shownDirectionList ) && heights.SequenceEqual( shownHeights ) )
			return;
		shown = unique;
		shownHeights = heights;
		shownDirectionList = directionList;
		shownDirections = directions == null ? [] : directions.Where( pair => unique.Any( cell => (cell.X, cell.Y) == pair.Key ) ).ToDictionary();
		Rebuild();
	}

	public void Clear() => Show( [] );

	/// <summary>
	/// Advances the ripple phase while the game runs. The original adds 0.1 to the phase on every unpaused frame of its
	/// dynamic-mesh flush and the vertex writer rebuilds every face from it, so the markers' height keeps moving.
	/// </summary>
	// [BIN:TPW-EXE:0x0053C762 ripple phase] the per-frame flush subtracts the constant 0x700DF8 (-0.1) from the global 0x874FC0 unless the pause test 0x402DF0 answers true
	// [EXT:blueprint-phase-step] the original steps the phase once per rendered frame (frame-rate dependent); OpenTPW steps 0.1 per 1/30 s of real time so the ripple speed does not depend on the display's frame rate
	public void Update( float deltaSeconds, bool paused )
	{
		if ( paused || shown.Length == 0 || !(deltaSeconds > 0) )
			return;
		phase = (phase + PhaseStep * Math.Min( deltaSeconds, 0.25f ) * AssumedFrameRate) % (MathF.PI * 2 * 64);
		// Only the heights move with the phase: the cells are unchanged, so the model's own vertex buffer takes the new vertices
		// instead of allocating a model and its buffers every frame.
		if ( Model is { } model && model.HasDynamicVertices )
		{
			var (vertices, _) = BuildGeometry( field, shown, shownDirections, phase, shownHeights );
			model.UpdateVertices( vertices );
			return;
		}
		Rebuild();
	}

	private void Rebuild()
	{
		var old = Model;
		Model = null;
		Hidden = shown.Length == 0;
		if ( shown.Length > 0 )
		{
			var (vertices, indices) = BuildGeometry( field, shown, shownDirections, phase, shownHeights );
			Model = new Model( vertices, indices, material, dynamicVertices: true );
		}
		old?.ReleaseBuffers();
	}

	/// <summary>Phase added per frame.</summary>
	public const float PhaseStep = 0.1f;
	private const float AssumedFrameRate = 30f;
	private float phase;
	private byte[] shownDirectionList = [];
	private IReadOnlyDictionary<(int X, int Y), byte> shownDirections = new Dictionary<(int X, int Y), byte>();

	/// <summary>
	/// Leaving the park releases the marker model, the markers' own material and the area texture they made from bytes (every park made a new
	/// one, and each stayed for the session); the marker textures loaded by path are shared game textures and stay.
	/// </summary>
	protected override void OnDelete()
	{
		Model?.ReleaseBuffers();
		Model = null;
		shown = [];
		material.ReleaseOwnedResources();
		areaTexture.ReleaseOwned();
	}

	private const int SineEntries = 4096;
	private static readonly float[] SineTable = Enumerable.Range( 0, SineEntries ).Select( index => (float)Math.Sin( (double)index / SineEntries * 6.2831854820251465 ) ).ToArray();

	/// <summary>
	/// The ripple the face builder adds to a vertex's height: a lookup in the 4096-entry sine table at
	/// <c>(phase + x + z)</c> radians, rounded to the nearest entry (amplitude 1 unit).
	/// </summary>
	// [BIN:TPW-EXE:0x0053DDD0 blueprint vertex writer] for a texture 0-14 face the vertex y becomes y + terrain(x, z) (0x4527F0) + sine[round((phase + x + z) * 4096 / 2π) & 4095]; the table is filled by 0x4708D0 (callers 0x449EB0, 0x44A1D0 pass 0x1000) with sin(i / 4096 * 2π)
	public static float Ripple( float phase, float x, float z )
	{
		var index = (int)MathF.Round( (phase + (z + x)) * (SineEntries * 0.15915493667125702f), MidpointRounding.ToEven );
		return SineTable[index & (SineEntries - 1)];
	}

	/// <summary>
	/// The height of one marker vertex in original units: the terrain under it, the face lift and the ripple. The original's
	/// <c>SetCell</c> gives a plain cell height 0 and the vertex writer adds the terrain, so a plain marker drapes over the ground.
	/// </summary>
	// [BIN:TPW-EXE:0x005330A5 plain cell height] SetCell 0x532FC0 passes 0 for a plain cell; the face is added at y + 1.5 (0x5338E7, constant 0x700D5C = -1.5 subtracted) and 0x53DDD0 adds the terrain at the vertex and the ripple
	// [APPROX:BLUEPRINT-001] occupied offsets are supplied separately for proven TPW object cells; unsupported secondary cells, missing/invalid maps, types 7/30 and TPI retain the unraised fallback (docs/reverse/BLUEPRINT.md).
	public static float VertexHeight( ModelHeightfield field, int cornerX, int cornerY, float phase ) =>
		field.GetCornerHeight( cornerX, cornerY ) + Lift + Ripple( phase, cornerX * FaceSize, cornerY * FaceSize );

	internal static (Vertex[] Vertices, uint[] Indices) BuildGeometry( ModelHeightfield field, IReadOnlyList<(int X, int Y, BlueprintMarker Marker)> cells,
		IReadOnlyDictionary<(int X, int Y), byte>? directions = null, float phase = 0,
		IReadOnlyDictionary<(int X, int Y), float?>? heights = null )
	{
		var vertices = new List<Vertex>();
		var indices = new List<uint>();
		foreach ( var (x, y, marker) in cells )
		{
			var offset = heights?.GetValueOrDefault( (x, y) );
			var raised = offset ?? 0;
			var left = x * FaceSize;
			var front = y * FaceSize;
			byte direction = 0;
			if ( directions != null )
				directions.TryGetValue( (x, y), out direction );
			AddFace( vertices, indices, field, (int)marker, direction,
				new( left, raised + VertexHeight( field, x, y, phase ), front ), new( left + FaceSize, raised + VertexHeight( field, x + 1, y, phase ), front ),
				new( left, raised + VertexHeight( field, x, y + 1, phase ), front + FaceSize ), new( left + FaceSize, raised + VertexHeight( field, x + 1, y + 1, phase ), front + FaceSize ) );

			if ( offset is not { } current ) continue;
			var wallMarker = marker == BlueprintMarker.Refused ? BlueprintMarker.Refused : marker == BlueprintMarker.Area ? BlueprintMarker.Area : BlueprintMarker.Allowed;
			// PC SetCell 0x53336B–0x53391B: lower neighbour to current, step 10; no cell means no wall.
			foreach ( var (dx, dy, ax, ay, bx, by) in Edges )
			{
				if ( heights?.GetValueOrDefault( (x + dx, y + dy) ) is not { } neighbour ) continue;
				for ( var step = (long)neighbour; step < current; step += (int)FaceSize )
				{
					var a = new System.Numerics.Vector3( (x + ax) * FaceSize, step + VertexHeight( field, x + ax, y + ay, phase ), (y + ay) * FaceSize );
					var b = new System.Numerics.Vector3( (x + bx) * FaceSize, step + VertexHeight( field, x + bx, y + by, phase ), (y + by) * FaceSize );
					var up = new System.Numerics.Vector3( 0, FaceSize, 0 );
					AddFace( vertices, indices, field, (int)wallMarker, 0, a, b, a + up, b + up );
				}
			}
		}
		return (vertices.ToArray(), indices.ToArray());
	}

	private static readonly (int Dx, int Dy, int Ax, int Ay, int Bx, int By)[] Edges =
		[(0, -1, 0, 0, 1, 0), (1, 0, 1, 0, 1, 1), (0, 1, 1, 1, 0, 1), (-1, 0, 0, 1, 0, 0)];

	// Snapshot the map (not just preview neighbours), so occupancy changes rebuild topology even while paused.
	internal static Dictionary<(int X, int Y), float?> CaptureHeights( ModelHeightfield field,
		IReadOnlyList<(int X, int Y, BlueprintMarker Marker)> cells, Func<int, int, float?>? sample )
	{
		var result = new Dictionary<(int X, int Y), float?>();
		if ( sample == null ) return result;
		foreach ( var (x, y, _) in cells )
		{
			Capture( x, y );
			foreach ( var (dx, dy, _, _, _, _) in Edges ) Capture( x + dx, y + dy );
		}
		return result;
		void Capture( int x, int y )
		{
			if ( !result.ContainsKey( (x, y) ) )
				result[(x, y)] = x >= 0 && y >= 0 && x < field.CellCountX && y < field.CellCountZ ? sample( x, y ) : null;
		}
	}

	/// <summary>
	/// The corner order of a face's texture coordinates by direction byte: vertex i takes the unit-square entry <c>order[i]</c>
	/// (0 = (0,0), 1 = (1,0), 2 = (0,1), 3 = (1,1)); every byte but 4, 0x10 and 0x40 uses the default order.
	/// </summary>
	// [BIN:TPW-EXE:0x0053DFF7 face corner order] the face builder 0x53DF30 picks the four table entries by the direction byte: default (0,1,2,3), 0x40 (1,3,0,2), 0x10 (3,2,1,0), 4 (2,0,3,1)
	public static int[] CornerOrder( byte direction ) => direction switch
	{
		0x40 => [1, 3, 0, 2],
		0x10 => [3, 2, 1, 0],
		4 => [2, 0, 3, 1],
		_ => [0, 1, 2, 3],
	};

	/// <summary>One textured quad from four original-space corners (u: first→second, v: first→third), both sides facing out.</summary>
	// [BIN:TPW-EXE:0x0053C3F0 blueprint UV table] the runtime table 0x871EE8 (12-byte entries, u at +0, v at +4) holds the unit square (0,0), (1,0), (0,1), (1,1) in entries 0-3 and (0.5, 0.5) in entry 4; entries 5-8 are the rotating +-0.4 square of the track symbols
	// [BIN:TPW-EXE:0x0053DF30 blueprint face builder] textures 8-14 (m_link to m_erase) use the rotating set from entry 5, every other texture the unit square from entry 0 (0x53DFE6–0x53DFF2); the corner order follows the direction byte: default (0,1,2,3), 0x40 (1,3,0,2), 0x10 (3,2,1,0), 4 (2,0,3,1) (0x53DFF7–0x53E034), which shows only on a non-uniform texture (the arrows of m_front, m_enter and m_exit; the blue and red markers are uniform) and is applied here by CornerOrder; both sides are drawn (no hardware cull)
	private static void AddFace( List<Vertex> vertices, List<uint> indices, ModelHeightfield field, int slot, byte direction,
		System.Numerics.Vector3 a, System.Numerics.Vector3 b, System.Numerics.Vector3 c, System.Numerics.Vector3 d )
	{
		var positions = new[] { a, b, c, d }.Select( corner => OriginalParkPlacement.ToEngine( field, corner ) ).ToArray();
		var normal = System.Numerics.Vector3.Normalize( System.Numerics.Vector3.Cross( positions[1] - positions[0], positions[2] - positions[0] ) );
		var square = new[] { new Vector2( 0, 0 ), new Vector2( 1, 0 ), new Vector2( 0, 1 ), new Vector2( 1, 1 ) };
		var order = CornerOrder( direction );
		var uv = order.Select( entry => square[entry] ).ToArray();
		foreach ( var (offsets, sign) in new[] { (new uint[] { 0, 2, 3, 3, 1, 0 }, 1f), (new uint[] { 0, 1, 3, 3, 2, 0 }, -1f) } )
		{
			var start = (uint)vertices.Count;
			for ( var index = 0; index < 4; index++ )
				vertices.Add( new Vertex { Position = positions[index], Normal = normal * sign, TexCoords = uv[index], TexIndex = slot } );
			foreach ( var offset in offsets )
				indices.Add( start + offset );
		}
	}
}
