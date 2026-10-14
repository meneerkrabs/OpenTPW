using OpenTPW.FrontEnd;

namespace OpenTPW;

/// <summary>
/// The front end's island effects, stepped once per lobby frame the way the original's islands component does
/// (<c>CLBScreenComponentIslands::vf2</c>, docs/AUDIO.md "Lobby effects"): the sky eases toward the nearest island's
/// <c>SKYCOLOUR</c>, rainy islands set the rain's drop count by their distance, an island with <c>LIGHTNING</c> strikes
/// on a draw of the lobby's own generator (a bolt, a one-frame flash and the thunder), and a second draw picks the
/// island's one-shot sounds. GPU-free and testable; <see cref="GameFlow"/> plays the sounds and draws the result.
/// </summary>
// [BIN:STP-PPC:0x10094474 lobby islands update] the per-frame update of the islands component: nearest island to the camera, sky target, raindrops, lightning and the island one-shots
// [APPROX:UI-940] the effects advance at a fixed 30 ticks a second; the original steps them once per rendered frame and its frame rate is not known
public sealed class LobbyEffects
{
	/// <summary>Seconds per effects tick (one original frame, assumed at 30 frames a second).</summary>
	public const float TickSeconds = 1f / 30f;
	/// <summary>A bolt's life: <c>AddLightning(bottom, top, 500, 1)</c>.</summary>
	public const float BoltSeconds = 0.5f;
	/// <summary>Live bolts the engine keeps at once.</summary>
	public const int MaximumBolts = 16;
	/// <summary>The rain's drop count: zero below this, and at most <see cref="MaximumDrops"/>.</summary>
	public const int MinimumDrops = 20;
	public const int MaximumDrops = 400;
	/// <summary>The globe's hard-coded sky target (jungle's colour).</summary>
	public static readonly (byte R, byte G, byte B) GlobeSky = (243, 203, 191);

	/// <summary>One lobby lightning bolt: its endpoints in lobby coordinates (X/Y ground, Z up) and its age.</summary>
	public readonly record struct LobbyBolt( Vector3 Bottom, Vector3 Top, float Age );

	/// <summary>An island the effects read: its lobby position and its RAINY and LIGHTNING settings.</summary>
	public readonly record struct Island( LobbyIslandInfo Info, float X, float Y );

	private uint state;
	private float pending;
	// The sky's current colour in 16.16 fixed point, eased 1/16 of the way to the target each tick.
	private int skyR, skyG, skyB;
	private bool skySet;
	private readonly List<LobbyBolt> bolts = new();
	private readonly List<WeatherStrike> strikes = new();

	/// <summary>Seeds the lobby's generator; the original seeds it once from <c>time(NULL)</c>.</summary>
	// [BIN:STP-PPC:0x1008CDBC lobby generator seed] the lobby system's constructor stores time(NULL) as the generator's state; it is never reset
	public LobbyEffects( uint seed ) => state = seed;

	/// <summary>True once a tick has set the sky (before that the caller draws its own default).</summary>
	public bool HasSky => skySet;

	/// <summary>The sky colour to draw now.</summary>
	public (byte R, byte G, byte B) Sky => ((byte)(skyR >> 16), (byte)(skyG >> 16), (byte)(skyB >> 16));

	/// <summary>The rain's drop count for the lobby renderer.</summary>
	public int RainDrops { get; private set; }

	/// <summary>The live bolts, oldest first.</summary>
	public IReadOnlyList<LobbyBolt> Bolts => bolts;

	/// <summary>True for the frame after a strike: the renderer draws one white full-screen quad and clears it.</summary>
	public bool Flash { get; set; }

	/// <summary>The generator's next state: it returns the full 32-bit value.</summary>
	// [BIN:STP-PPC:0x100904A8 lobby generator] state = state × 0x19660D + 0x3C6EF35F, stored at +0x34; the full state is returned (inlined in the islands update)
	// [APPROX:UI-941] only the islands' own draws advance the generator; the original's flying meshes and camera wander draw from it too (0x10090174, 0x1009476C), so the strikes come in a different rhythm
	public uint Draw() => state = unchecked(state * 0x19660Du + 0x3C6EF35Fu);

	/// <summary>A draw as the fraction the bolt offsets use: (state &amp; 0x3FFFFFFF) / 2^30, in [0, 1).</summary>
	private float Fraction() => (Draw() & 0x3FFFFFFF) / 1073741824f;

	/// <summary>
	/// Advances <paramref name="deltaSeconds"/> of wall-clock time: bolts age, and every whole tick runs the islands
	/// update with the camera at (<paramref name="cameraX"/>, <paramref name="cameraY"/>). Returns how many thunders and
	/// which island one-shot events (3, 4 or 5 of the island's <c>cat_locallobbysfx</c>) the ticks raised.
	/// </summary>
	public (int Thunders, IReadOnlyList<uint> OneShots) Step( float deltaSeconds, IReadOnlyList<Island> islands, float cameraX, float cameraY )
	{
		var thunders = 0;
		var oneShots = new List<uint>();
		if ( !float.IsFinite( deltaSeconds ) || deltaSeconds <= 0 )
			return (0, oneShots);
		// The engine ages a bolt by the frame's milliseconds, capped at 500.
		var age = Math.Min( deltaSeconds, BoltSeconds );
		for ( var index = bolts.Count - 1; index >= 0; index-- )
		{
			if ( bolts[index].Age + age >= BoltSeconds )
				bolts.RemoveAt( index );
			else
				bolts[index] = bolts[index] with { Age = bolts[index].Age + age };
		}
		pending = Math.Min( pending + deltaSeconds, 0.5f );
		while ( pending >= TickSeconds )
		{
			pending -= TickSeconds;
			var (thunder, oneShot) = Tick( islands, cameraX, cameraY );
			if ( thunder )
				thunders++;
			if ( oneShot is uint sound )
				oneShots.Add( sound );
		}
		return (thunders, oneShots);
	}

	/// <summary>Moves the strikes since the last call into <paramref name="into"/> (the renderer's lobby source).</summary>
	public void DrainStrikes( List<WeatherStrike> into )
	{
		into.AddRange( strikes );
		strikes.Clear();
	}

	/// <summary>No island effects on this backdrop (the online city): no rain falls.</summary>
	public void StopRain() => RainDrops = 0;

	/// <summary>Eases the sky toward the globe's colour while the globe is up; the islands do not update then (no rain).</summary>
	// [BIN:STP-PPC:0x100923A8 globe opens] the globe component sets the raindrops to 0, the field of view to 60 and the sky target to (243, 203, 191)
	public void StepGlobe( float deltaSeconds )
	{
		RainDrops = 0;
		pending = Math.Min( pending + Math.Max( 0, deltaSeconds ), 0.5f );
		while ( pending >= TickSeconds )
		{
			pending -= TickSeconds;
			EaseSky( GlobeSky );
		}
	}

	private (bool Thunder, uint? OneShot) Tick( IReadOnlyList<Island> islands, float cameraX, float cameraY )
	{
		if ( islands.Count == 0 )
			return (false, null);
		var nearest = islands[0];
		var nearestDistance = float.MaxValue;
		double rainySum = 0;
		var rainyCount = 0;
		foreach ( var island in islands )
		{
			var dx = island.X - cameraX;
			var dy = island.Y - cameraY;
			var distance = dx * dx + dy * dy;
			if ( distance < nearestDistance )
			{
				nearestDistance = distance;
				nearest = island;
			}
			if ( island.Info.Rainy )
			{
				rainySum += distance;
				rainyCount++;
			}
		}
		EaseSky( nearest.Info.SkyColour );
		RainDrops = RainCount( rainyCount == 0 ? null : rainySum / rainyCount );

		var thunder = false;
		var lightning = nearest.Info.Lightning;
		// [BIN:STP-PPC:0x10095124 lobby lightning] with LIGHTNING > 0, a draw strikes when (state & LIGHTNING) == 1; four fresh draws place the top at the island ±50 (height 500) and the bottom within ±10 of it (height 0)
		// [BIN:STP-PPC:0x10095310 lobby strike] AddLightning(bottom, top, 500, 1), LightningFlash() and cat_globallobbysfx 1 played flat at once (no thunder delay)
		if ( lightning > 0 && (Draw() & (uint)lightning) == 1 )
		{
			var topX = 100 * Fraction() + nearest.X - 50;
			var topY = 100 * Fraction() + nearest.Y - 50;
			var bottomX = 20 * Fraction() + topX - 10;
			var bottomY = 20 * Fraction() + topY - 10;
			if ( bolts.Count >= MaximumBolts )
				bolts.RemoveAt( 0 );
			bolts.Add( new LobbyBolt( new Vector3( bottomX, bottomY, 0 ), new Vector3( topX, topY, 500 ), 0 ) );
			// For the weather renderer, in original axes (Y up): the lobby's ground plane is its X/Y.
			strikes.Add( new WeatherStrike( new System.Numerics.Vector3( topX, 500, topY ), new System.Numerics.Vector3( bottomX, 0, bottomY ) ) );
			Flash = true;
			thunder = true;
		}
		// [BIN:STP-PPC:0x10095340 lobby one-shots] a draw with (state & 15) == 1 plays event 3 + (fresh draw mod 3) of the nearest island's cat_locallobbysfx
		uint? oneShot = null;
		if ( (Draw() & 15) == 1 )
			oneShot = 3 + Draw() % 3;
		return (thunder, oneShot);
	}

	/// <summary>The rain's drop count for a mean squared distance to the rainy islands: 10^17 / mean^4, zero below 20, at most 400.</summary>
	// [BIN:STP-PPC:0x10094F8C lobby rain] raindrops = 1e17 / (mean squared horizontal distance from the camera to the rainy islands)^4; below 20 none, capped at 400; rain type 1
	public static int RainCount( double? meanSquaredDistance )
	{
		if ( meanSquaredDistance is not double mean )
			return 0;
		if ( mean <= 0 )
			return MaximumDrops;
		var count = 1e17 / Math.Pow( mean, 4 );
		if ( count < MinimumDrops )
			return 0;
		return count >= MaximumDrops ? MaximumDrops : (int)count;
	}

	/// <summary>Eases the sky 1/16 of the way toward <paramref name="target"/> (16.16 fixed point); the first target is taken at once.</summary>
	// [BIN:STP-PPC:0x1008FAA0 lobby sky] the current colour eases 1/16 toward the target each frame and fills the engine sky's whole colour ramp, so the sky is one flat colour; [BIN:TPW-EXE:0x5D96C0] the same
	// [APPROX:UI-018] both builds ease the red channel toward the blue target; that quirk and the two settings flags that gate the eased sky (else a display default colour) are not reproduced until a capture confirms them
	private void EaseSky( (byte R, byte G, byte B) target )
	{
		if ( !skySet )
		{
			(skyR, skyG, skyB) = (target.R << 16, target.G << 16, target.B << 16);
			skySet = true;
			return;
		}
		skyR += ((target.R << 16) - skyR) / 16;
		skyG += ((target.G << 16) - skyG) / 16;
		skyB += ((target.B << 16) - skyB) / 16;
	}
}

/// <summary>The lobby's rain and lightning as the weather renderer reads them (<see cref="LobbyEffects"/>): rain, never snow, and 500 ms bolts.</summary>
// [BIN:STP-PPC:0x10094F8C lobby rain] the lobby sets rain type 1 and its drop count on the engine's CRain each frame; [BIN:STP-PPC:0x10095310 lobby strike] AddLightning(bottom, top, 500, 1)
internal sealed class LobbyWeatherSource( LobbyEffects effects ) : IWeatherVisualSource
{
	private readonly List<WeatherStrike> strikes = new();

	public int DropCount => effects.RainDrops;
	public bool Snow => false;
	public float BoltLifetimeSeconds => LobbyEffects.BoltSeconds;

	public IReadOnlyList<WeatherStrike> TakeStrikes()
	{
		strikes.Clear();
		effects.DrainStrikes( strikes );
		return strikes;
	}
}
