
namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-presence] The co-builders of a shared park as walking figures (C2d-4; docs/COOP.md, "Co-builders as figures"): while this game is logged in and in a running
/// session's park, whose room its chat follows, the flow's presence is the shared park's (<see cref="ParkPresence.ForSharedPark"/>). It is made for one park and one session and
/// goes with either, and with the login; a visit's presence is the visit's own and is never touched here. The player's own figure follows the view's centre: the overview's target,
/// or the free walk's place.
/// </summary>
internal sealed partial class GameFlow
{
	/// <summary>The shared park's presence this flow made, while it is the flow's presence (a teardown disposes it as any presence).</summary>
	private ParkPresence? coopPresence;
	/// <summary>The park, the session and the room <see cref="coopPresence"/> was made for.</summary>
	private (Level Level, CoopSession Session, string Room)? coopPresenceKey;

	/// <summary>Tests: makes the shared park's presence in place of <see cref="ParkPresence.ForSharedPark"/> (an asset-free park has no terrain to draw it on).</summary>
	internal Func<IPresenceSession, Level, string, Func<(float X, float Y)?>, Func<bool>, ParkPresence?>? CoopPresenceOverride { get; set; }

	/// <summary>The shared park's presence (tests).</summary>
	internal ParkPresence? CoopPresence => coopPresence;

	/// <summary>
	/// The presence's step, once a frame after the session's: the shared park's presence is made for the park and session the game is in, and disposed when the park, the session
	/// or the room changes, the session ends, or the player logs out. A park whose terrain or player name is missing is asked again the next frame.
	/// </summary>
	internal void StepCoopPresence()
	{
		// a teardown disposed it (as it disposes every presence), or another took the flow's place
		if ( coopPresence != null && !ReferenceEquals( presence, coopPresence ) )
		{
			coopPresence = null;
			coopPresenceKey = null;
		}
		// a visit's presence (or the visit smoke's) is the visit's own
		if ( presence != null && coopPresence == null )
			return;
		var wanted = WantedCoopPresence();
		if ( coopPresence != null && coopPresenceKey is { } key && wanted is { } same && ReferenceEquals( key.Level, same.Level ) && ReferenceEquals( key.Session, same.Session ) && key.Room == same.Room )
			return;
		if ( coopPresence != null )
		{
			coopPresence.Dispose();
			presence = null;
			coopPresence = null;
			coopPresenceKey = null;
		}
		if ( wanted is not { } made || onlineSession is not { } online )
			return;
		var (level, session, room) = made;
		bool Running() => ReferenceEquals( level.CoopSession, session ) && session.EndedWith == null && online.IsLoggedIn && online.SharedParkRoom == room;
		(float X, float Y)? View() => ViewCentre( level );
		var created = CoopPresenceOverride != null ? CoopPresenceOverride( online, level, room, View, Running ) : ParkPresence.ForSharedPark( online, level, room, View, Running );
		if ( created == null )
			return;
		presence = created;
		coopPresence = created;
		coopPresenceKey = made;
	}

	/// <summary>The park, session and room the shared park's presence is for now, or null: the online extension on, logged in, a running session's park with its HUD (not a visit), its room followed by the chat.</summary>
	private (Level Level, CoopSession Session, string Room)? WantedCoopPresence() =>
		FeatureGates.Online && onlineSession is { IsLoggedIn: true, SharedParkRoom: { } room } && Level is { IsReadOnlyVisit: false, CoopSession: { EndedWith: null } session } level && Hud != null
			? (level, session, room) : null;

	/// <summary>
	/// Where the player's view of <paramref name="level"/> is centred, in cells: the overview's target, or the free walk's place; null in any other view (the ride view, the globe), where
	/// the figure stays.
	/// </summary>
	internal static (float X, float Y)? ViewCentre( Level level )
	{
		if ( level.OriginalPark?.Heightfield is not { } field || !ReferenceEquals( Level.Current, level ) )
			return null;
		return Camera.Mode switch
		{
			ParkCameraMode park => OriginalParkPlacement.ToCells( field, park.Target.X, park.Target.Y ),
			FreeWalkCameraMode walk when ReferenceEquals( walk, level.FreeWalk ) => OriginalParkPlacement.ToCells( field, walk.Position.X, walk.Position.Y ),
			_ => null,
		};
	}
}
