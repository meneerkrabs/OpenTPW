namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	/// <summary>
	/// The tube as a park save holds it: every visible slot, oldest first, or null while the tube is empty (the field is then left out of the file). Rows that wait for their delayed release are
	/// already deleted and are not saved; the open panel is not either (a loaded park starts with its panel closed).
	/// </summary>
	// [BIN:TPW-EXE:0x48EE50 tube saved] the tube's saver writes the number of occupied slots, then per slot the tag id, the text, the lifetime and the context words (0x21C bytes each), not the time left; its loader 0x48EF70 adds each record through the tube's add routine 0x48EB20 again
	internal MessageTubeSaveData? CaptureMessageTube()
	{
		if ( pendingMessages == null )
			return null;
		var rows = new List<MessageTubeRowSave>();
		for ( var index = 0; index < pendingMessages.Count; index++ )
		{
			if ( !IsPendingMessageVisible( index ) )
				continue;
			var text = pendingMessages[index];
			if ( pendingChallengeRows![index] is { } row )
			{
				if ( row.IsOffer )
					rows.Add( new MessageTubeRowSave( MessageTubeRowSave.Offer, text, 0, 0, 0, 0, false ) );
				else if ( row.IsTerminal )
					rows.Add( new MessageTubeRowSave( MessageTubeRowSave.Terminal, text, row.Identity.Tag, row.Identity.ContextShort, row.Identity.ContextInt, row.Lifetime, !row.Active ) );
				else
					rows.Add( new MessageTubeRowSave( MessageTubeRowSave.Progress, text, row.Identity.Tag, row.Identity.ContextShort, row.Identity.ContextInt, 0, false ) );
			}
			else
				rows.Add( new MessageTubeRowSave( pendingActions![index].Any( IsChallengeAction ) ? MessageTubeRowSave.Offer : MessageTubeRowSave.Message, text, 0, 0, 0, 0, false ) );
		}
		return rows.Count == 0 ? null : new MessageTubeSaveData { Rows = rows };
	}

	/// <summary>
	/// Puts a saved (and validated) tube into this empty HUD the way the original's loader does: every record is added again (the new-message cue plays per row) and a terminal row's timers
	/// start over from the full lifetime; a row saved as read keeps its timers stopped.
	/// </summary>
	// [BIN:TPW-EXE:0x48EF70 tube loader] each saved record goes through the add routine 0x48EB20: the expiry timer 0x80080 is set to the lifetime and the warning timer 0x80081 to 5000 ms less (0x48ECD2..0x48ED0E) and cat_ui 0x1E plays (0x48EDBD); a record saved as read is then selected through 0x48D9C0, which stops the timers
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
			pendingChallengeRows.Add( row.Kind switch
			{
				MessageTubeRowSave.Offer => new ChallengeTubeRow( identity, ChallengeOfferLifetime, now, isOffer: true ),
				MessageTubeRowSave.Progress => new ChallengeTubeRow( identity ),
				MessageTubeRowSave.Terminal => new ChallengeTubeRow( identity, row.Lifetime, now )
				{
					Active = !row.Read, PreExpiryEnabled = !row.Read
				},
				_ => null
			} );
			GameAudio.PlayUi( MessagePostedEvent );
		}
	}
}
