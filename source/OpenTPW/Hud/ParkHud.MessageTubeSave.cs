namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	/// <summary>
	/// The tube as a park save holds it: every visible slot, oldest first, or null while the tube is empty (the field is then left out of the file). Rows that wait for their delayed release are
	/// already deleted and are not saved, except an offer still awaiting its delayed decline. That row stays hidden after load; the open panel is not saved either.
	/// </summary>
	// [BIN:TPW-EXE:0x48EE50 tube saved] the tube's saver writes the number of occupied slots, then per slot the tag id, the text, the lifetime and the context words (0x21C bytes each), not the time left; its loader 0x48EF70 adds each record through the tube's add routine 0x48EB20 again
	internal MessageTubeSaveData? CaptureMessageTube()
	{
		if ( pendingMessages == null )
			return null;
		var rows = new List<MessageTubeRowSave>();
		var now = ChallengeUiMilliseconds;
		for ( var index = 0; index < pendingMessages.Count; index++ )
		{
			var row = pendingChallengeRows![index];
			if ( !IsPendingMessageVisible( index ) )
			{
				if ( row is { IsOffer: true, ReleaseAt: { } release } && SameUnansweredOffer( row.PendingOfferIdentity, CurrentUnansweredOffer ) )
					rows.Add( new MessageTubeRowSave( MessageTubeRowSave.Offer, pendingMessages[index], 0, 0, 0, 0, false )
					{
						OfferReleaseDelayMs = (int)Math.Clamp( release - now, 0L, MessageTubeSaveData.MaximumOfferReleaseDelay )
					} );
				continue;
			}
			var text = pendingMessages[index];
			// [BIN:TPW-EXE:0x48EE50 tube saved] the record holds the tag's full lifetime, never the time left or whether it was read: the loader's add routine starts the timers again
			if ( row.IsOffer )
				rows.Add( new MessageTubeRowSave( MessageTubeRowSave.Offer, text, 0, 0, 0, 0, false ) );
			else if ( row.IsTerminal )
				rows.Add( new MessageTubeRowSave( MessageTubeRowSave.Terminal, text, row.Identity.Tag, row.Identity.ContextShort, row.Identity.ContextInt, row.Lifetime, false ) );
			else if ( row.Identity.Tag != MessageTag.NoTag )
				rows.Add( new MessageTubeRowSave( MessageTubeRowSave.Progress, text, row.Identity.Tag, row.Identity.ContextShort, row.Identity.ContextInt, 0, false ) );
			else
				rows.Add( new MessageTubeRowSave( pendingActions![index].Any( IsChallengeAction ) ? MessageTubeRowSave.Offer : MessageTubeRowSave.Message, text, 0, 0, 0, 0, false ) );
		}
		return rows.Count == 0 ? null : new MessageTubeSaveData { Rows = rows };
	}

	/// <summary>
	/// Puts a saved (and validated) tube into this empty HUD: visible rows replay the new-message cue and every row's timers start over from the full lifetime;
	/// only a row an older OpenTPW saved as read keeps its timers stopped. A hidden pending offer removal resumes silently with only its remaining release delay.
	/// </summary>
	// [BIN:TPW-EXE:0x48EF70 tube loader] each saved record goes through the add routine 0x48EB20: the expiry timer 0x80080 is set to the lifetime and the warning timer 0x80081 to 5000 ms less (0x48ECD2..0x48ED0E) and cat_ui 0x1E plays (0x48EDBD); the loader's own record byte +0x24 (the open flag the post wrapper 0x48EDE0 tests) is never read from the file, so no record is selected on load
	internal void RestoreMessageTube( MessageTubeSaveData? saved )
	{
		if ( saved == null )
			return;
		if ( saved.Problem() is { } problem )
			throw new InvalidDataException( $"The saved message tube is invalid: {problem}." );
		if ( saved.Rows.Any( row => row.OfferReleaseDelayMs != null ) && CurrentUnansweredOffer == null )
			throw new InvalidDataException( "A pending offer removal has no unanswered challenge." );
		if ( saved.Rows.Count == 0 || (pendingMessages?.Count ?? 0) != 0 )
			return;
		pendingMessages = new List<string>();
		pendingActions = new List<GameMessageAction[]>();
		pendingChallengeRows = new List<TubeRow>();
		var now = ChallengeUiMilliseconds;
		foreach ( var row in saved.Rows )
		{
			pendingMessages.Add( row.Text );
			pendingActions.Add( row.Kind == MessageTubeRowSave.Offer ? ChallengeActions( () => level?.Park?.Economy ) : Array.Empty<GameMessageAction>() );
			var tag = row.Kind == MessageTubeRowSave.Message ? MessageTag.NoTag : row.Tag;
			var identity = new ChallengeMessageIdentity( tag, row.ContextShort, row.ContextInt );
			var record = MessageTagTable.For( tag );
			var restored = row.Kind switch
			{
				MessageTubeRowSave.Offer => new TubeRow( identity, ChallengeOfferLifetime, isOffer: true, icon: record?.Icon ?? 4 )
				{
					OfferIdentity = CurrentUnansweredOffer,
					PendingOfferIdentity = row.OfferReleaseDelayMs != null ? CurrentUnansweredOffer : null,
					ReleaseAt = row.OfferReleaseDelayMs is { } delay ? now + delay : null,
					Hidden = row.OfferReleaseDelayMs != null
				},
				MessageTubeRowSave.Terminal => new TubeRow( identity, row.Lifetime, icon: record?.Icon ?? 0 ),
				_ => new TubeRow( identity, 0, icon: record?.Icon ?? 0 )
			};
			pendingChallengeRows.Add( restored );
			// [BIN:TPW-EXE:0x48EF70 tube loader] every record goes through the add routine, which starts the timers again from the full lifetime; only a row an earlier OpenTPW wrote as read keeps its timers stopped
			if ( !row.Read && row.OfferReleaseDelayMs == null )
				restored.StartTimers( ChallengeUiTicks );
			if ( row.OfferReleaseDelayMs == null )
			{
				ShowTubeRow( pendingMessages.Count - 1 );
				GameAudio.PlayUi( MessagePostedEvent );
			}
		}
	}
}
