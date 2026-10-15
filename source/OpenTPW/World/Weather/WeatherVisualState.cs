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
	/// <summary>The park's strike: the ground point at y = 0 straight below the top (the original's float ground draw is not traced).</summary>
	public static WeatherStrike Below( NVector3 top ) => new( top, new NVector3( top.X, WeatherVisualState.BoltGroundHeight, top.Z ) );
}

/// <summary>
/// The camera as the weather field needs it: the eye and the view rotation, both in original units with Y up.
/// <see cref="Right"/>, <see cref="Up"/> and <see cref="Forward"/> are the camera's axes expressed in world axes, so
/// a world direction <c>d</c> becomes camera space as <c>(Right·d, Up·d, Forward·d)</c>; the third component is the depth.
/// </summary>
public readonly record struct WeatherCamera( NVector3 Eye, NVector3 Right, NVector3 Up, NVector3 Forward )
{
	/// <summary>Rotates a world direction into camera space (x right, y up, z forward = depth).</summary>
	public NVector3 ToCamera( NVector3 direction ) => new( NVector3.Dot( Right, direction ), NVector3.Dot( Up, direction ), NVector3.Dot( Forward, direction ) );

	/// <summary>Straight ahead along +Z, the camera's depth axis, with the eye at the origin (tests).</summary>
	public static WeatherCamera Looking( NVector3 eye ) => new( eye, NVector3.UnitX, NVector3.UnitY, NVector3.UnitZ );
}

/// <summary>One vertex of a weather quad: a camera-space position (original units), a grey level, an alpha and a texture coordinate.</summary>
public readonly record struct WeatherVertex( NVector3 Position, float Grey, float Alpha, NVector2 TexCoords );

/// <summary>One straight piece of a bolt in original world units, and the branch depth it belongs to (0 is the trunk).</summary>
public readonly record struct WeatherBoltSegment( NVector3 A, NVector3 B, int Depth );

/// <summary>One live bolt: its fixed shape, its age, and the flicker term of its current colour.</summary>
public sealed class WeatherBolt
{
	internal WeatherBolt( IReadOnlyList<WeatherBoltSegment> segments, float lifetime )
	{
		Segments = segments;
		Lifetime = lifetime;
	}

	public IReadOnlyList<WeatherBoltSegment> Segments { get; }
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
// [APPROX:AUDIO-900] the parallax term is 0.1 x the camera's movement since the last frame, subtracted from the field's offset: equivalent to the wrapped absolute form only up to the sign with which the original adds the offset to the drops (not read); the 0.3 camera-mode case is not reproduced
// [APPROX:AUDIO-901] snow draws the jitter version only: the 128-point spline swirl (phase += 5 dt; offset = per-flake wobble x 0.4 + one of 16 group drifts x 6 + a global gust x 13) is not reproduced because the meaning of flag 0x115E94 bit 8 is not identified; each flake is drawn as a quad covering its atlas quadrant (the original submits a triangle whose exact corner mapping is not traced)
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
	/// <summary>The frame time cap: a hitch longer than this moves the field by 500 ms only.</summary>
	public const float MaximumStepSeconds = 0.5f;
	/// <summary>A rain streak's length along its fall, in original units.</summary>
	public const float StreakLength = 0.1f;
	/// <summary>A rain streak's width as a fraction of its length.</summary>
	public const float StreakWidthRatio = 0.2f;
	/// <summary>Depth at which the rain's grey ramp reaches 0.</summary>
	public const float RainFadeDepth = 6f;
	/// <summary>Half the edge of a snow flake, in original units.</summary>
	public const float SnowHalfSize = 0.1f;
	/// <summary>Per-frame random jitter of a flake, in original units (+-1/128).</summary>
	public const float SnowJitter = 1f / 128f;
	/// <summary>Divisor of the snow's depth spread: x and y scale by 1 + depth / 8.</summary>
	public const float SnowSpreadDepth = 8f;

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
	public const int MaximumBoltSegments = 512;

	/// <summary>The original's wind term |w| &lt;= 0.0017 tilts the fall by under 0.04 degrees: straight down.</summary>
	private static readonly NVector3 FallDirection = -NVector3.UnitY;

	private readonly DeterministicRandom random = new( VisualSeed );
	private readonly NVector3[] points = new NVector3[FieldSize];
	private readonly NVector3[] jitter = new NVector3[FieldSize];
	private readonly List<WeatherBolt> bolts = new();
	private NVector3 offset;
	private NVector3? lastEye;
	private int drawn;
	private bool snow;

	public WeatherVisualState()
	{
		// 1024 fixed random points in the box, stored 0..8 so the wrap is a plain modulo.
		for ( var index = 0; index < FieldSize; index++ )
			points[index] = new NVector3( Unit(), Unit(), Unit() ) * (2 * BoxHalfExtent);
	}

	private float Unit() => random.Next( 1 << 20 ) / (float)(1 << 20);
	private float Signed() => random.Next( 1 << 20 ) / (float)(1 << 19) - 1f;

	/// <summary>Drops drawn this frame: min(source drops, 1024).</summary>
	public int DropCount => drawn;
	public bool Snow => snow;
	public IReadOnlyList<WeatherBolt> Bolts => bolts;

	/// <summary>The scroll of the box, each axis in 0..8.</summary>
	public NVector3 Offset => offset;

	/// <summary>
	/// The step the field takes for a frame: wall-clock seconds capped at <see cref="MaximumStepSeconds"/>, zero while
	/// paused or for a non-finite or negative delta.
	/// </summary>
	// [APPROX:AUDIO-903] OpenTPW zeroes dt while the park is paused (simulation speed 0) and otherwise uses the wall-clock frame time, whatever the simulation speed; tp.exe's CRain update (0x57ff60) also zeroes dt while engine flag 0x8bd508 bit 2 is set (0x57ffb1, 0x57ffc9), and nothing writes that bit alone (only whole-word stores at 0x564342, 0x577326, 0x577446 and 0x583555), so what sets it is not identified
	public static float FrameSeconds( float deltaSeconds, bool paused ) =>
		paused || !float.IsFinite( deltaSeconds ) || deltaSeconds <= 0 ? 0f : MathF.Min( deltaSeconds, MaximumStepSeconds );

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
	/// velocity and by the camera's parallax; bolts age and expire.
	/// </summary>
	public void Update( float deltaSeconds, IWeatherVisualSource source, WeatherCamera camera )
	{
		drawn = Math.Clamp( source.DropCount, 0, FieldSize );
		snow = source.Snow;

		if ( lastEye is { } previous )
			offset -= (camera.Eye - previous) * ParallaxScale;
		lastEye = camera.Eye;

		if ( deltaSeconds > 0 )
		{
			offset += FallDirection * ((snow ? SnowVelocity : RainVelocity) * deltaSeconds);
			if ( snow )
				for ( var index = 0; index < drawn; index++ )
					jitter[index] = new NVector3( Signed(), Signed(), Signed() ) * SnowJitter;
		}
		var span = 2 * BoxHalfExtent;
		offset = new NVector3( offset.X - MathF.Floor( offset.X / span ) * span, offset.Y - MathF.Floor( offset.Y / span ) * span, offset.Z - MathF.Floor( offset.Z / span ) * span );

		for ( var index = bolts.Count - 1; index >= 0; index-- )
		{
			var bolt = bolts[index];
			bolt.Age += deltaSeconds;
			if ( bolt.Age >= bolt.Lifetime )
				bolts.RemoveAt( index );
			else if ( deltaSeconds > 0 )
				bolt.Flicker = random.Next( 64 );
		}

		foreach ( var strike in source.TakeStrikes() )
			AddBolt( strike.Top, strike.Bottom, source.BoltLifetimeSeconds );
	}

	/// <summary>Starts a bolt from <paramref name="top"/> (original units) down to y = 0 directly below it.</summary>
	// [BIN:STP-PPC:0x101035D4 bolt start] the strike hands the renderer the sky point (x, 300, z); up to 16 bolts are live
	// [BIN:STP-PPC:engine_shared 0x10020150 bolt ground] the ground point is at original y = 0, not on the terrain: the bolt keeps the top's x and z (the original's ground point is a uniform float draw whose range is not traced)
	// [BIN:STP-PPC:engine_shared 0x1001EA18 bolt shape] recursive midpoint displacement with roughness 0.6; a segment stops splitting when its squared length is under 6 + 2u (u in [-1,1)), so segments run about 2 to 2.8 units; each split has a 9/512 chance to branch, up to depth 4, a branch being 2.5 x (512..1535) / (96 x depth) long and bent downward; the shape is fixed per bolt from a seeded generator (the PC behaviour)
	// [APPROX:AUDIO-902] the displacement per split is OpenTPW's: roughness x segment length x 0.25 x u along both axes perpendicular to the segment, the branch direction is half the parent's plus a random sideways term and a downward bias; tp.exe moves the midpoint along one perpendicular (0x57e4b0: from one of three axes picked by rand % 3) by |segment| x roughness x r2 x r1^2 with a random sign (r = (rand % 0x1fff) / 8192), and a branch (0x57e810: (rand & 0x1ff) < 9, depth below +0xa1a0) heads 2.5 x (0x200 + rand & 0x3ff) / (96 x depth) along the segment plus (0x200 + rand & 0x3ff) / (96 x depth) x the bolt's direction, halved when it points back
	public WeatherBolt AddBolt( NVector3 top, float lifetimeSeconds ) => AddBolt( top, WeatherStrike.Below( top ).Bottom, lifetimeSeconds );

	/// <summary>Starts a bolt between <paramref name="top"/> and <paramref name="bottom"/> (original units): the lobby hands both.</summary>
	public WeatherBolt AddBolt( NVector3 top, NVector3 bottom, float lifetimeSeconds )
	{
		var bolt = new WeatherBolt( BuildBolt( top, bottom, random.NextUInt64() ), lifetimeSeconds );
		if ( bolts.Count >= MaximumBolts )
			bolts.RemoveAt( 0 );
		bolts.Add( bolt );
		return bolt;
	}

	/// <summary>A bolt's fixed shape: the same seed always gives the same segments.</summary>
	public static IReadOnlyList<WeatherBoltSegment> BuildBolt( NVector3 top, NVector3 bottom, ulong seed )
	{
		var rng = new DeterministicRandom( seed );
		var segments = new List<WeatherBoltSegment>();
		float U() => rng.Next( 1 << 20 ) / (float)(1 << 19) - 1f;
		// Every piece still to be split or already final counts once, so the leaves can never pass the bound however the recursion unwinds.
		var pieces = 1;

		void Split( NVector3 a, NVector3 b, int depth )
		{
			var lengthSquared = (b - a).LengthSquared();
			if ( lengthSquared < 6f + 2f * U() || pieces + 1 > MaximumBoltSegments )
			{
				segments.Add( new WeatherBoltSegment( a, b, depth ) );
				return;
			}
			var length = MathF.Sqrt( lengthSquared );
			var direction = (b - a) / length;
			var side = NVector3.Cross( direction, MathF.Abs( direction.Y ) < 0.9f ? NVector3.UnitY : NVector3.UnitX );
			side = NVector3.Normalize( side );
			var lift = NVector3.Cross( direction, side );
			var middle = (a + b) * 0.5f + (side * U() + lift * U()) * (BoltRoughness * length * 0.25f);

			pieces++;

			if ( depth < MaximumBranchDepth && pieces + 1 <= MaximumBoltSegments && rng.Next( 512 ) < 9 )
			{
				pieces++;
				var branchDepth = depth + 1;
				var branchLength = 2.5f * (512 + rng.Next( 1024 )) / (96f * branchDepth);
				var heading = NVector3.Normalize( direction * 0.5f + new NVector3( U(), 0, U() ) * 0.5f + new NVector3( 0, -0.6f, 0 ) );
				Split( middle, middle + heading * branchLength, branchDepth );
			}
			Split( a, middle, depth );
			Split( middle, b, depth );
		}

		Split( top, bottom, 0 );
		return segments;
	}

	/// <summary>The bolt's colour grey 0..1: opaque, pulsing in 500 ms cycles from 192 up to 255, plus its 0..63 flicker, capped at 255.</summary>
	// [APPROX:AUDIO-902] the 500 ms pulse is drawn as a sine between 192 and 255 plus rand(0..63); the original's exact curve (the segment emitter 0x57dd10) is not read
	public static float BoltGrey( WeatherBolt bolt )
	{
		var phase = bolt.Age / BoltPulseSeconds;
		var pulse = 192f + 63f * (0.5f + 0.5f * MathF.Sin( 2 * MathF.PI * phase ));
		return MathF.Min( 255f, pulse + bolt.Flicker ) / 255f;
	}

	/// <summary>A bolt branch's ribbon half-width in original units: 1.3 / (depth + 1).</summary>
	public static float BoltWidth( int branchDepth ) => BoltHalfWidth / (branchDepth + 1);

	/// <summary>The rain's grey 0..1 at a camera-space depth: 3 x max(0, int(12 x (6 - depth))) over 255.</summary>
	public static float RainGrey( float depth ) => 3 * Math.Max( 0, (int)(12f * (RainFadeDepth - depth)) ) / 255f;

	/// <summary>
	/// Writes the rain streaks of the drawn drops as camera-space triangles (six vertices a streak) and returns the vertex
	/// count. A streak is skipped when either end is at or behind the eye; the grey follows each end's depth and the alpha
	/// runs from 0x80 at the trailing end to 0xFF at the leading end.
	/// </summary>
	public int WriteRain( WeatherCamera camera, Span<WeatherVertex> output )
	{
		var fall = camera.ToCamera( FallDirection ) * StreakLength;
		var count = 0;
		for ( var index = 0; index < drawn && count + 6 <= output.Length; index++ )
		{
			var trailing = camera.ToCamera( DropOffset( index ) );
			var leading = trailing + fall;
			if ( trailing.Z <= 0 || leading.Z <= 0 )
				continue;
			var across = new NVector2( -fall.Y, fall.X );
			across = across.LengthSquared() > 1e-12f ? NVector2.Normalize( across ) : NVector2.UnitX;
			var half = new NVector3( across * (0.5f * StreakWidthRatio * StreakLength), 0 );
			var greyTrailing = RainGrey( trailing.Z );
			var greyLeading = RainGrey( leading.Z );
			var a = new WeatherVertex( trailing - half, greyTrailing, 128f / 255f, new NVector2( 0, 0 ) );
			var b = new WeatherVertex( trailing + half, greyTrailing, 128f / 255f, new NVector2( 1, 0 ) );
			var c = new WeatherVertex( leading - half, greyLeading, 1f, new NVector2( 0, 1 ) );
			var d = new WeatherVertex( leading + half, greyLeading, 1f, new NVector2( 1, 1 ) );
			output[count++] = a; output[count++] = b; output[count++] = c;
			output[count++] = b; output[count++] = d; output[count++] = c;
		}
		return count;
	}

	/// <summary>
	/// Writes the snow flakes of the drawn drops as camera-space quads (six vertices a flake): full white, the screen
	/// x and y spread by 1 + depth / 8, each mapped to the atlas quadrant (index &amp; 3).
	/// </summary>
	public int WriteSnow( WeatherCamera camera, Span<WeatherVertex> output )
	{
		var count = 0;
		for ( var index = 0; index < drawn && count + 6 <= output.Length; index++ )
		{
			var position = camera.ToCamera( DropOffset( index ) + jitter[index] );
			if ( position.Z <= 0 )
				continue;
			var spread = 1f + position.Z / SnowSpreadDepth;
			var centre = new NVector3( position.X * spread, position.Y * spread, position.Z );
			var quadrant = index & 3;
			var u0 = (quadrant & 1) * 0.5f;
			var v0 = (quadrant >> 1) * 0.5f;
			var a = new WeatherVertex( centre + new NVector3( -SnowHalfSize, SnowHalfSize, 0 ), 1f, 1f, new NVector2( u0, v0 ) );
			var b = new WeatherVertex( centre + new NVector3( SnowHalfSize, SnowHalfSize, 0 ), 1f, 1f, new NVector2( u0 + 0.5f, v0 ) );
			var c = new WeatherVertex( centre + new NVector3( -SnowHalfSize, -SnowHalfSize, 0 ), 1f, 1f, new NVector2( u0, v0 + 0.5f ) );
			var d = new WeatherVertex( centre + new NVector3( SnowHalfSize, -SnowHalfSize, 0 ), 1f, 1f, new NVector2( u0 + 0.5f, v0 + 0.5f ) );
			output[count++] = a; output[count++] = b; output[count++] = c;
			output[count++] = b; output[count++] = d; output[count++] = c;
		}
		return count;
	}
}
