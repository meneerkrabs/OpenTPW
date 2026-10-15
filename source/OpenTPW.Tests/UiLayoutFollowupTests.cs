using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass]
public class UiLayoutFollowupTests
{
	[TestMethod]
	public void EveryObjectWindowRowKindMatchesItsTableValueNode()
	{
		var tables = new (int Category, LayoutTable Table)[]
		{
			((int)BuildCategory.Rides, OriginalLayoutTables.RideWindow),
			((int)BuildCategory.Shops, OriginalLayoutTables.ShopWindow),
			((int)BuildCategory.Sideshows, OriginalLayoutTables.SideshowWindow),
		};
		foreach ( var (category, table) in tables )
		{
			var stats = table.All.First( node => node.Model == "!frame" && node.Rect.X == 853 );
			var values = stats.Children.Where( node => node.Rect.X >= 1000 ).OrderBy( node => node.Rect.Y ).ToArray();
			var kinds = ParkHud.RowKinds( category );
			Assert.AreEqual( values.Length, kinds.Count, table.Name );
			for ( var row = 0; row < values.Length; row++ )
			{
				if ( kinds[row] == ParkHud.ObjectRowKind.None )
					continue;
				Assert.AreEqual( values[row].Type == 9, ParkHud.IsGaugeRow( kinds[row] ), $"{table.Name} row {row}: a gauge row is a type-9 value control" );
			}
		}
	}

	[TestMethod]
	public void EveryObjectWindowRowLabelIsTheOriginalLabelRoutinesUiText()
	{
		// tp.exe 0x4ad890 (ride), 0x4afc10 (shop) and 0x4b1040 (sideshow) set the row labels top to bottom to UITEXT 0x11-0x17, 0x20-0x26 and 0x29-0x30
		CollectionAssert.AreEqual( Enumerable.Range( 17, 7 ).ToArray(), ParkHud.RowLabels( (int)BuildCategory.Rides ).Select( label => (int)label ).ToArray() );
		CollectionAssert.AreEqual( Enumerable.Range( 32, 7 ).ToArray(), ParkHud.RowLabels( (int)BuildCategory.Shops ).Select( label => (int)label ).ToArray() );
		CollectionAssert.AreEqual( Enumerable.Range( 41, 8 ).ToArray(), ParkHud.RowLabels( (int)BuildCategory.Sideshows ).Select( label => (int)label ).ToArray() );
		foreach ( var category in new[] { (int)BuildCategory.Rides, (int)BuildCategory.Shops, (int)BuildCategory.Sideshows } )
			Assert.AreEqual( ParkHud.RowKinds( category ).Count, ParkHud.RowLabels( category ).Count );
		// the shop window's quality and price labels (0xc07e, 0xc083) are UITEXT 0x27 and 0x28 (0x4b07f3, 0x4b0903)
		Assert.AreEqual( 38, (int)UIStrings.LocalHappiness );
		Assert.AreEqual( 39, (int)UIStrings.QualityOfGoods );
		Assert.AreEqual( 40, (int)UIStrings.SalePrice );
	}

	[TestMethod]
	public void TheSideshowWinnersGaugeIsTheWinnersPercentageOfTheCustomers()
	{
		Assert.AreEqual( 0f, ParkHud.WinnersGauge( 5, 0 ), "no customers: 0 (0x4e1f20 returns before dividing)" );
		Assert.AreEqual( 0.33f, ParkHud.WinnersGauge( 1, 3 ), 1e-6f, "trunc(100 / 3) = 33" );
		Assert.AreEqual( 1f, ParkHud.WinnersGauge( 4, 4 ) );
		Assert.AreEqual( ParkHud.ObjectRowKind.Winners, ParkHud.RowKinds( (int)BuildCategory.Sideshows )[1] );
		Assert.IsTrue( ParkHud.IsGaugeRow( ParkHud.ObjectRowKind.Winners ) );
	}

	[TestMethod]
	public void TheRightClickLeavesAScreenOnlyWithTheOption()
	{
		var previous = GameOptions.Current.RmbCancel;
		try
		{
			var right = UiInput.Idle( new System.Numerics.Vector2( 1, 1 ) ) with { RightPressed = true };
			GameOptions.Current.RmbCancel = true;
			Assert.IsTrue( UiScreen.RightCancels( right ) );
			GameOptions.Current.RmbCancel = false;
			Assert.IsFalse( UiScreen.RightCancels( right ) );
			var left = UiInput.Click( new System.Numerics.Vector2( 1, 1 ) );
			GameOptions.Current.RmbCancel = true;
			Assert.IsFalse( UiScreen.RightCancels( left ) );
		}
		finally
		{
			GameOptions.Current.RmbCancel = previous;
		}
	}

	[TestMethod]
	public void ADialogButtonsHelpFollowsItsLabel()
	{
		var label = "Yes";
		var screen = UiDialogs.Message( "t", () => "CAPTION\n\nBody", (() => label, () => { }), (() => "No", () => { }) );
		var tick = screen.Find( "choice0" )!;
		Assert.AreEqual( "Yes", tick.CurrentHelp );
		label = "Ja";
		Assert.AreEqual( "Ja", tick.CurrentHelp, "the help is read each time, so a language switch updates it" );
	}
}
