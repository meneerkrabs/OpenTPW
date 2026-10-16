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
}

/// <summary>
/// The original's BlueprintMesh: build-tool ghosts drawn as blue (or red) boxes over the cells of a path or queue
/// line and over an object's footprint while it is being placed. Each cell gets a flat face 10 units square at its
/// marker height plus 1.5, and vertical faces, 10 high, from each lower neighbour's marker height up to its own
/// (docs/reverse/BLUEPRINT.md). The geometry is rebuilt only when the set of marked cells changes.
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

	private const int TextureSlots = 16;
	private static readonly string[] TextureNames = ["blue", "red"];

	private readonly ModelHeightfield field;
	private readonly Material material;
	private (int X, int Y, BlueprintMarker Marker)[] shown = [];

	public BlueprintMarkers( ModelHeightfield field )
	{
		this.field = field;
		Name = "blueprint markers";
		Hidden = true;
		material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
		var textures = TextureNames.Select( name => LoadTexture( $"/generic/dynamic/textures/{name}.wct" ) ).ToList();
		textures.Add( AreaTexture() );
		while ( textures.Count < TextureSlots )
			textures.Add( textures[0] );
		material.Set( "Color", textures.ToArray(), SamplerType.Anisotropic );
	}

	/// <summary>
	/// The placement area's texture: translucent yellow and black squares, two by two to a cell.
	/// </summary>
	// [BIN:TPW-EXE:0x5243a6 area face] the cells of the coaster tool's placement area get face type 0x11f; the video (73:20-75:58) shows them as a translucent yellow with dark mottling, and the dynamic texture table holds no such texture (orange.tga is not on the PC CD)
	// [APPROX:COAST-028] the texture is generated: 4 x 4 squares of yellow (255, 214, 0) and near-black (40, 34, 0), both at alpha 205 — evidence needed: the original texture behind face type 0x11f
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

	/// <summary>Shows exactly these cells (later duplicates of a cell win); an empty list hides the markers.</summary>
	public void Show( IReadOnlyList<(int X, int Y, BlueprintMarker Marker)> cells )
	{
		var unique = cells
			.Where( cell => cell.X >= 0 && cell.Y >= 0 && cell.X < field.CellCountX && cell.Y < field.CellCountZ )
			.GroupBy( cell => (cell.X, cell.Y) ).Select( group => group.Last() ).ToArray();
		if ( unique.SequenceEqual( shown ) )
			return;
		shown = unique;
		var old = Model;
		Model = null;
		Hidden = unique.Length == 0;
		if ( unique.Length > 0 )
		{
			var (vertices, indices) = BuildGeometry( field, unique );
			Model = new Model( vertices, indices, material );
		}
		old?.ReleaseBuffers();
	}

	public void Clear() => Show( [] );

	/// <summary>Leaving the park releases the marker model and the markers' own material (the textures are shared game textures).</summary>
	protected override void OnDelete()
	{
		Model?.ReleaseBuffers();
		Model = null;
		shown = [];
		material.ReleaseOwnedResources();
	}

	/// <summary>
	/// The marker height of a cell: the original passes 0 for plain ground and the terrain height rounded up to a
	/// multiple of 10 next to rides and track; OpenTPW rounds the cell's corner height up for every cell.
	/// </summary>
	// [BIN:STP-PPC:0x10086794 SetCell ghost height] plain cells start at 0.0 (TOC −0x306c); cells of types 4, 7, 9, 10, 0xb, 0x10, 0x19 take 10 × ((int)h + 9) / 10 with h from 0x100509E8 (the terrain height at the cell corner x·10, y·10); 0x10086030 raises the height next to those types
	// [APPROX:BLUEPRINT-001] every cell takes its corner height rounded up to a multiple of 10 (the original's plain cells use 0, its flat park ground; ride and track neighbours, 0x10086030, are not reproduced) — evidence needed: original captures of ghosts on raised or sloped ground
	public static float MarkerHeight( ModelHeightfield field, int x, int y )
	{
		var height = (int)field.GetCornerHeight( x, y );
		return height <= 0 ? 0 : (height + 9) / 10 * 10;
	}

	internal static (Vertex[] Vertices, uint[] Indices) BuildGeometry( ModelHeightfield field, IReadOnlyList<(int X, int Y, BlueprintMarker Marker)> cells )
	{
		var vertices = new List<Vertex>();
		var indices = new List<uint>();
		foreach ( var (x, y, marker) in cells )
		{
			var height = MarkerHeight( field, x, y );
			var left = x * FaceSize;
			var front = y * FaceSize;
			AddFace( vertices, indices, field, (int)marker,
				new( left, height + Lift, front ), new( left + FaceSize, height + Lift, front ),
				new( left, height + Lift, front + FaceSize ), new( left + FaceSize, height + Lift, front + FaceSize ) );
			// Walls from each lower neighbour's marker height up to this cell's, one 10-unit face per step.
			// [BIN:STP-PPC:0x10086B9C SetCell ghost walls] fcmpu neighbour height with the cell's; a loop adds an edge face per 10 units (0x10086C2C) from the neighbour's height while below the cell's
			foreach ( var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) } )
			{
				var neighbour = x + dx >= 0 && y + dy >= 0 && x + dx < field.CellCountX && y + dy < field.CellCountZ
					? MarkerHeight( field, x + dx, y + dy ) : height;
				for ( var bottom = neighbour; bottom < height; bottom += FaceSize )
					AddWall( vertices, indices, field, (int)marker, x, y, dx, dy, bottom + Lift );
			}
		}
		return (vertices.ToArray(), indices.ToArray());
	}

	private static void AddWall( List<Vertex> vertices, List<uint> indices, ModelHeightfield field, int slot, int x, int y, int dx, int dy, float bottom )
	{
		var top = bottom + FaceSize;
		// The edge of cell (x, y) towards (x + dx, y + dy), as two points on the ground plane.
		var (ax, az, bx, bz) = (dx, dy) switch
		{
			(0, -1) => (x, y, x + 1, y),
			(1, 0) => (x + 1, y, x + 1, y + 1),
			(0, 1) => (x + 1, y + 1, x, y + 1),
			_ => (x, y + 1, x, y),
		};
		AddFace( vertices, indices, field, slot,
			new( ax * FaceSize, bottom, az * FaceSize ), new( bx * FaceSize, bottom, bz * FaceSize ),
			new( ax * FaceSize, top, az * FaceSize ), new( bx * FaceSize, top, bz * FaceSize ) );
	}

	/// <summary>One textured quad from four original-space corners (u: first→second, v: first→third), both sides facing out.</summary>
	// [BIN:TPW-EXE:0x0053C3F0 blueprint UV table] the runtime table 0x871EE8 (12-byte entries, u at +0, v at +4) holds the unit square (0,0), (1,0), (0,1), (1,1) in entries 0-3 and (0.5, 0.5) in entry 4; entries 5-8 are the rotating +-0.4 square of the track symbols
	// [BIN:TPW-EXE:0x0053DF30 blueprint face builder] textures 8-14 (m_link to m_erase) use the rotating set from entry 5, every other texture the unit square from entry 0 (0x53DFE6–0x53DFF2); the corner order follows the direction byte: default (0,1,2,3), 0x40 (1,3,0,2), 0x10 (3,2,1,0), 4 (2,0,3,1) (0x53DFF7–0x53E034), which shows only on a non-uniform texture; the blue and red markers are uniform, so each face shows its whole texture once, as here, and both sides are drawn (no hardware cull)
	private static void AddFace( List<Vertex> vertices, List<uint> indices, ModelHeightfield field, int slot,
		System.Numerics.Vector3 a, System.Numerics.Vector3 b, System.Numerics.Vector3 c, System.Numerics.Vector3 d )
	{
		var positions = new[] { a, b, c, d }.Select( corner => OriginalParkPlacement.ToEngine( field, corner ) ).ToArray();
		var normal = System.Numerics.Vector3.Normalize( System.Numerics.Vector3.Cross( positions[1] - positions[0], positions[2] - positions[0] ) );
		var uv = new[] { new Vector2( 0, 0 ), new Vector2( 1, 0 ), new Vector2( 0, 1 ), new Vector2( 1, 1 ) };
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
