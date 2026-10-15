using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

[TestClass]
public class ParkInputGuardTests
{
	[TestMethod]
	public void RideViewSuspendsArmedPrototypeAndObjectTools()
	{
		foreach ( var placing in new[] { false, true } )
			Assert.AreEqual( Level.WorldClickAction.None, Level.WorldClick( riding: true, leftDown: true,
				previousLeftDown: false, mouseCaptured: false, placing ), "even an uncaptured fresh click cannot use an armed tool while riding" );
		Assert.AreEqual( Level.WorldClickAction.PlacePrototype, Level.WorldClick( false, true, false, false, true ) );
		Assert.AreEqual( Level.WorldClickAction.UseTool, Level.WorldClick( false, true, false, false, false ) );
	}

	[TestMethod]
	public void HeldOrCapturedClicksDoNotBuildAfterLeavingRideView()
	{
		Assert.AreEqual( Level.WorldClickAction.None, Level.WorldClick( false, true, true, false, true ), "button held when leaving the ride" );
		Assert.AreEqual( Level.WorldClickAction.None, Level.WorldClick( false, true, false, true, true ), "UI still owns the click" );
		Assert.AreEqual( Level.WorldClickAction.None, Level.WorldClick( false, false, true, false, true ), "release cannot place" );
	}

	[TestMethod]
	public void RideAndDoorShortcutsWaitForModalTextAndKeyboardCapture()
	{
		Assert.IsTrue( ParkHud.ParkShortcutsAllowed( false, false, false, false ) );
		Assert.IsFalse( ParkHud.ParkShortcutsAllowed( true, false, false, false ), "pause menu owns shortcuts" );
		Assert.IsFalse( ParkHud.ParkShortcutsAllowed( false, true, false, false ), "a HUD menu owns shortcuts" );
		Assert.IsFalse( ParkHud.ParkShortcutsAllowed( false, false, true, false ), "typing E/O/C cannot act on the park" );
		Assert.IsFalse( ParkHud.ParkShortcutsAllowed( false, false, false, true ), "ImGui keyboard/text capture owns shortcuts" );
	}
}
