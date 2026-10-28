using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The camera panel in the HUD arm: tp.exe's drawer slide (0x4a14f0), its child clipping (0x4a1440) and the panel's help 0x131 (0x498bb0/0x498bd0).</summary>
[TestClass, DoNotParallelize]
public class CameraPanelSlideTests
{
	private RideUpgradeIngressTests? fixture;
	[TestInitialize]
	public void Init() => Log ??= new();
	[TestCleanup]
	public void Cleanup() => fixture?.Cleanup();

	[TestMethod]
	public void TheSlideTakesTenStepsOfTwentyMilliseconds()
	{
		var slide = new ArmSlide();
		slide.Open();
		Assert.IsFalse( slide.Update( 0.019f ) );
		Assert.AreEqual( 0, slide.Step );
		slide.Update( 0.001f );
		Assert.AreEqual( 1, slide.Step );
		slide.Update( 0.1f );
		Assert.AreEqual( 6, slide.Step );
		slide.Update( 1f );
		Assert.AreEqual( ArmSlide.Steps, slide.Step, "out after 200 ms" );
		slide.Close();
		Assert.IsFalse( slide.Update( 0.18f ) );
		Assert.AreEqual( 1, slide.Step );
		Assert.IsFalse( slide.Update( 0.02f ), "the tenth move is not the end" );
		Assert.AreEqual( 0, slide.Step );
		Assert.IsTrue( slide.Update( 0.02f ), "the eleventh tick, with the count at zero, ends the slide in once (cat_ui 0xC2, 0x4a18d8)" );
		Assert.IsFalse( slide.Update( 0.02f ) );
	}

	[TestMethod]
	public void TheCameraPanelStartsLeftOfTheArmAndEndsAtTheDrawersRuntimePlace()
	{
		// arm left 331, panel width 319: the panel starts at 331 - 319 - 50 = -38, 504 left of its table place 466
		Assert.AreEqual( (-504, -110), ParkHud.CameraPanelShift( 0 ), "vertical: 1069 + 213 - 76 = 1206 against the table's 1316" );
		// the step is (439 - 331 + 319 + 100) / 10 = 52 (527 / 10 truncated), added ten times: the end is 482, 16 right of the table place
		Assert.AreEqual( (-244, -110), ParkHud.CameraPanelShift( 5 ) );
		Assert.AreEqual( (16, -110), ParkHud.CameraPanelShift( ArmSlide.Steps ) );
		// a child shows only once its right edge passes the arm's left edge
		Assert.IsFalse( ParkHud.ArmChildShown( new UiRect( 200, 1316, 131, 153 ) ) );
		Assert.IsTrue( ParkHud.ArmChildShown( new UiRect( 200, 1316, 132, 153 ) ) );
	}

	[TestMethod]
	public void TheDrawersArithmeticFollowsTheRuntimeRectangles()
	{
		Assert.AreEqual( 361, ArmDrawer.ArmRestRight, "the arm is shortened by (its left - its handle's left) at 0x4a23e2: 1006 + (331 - 976)" );
		Assert.AreEqual( 52, ArmDrawer.TickStep( 319 ) );
		Assert.AreEqual( 482, ArmDrawer.EndLeft( 319 ), "-38 + 10 * 52, nine units short of the full travel 527" );
		Assert.AreEqual( 82, ArmDrawer.TickStep( 615 ) );
		Assert.AreEqual( 486, ArmDrawer.EndLeft( 615 ) );
		Assert.AreEqual( 1112, ArmDrawer.Top( 341 ), "the message panel: 1069 + 426 / 2 - 341 / 2 (halves rounded toward zero)" );
		Assert.AreEqual( 1206, ArmDrawer.Top( 153 ) );
		Assert.AreEqual( (28, 11), ParkHud.MessagePanelShift( ArmSlide.Steps ), "the message panel ends 28 right and 11 below its table place (458,1101)" );
		Assert.AreEqual( 361 + 10 * 52, ArmDrawer.ArmRight( 319, ArmSlide.Steps ) );
		// a slide in steps by (the arm's left - the handle's left) / 10 with the handle moved along: it undoes the slide out exactly
		Assert.AreEqual( -ArmDrawer.TickStep( 319 ), (ArmDrawer.ArmLeft - (ArmDrawer.ArmLeft + 10 * ArmDrawer.TickStep( 319 ))) / ArmDrawer.Ticks );
	}

	[TestMethod]
	public void TheCameraButtonPostsAndWithdrawsItsHelpAndTheButtonsSlideIn()
	{
		fixture = new(); fixture.Setup();
		const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
		object?[] args = { BuildCategory.Shops, null, null, null, null, null, ParkGameMode.FullSimulation, "" };
		var hud = (ParkHud)typeof( RideUpgradeIngressTests ).GetMethod( "Fixture", Private )!.Invoke( fixture, args )!;
		var context = (UiContext)typeof( RideUpgradeIngressTests ).GetMethod( "Context", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, null )!;
		var events = new List<string>();
		typeof( ParkHud ).GetField( "host", Private )!.SetValue( hud, new HudHost
		{
			HelpPosted = message => events.Add( $"post {message}" ),
			HelpWithdrawn = message => events.Add( $"withdraw {message}" )
		} );
		void Frame( float delta )
		{
			context.Delta = delta;
			hud.Update( context, UiInput.Idle( new System.Numerics.Vector2( -1 ) ), false, false, false );
		}
		var elements = hud.Screen.Elements.ToList();
		Assert.IsTrue( elements.IndexOf( hud.Screen.Find( "cameraCamcorder" )! ) < elements.IndexOf( hud.Screen.Find( "mainPanel" )! )
			&& elements.IndexOf( hud.Screen.Find( "cameraPanel" )! ) < elements.IndexOf( hud.Screen.Find( "camera" )! ),
			"the panel lies under the HUD, whose buttons win the hit test while it slides" );
		hud.ToggleCameraPanel();
		Frame( 0 );
		CollectionAssert.AreEqual( new[] { "post 305" }, events );
		Assert.IsFalse( hud.Screen.Find( "cameraCamcorder" )!.Visible, "still behind the HUD" );
		Frame( 0.2f );
		var camcorder = hud.Screen.Find( "cameraCamcorder" )!;
		Assert.IsTrue( camcorder.Visible );
		var table = OriginalLayoutTables.CameraPanel.Model( "b_1person" ).Rect;
		Assert.AreEqual( table with { X = table.X + 16, Y = table.Y - 110 }, camcorder.Bounds, "out at the drawer's end place" );
		hud.ToggleCameraPanel();
		CollectionAssert.AreEqual( new[] { "post 305", "withdraw 305" }, events );
		Frame( 0.2f );
		Assert.IsFalse( hud.Screen.Find( "cameraCamcorder" )!.Visible );
	}
}
