namespace OpenTPW;

/// <summary>Where the cache's cell textures live: the pages the mesh points into (<see cref="ProceduralTerrainAtlas"/>, or a stand-in in tests).</summary>
internal interface IProceduralPages
{
	/// <summary>The page a slot lives on.</summary>
	int PageOf( int slot );

	/// <summary>The texture coordinates a cell takes from its slot, (u0, v0) to (u1, v1), v running down the page.</summary>
	(float U0, float V0, float U1, float V1) CoordinatesOf( int slot );

	/// <summary>Writes a slot's N × N RGBA texels.</summary>
	void Upload( int slot, byte[] rgba );

	/// <summary>A scratch buffer of one tile for the texturer.</summary>
	byte[] Scratch { get; }
}

/// <summary>
/// The texture pages that hold the visible-cell cache's textures (<see cref="ProceduralTextureCache"/>): one N × N tile per cache slot, packed edge to edge in pages of at most
/// 2048 texels a side, with no gutter. The original packs its small textures into shared pages too (docs/LND.md) and takes each texture's coordinates from the texel centres
/// of its first and last texel, so a tile's edge texel is sampled exactly and its neighbour tile never bleeds into it.
/// </summary>
// [BIN:TPW-EXE:0x0058F190 page packer] the engine packs textures into shared pages; [BIN:TPW-EXE:0x00590350 page placement] the texture's coordinates start half a texel inside it, (x + 0.5) / page width, and span (w - 1) / page width (scale 1, set at 0x58D170)
internal sealed class ProceduralTerrainAtlas : IProceduralPages, IDisposable
{
	/// <summary>The most texels along a page's side.</summary>
	public const int PageSide = 2048;

	private readonly Texture[] pages;
	private readonly int tilesPerRow;
	private readonly int tilesPerPage;
	private readonly byte[] tile;

	public ProceduralTerrainAtlas( int tileSize, int slots )
	{
		if ( tileSize is not (16 or 32 or 64) )
			throw new ArgumentOutOfRangeException( nameof( tileSize ), "Procedural textures are 16, 32 or 64 texels." );
		if ( slots < 1 )
			throw new ArgumentOutOfRangeException( nameof( slots ), "An atlas needs at least one tile." );
		TileSize = tileSize;
		Slots = slots;
		tilesPerRow = PageSide / tileSize;
		tilesPerPage = tilesPerRow * tilesPerRow;
		tile = new byte[tileSize * tileSize * 4];
		var pageCount = (slots + tilesPerPage - 1) / tilesPerPage;
		pages = new Texture[pageCount];
		for ( var page = 0; page < pageCount; page++ )
		{
			var inPage = Math.Min( tilesPerPage, slots - page * tilesPerPage );
			var rows = (inPage + tilesPerRow - 1) / tilesPerRow;
			pages[page] = Texture.CreatePage( PageSide, rows * tileSize );
		}
	}

	/// <summary>Texels along a tile's side (N).</summary>
	public int TileSize { get; }

	/// <summary>Tiles the atlas holds, one per cache slot.</summary>
	public int Slots { get; }

	/// <summary>The pages, in slot order (slot / tiles per page).</summary>
	public IReadOnlyList<Texture> Pages => pages;

	/// <summary>The GPU bytes the pages hold.</summary>
	public long Bytes => pages.Sum( page => (long)page.Width * page.Height * 4 );

	/// <summary>The page a slot lives on.</summary>
	public int PageOf( int slot ) => slot / tilesPerPage;

	/// <summary>The pages a cache of <paramref name="slots"/> tiles of <paramref name="tileSize"/> texels needs.</summary>
	public static int PageCountFor( int tileSize, int slots )
	{
		var perRow = PageSide / tileSize;
		return (slots + perRow * perRow - 1) / (perRow * perRow);
	}

	/// <summary>The texel the tile of <paramref name="slot"/> starts at within its page.</summary>
	public (int X, int Y) TileOrigin( int slot )
	{
		var index = slot % tilesPerPage;
		return (index % tilesPerRow * TileSize, index / tilesPerRow * TileSize);
	}

	/// <summary>
	/// The texture coordinates a cell takes from its slot, as the original's page placement gives them: the first texel's centre to the last texel's centre, (u0, v0) to (u1, v1), v running down the page
	/// as the tile's rows do.
	/// </summary>
	public (float U0, float V0, float U1, float V1) CoordinatesOf( int slot )
	{
		var page = pages[PageOf( slot )];
		var (x, y) = TileOrigin( slot );
		return ((x + 0.5f) / page.Width, (y + 0.5f) / page.Height, (x + TileSize - 0.5f) / page.Width, (y + TileSize - 0.5f) / page.Height);
	}

	/// <summary>Writes a tile's N × N RGBA texels (rows top to bottom, as <see cref="ProceduralTerrainTexturer.BuildRgba"/> makes them) into the slot's place.</summary>
	public void Upload( int slot, byte[] rgba )
	{
		var (x, y) = TileOrigin( slot );
		pages[PageOf( slot )].UpdateRegion( x, y, TileSize, TileSize, rgba );
	}

	/// <summary>A scratch buffer of one tile for the texturer, reused by every build.</summary>
	public byte[] Scratch => tile;

	public void Dispose()
	{
		foreach ( var page in pages )
			page.ReleaseOwned();
	}
}
