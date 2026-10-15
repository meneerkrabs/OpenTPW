namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	/// <summary>
	/// Posts each challenge offer to the message tube with the panel's Accept and Decline buttons, which answer the offer the challenge system
	/// waits on (<see cref="ParkEconomy.AcceptChallenge"/>, <see cref="ParkEconomy.DeclineChallenge"/>). Until one is pressed no other challenge is
	/// offered, checked or timed out.
	/// </summary>
	// [BIN:TPW-EXE:0x004D2C50 challenge offer] the offer posts a message of type 25 (CMsgChallenge, 0x0040F7A0, vtable 0x006FD830 returns 0x19) to the bus, with the slot's advice text by type (0x004D3220) and the slot's prize, time and objects
	// [BIN:TPW-EXE:0x0048E0F0 message buttons] the message panel's action 0x1A calls the accept 0x004D2DF0 and action 0x1B the decline 0x004D2EC0; both act only while an offer waits for its answer (+0x3DA) and answer the current slot (+0x3CC)
	// The tube text is OpenTPW's description of the challenge (ParkObjectives.Describe): the advisor's spoken advice for the offer (advice 0xF7..0x117 by type, 0x004D3220) has no shipped text table
	private void HookChallengeMessages()
	{
		if ( level.Park != null )
			level.Park.EventRaised += OnChallengeEvent;
	}

	private void OnChallengeEvent( ParkEvent item )
	{
		if ( item.Kind != ParkEventKind.ChallengeOffered )
			return;
		PostGameMessage( item.Detail, ChallengeActions( () => level.Park?.Economy ) );
	}

	/// <summary>The Accept and Decline actions of an offer's message; each answers whatever offer waits when it is pressed, then closes the panel.</summary>
	internal GameMessageAction[] ChallengeActions( Func<ParkEconomy?> economy ) => new[]
	{
		new GameMessageAction( GameMessageKind.Accept, () => AnswerChallenge( economy(), accept: true ) ),
		new GameMessageAction( GameMessageKind.Decline, () => AnswerChallenge( economy(), accept: false ) )
	};

	/// <summary>Answers the waiting offer, if any (the original's accept and decline do nothing without one), and closes the message panel.</summary>
	internal bool AnswerChallenge( ParkEconomy? economy, bool accept )
	{
		if ( economy?.Objectives.Current is not { Accepted: false } || level != null && RejectReadOnlyAction() )
			return false;
		if ( accept )
			economy.AcceptChallenge();
		else
			economy.DeclineChallenge();
		CloseMessagePanel();
		return true;
	}
}
