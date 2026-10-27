namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	/// <summary>
	/// The tube as a park save holds it: every visible slot, oldest first, or null while the tube is empty (the field is then left out of the file). Rows that wait for their delayed release are
	/// already deleted and are not saved; the open panel is not either (a loaded park starts with its panel closed).
	/// </summary>
	// [BIN:TPW-EXE:0x48EE50 tube saved] the original's SaveTags writes the number of occupied slots, then per slot the tag id, the text, the lifetime and the context words (0x21C bytes each); its tube is part of the park save, so a loaded park shows the messages it was saved with
	internal MessageTubeSaveData? CaptureMessageTube()
	{
		if ( pendingMessages == null )
			return null;
		var now = ChallengeUiMilliseconds;
		var rows = new List<MessageTubeRowSave>();
		for ( var index = 0; index < pendingMessages.Count; index++ )
		{
			if ( !IsPendingMessageVisible( index ) )
				continue;
			var text = pendingMessages[index];
			if ( pendingChallengeRows![index] is { } row )
			{
				if ( row.IsTerminal )
				{
					// A read row has no running clock. Suspension restarts active origins on resume, so its continuation has a full lifetime.
					// For an active clock, every age at or past its lifetime has the same queued expiry; do not serialize unbounded wall time.
					var elapsed = !row.Active || challengeTimersSuspended ? 0 : Math.Clamp( now - row.Origin, 0L, (long)row.Lifetime );
					rows.Add( new MessageTubeRowSave( MessageTubeRowSave.Terminal, text, row.Identity.Tag, row.Identity.ContextShort, row.Identity.ContextInt, row.Lifetime,
						elapsed, row.Active, row.PreExpiryEnabled, row.PreExpiryReached )
					{
						// Pending delivery is not its effect: after load, input may still read this row before dispatch and cancel either event.
						PendingPreExpiry = row.Active && challengeTimerEvents?.Any( entry => ReferenceEquals( entry.Row, row ) && entry.Kind == ChallengeTimerEvent.PreExpiry ) == true,
						PendingExpiry = row.Active && challengeTimerEvents?.Any( entry => ReferenceEquals( entry.Row, row ) && entry.Kind == ChallengeTimerEvent.Expiry ) == true
					} );
				}
				else
					rows.Add( new MessageTubeRowSave( MessageTubeRowSave.Progress, text, row.Identity.Tag, row.Identity.ContextShort, row.Identity.ContextInt, 0, 0, false, false, false ) );
			}
			else
				rows.Add( new MessageTubeRowSave( pendingActions![index].Any( IsChallengeAction ) ? MessageTubeRowSave.Offer : MessageTubeRowSave.Message, text, 0, 0, 0, 0, 0, false, false, false ) );
		}
		return rows.Count == 0 ? null : new MessageTubeSaveData { Rows = rows };
	}

	/// <summary>Puts a saved (and validated) tube into this empty HUD without dispatch, sound or help. Pending timers are queued for normal delivery after input; running clocks continue from their saved age.</summary>
	internal void RestoreMessageTube( MessageTubeSaveData? saved )
	{
		if ( saved == null )
			return;
		if ( saved.Problem() is { } problem )
			throw new InvalidDataException( $"The saved message tube is invalid: {problem}." );
		if ( saved.Rows.Count == 0 || (pendingMessages?.Count ?? 0) != 0 )
			return;
		pendingMessages = new List<string>();
		pendingActions = new List<GameMessageAction[]>();
		pendingChallengeRows = new List<ChallengeTubeRow?>();
		var now = ChallengeUiMilliseconds;
		foreach ( var row in saved.Rows )
		{
			pendingMessages.Add( row.Text );
			pendingActions.Add( row.Kind == MessageTubeRowSave.Offer ? ChallengeActions( () => level?.Park?.Economy ) : Array.Empty<GameMessageAction>() );
			var identity = new ChallengeMessageIdentity( row.Tag, row.ContextShort, row.ContextInt );
			var challengeRow = row.Kind switch
			{
				MessageTubeRowSave.Progress => new ChallengeTubeRow( identity ),
				MessageTubeRowSave.Terminal => new ChallengeTubeRow( identity, row.Lifetime, now - row.ElapsedMs )
				{
					Active = row.Active, PreExpiryEnabled = row.PreExpiryEnabled, PreExpiryReached = row.PreExpiryReached,
					PreExpiryQueued = row.PendingPreExpiry, ExpiryQueued = row.PendingExpiry
				},
				_ => null
			};
			pendingChallengeRows.Add( challengeRow );
			if ( challengeRow != null && row.PendingPreExpiry )
				(challengeTimerEvents ??= new()).Enqueue( (challengeRow, ChallengeTimerEvent.PreExpiry) );
			if ( challengeRow != null && row.PendingExpiry )
				(challengeTimerEvents ??= new()).Enqueue( (challengeRow, ChallengeTimerEvent.Expiry) );
		}
	}
}
