namespace OpenTPW;

public sealed partial class GuestSimulation
{
	private readonly List<BalloonSpriteState> balloonPlayers = new();
	private long nextBalloonIdentity = 1;
	private bool balloonHistoryReconstructed;
	public IReadOnlyList<BalloonSpriteState> BalloonPlayers => balloonPlayers;

	internal void CreateBalloon( Guest guest )
	{
		if ( nextBalloonIdentity == long.MaxValue ) throw new InvalidOperationException( "Balloon presentation identity exhausted." );
		RemoveBalloonPlayer( guest );
		guest.HasBalloon = true;
		balloonPlayers.Add( new BalloonSpriteState { Identity = nextBalloonIdentity++, ColourGuestId = guest.Id,
			OwnerId = guest.Id, Deadline = unchecked( BodySpriteClockMilliseconds + 62 ) } );
	}

	internal void BurstBalloon( Guest guest )
	{
		foreach ( var player in balloonPlayers )
			if ( player.OwnerId == guest.Id ) player.Burst();
		guest.HasBalloon = false;
	}

	internal void DeleteBalloon( Guest guest )
	{
		RemoveBalloonPlayer( guest );
		guest.HasBalloon = false;
	}

	private void RemoveBalloonPlayer( Guest guest ) => balloonPlayers.RemoveAll( player => player.OwnerId == guest.Id );
	private void ScheduleBalloons()
	{
		for ( var index = balloonPlayers.Count - 1; index >= 0; index-- )
			if ( balloonPlayers[index].Tick( BodySpriteClockMilliseconds ) ) balloonPlayers.RemoveAt( index );
	}

	private BalloonPresentationSnapshot CaptureBalloons() => new()
	{
		Version = 1, NextIdentity = nextBalloonIdentity, HistoryReconstructed = balloonHistoryReconstructed,
		Players = balloonPlayers.Select( player => player.Copy() ).ToArray()
	};

	private static void ValidateBalloons( GuestSimulationSnapshot snapshot, Action<string> error )
	{
		if ( snapshot.Balloons is not { } state ) return; // Explicit old-save boundary.
		if ( snapshot.BodySpriteClockMilliseconds == null ) error( "Balloon history requires the sprite clock." );
		if ( state.Version != 1 || state.NextIdentity < 1 || state.Players == null )
		{ error( "Invalid balloon presentation version, identity or player array." ); return; }
		var identities = new HashSet<long>();
		var owners = new HashSet<int>();
		foreach ( var player in state.Players )
		{
			if ( player == null ) { error( "Missing balloon player." ); continue; }
			if ( player.Identity < 1 || player.Identity >= state.NextIdentity || !identities.Add( player.Identity ) || player.ColourGuestId < 1 ) error( "Invalid balloon identity." );
			if ( player.BurstStep < -1 || player.BurstStep > 13 || player.Ended != (player.BurstStep == 13) || (player.Ended && player.Visible) || (player.BurstStep is >= 0 and < 13 && !player.Visible) ||
				(player.PendingBurst && (player.BurstStep != -1 || player.OwnerId != null)) ||
				(player.OwnerId != null && (player.BurstStep != -1 || player.Ended)) ||
				(player.OwnerId == null && !player.PendingBurst && player.BurstStep == -1) ) error( "Invalid balloon program state." );
			if ( !float.IsFinite( player.X ) || !float.IsFinite( player.Y ) || !float.IsFinite( player.Lift ) || !float.IsFinite( player.LastX ) || !float.IsFinite( player.LastY ) ) error( "Invalid balloon placement." );
			if ( player.OwnerId is { } owner && (owner != player.ColourGuestId || !owners.Add( owner ) || !snapshot.Guests.Any( guest => guest != null && guest.Id == owner && guest.HasBalloon )) ) error( "Invalid held balloon owner." );
		}
		foreach ( var guest in snapshot.Guests )
			if ( guest != null && guest.HasBalloon && !owners.Contains( guest.Id ) ) error( "Held balloon has no presentation player." );
	}

	private void RestoreBalloons( BalloonPresentationSnapshot? state )
	{
		balloonPlayers.Clear();
		if ( state != null )
		{
			nextBalloonIdentity = state.NextIdentity;
			balloonHistoryReconstructed = state.HistoryReconstructed;
			balloonPlayers.AddRange( state.Players.Select( player => player.Copy() ) );
			return;
		}
		// SAVE-013: no native script epoch/phase or detached-slot reconstruction is claimed.
		nextBalloonIdentity = 1;
		balloonHistoryReconstructed = true;
		foreach ( var guest in guests ) if ( guest.HasBalloon ) CreateBalloon( guest );
	}
}
