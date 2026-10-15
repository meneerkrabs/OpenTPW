using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass]
public class UiFontsTests
{
	private static System.Func<string, FontAtlas> Loader( Dictionary<string, FontAtlas> loaded, params string[] missing ) => name =>
	{
		if ( missing.Contains( name ) )
			throw new FileNotFoundException( name );
		if ( !loaded.TryGetValue( name, out var atlas ) )
			loaded[name] = atlas = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'a', 2, 2, 0, 0, 3 ) ) );
		return atlas;
	};

	[DataTestMethod]
	[DataRow( UiFontTier.Small, "DATETINY.bf4" )]
	[DataRow( UiFontTier.Medium, "DATESMALL.bf4" )]
	[DataRow( UiFontTier.Big, "DATEMED.bf4" )]
	public void HudDateUsesTheOriginalDateFontOfItsTier( UiFontTier tier, string file )
	{
		var loaded = new Dictionary<string, FontAtlas>();
		var fonts = new UiFonts( Loader( loaded ), tier );
		Assert.AreSame( loaded[file], fonts.Date );
	}

	[TestMethod]
	public void HudDateFallsBackToTheSmallFontWithoutPatch2()
	{
		var fonts = new UiFonts( Loader( new Dictionary<string, FontAtlas>(), "DATETINY.bf4", "DATESMALL.bf4", "DATEMED.bf4", "DATEBIG.bf4" ) );
		Assert.AreSame( fonts.Small, fonts.Date );
	}
}
