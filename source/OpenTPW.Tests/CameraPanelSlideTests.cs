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
		Assert.IsTrue( slide.Update( 0.02f ), "the finished slide in is reported once (cat_ui 0xC2)" );
		Assert.IsFalse( slide.Update( 0.02f ) );
	}

	[TestMethod]
	public void ThePanelStartsLeftOfTheArmAndEndsAtItsTablePlace()
	{
		// arm left 331, panel width 319: the panel starts at 331 - 319 - 50 = -38, 504 left of its table place 466
		Assert.AreEqual( -504f, ParkHud.CameraPanelOffset( 0 ), 1e-3 );
		Assert.AreEqual( -252f, ParkHud.CameraPanelOffset( 5 ), 1e-3 );
		Assert.AreEqual( 0f, ParkHud.CameraPanelOffset( ArmSlide.Steps ) );
		// a child shows only once its right edge passes the arm's left edge
		Assert.IsFalse( ParkHud.ArmChildShown( new UiRect( 200, 1316, 131, 153 ) ) );
		Assert.IsTrue( ParkHud.ArmChildShown( new UiRect( 200, 1316, 132, 153 ) ) );
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
		Assert.AreEqual( OriginalLayoutTables.CameraPanel.Model( "b_1person" ).Rect, camcorder.Bounds, "out at the table place" );
		hud.ToggleCameraPanel();
		CollectionAssert.AreEqual( new[] { "post 305", "withdraw 305" }, events );
		Frame( 0.2f );
		Assert.IsFalse( hud.Screen.Find( "cameraCamcorder" )!.Visible );
	}
}
