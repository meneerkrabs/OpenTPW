using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class FrontEndProgressSmokeTests
{
	private PlayerProgressIngressTests fixture = null!;
	private GameFlow flow = null!;
	private FrontEndMenu menu = null!;
	private UiContext context = null!;
	private FrontEndSmokeTest smoke = null!;
	private int verified;
	private string Profile => Path.Combine( Get<string>( fixture, "root" ), PlayerTicketFile.FileName );
	private static T Get<T>( object target, string field ) => (T)target.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( target )!;
	private static void Set( object target, string field, object value ) => target.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );
	[TestInitialize]
	public void Initialize()
	{
		fixture = new(); fixture.Initialize(); flow = Get<GameFlow>( fixture, "flow" ); menu = Get<FrontEndMenu>( fixture, "menu" ); context = Get<UiContext>( fixture, "context" );
		Set( flow, "<Context>k__BackingField", context );
		smoke = (FrontEndSmokeTest)RuntimeHelpers.GetUninitializedObject( typeof( FrontEndSmokeTest ) );
		Set( smoke, "flow", flow ); Set( smoke, "steps", new Queue<(string Name, Func<bool> Step)>() );
		menu.SelectLevel( "jungle" ); menu.ShowGameMode();
		smoke.PlanFullSimulationStart( () => verified++ );
	}
	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();
	private void Frame()
	{
		smoke.Update();
		if ( flow.InjectedInput is { } input ) { flow.InjectedInput = null; menu.Stack.Update( context, input ); }
		typeof( PlayerProgressIngressTests ).GetMethod( "Pending", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture, null );
	}
	[TestMethod]
	public void ActualSmokePlanAcceptsMissingHistoryBeforeWaitingForPark()
	{
		Frame(); Frame(); Assert.IsInstanceOfType( menu.Stack.Top, typeof( ProgressChoiceScreen ) ); Assert.AreEqual( "newProgress", menu.Stack.Top!.Name );
		Assert.AreEqual( 1, verified ); Assert.AreEqual( 0, Get<int>( fixture, "built" ) ); Assert.IsFalse( File.Exists( Profile ) );
		for ( var frame = 0; frame < 5; frame++ ) Frame();
		Assert.AreEqual( 1, Get<int>( fixture, "built" ) ); Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( Profile )!.ExtraKeys );
		var before = File.ReadAllBytes( Profile ); for ( var frame = 0; frame < 45; frame++ ) Frame();
		Assert.AreEqual( 1, Get<int>( fixture, "built" ) ); CollectionAssert.AreEqual( before, File.ReadAllBytes( Profile ) );
	}
	[TestMethod]
	public void CancelledConsentCannotBeBypassedBySmokeWait()
	{
		Frame(); Frame(); menu.Stack.Update( context, UiInput.Key( UiKeys.Back ) );
		Assert.ThrowsException<InvalidOperationException>( () => { for ( var frame = 0; frame < 5; frame++ ) Frame(); } );
		Assert.IsFalse( File.Exists( Profile ) ); Assert.AreEqual( 0, Get<int>( fixture, "built" ) );
	}
	[TestMethod]
	public void KnownZeroKeyHistoryIsNeverResetToMakeSmokePass()
	{
		PlayerTicketFile.Save( Profile, new PlayerTicketLedger().Capture() ); var before = File.ReadAllBytes( Profile );
		Frame(); Frame(); Assert.ThrowsException<InvalidOperationException>( () => { for ( var frame = 0; frame < 5; frame++ ) Frame(); } );
		CollectionAssert.AreEqual( before, File.ReadAllBytes( Profile ) ); Assert.AreEqual( 0, Get<int>( fixture, "built" ) );
	}
	private void Building( bool value )
	{
		var type = typeof( GameFlow ).GetNestedType( "ParkBuild", BindingFlags.NonPublic )!;
		Set( flow, "parkBuild", value ? RuntimeHelpers.GetUninitializedObject( type ) : null! );
	}
	private void Activated( string theme = "jungle", bool hud = true )
	{
		var original = (OriginalPark)RuntimeHelpers.GetUninitializedObject( typeof( OriginalPark ) ); Set( original, "<LevelName>k__BackingField", theme );
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ); Set( level, "<OriginalPark>k__BackingField", original );
		var parkHud = hud ? (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) ) : null;
		if ( parkHud != null ) Set( parkHud, "<Stack>k__BackingField", new UiScreenStack() );
		Set( flow, "<Level>k__BackingField", level ); Set( flow, "<Hud>k__BackingField", parkHud! );
	}
	private void AfterPark( Action action ) => Get<Queue<(string Name, Func<bool> Step)>>( smoke, "steps" ).Enqueue( ("original post-load assertion", () => { action(); return true; }) );
	[TestMethod]
	public void ActualSmokeControllerWaitsBeyondFortyFramesForActivation()
	{
		for ( var n = 0; n < 6; n++ ) Frame();
		Building( true ); var reached = false;
		AfterPark( () => { Assert.IsFalse( flow.BuildingPark ); Assert.IsNotNull( flow.Level ); Assert.IsNotNull( flow.Hud ); reached = true; } );
		for ( var n = 0; n < 120; n++ ) Frame(); Assert.IsFalse( reached );
		Activated(); Frame(); Assert.IsFalse( reached, "An allocated HUD/level cannot bypass active construction." );
		Building( false ); Frame(); Assert.IsTrue( reached );
	}
	[TestMethod]
	public void WrongThemeCannotCompleteParkReadiness()
	{
		for ( var n = 0; n < 6; n++ ) Frame(); Activated( "fantasy" );
		var reached = false; AfterPark( () => reached = true );
		for ( var n = 0; n < 120; n++ ) Frame(); Assert.IsFalse( reached );
		Activated(); Frame(); Assert.IsTrue( reached );
	}
	[TestMethod]
	public void MissingHudCannotCompleteParkReadiness()
	{
		for ( var n = 0; n < 6; n++ ) Frame(); Activated( hud: false );
		var reached = false; AfterPark( () => reached = true );
		for ( var n = 0; n < 120; n++ ) Frame(); Assert.IsFalse( reached );
		Activated(); Frame(); Assert.IsTrue( reached );
	}
	[TestMethod]
	public void ActualProgressFailureStopsReadinessWithDiagnosis()
	{
		for ( var n = 0; n < 6; n++ ) Frame();
		typeof( GameFlow ).GetMethod( "ShowProgressFailure", BindingFlags.Instance | BindingFlags.NonPublic, null,
			new[] { typeof( string ), typeof( string ) }, null )!.Invoke( flow, new object?[] { "synthetic build failure", null } );
		Assert.AreEqual( "progressFailed", menu.Stack.Top!.Name );
		var error = Assert.ThrowsException<InvalidOperationException>( Frame );
		StringAssert.Contains( error.Message, "progress failure dialog" );
	}
	[TestMethod]
	public void ParkConstructionWaitIsOffTheFrameBudgetButBoundedInTime()
	{
		for ( var n = 0; n < 6; n++ ) Frame(); Building( true );
		// Waiting for construction is not part of the frame budget: the run survives the former limit.
		for ( var n = 0; n < 3200; n++ ) Frame();
		Assert.IsTrue( Get<int>( smoke, "loadFrames" ) > 3000 );
		var previous = FrontEndSmokeTest.LoadAllowance;
		try
		{
			FrontEndSmokeTest.LoadAllowance = TimeSpan.Zero;
			var error = Assert.ThrowsException<InvalidOperationException>( Frame );
			StringAssert.Contains( error.Message, "s of wall-clock time" );
			StringAssert.Contains( error.Message, "jungle park construction" );
		}
		finally { FrontEndSmokeTest.LoadAllowance = previous; }
	}
	[TestMethod]
	public void NoncompletionOfAnotherStepStillUsesStrictFrameBudget()
	{
		for ( var n = 0; n < 6; n++ ) Frame(); Building( true );
		Get<Queue<(string Name, Func<bool> Step)>>( smoke, "steps" ).Clear();
		Get<Queue<(string Name, Func<bool> Step)>>( smoke, "steps" ).Enqueue( ("stuck step", () => false) );
		while ( Get<int>( smoke, "frame" ) < 2999 ) Frame();
		var error = Assert.ThrowsException<InvalidOperationException>( Frame );
		StringAssert.Contains( error.Message, "finish within 3000 frames" );
		StringAssert.Contains( error.Message, "stuck step" );
	}

}
