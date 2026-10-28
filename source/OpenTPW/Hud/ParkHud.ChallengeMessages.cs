namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	/// <summary>
	/// Posts each challenge offer to the message tube with the panel's Accept and Decline buttons, which answer the offer the challenge system
	/// waits on (<see cref="ParkEconomy.AcceptChallenge"/>, <see cref="ParkEconomy.DeclineChallenge"/>). Until one is pressed no other challenge is
	/// offered, checked or timed out.
	/// </summary>
	// [BIN:TPW-EXE:0x004D2C50 challenge offer] the offer posts a message of type 25 (CMsgChallenge, 0x0040F7A0, vtable 0x006FD830 returns 0x19) to the bus, with a type-dependent text tag (0x004D3220), separate spoken advice and the slot's prize, time and objects
	// [BIN:TPW-EXE:0x0048E0F0 message buttons] the message panel's action 0x1A calls the accept 0x004D2DF0 and action 0x1B the decline 0x004D2EC0; both act only while an offer waits for its answer (+0x3DA) and answer the current slot (+0x3CC)
	// Offer wording/buttons stay the existing OpenTPW description. Progress and terminal rows use recovered TAG_SYSTEM bindings; the offer's 90 s expiry and decline on removal follow 0x48DB94; the repost of a refused or loaded-park offer is an extension.
	private void HookChallengeMessages()
	{
		if ( level.Park != null )
			level.Park.ChallengePresentationPosted += OnChallengePresentation;
	}

	private StringFile? challengeTemplates;
	private bool challengeTemplatesUnavailable;
	private HashSet<(int Type, ChallengePresentationKind Kind)>? unavailableChallengeCaptions;

	private void OnChallengePresentation( ChallengePresentation message )
	{
		// Apply transition identities even when localized text is unavailable. Offer repost never enters this path.
		if ( message.Kind == ChallengePresentationKind.Offer )
		{
			ApplyChallengePresentation( message, null, _ => null, _ => null );
			return;
		}
		try
		{
			if ( !challengeTemplatesUnavailable )
				challengeTemplates ??= GameLanguage.Current.LoadStrings( "TAG_SYSTEM.str" );
			string? StaffEntry( int index )
			{
				staffTypes ??= GameLanguage.Current.LoadStrings( "STAFF_TYPES.str" ).Entries;
				return index >= 0 && index < staffTypes.Length ? staffTypes[index] : null;
			}
			ApplyChallengePresentation( message, challengeTemplates, ChallengeObjectName, StaffEntry, out var unavailable );
			if ( unavailable )
				ReportUnavailableChallengeCaption( message );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or KeyNotFoundException )
		{
			challengeTemplatesUnavailable = true;
			// Loading can fail before Apply is reached. Missing captions never suppress the exact transition removal.
			if ( message.Kind is ChallengePresentationKind.Success or ChallengePresentationKind.Failure )
				ApplyChallengePresentation( message, null, _ => null, _ => null );
			ReportUnavailableChallengeCaption( message );
		}
	}

	private string? ChallengeObjectName( int infoId )
	{
		if ( !ObjectNameTable.TryGet( infoId, out var first, out var second ) )
			return null;
		var firstText = strings.Object( first );
		var secondText = second == ObjectNameTable.EmptyIndex ? "" : strings.Object( second );
		if ( string.IsNullOrWhiteSpace( firstText ) || second != ObjectNameTable.EmptyIndex && string.IsNullOrWhiteSpace( secondText ) )
			return null;
		return ObjectNames.Join( new[] { firstText, secondText } );
	}

	private void ReportUnavailableChallengeCaption( ChallengePresentation message )
	{
		unavailableChallengeCaptions ??= new();
		if ( unavailableChallengeCaptions.Add( (message.Type, message.Kind) ) )
			Log.Warning( $"Challenge {message.Kind} type {message.Type} has no complete localized template/arguments; its caption is skipped." );
	}

	/// <summary>
	/// Offer/result transitions remove only their traced replacement row; terminal captions use the immutable event payload.
	/// Returns whether a recognized transition was processed, not whether a row was admitted; a full tube is a normal refusal.
	/// A challenge caption is omitted when its localized typed template or required name is unavailable; there is no invented generic fallback.
	/// </summary>
	internal bool ApplyChallengePresentation( ChallengePresentation message, StringFile? templates, Func<int, string?> objectName, Func<int, string?> staffEntry ) =>
		ApplyChallengePresentation( message, templates, objectName, staffEntry, out _ );

	private bool ApplyChallengePresentation( ChallengePresentation message, StringFile? templates, Func<int, string?> objectName, Func<int, string?> staffEntry, out bool unavailableCaption )
	{
		unavailableCaption = false;
		if ( !ChallengeMessageRecords.TryGet( message.Type, message.Kind, out var record ) )
		{
			unavailableCaption = true;
			return false;
		}
		if ( message.Kind != ChallengePresentationKind.Progress )
			RemoveChallengeProgress( record.ReplacementTag );
		if ( message.Kind == ChallengePresentationKind.Offer )
		{
			PostChallengeOffer( message.OfferText, record.Tag, record.Lifetime > 0 ? record.Lifetime : ChallengeOfferLifetime );
			return true;
		}
		if ( !ChallengeMessageText.TryFormat( templates, message, objectName, staffEntry, out var text ) )
		{
			unavailableCaption = true;
			// Keep the existing terminal transition result: its replacement was handled even without a caption.
			return message.Kind != ChallengePresentationKind.Progress;
		}
		// A full tube is a normal insertion refusal, not evidence of a missing localized template.
		if ( message.Kind == ChallengePresentationKind.Progress )
			PostChallengeProgress( record.Tag, text );
		else
			PostChallengeTerminal( record, text );
		return true;
	}

	/// <summary>
	/// Keeps a waiting offer answerable: while the challenge system waits on an answer and neither the tube nor the open panel holds the offer's
	/// message (it was closed, deleted, or the park was loaded), the offer is posted again when a slot is free.
	/// Returns true only on insertion. A full tube leaves the offer awaiting its answer for a later ordinary update to retry.
	/// </summary>
	// [EXT:challenge-offer-repost] an offer that the tube refused (it was full) or that a loaded park lacks is posted again, so the challenge system (which waits on the answer) never stalls; a deleted or expired offer is declined as the original does (DeclineRemovedOffer)
	internal bool KeepChallengeOfferPosted( ParkEconomy? economy )
	{
		if ( economy?.Objectives.Current is not { Accepted: false } || economy.Objectives.CurrentDefinition is not { } definition )
			return false;
		if ( openedActions?.Any( IsChallengeAction ) == true || pendingActions != null && pendingActions.Any( actions => actions.Any( IsChallengeAction ) ) )
			return false;
		return PostChallengeOffer( economy.Objectives.Describe( definition ), 0, ChallengeOfferLifetime, () => economy );
	}

	/// <summary>Posts an offer as a tube row with its 90 s expiry and 5 s earlier warning timers (0x48ecce-0x48ed0e); reading it does not stop them.</summary>
	private bool PostChallengeOffer( string text, int tag, int lifetime, Func<ParkEconomy?>? economy = null )
	{
		var source = economy ?? ( () => level?.Park?.Economy );
		if ( !PostGameMessage( text, ChallengeActions( source ) ) )
			return false;
		pendingChallengeRows![^1] = new ChallengeTubeRow( new( tag, 0, 0 ), lifetime, ChallengeUiMilliseconds, isOffer: true )
		{
			OfferIdentity = source()?.Objectives.Current is { Accepted: false } offer ? offer : null
		};
		return true;
	}

	private ActiveChallenge? CurrentUnansweredOffer => level?.Park?.Economy?.Objectives.Current is { Accepted: false } offer ? offer : null;

	// The command layer's existing identity convention: definition and offered day, not a new challenge generation.
	private static bool SameUnansweredOffer( ActiveChallenge? expected, ActiveChallenge? current ) => expected != null && current is { Accepted: false }
		&& expected.DefinitionIndex == current.DefinitionIndex && expected.OfferedDay == current.OfferedDay;

	/// <summary>The delayed decline stays bound to the offer in this runtime. Answered cleanup is inert.</summary>
	private void DeclineRemovedOffer( ActiveChallenge? expected )
	{
		if ( level == null || level.IsReadOnlyVisit || !SameUnansweredOffer( expected, CurrentUnansweredOffer ) )
			return;
		// the answer is the host's (CoopBuildRights), so a guest's removal leaves the offer to the host's own timer
		if ( level.Commands.Rights is { } rights && !rights.Allows( level.Commands.Player, ParkCommandKind.AnswerChallenge ) )
			return;
		level.Commands.Issue( new AnswerChallengeCommand( expected!.DefinitionIndex, expected.OfferedDay, false ) );
	}

	private static bool IsChallengeAction( GameMessageAction action ) => action.Kind is GameMessageKind.Accept or GameMessageKind.Decline;

	/// <summary>The Accept and Decline actions of an offer's message; each answers whatever offer waits when it is pressed, then closes the panel.</summary>
	internal GameMessageAction[] ChallengeActions( Func<ParkEconomy?> economy ) => new[]
	{
		new GameMessageAction( GameMessageKind.Accept, () => AnswerChallenge( economy(), accept: true ) ),
		new GameMessageAction( GameMessageKind.Decline, () => AnswerChallenge( economy(), accept: false ) )
	};

	/// <summary>Answers the waiting offer, if any (the original's accept and decline do nothing without one), and closes the message panel.</summary>
	internal bool AnswerChallenge( ParkEconomy? economy, bool accept )
	{
		if ( economy?.Objectives.Current is not { Accepted: false } offer || level == null || RejectReadOnlyAction() )
			return false;
		// the command names the offer the message showed, so an answer that arrives late (a session's frame) never answers a newer one
		if ( !level.Commands.Issue( new AnswerChallengeCommand( offer.DefinitionIndex, offer.OfferedDay, accept ) ).Accepted )
			return false;
		// the action's result 2 removes the selected tag (0x48e531-0x48e544): the offer's row is hidden and released after 500 ms
		if ( openedChallengeRow is { IsOffer: true } row )
			BeginChallengeTerminalRemoval( row, closePanel: true );
		else
			CloseMessagePanel();
		return true;
	}
}
