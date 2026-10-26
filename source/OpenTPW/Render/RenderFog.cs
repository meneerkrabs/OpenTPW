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
/// [BIN:TPW-EXE:0x5D8B50 lobby fog] the lobby sets the fog colour 0xFF44DDFF, start 50 and end 300 (restoring the park's values on leaving), and a front-end screen 50 to 250 (0x5E1421); the lobby's projection is built with near 0.1, far 1000 and its own field of view (0x5D8CB0).
/// </remarks>
internal static class RenderFog
{
	/// <summary>FIRSTPERSONVIEWDISTANCE 0 to 3: the ray distance [0x8BD4C0] and the fog start and end [0x8BD490], [0x8BD494] in original units (0x44DBB0 to 0x44DC23).</summary>
	public static readonly (float Distance, float Start, float End)[] ViewDistances = [(200f, 50f, 60f), (325f, 100f, 110f), (450f, 135f, 150f), (700f, 230f, 250f)];

	/// <summary>The lobby's fog range in the lobby's own units (0x5D8B50: [0x8BD490] = 50, [0x8BD494] = 300).</summary>
	public static readonly (float Start, float End) LobbyRange = (50f, 300f);

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
		var (_, start, end) = ViewDistanceOf( option );
		SetOriginalRange( start, end, OriginalParkPlacement.ModelScale, OriginalProjection.FieldOfView );
	}

	/// <summary>The lobby's range (0x5D8B50), through the island view's projection.</summary>
	public static void SetLobbyRange() => SetOriginalRange( LobbyRange.Start, LobbyRange.End, 1f, LobbyCameraMode.IslandFieldOfView );

	/// <summary>
	/// What the shaders' <c>g_flViewDistanceScale</c> slot carries: the range, times the graphics preset's view distance <paramref name="scale"/> (an entity may override it), as
	/// start x 8 x 4096 + end x 8 in 1/8 world units (both below 4096 / 8 = 512; larger values are held there). 0 means no fog.
	/// </summary>
	public static float PackedRange( float scale )
	{
		if ( ViewRange.Start == float.MaxValue )
			return 0f;
		if ( !(scale > 0f) )
			scale = 1f;
		static float Quantised( float value ) => MathF.Round( Math.Clamp( value * 8f, 0f, 4095f ) );
		return Quantised( ViewRange.Start * scale ) * 4096f + Quantised( ViewRange.End * scale );
	}
}
