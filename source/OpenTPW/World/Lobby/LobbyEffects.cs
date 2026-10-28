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
// [BIN:TPW-EXE:0x5d5c50 lobby frame] the lobby's per-frame step calls every component's vf2 (offset 8), and the islands component's is 0x5e0470 (vtable 0x702cb0, slot 2 at 0x702cb8): the effects run once per rendered frame, not on the 31 ms simulation substep (0x54e360 state 2 calls 0x5d5c50 at 0x54e73a once per drawn frame and runs only 0x520130 on the 31 ms substep, 0x54e768-0x54e789)
// [EXT:frame-driven-at-60hz] the effects advance at the 60 Hz reference rate, whatever OpenTPW's own frame rate (docs/COMPATIBILITY.md "Frame-driven timing")
public sealed class LobbyEffects
{
	/// <summary>Seconds per effects tick: one original frame at the 60 Hz reference rate (<see cref="ReferenceFrameClock"/>).</summary>
	public const float TickSeconds = ReferenceFrameClock.FrameSeconds;
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
	// The sky's current colour and target in 16.16 fixed point; both start black (the scene's init zeroes them).
	private int skyR, skyG, skyB;
	private int targetR, targetG, targetB;
	private readonly List<LobbyBolt> bolts = new();
	private readonly List<WeatherStrike> strikes = new();

	/// <summary>Seeds the lobby's generator; the original seeds it once from <c>time(NULL)</c>.</summary>
	// [BIN:STP-PPC:0x1008CDBC lobby generator seed] the lobby system's constructor stores time(NULL) as the generator's state; it is never reset
	public LobbyEffects( uint seed ) => state = seed;

	/// <summary>
	/// The lobby's fog and clear colour (0x44DDFF): the screen behind the 3D scene is cleared to it, and the sky's colour
	/// grid shows it while the eased colour has reached its target.
	/// </summary>
	// [BIN:TPW-EXE:0x5d8b50 lobby fog] the lobby scene's init sets the fog colour [0x8bd47c] to 0xFF44DDFF (its fog range 50 to 300 is not drawn: the scene clears the fog enable bit 0x2000, 0x5D8BF9) and render flag 0x4000000
	// [BIN:TPW-EXE:0x5737aa lobby clear] the viewport clear (vtable +0x50, Clear2) uses the fog colour when render flags & 0x4002000, else black
	public static readonly (byte R, byte G, byte B) ClearColour = (0x44, 0xDD, 0xFF);

	/// <summary>
	/// The colour the lobby writes into the engine sky's 256-entry colour grid this tick (the colour of the cloud layer):
	/// the eased colour, or <see cref="ClearColour"/> while it equals its target.
	/// </summary>
	public (byte R, byte G, byte B) Sky { get; private set; } = ClearColour;

	/// <summary>The rain's drop count for the lobby renderer.</summary>
	public int RainDrops { get; private set; }

	/// <summary>The live bolts, oldest first.</summary>
	public IReadOnlyList<LobbyBolt> Bolts => bolts;

	/// <summary>True for the frame after a strike: the renderer draws one white full-screen quad and clears it.</summary>
	public bool Flash { get; set; }

	/// <summary>The generator's next draw: the LCG step rotated right by 13 bits, which is also the stored state.</summary>
	// [BIN:STP-PPC:0x100904A8 lobby generator] state = state × 0x19660D + 0x3C6EF35F, stored at +0x34 (inlined in the islands update)
	// [BIN:TPW-EXE:0x5e0f4b lobby generator draw] tp.exe stores the step at lobby +0x34, rotates it right by 13 (ror 0xd) and stores and uses the rotated value; every inlined draw does the same (0x5d98d1 flying-mesh placement, 0x5da0d4 flying-mesh spawn, 0x5e113f island one-shots)
	// [BIN:TPW-EXE:0x5d57cd lobby generator seed] the front-end object's constructor stores time(NULL) (0x67d3f0 with 0) at +0x34, and the camera wander's draws (0x5e09da and the retargets) take from the same state as the islands (0x5e0f4b) and the flyers (0x5d98d1); a stream seeded from the clock has no order to keep, so the wander's draws, which only shift which draw an island tick meets, change nothing a player can tell, and the draws are uniform whoever takes them
	public uint Draw()
	{
		state = System.Numerics.BitOperations.RotateRight( unchecked(state * 0x19660Du + 0x3C6EF35Fu), 13 );
		return state;
	}

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
		// [BIN:TPW-EXE:0x5e0f55 lobby lightning] tp.exe tests the rotated draw against the island's LIGHTNING (+0x5c) and places the bolt with the next four draws the same way (0x5e0f94-0x5e10a4)
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
		// [BIN:TPW-EXE:0x5e1149 lobby one-shots] (draw & 15) == 1 on the rotated draw, then the next draw mod 3 (0x5e117e) picks event 3, 4 or 5
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

	/// <summary>
	/// Sets the sky's target to <paramref name="target"/> and runs one step of the scene's sky update: unless the colour
	/// already equals its target, each channel moves (target − current) &gt;&gt; 4 in 16.16 fixed point, the red channel
	/// toward the blue target; the grid takes the eased colour, else the fog colour.
	/// </summary>
	// [BIN:STP-PPC:0x1008FAA0 lobby sky] the current colour eases 1/16 toward the target each frame and fills the engine sky's whole colour ramp
	// [BIN:TPW-EXE:0x5d96c0 lobby sky ease] while current != target: R += (B target − R) sar 4 (0x5d96fc-0x5d9709), G += (G target − G) sar 4, B += (B target − B) sar 4; the 256 grid entries at 0x8c2788 get 0xFF:R:G:B from the integer parts, else the fog colour [0x8bd47c]; the islands update copies the nearest island's SKYCOLOUR (+0x64..+0x6c) to the targets (0x5e11b2)
	// [DATA:video 0:40-1:10, 78:30-78:40] the lobby's cloud layer is tinted per island (light grey for Lost Kingdom, pink for Wonderland, dark for Halloween World and Space Zone), which the red-toward-blue colour gives, over a cyan clear colour
	// [BIN:TPW-EXE:0x5d96c0 lobby sky gates] the eased colour applies only while SKYQUALITY ([0x785888], settings entry 6) is above 1 and the first word of the graphics-device object 0x78d8d8 is 1 (0x5d96c6 and the next compare); otherwise the grid keeps the fog colour and the easing does not step
	// [BIN:TPW-EXE:0x5dfb4a default sky target] an island without a SKYCOLOUR line keeps the random target its constructor drew (LobbyScene mirrors the three draws)
	// [BIN:TPW-EXE:0x44dd50 device word] the first word of 0x78d8d8 is the 3D-acceleration switch: 1 sets the render mode [0x8bd524] to 1 (hardware), 0 to the software mode 4 (0x44de0b, 0x44dde9, 0x44de20); OpenTPW always renders on the GPU, so the word is 1 and only SKYQUALITY gates the colour
	/// <summary>Whether the cloud grid takes the eased colour: SKYQUALITY above 1 (0x5d96c0); at 0 or 1 it keeps the fog colour.</summary>
	public bool EasedSky { get; set; } = true;

	private void EaseSky( (byte R, byte G, byte B) target )
	{
		(targetR, targetG, targetB) = (target.R << 16, target.G << 16, target.B << 16);
		if ( !EasedSky )
		{
			Sky = ClearColour;
			return;
		}
		if ( skyR == targetR && skyG == targetG && skyB == targetB )
		{
			Sky = ClearColour;
			return;
		}
		skyR += (targetB - skyR) >> 4;
		skyG += (targetG - skyG) >> 4;
		skyB += (targetB - skyB) >> 4;
		Sky = ((byte)(skyR >> 16), (byte)(skyG >> 16), (byte)(skyB >> 16));
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
