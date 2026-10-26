using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] A guest's park windows send only what its player asked for: the object window's apply on its close, the research workload knob's projection on the window's
/// opening and the loans window's offer refresh would each be a request the guest's rights refuse, with a note about a click the player never made.
/// </summary>
public partial class CoopSessionTests
{
	[TestMethod]
	public void AGuestsWindowsSendNothingItsPlayerDidNotAskFor()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		guest.Enter();
		var hud = view.Hud;
		var refusals = guest.Level.SessionRefusals;
		// the ride's window opened and closed, the research and loans windows opened and closed
		hud.SelectObject( guest.One );
		view.Update();
		hud.SelectObject( null );
		view.Update();
		hud.OpenResearchWindow();
		view.Update();
		hud.CloseResearchWindow();
		view.Update();
		hud.OpenLoansWindow();
		view.Update();
		hud.CloseLoansWindow();
		view.Update();
		Assert.AreEqual( refusals, guest.Level.SessionRefusals, "no command went, so nothing was refused" );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopHostOnly ) );
		// a change the player made goes, and only the host may make it
		hud.SelectObject( guest.One );
		view.Update();
		hud.PendingSettings.Ride = hud.PendingSettings.Ride with { Speed = hud.PendingSettings.Ride.Speed + 1 };
		hud.SelectObject( null );
		view.Update();
		Assert.AreEqual( refusals + 1, guest.Level.SessionRefusals, "the changed ride's apply" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopHostOnly ) );
		CatchUpAndCompare( host, guest );
	}
}
