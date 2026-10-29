namespace OpenTPW;

/// <summary>One quad of the harbour's dynamic meshes, in original units (x and z on the ground, y up), with its four corners' normals and texture coordinates.</summary>
internal readonly record struct HarbourFace( int Slot,
	System.Numerics.Vector3 A, System.Numerics.Vector3 B, System.Numerics.Vector3 C, System.Numerics.Vector3 D,
	System.Numerics.Vector3 NormalA, System.Numerics.Vector3 NormalB, System.Numerics.Vector3 NormalC, System.Numerics.Vector3 NormalD,
	Vector2 UvA, Vector2 UvB, Vector2 UvC, Vector2 UvD );

/// <summary>
/// The harbour's waves and flags (docs/COMPATIBILITY.md, "Harbour waves and flags"): the original builds two dynamic meshes every frame besides the blueprint markers. The
/// WaterMesh is a grid of rolling waves from <c>WaterStartPos</c> out to sea, each square in one of three sea textures; the FlagsMesh is eight cloth flags over the entrance's flag
/// poles, waving and turned to the wind. Both move with the game and stand still while it is paused.
/// </summary>
// [BIN:TPW-EXE:0x0053C3F0 dynamic meshes flush] the render callback 0x54E2B0 calls it every park frame (0x54E2FC): the wave grid 0x53D380(16, 19, 20, phase 0x871F60), the sea's two patches 0x53CAB0, the cloth grid 0x53D570(7, 5, 1.8, phase 0x871ED0) and the eight flags 0x53CDA0, then the meshes are drawn (0x53D9E0)
public sealed class HarbourDynamics : ModelEntity
{
	/// <summary>The wave grid's columns (x) and rows (z) and its spacing.</summary>
	internal const int WaveColumns = 16, WaveRows = 19;
	internal const float WaveStep = 20f;
	/// <summary>The cloth grid's columns (along the flag) and rows (down from the pole's top) and its spacing.</summary>
	internal const int ClothColumns = 7, ClothRows = 5;
	internal const float ClothStep = 1.8f;
	/// <summary>The faces each flag is made of: 6 x 4 cells, each a front and a back face.</summary>
	internal const int FlagCellColumns = 6, FlagCellRows = 4;

	/// <summary>The sea's two patches: (first column, first row, columns, rows) of the wave grid.</summary>
	// [BIN:TPW-EXE:0x0053C64A sea patches] 0x53CAB0(WaterStartPos, 10, 11, 0, 0, 16, 19) and (…, 4, 6, 10, 0, 16, 19): 110 + 24 = 134 faces, the WaterMesh's 0x86
	internal static readonly (int Column, int Row, int Columns, int Rows)[] SeaPatches = [(0, 0, 10, 11), (10, 0, 4, 6)];

	/// <summary>The sea textures the per-face table picks from: the original's slots 15 to 17.</summary>
	internal const int SeaTextureCount = 3;
	/// <summary>The entries of the per-face sea texture table (one per face of the larger patch; the smaller patch reads its first 24).</summary>
	// [BIN:TPW-EXE:0x0053C03A sea texture table] the mesh creation 0x53BFE0 fills 0x871D18..0x871ED0 (110 words) with rand() % 3 + 15
	internal const int SeaTextureTableSize = 110;
	/// <summary>The flag texture's material slot (after the three sea textures).</summary>
	internal const int FlagSlot = SeaTextureCount;

	/// <summary>The eight flags' pole tops (original units) in drawing order; the textures alternate between the two halves of <c>gte_lgo1</c>, starting with the upper.</summary>
	// [BIN:TPW-EXE:0x00763CF4 flag table] 16-byte records (x, y, z, unused): x 411.25, 451.25, 511.25, 551.25, each at z 38.75 and 50.75, y 22
	internal static readonly System.Numerics.Vector3[] FlagPoles =
	[
		new( 411.25f, 22f, 38.75f ), new( 411.25f, 22f, 50.75f ), new( 451.25f, 22f, 38.75f ), new( 451.25f, 22f, 50.75f ),
		new( 511.25f, 22f, 38.75f ), new( 511.25f, 22f, 50.75f ), new( 551.25f, 22f, 38.75f ), new( 551.25f, 22f, 50.75f ),
	];

	private static readonly string[] TextureNames = ["gby_sur1", "gby_sur2", "gby_sur3", "gte_lgo1"];
	private const int TextureSlots = 16;

	private readonly ModelHeightfield field;
	private readonly System.Numerics.Vector3 waterStart;
	private readonly Material material;
	private readonly int[] seaTextures;
	private readonly HarbourPhases phases = new();
	private readonly HarbourBuffers buffers = new();
	private Vertex[] vertices = [];

	/// <summary>The wind's direction in degrees the flags turn to (the weather's <see cref="ParkWeather.WindDirection"/>).</summary>
	public float WindDirection { get; set; } = ParkWeather.InitialWindDirection;

	/// <summary>The sea texture of each face (0 to 2, the original's slots 15 to 17).</summary>
	internal IReadOnlyList<int> SeaTextures => seaTextures;

	/// <param name="field">The park's heightfield, for the engine placement.</param>
	/// <param name="waterStart">The level's <c>WaterStartPos</c> (original units).</param>
	/// <param name="level">The level whose <c>Dynamic/Textures</c> the textures are looked up in first.</param>
	public HarbourDynamics( ModelHeightfield field, System.Numerics.Vector3 waterStart, string level )
	{
		this.field = field;
		this.waterStart = waterStart;
		Name = "harbour waves and flags";
		seaTextures = SeaTextureTable();
		material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
		// [BIN:TPW-EXE:0x0053D790 dynamic mesh textures] a dynamic mesh looks a texture up in Data\Levels\<level>\Dynamic\Textures first, then in Data\Generic\Dynamic\Textures (table 0x763B38: slots 15-17 gby_sur1..3, 18 and 19 gte_lgo1)
		var textures = TextureNames.Select( name => LoadTexture( level, name ) ).ToList();
		// test.shader binds a fixed number of texture slots; the unused ones repeat the first
		while ( textures.Count < TextureSlots )
			textures.Add( textures[0] );
		material.Set( "Color", textures.ToArray(), SamplerType.Anisotropic );
		Rebuild();
	}

	/// <summary>The level's <c>WaterStartPos</c> (<c>levels/Standard.sam</c> overridden by the level's own), or null when neither layer has all three values.</summary>
	// [BIN:TPW-EXE:0x00408142 water start] the level setup copies WaterStartPos.X/Y/Z (0x785718..0x785720) to 0x870EC8..0x870ED0, which the sea patches start at
	public static System.Numerics.Vector3? LoadWaterStart( string levelName )
	{
		var values = new Dictionary<string, string[]>( StringComparer.OrdinalIgnoreCase );
		foreach ( var path in new[] { "/levels/Standard.sam", $"/levels/{levelName}/Standard.sam" } )
		{
			if ( FileSystem.FileExists( path ) )
				GuestSettings.ParseSam( FileSystem.ReadAllText( path ), values );
		}
		float? Read( string key ) => values.TryGetValue( key, out var tokens ) && tokens.Length > 0
			&& float.TryParse( tokens[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value ) ? value : null;
		return Read( "WaterStartPos.X" ) is { } x && Read( "WaterStartPos.Y" ) is { } y && Read( "WaterStartPos.Z" ) is { } z ? new System.Numerics.Vector3( x, y, z ) : null;
	}

	/// <summary>The per-face sea textures: <c>rand() % 3</c>, 110 times, from the game's C runtime stream (<see cref="CrtProcessRandom"/>), so every run draws its own sea.</summary>
	// [BIN:TPW-EXE:0x0053C03A sea texture table] the mesh creation fills the table (the write at 0x53C05F) from the game thread's rand() 0x67B5C0, which start-up seeded with
	// GetTickCount (0x54DDE3); the original's
	// other rand() callers between the seed and the park's start (its advisor sequences, path tile rolls, lobby clips) draw from that one stream, and OpenTPW's stand-ins for them keep
	// streams of their own, so only the generator, the seed and the table's own order are matched, not how many draws precede the table
	internal static int[] SeaTextureTable() => SeaTextureTable( CrtProcessRandom.Next );

	/// <summary>The table drawn from <paramref name="rand"/> (a C runtime <c>rand()</c>), in the original's order.</summary>
	internal static int[] SeaTextureTable( Func<int> rand )
	{
		var table = new int[SeaTextureTableSize];
		for ( var index = 0; index < table.Length; index++ )
			table[index] = rand() % SeaTextureCount;
		return table;
	}

	/// <summary>
	/// Advances the two phases (<see cref="HarbourPhases"/>) and rebuilds the faces.
	/// </summary>
	public void Update( float deltaSeconds, bool paused, int timeScale = 1 )
	{
		phases.Advance( deltaSeconds, paused, timeScale );
		Rebuild();
	}

	/// <summary>
	/// The wave grid of <paramref name="phase"/>: <c>WaveColumns</c> x <c>WaveRows</c> points 20 apart, each lifted by a travelling cosine 1.7 high, with its normal.
	/// </summary>
	// [BIN:TPW-EXE:0x0053D380 wave grid] k = 2 pi x 1/3; with s = table sin(10 deg), c = table cos(10 deg): angle = x k c - z k s - phase x 0.5; y = table cos(angle) x 1.7; the normal (sin, 1, sin) of the angle normalised; x steps across a row, z down the rows
	internal static (System.Numerics.Vector3[,] Positions, System.Numerics.Vector3[,] Normals) WaveGrid( float phase )
	{
		var grid = (new System.Numerics.Vector3[WaveColumns, WaveRows], new System.Numerics.Vector3[WaveColumns, WaveRows]);
		WaveGrid( phase, grid.Item1, grid.Item2 );
		return grid;
	}

	/// <summary><see cref="WaveGrid(float)"/> into the given arrays.</summary>
	internal static void WaveGrid( float phase, System.Numerics.Vector3[,] positions, System.Numerics.Vector3[,] normals )
	{
		var k = 6.2831850f * 0.33333334f;
		var tilt = 0.017453300f * 10f;
		var along = OriginalSineTable.Cos( tilt ) * k;
		var across = -(k * OriginalSineTable.Sin( tilt ));
		var z = 0f;
		for ( var row = 0; row < WaveRows; row++ )
		{
			var rowTerm = z * across;
			var shift = phase * 0.5f;
			var x = 0f;
			for ( var column = 0; column < WaveColumns; column++ )
			{
				var angle = x * along + rowTerm - shift;
				positions[column, row] = new System.Numerics.Vector3( x, OriginalSineTable.Cos( angle ) * 1.7f, z );
				var slope = OriginalSineTable.Sin( angle );
				normals[column, row] = System.Numerics.Vector3.Normalize( new System.Numerics.Vector3( slope, 1f, slope ) );
				x += WaveStep;
			}
			z += WaveStep;
		}
	}

	/// <summary>
	/// The cloth grid of <paramref name="phase"/>: <c>ClothColumns</c> x <c>ClothRows</c> points 1.8 apart hanging down from the pole top, rippling out from the pole and sagging toward the free end.
	/// </summary>
	// [BIN:TPW-EXE:0x0053D570 cloth grid] for row r (y0 = -r x 1.8, h = y0 - 5 x 1.8) and column c (x = c x 1.8): d = sqrt(x^2 + h^2) - phase x 0.6, a = min(x / ((7 - 4) x 1.8), 1);
	// position (x, 2a x table cos(2 pi x 0.25 x d) x 0.3 + y0 - 0.02 x^2, table cos(k d) x a x 0.3) with k = 2 pi x 1/3; normal (table sin(k d) x a, the same, 1) normalised
	internal static (System.Numerics.Vector3[,] Positions, System.Numerics.Vector3[,] Normals) ClothGrid( float phase )
	{
		var grid = (new System.Numerics.Vector3[ClothColumns, ClothRows], new System.Numerics.Vector3[ClothColumns, ClothRows]);
		ClothGrid( phase, grid.Item1, grid.Item2 );
		return grid;
	}

	/// <summary><see cref="ClothGrid(float)"/> into the given arrays.</summary>
	internal static void ClothGrid( float phase, System.Numerics.Vector3[,] positions, System.Numerics.Vector3[,] normals )
	{
		var k = 6.2831850f * 0.33333334f;
		var height = ClothRows * ClothStep;
		var width = (ClothColumns - 4) * ClothStep;
		var top = 0f;
		for ( var row = 0; row < ClothRows; row++ )
		{
			var below = top - height;
			var belowSquared = below * below;
			var shift = phase * 0.6f;
			var x = 0f;
			for ( var column = 0; column < ClothColumns; column++ )
			{
				var distance = MathF.Sqrt( x * x + belowSquared ) - shift;
				var wave = k * distance;
				var sag = 6.2831850f * distance * 0.25f;
				var reach = x / width;
				if ( !(reach < 1f) )
					reach = 1f;
				var outward = OriginalSineTable.Cos( wave ) * reach * 0.3f;
				var y = (reach + reach) * OriginalSineTable.Cos( sag ) * 0.3f + top - x * x * 0.02f;
				positions[column, row] = new System.Numerics.Vector3( x, y, outward );
				var slope = OriginalSineTable.Sin( wave ) * reach;
				normals[column, row] = System.Numerics.Vector3.Normalize( new System.Numerics.Vector3( slope, slope, 1f ) );
				x += ClothStep;
			}
			top -= ClothStep;
		}
	}

	/// <summary>The sea's faces from <paramref name="start"/> over the wave grid: the two patches, each square in its table texture.</summary>
	// [BIN:TPW-EXE:0x0053CAB0 sea patch] a face per cell (column, row) of the patch: corners (c, r), (c + 1, r), (c, r + 1), (c + 1, r + 1) of the grid plus the start, their grid normals, texture
	// coordinates (0, 0), (1, 0), (0, 1), (1, 1) and the texture of the table entry the patch's own face counter names (it starts at 0 for each patch), into the WaterMesh (0x53C9C0 with mesh 1)
	internal static IEnumerable<HarbourFace> SeaFaces( System.Numerics.Vector3 start, System.Numerics.Vector3[,] positions, System.Numerics.Vector3[,] normals, IReadOnlyList<int> textures )
	{
		foreach ( var (firstColumn, firstRow, columns, rows) in SeaPatches )
		{
			var face = 0;
			for ( var row = firstRow; row < firstRow + rows; row++ )
			{
				for ( var column = firstColumn; column < firstColumn + columns; column++ )
				{
					yield return new HarbourFace( textures[face++],
						positions[column, row] + start, positions[column + 1, row] + start, positions[column, row + 1] + start, positions[column + 1, row + 1] + start,
						normals[column, row], normals[column + 1, row], normals[column, row + 1], normals[column + 1, row + 1],
						new( 0, 0 ), new( 1, 0 ), new( 0, 1 ), new( 1, 1 ) );
				}
			}
		}
	}

	/// <summary>
	/// One flag's faces over the cloth grid: 6 x 4 cells, each a front face (normals reversed) and a back face, turned about the pole by <paramref name="angle"/> radians and set at the pole top.
	/// The upper flag texture (<paramref name="upper"/>) maps the cells from the texture's top (v 1) down 0.55, the lower one from v 0.55.
	/// </summary>
	// [BIN:TPW-EXE:0x0053CDA0 flag] with s = table sin(angle) and c = table cos(angle), every corner (x + 0.5, y, z) turns to (c x - s z, y, s x + c z) and moves to the pole top; its normal turns alike.
	// Per cell (column, row): the front face (corners (c+1, r), (c, r), (c+1, r+1), (c, r+1), the grid normals negated, u from (c + 1) / 6 to c / 6) and the back face (the corners (c, r), (c+1, r), (c, r+1),
	// (c+1, r+1) and their normals, u from c / 6 to (c + 1) / 6); v falls by 0.55 / 4 a row from 1 (texture 18) or 0.55 (texture 19); both into the FlagsMesh (0x53C9C0 with mesh 3)
	internal static IEnumerable<HarbourFace> FlagFaces( System.Numerics.Vector3 pole, System.Numerics.Vector3[,] positions, System.Numerics.Vector3[,] normals, bool upper, float angle )
	{
		var sin = OriginalSineTable.Sin( angle );
		var cos = OriginalSineTable.Cos( angle );
		var rowHeight = 0.55f / FlagCellRows;
		var v = upper ? 1f : 0.55f;
		System.Numerics.Vector3 Place( System.Numerics.Vector3 corner )
		{
			var x = corner.X + 0.5f;
			return new System.Numerics.Vector3( cos * x - sin * corner.Z, corner.Y, sin * x + cos * corner.Z ) + pole;
		}
		System.Numerics.Vector3 Turn( System.Numerics.Vector3 normal ) => new( normal.X * cos - sin * normal.Z, normal.Y, normal.X * sin + normal.Z * cos );
		for ( var row = 0; row < FlagCellRows; row++ )
		{
			var u = 0f;
			for ( var column = 0; column < FlagCellColumns; column++ )
			{
				var next = u + 1f / FlagCellColumns;
				var lower = v - rowHeight;
				var (a, b, c, d) = (positions[column, row], positions[column + 1, row], positions[column, row + 1], positions[column + 1, row + 1]);
				var (na, nb, nc, nd) = (normals[column, row], normals[column + 1, row], normals[column, row + 1], normals[column + 1, row + 1]);
				yield return new HarbourFace( FlagSlot, Place( b ), Place( a ), Place( d ), Place( c ), Turn( -nb ), Turn( -na ), Turn( -nd ), Turn( -nc ),
					new( next, v ), new( u, v ), new( next, lower ), new( u, lower ) );
				yield return new HarbourFace( FlagSlot, Place( a ), Place( b ), Place( c ), Place( d ), Turn( na ), Turn( nb ), Turn( nc ), Turn( nd ),
					new( u, v ), new( next, v ), new( u, lower ), new( next, lower ) );
				u = next;
			}
			v -= rowHeight;
		}
	}

	/// <summary>Every face of a frame: the sea, then the eight flags turned to <paramref name="windDirection"/> (degrees).</summary>
	// [BIN:TPW-EXE:0x0053C6B1 flags] the flags alternate the textures 18 and 19 from the first, and turn by the weather thing's +0x5C (0x519590 over world +0x1DA724) x 0.0174533
	internal static List<HarbourFace> Faces( System.Numerics.Vector3 start, IReadOnlyList<int> seaTextures, float wavePhase, float flagPhase, float windDirection )
	{
		var buffers = new HarbourBuffers();
		Faces( start, seaTextures, wavePhase, flagPhase, windDirection, buffers );
		return buffers.Faces;
	}

	/// <summary><see cref="Faces(System.Numerics.Vector3, IReadOnlyList{int}, float, float, float)"/> into reused grids and list (a frame's rebuild allocates none of them).</summary>
	internal static void Faces( System.Numerics.Vector3 start, IReadOnlyList<int> seaTextures, float wavePhase, float flagPhase, float windDirection, HarbourBuffers buffers )
	{
		var faces = buffers.Faces;
		faces.Clear();
		WaveGrid( wavePhase, buffers.Waves, buffers.WaveNormals );
		foreach ( var face in SeaFaces( start, buffers.Waves, buffers.WaveNormals, seaTextures ) )
			faces.Add( face );
		ClothGrid( flagPhase, buffers.Cloth, buffers.ClothNormals );
		var angle = windDirection * 0.017453300f;
		for ( var flag = 0; flag < FlagPoles.Length; flag++ )
			foreach ( var face in FlagFaces( FlagPoles[flag], buffers.Cloth, buffers.ClothNormals, flag % 2 == 0, angle ) )
				faces.Add( face );
	}

	/// <summary>
	/// The faces' vertices for the draw: four per face in engine coordinates, the second and third swapped where needed so the face's front side is the side its normals point to (the
	/// original's two triangles (2, 1, 0) and (1, 2, 3) of a face keep their diagonal). The engine placement swaps the y and z axes, a mirror, so a triangle whose corners turn about its
	/// normal in original units turns the other way in engine coordinates.
	/// </summary>
	// [BIN:TPW-EXE:0x0053E530 dynamic face] a face is written as the triangles (v2, v1, v0) and (v1, v2, v3) with each corner's normal (0x53DDD0 with no terrain and no ripple for these meshes)
	internal static Vertex[] Vertices( ModelHeightfield field, IReadOnlyList<HarbourFace> faces )
	{
		var result = new Vertex[faces.Count * 4];
		WriteVertices( field, faces, result );
		return result;
	}

	/// <summary><see cref="Vertices"/> into <paramref name="result"/> (four entries per face), so a frame's rebuild reuses one array.</summary>
	internal static void WriteVertices( ModelHeightfield field, IReadOnlyList<HarbourFace> faces, Vertex[] result )
	{
		static System.Numerics.Vector3 Direction( System.Numerics.Vector3 normal ) => new( normal.X, normal.Z, normal.Y );
		for ( var index = 0; index < faces.Count; index++ )
		{
			var face = faces[index];
			var a = (Position: OriginalParkPlacement.ToEngine( field, face.A ), Normal: Direction( face.NormalA ), Uv: face.UvA);
			var b = (Position: OriginalParkPlacement.ToEngine( field, face.B ), Normal: Direction( face.NormalB ), Uv: face.UvB);
			var c = (Position: OriginalParkPlacement.ToEngine( field, face.C ), Normal: Direction( face.NormalC ), Uv: face.UvC);
			var d = (Position: OriginalParkPlacement.ToEngine( field, face.D ), Normal: Direction( face.NormalD ), Uv: face.UvD);
			var facing = System.Numerics.Vector3.Cross( b.Position - c.Position, a.Position - c.Position );
			if ( System.Numerics.Vector3.Dot( facing, a.Normal + b.Normal + c.Normal + d.Normal ) > 0 )
				(b, c) = (c, b);
			var start = index * 4;
			result[start] = new Vertex { Position = a.Position, Normal = a.Normal, TexCoords = a.Uv, TexIndex = face.Slot };
			result[start + 1] = new Vertex { Position = b.Position, Normal = b.Normal, TexCoords = b.Uv, TexIndex = face.Slot };
			result[start + 2] = new Vertex { Position = c.Position, Normal = c.Normal, TexCoords = c.Uv, TexIndex = face.Slot };
			result[start + 3] = new Vertex { Position = d.Position, Normal = d.Normal, TexCoords = d.Uv, TexIndex = face.Slot };
		}
	}

	/// <summary>The fixed index pattern: per face the triangles (2, 1, 0) and (1, 2, 3).</summary>
	internal static uint[] Indices( int faceCount )
	{
		var indices = new uint[faceCount * 6];
		for ( var face = 0; face < faceCount; face++ )
		{
			var start = (uint)(face * 4);
			var offset = face * 6;
			(indices[offset], indices[offset + 1], indices[offset + 2]) = (start + 2, start + 1, start);
			(indices[offset + 3], indices[offset + 4], indices[offset + 5]) = (start + 1, start + 2, start + 3);
		}
		return indices;
	}

	private void Rebuild()
	{
		// the faces change only with the phases and the wind: a paused park keeps its vertices
		var state = (phases.Waves, phases.Flags, WindDirection);
		if ( Model != null && state == built )
			return;
		built = state;
		Faces( waterStart, seaTextures, phases.Waves, phases.Flags, WindDirection, buffers );
		var faces = buffers.Faces;
		if ( vertices.Length != faces.Count * 4 )
			vertices = new Vertex[faces.Count * 4];
		WriteVertices( field, faces, vertices );
		if ( Model is { } model && model.HasDynamicVertices )
		{
			model.UpdateVertices( vertices );
			return;
		}
		var old = Model;
		Model = new Model( vertices, Indices( faces.Count ), material, dynamicVertices: true );
		old?.ReleaseBuffers();
	}

	private (float Waves, float Flags, float Wind) built;

	private static Texture LoadTexture( string level, string name )
	{
		foreach ( var directory in new[] { $"/levels/{level}/dynamic/textures", "/generic/dynamic/textures" } )
		{
			var path = $"{directory}/{name}.wct";
			if ( FileSystem.FileExists( path ) )
				return new Texture( path, TextureFlags.Repeat );
		}
		Log.Warning( $"Harbour: {name}.wct is missing." );
		return Texture.Missing;
	}

	/// <summary>Leaving the park releases the model and the material; the textures are shared game textures and stay.</summary>
	protected override void OnDelete()
	{
		Model?.ReleaseBuffers();
		Model = null;
		material.ReleaseOwnedResources();
	}
}

/// <summary>The harbour's two phases: the waves' (the original's <c>0x871F60</c>) and the flags' (<c>0x871ED0</c>), both 0 when the park starts.</summary>
internal sealed class HarbourPhases
{
	/// <summary>The phases' step per flush at normal speed.</summary>
	// [BIN:TPW-EXE:0x0053C776 harbour phases] unless the clock is paused (0x402DF0) both 0x871F60 (waves) and 0x871ED0 (flags) gain 0.4 x the clock's time scale (0x785970 +0x20, 0x403390) per flush
	public const float Step = 0.4f;
	private const float AssumedFrameRate = 30f;
	private float remainder;

	public float Waves { get; private set; }
	public float Flags { get; private set; }

	/// <summary>One flush per 1/30 s of <paramref name="deltaSeconds"/> (at most a quarter second at once), each adding <see cref="Step"/> times the game speed, unless the game is paused.</summary>
	// [EXT:blueprint-phase-step] only the frame-rate part: the flush 0x53C3F0 steps once per rendered frame (frame-rate dependent); OpenTPW steps once per 1/30 s of real time, by the game speed as the original's by the clock scale
	public void Advance( float deltaSeconds, bool paused, int timeScale )
	{
		if ( paused || !(deltaSeconds > 0) )
			return;
		var ticks = remainder + Math.Min( deltaSeconds, 0.25f ) * AssumedFrameRate;
		var whole = (int)ticks;
		remainder = ticks - whole;
		for ( var tick = 0; tick < whole; tick++ )
		{
			Waves += Step * timeScale;
			Flags += Step * timeScale;
		}
	}
}

/// <summary>The grids and the face list one frame's rebuild fills (<see cref="HarbourDynamics.Faces(System.Numerics.Vector3, IReadOnlyList{int}, float, float, float, HarbourBuffers)"/>).</summary>
internal sealed class HarbourBuffers
{
	public readonly System.Numerics.Vector3[,] Waves = new System.Numerics.Vector3[HarbourDynamics.WaveColumns, HarbourDynamics.WaveRows];
	public readonly System.Numerics.Vector3[,] WaveNormals = new System.Numerics.Vector3[HarbourDynamics.WaveColumns, HarbourDynamics.WaveRows];
	public readonly System.Numerics.Vector3[,] Cloth = new System.Numerics.Vector3[HarbourDynamics.ClothColumns, HarbourDynamics.ClothRows];
	public readonly System.Numerics.Vector3[,] ClothNormals = new System.Numerics.Vector3[HarbourDynamics.ClothColumns, HarbourDynamics.ClothRows];
	public readonly List<HarbourFace> Faces = new( 134 + 8 * HarbourDynamics.FlagCellColumns * HarbourDynamics.FlagCellRows * 2 );
}
