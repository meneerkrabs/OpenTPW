using System.Diagnostics;

namespace OpenTPW;

using SysMatrix = System.Numerics.Matrix4x4;
using SysVector2 = System.Numerics.Vector2;
using SysVector3 = System.Numerics.Vector3;
using SysVector4 = System.Numerics.Vector4;

/// <summary>
/// The terrain of a level drawn with the original's procedural textures (option PROCEDURALTEXTURING, docs/LND.md). Each frame it takes the cells the original's span selection lists
/// (<see cref="TerrainVisibleSpans"/>), matches them against the visible-cell cache (<see cref="ProceduralTextureCache"/>, 1500, 2000 or 2500 slots), builds the texture of every cell that took
/// a new slot (<see cref="ProceduralTerrainTexturer"/>) into the texture pages (<see cref="ProceduralTerrainAtlas"/>) and rewrites one dynamic mesh whose quads point into those pages. Cells the
/// procedural textures do not reach (<see cref="IsProcedural"/>) are not part of it: the terrain draws them with their static ground texture.
/// </summary>
/// <remarks>
/// Like the original, the frame builds every cell it lists before it draws; the browser does too, on its single thread.
/// </remarks>
internal sealed class ProceduralTerrain : IDisposable
{
	private readonly ModelHeightfield field;
	private readonly ProceduralTerrainRectangle rectangle;
	private readonly ProceduralTerrainTexturer texturer;
	private readonly IProceduralPages atlas;
	private readonly ProceduralTerrainAtlas? ownedAtlas;
	private readonly ProceduralTextureCache cache;
	private readonly int pageBase;

	// engine space corner positions of the whole heightfield ((CellCountX + 1) x (CellCountZ + 1))
	private readonly SysVector3[] corners;
	private readonly int cornerStride;

	// the cells the procedural textures reach, the lowest and highest height of the field (model units) and the answers of the render hole delegate for the current hole version
	private readonly bool[] eligible;
	private readonly float lowHeight, highHeight;
	private readonly byte[] holeState;
	private readonly SysVector3[] cellNormals;
	private ulong holeStateVersion;
	private readonly TerrainVisibleSpans spans = new();

	// per cache slot: the cell whose texture the slot waits to be built for (x | z << 16), or -1 when the slot holds the pixels of the cell it was last given
	private readonly int[] slotPending;

	private readonly int[] visibleKeys, visibleSlots, visibleCells;
	private readonly bool[] visibleBuild;
	private int visibleCount;
	private int previousCount = -1;
	private readonly int[] previousKeys, previousSlots;
	private readonly Vertex[] vertices;
	private int filledQuads;
	private static readonly uint[] IndexPattern = [0, 2, 3, 3, 1, 0];

	/// <summary>The mesh the last refresh wrote: four vertices per cell in view in the order of <see cref="VisibleCells"/>, then degenerate ones.</summary>
	internal Vertex[] Vertices => vertices;

	/// <summary>The cells (x, z) in view at the last refresh, row by row.</summary>
	internal IEnumerable<(int X, int Z)> VisibleCells => Enumerable.Range( 0, visibleCount ).Select( index => (visibleCells[index] & 0xFFFF, visibleCells[index] >> 16) );

	/// <summary>The spans of the last refresh (rows, columns and the farthest corner distance, <see cref="TerrainVisibleSpans.FarthestCorner"/>).</summary>
	internal TerrainVisibleSpans Spans => spans;

	/// <summary>
	/// The distance the original's rays reach in original units (<c>0x8BD4C0</c>, set by FIRSTPERSONVIEWDISTANCE: 200, 325, 450 or 700); a ray's ground point is at most half of it from the camera.
	/// </summary>
	public float ViewDistance { get; set; } = 700f;

	/// <summary>The field of view of the span selection's rays in degrees: 90, the value of <c>[0x87AF58]</c> in the park camera (<c>0x42A990</c> to <c>0x578BE0</c>), whose half-height tangent is 1. OpenTPW's camera is a narrower one (about 53 degrees, a tangent of 0.5), so the rays reach twice as far to each side as the screen does.</summary>
	public const float SpanFieldOfView = 90f;

	/// <summary>Cells the last refresh drew with a procedural texture.</summary>
	public int DrawnCells { get; private set; }

	/// <summary>Cell textures built since this terrain was made.</summary>
	public long Builds { get; private set; }

	/// <summary>Milliseconds spent building cell textures (the texturer and the upload) since this terrain was made.</summary>
	public double BuildMilliseconds { get; private set; }

	/// <summary>The part of <see cref="BuildMilliseconds"/> the texturer took, without the upload.</summary>
	public double TexturerMilliseconds { get; private set; }

	public ProceduralTextureCache Cache => cache;
	public ProceduralTerrainAtlas Atlas => ownedAtlas ?? throw new InvalidOperationException( "This terrain has no texture pages of its own." );
	public ProceduralTerrainRectangle Rectangle => rectangle;
	public int Size => texturer.Size;

	/// <summary>The model the refresh rewrites; null until <see cref="CreateModel"/>.</summary>
	public Model? Model { get; private set; }

	/// <param name="pageBase">The material slot of the first texture page; the pages follow it.</param>
	public ProceduralTerrain( ModelHeightfield field, LndFile lnd, ProceduralTerrainRectangle rectangle, ProceduralTerrainSettings settings, int pageBase,
		IProceduralPages? pages = null )
	{
		this.field = field;
		this.rectangle = rectangle;
		this.pageBase = pageBase;
		texturer = new ProceduralTerrainTexturer( lnd, settings.Size );
		atlas = pages ?? (ownedAtlas = new ProceduralTerrainAtlas( settings.Size, settings.CacheCells ));
		cache = new ProceduralTextureCache( settings.CacheCells );
		slotPending = new int[settings.CacheCells];
		Array.Fill( slotPending, -1 );
		visibleKeys = new int[ProceduralTextureCache.MaximumVisibleCells];
		visibleSlots = new int[visibleKeys.Length];
		visibleCells = new int[visibleKeys.Length];
		visibleBuild = new bool[visibleKeys.Length];
		previousKeys = new int[visibleKeys.Length];
		previousSlots = new int[visibleKeys.Length];
		cornerStride = field.CellCountX + 1;
		corners = new SysVector3[cornerStride * (field.CellCountZ + 1)];
		lowHeight = float.MaxValue;
		highHeight = float.MinValue;
		for ( var z = 0; z <= field.CellCountZ; z++ )
		{
			for ( var x = 0; x <= field.CellCountX; x++ )
			{
				var height = field.GetCornerHeight( x, z );
				lowHeight = Math.Min( lowHeight, height );
				highHeight = Math.Max( highHeight, height );
				corners[z * cornerStride + x] = OriginalParkPlacement.ToEngine( field, new SysVector3( x * field.CellSizeX, height, z * field.CellSizeZ ) );
			}
		}
		eligible = new bool[field.CellCountX * field.CellCountZ];
		for ( var z = 0; z < field.CellCountZ; z++ )
			for ( var x = 0; x < field.CellCountX; x++ )
				eligible[z * field.CellCountX + x] = rectangle.Contains( x, z ) && texturer.CanBuild( x, z ) && field.GetCellTextureSlot( x, z ) >= 0;
		holeState = new byte[eligible.Length];
		cellNormals = new SysVector3[eligible.Length];
		// the mesh holds every cell a frame can list (2500), which can be more than the cache has slots for
		vertices = new Vertex[ProceduralTextureCache.MaximumVisibleCells * 4];
	}

	/// <summary>The pages the material needs after its static slots.</summary>
	public IReadOnlyList<Texture> Pages => Atlas.Pages;

	/// <summary>
	/// Whether the procedural texture of a cell can be drawn: it lies inside the rectangle, the weight grid reaches it, and the heightfield has a surface there.
	/// </summary>
	public bool IsProcedural( int x, int z ) => (uint)x < (uint)field.CellCountX && (uint)z < (uint)field.CellCountZ && eligible[z * field.CellCountX + x];

	/// <summary>The model, every vertex of the cache's capacity in place and a fixed index list, to be rewritten by <see cref="Refresh"/>.</summary>
	public Model CreateModel( Material material )
	{
		var quads = vertices.Length / 4;
		var indices = new uint[quads * 6];
		// every quad is drawn as the cell's diagonal split: the vertices of an anti-diagonal cell are taken in a turned order (see WriteCell), so one index pattern serves both
		for ( var quad = 0; quad < quads; quad++ )
		{
			var start = (uint)(quad * 4);
			for ( var index = 0; index < 6; index++ )
				indices[quad * 6 + index] = start + IndexPattern[index];
		}
		// the vertices start on one point of the ground, so an unused quad is degenerate and inside the model's bounds
		var anchor = corners[0];
		for ( var index = 0; index < vertices.Length; index++ )
			vertices[index] = new Vertex { Position = anchor, Normal = new SysVector3( 0, 0, 1 ) };
		Model = new Model( (Vertex[])vertices.Clone(), indices, material, dynamicVertices: true );
		return Model;
	}

	/// <summary>
	/// Selects the cells the original lists for this view, looks them up in the cache (every call, as the original does every frame: a hit moves the slot in the ring), builds what is
	/// missing and rewrites the mesh when it changed. Returns whether the mesh was rewritten.
	/// </summary>
	/// <param name="view">The matrix from the mesh's engine space to the camera.</param>
	/// <param name="projection">The camera's projection: its two scale factors give the field of view and the aspect.</param>
	/// <param name="renderHole">Cells left out of the drawn ground (the pools extension); null: none.</param>
	/// <param name="holeVersion">A number that changes whenever <paramref name="renderHole"/> would answer differently; the answers are kept until it does.</param>
	public bool Refresh( in SysMatrix view, in SysMatrix projection, Func<int, int, bool>? renderHole, ulong holeVersion = 0 )
	{
		if ( holeVersion != holeStateVersion )
		{
			Array.Clear( holeState );
			holeStateVersion = holeVersion;
		}
		CollectVisible( view, projection, renderHole );
		cache.Resolve( visibleKeys.AsSpan( 0, visibleCount ), visibleSlots.AsSpan( 0, visibleCount ), visibleBuild.AsSpan( 0, visibleCount ) );
		// a key that took a slot owns it until its pixels are built; a slot taken twice in one frame (a view of more cells than the cache holds) keeps the later cell's, as in the original
		for ( var index = 0; index < visibleCount; index++ )
			if ( visibleBuild[index] )
				slotPending[visibleSlots[index]] = visibleCells[index];

		// build what the view needs before the mesh is drawn
		var changed = false;
		var started = Stopwatch.GetTimestamp();
		var built = 0;
		for ( var index = 0; index < visibleCount; index++ )
		{
			var slot = visibleSlots[index];
			var cell = slotPending[slot];
			if ( cell < 0 )
				continue;
			var begin = Stopwatch.GetTimestamp();
			texturer.BuildRgba( cell & 0xFFFF, cell >> 16, atlas.Scratch );
			var builtAt = Stopwatch.GetTimestamp();
			atlas.Upload( slot, atlas.Scratch );
			TexturerMilliseconds += Stopwatch.GetElapsedTime( begin, builtAt ).TotalMilliseconds;
			BuildMilliseconds += Stopwatch.GetElapsedTime( begin ).TotalMilliseconds;
			slotPending[slot] = -1;
			built++;
			Builds++;
			changed = true;
		}
		if ( built >= 100 )
			Log.Trace( $"Procedural terrain: {built} cell textures built in {Stopwatch.GetElapsedTime( started ).TotalMilliseconds:F1} ms ({visibleCount} cells listed, {cache.Misses} misses so far)." );

		// the mesh changes with the cells listed and with each texture that arrived
		if ( !changed && previousCount == visibleCount )
		{
			var same = true;
			for ( var index = 0; index < visibleCount && same; index++ )
				same = previousKeys[index] == visibleKeys[index] && previousSlots[index] == visibleSlots[index];
			if ( same )
				return false;
		}
		WriteMesh();
		return true;
	}

	/// <summary>
	/// The cells the original lists for the view (<c>0x56F090</c>): the rows and columns of its span selection (<see cref="TerrainVisibleSpans"/>) with a surface, no render hole and a place in the rectangle,
	/// row by row, up to <see cref="ProceduralTextureCache.MaximumVisibleCells"/>.
	/// </summary>
	private void CollectVisible( in SysMatrix view, in SysMatrix projection, Func<int, int, bool>? renderHole )
	{
		visibleCount = 0;
		// the original's rays first; a view that lists more cells than the cache holds (only the wide zoom range reaches it) takes narrower rays, down to nearly the screen's own field of view (the
		// original's tangent is twice the screen's), so the cells would not push each other out of the cache every frame and the cap of a frame does not cut the far rows of the picture
		var screen = Math.Abs( 1 / projection.M22 );
		foreach ( var tangent in (ReadOnlySpan<float>)[1f, 0.75f, 0.625f, 0.5625f] )
		{
			var fieldOfView = tangent == 1f ? SpanFieldOfView : (float)(2 * Math.Atan( Math.Max( tangent * 2 * screen, screen ) ) * 180 / Math.PI);
			if ( !ComputeSpans( view, projection, fieldOfView ) )
			{
				visibleCount = 0;
				return;
			}
			ListSpanCells( renderHole );
			if ( visibleCount <= cache.Capacity )
				return;
		}
	}

	private void ListSpanCells( Func<int, int, bool>? renderHole )
	{
		visibleCount = 0;
		var countX = field.CellCountX;
		for ( var z = Math.Max( 0, spans.MinRow ); z < spans.MaxRow && z < field.CellCountZ && visibleCount < visibleKeys.Length; z++ )
		{
			var end = Math.Min( (int)spans.End[z], countX );
			for ( var x = Math.Max( 0, (int)spans.Start[z] ); x < end && visibleCount < visibleKeys.Length; x++ )
			{
				var cell = z * countX + x;
				if ( !eligible[cell] )
					continue;
				if ( renderHole != null )
				{
					if ( holeState[cell] == 0 )
						holeState[cell] = renderHole( x, z ) ? (byte)2 : (byte)1;
					if ( holeState[cell] == 2 )
						continue;
				}
				var key = ProceduralTextureCache.KeyOf( x, z, rectangle.X0, rectangle.Y0 );
				if ( key < 0 )
					continue;
				visibleKeys[visibleCount] = key;
				visibleCells[visibleCount] = x | (z << 16);
				visibleCount++;
			}
		}
	}

	/// <summary>
	/// The spans of the camera of <paramref name="view"/> and <paramref name="projection"/>: the camera's position and axes in the heightfield's own space (the engine space of the mesh undone: x, height, z),
	/// the field of view and the aspect word of the projection.
	/// </summary>
	private bool ComputeSpans( in SysMatrix view, in SysMatrix projection, float fieldOfView )
	{
		if ( !SysMatrix.Invert( view, out var world ) || projection.M11 == 0 || projection.M22 == 0 )
			return false;
		// the camera's axes in the mesh's space: right and up are the view's x and y; forward is the view axis the projection puts in front of the camera (positive w)
		var right = SysVector3.Normalize( SysVector3.TransformNormal( SysVector3.UnitX, world ) );
		var up = SysVector3.Normalize( SysVector3.TransformNormal( SysVector3.UnitY, world ) );
		var forward = SysVector4.Transform( new SysVector4( 0, 0, -1, 1 ), projection ).W > 0 ? -SysVector3.UnitZ : SysVector3.UnitZ;
		forward = SysVector3.Normalize( SysVector3.TransformNormal( forward, world ) );
		var eye = world.Translation;
		var origin = OriginalParkPlacement.GetOrigin( field );
		const float scale = OriginalParkPlacement.ModelScale;
		// engine (x, y, z) is model (x, z, y) scaled and centred (OriginalParkPlacement.ToEngine); directions only swap the axes
		static (float, float, float) Axis( SysVector3 v ) => (v.X, v.Z, v.Y);
		var (rx, ry, rz) = Axis( right );
		var (ux, uy, uz) = Axis( up );
		var (fx, fy, fz) = Axis( forward );
		Span<float> camera = [(eye.X + origin.X) / scale, eye.Z / scale, (eye.Y + origin.Y) / scale];
		Span<float> matrix = [rx, ry, rz, ux, uy, uz, fx, fy, fz];
		// the aspect word is the display's height over its width, which the projection's two scale factors give (M11 / M22)
		var aspect = Math.Abs( projection.M11 ) / Math.Abs( projection.M22 );
		// the option's distance (times the preset's view distance scale); only when the top edge of OpenTPW's own screen would reach the field's lowest point farther than half of it (the wide zoom range,
		// [EXT:wide-zoom], puts the camera higher than the original's ordinary ones, whose limit would then leave the ground at the screen's far edge undrawn) it is the distance that edge needs, with a margin
		var maxDistance = ViewDistance * CompatibilityRuntime.RenderQuality.ViewDistanceScale;
		var topRayDescent = fy + Math.Abs( 1 / projection.M22 ) * uy;
		if ( topRayDescent < -1e-4f && camera[1] > lowHeight )
			maxDistance = Math.Max( maxDistance, 2.2f * (camera[1] - lowHeight) / -topRayDescent );
		return spans.Compute( camera, matrix, lowHeight, highHeight, fieldOfView, aspect, maxDistance,
			0, 0, field.CellCountX, field.CellCountZ, field.CellSizeX, field.CellSizeZ );
	}

	// the mesh: one quad per cell in view in the order of the list; a cell's texture coordinates run from the first to the last texel's centre of its tile (ProceduralTerrainAtlas),
	// u along x and v along z, the tile's first row at the cell's z edge ([BIN:TPW-EXE:0x56F670 cell corners] corner (x, z) takes the tile's (0, 0), (x, z + 1) its (0, 1)); turn and mirror
	// bits of the cell word do not apply ([BIN:TPW-EXE:0x46DED0] clears them while the texturing is on). The terrain shader flips v, so the page's v goes in as 1 - v.
	private void WriteMesh()
	{
		for ( var index = 0; index < visibleCount; index++ )
		{
			var cell = visibleCells[index];
			var x = cell & 0xFFFF;
			var z = cell >> 16;
			WriteCell( index * 4, x, z, visibleSlots[index] );
			previousKeys[index] = visibleKeys[index];
			previousSlots[index] = visibleSlots[index];
		}
		previousCount = visibleCount;
		DrawnCells = visibleCount;
		// the quads that held a cell last time and hold none now are made degenerate on a point of the first quad (or of the ground when nothing is in view); the quads past them already are
		var anchor = visibleCount > 0 ? vertices[0].Position : corners[0];
		for ( var index = visibleCount * 4; index < Math.Max( visibleCount, filledQuads ) * 4; index++ )
			vertices[index] = new Vertex { Position = anchor, Normal = new SysVector3( 0, 0, 1 ) };
		filledQuads = visibleCount;
		if ( Model != null )
		{
			Model.DrawIndexLimit = (uint)(visibleCount * 6);
			Model.UpdateVertices( vertices );
		}
	}

	private void WriteCell( int first, int x, int z, int slot )
	{
		var c00 = corners[z * cornerStride + x];
		var c10 = corners[z * cornerStride + x + 1];
		var c01 = corners[(z + 1) * cornerStride + x];
		var c11 = corners[(z + 1) * cornerStride + x + 1];
		var normal = cellNormals[z * field.CellCountX + x];
		if ( normal == default )
		{
			normal = SysVector3.Normalize( SysVector3.Cross( c10 - c00, c01 - c00 ) );
			if ( normal.Z < 0 )
				normal = -normal;
			cellNormals[z * field.CellCountX + x] = normal;
		}
		Span<SysVector3> positions = [c00, c10, c01, c11];
		Span<SysVector2> coordinates = stackalloc SysVector2[4];
		var (u0, v0, u1, v1) = atlas.CoordinatesOf( slot );
		coordinates[0] = new SysVector2( u0, 1 - v0 );
		coordinates[1] = new SysVector2( u1, 1 - v0 );
		coordinates[2] = new SysVector2( u0, 1 - v1 );
		coordinates[3] = new SysVector2( u1, 1 - v1 );
		var textureIndex = pageBase + atlas.PageOf( slot );
		// a cell that splits along its other diagonal takes its corners in a turned order: slot 0 is (x + 1, z), 1 is (x + 1, z + 1), 2 is (x, z) and 3 is (x, z + 1), which the fixed index pattern 0 2 3, 3 1 0 turns into the triangles 0 2 1, 1 2 3
		ReadOnlySpan<int> straight = [0, 1, 2, 3];
		ReadOnlySpan<int> turned = [1, 3, 0, 2];
		var order = field.SplitsAlongAntiDiagonal( x, z ) ? turned : straight;
		for ( var corner = 0; corner < 4; corner++ )
		{
			var source = order[corner];
			vertices[first + corner] = new Vertex { Position = positions[source], Normal = normal, TexCoords = coordinates[source], TexIndex = textureIndex, MatFlags = TextureSlotAddressing.ClampFlag };
		}
	}

	public void Dispose() => ownedAtlas?.Dispose();
}
