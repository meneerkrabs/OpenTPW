using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
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
		Frame(); Assert.IsInstanceOfType( menu.Stack.Top, typeof( ProgressChoiceScreen ) ); Assert.AreEqual( "newProgress", menu.Stack.Top!.Name );
		Assert.AreEqual( 1, verified ); Assert.AreEqual( 0, Get<int>( fixture, "built" ) ); Assert.IsFalse( File.Exists( Profile ) );
		for ( var frame = 0; frame < 5; frame++ ) Frame();
		Assert.AreEqual( 1, Get<int>( fixture, "built" ) ); Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( Profile )!.ExtraKeys );
		var before = File.ReadAllBytes( Profile ); for ( var frame = 0; frame < 45; frame++ ) Frame();
		Assert.AreEqual( 1, Get<int>( fixture, "built" ) ); CollectionAssert.AreEqual( before, File.ReadAllBytes( Profile ) );
	}
	[TestMethod]
	public void CancelledConsentCannotBeBypassedBySmokeWait()
	{
		Frame(); menu.Stack.Update( context, UiInput.Key( UiKeys.Back ) );
		Assert.ThrowsException<InvalidOperationException>( () => { for ( var frame = 0; frame < 5; frame++ ) Frame(); } );
		Assert.IsFalse( File.Exists( Profile ) ); Assert.AreEqual( 0, Get<int>( fixture, "built" ) );
	}
	[TestMethod]
	public void KnownZeroKeyHistoryIsNeverResetToMakeSmokePass()
	{
		PlayerTicketFile.Save( Profile, new PlayerTicketLedger().Capture() ); var before = File.ReadAllBytes( Profile );
		Frame(); Assert.ThrowsException<InvalidOperationException>( () => { for ( var frame = 0; frame < 5; frame++ ) Frame(); } );
		CollectionAssert.AreEqual( before, File.ReadAllBytes( Profile ) ); Assert.AreEqual( 0, Get<int>( fixture, "built" ) );
	}
}
