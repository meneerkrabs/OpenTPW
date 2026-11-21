using OpenTPW.UI.Original;

namespace OpenTPW;

public sealed partial class ThemeParkIncHud
{
	private bool challengesOpen;

	/// <summary>The challenge panel's parts, hidden until the Challenges button opens them: the slots on offer, accepted or won, and the career's firing line and tickets.</summary>
	// [EXT:tpi-text-hud] the text HUD's challenge list stands in for the original's challenge mails (message 0x1B, the mail view 0x52C2B2); no original panel is claimed
	private void BuildChallengePanel()
	{
		hud.Add( new UiLabel
		{
			Id = "challengePanelTitle",
			Text = () => T( OpenTpwText.TpiChallenges ),
			Font = fonts => fonts.Heading,
			Color = UiColors.Title,
			Visible = false,
			Bounds = new UiRect( 244, 210, 1060, 70 ),
			Anchor = UiAnchor.TopLeft
		} );
		hud.Add( new UiLabel
		{
			Id = "challengePanelNote",
			Text = () => T( OpenTpwText.TpiChallengesNote ),
			Font = fonts => fonts.Small,
			Color = UiColors.Text,
			Visible = false,
			Bounds = new UiRect( 244, 284, 1560, 70 ),
			Anchor = UiAnchor.TopLeft
		} );
		hud.Add( new UiLabel
		{
			Id = "challengePanelCareer",
			Text = ChallengeCareerText,
			Font = fonts => fonts.Small,
			Color = UiColors.Text,
			Visible = false,
			Bounds = new UiRect( 244, 340, 1560, 50 ),
			Anchor = UiAnchor.TopLeft
		} );
		hud.Add( new UiScrollList
		{
			Id = "challengePanelList",
			Rows = ChallengeRows,
			RowHeight = 52,
			RowActivated = ActivateChallengeRow,
			ClicksOnActivate = true,
			Visible = false,
			Bounds = new UiRect( 244, 400, 1560, 980 ),
			Anchor = UiAnchor.TopLeft
		} );
		hud.Add( new UiButton
		{
			Id = "challengePanelCheatSkipMission",
			Text = () => SkipMissionLabel,
			Font = fonts => fonts.Menu,
			Visible = false,
			Clicked = () => QueueTpiCheat( CheatKind.SkipMission ),
			Bounds = new UiRect( 244, 1390, 600, 80 ),
			Anchor = UiAnchor.TopLeft
		} );
		hud.Add( new UiButton
		{
			Id = "challengePanelCheatGetPromoted",
			Text = () => GetPromotedLabel,
			Font = fonts => fonts.Menu,
			Visible = false,
			Clicked = () => QueueTpiCheat( CheatKind.GetPromoted ),
			Bounds = new UiRect( 864, 1390, 600, 80 ),
			Anchor = UiAnchor.TopLeft
		} );
		hud.Add( new UiButton
		{
			Id = "challengePanelClose",
			Text = () => T( OpenTpwText.TpiClose ),
			Font = fonts => fonts.Menu,
			Visible = false,
			Clicked = CloseChallengePanel,
			Bounds = new UiRect( 1330, 210, 474, 84 ),
			Anchor = UiAnchor.TopLeft
		} );
	}

	/// <summary>The slots of the listed rows, in row order (the rows <see cref="ChallengeRows"/> listed last).</summary>
	private readonly List<int> challengeRowSlots = new();

	private string ChallengeCareerText() => level.Park?.Economy is { TpiChallenges: not null, TpiCareer: { } career }
		? string.Format( T( OpenTpwText.TpiChallengesCareer ), career.FiringLine, career.GoldenTickets ) : "";

	/// <summary>A slot's title: <c>CHALLENGE_TITLES.str</c> at its <c>ScriptIndex</c>, or the panel's name and the index when the table lacks it.</summary>
	internal string ChallengeTitle( ThemeParkIncChallengeSlot slot ) =>
		strings.ChallengeTitle( slot.ScriptIndex ) is { Length: > 0 } title ? title : $"{T( OpenTpwText.TpiChallenges )} {slot.ScriptIndex}";

	/// <summary>Every slot that has left the idle state, in slot order: on offer (or lost, which is the same state 1), in its grace period, running, or won.</summary>
	internal IReadOnlyList<string> ChallengeRows()
	{
		challengeRowSlots.Clear();
		if ( level.Park?.Economy is not { TpiChallenges: { } challenges } economy )
			return Array.Empty<string>();
		var rows = new List<string>();
		for ( var index = 0; index < challenges.Slots.Count; index++ )
		{
			var slot = challenges.Slots[index];
			if ( slot.Type == 0 )
				continue;
			var title = ChallengeTitle( slot );
			var row = slot.Status switch
			{
				ThemeParkIncChallengeStatus.Offered => string.Format( T( OpenTpwText.TpiChallengeOfferedRow ), title ),
				ThemeParkIncChallengeStatus.Grace => string.Format( T( OpenTpwText.TpiChallengeGraceRow ), title, ThemeParkIncChallenges.DaysLeft( slot, economy.Turn ), slot.Value ),
				ThemeParkIncChallengeStatus.Running => string.Format( T( OpenTpwText.TpiChallengeRunningRow ), title, ThemeParkIncChallenges.DaysLeft( slot, economy.Turn ), slot.Value, slot.TargetVal ),
				ThemeParkIncChallengeStatus.Won => string.Format( T( OpenTpwText.TpiChallengeWonRow ), title ),
				_ => null
			};
			if ( row == null )
				continue;
			rows.Add( row );
			challengeRowSlots.Add( index );
		}
		if ( rows.Count == 0 )
			rows.Add( T( OpenTpwText.TpiChallengesNone ) );
		return rows;
	}

	/// <summary>A row activated: an offered challenge is accepted when the mail's accept control would be enabled (state 1, park open), else a note says to open the park.</summary>
	internal void ActivateChallengeRow( int index )
	{
		ChallengeRows();
		if ( index < 0 || index >= challengeRowSlots.Count || level.Park?.Economy is not { TpiChallenges: { } challenges } economy )
			return;
		var slotIndex = challengeRowSlots[index];
		if ( challenges.Slots[slotIndex].Status != ThemeParkIncChallengeStatus.Offered )
			return;
		if ( !economy.CanAcceptThemeParkIncChallenge( slotIndex ) )
		{
			PostMessage( T( OpenTpwText.TpiChallengeOpenPark ) );
			GameAudio.PlayUi( GameAudio.ErrorEvent );
			return;
		}
		economy.AcceptThemeParkIncChallenge( slotIndex );
		PostMessage( string.Format( T( OpenTpwText.TpiChallengeAcceptedNote ), ChallengeTitle( challenges.Slots[slotIndex] ) ) );
	}

	/// <summary>The offers, wins and losses since the last frame, as notes (the original's challenge mails of message 0x1B kinds 0, 2 and 3).</summary>
	private void ShowChallengeNotices()
	{
		if ( level.Park?.Economy is not { TpiChallenges: { } challenges } economy || economy.TpiChallengeNotices.Count == 0 )
			return;
		foreach ( var notice in economy.TpiChallengeNotices )
		{
			var title = ChallengeTitle( challenges.Slots[notice.Slot] );
			var key = notice.Kind switch
			{
				ThemeParkIncChallengeNoticeKind.Offered => OpenTpwText.TpiChallengeOfferNote,
				ThemeParkIncChallengeNoticeKind.Won => OpenTpwText.TpiChallengeWonNote,
				_ => OpenTpwText.TpiChallengeLostNote
			};
			PostMessage( string.Format( T( key ), title ) );
		}
		economy.TpiChallengeNotices.Clear();
	}

	private void ToggleChallengePanel()
	{
		if ( challengesOpen )
			CloseChallengePanel();
		else
		{
			CloseBuildWindow();
			CloseStaffPanel();
			challengesOpen = true;
			SetChallengePanelVisible( true );
		}
	}

	private void CloseChallengePanel()
	{
		challengesOpen = false;
		SetChallengePanelVisible( false );
	}

	private void SetChallengePanelVisible( bool visible )
	{
		foreach ( var element in hud.Elements )
		{
			if ( element.Id.StartsWith( "challengePanel", StringComparison.Ordinal ) )
				element.Visible = visible && (!element.Id.StartsWith( "challengePanelCheat", StringComparison.Ordinal ) || TpiCheatsAvailable);
		}
	}

	/// <summary>The debug dialog's button texts, as the executable holds them (UTF-16 at <c>0x9B79C8</c> and <c>0x9B79A8</c>; the original's dialog is English in every language).</summary>
	internal const string SkipMissionLabel = "Skip Mission";
	internal const string GetPromotedLabel = "Get Promoted";

	/// <summary>Whether the two Theme Park Inc cheats are offered: the cheats extension is on and the park is a writable one with a career.</summary>
	// [EXT:cheats-extension] Theme Park Inc's debug buttons are offered only with opentpw.cheats on (user decision 2026-11-21); the original's dialog 0x532A40 is a developer build's
	internal bool TpiCheatsAvailable => FeatureGates.Cheats && level is { IsReadOnlyVisit: false } && level.Park?.Economy.TpiCareer != null;

	/// <summary>Queues one of the two cheats for the next park turn and says so.</summary>
	internal void QueueTpiCheat( CheatKind kind )
	{
		if ( !TpiCheatsAvailable || level.Park?.Economy is not { } economy )
			return;
		if ( economy.QueueCheat( new CheatAction( kind ) ) )
			PostMessage( kind == CheatKind.SkipMission ? SkipMissionLabel : GetPromotedLabel );
	}
}
