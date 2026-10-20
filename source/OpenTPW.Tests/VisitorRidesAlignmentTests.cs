using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class VisitorRidesAlignmentTests
{
	[TestMethod]
	public void OnlyTheRidesTextColumnUsesItsLeftAlignedDefault()
	{
		CollectionAssert.AreEqual( new[] { UiAlign.Center, UiAlign.Right, UiAlign.Center, UiAlign.Left, UiAlign.Center, UiAlign.Center },
			ParkVisitorRoster.Columns.Select( column => column.Align ).ToArray() );
		CollectionAssert.AreEqual( new[] { "headerNumber", "headerCash", "headerTime", "headerRides", "headerThought", "headerHappiness" },
			ParkVisitorRoster.Columns.Select( column => column.Id ).ToArray() );
	}

	[DataTestMethod]
	[DataRow( 640, 480, 1, false )]
	[DataRow( 2560, 1920, 2, false )]
	[DataRow( 640, 480, 1, true )]
	[DataRow( 2560, 1920, 2, true )]
	public void ActualVisitorListDrawPlacesRidesAtTheLeadingEdge( int width, int height, int textScale, bool rightToLeft )
	{
		var hud = VisitorWorldSelectionTests.Fixture( out var simulation, out _ );
		var guest = new Guest( 11, 0 ) { State = GuestState.WalkingAround, Money = 22, Happiness = 50, TimeInParkSeconds = 3600, RidesTaken = 7, VisitorNumber = 11 };
		typeof( GuestSimulation ).GetField( "guests", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( simulation, new List<Guest> { guest } );
		hud.OpenVisitorWindow();
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, Enumerable.Range( '0', 10 ).Select( code =>
			new FontAtlasTests.SyntheticGlyph( (char)code, 3, 5, 0, 0, 4, Enumerable.Repeat( (byte)15, 15 ).ToArray() ) ).ToArray() ) );
		UiContext Context() => new( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ),
			new UiModels( name => throw new System.IO.FileNotFoundException( name ) ) )
		{
			Canvas = new UiCanvas( width, height, textScale ), RightToLeft = rightToLeft
		};
		var actual = Context();
		Assert.AreEqual( textScale, actual.Canvas.TextScale );
		var list = hud.Stack.Top!.Find( "visitorList" )!;
		list.Draw( actual, false, false );
		var values = hud.VisitorValuesOf( guest );
		Assert.AreEqual( 7, values.Rides );
		Assert.AreEqual( "7", hud.VisitorCellText( values, 3 ) );
		Assert.AreNotEqual( "7", hud.VisitorCellText( values, 0 ) );
		Assert.AreNotEqual( "7", hud.VisitorCellText( values, 2 ) );
		var rides = actual.Batch.Texts.Single( draw => draw.Text == "7" );
		var table = OriginalLayoutTables.VisitorsWindow;
		var header = table.Control( 0x13 ).Rect;
		var content = table.Control( 0x1E497 ).Inner!.Value;
		var cell = actual.Canvas.Map( new UiRect( header.X, content.Y, header.Width, ParkHud.VisitorRowHeight ), UiAnchor.Center );
		var expected = Context();
		expected.DrawFittedText( expected.Fonts.Label, "7", cell, RgbaByte.White, UiAlign.Left );
		Assert.AreEqual( expected.Batch.Texts.Single().Rect, rides.Rect, "the real row draw consumes the left-aligned rides column" );
		var centred = Context();
		centred.DrawFittedText( centred.Fonts.Label, "7", cell, RgbaByte.White, UiAlign.Center );
		Assert.AreNotEqual( centred.Batch.Texts.Single().Rect.X, rides.Rect.X, "a short digit must not remain centred" );
		Assert.IsTrue( rightToLeft ? rides.Rect.X > centred.Batch.Texts.Single().Rect.X : rides.Rect.X < centred.Batch.Texts.Single().Rect.X,
			"the existing text renderer mirrors left alignment for RTL" );
		// The same real draw preserves the explicitly centred number/time and right-aligned cash cells.
		foreach ( var column in new[] { 0, 1, 2 } )
		{
			var text = hud.VisitorCellText( values, column );
			var columnHeader = table.Control( (uint)(0x10 + column) ).Rect;
			var columnCell = actual.Canvas.Map( new UiRect( columnHeader.X, content.Y, columnHeader.Width, ParkHud.VisitorRowHeight ), UiAnchor.Center );
			var reference = Context();
			reference.DrawFittedText( reference.Fonts.Label, text, columnCell, RgbaByte.White, column == 1 ? UiAlign.Right : UiAlign.Center );
			Assert.AreEqual( reference.Batch.Texts.Single().Rect, actual.Batch.Texts.Single( draw => draw.Text == text ).Rect, $"column {column}" );
		}
	}
}
