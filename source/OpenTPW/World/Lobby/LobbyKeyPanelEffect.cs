namespace OpenTPW;

/// <summary>
/// The particle effect the lobby's key panel keeps running while the player owns enough keys for the selected island: the screen-space template 0x61
/// at the horizontal centre of the screen and the vertical centre of the panel's frame control (0x1E0EA), the place <see cref="LobbyEnterEffect"/> uses for 0x62.
/// </summary>
// [BIN:TPW-EXE:0x4B960E key panel effect] the refresh 0x4B9340 calls the creator 0x521E60 with effect 0x61, x 0xC350, y 0 and z from the frame control's rectangle (the same arithmetic as 0x4B8FD0) only while its global handle (0x7CC4BC) is zero, and stores the result; the selection-change refresh 0x4B90F0 does the same at 0x4B929B
// [BIN:TPW-EXE:0x4B9662 key panel effect end] a count below the selected island's cost ends the effect with 0x51FEB0 and clears the handle; the panel's window procedure does the same when the panel is hidden or destroyed (0x4B8B70: message 0x11 with 0, message 0x14, then 0x4B8EE0(0, 0), 0x4B8F9B)
internal static class LobbyKeyPanelEffect
{
	/// <summary>The template the panel keeps running (a screen-space effect).</summary>
	public const int EffectId = 0x61;

	/// <summary>The effect's native position: <see cref="LobbyEnterEffect.Position"/>, the key panel frame's centre row at the screen's centre column.</summary>
	public static (int X, int Y, int Z) Position() => LobbyEnterEffect.Position();

	/// <summary>
	/// A screen-space particle host for the effect (its own pool, deleted with the other entities when the lobby is torn down: a park load ends the pool's screen-space emitters, 0x5200B0);
	/// null when there is no renderer or the particle files are missing (the effect is presentation only).
	/// </summary>
	internal static IAdvisorParticlePool? CreateHost() =>
		ParticleRendererModule.CreateScreenRenderer() is { } renderer ? new ParticleSystemPool( renderer.Particles ) : null;
}
