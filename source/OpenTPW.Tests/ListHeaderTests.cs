using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Tests;

/// <summary>The list column headers (tp.exe 0x486660): the !cbut plate and the (255, 239, 0) text that does not change with the pointer.</summary>
[TestClass, DoNotParallelize]
public class ListHeaderTests
{
	private static UiContext Context()
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, "NamePrice".Select( character => new FontAtlasTests.SyntheticGlyph( character, 2, 2, 0, 0, 3 ) ).ToArray() ) );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1024, 768 ) };
	}

	[TestMethod]
	public void AHeaderWritesItsTextInTheHeaderColourWhateverTheState()
	{
		// the callers pass (0xFF, 0xEF, 0) (0x4936xx...), and 0x486660 hands the same colour to the press and hover setters (0x668f5a, 0x668f7b)
		Assert.AreEqual( new RgbaByte( 255, 239, 0, 255 ), UiColors.ListHeader );
		var context = Context();
		context.DrawListHeader( context.Fonts.Label, "Name", new UiRect( 100, 100, 200, 40 ) );
		var vertices = context.Batch.Draws.SelectMany( draw => draw.Vertices ).ToArray();
		Assert.IsTrue( vertices.Length > 0 );
		Assert.IsTrue( vertices.All( vertex => vertex.Color == UiColors.ListHeader ), "the glyphs carry the header colour; no model draws without ui.wad" );
		Assert.AreEqual( "!cbut", UiContext.ListHeaderModel );
	}

	[TestMethod]
	public void EveryHeaderClassDrawsThroughTheSharedHeaderBuilder()
	{
		// source-text contract, CRLF-safe: the six list windows' header controls use DrawListHeader and no other text colour
		var root = "";
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null && root.Length == 0; directory = directory.Parent )
			if ( File.Exists( Path.Combine( directory.FullName, "source", "OpenTPW", "OpenTPW.csproj" ) ) )
				root = Path.Combine( directory.FullName, "source", "OpenTPW", "Hud" );
		if ( root.Length == 0 )
			Assert.Inconclusive( "the source tree is not next to the test binaries" );
		foreach ( var file in new[] { "ParkHud.AllStaffWindow.cs", "ParkHud.AttractionsWindow.cs", "ParkHud.VisitorWindow.cs", "ParkHud.LoansWindow.cs", "ParkHud.Staff.cs", "ParkHud.BuyWindow.cs" } )
		{
			var text = File.ReadAllText( Path.Combine( root, file ) ).Replace( "\r\n", "\n" );
			StringAssert.Contains( text, "DrawListHeader(", file );
		}
	}
}
