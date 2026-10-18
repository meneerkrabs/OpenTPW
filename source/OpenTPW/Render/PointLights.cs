using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SVector3 = System.Numerics.Vector3;
using SVector4 = System.Numerics.Vector4;

namespace OpenTPW;

// [EXT:point-lights] The original engine supports point lights (tp.exe 0x457E70 builds light type 3, TPLight.cpp; the vertex lighting 0x5741B0 adds
// (radius^2 - distance^2) x (1 / radius^2) x colour for every enabled one) but nothing in the shipped game ever creates one, so OpenTPW's park has none of its own: every
// light in the world is an addition of this reimplementation (docs/LIGHTS.md). Presentation only: nothing here is read by the simulation or enters a state hash.

/// <summary>
/// One point light in engine units: where it shines from, the colour it adds (linear 0..1 channels, a light of colour 1 adds the full texel colour at its centre) and the radius at which
/// it has faded to nothing.
/// </summary>
public readonly record struct PointLight( SVector3 Position, SVector3 Colour, float Radius )
{
	/// <summary>
	/// Whether the light can change anything: a finite position, a finite radius of at least <see cref="PointLightMath.MinimumRadius"/> (a smaller one makes 1 / r2 overflow) and a finite colour
	/// with a non-zero channel (a negative channel darkens, as the original's unclamped float colour would).
	/// </summary>
	public bool IsUsable => float.IsFinite( Radius ) && Radius >= PointLightMath.MinimumRadius && float.IsFinite( Position.X ) && float.IsFinite( Position.Y ) && float.IsFinite( Position.Z )
		&& float.IsFinite( Colour.X ) && float.IsFinite( Colour.Y ) && float.IsFinite( Colour.Z ) && ( Colour.X != 0f || Colour.Y != 0f || Colour.Z != 0f );
}

/// <summary>Something that owns point lights (a placed object with light dummies): asked once per frame for the lights it shows now.</summary>
public interface IPointLightSource
{
	/// <summary>Adds the lights that are on, in engine units, in a stable order.</summary>
	void AppendPointLights( List<PointLight> lights );
}

/// <summary>A source of one fixed light (the smoke-only <c>--debug-light</c> option and tests).</summary>
public sealed class FixedPointLightSource( PointLight light ) : IPointLightSource
{
	public void AppendPointLights( List<PointLight> lights ) => lights.Add( light );
}

/// <summary>
/// A light as the shaders read it: two vec4 in the uniform block. The first is the position and the squared radius, the second the colour and one over the squared radius, so
/// the shader's falloff is the original's <c>(r2 - d2) * k</c> with <c>k = 1 / r2</c> (the light record's +0x48 and +0x4C) without a division per pixel.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct PointLightGpu
{
	public SVector4 PositionRadiusSquared; // 16
	public SVector4 ColourInverseRadiusSquared; // 16
}

[InlineArray( PointLightSet.MaxPerDraw )]
internal struct PointLightGpuArray
{
	private PointLightGpu element;
}

/// <summary>
/// The uniform block of a draw that has point lights: the engine's block as it always is (<see cref="ObjectUniformBuffer.LightCount"/> tells how many) followed by the lights, at
/// byte 272, which is where a std140 <c>vec4 g_vPointLight[32]</c> after the block's last member starts. Only the first <c>sizeof(ObjectUniformBuffer) + 32 x count</c> bytes are
/// uploaded, so a draw with no lights uploads exactly what it did before the lights existed.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct LitObjectUniformBuffer
{
	public ObjectUniformBuffer Base;
	public PointLightGpuArray Lights;

	/// <summary>The bytes to upload for <paramref name="count"/> lights.</summary>
	public static uint UploadSize( int count ) => (uint)( Unsafe.SizeOf<ObjectUniformBuffer>() + count * Unsafe.SizeOf<PointLightGpu>() );
}

/// <summary>
/// The point-light falloff and the unit conversion, on the CPU: the shaders' <c>pointLights()</c> function is the same arithmetic in the same order, and the tests compare the two
/// through this class (no light means a sum of exactly zero, so a draw without lights shades as it did).
/// </summary>
public static class PointLightMath
{
	/// <summary>The radius of a light the original creates without being told one (the light record's +0x44 is 100.0 in model units).</summary>
	// [BIN:TPW-EXE:0x457E70 point light defaults] radius 100.0 (+0x44), its square (+0x48) and one over the square (+0x4C), colour white
	public const float OriginalRadiusModelUnits = 100f;

	/// <summary>
	/// Model units to engine units: 1 model unit is <see cref="ObjectPlacement.ModelScale"/> = 0.2 engine units, so the original's 100-unit radius is 20 engine units = 10 cells of 2
	/// engine units (a cell is 10 model units).
	/// </summary>
	public static float ModelUnitsToEngine( float modelUnits ) => modelUnits * ObjectPlacement.ModelScale;

	/// <summary>The smallest radius a light can have: one model unit (0.2 engine units). A smaller radius would make 1 / r2 overflow and the falloff NaN.</summary>
	public const float MinimumRadiusModelUnits = 1f;

	/// <summary>The smallest radius in engine units.</summary>
	public static float MinimumRadius => ModelUnitsToEngine( MinimumRadiusModelUnits );

	/// <summary>
	/// The bound on each channel of a light colour, 4 either way. The original keeps the colour as unclamped floats (0x4587E0, 0x458890: an over-bright light and a negative, darkening one are
	/// possible), so OpenTPW does not clamp to 0..1; the bound only keeps a huge operand (SETLIGHT 32767) from reaching the shader as a number of thousands.
	/// </summary>
	public const float ColourBound = 4f;

	/// <summary>The default radius in engine units (20, ten cells).</summary>
	public static float DefaultRadius => ModelUnitsToEngine( OriginalRadiusModelUnits );

	/// <summary>
	/// The share of a light's colour a surface point at squared distance <paramref name="distanceSquared"/> receives: <c>(r2 - d2) x (1 / r2)</c>, 1 at the centre, 0 at the radius and
	/// beyond (the original skips a vertex when <c>d2 >= r2</c>, 0x5742D9..0x5742DB).
	/// </summary>
	// [BIN:TPW-EXE:0x5741B0 point term] the vertex's squared distance to the light is compared with +0x48 and a vertex at or beyond it is skipped; else (+0x48 - d2) x +0x4C scales the light colour added to the vertex colour (0x5742E1..0x574382); there is no normal term
	public static float Falloff( float distanceSquared, float radius )
	{
		var radiusSquared = radius * radius;
		var share = ( radiusSquared - distanceSquared ) * ( 1f / radiusSquared );
		return share > 0f ? share : 0f;
	}

	/// <summary>The light as the shaders get it.</summary>
	internal static PointLightGpu ToGpu( in PointLight light )
	{
		var radiusSquared = light.Radius * light.Radius;
		return new PointLightGpu
		{
			PositionRadiusSquared = new SVector4( light.Position, radiusSquared ),
			ColourInverseRadiusSquared = new SVector4( light.Colour, 1f / radiusSquared )
		};
	}

	/// <summary>The colour the lights add at <paramref name="position"/>: what the shaders' <c>pointLights()</c> returns, summed in the lights' order.</summary>
	internal static SVector3 Sum( ReadOnlySpan<PointLightGpu> lights, SVector3 position )
	{
		var sum = SVector3.Zero;
		foreach ( var light in lights )
		{
			var offset = position - new SVector3( light.PositionRadiusSquared.X, light.PositionRadiusSquared.Y, light.PositionRadiusSquared.Z );
			var share = ( light.PositionRadiusSquared.W - SVector3.Dot( offset, offset ) ) * light.ColourInverseRadiusSquared.W;
			sum += new SVector3( light.ColourInverseRadiusSquared.X, light.ColourInverseRadiusSquared.Y, light.ColourInverseRadiusSquared.Z ) * ( share > 0f ? share : 0f );
		}
		return sum;
	}
}

/// <summary>
/// The point lights of a level for one frame. Sources register once (a placed object with light dummies); <see cref="Gather"/> collects what they show at the start of the frame
/// and drops lights whose sphere lies wholly outside the view; <see cref="Select"/> then hands each draw the nearest few that can reach it. The draw's list rides in its own
/// uniform upload (<see cref="LitObjectUniformBuffer"/>), so a light costs 32 bytes in a draw it touches and nothing in a draw it does not; with no light in view a draw does no
/// light work at all.
/// </summary>
/// <remarks>
/// The order is deterministic: by the squared distance from the light to the draw's world box (0 inside it), then the squared distance to the camera, then the order the
/// sources gave them. A large draw (the ground is one mesh) therefore gets the lights nearest the camera among those in view. At most <see cref="MaxPerDraw"/> lights
/// reach one draw; the rest of a crowded scene is simply not lit by the farther ones, the sphere reject keeps a light off every draw it cannot touch. Single-threaded, like the
/// rest of the render path, and allocation-free once warmed up.
/// </remarks>
public sealed class PointLightSet
{
	/// <summary>The lights one draw can carry: the shaders' array has 2 x 16 vec4 (512 bytes), at most 784 bytes per draw with all of them.</summary>
	public const int MaxPerDraw = 16;

	private readonly List<IPointLightSource> sources = new();
	private readonly List<PointLight> gathered = new();
	private PointLight[] lights = Array.Empty<PointLight>();
	private int count;
	private readonly Candidate[] candidates = new Candidate[MaxPerDraw];

	// The coarse XY grid of this frame's lights (built by Gather): every light is listed in each cell its sphere's XY box overlaps, so a draw only looks at the lights of the cells its own box covers.
	private const int MaximumGridCells = 64;
	private const int GridMinimumLights = 8;
	private const int GridQueryLimit = 16;
	private bool gridValid;
	private float gridMinX, gridMinY, gridInverseCell;
	private int gridWidth, gridHeight;
	private int[] cellStart = Array.Empty<int>();
	private int[] cellItems = Array.Empty<int>();
	private int[] cellFill = Array.Empty<int>();
	private int[] stamp = Array.Empty<int>();
	private int queryId;

	private struct Candidate
	{
		public float BoxDistanceSquared;
		public float CameraDistanceSquared;
		public int Index;
	}

	/// <summary>The lights in view this frame, after <see cref="Gather"/>.</summary>
	public int Count => count;

	/// <summary>The registered sources.</summary>
	public int SourceCount => sources.Count;

	/// <summary>The lights in view this frame.</summary>
	public ReadOnlySpan<PointLight> Lights => lights.AsSpan( 0, count );

	public void Add( IPointLightSource source )
	{
		ArgumentNullException.ThrowIfNull( source );
		if ( !sources.Contains( source ) )
			sources.Add( source );
	}

	public void Remove( IPointLightSource source )
	{
		sources.Remove( source );
		if ( sources.Count == 0 )
			count = 0;
	}

	/// <summary>
	/// Collects the lights every source shows now. <paramref name="viewProjection"/> (view times projection, as the shaders multiply) drops the lights whose sphere is wholly
	/// outside the view; null keeps them all. A light that is not <see cref="PointLight.IsUsable"/> is dropped.
	/// </summary>
	public void Gather( Matrix4x4? viewProjection )
	{
		count = 0;
		if ( sources.Count == 0 )
			return;
		gathered.Clear();
		foreach ( var source in sources )
			source.AppendPointLights( gathered );
		if ( gathered.Count == 0 )
			return;
		if ( lights.Length < gathered.Count )
			lights = new PointLight[Math.Max( gathered.Count, lights.Length * 2 )];
		Span<SVector4> planes = stackalloc SVector4[5];
		var cull = viewProjection is { } matrix && ViewPlanes( matrix, planes );
		foreach ( var light in gathered )
		{
			if ( !light.IsUsable || ( cull && IsOutside( planes, light ) ) )
				continue;
			lights[count++] = light;
		}
		BuildGrid();
	}

	/// <summary>Lists every light in the grid cells its sphere overlaps (a counting sort into flat arrays; nothing is allocated once the arrays are big enough). Few lights keep the plain scan.</summary>
	private void BuildGrid()
	{
		gridValid = false;
		if ( count < GridMinimumLights )
			return;
		float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
		for ( var index = 0; index < count; index++ )
		{
			var light = lights[index];
			minX = MathF.Min( minX, light.Position.X - light.Radius );
			minY = MathF.Min( minY, light.Position.Y - light.Radius );
			maxX = MathF.Max( maxX, light.Position.X + light.Radius );
			maxY = MathF.Max( maxY, light.Position.Y + light.Radius );
		}
		var cell = MathF.Max( MathF.Max( maxX - minX, maxY - minY ) / MaximumGridCells, 1f );
		gridMinX = minX;
		gridMinY = minY;
		gridInverseCell = 1f / cell;
		gridWidth = Math.Min( (int)( ( maxX - minX ) * gridInverseCell ) + 1, MaximumGridCells );
		gridHeight = Math.Min( (int)( ( maxY - minY ) * gridInverseCell ) + 1, MaximumGridCells );
		var cells = gridWidth * gridHeight;
		if ( cellStart.Length < cells + 1 )
		{
			cellStart = new int[cells + 1];
			cellFill = new int[cells + 1];
		}
		Array.Clear( cellStart, 0, cells + 1 );
		for ( var pass = 0; pass < 2; pass++ )
		{
			for ( var index = 0; index < count; index++ )
			{
				var (x0, y0, x1, y1) = CellRange( lights[index].Position.X - lights[index].Radius, lights[index].Position.Y - lights[index].Radius, lights[index].Position.X + lights[index].Radius, lights[index].Position.Y + lights[index].Radius );
				for ( var y = y0; y <= y1; y++ )
					for ( var x = x0; x <= x1; x++ )
					{
						var at = y * gridWidth + x;
						if ( pass == 0 )
							cellStart[at + 1]++;
						else
							cellItems[cellFill[at]++] = index;
					}
			}
			if ( pass == 0 )
			{
				for ( var at = 0; at < cells; at++ )
					cellStart[at + 1] += cellStart[at];
				if ( cellItems.Length < cellStart[cells] )
					cellItems = new int[cellStart[cells]];
				Array.Copy( cellStart, cellFill, cells );
			}
		}
		if ( stamp.Length < count )
			stamp = new int[Math.Max( count, stamp.Length * 2 )];
		gridValid = true;
	}

	private (int X0, int Y0, int X1, int Y1) CellRange( float minX, float minY, float maxX, float maxY ) => (
		Math.Clamp( (int)MathF.Floor( ( minX - gridMinX ) * gridInverseCell ), 0, gridWidth - 1 ),
		Math.Clamp( (int)MathF.Floor( ( minY - gridMinY ) * gridInverseCell ), 0, gridHeight - 1 ),
		Math.Clamp( (int)MathF.Floor( ( maxX - gridMinX ) * gridInverseCell ), 0, gridWidth - 1 ),
		Math.Clamp( (int)MathF.Floor( ( maxY - gridMinY ) * gridInverseCell ), 0, gridHeight - 1 ) );

	/// <summary>
	/// The lights for a draw whose world-space box is (<paramref name="boxMin"/>, <paramref name="boxMax"/>): the nearest <see cref="MaxPerDraw"/> that reach it, written to
	/// <paramref name="output"/> (at least <see cref="MaxPerDraw"/> long). Returns how many. With enough lights a coarse XY grid limits the look to the cells the box covers (the
	/// result is the same as scanning every light: the order below is total).
	/// </summary>
	internal int Select( SVector3 boxMin, SVector3 boxMax, SVector3 camera, Span<PointLightGpu> output )
	{
		var chosen = 0;
		if ( gridValid )
		{
			var (x0, y0, x1, y1) = CellRange( boxMin.X, boxMin.Y, boxMax.X, boxMax.Y );
			if ( ( x1 - x0 + 1 ) * ( y1 - y0 + 1 ) <= GridQueryLimit )
			{
				if ( ++queryId == int.MaxValue )
				{
					Array.Clear( stamp );
					queryId = 1;
				}
				for ( var y = y0; y <= y1; y++ )
					for ( var x = x0; x <= x1; x++ )
					{
						var at = y * gridWidth + x;
						for ( var item = cellStart[at]; item < cellStart[at + 1]; item++ )
						{
							var index = cellItems[item];
							if ( stamp[index] != queryId )
							{
								stamp[index] = queryId;
								Consider( index, boxMin, boxMax, camera, ref chosen );
							}
						}
					}
				return Emit( chosen, output );
			}
		}
		for ( var index = 0; index < count; index++ )
			Consider( index, boxMin, boxMax, camera, ref chosen );
		return Emit( chosen, output );
	}

	/// <summary>Selection by scanning every light, whatever the grid says (the reference the grid is tested against).</summary>
	internal int SelectByScan( SVector3 boxMin, SVector3 boxMax, SVector3 camera, Span<PointLightGpu> output )
	{
		var chosen = 0;
		for ( var index = 0; index < count; index++ )
			Consider( index, boxMin, boxMax, camera, ref chosen );
		return Emit( chosen, output );
	}

	private void Consider( int index, SVector3 boxMin, SVector3 boxMax, SVector3 camera, ref int chosen )
	{
		var light = lights[index];
		var boxDistance = BoxDistanceSquared( boxMin, boxMax, light.Position );
		if ( boxDistance >= light.Radius * light.Radius )
			return;
		var cameraDistance = SVector3.DistanceSquared( camera, light.Position );
		// insertion into the sorted candidates; a full list drops its worst when the newcomer beats it
		var at = chosen;
		while ( at > 0 && Before( boxDistance, cameraDistance, index, candidates[at - 1] ) )
			at--;
		if ( at >= MaxPerDraw )
			return;
		var last = chosen < MaxPerDraw ? chosen : MaxPerDraw - 1;
		for ( var move = last; move > at; move-- )
			candidates[move] = candidates[move - 1];
		candidates[at] = new Candidate { BoxDistanceSquared = boxDistance, CameraDistanceSquared = cameraDistance, Index = index };
		if ( chosen < MaxPerDraw )
			chosen++;
	}

	private int Emit( int chosen, Span<PointLightGpu> output )
	{
		for ( var slot = 0; slot < chosen; slot++ )
			output[slot] = PointLightMath.ToGpu( lights[candidates[slot].Index] );
		return chosen;
	}

	private static bool Before( float boxDistance, float cameraDistance, int index, in Candidate other )
	{
		if ( boxDistance != other.BoxDistanceSquared )
			return boxDistance < other.BoxDistanceSquared;
		if ( cameraDistance != other.CameraDistanceSquared )
			return cameraDistance < other.CameraDistanceSquared;
		return index < other.Index;
	}

	/// <summary>The squared distance from <paramref name="point"/> to the box, 0 inside it.</summary>
	internal static float BoxDistanceSquared( SVector3 min, SVector3 max, SVector3 point )
	{
		var clamped = SVector3.Clamp( point, min, max );
		return SVector3.DistanceSquared( clamped, point );
	}

	/// <summary>The world-space box of a model-space box under <paramref name="model"/>: the centre moved, the half extents widened by the absolute matrix (the exact box of the transformed corners of an affine matrix).</summary>
	internal static void TransformBox( SVector3 min, SVector3 max, in Matrix4x4 model, out SVector3 worldMin, out SVector3 worldMax )
	{
		var centre = SVector3.Transform( ( min + max ) * 0.5f, model );
		var half = ( max - min ) * 0.5f;
		var extent = new SVector3(
			MathF.Abs( model.M11 ) * half.X + MathF.Abs( model.M21 ) * half.Y + MathF.Abs( model.M31 ) * half.Z,
			MathF.Abs( model.M12 ) * half.X + MathF.Abs( model.M22 ) * half.Y + MathF.Abs( model.M32 ) * half.Z,
			MathF.Abs( model.M13 ) * half.X + MathF.Abs( model.M23 ) * half.Y + MathF.Abs( model.M33 ) * half.Z );
		worldMin = centre - extent;
		worldMax = centre + extent;
	}

	/// <summary>
	/// The left, right, bottom, top and near planes (normalised, pointing in) of a row-vector view-projection. The near plane is at z = -w, which holds for the 0..1 and the
	/// -1..1 depth range, like <see cref="Frustum"/>; the far plane is left out (the fog hides what is far).
	/// </summary>
	private static bool ViewPlanes( in Matrix4x4 m, Span<SVector4> planes )
	{
		planes[0] = new SVector4( m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41 );
		planes[1] = new SVector4( m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41 );
		planes[2] = new SVector4( m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42 );
		planes[3] = new SVector4( m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42 );
		planes[4] = new SVector4( m.M14 + m.M13, m.M24 + m.M23, m.M34 + m.M33, m.M44 + m.M43 );
		for ( var index = 0; index < planes.Length; index++ )
		{
			var length = new SVector3( planes[index].X, planes[index].Y, planes[index].Z ).Length();
			if ( !float.IsFinite( length ) || length <= 0f )
				return false;
			planes[index] /= length;
		}
		return true;
	}

	private static bool IsOutside( ReadOnlySpan<SVector4> planes, in PointLight light )
	{
		foreach ( var plane in planes )
			if ( plane.X * light.Position.X + plane.Y * light.Position.Y + plane.Z * light.Position.Z + plane.W < -light.Radius )
				return true;
		return false;
	}
}
