namespace OpenTPW;

/// <summary>
/// The presentation half of the park's weather: the drop field and the bolt flashes the renderer draws
/// (docs/AUDIO.md "Weather visuals"). This is the testable, GPU-free state — the counts and lifetimes follow
/// the simulated weather (<see cref="ParkWeather.CurrentDrops"/>, <see cref="ParkWeather.Snow"/> and the strike
/// positions), while the motion itself is a procedural stand-in for the original's weather renderer
/// (FUN_0057d300 family, not traced).
/// </summary>
// [APPROX:AUDIO-900] the drops are drawn around the view target in a wrapping box and fall straight down at a fixed speed; the original's drop volume, speed and streak shape are not traced (FUN_0057d300)
// [APPROX:AUDIO-901] snow reuses the rain's drop count but falls slower with a sinusoidal sway; the original's snow motion is not traced
// [APPROX:AUDIO-904] the visual field holds at most MaximumDrops drops, so a level whose MaxRaindrops is higher draws fewer drops than it simulates
public sealed class WeatherVisualState
{
	/// <summary>Horizontal half-extent of the drop volume around the view target, in engine units.</summary>
	public const float VolumeRadius = 45f;
	/// <summary>Vertical span of the drop volume, in engine units.</summary>
	public const float VolumeHeight = 55f;
	/// <summary>How far below the view target the drop volume's bottom sits, so drops do not pop in at the target plane.</summary>
	public const float VolumeBottomOffset = 5f;
	/// <summary>Rain fall speed in engine units per second.</summary>
	public const float RainFallSpeed = 60f;
	/// <summary>Snow fall speed in engine units per second.</summary>
	public const float SnowFallSpeed = 8f;
	/// <summary>Peak horizontal speed of a snowflake's sway, in engine units per second.</summary>
	public const float SnowSwaySpeed = 2f;
	/// <summary>One full sway cycle of a snowflake, in seconds.</summary>
	public const float SnowSwaySeconds = 2.2f;
	/// <summary>How long a bolt stays on screen after its strike, in seconds.</summary>
	public const float BoltFlashSeconds = 0.45f;
	/// <summary>Points along a bolt's jittered polyline, top and bottom included.</summary>
	public const int BoltPointCount = 9;
	/// <summary>Peak horizontal offset of the bolt's jitter, in engine units.</summary>
	public const float BoltJitter = 4f;
	/// <summary>Bolt flashes kept at once; older flashes are dropped (strikes are at most one per park turn).</summary>
	public const int MaximumBoltFlashes = 4;
	/// <summary>Ceiling of drawn drops; the simulated cap (<c>WeatherEffects.MaxRaindrops</c>) is 600.</summary>
	public const int MaximumDrops = 600;

	/// <summary>One falling drop: its engine position and its sway phase (snow).</summary>
	public readonly record struct WeatherDrop( Vector3 Position, float Phase );

	/// <summary>One lightning flash: the bolt's engine endpoints, its age and the strike it belongs to.</summary>
	public readonly record struct WeatherBoltFlash( Vector3 Top, Vector3 Bottom, float Age, int Strike );

	private readonly DeterministicRandom random;
	private readonly List<WeatherDrop> drops = new();
	private readonly List<WeatherBoltFlash> flashes = new();
	private int strikes;

	/// <summary>The visual state's own draw stream, seeded apart from the park's simulation streams.</summary>
	public WeatherVisualState( ulong seed ) => random = new DeterministicRandom( seed );

	/// <summary>Simulation seconds the visuals have advanced; drops and flashes move only through <see cref="Step"/>.</summary>
	public float Elapsed { get; private set; }

	/// <summary>Drops currently on screen (at most <see cref="MaximumDrops"/>).</summary>
	public int DropCount => drops.Count;

	/// <summary>The drawn drops, in spawn order.</summary>
	public IReadOnlyList<WeatherDrop> Drops => drops;

	/// <summary>The live bolt flashes, oldest first.</summary>
	public IReadOnlyList<WeatherBoltFlash> Flashes => flashes;

	/// <summary>
	/// Advances the visuals by <paramref name="deltaSeconds"/> of simulation time: the drop count follows
	/// <paramref name="targetDrops"/> (spawns inside the volume, drops the newest when the weather dries up),
	/// every drop falls — straight for rain, slow and swaying for snow — and wraps back to the top of the
	/// volume around <paramref name="centre"/> (the view target). A delta of 0 (a paused park) freezes
	/// everything in place.
	/// </summary>
	// [APPROX:AUDIO-903] a paused park (delta 0) freezes the drops and bolts mid-flight; the original's pause behaviour for the weather renderer is not traced
	public void Step( float deltaSeconds, int targetDrops, bool snow, Vector3 centre )
	{
		if ( !float.IsFinite( deltaSeconds ) || deltaSeconds <= 0 )
			return;
		Elapsed += deltaSeconds;
		var target = Math.Clamp( targetDrops, 0, MaximumDrops );
		while ( drops.Count < target )
			drops.Add( new WeatherDrop( RandomInVolume( centre ), random.Next( 360 ) * (MathF.PI / 180f) ) );
		if ( drops.Count > target )
			drops.RemoveRange( target, drops.Count - target );

		var speed = snow ? SnowFallSpeed : RainFallSpeed;
		var omega = 2f * MathF.PI / SnowSwaySeconds;
		for ( var index = 0; index < drops.Count; index++ )
		{
			var drop = drops[index];
			var position = drop.Position;
			position.Z -= speed * deltaSeconds;
			if ( snow )
				position.X += MathF.Cos( drop.Phase + Elapsed * omega ) * SnowSwaySpeed * deltaSeconds;
			drops[index] = new WeatherDrop( WrapIntoVolume( position, centre ), drop.Phase );
		}

		for ( var index = flashes.Count - 1; index >= 0; index-- )
		{
			var age = flashes[index].Age + deltaSeconds;
			if ( age >= BoltFlashSeconds )
				flashes.RemoveAt( index );
			else
				flashes[index] = flashes[index] with { Age = age };
		}
	}

	/// <summary>Shows a freshly struck bolt between its engine endpoints, replacing the oldest flash past the cap.</summary>
	public void AddBoltFlash( Vector3 top, Vector3 bottom )
	{
		if ( flashes.Count >= MaximumBoltFlashes )
			flashes.RemoveAt( 0 );
		flashes.Add( new WeatherBoltFlash( top, bottom, 0f, strikes++ ) );
	}

	/// <summary>
	/// The bolt's jittered polyline from top to bottom: <see cref="BoltPointCount"/> points, the endpoints exact
	/// and the between points pushed sideways by a taper that peaks mid-bolt. The jitter comes from the strike's
	/// number and the point's index, so the same strike always draws the same bolt.
	/// </summary>
	// [APPROX:AUDIO-902] the bolt is a jittered polyline between the strike's top (height 300, FUN_00512c50) and the terrain, shown for a fixed flash time; the original's bolt shape, segmenting and flash timing are not traced
	public Vector3[] BoltPoints( WeatherBoltFlash flash )
	{
		var points = new Vector3[BoltPointCount];
		for ( var index = 0; index < BoltPointCount; index++ )
		{
			var along = index / (float)(BoltPointCount - 1);
			var taper = 1f - MathF.Abs( 1f - 2f * along );
			var jitter = JitterOf( flash.Strike, index ) * BoltJitter * taper;
			var position = flash.Top + (flash.Bottom - flash.Top) * along;
			points[index] = new Vector3( position.X + jitter, position.Y + JitterOf( flash.Strike, index + BoltPointCount ) * BoltJitter * taper, position.Z );
		}
		return points;
	}

	private Vector3 RandomInVolume( Vector3 centre )
	{
		var radius = VolumeRadius;
		return new Vector3(
			centre.X + (random.Next( 1_000_001 ) / 1_000_000f) * 2f * radius - radius,
			centre.Y + (random.Next( 1_000_001 ) / 1_000_000f) * 2f * radius - radius,
			VolumeBottom( centre ) + (random.Next( 1_000_001 ) / 1_000_000f) * VolumeHeight );
	}

	private static float VolumeBottom( Vector3 centre ) => centre.Z - VolumeBottomOffset;

	/// <summary>Re-anchors a position into the wrapping volume around the centre (falls out the bottom, back in at the top).</summary>
	private static Vector3 WrapIntoVolume( Vector3 position, Vector3 centre )
	{
		float Wrap( float offset, float span ) => ((offset % span) + span) % span;
		var radius = VolumeRadius;
		return new Vector3(
			centre.X + Wrap( position.X - centre.X + radius, 2f * radius ) - radius,
			centre.Y + Wrap( position.Y - centre.Y + radius, 2f * radius ) - radius,
			VolumeBottom( centre ) + Wrap( position.Z - VolumeBottom( centre ), VolumeHeight ) );
	}

	/// <summary>A deterministic sideways jitter in −1..1 for one point of one strike.</summary>
	private static float JitterOf( int strike, int index )
	{
		ulong z = (ulong)(strike * 0x9E37 + index * 0x85EB) + 0x9E3779B9UL;
		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
		return ((z ^ (z >> 31)) & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f;
	}
}
