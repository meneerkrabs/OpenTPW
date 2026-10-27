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
					rows.Add( new MessageTubeRowSave( MessageTubeRowSave.Terminal, text, row.Identity.Tag, row.Identity.ContextShort, row.Identity.ContextInt, row.Lifetime,
						Math.Max( 0, now - row.Origin ), row.Active, row.PreExpiryEnabled, row.PreExpiryReached ) );
				else
					rows.Add( new MessageTubeRowSave( MessageTubeRowSave.Progress, text, row.Identity.Tag, row.Identity.ContextShort, row.Identity.ContextInt, 0, 0, false, false, false ) );
			}
			else
				rows.Add( new MessageTubeRowSave( pendingActions![index].Any( IsChallengeAction ) ? MessageTubeRowSave.Offer : MessageTubeRowSave.Message, text, 0, 0, 0, 0, 0, false, false, false ) );
		}
		return rows.Count == 0 ? null : new MessageTubeSaveData { Rows = rows };
	}

	/// <summary>Puts a saved (and validated) tube into this empty HUD: no sound, help or timer queue runs for the restored rows, whose timers continue from the time they had passed.</summary>
	internal void RestoreMessageTube( MessageTubeSaveData? saved )
	{
		if ( saved == null || saved.Rows.Count == 0 || (pendingMessages?.Count ?? 0) != 0 )
			return;
		if ( saved.Problem() is { } problem )
			throw new InvalidDataException( $"The saved message tube is invalid: {problem}." );
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
				MessageTubeRowSave.Progress => new ChallengeTubeRow( identity ),
				MessageTubeRowSave.Terminal => new ChallengeTubeRow( identity, row.Lifetime, now - row.ElapsedMs )
				{
					Active = row.Active, PreExpiryEnabled = row.PreExpiryEnabled, PreExpiryReached = row.PreExpiryReached
				},
				_ => null
			} );
		}
	}
}
