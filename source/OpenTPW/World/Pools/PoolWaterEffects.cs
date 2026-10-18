using System.Runtime.CompilerServices;

namespace OpenTPW;

/// <summary>
/// The effects of a pool on the free swimmers in it (docs/POOLS.md, "Wave machine" and "Splash cannon and water jets"), run by the swim session once per park turn for every swimmer
/// (<see cref="IPoolAmbient"/>): the waves pay a small happiness bonus once per wave period while they run and push the swimmers along while a crest passes (<see cref="IPoolCurrent"/>), and the spray of a water jet pays a smaller one every few turns to a swimmer inside it.
/// Both are functions of the park turn and the swimmer's cell: no random draw, no new state, no new save field (the happiness the swimmer gained is saved with the guest).
/// It is registered as the activity <c>ambient.water</c>, which offers nothing; the registry is only how the host finds it.
/// </summary>
// [EXT:pools-waves] [EXT:pools-attractions] OpenTPW's own pool effects; the original game has no pools
public sealed class PoolWaterEffects : IPoolActivity, IPoolAmbient, IPoolCurrent
{
	public const string ActivityId = "ambient.water";
	private static readonly ConditionalWeakTable<PoolSwimRegion, WaveCache> caches = new();
	private readonly PoolAttractionSites? sites;

	private sealed class WaveCache
	{
		public int Writes = -1, SitesVersion = -1;
		public PoolWaveSettings? Settings;
	}

	public PoolWaterEffects( PoolAttractionSites? sites = null ) => this.sites = sites;

	public string Id => ActivityId;

	public PoolActivityOffer? Offer( GuestSimulation simulation, Guest guest, PoolSwimRegion region ) => null;

	public bool Start( GuestSimulation simulation, Guest guest, PoolActivityOffer offer, ref uint random ) => false;

	public PoolActivityStep Update( GuestSimulation simulation, Guest guest, ref uint random ) => PoolActivityStep.BackInWater;

	public void Abort( GuestSimulation simulation, Guest guest )
	{
	}

	/// <summary>
	/// The settings the waves of a swim region run with (<see cref="PoolWaveSettings.ActiveIn"/>: its wave machine whose settings are on, and none without a machine), cached per region until a pool cell or an object
	/// changes. Null when the pool has no waves.
	/// </summary>
	public static PoolWaveSettings? WavesOf( PoolAttractionSites? sites, ParkCellMap cells, PoolSwimRegion region )
	{
		if ( sites == null )
			return null;
		var cache = caches.GetOrCreateValue( region );
		var version = sites.All.Count >= 0 ? sites.Version : 0;
		if ( cache.Writes != cells.PoolWrites || cache.SitesVersion != version )
		{
			cache.Settings = PoolWaveSettings.ActiveIn( sites, cells, region );
			cache.Writes = cells.PoolWrites;
			cache.SitesVersion = sites.Version;
		}
		return cache.Settings;
	}

	/// <summary>[EXT:pools-waves] The push of the waves of the swimmer's pool on this turn (<see cref="PoolWaves.Push"/>, at the swimmer's point, where the drawn crest is), or none when the pool has no waves.</summary>
	public (int X, int Y) Current( GuestSimulation simulation, Guest guest, PoolSwimRegion region ) =>
		WavesOf( sites, simulation.Grid.Cells, region ) is { } waves
			? PoolWaves.Push( waves, simulation.ParkTurn, guest.Navigation.X, guest.Navigation.Y )
			: (0, 0);

	public void Ambient( GuestSimulation simulation, Guest guest, PoolSwimRegion region )
	{
		var turn = simulation.ParkTurn;
		if ( WavesOf( sites, simulation.Grid.Cells, region ) is { } waves && PoolWaves.HappinessGain( waves, turn ) is var gain and > 0f )
			guest.Happiness = MeterMath.Add( guest.Happiness, gain );
		if ( sites != null && turn % PoolAttractionSettings.SprayPeriodTurns == 0 && InSpray( sites, region, guest.CellX, guest.CellY ) )
			guest.Happiness = MeterMath.Add( guest.Happiness, PoolAttractionSettings.SprayHappiness );
	}

	/// <summary>Whether a cell is in the spray of a water jet of the region: within <see cref="PoolAttractionSettings.SprayRadius"/> cells (Chebyshev) of its water-side cell.</summary>
	public static bool InSpray( PoolAttractionSites sites, PoolSwimRegion region, int x, int y )
	{
		var all = sites.All;
		for ( var i = 0; i < all.Count; i++ )
		{
			var jet = all[i];
			if ( jet.Kind == PoolAttractionKind.Jets && region.Contains( jet.Access.X, jet.Access.Y ) && Math.Max( Math.Abs( x - jet.Access.X ), Math.Abs( y - jet.Access.Y ) ) <= PoolAttractionSettings.SprayRadius )
				return true;
		}
		return false;
	}

	/// <summary>
	/// The drawn-height offset of a swimmer on the waves of its pool, in engine units: a smooth sine at the exact time of the frame (presentation only, so it may use floating point and the wall-clock fraction of the
	/// turn), with the strength of the turn's envelope. Zero when the swimmer's pool has no waves or they rest.
	/// </summary>
	public static float Bob( GuestSimulation simulation, Guest guest, PoolAttractionSites? sites )
	{
		if ( !guest.IsSwimming || PoolActivityHelpers.RegionOf( simulation, guest ) is not { } region || WavesOf( sites, simulation.Grid.Cells, region ) is not { } waves )
			return 0f;
		return PoolWaves.SmoothBob( waves, SmoothTurn( simulation ), guest.DrawX, guest.DrawY );
	}

	/// <summary>
	/// The surface's rise on the waves of <paramref name="region"/> under a guest's drawn point, in engine units, whatever the guest is doing (the water level an activity draws its swimmer at rides the same
	/// swell as the surface). Zero when the pool has no waves or they rest. Presentation only.
	/// </summary>
	public static float SurfaceRise( GuestSimulation simulation, Guest guest, PoolSwimRegion? region, PoolAttractionSites? sites ) =>
		region != null && WavesOf( sites, simulation.Grid.Cells, region ) is { } waves ? PoolWaves.SmoothBob( waves, SmoothTurn( simulation ), guest.DrawX, guest.DrawY ) : 0f;

	private sealed class TurnClock
	{
		public long Turn = -1;
		public double StartedAt;
	}

	private static readonly ConditionalWeakTable<GuestSimulation, TurnClock> clocks = new();

	/// <summary>
	/// The park turn with a fraction, for the frame: the whole turn of the simulation plus how far the presentation clock has come since the turn changed (a turn is <see cref="ParkCalendar.TurnMilliseconds"/> ms).
	/// Presentation only: it reads and writes no simulation state. The surface shader is given the same value (<see cref="PoolRenderer"/>).
	/// </summary>
	public static double SmoothTurn( GuestSimulation simulation )
	{
		var clock = clocks.GetOrCreateValue( simulation );
		var turn = simulation.ParkTurn;
		if ( turn != clock.Turn )
			(clock.Turn, clock.StartedAt) = (turn, simulation.TimeSeconds);
		return turn + Math.Clamp( (simulation.TimeSeconds - clock.StartedAt) * 1000.0 / ParkCalendar.TurnMilliseconds, 0.0, 1.0 );
	}
}
