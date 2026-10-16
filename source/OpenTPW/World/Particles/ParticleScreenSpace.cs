using SVector2 = System.Numerics.Vector2;

namespace OpenTPW;

/// <summary>
/// The screen-space particles (templates with byte +5 set: the advisor's tickets and keys, the message tags): their native x and z
/// place them on the screen, their y is ignored. Screen units run from −1 to 1 across the width (x right) and the height (y down),
/// the space the advisor's model is drawn in (see <see cref="Advisor.Centre"/>); the native centre (50000, 37500) is the screen centre.
/// </summary>
// [BIN:TPW-EXE:0x51F01A screen-space draw list] with template byte +5 set the draw entry is x = ((px - 0xC35) << 4) / 49 and y = ((pz << 4) - 0x927C) / 37 (integer, truncating), both scaled around the emitter by the control first (0x51F026), z = the template word +0x52, the size (word +0x28 + 63 for negatives) >> 6 instead of << 1, and the flags | 0x54000
// [BIN:TPW-EXE:0x57C620 screen-space quads] flag 0x40000 skips the view transform 0x578D20: the entry / 1024 is the vertex position as is (w = 1), half-height = size / 1024 * 0.5, the quad (x -+ w, y -+ h) is rotated about its centre by 0x591430
public static class ParticleScreenSpace
{
	/// <summary>The native x of the screen centre (0xC35 &lt;&lt; 4) and of one screen unit.</summary>
	public const int CentreX = 50000;
	/// <summary>The native z of the screen centre (0x927C).</summary>
	public const int CentreZ = 37500;
	private const int DivisorX = 49;
	private const int DivisorZ = 37;

	/// <summary>The screen position (x right, y down, −1..1) of a particle at native-internal <paramref name="x"/>, <paramref name="z"/> (1/64 original unit, control applied).</summary>
	public static SVector2 Position( int x, int z ) =>
		new( unchecked((x - (CentreX >> 4)) << 4) / DivisorX / 1024f, unchecked((z << 4) - CentreZ) / DivisorZ / 1024f );

	/// <summary>The half-height of a screen-space quad in screen units: the size truncated to 1/64, stored as a word, / 1024 * 0.5.</summary>
	public static float HalfHeight( int size ) => (ushort)(size / 64) * (1f / 1024f) * 0.5f;

	// [BIN:TPW-EXE:0x5913F0 rotation table] 256 floats fsin(i * pi * (1/128))
	private static readonly float[] sine = BuildSine();

	private static float[] BuildSine()
	{
		var table = new float[256];
		for ( var index = 0; index < table.Length; index++ )
			table[index] = (float)Math.Sin( index * (double)(float)Math.PI * 0.0078125 );
		return table;
	}

	/// <summary>
	/// The four corners (top left, top right, bottom right, bottom left in screen order) of a screen-space quad of half-size
	/// <paramref name="halfWidth"/> by <paramref name="halfHeight"/> centred on <paramref name="centre"/> and turned by
	/// <paramref name="spin"/> (1/65536 turn, quantised to 1/256 turn as the original's table is).
	/// </summary>
	// [BIN:TPW-EXE:0x591430 quad rotation] index a = ftol(spin / 65536 * 256) & 0xFF, s = table[a], c = table[(0x40 + a) & 0xFF]; about the quad's centre each corner becomes (x c + y s, y c - x s), in the screen units as they are
	public static (SVector2 A, SVector2 B, SVector2 C, SVector2 D) Corners( SVector2 centre, float halfWidth, float halfHeight, int spin )
	{
		var a = (int)((ushort)spin * (1f / 65536f) * 256f);
		var s = sine[a & 0xFF];
		var c = sine[(0x40 + a) & 0xFF];
		SVector2 Turn( float x, float y ) => new( centre.X + x * c + y * s, centre.Y + y * c - x * s );
		return (Turn( -halfWidth, -halfHeight ), Turn( halfWidth, -halfHeight ), Turn( halfWidth, halfHeight ), Turn( -halfWidth, halfHeight ));
	}

	/// <summary>A screen position as the clip space OpenTPW draws in (y up).</summary>
	public static SVector2 ToClip( SVector2 screen ) => new( screen.X, -screen.Y );
}
