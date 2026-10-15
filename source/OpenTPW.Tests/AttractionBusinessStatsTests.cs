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
	// one frame past the list's 2 s value refresh (tp.exe timer 0x80083)
	private static void Frame( World w )
	{
		var delta = w.Context.Delta; w.Context.Delta = ParkHud.AttractionValueRefreshSeconds;
		try { w.Hud.Update( w.Context, UiInput.Idle( new System.Numerics.Vector2( -1 ) ), false, false, false ); }
		finally { w.Context.Delta = delta; }
	}
	private static void Show( World w )
	{
		w.Hud.OpenAttractionsWindow(); w.Hud.Stack.Top!.Focus( w.Hud.Stack.Top.Find( $"category{w.Category}" )! );
		w.Hud.Update( w.Context, UiInput.Key( UiKeys.Accept ), false, false, false );
	}
	/// <summary>The selected row's business cells in order: customers, profit last month, total profit; the not-simulated note where a cell has no value.</summary>
	private static string Value( World w, int index )
	{
		var row = w.Hud.AttractionItems.FirstOrDefault( item => item.AttractionId == w.Hud.SelectedAttractionId );
		if ( row.AttractionId == 0 ) return w.Context.Strings.Extra( OpenTpwText.NotSimulated );
		var category = row.Category;
		var columns = ParkAttractionRoster.Columns( category ).Select( ( column, at ) => (column.Kind, at) ).ToArray();
		var kind = new[] { AttractionColumnKind.Customers, AttractionColumnKind.ProfitLastMonth, AttractionColumnKind.TotalProfit }[index];
		var at = columns.Single( column => column.Kind == kind || kind == AttractionColumnKind.Customers && column.Kind == AttractionColumnKind.Users ).at;
		return w.Hud.AttractionCellsOf( row )[at].Text ?? w.Context.Strings.Extra( OpenTpwText.NotSimulated );
	}
	private static string Money( World w, long value ) => w.Context.Strings[value < 0 ? UIStrings.NegativeDollar : UIStrings.Dollar] + Math.Abs( value ).ToString( "#,0", System.Globalization.CultureInfo.InvariantCulture );
	private static void Check( World w, long customers, long monthProfit, long totalProfit )
	{
		Assert.AreEqual( customers.ToString( System.Globalization.CultureInfo.InvariantCulture ), Value( w, 0 ) );
		Assert.AreEqual( Money( w, monthProfit ), Value( w, 1 ) ); Assert.AreEqual( Money( w, totalProfit ), Value( w, 2 ) );
		var row = w.Hud.AttractionItems.First( item => item.AttractionId == w.Hud.SelectedAttractionId );
		Assert.IsNull( w.Hud.AttractionCellsOf( row ).Last().Gauge, "customer satisfaction is not simulated" );
		var label = w.Category == BuildCategory.Shops ? UIStrings.CustomersLastMonthShop : UIStrings.CustomersLastMonthGame;
		Assert.AreEqual( label, ParkAttractionRoster.Columns( w.Category )[1].Header );
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
	[TestMethod]
	public void MixedVisibleCategoriesKeepOnlyTheirOwnFrameCounters()
	{
		var w = Create( BuildCategory.Shops ); Pay( w, 2 ); w.Economy.AdvanceMonths( 1 );
		var side = (OriginalObject)typeof( RideUpgradeIngressTests ).GetMethod( "Object", Private )!.Invoke( fixture, new object[] { 1303, BuildCategory.Sideshows, w.Level.Objects.ScriptWorld, "" } )!;
		((List<OriginalObject>)w.Level.Objects.Objects).Add( side ); var state = w.Economy.RegisterExisting( 1303 );
		typeof( Level ).GetMethod( "LinkEconomy", Private )!.Invoke( w.Level, new object[] { side, state.Id } );
		state.CustomersLastMonth = 5; state.TakingsLastMonth = 60; state.CostsLastMonth = 9; state.TotalProfit = 51;
		Show( w ); Check( w, 2, 20, 20 );
		Assert.IsFalse( w.Hud.AttractionItems.Any( row => row.AttractionId == side.Runtime.AttractionId ), "the shops tab lists no sideshow" );
		w.Hud.Stack.Top!.Find( "categorySideshows" )!.Activate(); Check( w with { Category = BuildCategory.Sideshows, Owner = side }, 5, 51, 51 );
		Assert.IsFalse( w.Hud.AttractionItems.Any( row => row.AttractionId == w.Owner.Runtime.AttractionId ), "the sideshows tab lists no shop" );
		w.Hud.Stack.Top.Find( "categoryFeatures" )!.Activate(); Assert.AreEqual( w.Context.Strings.Extra( OpenTpwText.NotSimulated ), Value( w, 0 ) );
		w.Hud.Stack.Top.Find( "categoryShops" )!.Activate(); Check( w, 2, 20, 20 );
	}
	[TestMethod]
	public void SameInfoRemappingAndRecordReplacementRefreshOnlyAtTheNextFrame()
	{
		var w = Create( BuildCategory.Shops ); Pay( w, 2 ); w.Economy.AdvanceMonths( 1 ); Show( w ); Check( w, 2, 20, 20 );
		var other = State( w, w.Other ); other.CustomersLastMonth = 9; other.TakingsLastMonth = 7; other.CostsLastMonth = 20; other.TotalProfit = -13;
		var mapping = (Dictionary<OriginalObject, int>)typeof( Level ).GetField( "economyInstances", Private )!.GetValue( w.Level )!;
		mapping[w.Owner] = other.Id; Check( w, 2, 20, 20 ); Frame( w ); Check( w, 9, -13, -13 );
		var states = (SortedDictionary<int, ParkObjectState>)typeof( ParkEconomy ).GetField( "objects", Private )!.GetValue( w.Economy )!;
		states[other.Id] = new ParkObjectState { Id = other.Id, InfoId = other.InfoId, Kind = ParkObjectKind.Shop, CustomersLastMonth = 4, TakingsLastMonth = 20, CostsLastMonth = 3, TotalProfit = 17 };
		Check( w, 9, -13, -13 ); Frame( w ); Check( w, 4, 17, 17 );
	}
	[TestMethod]
	public void ForeignStatusEconomyCannotSupplyMatchingIdsAndFrameRefreshRestoresTrustedValues()
	{
		var w = Create( BuildCategory.Shops ); Pay( w, 2 ); w.Economy.AdvanceMonths( 1 ); Show( w );
		var foreign = EconomyTestData.Park(); var foreignState = foreign.RegisterExisting( 1203 ); foreignState.CustomersLastMonth = 999; foreignState.TotalProfit = 999;
		Set( w.Hud, "<Status>k__BackingField", new EconomyParkStatus( () => foreign ) ); Frame( w );
		for ( var i = 0; i < 3; i++ ) Assert.AreEqual( w.Context.Strings.Extra( OpenTpwText.NotSimulated ), Value( w, i ) );
		Set( w.Hud, "<Status>k__BackingField", new EconomyParkStatus( () => w.Economy ) ); Frame( w ); Check( w, 2, 20, 20 );
	}
	[TestMethod]
	public void AnObjectWindowOpensItsCategoryWithItsObjectAndOtherWindowsReopenTheLastCategory()
	{
		// tp.exe 0x4b0d06: the shop window opens category 1 with its object selected; the other windows pass -1, the category shown last (0x7ca154)
		var w = Create( BuildCategory.Shops );
		w.Hud.OpenAttractionsWindow( BuildCategory.Shops, w.Other );
		Assert.AreEqual( (BuildCategory.Shops, w.Other.Runtime.AttractionId), (w.Hud.AttractionCategory, w.Hud.SelectedAttractionId) );
		w.Hud.CloseAttractionsWindow(); w.Hud.OpenAttractionsWindow();
		Assert.AreEqual( BuildCategory.Shops, w.Hud.AttractionCategory );
		w.Hud.Stack.Top!.Find( "categoryFeatures" )!.Activate(); w.Hud.CloseAttractionsWindow(); w.Hud.OpenAttractionsWindow();
		Assert.AreEqual( BuildCategory.Features, w.Hud.AttractionCategory );
		Assert.AreEqual( "window4", ((UiModelImage)w.Hud.Stack.Top!.Find( "window" )!).Model );
		Assert.AreEqual( "b_okay", ((UiButton)w.Hud.Stack.Top.Find( "exit" )!).Model );
	}

	[TestMethod]
	public void TheValuesRefreshEveryTwoSeconds()
	{
		// tp.exe 0x4955e0: timer 0x80083 refreshes the rows every 2000 ms
		var w = Create( BuildCategory.Shops ); Pay( w, 2 ); w.Economy.AdvanceMonths( 1 ); Show( w ); Check( w, 2, 20, 20 );
		State( w, w.Owner ).CustomersLastMonth = 9;
		w.Context.Delta = ParkHud.AttractionValueRefreshSeconds / 4;
		for ( var frame = 0; frame < 3; frame++ ) w.Hud.Update( w.Context, UiInput.Idle( new System.Numerics.Vector2( -1 ) ), false, false, false );
		Assert.AreEqual( "2", Value( w, 0 ), "1.5 s after the last refresh the row keeps its value" );
		w.Hud.Update( w.Context, UiInput.Idle( new System.Numerics.Vector2( -1 ) ), false, false, false );
		Assert.AreEqual( "9", Value( w, 0 ), "at 2 s the row takes the new value" );
	}

	[TestMethod]
	public void SharedMoneyFormatterKeepsExistingIntegralOutputsAcrossCulturesAndDoublePrecisionBoundary()
	{
		var w = Create( BuildCategory.Shops ); var format = typeof( ParkHud ).GetMethod( "FormatMoney", Private )!;
		var oldCulture = System.Globalization.CultureInfo.CurrentCulture;
		try
		{
			foreach ( var culture in new[] { "en-US", "fr-FR" } )
			{
				System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo( culture );
				foreach ( var amount in new[] { 0L, 1L, -1L, 999L, -999L, 1000L, -1000L, 987654321L, -987654321L, 9007199254740993L, -9007199254740993L, long.MaxValue, -long.MaxValue } )
					Assert.AreEqual( Money( w, amount ), (string)format.Invoke( w.Hud, new object[] { amount } )! );
				Assert.AreEqual( w.Context.Strings[UIStrings.NegativeDollar] + "9,223,372,036,854,775,808", (string)format.Invoke( w.Hud, new object[] { long.MinValue } )! );
			}
		}
		finally { System.Globalization.CultureInfo.CurrentCulture = oldCulture; }
	}

}
