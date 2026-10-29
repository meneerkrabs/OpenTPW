using System.Runtime.InteropServices;
using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// The park's sky (docs/UI.md, UI-107): the engine sky object's cloud layers over its backdrop cylinder, both centred on the camera, so the sky
/// never parallaxes and only turns with the view. Presentation only: it draws no random numbers of the world and its scroll is
/// the original's frame-time scroll, not the simulation.
/// </summary>
/// <remarks>
/// The layout is the engine sky's own, built in the original's axes (Y up) and converted to engine axes when a vertex is written.
/// <list type="bullet">
/// <item>The cloud grid is 16 x 16 vertices, 2400 wide, hanging from 300 above the camera and sagging by a fifth of the distance from its centre;
/// the number of layers (1, 2 or 4) is the SKYQUALITY option, each layer the same grid with its own texture step, scroll speed, mirroring and alpha.</item>
/// <item>The backdrop cylinder is 16 segments of 4 rings (radius 720 down to 0.84 of it, from 156 above the camera to 156 below) textured with
/// <c>sky_cyl.tga</c>, whose haze blends into the fog colour the clear leaves behind it.</item>
/// <item>The clouds take their colour from <c>sky_rgb.tga</c>, sampled at 16 x 16 points, and their shape from <c>sky.tga</c>.</item>
/// </list>
/// A level without <c>sky.tga</c> or <c>sky_cyl.tga</c> draws no sky at all, as the original's draw (0x5863C0) is gated on both textures.
/// The moon billboards of the hallow and space levels (<c>SkyObjects</c> of Standard.sam, drawn by 0x5860D0) follow the clouds' camera-centred placement; <see cref="TryMoonQuad"/> holds their geometry.
/// </remarks>
// [BIN:TPW-EXE:0x5852B0 sky load] loads <level dir>\sky\sky.tga (clouds), sky_rgb.tga (the colour grid, freed after sampling) and sky_cyl.tga (the cylinder); a missing sky.tga or sky_cyl.tga logs an error and leaves its pointer null
// [BIN:TPW-EXE:0x584EF0 sky constructor] width = height = 0x960 (2400), centre (0, 300, 0) (0x43960000), 4 layers, far distance 24000 (+0x21E8); layer colour 0xCFFFFFFF with 0x38000000 less per layer (+0x2F0C, +0x2F10)
// [BIN:TPW-EXE:0x585720 sky geometry] cloud grid x = column * width / 15 - width / 2, z likewise, y = 300 - 0.2 * sqrt(x^2 + z^2); the cylinder radius is 0.6 * width / 2 with rings at radius x1, 0.98, 0.96, 0.84 and heights top, top / 2, 0, -top (top = 300 - 0.2 * radius)
// [BIN:TPW-EXE:0x5863C0 sky draw] the view matrix with its translation zeroed (camera-centred), the clouds layer by layer (state 0x401D56: wrap, alpha blend, no depth write), then the cylinder (state 0x1D56)
// [BIN:TPW-EXE:0x585F10 sky scroll] per frame offset += velocity * (frame seconds * 25), wrapped into -1..1, zero for a frame of a second or more
public sealed class Sky : IDisposable
{
	/// <summary>Vertices per side of the cloud grid.</summary>
	internal const int GridSize = 16;
	/// <summary>Width and height of the cloud grid in original units (0x960).</summary>
	internal const float Extent = 2400f;
	/// <summary>The height of the cloud grid's centre above the camera, in original units (0x43960000).</summary>
	internal const float CentreHeight = 300f;
	/// <summary>How much the grid sags per unit of distance from its centre (0.2).</summary>
	internal const float Sag = 0.2f;
	/// <summary>Columns of the backdrop cylinder: two half circles of nine, whose shared edge is drawn twice.</summary>
	internal const int CylinderColumns = 18;
	/// <summary>Rings of the backdrop cylinder.</summary>
	internal const int CylinderRings = 4;
	/// <summary>The cloud layers the engine can draw.</summary>
	internal const int MaximumLayers = 4;

	private const float VelocityScale = 25f;

	private readonly Material<ObjectUniformBuffer> cloudMaterial;
	private readonly Material<ObjectUniformBuffer> cylinderMaterial;
	private readonly List<Moon> moons = new();
	private readonly uint[] colourGrid = new uint[GridSize * GridSize];
	private readonly float[] offsetU = new float[MaximumLayers];
	private readonly float[] offsetV = new float[MaximumLayers];
	/// <summary>The sky's vertices for the layer count last drawn (the layers' cloud triangles, then the cylinder's): positions and tints are fixed, so a frame only rewrites the clouds' texture coordinates.</summary>
	private readonly Staging staging;
	private static readonly uint VertexStride = Vertex.SizeInBytes;
	private readonly (int V0, int V1, int V2)[] cloudTriangles;
	private readonly (int V0, int V1, int V2)[] cylinderTriangles;
	private readonly SVector3[] cloudPositions = new SVector3[GridSize * GridSize];
	private readonly SVector3[] cylinderPositions = new SVector3[CylinderColumns * CylinderRings];
	private DeviceBuffer? vertexBuffer;
	private long lastFrame = -1;
	private bool disposed;

	/// <summary>The texture files of a level's sky, relative to its folder.</summary>
	internal const string CloudsFile = "sky/sky.tga", ColoursFile = "sky/sky_rgb.tga", CylinderFile = "sky/sky_cyl.tga";

	private Sky( Texture clouds, Texture cylinder, uint[] colours )
	{
		Array.Copy( colours, colourGrid, colourGrid.Length );
		for ( var xi = 0; xi < GridSize; xi++ )
			for ( var zi = 0; zi < GridSize; zi++ )
				cloudPositions[xi * GridSize + zi] = ToEngine( CloudPosition( xi, zi ) );
		for ( var column = 0; column < CylinderColumns; column++ )
			for ( var ring = 0; ring < CylinderRings; ring++ )
				cylinderPositions[column * CylinderRings + ring] = ToEngine( CylinderPosition( column, ring ) );
		cloudTriangles = CloudTriangles().ToArray();
		cylinderTriangles = CylinderTriangles().ToArray();
		staging = new Staging( cloudPositions, colourGrid, cloudTriangles, cylinderPositions, cylinderTriangles );

		const MaterialFlags flags = MaterialFlags.DisableDepth | MaterialFlags.DoubleSided;
		cloudMaterial = new Material<ObjectUniformBuffer>( "content/shaders/particle.shader", flags );
		cloudMaterial.Set( "Color", clouds );
		cylinderMaterial = new Material<ObjectUniformBuffer>( "content/shaders/particle.shader", flags );
		cylinderMaterial.Set( "Color", cylinder );
	}

	/// <summary>
	/// The sky of a level (its folder under <c>/levels</c>), or null when the level ships no sky (or the files cannot be read): the engine
	/// then draws nothing but its clear colour.
	/// </summary>
	public static Sky? TryCreate( string levelName )
	{
		if ( global::Global.Render == null )
			return null;
		var folder = $"/levels/{levelName}/";
		try
		{
			// [BIN:TPW-EXE:0x585000 sky gate] the draw (0x5863C0) and the scroll (0x585F10) need the cloud and the cylinder textures; the colour grid is optional
			if ( !FileSystem.FileExists( folder + CloudsFile ) || !FileSystem.FileExists( folder + CylinderFile ) )
				return null;
			var colours = FileSystem.FileExists( folder + ColoursFile ) ? ReadColourGrid( folder + ColoursFile ) : FallbackColourGrid();
			// the wrap flag turns on the wrapped sampler the scrolling needs (the original's 0x400000 state bit)
			var sky = new Sky( new Texture( folder + CloudsFile, TextureFlags.Wrap ), new Texture( folder + CylinderFile, TextureFlags.Wrap ), colours );
			sky.LoadMoons( levelName );
			return sky;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or ArgumentException or KeyNotFoundException )
		{
			Log.Warning( $"Sky of level {levelName} not drawn: {exception.Message}" );
			return null;
		}
	}

	// ---- the sky objects (the moons) ------------------------------------------------------------------------------------------------------

	/// <summary>One sky object of a level's Standard.sam (<c>SkyObjects[i]</c>): a texture quad at a direction of the sky, in front of the clouds or behind them.</summary>
	/// <param name="Position">X, Y (up) and Z in the original's axes.</param>
	/// <param name="Size">The quad's size: half of it is the clip-space offset to each side of the point.</param>
	/// <param name="Front">FrontFlag: drawn after the cylinder (mask 1) or before the clouds (mask 2).</param>
	/// <param name="File">The texture under the level's <c>sky</c> folder.</param>
	internal readonly record struct SkyObject( SVector3 Position, float Size, bool Front, string File );

	/// <summary>The most sky objects the engine keeps (0x5850C0 refuses the seventeenth).</summary>
	internal const int MaximumObjects = 16;

	/// <summary>
	/// The sky objects of a level: <c>NumSkyObjects.n</c> and <c>SkyObjects[i].X, Y, Z, Size, FrontFlag, Filename</c> from the level's Standard.sam, each key falling back to the shared one (<c>/levels/Standard.sam</c>:
	/// no objects). An object without a file name is not added.
	/// </summary>
	// [BIN:TPW-EXE:0x418c30 sky object setup] for every object of the settings (count 0x785724, records of six words from 0x785728) the park calls the sky's add 0x5850C0 with the file, X, Y, Z, Size and the mask 1 for a non-zero FrontFlag, 2 for a zero one; the add refuses a seventeenth object (0x5850C7) and an object whose file does not load (0x58517B)
	// [DATA:levels/<theme>/Standard.sam:SkyObjects] hallow: moon.tga (2, 11, 15) size 5 behind the sky; space: moon.tga (1, 12, 13) size 5.5 and moon2.tga (15, 13, 1) size 3.5 in front of it; jungle and fantasy have none
	internal static IReadOnlyList<SkyObject> ReadObjects( string levelName, Func<string, bool> exists, Func<string, string, string?> read )
	{
		string? Value( string key )
		{
			foreach ( var path in new[] { $"/levels/{levelName}/Standard.sam", "/levels/Standard.sam" } )
			{
				if ( exists( path ) && read( path, key ) is { } text )
					return text;
			}
			return null;
		}
		float Number( string key ) => float.TryParse( Value( key )?.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number ) ? number : 0f;
		var objects = new List<SkyObject>();
		if ( !int.TryParse( Value( "NumSkyObjects.n" )?.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var count ) )
			return objects;
		for ( var index = 0; index < count && objects.Count < MaximumObjects; index++ )
		{
			var prefix = $"SkyObjects[{index}].";
			var file = Value( prefix + "Filename" )?.Trim() ?? "";
			if ( file.Length == 0 )
				continue;
			objects.Add( new SkyObject( new SVector3( Number( prefix + "X" ), Number( prefix + "Y" ), Number( prefix + "Z" ) ), Number( prefix + "Size" ),
				(int)Number( prefix + "FrontFlag" ) != 0, file ) );
		}
		return objects;
	}

	/// <summary>The sine table of the sprite rotation (0x591430): 256 entries of sin(i * pi / 128), built at 0x5913F0.</summary>
	private static readonly float[] SineTable = Enumerable.Range( 0, 256 ).Select( index => MathF.Sin( index * MathF.PI * 0.0078125f ) ).ToArray();

	/// <summary>
	/// The camera roll in turns that the quad turns against (<c>[0x87AF60]</c>, 0x578700): the angle of the camera's up vector from the world's, from the world-up components of its right and up vectors
	/// (the camera matrix's elements +4 and +0x14, 0x87AFE8): the negated arc cosine of the up component over their length, signed by the arc sine of the negated right component.
	/// </summary>
	// [BIN:TPW-EXE:0x578700 camera roll] a = the camera matrix's [+4] (the right vector's world-up component), b = its [+0x14] (the up vector's): with n = 1 / sqrt(a^2 + b^2) (a = b = 1 when both vanish) the roll is acos(b n) / 2pi, negative when asin(-a n) is not above zero; 0x87AFF0 is the camera's world matrix (0x5783B0 inverts it into the view 0x87AF68)
	internal static float CameraRoll( float rightUp, float upUp )
	{
		var sum = rightUp * rightUp + upUp * upUp;
		float a = rightUp, b = upUp, inverse;
		if ( sum == 0f )
			(a, b, inverse) = (1f, 1f, 1f);
		else
			inverse = (float)(1.0 / Math.Sqrt( sum ));
		var sine = MathF.Asin( Math.Clamp( -inverse * a, -1f, 1f ) );
		var cosine = MathF.Acos( Math.Clamp( inverse * b, -1f, 1f ) );
		return (sine > 0f ? cosine : -cosine) / (2f * MathF.PI);
	}

	/// <summary>The cosine and sine the sprite rotation takes for a roll in turns: table entries at the truncated 256 x roll (0x591461-0x59149D).</summary>
	// [BIN:TPW-EXE:0x591430 sprite rotation] sin = table[trunc(roll * 256) & 255], cos = table[(0x40 - trunc(roll * -256)) & 255]; each corner (x, y) about the quad's centre becomes (cos * x + sin * y, cos * y - sin * x)
	internal static (float Cos, float Sin) RollRotation( float rollTurns )
	{
		var sine = SineTable[(int)(rollTurns * 256f) & 0xFF];
		var cosine = SineTable[(0x40 - (int)(rollTurns * -256f)) & 0xFF];
		return (cosine, sine);
	}

	/// <summary>The depth along the camera's forward axis that <see cref="TryMoonQuad"/> places a quad at: inside the projection's range whatever the original's own distance of the object was.</summary>
	internal const float MoonDepth = 50f;

	/// <summary>
	/// The four corners (camera-relative engine positions, top left, top right, bottom right, bottom left) of a sky object, or false when it is behind the camera.
	/// </summary>
	/// <param name="viewRotation">The view matrix without its translation.</param>
	/// <param name="projection">The camera's projection.</param>
	/// <param name="rollTurns">The camera roll, <see cref="CameraRoll"/>.</param>
	// [BIN:TPW-EXE:0x5860D0 sky object draw] the point (X, Y, Z) goes through the sky's clip matrix (the view without its translation times the projection, 0x5865AA, case 0 of 0x578D20) to (x, y, z, w); the quad takes the offsets (-+h, +-h), h = Size x 0.5 (0x701F50), turns them by the camera roll (0x591430), scales the x offsets by the aspect word 0x8BCBCC (height over width) and adds them to x and y in clip space, keeping z and w: a square on the screen whose half height is h / w, its size falling with the depth; 0x565F90 mode -3 clips it against the frustum and takes it white (0xFFFFFFFF) with the corner coordinates of the texture page
	// the quad is built here at the depth MoonDepth along the same direction with the clip offsets scaled with it, which projects to the same picture (it depends only on the direction and on h / w) and keeps the quad inside the projection's depth range
	internal static bool TryMoonQuad( SVector3 position, float size, System.Numerics.Matrix4x4 viewRotation, System.Numerics.Matrix4x4 projection, float rollTurns, out SVector3[] corners )
	{
		corners = Array.Empty<SVector3>();
		var clipMatrix = viewRotation * projection;
		if ( !System.Numerics.Matrix4x4.Invert( clipMatrix, out var inverse ) )
			return false;
		var clip = System.Numerics.Vector4.Transform( new System.Numerics.Vector4( ToEngine( position ), 1f ), clipMatrix );
		if ( !(clip.W > 1e-4f) || !float.IsFinite( clip.W ) )
			return false;
		var scale = MoonDepth / clip.W;
		clip *= scale;
		var half = size * 0.5f * scale;
		var aspect = projection.M11 / projection.M22;
		var (cosine, sine) = RollRotation( rollTurns );
		var unit = new (float X, float Y)[] { (-1f, 1f), (1f, 1f), (1f, -1f), (-1f, -1f) };
		corners = new SVector3[4];
		for ( var index = 0; index < 4; index++ )
		{
			var (x, y) = unit[index];
			var turned = (X: cosine * x + sine * y, Y: cosine * y - sine * x);
			var corner = System.Numerics.Vector4.Transform( new System.Numerics.Vector4( clip.X + turned.X * aspect * half, clip.Y + turned.Y * half, clip.Z, clip.W ), inverse );
			if ( !(MathF.Abs( corner.W ) > 1e-9f) )
				return false;
			corners[index] = new SVector3( corner.X, corner.Y, corner.Z ) / corner.W;
		}
		return true;
	}

	/// <summary>A loaded sky object: its settings, its texture and the buffer its two triangles are written to each frame.</summary>
	private sealed class Moon( SkyObject definition, Material<ObjectUniformBuffer> material )
	{
		public SkyObject Definition { get; } = definition;
		public Material<ObjectUniformBuffer> Material { get; } = material;
		public DeviceBuffer? Buffer { get; set; }
	}

	private void LoadMoons( string levelName )
	{
		var folder = $"/levels/{levelName}/";
		IReadOnlyList<SkyObject> objects;
		try
		{
			objects = ReadObjects( levelName, path => FileSystem.FileExists( path ), ( path, key ) => new SettingsFile( path )[key] );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or ArgumentException or KeyNotFoundException )
		{
			Log.Warning( $"Sky objects of level {levelName} unreadable: {exception.Message}" );
			return;
		}
		foreach ( var definition in objects )
		{
			var path = folder + "sky/" + definition.File;
			if ( !FileSystem.FileExists( path ) )
				continue;
			try
			{
				var material = new Material<ObjectUniformBuffer>( "content/shaders/particle.shader", MaterialFlags.DisableDepth | MaterialFlags.DoubleSided );
				material.Set( "Color", new Texture( path ) );
				moons.Add( new Moon( definition, material ) );
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or ArgumentException or KeyNotFoundException )
			{
				Log.Warning( $"Sky object {definition.File} of level {levelName} not drawn: {exception.Message}" );
			}
		}
	}

	/// <summary>
	/// The camera roll of the current view: the world-up (engine Z) components of the camera's right and up vectors, which are the third row's first two elements of the view matrix.
	/// </summary>
	private static float CurrentRoll( System.Numerics.Matrix4x4 view ) => CameraRoll( view.M31, view.M32 );

	/// <summary>Draws the sky objects of one pass (the behind ones before the clouds, the front ones after the cylinder), each as a textured quad of two triangles in the sky's camera-centred space.</summary>
	private void DrawMoons( CommandList commandList, bool front, ObjectUniformBuffer uniforms )
	{
		if ( moons.Count == 0 || !CompatibilityRuntime.RenderQuality.SkyObjects )
			return;
		var roll = CurrentRoll( uniforms.g_mView );
		foreach ( var moon in moons )
		{
			if ( moon.Definition.Front != front || !TryMoonQuad( moon.Definition.Position, moon.Definition.Size, uniforms.g_mView, uniforms.g_mProj, roll, out var corners ) )
				continue;
			// the corner coordinates of the texture page of a standalone file: 0..1 each way, the file's first stored row at the top vertices (like the clouds')
			var white = Tint( 255, 255, 255, 255 );
			var vertices = new Vertex[6];
			var uv = new[] { new SVector2( 0f, 0f ), new SVector2( 1f, 0f ), new SVector2( 1f, 1f ), new SVector2( 0f, 1f ) };
			foreach ( var (slot, corner) in new[] { (0, 0), (1, 1), (2, 2), (3, 0), (4, 2), (5, 3) } )
				vertices[slot] = new Vertex( corners[corner], white, Upload( uv[corner] ) );
			var size = (uint)vertices.Length * VertexStride;
			moon.Buffer ??= Device.ResourceFactory.CreateBuffer( new BufferDescription( size, BufferUsage.VertexBuffer | BufferUsage.Dynamic ) );
			commandList.UpdateBuffer( moon.Buffer, 0, ref MemoryMarshal.GetArrayDataReference( vertices ), size );
			commandList.SetVertexBuffer( 0, moon.Buffer );
			moon.Material.Set( "ObjectUniformBuffer", uniforms );
			DrawRange( commandList, moon.Material, 0, vertices.Length );
		}
	}

	// ---- geometry in the original's axes (X, Y up, Z) -------------------------------------------------------------------------------------

	/// <summary>The cloud grid's vertex at column <paramref name="xi"/> (along X) and row <paramref name="zi"/> (along Z), index <c>xi * 16 + zi</c> in the original's array.</summary>
	internal static SVector3 CloudPosition( int xi, int zi )
	{
		var x = xi * Extent * (1f / 15f) - Extent * 0.5f;
		var z = zi * Extent * (1f / 15f) - Extent * 0.5f;
		return new SVector3( x, CentreHeight - MathF.Sqrt( x * x + z * z ) * Sag, z );
	}

	/// <summary>The cylinder's radius at its top ring: 0.6 of the grid's half width.</summary>
	internal static float CylinderRadius => Extent * 0.5f * 0.6f;

	/// <summary>The height of the cylinder's top ring, mirrored for its bottom: the grid's sag at the cylinder's radius.</summary>
	internal static float CylinderTop => CentreHeight - CylinderRadius * Sag;

	/// <summary>
	/// A cylinder vertex: <paramref name="column"/> 0..17 walks the circle in two runs of nine (the angle is pi * i / 16 for i = 0, 2 .. 16 and then again 16 .. 32),
	/// <paramref name="ring"/> 0..3 goes down from the top at radius 1, 0.98, 0.96 and 0.84 of the radius.
	/// </summary>
	internal static SVector3 CylinderPosition( int column, int ring )
	{
		var index = column < 9 ? column * 2 : 16 + (column - 9) * 2;
		var angle = MathF.PI * index / 16f;
		var radius = CylinderRadius * ring switch { 0 => 1f, 1 => 0.98f, 2 => 0.96f, _ => 0.84f };
		var top = CylinderTop;
		var height = ring switch { 0 => top, 1 => top * 0.5f, 2 => 0f, _ => -top };
		return new SVector3( MathF.Cos( angle ) * radius, height, MathF.Sin( angle ) * radius );
	}

	/// <summary>
	/// The cylinder texture coordinate: <c>u</c> runs 0 to 1 over each half circle; <c>v</c> picks one of the two stacked copies of the haze gradient
	/// in <c>sky_cyl.tga</c>, the first half circle the lower and the second the upper.
	/// </summary>
	// [BIN:TPW-EXE:0x5863C0 cylinder coordinates] the first run takes v 0.99, 0.84, 0.67, 0.51 and the second 0.49, 0.34, 0.17, 0.01 per ring (0x3F7D70A4, 0.34 + 0.5, 0.17 + 0.5, 0.01 + 0.5 and 0x3EFAE148, 0.34, 0.17, 0.01); the flag 0x2000000 swaps in 0.49, 0.01, 0.19 for the last three rings
	internal static SVector2 CylinderTexCoord( int column, int ring )
	{
		var u = (column < 9 ? column : column - 9) * 0.125f;
		var v = column < 9
			? ring switch { 0 => 0.99f, 1 => 0.34f + 0.5f, 2 => 0.17f + 0.5f, _ => 0.01f + 0.5f }
			: ring switch { 0 => 0.49f, 1 => 0.34f, 2 => 0.17f, _ => 0.01f };
		return new SVector2( u, v );
	}

	/// <summary>The cloud grid's triangles (indices <c>xi * 16 + zi</c>): every cell's two triangles whose corners all lie above the cylinder's bottom ring.</summary>
	// [BIN:TPW-EXE:0x585720 cloud indices] per cell (a, b): (a, b + 1), (a, b), (a + 1, b + 1) when those three are above -top, then (a, b), (a + 1, b), (a + 1, b + 1) when those are
	internal static IEnumerable<(int V0, int V1, int V2)> CloudTriangles()
	{
		var floor = -CylinderTop;
		bool Above( int xi, int zi ) => CloudPosition( xi, zi ).Y > floor;
		for ( var a = 0; a < GridSize - 1; a++ )
		{
			for ( var b = 0; b < GridSize - 1; b++ )
			{
				if ( Above( a, b ) && Above( a, b + 1 ) && Above( a + 1, b + 1 ) )
					yield return (a * GridSize + b + 1, a * GridSize + b, (a + 1) * GridSize + b + 1);
				if ( Above( a, b ) && Above( a + 1, b ) && Above( a + 1, b + 1 ) )
					yield return (a * GridSize + b, (a + 1) * GridSize + b, (a + 1) * GridSize + b + 1);
			}
		}
	}

	/// <summary>The cylinder's triangles (indices <c>column * 4 + ring</c>): per quad between two columns and two rings.</summary>
	// [BIN:TPW-EXE:0x584EF0 cylinder indices] per column of each half circle (eight of nine) and ring pair: (b, b + 5, b + 4) and (b, b + 1, b + 5) with b = ring + 4 * column
	internal static IEnumerable<(int V0, int V1, int V2)> CylinderTriangles()
	{
		for ( var half = 0; half < 2; half++ )
		{
			for ( var column = 0; column < 8; column++ )
			{
				for ( var ring = 0; ring < CylinderRings - 1; ring++ )
				{
					var b = ring + (half * 9 + column) * CylinderRings;
					yield return (b, b + 5, b + 4);
					yield return (b, b + 1, b + 5);
				}
			}
		}
	}

	/// <summary>The engine axes (X and Y on the ground, Z up) of an original position (Y up).</summary>
	internal static SVector3 ToEngine( SVector3 position ) => new( position.X, position.Z, position.Y );

	// ---- the cloud layers -------------------------------------------------------------------------------------------------------------------

	/// <summary>The texture step per grid vertex of a layer: <c>(7 - layer) * pi / 112</c>, so the first layer repeats the clouds about three times across the grid.</summary>
	// [BIN:TPW-EXE:0x584EF0 layer step] +0x21EC[layer] = (7 - layer) * pi * 0.0089285718 (0x701EE0, 0x701F48)
	internal static float LayerStep( int layer ) => (7 - layer) * MathF.PI * 0.008928572f;

	/// <summary>A layer's scroll speed along the texture's u axis, in texture repeats per (frame second * 25): 0.0008 + 0.0005 * layer.</summary>
	// [BIN:TPW-EXE:0x584EF0 layer velocity] +0x1F70[3 * layer] = 0.0008 - layer * -0.0005 (0x701F3C, 0x701F38)
	internal static float LayerVelocityU( int layer ) => 0.0008f + 0.0005f * layer;

	/// <summary>A layer's scroll speed along the texture's v axis: 0.00009 - 0.00003 * layer.</summary>
	// [BIN:TPW-EXE:0x584EF0 layer velocity] +0x1F78[3 * layer] = 0.00009 - layer * 0.00003 (0x701F44, 0x701F40)
	internal static float LayerVelocityV( int layer ) => 0.00009f - 0.00003f * layer;

	/// <summary>A layer's alpha: 0xCF less 0x38 per layer (0xCFFFFFFF - 0x38000000 * layer, ANDed with the grid colour's 0xFF).</summary>
	internal static byte LayerAlpha( int layer ) => (byte)((0xCFFFFFFFu - 0x38000000u * (uint)layer) >> 24);

	/// <summary>Moves a layer's scroll offsets by one frame; the original wraps each into -1..1 and holds still for a frame of a second or more.</summary>
	internal static (float U, float V) Scroll( int layer, float u, float v, float frameSeconds )
	{
		var step = frameSeconds * VelocityScale;
		if ( !(step <= VelocityScale && step >= -VelocityScale) )
			step = 0f;
		return (Wrap( u + LayerVelocityU( layer ) * step ), Wrap( v + LayerVelocityV( layer ) * step ));
	}

	private static float Wrap( float value ) => value > 1f || value < -1f ? value - MathF.Truncate( value ) : value;

	/// <summary>
	/// The texture coordinate of a layer's vertex: <c>u</c> follows the row (Z) and <c>v</c> the column (X), and the four layers mirror the texture in turn
	/// (u, v signs -+, --, +-, ++).
	/// </summary>
	// [BIN:TPW-EXE:0x5863C0 layer coordinates] u = (row * step + offset u) * s5 + o5 and v = (column * step + offset v) * s6 + o6, where each layer negates and swaps the texture page's scale and offset pairs, so with a whole page (scale 1, offset 0) the signs run (-1, +1), (-1, -1), (+1, -1), (+1, +1)
	internal static SVector2 LayerTexCoord( int layer, int xi, int zi, float offsetU, float offsetV )
	{
		var u = zi * LayerStep( layer ) + offsetU;
		var v = xi * LayerStep( layer ) + offsetV;
		return (layer & 3) switch
		{
			0 => new SVector2( -u, v ),
			1 => new SVector2( -u, -v ),
			2 => new SVector2( u, -v ),
			_ => new SVector2( u, v )
		};
	}

	// ---- the colour grid --------------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// The 16 x 16 colour grid of <c>sky_rgb.tga</c>: the pixel at the centre of each of 16 x 16 cells, index <c>x * 16 + y</c> (opaque ARGB). The cloud vertex
	/// (column <c>xi</c>, row <c>zi</c>) reads the cell at x = zi, y = xi.
	/// </summary>
	// [BIN:TPW-EXE:0x585CE0 colour grid] samples (width / 16) / 2 + k * (width / 16) over x and the same over y into the grid at +0xF60 (and its three copies); the draw reads index column + 16 * row (0x5863C0)
	internal static uint[] SampleColourGrid( byte[] rgba, int width, int height )
	{
		var grid = new uint[GridSize * GridSize];
		var stepX = width / GridSize;
		var stepY = height / GridSize;
		for ( var x = 0; x < GridSize; x++ )
		{
			for ( var y = 0; y < GridSize; y++ )
			{
				var px = Math.Clamp( stepX / 2 + x * stepX, 0, width - 1 );
				// file order: the first stored row is the bottom of the (top-down) decoded image
				var py = height - 1 - Math.Clamp( stepY / 2 + y * stepY, 0, height - 1 );
				var at = (py * width + px) * 4;
				grid[x * GridSize + y] = 0xFF000000u | (uint)rgba[at] << 16 | (uint)rgba[at + 1] << 8 | rgba[at + 2];
			}
		}
		return grid;
	}

	private static uint[] ReadColourGrid( string path )
	{
		var (data, width, height) = Texture.DecodeImage( path );
		return SampleColourGrid( data, width, height );
	}

	/// <summary>The grid without <c>sky_rgb.tga</c>: the engine's own start values, random greys from 128 to 255 (0x585720), here a fixed pattern.</summary>
	// [BIN:TPW-EXE:0x585720 default colours] ((rand() & 0x7F) * 0x10101) | 0xFF808080 per cell; OpenTPW takes its own deterministic hash so a presentation-only fallback never touches a random stream
	private static uint[] FallbackColourGrid()
	{
		var grid = new uint[GridSize * GridSize];
		for ( var index = 0; index < grid.Length; index++ )
		{
			var hash = (uint)index * 2654435761u;
			var grey = (hash >> 25) & 0x7F;
			grid[index] = 0xFF808080u | grey * 0x10101u;
		}
		return grid;
	}

	// ---- drawing ------------------------------------------------------------------------------------------------------------------------------

	private static SVector3 Tint( byte red, byte green, byte blue, byte alpha ) => new( red, green, blue + 256f * alpha );

	private static SVector3 CloudTint( uint[] colourGrid, int layer, int xi, int zi )
	{
		// [BIN:TPW-EXE:0x5863C0 cloud colour] the vertex colour is the layer's 0xCFFFFFFF - 0x38000000 * layer ANDed with the grid colour at index column + 16 * row
		var colour = colourGrid[zi * GridSize + xi];
		return Tint( (byte)(colour >> 16), (byte)(colour >> 8), (byte)colour, LayerAlpha( layer ) );
	}

	/// <summary>
	/// Every vertex of <paramref name="layers"/> cloud layers (each layer's triangles in order) followed by the cylinder's, built from scratch with the given scroll offsets;
	/// <paramref name="clouds"/> is the number of cloud vertices.
	/// </summary>
	internal static Vertex[] BuildVertices( int layers, SVector3[] cloudPositions, uint[] colourGrid, (int V0, int V1, int V2)[] cloudTriangles, SVector3[] cylinderPositions,
		(int V0, int V1, int V2)[] cylinderTriangles, float[] offsetU, float[] offsetV, out int clouds )
	{
		var vertices = new List<Vertex>();
		void AddCloud( int layer, int index )
		{
			var xi = index / GridSize;
			var zi = index % GridSize;
			vertices.Add( new Vertex( cloudPositions[index], CloudTint( colourGrid, layer, xi, zi ), Upload( LayerTexCoord( layer, xi, zi, offsetU[layer], offsetV[layer] ) ) ) );
		}
		void AddCylinder( int index, SVector3 tint ) =>
			vertices.Add( new Vertex( cylinderPositions[index], tint, Upload( CylinderTexCoord( index / CylinderRings, index % CylinderRings ) ) ) );
		for ( var layer = 0; layer < layers; layer++ )
		{
			foreach ( var (v0, v1, v2) in cloudTriangles )
			{
				AddCloud( layer, v0 );
				AddCloud( layer, v1 );
				AddCloud( layer, v2 );
			}
		}
		clouds = vertices.Count;
		var white = Tint( 255, 255, 255, 255 );
		foreach ( var (v0, v1, v2) in cylinderTriangles )
		{
			AddCylinder( v0, white );
			AddCylinder( v1, white );
			AddCylinder( v2, white );
		}
		return vertices.ToArray();
	}

	/// <summary>Rewrites the clouds' texture coordinates of vertices built by <see cref="BuildVertices"/>: one coordinate per grid vertex and layer, then every triangle corner takes its vertex's.</summary>
	internal static void ScrollTexCoords( Vertex[] staged, int layers, int[] cloudCorners, float[] offsetU, float[] offsetV, SVector2[] scratch )
	{
		for ( var layer = 0; layer < layers; layer++ )
		{
			for ( var index = 0; index < scratch.Length; index++ )
				scratch[index] = Upload( LayerTexCoord( layer, index / GridSize, index % GridSize, offsetU[layer], offsetV[layer] ) );
			var first = layer * cloudCorners.Length;
			for ( var corner = 0; corner < cloudCorners.Length; corner++ )
				staged[first + corner].TexCoords = scratch[cloudCorners[corner]];
		}
	}

	/// <summary>The triangle corners of the cloud grid in drawing order, as <see cref="ScrollTexCoords"/> reads them.</summary>
	internal static int[] CornersOf( (int V0, int V1, int V2)[] triangles ) => triangles.SelectMany( triangle => new[] { triangle.V0, triangle.V1, triangle.V2 } ).ToArray();

	/// <summary>
	/// The coordinate for OpenTPW's decoded image, which is top-down: the sky's TGA files are stored bottom-up and the engine addresses them in file order
	/// (v = 0 is the first stored row, the bottom of the picture), so v is mirrored.
	/// </summary>
	// [DATA:levels/<theme>/sky/*.tga] the three files have a descriptor without the top-origin bit; sky_cyl.tga's haze (alpha 24) is its last stored rows and the cylinder's top ring reads v 0.99 and 0.49, so the engine reads rows in file order and the haze joins the cloud layer at the top
	private static SVector2 Upload( SVector2 engine ) => new( engine.X, 1f - engine.Y );

	/// <summary>Moves the scroll along once per rendered frame by the scene clock's frame time (<see cref="SceneClock.CappedFrameSeconds"/>: the park's game speed, zero while paused).</summary>
	private void Advance( float frameSeconds )
	{
		var frame = global::Global.Render.FrameNumber;
		if ( frame == lastFrame )
			return;
		lastFrame = frame;
		for ( var layer = 0; layer < MaximumLayers; layer++ )
			(offsetU[layer], offsetV[layer]) = Scroll( layer, offsetU[layer], offsetV[layer], frameSeconds );
	}

	/// <summary>
	/// Gives every vertex of the cloud grid <paramref name="argb"/> (the lobby's sky colours its whole 16 x 16 grid with one colour each frame); the clouds are rebuilt on the next draw when it changed.
	/// </summary>
	// [BIN:TPW-EXE:0x5d96c0 lobby sky grid] the lobby fills the 256 entries of the engine sky's colour grid (0x8c2788) with one 0xFF:R:G:B each frame
	public void SetGridColour( uint argb )
	{
		if ( colourGrid[0] == argb && colourGrid.All( colour => colour == argb ) )
			return;
		Array.Fill( colourGrid, argb );
		staging.Invalidate();
	}

	/// <summary>Draws the sky behind everything else of the world pass: no depth test or write, centred on the camera.</summary>
	public void Draw( float frameSeconds, SVector3? tint = null )
	{
		if ( disposed || global::Global.Render == null )
			return;
		// [BIN:TPW-EXE:0x5863C0 gate] a layer count of 0 (SKYQUALITY 0) draws no sky
		var layers = Math.Min( CompatibilityRuntime.RenderQuality.SkyLayers, MaximumLayers );
		if ( layers <= 0 )
			return;
		Advance( frameSeconds );

		var staged = staging.Update( layers, offsetU, offsetV, out var cloudCount );

		var size = (uint)staged.Length * VertexStride;
		if ( vertexBuffer == null || vertexBuffer.SizeInBytes < size )
		{
			var old = vertexBuffer;
			vertexBuffer = Device.ResourceFactory.CreateBuffer( new BufferDescription( size, BufferUsage.VertexBuffer | BufferUsage.Dynamic ) );
			if ( old != null )
				global::Global.Render.ScheduleDelete( old.Dispose );
		}
		var commandList = global::Global.Render.CommandList;
		commandList.UpdateBuffer( vertexBuffer, 0, ref MemoryMarshal.GetArrayDataReference( staged ), size );
		commandList.SetVertexBuffer( 0, vertexBuffer );

		// the view without its translation: the sky is centred on the camera and only turns with it
		var view = Camera.ViewMatrix;
		view.M41 = view.M42 = view.M43 = 0f;
		var uniforms = new ObjectUniformBuffer
		{
			g_mModel = System.Numerics.Matrix4x4.Identity,
			g_mView = view,
			g_mProj = Camera.ProjMatrix,
			g_vCameraPos = SVector3.Zero,
			// [EXT:evening] the sky's tint at dusk and night rides in the light colour particle.shader never used (zero: no tint)
			g_vLightColor = tint ?? SVector3.Zero,
			g_flTime = Time.Now
		};
		cloudMaterial.Set( "ObjectUniformBuffer", uniforms );
		cylinderMaterial.Set( "ObjectUniformBuffer", uniforms );
		// [BIN:TPW-EXE:0x5863C0 sky object passes] the objects of mask 2 are drawn before the cloud layers (0x5865AC) and those of mask 1 after the cylinder (0x586C9E)
		DrawMoons( commandList, front: false, uniforms );
		commandList.SetVertexBuffer( 0, vertexBuffer );
		DrawRange( commandList, cloudMaterial, 0, cloudCount );
		DrawRange( commandList, cylinderMaterial, cloudCount, staged.Length - cloudCount );
		DrawMoons( commandList, front: true, uniforms );
	}

	/// <summary>The sky's vertices for the layer count last drawn: built once per layer count, then only the clouds' texture coordinates are rewritten per frame.</summary>
	internal sealed class Staging( SVector3[] cloudPositions, uint[] colourGrid, (int V0, int V1, int V2)[] cloudTriangles, SVector3[] cylinderPositions, (int V0, int V1, int V2)[] cylinderTriangles )
	{
		private readonly int[] cloudCorners = CornersOf( cloudTriangles );
		private readonly SVector2[] scratch = new SVector2[GridSize * GridSize];
		private Vertex[] staged = Array.Empty<Vertex>();
		private int stagedLayers = -1, stagedClouds;

		/// <summary>Builds the vertices again at the next update (the colour grid changed).</summary>
		public void Invalidate() => stagedLayers = -1;

		/// <summary>The vertices for <paramref name="layers"/> layers scrolled to the offsets, and how many of them are clouds.</summary>
		public Vertex[] Update( int layers, float[] offsetU, float[] offsetV, out int clouds )
		{
			if ( stagedLayers != layers )
			{
				staged = BuildVertices( layers, cloudPositions, colourGrid, cloudTriangles, cylinderPositions, cylinderTriangles, offsetU, offsetV, out stagedClouds );
				stagedLayers = layers;
			}
			ScrollTexCoords( staged, layers, cloudCorners, offsetU, offsetV, scratch );
			clouds = stagedClouds;
			return staged;
		}
	}

	private static void DrawRange( CommandList commandList, Material<ObjectUniformBuffer> material, int first, int count )
	{
		if ( count <= 0 )
			return;
		commandList.SetPipeline( material.Pipeline );
		var resourceSets = material.GetResourceSets();
		for ( uint index = 0; index < resourceSets.Length; ++index )
			commandList.SetGraphicsResourceSet( index, resourceSets[index] );
		commandList.Draw( (uint)count, 1, (uint)first, 0 );
	}

	/// <summary>The fog colour a level clears to (<c>ThemeEngine.FogColour</c> of its Standard.sam, a Direct3D colour 0xRRGGBB), or the front end's fog colour without one.</summary>
	// [BIN:TPW-EXE:0x4080E0 fog colour] the level load copies the setting (0x7856D4) into the fog colour 0x8BD47C that the viewport clear and the sky's haze blend with (0x5737AA)
	// [DATA:levels/<theme>/Standard.sam:ThemeEngine.FogColour] jungle and fantasy 4774136 (0x48D8F8), hallow 1122884 (0x112244), space 16286768 (0xF88430)
	internal static RgbaByte FogColour( string levelName ) =>
		FogColour( levelName, path => FileSystem.FileExists( path ), path => new SettingsFile( path )["ThemeEngine.FogColour"] );

	/// <summary>The fog colour from settings files read through <paramref name="exists"/> and <paramref name="read"/> (the setting's text, or null), the level's own file first.</summary>
	internal static RgbaByte FogColour( string levelName, Func<string, bool> exists, Func<string, string?> read )
	{
		try
		{
			foreach ( var path in new[] { $"/levels/{levelName}/Standard.sam", "/levels/Standard.sam" } )
			{
				if ( !exists( path ) )
					continue;
				var text = read( path );
				if ( !string.IsNullOrWhiteSpace( text ) && long.TryParse( text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value ) )
					return FogColour( value );
			}
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or ArgumentException or KeyNotFoundException )
		{
			Log.Warning( $"Fog colour of level {levelName} unreadable: {exception.Message}" );
		}
		return FogColour( ((long)LobbyEffects.ClearColour.R << 16) | ((long)LobbyEffects.ClearColour.G << 8) | LobbyEffects.ClearColour.B );
	}

	/// <summary>An opaque colour from a Direct3D colour value (the alpha byte is ignored).</summary>
	internal static RgbaByte FogColour( long value ) => new( (byte)(value >> 16), (byte)(value >> 8), (byte)value, 255 );

	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		// the cloud and cylinder textures are path textures, shared through the texture cache (Texture.TryGetCachedTexture): the next sky of
		// the same level takes them again instead of loading new ones, so they are kept (two per level), as ReleaseOwned keeps every path texture
		cloudMaterial.ReleaseOwnedResources();
		cylinderMaterial.ReleaseOwnedResources();
		foreach ( var moon in moons )
		{
			moon.Material.ReleaseOwnedResources();
			var moonBuffer = moon.Buffer;
			moon.Buffer = null;
			if ( moonBuffer != null && global::Global.Render is { } moonRender )
				moonRender.ScheduleDelete( moonBuffer.Dispose );
		}
		var buffer = vertexBuffer;
		vertexBuffer = null;
		if ( buffer != null && global::Global.Render is { } render )
			render.ScheduleDelete( buffer.Dispose );
	}
}
