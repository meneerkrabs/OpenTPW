using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The Publish Park form (tp.exe table 0x7595b0): its controls come from the table, OpenTPW's own controls lie inside the window and cover none of them.</summary>
[TestClass]
public class PublishFormLayoutTests
{
	private static readonly LayoutTable Table = OriginalLayoutTables.PublishWindow;

	private static bool Contains( UiRect outer, UiRect inner ) =>
		inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

	private static bool Overlaps( UiRect a, UiRect b ) =>
		a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

	private static UiRect[] OriginalControls() => new[] { Table.Control( 0x11E3 ).Rect, Table.Control( 0x11E4 ).Rect, Table.Control( 0x11E5 ).Rect,
		Table.Model( "b_okay" ).Rect, Table.Model( "b_exit" ).Rect };

	private static UiRect[] OwnControls() => new[] { OnlineScreens.PublishWebsiteBounds, OnlineScreens.PublishExportBounds, OnlineScreens.PublishStatusBounds };

	[TestMethod]
	public void TheTableHoldsTheTickAndTheCrossAsModels()
	{
		Assert.AreEqual( "window2", Table.Root.Model );
		Assert.AreEqual( "b_okay", Table.Model( "b_okay" ).Model );
		Assert.AreEqual( "b_exit", Table.Model( "b_exit" ).Model );
		Assert.AreEqual( 0xF, OnlineScreens.PublishNameLength );
		Assert.AreEqual( 0x7E, OnlineScreens.PublishDescriptionLength );
	}

	[TestMethod]
	public void EveryControlLiesInsideTheWindow()
	{
		var window = Table.Root.Rect;
		foreach ( var rect in OriginalControls().Concat( OwnControls() ) )
			Assert.IsTrue( Contains( window, rect ), $"{rect} lies inside the window {window}" );
	}

	[TestMethod]
	public void OpenTpwsControlsCoverNoOtherControl()
	{
		var all = OriginalControls().Concat( OwnControls() ).ToArray();
		var own = OwnControls();
		foreach ( var rect in own )
			foreach ( var other in all.Where( other => !other.Equals( rect ) ) )
				Assert.IsFalse( Overlaps( rect, other ), $"{rect} and {other} overlap" );
	}

	[TestMethod]
	public void TheModsRefusalIsLocalizedInEveryLanguageWithoutTheTypographicApostrophe()
	{
		foreach ( var (language, table) in OnlineStrings.Supplementary )
		{
			Assert.IsTrue( table.TryGetValue( OnlineLabel.ParkRecordsMods, out var text ) && text.Contains( "{0}" ), $"{language} has the mods refusal with its list" );
			Assert.IsFalse( text.Contains( '’' ), $"{language}: no U+2019" );
		}
		StringAssert.Contains( OnlineStrings.Supplementary["English"][OnlineLabel.ParkRecordsMods], "exported or published" );
	}
}
