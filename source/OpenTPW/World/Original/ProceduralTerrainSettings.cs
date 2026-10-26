namespace OpenTPW;

/// <summary>
/// What the options PROCEDURALTEXTURING and PROCEDURALTEXTURECACHE give a level load (docs/LND.md): the texture size N of the procedural terrain (0 = off, 16, 32 or 64) and the number of
/// cell textures the visible-cell cache keeps (1500, 2000 or 2500). Like the original's option apply, the values are read when a level is loaded, not while it runs.
/// </summary>
public readonly record struct ProceduralTerrainSettings( int Size, int CacheCells )
{
	/// <summary>The smallest cache: PROCEDURALTEXTURECACHE 0.</summary>
	public const int SmallCache = 1500;

	/// <summary>RAM below which the original builds 16 × 16 textures only (0x572110: 0x3000000 bytes).</summary>
	public const long SmallestMemoryBytes = 0x3000000;

	/// <summary>RAM below which the original builds no 64 × 64 textures (0x572110: 0x4000000 bytes).</summary>
	public const long LargeTextureMemoryBytes = 0x4000000;

	public bool Enabled => Size > 0;

	// [BIN:TPW-EXE:0x0044D926 option apply] PROCEDURALTEXTURING 0 turns the texturing off; 1, 2 and 3 turn it on with N = 16, 32 and 64 (the bits 4, 2 and 1 of [0x79D86C], which 0x457AC5 reads in that order); any other value leaves the previous state (off at start)
	// [BIN:TPW-EXE:0x0044D998 cache option] PROCEDURALTEXTURECACHE 0, 1 and 2 give 1500, 2000 and 2500 textures (0x5DC, 0x7D0, 0x9C4)
	// [BIN:TPW-EXE:0x00572110 memory clamp] N is 16 when [0x8BD520] is below 0x3000000 or [0x8C1624] is below 3 x the bytes per pixel in MB, and a 64 becomes 32 when it is below 0x4000000 or below 5 x the bytes per pixel in MB;
	// at 32 bits per pixel that is 12 MB and 20 MB of video memory, which every supported device clears, so OpenTPW tests the RAM figure only. [0x8BD524] equal to 4 skips the cache and its clamp altogether (0x57218E); OpenTPW has no such mode.
	/// <summary>The settings for the option values <paramref name="texturing"/> and <paramref name="cache"/> on a machine with <paramref name="memoryBytes"/> of RAM.</summary>
	public static ProceduralTerrainSettings For( int texturing, int cache, long memoryBytes )
	{
		var size = texturing switch { 1 => 16, 2 => 32, 3 => 64, _ => 0 };
		if ( size == 0 )
			return new ProceduralTerrainSettings( 0, CacheFor( cache ) );
		if ( memoryBytes < SmallestMemoryBytes )
			size = 16;
		if ( memoryBytes < LargeTextureMemoryBytes && size == 64 )
			size = 32;
		return new ProceduralTerrainSettings( size, CacheFor( cache ) );
	}

	private static int CacheFor( int cache ) => cache switch { 1 => 2000, 2 => 2500, _ => SmallCache };

	/// <summary>The settings of the active graphics options on this machine (off when no graphics settings exist, as in tests).</summary>
	public static ProceduralTerrainSettings Current()
	{
		if ( IGraphicsSettings.Instance is not { } graphics )
			return new ProceduralTerrainSettings( 0, SmallCache );
		var detail = graphics.Detail;
		return For( detail.ProceduralTexturing, detail.ProceduralTextureCache, MachineMemoryBytes() );
	}

	/// <summary>The RAM figure of this machine as the preset default reads it (the browser reports none and takes 4 GB).</summary>
	// [EXT:graphics-preset-browser] the browser (WASM) reports no usable memory size; it takes the same 4096 MB as the preset default, so the clamp never applies there
	public static long MachineMemoryBytes()
	{
		if ( OperatingSystem.IsBrowser() )
			return 4096L * 1024 * 1024;
		var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
		return bytes > 0 ? bytes : 64L * 1024 * 1024;
	}
}
