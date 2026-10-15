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
