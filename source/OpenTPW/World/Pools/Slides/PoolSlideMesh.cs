using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>The surfaces of the slides of a park (<see cref="PoolSlideRenderer"/>): the track's shell and its pylons, in engine space (X and Y on the ground, Z up).</summary>
public sealed record PoolSlideMesh( PoolMeshPart Track, PoolMeshPart Pylons )
{
	public static PoolSlideMesh Empty => new( new PoolMeshPart(), new PoolMeshPart() );
	public int TriangleCount => Track.TriangleCount + Pylons.TriangleCount;
	public bool IsEmpty => Track.Vertices.Count == 0 && Pylons.Vertices.Count == 0;
}

/// <summary>
/// The geometry of water slides (docs/POOLS.md, "Water slides"), CPU-only and deterministic: the track's centreline (<see cref="PoolSlide.PointAt"/>) swept with the profile's cross-section, and a pylon
/// under every piece. The open flume is a half pipe with a lip along both edges; the tube is a ring with a wide slit along its top, so riders stay visible in it. The cross-sections follow the track's
/// slope and turns exactly, which is why the slides are generated here rather than made of rigid models. Heights are engine units, a cell is <c>cellSize</c> wide.
/// </summary>
// [EXT:pools-render] OpenTPW's own slide rendering; the original game has no pools
public static class PoolSlideMeshBuilder
{
	/// <summary>The flume's radius: the half pipe's width is twice this.</summary>
	public const float FlumeRadius = 0.55f;
	/// <summary>The tube's radius.</summary>
	public const float TubeRadius = 0.62f;
	/// <summary>The width of the lip along the flume's edges.</summary>
	public const float Lip = 0.14f;
	/// <summary>The distance between two samples of the centreline, 1/1024 cell.</summary>
	public const int SampleStep = 96;
	/// <summary>Half the width of a pylon.</summary>
	public const float PylonHalfWidth = 0.13f;
	/// <summary>The engine units of track length one repeat of the texture covers.</summary>
	public const float TextureLength = 2f;

	/// <summary>Points of the profile's cross-section, as (lateral, vertical) offsets from the centreline's point with the outward normal of the shell there.</summary>
	public static IReadOnlyList<(float Lat, float Up, float NormalLat, float NormalUp)> Section( PoolSlideProfile profile )
	{
		var points = new List<(float, float, float, float)>();
		if ( profile == PoolSlideProfile.Flume )
		{
			var r = FlumeRadius;
			points.Add( (-r - Lip, r, 0f, 1f) );
			// the half pipe from the left rim down through the bottom to the right rim: the circle's lower half, 180 to 360 degrees
			for ( var step = 0; step <= 12; step++ )
			{
				var angle = Math.PI + Math.PI * step / 12;
				points.Add( ((float)(r * Math.Cos( angle )), (float)(r + r * Math.Sin( angle )), (float)Math.Cos( angle ), (float)Math.Sin( angle )) );
			}
			points.Add( (r + Lip, r, 0f, 1f) );
		}
		else
		{
			var r = TubeRadius;
			// the ring without a 140 degree slit on the top (wide enough that the bottom anchored rider, whose back rests on the bed, shows in the camera's oblique view): from 160 degrees round through the bottom to 380 (20) degrees
			for ( var step = 0; step <= 20; step++ )
			{
				var angle = (160 + 220.0 * step / 20) * Math.PI / 180;
				points.Add( ((float)(r * Math.Cos( angle )), (float)(r + r * Math.Sin( angle )), (float)Math.Cos( angle ), (float)Math.Sin( angle )) );
			}
		}
		return points;
	}

	/// <summary>The mesh of every slide, with the ground the pylons stand on and the water levels of the pool cells (pylons in the pool stand on its floor).</summary>
	public static PoolSlideMesh Build( IEnumerable<PoolSlide> slides, Func<float, float, float> ground, Func<int, int, float?> waterLevel, SVector2 origin, float cellSize, float poolDepth = 1.75f )
	{
		var mesh = PoolSlideMesh.Empty;
		foreach ( var slide in slides )
		{
			if ( slide.Pieces.Count == 0 )
				continue;
			AddTrack( mesh.Track, slide, origin, cellSize );
			AddPylons( mesh.Pylons, slide, ground, waterLevel, origin, cellSize, poolDepth );
		}
		return mesh;
	}

	/// <summary>The engine position of a point of a slide.</summary>
	public static SVector3 ToEngine( PoolSlidePoint point, SVector2 origin, float cellSize ) =>
		new( origin.X + point.X / (float)PoolSlide.Unit * cellSize, origin.Y + point.Y / (float)PoolSlide.Unit * cellSize, point.Height / (float)PoolSlide.HeightUnit );

	private static void AddTrack( PoolMeshPart part, PoolSlide slide, SVector2 origin, float cellSize )
	{
		var section = Section( slide.Profile );
		var positions = new List<int>();
		for ( var index = 0; index < slide.Pieces.Count; index++ )
		{
			var start = slide.StartOf( index );
			var length = PoolSlide.LengthOf( slide.Pieces[index] );
			var steps = Math.Max( 2, (length + SampleStep - 1) / SampleStep );
			for ( var step = index == 0 ? 0 : 1; step <= steps; step++ )
				positions.Add( start + length * step / steps );
		}
		// one ring of section points per sample, in the frame of the track there
		var rings = new List<(SVector3[] Points, SVector3[] Normals, float Length)>();
		foreach ( var position in positions )
		{
			var centre = ToEngine( slide.PointAt( position ), origin, cellSize );
			var behind = ToEngine( slide.PointAt( Math.Max( 0, position - 8 ) ), origin, cellSize );
			var ahead = ToEngine( slide.PointAt( Math.Min( slide.Length, position + 8 ) ), origin, cellSize );
			var tangent = SVector3.Normalize( ahead - behind );
			var lateral = SVector3.Normalize( new SVector3( -tangent.Y, tangent.X, 0 ) );
			var up = SVector3.Normalize( SVector3.Cross( tangent, lateral ) );
			var points = new SVector3[section.Count];
			var normals = new SVector3[section.Count];
			for ( var i = 0; i < section.Count; i++ )
			{
				points[i] = centre + lateral * section[i].Lat + up * section[i].Up;
				// the flume is seen from above, into the pipe: its shell is lit from the inside (the bed faces up), where the tube, a closed ring, is lit from the outside
				var facing = slide.Profile == PoolSlideProfile.Flume ? -1f : 1f;
				normals[i] = SVector3.Normalize( (lateral * section[i].NormalLat + up * section[i].NormalUp) * facing );
			}
			rings.Add( (points, normals, position / (float)PoolSlide.Unit * cellSize) );
		}
		for ( var k = 0; k + 1 < rings.Count; k++ )
		{
			var (a, b) = (rings[k], rings[k + 1]);
			float v0 = a.Length / TextureLength, v1 = b.Length / TextureLength;
			for ( var i = 0; i + 1 < section.Count; i++ )
			{
				float u0 = i / (float)(section.Count - 1), u1 = (i + 1) / (float)(section.Count - 1);
				var normal = SVector3.Normalize( a.Normals[i] + a.Normals[i + 1] + b.Normals[i] + b.Normals[i + 1] );
				part.AddQuad( a.Points[i], a.Points[i + 1], b.Points[i + 1], b.Points[i], normal, new SVector2( u0, v0 ), new SVector2( u1, v0 ), new SVector2( u1, v1 ), new SVector2( u0, v1 ) );
			}
		}
	}

	private static void AddPylons( PoolMeshPart part, PoolSlide slide, Func<float, float, float> ground, Func<int, int, float?> waterLevel, SVector2 origin, float cellSize, float poolDepth )
	{
		for ( var index = 0; index < slide.Pieces.Count; index++ )
		{
			var piece = slide.Pieces[index];
			var start = slide.StartOf( index );
			// a pylon in the middle of a piece; under the splash chute, at its start (its end is the middle of the pool cell)
			var position = piece == PoolSlidePiece.Splash ? start + 40 : start + PoolSlide.LengthOf( piece ) / 2;
			var point = ToEngine( slide.PointAt( position ), origin, cellSize );
			var (cx, cy) = slide.Cells[index];
			var bottom = waterLevel( cx, cy ) is { } level ? level - poolDepth : ground( (point.X - origin.X) / cellSize, (point.Y - origin.Y) / cellSize );
			var top = point.Z - (slide.Profile == PoolSlideProfile.Flume ? 0.02f : 0f);
			if ( top - bottom < 0.2f )
				continue;
			AddPost( part, point.X, point.Y, bottom, top );
		}
	}

	private static void AddPost( PoolMeshPart part, float x, float y, float bottom, float top )
	{
		var h = PylonHalfWidth;
		float v0 = bottom / 1.5f, v1 = top / 1.5f;
		SVector3 P( float dx, float dy, float z ) => new( x + dx, y + dy, z );
		// four sides, normals outwards; u runs round the post
		part.AddQuad( P( -h, -h, bottom ), P( h, -h, bottom ), P( h, -h, top ), P( -h, -h, top ), new SVector3( 0, -1, 0 ), new SVector2( 0, v0 ), new SVector2( 0.25f, v0 ), new SVector2( 0.25f, v1 ), new SVector2( 0, v1 ) );
		part.AddQuad( P( h, -h, bottom ), P( h, h, bottom ), P( h, h, top ), P( h, -h, top ), new SVector3( 1, 0, 0 ), new SVector2( 0.25f, v0 ), new SVector2( 0.5f, v0 ), new SVector2( 0.5f, v1 ), new SVector2( 0.25f, v1 ) );
		part.AddQuad( P( h, h, bottom ), P( -h, h, bottom ), P( -h, h, top ), P( h, h, top ), new SVector3( 0, 1, 0 ), new SVector2( 0.5f, v0 ), new SVector2( 0.75f, v0 ), new SVector2( 0.75f, v1 ), new SVector2( 0.5f, v1 ) );
		part.AddQuad( P( -h, h, bottom ), P( -h, -h, bottom ), P( -h, -h, top ), P( -h, h, top ), new SVector3( -1, 0, 0 ), new SVector2( 0.75f, v0 ), new SVector2( 1f, v0 ), new SVector2( 1f, v1 ), new SVector2( 0.75f, v1 ) );
	}
}

/// <summary>The slides' textures, generated in code like the pool's (docs/POOLS.md, "Styles"): nothing of the original game is used. Square RGBA, tiling.</summary>
// [EXT:pools-render] OpenTPW's own slide art; the original game has no pools
public static class PoolSlideTextures
{
	public const int Size = 64;

	/// <summary>
	/// The surface of a slide in a pool style: across the section (the first coordinate) a body colour with rim stripes, along the track (the second) a dash pattern. The flume takes the style's bright shallow
	/// water colour, the tube the coping's colours, so the four themes' slides read apart from their pools and from each other.
	/// </summary>
	public static byte[] Surface( PoolStyle style, PoolSlideProfile profile )
	{
		var body = profile == PoolSlideProfile.Flume ? style.WaterShallow.Mixed( new PoolColour( 255, 255, 255 ), 0.2f ) : style.CopingBase.Mixed( new PoolColour( 255, 255, 255 ), 0.15f );
		var stripe = profile == PoolSlideProfile.Flume ? style.CopingBase : style.CopingAccent.Mixed( new PoolColour( 255, 255, 255 ), 0.25f );
		var edge = profile == PoolSlideProfile.Flume ? style.CopingJoint.Mixed( style.CopingBase, 0.5f ) : style.CopingJoint.Mixed( style.CopingAccent, 0.4f );
		var pixels = new byte[Size * Size * 4];
		for ( var y = 0; y < Size; y++ )
		{
			for ( var x = 0; x < Size; x++ )
			{
				var colour = body;
				// rim stripes on both sides of the section
				if ( x < 6 || x >= Size - 6 )
					colour = x is 0 or 5 || x == Size - 1 || x == Size - 6 ? edge : stripe;
				// the flume's flow line, the tube's rings
				else if ( profile == PoolSlideProfile.Flume && x is >= 30 and <= 33 )
					colour = body.Mixed( new PoolColour( 255, 255, 255 ), 0.45f );
				else if ( profile == PoolSlideProfile.Tube && y % 32 < 3 )
					colour = stripe;
				// a faint grain so large areas are not flat
				var grain = 1f + (((x * 31 + y * 17) % 7) - 3) * 0.012f;
				colour = colour.Scaled( grain );
				var offset = (y * Size + x) * 4;
				pixels[offset] = (byte)colour.R;
				pixels[offset + 1] = (byte)colour.G;
				pixels[offset + 2] = (byte)colour.B;
				pixels[offset + 3] = 255;
			}
		}
		return pixels;
	}
}
