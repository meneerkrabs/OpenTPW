using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The Publish Park form (Mac UI table data:0x514de): its buttons are drawn and every control lies inside the window without covering another.</summary>
[TestClass]
public class PublishFormLayoutTests
{
	private static readonly UiRect NameField = new( 682, 279, 684, 93 );
	private static readonly UiRect DescriptionField = new( 684, 444, 680, 448 );

	private static bool Contains( UiRect outer, UiRect inner ) =>
		inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

	private static bool Overlaps( UiRect a, UiRect b ) =>
		a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

	[TestMethod]
	public void EveryControlLiesInsideTheWindow()
	{
		var window = OnlineScreens.PublishWindowBounds;
		foreach ( var rect in new[] { NameField, DescriptionField, OnlineScreens.PublishCheckBounds, OnlineScreens.PublishCloseBounds,
			OnlineScreens.PublishWebsiteBounds, OnlineScreens.PublishExportBounds, OnlineScreens.PublishStatusBounds } )
			Assert.IsTrue( Contains( window, rect ), $"{rect} lies inside the window {window}" );
	}

	[TestMethod]
	public void NoControlCoversAnother()
	{
		var controls = new[] { NameField, DescriptionField, OnlineScreens.PublishCheckBounds, OnlineScreens.PublishCloseBounds,
			OnlineScreens.PublishWebsiteBounds, OnlineScreens.PublishExportBounds, OnlineScreens.PublishStatusBounds };
		for ( var first = 0; first < controls.Length; ++first )
			for ( var second = first + 1; second < controls.Length; ++second )
				Assert.IsFalse( Overlaps( controls[first], controls[second] ), $"{controls[first]} and {controls[second]} overlap" );
	}

	[TestMethod]
	public void TheCheckAndCloseButtonsAreTheOriginalsAndDrawTheirModels()
	{
		Assert.AreEqual( new UiRect( 1660, 731, 83, 84 ), OnlineScreens.PublishCheckBounds );
		Assert.AreEqual( new UiRect( 1671, 820, 83, 83 ), OnlineScreens.PublishCloseBounds );
		// the form builds both from the original tick and cross models (the tables of the same window family put b_okay at -1 and b_exit at -2)
		var source = System.IO.File.ReadAllText( System.IO.Path.Combine( FindRoot(), "source", "OpenTPW", "Online", "OnlineScreens.cs" ) );
		var form = source[source.IndexOf( "private void ShowPublishForm", System.StringComparison.Ordinal )..];
		form = form[..form.IndexOf( "---- Postcards", System.StringComparison.Ordinal )];
		StringAssert.Contains( form, "Id = \"publish\", Model = \"b_okay\"" );
		StringAssert.Contains( form, "Id = \"close\", Model = \"b_exit\"" );
		Assert.IsTrue( OriginalLayoutTables.LoadDialog.Model( "b_okay" ).Rect.Width == 83 && OriginalLayoutTables.LoadDialog.Model( "b_exit" ).Rect.Width == 83 );
	}

	private static string FindRoot()
	{
		var directory = new System.IO.DirectoryInfo( System.AppContext.BaseDirectory );
		while ( directory != null && !System.IO.File.Exists( System.IO.Path.Combine( directory.FullName, "source", "OpenTPW", "Online", "OnlineScreens.cs" ) ) )
			directory = directory.Parent;
		return directory!.FullName;
	}
}
