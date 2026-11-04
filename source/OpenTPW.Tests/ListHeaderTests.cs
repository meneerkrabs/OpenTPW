using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Tests;

/// <summary>The list column headers (tp.exe 0x486660): the !cbut plate and the (255, 239, 0) text that does not change with the pointer.</summary>
[TestClass, DoNotParallelize]
public class ListHeaderTests
{
	private static UiContext Context( List<string>? requested = null )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, "NamePrice".Select( character => new FontAtlasTests.SyntheticGlyph( character, 2, 2, 0, 0, 3 ) ).ToArray() ) );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => { requested?.Add( name ); throw new FileNotFoundException( name ); } ) ) { Canvas = new UiCanvas( 1024, 768 ) };
	}

	[TestMethod]
	public void AHeaderWritesItsTextInTheHeaderColourWhateverTheState()
	{
		// the callers pass (0xFF, 0xEF, 0) (0x4936xx...), and 0x486660 hands the same colour to the press and hover setters (0x668f5a, 0x668f7b)
		Assert.AreEqual( new RgbaByte( 255, 239, 0, 255 ), UiColors.ListHeader );
		var requested = new List<string>();
		var context = Context( requested );
		context.DrawListHeader( context.Fonts.Label, "Name", new UiRect( 100, 100, 200, 40 ) );
		CollectionAssert.Contains( requested, "!cbut", "the header plate is the !cbut model" );
		var vertices = context.Batch.Draws.SelectMany( draw => draw.Vertices ).ToArray();
		Assert.IsTrue( vertices.Length > 0 );
		Assert.IsTrue( vertices.All( vertex => vertex.Color == UiColors.ListHeader ), "the glyphs carry the header colour; no model draws without ui.wad" );
		Assert.AreEqual( "!cbut", UiContext.ListHeaderModel );
	}

	[TestMethod]
	public void AHeaderMovesItsTextByOnePixelOnHoverAndTwoByOneWhenPressed()
	{
		// 0x668ad3 draws the text at (0, 0), hover (1, 1) and pressed (2, 1) pixels of the display mode
		static (float X, float Y) First( UiContext context, bool hover, bool pressed )
		{
			context.Batch.Clear();
			context.DrawListHeader( context.Fonts.Label, "N", new UiRect( 100, 100, 200, 40 ), hover, pressed );
			var vertices = context.Batch.Draws.SelectMany( draw => draw.Vertices ).ToArray();
			return (vertices.Min( vertex => vertex.Position.X ), vertices.Min( vertex => vertex.Position.Y ));
		}
		var context = Context();
		var step = context.ModePixel;
		var normal = First( context, false, false );
		var hover = First( context, true, false );
		var pressed = First( context, true, true );
		Assert.AreEqual( normal.X + step, hover.X, 0.01f );
		Assert.AreEqual( normal.Y + step, hover.Y, 0.01f );
		Assert.AreEqual( normal.X + 2 * step, pressed.X, 0.01f );
		Assert.AreEqual( normal.Y + step, pressed.Y, 0.01f );
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
			// every header class draws through it: no class named ...Header keeps a pointer-dependent text colour of its own
			foreach ( Match header in Regex.Matches( text, @"sealed class \w*Header\b.*?(?=\n\t(?:private|public|internal) )", RegexOptions.Singleline ) )
				Assert.IsFalse( header.Value.Contains( "UiColors.Highlight" ), $"{file}: {header.Value[..40]}" );
		}
	}
}
