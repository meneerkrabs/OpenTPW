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
		Assert.AreEqual( new RgbaByte( 255, 255, 255, 255 ), ParkHud.CashColor( long.MaxValue / 2 ) );
	}

	[TestMethod]
	public void StaffSortTogglesTheSameColumnAndRestartsANewOneAscending()
	{
		var hud = HudWith( 0 );
		bool Descending() => (bool)typeof( ParkHud ).GetField( "staffSortDescending", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
		bool ByWage() => (bool)typeof( ParkHud ).GetField( "staffSortByWage", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
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
