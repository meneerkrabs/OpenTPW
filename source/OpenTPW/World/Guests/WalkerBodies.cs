using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// The walking bodies of the script's walk table as the original's walk update draws them (tp.exe <c>0x00557D80</c>, which calls the body routine <c>0x005580A0</c> for every entry it passes; docs/TOURS.md,
/// "The walking body"). GPU-free: it turns a table entry into a <see cref="BounceRiderPose"/>, which <see cref="FlatRideBounceRiders"/> hands to the guest renderers.
/// </summary>
/// <remarks>
/// A body is drawn in every state of the entry: walking on (1) from the first to the second node of WALKON by the share of the walk-on time spent, on board (2) at the second node (only for the actions
/// 3, 5 and 6, and while the script's walk float runs for any action: the head actions 1, 2 and 4 hide it), walking off (3) from the third to the fourth node by the share of the walk-off time, and ready
/// (4) at the fourth node until WALKGET hands the rider out. The walk templates are the sprite sets' slots 1 (walking), 0 (standing, template 3), 4 (template 8, the strength games), 12 (template 5,
/// cheering) and 2 (running, template 2, the floating body).
/// </remarks>
internal static class WalkerBodies
{
	/// <summary>The class mask of the dummies a walk looks up (0x0044B220): the ride's walk nodes.</summary>
	public const int WalkClass = 0x800;
	/// <summary>The class mask of a head action's second node on the walk-on and first node on the walk-off.</summary>
	public const int HeadClass = 0x80;

	/// <summary>The sprite slot of one of the original's body templates (the table at 0x0075A418: template 1 walking, 2 running, 3 standing, 5 cheering, 8 the short gesture).</summary>
	// [BIN:TPW-EXE:0x0075A418 body templates] template 1 (0x0074DB60) selects slot 1, 2 (0x0074DBC0) slot 2, 3 (0x0074DC20) slot 0, 5 (0x0074DCE8) slot 12 and 8 (0x0074DD68) slot 4, each stepping the frames of its slot
	public static int SlotOf( int template ) => template switch
	{
		1 => GuestSpriteAtlas.WalkSlot,
		2 => GuestSpriteAtlas.RunSlot,
		3 => GuestSpriteAtlas.StandSlot,
		5 => 12,
		8 => 4,
		_ => GuestSpriteAtlas.StandSlot
	};

	/// <summary>A heading byte as the walk code keeps it: the sign of the value with its magnitude masked to three bits.</summary>
	// [BIN:TPW-EXE:0x00557F74 heading word] ((|value| & 7) with the sign put back; also 0x005570E1), written at +0x14 of the entry
	public static int SignedLow3( int value ) => value < 0 ? -(-value & 7) : value & 7;

	/// <summary>The heading byte for an offset of the original's two ground axes (<paramref name="dx"/> along x, <paramref name="dz"/> along z): the angle a = atan2(dz, dx) turned into a
	/// rounded eighth of a turn, ftol(0.5 - (6.28318 - (a - 1.570795)) x -1.2732406) (6 to 14), then <see cref="SignedLow3"/>.</summary>
	// [BIN:TPW-EXE:0x005570C2 walk heading] fpatan of (z2 - z1, x2 - x1), fsub 1.570795 (0x700FE8), fsubr 6.28318 (0x700FF0), fmul -1.2732406 (0x700FF8, -4/pi), fsubr 0.5 (0x701000),
	// __ftol, then the sign-keeping three-bit mask (0x005570E1); the on-board heading does the same (0x00557F52, mask 0x00557F74); the sprite pass keeps the low three bits (0x00542010)
	// [EXT:deterministic-math] the x87's fpatan in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
	public static int HeadingOf( float dx, float dz ) => SignedLow3( (int)(0.5 - (6.28318 - (DetMath.Atan2( dz, dx ) - 1.570795)) * -1.2732406201955062) );

	/// <summary>The sprite sector a heading byte selects: its low three bits (a negative heading wraps).</summary>
	public static int SectorOf( int heading ) => heading & 7;

	/// <summary>The walk's share in thousandths, as the update computes it: <c>(now x 1000 - began x 1000) / (end - began)</c> in unsigned 32-bit arithmetic (a walk of no length counts as done).</summary>
	// [BIN:TPW-EXE:0x00557DAB walk share] uint((now x 1000 + began x -1000) / (end - began)); 0x0055810B clamps a share above 1000 (signed compare) to 1000 before it moves the body
	public static uint Share( double now, double began, double ends )
	{
		var length = unchecked( (uint)((int)ends - (int)began) );
		if ( length == 0 )
			return 1000;
		return unchecked( (uint)((int)now * 1000 + (int)began * -1000) ) / length;
	}

	/// <summary>The share the body routine moves by: the share, clamped to 1000 when it is above (a signed compare, 0x0055810B).</summary>
	public static int BodyShare( uint share ) => (int)share > 1000 ? 1000 : (int)share;

	/// <summary>The body's point between two node positions for a share in thousandths: <c>((b - a) x share - a x -1000) x 0.001</c> on every axis.</summary>
	// [BIN:TPW-EXE:0x005580FF body position] the nodes' positions by the elapsed share, a float expression per axis
	public static SVector3 Between( SVector3 from, SVector3 to, int share ) => new(
		(((to.X - from.X) * share) - from.X * -1000f) * 0.001f,
		(((to.Y - from.Y) * share) - from.Y * -1000f) * 0.001f,
		(((to.Z - from.Z) * share) - from.Z * -1000f) * 0.001f );

	/// <summary>The offset of a floating body (the walk float of the script, WALKST_FLOAT) from its node, in the original's units: its x and z swing and its height.</summary>
	/// <param name="visitor">The rider's id.</param>
	/// <param name="clock">The script clock (0x00402D70), unsigned milliseconds.</param>
	/// <param name="start">The float's start on that clock (script +0x80).</param>
	/// <param name="duration">The float's duration in milliseconds (script +0x84, not 0).</param>
	/// <param name="words">The float's two words (+0x88 the swing, +0x8A the height).</param>
	/// <param name="speedBias">The script's speed bias (+0xC0).</param>
	// [BIN:TPW-EXE:0x005581D4 floating body] A = 0.4 + 0.1 (id & 7), B = 0.4 + 0.25 (id & 3), C = 0.5 + 0.01 bias, a and b the words x 0.001, env = sin(pi (clock - start) / duration) from the sine table, f = A clock C; height += (12 + 3 sin(0.004 f)) env b, x += sin(f / 300) B env 3 a, z += sin(B clock C / 300 + pi / 2) A env 3 a (the sine table of 0x004708D0, indices rounded and masked)
	public static (float X, float Z, float Height) FloatOffset( int visitor, uint clock, uint start, int duration, int words, int bias )
	{
		var a = 0.4f - (visitor & 7) * -0.1f;
		var b = 0.4f - (visitor & 3) * -0.25f;
		var c = 0.5f - bias * -0.01f;
		var swing = (ushort)words * 0.001f;
		var lift = (ushort)(words >> 16) * 0.001f;
		var now = (float)clock;
		var envelope = Sine( (float)(unchecked( (int)(clock - start) ) * 3.14159f / duration * BounceRiders.IndexScale) );
		var f = a * now * c;
		var height = (12f - Sine( f * 0.004f * BounceRiders.IndexScale ) * -3f) * envelope * lift;
		var x = Sine( f * 0.0033333334f * BounceRiders.IndexScale ) * b * envelope * 3f * swing;
		var z = Sine( (b * now * c * 0.0033333334f - -1.5707964f) * BounceRiders.IndexScale ) * a * envelope * 3f * swing;
		return (x, z, height);
	}

	private static float Sine( float index ) => BounceRiders.SineAt( (int)Math.Round( index, MidpointRounding.ToEven ) & (BounceRiders.SineTableSize - 1) );

	/// <summary>The heading a floating body takes: the clock divided by <c>ftol(B x 300)</c> (unsigned, kept as a 16-bit value) added to the heading, through <see cref="SignedLow3"/>.</summary>
	// [BIN:TPW-EXE:0x005583B0 float heading] (short)(clock / ftol((0.4 + 0.25 (id & 3)) x 300)) + the entry's heading, sign kept and magnitude masked to three bits, stored at +0x14 on every drawn frame
	public static int FloatHeading( int visitor, uint clock, int heading )
	{
		var divisor = (uint)((0.4f - (visitor & 3) * -0.25f) * 300f);
		return SignedLow3( unchecked( (short)(clock / Math.Max( 1u, divisor )) ) + heading );
	}

	/// <summary>
	/// The heading a body on board takes from its second node's orientation: the node's third row (original x, y, z = the engine matrix's M21, M23, M22), negated when the node's state has bit 0x10,
	/// normalised and scaled by 1024 in single steps, then <see cref="HeadingOf"/> of its x and z.
	/// </summary>
	// [BIN:TPW-EXE:0x00556B90 orientation row] negation of the three words when [node] & 0x10 (0x556C61..0x556C7D), the length sqrt(x*x + y*y + z*z) (the sums in the order x*y.. of 0x556CA0..0x556CCA), each word divided by it (0x556CD6..0x556CE2) and multiplied by 1024.0 (0x700FDC, 0x556D00..0x556D10), stored as singles; 0x557F4A takes the arc tangent of its z over its x (0x557F4E: fpatan)
	public static int OrientationHeading( System.Numerics.Matrix4x4 nodeEngine, bool negated )
	{
		float x = nodeEngine.M21, y = nodeEngine.M23, z = nodeEngine.M22;
		if ( negated )
			(x, y, z) = (-x, -y, -z);
		var length = MathF.Sqrt( x * x + y * y + z * z );
		return HeadingOf( x / length * 1024f, z / length * 1024f );
	}

	/// <summary>Whether a walk action hides the body on board: the head actions 1, 2 and 4 draw no body.</summary>
	// [BIN:TPW-EXE:0x00558448 hidden body] the actions 4, 1 and 2 call the hide routine 0x004F9ED0 instead of a body
	public static bool HidesOnBoard( int action ) => action is 1 or 2 or 4;

	/// <summary>The template of the body on board for a walk action, or 0 when none is drawn: 3 the standing body (template 3), 5 the short gesture (template 8), 6 the cheering body (template 5).</summary>
	// [BIN:TPW-EXE:0x00558401 body on board] action 3 draws template 3, action 5 template 8, action 6 template 5; every other action draws nothing
	public static int Template( int action ) => action switch { 3 => 3, 5 => 8, 6 => 5, _ => 0 };
}
