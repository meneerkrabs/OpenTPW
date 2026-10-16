using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// ADVISOR-009: the help the HUD's panels and tools post through tp.exe's 0x486B00 and withdraw through 0x486B40 or the interface mode change 0x52F200. A real
/// HUD over the object-window fixture, with the advisor replaced by a recorder of the host's help and mode callbacks. GPU-free.
/// </summary>
[TestClass, DoNotParallelize]
public class AdvisorToolHelpTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private RideUpgradeIngressTests? fixture;
	private UiContext? context;

	[TestInitialize]
	public void Init() => Log ??= new();

	[TestCleanup]
	public void Cleanup() => fixture?.Cleanup();

	/// <summary>A HUD whose host records every help post, withdrawal and mode change in <paramref name="events"/> ("post N", "withdraw N", "mode").</summary>
	private ParkHud NewHud( out List<string> events, out Level level, BuildCategory category = BuildCategory.Rides )
	{
		fixture = new();
		fixture.Setup();
		object?[] args = { category, null, null, null, null, null, ParkGameMode.FullSimulation, "" };
		var hud = (ParkHud)typeof( RideUpgradeIngressTests ).GetMethod( "Fixture", Private )!.Invoke( fixture, args )!;
		context = (UiContext)typeof( RideUpgradeIngressTests ).GetMethod( "Context", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, null )!;
		var recorded = new List<string>();
		events = recorded;
		typeof( ParkHud ).GetField( "host", Private )!.SetValue( hud, new HudHost
		{
			HelpPosted = message => recorded.Add( $"post {message}" ),
			HelpWithdrawn = message => recorded.Add( $"withdraw {message}" ),
			ModeChanged = () => recorded.Add( "mode" )
		} );
		level = (Level)typeof( ParkHud ).GetField( "level", Private )!.GetValue( hud )!;
		return hud;
	}

	private void Frame( ParkHud hud, float delta = 0.016f )
	{
		context!.Delta = delta;
		hud.Update( context, UiInput.Idle( new System.Numerics.Vector2( -1 ) ), false, false, false );
	}

	[TestMethod]
	public void AnOpenedMessagePostsHelp284AndClosingItWithdrawsIt()
	{
		var hud = NewHud( out var events, out _ );
		hud.PostGameMessage( "A message" );
		Frame( hud );
		Assert.AreEqual( 0, events.Count, "a pending message in the tube posts nothing" );
		Assert.IsTrue( hud.OpenPendingMessage( 0 ) );
		CollectionAssert.AreEqual( new[] { "post 284" }, events, "0x48D9C0 posts 0x11C when the item is selected" );
		hud.CloseMessagePanel();
		CollectionAssert.AreEqual( new[] { "post 284", "withdraw 284" }, events, "0x48E4BF withdraws it when the panel closes" );
		hud.CloseMessagePanel();
		Assert.AreEqual( 2, events.Count, "a panel that is already closed withdraws nothing" );
	}

	[TestMethod]
	public void TheMapWindowCarriesHelp304()
	{
		Assert.AreEqual( 304, AdvisorUiHelp.MessageFor( ParkHud.MapScreenName ) );
		CollectionAssert.AreEqual( new[] { 304 }, AdvisorUiHelp.WithdrawnWith( ParkHud.MapScreenName ).ToArray() );
		Assert.IsTrue( AdvisorUiHelp.AllWindowMessages.Contains( 304 ), "a park that ends withdraws it" );
	}
}
