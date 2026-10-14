using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class AttractionBusinessStatsTests
{
	private RideUpgradeIngressTests fixture = null!;
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private sealed record World( ParkHud Hud, Level Level, ParkEconomy Economy, OriginalObject Owner, OriginalObject Other, GuestEconomyBridge Payments, UiContext Context, BuildCategory Category );
	[TestInitialize]
	public void Setup() { fixture = new(); fixture.Setup(); }
	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();
	private static void Set( object target, string name, object? value ) => target.GetType().GetField( name, Private )!.SetValue( target, value );
	private World Create( BuildCategory category )
	{
		object?[] args = { category, null, null, null, null, null, ParkGameMode.FullSimulation, "" };
		var hud = (ParkHud)typeof( RideUpgradeIngressTests ).GetMethod( "Fixture", Private )!.Invoke( fixture, args )!;
		var context = (UiContext)typeof( RideUpgradeIngressTests ).GetMethod( "Context", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, null )!;
		return new( hud, (Level)args[1]!, (ParkEconomy)args[2]!, (OriginalObject)args[3]!, (OriginalObject)args[4]!, (GuestEconomyBridge)args[5]!, context, category );
	}
	private static ParkObjectState State( World w, OriginalObject owner ) { Assert.IsTrue( w.Economy.TryGetObject( w.Level.GetEconomyInstance( owner )!.Value, out var state ) ); return state; }
	private static void Frame( World w ) => w.Hud.Update( w.Context, UiInput.Idle( new System.Numerics.Vector2( -1 ) ), false, false, false );
	private static void Show( World w )
	{
		w.Hud.OpenAttractionsWindow(); w.Hud.Stack.Top!.Focus( w.Hud.Stack.Top.Find( $"category{w.Category}" )! );
		w.Hud.Update( w.Context, UiInput.Key( UiKeys.Accept ), false, false, false );
	}
	private static string Value( World w, int row ) => ((UiLabel)w.Hud.Stack.Top!.Find( $"statValue{row}" )!).Text();
	private static string Money( World w, long value ) => w.Context.Strings[value < 0 ? UIStrings.NegativeDollar : UIStrings.Dollar] + Math.Abs( value ).ToString( "#,0", System.Globalization.CultureInfo.InvariantCulture );
	private static void Check( World w, long customers, long monthProfit, long totalProfit )
	{
		var profitRow = w.Category == BuildCategory.Shops ? 1 : 2;
		Assert.AreEqual( customers.ToString( System.Globalization.CultureInfo.InvariantCulture ), Value( w, 0 ) );
		Assert.AreEqual( Money( w, monthProfit ), Value( w, profitRow ) ); Assert.AreEqual( Money( w, totalProfit ), Value( w, profitRow + 1 ) );
		Assert.AreEqual( w.Context.Strings.Extra( OpenTpwText.NotSimulated ), Value( w, profitRow + 2 ) );
		Assert.AreEqual( "0", Value( w, profitRow + 3 ) ); Assert.AreEqual( "0", Value( w, profitRow + 4 ) );
		var label = w.Category == BuildCategory.Shops ? UIStrings.CustomersLastMonthShop : UIStrings.CustomersLastMonthGame;
		Assert.AreEqual( w.Context.Strings[label], ((UiLabel)w.Hud.Stack.Top!.Find( "statLabel0" )!).Text() );
	}
	private static void Pay( World w, int count )
	{
		for ( var i = 0; i < count; i++ ) Assert.IsTrue( w.Payments.TryPayVisit( w.Owner.Visitors, 100, out _ ) );
	}

	[DataTestMethod, DataRow( BuildCategory.Shops ), DataRow( BuildCategory.Sideshows )]
	public void PaidVisitsMonthlyRolloverAndExactInstanceIdentityReachActualHud( BuildCategory category )
	{
		var w = Create( category );
		// Deliberately separate attraction ids from economy instance ids; same InfoId is not a grouping key.
		foreach ( var owner in new[] { w.Owner, w.Other } )
		{
			var fresh = w.Economy.RegisterExisting( owner.Entry.InfoId );
			typeof( Level ).GetMethod( "LinkEconomy", Private )!.Invoke( w.Level, new object[] { owner, fresh.Id } );
			w.Payments.Link( owner.Runtime.AttractionId, fresh.Id );
		}
		if ( category == BuildCategory.Sideshows ) w.Economy.SetPrices( State( w, w.Owner ).Id, 20, 50, 0 );
		Pay( w, 2 ); w.Economy.AdvanceMonths( 1 ); var state = State( w, w.Owner );
		Assert.AreEqual( 2L, state.CustomersLastMonth ); Assert.AreEqual( category == BuildCategory.Shops ? 20L : -60L, state.ProfitLastMonth ); Assert.AreEqual( 0L, State( w, w.Other ).CustomersLastMonth );
		Show( w ); Assert.AreEqual( w.Owner.Runtime.AttractionId, w.Hud.SelectedAttractionId ); Check( w, 2, state.ProfitLastMonth, state.TotalProfit );
		w.Hud.SelectAttraction( 1 ); Check( w, 0, 0, 0 ); w.Hud.SelectAttraction( 0 ); Check( w, 2, state.ProfitLastMonth, state.TotalProfit );
	}

	[TestMethod]
	public void CurrentMonthAndCopiedFrameRefreshStayDistinctWithoutMutatingSimulation()
	{
		var w = Create( BuildCategory.Shops ); Pay( w, 2 ); Show( w ); Check( w, 0, 0, 20 );
		w.Economy.AdvanceMonths( 1 ); Frame( w ); Check( w, 2, 20, 20 );
		Pay( w, 1 ); Frame( w ); Check( w, 2, 20, 30 );
		var state = State( w, w.Owner ); state.CustomersLastMonth = 7; state.TakingsLastMonth = 900; state.CostsLastMonth = 100; state.TotalProfit = 1000;
		Check( w, 2, 20, 30 ); // Label reads use the existing frame, not live mutable model references.
		var economy = ParkSaveFile.Serialize( w.Economy ); var guests = w.Level.Guests!.ComputeStateHash();
		Frame( w ); Check( w, 7, 800, 1000 ); Assert.AreEqual( economy, ParkSaveFile.Serialize( w.Economy ) ); Assert.AreEqual( guests, w.Level.Guests.ComputeStateHash() );
		w.Economy.AdvanceMonths( 1 ); Frame( w ); Check( w, 1, 10, 1000 );
	}

	[TestMethod]
	public void ExistingSaveRestoreAndFreshReopenUseTheSameRecordedCounters()
	{
		var w = Create( BuildCategory.Shops ); Pay( w, 2 ); w.Economy.AdvanceMonths( 1 ); Show( w ); Check( w, 2, 20, 20 );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Capture( w.Economy ), EconomyTestData.Settings(), EconomyTestData.Catalog() );
		Set( w.Level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( restored, ParkStartKind.FullSimulation ) ); Set( w.Hud, "<Status>k__BackingField", new EconomyParkStatus( () => restored ) );
		Frame( w ); Check( w, 2, 20, 20 ); w.Hud.CloseAttractionsWindow(); Show( w ); Check( w, 2, 20, 20 );
	}

	[DataTestMethod, DataRow( "unmapped" ), DataRow( "wrongKind" ), DataRow( "wrongInfo" ), DataRow( "noEconomy" ), DataRow( "readonly" ), DataRow( "tpi" ), DataRow( "foreignEconomy" )]
	public void UnsupportedOrChangedBindingsClearPreviouslyKnownFrameData( string gate )
	{
		var w = Create( BuildCategory.Shops ); Pay( w, 2 ); w.Economy.AdvanceMonths( 1 ); Show( w ); Check( w, 2, 20, 20 );
		if ( gate == "unmapped" ) ((Dictionary<OriginalObject, int>)typeof( Level ).GetField( "economyInstances", Private )!.GetValue( w.Level )!).Remove( w.Owner );
		if ( gate is "wrongKind" or "wrongInfo" )
		{
			var current = State( w, w.Owner ); var states = (SortedDictionary<int, ParkObjectState>)typeof( ParkEconomy ).GetField( "objects", Private )!.GetValue( w.Economy )!;
			states[current.Id] = new ParkObjectState { Id = current.Id, InfoId = gate == "wrongInfo" ? 1303 : current.InfoId, Kind = gate == "wrongKind" ? ParkObjectKind.Ride : current.Kind };
		}
		if ( gate == "noEconomy" ) Set( w.Hud, "<Status>k__BackingField", new NoEconomyStatus() );
		if ( gate == "readonly" ) Set( w.Level, "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		if ( gate == "tpi" ) GameEditions.Select( GameEdition.ThemeParkInc );
		if ( gate == "foreignEconomy" ) Set( w.Level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation ) );
		Frame( w ); for ( var i = 0; i < 3; i++ ) Assert.AreEqual( w.Context.Strings.Extra( OpenTpwText.NotSimulated ), Value( w, i ) );
	}

	[DataTestMethod, DataRow( long.MinValue, "9,223,372,036,854,775,808" ), DataRow( long.MaxValue, "9,223,372,036,854,775,807" ), DataRow( -1L, "1" ), DataRow( 0L, "0" ), DataRow( 1234L, "1,234" )]
	public void AcceptedSignedProfitRangeUsesExistingLocalizedMoneyShape( long profit, string magnitude )
	{
		var w = Create( BuildCategory.Shops ); var state = State( w, w.Owner ); state.TotalProfit = profit;
		var restored = ParkSaveFile.Restore( ParkSaveFile.Capture( w.Economy ), EconomyTestData.Settings(), EconomyTestData.Catalog() );
		Assert.IsTrue( restored.TryGetObject( state.Id, out var saved ) ); Assert.AreEqual( profit, saved.TotalProfit );
		Show( w );
		Assert.AreEqual( w.Context.Strings[profit < 0 ? UIStrings.NegativeDollar : UIStrings.Dollar] + magnitude, Value( w, 2 ) );
	}

	[TestMethod]
	public void RemovalEmptyCategorySortingAndReopenCannotRetainAnotherOwnerStats()
	{
		var w = Create( BuildCategory.Shops ); Pay( w, 2 ); w.Economy.AdvanceMonths( 1 ); Show( w );
		w.Hud.SortAttractions(); Assert.AreEqual( w.Owner.Runtime.AttractionId, w.Hud.SelectedAttractionId ); Check( w, 2, 20, 20 );
		w.Owner.Delete(); Frame( w ); Assert.AreEqual( w.Other.Runtime.AttractionId, w.Hud.SelectedAttractionId ); Check( w, 0, 0, 0 );
		w.Hud.Stack.Top!.Find( "categoryFeatures" )!.Activate(); Assert.AreEqual( 0, w.Hud.SelectedAttractionId ); Assert.AreEqual( w.Context.Strings.Extra( OpenTpwText.NotSimulated ), Value( w, 0 ) );
		w.Hud.Stack.Top.Find( "categoryShops" )!.Activate(); Check( w, 0, 0, 0 );
		w.Hud.CloseAttractionsWindow(); Show( w ); Check( w, 0, 0, 0 );
	}
}
