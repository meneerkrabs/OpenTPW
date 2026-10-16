using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// The particle effect the original's enter-park routine starts over the lobby's key panel: the screen-space template 0x62 at the
/// panel control's vertical centre, in the screen-space particle host the lobby screens get (see <see cref="ParticleRendererModule.CreateScreenRenderer"/>).
/// </summary>
// [BIN:TPW-EXE:0x4B8FD0 enter-park routine] with the lobby window present (0x7CC4B4) and the game-mode global 0xFB3B7C not 2, it fetches the control 0x1E0EA of the lobby window (0x65D7D0), copies its rectangle (0x65D42E with the animation offset +0xF8..+0xFE taken off), and calls the particle creator 0x521E60 with effect 0x62, x 0xC350 (50000, the screen's horizontal centre), y 0 and z = ((bottom - top) >> 1 + top) * 75000 / 0x600, then plays cat_globallobbysfx 4 (0x51BFC0); the whole block is skipped in game mode 2 (Instant Action)
// [BIN:TPW-EXE:0x521E60 particle creator] the creator shifts the three coordinates right by 4 into the emitter and starts the pool's emitter of template 0x62 (a screen-space template, byte +5; the post-load pass 0x5200B0 kills screen-space emitters when a park loads)
public static class LobbyEnterEffect
{
	/// <summary>The template the routine starts (a screen-space effect, template byte +5 set).</summary>
	public const int EffectId = 0x62;

	/// <summary>The lobby control the effect is placed on: the key panel (0x1E0EA, "s_key").</summary>
	public const uint KeyPanelControl = 0x1E0EA;

	/// <summary>The native x: 0xC350, the horizontal centre of the screen (<see cref="ParticleScreenSpace.CentreX"/>).</summary>
	public const int NativeX = 0xC350;

	// [BIN:TPW-EXE:0x4B9075 z scale] the canvas height is 0x600 (1536) rows and the native screen height 75000 (twice 37500): z = row * 75000 / 0x600, signed division truncating toward zero
	private const int CanvasHeight = 0x600;
	private const int NativeHeight = 75000;

	/// <summary>Whether the effect starts for the game mode that follows: not in Instant Action (game mode 2), the same condition that skips the enter-park sound.</summary>
	public static bool Starts( bool instantAction ) => !instantAction;

	/// <summary>The native z of the effect for a control spanning <paramref name="top"/> to <paramref name="bottom"/> of the 1536-row canvas.</summary>
	public static int NativeZ( int top, int bottom ) => ((bottom - top >> 1) + top) * NativeHeight / CanvasHeight;

	/// <summary>The native position of the effect over the lobby's key panel (control 0x1E0EA of layout table Lobby).</summary>
	public static (int X, int Y, int Z) Position()
	{
		var rect = OriginalLayoutTables.Lobby.Control( KeyPanelControl ).Rect;
		return (NativeX, 0, NativeZ( (int)rect.Y, (int)(rect.Y + rect.Height) ));
	}

	/// <summary>Starts the effect in <paramref name="pool"/> for the game mode that follows; returns the pool's handle, or null when it did not start (Instant Action, or the pool refused).</summary>
	internal static object? Start( IAdvisorParticlePool pool, bool instantAction ) =>
		Starts( instantAction ) ? pool.Create( EffectId, Position() ) : null;
}

/// <summary>A <see cref="ParticleSystem"/> as the screen effects' pool (handles are the system's own emitter handles).</summary>
internal sealed class ParticleSystemPool : IAdvisorParticlePool
{
	private readonly ParticleSystem system;

	public ParticleSystemPool( ParticleSystem system ) => this.system = system;

	public object? Create( int effectId, (int X, int Y, int Z) native )
	{
		var handle = system.Create( effectId, native );
		return handle is 0 or -1 ? null : handle;
	}

	public void Move( object handle, (int X, int Y, int Z) native )
	{
		if ( handle is int emitter )
			system.Move( emitter, native );
	}

	public void Kill( object handle )
	{
		if ( handle is int emitter )
			system.Kill( emitter );
	}
}
