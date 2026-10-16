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

	// ---- The coaster tool ----------------------------------------------------------------------------------

	private static void SetField( object target, string name, object? value ) =>
		target.GetType().GetField( name, Private )!.SetValue( target, value );

	/// <summary>Runs the coaster tool's panel on the first object of the fixture's level (the panel and the tool's station are all the HUD reads here).</summary>
	private static CoasterToolPanel StartCoasterTool( ParkHud hud, Level level, bool flume = false )
	{
		var settings = CoasterTests.PcSettings();
		if ( flume )
			settings["sCoasterType.bSpecialIsTrough"] = "1";
		var ride = new CoasterRide( CoasterDefinition.FromValues( settings ), new CoasterWorld( 1 ), "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, (20, 20) );
		var panel = new CoasterToolPanel( builder );
		SetField( level, "<CoasterToolStation>k__BackingField", level.Objects.Objects.First() );
		SetField( level, "<CoasterPanel>k__BackingField", panel );
		return panel;
	}

	private static void Press( ParkHud hud, string id )
	{
		var button = (UiButton)hud.Screen.Find( id )!;
		Assert.IsTrue( button.Enabled, id );
		button.Clicked!();
	}

	[TestMethod]
	public void TheCoasterPanelsButtonsPostTheirHelpAndTheCloseWithdrawsAllFive()
	{
		var hud = NewHud( out var events, out var level );
		Frame( hud );
		StartCoasterTool( hud, level );
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "mode" }, events, "the tool start is a mode change" );
		events.Clear();
		Press( hud, "coasterBuild" );
		Press( hud, "coasterMove" );
		Press( hud, "coasterLoop" );
		Press( hud, "coasterTilt" );
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "post 293", "post 289", "post 294" }, events, "build 0x125, move 0x121, loop 0x126; the tilt button posts none (0x4982A0)" );
		events.Clear();
		level.ExitCoasterTool();
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "withdraw 293", "withdraw 295", "withdraw 294", "withdraw 289", "withdraw 292", "mode" }, events,
			"the close message withdraws 0x125, 0x127, 0x126, 0x121 and 0x124 in that order" );
	}

	[TestMethod]
	public void TheLoopButtonOfAFlumeCoasterPostsHelp295()
	{
		var hud = NewHud( out var events, out var level );
		Frame( hud );
		StartCoasterTool( hud, level, flume: true );
		Frame( hud );
		events.Clear();
		Press( hud, "coasterLoop" );
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "post 295" }, events, "mask bit 0x200 (definition flag 0x100) posts 0x127 instead of 0x126" );
	}

	[TestMethod]
	public void ADisabledCoasterButtonPostsNothing()
	{
		var hud = NewHud( out var events, out var level );
		Frame( hud );
		var panel = StartCoasterTool( hud, level );
		Frame( hud );
		events.Clear();
		Assert.IsFalse( panel.IsEnabled( CoasterToolButton.Demolish ), "an unfinished circuit has no demolish button" );
		var button = (UiButton)hud.Screen.Find( "coasterDemolish" )!;
		Assert.IsFalse( button.Enabled );
		Frame( hud );
		Assert.AreEqual( 0, events.Count );
	}
}
