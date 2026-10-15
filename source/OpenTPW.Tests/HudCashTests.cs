using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Tests;

/// <summary>The HUD cash text (control 47) and the hire window's cash label (control 9366), tp.exe 0x48ff70 / 0x49b650.</summary>
[TestClass]
public class HudCashTests
{
	private static ParkHud HudWith( long money )
	{
		var hud = (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
		typeof( ParkHud ).GetField( "<Status>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( hud, new StubParkStatus( money ) );
		typeof( ParkHud ).GetField( "strings", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( hud, OriginalUiTests.FakeStrings() );
		return hud;
	}

	[TestMethod]
	public void HudCashShowsTheBareMagnitudeWithoutGroupingOrPrefix()
	{
		Assert.AreEqual( "1234567", HudWith( 1234567 ).MoneyText );
		Assert.AreEqual( "0", HudWith( 0 ).MoneyText );
		Assert.AreEqual( "1500", HudWith( -1500 ).MoneyText, "the sign is carried by control 50 (i_dollar), not the text" );
	}

	[TestMethod]
	public void HireWindowCashLabelUsesTheCashTexts()
	{
		Assert.AreEqual( "ui4571234", HudWith( 1234 ).StaffCashText, "UITEXT 0x1ca (entry 457) then the balance" );
		Assert.AreEqual( "ui45899", HudWith( -99 ).StaffCashText, "UITEXT 0x1cb (entry 458) then the magnitude" );
	}

	[TestMethod]
	public void BuyWindowCashLabelIsTheSameCashTextAsTheHireWindows()
	{
		Assert.AreEqual( "ui4571234", HudWith( 1234 ).BuyCashText, "control 512: UITEXT 0x1ca (entry 457) then the balance" );
		Assert.AreEqual( "ui45899", HudWith( -99 ).BuyCashText, "UITEXT 0x1cb (entry 458) then the magnitude" );
		Assert.AreEqual( "ui4570", HudWith( 0 ).BuyCashText, "a zero balance is not negative" );
	}

	[TestMethod]
	public void CashTextBoxIsNineDigitsWideAndCentredOnTheDollarIcon()
	{
		// width("999999999") 400 units, text height 80: H = 96, centred on the i_dollar centre y 160 (integer arithmetic).
		var box = ParkHud.CashTextBounds( 400, 80 );
		Assert.AreEqual( 258f, box.X );
		Assert.AreEqual( 400f, box.Width );
		Assert.AreEqual( 96f, box.Height, "text height + 16" );
		Assert.AreEqual( 112f, box.Y, "dollarTop 58 + (262 - 58) / 2 - 96 / 2" );
		Assert.AreEqual( 160f, box.Y + box.Height / 2, "centred on the i_dollar icon" );
		var odd = ParkHud.CashTextBounds( 391, 77 );
		Assert.AreEqual( 93f, odd.Height );
		Assert.AreEqual( 114f, odd.Y, "the half height truncates: 160 - 93 / 2 = 114" );
	}

	[TestMethod]
	public void ListSortsAreRememberedForTheSession()
	{
		var memory = new ListSortMemory();
		var first = HudWith( 0 );
		typeof( ParkHud ).GetField( "host", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( first, new HudHost { ListSorts = memory } );
		first.SortStaff( true );
		first.SortStaff( true );
		var second = HudWith( 0 );
		typeof( ParkHud ).GetField( "host", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( second, new HudHost { ListSorts = memory } );
		bool Descending( ParkHud hud ) => (bool)typeof( ParkHud ).GetProperty( "staffSortDescending", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
		bool ByWage( ParkHud hud ) => (bool)typeof( ParkHud ).GetProperty( "staffSortByWage", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
		Assert.IsTrue( ByWage( second ) && Descending( second ), "a later HUD of the same session starts on the wage column, descending" );
		var fresh = HudWith( 0 );
		typeof( ParkHud ).GetField( "host", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( fresh, new HudHost() );
		Assert.IsFalse( ByWage( fresh ) || Descending( fresh ), "a new session starts by name, ascending" );
	}

	[TestMethod]
	public void BuyListKeepsItsLastSortInTheSessionMemory()
	{
		var memory = new ListSortMemory();
		var catalog = new EmptyBuyCatalog();
		var first = new BuyListModel( catalog, item => item.Id, item => item.Cost, memory );
		first.SortBy( BuySortColumn.Price );
		first.SortBy( BuySortColumn.Price );
		var second = new BuyListModel( catalog, item => item.Id, item => item.Cost, memory );
		Assert.AreEqual( BuySortColumn.Price, second.Sort );
		Assert.IsTrue( second.SortDescending );
		Assert.AreEqual( BuySortColumn.Name, new BuyListModel( catalog, item => item.Id, item => item.Cost ).Sort, "no memory: name ascending" );
	}

	private sealed class EmptyBuyCatalog : IBuildCatalog
	{
		public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => Array.Empty<BuildItem>();
	}

	[TestMethod]
	public void CashColourStepsFromRedToWhiteByThousands()
	{
		Assert.AreEqual( new RgbaByte( 255, 0, 0, 255 ), ParkHud.CashColor( -5000 ) );
		Assert.AreEqual( new RgbaByte( 255, 0, 0, 255 ), ParkHud.CashColor( 0 ) );
		Assert.AreEqual( new RgbaByte( 250, 90, 0, 255 ), ParkHud.CashColor( 1 ) );
		Assert.AreEqual( new RgbaByte( 250, 90, 0, 255 ), ParkHud.CashColor( 1000 ) );
		Assert.AreEqual( new RgbaByte( 255, 175, 0, 255 ), ParkHud.CashColor( 1001 ) );
		Assert.AreEqual( new RgbaByte( 255, 200, 0, 255 ), ParkHud.CashColor( 3000 ) );
		Assert.AreEqual( new RgbaByte( 255, 255, 150, 255 ), ParkHud.CashColor( 4000 ) );
		Assert.AreEqual( new RgbaByte( 255, 255, 255, 255 ), ParkHud.CashColor( 5001 ) );
		Assert.AreEqual( new RgbaByte( 255, 255, 255, 255 ), ParkHud.CashColor( long.MaxValue ), "no overflow near the maximum" );
		Assert.AreEqual( new RgbaByte( 255, 0, 0, 255 ), ParkHud.CashColor( long.MinValue ) );
	}

	[TestMethod]
	public void StaffSortTogglesTheSameColumnAndRestartsANewOneAscending()
	{
		var hud = HudWith( 0 );
		bool Descending() => (bool)typeof( ParkHud ).GetProperty( "staffSortDescending", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
		bool ByWage() => (bool)typeof( ParkHud ).GetProperty( "staffSortByWage", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
		Assert.IsFalse( Descending() || ByWage(), "column 0 (name) starts ascending" );
		hud.SortStaff( false );
		Assert.IsTrue( Descending(), "the first press on column 0 descends" );
		hud.SortStaff( false );
		Assert.IsFalse( Descending() );
		hud.SortStaff( true );
		Assert.IsTrue( ByWage() && !Descending(), "a new column starts ascending" );
		hud.SortStaff( true );
		Assert.IsTrue( Descending() );
		hud.SortStaff( false );
		Assert.IsTrue( !ByWage() && !Descending(), "returning to the first column resets the direction" );
	}
}
