using SVector3 = System.Numerics.Vector3;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// The colour of the engine's distance fog (docs/UI.md, UI-107): every fogged shader (objects, terrain, water, kid sprites) blends distant
/// pixels into it, so the world fades into the same colour the viewport is cleared to and the sky's haze ends in.
/// Presentation only; nothing here is read by the simulation.
/// </summary>
/// <remarks>
/// [BIN:TPW-EXE:0x4080E0 fog colour] the level load copies <c>ThemeEngine.FogColour</c> into the engine fog colour [0x8BD47C], which the
/// viewport clear (0x5737AA), the sky (0x583F0C, 0x58438C) and the fogged vertices (0x5747D0, 0x576B41 hands it to the device) all use.
/// [APPROX:UI-107] the original's fog range is linear in view depth from FIRSTPERSONVIEWDISTANCE 0-3 (0x44D8F0 -> 0x583280 -> the device's
/// D3DRS_FOGSTART/END, 0x567AD0): 50-60, 100-110, 135-150 or 230-250 original units, 0.2 engine units each. OpenTPW keeps its own range
/// (the shaders) because its park camera is not the original's (UI-101); a literal range would hide the whole park from the default camera.
/// </remarks>
internal static class RenderFog
{
	/// <summary>The fog colour of a Direct3D colour (alpha ignored) as 0-1 channels.</summary>
	public static SVector3 ColourOf( RgbaByte colour ) => new( colour.R / 255f, colour.G / 255f, colour.B / 255f );

	/// <summary>The front end's fog colour (the lobby clear colour), which a scene without a level fades into.</summary>
	public static RgbaByte LobbyColour { get; } = new( LobbyEffects.ClearColour.R, LobbyEffects.ClearColour.G, LobbyEffects.ClearColour.B, 255 );

	/// <summary>The colour the world pass draws its fog in; the game flow sets it to the level's fog colour (or the lobby's) before each world pass.</summary>
	public static SVector3 WorldColour { get; set; } = ColourOf( LobbyColour );
}
