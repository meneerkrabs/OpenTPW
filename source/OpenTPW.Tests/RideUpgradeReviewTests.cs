using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class RideUpgradeReviewTests
{
	private RideUpgradeIngressTests fixture = null!;
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	[TestInitialize]
	public void Setup() { fixture = new(); fixture.Setup(); }
	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();
	private static object? CallStatic( string name, params object[] args ) => typeof( RideUpgradeIngressTests ).GetMethod( name, BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, args );
	private (ParkHud Hud, Level Level, ParkEconomy Economy, OriginalObject Owner, OriginalObject Other) World()
	{
		object?[] args = { BuildCategory.Rides, null, null, null, null, null, ParkGameMode.FullSimulation, "" };
		var hud = (ParkHud)typeof( RideUpgradeIngressTests ).GetMethod( "Fixture", Private )!.Invoke( fixture, args )!;
		return (hud, (Level)args[1]!, (ParkEconomy)args[2]!, (OriginalObject)args[3]!, (OriginalObject)args[4]!);
	}
	private static UiContext Context() => (UiContext)CallStatic( "Context" )!;
	private static void Open( ParkHud hud, UiContext context ) => CallStatic( "Open", hud, context );

	[TestMethod]
	public void PreviewCostIsExactAndChangedCostRequiresFreshConfirmation()
	{
		var w = World(); var context = Context(); CallStatic( "Research", w.Economy ); var cash = w.Economy.Balance;
		Open( w.Hud, context ); var screen = w.Hud.Stack.Top!;
		Assert.AreEqual( $"{context.Strings[UIStrings.UpgradeToLevel]} 1\n{context.Strings[UIStrings.EstimatedPrice]} {context.Strings[UIStrings.Dollar]}400", ((UiLabel)screen.Find( "preview" )!).Text() );
		Assert.IsTrue( w.Economy.Catalog.TryGet( 1100, out var info ) );
		var tiers = (UpgradeLevelInfo[])info.Upgrades; tiers[1] = tiers[1] with { CostOfUpgrade = 450 };
		w.Hud.Update( context, UiInput.Key( UiKeys.Accept ), false, false, false );
		Assert.AreSame( screen, w.Hud.Stack.Top ); Assert.AreEqual( cash, w.Economy.Balance );
		Assert.IsTrue( ((UiLabel)screen.Find( "error" )!).Text().Length > 0 );
		screen.Back!(); Open( w.Hud, context );
		Assert.AreEqual( $"{context.Strings[UIStrings.UpgradeToLevel]} 1\n{context.Strings[UIStrings.EstimatedPrice]} {context.Strings[UIStrings.Dollar]}450", ((UiLabel)w.Hud.Stack.Top!.Find( "preview" )!).Text() );
		w.Hud.Update( context, UiInput.Key( UiKeys.Accept ), false, false, false ); Assert.AreEqual( cash - 450, w.Economy.Balance );
	}

	[TestMethod]
	public void EconomyReplacementWhileConfirmingCannotChargeEitherPark()
	{
		var w = World(); var context = Context(); CallStatic( "Research", w.Economy ); Open( w.Hud, context );
		var replacement = EconomyTestData.Park(); var oldCash = w.Economy.Balance; var newCash = replacement.Balance;
		typeof( ParkHud ).GetField( "<Status>k__BackingField", Private )!.SetValue( w.Hud, new EconomyParkStatus( () => replacement ) );
		w.Hud.Update( context, UiInput.Key( UiKeys.Accept ), false, false, false );
		Assert.AreEqual( oldCash, w.Economy.Balance ); Assert.AreEqual( newCash, replacement.Balance );
		Assert.AreEqual( "rideUpgrade", w.Hud.Stack.Top!.Name );
	}

	[TestMethod]
	public void ActualHudCompletionPreservesAuthoredValuesWaitAndOccupiedHeads()
	{
		var root = (string)typeof( RideUpgradeIngressTests ).GetField( "root", Private )!.GetValue( fixture )!;
		File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm()
			.I( Opcode.COPY, RideVMTests.V( (int)RideVariables.VAR_CAPACITY ), 9 )
			.I( Opcode.COPY, RideVMTests.V( (int)RideVariables.VAR_DURATION ), 77 ).I( Opcode.WAIT, 5000 ).I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
		var w = World(); var context = Context(); w.Owner.Runtime.Simulate( .001 );
		var heads = RideHeadSpace.Of( new[] { 0, 0 }, 1 ); heads.Add( 42 ); w.Owner.Visitors.Heads = heads;
		w.Owner.Visitors.RecomputeQueue( w.Level.Guests!.Grid );
		w.Owner.Visitors.RestoreSnapshot( w.Owner.Visitors.CaptureSnapshot() with { OnRide = new[] { 42 }, BoardedTotal = 1 } );
		var before = w.Owner.Script!.CaptureSnapshot(); var bridge = w.Owner.Visitors;
		CallStatic( "Research", w.Economy ); Open( w.Hud, context ); w.Hud.Update( context, UiInput.Key( UiKeys.Accept ), false, false, false );
		EconomyTestData.HireBest( w.Economy, StaffType.Mechanic );
		Assert.IsTrue( w.Economy.TryGetObject( w.Level.GetEconomyInstance( w.Owner )!.Value, out var state ) );
		for ( var i = 0; i < 20 && state.Level < 1; i++ ) w.Economy.AdvanceDays( 1 );
		Assert.AreEqual( 1, state.Level ); Assert.AreEqual( 3, bridge.Capacity ); Assert.AreEqual( 1, w.Other.Visitors.Capacity );
		Assert.AreEqual( 3, w.Owner.Runtime.GetVariable( RideVariables.VAR_CAPACITY ), "QUEUE-011: the completion writes InitCapacity" ); Assert.AreEqual( 0, w.Owner.Runtime.GetVariable( RideVariables.VAR_DURATION ), "InitDuration 20 clamped to the absent MaxDuration 0 (0x4df8f0)" );
		Assert.AreSame( bridge, w.Owner.Visitors ); Assert.AreSame( heads, bridge.Heads ); CollectionAssert.AreEqual( new[] { 42 }, bridge.Riders.ToArray() );
		var after = w.Owner.Script.CaptureSnapshot(); Assert.AreEqual( before.Flags, after.Flags ); Assert.AreEqual( before.State, after.State ); Assert.AreEqual( before.ProgramCounter, after.ProgramCounter ); Assert.AreEqual( before.WakeTimeMilliseconds, after.WakeTimeMilliseconds );
	}
}
