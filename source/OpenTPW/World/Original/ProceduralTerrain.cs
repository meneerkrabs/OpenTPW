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
	private readonly byte[] holeState;
	// the answers before the last change of the hole version (2 = hole): a cell that was a hole and is not any more is dirty, as the original's restore of a cleared cell sets the dirty bit 0x100 on its word
	private readonly byte[] previousHoleState;
	private readonly HashSet<int> dirtyCells = new();
	// the art of the cells whose word has bit 0x80 (the path tiles): composited over the cell's base when it is built, and its turn bits turn the cell's texture coordinates
	private readonly ProceduralCellArt?[] cellArt;
	private int artCells;
	private readonly byte[] baseScratch;
	private readonly bool[] visibleDirty = new bool[ProceduralTextureCache.MaximumVisibleCells];
	private readonly SysVector3[] cellNormals;
	private ulong holeStateVersion;
	private readonly TerrainViewSpans viewSpans;
	private TerrainVisibleSpans spans => viewSpans.Spans;
	private readonly TerrainListGate gate = new();

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

	/// <summary>The span selection's inputs and results (shared with the level's far plane).</summary>
	internal TerrainViewSpans ViewSpans => viewSpans;

	/// <summary>Cells the last refresh drew with a procedural texture.</summary>
	public int DrawnCells { get; private set; }

	/// <summary>Cell textures built since this terrain was made.</summary>
	public long Builds { get; private set; }

	/// <summary>Milliseconds spent building cell textures (the texturer and the upload) since this terrain was made.</summary>
	public double BuildMilliseconds { get; private set; }

	/// <summary>The part of <see cref="BuildMilliseconds"/> the texturer took, without the upload.</summary>
	public double TexturerMilliseconds { get; private set; }

	/// <summary>
	/// A change of the scene's cells or nodes: the original's setters (<c>0x576574</c>, <c>0x576603</c>, <c>0x5767CA</c>, <c>0x473D9E</c>, <c>0x477177</c>, <c>0x477FAE</c>, <c>0x4786A7</c>) set bit 0x80000000 of <c>[0x8BD508]</c>, which the frame
	/// function keeps (see <see cref="TerrainListGate"/>), so the cell list is built and looked up in every frame from then on.
	/// </summary>
	public void MarkSceneChanged() => gate.MarkSceneChanged();

	public ProceduralTextureCache Cache => cache;

	/// <summary>The texturer that builds this level's cell textures; the floor composites take their bases from it (<see cref="ObjectFloorBase"/>). Not thread-safe: use it on the thread that refreshes the terrain.</summary>
	internal ProceduralTerrainTexturer Texturer => texturer;
	public ProceduralTerrainAtlas Atlas => ownedAtlas ?? throw new InvalidOperationException( "This terrain has no texture pages of its own." );
	public ProceduralTerrainRectangle Rectangle => rectangle;
	public int Size => texturer.Size;

	/// <summary>The model the refresh rewrites; null until <see cref="CreateModel"/>.</summary>
	public Model? Model { get; private set; }

	/// <param name="pageBase">The material slot of the first texture page; the pages follow it.</param>
	public ProceduralTerrain( ModelHeightfield field, LndFile lnd, ProceduralTerrainRectangle rectangle, ProceduralTerrainSettings settings, int pageBase,
		IProceduralPages? pages = null, TerrainViewSpans? viewSpans = null )
	{
		this.field = field;
		this.rectangle = rectangle;
		this.viewSpans = viewSpans ?? new TerrainViewSpans( field );
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
		for ( var z = 0; z <= field.CellCountZ; z++ )
		{
			for ( var x = 0; x <= field.CellCountX; x++ )
			{
				var height = field.GetCornerHeight( x, z );
				corners[z * cornerStride + x] = OriginalParkPlacement.ToEngine( field, new SysVector3( x * field.CellSizeX, height, z * field.CellSizeZ ) );
			}
		}
		eligible = new bool[field.CellCountX * field.CellCountZ];
		for ( var z = 0; z < field.CellCountZ; z++ )
			for ( var x = 0; x < field.CellCountX; x++ )
				eligible[z * field.CellCountX + x] = rectangle.Contains( x, z ) && texturer.CanBuild( x, z ) && field.GetCellTextureSlot( x, z ) >= 0;
		holeState = new byte[eligible.Length];
		cellArt = new ProceduralCellArt?[eligible.Length];
		baseScratch = new byte[texturer.TextureBytes];
		previousHoleState = new byte[eligible.Length];
		cellNormals = new SysVector3[eligible.Length];
		// the mesh holds every cell a frame can list (2500), which can be more than the cache has slots for
		vertices = new Vertex[ProceduralTextureCache.MaximumVisibleCells * 4];
		// the level's scene nodes were just added (0x5762EE, 0x576574): the original's list is built from the first frame on
		MarkSceneChanged();
	}

	/// <summary>The pages the material needs after its static slots.</summary>
	public IReadOnlyList<Texture> Pages => Atlas.Pages;

	/// <summary>
	/// Whether the procedural texture of a cell can be drawn: it lies inside the rectangle, the weight grid reaches it, and the heightfield has a surface there.
	/// </summary>
	public bool IsProcedural( int x, int z ) => (uint)x < (uint)field.CellCountX && (uint)z < (uint)field.CellCountZ && eligible[z * field.CellCountX + x];

	/// <summary>Cells that carry art now.</summary>
	public int ArtCells => artCells;

	/// <summary>The art the cell (<paramref name="x"/>, <paramref name="z"/>) carries, or null.</summary>
	internal ProceduralCellArt? ArtOf( int x, int z ) => (uint)x < (uint)field.CellCountX && (uint)z < (uint)field.CellCountZ ? cellArt[z * field.CellCountX + x] : null;

	/// <summary>
	/// Gives a cell the art of its word, or takes it away (<paramref name="art"/> null: the cell's word is its ground word again). The original's writer (<c>0x46DF50</c>) sets the dirty bit 0x100 of the word
	/// for a path tile and for a restored cell alike, and <c>0x572440</c> builds the cell again when it hits a cache slot whose recorded word had that bit, so a cell whose art changed is built again the next time
	/// it is listed, and a cell outside the view keeps the change until then. A cell whose art builds the same texture (<see cref="ProceduralCellArt.Same"/>) is not marked: its pixels would come out the same.
	/// </summary>
	// [BIN:TPW-EXE:0x0046DF50 dirty bit] the tile write ORs 0x180, the restore sets byte 1 bit 0 (0x100) and the hole write ORs 0x101; 0x56F090 clears the bit of every cell it lists and records the word it had, 0x572440 builds a hit again when the recorded word has it
	internal bool SetCellArt( int x, int z, ProceduralCellArt? art )
	{
		if ( (uint)x >= (uint)field.CellCountX || (uint)z >= (uint)field.CellCountZ )
			return false;
		var index = z * field.CellCountX + x;
		if ( ProceduralCellArt.Same( cellArt[index], art ) )
		{
			// the pixels stay; the art object may be a new one with the same texels
			if ( art != null )
				cellArt[index] = art;
			return false;
		}
		artCells += (art != null ? 1 : 0) - (cellArt[index] != null ? 1 : 0);
		cellArt[index] = art;
		dirtyCells.Add( x | (z << 16) );
		MarkSceneChanged();
		return true;
	}

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
			// a cell no frame has asked about since an earlier change keeps what that change left
			for ( var cell = 0; cell < holeState.Length; cell++ )
				if ( holeState[cell] != 0 )
					previousHoleState[cell] = holeState[cell];
			Array.Clear( holeState );
			holeStateVersion = holeVersion;
			MarkSceneChanged();
		}
		if ( !gate.Frame( view ) )
			return false;
		CollectVisible( view, projection, renderHole );
		cache.Resolve( visibleKeys.AsSpan( 0, visibleCount ), visibleSlots.AsSpan( 0, visibleCount ), visibleBuild.AsSpan( 0, visibleCount ), visibleDirty.AsSpan( 0, visibleCount ) );
		// a dirty cell that was listed is rebuilt (a cache hit rebuilds when the recorded word had bit 0x100) and is clean again
		for ( var index = 0; index < visibleCount; index++ )
			if ( visibleDirty[index] )
				dirtyCells.Remove( visibleCells[index] );
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
			BuildCell( cell & 0xFFFF, cell >> 16, atlas.Scratch );
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
	/// The texture of a cell: its procedural base, with the art of its word composited over it when it has some (<c>0x572440</c> builds the cell through <c>0x55F780</c> with the descriptor <c>0x56F090</c> recorded).
	/// </summary>
	private void BuildCell( int x, int z, byte[] destination )
	{
		if ( cellArt[z * field.CellCountX + x] is not { } art )
		{
			texturer.BuildRgba( x, z, destination );
			return;
		}
		texturer.BuildRgba( x, z, baseScratch );
		ProceduralTerrainTexturer.Compose( baseScratch, texturer.Size, art.Rgba, art.Width, art.Height, (int)(art.Word & 0xFFFF), destination );
	}

	/// <summary>
	/// The cells the original lists for the view (<c>0x56F090</c>): the rows and columns of its span selection (<see cref="TerrainVisibleSpans"/>) with a surface, no render hole and a place in the rectangle,
	/// row by row, up to <see cref="ProceduralTextureCache.MaximumVisibleCells"/>.
	/// </summary>
	private void CollectVisible( in SysMatrix view, in SysMatrix projection, Func<int, int, bool>? renderHole )
	{
		visibleCount = 0;
		if ( !viewSpans.Update( view, Math.Abs( projection.M11 ) / Math.Abs( projection.M22 ), TerrainViewSpans.LooksAlongNegativeZ( projection ) ) )
			return;
		ListSpanCells( renderHole );
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
					{
						holeState[cell] = renderHole( x, z ) ? (byte)2 : (byte)1;
						// [BIN:TPW-EXE:0x0046DF50 word restore] a cell that stops being a hole took its backup word again with the dirty bit 0x100
						if ( holeState[cell] == 1 && previousHoleState[cell] == 2 )
							dirtyCells.Add( x | (z << 16) );
						previousHoleState[cell] = 0;
					}
					if ( holeState[cell] == 2 )
						continue;
				}
				var key = ProceduralTextureCache.KeyOf( x, z, rectangle.X0, rectangle.Y0 );
				if ( key < 0 )
					continue;
				visibleKeys[visibleCount] = key;
				visibleCells[visibleCount] = x | (z << 16);
				visibleDirty[visibleCount] = dirtyCells.Count > 0 && dirtyCells.Contains( x | (z << 16) );
				visibleCount++;
			}
		}
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
			WriteCell( index * 4, x, z, visibleSlots[index], cellArt[z * field.CellCountX + x]?.Word ?? 0 );
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

	private void WriteCell( int first, int x, int z, int slot, uint word )
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
		// [BIN:TPW-EXE:0x56F4F0 cell corners] the cell word's turn bits permute the texture corners each cell corner takes, for every cell the terrain draws; the procedural cells that have no art lost their turn bits (0x46DED0), and a path tile's stay, which is
		// what the composite's reading of the base undoes (ProceduralTerrainTexturer.BaseTexel): the base stays upright and the art turns
		Span<int> cornerOf = [TerrainCellTexture.Cell00, TerrainCellTexture.Cell10, TerrainCellTexture.Cell01, TerrainCellTexture.Cell11];
		if ( word != 0 )
		{
			Span<SysVector2> permuted = stackalloc SysVector2[4];
			for ( var corner = 0; corner < 4; corner++ )
			{
				var (u, v) = TerrainCellTexture.Coordinate( cornerOf[corner], word );
				permuted[corner] = new SysVector2( u == 0 ? u0 : u1, 1 - (v == 0 ? v0 : v1) );
			}
			permuted.CopyTo( coordinates );
		}
		var flags = TextureSlotAddressing.ClampFlag | (PathCellWord.HasArt( word ) ? TextureSlotAddressing.SlushFlag : 0u);
		// a cell that splits along its other diagonal takes its corners in a turned order: slot 0 is (x + 1, z), 1 is (x + 1, z + 1), 2 is (x, z) and 3 is (x, z + 1), which the fixed index pattern 0 2 3, 3 1 0 turns into the triangles 0 2 1, 1 2 3
		ReadOnlySpan<int> straight = [0, 1, 2, 3];
		ReadOnlySpan<int> turned = [1, 3, 0, 2];
		var order = field.SplitsAlongAntiDiagonal( x, z ) ? turned : straight;
		for ( var corner = 0; corner < 4; corner++ )
		{
			var source = order[corner];
			vertices[first + corner] = new Vertex { Position = positions[source], Normal = normal, TexCoords = coordinates[source], TexIndex = textureIndex, MatFlags = flags };
		}
	}

	public void Dispose() => ownedAtlas?.Dispose();
}
