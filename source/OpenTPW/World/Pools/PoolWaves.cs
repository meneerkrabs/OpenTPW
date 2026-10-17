namespace OpenTPW;

/// <summary>
/// The settings of a pool's wave machine (docs/POOLS.md, "Wave machine"): switched on or off, a wave height (1 low, 2 medium, 3 high) and an interval between the runs (0 short, 1 medium, 2 long).
/// RCT3 keeps more (length, run time); here the run time is fixed (<see cref="PoolWaves.RunTurns"/>) and the length is the swell of the water shader. They are stored per pool region in the flags
/// of the region's first pool cell (<see cref="ParkCellFlags.PoolWavesOn"/>, <see cref="ParkCellFlags.PoolWaveHeightMask"/>, <see cref="ParkCellFlags.PoolWaveIntervalMask"/>), so the cell map's
/// save, restore, validation and canonical hash carry them with no format of their own: a park without a wave machine has no such flag.
/// </summary>
/// <param name="On">Whether the waves run.</param>
/// <param name="Height">1 to 3.</param>
/// <param name="Interval">0 to 2.</param>
// [EXT:pools-waves] OpenTPW's own wave machine; the original game has no pools
public readonly record struct PoolWaveSettings( bool On, int Height, int Interval )
{
	/// <summary>The settings a freshly placed wave machine writes: on, medium height, medium interval.</summary>
	public static PoolWaveSettings Default { get; } = new( true, 2, 1 );

	/// <summary>
	/// The settings a click on a wave machine steps through (the interim control; the player's window comes later): the default, strong and frequent, gentle and rare, then off, and round again.
	/// </summary>
	public static IReadOnlyList<PoolWaveSettings> Presets { get; } = new[] { Default, new PoolWaveSettings( true, 3, 0 ), new PoolWaveSettings( true, 1, 2 ), new PoolWaveSettings( false, 2, 1 ) };

	/// <summary>The preset after <paramref name="current"/> (the first when the pool has none yet, or settings that are none of the presets).</summary>
	public static PoolWaveSettings NextPreset( PoolWaveSettings? current )
	{
		var index = current is { } settings ? Presets.ToList().IndexOf( settings ) : -1;
		return Presets[(index + 1) % Presets.Count];
	}

	private const int HeightShift = 8, IntervalShift = 14;

	/// <summary>The flag bits of the settings.</summary>
	public ParkCellFlags ToFlags() =>
		(On ? ParkCellFlags.PoolWavesOn : 0) | (ParkCellFlags)(Math.Clamp( Height, 1, 3 ) << HeightShift) | (ParkCellFlags)(Math.Clamp( Interval, 0, 2 ) << IntervalShift);

	/// <summary>Whether a cell's flags hold wave settings (a height was stored).</summary>
	public static bool Has( ParkCellFlags flags ) => (flags & ParkCellFlags.PoolWaveHeightMask) != 0;

	/// <summary>The settings in a cell's flags, or null when it holds none.</summary>
	public static PoolWaveSettings? FromFlags( ParkCellFlags flags ) =>
		Has( flags ) ? new PoolWaveSettings( flags.HasFlag( ParkCellFlags.PoolWavesOn ), ((int)(flags & ParkCellFlags.PoolWaveHeightMask)) >> HeightShift, Math.Min( ((int)(flags & ParkCellFlags.PoolWaveIntervalMask)) >> IntervalShift, 2 ) ) : null;

	/// <summary>The settings of a pool region: those of its first cell (row by row) that holds some, or null when the pool has none (no wave machine has been placed on it).</summary>
	public static PoolWaveSettings? Read( ParkCellMap cells, PoolSwimRegion region )
	{
		ArgumentNullException.ThrowIfNull( cells );
		ArgumentNullException.ThrowIfNull( region );
		foreach ( var (x, y) in region.Cells )
			if ( FromFlags( cells.FlagsAt( x, y ) ) is { } settings )
				return settings;
		return null;
	}

	/// <summary>Writes the settings to the region: its first cell holds them and no other cell of the region does.</summary>
	public static void Write( ParkCellMap cells, PoolSwimRegion region, PoolWaveSettings settings )
	{
		ArgumentNullException.ThrowIfNull( cells );
		ArgumentNullException.ThrowIfNull( region );
		for ( var i = 0; i < region.Cells.Count; i++ )
		{
			var (x, y) = region.Cells[i];
			var flags = cells.FlagsAt( x, y ) & ~ParkCellFlags.PoolWaveBits;
			if ( i == 0 )
				flags |= settings.ToFlags();
			if ( flags != cells.FlagsAt( x, y ) )
				cells.SetFlags( x, y, flags );
		}
	}

	/// <summary>
	/// Keeps a pool's settings when their cell is erased: moves the wave bits of (x, y) to its first pool neighbour (north, east, south, west) that holds none. Call before the cell is cleared.
	/// </summary>
	public static void MoveFromErased( ParkCellMap cells, int x, int y )
	{
		ArgumentNullException.ThrowIfNull( cells );
		var bits = cells.FlagsAt( x, y ) & ParkCellFlags.PoolWaveBits;
		if ( bits == 0 )
			return;
		cells.SetFlags( x, y, cells.FlagsAt( x, y ) & ~ParkCellFlags.PoolWaveBits );
		foreach ( var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) } )
		{
			var (nx, ny) = (x + dx, y + dy);
			if ( cells.InBounds( nx, ny ) && cells.RawTypeAt( nx, ny ) == ParkCellTypes.PoolWater && !Has( cells.FlagsAt( nx, ny ) ) )
			{
				cells.SetFlags( nx, ny, cells.FlagsAt( nx, ny ) | bits );
				return;
			}
		}
	}
}

/// <summary>
/// The waves of a pool, a pure function of the park turn and the settings (docs/POOLS.md, "Wave machine"; docs/DETERMINISM.md): every number is an integer, so a wave is the same on every machine and
/// in every save. A machine runs for <see cref="RunTurns"/>, rests for the interval's turns and runs again; a run swells in and out over <see cref="SwellTurns"/>. The surface (the pool water shader) and the swimmers' bobbing read the
/// same wave, so they agree. The bob only moves a swimmer's drawn height, never its position or state: it is presentation, simulation-neutral. The happiness bonus is the one simulation effect
/// (<see cref="HappinessGain"/>).
/// </summary>
// [EXT:pools-waves] OpenTPW's own wave machine; the original game has no pools
public static class PoolWaves
{
	/// <summary>A run lasts 120 park turns, about 30 seconds.</summary>
	public const int RunTurns = 120;
	/// <summary>The rest between two runs, by interval 0, 1, 2: 40, 100 and 200 turns (10, 25 and 50 seconds).</summary>
	public static readonly int[] RestTurns = { 40, 100, 200 };
	/// <summary>A run swells in over its first and out over its last 12 turns.</summary>
	public const int SwellTurns = 12;
	/// <summary>One wave period, in park turns (6 seconds); the happiness bonus is paid once per period.</summary>
	public const int PeriodTurns = 24;
	/// <summary>The happiness a swimmer in the waves gets per period and height step: 0.4, 0.8 and 1.2 (meter 0 to 100).</summary>
	public const float HappinessPerHeight = 0.4f;
	/// <summary>The wave height of the water surface, by height 1, 2, 3, in engine units (a cell is 2.0 across, a guest 2.0 tall).</summary>
	public static readonly float[] SurfaceHeights = { 0.05f, 0.09f, 0.14f };
	/// <summary>How far a swimmer's drawn height bobs, by height 1, 2, 3, in engine units: half the surface wave, as a body rides it.</summary>
	public static readonly float[] BobHeights = { 0.03f, 0.05f, 0.08f };
	/// <summary>The phase a wave front advances per cell along the machine's direction (of 64 steps of a period): the wave length, about 3 cells.</summary>
	public const int PhasePerCell = 21;

	/// <summary>The turns of a whole cycle, a run and its rest.</summary>
	public static int CycleTurns( PoolWaveSettings settings ) => RunTurns + RestTurns[Math.Clamp( settings.Interval, 0, 2 )];

	/// <summary>Whether the waves run on this park turn: the settings are on and the turn lies in a run.</summary>
	public static bool IsRunning( PoolWaveSettings settings, long turn ) => settings.On && turn >= 0 && turn % CycleTurns( settings ) < RunTurns;

	/// <summary>How strong the waves are on a turn, 0 to 64 (16.16-free): 0 at rest, ramping up over <see cref="SwellTurns"/> to 64 and down again.</summary>
	public static int Envelope( PoolWaveSettings settings, long turn )
	{
		if ( !IsRunning( settings, turn ) )
			return 0;
		var inRun = (int)(turn % CycleTurns( settings ));
		var edge = Math.Min( inRun, RunTurns - 1 - inRun );
		return Math.Min( 64, (edge + 1) * 64 / SwellTurns );
	}

	/// <summary>The wave at a point (16.16 cells) on a turn, from -64 to 64 (a sine of the phase times the envelope, divided by 64): the same value drives the surface and the bob.</summary>
	public static int Wave( PoolWaveSettings settings, long turn, int x, int y )
	{
		var envelope = Envelope( settings, turn );
		if ( envelope == 0 )
			return 0;
		// the wave front travels along +x +y: phase (of 64 steps of a period) = turn * 64 / PeriodTurns - (x + y) * PhasePerCell
		var phase = (int)(turn * 64 / PeriodTurns % 64) - (int)(((long)(x + y) * PhasePerCell) >> 16);
		var sine = PoolSwimming.Direction( phase & 63 ).Y;     // sin(phase), 16.16: heading 16 is +y, so Direction(h).Y is the sine of h steps
		return (int)((long)sine * envelope >> 16);
	}

	/// <summary>The drawn-height offset of a swimmer at a point on a turn, in engine units. Presentation only.</summary>
	public static float Bob( PoolWaveSettings settings, long turn, int x, int y ) => Wave( settings, turn, x, y ) / 64f * BobHeights[Math.Clamp( settings.Height, 1, 3 ) - 1];

	/// <summary>
	/// The smooth wave at a point (cells) at a fractional park turn, for the frame: the same plane wave as <see cref="Wave"/> (period <see cref="PeriodTurns"/>, <see cref="PhasePerCell"/> per cell along +x +y)
	/// with the envelope of the whole turn, from -1 to 1. Floating point: presentation only (the shader draws the same function), never read by the simulation.
	/// </summary>
	public static float SmoothWave( PoolWaveSettings settings, double turn, float cellX, float cellY )
	{
		var envelope = Envelope( settings, (long)Math.Floor( turn ) );
		if ( envelope == 0 )
			return 0f;
		var cycles = turn / PeriodTurns - (cellX + cellY) * PhasePerCell / 64.0;
		return (float)(Math.Sin( 2 * Math.PI * cycles ) * envelope / 64.0);
	}

	/// <summary>The drawn-height offset of a point (cells) at a fractional park turn, in engine units (<see cref="SmoothWave"/> times the bob height).</summary>
	public static float SmoothBob( PoolWaveSettings settings, double turn, float cellX, float cellY ) => SmoothWave( settings, turn, cellX, cellY ) * BobHeights[Math.Clamp( settings.Height, 1, 3 ) - 1];

	/// <summary>The water surface's wave height at a point on a turn, in engine units. Presentation only.</summary>
	public static float Surface( PoolWaveSettings settings, long turn, int x, int y ) => Wave( settings, turn, x, y ) / 64f * SurfaceHeights[Math.Clamp( settings.Height, 1, 3 ) - 1];

	/// <summary>The happiness a swimmer in the waves is paid on a turn: <see cref="HappinessPerHeight"/> times the height once per period while the waves run, else 0.</summary>
	public static float HappinessGain( PoolWaveSettings settings, long turn ) =>
		IsRunning( settings, turn ) && turn % PeriodTurns == 0 ? HappinessPerHeight * Math.Clamp( settings.Height, 1, 3 ) : 0f;
}
