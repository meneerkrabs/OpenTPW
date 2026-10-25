using System.Diagnostics;

namespace OpenTPW;

using SysMatrix = System.Numerics.Matrix4x4;
using SysVector2 = System.Numerics.Vector2;
using SysVector3 = System.Numerics.Vector3;
using SysVector4 = System.Numerics.Vector4;

/// <summary>
/// The terrain of a level drawn with the original's procedural textures (option PROCEDURALTEXTURING, docs/LND.md). Each frame it finds the heightfield cells in view, matches them against the
/// visible-cell cache (<see cref="ProceduralTextureCache"/>, 1500, 2000 or 2500 slots), builds the texture of every cell that took a new slot (<see cref="ProceduralTerrainTexturer"/>) into the
/// texture pages (<see cref="ProceduralTerrainAtlas"/>) and rewrites one dynamic mesh whose quads point into those pages. Cells outside the procedural rectangle (<see cref="ProceduralTerrainRectangle"/>)
/// are not part of it: the terrain draws them with their static ground texture.
/// </summary>
/// <remarks>
/// A cell whose texture is not built yet (the build time of a frame is used up; only in the browser) shows its static ground texture for the frames it waits: the original builds
/// every visible cell before it draws, and so does the desktop.
/// </remarks>
internal sealed class ProceduralTerrain : IDisposable
{
	/// <summary>
	/// The time a frame may spend building cell textures: unlimited on the desktop, where the original builds every visible cell before it draws, and a few milliseconds in
	/// the browser, whose single thread has to stay cooperative (a cell that waits shows its static ground texture).
	/// </summary>
	public static double BuildBudgetMilliseconds { get; set; } = OperatingSystem.IsBrowser() ? 4 : double.PositiveInfinity;

	private readonly ModelHeightfield field;
	private readonly ProceduralTerrainRectangle rectangle;
	private readonly ProceduralTerrainTexturer texturer;
	private readonly IProceduralPages atlas;
	private readonly ProceduralTerrainAtlas? ownedAtlas;
	private readonly ProceduralTextureCache cache;
	private readonly Func<int, int, int> staticSlotIndex;
	private readonly int pageBase;

	// engine space corner positions of the whole heightfield ((CellCountX + 1) x (CellCountZ + 1)) and the clip codes of the current frame
	private readonly SysVector3[] corners;
	private readonly byte[] codes;
	private readonly int cornerStride;

	// per cache slot: the cell whose texture the slot waits to be built for (x | z << 16), or -1 when the slot holds the pixels of the cell it was last given
	private readonly int[] slotPending;

	private readonly int[] visibleKeys, visibleSlots, visibleCells;
	private readonly bool[] visibleBuild;
	private int visibleCount;
	private int previousCount = -1;
	private readonly int[] previousKeys, previousSlots;
	private readonly bool[] previousBuilt;
	private readonly Vertex[] vertices;
	private int filledQuads;
	private SysMatrix lastView;
	private ulong lastHoleVersion;
	private bool hasView;

	/// <summary>The mesh the last refresh wrote: four vertices per cell in view in the order of <see cref="VisibleCells"/>, then degenerate ones.</summary>
	internal Vertex[] Vertices => vertices;

	/// <summary>The cells (x, z) in view at the last refresh, row by row.</summary>
	internal IEnumerable<(int X, int Z)> VisibleCells => Enumerable.Range( 0, visibleCount ).Select( index => (visibleCells[index] & 0xFFFF, visibleCells[index] >> 16) );

	/// <summary>Cells in the last frame's view that were not built yet when the mesh was written.</summary>
	public int PendingCells { get; private set; }

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

	/// <param name="staticSlotIndex">The material slot of the static ground texture of a cell (x, z), for a cell whose texture is not built yet.</param>
	/// <param name="pageBase">The material slot of the first texture page; the pages follow it.</param>
	public ProceduralTerrain( ModelHeightfield field, LndFile lnd, ProceduralTerrainRectangle rectangle, ProceduralTerrainSettings settings, Func<int, int, int> staticSlotIndex, int pageBase,
		IProceduralPages? pages = null )
	{
		this.field = field;
		this.rectangle = rectangle;
		this.staticSlotIndex = staticSlotIndex;
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
		previousBuilt = new bool[visibleKeys.Length];
		cornerStride = field.CellCountX + 1;
		corners = new SysVector3[cornerStride * (field.CellCountZ + 1)];
		codes = new byte[corners.Length];
		for ( var z = 0; z <= field.CellCountZ; z++ )
			for ( var x = 0; x <= field.CellCountX; x++ )
				corners[z * cornerStride + x] = OriginalParkPlacement.ToEngine( field, new SysVector3( x * field.CellSizeX, field.GetCornerHeight( x, z ), z * field.CellSizeZ ) );
		// the mesh holds every cell a frame can list (2500), which can be more than the cache has slots for
		vertices = new Vertex[ProceduralTextureCache.MaximumVisibleCells * 4];
	}

	/// <summary>The pages the material needs after its static slots.</summary>
	public IReadOnlyList<Texture> Pages => Atlas.Pages;

	/// <summary>
	/// Whether the procedural texture of a cell can be drawn: it lies inside the rectangle, the weight grid reaches it and the heightfield has a surface there.
	/// </summary>
	public bool IsProcedural( int x, int z ) => rectangle.Contains( x, z ) && texturer.CanBuild( x, z ) && field.GetCellTextureSlot( x, z ) >= 0;

	/// <summary>The model, every vertex of the cache's capacity in place and a fixed index list, to be rewritten by <see cref="Refresh"/>.</summary>
	public Model CreateModel( Material material )
	{
		var quads = vertices.Length / 4;
		var indices = new uint[quads * 6];
		// every quad is drawn as the cell's diagonal split: the vertices of an anti-diagonal cell are taken in a turned order (see WriteCell), so one index pattern serves both
		for ( var quad = 0; quad < quads; quad++ )
		{
			var start = (uint)(quad * 4);
			uint[] pattern = [0, 2, 3, 3, 1, 0];
			for ( var index = 0; index < 6; index++ )
				indices[quad * 6 + index] = start + pattern[index];
		}
		// the vertices start on one point of the ground, so an unused quad is degenerate and inside the model's bounds
		var anchor = corners[0];
		for ( var index = 0; index < vertices.Length; index++ )
			vertices[index] = new Vertex { Position = anchor, Normal = new SysVector3( 0, 0, 1 ) };
		Model = new Model( (Vertex[])vertices.Clone(), indices, material, dynamicVertices: true );
		return Model;
	}

	/// <summary>
	/// Finds the cells in view of <paramref name="modelViewProjection"/>, caches them and rewrites the mesh when the picture changed. Returns whether the mesh was rewritten.
	/// </summary>
	/// <param name="renderHole">Cells left out of the drawn ground (the pools extension); null: none.</param>
	/// <param name="holeVersion">A number that changes whenever <paramref name="renderHole"/> would answer differently.</param>
	public bool Refresh( in SysMatrix modelViewProjection, Func<int, int, bool>? renderHole, ulong holeVersion = 0 )
	{
		// the same view of the same cells with every texture built: nothing to find, look up or write (the cache would only see the same hits again)
		if ( hasView && PendingCells == 0 && holeVersion == lastHoleVersion && modelViewProjection == lastView )
			return false;
		lastView = modelViewProjection;
		lastHoleVersion = holeVersion;
		hasView = true;
		CollectVisible( modelViewProjection, renderHole );
		cache.Resolve( visibleKeys.AsSpan( 0, visibleCount ), visibleSlots.AsSpan( 0, visibleCount ), visibleBuild.AsSpan( 0, visibleCount ) );
		// a key that took a slot owns it until its pixels are built; a slot taken twice in one frame (a view of more cells than the cache holds) keeps the later cell's, as in the original
		for ( var index = 0; index < visibleCount; index++ )
			if ( visibleBuild[index] )
				slotPending[visibleSlots[index]] = visibleCells[index];

		// build what the view needs, as much as the frame's time allows (all of it on the desktop)
		var changed = false;
		var watch = Stopwatch.StartNew();
		var built = 0;
		PendingCells = 0;
		for ( var index = 0; index < visibleCount; index++ )
		{
			var slot = visibleSlots[index];
			var cell = slotPending[slot];
			if ( cell < 0 )
				continue;
			if ( built > 0 && watch.Elapsed.TotalMilliseconds > BuildBudgetMilliseconds )
			{
				PendingCells++;
				continue;
			}
			var started = Stopwatch.GetTimestamp();
			texturer.BuildRgba( cell & 0xFFFF, cell >> 16, atlas.Scratch );
			var builtAt = Stopwatch.GetTimestamp();
			atlas.Upload( slot, atlas.Scratch );
			TexturerMilliseconds += Stopwatch.GetElapsedTime( started, builtAt ).TotalMilliseconds;
			BuildMilliseconds += Stopwatch.GetElapsedTime( started ).TotalMilliseconds;
			slotPending[slot] = -1;
			built++;
			Builds++;
			changed = true;
		}
		if ( built >= 100 )
			Log.Trace( $"Procedural terrain: {built} cell textures built in {watch.Elapsed.TotalMilliseconds:F1} ms ({visibleCount} cells in view, {cache.Misses} misses so far)." );

		// the mesh changes with the cells in view and with each texture that arrived
		if ( !changed && PendingCells == 0 && previousCount == visibleCount )
		{
			var same = true;
			for ( var index = 0; index < visibleCount && same; index++ )
				same = previousKeys[index] == visibleKeys[index] && previousSlots[index] == visibleSlots[index] && previousBuilt[index] == (slotPending[visibleSlots[index]] < 0);
			if ( same )
				return false;
		}
		WriteMesh();
		return true;
	}

	/// <summary>The cells of the rectangle that have a surface, are no render hole and have a corner inside the view, row by row, up to <see cref="ProceduralTextureCache.MaximumVisibleCells"/>.</summary>
	private void CollectVisible( in SysMatrix modelViewProjection, Func<int, int, bool>? renderHole )
	{
		var x0 = Math.Max( 0, rectangle.X0 );
		var z0 = Math.Max( 0, rectangle.Y0 );
		var x1 = Math.Min( Math.Min( rectangle.X1, texturer.CellsX ), field.CellCountX - 1 );
		var z1 = Math.Min( Math.Min( rectangle.Y1, texturer.CellsY - 1 ), field.CellCountZ - 1 );
		// the clip planes as the view test of the model uses them (Frustum.IsOutside): the near plane at z = -w holds for both depth ranges, so nothing the GPU would draw is skipped
		for ( var z = z0; z <= z1 + 1; z++ )
		{
			for ( var x = x0; x <= x1 + 1; x++ )
			{
				var clip = SysVector4.Transform( corners[z * cornerStride + x], modelViewProjection );
				var code = 0;
				if ( clip.X < -clip.W ) code |= 1;
				if ( clip.X > clip.W ) code |= 2;
				if ( clip.Y < -clip.W ) code |= 4;
				if ( clip.Y > clip.W ) code |= 8;
				if ( clip.Z < -clip.W ) code |= 16;
				if ( clip.Z > clip.W ) code |= 32;
				codes[z * cornerStride + x] = (byte)code;
			}
		}
		visibleCount = 0;
		for ( var z = z0; z <= z1 && visibleCount < visibleKeys.Length; z++ )
		{
			for ( var x = x0; x <= x1 && visibleCount < visibleKeys.Length; x++ )
			{
				var corner = z * cornerStride + x;
				if ( (codes[corner] & codes[corner + 1] & codes[corner + cornerStride] & codes[corner + cornerStride + 1]) != 0 )
					continue;
				if ( !texturer.CanBuild( x, z ) || field.GetCellTextureSlot( x, z ) < 0 || renderHole?.Invoke( x, z ) == true )
					continue;
				var key = ProceduralTextureCache.KeyOf( x, z, rectangle.X0, rectangle.Y0 );
				if ( key < 0 )
					continue;
				visibleKeys[visibleCount] = key;
				visibleCells[visibleCount] = x | (z << 16);
				visibleCount++;
			}
		}
	}

	// the mesh: one quad per cell in view in the order of the list; a cell's texture coordinates run from the first to the last texel's centre of its tile (ProceduralTerrainAtlas),
	// u along x and v along z, the tile's first row at the cell's z edge ([BIN:TPW-EXE:0x56F670 cell corners] corner (x, z) takes the tile's (0, 0), (x, z + 1) its (0, 1)); turn and mirror
	// bits of the cell word do not apply ([BIN:TPW-EXE:0x46DED0] clears them while the texturing is on). The terrain shader flips v, so the page's v goes in as 1 - v.
	private void WriteMesh()
	{
		var drawn = 0;
		for ( var index = 0; index < visibleCount; index++ )
		{
			var cell = visibleCells[index];
			var x = cell & 0xFFFF;
			var z = cell >> 16;
			var built = slotPending[visibleSlots[index]] < 0;
			WriteCell( index * 4, x, z, built ? visibleSlots[index] : -1 );
			previousKeys[index] = visibleKeys[index];
			previousSlots[index] = visibleSlots[index];
			previousBuilt[index] = built;
			if ( built )
				drawn++;
		}
		previousCount = visibleCount;
		DrawnCells = drawn;
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
		var normal = SysVector3.Normalize( SysVector3.Cross( c10 - c00, c01 - c00 ) );
		if ( normal.Z < 0 )
			normal = -normal;
		Span<SysVector3> positions = [c00, c10, c01, c11];
		Span<SysVector2> coordinates = stackalloc SysVector2[4];
		int textureIndex;
		if ( slot >= 0 )
		{
			var (u0, v0, u1, v1) = atlas.CoordinatesOf( slot );
			coordinates[0] = new SysVector2( u0, 1 - v0 );
			coordinates[1] = new SysVector2( u1, 1 - v0 );
			coordinates[2] = new SysVector2( u0, 1 - v1 );
			coordinates[3] = new SysVector2( u1, 1 - v1 );
			textureIndex = pageBase + atlas.PageOf( slot );
		}
		else
		{
			// the static ground texture of the cell, turned and mirrored by its cell word as the static mesh does
			var word = field.GetCellWord( x, z );
			(coordinates[0].X, coordinates[0].Y) = TerrainCellTexture.Coordinate( TerrainCellTexture.Cell00, word );
			(coordinates[1].X, coordinates[1].Y) = TerrainCellTexture.Coordinate( TerrainCellTexture.Cell10, word );
			(coordinates[2].X, coordinates[2].Y) = TerrainCellTexture.Coordinate( TerrainCellTexture.Cell01, word );
			(coordinates[3].X, coordinates[3].Y) = TerrainCellTexture.Coordinate( TerrainCellTexture.Cell11, word );
			textureIndex = staticSlotIndex( x, z );
		}
		// a cell that splits along its other diagonal takes its corners in a turned order: slot 0 is (x + 1, z), 1 is (x + 1, z + 1), 2 is (x, z) and 3 is (x, z + 1), which the fixed index pattern 0 2 3, 3 1 0 turns into the triangles 0 2 1, 1 2 3
		ReadOnlySpan<int> straight = [0, 1, 2, 3];
		ReadOnlySpan<int> turned = [1, 3, 0, 2];
		var order = field.SplitsAlongAntiDiagonal( x, z ) ? turned : straight;
		for ( var corner = 0; corner < 4; corner++ )
		{
			var source = order[corner];
			vertices[first + corner] = new Vertex { Position = positions[source], Normal = normal, TexCoords = coordinates[source], TexIndex = textureIndex };
		}
	}

	public void Dispose() => ownedAtlas?.Dispose();
}
