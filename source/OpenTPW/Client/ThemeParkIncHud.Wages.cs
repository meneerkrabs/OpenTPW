namespace OpenTPW;

public sealed partial class ThemeParkIncHud
{
	/// <summary>The open wage demand window (null when it is closed).</summary>
	internal ThemeParkIncWageDemandWindow? WageDemandWindow { get; private set; }

	/// <summary>The open wage negotiation dialog (null when it is closed).</summary>
	internal ThemeParkIncWageNegotiationWindow? NegotiationWindow { get; private set; }

	/// <summary>The name a wage demand row's member has now (its key is his thing).</summary>
	private string MemberName( ThemeParkIncTagTubeRow row ) =>
		level.Park?.Economy.ThemeParkIncMemberOfRow( row ) is { } member ? strings.StaffName( (int)member.Role, member.NameIndex ) : "";

	/// <summary>The tube window's go-to (<c>0x5511B0</c>): a wage demand row's kind 0xE opens the wage demand window on its member and demand; the others run the letter go-to.</summary>
	// [BIN:TPI-EXE:0x55147A wage demand go-to] kind 0xE: 0x59ABB0 on the controller 0xA6C2B0 with the thing of +0xE14 and the demand +0xE18
	internal void RunTubeGoto( ThemeParkIncTagTubeRow row )
	{
		if ( row.GotoKind == ThemeParkIncTagLetter.WageDemandGoto )
			OpenWageDemand( row );
		else
			RunLetterGoto( row.GotoKind, row.GotoArgument );
	}

	/// <summary>
	/// Opens the wage demand window for a wage demand row; nothing while one is open (the builder tests <c>+0</c>) or when the row's member is gone. True when it opened.
	/// </summary>
	// [BIN:TPI-EXE:0x59ABB3 wage window once] +0 set: nothing; the HUD's window closes first (0x5013C0) and the window registers itself (0x501CD0); the rows of a dismissed member are deleted with him
	// (0x636B10), so the thing word of a listed row names a member
	internal bool OpenWageDemand( ThemeParkIncTagTubeRow row )
	{
		if ( WageDemandWindow != null || level.Park?.Economy is not { TpiStaff: { } staff } economy || economy.ThemeParkIncMemberOfRow( row ) is not { } member )
			return false;
		CloseHudWindow();
		ThemeParkIncWageDemandWindow? window = null;
		window = new ThemeParkIncWageDemandWindow( strings, staff.Settings, member, row.GotoArgument, () => RemoveScreen( ThemeParkIncWageDemandWindow.ScreenName, () => WageDemandWindow = null ),
			OpenNegotiation, level.DismissTpiMember );
		WageDemandWindow = window;
		Stack.Push( window.Build() );
		return true;
	}

	/// <summary>Opens the negotiation dialog on the offer (<c>0x559C40</c>); nothing while one is open.</summary>
	internal void OpenNegotiation( ThemeParkIncStaffMember member, int demand, int offer )
	{
		if ( NegotiationWindow != null || level.Park?.Economy is not { TpiStaff: { } staff } || !staff.Members.Contains( member ) )
			return;
		CloseHudWindow();
		var window = new ThemeParkIncWageNegotiationWindow( strings, staff.Settings, member, demand, offer,
			() => RemoveScreen( ThemeParkIncWageNegotiationWindow.ScreenName, () => NegotiationWindow = null ),
			( result, choice ) => level.EndTpiNegotiation( member, result, offer, demand, choice ) );
		NegotiationWindow = window;
		Stack.Push( window.Build() );
	}

	/// <summary>The HUD's message 5 to its registered window (<c>0x5013C0</c>): an open letter, mission window, tag tube, wage window or negotiation dialog closes.</summary>
	private void CloseHudWindow()
	{
		if ( LetterOpen )
			closeOpenLetter?.Invoke();
		CloseMissionWindow();
		CloseTagTube();
		WageDemandWindow?.Close();
		NegotiationWindow?.Close();
	}

	private void RemoveScreen( string name, Action cleared )
	{
		Stack.RemoveWhere( screen => screen.Name == name );
		cleared();
	}
}
