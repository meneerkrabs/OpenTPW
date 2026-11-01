namespace OpenTPW;

/// <summary>Texture of a blueprint marker cell, as an index into the original's dynamic texture table.</summary>
// [BIN:STP-PPC:0x1022C374 dynamic texture table] 16-byte records "blue.tga", "red.tga", "orange.tga", "m_enter.tga", … from Data:Generic:Dynamic:Textures; a face's texture is its index in this table (docs/reverse/BLUEPRINT.md)
public enum BlueprintMarker
{
	/// <summary>A cell the tool may build on.</summary>
	Allowed = 0,
	/// <summary>A cell the tool refuses (including a cell it cannot afford).</summary>
	Refused = 1,
	/// <summary>A cell of the coaster tool's placement area: the level's translucent orange face (face type 0x11f, <c>0x5243a6</c>, table slot 2).</summary>
	Area = 2,
	/// <summary>The entrance's outside cell: <c>m_enter</c>, an arrow turned to the direction byte (texture 3).</summary>
	Enter = 3,
	/// <summary>The exit's outside cell: <c>m_exit</c>, an arrow turned to the direction byte (texture 4).</summary>
	Exit = 4,
	/// <summary>A tile the track edit tool can pick: <c>m_break</c>, one face over the tile's 2 x 2 cells (texture 9, <see cref="TrackSymbolFace"/>).</summary>
	Break = 9,
	/// <summary>A tile of the track edit tool's cut: <c>m_erase</c>, one face over the tile's 2 x 2 cells (texture 13).</summary>
	Erase = 13,
	/// <summary>A tile the track edit tool's chooser refuses (the answer 1): the red face (texture 1) over the tile's 2 x 2 cells with red walls, which blinks like every red face.</summary>
	RefusedTile = 17,
	/// <summary>A tile the kart or water line tool's ghost may lay: the blue face (texture 0) over the tile's 2 x 2 cells with blue walls (<see cref="TileLineGhost"/>).</summary>
	AllowedTile = 18,
	/// <summary>A tile where the line tool's line meets a tile of its own kind: <c>m_end</c> (texture 11) over the tile's 2 x 2 cells, which turns with the camera.</summary>
	EndTile = 19,
	/// <summary>The cell beside a station's track end: <c>m_direct</c>, an arrow turned to the direction byte (texture 5).</summary>
	Direct = 5,
	/// <summary>The front row of an object's footprint: <c>m_front</c>, turned to the build direction (texture 6).</summary>
	Front = 6,
	/// <summary>A line cell where the tool's line ends on an existing path (or queue): <c>m_link</c> (texture 8), which turns with the camera.</summary>
	Link = 8,
	/// <summary>A line cell where the tool's line meets a cell of its own kind: <c>m_end</c> (texture 11), which turns with the camera.</summary>
	End = 11,
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
	private static readonly (int Slot, string Name)[] ArrowTextures = [(3, "m_enter"), (4, "m_exit"), (5, "m_direct"), (6, "m_front"), (7, "m_inout"), (8, "m_link"), (9, "m_break"), (11, "m_end"), (13, "m_erase")];

	private readonly ModelHeightfield field;
	private readonly Material material;
	/// <summary>The placement area's texture (the level's orange.wct, a shared game texture that stays when the markers go).</summary>
	private readonly Texture areaTexture;
	private (int X, int Y, BlueprintMarker Marker)[] shown = [];
	private readonly Func<int, int, float?>? heightOffset;
	private Dictionary<(int X, int Y), float?> shownHeights = [];
	private readonly Func<int, int, float?>? tileRaise;
	private Dictionary<(int X, int Y), float?> shownTileRaises = [];

	public BlueprintMarkers( ModelHeightfield field, Func<int, int, float?>? heightOffset = null, string? levelName = null, Func<int, int, float?>? tileRaise = null )
	{
		this.field = field;
		this.heightOffset = heightOffset;
		this.tileRaise = tileRaise;
		Name = "blueprint markers";
		Hidden = true;
		material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
		var textures = TextureNames.Select( name => LoadTexture( $"/generic/dynamic/textures/{name}.wct" ) ).ToList();
		areaTexture = AreaTexture( levelName );
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
	/// The placement area's texture: the level's own <c>orange.wct</c> (table slot 2), from the level's dynamic archive (<c>Levels/&lt;level&gt;/dynamic.wad</c>, <c>textures</c>, or <c>stexture</c> in the low-detail set).
	/// </summary>
	// [BIN:TPW-EXE:0x5243a6 area face] the cells of the coaster tool's placement area get face type 0x11f (the area loop 0x5243a6..0x524458 calls 0x536100(0x11f, x, y) → 0x532fc0, whose test of bit 0x100 at 0x533014 selects the dynamic face branch): its kind is the verdict 0x535670(0x1f, 0) at 0x5331b5, and 0x535670 opens with "type 0x1f → 2", so the face builder 0x53c8d0 stores kind 2 and the face renderer (0x53da77, 0x53dadd) takes the 16-byte record 2 of the dynamic texture table 0x763b38: orange.tga
	// [BIN:TPW-EXE:0x53d790 dynamic texture folder] the table's file names get the level's dynamic folder (Data\Levels\<level>\Dynamic\Textures, \STexture in the low-detail set, 0x54dcc0); each level's dynamic.wad holds orange.wct in both folders (records 0 to 14 of the table 0x763b38 all carry texture flags 7, so the area face is drawn as the other markers; the video's translucent yellow with dark mottling: average rgba 216,161,1 at alpha 192 in the jungle, 158 in the space level), not the generic folder, which has no orange
	private static Texture AreaTexture( string? levelName )
	{
		if ( AreaTexturePath( levelName ) is { } path )
			return new Texture( path, TextureFlags.Repeat );
		Log.Warning( $"Blueprint markers: the level's dynamic archive has no orange.wct ({levelName ?? "no level"})." );
		return Texture.Missing;
	}

	/// <summary>The file of the placement area's orange texture in <paramref name="levelName"/>'s dynamic archive (the low-detail folder first in the low-detail set), or null.</summary>
	internal static string? AreaTexturePath( string? levelName )
	{
		if ( string.IsNullOrEmpty( levelName ) )
			return null;
		foreach ( var directory in ObjectAssets.TextureDirectoryNames( $"/levels/{levelName}/dynamic", levelName ) )
		{
			if ( ResolveTexturePath( $"{directory}/orange.wct" ) is { } path )
				return path;
		}
		return null;
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

	/// <summary>
	/// The camera's yaw in the original's radians (<c>0x790A38</c>, <see cref="ParkCameraMode.OriginalYaw"/>), which turns the texture coordinates of the track symbols (<see cref="BlueprintMarker.Break"/>,
	/// <see cref="BlueprintMarker.Erase"/>) so they stay upright on the screen.
	/// </summary>
	public float SymbolAngle { get; set; }

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
		var tileRaises = CaptureTileRaises( field, unique, tileRaise );
		if ( unique.SequenceEqual( shown ) && directionList.AsSpan().SequenceEqual( shownDirectionList ) && heights.SequenceEqual( shownHeights ) && tileRaises.SequenceEqual( shownTileRaises ) )
			return;
		shown = unique;
		shownHeights = heights;
		shownTileRaises = tileRaises;
		shownDirectionList = directionList;
		shownDirections = directions == null ? [] : directions.Where( pair => unique.Any( cell => (cell.X, cell.Y) == pair.Key ) ).ToDictionary();
		Rebuild();
	}

	public void Clear() => Show( [] );

	/// <summary>
	/// While no cell is shown, shows none: nothing changes, but the runtime's first-call work of <see cref="Show"/> (its queries over the cell tuples,
	/// several milliseconds in the browser) is done before the park HUD's first update calls it in the entering frame (docs/BROWSER-PERFORMANCE.md, "Build steps").
	/// </summary>
	internal void WarmUp()
	{
		if ( shown.Length == 0 )
			Show( new List<(int X, int Y, BlueprintMarker Marker)>(), null );
	}

	/// <summary>
	/// Advances the ripple phase, the red faces' blink and the ghost's glide. The original's flush runs once per rendered frame: it adds 0.1 to the ripple phase, counts the blink
	/// (hidden for two frames, shown for seven) and moves the glide point half the way to the cursor, all unless the game is paused.
	/// </summary>
	// [BIN:TPW-EXE:0x0053C762 ripple phase] the per-frame flush subtracts the constant 0x700DF8 (-0.1) from the global 0x874FC0 unless the pause test 0x402DF0 answers true
	// [BIN:TPW-EXE:0x0053C7BB red blink] the flush (0x53C3F0) adds the game clock's time scale (0x785970 +0x20, truncated: the game speed factor, 1.0 at normal speed, which 0x4032F0 and 0x403340 step by 1.25 within 0.25 and 2.0) to the counter 0x874FC8 when the clock is not paused (+0x30): with the flag 0x763C98 clear the flag is set after the counter exceeds 1 and with it set cleared after it exceeds 6 (the counter restarts at 0 at each change), and 0x53C8D0 queues a face of texture 1 (red) only while the flag is set
	// [BIN:TPW-EXE:0x0053C42B glide] every flush moves 0x820AFC/0x820AF8 by half the way to 10 x the hover's cell (0x8186B8/0x8186B4), and the face builder 0x53DF30 offsets a face's x and z by (glide - 10 x cell) for a texture below 13 while the tool is the placement (4) or an edit tool (0xE, 0x13) and the flag 0x874FC4 is clear (0x53DF67..0x53DFCF)
	// [EXT:blueprint-phase-step] only the frame-rate part: the original steps once per rendered frame (frame-rate dependent); OpenTPW steps once per 1/30 s of real time so the ripple, the blink and the glide do not depend on the display's frame rate; the blink counter moves by the game speed per step (1, 2 or 3 in OpenTPW's speeds) as the original's by the clock scale
	public void Update( float deltaSeconds, bool paused, int timeScale = 1 )
	{
		if ( paused || !(deltaSeconds > 0) )
			return;
		var ticks = tickRemainder + Math.Min( deltaSeconds, 0.25f ) * AssumedFrameRate;
		var whole = (int)ticks;
		tickRemainder = ticks - whole;
		for ( var tick = 0; tick < whole; tick++ )
			Tick( timeScale );
		if ( shown.Length == 0 )
			return;
		phase = (phase + PhaseStep * Math.Min( deltaSeconds, 0.25f ) * AssumedFrameRate) % (MathF.PI * 2 * 64);
		// Only the heights move with the phase: the cells are unchanged, so the model's own vertex buffer takes the new vertices
		// instead of allocating a model and its buffers every frame.
		if ( Model is { } model && model.HasDynamicVertices )
		{
			var (vertices, _) = Geometry();
			model.UpdateVertices( vertices );
			return;
		}
		Rebuild();
	}

	private (Vertex[] Vertices, uint[] Indices) Geometry() =>
		BuildGeometry( field, shown, shownDirections, phase, shownHeights, SymbolAngle, flush.RedShown, GlideActive ? GlideOffset : default, shownTileRaises );

	/// <summary>The cell the hover last named (the original's <c>0x8186B8/0x8186B4</c>), which the glide point chases.</summary>
	public (int X, int Y) GlideCursor { get; set; }

	/// <summary>The tool is the placement or an edit tool: the faces of a texture below 13 lag behind the cursor by the glide's remaining distance.</summary>
	public bool GlideActive { get; set; }

	/// <summary>The offset the glide gives a face (original units): the glide point less 10 x the cursor cell.</summary>
	internal Vector2 GlideOffset => flush.Offset( GlideCursor );

	/// <summary>Whether the red faces are in their shown phase (the blink flag <c>0x763C98</c>).</summary>
	internal bool RedShown => flush.RedShown;

	private void Tick( int timeScale ) => flush.Tick( GlideCursor, timeScale );

	private float tickRemainder;
	private readonly BlueprintFlush flush = new();

	private void Rebuild()
	{
		var old = Model;
		Model = null;
		Hidden = shown.Length == 0;
		if ( shown.Length > 0 )
		{
			var (vertices, indices) = Geometry();
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
	/// Leaving the park releases the marker model and the markers' own material; the marker textures loaded by path (the area's orange included) are shared game textures and stay.
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
	/// <summary>The sine table entry nearest to <paramref name="angle"/> radians (the original's rounding, <c>fistp</c>).</summary>
	private static int TableIndex( float angle ) => (int)MathF.Round( angle * (SineEntries * 0.15915493667125702f), MidpointRounding.ToEven ) & (SineEntries - 1);
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
	// [APPROX:BLUEPRINT-001] a typed TPW world supplies each cell's raise (BlueprintHeights, the tile raise of TileLineGhost); a world that types no cells ([EXT:untyped-harness-world]), a missing or invalid map and Theme Park Inc keep the unraised terrain (docs/reverse/BLUEPRINT.md).
	public static float VertexHeight( ModelHeightfield field, int cornerX, int cornerY, float phase ) =>
		field.GetCornerHeight( cornerX, cornerY ) + Lift + Ripple( phase, cornerX * FaceSize, cornerY * FaceSize );

	/// <summary>The slot (the texture index) a marker's faces take: the symbols' own, red for a refused tile.</summary>
	internal static int SlotOf( BlueprintMarker marker ) => marker switch { BlueprintMarker.RefusedTile => (int)BlueprintMarker.Refused, BlueprintMarker.AllowedTile => (int)BlueprintMarker.Allowed, BlueprintMarker.EndTile => (int)BlueprintMarker.End, _ => (int)marker };

	/// <summary>Whether a marker is a face over a whole tile (2 x 2 cells) rather than over one cell.</summary>
	internal static bool IsTileMarker( BlueprintMarker marker ) => marker is BlueprintMarker.Break or BlueprintMarker.Erase or BlueprintMarker.RefusedTile or BlueprintMarker.AllowedTile or BlueprintMarker.EndTile;

	internal static (Vertex[] Vertices, uint[] Indices) BuildGeometry( ModelHeightfield field, IReadOnlyList<(int X, int Y, BlueprintMarker Marker)> cells,
		IReadOnlyDictionary<(int X, int Y), byte>? directions = null, float phase = 0,
		IReadOnlyDictionary<(int X, int Y), float?>? heights = null, float symbolAngle = 0, bool showRed = true, Vector2 glide = default,
		IReadOnlyDictionary<(int X, int Y), float?>? tileRaises = null )
	{
		var vertices = new List<Vertex>();
		var indices = new List<uint>();
		// [BIN:TPW-EXE:0x0053C8D0 red face blink] a face of texture 1 is queued only while the blink flag 0x763C98 is set; [BIN:TPW-EXE:0x0053DF67 glide] a texture below 13 is moved by the glide offset in x and z
		void Face( int slot, byte direction, Vector2[]? uv, System.Numerics.Vector3 a, System.Numerics.Vector3 b, System.Numerics.Vector3 c, System.Numerics.Vector3 d )
		{
			// a hidden red face keeps its place in the arrays (a dynamic model keeps its vertex count) as a point
			if ( slot == (int)BlueprintMarker.Refused && !showRed )
				(b, c, d) = (a, a, a);
			if ( slot < 13 && glide != default )
			{
				var shift = new System.Numerics.Vector3( glide.X, 0, glide.Y );
				(a, b, c, d) = (a + shift, b + shift, c + shift, d + shift);
			}
			if ( uv != null )
				AddFace( vertices, indices, field, slot, uv, a, b, c, d );
			else
				AddFace( vertices, indices, field, slot, direction, a, b, c, d );
		}
		float Raise( int x, int y ) => heights?.GetValueOrDefault( (x, y) ) ?? 0;
		foreach ( var (x, y, marker) in cells )
		{
			var offset = heights?.GetValueOrDefault( (x, y) );
			var raised = offset ?? 0;
			var left = x * FaceSize;
			var front = y * FaceSize;
			if ( IsTileMarker( marker ) )
			{
				AddTileSymbol( Face, field, SlotOf( marker ), x, y, TileRaise( heights, tileRaises, x, y ), phase, symbolAngle );
				AddTileWalls( Face, field, marker, x, y, heights, tileRaises, phase );
				continue;
			}
			byte direction = 0;
			if ( directions != null )
				directions.TryGetValue( (x, y), out direction );
			// [BIN:TPW-EXE:0x0053DFE6 symbol table pick] a face of a texture from 8 to 14 (m_link, m_break, m_end, m_erase) takes the rotating set whatever its size
			Face( SlotOf( marker ), direction, SlotOf( marker ) is >= 8 and <= 14 ? RotatingUv( symbolAngle ) : null,
				new( left, raised + VertexHeight( field, x, y, phase ), front ), new( left + FaceSize, raised + VertexHeight( field, x + 1, y, phase ), front ),
				new( left, raised + VertexHeight( field, x, y + 1, phase ), front + FaceSize ), new( left + FaceSize, raised + VertexHeight( field, x + 1, y + 1, phase ), front + FaceSize ) );

			if ( offset is not { } current ) continue;
			var wallMarker = marker == BlueprintMarker.Refused ? BlueprintMarker.Refused : marker == BlueprintMarker.Area ? BlueprintMarker.Area : BlueprintMarker.Allowed;
			// PC SetCell 0x53336B–0x53391B: lower neighbour to current, step 10; no cell means no wall.
			foreach ( var (_, step, ax, ay, bx, by) in WallSteps( x, y, current, ( nx, ny ) => heights?.GetValueOrDefault( (nx, ny) ) ) )
			{
				var a = new System.Numerics.Vector3( (x + ax) * FaceSize, step + VertexHeight( field, x + ax, y + ay, phase ), (y + ay) * FaceSize );
				var b = new System.Numerics.Vector3( (x + bx) * FaceSize, step + VertexHeight( field, x + bx, y + by, phase ), (y + by) * FaceSize );
				var up = new System.Numerics.Vector3( 0, FaceSize, 0 );
				Face( SlotOf( wallMarker ), 0, null, a, b, a + up, b + up );
			}
		}
		return (vertices.ToArray(), indices.ToArray());
	}

	/// <summary>
	/// The height a tile's face stands at: the greatest of the raises of its four cells (a cell without a raise counts 0).
	/// </summary>
	// [BIN:TPW-EXE:0x00536CA0 tile raise] the neighbour raise of the track ghost: the cell's own raise (a track, object or type-7 cell: 10 x ((height + 9) / 10), 0x452AE0 → 0x67A830) and those of the cells at 0x7CDBA8, 0x7CDBC8 and 0x7CDBB8 (the other three of the 2 x 2 tile) are compared in the x87 and the greatest is returned, starting from the argument (0)
	internal static float TileRaise( IReadOnlyDictionary<(int X, int Y), float?>? heights, IReadOnlyDictionary<(int X, int Y), float?>? tileRaises, int x, int y ) =>
		tileRaises != null && tileRaises.TryGetValue( (x, y), out var exact ) ? exact ?? 0f : TileRaise( heights, x, y );

	internal static float TileRaise( IReadOnlyDictionary<(int X, int Y), float?>? heights, int x, int y )
	{
		var best = 0f;
		foreach ( var (dx, dy) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) } )
			if ( heights?.GetValueOrDefault( (x + dx, y + dy) ) is { } raise && raise > best )
				best = raise;
		return best;
	}

	/// <summary>
	/// The walls of a tile symbol: on each of the four sides, where the tile two cells away that way stands lower, faces 20 units wide and 10 high from that tile's raise up to this one's, blue (red for a
	/// refused tile).
	/// </summary>
	// [BIN:TPW-EXE:0x005370E0 ghost walls] after the tile's face (0x53C8D0 at the tile's raise + 1.5, size flag 1) each side's neighbour (the cells 0x7CDBA0, 0x7CDBC8, 0x7CDBA8 and 0x7CDBD0 two cells on, side codes 1, 4, 0x10 and 0x40) is read through 0x536CA0 and, when its raise differs, a wall face is queued for every 10 units from the neighbour's raise to this tile's (0x537A35..0x53810F); the wall's texture is 0 (blue), or 1 for the refused tile
	private static void AddTileWalls( Action<int, byte, Vector2[]?, System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3> face, ModelHeightfield field, BlueprintMarker marker, int x, int y,
		IReadOnlyDictionary<(int X, int Y), float?>? heights, IReadOnlyDictionary<(int X, int Y), float?>? tileRaises, float phase )
	{
		var current = TileRaise( heights, tileRaises, x, y );
		var wallSlot = marker == BlueprintMarker.RefusedTile ? (int)BlueprintMarker.Refused : (int)BlueprintMarker.Allowed;
		foreach ( var (dx, dy, ax, ay, bx, by) in TileEdges )
		{
			// [BIN:TPW-EXE:0x005373CE missing neighbour] a side whose tile lies off the map gets no wall
			if ( tileRaises != null && tileRaises.TryGetValue( (x + dx, y + dy), out var known ) && known == null )
				continue;
			var neighbour = TileRaise( heights, tileRaises, x + dx, y + dy );
			for ( var step = (long)neighbour; step < current; step += (int)FaceSize )
			{
				var a = new System.Numerics.Vector3( (x + ax) * FaceSize, step + VertexHeight( field, x + ax, y + ay, phase ), (y + ay) * FaceSize );
				var b = new System.Numerics.Vector3( (x + bx) * FaceSize, step + VertexHeight( field, x + bx, y + by, phase ), (y + by) * FaceSize );
				var up = new System.Numerics.Vector3( 0, FaceSize, 0 );
				face( wallSlot, 0, null, a, b, a + up, b + up );
			}
		}
	}

	private static readonly (int Dx, int Dy, int Ax, int Ay, int Bx, int By)[] TileEdges =
		[(0, -2, 0, 0, 2, 0), (2, 0, 2, 0, 2, 2), (0, 2, 2, 2, 0, 2), (-2, 0, 0, 2, 0, 0)];

	/// <summary>
	/// One track symbol face: 20 x 20 units over the tile whose origin cell is (<paramref name="x"/>, <paramref name="y"/>), its four vertices on the ground plus the face lift and the ripple like every
	/// face (<see cref="VertexHeight"/>), its texture coordinates the ±0.4 square about the centre turned by the camera's yaw.
	/// </summary>
	// [BIN:TPW-EXE:0x0053C3F0 symbol UV set] the entries 5 to 8 of the table 0x871EE8 are the square (-0.4, -0.4), (0.4, -0.4), (-0.4, 0.4), (0.4, 0.4) (0x53C480..0x53C4C2), each turned about the origin by the camera yaw 0x790A38 with the 4096-entry sine table (0x53C4F1..0x53C52C) and moved by (0.5, 0.5) (0x53C54F..0x53C612); a face of texture 8 to 14 takes them (0x53DFED)
	// [BIN:TPW-EXE:0x0053DB0A symbol face] the flush hands each face record (x, y, z, type, direction byte, texture, size flag) to 0x53DF30, which takes the table entries from 5 for a texture from 8 to 14 and builds a floor face (type 0) 20 units square when the size flag is set
	private static void AddTileSymbol( Action<int, byte, Vector2[]?, System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3> face, ModelHeightfield field, int slot, int x, int y, float raised, float phase, float angle )
	{
		var uv = RotatingUv( angle );
		var size = 2 * FaceSize;
		var (left, front) = (x * FaceSize, y * FaceSize);
		// [BIN:TPW-EXE:0x0053DFE6 symbol table pick] only a texture from 8 to 14 takes the rotating set (entry 5 on); a texture below 8 over a tile (the refused tile's red) takes the unit square
		face( slot, 0, slot >= 8 ? uv : null,
			new( left, raised + VertexHeight( field, x, y, phase ), front ), new( left + size, raised + VertexHeight( field, x + 2, y, phase ), front ),
			new( left, raised + VertexHeight( field, x, y + 2, phase ), front + size ), new( left + size, raised + VertexHeight( field, x + 2, y + 2, phase ), front + size ) );
	}

	/// <summary>The rotating texture set of the track symbols: the ±0.4 square about the centre turned by the camera's yaw.</summary>
	private static Vector2[] RotatingUv( float angle )
	{
		var sin = SineTable[TableIndex( angle )];
		var cos = SineTable[TableIndex( angle + 1.5707964f )];
		Vector2 Uv( float u, float v ) => new( 0.5f + (cos * u - sin * v), 0.5f + (sin * u + cos * v) );
		return [Uv( -0.4f, -0.4f ), Uv( 0.4f, -0.4f ), Uv( -0.4f, 0.4f ), Uv( 0.4f, 0.4f )];
	}

	private static readonly (int Dx, int Dy, int Ax, int Ay, int Bx, int By, int Side)[] Edges =
		[(0, -1, 0, 0, 1, 0, 1), (1, 0, 1, 0, 1, 1, 4), (0, 1, 1, 1, 0, 1, 0x10), (-1, 0, 0, 1, 0, 0, 0x40)];

	/// <summary>
	/// The wall faces a cell's marker gets: on each of the four sides in the order of the face codes 1, 4, 0x10 and 0x40, one face for every 10 units from the neighbour's raise up to the cell's, where the neighbour's
	/// raise is known and lower. <paramref name="raiseOf"/> answers a neighbour's raise (null where the map has no cell there).
	/// </summary>
	// [BIN:TPW-EXE:0x0053323C wall loops] the four neighbour loops (0x7CDBA0, 0x7CDBC8, 0x7CDBA8, 0x7CDBD0: codes 1, 4, 0x10, 0x40) skip a missing neighbour and a neighbour that is not lower (both raises truncated), and queue a wall face from the neighbour's raise in steps of 10 (0x53C8D0, y = step + 1.5)
	internal static IEnumerable<(int Side, long Step, int Ax, int Ay, int Bx, int By)> WallSteps( int x, int y, float current, Func<int, int, float?> raiseOf )
	{
		foreach ( var (dx, dy, ax, ay, bx, by, side) in Edges )
		{
			if ( raiseOf( x + dx, y + dy ) is not { } neighbour )
				continue;
			for ( var step = (long)neighbour; step < (long)current; step += (int)FaceSize )
				yield return (side, step, ax, ay, bx, by);
		}
	}

	/// <summary>The raises of the tile markers' tiles and of the four tiles beside them as <see cref="TileLineGhost.TileRaise"/> gives them; empty without a tile raise (a world that types no cells).</summary>
	internal static Dictionary<(int X, int Y), float?> CaptureTileRaises( ModelHeightfield field, IReadOnlyList<(int X, int Y, BlueprintMarker Marker)> cells, Func<int, int, float?>? sample )
	{
		var result = new Dictionary<(int X, int Y), float?>();
		if ( sample == null )
			return result;
		foreach ( var (x, y, marker) in cells )
		{
			if ( !IsTileMarker( marker ) )
				continue;
			foreach ( var (dx, dy) in new[] { (0, 0), (0, -2), (2, 0), (0, 2), (-2, 0) } )
			{
				var (tx, ty) = (x + dx, y + dy);
				if ( !result.ContainsKey( (tx, ty) ) )
					result[(tx, ty)] = tx >= 0 && ty >= 0 && tx < field.CellCountX && ty < field.CellCountZ ? sample( tx, ty ) : null;
			}
		}
		return result;
	}

	// Snapshot the map (not just preview neighbours), so occupancy changes rebuild topology even while paused.
	internal static Dictionary<(int X, int Y), float?> CaptureHeights( ModelHeightfield field,
		IReadOnlyList<(int X, int Y, BlueprintMarker Marker)> cells, Func<int, int, float?>? sample )
	{
		var result = new Dictionary<(int X, int Y), float?>();
		if ( sample == null ) return result;
		foreach ( var (x, y, marker) in cells )
		{
			Capture( x, y );
			foreach ( var (dx, dy, _, _, _, _, _) in Edges ) Capture( x + dx, y + dy );
			if ( !IsTileMarker( marker ) )
				continue;
			// a tile's raise reads its four cells and each wall the four cells of the tile two cells away (0x536CA0)
			foreach ( var (tx, ty) in new[] { (0, 0), (0, -2), (2, 0), (0, 2), (-2, 0) } )
				foreach ( var (cx, cy) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) } )
					Capture( x + tx + cx, y + ty + cy );
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
		var square = new[] { new Vector2( 0, 0 ), new Vector2( 1, 0 ), new Vector2( 0, 1 ), new Vector2( 1, 1 ) };
		AddFace( vertices, indices, field, slot, CornerOrder( direction ).Select( entry => square[entry] ).ToArray(), a, b, c, d );
	}

	private static void AddFace( List<Vertex> vertices, List<uint> indices, ModelHeightfield field, int slot, Vector2[] uv,
		System.Numerics.Vector3 a, System.Numerics.Vector3 b, System.Numerics.Vector3 c, System.Numerics.Vector3 d )
	{
		var positions = new[] { a, b, c, d }.Select( corner => OriginalParkPlacement.ToEngine( field, corner ) ).ToArray();
		var cross = System.Numerics.Vector3.Cross( positions[1] - positions[0], positions[2] - positions[0] );
		var normal = cross.LengthSquared() > 0 ? System.Numerics.Vector3.Normalize( cross ) : System.Numerics.Vector3.UnitY;
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

/// <summary>
/// The state of the original's per-frame flush of the blueprint's dynamic faces (tp.exe <c>0x53C3F0</c>) that is not the ripple: the blink counter and flag of the red faces and the glide point that the
/// faces of the placement and the edit tools lag behind the cursor by.
/// </summary>
internal sealed class BlueprintFlush
{
	private int counter;
	private float glideX;
	private float glideZ;

	/// <summary>The blink flag <c>0x763C98</c>: red faces are queued only while it is set.</summary>
	public bool RedShown { get; private set; }

	/// <summary>One flush: the counter moves by the clock's time scale (1 at normal speed) and the glide point half the way to 10 x the cursor cell.</summary>
	public void Tick( (int X, int Y) cursor, int timeScale = 1 )
	{
		counter += timeScale;
		if ( !RedShown )
		{
			if ( counter > 1 )
			{
				RedShown = true;
				counter = 0;
			}
		}
		else if ( counter > 6 )
		{
			RedShown = false;
			counter = 0;
		}
		glideX += (cursor.X * BlueprintMarkers.FaceSize - glideX) * 0.5f;
		glideZ += (cursor.Y * BlueprintMarkers.FaceSize - glideZ) * 0.5f;
	}

	/// <summary>The offset a glided face takes: the glide point less 10 x the cursor cell.</summary>
	public Vector2 Offset( (int X, int Y) cursor ) => new( glideX - cursor.X * BlueprintMarkers.FaceSize, glideZ - cursor.Y * BlueprintMarkers.FaceSize );
}
