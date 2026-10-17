using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A placed object the economy catalogue knows always has its economy record, so the park-resume save validates ("World save is inconsistent: placed attraction N is missing its economy binding").
/// The build flow buys the object, registers it in the park, runs the placement handlers and only then binds it; a handler (or the effect or cue) that throws used to leave an object that stood in the
/// park, paid for, without its binding, and the next save refused the whole park.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PlacedObjectEconomyBindingTests
{
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;
	private Level level = null!;
	private ParkEconomy economy = null!;
	private OriginalObject one = null!;
	private Dictionary<string, string> assets = null!;

	[TestInitialize]
	public void Initialize()
	{
		renderer = new AutomaticCheckpointCpuRendererScope();
		fixture = new UpgradeCatalogueDefaultsTests();
		fixture.Setup();
		var type = typeof( UpgradeCatalogueDefaultsTests );
		var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture, new object[] { "", 0, false } )!;
		assets = (Dictionary<string, string>)type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;
		typeof( Level ).GetMethod( "SetupStaff", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
		typeof( Level ).GetMethod( "SetupParkGate", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
		(level, economy, one) = (world.Level, world.Economy, world.One);
		typeof( Level ).GetMethod( "SetupPaths", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( level, null );
		level.ConfigureObjectHooks();
	}

	[TestCleanup]
	public void Cleanup()
	{
		fixture?.Cleanup();
		renderer?.Dispose();
	}

	private ParkResumeSnapshot Capture() => level.CaptureResumeSnapshot( null, "binding", assets, new DateTimeOffset( 2026, 10, 18, 12, 0, 0, TimeSpan.Zero ) );

	private void Unbind( OriginalObject item ) =>
		((Dictionary<OriginalObject, int>)typeof( Level ).GetField( "economyInstances", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( level )!).Remove( item );

	private static bool Bound( ParkResumeSnapshot snapshot, OriginalObject item ) => snapshot.EconomyBindings.Any( binding => binding.AttractionId == item.Visitors.AttractionId );

	[TestMethod]
	public void ABuiltObjectWhoseHandlerThrowsStillGetsTheRecordItWasBoughtWithAndTheParkSaves()
	{
		var before = economy.Objects.Count;
		Action<OriginalObject> failing = _ => throw new InvalidOperationException( "handler failed" );
		level.Objects.ObjectPlaced += failing;
		try
		{
			Assert.ThrowsException<InvalidOperationException>( () => level.PlaceObject( one.Entry, 20, 20, 0 ) );
		}
		finally
		{
			level.Objects.ObjectPlaced -= failing;
		}
		var stranded = level.Objects.Objects.Single( item => !ReferenceEquals( item, one ) && item.Placement.X is >= 18 and <= 22 );
		Assert.AreEqual( before + 1, economy.Objects.Count, "the purchase made one record" );
		Assert.IsNotNull( level.GetEconomyInstance( stranded ), "the object stands in the park, so it holds the record its purchase made" );
		Assert.IsTrue( Bound( Capture(), stranded ), "and the save holds the binding" );
	}

	[TestMethod]
	public void AParkThatLostABindingAnywayAdoptsTheUnownedRecordWhenItIsSavedAndLogsIt()
	{
		var placed = level.PlaceObject( one.Entry, 20, 20, 0 )!;
		var instance = level.GetEconomyInstance( placed )!.Value;
		var records = economy.Objects.Count;
		Unbind( placed );
		Assert.IsNull( level.GetEconomyInstance( placed ) );
		var snapshot = Capture();
		Assert.AreEqual( instance, level.GetEconomyInstance( placed ), "the object takes back the record of its purchase" );
		Assert.AreEqual( records, economy.Objects.Count, "no second record is made" );
		Assert.IsTrue( Bound( snapshot, placed ) );
	}

	[TestMethod]
	public void AnObjectWithNoRecordAtAllGetsAnUnchargedOneWhenTheParkIsSaved()
	{
		var placed = level.PlaceObject( one.Entry, 20, 20, 0 )!;
		var instance = level.GetEconomyInstance( placed )!.Value;
		Unbind( placed );
		Assert.IsTrue( economy.Sell( instance ) >= 0 );
		var balance = economy.Balance;
		var snapshot = Capture();
		Assert.IsNotNull( level.GetEconomyInstance( placed ) );
		Assert.AreEqual( balance, economy.Balance, "registering the record charges nothing" );
		Assert.IsTrue( Bound( snapshot, placed ) );
	}
}
