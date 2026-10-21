namespace OpenTPW;

internal sealed partial class GameFlow
{
	/// <summary>
	/// [EXT:online-coop-snapshot-join] Every way out of a shared park (leaving it, a load, a restart, quitting) ends its session first and lets the player's progress take the park back (its
	/// hand-back, docs/COOP.md "Golden tickets in a session"), before anything saves the park or detaches progress, so an exit checkpoint holds the reclaimed park.
	/// </summary>
	private void LeaveCoopSession( string reason )
	{
		Level?.CoopSession?.End( reason );
		// also when the session ended earlier in this frame (the server or the host's stream ended it): its hand-back is still pending
		progressOwner?.Pump();
	}

	/// <summary>
	/// [EXT:online-coop-lockstep] A level's time scale in a co-op session: the park's own speed, whatever screens this player has open (the pause menu, the map window, a postcard
	/// and the delete confirmations stop no shared park); null outside a session.
	/// </summary>
	internal static float? SessionTimeScale( Level level ) => level.CoopRole == CoopRole.None ? null : GameSpeedScale.Of( level.Park?.Economy.Speed ?? GameSpeed.Normal );
}
