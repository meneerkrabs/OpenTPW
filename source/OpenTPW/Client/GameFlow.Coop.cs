namespace OpenTPW;

internal sealed partial class GameFlow
{
	/// <summary>
	/// [EXT:online-coop-lockstep] A level's time scale in a co-op session: the park's own speed, whatever screens this player has open (the pause menu, the map window, a postcard
	/// and the delete confirmations stop no shared park); null outside a session.
	/// </summary>
	internal static float? SessionTimeScale( Level level ) => level.CoopRole == CoopRole.None ? null : GameSpeedScale.Of( level.Park?.Economy.Speed ?? GameSpeed.Normal );
}
