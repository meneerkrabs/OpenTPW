using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// What the weather renderer reads each frame, so the same renderer can serve the park (<see cref="ParkWeather"/>)
/// and, later, the lobby without knowing either. Positions are ORIGINAL units, Y up (the units of the shipped data).
/// </summary>
public interface IWeatherVisualSource
{
	/// <summary>The simulated current drops; the renderer draws the smaller of this and the 1024-point field.</summary>
	int DropCount { get; }

	/// <summary>Whether the field is snow rather than rain.</summary>
	bool Snow { get; }

	/// <summary>How long a bolt stays alive, in seconds (<c>WeatherEffects.LightningTime</c> / 1000).</summary>
	float BoltLifetimeSeconds { get; }

	/// <summary>The strikes since the last call: each bolt's top and bottom (original units, Y up).</summary>
	IReadOnlyList<WeatherStrike> TakeStrikes();
}

/// <summary>One strike handed to the renderer: <c>AddLightning(bottom, top, …)</c>'s two points, in original units with Y up.</summary>
public readonly record struct WeatherStrike( NVector3 Top, NVector3 Bottom )
{
	/// <summary>A strike without a ground point of its own (the lobby, tests): the ground point at y = 0 straight below the top.</summary>
	public static WeatherStrike Below( NVector3 top ) => new( top, new NVector3( top.X, WeatherVisualState.BoltGroundHeight, top.Z ) );
}

/// <summary>
/// The camera as the weather field needs it: the eye and the view rotation, both in original units with Y up.
/// <see cref="Right"/>, <see cref="Up"/> and <see cref="Forward"/> are the camera's axes expressed in world axes, so
/// a world direction <c>d</c> becomes camera space as <c>(Right·d, Up·d, Forward·d)</c>; the third component is the depth.
/// </summary>
public readonly record struct WeatherCamera( NVector3 Eye, NVector3 Right, NVector3 Up, NVector3 Forward, bool EyeMode = false )
{
	/// <summary>Rotates a world direction into camera space (x right, y up, z forward = depth).</summary>
	public NVector3 ToCamera( NVector3 direction ) => new( NVector3.Dot( Right, direction ), NVector3.Dot( Up, direction ), NVector3.Dot( Forward, direction ) );

	/// <summary>Straight ahead along +Z, the camera's depth axis, with the eye at the origin (tests).</summary>
	public static WeatherCamera Looking( NVector3 eye ) => new( eye, NVector3.UnitX, NVector3.UnitY, NVector3.UnitZ );
}

/// <summary>
/// One vertex of a rain streak or snow flake as tp.exe hands it to its polygon submit (<c>0x565BB0</c>): homogeneous clip coordinates (x, y, z in <see cref="Position"/>, the fourth word in <see cref="W"/>),
/// y running down the screen, a grey level, an alpha and a texture coordinate (v as OpenTPW's upright textures read it).
/// </summary>
public readonly record struct WeatherVertex( NVector3 Position, float W, float Grey, float Alpha, NVector2 TexCoords );

/// <summary>One straight piece of a bolt in original world units, and the branch depth it belongs to (0 is the trunk).</summary>
public readonly record struct WeatherBoltSegment( NVector3 A, NVector3 B, int Depth );

/// <summary>
/// One call of the walk to the ribbon emitter (<c>0x57DD10</c>): a point pair at a branch depth, the closing call of a polyline (<see cref="First"/> null) or the start of a branch's polyline
/// (<see cref="StartsBranch"/>, the original's reset of that depth's counter).
/// </summary>
public readonly record struct WeatherBoltCall( NVector3? First, NVector3 Second, int Depth, bool StartsBranch = false );

/// <summary>One live bolt: its fixed shape, its age, and the flicker term of its current colour.</summary>
public sealed class WeatherBolt
{
	internal WeatherBolt( IReadOnlyList<WeatherBoltSegment> segments, IReadOnlyList<WeatherBoltCall> calls, float lifetime )
	{
		Segments = segments;
		Calls = calls;
		Lifetime = lifetime;
	}

	public IReadOnlyList<WeatherBoltSegment> Segments { get; }
	/// <summary>The emitter calls of the walk in order: the ribbon follows these, not the unordered segments.</summary>
	public IReadOnlyList<WeatherBoltCall> Calls { get; }
	public float Age { get; internal set; }
	public float Lifetime { get; }

	/// <summary>The random 0..63 added to the pulse, redrawn every frame.</summary>
	public int Flicker { get; internal set; }
}

/// <summary>
/// The park's weather as the original draws it: the engine's rain renderer <c>CRain</c> (docs/AUDIO.md "Weather visuals").
/// This is the testable, GPU-free state: the 1024-point drop field, its scrolling, the rain streaks, the snow flakes and
/// the lightning bolts, all as vertices in camera space (or bolt segments in world space). The counts and lifetimes follow
/// the simulated weather through <see cref="IWeatherVisualSource"/>; every random draw comes from this class's own stream
/// (<see cref="VisualSeed"/>), never from a simulation stream.
/// </summary>
// [BIN:STP-PPC:engine_shared 0x1001CF38 CRain::GameLoad] loads Data/Generic/Weather/raindrop.tga (32x32, 24-bit, white teardrop on black), snow.tga (128x128 RGBA, 2x2 atlas of four flakes) and lightning.tga (128x128 RGBA, vertical strip, white core, blue glow)
// [BIN:STP-PPC:engine_shared 0x1001C884 CRain field] the drop field is 1024 fixed random points in a box of -4..4 on every axis (original units), centred on the camera eye, in camera space through the view rotation only, wrapping per axis; the drawn count is min(CurrentDrops, MaxRaindrops) of the 1024
// [BIN:STP-PPC:engine_shared 0x1001FCE4 CRain scroll] the box scrolls by direction x velocity x dt plus a camera-position parallax term; rain velocity 2.5 units/s (the game sets it; the engine default is 5), snow 0.8; the direction is effectively normalize(0.2w, -1, 0.2w) with |w| <= 0.0017, i.e. straight down
// [BIN:STP-PPC:engine_shared 0x1001ECD0 CRain::DrawRain] streak from p to p + 0.1 x dir as a camera-space quad 0.2 x length wide; grey c = 3 x max(0, int(12 x (6 - depth))) (216 at the eye, 0 at depth 6); alpha 0x80 at the trailing end and 0xFF at the leading end; only depth > 0 is drawn
// [BIN:TPW-EXE:0x57F1F0-0x57F380] the Windows executable's CRain::Draw agrees with the Mac engine on the field, the colour ramp and the streak
// [BIN:STP-PPC:engine_shared 0x1001F198 CRain::DrawSnow] same 1024 points and count; each flake is a triangle of half-size 0.1 mapped to atlas quadrant (index & 3), full white, its screen x and y spread by (1 + depth/8); an engine flag (0x115E94 bit 8) swirls the flakes along a closed 128-point cubic spline, otherwise they jitter by +-1/128 per frame
// [BIN:STP-PPC:engine_shared 0x1001FCE4 dt] dt is the wall-clock frame time in ms capped at 500 ms, zeroed by an engine flag
// [BIN:TPW-EXE:0x57fff8 rain parallax] the factor (CRain +0x3340, 0.1 from the init at 0x57d1fc) is 0.3 while the camera-mode byte 0x790ab0 has bits 0x16 (0x580003-0x58000b); per axis the field offset is wrap(factor x camera position + 4) - 4 into [-4, 4) (x and z use +factor, y uses -factor; the wrap is x16384 & 0x1FFFF, 0x580013-0x5800b7), an absolute position, not a per-frame delta
// [BIN:TPW-EXE:0x57efd0 rain draw] the drop is (p.x - o.x, o.y + p.y, p.z - o.z) with o the scroll plus the wrapped parallax, whose y carries -factor: every axis moves by -factor x the camera's position, so the field here is wrapped (fall - 0.1 x eye)
// [BIN:TPW-EXE:0x57fff8 eye views] the factor 0.3 applies in the park's first-person views, the ones that raise bits 0x16 of the camera-mode byte 0x790ab0: the terrain camcorder (bit 0x2, toggled by 0x42AB20 from input event 2; OpenTPW's free walk) and the ride follow (bit 0x4, event 4); no instruction sets 0x10, so the factor is 0.1 in the overview and the lobby (WeatherRenderer.IsEyeView)
// [BIN:TPW-EXE:0x57f490 CRain snow] the swirl is selected by engine flag 0x8bcbc8 bit 0x100, which no instruction sets (the word is .bss, and its writers, among them 0x44d8f0, 0x40c830, 0x54fae9, 0x566840, 0x5673d0, 0x576f07, 0x583d30, 0x587950, set other bits or restore saved copies), so the PC always draws the jitter version: each frame every axis gets ((rand & 31) - 16) / 2048 before the camera transform; each flake is one triangle: apex (x, y + h) at the top middle of its atlas quadrant's row, base corners (x +- w, y - h) at the row's lower corners, h the half-size (+0x3354)
// [BIN:TPW-EXE:0x57f490 flake vertex] the vertex is (x, y, z + 0.1, w = z + 0.1501) in homogeneous clip coordinates handed straight to the polygon submit 0x565bb0 (it clips with the clip codes 0x561bc0, which compare |x|, |y|, |z| against w, and the viewport 0x5796a0 lands it at x / w and y / w of the half screen), x and y being the spread camera-space position (y negated); the flake's base half-width h x [0x8bcbcc] (height / width) in that clip x keeps it as wide as it is tall on the screen, and the position itself is not multiplied by the projection's or the screen matrix's factors (the weather is not the world: a flake sits at x s / w of the half screen where a world point at the same camera-space offset would sit at 2 a x / z of it)
public sealed class WeatherVisualState
{
	/// <summary>The presentation field's draw stream seed; it shares nothing with the park's simulation streams.</summary>
	public const ulong VisualSeed = 0x51EA7E5D5EEDUL;

	/// <summary>The fixed number of random points in the drop field.</summary>
	public const int FieldSize = 1024;
	/// <summary>Half the box's edge: the points lie in -4..4 on every axis (original units).</summary>
	public const float BoxHalfExtent = 4f;
	/// <summary>The game's rain velocity in original units per second.</summary>
	public const float RainVelocity = 2.5f;
	/// <summary>The snow velocity in original units per second.</summary>
	public const float SnowVelocity = 0.8f;
	/// <summary>Weight of the camera's movement in the field's scroll.</summary>
	public const float ParallaxScale = 0.1f;
	/// <summary>The weight while the camera is in one of the original's eye modes (camera-mode byte 0x790AB0 bits 0x16).</summary>
	public const float EyeModeParallaxScale = 0.3f;
	/// <summary>The wrapped parallax term's grid: the original truncates it to 1/16384 of a unit.</summary>
	public const float ParallaxGrid = 16384f;
	/// <summary>The frame time cap: a hitch longer than this moves the field by 500 ms only.</summary>
	public const float MaximumStepSeconds = 0.5f;
	/// <summary>A rain streak's length along its fall, in original units.</summary>
	public const float StreakLength = 0.1f;
	/// <summary>A rain streak's half-width vector is this times the streak's own projected vector turned a quarter (0x701D00 = 0.1): a streak is 0.2 x its length wide.</summary>
	public const float StreakHalfWidthFactor = 0.1f;
	/// <summary>The rain vertex's clip z is the trailing end's depth plus this (0x701D0C is -0.04, subtracted).</summary>
	public const float RainDepthBias = 0.04f;
	/// <summary>The rain vertex's clip w is its own end's depth plus this (0x701D10 is -0.1, subtracted).</summary>
	public const float RainW = 0.1f;
	/// <summary>Depth at which the rain's grey ramp reaches 0.</summary>
	public const float RainFadeDepth = 6f;
	/// <summary>Half the edge of a snow flake, in original units.</summary>
	public const float SnowHalfSize = 0.1f;
	/// <summary>Per-frame random jitter of a flake, in original units: ((rand &amp; 31) - 16) / 2048, -1/128 to 15/2048.</summary>
	public const float SnowJitter = 1f / 2048f;
	/// <summary>Divisor of the snow's depth spread: x and y scale by 1 + depth / 8.</summary>
	public const float SnowSpreadDepth = 8f;
	/// <summary>The original adds this to a flake's depth to form the vertex's w, so the flake projects slightly farther than its depth.</summary>
	public const float SnowProjectionBias = 0.1501f;
	/// <summary>The original adds this to a flake's depth to form the vertex's clip z.</summary>
	public const float SnowDepthBias = 0.1f;
	/// <summary>Vertices a snow flake takes: one triangle.</summary>
	public const int SnowVerticesPerFlake = 3;

	/// <summary>Bolts alive at once; a newer strike past this replaces the oldest.</summary>
	public const int MaximumBolts = 16;
	/// <summary>Ground height of every bolt in original units: the original's bolt reaches y = 0, not the terrain.</summary>
	public const float BoltGroundHeight = 0f;
	/// <summary>The midpoint displacement's roughness.</summary>
	public const float BoltRoughness = 0.6f;
	/// <summary>Deepest branch level.</summary>
	public const int MaximumBranchDepth = 4;
	/// <summary>The pulse cycle of a bolt's colour.</summary>
	public const float BoltPulseSeconds = 0.5f;
	/// <summary>Half-width of a bolt's ribbon at branch depth 0, in original units; deeper branches get 1.3 / (depth + 1).</summary>
	public const float BoltHalfWidth = 1.3f;
	/// <summary>Bound on the segments of one bolt, so the GPU buffer is sized once.</summary>
	public const int MaximumBoltSegments = 1024;

	/// <summary>The original's wind term |w| &lt;= 0.0017 tilts the fall by under 0.04 degrees: straight down.</summary>
	private static readonly NVector3 FallDirection = -NVector3.UnitY;

	private readonly DeterministicRandom random = new( VisualSeed );
	private readonly NVector3[] points = new NVector3[FieldSize];
	private readonly NVector3[] jitter = new NVector3[FieldSize];
	private readonly List<WeatherBolt> bolts = new();
	private NVector3 offset;
	private NVector3 fall;
	private int drawn;
	private bool snow;

	public WeatherVisualState()
	{
		// 1024 fixed random points in the box, stored 0..8 so the wrap is a plain modulo.
		for ( var index = 0; index < FieldSize; index++ )
			points[index] = new NVector3( Unit(), Unit(), Unit() ) * (2 * BoxHalfExtent);
	}

	private float Unit() => random.Next( 1 << 20 ) / (float)(1 << 20);
	private float JitterStep() => random.Next( 32 ) - 16;

	/// <summary>Drops drawn this frame: min(source drops, 1024).</summary>
	public int DropCount => drawn;
	public bool Snow => snow;
	public IReadOnlyList<WeatherBolt> Bolts => bolts;

	/// <summary>This frame's jitter of the flake at <paramref name="index"/>, in original units.</summary>
	internal NVector3 JitterOf( int index ) => jitter[index];

	/// <summary>The scroll of the box, each axis in 0..8.</summary>
	public NVector3 Offset => offset;

	/// <summary>
	/// The step the field takes for a frame: wall-clock seconds scaled by the simulation's time scale and capped at
	/// <see cref="MaximumStepSeconds"/>, zero while paused (scale 0) or for a non-finite or negative delta.
	/// </summary>
	// [BIN:TPW-EXE:0x57ff60 CRain dt] dt is min(now - last, 500) ms x 0.001 on the clock 0x402d70: the timer 0x785970's rate-scaled accumulator (0x4033a0, rate +0x20, 0.25..2 by the speed keys) minus its paused time, which stands still while paused (0x4030d0); OpenTPW's simulation time scale plays that rate
	// [BIN:TPW-EXE:0x57ffb1 dt flag] the test is the mask 0x4 on the engine word 0x8bd508 (mov al, 4); nothing sets that bit: the word is .bss (zero at the start), the direct writers set 0x1 (0x54e855 with ebx = 1, 0x58744f), 0x40 (0x44ddc3), 0x80000000, 0x8000000 and 0x3000141 (0x583555, which clears it), the members called on it (0x5836b0 sets 0x40000000, 0x5835c0 prints) leave it alone, and the other stores (0x564342, 0x577326, 0x577446, 0x583893) put back copies of the word itself, so the original never zeroes the weather's dt for it and OpenTPW does not either
	/// <summary>The unscaled step for the other presentation clocks (the particles): wall-clock seconds capped at <see cref="MaximumStepSeconds"/>, zero while paused.</summary>
	public static float FrameSeconds( float deltaSeconds, bool paused ) => FrameSeconds( deltaSeconds, paused ? 0f : 1f );

	public static float FrameSeconds( float deltaSeconds, float timeScale ) =>
		!(timeScale > 0) || !float.IsFinite( timeScale ) || !float.IsFinite( deltaSeconds ) || deltaSeconds <= 0 ? 0f : MathF.Min( deltaSeconds * timeScale, MaximumStepSeconds );

	/// <summary>
	/// One axis of the original's wrapped parallax: ((trunc((value + 4) x 16384) &amp; 0x1FFFF) / 16384) - 4, a value in [-4, 4) with the
	/// camera's position times the factor wrapped on a 1/16384 grid (the integer is truncated toward zero before the mask).
	/// </summary>
	// [BIN:TPW-EXE:0x580013-0x5800B7 parallax quantisation] value - (-4) times 16384 through __ftol (0x67A830), and 0x1FFFF, times 1/16384 (0x701C60), minus 4 (0x701CF0), for each of the camera matrix's position floats at +0x30, +0x34 (negated) and +0x38
	internal static float QuantisedParallax( float value ) => ((int)((value + BoxHalfExtent) * ParallaxGrid) & 0x1FFFF) / ParallaxGrid - BoxHalfExtent;

	/// <summary>The drop point's position relative to the eye, in world axes (original units): the scrolled point wrapped into -4..4.</summary>
	public NVector3 DropOffset( int index )
	{
		static float Wrap( float value ) => value - MathF.Floor( value / (2 * BoxHalfExtent) ) * (2 * BoxHalfExtent);
		var point = points[index] + offset;
		return new NVector3( Wrap( point.X ), Wrap( point.Y ), Wrap( point.Z ) ) - new NVector3( BoxHalfExtent );
	}

	/// <summary>
	/// Advances the field by <paramref name="deltaSeconds"/> (already capped, see <see cref="FrameSeconds"/>) and
	/// reads the source's drop count and weather kind. The box scrolls along the fall direction at the rain or snow
	/// velocity and by the camera's parallax; bolts age and expire. <paramref name="drawFrame"/> is true for a frame that
	/// redraws the random snow jitter and the bolts' flicker (see the note in the body).
	/// </summary>
	public void Update( float deltaSeconds, IWeatherVisualSource source, WeatherCamera camera, bool drawFrame = true )
	{
		drawn = Math.Clamp( source.DropCount, 0, FieldSize );
		snow = source.Snow;

		if ( deltaSeconds > 0 )
			fall += FallDirection * ((snow ? SnowVelocity : RainVelocity) * deltaSeconds);
		// the original draws a new jitter for every flake in every draw, paused or not
		// [EXT:frame-driven-at-60hz] a draw of the original is a rendered frame (0x54e360), so the jitter changes once per 60 Hz reference frame (drawFrame), whatever OpenTPW's own frame rate; the scroll and the bolts' age follow the real time
		if ( snow && drawFrame )
			for ( var index = 0; index < drawn; index++ )
				jitter[index] = new NVector3( JitterStep(), JitterStep(), JitterStep() ) * SnowJitter;
		var span = 2 * BoxHalfExtent;
		static NVector3 Wrap( NVector3 value, float span ) => new( value.X - MathF.Floor( value.X / span ) * span, value.Y - MathF.Floor( value.Y / span ) * span, value.Z - MathF.Floor( value.Z / span ) * span );
		fall = Wrap( fall, span );
		var factor = camera.EyeMode ? EyeModeParallaxScale : ParallaxScale;
		// x and z subtract the wrapped term, y adds the one wrapped from the negated position: every axis moves by -factor x the position.
		var parallax = new NVector3( -QuantisedParallax( factor * camera.Eye.X ), QuantisedParallax( -factor * camera.Eye.Y ), -QuantisedParallax( factor * camera.Eye.Z ) );
		offset = Wrap( fall + parallax, span );

		for ( var index = bolts.Count - 1; index >= 0; index-- )
		{
			var bolt = bolts[index];
			bolt.Age += deltaSeconds;
			if ( bolt.Age >= bolt.Lifetime )
				bolts.RemoveAt( index );
			else if ( deltaSeconds > 0 && drawFrame )
				bolt.Flicker = random.Next( 64 );
		}

		foreach ( var strike in source.TakeStrikes() )
			AddBolt( strike.Top, strike.Bottom, source.BoltLifetimeSeconds );
	}

	/// <summary>Starts a bolt from <paramref name="top"/> (original units) down to y = 0 directly below it.</summary>
	// [BIN:STP-PPC:0x101035D4 bolt start] the strike hands the renderer the sky point (x, 300, z); up to 16 bolts are live
	// [BIN:TPW-EXE:0x512C50 bolt ground] the ground point is at original y = 0, not on the terrain: x and z are the draws the strike made before the sky point's offset of up to 50 (ParkWeather hands both); a lobby strike without a ground point falls back to the point straight below the top
	// [BIN:STP-PPC:engine_shared 0x1001EA18 bolt shape] recursive midpoint displacement with roughness 0.6, a 9/512 branch chance per split up to depth 4 and a stop threshold of 6 + 2u (u in [-1,1)); the PC's 0x57E810/0x57E4B0 (BuildBolt) agree on those numbers and differ in the displacement and branch heading, which BuildBolt follows
	public WeatherBolt AddBolt( NVector3 top, float lifetimeSeconds ) => AddBolt( top, WeatherStrike.Below( top ).Bottom, lifetimeSeconds );

	/// <summary>Starts a bolt between <paramref name="top"/> and <paramref name="bottom"/> (original units): the lobby hands both.</summary>
	public WeatherBolt AddBolt( NVector3 top, NVector3 bottom, float lifetimeSeconds )
	{
		var (segments, calls) = Build( top, bottom, random.NextUInt64() );
		var bolt = new WeatherBolt( segments, calls, lifetimeSeconds );
		if ( bolts.Count >= MaximumBolts )
			bolts.RemoveAt( 0 );
		bolts.Add( bolt );
		return bolt;
	}

	/// <summary>
	/// A bolt's fixed shape: the same seed always gives the same segments. The shape is tp.exe's recursive midpoint
	/// displacement <c>0x57E810</c> with the displacement of <c>0x57E4B0</c>, run on the original's own stream
	/// (<see cref="CRainRandom"/>); <paramref name="top"/> is the sky end and <paramref name="bottom"/> the ground end.
	/// </summary>
	// [BIN:TPW-EXE:0x57EC50 bolt walk] the walk starts at depth 0 with the sky point first and the ground point second (0x57EE30 passes the record's second point, the sky one, as the first argument), the branch depth limit 4 (+0xA1A0, set by 0x57EE30 for a seeded bolt) and the roughness 0.6 (+0x3310 = 0x3F19999A, init 0x57D2AB)
	// [BIN:TPW-EXE:0x57E810 split] each call draws the midpoint (0x57E4B0), then one word for the branch ((word & 0x1FF) < 9 and depth < 4), then one word for the stop threshold: threshold = 6 (+0xA180) + ((word & 0xFFF) - 0x800) x 2 (+0xA184) / 2048, i.e. 4 to 8; each half (midpoint to the far end, then the near end to the midpoint) splits again when its squared length is at least the threshold and is a final segment otherwise, so the test runs on the halves, not on the whole piece
	// [BIN:TPW-EXE:0x57E810 branch] a branch takes depth + 1, scale 1 / (96 x depth) and two words: along-segment = ((w1 & 0x3FF) + 0x200) x scale x 2.5, along-bolt = ((w2 & 0x3FF) + 0x200) x scale; its heading is along-bolt x the bolt's unit direction (record +0x18, ground minus sky) + along-segment x the segment's unit direction, and when that points against the bolt direction the along-bolt term is halved and the sum is the halved term x the bolt direction + half the first heading; the branch starts at the segment's near end and runs along that heading, splitting from there
	public static IReadOnlyList<WeatherBoltSegment> BuildBolt( NVector3 top, NVector3 bottom, ulong seed ) => Build( top, bottom, seed ).Segments;

	/// <summary>The bolt's final segments and the ordered emitter calls of its walk (the ribbon's input), from the same walk.</summary>
	// [BIN:TPW-EXE:0x57EC50 bolt emit order] the walk calls the emitter 0x57DD10 with (midpoint, far) and then (near, midpoint) for every final half, a branch resets its depth's counter, walks from the branch end to the segment's near end and closes with a call (0, branch end) at that depth; the trunk closes with (0, near) at depth 0 (the sky point)
	internal static (IReadOnlyList<WeatherBoltSegment> Segments, IReadOnlyList<WeatherBoltCall> Calls) Build( NVector3 top, NVector3 bottom, ulong seed )
	{
		var rng = new CRainRandom( (uint)(seed ^ (seed >> 32)) | 1u );
		var segments = new List<WeatherBoltSegment>();
		var calls = new List<WeatherBoltCall>();
		var boltDirection = bottom - top;
		var boltLength = boltDirection.Length();
		if ( boltLength != 0 )
			boltDirection /= boltLength;
		var depth = 0;
		// Guards of OpenTPW's fixed vertex buffer (the original grows its draw list): a piece that cannot split any further within the budget is dropped.
		var walks = 0;

		void Leaf( NVector3 a, NVector3 b )
		{
			if ( segments.Count >= MaximumBoltSegments )
				return;
			segments.Add( new WeatherBoltSegment( a, b, depth ) );
			calls.Add( new WeatherBoltCall( a, b, depth ) );
		}

		void Walk( NVector3 near, NVector3 far )
		{
			if ( ++walks > 4 * MaximumBoltSegments )
				return;
			var middle = Midpoint( ref rng, far, near );

			if ( (rng.Next() & 0x1FF) < 9 && depth < MaximumBranchDepth )
			{
				depth++;
				calls.Add( new WeatherBoltCall( null, default, depth, StartsBranch: true ) );
				var scale = 1f / (depth * 96);
				var along = far - near;
				var alongLength = along.Length();
				if ( alongLength != 0 )
					along /= alongLength;
				var alongSegment = ((rng.Next() & 0x3FF) + 0x200) * scale * 2.5f;
				var alongBolt = ((rng.Next() & 0x3FF) + 0x200) * scale;
				var heading = boltDirection * alongBolt + along * alongSegment;
				if ( NVector3.Dot( boltDirection, heading ) < 0 )
				{
					alongBolt *= 0.5f;
					heading = boltDirection * alongBolt + heading * 0.5f;
				}
				var branchEnd = near + heading;
				Walk( branchEnd, near );
				calls.Add( new WeatherBoltCall( null, branchEnd, depth ) );
				depth--;
			}

			var threshold = BoltStopBase + (int)((rng.Next() & 0xFFF) - 0x800) * BoltStopSpread * (1f / 2048f);
			if ( threshold <= (middle - far).LengthSquared() )
				Walk( middle, far );
			else
				Leaf( middle, far );
			if ( threshold <= (middle - near).LengthSquared() )
				Walk( near, middle );
			else
				Leaf( near, middle );
		}

		Walk( top, bottom );
		calls.Add( new WeatherBoltCall( null, top, 0 ) );
		return (segments, calls);
	}

	/// <summary>Squared length below which a half of a bolt stops splitting, before its draw (+0xA180).</summary>
	public const float BoltStopBase = 6f;
	/// <summary>Spread of that threshold's draw (+0xA184): the threshold is 6 + (-1..1) x 2.</summary>
	public const float BoltStopSpread = 2f;

	/// <summary>
	/// A split's new midpoint, tp.exe's <c>0x57E4B0</c>: five draws, the displacement amplitude |segment| x roughness x r2 x r1^2
	/// (r the signed remainder of a draw over 0x1FFF divided by 8192, a draw's bit 0 flipping the sign) along the vector P, which is the
	/// segment's midpoint M with the component of the axis the third pick selects replaced by M.x - |M|^2 / segment.axis (the
	/// original's axis scan order 0x57E6xx), normalised.
	/// </summary>
	// [BIN:TPW-EXE:0x57E4B0 midpoint] draws: r1 = (s % 0x1FFF) / 8192, r2 = (s % 0x1FFF) / 8192 x r1 x r1, a sign draw (state bit 0 flips r2), a draw that is discarded, and the axis pick (s % 3, a signed remainder, so a negative state lands in the z-first case with 2); M = (a + b) / 2, d = b - a; the displaced point is M + P x (|d| x 0.6 x r) / |P|, the cross product d x P being scaled by 0 / |d x P| in the original (0x701CA0 is 0.0), so it adds nothing
	internal static NVector3 Midpoint( ref CRainRandom rng, NVector3 a, NVector3 b )
	{
		var middle = (a + b) * 0.5f;
		var d = b - a;
		var middleSquared = middle.LengthSquared();
		var r1 = (int)rng.Next() % 0x1FFF * (1f / 8192f);
		var amplitude = (int)rng.Next() % 0x1FFF * (1f / 8192f) * r1 * r1;
		if ( (rng.Next() & 1) != 0 )
			amplitude = -amplitude;
		rng.Next();
		var pick = (int)rng.Next() % 3;

		var p = middle;
		// The scan order of the axis cases: x first for pick 0, y first for pick 1 (falling to z then x), z first otherwise (then x, then y).
		bool UseX() { p.X = middle.X - middleSquared / d.X; return true; }
		bool UseZ() { p.Z = middle.Z - middleSquared / d.Z; return true; }
		bool UseY() { p.Y = middle.Y - middleSquared / d.Y; return true; }
		if ( pick == 0 )
		{
			if ( d.X != 0 ) UseX();
			else if ( d.Y == 0 ) UseZ();
			else UseY();
		}
		else if ( pick == 1 )
		{
			if ( d.Y == 0 )
			{
				if ( d.Z != 0 ) UseZ();
				else UseX();
			}
			else UseY();
		}
		else
		{
			if ( d.Z != 0 ) UseZ();
			else if ( d.X != 0 ) UseX();
			else UseY();
		}

		var scale = d.Length() * BoltRoughness * amplitude / p.Length();
		var result = middle + p * scale;
		// A degenerate piece (a zero P or a zero axis divisor) gives the original NaN; OpenTPW keeps the midpoint.
		return float.IsFinite( result.X ) && float.IsFinite( result.Y ) && float.IsFinite( result.Z ) ? result : middle;
	}

	/// <summary>
	/// The bolt's colour grey 0..1, tp.exe's <c>0x57EE30</c>: with x = (remaining ms mod 500) / 500 the base is 255 - trunc((0.9 - x) x 71.04)
	/// below 0.9 and 192 + trunc((1 - x) x 640) from 0.9 up, plus the frame's rand &amp; 63; the sum is written into the colour's three
	/// channels through an OR with 0xFFFFFF00, so a sum of 256 or more wraps to its low byte (the bolt flickers dark).
	/// </summary>
	// [BIN:TPW-EXE:0x57EEBA-0x57EF61 bolt colour] x = (remaining % 500) x 0.002; x < 0.9: 255 - ftol((0.9 - x) x 1.11 x 64); else 192 - ftol((1 - x) x 10 x -64); the sum with (rand & 0x3F) is ORed into 0xFF000000 | v << 16 | v << 8 | v as ((v | 0xFFFFFF00) << 8 | v) << 8 | v, so bit 8 of v is lost
	public static float BoltGrey( WeatherBolt bolt )
	{
		var remaining = Math.Max( 0, (int)MathF.Round( bolt.Lifetime * 1000f ) - (int)(bolt.Age * 1000f) );
		var x = remaining % 500 * 0.002f;
		var baseGrey = x < 0.9f
			? 255 - (int)((0.9f - x) * 1.11f * 64f)
			: 192 - (int)((1f - x) * 10f * -64f);
		var value = (uint)(baseGrey + bolt.Flicker);
		var word = (((value | 0xFFFFFF00u) << 8 | value) << 8) | value;
		return (word & 0xFFu) / 255f;
	}

	/// <summary>A bolt branch's ribbon half-width in original units: 1.3 / (depth + 1).</summary>
	public static float BoltWidth( int branchDepth ) => BoltHalfWidth / (branchDepth + 1);

	/// <summary>The rain's grey 0..1 at a camera-space depth: 3 x max(0, int(12 x (6 - depth))) over 255.</summary>
	public static float RainGrey( float depth ) => 3 * Math.Max( 0, (int)(12f * (RainFadeDepth - depth)) ) / 255f;

	/// <summary>
	/// Writes the rain streaks of the drawn drops as clip-space triangles (six vertices a streak, the fan (0, 1, 2) and (0, 2, 3) of the original's four vertices) and returns the vertex count. A streak is skipped
	/// when its trailing end is at or behind the eye (the leading end is not tested); the grey follows the trailing end's depth for both ends and the alpha runs from 0x80 at the trailing end to 0xFF at the leading end.
	/// </summary>
	// [BIN:TPW-EXE:0x57F1F0 rain vertex] the vertices are submitted straight to the polygon path, no projection: with A the trailing end in camera space (y negated, the screen runs down) and d = B - A the streak (0.1 along the fall), p = 0.1 d.x and q = 0.1 d.y: V0 = A + (q, -p), V1 = A - (q, -p), V2 = B - (q, -p), V3 = B + (q, -p), the same z = A.z + 0.04 (0x701D0C) on all four and w = z of the vertex's own end + 0.1 (0x701D10: A.z on V0 and V1, B.z on V2 and V3), the colour 3c in each channel with c = max(0, int(12 (6 - A.z))) and alpha 0x80 on V0 and V1 and 0xFF on V2 and V3 (the leading end's colour uses the trailing end's depth too), only A.z > 0 tested
	public int WriteRain( WeatherCamera camera, Span<WeatherVertex> output )
	{
		var fall = camera.ToCamera( FallDirection ) * StreakLength;
		var count = 0;
		for ( var index = 0; index < drawn && count + 6 <= output.Length; index++ )
			count += WriteStreak( camera.ToCamera( DropOffset( index ) ), fall, output[count..] );
		return count;
	}

	/// <summary>One streak from its trailing end and its (camera-space) fall vector: six vertices, or none when the trailing end is not in front of the eye.</summary>
	internal static int WriteStreak( NVector3 trailing, NVector3 fall, Span<WeatherVertex> output )
	{
		if ( trailing.Z <= 0 )
			return 0;
		var p = StreakHalfWidthFactor * fall.X;
		var q = StreakHalfWidthFactor * -fall.Y;
		var grey = RainGrey( trailing.Z );
		var z = trailing.Z + RainDepthBias;
		var wTrailing = trailing.Z + RainW;
		var wLeading = trailing.Z + fall.Z + RainW;
		var startX = trailing.X;
		var startY = -trailing.Y;
		var endX = trailing.X + fall.X;
		var endY = -(trailing.Y + fall.Y);
		var v0 = new WeatherVertex( new NVector3( startX + q, startY - p, z ), wTrailing, grey, 128f / 255f, new NVector2( 1, 0 ) );
		var v1 = new WeatherVertex( new NVector3( startX - q, startY + p, z ), wTrailing, grey, 128f / 255f, new NVector2( 0, 0 ) );
		var v2 = new WeatherVertex( new NVector3( endX - q, endY + p, z ), wLeading, grey, 1f, new NVector2( 0, 1 ) );
		var v3 = new WeatherVertex( new NVector3( endX + q, endY - p, z ), wLeading, grey, 1f, new NVector2( 1, 1 ) );
		output[0] = v0; output[1] = v1; output[2] = v2;
		output[3] = v0; output[4] = v2; output[5] = v3;
		return 6;
	}

	/// <summary>
	/// Writes the snow flakes of the drawn drops as clip-space triangles (three vertices a flake): full white, the camera-space x and y spread by 1 + depth / 8, the apex above the flake's centre and the base
	/// <paramref name="displayAspect"/> (the display's height over its width, the word 0x8BCBCC) times the half-size to each side, the apex at the lower middle and the base at the upper corners of the flake's atlas quadrant (index &amp; 3).
	/// </summary>
	// [BIN:TPW-EXE:0x57F490 flake vertex] with the flake's camera-space position (x, y, z) (y negated for the screen), s = 1 + z / 8, h = 0.1 (+0x3354) and a = [0x8BCBCC]: the apex is (s x, -(s y + h)), the base corners (s x + h a, h - s y) and (s x - h a, h - s y), all with z + 0.1 and w = z + 0.1501 (0x701D10, 0x701D24), colour [0x87A248 + 0x2c], and only z > 0 is drawn; nothing multiplies x and y by the projection or the screen matrix, so the flake lands at x s / w of the screen half-width and the base half-width h a in clip x is h in clip y on the screen
	// [BIN:TPW-EXE:0x57F490 flake texture] the original's v runs up the picture (its textures keep the TGA's bottom-up rows): the apex takes the row's lower v and the base its upper v, so OpenTPW's upright textures read the apex at the quadrant's bottom edge and the base at its top edge
	public int WriteSnow( WeatherCamera camera, Span<WeatherVertex> output, float displayAspect = 0.75f )
	{
		var count = 0;
		for ( var index = 0; index < drawn && count + SnowVerticesPerFlake <= output.Length; index++ )
			count += WriteFlake( camera.ToCamera( DropOffset( index ) + jitter[index] ), index, displayAspect, output[count..] );
		return count;
	}

	/// <summary>One flake at its (camera-space, jittered) position: three vertices, or none when it is not in front of the eye.</summary>
	internal static int WriteFlake( NVector3 position, int index, float displayAspect, Span<WeatherVertex> output )
	{
		if ( position.Z <= 0 )
			return 0;
		var spread = 1f + position.Z / SnowSpreadDepth;
		var x = position.X * spread;
		var y = position.Y * spread;
		var z = position.Z + SnowDepthBias;
		var w = position.Z + SnowProjectionBias;
		var quadrant = index & 3;
		var u0 = (quadrant & 1) * 0.5f;
		var row = quadrant >> 1;
		var apexV = 1f - 0.5f * row;
		var baseV = 0.5f - 0.5f * row;
		output[0] = new WeatherVertex( new NVector3( x, -(y + SnowHalfSize), z ), w, 1f, 1f, new NVector2( u0 + 0.25f, apexV ) );
		output[1] = new WeatherVertex( new NVector3( x + SnowHalfSize * displayAspect, SnowHalfSize - y, z ), w, 1f, 1f, new NVector2( u0 + 0.5f, baseV ) );
		output[2] = new WeatherVertex( new NVector3( x - SnowHalfSize * displayAspect, SnowHalfSize - y, z ), w, 1f, 1f, new NVector2( u0, baseV ) );
		return SnowVerticesPerFlake;
	}
}
