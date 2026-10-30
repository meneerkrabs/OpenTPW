using SVector3 = System.Numerics.Vector3;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// The engine's distance fog (docs/UI.md, UI-107; docs/LND.md "Fog and far plane"): every fogged shader (objects, terrain, water, kid sprites) blends distant pixels into the fog colour with the original's
/// linear range, so the world fades into the same colour the viewport is cleared to and the sky's haze ends in. Presentation only; nothing here is read by the simulation.
/// </summary>
/// <remarks>
/// [BIN:TPW-EXE:0x4080E0 fog colour] the level load copies <c>ThemeEngine.FogColour</c> into the engine fog colour [0x8BD47C], which the
/// viewport clear (0x5737AA), the sky (0x583F0C, 0x58438C) and the device fog (0x576B41 hands it to <c>SetFog</c> 0x567AD0) all use.
/// [BIN:TPW-EXE:0x44DBB0 FIRSTPERSONVIEWDISTANCE] the option apply sets the fog start and end [0x8BD490] and [0x8BD494] (through 0x583280) and the ray distance [0x8BD4C0]: 0 gives 50 to 60 and 200, 1 gives 100 to 110 and 325, 2 gives 135 to 150 and 450,
/// 3 gives 230 to 250 and 700 (original units; the options 0 to 3 of the Low, Medium and High presets are 0, 1 and 2). The frame function (0x576A00) hands start, end and colour to the device every frame through the object's virtual 0x18
/// (<c>SetFog</c> 0x567AD0): range fog off (state 0x30 = 0), the colour (0x22), and, unless bits 0xC000 of [0x8BCBC8] are set (a vendor workaround for old S3 and ATI devices, 0x566A92 to 0x566B45), table fog LINEAR (0x23 = 3) with start (0x24) and end (0x25).
/// The depth the table fog measures is the projection's w: the matrix 0x578BE0 builds has w = sin(fov / 2) x the view depth (0.7071 at the park camera's 90 degrees; the vertex projection 0x579690 stores 1 / w as the rhw).
/// [BIN:TPW-EXE:0x56778B fog enable] the device's fog is enabled only through bit 0x100000 of a primitive's state word, which every draw path adds only while bit 0x2000 of [0x8BCBC8] is set; that bit is toggled with the park's view mode ([0x790AB0] & 0x16, 0x54FAD7), so the park has fog only in the first-person views (ride, walk, camcorder), not in the overview.
/// [EXT:hardware-d3d-path] the vendor workaround that turns the table fog off while a bit of 0xC000 is set in [0x8BCBC8] (0x566A92 to 0x566B45: old S3 and ATI devices) is not modelled: it belongs to the original's per-device Direct3D path, which depends on the host's graphics hardware, and OpenTPW's renderer has one path (user-approved 2026-10-30, UI-107 narrowed).
/// [BIN:TPW-EXE:0x5D8B50 lobby fog] the lobby sets the fog colour 0xFF44DDFF, start 50 and end 300 but clears bit 0x2000 (0x5D8BF9), so it has no fog; the globe clears it too (0x5DD04C); front-end screens that call 0x5D9680 (50 to 250) are fogged and are not mapped here. The lobby's projection is built with near 0.1, far 1000 and its own field of view (0x5D8CB0).
/// The packed range holds start and end each clamped to 511.875 (4095 / 8 in the 12 bit fields): the park's values times the preset's view distance scale stay far below that, and the globe's 1000 scale is a plain no-fog range that the clamp leaves effectively unbounded for the visible depths.
/// </remarks>
internal static class RenderFog
{
	/// <summary>FIRSTPERSONVIEWDISTANCE 0 to 3: the ray distance [0x8BD4C0] and the fog start and end [0x8BD490], [0x8BD494] in original units (0x44DBB0 to 0x44DC23).</summary>
	public static readonly (float Distance, float Start, float End)[] ViewDistances = [(200f, 50f, 60f), (325f, 100f, 110f), (450f, 135f, 150f), (700f, 230f, 250f)];

	/// <summary>The values of the option <paramref name="option"/> (other values keep the earlier ones in the original; OpenTPW takes the nearest).</summary>
	public static (float Distance, float Start, float End) ViewDistanceOf( int option ) => ViewDistances[Math.Clamp( option, 0, ViewDistances.Length - 1 )];

	/// <summary>The active FIRSTPERSONVIEWDISTANCE (the high preset's 2 when no graphics settings exist, as in tests).</summary>
	public static int ViewDistanceOption() => OptionOverride ?? IGraphicsSettings.Instance?.Detail.FirstPersonViewDistance ?? 2;

	/// <summary>Tests: a fixed option in place of the graphics settings.</summary>
	internal static int? OptionOverride { get; set; }

	/// <summary>The fog colour of a Direct3D colour (alpha ignored) as 0-1 channels.</summary>
	public static SVector3 ColourOf( RgbaByte colour ) => new( colour.R / 255f, colour.G / 255f, colour.B / 255f );

	/// <summary>The front end's fog colour (the lobby clear colour), which a scene without a level fades into.</summary>
	public static RgbaByte LobbyColour { get; } = new( LobbyEffects.ClearColour.R, LobbyEffects.ClearColour.G, LobbyEffects.ClearColour.B, 255 );

	/// <summary>The colour the world pass draws its fog in; the game flow sets it to the level's fog colour (or the lobby's) before each world pass.</summary>
	public static SVector3 WorldColour { get; set; } = ColourOf( LobbyColour );

	/// <summary>The fog's start and end in view depth (the distance along the camera's axis) in world units, before the graphics preset's view distance scale.</summary>
	public static (float Start, float End) ViewRange { get; private set; } = (float.MaxValue, float.MaxValue);

	/// <summary>
	/// Whether the world is fogged at all. In the original the device's fog enable (render state 0x1C) comes only from bit 0x100000 of a primitive's state word (0x56778B to 0x5677A4), which every draw path adds only while bit 0x2000 of
	/// <c>[0x8BCBC8]</c> is set (the terrain 0x570E9A, objects 0x565FA6 to 0x565FB7, 0x58179F to 0x5817A5). That bit is cleared on entering the lobby (0x5D8BF9) and on the globe (0x5DD04C) and by the FOG option (0x44DB16), set by the front-end
	/// screens that give the fog 50 to 250 (through 0x5D9680, from 0x5E1407, 0x5E1980 and 0x5E24A2) and toggled together with 0x80 by the park frame function (0x54FAD7 to 0x54FAE9) whenever <c>[0x790AB0] &amp; 0x16</c> changes, so it is on in the park only
	/// while the terrain camcorder or the ride follow (or the mode with bit 0x10) is active: OpenTPW's free walk and ride cameras. The park overview and the lobby have no fog.
	/// </summary>
	public static bool Enabled { get; private set; }

	/// <summary>Fog off: the park overview, the lobby, the globe.</summary>
	public static void Disable() => Enabled = false;

	/// <summary>Tests: no fog and no range, the state before any scene set one.</summary>
	internal static void Reset()
	{
		Enabled = false;
		ViewRange = (float.MaxValue, float.MaxValue);
	}

	/// <summary>
	/// Sets the range from the original's start and end (<paramref name="start"/>, <paramref name="end"/> in the original's units of the scene: <paramref name="unitScale"/> world units each; 0.2 in a park, 1 in the lobby) and the
	/// field of view of the projection the original builds the scene with, whose w is sin(fov / 2) times the view depth (the fog measures w).
	/// </summary>
	public static void SetOriginalRange( float start, float end, float unitScale, float projectionFieldOfViewDegrees )
	{
		var w = MathF.Sin( projectionFieldOfViewDegrees * MathF.PI / 360f );
		ViewRange = (start * unitScale / w, end * unitScale / w);
	}

	/// <summary>The park's range from FIRSTPERSONVIEWDISTANCE (0.2 world units to an original unit, the park camera's 90 degree projection).</summary>
	public static void SetParkRange( int option )
	{
		Enabled = true;
		var (_, start, end) = ViewDistanceOf( option );
		SetOriginalRange( start, end, OriginalParkPlacement.ModelScale, OriginalProjection.FieldOfView );
	}

	/// <summary>
	/// What the shaders' <c>g_flViewDistanceScale</c> slot carries: the range, times the graphics preset's view distance <paramref name="scale"/> (an entity may override it), as
	/// start x 8 x 4096 + end x 8 in 1/8 world units (each held at 4095 / 8 = 511.875, which only a scale beyond what a park uses reaches: the globe's 1000 would, but the globe has no fog). 0 means no fog.
	/// </summary>
	public static float PackedRange( float scale )
	{
		if ( !Enabled || ViewRange.Start == float.MaxValue )
			return 0f;
		if ( !(scale > 0f) )
			scale = 1f;
		static float Quantised( float value ) => MathF.Round( Math.Clamp( value * 8f, 0f, 4095f ) );
		return Quantised( ViewRange.Start * scale ) * 4096f + Quantised( ViewRange.End * scale );
	}
}
