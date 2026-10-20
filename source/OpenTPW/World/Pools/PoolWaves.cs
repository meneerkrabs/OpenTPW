namespace OpenTPW;

/// <summary>
/// The settings of a pool's wave machine (docs/POOLS.md, "Wave machine"): switched on or off, a wave height (1 low, 2 medium, 3 high) and an interval between the runs (0 short, 1 medium, 2 long).
/// RCT3 keeps more (length, run time); here the run time is fixed (<see cref="PoolWaves.RunTurns"/>) and the length is the swell of the water shader. They belong to their **machine**: they are stored in the flags
/// of the machine's own access cell, the water-side pool cell it stands at (<see cref="ParkCellFlags.PoolWavesOn"/>, <see cref="ParkCellFlags.PoolWaveHeightMask"/>, <see cref="ParkCellFlags.PoolWaveIntervalMask"/>),
/// so the cell map's save, restore, validation and canonical hash carry them with no format of their own, and a pool without a wave machine has no waves whatever its cells hold. A machine whose cell holds
/// none runs the <see cref="Default"/>.
/// </summary>
/// <param name="On">Whether the waves run.</param>
/// <param name="Height">1 to 3.</param>
/// <param name="Interval">0 to 2.</param>
// [EXT:pools-waves] OpenTPW's own wave machine; the original game has no pools
public readonly record struct PoolWaveSettings( bool On, int Height, int Interval )
{
	/// <summary>The settings of a wave machine whose access cell holds none: on, medium height, medium interval.</summary>
	public static PoolWaveSettings Default { get; } = new( true, 2, 1 );

	/// <summary>
	/// The settings <c>Level.CyclePoolWaves</c> steps through, for scripted use (the player sets them in the machine's settings window): the default, strong and frequent, gentle and rare, then off, and round again.
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

	/// <summary>The settings of the wave machine whose access cell is (x, y): what its cell holds, else <see cref="Default"/>.</summary>
	public static PoolWaveSettings At( ParkCellMap cells, (int X, int Y) access )
	{
		ArgumentNullException.ThrowIfNull( cells );
		return FromFlags( cells.FlagsAt( access.X, access.Y ) ) ?? Default;
	}

	/// <summary>Stores the settings of the machine whose access cell is (x, y) in the flags of that cell, keeping its other flags.</summary>
	public static void SetAt( ParkCellMap cells, (int X, int Y) access, PoolWaveSettings settings )
	{
		ArgumentNullException.ThrowIfNull( cells );
		var flags = (cells.FlagsAt( access.X, access.Y ) & ~ParkCellFlags.PoolWaveBits) | settings.ToFlags();
		if ( flags != cells.FlagsAt( access.X, access.Y ) )
			cells.SetFlags( access.X, access.Y, flags );
	}

	/// <summary>
	/// The settings the waves of a pool region run with: those of its wave machine with the lowest object id whose settings are on (the machines of one pool do not add up), or null when the region has no
	/// wave machine or none of them is on. A pool without a machine has no waves, whatever bits its cells hold. Allocation-free.
	/// </summary>
	public static PoolWaveSettings? ActiveIn( PoolAttractionSites sites, ParkCellMap cells, PoolSwimRegion region ) =>
		ActiveMachineIn( sites, cells, region ) is { } machine ? At( cells, machine.Access ) : null;

	/// <summary>The wave machine whose settings the waves of <paramref name="region"/> run with (<see cref="ActiveIn"/>: the one with the lowest object id whose settings are on), or null.</summary>
	public static PoolAttractionSite? ActiveMachineIn( PoolAttractionSites sites, ParkCellMap cells, PoolSwimRegion region )
	{
		ArgumentNullException.ThrowIfNull( sites );
		var all = sites.All;
		PoolAttractionSite? best = null;
		for ( var i = 0; i < all.Count; i++ )
		{
			var site = all[i];
			if ( site.Kind != PoolAttractionKind.WaveMachine || !region.Contains( site.Access.X, site.Access.Y ) || (best != null && site.ObjectId > best.ObjectId) )
				continue;
			if ( At( cells, site.Access ).On )
				best = site;
		}
		return best;
	}
}

/// <summary>
/// The waves of a pool, a function of the park turn and the settings of its wave machine (docs/POOLS.md, "Wave machine"; docs/DETERMINISM.md). What the simulation reads is only turn counts and
/// integer arithmetic: whether the waves run (<see cref="IsRunning"/>), their strength (<see cref="Envelope"/>), the happiness paid once per period (<see cref="HappinessGain"/>) and the push on the free swimmers (<see cref="Push"/>). A machine runs for
/// <see cref="RunTurns"/>, rests for the interval's turns and runs again; a run swells in and out over <see cref="SwellTurns"/>. The surface (the pool water shader, which raises and lowers the water mesh) and the swimmers' bob
/// draw the same plane wave (<see cref="SmoothWave"/>, floating point at the time of the frame, presentation only): the bob only moves a swimmer's drawn height, never its position, state or hash.
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
	/// <summary>
	/// How far the water surface rises and falls at height 3, in engine units (a cell is 2 wide): the pool water shader's <c>kMaxHeave</c>, which raises the mesh by this times its strength (the
	/// envelope, 0 to 1, times height / 3). It stays under the 0.12 the water lies below the lowest deck (<see cref="PoolMeshOptions.WaterDrop"/>), so a crest never tops the coping.
	/// </summary>
	public const float MaxHeave = 0.105f;
	/// <summary>How far a swimmer's drawn height bobs, by height 1, 2, 3, in engine units: the surface's own rise at that height, so a swimmer rides the water it is drawn in.</summary>
	public static readonly float[] BobHeights = { MaxHeave / 3, MaxHeave * 2 / 3, MaxHeave };
	/// <summary>The phase a wave front advances per cell along +x +y (of 64 steps of a period): the wave length, about 3 cells. The direction is the same for every pool.</summary>
	public const int PhasePerCell = 21;

	/// <summary>The turns of a whole cycle, a run and its rest.</summary>
	public static int CycleTurns( PoolWaveSettings settings ) => RunTurns + RestTurns[Math.Clamp( settings.Interval, 0, 2 )];

	/// <summary>Whether the waves run on this park turn: the settings are on and the turn lies in a run.</summary>
	public static bool IsRunning( PoolWaveSettings settings, long turn ) => settings.On && turn >= 0 && turn % CycleTurns( settings ) < RunTurns;

	/// <summary>How strong the waves are on a turn, 0 to 64: 0 at rest, ramping up over <see cref="SwellTurns"/> to 64 and down again.</summary>
	public static int Envelope( PoolWaveSettings settings, long turn )
	{
		if ( !IsRunning( settings, turn ) )
			return 0;
		var inRun = (int)(turn % CycleTurns( settings ));
		var edge = Math.Min( inRun, RunTurns - 1 - inRun );
		return Math.Min( 64, (edge + 1) * 64 / SwellTurns );
	}

	/// <summary>
	/// The smooth wave at a point (cells) at a fractional park turn, for the frame: a plane wave running along +x +y (period <see cref="PeriodTurns"/>, <see cref="PhasePerCell"/> per cell) with the envelope
	/// of the whole turn, from -1 to 1. Floating point: presentation only (the shader draws the same function), never read by the simulation.
	/// </summary>
	public static float SmoothWave( PoolWaveSettings settings, double turn, float cellX, float cellY )
	{
		var envelope = Envelope( settings, (long)Math.Floor( turn ) );
		if ( envelope == 0 )
			return 0f;
		var cycles = turn / PeriodTurns - (cellX + cellY) * PhasePerCell / 64.0;
		return (float)(Math.Sin( 2 * Math.PI * cycles ) * envelope / 64.0); // [DET:presentation] the drawn wave
	}

	/// <summary>The drawn-height offset of a point (cells) at a fractional park turn, in engine units (<see cref="SmoothWave"/> times the bob height). Presentation only.</summary>
	public static float SmoothBob( PoolWaveSettings settings, double turn, float cellX, float cellY ) => SmoothWave( settings, turn, cellX, cellY ) * BobHeights[Math.Clamp( settings.Height, 1, 3 ) - 1];

	/// <summary>
	/// How far a crest at full swell carries a free swimmer per park turn at height 3, in 16.16 cells (0.12 cell; about 0.085 on each axis, about the slowest stroke, 60 x 90 / 65536 = 0.082 cell a turn):
	/// at the crest a slow swimmer heading against the waves is carried back a little. Over a whole period the push averages about a third of it, less than the slowest stroke, so every swimmer still
	/// reaches its goals.
	/// </summary>
	public const int MaxPush = (int)(0.12 * Fixed16.One);

	/// <summary>
	/// The push of the waves on a free swimmer at (<paramref name="x"/>, <paramref name="y"/>) (16.16 cells) on a park turn, in 16.16 cells along +x and +y: the integer form of <see cref="SmoothWave"/> at that point
	/// (the phase in 64 steps of a period, the sine from the swimmers' heading table) while its crest is up, times the envelope and the height share of <see cref="MaxPush"/>; nothing in a trough, in a rest or
	/// with the waves off. Integers only, so every machine agrees (docs/DETERMINISM.md).
	/// </summary>
	public static (int X, int Y) Push( PoolWaveSettings settings, long turn, int x, int y ) => Carry( settings, turn, x, y, swaying: false );

	/// <summary>
	/// [EXT:pools-waves] The sway of the waves on a body that floats on them (the wave pool's ring floaters): the same as <see cref="Push"/> while the crest is up, and as much back along -x -y while the
	/// trough passes, so a floater rocks to and fro with the swell instead of being carried away. Integers only.
	/// </summary>
	public static (int X, int Y) Sway( PoolWaveSettings settings, long turn, int x, int y ) => Carry( settings, turn, x, y, swaying: true );

	private static (int X, int Y) Carry( PoolWaveSettings settings, long turn, int x, int y, bool swaying )
	{
		var envelope = Envelope( settings, turn );
		if ( envelope == 0 )
			return (0, 0);
		// SmoothWave's cycles are turn / 24 - (x + y) x 21/64; in 64ths of a period the point's share is (x + y) x 21 in 16.16, shifted down
		var phase = (int)(turn % PeriodTurns) * 64 / PeriodTurns - (int)(((long)x + y) * PhasePerCell >> 16);
		var sine = PoolSwimming.Direction( phase & 63 ).Y;
		if ( sine <= 0 && !swaying )
			return (0, 0);
		var magnitude = (int)((long)MaxPush * Math.Clamp( settings.Height, 1, 3 ) * envelope * sine / (3L * 64 * Fixed16.One));
		// along the diagonal: both axes take cos 45 degrees of it (heading 8 of the table)
		var along = Fixed16.Mul( magnitude, PoolSwimming.Direction( 8 ).X );
		return (along, along);
	}

	/// <summary>The happiness a swimmer in the waves is paid on a turn: <see cref="HappinessPerHeight"/> times the height once per period while the waves run, else 0.</summary>
	public static float HappinessGain( PoolWaveSettings settings, long turn ) =>
		IsRunning( settings, turn ) && turn % PeriodTurns == 0 ? HappinessPerHeight * Math.Clamp( settings.Height, 1, 3 ) : 0f;
}
